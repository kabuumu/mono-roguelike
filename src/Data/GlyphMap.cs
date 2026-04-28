namespace MonoRogue.Data;

using System.Collections.Immutable;
using System.Text.Json;

/// <summary>
/// Maps a semantic SpriteKey to an ASCII glyph and optional override color.
/// This is the ASCII-mode renderer's lookup table.
///
/// When upgrading to sprite mode, add a <c>SpriteIndex</c> class that maps
/// the same SpriteKey strings to texture atlas coordinates. The Core never
/// changes — only the renderer switches.
/// </summary>
public readonly record struct GlyphEntry(char Glyph, string? ColorOverride = null);

public sealed record GlyphMap(ImmutableDictionary<string, GlyphEntry> Entries)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip,
        AllowTrailingCommas         = true,
    };

    /// <summary>Fallback glyph when a SpriteKey has no mapping.</summary>
    public static readonly GlyphEntry Fallback = new('?', "#FF00FF");

    public static GlyphMap LoadFrom(string jsonPath)
    {
        var json    = File.ReadAllText(jsonPath);
        var raw     = JsonSerializer.Deserialize<Dictionary<string, GlyphEntry>>(json, JsonOptions) ?? [];
        var entries = raw.ToImmutableDictionary();
        return new GlyphMap(entries);
    }

    /// <summary>Returns an empty map for testing (everything renders as '?').</summary>
    public static GlyphMap Empty() =>
        new(ImmutableDictionary<string, GlyphEntry>.Empty);

    /// <summary>
    /// Resolves a SpriteKey to a glyph. Returns <see cref="Fallback"/> if unmapped.
    /// </summary>
    public GlyphEntry Resolve(string spriteKey) =>
        Entries.TryGetValue(spriteKey, out var entry) ? entry : Fallback;
}
