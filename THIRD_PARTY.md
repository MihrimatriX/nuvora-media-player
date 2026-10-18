# Üçüncü taraf bileşenler

| Bileşen | Lisans | Not |
|---|---|---|
| Avalonia 11 | MIT | NuGet |
| CommunityToolkit.Mvvm | MIT | NuGet |
| Microsoft.Extensions.DependencyInjection | MIT | NuGet |
| Manrope (yazı tipi) | SIL Open Font License 1.1 (OFL-1.1) | `src/Player.App/Assets/Fonts` |
| IBM Plex Mono (yazı tipi) | SIL Open Font License 1.1 (OFL-1.1) | `src/Player.App/Assets/Fonts` |
| Velopack | MIT | NuGet; kurulum ve güncelleme |
| xUnit, Avalonia.Headless (yalnızca testler) | Apache-2.0 / MIT | NuGet, dağıtılmaz |
| libmpv (`libmpv-2.dll`) | LGPL-2.1+ **veya** GPL-2.0+ (derlemeye bağlı) | aşağıya bakın |
| FFmpeg (libmpv içinde) | LGPL-2.1+ **veya** GPL (derlemeye bağlı) | aşağıya bakın |

## Yazı tipi lisansları (SIL Open Font License 1.1)

- **Manrope:** Copyright © 2019 The Manrope Project Authors (https://github.com/sharanda/manrope)
- **IBM Plex Mono:** Copyright © 2017 IBM Corp. with Reserved Font Name "Plex" (https://github.com/IBM/plex)

Her iki yazı tipi de **SIL Open Font License, Version 1.1** (http://scripts.sil.org/OFL) altında lisanslanmıştır. Yazı tipi dosyaları tek başına satılamaz; türev çalışmalarda "Plex" ayrılmış adı kullanılamaz; yazılımla birlikte dağıtıldığında telif hakkı bildirimleri ve OFL-1.1 lisansı korunmalıdır.

## libmpv / FFmpeg (LGPL-2.1+)

Dağıtılan `libmpv-2.dll`, `scripts/libmpv.json` içinde sabitlenen **LGPL** derlemesidir
([zhongfly/mpv-winbuild](https://github.com/zhongfly/mpv-winbuild), `mpv-dev-lgpl-x86_64-*`): mpv `-Dgpl=false`,
FFmpeg `--enable-gpl` ve `--enable-nonfree` olmadan derlenmiştir. Arşivin SHA-256'sı her build'de doğrulanır.

- `libmpv-2.dll` dinamik olarak bağlanır; kullanıcı kurulum klasöründeki DLL'i kendi derlemesiyle değiştirebilir.
- Yayın paketleri `licenses/` klasöründe bu dosyayı, LGPL-2.1 metnini ve derlemenin etiketini (`libmpv-build.json`) taşır.
- Kaynak kod: mpv https://github.com/mpv-player/mpv , FFmpeg https://ffmpeg.org . Derlemenin tam kaynak
  sürümleri ve betikleri, `libmpv-build.json`'daki etiketle zhongfly/mpv-winbuild sürüm sayfasındadır. Talep
  edilirse aynı kaynaklar proje sahibi tarafından da sağlanır.
- LGPL derlemesinde GPL'e bağlı birkaç özellik (ör. bazı FFmpeg filtreleri, DVD/Blu-ray menüleri) yoktur.
