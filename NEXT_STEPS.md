# Proje Durumu (22.10.2026)

Build temiz (0 uyarı), 94 test geçiyor. v0.1.0 18.10.2026'da yayınlandı; kurulum Velopack `Setup.exe` + portable zip
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

## Açık kalanlar
- Katman 1'i RTX 20+ veya Intel Arc olan bir makinede elle test et (Aktif katman = "Sürücü · RTX VSR", OSD fallback).
- Şartnamedeki örnek dosya seti (H.264, HEVC 10-bit, AV1, VP9, HDR10, çok parçalı MKV, ASS anime,
  AVI/WMV/MTS, bozuk dosya) ile elle oynatma turu.
- Linux/macOS: `MpvNative.cs` için libmpv adı çözücüsü.
