using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PadStudio;
internal static class PersistenceTests
{
    static void Assert(bool ok,string message="Persistence assertion failed") { if(!ok) throw new Exception(message); }
    static AppConfig Sample()=>new() { DeviceHardwareId="test-receiver",ThemeId="aurora",Bindings=new() {
        ["N4"]=new(){Title="Kalıcı site",Kind=ActionKind.Website,Target="https://example.com/saved"},
        ["N7"]=new(){Title="Kalıcı makro",Kind=ActionKind.Macro,Steps=new(){new(){Kind=StepKind.Chord,Keys=new(){0x11,0x43}},new(){Kind=StepKind.Delay,DelayMs=350},new(){Kind=StepKind.Chord,Keys=new(){0x11,0x56}}}}
    }};
    internal static void Run(string reportPath,Action<string,Action> check)
    {
        var root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!,"persistence-"+Guid.NewGuid().ToString("N"));
        ConfigRepository Repository(string name) { var dir=Path.Combine(root,name); return new(Path.Combine(dir,"profile","save.json"),Path.Combine(dir,"portable","save","numpad-save.json"),Path.Combine(dir,"profile","config.json")); }
        check("Saved macro and website survive a fresh repository instance",()=> {
            var repo=Repository("roundtrip"); var config=Sample(); repo.Save(config);
            var loaded=new ConfigRepository(repo.PrimaryPath,repo.MirrorPath,repo.LegacyPath).Load();
            Assert(loaded.Bindings["N4"].Target==config.Bindings["N4"].Target); Assert(loaded.Bindings["N7"].Steps[1].DelayMs==350); Assert(loaded.AutoActivate && loaded.DeviceHardwareId=="test-receiver");
            Assert(File.ReadAllText(repo.PrimaryPath)==File.ReadAllText(repo.MirrorPath)); Assert(loaded.SavedAtUtc!=default); Assert(loaded.ThemeId=="aurora");
        });
        check("Legacy config.json migrates without losing assignments or preferences",()=> {
            var repo=Repository("migration"); var config=Sample(); config.StartWithWindows=true; config.CloseToTray=false;
            var json=JsonSerializer.SerializeToNode(config)!.AsObject(); json.Remove("AutoActivate"); json.Remove("SavedAtUtc"); json.Remove("ThemeId"); Directory.CreateDirectory(Path.GetDirectoryName(repo.LegacyPath)!); File.WriteAllText(repo.LegacyPath,json.ToJsonString());
            var loaded=repo.Load(); Assert(loaded.Bindings.Count==2 && loaded.StartWithWindows && !loaded.CloseToTray && loaded.AutoActivate); Assert(loaded.ThemeId=="liquid-gray"); Assert(File.Exists(repo.PrimaryPath)&&File.Exists(repo.MirrorPath)); Assert(File.ReadAllText(repo.LegacyPath)==json.ToJsonString());
        });
        check("Unknown and missing themes fall back without discarding tasks",()=> {
            var repo=Repository("theme-fallback"); var config=Sample(); config.ThemeId="a-future-theme"; repo.Save(config);
            var loaded=repo.Load(); Assert(loaded.ThemeId=="liquid-gray" && loaded.Bindings.Count==2 && loaded.DeviceHardwareId=="test-receiver");
        });
        check("Corrupted primary file recovers the newest mirror and preserves raw data",()=> {
            var repo=Repository("mirror"); var config=Sample(); repo.Save(config); config.Bindings["N4"].Target="https://example.com/newest"; repo.Save(config); File.WriteAllText(repo.PrimaryPath,"{broken-primary");
            var loaded=repo.Load(); Assert(loaded.Bindings["N4"].Target.EndsWith("newest")); Assert(repo.LoadWarning is not null); Assert(JsonDocument.Parse(File.ReadAllText(repo.PrimaryPath)).RootElement.GetProperty("Bindings").GetProperty("N4").GetProperty("Target").GetString()!.EndsWith("newest"));
            Assert(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(repo.PrimaryPath)!,"backups")).Any(p=>File.ReadAllText(p)=="{broken-primary"));
        });
        check("Two corrupt current copies recover the previous valid save",()=> {
            var repo=Repository("backup"); var config=Sample(); repo.Save(config); config.Bindings["N4"].Target="https://example.com/second"; repo.Save(config); File.WriteAllText(repo.PrimaryPath,"broken"); File.WriteAllText(repo.MirrorPath,"broken");
            var loaded=repo.Load(); Assert(loaded.Bindings["N4"].Target=="https://example.com/saved" && loaded.Bindings.ContainsKey("N7"));
        });
        check("One invalid binding cannot discard other new bindings or restore deleted keys",()=> {
            var repo=Repository("partial"); var config=Sample(); config.Bindings["N2"]=AppConfig.Shortcut("Eski",0x43); repo.Save(config);
            config.Bindings.Remove("N2"); config.Bindings["N9"]=new(){Title="Yeni site",Kind=ActionKind.Website,Target="https://example.com/latest"}; repo.Save(config);
            var json=JsonNode.Parse(File.ReadAllText(repo.PrimaryPath))!; json["Bindings"]!["N7"]!["Steps"]=new JsonArray(); json["SavedAtUtc"]=DateTimeOffset.UtcNow.AddSeconds(1); File.WriteAllText(repo.PrimaryPath,json.ToJsonString()); File.WriteAllText(repo.MirrorPath,json.ToJsonString());
            var loaded=repo.Load(); Assert(loaded.Bindings.ContainsKey("N9") && loaded.Bindings["N7"].Steps.Count==3); Assert(!loaded.Bindings.ContainsKey("N2"));
        });
        check("Invalid edits cannot overwrite the last valid saved file",()=> {
            var repo=Repository("invalid"); var config=Sample(); repo.Save(config); var before=File.ReadAllBytes(repo.PrimaryPath); config.Bindings["N4"].Target="not-a-url";
            bool rejected=false; try { repo.Save(config); } catch(InvalidDataException) { rejected=true; }
            Assert(rejected && File.ReadAllBytes(repo.PrimaryPath).SequenceEqual(before));
        });
        check("An unavailable primary location still saves and reloads the mirror",()=> {
            var dir=Path.Combine(root,"unavailable"); Directory.CreateDirectory(dir); var blocked=Path.Combine(dir,"file-instead-of-directory"); File.WriteAllText(blocked,"blocked");
            var repo=new ConfigRepository(Path.Combine(blocked,"save.json"),Path.Combine(dir,"mirror","save.json"),Path.Combine(dir,"legacy.json")); repo.Save(Sample()); Assert(repo.SaveWarning is not null && repo.ActivePath==repo.MirrorPath); Assert(repo.Load().Bindings.Count==2);
        });
        check("Unreadable files remain untouched until an explicit save",()=> {
            var repo=Repository("unreadable"); Directory.CreateDirectory(Path.GetDirectoryName(repo.PrimaryPath)!); File.WriteAllText(repo.PrimaryPath,"recover-me"); repo.Load();
            Assert(File.ReadAllText(repo.PrimaryPath)=="recover-me" && repo.LoadWarning is not null); repo.Save(Sample());
            Assert(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(repo.PrimaryPath)!,"backups")).Any(p=>File.ReadAllText(p)=="recover-me"));
        });
        check("Factory reset clears every task and pairing, including after backup recovery",()=> {
            var repo=Repository("reset"); var config=Sample(); config.StartWithWindows=true; config.AutoActivate=false; config.CloseToTray=false; repo.Save(config); DurableJson.Write(repo.LegacyPath,config);
            var reset=repo.Reset(); Assert(reset.Bindings.Count==0 && reset.DeviceHardwareId=="" && !reset.StartWithWindows && reset.AutoActivate && reset.CloseToTray && reset.ThemeId=="liquid-gray");
            File.WriteAllText(repo.PrimaryPath,"broken"); File.WriteAllText(repo.MirrorPath,"broken"); var loaded=repo.Load(); Assert(loaded.Bindings.Count==0 && loaded.DeviceHardwareId=="");
            Assert(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(repo.PrimaryPath)!,"backups"),"before-reset-*").Length>0);
        });
        check("Incomplete editor drafts survive reopening and clear markers prevent resurrection",()=> {
            var dir=Path.Combine(root,"draft"); var first=Path.Combine(dir,"profile.json"); var second=Path.Combine(dir,"portable.json"); var drafts=new DraftRepository(first,second);
            drafts.Save("N8",new(){Title="",Kind=ActionKind.Website,Target="https://"}); var stale=File.ReadAllText(first); var reopened=new DraftRepository(first,second).Load(); Assert(reopened?.KeyId=="N8" && reopened.Action!.Target=="https://");
            drafts.Clear(); File.WriteAllText(first,stale); Assert(new DraftRepository(first,second).Load() is null);
        });
        check("Factory reset clears the visible editor and all sample bindings",()=> {
            var window=new MainWindow(true); Assert(window.TestConfig.Bindings.Count>0); Assert(window.ResetAllSettings()); Assert(window.TestConfig.Bindings.Count==0 && window.TestConfig.DeviceHardwareId=="");
            Assert(((System.Windows.Controls.TextBox)window.FindName("TitleBox")).Text=="");
        });
        check("A pre-reset draft cannot restore a cleared task after factory reset",()=> {
            var before=new EditorDraft { KeyId="N4",Action=Sample().Bindings["N4"],UpdatedAtUtc=DateTimeOffset.UtcNow.AddSeconds(-1) };
            var reset=AppConfig.FactoryDefaults(); Assert(before.IsOlderThanReset(reset));
            var after=new EditorDraft { KeyId="N4",Action=Sample().Bindings["N4"],UpdatedAtUtc=reset.ResetAtUtc.AddSeconds(1) }; Assert(!after.IsOlderThanReset(reset));
        });
        check("Save data survives two completely separate application processes",()=> {
            var dir=Path.Combine(root,"process-restart");
            foreach(var phase in new[]{"--persistence-write","--persistence-read"}) {
                var info=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false,CreateNoWindow=true }; info.ArgumentList.Add(phase); info.ArgumentList.Add(dir);
                using var process=Process.Start(info)??throw new Exception("Test process did not start");
                if(!process.WaitForExit(15000)) { process.Kill(); throw new Exception("Persistence child process timed out"); }
                Assert(process.ExitCode==0,"Fresh process persistence test failed: "+phase);
            }
        });
    }
    internal static int Cycle(string directory,bool write)
    {
        Directory.CreateDirectory(directory);
        var repo=new ConfigRepository(Path.Combine(directory,"profile","save.json"),Path.Combine(directory,"portable","save","numpad-save.json"),Path.Combine(directory,"profile","config.json"));
        try {
            if(write) repo.Save(Sample());
            else { var config=repo.Load(); Assert(config.Bindings["N4"].Target=="https://example.com/saved"); Assert(config.Bindings["N7"].Steps[1].DelayMs==350); Assert(config.AutoActivate); Assert(config.ThemeId=="aurora"); }
            File.WriteAllText(Path.Combine(directory,write?"write-result.txt":"read-result.txt"),"PASS"); return 0;
        } catch(Exception ex) { File.WriteAllText(Path.Combine(directory,"cycle-error.txt"),ex.ToString()); return 1; }
    }
}
