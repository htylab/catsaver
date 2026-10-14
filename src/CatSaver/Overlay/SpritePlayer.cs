using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CatSaver.Overlay;

/// <summary>
/// Plays a list of sprite sheets in order (each for ~<see cref="SecondsPerSheet"/>,
/// whole loops only), looping. The current sheet is decoded on demand and freed
/// by <see cref="Release"/>.
/// </summary>
internal sealed class SpritePlayer : IDisposable
{
    private const double SecondsPerSheet = 4.0;

    private readonly SpriteCatalog _catalog;
    private readonly List<SpriteCatalog.SheetInfo> _playlist;
    private readonly double _cycleSeconds;
    private readonly ImageAttributes _clampEdges = new();
    private Bitmap? _bitmap;
    private string? _loaded;

    public SpritePlayer(SpriteCatalog catalog, IEnumerable<string> playlist)
    {
        _catalog = catalog;
        _playlist = playlist.Where(catalog.Sheets.ContainsKey).Select(n => catalog.Sheets[n]).ToList();
        _cycleSeconds = _playlist.Sum(PlaySeconds);
        // Stops bicubic filtering from sampling the neighbouring frame in the sheet.
        _clampEdges.SetWrapMode(WrapMode.TileFlipXY);
    }

    /// <summary>The sheet currently playing (first one before any draw).</summary>
    public SpriteCatalog.SheetInfo? First => _playlist.FirstOrDefault();

    private static double PlaySeconds(SpriteCatalog.SheetInfo s) =>
        Math.Max(1, Math.Round(SecondsPerSheet / s.LoopSeconds)) * s.LoopSeconds;

    /// <summary>
    /// Draws the frame for time <paramref name="t"/> (seconds) into <paramref name="dest"/>.
    /// <paramref name="forceSheet"/> (e.g. "1f63b") plays that sheet instead of the playlist.
    /// </summary>
    public void Draw(Graphics g, double t, RectangleF dest, string? forceSheet = null, bool mirror = false)
    {
        SpriteCatalog.SheetInfo sheet;
        double local;
        if (forceSheet is not null && _catalog.Sheets.TryGetValue(forceSheet, out var forced))
        {
            sheet = forced;
            local = t % sheet.LoopSeconds;
        }
        else
        {
            if (_playlist.Count == 0) return;
            local = t % _cycleSeconds;
            int index = 0;
            while (index < _playlist.Count - 1 && local >= PlaySeconds(_playlist[index]))
            {
                local -= PlaySeconds(_playlist[index]);
                index++;
            }
            sheet = _playlist[index];
            local %= sheet.LoopSeconds;
        }

        var bitmap = Load(sheet);
        if (bitmap is null) return;

        int frame = sheet.FrameAt((int)(local * 1000 / sheet.FrameMs));
        int fw = sheet.FrameWidth, fh = sheet.FrameHeight;
        var src = new Rectangle((frame % sheet.Columns) * fw, (frame / sheet.Columns) * fh, fw, fh);

        var state = g.Save();
        if (mirror)
        {
            // Flip horizontally around the destination's centre (e.g. a cat walking left).
            float cx = dest.X + dest.Width / 2;
            g.TranslateTransform(cx, 0);
            g.ScaleTransform(-1, 1);
            g.TranslateTransform(-cx, 0);
        }
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(bitmap, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, _clampEdges);
        g.Restore(state);
    }

    private Bitmap? Load(SpriteCatalog.SheetInfo sheet)
    {
        string name = sheet.Name;
        if (_loaded == name) return _bitmap;
        Release();
        try
        {
            using var stream = typeof(SpritePlayer).Assembly.GetManifestResourceStream(sheet.ResourcePrefix + name + ".png");
            if (stream is null) return null;
            using var raw = new Bitmap(stream);
            // The PNGs are palette-based to stay small; convert once to the fastest
            // format for drawing.
            _bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_bitmap))
                g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
            _loaded = name;
            return _bitmap;
        }
        catch (Exception ex)
        {
            Log.Error($"Loading sprite {name}", ex);
            Release();
            return null;
        }
    }

    /// <summary>Frees the decoded sheet (it is reloaded on the next draw).</summary>
    public void Release()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _loaded = null;
    }

    public void Dispose()
    {
        Release();
        _clampEdges.Dispose();
    }
}
