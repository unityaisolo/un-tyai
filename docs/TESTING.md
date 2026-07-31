# Test Rehberi

> Not: Sahte (mock) sağlayıcı bilinçli olarak kayıtlı DEĞİL — anahtar yoksa backend
> uydurma cevap üretmez, net hata döner. Anahtarsız test için bölüm D (Ollama).

## A) Groq ile hızlı backend testi (varsayılan beyin)

Anahtarını sohbete yapıştırma; sadece kendi makinende `.env`'e koy.

```bash
cd backend
cp .env.example .env
# .env içine ekle:  GROQ_API_KEY=SENIN_ANAHTARIN
npm install
npm run dev            # http://localhost:8787
```

Tek adımlı test:
```bash
curl -N -X POST http://localhost:8787/v1/chat -H 'Content-Type: application/json' \
  -d '{"model":"nova-flash","messages":[
        {"role":"user","content":"sahneye bir küp ekle"}]}'
```
Beklenen: token akışı + `tool_call` (CreateGameObject) + `usage` + `billing` olayları.

Alternatif sağlayıcılar: `.env`'e `GEMINI_API_KEY` koyup model olarak
`gemini-2.5-flash` (veya alias `nova-gemini`), `DEEPSEEK_API_KEY` ile `nova-code`.

> Not: Anahtar `.env`'de olduğu için istekler "havuz" sayılır ve komisyon uygulanır
> (billing olayındaki `commissionUsd`). Kullanıcının kendi anahtarını BYO olarak
> kaydetmek istersen: `POST /v1/keys {"provider":"gemini","apiKey":"..."}` — o zaman komisyon uygulanmaz.

## B) Unity içinde tam test (agent döngüsü)

1. `backend` çalışıyor olsun (yukarıdaki gibi).
2. Unity 2022.3+ projesinde `unity-plugin/` klasörünü `Packages/` altına kopyala
   (veya Package Manager → "Add package from disk" → `unity-plugin/package.json`).
3. Menü: **Window → Nova · UnityAI** (kısayol Ctrl/Cmd+G).
4. Beyin sabittir: `nova-flash` (Groq). Model açılırı UI'dan kaldırıldı;
   farklı model denemek için backend'e curl ile istek at (bölüm A).
5. Dene:
   - "sahneye 3 katlı bir kule yap" → çok adımlı döngü: her adımda bir küp oluşur.
   - "seçili nesneye Rigidbody ekle ve kütlesini 5 yap" → AddComponent + SetComponentProperty.
   - "sahne hiyerarşisini oku" → ReadSceneHierarchy.
   - "Assets/Scripts/Spin.cs adında döndüren bir script yaz" → WriteScript (onay ister).

### Onay modu
- **Otomatik onay** kapalıyken yıkıcı aksiyonlar (DeleteGameObject, WriteScript) onay diyaloğu açar.
- Açıkken hepsi otomatik çalışır. Tüm oluşturma/değişiklik aksiyonları **Undo** ile geri alınabilir (Ctrl+Z).

### Maliyet
- Pencerenin sağ üstündeki `$0.0000` göstergesi thread boyunca biriken maliyeti gösterir.

## C) Anahtarsız test
Mock sağlayıcı kaldırıldı (kullanıcıya sahte cevap göstermeme ilkesi).
Anahtarsız/ücretsiz test için bölüm D'deki Ollama yolunu kullan.

## D) Ollama ile ücretsiz yerel test (önerilen)

Hiçbir API ücreti yok, internet gerekmez. Araç çağırma destekli model gerekir (llama3.1, qwen2.5).

1. Ollama'yı kur: https://ollama.com/download
2. Araç destekli bir model indir (terminal):
   ```
   ollama pull llama3.1
   ```
   (Alternatif: `ollama pull qwen2.5`)
3. Ollama arka planda çalışır (varsayılan http://localhost:11434). Kontrol:
   ```
   ollama list
   ```
4. Backend'i yeniden başlat (yeni sağlayıcı kodu için): 1. terminalde Ctrl+C → `npm run dev`
5. Testi Ollama modeliyle çalıştır (2. terminal):
   ```
   $env:MODEL="ollama/llama3.1"; node test-gemini.mjs
   ```
   Model adı backend'de `ollama/` önekiyle yönlendirilir; Ollama'ya `llama3.1` olarak gider.

Unity içinde: model açılırı kaldırıldığı için Ollama'yı beyin yapmak istersen
backend `.env`'ine `NOVA_FLASH=ollama/llama3.1` yaz ve backend'i yeniden başlat
(alias katmanı `nova-flash`'i Ollama'ya yönlendirir).

Notlar:
- Küçük modeller araç çağırmada bazen tutarsız olabilir; llama3.1 8B iyi bir başlangıç.
- Ollama farklı portta ise `.env` içine `OLLAMA_HOST=http://localhost:PORT` ekle.
- Maliyet göstergesi $0.0000 kalır (yerel model ücretsiz).
