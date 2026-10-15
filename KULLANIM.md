# Pad Studio

Windows için Türkçe, yerel numpad kontrol paneli. .NET 10 Desktop Runtime x64 gerektirir.

## Başlatma

Hazır ZIP paketini tamamen çıkartıp `PadStudio/PadStudio.exe` dosyasını aç. Kaynaktan derlediysen uygulama `release/PadStudio/PadStudio.exe` yolundadır. Kopyala, Yapıştır ve Geri Al örnekleri 1, 2 ve 3 tuşlarında hazırdır. Diğer tuşları seçip görev ekleyebilirsin. Eşleştirilmiş numpad bulunursa açılışta otomatik etkinleşir. Bir görevi Kaydet'e basarak kaydettiğinde de otomatik etkinleşir.

## Cihaz kurulumu

Ana klavye ile harici numpadin tamamen ayrı çalışması için Interception gerekir. Sürücü henüz otomatik olarak kurulmaz.

Önce **Cihaz ve ayarlar → Numpad bağlantısını test et** ile sürücü kurmadan iki cihazın ayrı göründüğünü doğrulayabilirsin. Harici numpadi dinle'ye basıp harici numpadde 0'a bas; ardından Ana klavyeyi dinle'ye basıp ana klavyede 0'a bas. Bu pencere açıkken yalnızca bu test için tuş sinyali okunur; kayıt tutulmaz.

1. Cihaz ve ayarlar → Sürücüyü kur. Windows yönetici izni ister. Kurulum Windows'un klavye sürücü zincirini değiştirir.
2. Açık çalışmalarını kaydet ve Windows'u yeniden başlat. Uygulama bilgisayarı kendi kendine yeniden başlatmaz.
3. Pad Studio → Cihazı tanıt. Yalnızca harici numpadde **0'a bir kez** bas. Enter gerekmez; NumLock açık veya kapalı olabilir. Tanıtma sırasında tuşlar Windows'a normal olarak iletilir. Cihaz 30 saniye içinde tanıtılmazsa işlem iptal edilir. Vazgeç düğmesiyle de iptal edebilirsin.
4. Bir görevi Kaydet'e basarak kaydet; tanıtılmış numpad otomatik etkinleşir. İstersen Etkinleştir/Duraklat düğmesini elle de kullanabilirsin. Ana klavyenin aynı tuşları normal çalışır. Harici numpadin boş tuşları karakter yazmaz.

Sürücünün resmi dokümanı Windows 10'a kadar test edildiğini belirtir. Windows 11 uyumluluğu kullanılacak bilgisayarda ayrıca doğrulanmalıdır. Program güvenlik özelliklerini kapatmaz. Uyumsuzluk durumunda Cihaz ve ayarlar → Sürücüyü kaldır ile kaldırıp yeniden başlat. Kurulum günlüğü `%LOCALAPPDATA%/PadStudio/driver-setup.log` yolundadır. Uygulamaya erişilemiyorsa, `driver/install-interception.exe /uninstall` komutunu yönetici terminalinde çalıştırıp yeniden başlatabilirsin.

Sürücü yüklü değilken düzen oluşturabilir, içe/dışa aktarabilir, 3 saniyelik denemeyi kullanabilirsin. Cihaza özel fiziksel tuş görevleri etkinleştirilemez.

## Görünüm ve uygulama simgesi

Cihaz ve ayarlar → Görünüm bölümünde **Liquid Glass Gri**, **Gece Camı**, **Aurora** ve **Porselen** temaları bulunur. Bir kartı seçtiğinde ana pencere, makro klavyesi ve ayarlar aynı anda yenilenir. **Ayarları kaydet** seçimi kalıcı olarak saklar; kaydetmeden kapatırsan önceki tema geri gelir. Eski kayıtlar görevleri korunarak Liquid Glass Gri ile açılır. Fabrika ayarlarına dönmek görünümü de varsayılana getirir.

Pad Studio adı korunmuştur. Gümüş cam P logosu uygulama dosyasına, pencere simgesine ve sistem tepsisine gömülüdür. Kaynak görsel `src/Assets/pad-studio.png`, Windows simgesi `src/Assets/pad-studio.ico`; üretim istemi `src/Assets/LOGO-PROMPT.md` dosyasındadır. Cam görünümü Windows masaüstü için yerel WPF yüzeyleri, ışık geçişleri ve ince kenar yansımalarıyla çizilir.

## Makrolar

Bir tuş seç → Klavye makrosu → Klavyeden makro oluştur.

- Aynı anda: Ctrl ve C'ye tıkla → Tuşları ekle. Tek bir Ctrl+C adımı oluşur.
- Sırayla: A, B, C'ye tıkla → Sırayla bas → Tuşları ekle. Üç ayrı adım oluşur.
- Bekleme: örneğin 300 ms ekle. 1000 ms = 1 saniye.
- Metin: Türkçe karakterler dahil metin yazma adımı ekle.
- Adımları seçip düzenleyebilir, yukarı/aşağı taşıyabilir veya silebilirsin.
- Makroyu uygula, ardından ana ekranda Kaydet.

Kısayollar aktif pencereye gönderilir. Makro çalışırken pencere odağı değişirse kalan adımlar iptal edilir. Basılı Ctrl/Alt/Shift/Win tuşlarını bırakman beklenir. Dene düğmesi 3 saniye verir; hedef uygulamaya geç. Yönetici olarak açılan bir hedefe tuş göndermek için uygulamaların yetki seviyeleri aynı olmalıdır. Ctrl+Alt+Delete gibi Windows güvenlik kısayolları otomasyonla gönderilemez.

## Diğer görevler

Uygulama aç: .exe veya .lnk seç; gerekiyorsa başlatma parametreleri ve çalışma klasörü yaz. Discord için masaüstü kısayolunu seçebilirsin. Web sitesi aç: http/https adresi yaz. Site varsayılan tarayıcıda açılır. Klasör, düz metin ve altı medya kontrolü de desteklenir.

## Arka planda çalışma ve veriler

Pencere varsayılan olarak tepsiye kapanır; P simgesine çift tıklayarak geri aç. Tepsi menüsünden Duraklat veya Çıkış yap. Windows ile başlama isteğe bağlıdır. Numpad eşleştirmesi bulunursa uygulama açılışında ve görev kaydettiğinde otomatik etkinleşir. Bu davranışı Cihaz ve ayarlar bölümünden kapatabilirsin. Belirsiz cihaz eşleşmesinde program otomatik seçim yapmaz.

Ayarlar `%LOCALAPPDATA%/PadStudio/save.json` dosyasına ve uygulamanın yanındaki `save/numpad-save.json` dosyasına yazılır. Eski `config.json` dosyası ilk açılışta görevler korunarak taşınır. Kayıt dosyaları diske yazılıp kapatıldıktan sonra Kaydedildi bilgisi gösterilir; önceki kayıtlar `.bak` dosyalarında tutulur. Bir kopya bozulursa sağlam kopya veya son sağlam yedek yüklenir. Bozuk ham dosyalar kurtarma klasöründe korunur.

Son düzenleme 500 ms sonra ayrı bir taslak dosyasına kaydedilir; taslak henüz çalışan görevlerin üzerine uygulanmaz. Windows normal biçimde kapanırken tamamlanmış, geçerli düzenleme de kaydedilir. Yarım kalmış bir düzenleme varsa sonraki açılışta düzenleyiciye geri gelir. Kaydet'e basarak etkin göreve dönüştürebilirsin.

Cihaz ve ayarlar bölümünde **Save klasörünü aç** ve **Kurtarma yedeklerini aç** düğmeleri bulunur. Dışa/içe aktarma cihaz eşleştirmesini başka bilgisayara taşımaz. Program tuş geçmişi kaydetmez ve ağ isteği yapmaz; site açma görevi tarayıcıyı açar.

## Fabrika ayarları

Cihaz ve ayarlar bölümünün en altındaki **Fabrika ayarlarına döndür** düğmesi onaydan sonra bütün tuş görevlerini, makroları, site/uygulama atamalarını, cihaz eşleştirmesini ve uygulama tercihlerini temizler. Windows ile başlama kapatılır. Sıfırlamadan önce mevcut dosyalar `%LOCALAPPDATA%/PadStudio/backups` klasörüne alınır. Otomatik kurtarma kopyaları da boş düzeni yansıtır; eski görevler sonraki açılışta kendiliğinden geri gelmez. Kurulu cihaz sürücüsü yerinde kalır.

## Geliştirme

`powershell -ExecutionPolicy Bypass -File tools/build.ps1`

Derleme betiği, eksikse resmi Interception v1.0.1 arşivini indirir, sabit SHA256 değeriyle doğrular ve `vendor/Interception` içine açar. Proje x64 DLL ve kurucuyu bu klasörden alır. Kaynak ve lisans bilgileri THIRD-PARTY.md dosyasında.

Doğrulama: `PadStudio.exe --self-test artifacts/self-test.txt`; donanım bilgisi (tuşları dinlemez): `PadStudio.exe --diagnostics artifacts/diagnostics.txt`; arayüz renderi: `PadStudio.exe --render artifacts`.
