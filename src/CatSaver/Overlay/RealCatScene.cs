namespace CatSaver.Overlay;

/// <summary>
/// One real (video) cat acting out a routine across the full width of the screen:
/// it walks in from the left, sits down under the speech bubble, looks around, does
/// something (meows, waves, drinks...), stands up, walks off to the right, then comes
/// back from the right, sits again and leaves to the left. Repeats.
/// All clips are walked/aligned by their <see cref="SpriteCatalog.SheetInfo.AnchorX"/>
/// and cross-fade briefly where two clips meet.
/// </summary>
internal sealed class RealCatScene : IDisposable
{
    private const float SitHeight = 430;      // logical units for the sitting cat
    private const double IdleSeconds = 3.0;   // looking around before and after each act
    private const double Fade = 0.3;          // cross-fade between clips

    private readonly SpritePlayer _player;
    private readonly SpriteCatalog.SheetInfo _walk, _sit;
    private readonly List<SpriteCatalog.SheetInfo> _sitDowns;
    private readonly List<SpriteCatalog.SheetInfo> _acts;
    private readonly float _k; // logical units per sprite pixel (same for every clip)

    public RealCatScene(SpriteCatalog catalog, SpriteCatalog.CatSet set, Random rng)
    {
        _walk = catalog.Sheets[set.Walkers[rng.Next(set.Walkers.Count)]];
        _sit = catalog.Sheets[set.Emoji[rng.Next(set.Emoji.Count)]];
        _sitDowns = set.SitDowns.Select(a => catalog.Sheets[a]).OrderBy(_ => rng.Next()).ToList();
        _acts = set.Acts.Select(a => catalog.Sheets[a]).OrderBy(_ => rng.Next()).ToList();
        _player = new SpritePlayer(catalog, Array.Empty<string>());
        _k = SitHeight / _sit.FrameHeight;
    }

    // ---- timings --------------------------------------------------------------

    /// <summary>How the cat sits down on this visit (e.g. walk-and-sit, or stretch-then-sit); they alternate.</summary>
    // (visit + visit / 2) so each way of sitting down happens from both sides.
    private SpriteCatalog.SheetInfo? SitDown(int visit) =>
        _sitDowns.Count == 0 ? null : _sitDowns[(visit + visit / 2) % _sitDowns.Count];
    private double SitDownSeconds(int visit) => SitDown(visit)?.Seconds ?? 0;
    /// <summary>Part of the sit-down clip before the cat starts sitting.</summary>
    private double BeforeSitSeconds(int visit) =>
        SitDown(visit) is { } s ? s.WalkFrames * s.FrameMs / 1000.0 : 0;
    /// <summary>How long the cat keeps moving during the sit-down clip (only if it walks in it).</summary>
    private double MovingSeconds(int visit) => SitDown(visit) is { Walks: true } ? BeforeSitSeconds(visit) : 0;
    /// <summary>Standing up = the sitting part of the sit-down clip, backwards.</summary>
    private double StandUpSeconds(int visit) => SitDownSeconds(visit) - BeforeSitSeconds(visit);

    private SpriteCatalog.SheetInfo? Act(int visit) => _acts.Count == 0 ? null : _acts[visit % _acts.Count];
    private double SitSeconds(int visit) => IdleSeconds * 2 + (Act(visit)?.Seconds ?? 0);

    /// <summary>
    /// Draws the cat for time <paramref name="t"/>. <paramref name="left"/>/<paramref name="right"/>
    /// are the visible logical x range; it sits at <paramref name="centerX"/>.
    /// </summary>
    public void Draw(Graphics g, double t, float left, float right, float centerX, float ground)
    {
        float walkW = _walk.FrameWidth * _k;
        double speed = Math.Max(0.1, _walk.Speed) * walkW; // logical units per second

        // A "visit": walk in, sit down, sit, stand up, walk out. Two visits per round
        // (in from the left and out right, then in from the right and out left).
        double outDist = right + walkW - centerX;
        double visitOut = outDist / speed;
        double Approach(int v) => MovingSeconds(v) * speed; // still walking during the sit-down clip
        double VisitIn(int v) => (centerX - Approach(v) - (left - walkW)) / speed;
        double Visit(int v) => VisitIn(v) + SitDownSeconds(v) + SitSeconds(v) + StandUpSeconds(v) + visitOut;

        // Visits differ in length (different sit-downs and acts), so step through them;
        // the pattern repeats after cycleVisits visits.
        int visit = 0;
        double p = t;
        double period = 0;
        int cycleVisits = Lcm(Lcm(2, Math.Max(1, _sitDowns.Count) * 2), Math.Max(1, _acts.Count)) * 2;
        for (int v = 0; v < cycleVisits; v++) period += Visit(v);
        p %= period;
        while (p >= Visit(visit)) { p -= Visit(visit); visit++; }
        bool fromRight = visit % 2 == 1;
        var sitDown = SitDown(visit);
        double visitIn = VisitIn(visit);
        float approach = (float)Approach(visit);
        int dir = fromRight ? -1 : 1;
        float edgeIn = fromRight ? right + walkW : left - walkW;

        // 1. Walk in.
        if (p < visitIn) { DrawWalk(g, edgeIn + dir * (float)(p * speed), fromRight, p, ground, 1); return; }
        p -= visitIn;
        float stopX = centerX - dir * approach;

        // 2. Sit down (moving on while the clip still shows walking).
        if (p < SitDownSeconds(visit) && sitDown is not null)
        {
            float x = stopX + dir * (float)(Math.Min(p, MovingSeconds(visit)) * speed);
            float k = (float)Math.Clamp(p / Fade, 0, 1);
            if (k < 1) DrawWalk(g, x, fromRight, visitIn + p, ground, 1 - k);
            DrawSheet(g, sitDown, FrameOf(sitDown, p), x, ground, fromRight, k);
            return;
        }
        p -= SitDownSeconds(visit);

        // 3. Sit: look around, do something, look around.
        double sit = SitSeconds(visit);
        if (p < sit) { DrawSitting(g, centerX, fromRight, p, visit, ground); return; }
        p -= sit;

        // 4. Stand up (the sit-down clip backwards, from sitting back to standing).
        if (p < StandUpSeconds(visit) && sitDown is not null)
        {
            int frame = sitDown.Frames - 1 - FrameOf(sitDown, p);
            float k = (float)Math.Clamp(p / Fade, 0, 1);
            if (k < 1) DrawSheet(g, _sit, 0, centerX, ground, fromRight, 1 - k);
            DrawSheet(g, sitDown, Math.Max(frame, sitDown.WalkFrames), centerX, ground, fromRight, k);
            return;
        }
        p -= StandUpSeconds(visit);

        // 5. Walk out the other side.
        float fade = (float)Math.Clamp(p / Fade, 0, 1);
        if (fade < 1 && sitDown is not null)
            DrawSheet(g, sitDown, sitDown.WalkFrames, centerX, ground, fromRight, 1 - fade);
        DrawWalk(g, centerX + dir * (float)(p * speed), fromRight, p, ground, fade);
    }

    private static int Lcm(int a, int b)
    {
        int x = a, y = b;
        while (y != 0) (x, y) = (y, x % y);
        return a / x * b;
    }

    private void DrawSitting(Graphics g, float x, bool mirror, double p, int visit, float ground)
    {
        var act = Act(visit);
        double actSeconds = act?.Seconds ?? 0;

        // Fade in from the end of the sit-down clip, out into the stand-up clip.
        float start = (float)Math.Clamp(p / Fade, 0, 1);
        if (start < 1 && SitDown(visit) is { } sitDown)
            DrawSheet(g, sitDown, sitDown.Frames - 1, x, ground, mirror, 1 - start);

        bool acting = act is not null && p >= IdleSeconds && p < IdleSeconds + actSeconds;
        if (!acting)
        {
            double idleT = p < IdleSeconds ? p : p - IdleSeconds - actSeconds;
            DrawSheet(g, _sit, FrameOf(_sit, idleT), x, ground, mirror, start);
            // About to start the act: blend it in.
            if (act is not null && p < IdleSeconds && IdleSeconds - p < Fade)
                DrawSheet(g, act, 0, x, ground, mirror, (float)(1 - (IdleSeconds - p) / Fade));
            return;
        }

        double actT = p - IdleSeconds;
        float actOpacity = (float)Math.Clamp((actSeconds - actT) / Fade, 0, 1); // blend out at the end
        if (actOpacity < 1) DrawSheet(g, _sit, 0, x, ground, mirror, 1);
        DrawSheet(g, act!, FrameOf(act!, actT), x, ground, mirror, actOpacity);
    }

    private void DrawWalk(Graphics g, float x, bool headingLeft, double t, float ground, float opacity) =>
        DrawSheet(g, _walk, _walk.FrameAt((int)(t * 1000 / _walk.FrameMs)), x, ground, headingLeft, opacity);

    private static int FrameOf(SpriteCatalog.SheetInfo sheet, double seconds) =>
        sheet.PingPong ? sheet.FrameAt((int)(seconds * 1000 / sheet.FrameMs))
                       : Math.Clamp((int)(seconds * 1000 / sheet.FrameMs), 0, sheet.Frames - 1);

    /// <summary>Draws a frame with its anchor at <paramref name="x"/> and its bottom on the ground.</summary>
    private void DrawSheet(Graphics g, SpriteCatalog.SheetInfo sheet, int frame, float x, float ground, bool mirror, float opacity)
    {
        if (opacity <= 0.01f) return;
        float w = sheet.FrameWidth * _k, h = sheet.FrameHeight * _k;
        float anchor = (float)(mirror ? 1 - sheet.AnchorX : sheet.AnchorX);
        var dest = new RectangleF(x - anchor * w, ground - h + 6, w, h);

        if (opacity > 0.5f)
        {
            using var shadow = new SolidBrush(Color.FromArgb((int)(45 * opacity), 0, 0, 0));
            float sw = Math.Min(w, 380) * 0.8f;
            g.FillEllipse(shadow, x - sw / 2, ground - 12, sw, 26);
        }
        _player.DrawFrame(g, sheet, frame, dest, mirror, opacity);
    }

    public void Release() => _player.Release();

    public void Dispose() => _player.Dispose();
}
