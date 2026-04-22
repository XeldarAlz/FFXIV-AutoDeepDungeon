using System;

namespace AutoDeepDungeon.Helpers;

/// <summary>
/// Log-normal delay generator used to add human-like jitter between automated actions.
/// The log-normal shape concentrates most samples near the median while still producing
/// the occasional slow sample, which is harder to distinguish from real input than a
/// uniform distribution.
/// </summary>
public static class Humanizer
{
    private const double MedianMs = 650.0;
    private const double Sigma    = 0.35;

    private static readonly Random Rng = Random.Shared;

    public static int NextDelayMs(int minMs = 400, int maxMs = 1200)
    {
        var u1 = 1.0 - Rng.NextDouble();
        var u2 = 1.0 - Rng.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        var ms = Math.Exp(Math.Log(MedianMs) + Sigma * normal);
        return (int)Math.Clamp(ms, minMs, maxMs);
    }

    public static TimeSpan NextDelay(int minMs = 400, int maxMs = 1200)
        => TimeSpan.FromMilliseconds(NextDelayMs(minMs, maxMs));
}
