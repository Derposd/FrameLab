using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dota2Optimizer.Core.Benchmark;
using Microsoft.Win32;

namespace Dota2Optimizer.App.Views;

/// <summary>Renders the exact same card for preview and PNG. Only recorded values; no user paths or nicknames.</summary>
public sealed class ResultCardDialog : Window
{
    private readonly BenchResult _before, _after;
    private readonly string _name;
    private readonly CheckBox _hardware = new() { Content = "Показать модель CPU и GPU", IsChecked = false, Margin = new Thickness(0,12,0,8) };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "Пост · 1080 × 1080", "История · 1080 × 1920" }, SelectedIndex = 0, Width = 215 };
    private readonly CheckBox _dark = new() { Content = "Тёмная карточка", IsChecked = true, Margin = new Thickness(18,0,0,0), VerticalAlignment=VerticalAlignment.Center };
    private readonly Viewbox _preview = new() { Stretch = Stretch.Uniform, Margin=new Thickness(0,16,0,8) };
    private FrameworkElement? _card;
    public ResultCardDialog(BenchResult before, BenchResult after, string name)
    {
        if (!ShareComparison.CanCompare(before,after)) throw new ArgumentException("Несопоставимые замеры");
        _before=before;_after=after;_name=name;
        Title="FrameLab · Карточка результата";Width=710;Height=810;MinWidth=560;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Owner=Application.Current.MainWindow;Style=(Style)Application.Current.FindResource("DarkWindow");
        var grid=new Grid{Margin=new Thickness(26),Background=(Brush)Application.Current.FindResource("Bg")};
        grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var controls=new StackPanel{Orientation=Orientation.Horizontal};controls.Children.Add(_format);controls.Children.Add(_dark);grid.Children.Add(controls);
        Grid.SetRow(_preview,1);grid.Children.Add(_preview);
        var footer=new StackPanel();Grid.SetRow(footer,2);grid.Children.Add(footer);footer.Children.Add(_hardware);
        var save=new Button{Content="Сохранить PNG",Style=(Style)Application.Current.FindResource("Primary"),HorizontalAlignment=HorizontalAlignment.Left};save.Click+=(_,_)=>Save();footer.Children.Add(save);
        Content=grid;
        _format.SelectionChanged+=(_,_)=>Render();_dark.Checked+=(_,_)=>Render();_dark.Unchecked+=(_,_)=>Render();_hardware.Checked+=(_,_)=>Render();_hardware.Unchecked+=(_,_)=>Render();
        _hardware.IsEnabled=!string.IsNullOrWhiteSpace(after.Hardware);Render();
    }
    private void Render()
    {
        bool dark=_dark.IsChecked==true, vertical=_format.SelectedIndex==1;
        Brush bg=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#22241F":"#F1EEE5"));
        Brush fg=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#F1EEE5":"#191A17"));
        Brush muted=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#B7BAAC":"#626459"));
        Brush accent=new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark?"#FF7950":"#D63912"));
        var canvas=new Grid{Width=1080,Height=vertical?1920:1080,Background=bg};
        var panel=new StackPanel{Width=924};canvas.Children.Add(new Viewbox{Child=panel,Margin=new Thickness(78),Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly});
        TextBlock Text(string text,double size,Brush brush,bool bold=false)=>new(){Text=text,FontSize=size,Foreground=brush,FontFamily=new FontFamily("Segoe UI"),FontWeight=bold?FontWeights.Bold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap};
        panel.Children.Add(Text("F R A M E L A B",28,fg,true));
        var title=Text("Мой ПК.\nМой результат.",vertical?82:74,fg,true);title.Margin=new Thickness(0,46,0,26);panel.Children.Add(title);
        panel.Children.Add(Text(_name,28,muted));panel.Children.Add(Text(_after.Conditions??"Замер игровой сессии",20,muted));
        var stats=new Grid{Margin=new Thickness(0,44,0,32)};stats.ColumnDefinitions.Add(new ColumnDefinition());stats.ColumnDefinitions.Add(new ColumnDefinition());
        void Metric(BenchResult run,int col,string label,Brush brush)
        {
            var p=new StackPanel();Grid.SetColumn(p,col);stats.Children.Add(p);p.Children.Add(Text(label,23,muted));p.Children.Add(Text(run.AvgFps.ToString("0",CultureInfo.CurrentCulture),116,brush,true));p.Children.Add(Text("средний FPS",24,muted));p.Children.Add(Text($"1% low · {run.Low1Fps:0} FPS",29,fg));
        }
        Metric(_before,0,"ДО",fg);Metric(_after,1,"ПОСЛЕ",accent);panel.Children.Add(stats);
        var delta=(_after.AvgFps/_before.AvgFps-1)*100;
        panel.Children.Add(Text($"{delta:+0.0;-0.0;0}%  · средний FPS",42,accent,true));
        panel.Children.Add(Text($"До: {_before.Time:dd.MM.yyyy HH:mm} / {_before.DurationSec:0} с\nПосле: {_after.Time:dd.MM.yyyy HH:mm} / {_after.DurationSec:0} с",20,muted));
        if(_hardware.IsChecked==true && _after.Hardware is { } hw){var text=Text(hw,22,muted);text.Margin=new Thickness(0,30,0,0);panel.Children.Add(text);}
        var note=Text(_after.Game.StartsWith("arena-")?"Синтетический тест, не FPS в конкретной игре. Сравнение одного ПК при одинаковых условиях.":"Результат этого ПК и игровой сцены. Сравнивайте одинаковые условия и настройки.",22,muted);note.Margin=new Thickness(0,32,0,0);panel.Children.Add(note);
        panel.Children.Add(Text("Разница до 3% может быть разбросом между запусками.",20,muted));
        _card=canvas;_preview.Child=canvas;
    }
    private void Save()
    {
        var dialog=new SaveFileDialog{Filter="PNG|*.png",FileName=$"FrameLab-result-{DateTime.Now:yyyyMMdd-HHmm}.png"};
        if(dialog.ShowDialog(this)!=true||_card is null)return;
        try
        {
            var bitmap=RenderCard();
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create(dialog.FileName);encoder.Save(file);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){MessageBox.Show(this,"Не удалось сохранить PNG: "+ex.Message,"FrameLab");}
    }
    public BitmapSource RenderCard()
    {
        var card=_card??throw new InvalidOperationException("Карточка ещё не создана.");
        card.Measure(new Size(card.Width,card.Height));card.Arrange(new Rect(0,0,card.Width,card.Height));card.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)card.Width,(int)card.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(card);return bitmap;
    }
}
