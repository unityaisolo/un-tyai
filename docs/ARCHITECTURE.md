# Mimari

## Genel akış (proxy mimarisi)

```
┌─────────────────────┐        HTTPS / SSE        ┌──────────────────────┐        ┌─────────────┐
│   Unity Editor      │  ───────────────────────▶ │   UnityAI Backend    │ ─────▶ │  LLM Sağlay. │
│   (C# plugin)       │                           │   (Node + TS)        │        │  OpenAI /    │
│                     │ ◀───────────────────────  │                      │ ◀───── │  Anthropic / │
│  - Chat UI          │   stream: token + tool     │  - Auth              │        │  Gemini /    │
│  - Tool executor    │   çağrıları                │  - Key vault (BYO)   │        │  Ollama...   │
│  - Context toplayıcı│                           │  - Metering/billing  │        └─────────────┘
└─────────────────────┘   tool sonuçları POST      │  - Agent orchestr.   │
                          ─────────────────────▶   └──────────────────────┘
```

**Neden proxy?** Kullanıcı kendi anahtarını girse bile istek bizim sunucumuzdan geçer. Böylece: token sayabiliriz, komisyon/abonelik uygulayabiliriz, sağlayıcı API'lerini soyutlarız (Unity tarafı tek bir protokol bilir), anahtarları istemcide saklamayız.

## Bileşenler

### Unity Plugin (C#, Editor-only)
- **ChatWindow** (`EditorWindow`): UXML/USS ile UI Toolkit arayüzü.
- **BackendClient**: HTTP + SSE istemcisi. `/v1/chat` akışını dinler.
- **ToolRegistry**: `ITool` implementasyonları. LLM bir araç çağırınca burada eşleşir, main thread'de `EditorApplication.delayCall` ile çalışır.
- **ContextCollector**: Seçili obje, sahne hiyerarşisi, konsol logları, seçili asset'leri toplar.
- **Undo entegrasyonu**: Her araç `Undo.RegisterCreatedObjectUndo` vb. kullanır → geri alınabilir.

### Backend (Node + TypeScript)
- **`/v1/chat` (POST, SSE)**: Mesaj geçmişi + araç şemaları alır. Alias katmanı (`nova-*` →
  gerçek model) ve ProviderRouter üzerinden seçilen sağlayıcıya iletir; cevabı token token
  stream eder. Araç çağrıları `tool_call` olayları olarak akar. **Araç sonuçları ayrı bir
  endpoint'e DEĞİL, bir sonraki `/v1/chat` isteğinde `role:"tool"` mesajları olarak döner**
  (agent döngüsünü Unity tarafı sürdürür). Görsel eklenirse önce vision modeliyle metne
  çevrilir; Council açıksa denetçi turu araya girer.
- **ProviderRouter**: `groq | openrouter | openai | deepseek | anthropic | gemini | ollama` —
  ortak arayüz (`ChatProvider`). Mock bilinçli kayıtlı değil (sahte cevap ilkesi).
- **Alias katmanı** (`aliases.ts`): white-label `nova-flash/code/pro/local/vision` isimleri;
  `.env` (`NOVA_FLASH` vb.) ile arkadaki model değiştirilebilir.
- **KeyVault**: BYO anahtarları AES-GCM ile şifreli saklar; istek anında çözer, loglamaz.
  MVP: in-memory. BYO yoksa `.env`'deki havuz anahtarı kullanılır (komisyonlu).
- **Metering**: her istekte giriş/çıkış token, model, maliyet, komisyon → in-memory ledger
  (`/v1/usage`). Üretim işleri (3D/rig/görsel) iş başına fiyatlanır.
- **Medya**: `/v1/generate/3d|image` (fal/Tripo/FLUX), `/v1/character/pipeline` (rig+anim),
  `/v1/world/plan|curate|review` (dünya planlama + küratör + görsel denetim).
- **Faz 5 (henüz yok)**: gerçek auth (Firebase), MoR ödeme (Polar/Paddle), kalıcı DB, kota.

## Araç çağrı protokolü (öz)

Backend, sağlayıcının native tool-calling'ini kendi şemamıza normalize eder. Unity'ye giden olay:

```json
{ "type": "tool_call", "id": "call_1", "name": "CreateGameObject",
  "args": { "name": "Enemy", "primitive": "Cube", "position": [0,1,0] } }
```

Unity çalıştırır, döner:

```json
{ "type": "tool_result", "id": "call_1", "ok": true,
  "data": { "instanceId": 13456, "path": "Enemy" } }
```

## Güvenlik ilkeleri
- Anahtarlar asla istemcide veya logda plaintext durmaz.
- Araçlar allow-list; her araç JSON-Schema ile doğrulanır.
- Yıkıcı aksiyonlar (silme, dosya yazma) auto-approve kapalıyken onay ister.
- Script yazma/okuma yolları proje `Assets/` klasörüyle sınırlıdır (path traversal engeli).
- Rate limit (in-memory, kullanıcı+IP başına, `RATE_LIMIT_PER_MIN`); kota Faz 5'te.
- Opsiyonel erişim kilidi: `API_TOKENS` ayarlanırsa yalnız listedeki Bearer token'lar kabul
  edilir (küçük ekip/kapalı beta için). Gerçek JWT/OAuth Faz 5.

## Teknoloji seçimleri (mevcut durum)
- Backend: Node 20, TypeScript, Express, `zod` (şema doğrulama). SSE el yazımı (ek kütüphane
  yok). Kalıcılık: MVP'de in-memory (key vault + usage ledger). Planlanan (Faz 5): Firebase
  Auth + Firestore, MoR ödeme (Polar.sh/Paddle — Stripe Türkiye'de yok).
- Unity: 2022.3+ / 6000.x, UI Toolkit, `com.unity.cloud.gltfast` (3D import).
