# Nova · Kurulum

Unity içinde çalışan AI asistanı. Sahne kurar, arazi/oyun üretir, kod yazar.

**Gereken süre:** ~10 dakika · **Ücret:** ücretsiz başlayabilirsin (Groq'un ücretsiz kotası yeter)

---

## Neye ihtiyacın var

| | |
|---|---|
| Unity | **6000.0** veya üstü (Unity 6) |
| Render hattı | **URP** önerilir (Built-in de çalışır; Nova pembe materyalleri URP'ye çevirebiliyor) |
| Node.js | **20** veya üstü — [nodejs.org](https://nodejs.org) |
| API anahtarı | Ücretsiz başlamak için [console.groq.com/keys](https://console.groq.com/keys) |

> **DirectX notu:** Windows'ta Unity'yi **DX11** ile açman önerilir. DX12'de çok sayıda model aynı anda sahneye eklenirken sürücü çökmesi görülebiliyor. Kısayola `-force-d3d11` ekleyebilirsin.

---

## 1) Nova'yı Unity'ye ekle

Unity'de **Window → Package Manager → + → Add package from git URL**, şunu yapıştır:

```
https://github.com/unityaisolo/un-tyai.git?path=unity-plugin
```

Unity, gerekli iki bağımlılığı (glTFast ve Input System) kendisi kurar. Input System ilk kurulduğunda Unity yeniden başlatma isteyebilir — kabul et.

Kurulunca üst menüde **UnityAI** görünür. Pencereyi açmak için: **UnityAI → Nova penceresini aç** (veya `Ctrl+G`).

---

## 2) Sunucuyu çalıştır

Nova'nın beyni küçük bir yerel sunucudur. **API anahtarların bu bilgisayardan hiç çıkmaz.**

```bash
git clone https://github.com/unityaisolo/un-tyai.git
cd un-tyai/backend
npm install
npm run dev
```

Şunu görmelisin:

```
UnityAI backend http://localhost:8787 üzerinde çalışıyor
Anahtar kasası: C:\Users\<sen>\.nova\vault.json
[anahtar] BYO zorunlu — her kullanıcı kendi anahtarını Ayarlar'dan girer (havuz kapalı).
```

> `.env` dosyası oluşturmana **gerek yok**. Anahtarını bir sonraki adımda Unity'den gireceksin.
>
> Sunucu çalışırken bu pencereyi kapatma. Unity'yi her açtığında sunucuyu da başlat.

---

## 3) API anahtarını bağla

1. Nova penceresinde sağ üstteki **⋮** menüsünden **Ayarlar — API anahtarı**
2. Anahtarını yapıştır → **Bağla**

Nova anahtarın hangi servise ait olduğunu kendisi anlar, o servisin model listesini çeker ve **Nova ile gerçekten çalışan** bir model seçer (araç çağırmayı destekleyen, token limiti yeterli olan). Model adı yazmana gerek yok.

Birkaç saniye sonra: `✓ Groq bağlandı · model: … — Nova hazır.`

**Desteklenen servisler:** Groq, OpenAI, Anthropic, Gemini, DeepSeek, OpenRouter otomatik tanınır. Together, Fireworks, Cerebras, DeepInfra, Alibaba Qwen, Moonshot/Kimi ve OpenAI uyumlu her servis için "Listede olmayan bir servis mi kullanıyorsun?" bölümünden adresi seç. Ollama / LM Studio / vLLM ile tamamen yerel de çalışır (anahtar gerekmez).

---

## 4) 3D model kütüphanesi (Dünya sekmesi için)

Dünya ve oyun şablonları hazır 3D modeller kullanır.

**UnityAI → Kütüphaneyi Buluttan İndir**

Katalog bir kez inip `<Projen>/NovaAssets/` altına yazılır. Modeller **kullanıldıkça** indirilir — hepsini birden indirmezsin.

Kütüphane zaten diskindeyse: **UnityAI → Asset Kütüphanesi…** ile klasörü seçebilirsin.

---

## İlk denemen

**Dünya** sekmesi → Oyun tipi: *Açık Dünya (FPS)* → Arazi tipi: *Dağ Vadisi* → **Haritayı kur**.

Kurulum bitince **Play**'e bas: WASD ile gez, fare ile bak.

Sohbet için **Kod Ajanı** sekmesi: *"sahneye kırmızı bir küp ekle"* yazıp dene.

---

## Sorun giderme

| Belirti | Sebep / çözüm |
|---|---|
| "Sunucuya ulaşılamadı" | Backend çalışmıyor. `cd backend && npm run dev` |
| "Sunucu eski sürüm" | Backend'i durdurup yeniden başlat |
| "… için API anahtarı yok" | ⋮ → Ayarlar'dan anahtarını bağla |
| "araç çağırmayı desteklemiyor" | Nova o modeli zaten eleyip başkasını seçer; hepsi elenirse başka bir servis dene |
| Konsolda `429` / rate limit | Ücretsiz kotanın dakikalık sınırı. Bir dakika bekle |
| Materyaller pembe | URP projesinde Built-in materyaller. Sohbete *"URP'ye geçir"* yaz |
| Dünya kurarken editör çöküyor | DX11 ile başlat (`-force-d3d11`) |
| Arazi kuruldu ama bitki yok | Asset kütüphanesi bağlı değil — adım 4 |

---

## Gizlilik

- API anahtarların **yalnızca kendi bilgisayarında**, `~/.nova/vault.json` içinde AES-256-GCM ile şifreli durur (dosya izni `0600`).
- Anahtarlar hiçbir sunucuya gönderilmez, log'lanmaz, ekranda bir daha tam olarak gösterilmez.
- Model isteklerin doğrudan **senin seçtiğin sağlayıcıya** gider.
- Anahtarını silmek için: ⋮ → Ayarlar → ilgili satırda **Sil**.
