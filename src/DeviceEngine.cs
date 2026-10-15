using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PadStudio;

public record DeviceInfo(int Id,string HardwareId)
{
    public string Label => $"USB klavye #{Id} · "+HardwareId.Split('\0')[0];
}
public readonly record struct RoutedStroke(int Device,int Scan,bool Extended,bool Up);
public static class KeypadPairing
{
    // Some receivers emit a regular number-row 0. Enter and NumLock helper events
    // are not a reliable second handshake; identify the source of the explicit 0 press.
    public static bool IsIdentificationPress(RoutedStroke stroke) =>
        stroke.Device is >=1 and <=10 && !stroke.Up && !stroke.Extended && stroke.Scan is 0x52 or 0x0B;
}
public sealed class StrokeRouter
{
    readonly HashSet<(int,bool)> held=new();
    public int SelectedDevice { get; set; }
    public bool Active { get; set; }
    public void Reset()=>held.Clear();
    // Passthrough and key-repeat decisions are independent of UI and driver I/O.
    public (bool Consume,PadKey? Trigger) Route(RoutedStroke e)
    {
        if(!Active || SelectedDevice==0 || e.Device!=SelectedDevice) return(false,null);
        var key=(e.Scan,e.Extended);
        if(e.Up) { held.Remove(key); return(true,null); }
        if(!held.Add(key)) return(true,null);
        return(true,PadLayout.FromScan(e.Scan,e.Extended));
    }
}
public sealed class DeviceEngine : IDisposable
{
    readonly ConcurrentQueue<Action> commands=new();
    readonly StrokeRouter router=new();
    readonly Thread thread;
    readonly Interception.Predicate allKeyboards = d=>d is >=1 and <=10?1:0;
    IntPtr context;
    volatile bool quit;
    volatile bool learning;
    DateTime learnUntil;
    string selectedHardware="";
    DateTime nextCheck;
    public bool DriverAvailable { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPairing => learning;
    public DeviceInfo? Selected { get; private set; }
    public event Action<string>? Status;
    public event Action? Changed;
    public event Action<DeviceInfo>? Paired;
    public event Action<PadKey>? Pressed;
    public DeviceEngine() { thread=new Thread(Loop) { IsBackground=true,Name="PadStudio input" }; thread.Start(); }
    public void Pair()=>commands.Enqueue(()=> {
        if(context==IntPtr.Zero) { Status?.Invoke("Cihaza özel kullanım için önce sürücü kurulmalı."); return; }
        SetActiveCore(false); learning=true; learnUntil=DateTime.UtcNow.AddSeconds(30);
        Interception.SetFilter(context,allKeyboards,Interception.AllKeyboardEvents);
        Status?.Invoke("Yalnızca harici numpadde 0'a bir kez bas. Enter'a basmana gerek yok; NumLock açık veya kapalı olabilir."); Changed?.Invoke();
    });
    public void CancelPair()=>commands.Enqueue(()=> { if(learning) { learning=false; ClearFilters(); Status?.Invoke("Cihaz tanıtma iptal edildi."); Changed?.Invoke(); } });
    public void SelectSaved(string hardware,bool activate=false)=>commands.Enqueue(()=> {
        if(context==IntPtr.Zero || string.IsNullOrEmpty(hardware)) return;
        var matches=Devices().Where(d=>d.HardwareId==hardware).ToArray();
        if(matches.Length==1) { Selected=matches[0]; selectedHardware=hardware; if(activate) SetActiveCore(true); else Changed?.Invoke(); }
        else Status?.Invoke("Kaydedilmiş numpad bulunamadı veya birden fazla eşleşme var. Cihazı yeniden tanıt.");
    });
    public void ForgetDevice()=>commands.Enqueue(()=> { SetActiveCore(false); Selected=null; selectedHardware=""; Changed?.Invoke(); });
    public void SetActive(bool active)=>commands.Enqueue(()=>SetActiveCore(active));
    void SetActiveCore(bool active)
    {
        learning=false; ClearFilters(); router.Reset(); IsActive=router.Active=false;
        if(active) {
            if(Selected is null || GetHardwareId(Selected.Id)!=selectedHardware) { IsActive=false; router.Active=false; Status?.Invoke("Numpad bulunamadı. Cihazı yeniden tanıt."); Changed?.Invoke(); return; }
            var id=Selected.Id; Interception.Predicate selected=d=>d==id?1:0;
            // v1.0.1 get_filter returns 0 on this driver even after successful writes.
            // Follow the public API's set_filter contract; do not reject its writes
            // using that unreliable readback. Hardware delivery is tested separately.
            Interception.SetFilter(context,selected,Interception.AllKeyboardEvents);
            router.SelectedDevice=id;
        }
        IsActive=router.Active=active; Changed?.Invoke(); Status?.Invoke(active?"Numpad etkin. Ana klavye normal çalışır.":"Numpad duraklatıldı.");
    }
    void ClearFilters() { if(context!=IntPtr.Zero) Interception.SetFilter(context,allKeyboards,0); }
    string GetHardwareId(int id)
    {
        var buffer=new byte[2048]; var bytes=Interception.GetHardwareId(context,id,buffer,(uint)buffer.Length);
        return bytes==0?"":Encoding.Unicode.GetString(buffer,0,(int)Math.Min(bytes,buffer.Length)).TrimEnd('\0');
    }
    List<DeviceInfo> Devices()
    {
        var devices=new List<DeviceInfo>(); for(var id=1;id<=10;id++) { var hw=GetHardwareId(id); if(hw.Length>0) devices.Add(new(id,hw)); } return devices;
    }
    void Loop()
    {
        try {
            context=Interception.CreateContext(); DriverAvailable=context!=IntPtr.Zero; Changed?.Invoke();
            while(!quit) {
                while(commands.TryDequeue(out var command)) command();
                if(context==IntPtr.Zero) { Thread.Sleep(40); continue; }
                if(learning && DateTime.UtcNow>learnUntil) { learning=false; ClearFilters(); Status?.Invoke("Süre doldu. Cihazı tanıt'a basıp yeniden dene."); Changed?.Invoke(); }
                if(DateTime.UtcNow>nextCheck) {
                    nextCheck=DateTime.UtcNow.AddSeconds(2);
                    if(Selected is not null && GetHardwareId(Selected.Id)!=selectedHardware) { SetActiveCore(false); Selected=null; selectedHardware=""; Status?.Invoke("Numpad bağlantısı değişti. Yeniden tanıtman gerekiyor."); Changed?.Invoke(); }
                }
                var device=Interception.Wait(context,40); if(device==0) continue;
                if(Interception.Receive(context,device,out var stroke,1)!=1) continue;
                bool up=(stroke.State&1)!=0, extended=(stroke.State&2)!=0;
                if(learning) {
                    // Forward before identification so pairing never blocks a keyboard.
                    Interception.Send(context,device,ref stroke,1);
                    if(KeypadPairing.IsIdentificationPress(new(device,stroke.Code,extended,up))) {
                        learning=false; ClearFilters(); var hw=GetHardwareId(device);
                        if(hw.Length==0) { Status?.Invoke("Cihaz kimliği okunamadı. Yeniden dene."); continue; }
                        Selected=new(device,hw); selectedHardware=hw; Paired?.Invoke(Selected); Changed?.Invoke(); Status?.Invoke("Numpad tanıtıldı. Görevlerini ayarlayıp etkinleştirebilirsin.");
                    }
                    continue;
                }
                var result=router.Route(new(device,stroke.Code,extended,up));
                if(!result.Consume) Interception.Send(context,device,ref stroke,1);
                if(result.Trigger is not null) Pressed?.Invoke(result.Trigger);
            }
        }
        catch(Exception ex) { IsActive=false; DriverAvailable=false; Status?.Invoke("Cihaz desteği kullanılamıyor: "+ex.Message); Changed?.Invoke(); }
        finally { ClearFilters(); if(context!=IntPtr.Zero) { Interception.DestroyContext(context); context=IntPtr.Zero; } }
    }
    public void Dispose() { quit=true; thread.Join(1500); }
}
internal static class Interception
{
    // Includes E0/E1 events for keypad Enter and Divide, as defined by interception.h.
    internal const ushort AllKeyboardEvents=ushort.MaxValue;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int Predicate(int device);
    [StructLayout(LayoutKind.Sequential)] internal struct Stroke { public ushort Code,State; public uint Information; }
    [DllImport("interception.dll",EntryPoint="interception_create_context",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr CreateContext();
    [DllImport("interception.dll",EntryPoint="interception_destroy_context",CallingConvention=CallingConvention.Cdecl)] internal static extern void DestroyContext(IntPtr context);
    [DllImport("interception.dll",EntryPoint="interception_set_filter",CallingConvention=CallingConvention.Cdecl)] internal static extern void SetFilter(IntPtr context,Predicate predicate,ushort filter);
    [DllImport("interception.dll",EntryPoint="interception_get_filter",CallingConvention=CallingConvention.Cdecl)] internal static extern ushort GetFilter(IntPtr context,int device);
    [DllImport("interception.dll",EntryPoint="interception_wait_with_timeout",CallingConvention=CallingConvention.Cdecl)] internal static extern int Wait(IntPtr context,uint milliseconds);
    [DllImport("interception.dll",EntryPoint="interception_receive",CallingConvention=CallingConvention.Cdecl)] internal static extern int Receive(IntPtr context,int device,out Stroke stroke,uint count);
    [DllImport("interception.dll",EntryPoint="interception_send",CallingConvention=CallingConvention.Cdecl)] internal static extern int Send(IntPtr context,int device,ref Stroke stroke,uint count);
    [DllImport("interception.dll",EntryPoint="interception_get_hardware_id",CallingConvention=CallingConvention.Cdecl)] internal static extern uint GetHardwareId(IntPtr context,int device,byte[] buffer,uint size);
}
