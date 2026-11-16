using System.Text.Json;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.Core.Tweaks;

/// <summary>Only explicitly listed tweaks participate. False restores our backup, never Windows defaults.</summary>
public sealed record PersonalProfile(string Id, string Name, Dictionary<string, bool> Settings)
{
    public override string ToString() => Name;
}

public sealed class PersonalProfileStore(IFileSystem fs, string path)
{
    private sealed record Document(int Version, List<PersonalProfile> Profiles);
    public IReadOnlyList<PersonalProfile> Load()
    {
        if (!fs.FileExists(path)) return Defaults();
        var doc = JsonSerializer.Deserialize<Document>(fs.ReadAllText(path));
        if (doc is not { Version: 1, Profiles: not null }) throw new InvalidDataException("Неизвестный формат личных профилей. Исходный файл сохранён.");
        Validate(doc.Profiles);
        return doc.Profiles;
    }

    public void Save(IEnumerable<PersonalProfile> profiles)
    {
        var list = profiles.ToList();
        Validate(list);
        fs.WriteAllText(path, JsonSerializer.Serialize(new Document(1, list), new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Validate(List<PersonalProfile> profiles)
    {
        if (profiles.Count > 50 || profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) ||
            string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 48 || p.Settings is null || p.Settings.Count > 200 ||
            p.Settings.Keys.Any(string.IsNullOrWhiteSpace)) || profiles.Select(p => p.Id).Distinct().Count() != profiles.Count)
            throw new InvalidDataException("Повреждён список личных профилей. Исходный файл сохранён.");
    }

    public static IReadOnlyList<PersonalProfile> Defaults() => new[]
    {
        new PersonalProfile("play", "Играю", new() { ["win.gamemode"] = true, ["win.gamedvr"] = true, ["win.powerplan"] = true }),
        new PersonalProfile("stream", "Стримлю", new() { ["win.gamemode"] = true, ["win.gamedvr"] = false, ["win.powerplan"] = false }),
        new PersonalProfile("quiet", "Тихий ноутбук", new() { ["win.gamemode"] = true, ["win.powerplan.ultimate"] = false, ["win.powerplan"] = false }),
    };
}
