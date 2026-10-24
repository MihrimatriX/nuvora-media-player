# Proje Durumu (24.10.2026)

Build temiz (0 uyarı), 125 test geçiyor (libmpv gerektiren 3 test DLL yoksa atlanır). v0.1.0 18.10.2026'da yayınlandı; kurulum Velopack `Setup.exe` + portable zip
(`release.yml`), tanıtım sayfası GitHub Pages'te.

## 22.10.2026'da eklenenler
- **Katman 1 – sürücü VSR:** RTX VSR / Intel VSR (`vf=d3d11vpp=…:scaling-mode=nvidia|intel`, `hwdec=d3d11va`).
  Video büyütülürken shader katmanından önce denenir; mpv filtreyi reddederse shader katmanına düşer ve OSD'de
  gösterilir. Ayarlar → Video'dan kapatılabilir. Aynı ön ayara tekrar tıklamak yeniden dener.
- **RTX Video HDR anahtarı** artık çalışıyor (`nvidia-true-hdr` + `target-colorspace-hint`). İşlemci üzerinde
  çalışan 60/90/120 FPS filtresiyle birlikte kullanılamaz; anahtar kilitlenir ve tooltip nedenini söyler.
- **Kullanıcı ön ayarları:** Görüntü & Ses panelindeki profil menüsünden kaydet / uygula / yeniden adlandır / sil.
  Taban ön ayar, renk/keskinlik kaydırıcıları ve FPS hedefi saklanır (`settings.json` → `UserPresets`).
- **Genişletme noktaları:** `IFrameInterpolator` (SVP/RIFE), `IOfflineRemaster` + `RemasterKey` (TODO, UI yok).

## 24.10.2026'da eklenenler
- **Ses ve düğmeler:** ses kaydırıcısı sürüklenirken mpv'nin gecikmeli onayları kaydırıcıyı geri çekmiyor; sessizdeyken
  sesi değiştirmek sesi açıyor; tekerlek/touchpad küçük adımları biriktiriyor (touchpad'de ses artık zıplamıyor);
  hoparlör simgesi seviyeyi gösteriyor (0 / düşük / yüksek). Dosya sonunda Oynat baştan oynatıyor; ilk dosyada
  Önceki dosyayı başa sarıyor, son dosyada Sonraki "Listenin sonu" diyor. Tam ekran ve büyüt düğmeleri duruma göre simge
  değiştiriyor. Fare pencere dışında bırakılırsa ilerleme çubuğu donmuyor.
- **Renk ve sinema filtreleri** (`looks.glsl`, `ColorLooks`), V kısayolu, sağ tık menüsü, kullanıcı ön ayarlarında saklanır.
- **Performans:** time-pos güncellemeleri kısıldı (film başına ~4/sn, kısa kliplerde ≤30/sn; atlama ve duraklatma
  anında); istatistik kartı yalnızca açıkken hesaplanıyor; arabellek çubuğu bölüm işaretlerini her güncellemede yeniden
  kurmuyor; fare hareketi mpv'ye `sub-margin-y` yağdırmıyor; imleçler önbellekte; log yazımı arka plan iş parçacığında.
- **Windows Gezgini:** video küçük resim sağlayıcısı, `TypeOverlay` (köşede logo yok), PerceivedType, tür başına belge ikonu.

## Açık kalanlar
- Filtreleri ve Gezgin küçük resimlerini Windows'ta elle doğrula: `looks.glsl` glslang ile derleniyor ama gerçek
  libplacebo + D3D11 üzerinde çalıştırılmadı; HDR (PQ) içerikte görünümler gama kodlu değerler üzerinde çalışır.
- Katman 1'i RTX 20+ veya Intel Arc olan bir makinede elle test et (Aktif katman = "Sürücü · RTX VSR", OSD fallback).
- Şartnamedeki örnek dosya seti (H.264, HEVC 10-bit, AV1, VP9, HDR10, çok parçalı MKV, ASS anime,
  AVI/WMV/MTS, bozuk dosya) ile elle oynatma turu.
- Linux/macOS: `MpvNative.cs` için libmpv adı çözücüsü.
