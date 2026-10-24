# Nuvora Player

Kompakt, video odaklı Windows medya oynatıcısı. C# / .NET 10, Avalonia 11, libmpv.

**[İndir (Windows kurulum)](https://github.com/MihrimatriX/nuvora-media-player/releases/latest/download/NuvoraPlayer-win-Setup.exe)**
· [Tanıtım sayfası](https://mihrimatrix.github.io/nuvora-media-player/)
· [Tüm sürümler](https://github.com/MihrimatriX/nuvora-media-player/releases)

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
  Kurulum ve her güncelleme, oynatıcıyı "Birlikte aç" menüsüne ve Ayarlar → Varsayılan uygulamalar'a kaydeder;
  varsayılan yapmayı Windows yalnızca kullanıcıya bırakır. Kaldırma bu kayıtları siler.
- **Portable:** `NuvoraPlayer-<sürüm>-win-x64-portable.zip`, `portable` klasörüyle gelir; otomatik güncelleme yapmaz.
- Paketler doğrudan yayınlanan bir GitHub sürümüne eklenir. Etiketsiz koşular (PR, elle tetikleme)
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
| V | Sıradaki renk filtresi |
| \ (basılı tut) | Orijinali göster (iyileştirme karşılaştırması) |
| Ctrl+O | Dosya aç |
| Ctrl+, | Ayarlar |

Altyazı dosyasını (.srt/.ass/…) oynatma sırasında pencereye bırakmak onu yükler.

## Renk ve sinema filtreleri

Görüntü & Ses paneli → **Renk ve Sinema Filtreleri**: 11 hazır görünüm (Sıcak, Soğuk, Gişe Filmi / teal & orange,
Technicolor, Ağartılmış, Mat Film, Vintage, Sepya, Siyah-Beyaz, Kara Film, Neon), yoğunluk ayarı, film greni, vinyet ve
2.39:1 sinemaskop bantları. Hepsi tek bir GPU geçişinde (`Assets/Shaders/looks.glsl`, MAIN kancası, videonun kendi
çözünürlüğünde) çalışır; ayar değiştirmek yalnızca `glsl-shader-opts` uniform'larını günceller, shader yeniden derlenmez.
Hiçbir filtre açık değilken geçiş hiç yüklenmez. Önce/Sonra karşılaştırmasında filtreler "Sonra" tarafına uygulanır;
kullanıcı ön ayarları filtreleri de saklar. Renk matematiği `Player.Enhancement/ColorLooks.cs` içinde, CPU ikizi testlerde.

## Windows Gezgini

Kurulum oynatıcıyı video, ses ve çalma listesi için ayrı ProgId'lerle kaydeder. Video ProgId'si Windows'un kendi video
küçük resim çıkarıcısını (Media Foundation) gösterir; böylece Nuvora varsayılan oynatıcıyken de MKV/WebM/TS dahil
dosyalar uygulama logosu yerine kare önizlemesiyle görünür. Küçük resim üretilemeyen dosyalar (codec yoksa, liste
görünümünde) logo yerine `Assets/FileTypes` altındaki belge ikonlarını kullanır (`python scripts/make_file_icons.py`).

## Mimari

```
src/Player.Core         libmpv P/Invoke (MpvNative), MpvPlayer : IPlayerService, Track
src/Player.Enhancement  DXGI GPU algılama (GpuInfo), katman 1 sürücü VSR / RTX Video HDR (DriverLayer),
                        katman 2 shader'lar ve otomatik düşüş (ShaderLayers, LayerFallback), presetler
                        (ImagePresets, UserPresets), SVP/RIFE ve Remaster genişletme noktaları (ExtensionPoints)
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
- Katman 1 (RTX VSR, Intel VSR, RTX Video HDR) RTX/Intel donanımı olmadan test edilemedi; birim testleri yalnızca
  mpv'ye giden komutları doğruluyor. Sürücü VSR'ı GPU yükü altında mpv'ye haber vermeden kapatabilir; bu
  durum "Aktif katman" etiketine yansımaz. Eski Intel iGPU'lar hata vermek yerine standart ölçeklemeye düşebilir.
- RTX Video HDR'ın ekranda HDR görünmesi için Windows HDR açık olmalı; kapalıysa mpv sonucu SDR'a geri eşler.
- SVP/RIFE (VapourSynth) ve çevrimdışı Remaster yalnızca arayüz olarak var (`ExtensionPoints.cs`).
- Yalnızca Windows'ta test edildi. Linux ve macOS için libmpv kütüphane adı çözücüsü gerekiyor (`MpvNative.cs`).

## Tanıtım sayfası

`site/` tek dosyalık statik bir sayfa. `main`'e push edilince `.github/workflows/pages.yml` onu GitHub Pages'e
yayınlar (Settings → Pages → Source: **GitHub Actions**). Vercel'de projeyi içe aktarıp Root Directory olarak
`site` seçmek yeterli, build adımı yok.
