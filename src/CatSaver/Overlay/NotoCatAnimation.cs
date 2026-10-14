using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace CatSaver.Overlay;

/// <summary>
/// Plays Google's Noto animated cat emoji (CC BY 4.0) from embedded PNG sprite
/// sheets, cycling through the expressions listed in Assets/Noto/sprites.json.
/// Sheets are decoded on demand and released when the overlay closes.
/// Regenerate the assets with tools/make_noto_sprites.py.
/// </summary>
internal sealed class NotoCatAnimation : IDisposable
{
    private const string ResourcePrefix = "CatSaver.Noto.";
    /// <summary>Roughly how long each expression stays before switching to the next.</summary>
    private const double SecondsPerExpression = 4.0;

    private sealed record SheetInfo(string Name, int Frames, int Columns, int Size, int FrameMs)
    {
        public double LoopSeconds => Frames * FrameMs / 1000.0;
        public int Loops => Math.Max(1, (int)Math.Round(SecondsPerExpression / LoopSeconds));
        public double PlaySeconds => Loops * LoopSeconds;
    }

    private readonly List<SheetInfo> _sheets;
    private readonly double _cycleSeconds;
    private readonly ImageAttributes _clampEdges = new();
    private Bitmap? _bitmap;
    private int _loaded = -1;

    private NotoCatAnimation(List<SheetInfo> sheets)
    {
        _sheets = sheets;
        _cycleSeconds = sheets.Sum(s => s.PlaySeconds);
        // Stops bicubic filtering from sampling the neighbouring frame in the sheet.
        _clampEdges.SetWrapMode(WrapMode.TileFlipXY);
    }

    /// <summary>Returns null if the embedded assets are missing or unreadable.</summary>
    public static NotoCatAnimation? TryLoad()
    {
        try
        {
            using var stream = typeof(NotoCatAnimation).Assembly.GetManifestResourceStream(ResourcePrefix + "sprites.json");
            if (stream is null) return null;
            var sheets = JsonSerializer.Deserialize<List<SheetInfo>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return sheets is { Count: > 0 } ? new NotoCatAnimation(sheets) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Draws the frame for time <paramref name="t"/> (seconds) into <paramref name="dest"/>.
    /// <paramref name="forceSheet"/> (e.g. "1f63b") plays that expression instead of the cycle.
    /// </summary>
    public void Draw(Graphics g, double t, RectangleF dest, string? forceSheet = null)
    {
        // Which expression, and how far into it?
        double local = t % _cycleSeconds;
        int index = 0;
        int forced = forceSheet is null ? -1 : _sheets.FindIndex(s => s.Name == forceSheet);
        if (forced >= 0)
        {
            index = forced;
            local = t % _sheets[index].LoopSeconds;
        }
        else
        {
            while (index < _sheets.Count - 1 && local >= _sheets[index].PlaySeconds)
            {
                local -= _sheets[index].PlaySeconds;
                index++;
            }
        }

        var sheet = _sheets[index];
        var bitmap = Load(index);
        if (bitmap is null) return;

        int frame = (int)(local * 1000 / sheet.FrameMs) % sheet.Frames;
        var src = new Rectangle((frame % sheet.Columns) * sheet.Size, (frame / sheet.Columns) * sheet.Size, sheet.Size, sheet.Size);

        var oldMode = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(bitmap, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, _clampEdges);
        g.InterpolationMode = oldMode;
    }

    private Bitmap? Load(int index)
    {
        if (_loaded == index) return _bitmap;
        Release();
        try
        {
            using var stream = typeof(NotoCatAnimation).Assembly.GetManifestResourceStream(ResourcePrefix + _sheets[index].Name + ".png");
            if (stream is null) return null;
            using var raw = new Bitmap(stream);
            // The PNGs are palette-based to stay small; convert once to the fastest
            // format for drawing.
            _bitmap = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(_bitmap))
                g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
            _loaded = index;
            return _bitmap;
        }
        catch
        {
            Release();
            return null;
        }
    }

    /// <summary>Frees the decoded sheet (it is reloaded on the next draw).</summary>
    public void Release()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _loaded = -1;
    }

    public void Dispose()
    {
        Release();
        _clampEdges.Dispose();
    }
}
