# Nova Asset Pipeline — İsimlendirme + Metadata Şeması

## Altın ilke: DOSYALARI ELLE ADLANDIRMA
10 binlerce dosyayı elle isimlendirmek imkânsız ve kırılgan. Bunun yerine:
- **Orijinal dosyaları koru.**
- Bir **script** ile otomatik **`catalog.json`** üret → her asset'in yapılandırılmış metadata'sı. **World Builder beyni bu katalogu okur.**
- Her asset'e stabil bir **id (slug)** verilir: `pack_category_name`. Dosyayı değiştirmeden tutarlı "isim".

## Senin yapacağın TEK manuel iş: klasör düzeni + pack.json
```
assets-raw/
  kenney-nature-kit/
    pack.json            <- BİR kez doldur (aşağıda)
    *.glb  (veya alt klasörler)
  quaternius-modular-city/
    pack.json
    ...
```
Her paket için bir kez `pack.json` doldurursun (stil + lisans + kaynak). Gerisi otomatik.

### pack.json örneği
```json
{
  "pack": "kenney-nature-kit",
  "style": "low-poly",
  "license": "CC0",
  "source": "https://kenney.nl/assets/nature-kit"
}
```
> Lisans **CC0** veya açıkça **redistribution izinli** olmalı. Emin değilsen `"license": "UNKNOWN"` bırak — script bunları "review" işaretler, sen sonra elersin.

## catalog.json — script'in ürettiği her asset kaydı
| alan | açıklama | kaynak |
|---|---|---|
| `id` | `pack_category_name` (slug) | otomatik |
| `name` | orijinal dosya adı | dosya |
| `file` | göreli yol | dosya |
| `pack`,`style`,`license`,`source` | paket bilgisi | pack.json |
| `category` | nature / building / road / prop / vehicle / character / misc | klasör+isim kuralı |
| `type` | tree / house / barrel ... | isim kuralı |
| `tags` | anahtar kelimeler | isim+klasör |
| `sizeMeters` `{x,y,z}` | mesh sınır kutusu (m) | **mesh — otomatik** |
| `footprint` `{x,z}` | taban ayak izi (yerleştirme için) | mesh |
| `pivotBottom` | pivot tabanda mı (min.y≈0) | mesh |
| `triangles` | üçgen sayısı | mesh |
| `format` | glb / gltf | dosya |
| `review` | lisans UNKNOWN veya kategori misc → elle bak | otomatik |

## Kategori/tip kuralları (script içinde — genişletebilirsin)
- **nature:** tree, bush, rock, grass, plant, flower, log, stump, mushroom, cliff, hedge, fern
- **building:** house, wall, roof, door, window, tower, castle, hut, shop, building, stair, floor, pillar, column
- **road:** road, street, path, pavement, sidewalk, bridge, curb, crossing
- **prop:** barrel, crate, box, fence, lamp, bench, sign, table, chair, chest, pot, lantern, well, cart, ladder
- **vehicle:** car, truck, cart, boat, bike, wagon, plane, ship, tank
- **character:** character, npc, human, enemy, zombie, robot, animal, skeleton, knight, soldier

## Çalıştırma (Claude Code / terminal)
```bash
cd asset-pipeline
npm init -y
npm i @gltf-transform/core
node ingest.mjs ../assets-raw ./catalog.json
```
→ `catalog.json` üretilir. Konsolda kaç asset işlendiği + kaç tanesinin "review" olduğu yazar.

## Format notu
- Kural: paketleri **GLB/glTF** olarak indir (Kenney, Quaternius, Poly Pizza GLB verir). Script GLB/glTF okur, boyutu mesh'ten çıkarır.
- **FBX** gelirse: ya GLB'ye çevir (Blender/CLI) ya da Unity'ye alıp boyutu oradan çıkar (ileride ekleriz). Şimdilik GLB'ye odaklan.

## AI ile ince ayar (opsiyonel, 10k'da ucuz)
Kural tabanlı kategori çoğunu yakalar. Kalan belirsizler (`category=misc`) için LLM ile sınıflandırma (dosya adı + istersen thumbnail). Sadece belirsizlere → maliyet minimal.

## Firebase yapısı
- **Binary'ler → Firebase Storage:** `assets/<pack>/<file>` (script batch upload edebilir — sonraki adım).
- **catalog.json → Firestore:** koleksiyon `assets`, doküman id = `asset.id`. Batch write.
- Beyin sorgusu: Firestore'da `style` + `category` filtre → `footprint`'e göre parsele sığanları seç → yerleştir.

## World Builder beyni bunu nasıl kullanır
1. LLM plan: prompt → `{ style, districts, categoryDağılımı, density }`.
2. Motor: her parsel için → catalog'dan `style` + uygun `category` + parsele sığan `footprint` → rastgele-ağırlıklı seç → yerleştir (pivotBottom ile zemine otur).
3. Kullanıcı beğenmezse: o parçayı aynı kategoriden yenisiyle değiştir.
