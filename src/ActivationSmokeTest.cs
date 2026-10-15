using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace PadStudio;
// Explicit diagnostic command; never runs during normal startup or unit tests.
internal static class ActivationSmokeTest
{
    internal static int Run(string path)
    {
        var report=new StringBuilder(); int exit=1;
        try {
            var hardware=ConfigStore.Load().DeviceHardwareId;
            if(string.IsNullOrWhiteSpace(hardware)) throw new InvalidOperationException("No saved receiver to test.");
            using var changed=new AutoResetEvent(false);
            using var engine=new DeviceEngine();
            string lastStatus="";
            engine.Changed+=()=>changed.Set(); engine.Status+=status=>lastStatus=status;
            bool WaitFor(Func<bool> condition) { var until=DateTime.UtcNow.AddSeconds(4); while(!condition() && DateTime.UtcNow<until) changed.WaitOne(50); return condition(); }
            engine.SelectSaved(hardware,activate:true);
            if(!WaitFor(()=>engine.Selected is not null)) throw new InvalidOperationException("Saved receiver not selected: "+lastStatus);
            report.AppendLine("Saved receiver selected: True");
            bool enabled=WaitFor(()=>engine.IsActive); report.AppendLine("DeviceEngine activation: "+enabled);
            if(!enabled) throw new InvalidOperationException(lastStatus);
            engine.SetActive(false);
            bool paused=WaitFor(()=>!engine.IsActive); report.AppendLine("DeviceEngine pause: "+paused);
            if(!paused) throw new InvalidOperationException("Pause did not complete.");
            report.AppendLine("No assigned actions executed. Test context closed and its filters removed."); exit=0;
        }
        catch(Exception ex) { report.AppendLine("FAIL: "+ex.Message); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path,report.ToString()); return exit;
    }
}
