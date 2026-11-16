using System.Windows;
using System.Windows.Controls;
using Dota2Optimizer.App.ViewModels;
using Dota2Optimizer.Core.Infrastructure;
using Dota2Optimizer.Core.Tweaks;

namespace Dota2Optimizer.App.Views;

public sealed class PersonalProfilesPage : UserControl
{
    private readonly StackPanel _body=new(){Margin=new Thickness(36,24,40,30)};
    private readonly StackPanel _rows=new();
    private readonly WrapPanel _profileBar=new(){Margin=new Thickness(0,20,0,10)};
    private readonly ComboBox _pick=new(){MinWidth=200,DisplayMemberPath="Name"};
    private readonly TextBox _name=new(){MinWidth=160,MaxLength=48,Margin=new Thickness(0,0,12,0)};
    private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,15,0,0)};
    private readonly Dictionary<string,ComboBox> _choices=new();
    private readonly List<PersonalProfile> _profiles=new();
    private PersonalProfileStore? _store;
    private AppServices? _services;
    private bool _loaded;
    public PersonalProfilesPage()
    {
        Content=new ScrollViewer{Content=_body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        _body.Children.Add(Heading("ВАШИ НАСТРОЙКИ","Kicker"));_body.Children.Add(Heading("Один ПК. Разные задачи.","PageTitle"));
        _body.Children.Add(Heading("Сохраните свои наборы твиков Windows. Переключение всегда начинается с предпросмотра; программы не закрываются.","PageSub"));
        _body.Children.Add(_profileBar);
        var toolbar=new WrapPanel{Margin=new Thickness(0,0,0,18)};_pick.Visibility=Visibility.Collapsed;toolbar.Children.Add(_pick);toolbar.Children.Add(Action("+ Свой профиль",()=>Create(false)));toolbar.Children.Add(Action("Снимок текущих твиков",()=>Run(()=>Capture())));_body.Children.Add(toolbar);
        var names=new WrapPanel{Margin=new Thickness(0,0,0,12)};names.Children.Add(_name);names.Children.Add(Action("Сохранить",Save));names.Children.Add(Action("Удалить",Delete));_body.Children.Add(names);
        _body.Children.Add(Heading("«Вернуть исходное» откатывает только изменения FrameLab с сохранённой копией. Уже включённые пользователем настройки не выключаются. Невыбранные пункты не затрагиваются.","Caption"));
        _body.Children.Add(Action("Посмотреть изменения и переключить",()=>Run(Apply),true));_body.Children.Add(_status);_body.Children.Add(_rows);
        _pick.SelectionChanged+=(_,_)=>ShowProfile();
        Loaded+=(_,_)=>{if(_loaded)return;_loaded=true;LoadProfiles();};
    }
    internal static TextBlock Heading(string text,string style)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Style=(Style)Application.Current.FindResource(style)};
    internal static Button Action(string text,Action action,bool primary=false)
    {
        var button=new Button{Content=text,Style=(Style)Application.Current.FindResource(primary ? (object)"Primary" : typeof(Button)),Margin=new Thickness(0,0,10,8),HorizontalAlignment=HorizontalAlignment.Left};button.Click+=(_,_)=>action();return button;
    }
    private void LoadProfiles()
    {
        if(DataContext is not MainViewModel vm)return;_services=vm.Services;_store=new(_services.Fs,Path.Combine(AppPaths.Root,"personal-profiles.json"));
        try{_profiles.AddRange(_store.Load());RefreshPick(_profiles.FirstOrDefault()?.Id);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException){_status.Text="Не удалось прочитать профили. Файл не изменён: "+ex.Message;_rows.IsEnabled=false;_store=null;}
    }
    private void RefreshPick(string? id)
    {
        _pick.ItemsSource=null;_pick.ItemsSource=_profiles;_pick.SelectedItem=_profiles.FirstOrDefault(p=>p.Id==id)??_profiles.FirstOrDefault();
        _profileBar.Children.Clear();
        foreach(var profile in _profiles)
        {
            var selected=profile.Id==(_pick.SelectedItem as PersonalProfile)?.Id;
            var radio=new RadioButton{Content=profile.Name,GroupName="personal-profile",Style=(Style)Application.Current.FindResource("Segment"),IsChecked=selected,FontSize=18,Padding=new Thickness(20,16,20,16)};
            radio.Checked+=(_,_)=>_pick.SelectedItem=profile;_profileBar.Children.Add(radio);
        }
    }
    private void ShowProfile()
    {
        _rows.Children.Clear();_choices.Clear();if(_pick.SelectedItem is not PersonalProfile p)return;
        _name.Text=p.Name;
        _status.Text=p.Id switch
        {
            "quiet"=>"Возвращает схему питания, сохранённую перед твиками FrameLab. Скоростью вентиляторов не управляет; если копии нет, питание не меняется.",
            "stream"=>"Сохраняет игровой режим и возвращает ваши исходные настройки записи и питания. OBS и другие программы не закрываются.",
            _=>"Выберите действия для каждого пункта. Ничего не меняется до подтверждения.",
        };
        var additional=new StackPanel();
        foreach(var tweak in SystemTweakCatalog.All.OrderBy(t=>p.Settings.ContainsKey(t.Id)?0:1))
        {
            var row=new Grid{Margin=new Thickness(0,0,0,0)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});
            var text=new StackPanel{Margin=new Thickness(0,12,18,12)};text.Children.Add(Heading(tweak.Title,"H1"));text.Children.Add(Heading(tweak.Risk,"Caption"));row.Children.Add(text);
            var choice=new ComboBox{ItemsSource=new[]{"Не менять","Включить","Вернуть исходное"},SelectedIndex=p.Settings.TryGetValue(tweak.Id,out var on)?on?1:2:0,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(choice,1);row.Children.Add(choice);_choices[tweak.Id]=choice;
            (p.Settings.ContainsKey(tweak.Id)?_rows:additional).Children.Add(new Border{Child=row,BorderThickness=new Thickness(0,0,0,1),BorderBrush=(System.Windows.Media.Brush)Application.Current.FindResource("BorderBrush")});
        }
        _rows.Children.Add(new Expander{Header="Добавить другие настройки в профиль",Content=additional,Margin=new Thickness(0,20,0,0)});
    }
    private Dictionary<string,bool> ReadSettings()=>_choices.Where(p=>p.Value.SelectedIndex>0).ToDictionary(p=>p.Key,p=>p.Value.SelectedIndex==1);
    private bool SaveCurrent()
    {
        if(_store is null||_pick.SelectedItem is not PersonalProfile p)return false;
        if(string.IsNullOrWhiteSpace(_name.Text)){_status.Text="Введите название профиля.";return false;}
        var settings=ReadSettings();
        if(settings.GetValueOrDefault("win.powerplan")&&settings.GetValueOrDefault("win.powerplan.ultimate")){_status.Text="Выберите только одну схему питания.";return false;}
        var updated=p with{Name=_name.Text.Trim(),Settings=settings};
        var list=_profiles.Select(x=>x.Id==p.Id?updated:x).ToList();_store.Save(list);_profiles.Clear();_profiles.AddRange(list);RefreshPick(p.Id);return true;
    }
    private void Save(){try{if(SaveCurrent())_status.Text="Профиль сохранён. Настройки компьютера не изменялись.";}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){_status.Text=ex.Message;}}
    private void Create(bool capture)
    {
        if(_store is null)return;var p=new PersonalProfile(Guid.NewGuid().ToString("N"),"Мой профиль",new());
        try{_store.Save(_profiles.Append(p));_profiles.Add(p);RefreshPick(p.Id);_name.Focus();_name.SelectAll();}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){_status.Text=ex.Message;}
    }
    private void Delete()
    {
        if(_store is null||_pick.SelectedItem is not PersonalProfile p||!Ui.Confirm($"Удалить профиль «{p.Name}»? Настройки Windows останутся как есть."))return;
        try{var list=_profiles.Where(x=>x.Id!=p.Id).ToList();_store.Save(list);_profiles.Clear();_profiles.AddRange(list);RefreshPick(null);_name.Text=_pick.SelectedItem is PersonalProfile selected?selected.Name:"";_status.Text="Профиль удалён. Windows не изменялась.";}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){_status.Text=ex.Message;}
    }
    private async void Run(Func<Task> action)
    {
        _body.IsEnabled=false;
        try{await action();}catch(Exception ex){Log.Error("Личный профиль",ex);_status.Text="Операция не завершена: "+ex.Message;}finally{_body.IsEnabled=true;}
    }
    private async Task<IReadOnlyList<TweakToggle>> Evaluate()
    {
        var s=_services!;await s.EnsureSnapshotAsync();
        var snapshot=await Task.Run(()=>s.QuickScan()??s.Scan());s.Snapshot=snapshot;
        return await Task.Run(()=>TweakEngine.EvaluateAll(SystemTweakCatalog.All,s.Context(snapshot),s.Store));
    }
    private async Task Capture()
    {
        if(_store is null)return;var states=await Evaluate();
        var p=new PersonalProfile(Guid.NewGuid().ToString("N"),"Снимок "+DateTime.Now.ToString("dd.MM HH:mm"),states.Where(t=>t.Plan.Status is TweakStatus.AlreadyOptimal or TweakStatus.CanApply).ToDictionary(t=>t.Tweak.Id,t=>t.State==TweakState.Applied));
        _store.Save(_profiles.Append(p));_profiles.Add(p);RefreshPick(p.Id);_status.Text="Сохранены состояния твиков Windows. Файлы игр и фоновые программы в профиль не входят.";
    }
    private async Task Apply()
    {
        if(_services is null||!SaveCurrent()||_pick.SelectedItem is not PersonalProfile profile)return;
        var states=await Evaluate();var target=profile.Settings;
        var undo=states.Where(t=>target.TryGetValue(t.Tweak.Id,out var on)&&!on&&t.CanTurnOff).ToList();
        var apply=states.Where(t=>target.TryGetValue(t.Tweak.Id,out var on)&&on&&t.CanTurnOn).ToList();
        var unavailable=states.Where(t=>target.TryGetValue(t.Tweak.Id,out var on)&&on&&!t.CanTurnOn&&t.State!=TweakState.Applied).ToList();
        var description=string.Join("\n",undo.Select(t=>"Вернуть исходное: "+t.Tweak.Title).Concat(apply.Select(t=>"Включить: "+t.Tweak.Title+" — "+t.Tweak.Risk)).Concat(unavailable.Select(t=>"Недоступно: "+t.Tweak.Title+" — "+t.Note)));
        if(undo.Count+apply.Count==0){_status.Text=unavailable.Count==0?"Изменять нечего: выбранные пункты уже настроены.":description;return;}
        if(!Ui.Confirm($"Профиль «{profile.Name}»\n\n{description}\n\nПродолжить к подтверждению отката и предпросмотру значений?"))return;
        if(undo.Count>0)
        {
            var report=await _services.UndoManyAsync(undo.SelectMany(t=>t.OwnChanges).Select(c=>c.Id).ToList(),profile.Name);
            if(report is null){_status.Text="Переключение отменено.";return;}
            // Do not proceed over a failed rollback or cancellation in the elevated helper.
            var check=await Evaluate();if(check.Any(t=>undo.Any(u=>u.Tweak.Id==t.Tweak.Id)&&t.CanTurnOff)){_status.Text="Не все изменения удалось откатить. Применение следующего набора остановлено.\n"+string.Join("\n",report);return;}
        }
        states=await Evaluate();apply=states.Where(t=>target.TryGetValue(t.Tweak.Id,out var on)&&on&&t.CanTurnOn).ToList();
        var lines=await _services.ConfirmAndApplyAsync(apply.Select(t=>t.Plan).ToList(),profile.Name);
        if(lines is null){_status.Text="Применение отменено. Уже выполненный откат сохранён в журнале.";return;}
        _status.Text=string.Join("\n",lines.Select(l=>l.Text));
        var final=await Evaluate();var missing=final.Where(t=>target.TryGetValue(t.Tweak.Id,out var on)&&on&&t.State!=TweakState.Applied).Select(t=>t.Tweak.Title).ToList();
        _status.Text+="\n"+(missing.Count==0?"Выбранные включения проверены. Откат доступен в разделе «Откат».":"Не всё применено: "+string.Join(", ",missing));
    }
}
