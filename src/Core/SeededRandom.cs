namespace MonoRogue.Core;

/// <summary>
/// Deterministic random number generator wrapper.
/// Always constructed with an explicit seed — never uses Environment.TickCount.
/// Use <see cref="Split"/> to derive child streams for subsystems so they don't
/// interfere with each other while remaining fully reproducible.
/// </summary>
public sealed class SeededRandom
{
    private readonly Random _rng;
    public int Seed { get; }

    public SeededRandom(int seed)
    {
        Seed = seed;
        _rng = new Random(seed);
    }

    public int Next(int minInclusive, int maxExclusive) =>
        _rng.Next(minInclusive, maxExclusive);

    public int Next(int maxExclusive) => _rng.Next(maxExclusive);

    public bool NextBool(double probability = 0.5) =>
        _rng.NextDouble() < probability;

    /// <summary>
    /// Derives a child RNG with a deterministic seed so subsystems get
    /// independent streams without sharing state.
    /// </summary>
    public SeededRandom Split(int salt) =>
        new(HashCode.Combine(Seed, salt));

    public SeededRandom Split(string salt) =>
        new(HashCode.Combine(Seed, salt.GetHashCode(StringComparison.Ordinal)));
}
