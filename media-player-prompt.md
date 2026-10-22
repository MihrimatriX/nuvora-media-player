# Görev: Modern, kompakt ve güçlü bir Windows medya oynatıcısı geliştir

## Rol ve çalışma şekli
Kıdemli bir .NET masaüstü ve multimedya geliştiricisi gibi çalış. Kod yazmadan önce
mimariyi ve klasör yapısını içeren kısa bir plan sun, onayımı al. Ardından aşağıdaki
aşamaları sırayla uygula. Her aşamanın sonunda çalışır bir build, kısa bir değişiklik
özeti ve manuel test adımları ver.

## Ürün hedefi
- Neredeyse tüm video ve ses formatlarını, sistemde codec paketi (K-Lite vb.)
  kurulu olmadan oynatan bir oynatıcı.
- Şık, kompakt ve video odaklı bir arayüz: oynatma sırasında neredeyse hiç kontrol
  görünmesin.
- Gerçek zamanlı görüntü iyileştirme (AI upscale, HDR, akıcı hareket) ve gelişmiş
  ses kontrolleri.
- Mirillis Splash'in tasarım dilinden ilham al, ama kendi kimliği olsun. Splash'in
  adını, logosunu veya varlıklarını kullanma.

## Teknoloji kararları (değiştirmeden önce sor)
- **Dil ve runtime:** C#, .NET 10, nullable açık.
- **UI:** Avalonia 11, MVVM için CommunityToolkit.Mvvm, DI için
  Microsoft.Extensions.DependencyInjection.
- **Oynatma motoru:** libmpv (libmpv-2.dll) için kendi ince P/Invoke sarmalayıcımız.
  Video yüzeyi `NativeControlHost` ile alınan HWND'nin `wid` olarak verilmesiyle
  oluşturulsun.
- **Varsayılan mpv ayarları:** `vo=gpu-next`, `hwdec=auto-safe`, `osc=no`,
  `input-default-bindings=no`, `keep-open=yes`.
- **Platform:** Önce Windows x64. Kod, ileride Linux ve macOS'a taşınmayı
  engellemeyecek şekilde soyutlansın.
- **Lisans:** Uygulama kapalı kaynak olabilir, bu yüzden LGPL uyumlu bir
  libmpv/FFmpeg derlemesi hedeflensin. Kullanılan binary'lerin lisanslarını
  THIRD_PARTY.md dosyasında listele.

## Mimari
- `Player.Core`: mpv sarmalayıcısı (create/initialize, komut, property get/set,
  `mpv_observe_property`, event loop'u ayrı bir thread'de çalıştırıp UI thread'ine
  marshal etme), `IPlayerService` arayüzü.
- `Player.Enhancement`: GPU yetenek algılama ve görüntü/ses preset motoru.
- `Player.App`: Avalonia arayüzü, ViewModel'ler, tema, ayarlar.
- Ayarlar ve preset'ler `%AppData%` altında JSON olarak saklansın (portable mod:
  exe'nin yanında bir `portable` klasörü varsa orayı kullan).
- **Airspace sorunu:** Overlay kontrolleri (üst şerit, alt bar, paneller) HWND'nin
  üzerine çizilemez. Bunları video penceresine bağlı, şeffaf ve sahipli
  (owned) ayrı bir pencerede çiz. Konum, boyut ve tam ekran durumunu senkronize et.
  Bu yaklaşımı Aşama 1'de prototiple ve doğrula.

## Tasarım dili
- Koyu arduvaz zemin, tek vurgu rengi (varsayılan Raspberry #E8326E). Tema
  seçilebilir olsun, renkler tamamen token tabanlı tanımlansın.
- Video üzerinde yüzen, yarı saydam paneller. Fare hareketsiz kalınca 2 saniye
  içinde tüm kontroller ve imleç kaybolsun.
- **Üst şerit:** büyük kare ikonlu tile butonlar (Dosya aç, Klasör aç, Altyazı,
  Görüntü & Ses, Ayarlar).
- **Alt bar:** ince vurgu renkli seek çizgisi ve tutamacı, ortada dairesel
  oynat/duraklat, önceki/sonraki, kare adımlama, oynatma listesi; solda
  bilgi ve ekran görüntüsü, sağda tekrar ve ses.
- **Ayarlar penceresi:** üstte sekme tile'ları (Genel, Video, Ses, Altyazı,
  Kısayollar), vurgu renkli kare checkbox'lar. Tüm kontroller tek bir
  ControlTheme ile stillensin.
- Animasyonlar kısa ve ölçülü (150–200 ms). Klavyeyle tam kullanılabilirlik
  ve yüksek DPI desteği zorunlu.

## Aşama 1: Temel oynatıcı
- Dosya ve klasör açma, sürükle-bırak, komut satırı argümanı, dosya
  ilişkilendirme için hazırlık.
- Oynat/duraklat, seek, kare adımlama, ses/sessiz, hız, tam ekran,
  sığdır/doldur.
- Ses, altyazı ve video parçası seçimi (`track-list` üzerinden), harici altyazı
  yükleme.
- Kısayollar: Space, ←/→ (ayarlanabilir atlama aralığı, varsayılan 10 sn),
  F/F11, M, fare tekerleği ile ses.
- Kaldığı yerden devam etme (`save-position-on-quit` / watch-later), klasördeki
  sonraki dosyayı otomatik açma.
- Kare yakalama (`screenshot video`), hedef klasör ayarlardan seçilebilsin.
- Overlay penceresi ve otomatik gizlenen kontroller.

## Aşama 2: SmartSeek ve oynatma listesi
- Seek çubuğunda fareyle gezerken küçük kare önizlemesi. Bunun için gizli, ikinci
  bir libmpv örneği kullan (`vo=null` değil, offscreen render veya screenshot ile),
  istekleri debounce et ve önbelleğe al. Ana oynatmayı asla yavaşlatmamalı.
- Yüzen ve sürüklenebilir oynatma listesi paneli.

## Aşama 3: Görüntü iyileştirme motoru
- **GPU algılama:** DXGI (`IDXGIAdapter1.GetDesc1`) ile üretici kimliği
  (NVIDIA 0x10DE, Intel 0x8086, AMD 0x1002) ve adaptör adı.
- **Katman 1, sürücü tabanlı:** NVIDIA RTX VSR
  (`d3d11vpp=format=nv12:scale=2:scaling-mode=nvidia`), Intel VSR
  (`scaling-mode=intel`) ve RTX Video HDR. Bunlar `hwdec=d3d11va` gerektirir.
- **Katman 2, shader tabanlı (her GPU):** `glsl-shaders` ile FSRCNNX, ArtCNN,
  Anime4K ve AMD FSR GLSL portu. Shader dosyaları uygulamayla birlikte gelsin ve
  lisansları THIRD_PARTY.md'ye eklensin.
- **Katman 3, akıcı hareket:** mpv `interpolation=yes` + `video-sync=display-resample`.
  SVP/RIFE (VapourSynth) entegrasyonu için yalnızca bir arayüz ve TODO bırak.
- **Preset'ler:** Kapalı / Dengeli / Anime / Maksimum, ayrıca kullanıcı tanımlı
  preset'ler (kaydet, sil, yeniden adlandır).
- **Otomatik düşüş:** En iyi kullanılabilir katmanı seç. Katman 1 yoksa veya
  uygulanamazsa sessizce katman 2'ye geç ve bunu logla.
- **Doğrulama:** RTX VSR, GPU zorlandığında hata vermeden devre dışı kalabiliyor.
  Filtre uygulandıktan sonra `vf` property'sini ve mpv log/event'lerini kontrol et;
  paneldeki "Aktif katman" etiketi gerçek durumu göstersin.
- **Uyumsuzluklar:** Birlikte çalışamayan seçenekleri (ör. d3d11vpp ile bazı
  çıkış modları veya NVIDIA Smooth Motion) UI'da birbirine kilitle ve nedenini
  tooltip ile açıkla.
- **Önce/sonra:** Bir kısayol basılı tutulduğunda iyileştirmeleri geçici olarak
  kapatıp aynı karede orijinal görüntüyü göster.

## Aşama 4: Ses kontrolleri
- Audio Boost (`volume-max=200`), isteğe bağlı `dynaudnorm` normalizasyonu.
- AC-3 DRC (`ad-lavc-ac3drc`), açılır listeden seçilebilir seviyeler.
- Stereo downmix: hoparlör diyagramlı bir UI. Kanal başına seviyeler
  `af=lavfi=[pan=stereo|...]` ile uygulansın.
- Ses gecikmesi ve altyazı gecikmesi ayarları.

## Aşama 5: Ayarlar, tema ve paketleme
- Ayarlar penceresi (Genel, Video, Ses, Altyazı, Kısayollar), tema seçimi,
  dil altyapısı (TR/EN).
- Kısayolların özelleştirilebilmesi.
- Portable zip ve MSIX ya da Inno Setup kurulum paketi.

## Kapsam dışı ve kesin kısıtlar
- DLSS 5 veya herhangi bir resmi olmayan, değiştirilmiş ya da imzasız NVIDIA
  runtime'ı kullanma, paketleme. DLSS oyunlara özel bir teknoloji; video
  karşılığı RTX VSR ve RTX Video HDR'dır.
- Sistem genelinde codec kurma veya DirectShow'a bağımlı olma.
- Offline AI "Remaster" (videoyu önceden işleyip önbelleğe alma) bu sürümde yok.
  İleride eklenirse ayrı bir yardımcı süreçte çalışmalı ve ayarlar önbellek
  anahtarının parçası olmalı. Şimdilik sadece genişletme noktası bırak.

## Test ve kalite
- Geliştirme makinesi GTX 1080 (Tensor çekirdeği yok): Katman 2 ve 3 burada
  test edilecek. Katman 1 kodu, RTX/Intel donanımı olmayan makinede güvenli
  şekilde devre dışı kalmalı ve çökmemeli.
- Test seti: H.264, HEVC 10-bit, AV1, VP9, HDR10 MKV, çoklu ses/altyazı parçalı
  MKV, ASS altyazılı anime, eski AVI/WMV/MTS, bozuk/kesik dosya.
- `Player.Core` ve `Player.Enhancement` için birim testleri (mpv'yi mock'layarak).
- Hiçbir mpv çağrısı UI thread'ini bloklamamalı, kapatmada handle'lar temiz
  şekilde serbest bırakılmalı.

## Teslimat beklentisi
Her aşamada: derlenen çözüm, README'de çalıştırma ve libmpv-2.dll edinme adımları,
kısa bir mimari notu ve bilinen sınırlamalar listesi.
