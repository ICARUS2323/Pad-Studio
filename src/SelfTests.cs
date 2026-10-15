using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PadStudio;
internal static class SelfTests
{
    public static int Run(string reportPath)
    {
        var report=new StringBuilder(); int passed=0,failed=0;
        void Check(string name,Action check) { try { check(); report.AppendLine("PASS "+name); passed++; } catch(Exception ex) { report.AppendLine("FAIL "+name+": "+ex.Message); failed++; } }
        void Assert(bool condition,string message="Assertion failed") { if(!condition) throw new Exception(message); }
        void Reject(PadAction action) { try { ActionValidator.Validate(action); } catch(InvalidDataException) { return; } throw new Exception("Invalid action was accepted"); }
        Check("18 physical keys occupy all 20 grid cells without overlap",()=> {
            Assert(PadLayout.Keys.Length==18); var cells=new HashSet<(int,int)>(); foreach(var k in PadLayout.Keys) for(int r=k.Row;r<k.Row+k.RowSpan;r++) for(int c=k.Column;c<k.Column+k.ColumnSpan;c++) Assert(cells.Add((r,c))); Assert(cells.Count==20);
        });
        Check("Main keyboard is never consumed",()=> { var router=new StrokeRouter{Active=true,SelectedDevice=3}; var r=router.Route(new(1,0x4F,false,false)); Assert(!r.Consume && r.Trigger is null); });
        Check("Selected keypad consumes original digit",()=> { var router=new StrokeRouter{Active=true,SelectedDevice=3}; var r=router.Route(new(3,0x4F,false,false)); Assert(r.Consume && r.Trigger?.Id=="N1"); });
        Check("Held keys trigger once until release",()=> { var router=new StrokeRouter{Active=true,SelectedDevice=3}; Assert(router.Route(new(3,0x4F,false,false)).Trigger is not null); Assert(router.Route(new(3,0x4F,false,false)).Trigger is null); Assert(router.Route(new(3,0x4F,false,true)).Trigger is null); Assert(router.Route(new(3,0x4F,false,false)).Trigger is not null); });
        Check("Paused mode passes through all keys",()=> { var router=new StrokeRouter{Active=false,SelectedDevice=3}; Assert(!router.Route(new(3,0x4F,false,false)).Consume); });
        Check("Device zero cannot be consumed",()=> { var router=new StrokeRouter{Active=true}; Assert(!router.Route(new(0,0x4F,false,false)).Consume); });
        Check("One keypad zero press identifies its source without waiting for Enter",()=> {
            var trace=new[]{new RoutedStroke(5,0x45,false,false),new(5,0x45,false,true),new(5,0x52,false,false),new(5,0x52,false,true),new(5,0x45,false,false),new(5,0x45,false,true)};
            var found=trace.Where(KeypadPairing.IsIdentificationPress).ToArray(); Assert(found.Length==1 && found[0].Device==5);
        });
        Check("Pairing supports number-row zero but rejects helper events and releases",()=> {
            Assert(KeypadPairing.IsIdentificationPress(new(5,0x0B,false,false)));
            foreach(var stroke in new[]{new RoutedStroke(5,0x45,false,false),new(5,0x52,false,true),new(5,0x52,true,false),new(5,0x1C,true,false),new(5,0x4F,false,false),new(0,0x52,false,false),new(11,0x52,false,false)}) Assert(!KeypadPairing.IsIdentificationPress(stroke));
        });
        Check("Keypad Enter is different from regular Enter",()=> { Assert(PadLayout.FromScan(0x1C,true)?.Id=="Enter"); Assert(PadLayout.FromScan(0x1C,false) is null); });
        Check("Navigation arrows do not match numpad digits",()=> { Assert(PadLayout.FromScan(0x4F,true) is null); Assert(PadLayout.FromScan(0x4F,false)?.Id=="N1"); });
        Check("Wireless NumLock noise consumed without becoming another key",()=> { var router=new StrokeRouter{Active=true,SelectedDevice=3}; Assert(router.Route(new(3,0x2A,false,false)).Consume); Assert(router.Route(new(3,0x2A,false,false)).Trigger is null); });
        Check("Chord modifiers pressed first and released last",()=> { var events=InputPlans.Chord(new[]{0x43,0x11}); Assert(events.SequenceEqual(new[]{new KeyEvent(0x11,false),new KeyEvent(0x43,false),new KeyEvent(0x43,true),new KeyEvent(0x11,true)})); });
        Check("Multiple modifiers and duplicate keys handled",()=> { var events=InputPlans.Chord(new[]{0x43,0x11,0x10,0x43}); Assert(events.Count==6); Assert(events.Take(2).All(e=>KeyboardCatalog.IsModifier(e.Key))); Assert(events.Skip(3).All(e=>e.Up)); });
        Check("Unicode text produces paired events including Turkish and emoji",()=> { string text="İıŞğö 😊"; var events=InputPlans.Text(text); Assert(events.Count==text.Length*2); for(int i=0;i<text.Length;i++) Assert(events[i*2]==new KeyEvent(text[i],false,true)&&events[i*2+1]==new KeyEvent(text[i],true,true)); });
        Check("x64 SendInput structure matches ABI",()=>Assert(Marshal.SizeOf<Native.Input>()==40));
        Check("Interception key stroke matches ABI",()=>Assert(Marshal.SizeOf<Interception.Stroke>()==8));
        Check("Invalid URLs and executable URL schemes rejected",()=> { foreach(var url in new[]{"example.com","javascript:alert(1)","file:///c:/test"}) Reject(new(){Title="Site",Kind=ActionKind.Website,Target=url}); });
        Check("Delay bounds and unknown keys rejected",()=> { Reject(new(){Title="Bad",Kind=ActionKind.Macro,Steps=new(){new(){Kind=StepKind.Delay,DelayMs=60001}}}); Reject(new(){Title="Bad",Kind=ActionKind.Macro,Steps=new(){new(){Kind=StepKind.Chord,Keys=new(){999}}}}); });
        Check("Empty macro and invalid media rejected",()=> { Reject(new(){Title="Bad",Kind=ActionKind.Macro}); Reject(new(){Title="Bad",Kind=ActionKind.Media,Target="shutdown"}); });
        Check("Defaults validate and clones are independent",()=> { var c=AppConfig.Defaults(); ConfigStore.Validate(c); var clone=c.Bindings["N1"].Clone(); clone.Steps[0].Keys.Clear(); Assert(c.Bindings["N1"].Steps[0].Keys.Count==2); });
        Check("Configuration roundtrip preserves sequences and application arguments",()=> { var c=AppConfig.Defaults(); c.Bindings["N0"]=new(){Title="Discord",Kind=ActionKind.Application,Target="C:\\a b\\Update.exe",Arguments="--processStart Discord.exe"}; c.Bindings["N1"].Steps.Add(new(){Kind=StepKind.Delay,DelayMs=350}); var json=JsonSerializer.Serialize(c); var restored=JsonSerializer.Deserialize<AppConfig>(json)!; ConfigStore.Validate(restored); Assert(restored.Bindings["N0"].Arguments==c.Bindings["N0"].Arguments); Assert(restored.Bindings["N1"].Steps[1].DelayMs==350); });
        Check("Import rejects unknown physical keys",()=> { var c=AppConfig.Defaults(); c.Bindings["FictionalKey"]=AppConfig.Shortcut("bad",0x43); try { ConfigStore.Validate(c); } catch(InvalidDataException) { return; } throw new Exception("Unknown key accepted"); });
        Check("Visual keyboard adds a simultaneous chord through its actual button",()=> {
            var window=new MacroWindow(new()); window.PreviewSelection(0x11,0x43);
            var root=(FrameworkElement)window.Content; root.Measure(new Size(1090,800)); root.Arrange(new Rect(0,0,1090,800)); root.UpdateLayout();
            Button? FindButton(DependencyObject parent,string content) { for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); if(child is Button b && b.Content as string==content) return b; var nested=FindButton(child,content); if(nested is not null) return nested; } return null; }
            var add=FindButton(root,"+ Tuşları ekle")??throw new Exception("Add key button missing"); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Result.Count==1 && window.Result[0].Keys.SequenceEqual(new[]{0x11,0x43}));
            Assert(((TextBlock)window.FindName("SelectedKeysText")).Text=="Klavyeden tuş seç…","Selection was not cleared after adding a chord");
            ((ComboBox)window.FindName("ModeBox")).SelectedIndex=1; window.PreviewSelection(0x41,0x42,0x43); add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(window.Result.Count==4 && window.Result.Skip(1).SelectMany(s=>s.Keys).SequenceEqual(new[]{0x41,0x42,0x43}));
        });
        Check("Main editor saves and clears a task through actual UI buttons",()=> {
            var window=new MainWindow(true); var root=(FrameworkElement)window.Content; root.Measure(new Size(1300,840)); root.Arrange(new Rect(0,0,1300,840)); root.UpdateLayout();
            Assert(window.Background is LinearGradientBrush,"Main window glass backdrop is missing");
            ((ComboBox)window.FindName("KindBox")).SelectedIndex=(int)ActionKind.Website; ((TextBox)window.FindName("TitleBox")).Text="Örnek site"; ((TextBox)window.FindName("TargetBox")).Text="https://example.com";
            Button? FindButton(DependencyObject parent,string content) { for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); if(child is Button b && b.Content as string==content) return b; var nested=FindButton(child,content); if(nested is not null) return nested; } return null; }
            FindButton(root,"Kaydet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(window.TestConfig.Bindings["N1"].Target=="https://example.com"); Assert(window.TestConfig.Bindings["N1"].Kind==ActionKind.Website);
            FindButton(root,"Sil")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert(!window.TestConfig.Bindings.ContainsKey("N1"));
        });
        Check("Import/export roundtrip on a real temporary file",()=> {
            var path=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"test-roundtrip.json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try { ConfigStore.Export(AppConfig.Defaults(),path); var restored=ConfigStore.Import(path); Assert(restored.Bindings["N2"].Steps[0].Keys.SequenceEqual(new[]{0x11,0x56})); } finally { if(File.Exists(path)) File.Delete(path); }
        });
        Check("Live theme switching updates existing controls without changing assignments",()=> {
            var window=new MainWindow(true); var before=JsonSerializer.Serialize(window.TestConfig.Bindings);
            var text=(TextBox)window.FindName("TitleBox");
            foreach(var theme in ThemeManager.Themes) {
                ThemeManager.Apply(theme.Id); window.UpdateLayout();
                Assert(((SolidColorBrush)text.Foreground).Color==ThemeManager.Brush(theme.Ink).Color,"Editor text did not follow the theme");
                Assert(((LinearGradientBrush)window.Background).GradientStops[0].Color==ThemeManager.Brush(theme.Top).Color);
                Assert(JsonSerializer.Serialize(window.TestConfig.Bindings)==before,"Theme switching changed a task");
            }
            ThemeManager.Apply("liquid-gray"); window.Close();
        });
        Check("Closing unsaved theme settings restores the previous appearance",()=> {
            ThemeManager.Apply("night-glass"); var settings=new SettingsWindow(new(){ThemeId="night-glass"},true);
            var root=(FrameworkElement)settings.Content; root.Measure(new Size(680,800)); root.Arrange(new Rect(0,0,680,800)); root.UpdateLayout();
            Button? FindTheme(DependencyObject parent) { for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i); if(child is Button b && b.Tag as string=="aurora") return b; var nested=FindTheme(child); if(nested is not null) return nested; } return null; }
            var button=FindTheme(root)??throw new Exception("Theme card missing"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert(ThemeManager.Current.Id=="aurora" && settings.SelectedThemeId=="aurora"); settings.Close();
            Assert(ThemeManager.Current.Id=="night-glass"); ThemeManager.Apply("liquid-gray");
        });
        Check("Embedded app icon contains seven sizes and is available to windows and tray",()=> {
            using var stream=Application.GetResourceStream(new Uri("pack://application:,,,/Assets/pad-studio.ico")).Stream;
            var decoder=System.Windows.Media.Imaging.BitmapDecoder.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            Assert(decoder.Frames.Select(f=>f.PixelWidth).OrderBy(x=>x).SequenceEqual(new[]{16,24,32,48,64,128,256}));
            using var trayIcon=App.CreateIcon(); Assert(trayIcon.Width==32); Assert(new MainWindow(true).Icon is not null);
        });
        PersistenceTests.Run(reportPath,Check);
        report.AppendLine($"\n{passed} passed, {failed} failed. No real keystrokes sent; no driver installed or filters changed.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!); File.WriteAllText(reportPath,report.ToString()); return failed==0?0:1;
    }
}
