using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Dota2Optimizer.App.ViewModels;
using Dota2Optimizer.Core.Benchmark;
namespace Dota2Optimizer.App.Views;
public partial class BenchmarkPage : UserControl
{
    private readonly List<double> _frameTimes = new();
    private readonly Stopwatch _clock = new();
    private ArenaScene? _scene;
    private Window? _window;
    private TimeSpan _lastRender;
    private double _lastSeconds, _hudAt;
    private bool _capturing;
    private ArenaRunSpec? _spec;
    private string _label = "before";
    private const int Warmup = 3, TestSeconds = 30;
    public BenchmarkPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_scene is null) { _scene = new ArenaScene(); SyntheticViewport.Camera = _scene.Camera; SyntheticViewport.Children.Add(new ModelVisual3D { Content = _scene.World }); }
            CompositionTarget.Rendering -= OnRendering; CompositionTarget.Rendering += OnRendering;
            _clock.Restart(); _lastRender = TimeSpan.Zero;
            _window = Window.GetWindow(this); if (_window is not null) _window.Deactivated += OnDeactivated;
            UpdateSpec();
        };
        Unloaded += (_, _) => { CompositionTarget.Rendering -= OnRendering; if (_window is not null) _window.Deactivated -= OnDeactivated; Finish(false, "Тест остановлен при переходе в другой раздел."); _clock.Stop(); };
        SyntheticViewport.SizeChanged += (_, _) => { if (_capturing) Finish(false, "Размер сцены изменился. Повторите тест в том же размере окна."); UpdateSpec(); };
    }
    private ArenaRunSpec Spec()
    {
        var dpi = VisualTreeHelper.GetDpi(SyntheticViewport);
        return new((int)Math.Round(SyntheticViewport.ActualWidth * dpi.DpiScaleX), (int)Math.Round(SyntheticViewport.ActualHeight * dpi.DpiScaleY), RenderCapability.Tier >> 16);
    }
    private void UpdateSpec()
    {
        if (!IsLoaded) return;
        var spec = Spec(); ArenaConditions.Text = spec.Description;
        if (spec.Valid) (DataContext as BenchmarkViewModel)?.SelectArena(spec);
    }
    private void OnDeactivated(object? sender, EventArgs e) { if (_capturing) Finish(false, "Окно потеряло фокус. Прерванный замер не сохранён."); }
    private void SyntheticStart_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not BenchmarkViewModel vm || vm.IsRunning) return;
        _spec = Spec(); if (!_spec.Valid) { SyntheticStatus.Text = "Сцена ещё не готова. Повторите запуск."; return; }
        _label = vm.IsAfter ? "after" : "before"; _frameTimes.Clear(); _clock.Restart(); _lastRender = TimeSpan.Zero; _lastSeconds = 0; _hudAt = 0; _capturing = true;
        SyntheticStart.IsEnabled = false; SyntheticStop.IsEnabled = true; ArenaLabels.IsEnabled = false; SyntheticProgress.Value = 0;
        SyntheticStatus.Text = "Прогрев · 3 секунды. Оставьте окно активным.";
        SyntheticLabel.Text = _label == "after" ? "После изменений" : "До изменений"; vm.SetSyntheticRunning(true);
    }
    private void SyntheticStop_Click(object sender, RoutedEventArgs e) => Finish(false, "Тест остановлен, результат не сохранён.");
    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args || args.RenderingTime == _lastRender || !IsVisible) return;
        _lastRender = args.RenderingTime;
        var seconds = _clock.Elapsed.TotalSeconds;
        if (_capturing && (_window?.WindowState == WindowState.Minimized || Spec() != _spec)) { Finish(false, "Условия отображения изменились. Замер отменён."); return; }
        if (!_capturing) return;
        var sample = (seconds - _lastSeconds) * 1000;
        if (_lastSeconds >= Warmup && sample > 0) _frameTimes.Add(sample);
        _lastSeconds = seconds;
        var phase = Math.Max(0, seconds - Warmup);
        _scene?.Update(phase); SyntheticProgress.Value = Math.Clamp(phase / TestSeconds, 0, 1);
        if (seconds >= _hudAt)
        {
            _hudAt = seconds + .25; ArenaChapter.Text = _scene?.Chapter(phase);
            ArenaFps.Text = seconds < Warmup ? "ПРОГРЕВ" : $"{(_frameTimes.Count > 0 ? 1000 / _frameTimes.TakeLast(30).Average() : 0):0} FPS";
            SyntheticStatus.Text = seconds < Warmup ? $"Прогрев · {Math.Ceiling(Warmup - seconds):0} с" : $"Замер · {Math.Min(TestSeconds, (int)phase)} / {TestSeconds} с";
        }
        if (seconds >= Warmup + TestSeconds) Finish(true);
    }
    private void Finish(bool save, string? message = null)
    {
        if (!_capturing) return;
        _capturing = false; SyntheticStart.IsEnabled = true; SyntheticStop.IsEnabled = false; ArenaLabels.IsEnabled = true;
        var vm = DataContext as BenchmarkViewModel; vm?.SetSyntheticRunning(false);
        if (save && _spec is not null)
        {
            try { vm?.SaveSyntheticRun(_frameTimes, _spec, _label); SyntheticStatus.Text = vm?.Status; SyntheticProgress.Value = 1; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SyntheticStatus.Text = "Не удалось сохранить замер: " + ex.Message; }
        }
        else { SyntheticStatus.Text = message ?? "Замер отменён."; SyntheticProgress.Value = 0; }
        ArenaFps.Text = "ТЕСТ ЗАВЕРШЁН";
    }
    private void ShareArena_Click(object sender, RoutedEventArgs e) { if (DataContext is BenchmarkViewModel vm) vm.ShowShareCard(true); }
    private void ShareGame_Click(object sender, RoutedEventArgs e) { if (DataContext is BenchmarkViewModel vm) vm.ShowShareCard(false); }
}
