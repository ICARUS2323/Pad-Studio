using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace PadStudio;
// Raw Input is used only while this explicit test dialog is open. It never blocks or persists input.
public sealed class SignalWindow : Window
{
    readonly TextBlock status=new(){Text="Bir kayıt düğmesine bas, sonra ilgili cihazın 0 tuşuna bas.",TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,16,0,0)};
    readonly TextBlock externalLabel=new(){Text="Harici numpad: henüz okunmadı",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,16,0,0)};
    readonly TextBlock mainLabel=new(){Text="Ana klavye: henüz okunmadı",TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,12,0,0)};
    HwndSource? source;
    IntPtr externalDevice,mainDevice;
    int capture;
    bool registered;
    public SignalWindow()
    {
        Style=(Style)FindResource(typeof(Window)); Title="Numpad bağlantı testi · Pad Studio"; Width=680; Height=520; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(28) }; Content=panel;
        panel.Children.Add(new TextBlock{Text="İki cihazı ayrı görelim.",FontSize=25,FontWeight=FontWeights.SemiBold});
        panel.Children.Add(new TextBlock{Text="Bu test için sürücü kurulumu gerekmez. Önce harici numpadi, sonra ana klavyeni kaydet. Yalnızca 0 tuşunu kullan.",TextWrapping=TextWrapping.Wrap,FontSize=13,Margin=new Thickness(0,12,0,20)});
        var row=new StackPanel{Orientation=Orientation.Horizontal}; var external=new Button{Content="1. Harici numpadi dinle",Margin=new Thickness(0,0,10,0)}; var main=new Button{Content="2. Ana klavyeyi dinle"};
        external.Click+=(_,_)=> { capture=1; status.Text="Şimdi yalnızca harici numpadde 0'a bas."; }; main.Click+=(_,_)=> { capture=2; status.Text="Şimdi ana klavyede 0'a bas (üst sıra veya numpad)."; };
        row.Children.Add(external); row.Children.Add(main); panel.Children.Add(row); panel.Children.Add(externalLabel); panel.Children.Add(mainLabel); panel.Children.Add(status);
        panel.Children.Add(new TextBlock{Text="Tuşların normal işlevi bu test sırasında devam eder. Test kapanınca dinleme biter; tuş geçmişi kaydedilmez.",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new Thickness(0,22,0,0)});
        var close=new Button{Content="Tamam",HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,24,0,0)}; close.Click+=(_,_)=>Close(); panel.Children.Add(close);
        SourceInitialized+=(_,_)=> {
            var handle=new WindowInteropHelper(this).Handle; source=HwndSource.FromHwnd(handle); source.AddHook(Hook);
            var devices=new[]{new RawRegistration{Page=1,Usage=6,Flags=0x100,Target=handle}};
            registered=RegisterRawInputDevices(devices,1,(uint)Marshal.SizeOf<RawRegistration>());
            if(!registered) status.Text="Sinyal testi başlatılamadı: "+new Win32Exception(Marshal.GetLastWin32Error()).Message;
        };
        Closed+=(_,_)=> { if(registered) RegisterRawInputDevices(new[]{new RawRegistration{Page=1,Usage=6,Flags=1}},1,(uint)Marshal.SizeOf<RawRegistration>()); source?.RemoveHook(Hook); };
    }
    IntPtr Hook(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(message!=0x00FF || capture==0) return IntPtr.Zero;
        uint size=0,headerSize=(uint)Marshal.SizeOf<RawHeader>();
        if(GetRawInputData(lParam,0x10000003,IntPtr.Zero,ref size,headerSize)==uint.MaxValue || size<headerSize+16) return IntPtr.Zero;
        var data=Marshal.AllocHGlobal((int)size);
        try {
            if(GetRawInputData(lParam,0x10000003,data,ref size,headerSize)==uint.MaxValue) return IntPtr.Zero;
            var header=Marshal.PtrToStructure<RawHeader>(data); if(header.Type!=1) return IntPtr.Zero;
            var keyboard=Marshal.PtrToStructure<RawKeyboard>(IntPtr.Add(data,(int)headerSize)); if((keyboard.Flags&1)!=0 || keyboard.VKey is not (0x30 or 0x60) && keyboard.MakeCode!=0x52) return IntPtr.Zero;
            uint chars=0; GetRawInputDeviceInfo(header.Device,0x20000007,null,ref chars); var name=new StringBuilder((int)chars+1); GetRawInputDeviceInfo(header.Device,0x20000007,name,ref chars);
            var shortName=name.ToString().Replace("\\\\?\\HID#","").Split('#')[0];
            if(capture==1) { externalDevice=header.Device; externalLabel.Text=$"Harici numpad: {shortName}\nTuş kodu: {keyboard.MakeCode:X2} · {(keyboard.Flags&2)!=0}"; }
            else { mainDevice=header.Device; mainLabel.Text="Ana klavye: "+shortName; }
            capture=0;
            status.Text=externalDevice!=IntPtr.Zero && mainDevice!=IntPtr.Zero?(externalDevice!=mainDevice?"✓ İki cihaz ayrı algılandı. Ana klavye ile numpad bağımsız eşlenebilir.":"İki kez aynı cihazdan sinyal geldi. İkinci kaydı ana klavyenden tekrar yap."):"Sinyal alındı. Diğer cihazı da kaydedebilirsin.";
        } finally { Marshal.FreeHGlobal(data); }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] struct RawRegistration { public ushort Page,Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)] struct RawHeader { public uint Type,Size; public IntPtr Device,WParam; }
    [StructLayout(LayoutKind.Sequential)] struct RawKeyboard { public ushort MakeCode,Flags,Reserved,VKey; public uint Message,Extra; }
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(RawRegistration[] devices,uint count,uint size);
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint headerSize);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,StringBuilder? data,ref uint size);
}

