// Poly Pizza API yoklama betiği — sadece yanıt şemasını görmek için.
// Çalıştır:  node probe-polypizza.mjs
// Node 18+ gerekir (global fetch). Bağımlılık yok.
import fs from "node:fs";

// .env'den anahtarı oku
function readEnv(key) {
  try {
    const txt = fs.readFileSync(new URL("./.env", import.meta.url), "utf8");
    for (const line of txt.split(/\r?\n/)) {
      const m = line.match(/^\s*([A-Z_]+)\s*=\s*(.*)\s*$/);
      if (m && m[1] === key) return m[2].trim();
    }
  } catch (e) { console.error(".env okunamadı:", e.message); }
  return null;
}

const KEY = readEnv("POLYPIZZA_API_KEY");
if (!KEY) { console.error("POLYPIZZA_API_KEY .env'de bulunamadı."); process.exit(1); }
console.log("Anahtar bulundu (ilk 4:", KEY.slice(0, 4) + "...), uzunluk:", KEY.length);

// En olası endpoint + auth başlığı ile deneriz.
const url = "https://api.poly.pizza/v1.1/search/tree?Limit=3";
console.log("\nİstek:", url);

try {
  const res = await fetch(url, { headers: { "x-auth-token": KEY } });
  console.log("HTTP durum:", res.status, res.statusText);
  const text = await res.text();
  let json;
  try { json = JSON.parse(text); } catch { console.log("Yanıt (ham, ilk 1500):\n", text.slice(0, 1500)); process.exit(0); }

  console.log("\n=== Üst düzey alanlar ===");
  console.log(Object.keys(json));
  const arr = json.results || json.Results || json.models || json.data || (Array.isArray(json) ? json : null);
  if (arr && arr.length) {
    console.log("\n=== İlk sonucun TAM yapısı ===");
    console.log(JSON.stringify(arr[0], null, 2));
    console.log("\nToplam sonuç alanı olabilecekler:", { total: json.total, Total: json.Total, count: json.count });
  } else {
    console.log("\n(results dizisi bulunamadı) Tüm yanıt (ilk 2000):");
    console.log(JSON.stringify(json, null, 2).slice(0, 2000));
  }
} catch (e) {
  console.error("İstek hatası:", e.message);
  console.error("Not: 401/403 gelirse auth başlığı farklı olabilir; söyle, değiştireyim.");
}
