using System.Windows;
using System.Windows.Controls;
using Dota2Optimizer.App.ViewModels;
using Dota2Optimizer.Core.Infrastructure;

namespace Dota2Optimizer.App.Views;

public sealed class GettingStartedPage : UserControl
{
    private readonly TextBlock _route=PersonalProfilesPage.Heading("Начнём с исходного замера, затем выберем изменения и сравним результат.","PageSub");
    private readonly ComboBox _game=new(){ItemsSource=new[]{"Dota 2","Counter-Strike 2","GTA V · RP"},SelectedIndex=0,MinWidth=220,Margin=new Thickness(0,12,0,16),HorizontalAlignment=HorizontalAlignment.Left};
    private int _goal;
    private readonly string[] _routes={"Исходный замер → диагностика → выбранные изменения → повторный замер.","Выберите игру. Начнём с диагностики: частота монитора, синхронизация и параметры игры.","Выберите игру. Мастер проверит оборудование и предложит графику под этот ПК."};
    public GettingStartedPage()
    {
        var panel=new StackPanel{Margin=new Thickness(36,28,40,30)};Content=new ScrollViewer{Content=panel};
        panel.Children.Add(PersonalProfilesPage.Heading("ЗНАКОМСТВО / 01","Kicker"));panel.Children.Add(PersonalProfilesPage.Heading("С чего начнём?","PageTitle"));
        panel.Children.Add(PersonalProfilesPage.Heading("Выберите, что важно сейчас. FrameLab предложит порядок действий. Ничего не применяется автоматически.","PageSub"));
        var titles=new[]{"01   Играть без рывков","02   Быстрее чувствовать управление","03   Настроить слабый ПК"};
        var descriptions=new[]{"Проверить просадки и стабильность кадров.","Проверить экран и настройки задержки.","Подобрать графику и оценить фоновую нагрузку."};
        for(int i=0;i<3;i++)
        {
            var index=i;var content=new StackPanel();content.Children.Add(PersonalProfilesPage.Heading(titles[i],"H1"));content.Children.Add(PersonalProfilesPage.Heading(descriptions[i],"Caption"));
            var radio=new RadioButton{Content=content,GroupName="welcome-goal",IsChecked=i==0,Style=(Style)Application.Current.FindResource("ChoiceCard"),Margin=new Thickness(0,16,0,0)};
            radio.Checked+=(_,_)=>{_goal=index;_route.Text=_routes[index];_game.Visibility=index==0?Visibility.Collapsed:Visibility.Visible;};panel.Children.Add(radio);
        }
        _game.Visibility=Visibility.Collapsed;panel.Children.Add(_game);_route.Margin=new Thickness(0,22,0,18);panel.Children.Add(_route);
        var actions=new WrapPanel();actions.Children.Add(PersonalProfilesPage.Action("Начать с проверки",Start,true));actions.Children.Add(PersonalProfilesPage.Action("Перейти к Windows · ПК",()=>{if(MarkSeen()&&DataContext is MainViewModel vm)vm.GoToPcPage(MainViewModel.SystemTweaksTab);}));panel.Children.Add(actions);
        panel.Children.Add(PersonalProfilesPage.Heading("Этот экран всегда можно открыть снова через «С чего начать» в левом меню.","Caption"));
    }
    public static string Marker=>Path.Combine(AppPaths.Root,"getting-started.json");
    private bool MarkSeen()
    {
        try{new RealFileSystem().WriteAllText(Marker,System.Text.Json.JsonSerializer.Serialize(new {Version=1,Goal=_goal}));return true;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){_route.Text="Не удалось сохранить выбор: "+ex.Message;return false;}
    }
    private void Start()
    {
        if(DataContext is not MainViewModel vm||!MarkSeen())return;
        if(_goal==0){vm.GoToPcPage(MainViewModel.BenchmarkTab);return;}
        if(_game.SelectedIndex==2){vm.IsGta=true;vm.SelectedTab=MainViewModel.GtaModesTab;}
        else{if(_game.SelectedIndex==1)vm.IsCs2=true;else vm.IsDota=true;vm.SelectedTab=MainViewModel.OptimizeTab;}
    }
}
