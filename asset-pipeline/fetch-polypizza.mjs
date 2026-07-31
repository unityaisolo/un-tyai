// Poly Pizza fetcher — MVP low-poly şehir kiti.
// Çalıştır:  node fetch-polypizza.mjs
// Node 18+ (global fetch). Bağımlılık yok.
//
// Ne yapar: aşağıdaki JOBS'taki kategoriler için Poly Pizza'da arar,
// SADECE CC0 / CC-BY lisanslıları alır, GLB'yi indirir ve her asset için
// meta.json (lisans + atıf + yazar + etiket) yazar. assets-raw/<kategori>/<asset>/.
// Nazik: istekler arası kısa bekleme + kendi User-Agent'ı. Tekrar çalıştırınca
// var olanları atlar (artımlı).

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const OUT_ROOT = path.join(HERE, "assets-raw");
const UA = "NovaAssetFetcher/1.0 (indie gamedev; CC0/CC-BY only; self-hosted)";
const PER_REQUEST_DELAY = 300; // ms, sunucuyu yormamak için

// ---- Ayarlanabilir iş listesi (kategori -> arama terimleri, adet) ----
// Faz 0: kütüphaneyi büyüt — yol modülleri, sokak öğeleri, kaya/doğa,
// araç çeşidi ve bina tipleri (konut/ticari/kamu) özellikle zenginleştirildi.
const JOBS = [
  // Doğa
  { cat: "trees",        terms: ["tree", "pine tree", "oak tree", "palm tree", "dead tree"], limit: 20 },
  { cat: "bushes",       terms: ["bush", "shrub", "hedge"],                     limit: 12 },
  { cat: "plants",       terms: ["plant", "flower", "grass", "fern", "mushroom"], limit: 12 },
  { cat: "rocks",        terms: ["rock", "boulder", "stone", "cliff"],          limit: 15 },
  // Binalar — konut
  { cat: "houses",       terms: ["house", "cottage", "cabin", "suburban house", "brick house"], limit: 18 },
  { cat: "apartments",   terms: ["apartment", "apartment building", "office building", "skyscraper"], limit: 15 },
  // Binalar — ticari
  { cat: "shops",        terms: ["shop", "store", "market stall", "restaurant", "cafe", "gas station"], limit: 15 },
  // Binalar — kamu / landmark
  { cat: "civic",        terms: ["church", "tower", "windmill", "town hall", "school", "fire station", "hospital"], limit: 12 },
  // Yol modülleri (Faz 2 organik ağ için kritik: düz/viraj/kavşak)
  { cat: "roads",        terms: ["road straight", "road curve", "road corner", "road intersection", "road piece", "street road", "crosswalk", "t junction", "road junction", "4 way road", "road tile", "road end"], limit: 15 },
  // Sokak öğeleri
  { cat: "streetlights", terms: ["street lamp", "lamp post", "street light", "lantern"], limit: 10 },
  { cat: "benches",      terms: ["bench", "park bench", "wooden bench", "picnic table"], limit: 8 },
  { cat: "signs",        terms: ["street sign", "traffic light", "traffic sign", "billboard", "stop sign"], limit: 12 },
  { cat: "fences",       terms: ["fence", "wall", "barrier", "guardrail"],      limit: 12 },
  { cat: "fountains",    terms: ["fountain", "well", "statue"],                 limit: 8 },
  { cat: "props",        terms: ["barrel", "crate", "trash can", "fire hydrant", "mailbox", "bus stop", "flower pot", "dumpster"], limit: 12 },
  // Araç çeşidi
  { cat: "vehicles",     terms: ["car", "truck", "bus", "van", "police car", "taxi", "pickup truck", "ambulance"], limit: 12 },
];

// ---- Yardımcılar ----
function readEnv(key) {
  try {
    const txt = fs.readFileSync(path.join(HERE, ".env"), "utf8");
    for (const line of txt.split(/\r?\n/)) {
      const m = line.match(/^\s*([A-Z_]+)\s*=\s*(.*)\s*$/);
      if (m && m[1] === key) return m[2].trim();
    }
  } catch {}
  return null;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const slug = (s) => (s || "asset").toLowerCase().normalize("NFKD").replace(/[^\w]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40) || "asset";

// CC0 ve CC-BY kabul; NC/ND/SA reddet.
function licenceOk(lic) {
  if (!lic) return false;
  const l = lic.toLowerCase();
  if (/nc|nd|sa/.test(l)) return false;              // ticari-olmayan / türev-yok / share-alike YOK
  return /^cc0/.test(l) || /^cc[\s-]?by/.test(l);    // CC0 veya CC-BY
}

const KEY = readEnv("POLYPIZZA_API_KEY");
if (!KEY) { console.error("POLYPIZZA_API_KEY .env'de yok."); process.exit(1); }

const headers = { "x-auth-token": KEY, "User-Agent": UA };
const credits = [];
let dl = 0, skip = 0, rej = 0, fail = 0;
const seen = new Set();

async function search(term, limit) {
  const url = `https://api.poly.pizza/v1.1/search/${encodeURIComponent(term)}?Limit=${limit}`;
  const res = await fetch(url, { headers });
  if (!res.ok) { console.warn(`  ! arama başarısız (${term}): HTTP ${res.status}`); return []; }
  const j = await res.json();
  return j.results || [];
}

async function download(url, dest) {
  const res = await fetch(url, { headers: { "User-Agent": UA } });
  if (!res.ok) throw new Error("HTTP " + res.status);
  const buf = Buffer.from(await res.arrayBuffer());
  fs.writeFileSync(dest, buf);
  return buf.length;
}

async function run() {
  fs.mkdirSync(OUT_ROOT, { recursive: true });
  console.log(`Poly Pizza fetcher başladı → ${OUT_ROOT}\n`);

  for (const job of JOBS) {
    const catDir = path.join(OUT_ROOT, job.cat);
    fs.mkdirSync(catDir, { recursive: true });
    console.log(`### ${job.cat}`);
    for (const term of job.terms) {
      let results = [];
      try { results = await search(term, job.limit); } catch (e) { console.warn("  arama hatası:", e.message); }
      await sleep(PER_REQUEST_DELAY);
      for (const r of results) {
        if (!r || !r.Download || !r.ID) continue;
        if (seen.has(r.ID)) { continue; }         // aynı model iki terimde çıkarsa bir kez
        seen.add(r.ID);
        if (!licenceOk(r.Licence)) { rej++; continue; }

        const name = `${slug(r.Title)}_${r.ID}`;
        const dir = path.join(catDir, name);
        const glb = path.join(dir, `${slug(r.Title)}.glb`);
        if (fs.existsSync(glb)) { skip++; continue; } // artımlı

        fs.mkdirSync(dir, { recursive: true });
        try {
          const size = await download(r.Download, glb);
          const meta = {
            id: r.ID,
            title: r.Title,
            source: "poly.pizza",
            sourceUrl: `https://poly.pizza/m/${r.ID}`,
            license: r.Licence,
            attribution: r.Attribution || "",
            author: r.Creator?.Username || "",
            category: job.cat,
            sourceCategory: r.Category || "",
            style: "low-poly",
            tags: r.Tags || [],
            triangles: r["Tri Count"] || 0,
            file: `${slug(r.Title)}.glb`,
            bytes: size,
          };
          fs.writeFileSync(path.join(dir, "meta.json"), JSON.stringify(meta, null, 2));
          if (/cc[\s-]?by/i.test(r.Licence) && r.Attribution) credits.push(r.Attribution);
          dl++;
          process.stdout.write(`  ✓ ${job.cat}/${name} (${r.Licence})\n`);
          await sleep(PER_REQUEST_DELAY);
        } catch (e) {
          fail++;
          console.warn(`  ✗ indirilemedi ${name}: ${e.message}`);
          try { fs.rmSync(dir, { recursive: true, force: true }); } catch {}
        }
      }
    }
  }

  // CC-BY atıfları — Credits dosyası (ürün jeneriği için)
  if (credits.length) {
    const uniq = [...new Set(credits)];
    fs.writeFileSync(path.join(OUT_ROOT, "CREDITS.md"),
      "# Asset Credits (CC-BY)\n\n" + uniq.map((c) => "- " + c).join("\n") + "\n");
  }

  console.log(`\n=== BİTTİ ===`);
  console.log(`İndirilen: ${dl} · Atlanan(var): ${skip} · Reddedilen(lisans): ${rej} · Hata: ${fail}`);
  console.log(`CC-BY atıfları: ${credits.length ? "assets-raw/CREDITS.md" : "yok"}`);
}

run();
