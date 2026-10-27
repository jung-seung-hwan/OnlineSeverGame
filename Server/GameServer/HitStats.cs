using OnlineSeverGame.Shared.Simulation;

namespace OnlineSeverGame.Server;

/// <summary>
/// Compares the two hazard hit rules over a round, for logs: the drawn shapes (HitShapes, what decides now) and
/// the old boxes (hazard spec box vs the 800 x 1200 movement box). For every falling hazard and every living
/// participant it keeps the closest gap under each rule, then counts deaths the other rule would have spared,
/// survivors the other rule would have killed, and near misses (active-rule gap under 0.1 u).
/// </summary>
public sealed class HitStats
{
    public const int NearMissGap = 100;

    private sealed class Pair
    {
        public int MinVisual = int.MaxValue;
        public int MinLegacy = int.MaxValue;
        public bool Died;
    }

    private readonly Dictionary<(ushort Knife, byte Player), Pair> _pairs = new();

    public int Deaths { get; private set; }
    /// <summary>Deaths where the other rule did not touch on the death tick ("hit without a visible touch" when legacy decides).</summary>
    public int DeathsOtherMissed { get; private set; }
    /// <summary>Hazards that passed a player without killing, although the other rule overlapped at some tick.</summary>
    public int SurvivorsOtherHit { get; private set; }
    public int NearMisses { get; private set; }

    public void Record(ushort knife, byte player, bool hit, int visual, int legacy, bool legacyActive)
    {
        if (!_pairs.TryGetValue((knife, player), out var pair)) _pairs[(knife, player)] = pair = new Pair();
        pair.MinVisual = Math.Min(pair.MinVisual, visual);
        pair.MinLegacy = Math.Min(pair.MinLegacy, legacy);
        if (!hit) return;
        pair.Died = true;
        Deaths++;
        if ((legacyActive ? visual : legacy) >= 0) DeathsOtherMissed++;
    }

    /// <summary>The hazard stopped falling: settle its pairs. log != null writes each notable pair.</summary>
    public void Finish(ushort knife, HazardKind kind, bool legacyActive, Action<string> log)
    {
        List<(ushort, byte)> done = null;
        foreach (var kv in _pairs)
        {
            if (kv.Key.Knife != knife) continue;
            (done ??= new()).Add(kv.Key);
            var p = kv.Value;
            if (p.Died) continue;
            int active = legacyActive ? p.MinLegacy : p.MinVisual;
            int other = legacyActive ? p.MinVisual : p.MinLegacy;
            string name = kind.ToString().ToLowerInvariant();
            if (other < 0)
            {
                SurvivorsOtherHit++;
                log?.Invoke($"spared #{kv.Key.Player} by {name}#{knife}: {(legacyActive ? "drawn shapes" : "old boxes")} overlapped " +
                            $"{-other / 1000.0:0.00} u, active gap {active / 1000.0:0.00} u");
            }
            else if (active < NearMissGap)
            {
                NearMisses++;
                log?.Invoke($"near  #{kv.Key.Player} by {name}#{knife}: gap {active / 1000.0:0.00} u");
            }
        }
        if (done != null)
            foreach (var key in done) _pairs.Remove(key);
    }

    /// <summary>One line for the round summary, then starts over.</summary>
    public string TakeSummary(bool legacyActive)
    {
        string other = legacyActive ? "drawn shapes" : "old boxes";
        string line = $"hit rule {(legacyActive ? "old boxes (--legacy-hitbox)" : "drawn shapes")}: deaths {Deaths} " +
                      $"(not touching under {other}: {DeathsOtherMissed}), survivors {other} would have killed: {SurvivorsOtherHit}, " +
                      $"near misses (< {NearMissGap / 1000.0:0.0} u): {NearMisses}";
        _pairs.Clear();
        Deaths = DeathsOtherMissed = SurvivorsOtherHit = NearMisses = 0;
        return line;
    }
}
