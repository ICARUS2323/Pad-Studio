using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PadStudio;

public readonly record struct KeyEvent(int Key, bool Up, bool Unicode=false);
public interface IKeySink { void Send(IReadOnlyList<KeyEvent> events); }
public sealed class WindowsKeySink : IKeySink
{
    public void Send(IReadOnlyList<KeyEvent> events)
    {
        var inputs=events.Select(e=>new Native.Input { Type=1, Keyboard=new Native.KeyboardInput { Vk=e.Unicode?(ushort)0:(ushort)e.Key, Scan=e.Unicode?(ushort)e.Key:(ushort)0, Flags=(e.Up?2u:0u)|(e.Unicode?4u:0u)|(!e.Unicode && IsExtended(e.Key)?1u:0u) } }).ToArray();
        if(inputs.Length>0 && Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.Input>())!=inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Tuşlar gönderilemedi. Hedef uygulama yönetici olarak çalışıyorsa Pad Studio da aynı yetkide olmalı.");
    }
    static bool IsExtended(int vk)=>vk is 0x5B or 0xA5 or >=0x21 and <=0x2E or >=0xAD and <=0xB3;
}
public static class InputPlans
{
    public static List<KeyEvent> Chord(IEnumerable<int> keys)
    {
        var ordered=KeyboardCatalog.Ordered(keys.Distinct()).ToArray();
        return ordered.Select(k=>new KeyEvent(k,false)).Concat(ordered.Reverse().Select(k=>new KeyEvent(k,true))).ToList();
    }
    public static List<KeyEvent> Text(string text)=>text.SelectMany(c=>new[]{new KeyEvent(c,false,true),new KeyEvent(c,true,true)}).ToList();
}
public sealed class ActionRunner : IDisposable
{
    readonly IKeySink sink;
    readonly SemaphoreSlim gate=new(1,1);
    CancellationTokenSource stop=new();
    public event Action<string>? Status;
    public ActionRunner(IKeySink? keySink=null)=>sink=keySink??new WindowsKeySink();
    public void Cancel() { stop.Cancel(); }
    public async Task RunAsync(PadAction action,IntPtr ownWindow)
    {
        if(!await gate.WaitAsync(0)) { Status?.Invoke("Önceki görev hâlâ çalışıyor."); return; }
        stop.Dispose(); stop=new(); var token=stop.Token;
        try {
            ActionValidator.Validate(action);
            var targetWindow=Native.GetForegroundWindow();
            if(action.Kind is ActionKind.Macro or ActionKind.Text or ActionKind.Media) {
                if(targetWindow==ownWindow) throw new InvalidOperationException("Kısayolu kullanacağın uygulamaya geç. Deneme için 3 saniyelik geri sayımı kullan.");
                var deadline=DateTime.UtcNow.AddSeconds(2);
                while(new[]{0x10,0x11,0x12,0x5B,0x5C}.Any(k=>(Native.GetAsyncKeyState(k)&0x8000)!=0)) {
                    token.ThrowIfCancellationRequested(); if(DateTime.UtcNow>deadline) throw new InvalidOperationException("Klavye kısayolunu göndermek için basılı Ctrl, Alt, Shift veya Win tuşunu bırak.");
                    await Task.Delay(20,token);
                }
            }
            void EnsureTarget() { token.ThrowIfCancellationRequested(); if(Native.GetForegroundWindow()!=targetWindow) throw new OperationCanceledException("Odak değiştiği için makro durduruldu."); }
            if(action.Kind==ActionKind.Macro) {
                foreach(var step in action.Steps) {
                    EnsureTarget();
                    if(step.Kind==StepKind.Delay) await Task.Delay(step.DelayMs,token);
                    else if(step.Kind==StepKind.Text) SendText(step.Text,EnsureTarget);
                    else { SendChord(step.Keys); await Task.Delay(45,token); }
                }
            }
            else if(action.Kind==ActionKind.Text) { EnsureTarget(); SendText(action.Target,EnsureTarget); }
            else if(action.Kind==ActionKind.Media) { EnsureTarget(); SendChord(new[]{action.Target switch { "volume_up"=>0xAF,"volume_down"=>0xAE,"mute"=>0xAD,"play_pause"=>0xB3,"next"=>0xB0,_=>0xB1 }}); }
            else if(action.Kind==ActionKind.Application) {
                token.ThrowIfCancellationRequested();
                var info=new ProcessStartInfo(Environment.ExpandEnvironmentVariables(action.Target)) { UseShellExecute=true, Arguments=action.Arguments };
                if(!string.IsNullOrWhiteSpace(action.WorkingDirectory)) info.WorkingDirectory=Environment.ExpandEnvironmentVariables(action.WorkingDirectory);
                Process.Start(info);
            }
            else if(action.Kind is ActionKind.Website or ActionKind.Folder) { token.ThrowIfCancellationRequested(); Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(action.Target)) { UseShellExecute=true }); }
            Status?.Invoke(action.Title+" çalıştı.");
        }
        catch(OperationCanceledException) { Status?.Invoke("Görev durduruldu."); }
        catch(Exception ex) { Status?.Invoke(ex.Message); }
        finally { gate.Release(); }
    }
    void SendChord(IEnumerable<int> keys)
    {
        var events=InputPlans.Chord(keys);
        try { sink.Send(events); }
        catch { try { sink.Send(events.Where(e=>e.Up).ToArray()); } catch { } throw; }
    }
    void SendText(string text,Action ensureTarget)
    {
        foreach(var chunk in text.Chunk(64)) { ensureTarget(); sink.Send(InputPlans.Text(new string(chunk))); }
    }
    public void Dispose() { Cancel(); }
}
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardInput { public ushort Vk,Scan; public uint Flags,Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit,Size=40)] internal struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,Input[] inputs,int size);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
}
