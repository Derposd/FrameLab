using System.Windows;
using System.Windows.Media;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>Everything shown for one game: its wizard, issues, match monitor and crash pages.</summary>
public sealed class GameWorkspace
{
    public GameWorkspace(AppServices services, GameDefinition game, Action<int> navigate)
    {
        Game = game;
        Wizard = new WizardViewModel(services, game);
        Issues = new KnownIssuesViewModel(services, game, navigate);
        Monitor = new MatchMonitorViewModel(services, game);
        Crash = new CrashViewModel(services, game);
    }

    public GameDefinition Game { get; }
    public string Name => Game.Name;
    public string ShortName => Game.ShortName;

    /// <summary>Header of this game's group in the side navigation.</summary>
    public string SectionTitle => Game.Name.ToUpperInvariant();

    /// <summary>Two- or three-letter mark on the game tile.</summary>
    public string Monogram => Game.Id == GameId.Cs2 ? "CS2" : "D2";

    public string Genre => Game.Id == GameId.Cs2 ? "Тактический шутер" : "MOBA";

    public WizardViewModel Wizard { get; }
    public KnownIssuesViewModel Issues { get; }
    public MatchMonitorViewModel Monitor { get; }
    public CrashViewModel Crash { get; }
}

/// <summary>The app keeps one vermilion accent across every game profile, as in the FrameLab specification layout.</summary>
public static class GameTheme
{
    public static void Apply(GameId game) => ApplyBrand();

    /// <summary>Windows profile uses the same brand accent as the game profiles.</summary>
    public static void ApplyPc() => ApplyBrand();

    public static void ApplyGta() => ApplyBrand();

    private static void ApplyBrand() => Apply(Color.FromRgb(0xF2, 0x38, 0x0B), Color.FromRgb(0xD9, 0x32, 0x0A), Color.FromRgb(0xB9, 0x2B, 0x0A), Color.FromRgb(0xF2, 0xEF, 0xE7));

    private static void Apply(Color accent, Color hover, Color pressed, Color onAccent)
    {
        var r = Application.Current.Resources;
        r["AccentColor"] = accent;
        r["Accent"] = Frozen(new SolidColorBrush(accent));
        r["AccentHover"] = Frozen(new SolidColorBrush(hover));
        r["AccentPressed"] = Frozen(new SolidColorBrush(pressed));
        r["OnAccent"] = Frozen(new SolidColorBrush(onAccent));
        r["AccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)));
        r["AccentSoftBorder"] = Frozen(new SolidColorBrush(Color.FromArgb(0x55, accent.R, accent.G, accent.B)));
        var accentBrush = Frozen(new SolidColorBrush(accent));
        r["AccentGradient"] = accentBrush;
        r["BarGradient"] = accentBrush;
        r["HeroGradient"] = Brushes.Transparent;
        r["HeroBorder"] = Frozen(new SolidColorBrush(Color.FromRgb(0x17, 0x16, 0x14)));
        r["Glow.Button"] = null;
        r["Glow"] = Brushes.Transparent;
    }

    private static T Frozen<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }
}

/// <summary>Remembers the last opened Steam game (Dota 2 or CS2) between launches. A plain text file in the app's data folder.</summary>
internal static class UiState
{
    private static string File => Path.Combine(AppPaths.Root, "last-game.txt");

    public static GameId LastGame()
    {
        try
        {
            return System.IO.File.Exists(File) && Enum.TryParse<GameId>(System.IO.File.ReadAllText(File).Trim(), out var g) ? g : GameId.Dota2;
        }
        catch (IOException)
        {
            return GameId.Dota2;
        }
    }

    public static void SaveLastGame(GameId game) => Save(game.ToString());

    private static void Save(string section)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            System.IO.File.WriteAllText(File, section);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only a convenience.
        }
    }
}
