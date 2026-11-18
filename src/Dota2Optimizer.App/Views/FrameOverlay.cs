using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Dota2Optimizer.Core.Diagnostics;

namespace Dota2Optimizer.App.Views;

internal sealed class FrameOverlay : Window
{
    public static readonly string[] Metrics = ["FPS", "Средний FPS", "1% low", "Кадр", "CPU", "GPU", "RAM", "CPU °C", "GPU °C", "VRAM", "График"];
    private readonly TextBlock _display = new() { Foreground = Brushes.White, FontSize = 15 };
    private readonly Canvas _graph = new() { Width=240, Height=50 };
    private readonly OverlayOptions _options;
    private readonly HashSet<string> _hidden;
    private HwndSource? _source;
    private bool _registered;
    public bool HotkeyReady => _registered;
    private const int HotkeyId = 0x4261;

    public FrameOverlay(OverlayOptions options)
    {
        _options = options;
        _hidden = options.Hidden.Split('|').ToHashSet();
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = options.Opacity / 100d;
        var stack = new StackPanel { Margin = new Thickness(12), LayoutTransform = new ScaleTransform(options.Scale/100d,options.Scale/100d) };
        stack.Children.Add(_display);
        stack.Children.Add(_graph);
        _display.Text = "FrameLab · ожидание датчиков";
        Content = new Border { Background = new SolidColorBrush(Color.FromRgb(23,24,26)), Child = stack };
        SourceInitialized += (_,_) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(handle,-20, GetWindowLongPtr(handle,-20) | 0x08000020);
            _source = HwndSource.FromHwnd(handle);
            _source.AddHook(Hotkey);
            _registered = RegisterHotKey(handle,HotkeyId,0x4003,(uint)(0x70+options.Key-1));
        };
        SizeChanged += (_,_) =>
        {
            var area = SystemParameters.WorkArea;
            Left = options.Corner % 2 == 0 ? area.Left+12 : area.Right-ActualWidth-12;
            Top = options.Corner < 2 ? area.Top+12 : area.Bottom-ActualHeight-12;
        };
        Closed += (_,_) =>
        {
            if (_registered) UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
            _source?.RemoveHook(Hotkey);
        };
    }

    private nint Hotkey(nint hwnd, int msg, nint key, nint lParam, ref bool handled)
    {
        if (msg == 0x0312 && key == HotkeyId)
        {
            if (IsVisible) Hide(); else Show();
            handled = true;
        }
        return 0;
    }

    public void Render(DiagnosticSample sample)
    {
        if (!IsVisible) return;
        double? last = sample.Times.LastOrDefault();
        if (last == 0) last = null;
        var values = new double?[] {1000 / last, sample.Frames?.AvgFps, sample.Frames?.OnePercentLowFps, last,
            sample.Load.CpuPercent, sample.Load.GpuPercent, sample.Load.RamPercent,
            sample.Sensors.CpuTemp, sample.Sensors.GpuTemp, sample.Sensors.VramGb};
        int[] visible = _options.Mode switch {0 => [0,2,4,5], 1=>[0,3,4,5,6,7,8], _=>[0,1,2,3,4,5,6,7,8,9]};
        string[] units = ["","",""," мс","%","%","%","",""," ГБ"];
        _display.Text = sample.FrameSource + "\n" + string.Join("\n",visible.Where(i=>!_hidden.Contains(Metrics[i]))
            .Select(i=>Metrics[i]+"    "+(values[i] is double v ? $"{v:0.#}{units[i]}" : "н/д")));
        _graph.Visibility = _options.Mode == 2 && !_hidden.Contains("График") ? Visibility.Visible : Visibility.Collapsed;
        if (_graph.IsVisible) Plot(_graph, sample.Times.Select(ms=>(double?)ms).ToArray());
    }

    internal static void Plot(Canvas canvas, IReadOnlyList<double?> values)
    {
        canvas.Children.Clear();
        double max = Math.Max(1,values.Max() ?? 0);
        double width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 240;
        Polyline? line = null;
        for(int i=0;i<values.Count;i++)
        {
            if(values[i] is not double v) { line=null; continue; }
            if(line == null) { line=new Polyline { Stroke=Brushes.OrangeRed,StrokeThickness=2 };canvas.Children.Add(line); }
            line.Points.Add(new Point(i*(width-4)/Math.Max(1,values.Count-1)+2,canvas.Height-2-v/max*(canvas.Height-4)));
        }
    }

    [DllImport("user32",EntryPoint="GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32",EntryPoint="SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd,int index,nint value);
    [DllImport("user32")] private static extern bool RegisterHotKey(nint hwnd,int id,uint mods,uint key);
    [DllImport("user32")] private static extern bool UnregisterHotKey(nint hwnd,int id);
}
