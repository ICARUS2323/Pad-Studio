using System;
using System.Runtime.InteropServices;
using System.Text;

namespace PadStudio;
internal static class Diagnostics
{
    [StructLayout(LayoutKind.Sequential)] struct RawDevice { public IntPtr Handle; public uint Type; }
    [DllImport("user32.dll",SetLastError=true)] static extern uint GetRawInputDeviceList([Out] RawDevice[]? devices,ref uint count,uint size);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetRawInputDeviceInfo(IntPtr device,uint command,StringBuilder? data,ref uint size);
    public static string Read()
    {
        var result=new StringBuilder(); result.AppendLine("Pad Studio device diagnostics (no keys captured)"); result.AppendLine("OS: "+Environment.OSVersion); result.AppendLine("Runtime: "+Environment.Version);
        uint count=0,size=(uint)Marshal.SizeOf<RawDevice>();
        if(GetRawInputDeviceList(null,ref count,size)==uint.MaxValue) { result.AppendLine("Raw device list error: "+Marshal.GetLastWin32Error()); return result.ToString(); }
        var devices=new RawDevice[count]; var found=GetRawInputDeviceList(devices,ref count,size);
        if(found==uint.MaxValue) { result.AppendLine("Raw device enumeration error: "+Marshal.GetLastWin32Error()); return result.ToString(); }
        for(int i=0;i<found;i++) { if(devices[i].Type!=1) continue; uint chars=0; GetRawInputDeviceInfo(devices[i].Handle,0x20000007,null,ref chars); var name=new StringBuilder((int)chars+1); GetRawInputDeviceInfo(devices[i].Handle,0x20000007,name,ref chars); result.AppendLine("Keyboard: "+name); }
        try { var context=Interception.CreateContext(); result.AppendLine("Interception driver accessible: "+(context!=IntPtr.Zero)); if(context!=IntPtr.Zero) Interception.DestroyContext(context); } catch(Exception ex) { result.AppendLine("Interception: "+ex.Message); }
        return result.ToString();
    }
}
