using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Diagnostics;
using Dota2Optimizer.App.ViewModels;

namespace Dota2Optimizer.App.Views;

public partial class BenchmarkPage : UserControl
{
    private readonly List<double> _frameTimes = new();
    private readonly Stopwatch _timer = new();
    private readonly AxisAngleRotation3D _rotation = new(new Vector3D(0, 1, 0), 0);
    private TimeSpan _lastRender;
    private bool _capturing;
    private const int TestSeconds = 30;

    public BenchmarkPage()
    {
        InitializeComponent();
        BuildScene();
        Loaded += (_, _) => CompositionTarget.Rendering += OnRendering;
        Unloaded += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            FinishSynthetic(false);
        };
    }

    private void BuildScene()
    {
        SyntheticViewport.Camera = new PerspectiveCamera(new Point3D(0, 4.5, 13), new Vector3D(0, -1.2, -10), new Vector3D(0, 1, 0), 54);
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(92, 96, 112)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(245, 230, 205), new Vector3D(-1, -2, -3)));
        scene.Children.Add(new DirectionalLight(Color.FromRgb(80, 130, 255), new Vector3D(2, -1, 1)));

        var objects = new Model3DGroup();
        var random = new Random(481516);
        var palette = new[] { Color.FromRgb(238, 67, 51), Color.FromRgb(69, 115, 228), Color.FromRgb(238, 163, 57), Color.FromRgb(74, 187, 156), Color.FromRgb(155, 111, 224) };
        for (var i = 0; i < 72; i++)
        {
            var x = (random.NextDouble() - .5) * 18;
            var z = (random.NextDouble() - .5) * 24;
            var h = .35 + random.NextDouble() * 2.1;
            var cube = Box(.35 + random.NextDouble() * .55, h, .35 + random.NextDouble() * .55, palette[i % palette.Length]);
            cube.Transform = new TranslateTransform3D(x, h / 2, z);
            objects.Children.Add(cube);
        }

        var floor = Box(24, .08, 30, Color.FromRgb(34, 38, 50));
        floor.Transform = new TranslateTransform3D(0, -.08, 0);
        objects.Children.Add(floor);
        objects.Transform = new RotateTransform3D(_rotation);
        scene.Children.Add(objects);
        SyntheticViewport.Children.Add(new ModelVisual3D { Content = scene });
    }

    private static GeometryModel3D Box(double width, double height, double depth, Color color)
    {
        var x = width / 2; var y = height / 2; var z = depth / 2;
        var p = new[] { new Point3D(-x,-y,-z), new Point3D(x,-y,-z), new Point3D(x,y,-z), new Point3D(-x,y,-z), new Point3D(-x,-y,z), new Point3D(x,-y,z), new Point3D(x,y,z), new Point3D(-x,y,z) };
        var mesh = new MeshGeometry3D();
        foreach (var i in new[] { 0,1,2, 0,2,3, 4,6,5, 4,7,6, 0,4,5, 0,5,1, 3,2,6, 3,6,7, 1,5,6, 1,6,2, 0,3,7, 0,7,4 }) mesh.Positions.Add(p[i]);
        var brush = new SolidColorBrush(color); brush.Freeze();
        return new GeometryModel3D(mesh, new DiffuseMaterial(brush)) { BackMaterial = new DiffuseMaterial(brush) };
    }

    private void SyntheticStart_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _frameTimes.Clear(); _timer.Restart(); _lastRender = TimeSpan.Zero; _capturing = true;
        SyntheticStart.IsEnabled = false; SyntheticStop.IsEnabled = true; SyntheticProgress.Value = 0;
        SyntheticStatus.Text = "Идёт тест сцены. Не переключайте окно до завершения.";
        SyntheticLabel.Text = (DataContext as BenchmarkViewModel)?.IsAfter == true ? "После изменений" : "До изменений";
        (DataContext as BenchmarkViewModel)?.SetSyntheticRunning(true);
    }

    private void SyntheticStop_Click(object sender, System.Windows.RoutedEventArgs e) => FinishSynthetic(false);

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs args) return;
        _rotation.Angle = args.RenderingTime.TotalSeconds * 18;
        if (!_capturing) return;
        var now = args.RenderingTime;
        if (_lastRender != TimeSpan.Zero)
        {
            var ms = (now - _lastRender).TotalMilliseconds;
            if (ms is > 0 and < 5000 && _timer.Elapsed.TotalSeconds >= 2) _frameTimes.Add(ms);
        }
        _lastRender = now;
        SyntheticProgress.Value = Math.Min(1, _timer.Elapsed.TotalSeconds / TestSeconds);
        if (_timer.Elapsed.TotalSeconds >= TestSeconds) FinishSynthetic(true);
    }

    private void FinishSynthetic(bool save)
    {
        if (!_capturing) return;
        _capturing = false; _timer.Stop();
        SyntheticStart.IsEnabled = true; SyntheticStop.IsEnabled = false;
        (DataContext as BenchmarkViewModel)?.SetSyntheticRunning(false);
        if (save)
        {
            (DataContext as BenchmarkViewModel)?.SaveSyntheticRun(_frameTimes, TestSeconds);
            SyntheticProgress.Value = 1;
            SyntheticStatus.Text = $"Готово · {_frameTimes.Count:N0} кадров измерено · результат относится только к тестовой сцене.";
        }
        else
        {
            SyntheticProgress.Value = 0;
            SyntheticStatus.Text = "Тест остановлен, результат не сохранён.";
        }
    }
}
