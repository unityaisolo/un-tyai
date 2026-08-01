# Kredi Sistemi Güvenlik Denetimi

**Tarih:** 2026-08-01 · **Kapsam:** `credits.ts`, `gate.ts`, `metering.ts`, `account.ts`
**Amaç:** Para işin içine girmeden önce (a) suistimal edilemez, (b) kullanıcıya şeffaf olduğundan emin olmak.

> Bu denetim, ödeme ve bulut adımlarının (M2, M3) **ön koşuludur.**

---

## Özet

| # | Bulgu | Etki | Durum |
|---|---|---|---|
| 1 | Kapı "herhangi bir anahtarı var" diye bedava geçiriyordu | Sınırsız bedava kullanım | **Düzeltildi** |
| 2 | Bilinmeyen model $0 sayılıyordu | Sistematik gelir kaybı | **Düzeltildi** |
| 3 | Ücretlendirme hatası sessizce yutuluyordu | Kayıp fark edilmiyor | **Düzeltildi** |
| 4 | Karşılanamayan kullanım iz bırakmadan siliniyordu | Ölçülemeyen zarar | **Düzeltildi** |
| 5 | Dosya tabanlı defter çok örnekli çalışmada bozuluyor | Ücretlendirme atlatılabilir | **Çözüldü** (Firestore transaction) |
| 6 | İşlem dökümü kalıcı değil, kullanıcı göremiyor | Güven / "dolandırıcı" algısı | **Çözüldü** (kalıcı defter) |
| 7 | Deneme kredisi hesap açarak sömürülebilir | Bedava kaynak | **Azaltıldı** (doğrulanmış e-posta) |
| 8 | Üyelikte üst sınır yok | Öngörülemeyen maliyet | **Çözüldü** (aylık tavan) |
| 9 | Akış ortasında kesme yok | Bakiyenin katı harcanabiliyor | **Çözüldü** |
| 10 | Borç hiç tahsil edilmiyordu | Kalıcı zarar | **Çözüldü** (yüklemede mahsup) |

---

## Düzeltilenler

### 1 · Bedava kullanım açığı (en ciddi)

`gate()` kullanıcının **herhangi bir** kayıtlı anahtarı varsa Kod Ajanı'nı bedava geçiriyordu.
Ama kayıtlı anahtar, isteğin gerçekten kullandığı sağlayıcıya ait olmayabilir.

**Sömürü:** kullanıcı işe yaramaz bir `custom` anahtar kaydeder → kapı "kendi anahtarı var,
bedava" der → istek bizim havuz anahtarımıza düşer → bakiye zaten `Math.max(0, …)` ile sıfırda
durduğu için borç da birikmez. **Sınırsız bedava kullanım.**

Muhasebe kararı artık isteği çalıştıran katmanın döndürdüğü `pooled` bilgisine dayanıyor;
`hasAnyOwnKey` yalnızca bir ön eleme olarak adlandırıldı ve bu sınır koda yazıldı.

### 2 · Bilinmeyen model $0

`priceFor()` tabloda olmayan model için `{input: 0, output: 0}` dönüyordu.

Bu, otomatik model seçimini eklediğimizden beri **istisna değil kural** hâline geldi: modeli
artık kullanıcının sağlayıcısı belirliyor, tablomuzda olmaması normal. Yani neredeyse her
istek $0 maliyetle kaydediliyordu.

Artık temkinli bir varsayılan uygulanıyor ($3 / $15 per 1M token, `NOVA_FALLBACK_PRICE_*` ile
ayarlanabilir). Hata yaparsak kendi lehimize değil, temkinli tarafa yapıyoruz.

### 3 · Sessiz ücretlendirme hatası

`charge()` içindeki `catch {}` hatayı yutuyordu — hizmet bedavaya gidiyor, kimse fark etmiyordu.
Artık `[BILLING]` etiketiyle hata seviyesinde loglanıyor. İstek yine bozulmuyor.

### 4 · Karşılanamayan kullanım

Bakiye 5 kredi, kullanım 500 kredi ise fark sessizce siliniyordu. Artık `debt` alanına
yazılıyor ve loglanıyor. Bir sonraki yüklemede mahsup edilmesi gerekiyor — **bu henüz yapılmadı.**

### 5 · Çok örnekli çalışma

Defter süreç belleğinde tutuluyor ve her değişiklikte dosyanın tamamı yeniden yazılıyor.
Tek süreçte güvenli (olay döngüsü tek iş parçacıklı, oku-değiştir-yaz arasında `await` yok).

**Cloud Run'da bozulur:** otomatik ölçekleme birden çok örnek açar, her birinin kendi bellek
kopyası olur, `persist()` son yazan kazanır. Kullanıcı paralel istekle ücretlendirmeyi
atlatabilir. Ayrıca Cloud Run diski kalıcı değil — örnek kapanınca defter tamamen kaybolur.

Backend artık `NOVA_CLOUD=true` + dosya defteri kombinasyonuyla **başlamayı reddediyor.**
Yanlış yapılandırmayla canlıya çıkma ihtimali kod seviyesinde kapatıldı.

---

## Açık kalanlar — canlıya çıkmadan önce yapılmalı

### 6 · İşlem dökümü (şeffaflık)

`getLedger` bellekte; sunucu yeniden başlayınca kayıtlar gidiyor ve kullanıcı ne için ne kadar
ödediğini göremiyor.

Senin "dolandırıcı yaftası yememeliyiz" endişenin tek panzehiri bu. Kullanıcı her satırı
görebilmeli: tarih, özellik, model, token, tutar. Kalıcı kayıt + Unity'de görünür bir ekran.

### 7 · Deneme kredisi sömürüsü

`SIGNUP_BONUS_CREDITS` her yeni `userId` için veriliyor. Firebase sınırsız e-posta kaydına izin
verir → hesap üreterek bedava kaynak alınabilir.

En az: e-posta doğrulaması zorunlu. Daha iyisi: bonusu ödeme yöntemi eklenince ver, ya da
tamamen kapat (`SIGNUP_BONUS_CREDITS=0`, şu anki varsayılan).

### 10 · İade politikası

Yazılı bir politika yok. Ödeme sağlayıcıları (Polar/Paddle) bunu şart koşuyor. M2 öncesi
belirlenmeli.

---

## Sonuç

Dört para kaybı düzeltildi ve yanlış yapılandırmayla buluta çıkma yolu kapatıldı.

**M2 (ödeme) ve M3 (bulut) için kalan zorunlu iş:** deneme kredisi suistimali (madde 7),
iade politikası, ve Firestore sürücüsünün gerçek Firestore'a karşı doğrulanması.

Bunlar bitmeden gerçek para akmamalı.

---

## Ek: 2026-08-01 ikinci tur

**Üyelik tavanı** eklendi (`MEMBER_MONTHLY_CREDITS`, varsayılan 20000 = $20/ay, 30 günlük
kayan pencere). Üyelik ilk başlarken dönem sıfırlanıyor — testte çıktı ki kullanıcının üye
olmadan önce harcadıkları da tavana sayılıyor, ödeme yapan kişi tavanı dolmuş başlıyordu.
Uzatmada sıfırlanmıyor, yoksa her uzatma tavanı yenileyerek suistimal edilirdi.

**Akış ortasında kesme** eklendi: `budgetUsd()` isteğin üst sınırını verir, `chat.ts` her
usage olayında anlık maliyeti karşılaştırır, aşılırsa akışı keser ve denetçi turunu
çalıştırmaz (aksi halde model ikinci kez çalışıp harcamaya devam ederdi).

**Borç mahsubu** eklendi: karşılanamayan kullanım bir sonraki kredi yüklemesinden düşülüyor.
Öncesinde `debt` yalnızca kaydediliyor, hiç tahsil edilmiyordu.

**Depolama sürücüye ayrıldı** (`creditstore.ts`): dosya ve Firestore. Firestore sürücüsü
`runTransaction` kullanır, yani oku-değiştir-yaz atomiktir ve çok örnekli çalışmada düşüm
kaybolmaz. Kredi API'si asenkron oldu — Firestore ağ üzerinden çalıştığı için senkron imza
sürdürülemezdi; sessizce eski değeri okumak ücretlendirmeyi kaybetmenin en kolay yoluydu.
`@google-cloud/firestore` opsiyonel bağımlılık ve dinamik yükleniyor: yerel kullanıcı kurmak
zorunda değil. Cloud Run'da kimlik doğrulama ADC ile otomatik, anahtar dosyası gerekmez.

**Doğrulama:** dosya sürücüsüyle beş senaryo + 20 paralel düşüm testi geçti (kayıp yok).
**Firestore sürücüsü gerçek bir Firestore'a karşı HENÜZ ÇALIŞTIRILMADI** — yalnızca derleniyor.
Bu, M3 deploy'unun ilk doğrulama adımı olmalı.

---

## Ek: işlem dökümü (madde 6)

`usagelog.ts` eklendi — ekle-only işlem defteri. Kayıtlar asla güncellenmez veya silinmez;
iade bile ayrı bir satırdır. Geçmişi değiştirebilen bir defter, defter değildir.

**Tek yazma noktası:** kayıt, paranın hesaptan çıktığı yerde (`gate.charge`) atılır.
Ücretlendirme ve kayıt ayrı çağrılsaydı biri başarılı diğeri başarısız olduğunda döküm ile
bakiye birbirini tutmazdı — itiraz anında hangisinin doğru olduğunu bilemezdik.

Yüklemeler ve üyelik de deftere giriyor. Yalnızca harcamayı gösteren tek taraflı bir döküm
güven vermez; kullanıcı gelen krediyi de görebilmeli.

Uçlar: `GET /v1/usage` ve `GET /v1/account/usage` (tarih, özellik, model, token, tutar + özet).

Saklama sürücü sözleşmesine alındı (`appendTx` / `readTx`): Firestore'da `novaCredits/{uid}/tx`
alt koleksiyonu, yerelde `~/.nova/usage.jsonl`. Sürücü yazamazsa yerele düşer — defterde
boşluk olmamalı. JSONL seçildi çünkü her satır bağımsız: bir satır bozulsa tüm döküm çökmüyor.

**Doğrulama:** dört senaryo geçti — kullanıcı ayrımı (b'nin kaydı a'ya sızmıyor), sıralama,
özet toplamları, süreç yeniden başladıktan sonra okunabilirlik ve bozuk satır dayanıklılığı.

**Kalan:** Unity tarafında bu dökümü gösteren ekran (M1e ile birlikte).

---

## Ek: deneme kredisi (madde 7)

Bonus artık yalnızca **e-postası doğrulanmış** hesaba veriliyor
(`SIGNUP_BONUS_REQUIRE_VERIFIED_EMAIL`, varsayılan açık). Doğrulama bilgisi Firebase
kimlik jetonundan (`email_verified`) geliyor, yani istemci taklit edemiyor.

**Bu tam bir çözüm değil** — tek kullanımlık e-posta servisleri var. Ama hesap üretmenin
maliyetini belirgin şekilde artırıyor. Gerçek savunma ödeme yöntemi doğrulaması; M2 ile
gelecek. Varsayılan bonus hâlâ 0, yani şu an risk zaten yok.

## Ek: iade politikası

Taslak yazıldı: `docs/IADE-POLITIKASI-TASLAK.md`. Kod değil, ticari karar —
dört maddede onay bekliyor. `logTx` içinde `refund` türü hazır: iade ayrı bir satır
olarak yazılıyor, geçmiş değiştirilmiyor.
