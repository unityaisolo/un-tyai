# UnityAI (kod adı)

Unity içinde çalışan AI copilot — Coplay/Aura alternatifi. Doğal dil ile Unity editörünü kontrol et. Kullanıcı kendi API anahtarını bağlar **veya** bizim bulut planımızı kullanır (abonelik + komisyon).

## Depo yapısı

```
backend/        Node + TypeScript proxy (LLM orkestrasyonu, key vault, metering, billing)
unity-plugin/   Unity Editor paketi (C# — chat penceresi, araç katmanı)
docs/           ARCHITECTURE.md ve diğer tasarım notları
reference/      Coplay'in AÇIK UI dosyaları (yalnızca inceleme için; DLL'ler silindi)
ROADMAP.md      Faz planı
FEATURES.md     Özellik envanteri + farklılaştırıcılar
```

## Hızlı başlangıç

### 1) Backend
```bash
cd backend
cp .env.example .env        # en azından GROQ_API_KEY doldur (varsayılan beyin)
npm install
npm run dev                 # http://localhost:8787
```
Sağlık kontrolü: `curl http://localhost:8787/health`

> Not: Sahte (mock) sağlayıcı bilinçli olarak KAPALI — kullanıcıya asla uydurma cevap
> göstermeyiz. Anahtar yoksa net hata döner. Anahtarsız test için Ollama kullan
> (bkz. `docs/TESTING.md` bölüm D).

Hızlı test (Groq anahtarıyla):
```bash
curl -N -X POST http://localhost:8787/v1/chat -H 'Content-Type: application/json' \
  -d '{"model":"nova-flash","messages":[{"role":"user","content":"bir cube oluştur"}]}'
```

Modeller white-label alias'larla seçilir: `nova-flash` (Groq), `nova-code` (DeepSeek),
`nova-gemini`, `nova-local` (Ollama), `nova-pro` (premium). Gerçek model id'leri de
(`gemini-2.5-flash`, `deepseek-chat`, `ollama/llama3.1`...) doğrudan kullanılabilir.
BYO anahtar: `POST /v1/keys {"provider":"groq","apiKey":"..."}` — o kullanıcı için
komisyonsuz çalışır.

### 2) Unity plugin
- Unity 2022.3+ bir projede: `unity-plugin/` klasörünü projenin `Packages/` altına kopyala
  (veya Package Manager → "Add package from disk" → `unity-plugin/package.json`).
- Menü: **Window → Nova · UnityAI** (veya **UnityAI → Nova penceresini aç/kapat**, kısayol Ctrl/Cmd+G).
- "sahneye bir cube ekle" yaz → Gönder. Backend (Groq/nova-flash) tool_call döner,
  plugin `CreateGameObject` aracını çalıştırır ve sahneye küp ekler (Undo ile geri alınabilir).

## Durum

Uçtan uca hat çalışıyor: **Unity chat → SSE proxy → LLM → tool_call → Unity araçları → tool_result → döngü**.
16 araç, diff onaylı kod yazımı, 3D Stüdyo (fal/Tripo), arazi/biome üretici mevcut.
Güncel odak ve sıradaki adımlar için `ROADMAP.md`'ye bak (2026-07 pivot: arazi üretici).

## Lisans / etik notu

Coplay'in kodunu veya derlenmiş DLL'ini kopyalamıyoruz. `reference/` yalnızca onların
**herkese açık** UI dosyalarını (UXML/USS) içerir ve sadece özellik envanteri çıkarmak için
incelendi; DLL'ler depodan silindi. Tüm çekirdek kod özgün olarak bu depoda yazılıyor.
