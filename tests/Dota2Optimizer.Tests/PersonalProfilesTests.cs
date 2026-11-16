using Dota2Optimizer.Core.Tweaks;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Tests;

public class PersonalProfilesTests
{
    [Fact] public void Empty_profile_stays_empty_and_false_is_preserved()
    {
        var fs = new InMemoryFileSystem(); var store = new PersonalProfileStore(fs, "profiles.json");
        store.Save(new[] { new PersonalProfile("empty", "Пустой", new()), new PersonalProfile("undo", "Откат", new() { ["win.gamedvr"] = false }) });
        Assert.Empty(store.Load()[0].Settings); Assert.False(store.Load()[1].Settings["win.gamedvr"]);
    }
    [Fact] public void Corrupt_profiles_are_not_silently_replaced()
    {
        var fs = new InMemoryFileSystem(); fs.WriteAllText("p.json", "{broken");
        Assert.Throws<System.Text.Json.JsonException>(() => new PersonalProfileStore(fs,"p.json").Load());
        Assert.Equal("{broken",fs.ReadAllText("p.json"));
    }
    [Fact] public void Duplicate_profile_ids_are_rejected()
    {
        var store = new PersonalProfileStore(new InMemoryFileSystem(), "p.json");
        Assert.Throws<InvalidDataException>(() => store.Save(new[] { new PersonalProfile("a","A",new()), new PersonalProfile("a","B",new()) }));
    }
    [Fact] public void Arena_comparison_separates_render_sizes_and_revisions()
    {
        var before = new BenchResult { Game = new ArenaRunSpec(960,540,2).Key, AvgFps=60, Low1Fps=50, DurationSec=30 };
        var after = before with { Label="after", Time=before.Time.AddMinutes(1) };
        Assert.True(ShareComparison.CanCompare(before,after));
        Assert.False(ShareComparison.CanCompare(before,after with { Game=new ArenaRunSpec(1280,720,2).Key }));
        Assert.False(ShareComparison.CanCompare(before,after with { Game="synthetic-scene-v1" }));
        Assert.False(ShareComparison.CanCompare(before,after with { AvgFps=double.NaN }));
        Assert.False(ShareComparison.CanCompare(before,after with { DurationSec=60 }));
    }
}
