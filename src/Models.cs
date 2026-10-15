using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PadStudio;

public enum ActionKind { None, Macro, Application, Website, Text, Media, Folder }
public enum StepKind { Chord, Delay, Text }
public sealed class MacroStep
{
    public StepKind Kind { get; set; }
    public List<int> Keys { get; set; } = new();
    public int DelayMs { get; set; } = 150;
    public string Text { get; set; } = "";
    public string Description => Kind switch { StepKind.Delay => $"{DelayMs} ms bekle", StepKind.Text => $"Metin: {Text}", _ => string.Join(" + ", Keys.Select(KeyboardCatalog.Name)) };
}
public sealed class PadAction
{
    public string Title { get; set; } = "";
    public ActionKind Kind { get; set; }
    public string Target { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public List<MacroStep> Steps { get; set; } = new();
    public PadAction Clone() => JsonSerializer.Deserialize<PadAction>(JsonSerializer.Serialize(this))!;
    public string Summary => Kind switch { ActionKind.Macro => $"{Steps.Count} adımlı makro", ActionKind.Application => "Uygulama aç", ActionKind.Website => "Site aç", ActionKind.Text => "Metin yaz", ActionKind.Media => "Medya kontrolü", ActionKind.Folder => "Klasör aç", _ => "Görev ekle" };
    public string Glyph => Kind switch { ActionKind.Macro => "⌘", ActionKind.Application => "↗", ActionKind.Website => "◎", ActionKind.Text => "Aa", ActionKind.Media => "♪", ActionKind.Folder => "▣", _ => "+" };
}
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public string DeviceHardwareId { get; set; } = "";
    public bool CloseToTray { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AutoActivate { get; set; } = true;
    public string ThemeId { get; set; } = "liquid-gray";
    public DateTimeOffset SavedAtUtc { get; set; }
    public DateTimeOffset ResetAtUtc { get; set; }
    public Dictionary<string, PadAction> Bindings { get; set; } = new();
    public static AppConfig Defaults() => new() { Bindings = new() {
        ["N1"] = Shortcut("Kopyala", 0x11,0x43), ["N2"] = Shortcut("Yapıştır",0x11,0x56), ["N3"] = Shortcut("Geri al",0x11,0x5A) } };
    public static PadAction Shortcut(string title, params int[] keys) => new() { Title=title, Kind=ActionKind.Macro, Steps=new() { new() { Kind=StepKind.Chord, Keys=keys.ToList() } } };
    public static AppConfig FactoryDefaults() => new() { ResetAtUtc=DateTimeOffset.UtcNow };
}
public static class ActionValidator
{
    public static void Validate(PadAction a)
    {
        if(!Enum.IsDefined(a.Kind) || a.Steps is null || a.Target is null || a.Arguments is null || a.WorkingDirectory is null) throw new InvalidDataException("Geçersiz görev türü veya eksik görev alanı.");
        if(a.Kind==ActionKind.None) return;
        if(string.IsNullOrWhiteSpace(a.Title)) throw new InvalidDataException("Göreve bir ad ver.");
        if(a.Kind==ActionKind.Macro) {
            if(a.Steps.Count is <1 or >100) throw new InvalidDataException("Makro 1–100 adım içermeli.");
            foreach(var s in a.Steps) {
                if(s is null || !Enum.IsDefined(s.Kind)) throw new InvalidDataException("Geçersiz makro adımı.");
                if(s.Kind==StepKind.Delay && s.DelayMs is <0 or >60000) throw new InvalidDataException("Bekleme 0–60000 ms arasında olmalı.");
                if(s.Kind==StepKind.Chord && (s.Keys is null || s.Keys.Count is <1 or >12 || s.Keys.Any(k=>!KeyboardCatalog.All.Any(x=>x.Vk==k)) || s.Keys.Distinct().Count()!=s.Keys.Count)) throw new InvalidDataException("Tuş grubunda geçersiz tuş var.");
                if(s.Kind==StepKind.Text && (s.Text is null || s.Text.Length>10000)) throw new InvalidDataException("Metin en fazla 10000 karakter olabilir.");
            }
        }
        if(a.Kind==ActionKind.Website && (!Uri.TryCreate(a.Target,UriKind.Absolute,out var u) || (u.Scheme!="https" && u.Scheme!="http"))) throw new InvalidDataException("http:// veya https:// ile başlayan bir site adresi yaz.");
        if((a.Kind==ActionKind.Application || a.Kind==ActionKind.Folder) && string.IsNullOrWhiteSpace(a.Target)) throw new InvalidDataException("Açılacak dosyayı veya klasörü seç.");
        if(a.Kind==ActionKind.Text && (string.IsNullOrEmpty(a.Target) || a.Target.Length>10000)) throw new InvalidDataException("1–10000 karakter arasında bir metin yaz.");
        if(a.Kind==ActionKind.Media && !new[]{"volume_up","volume_down","mute","play_pause","next","previous"}.Contains(a.Target)) throw new InvalidDataException("Bir medya işlemi seç.");
    }
}
public record PadKey(string Id,string Label,int Scan,bool Extended,int Row,int Column,int RowSpan=1,int ColumnSpan=1);
public static class PadLayout
{
    public static readonly PadKey[] Keys = {
        new("NumLock","Num Lock",0x45,false,0,0),new("Divide","/",0x35,true,0,1),new("Multiply","*",0x37,false,0,2),new("Subtract","−",0x4A,false,0,3),
        new("N7","7",0x47,false,1,0),new("N8","8",0x48,false,1,1),new("N9","9",0x49,false,1,2),new("Add","+",0x4E,false,1,3),
        new("N4","4",0x4B,false,2,0),new("N5","5",0x4C,false,2,1),new("N6","6",0x4D,false,2,2),new("Backspace","⌫",0x0E,false,2,3),
        new("N1","1",0x4F,false,3,0),new("N2","2",0x50,false,3,1),new("N3","3",0x51,false,3,2),new("Enter","Enter ↵",0x1C,true,3,3,2),
        new("N0","0",0x52,false,4,0,1,2),new("Decimal",".",0x53,false,4,2)
    };
    public static PadKey? FromScan(int scan,bool extended) => Keys.FirstOrDefault(k=>k.Scan==scan && k.Extended==extended);
}
public record VirtualKey(int Vk,string Label,double Width=1);
public static class KeyboardCatalog
{
    public static readonly VirtualKey[][] Rows = {
        new[]{new VirtualKey(0x1B,"Esc"),new(0x70,"F1"),new(0x71,"F2"),new(0x72,"F3"),new(0x73,"F4"),new(0x74,"F5"),new(0x75,"F6"),new(0x76,"F7"),new(0x77,"F8"),new(0x78,"F9"),new(0x79,"F10"),new(0x7A,"F11"),new(0x7B,"F12")},
        new[]{new VirtualKey(0xC0,"\""),new(0x31,"1"),new(0x32,"2"),new(0x33,"3"),new(0x34,"4"),new(0x35,"5"),new(0x36,"6"),new(0x37,"7"),new(0x38,"8"),new(0x39,"9"),new(0x30,"0"),new(0xDF,"*"),new(0xBD,"−"),new(0x08,"⌫",1.6)},
        new[]{new VirtualKey(0x09,"Tab",1.4),new(0x51,"Q"),new(0x57,"W"),new(0x45,"E"),new(0x52,"R"),new(0x54,"T"),new(0x59,"Y"),new(0x55,"U"),new(0x49,"I"),new(0x4F,"O"),new(0x50,"P"),new(0xDB,"Ğ"),new(0xDD,"Ü")},
        new[]{new VirtualKey(0x14,"Caps",1.6),new(0x41,"A"),new(0x53,"S"),new(0x44,"D"),new(0x46,"F"),new(0x47,"G"),new(0x48,"H"),new(0x4A,"J"),new(0x4B,"K"),new(0x4C,"L"),new(0xBA,"Ş"),new(0xDE,"İ"),new(0x0D,"Enter",1.6)},
        new[]{new VirtualKey(0x10,"Shift",2),new(0xE2,"<"),new(0x5A,"Z"),new(0x58,"X"),new(0x43,"C"),new(0x56,"V"),new(0x42,"B"),new(0x4E,"N"),new(0x4D,"M"),new(0xBC,"Ö"),new(0xBE,"Ç"),new(0xBF,"."),new(0xDC,",")},
        new[]{new VirtualKey(0x11,"Ctrl",1.4),new(0x5B,"Win",1.2),new(0x12,"Alt",1.2),new(0x20,"Boşluk",5.5),new(0xA5,"Alt Gr",1.4),new(0x25,"←"),new(0x28,"↓"),new(0x26,"↑"),new(0x27,"→")},
        new[]{new VirtualKey(0x2D,"Insert",1.4),new(0x2E,"Delete",1.4),new(0x24,"Home",1.4),new(0x23,"End",1.4),new(0x21,"Page Up",1.6),new(0x22,"Page Down",1.8),new(0x2C,"PrtSc",1.4),new(0x13,"Pause",1.4)}
    };
    public static IEnumerable<VirtualKey> All => Rows.SelectMany(x=>x);
    public static string Name(int vk)=>All.FirstOrDefault(x=>x.Vk==vk)?.Label ?? $"0x{vk:X2}";
    public static bool IsModifier(int vk)=>vk is 0x10 or 0x11 or 0x12 or 0x5B or 0xA5;
    public static IEnumerable<int> Ordered(IEnumerable<int> keys)=>keys.OrderBy(k=>IsModifier(k)?0:1);
}
