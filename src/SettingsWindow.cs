using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PadStudio;
public sealed class SettingsWindow : Window
{
    readonly CheckBox closeBox=new(){Content="Pencereyi kapattığımda sistem tepsisinde çalışsın"};
    readonly CheckBox startBox=new(){Content="Windows açıldığında başlasın"};
    readonly CheckBox autoBox=new(){Content="Açılışta ve Kaydet'te numpadi otomatik etkinleştir"};
    readonly TextBlock result=ThemeManager.Text("",12,"Muted");
    readonly Dictionary<string,Button> themeButtons=new();
    readonly string originalTheme;
    public bool CloseToTray=>closeBox.IsChecked==true;
    public bool StartWithWindows=>startBox.IsChecked==true;
    public bool AutoActivate=>autoBox.IsChecked==true;
    public string SelectedThemeId { get; private set; }
    public bool FactoryResetRequested { get; private set; }
    public SettingsWindow(AppConfig config,bool driverAvailable)
    {
        Style=(Style)FindResource(typeof(Window));
        Title="Cihaz ve ayarlar · Pad Studio"; Width=700; Height=840; MinHeight=640; ResizeMode=ResizeMode.CanResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        originalTheme=ThemeManager.Current.Id; SelectedThemeId=ThemeManager.Normalize(config.ThemeId);
        Closed+=(_,_)=> { if(DialogResult!=true) ThemeManager.Apply(originalTheme); };
        var panel=new StackPanel { Margin=new Thickness(30,26,30,30) }; Content=new ScrollViewer { Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
        var header=new DockPanel(); var logo=new Image { Source=new BitmapImage(new Uri("pack://application:,,,/Assets/pad-studio.png")),Width=62,Height=62,Margin=new Thickness(0,0,16,0) }; DockPanel.SetDock(logo,Dock.Left); header.Children.Add(logo);
        var titles=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; titles.Children.Add(ThemeManager.Text("Cihaz ve ayarlar",27)); titles.Children.Add(ThemeManager.Text("Pad Studio'yu kendine göre şekillendir.",12,"Muted")); header.Children.Add(titles); panel.Children.Add(header);

        var appearance=Section(panel,"Görünüm","Bir tema seç; değişimi anında gör. Seçimini Ayarları kaydet ile koru.");
        var themes=new System.Windows.Controls.Primitives.UniformGrid { Columns=2,Margin=new Thickness(0,15,0,0) };
        foreach(var theme in ThemeManager.Themes) {
            var swatch=new Grid { Height=48,Margin=new Thickness(0,0,0,11),Background=new LinearGradientBrush(ThemeManager.Brush(theme.Top).Color,ThemeManager.Brush(theme.Bottom).Color,30) };
            var squares=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            for(int i=0;i<4;i++) squares.Children.Add(new Border { Width=26,Height=26,CornerRadius=new CornerRadius(7),Margin=new Thickness(3,0,3,0),Background=ThemeManager.Brush(theme.Surface),BorderBrush=ThemeManager.Brush(theme.Dark?"#74FFFFFF":"#EFFFFFFF"),BorderThickness=new Thickness(1) });
            swatch.Children.Add(squares);
            var stack=new StackPanel(); stack.Children.Add(swatch); stack.Children.Add(ThemeManager.Text(theme.Name,14)); stack.Children.Add(ThemeManager.Text(theme.Description,10,"Muted"));
            var button=new Button { Content=stack,Tag=theme.Id,Margin=new Thickness(0,0,9,10),Padding=new Thickness(12),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=theme.Description };
            button.Click+=(_,_)=>SelectTheme(theme.Id); themeButtons.Add(theme.Id,button); themes.Children.Add(button);
        }
        appearance.Children.Add(themes); RefreshThemeSelection();

        var preferences=Section(panel,"Uygulama","Her gün kullandığın küçük tercihler.");
        closeBox.IsChecked=config.CloseToTray; startBox.IsChecked=config.StartWithWindows; autoBox.IsChecked=config.AutoActivate;
        preferences.Children.Add(closeBox); preferences.Children.Add(startBox); preferences.Children.Add(autoBox);

        var device=Section(panel,"Cihaz bağlantısı",driverAvailable?"●  Numpad desteği hazır":"●  Numpad desteği için kurulum gerekli");
        var signal=new Button { Content="Numpad bağlantısını test et",HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,13,0,0) };
        signal.Click+=(_,_)=>new SignalWindow{Owner=this}.ShowDialog(); device.Children.Add(signal);
        var details=new StackPanel();
        details.Children.Add(ThemeManager.Text("Interception sürücüsü, harici numpadi ana klavyenden ayırır. Kurulum klavye sürücü zincirini değiştirir; yönetici izni ve yeniden başlatma gerektirir.",12,"Muted"));
        var row=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,14,0,0) };
        var install=new Button { Content="Sürücüyü kur",Margin=new Thickness(0,0,8,0) }; var uninstall=new Button { Content="Sürücüyü kaldır" };
        install.Click+=async(_,_)=>await DriverAction("install",install,uninstall); uninstall.Click+=async(_,_)=>await DriverAction("uninstall",install,uninstall);
        row.Children.Add(install); row.Children.Add(uninstall); details.Children.Add(row);
        var help=new Button { Content="Kurulum günlüğünü aç ↗",HorizontalAlignment=HorizontalAlignment.Left,FontSize=11,Margin=new Thickness(0,10,0,0) };
        help.Click+=(_,_)=> { var path=Path.Combine(ConfigStore.DirectoryPath,"driver-setup.log"); if(File.Exists(path)) Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); else result.Text="Henüz kurulum yapılmadı."; }; details.Children.Add(help);
        var expander=new Expander { Header="Sürücü yönetimi",Content=details,Margin=new Thickness(0,17,0,0),Padding=new Thickness(0,6,0,0) }; expander.SetResourceReference(ForegroundProperty,"Muted"); device.Children.Add(expander);

        var files=Section(panel,"Kayıt ve yedekler","Görevlerin bu bilgisayarda saklanır. Save klasöründe ikinci kopya, önceki kayıtlarda kurtarma yedekleri tutulur.");
        var pathLabel=ThemeManager.Text(ConfigStore.MirrorPath,11,"Muted"); pathLabel.Margin=new Thickness(0,10,0,0); files.Children.Add(pathLabel);
        var fileRow=new WrapPanel { Margin=new Thickness(0,13,0,0) };
        void FileButton(string label,Action action) { var button=new Button { Content=label,FontSize=11,Margin=new Thickness(0,0,8,8) }; button.Click+=(_,_)=>action(); fileRow.Children.Add(button); }
        FileButton("Save klasörünü aç",()=>OpenFolder(Path.GetDirectoryName(ConfigStore.MirrorPath)!));
        FileButton("Yedekleri aç",()=>OpenFolder(Path.Combine(ConfigStore.DirectoryPath,"backups")));
        FileButton("Düzeni içe aktar…",()=> { DialogResult=false; (Owner as MainWindow)?.ImportConfig(); }); files.Children.Add(fileRow);
        result.Margin=new Thickness(0,12,0,0); panel.Children.Add(result);
        var save=new Button { Content="Ayarları kaydet",HorizontalAlignment=HorizontalAlignment.Stretch,Style=(Style)FindResource("Primary"),Margin=new Thickness(0,12,0,0),Padding=new Thickness(16,14,16,14) };
        save.Click+=(_,_)=> { try { StartupRegistration.Set(startBox.IsChecked==true); DialogResult=true; } catch(Exception ex) { result.Text="Başlangıç ayarı değiştirilemedi: "+ex.Message; } }; panel.Children.Add(save);

        var resetPanel=new StackPanel(); var resetTitle=ThemeManager.Text("Fabrika ayarları",17,"Danger"); resetTitle.FontWeight=FontWeights.SemiBold; resetPanel.Children.Add(resetTitle);
        var resetDescription=ThemeManager.Text("Bütün görevleri, numpad eşleştirmesini ve uygulama tercihlerini sıfırlar. Önce mevcut kaydın yedeği alınır.",12,"Muted"); resetDescription.Margin=new Thickness(0,9,0,14); resetPanel.Children.Add(resetDescription);
        var reset=new Button { Content="Fabrika ayarlarına döndür",HorizontalAlignment=HorizontalAlignment.Left }; reset.SetResourceReference(BackgroundProperty,"DangerBg"); reset.SetResourceReference(ForegroundProperty,"Danger"); reset.SetResourceReference(BorderBrushProperty,"Danger");
        reset.Click+=(_,_)=> {
            if(MessageBox.Show(this,"Tüm tuş görevleri, numpad eşleştirmesi ve uygulama ayarları sıfırlanacak. Devam edilsin mi?","Fabrika ayarlarına döndür",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            FactoryResetRequested=true; DialogResult=true;
        };
        resetPanel.Children.Add(reset); var resetCard=new Border { Child=resetPanel,Style=(Style)FindResource("GlassCard"),Padding=new Thickness(21),Margin=new Thickness(0,28,0,0) }; resetCard.SetResourceReference(Border.BackgroundProperty,"DangerBg"); panel.Children.Add(resetCard);
    }
    StackPanel Section(StackPanel parent,string title,string description)
    {
        var stack=new StackPanel(); var heading=ThemeManager.Text(title,18); heading.FontWeight=FontWeights.SemiBold; stack.Children.Add(heading);
        var subtitle=ThemeManager.Text(description,12,"Muted"); subtitle.Margin=new Thickness(0,7,0,0); stack.Children.Add(subtitle);
        parent.Children.Add(new Border { Style=(Style)FindResource("GlassCard"),Padding=new Thickness(21),Margin=new Thickness(0,22,0,0),Child=stack }); return stack;
    }
    internal void SelectTheme(string id) { SelectedThemeId=ThemeManager.Normalize(id); ThemeManager.Apply(SelectedThemeId); RefreshThemeSelection(); }
    void RefreshThemeSelection() { foreach(var pair in themeButtons) { bool on=pair.Key==SelectedThemeId; pair.Value.SetResourceReference(BorderBrushProperty,on?"Accent":"GlassEdge"); pair.Value.BorderThickness=new Thickness(on?2:1); } }
    void OpenFolder(string path)
    {
        try { Directory.CreateDirectory(path); Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); }
        catch(Exception ex) { result.Text="Klasör açılamadı: "+ex.Message; }
    }
    async Task DriverAction(string action,params Button[] buttons)
    {
        foreach(var button in buttons) button.IsEnabled=false;
        try {
            var exe=Environment.ProcessPath??throw new InvalidOperationException("Uygulama yolu bulunamadı.");
            using var process=Process.Start(new ProcessStartInfo(exe,"--driver-"+action) { UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden });
            if(process is null) throw new InvalidOperationException("Kurulum başlatılamadı.");
            await process.WaitForExitAsync();
            result.Text=process.ExitCode==0?"Sürücü komutu tamamlandı. Çalışmalarını kaydedip Windows'u yeniden başlat.":"Kurulum tamamlanamadı. Kurulum günlüğünü inceleyebilirsin.";
        } catch(System.ComponentModel.Win32Exception ex) when(ex.NativeErrorCode==1223) { result.Text="Windows yönetici izni verilmedi; kurulum yapılmadı."; }
        catch(Exception ex) { result.Text=ex.Message; }
        finally { foreach(var button in buttons) button.IsEnabled=true; }
    }
}
public static class StartupRegistration
{
    public static void Set(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(enabled) key.SetValue("PadStudio","\""+Environment.ProcessPath+"\" --tray"); else key.DeleteValue("PadStudio",false);
    }
}
