# İade Politikası — TASLAK (onayın bekleniyor)

> **Bu bir taslaktır, yürürlükte değildir.** Ödeme sağlayıcıları (Polar, Paddle) hesap
> açarken yayımlanmış bir iade politikası ister; onlar aynı zamanda *merchant of record*
> olduğu için kendi asgari kurallarını da dayatır. M2 (ödeme) öncesi senin onayın gerekiyor.
>
> Ben avukat değilim; bu metin ticari bir çerçeve önerisidir, hukuki görüş değildir.
> Yayımlamadan önce bir hukukçuya okutman doğru olur.

---

## Neden önemli

Kredi satan bir üründe iade politikası iki yönlü bir korumadır:

- **Kullanıcı için:** parasının karşılığını alamazsa ne olacağını önceden bilir.
- **Bizim için:** sınırsız iade talebi ve *chargeback* (kart itirazı) riskini sınırlar.
  Chargeback yalnızca parayı geri vermekle kalmaz, sağlayıcı ceza da keser ve oran
  yükselirse hesabımız kapatılabilir.

Senin "dolandırıcı yaftası almamalıyız" kaygının yazılı karşılığı bu belge.

---

## Önerilen çerçeve

### 1 · Kullanılmamış kredi — 14 gün içinde tam iade

Satın alma tarihinden itibaren 14 gün içinde, **hiç harcanmamış** kredi tam iade edilir.

*Gerekçe:* AB'de dijital ürünlerde 14 günlük cayma hakkı standarttır ve Türkiye'deki
mesafeli satış mevzuatı da benzer bir çerçeve öngörür. Bunu baştan tanımak, hem
sağlayıcının onay sürecini kolaylaştırır hem de itiraz oranını düşürür.

### 2 · Kısmen kullanılmış kredi — kalan bakiye iadesi

14 gün içinde, harcanan kısım düşülerek **kalan bakiye** iade edilir.

*Gerekçe:* İşlem dökümü (`usagelog`) sayesinde ne kadar harcandığını satır satır
gösterebiliyoruz. Kanıtı olan bir kesinti tartışma yaratmaz.

### 3 · Bizim hatamız — koşulsuz iade

Servis kesintisi, hatalı ücretlendirme veya üretilen çıktının teknik olarak bozuk olması
durumunda süre sınırı olmaksızın iade edilir.

*Gerekçe:* Bu bizim yükümlülüğümüz. Tartışmaya açmak itibar maliyeti doğurur ve
neredeyse her zaman iade tutarından pahalıya gelir.

### 4 · İade edilmeyen durumlar

- Kullanılmış kredi, 14 gün geçtikten sonra
- Sonuçtan memnun kalmama (yapay zekâ çıktısı öznel bir beğeni meselesidir)
- Kullanım koşullarının ihlali sonucu kapatılan hesaplar

*Gerekçe:* "Beğenmedim" gerekçesi sınırsız iade kapısı açar; model maliyeti bizde kaldığı
için sürdürülemez. Bunun yerine 3. maddeyi geniş yorumlamak daha adil.

### 5 · Üyelik

Aylık üyelik, sonraki dönem başlamadan iptal edilebilir. Başlamış dönem için oransal iade
yapılmaz; iptal, yenilemeyi durdurur.

*Gerekçe:* Sektör standardı ve sağlayıcıların beklediği model.

### 6 · Nasıl talep edilir

E-posta ile, hesap adresinden. **5 iş günü** içinde yanıtlanır. Onaylanan iade, ödemenin
yapıldığı yönteme yapılır.

---

## Uygulama için gereken kod

Politika kabul edilirse şunlar gerekiyor:

- `logTx` içinde **`refund`** türü zaten var — iade ayrı bir satır olarak yazılıyor,
  geçmiş değiştirilmiyor.
- İade veren bir yönetici ucu (`/account/refund`), `ADMIN_SECRET` korumalı.
- Kullanıcıya "iade talep et" bağlantısı (Unity hesap panelinde).

---

## Karar vermen gerekenler

| Konu | Öneri | Alternatif |
|---|---|---|
| Cayma süresi | 14 gün | 7 gün (daha dar, ama AB'de sorun çıkarabilir) |
| Kısmi kullanımda iade | Kalan bakiye iade edilir | Hiç iade yok (itiraz riski artar) |
| Üyelikte oransal iade | Yok | Var (daha cömert, gelir öngörülemez olur) |
| Yanıt süresi | 5 iş günü | 2 iş günü (daha iyi algı, operasyon yükü artar) |

Bu dört satıra karar verirsen metni son haline getirip `IADE-POLITIKASI.md` olarak
yayımlanabilir hale getiririm.
