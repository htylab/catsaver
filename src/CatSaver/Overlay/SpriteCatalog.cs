using System.Text.Json;

namespace CatSaver.Overlay;

/// <summary>
/// The embedded Noto emoji sprite sheets (CC BY 4.0, Google) and the cat sets /
/// drink props defined in Assets/Noto/sprites.json. Regenerate with
/// tools/make_noto_sprites.py.
/// </summary>
internal sealed class SpriteCatalog
{
    internal const string ResourcePrefix = "CatSaver.Noto.";

    public sealed record SheetInfo(string Name, int Frames, int Columns, int Size, int FrameMs)
    {
        public double LoopSeconds => Frames * FrameMs / 1000.0;
    }

    public sealed record CatSet(string Id, string Name, List<string> Emoji);

    private sealed record Catalog(List<SheetInfo> Sprites, List<CatSet> Sets, List<string> Props);

    public IReadOnlyDictionary<string, SheetInfo> Sheets { get; }
    public IReadOnlyList<CatSet> Sets { get; }
    public IReadOnlyList<string> Props { get; }

    private SpriteCatalog(Catalog c)
    {
        Sheets = c.Sprites.ToDictionary(s => s.Name);
        // Drop anything that references a missing sheet.
        Sets = c.Sets.Where(s => s.Emoji.Count > 0 && s.Emoji.All(Sheets.ContainsKey)).ToList();
        Props = c.Props.Where(Sheets.ContainsKey).ToList();
    }

    private static readonly Lazy<SpriteCatalog?> Shared = new(Load);

    /// <summary>The embedded catalog, or null if the assets are missing/broken.</summary>
    public static SpriteCatalog? Instance => Shared.Value;

    private static SpriteCatalog? Load()
    {
        try
        {
            using var stream = typeof(SpriteCatalog).Assembly.GetManifestResourceStream(ResourcePrefix + "sprites.json");
            if (stream is null) return null;
            var catalog = JsonSerializer.Deserialize<Catalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (catalog is null) return null;
            var result = new SpriteCatalog(catalog);
            return result.Sets.Count > 0 ? result : null;
        }
        catch (Exception ex)
        {
            Log.Error("Loading sprites.json", ex);
            return null;
        }
    }

    public CatSet? FindSet(string id) => Sets.FirstOrDefault(s => s.Id == id);
}
