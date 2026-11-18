using System.Text.Json;
namespace Dota2Optimizer.Core.Diagnostics;

public sealed record OverlayOptions(int Mode = 1, int Corner = 1, int Scale = 100, int Opacity = 85,
    int Interval = 1000, int Window = 120, int Key = 9, string Hidden = "")
{
    public OverlayOptions Validate() => this with {
        Mode = Math.Clamp(Mode,0,2), Corner = Math.Clamp(Corner,0,3), Scale = Math.Clamp(Scale,75,150),
        Opacity = Math.Clamp(Opacity,35,100), Interval = Math.Clamp(Interval,500,3000),
        Window = Math.Clamp(Window,30,480), Key = Math.Clamp(Key,8,11), Hidden = Hidden ?? "" };
    public static OverlayOptions Load(string path) => File.Exists(path)
        ? (JsonSerializer.Deserialize<OverlayOptions>(File.ReadAllText(path)) ?? new()).Validate() : new();
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + ".tmp";
        File.WriteAllText(pending, JsonSerializer.Serialize(Validate()));
        File.Move(pending,path,true);
    }
}
