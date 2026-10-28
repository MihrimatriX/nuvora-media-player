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
| FSRCNNX x2 8-0-4-1 (shader) | LGPL-3.0-or-later | `Assets/Shaders/FSRCNNX`, aşağıya bakın |
| ArtCNN C4F16 (shader) | MIT | `Assets/Shaders/ArtCNN` |
| Anime4K v4 (shader) | MIT | `Assets/Shaders/Anime4K` |

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

## GPU shader katmanı

Shader dosyaları **değiştirilmeden** paketlenir; libmpv onları çalışma anında diskteki ayrı dosyalar
olarak okur (`glsl-shaders`). Lisans metinleri her klasörde shader'ların yanında durur ve kurulumla
birlikte kopyalanır.

| Dosya | Kaynak | Telif | SHA-256 |
|---|---|---|---|
| `FSRCNNX/FSRCNNX_x2_8-0-4-1.glsl` | https://github.com/igv/FSRCNN-TensorFlow/releases/tag/1.1 | © 2017-2021 igv | `e800dbc5c1c95185cc82216c597724533ff5f2880179f256eef600f03e8dc2ae` |
| `ArtCNN/ArtCNN_C4F16.glsl` | https://github.com/Artoriuz/ArtCNN (`GLSL/`, main) | © 2024 João Chrisóstomo, Kacper Michajłow | `03d0b3d31cb82c898a94a46663021a3e8f02c5a21d69c5cfdf0208de4bfd453e` |
| `Anime4K/Anime4K_Clamp_Highlights.glsl` | https://github.com/bloc97/Anime4K (`glsl/Restore`, master) | © 2019-2021 bloc97 | `a2a9bf7fbc1d75d09660ca2e701e4d7fb0cf5457b94da47e1825032fa2b3671a` |
| `Anime4K/Anime4K_Restore_CNN_M.glsl` | aynı (`glsl/Restore`) | © 2019-2021 bloc97 | `67ea3ed26539e8de3b7d307688535d2ff17e8d147e11dda0247da7770dbecf41` |
| `Anime4K/Anime4K_Upscale_CNN_x2_M.glsl` | aynı (`glsl/Upscale`) | © 2019-2021 bloc97 | `716e02098a68f0d648761f2b96b4dd139e1cb09b174bb369fca3aa34328fff7e` |
| `Anime4K/Anime4K_Upscale_CNN_x2_S.glsl` | aynı (`glsl/Upscale`) | © 2019-2021 bloc97 | `4c53ec2e287908f7ee7bcb266b0170421626d663576468b7d7dafc62962649a4` |
| `Anime4K/Anime4K_AutoDownscalePre_x2.glsl` | aynı (`glsl/Upscale`) | © 2019-2021 bloc97 | `8c58291740146bd766a4d73f132775a797fe80f7d07919b5d767e27a5dc85656` |
| `Anime4K/Anime4K_AutoDownscalePre_x4.glsl` | aynı (`glsl/Upscale`) | © 2019-2021 bloc97 | `5af62d8cd844916dc1126613e13bad3beab195787f93a71200b47c6ec78f2e41` |

**FSRCNNX (LGPL-3.0):** Shader ayrı bir dosya olarak dağıtıldığı ve uygulamayla derlenmediği için
uygulamanın geri kalanı LGPL'ye tabi değildir. Koşullar: dosya değiştirilmeden ve telif başlığıyla
dağıtılır, `COPYING.LESSER` (LGPL-3.0) ve `COPYING` (GPL-3.0) yanında durur, kullanıcı dosyayı kendi
sürümüyle değiştirebilir (uygulama dosyayı adından yükler). Shader'ın kaynağı dosyanın kendisidir.
