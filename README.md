# Nuvora Player

Kompakt, video odaklı Windows medya oynatıcısı. C# / .NET 10, Avalonia 11, libmpv.

## Çalıştırma

1. **libmpv-2.dll edin** (x64):
   ```
   pwsh scripts/get-libmpv.ps1
   ```
   Betik `scripts/libmpv.json` içinde sabitlenmiş LGPL derlemesini indirir, SHA-256'sını doğrular ve
   `libmpv-2.dll`'i `lib/` klasörüne koyar. Build sırasında exe'nin yanına kopyalanır. CI ve yayın da aynı DLL'i kullanır.
   - mpv **0.39 veya üstü** gerekli (`autocreate-playlist` için).
   - Sürümü yükseltmek için `libmpv.json`'daki `tag`, `asset` ve `sha256` alanlarını boşaltıp betiği çalıştır;
     yazdırdığı değerleri geri yaz.
2. Çalıştır:
   ```
   dotnet run --project src/Player.App -- "C:\video.mkv"
   ```
3. Testler: `dotnet test`. `[MpvFact]` testleri gerçek libmpv'yi `vo=null`/`ao=null` ile sürer ve DLL
   yoksa atlanır; arayüz testleri (`tests/Player.App.Tests`) Avalonia.Headless ile çalışır.

Her PR ve `main` push'unda GitHub Actions (`.github/workflows/ci.yml`) Windows'ta build ve test çalıştırır.

## Yayın

`v*` etiketi (ör. `git tag v0.1.0 && git push origin v0.1.0`) `.github/workflows/release.yml`'i tetikler:

- **Kurulum:** Velopack `NuvoraPlayer-win-Setup.exe` (yönetici izni istemez, `%LocalAppData%\NuvoraPlayer`'a kurar)
  ve tam + delta `.nupkg` paketleri. Kurulu sürüm açılışta GitHub Releases'i denetler (Ayarlar → Genel →
  "Güncellemeleri denetle"), yeni sürümü arka planda indirir ve oynatıcı kapanınca sessizce uygular.
- **Portable:** `NuvoraPlayer-<sürüm>-win-x64-portable.zip`, `portable` klasörüyle gelir; otomatik güncelleme yapmaz.
- Paketler **taslak** bir GitHub sürümüne eklenir; yayınlamak elle yapılır. Etiketsiz koşular (PR, elle tetikleme)
  aynı paketleri yalnızca workflow artifact'ı olarak üretir.

**Tek örnek:** Oynatıcı açıkken başka bir dosyayı açmak (Explorer, "Birlikte aç") yeni pencere açmaz; dosya
çalışan oynatıcıya iletilir ve pencere öne gelir.

**Portable mod:** exe'nin yanında `portable` klasörü varsa ayarlar ve kaldığın yer bilgileri oraya,
yoksa `%AppData%\Nuvora Player` altına yazılır.

## Kısayollar

Tüm kısayollar **Ayarlar → Kısayollar** sayfasından özelleştirilebilir (Türkçe Q klavye düzenine duyarlıdır). Varsayılanlar:

| Tuş | İşlev |
|---|---|
| Boşluk | Oynat / duraklat |
| ← / → | Geri / ileri atla (`SeekStepSeconds`) |
| ↑ / ↓, tekerlek | Sesi aç / kıs (`VolumeStep`) |
| Ctrl+↑ / Ctrl+↓ | Hızlandır / yavaşlat |
| F, F11, çift tık | Tam ekran (Esc ile çıkış) |
| M | Sessiz |
| , / . | Kare geri / ileri |
| PgUp / PgDn | Önceki / sonraki dosya |
| Z | Sığdır ↔ doldur |
| S | Kare yakala (`ScreenshotDirectory`) |
| E | Görüntü & Ses paneli |
| \ (basılı tut) | Orijinali göster (iyileştirme karşılaştırması) |
| Ctrl+O | Dosya aç |
| Ctrl+, | Ayarlar |

Altyazı dosyasını (.srt/.ass/…) oynatma sırasında pencereye bırakmak onu yükler.

## Mimari

```
src/Player.Core         libmpv P/Invoke (MpvNative), MpvPlayer : IPlayerService, Track
src/Player.Enhancement  DXGI GPU algılama (GpuInfo), görüntü ölçekleyici presetleri (ImagePresets)
src/Player.Windows      Windows kabuğu: SMTC (MediaSession), görev çubuğu düğmeleri/ilerleme (TaskbarButtons), JumpList
src/Player.App          Avalonia arayüzü (Manrope + IBM Plex Mono)
  MainWindow            yalnızca video: VideoHost (NativeControlHost → HWND → mpv "wid")
  OverlayWindow         şeffaf, sahipli (owned) pencere: başlık çubuğu, bar, Görüntü & Ses paneli, Ayarlar
  PlayerViewModel       mpv property'leri ↔ UI (.Image.cs ve .Audio.cs parçalı sınıfları)
  ShellIntegration      PlayerViewModel ↔ Player.Windows köprüsü
tests/Player.Core.Tests
```

- **Airspace:** Avalonia, native bir child HWND'nin üstüne çizim yapamaz. Bu yüzden kontroller ayrı
  bir şeffaf pencerede duruyor. Bu pencere `Show(owner)` ile ana pencereye bağlı; konumu ve boyutu
  `PositionChanged`, `ClientSize` ve `WindowState` değişimlerinde ana pencerenin client alanına
  eşitleniyor. Küçültme ve kapatmayı Windows'un owned-window davranışı hallediyor.
- **Thread modeli:** Tüm komutlar `mpv_command_async` ile gidiyor, UI thread'i bloklanmıyor.
  Event'ler `mpv-events` thread'inde okunuyor ve UI'ın `SynchronizationContext`'ine post ediliyor.
  Kapanışta önce `quit` gönderiliyor (watch-later kaydı için), sonra event thread'i bekleniyor,
  ardından `mpv_terminate_destroy` çağrılıyor. Bunların hepsi video HWND'si yok edilmeden önce oluyor.
- **Windows entegrasyonu:** Medya tuşları, Windows medya paneli ve kilit ekranı
  `SystemMediaTransportControls` üzerinden çalışıyor (paketsiz uygulama olduğu için
  `SystemMediaTransportControlsInterop.GetForWindow` ile ana pencerenin HWND'sine bağlı). Küçük resim
  araç çubuğu (önceki / oynat-duraklat / sonraki) ve görev çubuğu ilerlemesi `ITaskbarList3` ile,
  "Son açılanlar" atlama listesi `ICustomDestinationList` ile kuruluyor. COM arayüzleri
  `[GeneratedComInterface]` ile tanımlı; her parça kendi başına hata verip yalnızca log yazar.
  Bu yüzden Player.App ve testler `net10.0-windows10.0.19041.0` hedefliyor (en düşük Windows 10 1809);
  Core ve Enhancement platformdan bağımsız `net10.0` olarak kalıyor. Linux'ta derleme ve testler
  `EnableWindowsTargeting` sayesinde çalışıyor.

## Bilinen sınırlamalar

- Avalonia'da native pencere üstünde gerçek zamanlı backdrop blur olmadığı için paneller yüksek opaklıklı yarı saydam yüzey kullanır.
- Seek çubuğu bölüm işaretlerini ve zaman balonunu gösterir; küçük kare (SmartSeek) önizlemesi henüz eklenmedi.
- Yalnızca Windows'ta test edildi. Linux ve macOS için libmpv kütüphane adı çözücüsü gerekiyor (`MpvNative.cs`).
