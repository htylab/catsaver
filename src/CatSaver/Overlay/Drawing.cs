using System.Drawing.Drawing2D;

namespace CatSaver.Overlay;

/// <summary>Small GDI+ helpers shared by the overlay windows.</summary>
internal static class Drawing
{
    public static FontFamily ResolveFont(string preferred)
    {
        string[] candidates = { preferred, "Microsoft JhengHei UI", "Microsoft JhengHei", "Microsoft YaHei UI", "Segoe UI" };
        var installed = FontFamily.Families;
        foreach (var name in candidates)
        {
            var match = installed.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return FontFamily.GenericSansSerif;
    }

    /// <summary>Draws centred text, shrinking it to fit the box if needed.</summary>
    public static void DrawFittedText(Graphics g, FontFamily font, string text, FontStyle style, float emSize, Color color, RectangleF box)
    {
        if (string.IsNullOrEmpty(text)) return;
        var fontStyle = font.IsStyleAvailable(style) ? style : FontStyle.Regular;

        using var path = new GraphicsPath();
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        path.AddString(text, font, (int)fontStyle, emSize, PointF.Empty, format);
        var bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        float scale = Math.Min(1f, Math.Min(box.Width / bounds.Width, box.Height / bounds.Height));
        using var m = new Matrix();
        m.Translate(box.X + box.Width / 2, box.Y + box.Height / 2);
        m.Scale(scale, scale);
        m.Translate(-(bounds.X + bounds.Width / 2), -(bounds.Y + bounds.Height / 2));
        path.Transform(m);

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
