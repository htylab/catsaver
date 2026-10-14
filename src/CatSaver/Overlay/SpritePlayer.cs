using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CatSaver.Overlay;

/// <summary>
/// Draws frames from the catalog's sprite sheets. <see cref="Draw"/> plays a
/// playlist in order (each sheet for ~<see cref="SecondsPerSheet"/>, whole loops
/// only); <see cref="DrawFrame"/> draws one specific frame of any sheet. Decoded
/// sheets are cached (a few at a time) and freed by <see cref="Release"/>.
/// </summary>
internal sealed class SpritePlayer : IDisposable
{
    private const double SecondsPerSheet = 4.0;
    private const int MaxCachedSheets = 4;

    private readonly SpriteCatalog _catalog;
    private readonly List<SpriteCatalog.SheetInfo> _playlist;
    private readonly double _cycleSeconds;
    private readonly ImageAttributes _clampEdges = new();
    private readonly Dictionary<string, Bitmap> _cache = new();
    private readonly LinkedList<string> _recent = new();

    public SpritePlayer(SpriteCatalog catalog, IEnumerable<string> playlist)
    {
        _catalog = catalog;
        _playlist = playlist.Where(catalog.Sheets.ContainsKey).Select(n => catalog.Sheets[n]).ToList();
        _cycleSeconds = _playlist.Sum(PlaySeconds);
        // Stops bicubic filtering from sampling the neighbouring frame in the sheet.
        _clampEdges.SetWrapMode(WrapMode.TileFlipXY);
    }

    /// <summary>The first sheet of the playlist.</summary>
    public SpriteCatalog.SheetInfo? First => _playlist.FirstOrDefault();

    private static double PlaySeconds(SpriteCatalog.SheetInfo s) =>
        Math.Max(1, Math.Round(SecondsPerSheet / s.LoopSeconds)) * s.LoopSeconds;

    /// <summary>
    /// Draws the playlist frame for time <paramref name="t"/> (seconds) into <paramref name="dest"/>.
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
        DrawFrame(g, sheet, sheet.FrameAt((int)(local * 1000 / sheet.FrameMs)), dest, mirror);
    }

    /// <summary>Draws frame <paramref name="frame"/> of <paramref name="sheet"/>, optionally mirrored and faded.</summary>
    public void DrawFrame(Graphics g, SpriteCatalog.SheetInfo sheet, int frame, RectangleF dest, bool mirror = false, float opacity = 1f)
    {
        if (opacity <= 0.01f) return;
        var bitmap = Load(sheet);
        if (bitmap is null) return;

        frame = Math.Clamp(frame, 0, sheet.Frames - 1);
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
        if (opacity >= 0.99f)
        {
            g.DrawImage(bitmap, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, _clampEdges);
        }
        else
        {
            using var faded = new ImageAttributes();
            faded.SetWrapMode(WrapMode.TileFlipXY);
            faded.SetColorMatrix(new ColorMatrix { Matrix33 = opacity });
            g.DrawImage(bitmap, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, faded);
        }
        g.Restore(state);
    }

    private Bitmap? Load(SpriteCatalog.SheetInfo sheet)
    {
        string name = sheet.Name;
        if (_cache.TryGetValue(name, out var cached))
        {
            _recent.Remove(name);
            _recent.AddFirst(name);
            return cached;
        }
        try
        {
            using var stream = typeof(SpritePlayer).Assembly.GetManifestResourceStream(sheet.ResourcePrefix + name + ".png");
            if (stream is null) return null;
            using var raw = new Bitmap(stream);
            // The PNGs are palette-based to stay small; convert once to the fastest
            // format for drawing.
            var bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
                g.DrawImage(raw, 0, 0, raw.Width, raw.Height);

            _cache[name] = bitmap;
            _recent.AddFirst(name);
            while (_recent.Count > MaxCachedSheets)
            {
                string oldest = _recent.Last!.Value;
                _recent.RemoveLast();
                _cache.Remove(oldest, out var old);
                old?.Dispose();
            }
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error($"Loading sprite {name}", ex);
            return null;
        }
    }

    /// <summary>Frees the decoded sheets (they are reloaded on the next draw).</summary>
    public void Release()
    {
        foreach (var bitmap in _cache.Values) bitmap.Dispose();
        _cache.Clear();
        _recent.Clear();
    }

    public void Dispose()
    {
        Release();
        _clampEdges.Dispose();
    }
}
