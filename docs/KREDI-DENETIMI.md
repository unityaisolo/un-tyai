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
| 5 | Dosya tabanlı defter çok örnekli çalışmada bozuluyor | Ücretlendirme atlatılabilir | **Engellendi** (bulutta başlatmıyor) |
| 6 | İşlem dökümü kalıcı değil, kullanıcı göremiyor | Güven / "dolandırıcı" algısı | **Açık** |
| 7 | Deneme kredisi hesap açarak sömürülebilir | Bedava kaynak | **Açık** |
| 8 | Üyelikte üst sınır yok | Öngörülemeyen maliyet | **Açık** |

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

### 8 · Üyelikte üst sınır yok

`checkAccess`: `plan === "member"` ise kredi 0 olsa bile geçiyor; `chargeUsd` de sıfırda
duruyor. Yani **üye = sınırsız kullanım.** Tek bir ağır kullanıcı üyelik ücretinin kat kat
üstünde maliyet çıkarabilir.

Gereken: üyeliğe aylık kredi tavanı, tavan aşılınca ek kredi satın alma.

### 9 · Akış ortasında kesme yok

Kapı yalnızca istek **başında** bakiyeye bakıyor. 1 kredisi olan kullanıcı çok uzun bir akış
başlatıp bakiyesinin kat kat üstünde harcayabilir (bkz. madde 4 — artık en azından iz bırakıyor).

Gereken: akış sırasında token sayacı, tavan aşılınca akışı kes.

### 10 · İade politikası

Yazılı bir politika yok. Ödeme sağlayıcıları (Polar/Paddle) bunu şart koşuyor. M2 öncesi
belirlenmeli.

---

## Sonuç

Dört para kaybı düzeltildi ve yanlış yapılandırmayla buluta çıkma yolu kapatıldı.

**M2 (ödeme) ve M3 (bulut) için kalan zorunlu iş:** transaction destekli veritabanı (madde 5),
kalıcı işlem dökümü (madde 6), üyelik tavanı (madde 8), akış ortasında kesme (madde 9),
iade politikası (madde 10).

Bunlar bitmeden gerçek para akmamalı.
