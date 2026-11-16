using System.Collections.ObjectModel;
using System.Windows.Input;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Network;
using Dota2Optimizer.Core.Session;

namespace Dota2Optimizer.App.ViewModels;

/// <summary>A game that can be measured now (it is running).</summary>
public sealed record BenchGame(string Process, string Name)
{
    public string Exe => Process + ".exe";
    public override string ToString() => Name;
}

public sealed record BenchDeltaVm(BenchDelta Delta)
{
    public string Metric => Delta.Metric;
    public double Before => Delta.Before;
    public double After => Delta.After;
    public string Change => Delta.Noise ? "≈ погрешность" : $"{(Delta.Percent > 0 ? "+" : "−")}{Math.Abs(Delta.Percent):0}%";
    public bool Better => Delta.Better;
    public bool Worse => !Delta.Noise && !Delta.Better;
    public double BeforeBar => Math.Max(Before, After) <= 0 ? 0 : Before / Math.Max(Before, After);
    public double AfterBar => Math.Max(Before, After) <= 0 ? 0 : After / Math.Max(Before, After);
}

/// <summary>«Бенчмарк»: FPS and frame time through PresentMon plus ping, saved as «до» and «после» and compared.</summary>
public sealed class BenchmarkViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly BenchmarkStore _store;
    private BenchGame? _game;
    private int _seconds = 60;
    private bool _isAfter;
    private bool _running;
    private bool _syntheticRunning;
    private double _progress;
    private string _status = "Запустите игру, зайдите в одну и ту же сцену (тренировка, реплей, одно место на карте) и нажмите «Начать замер».";
    private BenchResult? _before;
    private BenchResult? _after;
    private BenchResult? _syntheticBefore;
    private BenchResult? _syntheticAfter;
    private string? _verdict;
    private CancellationTokenSource? _cts;
    private ArenaRunSpec? _arena;
    public string ArenaVerdict => ShareComparison.CanCompare(SyntheticBefore, SyntheticAfter)
        ? BenchCompare.Verdict(BenchCompare.Deltas(SyntheticBefore!, SyntheticAfter!))
        : "Для сравнения запишите «до» и «после» при одинаковом размере сцены.";

    public void SelectArena(ArenaRunSpec spec)
    {
        _arena = spec;
        (SyntheticBefore, SyntheticAfter) = _store.Pair(spec.Key);
        OnPropertyChanged(nameof(ArenaVerdict));
    }

    public void ShowShareCard(bool synthetic)
    {
        if (IsRunning || IsSyntheticRunning) { Ui.Info("Сначала завершите замер."); return; }
        var before = synthetic ? SyntheticBefore : Before;
        var after = synthetic ? SyntheticAfter : After;
        if (!ShareComparison.CanCompare(before, after)) { Ui.Info("Нужны два завершённых замера «до» и «после» одной сцены и одинаковой длительности."); return; }
        new Views.ResultCardDialog(before!, after!, synthetic ? "FrameLab Arena" : SelectedGame?.Name ?? "Игра").ShowDialog();
    }

    private string? Hardware => _s.Snapshot is { } snapshot ? $"{snapshot.Cpu?.Name ?? "CPU не определён"} · {snapshot.PrimaryGpu?.Name ?? "GPU не определён"}" : null;

    public BenchmarkViewModel(AppServices services)
    {
        _s = services;
        _store = new BenchmarkStore(services.Fs, AppPaths.Benchmarks);
        RefreshGamesCommand = new RelayCommand(RefreshGames);
        StartCommand = new AsyncCommand(RunAsync, () => !IsRunning && !IsSyntheticRunning && SelectedGame is not null);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsRunning);
    }

    public ObservableCollection<BenchGame> Games { get; } = new();
    public ObservableCollection<BenchDeltaVm> Deltas { get; } = new();
    public ICommand RefreshGamesCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }

    public BenchGame? SelectedGame
    {
        get => _game;
        set
        {
            if (Set(ref _game, value))
            {
                LoadPair();
            }
        }
    }

    public bool Is30
    {
        get => _seconds == 30;
        set => SetSeconds(value, 30);
    }

    public bool Is60
    {
        get => _seconds == 60;
        set => SetSeconds(value, 60);
    }

    public bool Is120
    {
        get => _seconds == 120;
        set => SetSeconds(value, 120);
    }

    public bool IsBefore
    {
        get => !_isAfter;
        set
        {
            if (value)
            {
                _isAfter = false;
                OnPropertyChanged(nameof(IsBefore));
                OnPropertyChanged(nameof(IsAfter));
            }
        }
    }

    public bool IsAfter
    {
        get => _isAfter;
        set
        {
            if (value)
            {
                _isAfter = true;
                OnPropertyChanged(nameof(IsAfter));
                OnPropertyChanged(nameof(IsBefore));
            }
        }
    }

    public bool IsRunning
    {
        get => _running;
        private set
        {
            Set(ref _running, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsSyntheticRunning
    {
        get => _syntheticRunning;
        private set => Set(ref _syntheticRunning, value);
    }

    /// <summary>Stores results from FrameLab's fixed synthetic 3D scene, separate from game captures.</summary>
    public void SaveSyntheticRun(IReadOnlyList<double> frameTimes, ArenaRunSpec spec, string label)
    {
        var stats = FrameStats.FromFrameTimes(frameTimes);
        if (stats is null)
        {
            Status = "Сцена не успела отрисовать достаточно кадров. Повторите тест после прогрева.";
            return;
        }

        var result = BenchResult.From(spec.Key, label, stats, frameTimes, null, null) with
        {
            Conditions = spec.Description,
            Hardware = Hardware,
        };
        _store.Add(result);
        SelectArena(spec);
        Status = $"Arena: {stats.AvgFps:0} FPS, 1% low {stats.OnePercentLowFps:0}. Это результат встроенного теста, не FPS в конкретной игре.";
        _s.History.Add(new HistoryEntry(DateTime.Now, "Синтетический бенчмарк", label == "before" ? "До изменений" : "После изменений", Status));
    }

    public void SetSyntheticRunning(bool running)
    {
        IsSyntheticRunning = running;
        CommandManager.InvalidateRequerySuggested();
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public BenchResult? Before
    {
        get => _before;
        private set
        {
            Set(ref _before, value);
            OnPropertyChanged(nameof(HasBefore));
        }
    }

    public BenchResult? After
    {
        get => _after;
        private set
        {
            Set(ref _after, value);
            OnPropertyChanged(nameof(HasAfter));
        }
    }

    public bool HasBefore => Before is not null;
    public bool HasAfter => After is not null;
    public BenchResult? SyntheticBefore { get => _syntheticBefore; private set { Set(ref _syntheticBefore, value); OnPropertyChanged(nameof(HasSyntheticBefore)); } }
    public BenchResult? SyntheticAfter { get => _syntheticAfter; private set { Set(ref _syntheticAfter, value); OnPropertyChanged(nameof(HasSyntheticAfter)); } }
    public bool HasSyntheticBefore => SyntheticBefore is not null;
    public bool HasSyntheticAfter => SyntheticAfter is not null;

    public string? Verdict
    {
        get => _verdict;
        private set => Set(ref _verdict, value);
    }

    public Task ActivateAsync()
    {
        RefreshGames();
        if (_arena is not null) SelectArena(_arena);
        return Task.CompletedTask;
    }

    private void SetSeconds(bool on, int s)
    {
        if (!on)
        {
            return;
        }

        _seconds = s;
        OnPropertyChanged(nameof(Is30));
        OnPropertyChanged(nameof(Is60));
        OnPropertyChanged(nameof(Is120));
    }

    private void RefreshGames()
    {
        var running = new ProcessControl().RunningNames();
        var config = GameModeConfigStore.Load(_s.Fs, AppPaths.GameModeConfig);
        var known = config.Games.Concat(GameModePresets.KnownGames).ToList();
        var set = new HashSet<string>(running, StringComparer.OrdinalIgnoreCase);
        var keep = SelectedGame?.Process;
        Games.Clear();
        foreach (var g in known.Where(g => set.Contains(g.Process)).DistinctBy(g => g.Process, StringComparer.OrdinalIgnoreCase))
        {
            Games.Add(new BenchGame(g.Process, g.Name));
        }

        SelectedGame = Games.FirstOrDefault(g => g.Process == keep) ?? Games.FirstOrDefault();
        if (Games.Count == 0)
        {
            Status = "Запущенных игр из списка не найдено. Запустите игру (или добавьте её exe на странице «Игровой режим») и нажмите «Обновить».";
        }
    }

    private void LoadPair()
    {
        if (SelectedGame is null)
        {
            return;
        }

        var (before, after) = _store.Pair(SelectedGame.Process);
        Before = before;
        After = after;
        Compare();
    }

    private void Compare()
    {
        Deltas.Clear();
        Verdict = null;
        if (Before is null || After is null)
        {
            return;
        }

        var deltas = BenchCompare.Deltas(Before, After);
        foreach (var d in deltas)
        {
            Deltas.Add(new BenchDeltaVm(d));
        }

        Verdict = BenchCompare.Verdict(deltas);
    }

    private async Task RunAsync()
    {
        var game = SelectedGame!;
        var presentMon = PresentMonTool.Find();
        if (presentMon is null)
        {
            Status = "PresentMon не найден рядом с программой — запись FPS недоступна в этой сборке.";
            return;
        }

        IsRunning = true;
        Progress = 0;
        _cts = new CancellationTokenSource();
        var label = _isAfter ? "after" : "before";
        try
        {
            Status = "Запрашиваю права администратора для записи кадров (Windows спросит один раз)…";
            var capture = await Task.Run(() => FpsCapture.Start(presentMon, game.Exe, _seconds, "bench", "FrameLabBench"));
            if (capture.Declined || capture.StartError is not null)
            {
                Status = "Замер не начат: " + capture.Problem(0);
                return;
            }

            Status = $"Идёт замер {game.Name}: не переключайтесь из игры {_seconds} с.";
            var ping = NetworkDiagnostics.PingAsync("1.1.1.1", _seconds * 2, _cts.Token, timeoutMs: 1000, intervalMs: 500);
            var started = DateTime.Now;
            while ((DateTime.Now - started).TotalSeconds < _seconds + 15 && !(capture.HasExited && (DateTime.Now - started).TotalSeconds > 3))
            {
                Progress = Math.Min(1, (DateTime.Now - started).TotalSeconds / _seconds);
                await Task.Delay(500, _cts.Token);
            }

            Progress = 1;
            PingStats? pingStats = null;
            try
            {
                pingStats = await ping;
            }
            catch (Exception ex) when (ex is OperationCanceledException or System.Net.NetworkInformation.PingException)
            {
            }

            var csv = capture.ReadCsv() ?? string.Empty;
            var frames = PresentMonCsv.ReadFrameTimes(csv, game.Exe);
            var stats = FrameStats.FromFrameTimes(frames);
            if (stats is null)
            {
                Status = "Не удалось записать кадры: " + (capture.Problem(frames.Count) ?? "слишком мало кадров");
                return;
            }

            var result = BenchResult.From(game.Process, label, stats, frames, pingStats?.AvgMs, pingStats?.JitterMs) with { Hardware = Hardware, Conditions = "PresentMon · " + game.Name };
            _store.Add(result);
            LoadPair();
            if (label == "before")
            {
                After = null;
                Deltas.Clear();
                Verdict = null;
            }

            Status = $"Готово: {stats.AvgFps:0} FPS в среднем, 1% low {stats.OnePercentLowFps:0}. " +
                     (label == "before" ? "Теперь примените изменения, выберите «После» и повторите замер в той же сцене." : "Сравнение ниже.");
            _s.History.Add(new HistoryEntry(DateTime.Now, "Бенчмарк", $"{game.Name} ({(label == "before" ? "до" : "после")})", Status));
        }
        catch (OperationCanceledException)
        {
            Status = "Замер остановлен.";
        }
        finally
        {
            IsRunning = false;
        }
    }
}
