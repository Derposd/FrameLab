using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Dota2Optimizer.App.ViewModels;
using Dota2Optimizer.Core.Benchmark;
using Dota2Optimizer.Core.Crash;
using Dota2Optimizer.Core.Diagnostics;
using Dota2Optimizer.Core.Game;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Network;
using Dota2Optimizer.Core.SystemInfo;
using static Dota2Optimizer.App.Views.PersonalProfilesPage;

namespace Dota2Optimizer.App.Views;

public sealed class DiagnosticsPage : UserControl
{
    private enum Tool { Game, Overlay, Network, Input, Hardware }
    private static readonly string[] Titles = ["Game Doctor","FrameLab Overlay","Network Doctor","Задержка ввода","Мой компьютер"];
    private static readonly string[] Symptoms = ["Низкий FPS","Фризы и статтеры","Высокий пинг","Вылеты игры","Долгая загрузка","Высокие температуры","Задержка ввода"];
    private readonly StackPanel _settings = new(), _results = new();
    private readonly WrapPanel _actions = new();
    private readonly ComboBox _tool = Choices(Titles), _game = Choices(["Dota 2","Counter-Strike 2","GTA V"]), _symptom = Choices(Symptoms);
    private readonly DiagnosticFeed _feed = new(() => new WindowsDiagnosticSource());
    private readonly TextBlock _status = Heading("Ожидание проверки","PageSub");
    private CancellationTokenSource? _operation, _monitor;
    private FrameOverlay? _overlay;
    private OverlayOptions _options = new();
    private string? _export;
    private readonly CheckBox _valve = new() { Content = "Включить проверку опубликованного релея Valve" };
    private MainViewModel Vm => (MainViewModel)DataContext;
    private Tool CurrentTool => (Tool)_tool.SelectedIndex;
    private string Exe => _game.SelectedIndex switch {0=>"dota2.exe",1=>"cs2.exe",_=>"GTA5.exe"};
    private static string OptionsPath => Path.Combine(AppPaths.Root,"overlay.json");

    public DiagnosticsPage()
    {
        var body = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(Heading("Диагностика","PageTitle"));
        body.Children.Add(_tool);
        body.Children.Add(_settings);
        body.Children.Add(_actions);
        body.Children.Add(_status);
        body.Children.Add(_results);
        var theme = new CheckBox { Content="Тёмная тема диагностики", Margin=new Thickness(0,16,0,6) };
        body.Children.Add(theme);
        _dark=File.Exists(Path.Combine(AppPaths.Root,"diagnostics-dark"));
        theme.IsChecked=_dark;
        theme.Click+=(_,_)=>{
            _dark=theme.IsChecked==true;
            ApplyTheme();
            try
            {
                var path=Path.Combine(AppPaths.Root,"diagnostics-dark");
                Directory.CreateDirectory(AppPaths.Root);
                if(_dark) File.WriteAllText(path,"1"); else File.Delete(path);
            }
            catch(Exception e){ _status.Text="Не удалось сохранить тему. "+e.Message; }
        };
        try { _options = OverlayOptions.Load(OptionsPath); } catch (Exception e) { _status.Text = "Настройки оверлея недоступны. "+e.Message; }
        string? loadWarning=_status.Text.StartsWith("Настройки") ? _status.Text : null;
        _tool.SelectionChanged += (_,_)=>Configure();
        _symptom.SelectionChanged += (_,_)=>Configure();
        _game.SelectionChanged += (_,_)=>Configure();
        Unloaded += (_,_) => _operation?.Cancel();
        Configure();
        if(loadWarning!=null)_status.Text=loadWarning;
    }

    private static ComboBox Choices(IEnumerable<string> items,int index=0) =>
        new() { ItemsSource=items.ToArray(),SelectedIndex=index, MinWidth=160, Margin=new Thickness(0,4,12,10),HorizontalAlignment=HorizontalAlignment.Left };
    private void Configure()
    {
        _settings.Children.Clear(); _actions.Children.Clear(); _results.Children.Clear(); _export=null;
        _status.Text="Ожидание проверки";
        if (CurrentTool is Tool.Game or Tool.Input or Tool.Overlay)
        {
            _settings.Children.Add(Heading("Игра","Caption")); _settings.Children.Add(_game);
        }
        if(CurrentTool == Tool.Game) _settings.Children.Add(_symptom);
        if(CurrentTool == Tool.Overlay) { ConfigureOverlay(); ApplyTheme(); return; }
        bool network = CurrentTool == Tool.Network || CurrentTool == Tool.Game && (Symptom)_symptom.SelectedIndex == Symptom.Network;
        if(network)
        {
            _settings.Children.Add(Heading("16 ICMP-проб до роутера и 1.1.1.1, запрос к DNS адаптера. Задержку игры сверяйте в её клиенте. Для bufferbloat нужен контролируемый тест под нагрузкой.","PageSub"));
            _settings.Children.Add(_valve);
        }
        else _settings.Children.Add(Heading(CurrentTool == Tool.Input ? "Проверка времени кадра, дисплея и настроек игры. Для физического измерения нажатия до изображения нужен внешний датчик." :
            "Запустите проверку во время проблемы. Для данных о кадрах включите запись в разделе Overlay или бенчмарке.","PageSub"));
        Command(network?"Начать сетевые пробы":"Проверить",()=>Run(network?NetworkReport:LocalReport));
        Command("Отменить",()=>_operation?.Cancel());
        ApplyTheme();
    }

    private bool _dark;
    private void ApplyTheme()
    {
        Background=_dark ? new SolidColorBrush(Color.FromRgb(30,31,32)) : Brushes.Transparent;
        void Paint(DependencyObject node)
        {
            if(node is TextBlock text)
            {
                if(_dark)text.Foreground=Brushes.WhiteSmoke;
                else text.ClearValue(TextBlock.ForegroundProperty);
            }
            if(node is CheckBox box)
            {
                if(_dark)box.Foreground=Brushes.WhiteSmoke;
                else box.ClearValue(ForegroundProperty);
            }
            foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())Paint(child);
        }
        Paint(this);
    }

    private void Command(string label,Action action) => _actions.Children.Add(Action(label,action));
    private void Add(string title,string detail)
    {
        _results.Children.Add(Heading(title,"H1"));
        _results.Children.Add(Heading(detail,"PageSub"));
    }

    private async void Run(Func<CancellationToken,Task> work)
    {
        if(_operation != null) return;
        using var cts=new CancellationTokenSource();
        _operation=cts;
        _settings.IsEnabled=_tool.IsEnabled=false;
        _status.Text="Сбор данных…";
        _results.Children.Clear(); _export=null;
        try { await work(cts.Token); cts.Token.ThrowIfCancellationRequested(); _status.Text="Проверка завершена"; }
        catch(OperationCanceledException) { _results.Children.Clear(); _status.Text="Проверка отменена"; }
        catch(Exception e) { _status.Text="Проверка недоступна. "+e.Message; }
        finally { _operation=null; _settings.IsEnabled=_tool.IsEnabled=true; ApplyTheme(); }
    }

    private async Task NetworkReport(CancellationToken token)
    {
        var valve = _valve.IsChecked==true;
        var commands=Vm.Services.Commands;
        var report=await Task.Run(()=>DoctorNetwork.MeasureAsync(commands,valve,token),token);
        token.ThrowIfCancellationRequested();
        Add(report.Conclusion,report.Adapter+"\n"+report.Dns);
        foreach(var series in report.Series)
        {
            var s=series.Target.Stats;
            Add(series.Target.Name,$"Ответы {s.Received}/{s.Sent} · потери {s.LossPercent:0.#}%\nМин / сред / макс   {V(s.MinMs)} / {V(s.AvgMs)} / {V(s.MaxMs)} мс · jitter {V(s.JitterMs)} мс");
            var chart=new Canvas {Height=55, Width=260, HorizontalAlignment=HorizontalAlignment.Left,ToolTip="Время ответа в мс. Разрывы означают пропущенные ответы."};
            _results.Children.Add(chart);
            FrameOverlay.Plot(chart,series.Samples.Select(x=>(double?)x).ToArray());
        }
    }

    private static string V(double? v)=>DoctorRules.Value(v);
    private async Task<DiagnosticSample> ReadSample(CancellationToken token)
    {
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(token);
        DiagnosticSample? result=null;
        int count=0;
        try
        {
            await _feed.RunAsync(1000,120,s=>
            {
                result=s;
                if(++count==2) stop.Cancel();
            },stop.Token);
        }
        catch(OperationCanceledException) when(!token.IsCancellationRequested && result!=null) {}
        token.ThrowIfCancellationRequested();
        return result ?? throw new InvalidOperationException("Нет образцов телеметрии.");
    }

    private async Task LocalReport(CancellationToken token)
    {
        var services=Vm.Services; var exe=Exe;
        var game = _game.SelectedIndex==2 ? null : Games.Get(_game.SelectedIndex==0?GameId.Dota2:GameId.Cs2);
        var symptom=(Symptom)_symptom.SelectedIndex;
        var pc=await services.EnsureSnapshotAsync();
        token.ThrowIfCancellationRequested();
        var sample=await ReadSample(token);
        var live=sample.Load; var sensors=sample.Sensors;
        if(CurrentTool != Tool.Hardware && sample.FrameSource!=exe)
            sample=sample with { Frames=null, Times=[], FrameSource="Нет активной записи "+exe };
        var crash=CurrentTool==Tool.Game && symptom==Symptom.Crash;
        var evidence=await Task.Run(()=>new DoctorEvidence(pc,sample,game==null?null:InputLagAdvisor.ReadVSync(services.Fs,pc.For(game.Id)),
            crash?EventLogSource.Read(7).Count(ev=>ev.Log=="Application" && (ev.Message.Contains(exe,StringComparison.OrdinalIgnoreCase) || ev.Properties.Any(p=>p.Contains(exe,StringComparison.OrdinalIgnoreCase)))):null,BackgroundApps.FindRunning().Count),token);
        token.ThrowIfCancellationRequested();
        string[] facts=[
            $"CPU   {pc.Cpu?.Name ?? "н/д"} · {V(sensors.CpuMhz)} МГц · {V(live.CpuPercent)}%",
            $"GPU   {pc.PrimaryGpu?.Name ?? "н/д"} · 3D {V(live.GpuPercent)}% · драйвер {pc.PrimaryGpu?.DriverVersion ?? "н/д"}",
            $"VRAM   {V(sensors.VramGb)} ГБ занято на наиболее загруженном адаптере",
            $"ОЗУ   {V(live.RamPercent)}% · {V(pc.Memory?.TotalGb)} ГБ · модули {string.Join(", ",pc.Memory?.Modules.Select(m=>$"{m.SpeedMhz} МГц") ?? [])}",
            $"Накопители   {(pc.Disks.Count==0?"н/д":string.Join(", ",pc.Disks.Select(d=>d.Model+" "+d.Kind)))} · активность {V(sensors.DiskBusy)}%",
            pc.Display is {} display ? $"Экран   {display.Width} × {display.Height} · {display.RefreshHz} Гц" : "Экран   н/д",
            $"Питание   {pc.PowerPlan?.ActiveName ?? "н/д"}",
            $"Температура   CPU {V(sensors.CpuTemp)} °C · GPU {V(sensors.GpuTemp)} °C. Источник — локальный WMI LibreHardwareMonitor.",
            $"Кадры   {sample.FrameSource} · медиана {V(sample.Frames?.MedianFrameMs)} мс · 1% low {V(sample.Frames?.OnePercentLowFps)} FPS"
        ];
        if(CurrentTool==Tool.Hardware)
            Add("Оборудование и нагрузка",string.Join("\n",facts));
        else
        {
            Add("Условия проверки",string.Join("\n", CurrentTool == Tool.Input ? facts.Skip(5) : facts));
            if(crash) Add("Журнал приложения", $"За последние семь дней найдено {evidence.Events} событий с именем {exe} в доступных записях.");
            if(sample.Frames==null && (CurrentTool==Tool.Input || symptom is Symptom.Fps or Symptom.Stutter))
                _results.Children.Add(Action("Перейти к записи кадров",()=>_tool.SelectedIndex=(int)Tool.Overlay));
            if(CurrentTool==Tool.Input || symptom==Symptom.Input)
            {
                bool? fullscreen = game==null ? null : await Task.Run(()=>InputLagAdvisor.ReadFlag(services.Fs,pc.For(game.Id),"setting.fullscreen"),token);
                token.ThrowIfCancellationRequested();
                string mode=fullscreen switch {true=>"полноэкранный",false=>"оконный",_=>"н/д"};
                Add("Видеорежим", $"Конфигурация {exe}: режим {mode}, V-Sync {(evidence.Vsync is true?"вкл":evidence.Vsync is false?"выкл":"н/д")}. Состояние VRR и переопределения драйвера недоступны. Проверьте их в панели видеодрайвера.");
                _results.Children.Add(Action("Параметры дисплея Windows",()=>OpenDisplay()));
            }
        }
        var scenarios = CurrentTool switch {
            Tool.Input=>new[]{Symptom.Input},Tool.Hardware=>new[]{Symptom.Fps,Symptom.Stutter,Symptom.Load,Symptom.Heat},_=>new[]{symptom}};
        var findings=DoctorRules.Evaluate(evidence,scenarios).ToArray();
        foreach(var f in findings) Add(f.Title,f.Evidence+"\nУверенность: "+f.Confidence+". "+f.Next);
        if(CurrentTool==Tool.Hardware)
        {
            // Custom power plan names may contain personal information. Export only hardware and metrics.
            _export=string.Join("\n",facts.Where(f=>!f.StartsWith("Питание")).Concat(findings.Select(f=>f.Title+" · "+f.Evidence+" "+f.Next)));
            _results.Children.Add(Action("Сохранить отчёт…",Export));
        }
        if(crash) _results.Children.Add(Action("Открыть диагностику вылетов",()=>{
            if(game==null){Vm.IsGta=true;Vm.SelectedTab=MainViewModel.GtaDiagnosisTab;}
            else {if(game.Id==GameId.Cs2)Vm.IsCs2=true;else Vm.IsDota=true;Vm.SelectedTab=MainViewModel.CrashTab;}
        }));
    }

    private void OpenDisplay()
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:display-advanced"){UseShellExecute=true}); }
        catch(Exception e){_status.Text=e.Message;}
    }
    private void Export()
    {
        if(_export==null)return;
        var dialog=new SaveFileDialog{FileName="FrameLab-PC.txt",Filter="Текстовый отчёт|*.txt"};
        try {if(dialog.ShowDialog()==true)File.WriteAllText(dialog.FileName,_export);}
        catch(Exception e){_status.Text=e.Message;}
    }

    private void ConfigureOverlay()
    {
        _settings.Children.Add(Heading("Окно поверх игры для оконного и безрамочного режима. Эксклюзивный полный экран и защищённые игры могут его скрывать. Позиция задаётся относительно основного экрана.","PageSub"));
        var optionsPanel=new WrapPanel(); _settings.Children.Add(optionsPanel);
        void Pick(string name,string[] values,int index,Action<int> change)
        {
            var field=new StackPanel();field.Children.Add(Heading(name,"Caption"));
            var pick=Choices(values,index);field.Children.Add(pick);optionsPanel.Children.Add(field);
            pick.SelectionChanged+=(_,_)=>change(pick.SelectedIndex);
        }
        Pick("Режим",["Compact","Standard","Advanced"],_options.Mode,i=>_options=_options with {Mode=i});
        Pick("Угол",["Слева сверху","Справа сверху","Слева снизу","Справа снизу"],_options.Corner,i=>_options=_options with {Corner=i});
        int[] scales=[75,100,125,150],opacity=[35,60,85,100],interval=[500,1000,2000,3000],window=[30,120,240,480],keys=[8,9,10,11];
        void Numbers(string label,int[] values,int selected,Action<int> change,string prefix="")
            => Pick(label,values.Select(i=>prefix+i).ToArray(),Math.Max(0,Array.IndexOf(values,selected)),i=>change(values[i]));
        Numbers("Масштаб, %",scales,_options.Scale,v=>_options=_options with {Scale=v});
        Numbers("Непрозрачность, %",opacity,_options.Opacity,v=>_options=_options with {Opacity=v});
        Numbers("Обновление, мс",interval,_options.Interval,v=>_options=_options with {Interval=v});
        Numbers("Окно, кадров",window,_options.Window,v=>_options=_options with {Window=v});
        Numbers("Показ / скрытие",keys,_options.Key,v=>_options=_options with {Key=v},"Ctrl+Alt+F");
        var flags=new WrapPanel(); _settings.Children.Add(flags);
        foreach(var metric in FrameOverlay.Metrics)
        {
            var box=new CheckBox{Content=metric,IsChecked=!_options.Hidden.Split('|').Contains(metric),Margin=new Thickness(0,3,14,3)};
            flags.Children.Add(box);
            box.Click+=(_,_)=>_options=_options with {Hidden=string.Join("|",flags.Children.OfType<CheckBox>().Where(c=>c.IsChecked!=true).Select(c=>c.Content))};
        }
        Command("Включить с этими настройками",StartOverlay);
        Command("Остановить оверлей",()=>_monitor?.Cancel());
        Command("Записать кадры игры · 60 с",()=>Run(StartFrames));
        Command("Оценить нагрузку окна · 10 с",()=>Run(CompareCost));
        Command("Отменить проверку нагрузки",()=>_operation?.Cancel());
    }

    private async Task StartFrames(CancellationToken token)
    {
        var exe=Exe;
        var capture=await Task.Run(()=>
        {
            var tool=PresentMonTool.Find()??throw new InvalidOperationException("Отсутствует PresentMon. Установите полную сборку.");
            return FpsCapture.Start(tool,exe,60,"doctor","FrameLabDoctor");
        },token);
        if(capture.Declined || capture.StartError!=null)throw new InvalidOperationException(capture.StartError??"Запрос прав отклонён.");
        await Task.Delay(500,token);
        if(capture.HasExited && capture.ExitCode!=0)
            throw new InvalidOperationException(capture.Problem(0)??"Запись завершилась до получения кадров.");
        Add("Запись запущена",exe+" · до 60 секунд. Следите за кадрами в оверлее.");
    }

    private async void StartOverlay()
    {
        if (_monitor!=null){_status.Text="Сначала остановите действующий оверлей.";return;}
        using var stop=new CancellationTokenSource();
        _monitor=stop;
        try
        {
            var options=_options.Validate();options.Save(OptionsPath);
            var overlay=new FrameOverlay(options);
            var main=Window.GetWindow(this);
            void CloseWithMain(object? sender, EventArgs args) => overlay.Close();
            if(main!=null) main.Closed+=CloseWithMain;
            overlay.Closed+=(_,_)=> { if(main!=null) main.Closed-=CloseWithMain; };
            _overlay=overlay;
            overlay.Closed+=(_,_)=>stop.Cancel();
            overlay.Show();
            _status.Text=overlay.HotkeyReady?"Оверлей включён":"Оверлей включён. Горячая клавиша занята.";
            await _feed.RunAsync(options.Interval,options.Window,s=>
            {
                Dispatcher.Invoke(()=>{ if(!stop.IsCancellationRequested)overlay.Render(s); });
            },stop.Token);
        }
        catch(OperationCanceledException){_status.Text="Оверлей остановлен";}
        catch(Exception e){_status.Text="Оверлей недоступен. "+e.Message;}
        finally {if (_overlay?.IsLoaded==true)_overlay.Close(); _overlay=null; _monitor=null;}
    }

    private async Task CompareCost(CancellationToken token)
    {
        var overlay=_overlay??throw new InvalidOperationException("Сначала включите оверлей.");
        bool visible=overlay.IsVisible;
        using var process=Process.GetCurrentProcess();
        async Task<double> Cpu(bool show)
        {
            if(show)overlay.Show();else overlay.Hide();
            var timer=Stopwatch.StartNew();var before=process.TotalProcessorTime;
            await Task.Delay(5000,token);
            return (process.TotalProcessorTime-before).TotalMilliseconds/timer.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
        }
        try
        {
            var off=await Cpu(false);var on=await Cpu(true);
            Add("CPU процесса FrameLab",$"Скрыто {off:0.00}% · видно {on:0.00}%. Сбор включён в обоих интервалах. Для влияния на FPS повторите одну игровую сцену с окном и без него.");
        }
        finally{if(_overlay==overlay && overlay.IsLoaded){if(visible)overlay.Show();else overlay.Hide();}}
    }
}
