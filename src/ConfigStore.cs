using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PadStudio;

internal static class DurableJson
{
    internal static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
    internal static void Write<T>(string path,T value,bool backup=true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp=path+".tmp";
        using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
            JsonSerializer.Serialize(stream,value,Options);
            stream.Flush(flushToDisk:true);
        }
        if(File.Exists(path)) File.Replace(temp,path,backup?path+".bak":null);
        else File.Move(temp,path);
    }
}

public sealed class ConfigRepository
{
    readonly object sync=new();
    public string PrimaryPath { get; }
    public string MirrorPath { get; }
    public string LegacyPath { get; }
    public string ActivePath { get; private set; }
    public string? LoadWarning { get; private set; }
    public string? SaveWarning { get; private set; }
    readonly HashSet<string> preserveBeforeWrite=new(StringComparer.OrdinalIgnoreCase);
    record Candidate(string Path,AppConfig Config,DateTimeOffset Stamp,List<string> InvalidKeys,int Priority);
    public ConfigRepository(string primaryPath,string mirrorPath,string legacyPath)
    {
        PrimaryPath=Path.GetFullPath(primaryPath); MirrorPath=Path.GetFullPath(mirrorPath); LegacyPath=Path.GetFullPath(legacyPath); ActivePath=PrimaryPath;
    }
    IEnumerable<string> ReadPaths()=>new[]{PrimaryPath,MirrorPath,LegacyPath,PrimaryPath+".bak",MirrorPath+".bak",LegacyPath+".bak"}.Distinct(StringComparer.OrdinalIgnoreCase);
    public AppConfig Load()
    {
        lock(sync) {
            LoadWarning=null; var candidates=new List<Candidate>(); int priority=0; bool anyFile=false;
            foreach(var path in ReadPaths()) {
                priority++;
                if(!File.Exists(path)) continue;
                anyFile=true;
                try {
                    var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path))??throw new InvalidDataException();
                    if(config.Version!=1 || config.Bindings is null) throw new InvalidDataException();
                    config.DeviceHardwareId??="";
                    config.ThemeId=ThemeManager.Normalize(config.ThemeId);
                    var invalid=new List<string>();
                    foreach(var binding in config.Bindings.ToArray()) {
                        try { if(!PadLayout.Keys.Any(k=>k.Id==binding.Key) || binding.Value is null) throw new InvalidDataException(); ActionValidator.Validate(binding.Value); }
                        catch { invalid.Add(binding.Key); config.Bindings.Remove(binding.Key); }
                    }
                    if(invalid.Count>0) preserveBeforeWrite.Add(path);
                    var stamp=config.SavedAtUtc==default?new DateTimeOffset(File.GetLastWriteTimeUtc(path),TimeSpan.Zero):config.SavedAtUtc;
                    candidates.Add(new(path,config,stamp,invalid,priority));
                }
                catch { preserveBeforeWrite.Add(path); }
            }
            var best=candidates.OrderByDescending(c=>c.Stamp).ThenBy(c=>c.InvalidKeys.Count>0?1:0).ThenBy(c=>c.Priority).FirstOrDefault();
            if(best is null) {
                if(anyFile) LoadWarning="Kayıt dosyaları okunamadı. Orijinal dosyalar korunuyor; kayıt klasörünü kontrol et.";
                return AppConfig.Defaults();
            }
            ActivePath=best.Path;
            foreach(var key in best.InvalidKeys) {
                var fallback=candidates.Where(c=>c.Path!=best.Path).OrderByDescending(c=>c.Stamp).Select(c=>c.Config.Bindings.GetValueOrDefault(key)).FirstOrDefault(a=>a is not null);
                if(fallback is not null && PadLayout.Keys.Any(k=>k.Id==key)) best.Config.Bindings[key]=fallback.Clone();
            }
            if(best.InvalidKeys.Count>0) LoadWarning="Kayıt onarıldı. Sağlam görevler ve yedeklerde bulunan görevler korundu.";
            else if(best.Path.EndsWith(".bak",StringComparison.OrdinalIgnoreCase) || preserveBeforeWrite.Contains(PrimaryPath)) LoadWarning="Kayıt dosyası sağlam yedeğinden kurtarıldı.";
            // Migrate config.json and repair missing/broken copies. Never seed defaults
            // over unreadable files; an explicit user save is required in that case.
            if(best.Path==LegacyPath || best.Path.EndsWith(".bak",StringComparison.OrdinalIgnoreCase) || best.InvalidKeys.Count>0 || !File.Exists(PrimaryPath) || !File.Exists(MirrorPath) || preserveBeforeWrite.Contains(PrimaryPath)) {
                try { Save(best.Config); } catch(Exception ex) { LoadWarning=(LoadWarning is null?"":LoadWarning+" ")+"Kayıt onarılamadı: "+ex.Message; }
            }
            return best.Config;
        }
    }
    public void Save(AppConfig config)
    {
        lock(sync) {
            ConfigStore.Validate(config);
            foreach(var path in preserveBeforeWrite.ToArray()) { if(File.Exists(path)) Archive(path,"recovered"); preserveBeforeWrite.Remove(path); }
            var snapshot=JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(config))!;
            snapshot.SavedAtUtc=DateTimeOffset.UtcNow;
            var successes=new List<string>(); var failures=new List<string>();
            foreach(var path in new[]{PrimaryPath,MirrorPath}.Distinct(StringComparer.OrdinalIgnoreCase)) {
                try { DurableJson.Write(path,snapshot); successes.Add(path); }
                catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { failures.Add(path+": "+ex.Message); }
            }
            if(successes.Count==0) throw new IOException("Kayıt hiçbir konuma yazılamadı. "+string.Join(" · ",failures));
            config.SavedAtUtc=snapshot.SavedAtUtc; ActivePath=successes[0];
            SaveWarning=failures.Count>0?"Görevler kaydedildi; ikinci kayıt kopyası yazılamadı.":null;
        }
    }
    public AppConfig Reset()
    {
        lock(sync) {
            foreach(var path in ReadPaths()) if(File.Exists(path)) Archive(path,"before-reset");
            var reset=AppConfig.FactoryDefaults(); Save(reset);
            // Make every automatic recovery source reflect the explicit reset.
            foreach(var path in new[]{PrimaryPath+".bak",MirrorPath+".bak",LegacyPath,LegacyPath+".bak"}.Distinct(StringComparer.OrdinalIgnoreCase)) {
                try { DurableJson.Write(path,reset,backup:false); } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { SaveWarning="Sıfırlama kaydedildi; eski bir yedek yazılamadı: "+ex.Message; }
            }
            return reset;
        }
    }
    void Archive(string path,string reason)
    {
        var directory=Path.Combine(Path.GetDirectoryName(PrimaryPath)!,"backups"); Directory.CreateDirectory(directory);
        var name=reason+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+"-"+Guid.NewGuid().ToString("N")[..6]+"-"+Path.GetFileName(path);
        File.Copy(path,Path.Combine(directory,name),overwrite:false);
    }
}

public sealed class EditorDraft
{
    public string KeyId { get; set; } = "";
    public PadAction? Action { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public bool Cleared { get; set; }
    public bool IsOlderThanReset(AppConfig config)=>config.ResetAtUtc!=default && UpdatedAtUtc<=config.ResetAtUtc;
}
public sealed class DraftRepository
{
    readonly string[] paths;
    public DraftRepository(params string[] locations)=>paths=locations;
    public EditorDraft? Load()
    {
        var drafts=new List<EditorDraft>();
        foreach(var path in paths) {
            try { if(File.Exists(path)) { var draft=JsonSerializer.Deserialize<EditorDraft>(File.ReadAllText(path)); if(draft is not null) drafts.Add(draft); } } catch { }
        }
        var latest=drafts.OrderByDescending(d=>d.UpdatedAtUtc).FirstOrDefault();
        return latest is null || latest.Cleared || latest.Action is null || !PadLayout.Keys.Any(k=>k.Id==latest.KeyId)?null:latest;
    }
    public void Save(string key,PadAction action)=>Write(new(){KeyId=key,Action=action.Clone(),UpdatedAtUtc=DateTimeOffset.UtcNow});
    public void Clear()=>Write(new(){Cleared=true,UpdatedAtUtc=DateTimeOffset.UtcNow});
    void Write(EditorDraft draft)
    {
        int saved=0; Exception? error=null;
        foreach(var path in paths) { try { DurableJson.Write(path,draft,backup:false); saved++; } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { error=ex; } }
        if(saved==0) throw new IOException("Düzenleme taslağı kaydedilemedi.",error);
    }
}

public static class ConfigStore
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PadStudio");
    public static string MirrorPath => Path.Combine(AppContext.BaseDirectory,"save","numpad-save.json");
    static readonly Lazy<ConfigRepository> Repository=new(()=>new(Path.Combine(DirectoryPath,"save.json"),MirrorPath,Path.Combine(DirectoryPath,"config.json")));
    static readonly Lazy<DraftRepository> Drafts=new(()=>new(Path.Combine(DirectoryPath,"draft.json"),Path.Combine(AppContext.BaseDirectory,"save","numpad-draft.json")));
    public static string FilePath => Repository.Value.ActivePath;
    public static string? LoadWarning=>Repository.Value.LoadWarning;
    public static string? SaveWarning=>Repository.Value.SaveWarning;
    public static AppConfig Load()=>Repository.Value.Load();
    public static void Save(AppConfig config)=>Repository.Value.Save(config);
    public static AppConfig Reset()=>Repository.Value.Reset();
    public static EditorDraft? LoadDraft()=>Drafts.Value.Load();
    public static void SaveDraft(string key,PadAction action)=>Drafts.Value.Save(key,action);
    public static void ClearDraft()=>Drafts.Value.Clear();
    public static void Validate(AppConfig config)
    {
        if(config.Version!=1 || config.Bindings is null || config.DeviceHardwareId is null) throw new InvalidDataException("Ayar dosyası sürümü veya cihaz bilgisi geçersiz.");
        foreach(var pair in config.Bindings) {
            if(!PadLayout.Keys.Any(k=>k.Id==pair.Key) || pair.Value is null) throw new InvalidDataException("Geçersiz numpad tuşu.");
            ActionValidator.Validate(pair.Value);
        }
    }
    public static void Export(AppConfig config,string path) { Validate(config); DurableJson.Write(path,config); }
    public static AppConfig Import(string path) { var config=JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path))??throw new InvalidDataException("Dosya boş."); Validate(config); return config; }
}
