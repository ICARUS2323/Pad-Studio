# Pad Studio v0.3.0 — yayın doğrulaması

Tarih: 15 Ekim 2026. Ortam: Windows, .NET SDK 10.0.301, x64 WPF.

## Sonuçlar

- Kaynak koddan Release derlemesi tamamlandı.
- Temiz paket derlemesinde **42 kontrol geçti, 0 başarısız**. Ham rapor: [SELF-TEST.txt](SELF-TEST.txt).
- Dağıtım ZIP'i ayrı bir klasöre çıkartılıp içindeki uygulamayla aynı 42 kontrol tekrar başarıyla çalıştırıldı.
- Dört tema, makro düzenleyicisi, ayarlar, fabrika ayarları ve cihaz sinyal testi için WPF önizlemeleri üretildi. Ana ekran ve makro düzenleyicisi görsel olarak incelendi.
- ZIP içeriğinde uygulama, çalışma zamanı tanımı, Interception DLL'i, sürücü kurucusu, lisans metni ve Türkçe rehber doğrulandı.
- Kişisel görevler, cihaz eşleştirmeleri, kayıt klasörleri, günlükler, yedekler ve PDB dosyaları dağıtım paketine alınmadı.
- Yerel belge ve görsel bağlantıları kontrol edildi. ZIP için SHA256 dosyası üretildi.

## Kontrollerin kapsamı

18 tuşun yerleşimi; ana klavye ve seçilen numpadin ayrılması; basılı tuş tekrarları; duraklatma; tek 0 basışıyla eşleştirme mantığı; Enter ve gezinme tuşlarının ayrımı; makro tuş sırası; Türkçe/emoji Unicode olayları; x64 yerel yapı boyutları; URL ve görev doğrulaması; gerçek WPF düğmeleriyle makro ve görev düzenleme; içe/dışa aktarma; tema geçişi ve vazgeçme; gömülü simge; kayıt aktarımı, yedekten kurtarma ve bozuk kayıtları koruma; taslak kurtarma; test profillerinde fabrika ayarları ve ayrı uygulama süreçleri arasında kayıt kalıcılığı.

## Sınırlar

Bu yayın kontrolünde fiziksel tuş gönderilmedi, gerçek cihaz filtreleri değiştirilmedi ve sürücü kurulmadı veya kaldırılmadı. Kişisel ayarlar sıfırlanmadı. Fiziksel numpad davranışı, farklı bilgisayarlarda sürücü uyumluluğu ve Windows 11 kurulumu bu otomatik testlerden çıkarılamaz. Önizlemeler örnek görevlerle oluşturulmuştur.

## Yeniden çalıştırma

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/build.ps1 -OutputDirectory artifacts/package-build/PadStudio
$app = (Resolve-Path ./artifacts/package-build/PadStudio/PadStudio.exe).Path
Start-Process -FilePath $app -ArgumentList '--self-test','artifacts/self-test.txt' -WindowStyle Hidden -Wait
Start-Process -FilePath $app -ArgumentList '--render','artifacts/preview' -WindowStyle Hidden -Wait
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/package.ps1
```
