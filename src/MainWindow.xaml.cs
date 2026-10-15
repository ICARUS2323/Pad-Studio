using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace PadStudio;

public partial class MainWindow : Window
{
    AppConfig config;
    readonly DeviceEngine? engine;
    readonly ActionRunner runner=new();
    readonly Dictionary<string,Button> padButtons=new();
    readonly bool preview;
    PadKey selected=PadLayout.Keys.First(k=>k.Id=="N1");
    List<MacroStep> draftSteps=new();
    bool loading=true,dirty,exiting;
    Forms.NotifyIcon? tray;
    IntPtr ownHandle;
    DispatcherTimer? draftTimer;
    public MainWindow(bool previewMode=false)
    {
        preview=previewMode; config=preview?AppConfig.Defaults():ConfigStore.Load();
        if(!preview) ThemeManager.Apply(config.ThemeId);
        InitializeComponent();
        KindBox.ItemsSource=new[]{"Görev yok","Klavye makrosu","Uygulama aç","Web sitesi aç","Metin yaz","Medya kontrolü","Klasör aç"};
        MediaBox.ItemsSource=new[]{"Sesi artır","Sesi azalt","Sesi kapat / aç","Oynat / duraklat","Sonraki parça","Önceki parça"};
        BuildPad(); SelectKey(selected); runner.Status+=ShowStatus;
        if(!preview) {
            draftTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) };
            draftTimer.Tick+=(_,_)=> { draftTimer.Stop(); PersistEditorDraft(); };
            engine=new DeviceEngine(); engine.Status+=ShowStatus;
            engine.Changed+=()=>Dispatcher.BeginInvoke(RefreshDevice);
            engine.Paired+=device=>Dispatcher.BeginInvoke(()=> { config.DeviceHardwareId=device.HardwareId; TrySaveConfig(); RefreshDevice(); });
            engine.Pressed+=key=>Dispatcher.BeginInvoke(()=>OnPadPressed(key));
            engine.SelectSaved(config.DeviceHardwareId,config.AutoActivate);
            Loaded+=(_,_)=> { ownHandle=new WindowInteropHelper(this).Handle; InitTray(); RefreshDevice(); if(ConfigStore.LoadWarning is not null) ShowStatus(ConfigStore.LoadWarning); RestoreEditorDraft(); };
        }
    }
    public void PreparePreview()
    {
        config.Bindings["N7"]=new(){Title="Discord",Kind=ActionKind.Application,Target="Discord.exe"};
        config.Bindings["N8"]=new(){Title="YouTube",Kind=ActionKind.Website,Target="https://youtube.com"};
        config.Bindings["Add"]=new(){Title="Ses artır",Kind=ActionKind.Media,Target="volume_up"};
        config.Bindings["Enter"]=new(){Title="Oynat / duraklat",Kind=ActionKind.Media,Target="play_pause"};
        RefreshPad();
    }
    void BuildPad()
    {
        foreach(var key in PadLayout.Keys) {
            var button=new Button { Margin=new Thickness(4),Padding=new Thickness(10,7,9,7),Tag=key,HorizontalContentAlignment=HorizontalAlignment.Stretch };
            button.Click+=(_,_)=> { if(CheckDraft()) SelectKey(key); };
            Grid.SetRow(button,key.Row); Grid.SetColumn(button,key.Column); Grid.SetRowSpan(button,key.RowSpan); Grid.SetColumnSpan(button,key.ColumnSpan);
            PadGrid.Children.Add(button); padButtons[key.Id]=button;
        }
        RefreshPad();
    }
    void RefreshPad()
    {
        foreach(var key in PadLayout.Keys) {
            config.Bindings.TryGetValue(key.Id,out var action); var assigned=action is not null && action.Kind!=ActionKind.None;
            var isSelected=key.Id==selected.Id; var button=padButtons[key.Id];
            button.SetResourceReference(BackgroundProperty,isSelected?"KeySelected":assigned?"KeyAssigned":"Key");
            button.SetResourceReference(BorderBrushProperty,isSelected?"Accent":"GlassEdge");
            button.BorderThickness=new Thickness(isSelected?2:1);
            var stack=new Grid(); stack.RowDefinitions.Add(new(){Height=GridLength.Auto}); stack.RowDefinitions.Add(new()); stack.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var label=new TextBlock { Text=key.Label,FontSize=13,FontWeight=FontWeights.SemiBold }; ThemeManager.Bind(label,TextBlock.ForegroundProperty,isSelected?"Accent":"Muted"); stack.Children.Add(label);
            var icon=new TextBlock { Text=assigned?action!.Glyph:"·",FontSize=assigned?24:24,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center }; ThemeManager.Bind(icon,TextBlock.ForegroundProperty,assigned?"Accent":"Muted"); Grid.SetRow(icon,1); stack.Children.Add(icon);
            var name=new TextBlock { Text=assigned?action!.Title:"",FontSize=12,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,MaxHeight=35 }; ThemeManager.Bind(name,TextBlock.ForegroundProperty,"Text"); Grid.SetRow(name,2); stack.Children.Add(name);
            button.Content=stack;
        }
        AssignedCount.Text=$"{config.Bindings.Values.Count(a=>a.Kind!=ActionKind.None)} / 18";
    }
    internal static SolidColorBrush Brush(string color)=>new((Color)ColorConverter.ConvertFromString(color));
    void SelectKey(PadKey key)
    {
        config.Bindings.TryGetValue(key.Id,out var action); LoadEditor(key,action?.Clone()??new());
    }
    void LoadEditor(PadKey key,PadAction action)
    {
        loading=true; selected=key; draftTimer?.Stop();
        SelectedTitle.Text=key.Label+" tuşu"; SelectedBadge.Text=key.Label;
        TitleBox.Text=action.Title; KindBox.SelectedIndex=(int)action.Kind;
        TargetBox.Text=action.Target; ArgumentsBox.Text=action.Arguments; WorkingBox.Text=action.WorkingDirectory;
        TextContentBox.Text=action.Kind==ActionKind.Text?action.Target:"";
        MediaBox.SelectedIndex=Array.IndexOf(MediaTargets,action.Target); if(MediaBox.SelectedIndex<0) MediaBox.SelectedIndex=0;
        draftSteps=action.Steps??new(); RefreshSteps(); RefreshPanels(); loading=false; dirty=false; RefreshPad();
    }
    static readonly string[] MediaTargets={"volume_up","volume_down","mute","play_pause","next","previous"};
    void RefreshSteps() { StepList.ItemsSource=null; StepList.ItemsSource=draftSteps; StepCount.Text=$"{draftSteps.Count} adım"; }
    void RefreshPanels()
    {
        var kind=(ActionKind)Math.Max(KindBox.SelectedIndex,0);
        TargetPanel.Visibility=kind is ActionKind.Application or ActionKind.Website or ActionKind.Folder?Visibility.Visible:Visibility.Collapsed;
        ArgumentsPanel.Visibility=kind==ActionKind.Application?Visibility.Visible:Visibility.Collapsed;
        TextPanel.Visibility=kind==ActionKind.Text?Visibility.Visible:Visibility.Collapsed;
        MediaPanel.Visibility=kind==ActionKind.Media?Visibility.Visible:Visibility.Collapsed;
        MacroPanel.Visibility=kind==ActionKind.Macro?Visibility.Visible:Visibility.Collapsed;
        BrowseButton.Visibility=kind is ActionKind.Application or ActionKind.Folder?Visibility.Visible:Visibility.Collapsed;
        TargetLabel.Text=kind==ActionKind.Website?"SİTE ADRESİ":kind==ActionKind.Folder?"KLASÖR YOLU":"UYGULAMA VEYA KISAYOL YOLU";
        EditorHint.Text=kind switch { ActionKind.Macro=>"Kısayol, o anda kullandığın uygulamaya gönderilir.",ActionKind.Website=>"Site, varsayılan tarayıcında açılır.",ActionKind.Application=>"Program dosyası (.exe) veya masaüstü kısayolu (.lnk) seçebilirsin.",ActionKind.None=>"Bir görev türü seçerek bu tuşu özelleştir.",_=>"Kaydettiğin görev, numpad etkin olduğunda çalışır." };
    }
    void Kind_Changed(object sender,SelectionChangedEventArgs e) { if(MacroPanel is null) return; RefreshPanels(); MarkDirty(); }
    void Media_Changed(object sender,SelectionChangedEventArgs e)=>MarkDirty();
    void Editor_Changed(object sender,TextChangedEventArgs e)=>MarkDirty();
    void MarkDirty() { if(!loading) { dirty=true; if(StatusText is not null) StatusText.Text="Kaydedilmemiş değişiklikler var. Taslak otomatik korunur."; draftTimer?.Stop(); draftTimer?.Start(); } }
    void PersistEditorDraft()
    {
        if(preview || !dirty) return;
        try { ConfigStore.SaveDraft(selected.Id,ReadDraft()); }
        catch(Exception ex) { ShowStatus("Düzenleme taslağı kaydedilemedi: "+ex.Message); }
    }
    void ClearEditorDraft()
    {
        draftTimer?.Stop();
        if(preview) return;
        try { ConfigStore.ClearDraft(); } catch(Exception ex) { ShowStatus("Taslak kaydı temizlenemedi: "+ex.Message); }
    }
    void RestoreEditorDraft()
    {
        if(preview) return;
        try {
            var draft=ConfigStore.LoadDraft(); if(draft?.Action is null || !Enum.IsDefined(draft.Action.Kind)) return;
            if(draft.IsOlderThanReset(config)) { ClearEditorDraft(); return; }
            if(config.Bindings.TryGetValue(draft.KeyId,out var saved) && System.Text.Json.JsonSerializer.Serialize(saved)==System.Text.Json.JsonSerializer.Serialize(draft.Action)) { ClearEditorDraft(); return; }
            LoadEditor(PadLayout.Keys.First(k=>k.Id==draft.KeyId),draft.Action); dirty=true;
            ShowStatus("Kaydedilmemiş düzenlemen geri yüklendi. Tamamladığında Kaydet'e bas.");
        } catch(Exception ex) { ShowStatus("Taslak okunamadı: "+ex.Message); }
    }
    bool CheckDraft()
    {
        if(!dirty) return true;
        var result=MessageBox.Show(this,"Bu tuştaki değişiklikleri kaydetmek ister misin?","Kaydedilmemiş değişiklikler",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        if(result==MessageBoxResult.Cancel) return false;
        if(result==MessageBoxResult.No) ClearEditorDraft();
        return result!=MessageBoxResult.Yes || SaveDraft();
    }
    PadAction ReadDraft()=>new() { Title=TitleBox.Text.Trim(),Kind=(ActionKind)Math.Max(KindBox.SelectedIndex,0),Target=(ActionKind)KindBox.SelectedIndex switch { ActionKind.Text=>TextContentBox.Text,ActionKind.Media=>MediaTargets[Math.Max(0,MediaBox.SelectedIndex)],_=>TargetBox.Text.Trim().Trim('"') },Arguments=ArgumentsBox.Text,WorkingDirectory=WorkingBox.Text.Trim().Trim('"'),Steps=draftSteps };
    bool SaveDraft(bool silent=false,bool activate=true)
    {
        try {
            var action=ReadDraft(); ActionValidator.Validate(action);
            var next=ConfigClone(); if(action.Kind==ActionKind.None) next.Bindings.Remove(selected.Id); else next.Bindings[selected.Id]=action.Clone();
            if(!preview) ConfigStore.Save(next); config=next; dirty=false; ClearEditorDraft(); RefreshPad(); ShowStatus(selected.Label+" tuşu kalıcı olarak kaydedildi."+(ConfigStore.SaveWarning is null?"":" "+ConfigStore.SaveWarning));
            if(activate) ActivateSavedDevice(); return true;
        } catch(Exception ex) { if(!silent) MessageBox.Show(this,ex.Message,"Görev kaydedilemedi",MessageBoxButton.OK,MessageBoxImage.Information); return false; }
    }
    void ActivateSavedDevice()
    {
        if(!preview && config.AutoActivate && !string.IsNullOrEmpty(config.DeviceHardwareId)) engine?.SelectSaved(config.DeviceHardwareId,activate:true);
    }
    AppConfig ConfigClone()=>System.Text.Json.JsonSerializer.Deserialize<AppConfig>(System.Text.Json.JsonSerializer.Serialize(config))!;
    bool TrySaveConfig() { try { if(!preview) ConfigStore.Save(config); return true; } catch(Exception ex) { ShowStatus("Ayarlar kaydedilemedi: "+ex.Message); return false; } }
    void Save_Click(object sender,RoutedEventArgs e)=>SaveDraft();
    void Clear_Click(object sender,RoutedEventArgs e)
    {
        var next=ConfigClone(); next.Bindings.Remove(selected.Id);
        try { if(!preview) ConfigStore.Save(next); config=next; ClearEditorDraft(); SelectKey(selected); ShowStatus("Tuşun görevi silindi."); ActivateSavedDevice(); } catch(Exception ex) { ShowStatus(ex.Message); }
    }
    void Macro_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new MacroWindow(draftSteps) { Owner=this };
        if(dialog.ShowDialog()==true) { draftSteps=dialog.Result; RefreshSteps(); MarkDirty(); }
    }
    void Browse_Click(object sender,RoutedEventArgs e)
    {
        if((ActionKind)KindBox.SelectedIndex==ActionKind.Folder) { var dialog=new OpenFolderDialog(); if(dialog.ShowDialog(this)==true) TargetBox.Text=dialog.FolderName; }
        else { var dialog=new OpenFileDialog { Filter="Uygulamalar ve kısayollar|*.exe;*.lnk;*.url|Tüm dosyalar|*.*" }; if(dialog.ShowDialog(this)==true) { TargetBox.Text=dialog.FileName; if(string.IsNullOrWhiteSpace(TitleBox.Text)) TitleBox.Text=System.IO.Path.GetFileNameWithoutExtension(dialog.FileName); } }
    }
    void PresetCopy_Click(object sender,RoutedEventArgs e) { KindBox.SelectedIndex=(int)ActionKind.Macro; TitleBox.Text="Kopyala"; draftSteps=AppConfig.Shortcut("Kopyala",0x11,0x43).Steps; RefreshSteps(); MarkDirty(); }
    void PresetApp_Click(object sender,RoutedEventArgs e) { KindBox.SelectedIndex=(int)ActionKind.Application; Browse_Click(sender,e); }
    void PresetWeb_Click(object sender,RoutedEventArgs e) { KindBox.SelectedIndex=(int)ActionKind.Website; TargetBox.Text="https://"; TargetBox.Focus(); TargetBox.CaretIndex=TargetBox.Text.Length; }
    async void Test_Click(object sender,RoutedEventArgs e)
    {
        if(preview) return;
        try { var action=ReadDraft().Clone(); ActionValidator.Validate(action); TestButton.IsEnabled=false;
            for(int i=3;i>0;i--) { ShowStatus($"{i} saniye… Görevin çalışacağı uygulamaya geç."); await Task.Delay(1000); if(exiting) return; }
            await runner.RunAsync(action,ownHandle);
        } catch(Exception ex) { ShowStatus(ex.Message); } finally { TestButton.IsEnabled=true; }
    }
    void OnPadPressed(PadKey key)
    {
        if(!IsEnabled || engine?.IsActive!=true) return;
        if(config.Bindings.TryGetValue(key.Id,out var action)) _=runner.RunAsync(action.Clone(),ownHandle);
        else ShowStatus(key.Label+" tuşuna henüz görev atanmadı.");
        var button=padButtons[key.Id]; button.Opacity=0.5;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(130) }; timer.Tick+=(_,_)=> { button.Opacity=1; timer.Stop(); }; timer.Start();
    }
    void Active_Click(object sender,RoutedEventArgs e)
    {
        if(engine is null) return;
        if(engine.IsActive) { runner.Cancel(); engine.SetActive(false); }
        else if(CheckDraft()) engine.SetActive(true);
    }
    void Pair_Click(object sender,RoutedEventArgs e)
    {
        if(engine?.IsPairing==true) engine.CancelPair();
        else if(engine?.DriverAvailable==true) {
            runner.Cancel(); engine.Pair();
            // Enter from the keypad must not click the still-focused pairing button again.
            PadGrid.Focusable=true; Keyboard.Focus(PadGrid);
        }
        else ShowSettings();
    }
    void RefreshDevice()
    {
        if(engine is null) return;
        ActiveButton.Content=engine.IsActive?"Ⅱ  Duraklat":"▷  Etkinleştir";
        ActiveButton.IsEnabled=engine.DriverAvailable && engine.Selected is not null && !engine.IsPairing;
        if(!engine.DriverAvailable) { DeviceTitle.Text="Bir kez cihaz kurulumu gerekli"; DeviceSubtitle.Text="Harici numpadi ana klavyeden ayırmak için cihaz desteğini kur."; PairButton.Content="Kurulumu aç →"; }
        else if(engine.IsPairing) { DeviceTitle.Text="Harici numpadde 0'a bir kez bas"; DeviceSubtitle.Text="Enter gerekmez. NumLock açık veya kapalı olabilir. 30 saniye içinde cihaz seçilecek."; PairButton.Content="Vazgeç"; }
        else if(engine.Selected is null) { DeviceTitle.Text="Numpadini tanıt"; DeviceSubtitle.Text="Cihazı tanıt'a tıkla, ardından yalnızca harici numpadde 0'a bir kez bas."; PairButton.Content="Cihazı tanıt →"; }
        else { DeviceTitle.Text=engine.IsActive?"●  Numpad etkin":"●  Numpad hazır"; DeviceSubtitle.Text="Ana klavyen normal çalışır. "+(engine.IsActive?"Atadığın görevleri kullanabilirsin.":config.AutoActivate?"Kaydettiğinde numpad otomatik etkinleşir.":"Görevlerini kaydet, ardından etkinleştir."); PairButton.Content="Yeniden tanıt"; }
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,engine.IsActive?"Mint":"Accent");
        if(tray is not null) tray.Text=engine.IsActive?"Pad Studio · Numpad etkin":"Pad Studio · Duraklatıldı";
    }
    void ShowStatus(string text) { if(Dispatcher.HasShutdownStarted) return; Dispatcher.BeginInvoke(()=>StatusText.Text=text); }
    void Settings_Click(object sender,RoutedEventArgs e)=>ShowSettings();
    void ShowSettings()
    {
        if(preview) return;
        runner.Cancel(); engine?.CancelPair(); engine?.SetActive(false);
        var dialog=new SettingsWindow(config,engine?.DriverAvailable==true) { Owner=this };
        if(dialog.ShowDialog()==true) {
            if(dialog.FactoryResetRequested) { ResetAllSettings(); return; }
            var next=ConfigClone(); next.CloseToTray=dialog.CloseToTray; next.StartWithWindows=dialog.StartWithWindows; next.AutoActivate=dialog.AutoActivate; next.ThemeId=dialog.SelectedThemeId;
            try { ConfigStore.Save(next); config=next; } catch(Exception ex) { ShowStatus("Ayarlar kaydedilemedi: "+ex.Message); }
        }
        ThemeManager.Apply(config.ThemeId);
        ActivateSavedDevice();
        RefreshDevice();
    }
    internal bool ResetAllSettings()
    {
        try {
            runner.Cancel();
            if(!preview) StartupRegistration.Set(false);
            var reset=preview?AppConfig.FactoryDefaults():ConfigStore.Reset();
            config=reset; ThemeManager.Apply(config.ThemeId); ClearEditorDraft(); engine?.ForgetDevice(); SelectKey(PadLayout.Keys.First(k=>k.Id=="N1")); RefreshDevice();
            ShowStatus("Fabrika ayarlarına dönüldü. Tüm tuşlar boşaltıldı; numpadini yeniden tanıtabilirsin."); return true;
        } catch(Exception ex) { ShowStatus("Sıfırlama tamamlanamadı: "+ex.Message); return false; }
    }
    void Export_Click(object sender,RoutedEventArgs e)
    {
        if(!CheckDraft()) return;
        var dialog=new SaveFileDialog { Filter="Pad Studio düzeni|*.json",FileName="numpad-duzenim.json" };
        if(dialog.ShowDialog(this)==true) { try { ConfigStore.Export(config,dialog.FileName); ShowStatus("Düzen dışa aktarıldı."); } catch(Exception ex) { ShowStatus(ex.Message); } }
    }
    internal void ImportConfig()
    {
        if(!CheckDraft()) return;
        var dialog=new OpenFileDialog { Filter="Pad Studio düzeni|*.json" };
        if(dialog.ShowDialog(this)!=true) return;
        try {
            var next=ConfigStore.Import(dialog.FileName); next.DeviceHardwareId=config.DeviceHardwareId; next.StartWithWindows=config.StartWithWindows; next.CloseToTray=config.CloseToTray; next.AutoActivate=config.AutoActivate; next.ThemeId=config.ThemeId;
            engine?.SetActive(false); runner.Cancel(); ConfigStore.Save(next); config=next; ClearEditorDraft(); SelectKey(selected); ActivateSavedDevice(); ShowStatus("Düzen içe aktarıldı ve kalıcı olarak kaydedildi.");
        } catch(Exception ex) { MessageBox.Show(this,ex.Message,"Düzen okunamadı",MessageBoxButton.OK,MessageBoxImage.Information); }
    }
    void InitTray()
    {
        tray=new Forms.NotifyIcon { Icon=App.CreateIcon(),Text="Pad Studio · Duraklatıldı",Visible=true };
        var menu=new Forms.ContextMenuStrip(); menu.Items.Add("Pad Studio'yu aç",null,(_,_)=>Restore()); menu.Items.Add("Duraklat",null,(_,_)=> { runner.Cancel(); engine?.SetActive(false); });
        menu.Items.Add("Çıkış",null,(_,_)=> { Restore(); if(CheckDraft()) { exiting=true; Close(); } }); tray.ContextMenuStrip=menu; tray.DoubleClick+=(_,_)=>Restore();
    }
    public void Restore() { Show(); WindowState=WindowState.Normal; Activate(); }
    internal AppConfig TestConfig=>config;
    internal void ForceExit()
    {
        if(!preview && dirty) { PersistEditorDraft(); SaveDraft(silent:true,activate:false); }
        exiting=true; Close();
    }
    void Window_Closing(object? sender,CancelEventArgs e)
    {
        if(!preview && !exiting && config.CloseToTray && tray is not null) { PersistEditorDraft(); e.Cancel=true; Hide(); return; }
        if(!preview && !exiting && !CheckDraft()) { e.Cancel=true; return; }
        exiting=true; draftTimer?.Stop(); runner.Dispose(); engine?.Dispose(); tray?.Dispose();
    }
}
