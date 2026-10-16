# Proje Durumu (16.10.2026)

## Durum: Nihai Sürüm Hazır (`0 Uyarı, 0 Hata`, `7/7 Test Başarılı`)

## Tamamlanan Bileşenler ve Düzenlemeler
- **Arayüz & Tasarım:**
  - Başlangıç ekranı (`StartView`), özel başlık çubuğu ve otomatik gizlenen oynatma barı (`OverlayWindow`), Görüntü & Ses paneli (`AvPanel`, `#EB0E1016` yüksek opaklık), Ayarlar penceresi (`SettingsView`).
  - Manrope + IBM Plex Mono gömülü yazı tipleri (`src/Player.App/Assets/Fonts`), canlı önizlemeli 4 vurgu rengi, tek `Button` ve `RadioButton` teması (`App.axaml`, `Tokens.axaml`).
- **Oynatma & Pencere Yönetimi:**
  - `Player.Core`: `libmpv-2.dll` P/Invoke sarmalayıcısı (`MpvNative`, `MpvPlayer`), `mpv_command_async` ve arka plan event döngüsü, parça ayrıştırıcı (`Track`).
  - `MainWindow` + `OverlayWindow`: Airspace kısıtlamasını aşan sahipli (owned) şeffaf pencere mimarisi; videoya göre pencere boyutlandırma (`FitWindowToVideo`), videonun ortasından sürükleme, yeni dosya açıldığında video boyut önbelleğinin (`_dw`, `_dh`) sıfırlanması.
  - Son oynatılanlar (`RecentItem`): bellek içi (`MemoryStream`) güvenli küçük resim yükleme (dosya kilitlemesini ve bozuk görüntü çökmelerini önler).
- **Görüntü & Ses İyileştirme:**
  - `Player.Enhancement`: DXGI GPU algılama (`GpuInfo`), yerleşik ölçekleyici presetleri (`ImagePresets`: Kapalı, Dengeli, Anime, Maksimum).
  - Akıcı hareket (`interpolation` + `display-resample`), renk kontrolleri, `\` ile basılı tutarak orijinal görüntü karşılaştırması.
  - Ses yükseltme (`volume-max=200`), `dynaudnorm` normalizasyonu, AC-3 DRC, 5.1 → stereo kanal mikseri (`pan=stereo`), ses/altyazı senkron gecikmesi.
- **Ayarlar, Kısayollar ve Dosya İlişkilendirme:**
  - `SettingsView` & `SettingsViewModel`: Tüm sayfalar `ScrollViewer` içinde; Genel, Oynatma, Video, Ses, Altyazı, Kısayollar ve Hakkında sekmeleri.
  - `Shortcuts.cs`: Türkçe Q klavye düzenine duyarlı tuş yakalama (`FromKey`), eşleştirme (`Matches`) ve çakışma çözümü. `StartView` ve `AvPanel` üzerindeki kısayol etiketleri (`OpenKeyText`, `CompareKeyText`) dinamik olarak güncellenir.
  - Ayarlar penceresinde `Esc` ile çıkışta canlı vurgu rengi önizlemesinin geri alınması (`Settings.Cancel()`) ve sekme değişiminde aktif tuş yakalamanın iptali.
  - `FileAssociation.cs`: Yönetici izni gerektirmeyen `HKCU` medya işleyicisi kaydı ve Windows "Varsayılan uygulamalar" sayfası entegrasyonu.
- **Testler & Lisanslama:**
  - `tests/Player.Core.Tests`: `TrackTests`, `ImagePresetTests`, `ShortcutsTests` (toplam 7 birim testi).
  - `README.md` ve `THIRD_PARTY.md`: SIL Open Font License 1.1 (Manrope & IBM Plex Mono) telif bildirimleri ve LGPL/GPL notları.
