# UnityAI — Yol Haritası (Unity içinde Cursor)

> Kod adı: **UnityAI** · Marka çalışma adı: **Nova**
> Vizyon: Unity editöründen **hiç çıkmadan** her şeyi yapan tam bir AI geliştirme ortamı —
> **kod yazar (Cursor gibi)**, **sahneyi yönetir (Coplay gibi)**, **3D üretir**,
> asset (texture, ses, skybox) üretir ve prodüksiyona hazırlar. Kullanıcı kendi API anahtarını
> bağlar veya bizim planımızı kullanır (abonelik + kullanım kredisi).

## Ürünün sütunları
1. **Kod (Cursor modu):** script oluştur/düzenle, diff onayı, derleme hatalarını okuyup düzelt, çoklu-dosya bağlam.
2. **Sahne/Editör (Coplay işi):** GameObject, bileşen, transform, prefab, sahne düzeni.
3. **Üretim Stüdyosu:** metin/görselden 3D → önizle → düzenle → sahnele. Dahası: rig, animasyon, texture, skybox, ses.
4. **Prodüksiyon:** prefab'lama, collider/LOD, mobil optimize, sahne/level otomasyonu.

## Sağlayıcı & model stratejisi (özet — detay: docs/STRATEGY.md)
- Beyin: **Gemini 2.5 Flash** · Kod: **DeepSeek V3** · 3D: **Tripo v2.5 (fal)** · Ücretsiz: **Ollama** · Premium: Claude/GPT
- Kullanıcıya **white-label "Nova"** isimleri (Nova Flash/Code/Pro/Local, "Nova 3D") — arkadaki model gizli, alias ile değiştirilebilir.
- **Council modu (kullanıcı tetikler):** beyin üretir, denetçi inceler. Para gelince denetçi→Sonnet, kod→Claude.
- **Ücret ilkesi:** yerel işlemler (scale/renk/transform) ücretsiz; yalnız üretim (fal) kredi harcar.

---

## Fazlar

### Faz 0 — Temeller ✓
- [x] Repo analizi, mimari, monorepo, dikey dilim MVP, SSE streaming

### Faz 1 — Çekirdek Agent ✓
- [x] 12 Unity aracı, çok adımlı agent döngüsü, onay modu, bağlam, maliyet göstergesi
- [x] Çok sağlayıcı: mock, OpenAI, Anthropic, Gemini, DeepSeek, Ollama; canlı test

### Faz 1.5 — Sağlayıcı & Marka ✓
- [x] DeepSeek sağlayıcısı (OpenAI-uyumlu), White-label Nova alias router
- [x] Council modu orkestrasyonu (backend, actor+denetçi)

### Faz 2 — 3D Stüdyo (fal) ✓
- [x] fal medya sağlayıcısı + `Generate3DModel` (text/image-to-3D → GLB)
- [x] **Tripo v2.5** (ucuz + tutarlı), modaliteye göre endpoint
- [x] glTFast import hattı (editörde kararlı)
- [x] **Önce önizle → beğenirsen sahneye ekle** akışı (izole önizleyici, sürükle-döndür/zoom)
- [x] Aşamalı progress göstergesi + sağ panelde otomatik model bilgisi (vertex/tri/mat/tex/boyut)
- [x] **Edit**: anında scale/renk (ücretsiz) + doğal dil "yeniden üret" (onaylı, kredili)
- [ ] **Poligon kontrolü**: üretimde `face_limit` (düşük-poli/mobil), `negative_prompt` (bozulma/eksik uzuv önleme), sağ panelde yüksek-poli uyarısı

### Faz 3 — Kod (Cursor modu) ✓ (temel)
- [x] `ReadScript` / `WriteScript`, satır-bazlı diff onayı (LCS), Kod paneli
- [ ] Çoklu-dosya bağlam + proje-genişliği düzenleme (ileri)

### Faz 4 — Tasarım & UX ✓
- [x] Sekmeli düzen [Sohbet | Kod | 3D Stüdyo], ayarlanabilir sunucu, "Yeni sohbet"
- [x] **Premium tasarım sistemi**: accent, yuvarlak kartlar, pill sekmeler, buton animasyonları
- [x] **Açık/Koyu tema** (kalıcı), sohbet balonları + fade-in animasyon

---

## ← ŞU AN (2026-07 PİVOT): Odak = ARAZİ/BIOME ÜRETİCİ. Şehir kurucu ASKIDA.

> **Pivot kararı:** Tek-tıkla şehir kurma erken aşamada rafa kalktı (asset kalitesine aşırı
> bağımlı; tutarlılık maliyeti yüksek). Kod duruyor (CityLayout, BuildOrganic, küratör beyin,
> AI görsel denetim) — ileride "birkaç tıkla oyun yapma" vizyonuyla geri açılabilir.
> Yeni odak: **Arazi/biome üretici** (Unity Terrain + kendi heightmap/splat/saçılım motorumuz)
> — açık dünya yapan herkesin işine yarar, asset bağımlılığı düşük, farkımız net.

### Faz T — Arazi Üretici (AKTİF ODAK)
- [x] T0: 10 biome (ova/orman/dağ vadisi/tepelik/sahil/çöl + kar/bataklık/kanyon/volkanik) + ırmak/göl kazma + su düzlemi
- [x] T1 (kısmi): ridge-noise gerçek dağlar + domain warping (valley/hills/snow) + kanyon terracing + volkan konisi; biome başına doku paleti ve asset yasak listeleri
- [x] T0: ambientCG doku katmanları (eğim/su-kıyısı splat kuralları), katalogdan bitki saçılımı
- [x] T0: menü sihirbazı (tip → bileşen → boyut), FPS gezinti, deterministik denetçi (SceneLint)
  - ⛔ **AI görsel denetim KALDIRILDI (2026-07-28).** Vision modeli JSON karar yerine `<think>` muhakeme metni döndürüyor, her kurulumda boşuna token harcayıp kullanıcıya anlamsız İngilizce "bulgu" gösteriyordu. Silinenler: `WorldQA.cs`, `world_opt_ai` toggle'ı, `aiReview` alanları, `POST /v1/world/review` ucu, 13 locale anahtarı. Sahne doğrulaması artık yalnız deterministik SceneLint.
  - Yan kazanım: `stripReasoning()` eklendi (backend) — küratör beyni de artık `<think>` bloğundan etkilenmiyor.
- [ ] T1 — Doğallık: domain warping + ridge noise dağlar, basit erozyon geçişi, daha iyi vadi tabanı
- [ ] T2 — Su: gerçek nehir spline'ı (genişleyen yatak, akış yönü), kıyı/köpük, şelale noktaları
- [ ] T3 — Gökyüzü: indirilen Poly Haven HDRI'ları Skybox/Panoramic ile kullan (şu an kullanılmıyor!)
- [ ] T4 — Bitki örtüsü v2: Unity Terrain detail/tree sistemine geçiş (GPU instancing, rüzgâr, çok daha yüksek yoğunluk)
- [ ] T5 — Patika/toprak yol: spline ile splat'e yol çizme + araziye oturan yol
- [ ] T6 — KALICILIK: Terrain + dokuları proje asset'i olarak kaydet (şu an oturum-içi; editör yeniden açılınca dokular gidiyor)
- [x] T7 — Oyuna hazırlık: tek tık NavMesh bake + güvenli oyuncu spawn noktası + üstten minimap PNG (Dünya sekmesi '🎮 Oyna hazırla' + PrepareForPlay aracı)
- [ ] T8 — Karma biome: tek haritada geçişler (dağ→orman→ova), biome maskeleri

### Faz G — Oyun Şablonları ("tek tıkla oynanabilir başlangıç") ⭐ YENİ
> Dünya sekmesi artık sadece FPS açık dünya değil; farklı oyun tipleri için oynanabilir başlangıç kurar.
- [x] G1 🏃 3D Sonsuz Koşu (Subway Surfers tarzı) — oyuncu + takip kamerası + prosedürel şerit/engel/coin + skor + game-over/restart. Engeller katalogdan, yoksa primitive. Runtime NovaRunner (BuildRunner aracı + UI butonu). A/D şerit, Space zıpla, R yeniden başla.
- [x] G2 🎯 FPS Arena / Dalga savunması — haritada düşman dalgaları (sayı+hız artan), raycast ateş, can/skor. NavMesh GEREKTİRMEZ (zemine raycast AI). Mevcut arazi varsa onu kullanır.
- [x] G3 🦘 3D Platformer — prosedürel platform dizisi (yanal salınanlar dahil), coin toplama, checkpoint'li düşme.
- [x] G4 🏁 Yarış / Drift — prosedürel kapalı pist (kıvrımlı spline) + arcade araç fiziği (gaz/fren/direksiyon, el freniyle drift), checkpoint'li tur süresi + en iyi tur, pist dışı cezası. Araç katalogdan (car/truck rolü).
- [x] G5 🗼 Kule Savunma — kıvrımlı düşman yolu + üs, yol kenarına fareyle kule kurma (altın ekonomisi), menzilli otomatik ateş (en ileri hedefi vurur), artan dalgalar, izometrik kamera. NavMesh GEREKTİRMEZ.
- [ ] G6 2D şablonları (Flappy / 2D platformer / 2D runner) — ayrı 2D asset+fizik hattı gerekir

#### 📝 Oyun şablonları — revizyon notları (ileride ince ayar)
> Şablonlar çalışıyor; bunlar "daha iyi hissettirme" işleri, blocker değil.
- **Kule Savunma:** ekonomi dengesi ilk turda sıkıydı (240 altın / 40 maliyet / +60 dalga bonusu ile açıldı) — uzun oyunda tekrar bakılmalı. Kule çeşidi (menzil/hasar/yavaşlatma), kule satma/yükseltme, dalga önizlemesi yok.
- **Yarış:** drift tutuşu (`driftGrip`) ve kamera mesafesi oynanışa göre ayarlanabilir; sıralama/rakip AI, tur sayısı hedefi, pist bariyerleri yok.
- **FPS Arena:** silah çeşidi/şarjör, düşman türleri (menzilli/hızlı), can toplama yok. Dalga zorluk eğrisi test edilmeli.
- **Platformer:** zıplama mesafesi ile platform aralığı (`gapMin/Max`) birlikte ayarlanmalı; hareketli platformların hızı zorluğu çok değiştiriyor.
- **Sonsuz Koşu:** hız artış eğrisi ve engel sıklığı; güçlendirme (mıknatıs/kalkan) yok.
- **Genel:** ses efekti/müzik hiçbirinde yok. Skor kaydı (high score) kalıcı değil. Şablonlar prefab olarak kaydedilmiyor (sahnede yaşıyor).

### Faz E — Editör Otomasyonları ("günler süren iş → birkaç tık" serisi)
> Piyasa raporu (docs/PIYASA-ARASTIRMASI.md) sıralamasıyla. Her biri ayrı video/duyuru konusu.
- [x] E1 ⭐ Akıllı Sahne Dekoratörü — "buraya kamp alanı kur"; doğal dil → plan beyni → küratör → yerleştirme (DecorateArea aracı). v3: düzenleme (EditDecor: kaldır/çeşitle/seçili parçayı değiştir)
- [x] E2 ⭐ Sahne Sağlık Botu v2 — HER Unity sahnesinde çalışır (Nova-bağımsız). Kayıp script/materyal/mesh, bozuk ölçek, sıfır collider, boş nesne, çoklu AudioListener, dev doku (>2048px), havada asılı/renksiz modeller + tek tık güvenli düzeltme (Undo'lu). "neden kasıyor / optimize et" cevabı
- [x] E3 Işık & Atmosfer v1 — sis/korku + şafak presetleri (v2: HDRI + post-processing + lightmap önerisi)
- [x] A9 ⭐ URP Göç Asistanı v1 — Standard/pembe materyalleri tara → tek tık URP/Lit'e çevir (renk/doku/metallic/normal/emisyon eşlenir); özel shader'lar kod ajanına devredilir. (HDRP maintenance mode 2026-02 → taze pazar penceresi, rakipsiz)
- [ ] E4 Tek Tık Prefab Fabrikası — collider + LOD + ölçek + prefab düzeni
- [ ] E5 Doğal Dilden NPC Davranışı — NavMesh + FSM üretimi
- [ ] E6 Doğal Dilden UI Üretici — menü/ayarlar/duraklat ekranları
- [ ] E7 Mikro-Animasyon Asistanı — tween/Animator doğal dille
- [ ] E8 Optimizasyon Asistanı — profil oku, onaylı otomatik düzelt

### Faz B — Beta dağıtımı (GitHub URL + Firebase asset bulutu)
- [x] **B1 Katalog yolu taşınabilir** — sabit `E:\...\catalog.json` kaldırıldı. `NovaAssetLibrary` 5 kademeli çözüm yapar (kayıtlı yol → `<Proje>/NovaAssets` → `Assets/NovaAssets` → projenin yanı → paketin yanı) ve seçimi EditorPrefs'te saklar. `UnityAI ▸ Asset Kütüphanesi…` menüsünden elle seçilebilir.
- [x] **B2 Kütüphane yoksa çökmez** — arazi/oyun şablonları basit geometriyle yine kurulur (`WarnIfMissing`), tamamen katalog bağımlı akışlar (şehir, dekoratör) nazikçe durur. 13 yeni `lib.*`/`dl.*` locale anahtarı × 4 dil.
- [x] **B3 Bulut asset dağıtımı** — backend `GET /v1/assets/manifest` (env: `NOVA_ASSET_CATALOG_URL`, `NOVA_ASSET_BASE_URL`, `NOVA_TEXTURES_ZIP_URL`). Plugin `catalog.json`'u bir kez, **GLB'leri talep üzerine (lazy)** `<Proje>/NovaAssets/assets-raw` altına indirir → kullanıcı GB'larca dosya çekmez. `UnityAI ▸ Kütüphaneyi Buluttan İndir`. UPM paketi salt okunur olduğu için indirmeler asla paket klasörüne yazılmaz.
  - URL biçimi: `NOVA_ASSET_BASE_URL` ya yol biçimi (`https://cdn/…/assets-raw/`) ya da `{file}` şablonu (`https://…/o/assets-raw%2F{file}?alt=media` — Firebase v0 API) olabilir; plugin ikisini de kurar.
  - Güvenlik: katalogdan gelen dosya adları mutlak yol/`..` içeremez, indirme kökü dışına yazamaz; doku zip'i zip-slip korumalı açılır.
- [x] **B4 Firebase kurulumu TAMAM** (2026-07-28) — bucket `unityai-dd9c1.firebasestorage.app` · **europe-west1** · Blaze. 1863 dosya / 1.8 GiB + `catalog.json` + `textures-raw.zip` (393 MB) yüklendi, `Cache-Control: max-age=31536000`. Storage kuralı **en dar yetki**: yalnız `catalog.json`, `assets-raw/**`, `textures-raw.zip` herkese açık OKUNUR; yazma tamamen kapalı. Rules Playground'da 3/3 doğrulandı (izinli okuma ✓, izinsiz yol 🚫, yazma 🚫). Yükleme scripti: `asset-pipeline/upload-firebase.ps1`.
  - Maliyet gerçeği: GCS "Always Free" 5 GB **sadece ABD bölgelerinde**. europe-west1'de depolama ~$0.05/ay + indirme $0.12/GB (~100 kullanıcı ≈ $0.65/ay). Düşük gecikme için bilinçli tercih.
  - ⚠ Açık madde: Google Cloud Billing → Budgets & alerts'te $5 bütçe uyarısı kurulmalı.
- [x] **B7 Teşhis: model kaynağı** — her kurulumda Console'a `[Nova] Katalog: <yol>` ve `[Nova] Model kaynağı: N yerel klasör · N indirme önbelleği · N BULUTTAN indirildi` yazılır. Beta destek sorularının çoğu bu iki satırla çözülür.
- [x] **B8 EditorPrefs sızıntısı** — katalog yolu Unity'de PROJE değil KULLANICI bazlı saklanır; bir projede bulunan yol tüm projelere sızıyordu ve temiz kurulum testini imkânsız kılıyordu. `UnityAI ▸ Asset Kütüphanesini Sıfırla` menüsü eklendi.
- [ ] B5 KURULUM.md — git URL (`?path=unity-plugin`), glTFast/Input System bağımlılıkları, backend adresi + token, DX11 notu
- [ ] B6 Sürüm etiketi kontrolü — `NOVA_ASSET_VERSION` değişince yerel katalogu otomatik yenile

### Faz K — Kullanıcı kendi API anahtarını getirir (BYO) ✓ 2026-07-28
> **Mimari karar:** beta'da backend KULLANICININ kendi makinesinde çalışır. Anahtarları kendi
> diskinde kalır, biz hiç görmeyiz — hukuki/güvenlik/fatura yükü sıfır. Ücretli havuz modeli
> (hosted) Faz 5'e bırakıldı; o senaryoda havuz anahtarları YALNIZCA sunucu ortam
> değişkenlerinde bulunur ve hiçbir uç istemciye anahtar döndürmez.

- [x] **K1 Kasa kalıcı + gerçekten şifreli** — eskiden `new Map()` (her restart'ta anahtarlar uçuyordu) ve sır repoda yazılı `"dev-only-insecure-secret"` idi. Artık `~/.nova/vault.json` + ilk açılışta üretilen rastgele `vault.key`, ikisi de `0600`, dizin `0700`. Atomik yazım (tmp+rename).
- [x] **K2 BYO zorunlu** — `resolveKey` anahtar yokken sessizce `.env` havuzuna düşüyordu. Artık `null` dönüyor ve kullanıcı sağlayıcı adı + anahtar alma linki içeren yönlendirici hata görüyor. Havuz yalnız `ALLOW_POOL_KEYS=true` ile.
- [x] **K3 Özel / açık kaynak endpoint** — `custom/<model>` önekli modeller kullanıcının girdiği base URL'e gider. Together, Fireworks, DeepInfra, Cerebras, Nebius, vLLM, LM Studio, llama.cpp, Azure OpenAI — tek sağlayıcıyla hepsi.
- [x] **K4 Rol bazlı model** — beyin / kod / görsel / küratör ayrı ayrı seçilir. Dağınık 6 çözüm noktası tek `resolveTarget()` fonksiyonuna indirildi; yeni özellik eklenirken bir akış atlanamıyor.
- [x] **K6 Servis kataloğu + tek adımda kurulum** — 18 hazır servis, adresleri **doğrulanmış** olarak gelir; kullanıcının base URL bilmesi gerekmez. Açık kaynak sunucuları (Together, Fireworks, Cerebras, DeepInfra) ve **Asya sağlayıcıları (Alibaba Qwen uluslararası + Çin, Moonshot/Kimi)** dahil; yerel için Ollama / LM Studio / vLLM; bilinmeyenler için "Diğer".
  - **Çözülen kritik akış hatası:** kullanıcı OpenAI anahtarını kaydedip "kaydedildi" görüyor ama beyin hâlâ varsayılan Groq modelini kullanıyordu ("kaydettim, hiçbir şey değişmedi"). Yeni `POST /v1/settings/setup` anahtar + adres + model seçimini AYNI anda yazar; "Ana model olarak kullan" varsayılan işaretli.
  - `custom/` ve `ollama/` yönlendirme önekleri kullanıcıdan istenmez, backend ekler.
  - Emin olunmayan model adları UYDURULMAZ: yalnız projede gerçekten kullanılan adlar önerilir, diğerlerinde "servisin panelinden kopyala" ipucu verilir.
  - ⚠ Bilinen sınır: aynı anda **tek** özel endpoint (ortak `customBaseUrl` + `custom` anahtarı). Qwen ve Together'ı birlikte kullanmak için ileride servis başına kayıt gerekir.
- [x] **K5 Ayarlar sekmesi (Unity)** — gruplu anahtar alanları (ücretsiz / güçlü / özel / yerel / 3D), maskeli gösterim, kaydettikten sonra alan temizlenir, sil onayı, rol→model dropdownları (indeks tabanlı → dil değişince bozulmaz), bağlantı testi. 60 yeni locale anahtarı × 4 dil.

**Güvenlik sözleşmesi (kodda uygulanıyor, testle doğrulandı):** hiçbir uç anahtarın tam değerini döndürmez; maske yalnız kullanıcının KENDİ anahtarı için üretilir; havuz anahtarları için sadece `poolAvailable` boolean'ı döner; test hatalarında anahtar deseni `***` ile maskelenir; `.gitignore`'da `vault.json`, `vault.key`, `.nova/`.

**Doğrulama:** çalışan sunucuya karşı 12 uçtan uca test (anahtar yaz/oku/sil, kısa anahtar reddi, maske, kullanıcı izolasyonu, geçersiz URL reddi, BYO hata mesajı) + sahte OpenAI-uyumlu sunucuyla özel endpoint zinciri (doğru yol, `custom/` öneki ayıklandı, kullanıcının anahtarı gönderildi) + kasanın 11 birim testi. Hepsi PASS.

### Faz M — Para modeli ve erişim politikası (KARAR: 2026-07-30)

**Ürün kararı — hangi özellik neyle çalışır:**

| Özellik | Erişim |
|---|---|
| **Kod Ajanı** (sohbet + araçlar) | Kullanıcı KENDİ anahtarını bağlar (ücretsiz) **veya** Nova kredisi harcar |
| **3D Stüdyo** | Yalnızca **Nova Cloud + üyelik** |
| **Malzeme** | Yalnızca **Nova Cloud + üyelik** |
| **Dünya / Oyun üretici** | Yalnızca **Nova Cloud + üyelik** |

**Mimari — İKİ ADRES:**
- Kod Ajanı → kullanıcının **yerel backend'i** (`UnityAIConfig.BaseUrl`, varsayılan `localhost:8787`). Anahtarları kendi diskinde; biz hiç görmeyiz.
- 3D / Malzeme / Dünya → **Nova Cloud** (plugin'de gömülü sabit URL). Havuz anahtarları YALNIZCA bulut sunucusunun ortam değişkenlerinde; istemciye asla gitmez, pakete girmez.
- Kullanıcı yerel backend kurmasa bile bulut özellikleri çalışır (onboarding kolaylığı).

**Yapılacaklar (sırayla):**
- [x] M0 Ayarlar sekmesi → pencere **üç nokta menüsüne** taşındı (`IHasCustomMenu`). Anahtar bir kez girilir; sekme çubuğunda sürekli yer kaplaması gereksizdi.
- [x] M0b UI taşma düzeltmesi — header/sekme çubuğu/composer `flex-shrink: 0`, panel `min-height: 0`. Pencere kısaldığında sekmeler logonun hizasına biniyor, mesaj balonu sekmelerin arkasında kalıyordu.
- [x] **M1a Kimlik doğrulama (backend)** — `lib/idtoken.ts`: Firebase ID token'ı **sıfır yeni bağımlılıkla** doğrular (node:crypto + Google'ın açık x509 sertifikaları). `firebase-admin` kurmaya gerek yok. Doğrulanan 7 şey: imza, `alg=RS256` (alg-none saldırısına kapalı), `iss`, `aud`, `exp`, `iat`, `sub`. Auth middleware'e 3. mod olarak eklendi; doğrulanmış uid gerçek kimlik olur (`req.authed`).
- [x] **M1b Kredi + üyelik defteri** — `lib/credits.ts`: kalıcı `credits.json` (0600), 1 kredi = 0.001 USD (tam sayı — yuvarlama aşınması yok), plan `free`/`member`, süresi geçen üyelik otomatik düşer.
- [x] **M1c Erişim kapısı** — `lib/gate.ts`: world/studio/material **yalnız kredi**; chat kendi anahtarı varsa **bedava**, yoksa kredi. Yerel modda (`NOVA_CLOUD` boş) kapı tamamen açık. 8 uca bağlandı (world ×4, studio ×3, chat ×1). Kullanım sonrası `charge()` yalnız `pooled=true` iken düşer — kullanıcının kendi anahtarı harcanınca kredi gitmez.
- [x] **M1d Hesap uçları** — `GET /v1/account` (plan, kredi, harcama), `POST /v1/account/grant` (`x-admin-secret`, sabit süreli karşılaştırma). Ödeme webhook'u bu ucu çağıracak.
- [ ] **M1-DENETİM ⚠ PARA KRİTİK — canlıya çıkmadan önce zorunlu.** Kredi sistemi hem saldırıya hem "dolandırıcı" algısına açık; şunlar tek tek doğrulanmalı:
  - Kredi düşme **atomik** mi? Şu an `credits.json` dosyaya yazıyor — eşzamanlı iki istek aynı bakiyeyi okuyup üzerine yazabilir (race → bedava kullanım). Tek süreçte sıralı, ama çok işlemli/çok sunuculu dağıtımda **veritabanı + transaction** şart.
  - Akış yarıda kesilirse ücret alınıyor mu? Kullanıcı iptal ederse/hata olursa **ücret alınmamalı** (şu an `charge` yalnız başarılı `recordUsage` sonrası — doğrulanmalı).
  - Fiyat tablosu eskimiş model için $0 sayıyor → **bizim zararımıza**. Bilinmeyen modelde muhafazakâr varsayılan fiyat konmalı.
  - Kullanıcı **ne için ne kadar ödediğini görebilmeli** (işlem dökümü). Şeffaflık, "dolandırıcı" algısının tek panzehiri.
  - İade/anlaşmazlık politikası + kullanım kaydının kullanıcıya açık olması.
  - `ADMIN_SECRET` sızarsa sınırsız kredi yazılır → sır rotasyonu + grant için ayrı log.
- [ ] M1e Unity tarafı: giriş ekranı (Firebase REST ile e-posta/şifre + Google), kredi göstergesi, 402/401 yanıtlarında yönlendirme
- [ ] M2 Ödeme — MoR (Polar.sh / Paddle; Stripe TR'de yok)
- [ ] M3 Nova Cloud deploy + `CloudUrl` sabiti + bulut özelliklerinin o adrese yönlendirilmesi

  **KARAR (2026-07-31): HİBRİT mimari.** Backend **Google Cloud Run**'a çıkar (Firebase
  Hosting yapamaz — o statik dosya sunar; bizimki sürekli çalışan Node + SSE akışı).
  Aynı Firebase/GCP projesi, boştayken sıfıra iner, alan adı zorunlu değil.

  Anahtarlar nerede durur:
  - **Bizim anahtarlarımız** → yalnızca Cloud Run ortam değişkenlerinde. Kullanıcı hiçbir
    kurulum yapmaz, terminal görmez. 3D / Malzeme / Dünya buradan çalışır (üyelik + kredi).
  - **Kullanıcının kendi anahtarı → YEREL KALIR.** Buluta ASLA gönderilmez. Kendi
    anahtarını bağlamak isteyen yerel sunucuyu kurar (`npm run dev`).

  Neden: bulutta BYO anahtar saklarsak binlerce kullanıcının API anahtarını biz tutarız —
  sızıntı = onların faturası, üstüne KVKK/GDPR yükümlülüğü. Ayrıca KURULUM.md'deki
  "anahtarların bilgisayarından çıkmaz" vaadi yalan olurdu. Bedeli: iki kurulum yolu ve
  iki ayrı dokümantasyon dalı — kabul edildi.

  Yapılacaklar: Dockerfile · Cloud Run servisi (min-instances=0) · `NOVA_CLOUD=true` +
  havuz anahtarları yalnızca orada · SSE'nin Cloud Run üzerinden aktığının doğrulanması ·
  plugin'de `CloudUrl` sabiti · KURULUM.md'nin "bulut (kurulum yok)" ve "yerel (kendi
  anahtarım)" olarak ikiye ayrılması.

- [ ] **M3b Yerel kurulumda `npm run dev` sürtünmesi.** Kendi anahtarını kullanacak
  kullanıcıya "terminal aç, komut yaz, pencereyi kapatma" demek beta için tamam, genel
  yayın için değil. Seçenek: eklenti Unity açılınca backend'i arka planda kendisi
  başlatsın (Node yine gerekir ama terminal görünmez). M3'ten sonra değerlendirilecek.
- [ ] M4 Sohbette "kendi anahtarım / Nova kredisi" seçimi + kredi göstergesi (M1 olmadan sadece görsel kalır)
- [ ] M5 Bulut özelliklerinde üyelik yoksa net yönlendirme ("Dünya üreteci Nova üyeliği gerektirir")

### [ASKIDA] Eski plan: Faz 5 (para altyapısı) — pivot sonrası yeniden tarihlenecek

### Faz 5 — Monetizasyon
- [ ] Auth: **Firebase Auth** (email + OAuth), hesap paneli
- [ ] **MoR ödeme (Polar.sh/Paddle)**: abonelik + kredi, Türkiye payout; webhook → Firestore
- [ ] **Firestore** hesap/kredi kayıtları; kredi-gating (üretimden önce bakiye kontrolü), kota/rate-limit, BYO-key paneli

### [ASKIDA — 2026-07] Faz 5.5 — Prompt → Oynanabilir Şehir (World Builder) ⭐⭐ MOONSHOT
> "bana modern bir şehir tasarla" → 2-3 dk'da gezilebilir şehir. Yöntem: her şeyi AI'ya
> ürettirmek DEĞİL; CC0/redistribution-lisanslı **modüler kit kütüphanesi** + AI'nın plan
> yapıp **kısıtlı yerleştirmesi**. (Ödeme/Firebase'den sonra; piyasada gerçek rakibi yok.)
- [ ] **Lisans temeli:** yalnız redistribution/SDK izinli varlıklar (CC0 en güvenli) — hukuki zemin önce
- [ ] **Depolama mimarisi:** metadata/katalog Firestore'da, GLB binary'ler CDN/Object Storage'da (Firebase Storage/R2/S3)
- [ ] **Metadata şeması:** kategori, stil, boyut/ayak izi, pivot, yön, socket/grid, tileable
- [ ] **Yerleştirme motoru** (işin kalbi): ızgara/parsel → yol ağı → bina → ağaç/prop, çakışmasız & bağlantılı
- [ ] **LLM plan üretimi:** prompt → yapılandırılmış plan JSON (stil, yoğunluk, mahalle/kategori dağılımı)
- [ ] **Oynanabilirlik:** collider + NavMesh bake + basit player controller + spawn + skybox/ışık
- [ ] **Performans:** GPU instancing + LOD + static batching (yüzlerce modül için şart)
- [ ] **Parça değiştirme UX:** beğenmediğin binayı/arabayı tek tık kategoriden yenisiyle değiştir
- [ ] MVP: tek stil kiti + küçük kasaba → kanıtla → kütüphaneyi büyüt → şehir
> Bağımlılık: Faz 9 kalıcılık/prefab'lama + poligon kontrolü (bitti) + Faz 7.5 kit altyapısı

### Kapsam dışı — Karakter Rigleme (bilinçli karar)
> AI auto-rig hem güvenilmez hem azınlık ihtiyaç. Enerjiyi kod + harita + asset'e veriyoruz.
> Kullanıcılar rigli karakter için hazır asset (Mixamo/Asset Store) kullanır. İleride proje
> çok tutar ve talep gelirse yeniden değerlendiririz. (Rig UI ve araçları üründen kaldırıldı.)

### Faz 7 — Asset Üretimi (3D'nin ötesi)
- [ ] **Texture / PBR** üretimi ve yeniden dokulama ("paslı metal yap")
- [ ] **Skybox / HDRI** üretimi (ortam ışığı dahil)
- [ ] **Ses**: SFX, ayak sesi, ambiyans, UI sesi, NPC seslendirme, müzik
- [ ] Stüdyo'ya "Asset" sekmesi: tür seç, üret, önizle, projeye ekle

- [ ] **Yüzey Boyama (mask / lokal retexture)** — tek-parça mesh'te bölgesel malzeme; UV/mask tabanlı boyama aracı (ileri, ertelenen)

### Faz 7.5 — Asset Marketplace ⭐ (güçlü gelir kalemi)
> Kullanıcıların editörden çıkmadan kredi karşılığı kullanabileceği CC0/redistribution-lisanslı
> asset kütüphanesi: prop'lar, çevre kitleri, texture/malzeme, skybox, ses. World Builder'ı besler.
- [ ] Katalog + metadata şeması (kategori, stil, boyut, etiketler)
- [ ] Depolama: metadata Firestore, binary'ler CDN/Object Storage
- [ ] Editör içi mağaza: ara, önizle, krediyle projeye ekle
- [ ] Lisans: yalnız redistribution/SDK izinli (CC0 en güvenli)
- [ ] World Builder ile ortak asset havuzu

### Faz 8 — Kod Copilot (ileri)
- [ ] **Console hatası → otomatik düzeltme döngüsü** (oku → diff öner → uygula)
- [ ] Gameplay şablonları: player controller, can sistemi, envanter, diyalog, kayıt
- [ ] **Shader üretimi** (Shader Graph / HLSL), editör aracı script'leri, test üretimi

### Faz 9 — Sahne + Prodüksiyon
- [ ] **Prefab'lama**: üretilen modeli prefab + collider + LOD + import ayarları (production-ready)
- [ ] **Mobil optimize**: mesh sadeleştirme (LOD), texture sıkıştırma, atlas
- [ ] Prosedürel sahne/level ("bir köy kur", prop yerleştir), terrain üretimi
- [ ] Otomatik ışık, NavMesh bake, collider otomatik oturt; toplu işlem ("tüm ağaçlara LOD")
- [ ] Performans/draw-call analizi + öneri, kullanılmayan asset temizliği

### Faz 10 — İş Akışı & Lansman
- [ ] Sesli komut, "projemi/şu script'i açıkla", otomatik dokümantasyon, "nasıl yaparım" yardımı
- [ ] Unity Asset Store + OpenUPM, dokümantasyon, Discord, telemetri

---

## Araç kataloğu
**Mevcut:** CreateGameObject, CreatePrimitive, DeleteGameObject, SetTransform, AddComponent,
SetComponentProperty, InstantiatePrefab, ReadSceneHierarchy, ReadConsoleLogs, Generate3DModel, ReadScript, WriteScript,
ListPlacedAssets, RemovePlacedAssets, BuildTerrain, ScanScene, DecorateArea, EditDecor, MigrateToURP, PrepareForPlay, BuildRunner, BuildGameTemplate, AskUser

**Hedef (yeni fazlar):** AutoRig, GenerateAnimation, RetargetAnimation, GenerateTexture, RetextureModel,
GenerateSkybox, GenerateSFX, GenerateMusic, GenerateVoice, FixConsoleErrors, GenerateShader,
GenerateGameplayTemplate, MakePrefab, GenerateLOD, OptimizeForMobile, GenerateScene, BakeNavMesh, AutoCollider
