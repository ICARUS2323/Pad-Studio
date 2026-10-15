using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PadStudio;
public partial class App : System.Windows.Application
{
    Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Initialize();
        if(e.Args.Contains("--persistence-write")) { Shutdown(PersistenceTests.Cycle(ArgAfter(e.Args,"--persistence-write")!,write:true)); return; }
        if(e.Args.Contains("--persistence-read")) { Shutdown(PersistenceTests.Cycle(ArgAfter(e.Args,"--persistence-read")!,write:false)); return; }
        if(e.Args.Contains("--self-test")) { var path=ArgAfter(e.Args,"--self-test")??Path.Combine(AppContext.BaseDirectory,"self-test.txt"); Shutdown(SelfTests.Run(path)); return; }
        if(e.Args.Contains("--render")) { try { RenderPreviews(ArgAfter(e.Args,"--render")??AppContext.BaseDirectory); Shutdown(0); } catch(Exception ex) { File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"render-error.txt"),ex.ToString()); Shutdown(1); } return; }
        if(e.Args.Contains("--diagnostics")) { var path=ArgAfter(e.Args,"--diagnostics")??Path.Combine(AppContext.BaseDirectory,"diagnostics.txt"); File.WriteAllText(path,Diagnostics.Read()); Shutdown(); return; }
        if(e.Args.Contains("--activation-test")) { Shutdown(ActivationSmokeTest.Run(ArgAfter(e.Args,"--activation-test")??Path.Combine(AppContext.BaseDirectory,"activation-test.txt"))); return; }
        if(e.Args.Contains("--driver-install")||e.Args.Contains("--driver-uninstall")) { Shutdown(DriverCommand(e.Args.Contains("--driver-install"))); return; }
        singleInstance=new Mutex(true,"Local\\PadStudio-User-"+Environment.UserName,out bool created);
        if(!created) { MessageBox.Show("Pad Studio zaten çalışıyor. Sistem tepsisindeki P simgesine çift tıkla.","Pad Studio"); Shutdown(); return; }
        DispatcherUnhandledException+=(_,args)=> {
            try { Directory.CreateDirectory(ConfigStore.DirectoryPath); File.AppendAllText(Path.Combine(ConfigStore.DirectoryPath,"error.log"),DateTime.Now+"\n"+args.Exception+"\n"); } catch { }
            MessageBox.Show(args.Exception.Message,"Pad Studio",MessageBoxButton.OK,MessageBoxImage.Error); args.Handled=true;
        };
        var window=new MainWindow(); MainWindow=window;
        SessionEnding+=(_,_)=>window.ForceExit();
        window.Show(); if(e.Args.Contains("--tray")) window.Hide();
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
    static string? ArgAfter(string[] args,string flag) { int i=Array.IndexOf(args,flag); return i>=0 && i+1<args.Length?args[i+1]:null; }
    static int DriverCommand(bool install)
    {
        try {
            var path=Path.Combine(AppContext.BaseDirectory,"driver","install-interception.exe");
            var info=new ProcessStartInfo(path,install?"/install":"/uninstall") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(path)! };
            using var process=Process.Start(info)??throw new InvalidOperationException("Sürücü kurucusu açılamadı.");
            var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync(); process.WaitForExit();
            Directory.CreateDirectory(ConfigStore.DirectoryPath);
            File.WriteAllText(Path.Combine(ConfigStore.DirectoryPath,"driver-setup.log"),$"{DateTime.Now:O}\nOperation: {(install?"install":"uninstall")}\nExit code: {process.ExitCode}\n"+output.GetAwaiter().GetResult()+"\n"+error.GetAwaiter().GetResult());
            return process.ExitCode;
        } catch(Exception ex) { try { Directory.CreateDirectory(ConfigStore.DirectoryPath); File.WriteAllText(Path.Combine(ConfigStore.DirectoryPath,"driver-setup.log"),ex.ToString()); } catch { } return 1; }
    }
    internal static System.Drawing.Icon CreateIcon()
    {
        using var stream=GetResourceStream(new Uri("pack://application:,,,/Assets/pad-studio.ico")).Stream;
        using var icon=new System.Drawing.Icon(stream,32,32); return (System.Drawing.Icon)icon.Clone();
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    static void RenderPreviews(string directory)
    {
        Directory.CreateDirectory(directory);
        ThemeManager.Apply("liquid-gray");
        var main=new MainWindow(true); Render(main,Path.Combine(directory,"pad-studio.png"),1300,840);
        main.PreparePreview(); Render(main,Path.Combine(directory,"pad-studio-example.png"),1300,840);
        foreach(var theme in ThemeManager.Themes) { ThemeManager.Apply(theme.Id); Render(main,Path.Combine(directory,"theme-"+theme.Id+".png"),1300,840); }
        ThemeManager.Apply("liquid-gray");
        var macro=new MacroWindow(new() { new(){Kind=StepKind.Chord,Keys=new(){0x11,0x43}},new(){Kind=StepKind.Delay,DelayMs=300},new(){Kind=StepKind.Chord,Keys=new(){0x11,0x56}} });
        macro.PreviewSelection(0x11,0x43); Render(macro,Path.Combine(directory,"macro-editor.png"),1090,800);
        var settings=new SettingsWindow(AppConfig.Defaults(),false);
        Render(settings,Path.Combine(directory,"settings.png"),680,800);
        ((System.Windows.Controls.ScrollViewer)settings.Content).ScrollToBottom(); ((FrameworkElement)settings.Content).UpdateLayout();
        Render(settings,Path.Combine(directory,"settings-reset.png"),680,800);
        Render(new SignalWindow(),Path.Combine(directory,"signal-test.png"),660,480);
    }
    static void Render(Window window,string file,int width,int height)
    {
        var root=(FrameworkElement)window.Content;
        root.Measure(new System.Windows.Size(width,height)); root.Arrange(new Rect(0,0,width,height)); root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual(); using(var drawing=background.RenderOpen()) drawing.DrawRectangle(window.Background,null,new Rect(0,0,width,height)); bitmap.Render(background); bitmap.Render(root);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream=File.Create(file); encoder.Save(stream);
    }
}
