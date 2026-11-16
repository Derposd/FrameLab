namespace Dota2Optimizer.Core.Benchmark;

/// <summary>A new scene revision cannot be compared with the old cube test or a different render size.</summary>
public sealed record ArenaRunSpec(int Width, int Height, int RenderTier)
{
    public const string Revision = "arena-night-v1";
    public string Key => $"{Revision}:{Width}x{Height}:tier{RenderTier}";
    public string Description => $"Arena · Ночной город · {Width} × {Height} · WPF tier {RenderTier}";
    public bool Valid => Width >= 200 && Height >= 100 && RenderTier is >= 0 and <= 2;
}

public static class ShareComparison
{
    public static bool CanCompare(BenchResult? before, BenchResult? after) =>
        before is not null && after is not null && before.Game == after.Game && before.Label == "before" && after.Label == "after" &&
        after.Time >= before.Time && Valid(before) && Valid(after) && Math.Abs(before.DurationSec - after.DurationSec) <= 2;

    private static bool Valid(BenchResult r) => double.IsFinite(r.AvgFps) && r.AvgFps > 0 &&
        double.IsFinite(r.Low1Fps) && r.Low1Fps >= 0 && double.IsFinite(r.DurationSec) && r.DurationSec >= 5;
}
