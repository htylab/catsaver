using System.Drawing.Drawing2D;

namespace CatSaver.Overlay;

/// <summary>Everything that changes from frame to frame.</summary>
internal readonly record struct CatFrame(double Time, TimeSpan Remaining);

/// <summary>
/// Draws one frame: the cat (a Noto emoji set from <see cref="SpriteCatalog"/>, or
/// the hand-drawn "classic" cat), a drink prop, floating drops/hearts and the speech
/// bubble. Everything is drawn in a fixed logical canvas
/// (<see cref="CanvasWidth"/> × <see cref="CanvasHeight"/>) and scaled by the window.
/// Each reminder picks a cat set (when "random"), a drink prop and an entrance.
/// </summary>
internal sealed class CatRenderer : IDisposable
{
    public const float CanvasWidth = 800;
    public const float CanvasHeight = 880;

    private const float Cx = CanvasWidth / 2;

    private readonly AppConfig _config;
    private readonly FontFamily _font;
    private readonly SpriteCatalog? _catalog;
    private static readonly Random Rng = new();

    /// <summary>How the cat arrives on screen; picked at random per reminder.</summary>
    private enum Entrance { Pop, Drop, Rise }

    // Per-reminder variation.
    private SpritePlayer? _cat;   // null = hand-drawn cat
    private SpritePlayer? _prop;
    private bool _realCat;  // _cat is a real (video) cat rather than an emoji
    private string? _lastSetId;
    private Entrance _entrance;

    // Interaction state (shared by the cats on every monitor).
    private double? _pokeAt;
    private string? _celebrateTitle;
    private string? _celebrateSub;
    private double _celebrateAt;
    private const double CelebrateTextSeconds = 4.0;

    private const double PokeJumpSeconds = 0.55, HeartBurstSeconds = 1.3, HeartEyesSeconds = 2.0;

    private readonly Color _fur;
    private readonly Color _furDark;
    private readonly Color _furLight;
    private readonly Color _outline;
    private static readonly Color Pink = Color.FromArgb(255, 160, 175);
    private static readonly Color Blush = Color.FromArgb(120, 255, 120, 140);
    private static readonly Color Ink = Color.FromArgb(70, 50, 45);
    private static readonly Color Water = Color.FromArgb(150, 90, 180, 255);
    private static readonly Color BubbleBorder = Color.FromArgb(255, 140, 90);

    public CatRenderer(AppConfig config)
    {
        _config = config;
        _font = Drawing.ResolveFont(config.FontFamily);
        _fur = config.GetCatColor();
        _furDark = Shade(_fur, 0.72f);
        _furLight = Mix(_fur, Color.White, 0.65f);
        _outline = Shade(_fur, 0.45f);
        if (config.Animation != "classic")
            _catalog = SpriteCatalog.Instance; // null → falls back to the hand-drawn cat
        PickVariation();
    }

    /// <summary>Chooses the cat set, drink prop and entrance for the next reminder.</summary>
    private void PickVariation()
    {
        _cat?.Dispose();
        _prop?.Dispose();
        _cat = _prop = null;
        _realCat = false;
        WalkerSheet = null;
        _entrance = (Entrance)Rng.Next(3);

        if (_catalog is null) return;
        var set = _config.Animation == "random" ? null : _catalog.FindSet(_config.Animation);
        if (set is null)
        {
            // Random, but not the same set twice in a row.
            var pool = _catalog.Sets.Where(s => s.Id != _lastSetId).ToList();
            if (pool.Count == 0) pool = _catalog.Sets.ToList();
            set = pool[Rng.Next(pool.Count)];
        }
        _lastSetId = set.Id;
        if (set.IsReal)
        {
            // Real cats: one sitting cat in the middle, one walking cat along the bottom.
            _realCat = true;
            _cat = new SpritePlayer(_catalog, new[] { set.Emoji[Rng.Next(set.Emoji.Count)] });
            WalkerSheet = _catalog.Sheets[set.Walkers[Rng.Next(set.Walkers.Count)]];
        }
        else
        {
            _cat = new SpritePlayer(_catalog, set.Emoji);
        }
        if (_catalog.Props.Count > 0)
            _prop = new SpritePlayer(_catalog, new[] { _catalog.Props[Rng.Next(_catalog.Props.Count)] });
    }

    /// <summary>The walking cat for this reminder (real-cat sets only), shown by <see cref="WalkerWindow"/>.</summary>
    public SpriteCatalog.SheetInfo? WalkerSheet { get; private set; }

    public SpriteCatalog? Catalog => _catalog;

    /// <summary>Frees decoded animation frames while no reminder is on screen.</summary>
    public void ReleaseResources()
    {
        _cat?.Release();
        _prop?.Release();
    }

    public void Dispose()
    {
        _cat?.Dispose();
        _prop?.Dispose();
    }

    public FontFamily Font => _font;

    /// <summary>Logical-canvas area covered by the cat's body (click target).</summary>
    public static RectangleF CatBodyArea => new(160, 320, 480, 530);

    /// <summary>
    /// Called when a new reminder starts: clears pokes/celebrations and picks a
    /// (possibly different) cat, prop and entrance.
    /// </summary>
    public void ResetInteraction()
    {
        _pokeAt = null;
        _celebrateTitle = _celebrateSub = null;
        PickVariation();
    }

    /// <summary>The cat was clicked at time <paramref name="t"/>: jump, heart eyes, burst of hearts.</summary>
    public void Poke(double t)
    {
        // Ignore rapid repeat clicks mid-jump so the animation stays smooth.
        if (_pokeAt is double last && t - last < PokeJumpSeconds) return;
        _pokeAt = t;
    }

    /// <summary>Shows other bubble text for a few seconds (e.g. after 「我喝了！」) and pokes the cat.</summary>
    public void Celebrate(double t, string title, string sub)
    {
        _celebrateAt = t;
        _celebrateTitle = title;
        _celebrateSub = sub;
        _pokeAt = t;
    }

    private float PokeJump(double t)
    {
        if (_pokeAt is not double p) return 0;
        double k = (t - p) / PokeJumpSeconds;
        return k is >= 0 and < 1 ? (float)Math.Sin(Math.PI * k) * 70f : 0f;
    }

    public void Draw(Graphics g, in CatFrame f)
    {
        double t = f.Time;

        DrawDrops(g, t);

        var catState = g.Save();
        ApplyEntrance(g, t);
        if (_prop is not null) DrawProp(g, t, _prop, _realCat ? Cx - 300 : Cx - 235);
        g.TranslateTransform(0, -PokeJump(t));
        if (_cat is not null && _realCat) DrawRealCat(g, t, _cat);
        else if (_cat is not null) DrawNotoCat(g, t, _cat);
        else DrawCat(g, t);
        g.Restore(catState);

        DrawHeartBurst(g, t);

        // The bubble pops in (scale with a little overshoot), slightly after the cat.
        float pop = EaseOutBack((float)Math.Clamp((t - 0.15) / 0.6, 0, 1));
        var bubbleState = g.Save();
        g.TranslateTransform(Cx, 260);
        g.ScaleTransform(0.2f + 0.8f * pop, 0.2f + 0.8f * pop);
        g.TranslateTransform(-Cx, -260);
        if (pop > 0) DrawBubble(g, t, f.Remaining);
        g.Restore(bubbleState);
    }

    private const float Ground = 835;

    private void ApplyEntrance(Graphics g, double t)
    {
        switch (_entrance)
        {
            case Entrance.Pop:
            {
                // Grows from the ground with an overshoot.
                float k = EaseOutBack((float)Math.Clamp(t / 0.7, 0, 1));
                float s = 0.3f + 0.7f * k;
                g.TranslateTransform(Cx, Ground);
                g.ScaleTransform(s, s);
                g.TranslateTransform(-Cx, -Ground);
                break;
            }
            case Entrance.Drop:
            {
                // Falls from above and bounces.
                float k = EaseOutBounce((float)Math.Clamp(t / 1.1, 0, 1));
                g.TranslateTransform(0, -(1 - k) * 700);
                break;
            }
            case Entrance.Rise:
            {
                // Peeks up from below the window edge.
                float k = EaseOutBack((float)Math.Clamp(t / 0.9, 0, 1));
                g.TranslateTransform(0, (1 - k) * 560);
                break;
            }
        }
    }

    /// <summary>The drink (bubble tea / tea / water drop) bobbing next to the cat.</summary>
    private static void DrawProp(Graphics g, double t, SpritePlayer prop, float x)
    {
        const float size = 175;
        float bob = (float)Math.Sin(t * 2.8) * 8f;
        var state = g.Save();
        g.TranslateTransform(x, Ground - size / 2 + 8 + bob);
        g.RotateTransform((float)Math.Sin(t * 1.9) * 6f);
        prop.Draw(g, t, new RectangleF(-size / 2, -size / 2, size, size));
        g.Restore(state);
    }

    // ------------------------------------------------------------- real cat

    /// <summary>A background-removed video cat sitting in the middle (no cartoon squash/stretch).</summary>
    private static void DrawRealCat(Graphics g, double t, SpritePlayer cat)
    {
        var sheet = cat.First;
        if (sheet is null) return;
        const float maxW = 470, maxH = 450;
        float scale = Math.Min(maxW / sheet.FrameWidth, maxH / sheet.FrameHeight);
        float w = sheet.FrameWidth * scale, h = sheet.FrameHeight * scale;

        using (var shadow = new SolidBrush(Color.FromArgb(55, 0, 0, 0)))
            g.FillEllipse(shadow, Cx - w * 0.42f, Ground - 14, w * 0.84f, 30);
        cat.Draw(g, t, new RectangleF(Cx - w / 2, Ground - h + 6, w, h));
    }

    // ------------------------------------------------------------- noto cat

    private void DrawNotoCat(Graphics g, double t, SpritePlayer noto)
    {
        const float size = 480, bottom = Ground;

        // Every few seconds: a little hop, then a squash on landing.
        const double hopEvery = 3.2, hopTime = 0.45, squashTime = 0.18;
        double p = t % hopEvery;
        float hop = p < hopTime ? (float)Math.Sin(Math.PI * p / hopTime) : 0f;
        float squash = p >= hopTime && p < hopTime + squashTime
            ? (float)Math.Sin(Math.PI * (p - hopTime) / squashTime)
            : 0f;
        float breathe = (float)Math.Sin(t * 2.4) * 0.015f;

        float sx = 1f + squash * 0.07f - breathe * 0.5f;
        float sy = 1f - squash * 0.09f + breathe;
        float lift = hop * 45f;

        // Shadow shrinks while in the air.
        using (var shadow = new SolidBrush(Color.FromArgb((int)(55 - hop * 25), 0, 0, 0)))
        {
            float w = 330 * (1 - hop * 0.25f) * sx;
            g.FillEllipse(shadow, Cx - w / 2, bottom - 14, w, 32);
        }

        var state = g.Save();
        // Scale/rotate around the bottom centre so the cat stays "on the ground".
        g.TranslateTransform(Cx, bottom - lift);
        g.RotateTransform((float)Math.Sin(t * 1.6) * 4f);
        g.ScaleTransform(sx, sy);
        // Heart eyes for a moment after being clicked.
        string? force = !_realCat && _pokeAt is double p0 && t - p0 < HeartEyesSeconds ? "1f63b" : null;
        noto.Draw(g, t, new RectangleF(-size / 2, -size + 22, size, size), force); // emoji has ~22px padding at the bottom
        g.Restore(state);
    }

    private void DrawHeartBurst(Graphics g, double t)
    {
        if (_pokeAt is not double p) return;
        double k = (t - p) / HeartBurstSeconds;
        if (k is < 0 or >= 1) return;

        const int count = 10;
        using var brush = new SolidBrush(Color.Transparent);
        for (int i = 0; i < count; i++)
        {
            double angle = -Math.PI / 2 + (i - (count - 1) / 2.0) * 0.32; // fan out upwards
            float dist = (float)(60 + 300 * EaseOutCubic(k));
            float x = Cx + (float)Math.Cos(angle) * dist * 1.25f;
            float y = 600 + (float)Math.Sin(angle) * dist * 0.85f;
            float size = 34 + (i % 3) * 10;
            brush.Color = Color.FromArgb((int)(230 * (1 - k)), i % 2 == 0 ? Color.FromArgb(255, 95, 140) : Color.FromArgb(255, 150, 185));
            FillHeart(g, brush, x, y, size);
        }
    }

    private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);

    // ------------------------------------------------------------------ cat

    private void DrawCat(Graphics g, double t)
    {
        // Gentle breathing / bobbing.
        float bob = (float)Math.Sin(t * 2.4) * 6f;
        float breathe = 1f + (float)Math.Sin(t * 2.4) * 0.015f;

        using var outlinePen = new Pen(_outline, 6f) { LineJoin = LineJoin.Round };

        // Shadow
        using (var shadow = new SolidBrush(Color.FromArgb(50, 0, 0, 0)))
            g.FillEllipse(shadow, Cx - 190, 832, 380, 36);

        DrawTail(g, t, outlinePen);

        // Body
        var body = new RectangleF(Cx - 165, 600 + bob - 145 * (breathe - 1), 330, 260 * breathe);
        using (var furBrush = new SolidBrush(_fur))
        {
            g.FillEllipse(furBrush, body);
            g.DrawEllipse(outlinePen, body);
        }
        using (var belly = new SolidBrush(_furLight))
            g.FillEllipse(belly, Cx - 90, 650 + bob, 180, 190);

        // Feet
        DrawFoot(g, Cx - 80, 838, outlinePen);
        DrawFoot(g, Cx + 80, 838, outlinePen);

        // Left arm holding a glass of water in front of the belly.
        DrawGlass(g, t, Cx - 150, 640 + bob);
        DrawPaw(g, Cx - 150, 735 + bob, 28, outlinePen);

        // Head (with a slight tilt)
        float tilt = (float)Math.Sin(t * 1.3) * 5f;
        var s = g.Save();
        g.TranslateTransform(Cx, 600 + bob);
        g.RotateTransform(tilt);
        g.TranslateTransform(-Cx, -(600 + bob));
        DrawHead(g, t, Cx, 470 + bob, outlinePen);
        g.Restore(s);

        // Right arm waving hello.
        DrawWavingArm(g, t, Cx + 120, 640 + bob, outlinePen);
    }

    private void DrawTail(Graphics g, double t, Pen outlinePen)
    {
        float sway = (float)Math.Sin(t * 2.0);
        var p0 = new PointF(Cx + 130, 790);
        var p1 = new PointF(Cx + 290, 820);
        var p2 = new PointF(Cx + 320 + sway * 30, 650);
        var p3 = new PointF(Cx + 250 + sway * 60, 560 - Math.Abs(sway) * 15);

        using var path = new GraphicsPath();
        path.AddBezier(p0, p1, p2, p3);
        using (var outline = new Pen(_outline, 50f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawPath(outline, path);
        using (var fill = new Pen(_fur, 38f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawPath(fill, path);
    }

    private void DrawFoot(Graphics g, float x, float y, Pen outlinePen)
    {
        var r = new RectangleF(x - 50, y - 28, 100, 56);
        using var b = new SolidBrush(_furLight);
        g.FillEllipse(b, r);
        g.DrawEllipse(outlinePen, r);
        using var toe = new Pen(_outline, 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(toe, x - 15, y - 10, x - 15, y + 8);
        g.DrawLine(toe, x + 15, y - 10, x + 15, y + 8);
    }

    private void DrawPaw(Graphics g, float x, float y, float r, Pen outlinePen)
    {
        var rect = new RectangleF(x - r, y - r, r * 2, r * 2);
        using var b = new SolidBrush(_furLight);
        g.FillEllipse(b, rect);
        g.DrawEllipse(outlinePen, rect);
    }

    private void DrawWavingArm(Graphics g, double t, float sx, float sy, Pen outlinePen)
    {
        // Angle from straight up, swinging back and forth.
        double angle = (38 + 22 * Math.Sin(t * 7.0)) * Math.PI / 180;
        const float len = 150;
        var hand = new PointF(sx + (float)Math.Sin(angle) * len, sy - (float)Math.Cos(angle) * len);

        using (var outline = new Pen(_outline, 62f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(outline, sx, sy, hand.X, hand.Y);
        using (var fill = new Pen(_fur, 50f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(fill, sx, sy, hand.X, hand.Y);

        DrawPaw(g, hand.X, hand.Y, 36, outlinePen);
        // Toe beans
        using var bean = new SolidBrush(Pink);
        g.FillEllipse(bean, hand.X - 12, hand.Y - 2, 24, 18);
        g.FillEllipse(bean, hand.X - 22, hand.Y - 20, 11, 11);
        g.FillEllipse(bean, hand.X - 5, hand.Y - 26, 11, 11);
        g.FillEllipse(bean, hand.X + 12, hand.Y - 20, 11, 11);

        // Little motion lines next to the paw.
        using var motion = new Pen(Color.FromArgb(160, _outline), 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        float flick = (float)Math.Cos(t * 7.0);
        if (Math.Abs(flick) > 0.4f)
        {
            float side = flick > 0 ? 1 : -1;
            g.DrawArc(motion, hand.X - 60, hand.Y - 60, 120, 120, side > 0 ? -60 : 200, 40);
            g.DrawArc(motion, hand.X - 78, hand.Y - 78, 156, 156, side > 0 ? -55 : 205, 30);
        }
    }

    private void DrawHead(Graphics g, double t, float hx, float hy, Pen outlinePen)
    {
        using var furBrush = new SolidBrush(_fur);
        using var pinkBrush = new SolidBrush(Pink);

        // Ears (the right one twitches now and then).
        float twitch = (t % 4.0) < 0.25 ? (float)Math.Sin((t % 4.0) / 0.25 * Math.PI) * 10f : 0f;
        DrawEar(g, new PointF(hx - 135, hy - 50), new PointF(hx - 115, hy - 175), new PointF(hx - 35, hy - 120), furBrush, pinkBrush, outlinePen, 0);
        DrawEar(g, new PointF(hx + 135, hy - 50), new PointF(hx + 115, hy - 175), new PointF(hx + 35, hy - 120), furBrush, pinkBrush, outlinePen, twitch);

        var face = new RectangleF(hx - 165, hy - 140, 330, 280);
        g.FillEllipse(furBrush, face);
        g.DrawEllipse(outlinePen, face);

        // Tabby stripes on the forehead.
        using (var stripe = new Pen(_furDark, 10f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(stripe, hx, hy - 132, hx, hy - 95);
            g.DrawLine(stripe, hx - 32, hy - 126, hx - 26, hy - 98);
            g.DrawLine(stripe, hx + 32, hy - 126, hx + 26, hy - 98);
        }

        // Muzzle
        using (var muzzle = new SolidBrush(_furLight))
            g.FillEllipse(muzzle, hx - 70, hy + 20, 140, 90);

        // Eyes: blink every ~3.3s.
        double cycle = t % 3.3;
        bool blink = cycle > 3.12;
        DrawEye(g, hx - 62, hy - 5, blink);
        DrawEye(g, hx + 62, hy - 5, blink);

        // Blush
        using (var blush = new SolidBrush(Blush))
        {
            g.FillEllipse(blush, hx - 130, hy + 30, 56, 30);
            g.FillEllipse(blush, hx + 74, hy + 30, 56, 30);
        }

        // Nose
        using (var nose = new GraphicsPath())
        {
            nose.AddPolygon(new[] { new PointF(hx - 13, hy + 35), new PointF(hx + 13, hy + 35), new PointF(hx, hy + 49) });
            g.FillPath(pinkBrush, nose);
        }

        // "ω" mouth, opening a little like it's talking.
        using var mouthPen = new Pen(Ink, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(mouthPen, hx - 30, hy + 38, 30, 28, 0, 180);
        g.DrawArc(mouthPen, hx, hy + 38, 30, 28, 0, 180);
        float talk = (float)Math.Max(0, Math.Sin(t * 9.0)) * 12f;
        if (talk > 2)
        {
            using var mouth = new SolidBrush(Color.FromArgb(220, 90, 100));
            g.FillEllipse(mouth, hx - 11, hy + 62, 22, talk);
        }

        // Whiskers
        using var whisker = new Pen(Color.FromArgb(200, Ink), 3.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = -1; i <= 1; i++)
        {
            g.DrawLine(whisker, hx - 80, hy + 55 + i * 14, hx - 175, hy + 45 + i * 26);
            g.DrawLine(whisker, hx + 80, hy + 55 + i * 14, hx + 175, hy + 45 + i * 26);
        }
    }

    private static void DrawEar(Graphics g, PointF a, PointF tip, PointF b, Brush fur, Brush pink, Pen outline, float rotate)
    {
        var s = g.Save();
        if (rotate != 0)
        {
            g.TranslateTransform(a.X, a.Y);
            g.RotateTransform(rotate);
            g.TranslateTransform(-a.X, -a.Y);
        }
        var outer = new[] { a, tip, b };
        g.FillPolygon(fur, outer);
        g.DrawPolygon(outline, outer);

        var c = new PointF((a.X + tip.X + b.X) / 3, (a.Y + tip.Y + b.Y) / 3);
        PointF Inset(PointF p) => new(c.X + (p.X - c.X) * 0.55f, c.Y + (p.Y - c.Y) * 0.55f + 8);
        g.FillPolygon(pink, new[] { Inset(a), Inset(tip), Inset(b) });
        g.Restore(s);
    }

    private static void DrawEye(Graphics g, float x, float y, bool blink)
    {
        if (blink)
        {
            using var pen = new Pen(Ink, 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, x - 22, y - 12, 44, 26, 20, 140);
            return;
        }
        using (var eye = new SolidBrush(Ink))
            g.FillEllipse(eye, x - 22, y - 28, 44, 56);
        using var shine = new SolidBrush(Color.White);
        g.FillEllipse(shine, x - 12, y - 20, 17, 17);
        g.FillEllipse(shine, x + 5, y + 6, 8, 8);
    }

    private static void DrawGlass(Graphics g, double t, float x, float y)
    {
        // Trapezoid glass: x,y = top-left.
        const float wTop = 100, wBottom = 80, h = 130;
        var glass = new[]
        {
            new PointF(x, y), new PointF(x + wTop, y),
            new PointF(x + (wTop + wBottom) / 2, y + h), new PointF(x + (wTop - wBottom) / 2, y + h),
        };

        // Water with a sloshing surface.
        float level = y + 38;
        float slosh = (float)Math.Sin(t * 3.0) * 6f;
        using (var water = new GraphicsPath())
        {
            float inset = (level - y) / h * (wTop - wBottom) / 2;
            water.AddLine(x + inset + 4, level + slosh, x + wTop - inset - 4, level - slosh);
            water.AddLine(x + (wTop + wBottom) / 2 - 3, y + h - 4, x + (wTop - wBottom) / 2 + 3, y + h - 4);
            water.CloseFigure();
            using var wb = new SolidBrush(Water);
            g.FillPath(wb, water);
        }

        // Straw
        using (var straw = new Pen(Color.FromArgb(255, 110, 140), 9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(straw, x + 60, y + 100, x + 72, y - 40);
            g.DrawLine(straw, x + 72, y - 40, x + 100, y - 55);
        }

        using (var glassFill = new SolidBrush(Color.FromArgb(70, 220, 240, 255)))
            g.FillPolygon(glassFill, glass);
        using (var glassPen = new Pen(Color.FromArgb(230, 80, 140, 200), 5f) { LineJoin = LineJoin.Round })
            g.DrawPolygon(glassPen, glass);
        using var gleam = new Pen(Color.FromArgb(200, 255, 255, 255), 6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(gleam, x + 16, y + 18, x + 22, y + 100);
    }

    // ---------------------------------------------------------------- drops

    private static void DrawDrops(Graphics g, double t)
    {
        // A handful of water drops drifting upward around the cat.
        ReadOnlySpan<float> xs = stackalloc float[] { 90, 170, 640, 720, 120, 690, 40, 760 };
        using var brush = new SolidBrush(Color.White);
        for (int i = 0; i < xs.Length; i++)
        {
            double speed = 45 + i * 7;
            double range = 520;
            double p = (t * speed + i * 137) % range;
            float y = 860 - (float)p;
            float x = xs[i] + (float)Math.Sin(t * 1.5 + i) * 14;
            float fade = (float)Math.Sin(p / range * Math.PI); // fade in and out
            float size = 22 + (i % 3) * 8;
            if (i % 3 == 1)
            {
                brush.Color = Color.FromArgb((int)(210 * fade), 255, 120, 160);
                FillHeart(g, brush, x, y, size * 1.1f);
            }
            else
            {
                brush.Color = Color.FromArgb((int)(200 * fade), 110, 190, 255);
                FillDrop(g, brush, x, y, size);
            }
        }
    }

    private static void FillDrop(Graphics g, Brush brush, float x, float y, float size)
    {
        using var path = new GraphicsPath();
        float r = size / 2;
        path.AddArc(x - r, y - r, size, size, -30, 240);
        path.AddLine(x - r * 0.87f, y - r * 0.5f, x, y - size * 1.25f);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    private static void FillHeart(Graphics g, Brush brush, float x, float y, float size)
    {
        using var path = new GraphicsPath();
        float r = size / 4;
        path.AddArc(x - 2 * r, y - r * 1.6f, 2 * r, 2 * r, 150, 210);
        path.AddArc(x, y - r * 1.6f, 2 * r, 2 * r, 180, 210);
        path.AddLine(x + r * 1.85f, y + r * 0.1f, x, y + size * 0.55f);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    // --------------------------------------------------------------- bubble

    private void DrawBubble(Graphics g, double t, TimeSpan remaining)
    {
        if (_celebrateTitle is not null && t - _celebrateAt >= CelebrateTextSeconds)
            _celebrateTitle = _celebrateSub = null; // back to the normal message + countdown

        float pulse = 1f + (float)Math.Sin(t * 3.2) * 0.025f;
        var s = g.Save();
        g.TranslateTransform(Cx, 150);
        g.ScaleTransform(pulse, pulse);
        g.TranslateTransform(-Cx, -150);

        var rect = new RectangleF(40, 20, CanvasWidth - 80, 240);
        using (var shadow = Drawing.RoundedRect(new RectangleF(rect.X + 6, rect.Y + 10, rect.Width, rect.Height), 60))
        using (var shadowBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            g.FillPath(shadowBrush, shadow);

        using var bubble = Drawing.RoundedRect(rect, 60);
        // Tail pointing down at the cat's head.
        using var tail = new GraphicsPath();
        tail.AddPolygon(new[] { new PointF(Cx - 40, rect.Bottom - 4), new PointF(Cx + 40, rect.Bottom - 4), new PointF(Cx + 10, rect.Bottom + 55) });

        using var fill = new SolidBrush(Color.FromArgb(255, 255, 252, 245));
        using var border = new Pen(BubbleBorder, 7f) { LineJoin = LineJoin.Round };
        g.FillPath(fill, tail);
        g.DrawPath(border, tail);
        g.FillPath(fill, bubble);
        g.DrawPath(border, bubble);
        // Hide the seam between bubble and tail.
        g.FillRectangle(fill, Cx - 34, rect.Bottom - 8, 66, 10);

        var textArea = RectangleF.Inflate(rect, -40, -20);
        DrawFittedText(g, _celebrateTitle ?? _config.Message, FontStyle.Bold, 92, Color.FromArgb(255, 235, 105, 60),
            new RectangleF(textArea.X, textArea.Y + 5, textArea.Width, 125));

        string sub = _celebrateSub ?? _config.SubMessage;
        if (_config.ShowCountdown && _celebrateSub is null)
        {
            int secs = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            string countdown = $"還剩 {secs / 60:00}:{secs % 60:00}";
            sub = string.IsNullOrWhiteSpace(sub) ? countdown : $"{sub}  {countdown}";
        }
        if (!string.IsNullOrWhiteSpace(sub))
            DrawFittedText(g, sub, FontStyle.Regular, 36, Color.FromArgb(255, 110, 85, 75),
                new RectangleF(textArea.X, textArea.Y + 135, textArea.Width, 55));

        g.Restore(s);
    }

    private void DrawFittedText(Graphics g, string text, FontStyle style, float emSize, Color color, RectangleF box) =>
        Drawing.DrawFittedText(g, _font, text, style, emSize, color, box);

    private static float EaseOutBounce(float x)
    {
        const float n1 = 7.5625f, d1 = 2.75f;
        if (x < 1 / d1) return n1 * x * x;
        if (x < 2 / d1) { x -= 1.5f / d1; return n1 * x * x + 0.75f; }
        if (x < 2.5f / d1) { x -= 2.25f / d1; return n1 * x * x + 0.9375f; }
        x -= 2.625f / d1;
        return n1 * x * x + 0.984375f;
    }

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1;
        return 1 + c3 * MathF.Pow(x - 1, 3) + c1 * MathF.Pow(x - 1, 2);
    }

    private static Color Shade(Color c, float f) =>
        Color.FromArgb(c.A, (int)(c.R * f), (int)(c.G * f), (int)(c.B * f));

    private static Color Mix(Color a, Color b, float f) =>
        Color.FromArgb(255,
            (int)(a.R + (b.R - a.R) * f),
            (int)(a.G + (b.G - a.G) * f),
            (int)(a.B + (b.B - a.B) * f));
}
