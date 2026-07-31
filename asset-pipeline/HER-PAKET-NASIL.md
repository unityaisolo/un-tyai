# Her indirdiğin paket için yapılacaklar (kısa reçete)

Sadece 4 adım. **Dosyaları tek tek ADLANDIRMA yok.**

## 1) Klasör aç
`asset-pipeline/assets-raw/` altında paket için bir klasör:
- İsim kuralı: **küçük harf + tire**, "üretici-kitadı"
- Örnek: `kenney-city-kit`, `quaternius-ultimate-nature`, `polypizza-medieval`

## 2) İçine çıkar
İndirdiğin zip'i o klasöre çıkar. **GLB formatını tercih et.**
- Alt klasör olması sorun değil (script hepsini tarar).
- GLB dışı format (FBX/OBJ/DAE/STL) + fazla PNG script tarafından **görmezden gelinir** — silmek istersen sil, şart değil.

## 3) pack.json koy (o klasörün içine)
5 alan. Kopyala, değiştir:
```json
{
  "pack": "kenney-city-kit",
  "style": "low-poly",
  "license": "CC0",
  "source": "https://kenney.nl/assets/city-kit",
  "themes": ["city", "urban", "modern"]
}
```
- **pack:** klasör adının aynısı
- **style:** low-poly | stylized | realistic | voxel | pixel
- **license:** CC0 (emin değilsen `"UNKNOWN"` yaz → script "review" işaretler, sonra elersin)
- **source:** indirdiğin sayfanın linki
- **themes:** kullanıcı hangi kelimelerle isteyebilir? Ör. `["city","urban","modern"]`, `["medieval","village","fantasy"]`, `["desert","sci-fi"]`. **Beyin bu temalarla eşleştirecek.**

## 4) Tek komut (terminal, asset-pipeline klasöründe)
```
node ingest.mjs .\assets-raw .\catalog.json
```
→ TÜM paketleri yeniden kataloglar (`catalog.json`). npm kurulumu bir kere yeter.

## Bitti.
Her yeni pakette: **klasör aç → çıkar → pack.json → komut.** O kadar.

## İpuçları
- Bir paket "misc/review" çıkarsa: konsolda görünür. O kelimeleri bana yolla, `ingest.mjs`'e eklerim (ya da CATS'e sen ekle).
- Boyutlar/kategoriler otomatik; sen sadece `pack.json`'ı doğru doldur.
