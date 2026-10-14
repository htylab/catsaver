using System.Text.Json;

namespace CatSaver.Overlay;

/// <summary>
/// The embedded sprite sheets and the cat sets / drink props built from them:
/// <list type="bullet">
/// <item>Assets/Noto/sprites.json: Noto animated emoji (CC BY 4.0, Google), from tools/make_noto_sprites.py</item>
/// <item>Assets/RealCats/realcats.json: background-removed green-screen cat videos, from tools/make_real_cats.py</item>
/// </list>
/// </summary>
internal sealed class SpriteCatalog
{
    public sealed class SheetInfo
    {
        public string Name { get; set; } = "";
        public int Frames { get; set; }
        public int Columns { get; set; }
        public int Size { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameMs { get; set; } = 66;
        /// <summary>Play forward then backward (for idle clips that don't loop on their own).</summary>
        public bool PingPong { get; set; }
        /// <summary>Walking clips: ground speed in body widths per second.</summary>
        public double Speed { get; set; }
        /// <summary>Horizontal centre of the cat's body as a fraction of the frame width (to line clips up).</summary>
        public double AnchorX { get; set; } = 0.5;
        /// <summary>Sit-down clips: the frame where sitting starts (standing up plays from the end back to here).
        /// Stand-up clips: the frame where the cat has stood up and starts walking.</summary>
        public int WalkFrames { get; set; }
        /// <summary>Sit-down clips: true if the cat walks (in place) before sitting, so it should keep moving.</summary>
        public bool Walks { get; set; } = true;
        /// <summary>Stand-up clips: the cat's centre (fraction of the frame width) in the first frame and while walking off.</summary>
        public double StartAnchorX { get; set; } = 0.5;
        public double EndAnchorX { get; set; } = 0.5;

        public double Seconds => Frames * FrameMs / 1000.0;

        /// <summary>Embedded resource prefix the sheet lives under (set on load).</summary>
        public string ResourcePrefix { get; set; } = "";

        public int FrameWidth => Width > 0 ? Width : Size;
        public int FrameHeight => Height > 0 ? Height : Size;
        public int PlayFrames => PingPong && Frames > 2 ? Frames * 2 - 2 : Frames;
        public double LoopSeconds => PlayFrames * FrameMs / 1000.0;

        /// <summary>Sheet frame index for the n-th played frame (handles ping-pong).</summary>
        public int FrameAt(int n)
        {
            n %= PlayFrames;
            return n < Frames ? n : PlayFrames - n;
        }
    }

    public sealed class CatSet
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Sheets for the cat in the middle. Emoji sets play them in turn; real-cat sets pick one.</summary>
        public List<string> Emoji { get; set; } = new();
        /// <summary>Real-cat sets: the cat walking in place (it is moved across the screen).</summary>
        public List<string> Walkers { get; set; } = new();
        /// <summary>Real-cat sets: things the sitting cat does now and then (meow, wave, drink...).</summary>
        public List<string> Acts { get; set; } = new();
        /// <summary>Real-cat sets: the cat walking in and sitting down (also played backwards to stand up).</summary>
        public List<string> SitDowns { get; set; } = new();
        /// <summary>Real-cat sets: the cat getting up and walking off (else a sit-down clip is played backwards).</summary>
        public List<string> StandUps { get; set; } = new();

        public bool IsReal => Walkers.Count > 0;
    }

    private sealed class Catalog
    {
        public List<SheetInfo> Sprites { get; set; } = new();
        public List<CatSet> Sets { get; set; } = new();
        public List<string> Props { get; set; } = new();
    }

    public IReadOnlyDictionary<string, SheetInfo> Sheets { get; }
    public IReadOnlyList<CatSet> Sets { get; }
    public IReadOnlyList<string> Props { get; }

    private SpriteCatalog(List<(string Prefix, Catalog Catalog)> parts)
    {
        var sheets = new Dictionary<string, SheetInfo>();
        foreach (var (prefix, c) in parts)
            foreach (var s in c.Sprites)
            {
                s.ResourcePrefix = prefix;
                sheets[s.Name] = s;
            }
        Sheets = sheets;
        // Drop anything that references a missing sheet.
        Sets = parts.SelectMany(p => p.Catalog.Sets)
            .Where(s => s.Emoji.Count > 0 && s.Emoji.All(sheets.ContainsKey) && s.Walkers.All(sheets.ContainsKey)
                        && s.Acts.All(sheets.ContainsKey) && s.SitDowns.All(sheets.ContainsKey)
                        && s.StandUps.All(sheets.ContainsKey))
            .ToList();
        Props = parts.SelectMany(p => p.Catalog.Props).Where(sheets.ContainsKey).ToList();
    }

    private static readonly Lazy<SpriteCatalog?> Shared = new(Load);

    /// <summary>The embedded catalog, or null if the assets are missing/broken.</summary>
    public static SpriteCatalog? Instance => Shared.Value;

    private static SpriteCatalog? Load()
    {
        var parts = new List<(string, Catalog)>();
        foreach (var (prefix, file) in new[] { ("CatSaver.Noto.", "sprites.json"), ("CatSaver.RealCats.", "realcats.json") })
        {
            try
            {
                using var stream = typeof(SpriteCatalog).Assembly.GetManifestResourceStream(prefix + file);
                if (stream is null) continue;
                var catalog = JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (catalog is not null) parts.Add((prefix, catalog));
            }
            catch (Exception ex)
            {
                Log.Error($"Loading {file}", ex);
            }
        }
        var result = new SpriteCatalog(parts);
        return result.Sets.Count > 0 ? result : null;
    }

    public CatSet? FindSet(string id) => Sets.FirstOrDefault(s => s.Id == id);
}
