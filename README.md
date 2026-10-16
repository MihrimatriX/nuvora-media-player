# Nuvora Player

Kompakt, video odaklı Windows medya oynatıcısı. C# / .NET 10, Avalonia 11, libmpv.

## Çalıştırma

1. **libmpv-2.dll edin** (x64):
   - https://github.com/shinchiro/mpv-winbuild-cmake/releases adresinden `mpv-dev-x86_64-*.7z`
     (CPU'n AVX2 destekliyorsa `x86_64-v3`) indir.
   - Arşivden `libmpv-2.dll` dosyasını repo kökündeki `lib/` klasörüne koy. Build sırasında exe'nin yanına kopyalanır.
   - mpv **0.39 veya üstü** gerekli (`autocreate-playlist` için). Daha eski bir sürümle de oynatır, ama klasördeki sonraki dosyaya geçmez.
2. Çalıştır:
   ```
   dotnet run --project src/Player.App -- "C:\video.mkv"
   ```
3. Testler: `dotnet test`

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
src/Player.App          Avalonia arayüzü (Manrope + IBM Plex Mono)
  MainWindow            yalnızca video: VideoHost (NativeControlHost → HWND → mpv "wid")
  OverlayWindow         şeffaf, sahipli (owned) pencere: başlık çubuğu, bar, Görüntü & Ses paneli, Ayarlar
  PlayerViewModel       mpv property'leri ↔ UI (.Image.cs ve .Audio.cs parçalı sınıfları)
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

## Bilinen sınırlamalar

- Avalonia'da native pencere üstünde gerçek zamanlı backdrop blur olmadığı için paneller yüksek opaklıklı yarı saydam yüzey kullanır.
- Seek çubuğu bölüm işaretlerini ve zaman balonunu gösterir; küçük kare (SmartSeek) önizlemesi henüz eklenmedi.
- Yalnızca Windows'ta test edildi. Linux ve macOS için libmpv kütüphane adı çözücüsü gerekiyor (`MpvNative.cs`).
