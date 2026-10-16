# Üçüncü taraf bileşenler

| Bileşen | Lisans | Not |
|---|---|---|
| Avalonia 11 | MIT | NuGet |
| CommunityToolkit.Mvvm | MIT | NuGet |
| Microsoft.Extensions.DependencyInjection | MIT | NuGet |
| Manrope (yazı tipi) | SIL Open Font License 1.1 (OFL-1.1) | `src/Player.App/Assets/Fonts` |
| IBM Plex Mono (yazı tipi) | SIL Open Font License 1.1 (OFL-1.1) | `src/Player.App/Assets/Fonts` |
| xUnit (yalnızca testler) | Apache-2.0 | NuGet, dağıtılmaz |
| libmpv (`libmpv-2.dll`) | LGPL-2.1+ **veya** GPL-2.0+ (derlemeye bağlı) | aşağıya bakın |
| FFmpeg (libmpv içinde) | LGPL-2.1+ **veya** GPL (derlemeye bağlı) | aşağıya bakın |

## Yazı tipi lisansları (SIL Open Font License 1.1)

- **Manrope:** Copyright © 2019 The Manrope Project Authors (https://github.com/sharanda/manrope)
- **IBM Plex Mono:** Copyright © 2017 IBM Corp. with Reserved Font Name "Plex" (https://github.com/IBM/plex)

Her iki yazı tipi de **SIL Open Font License, Version 1.1** (http://scripts.sil.org/OFL) altında lisanslanmıştır. Yazı tipi dosyaları tek başına satılamaz; türev çalışmalarda "Plex" ayrılmış adı kullanılamaz; yazılımla birlikte dağıtıldığında telif hakkı bildirimleri ve OFL-1.1 lisansı korunmalıdır.

## libmpv / FFmpeg lisans uyarısı

README'de geliştirme için önerilen shinchiro derlemeleri **GPL** bileşenler içerir. Bunlar yalnızca
geliştirme için uygundur. Kapalı kaynak dağıtım için:

- mpv `-Dgpl=false` ile derlenmeli,
- FFmpeg `--enable-gpl` ve `--enable-nonfree` **olmadan** derlenmeli (x264/x265 gibi GPL kütüphaneler hariç),
- `libmpv-2.dll` dinamik olarak bağlanmalı (şu an öyle), kullanıcının DLL'i değiştirebilmesine izin
  verilmeli, LGPL metni ve kaynak kod teklifi pakete eklenmeli.

Dağıtılan DLL'in hangi derleme seçenekleriyle üretildiği bu dosyaya yazılmalı.
