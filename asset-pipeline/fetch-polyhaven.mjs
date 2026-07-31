// Poly Haven fetcher — gerçekçi doğa modelleri (ağaç/kaya/bitki) + HDRI gökyüzü.
// Çalıştır:  node fetch-polyhaven.mjs
// Hepsi CC0; anahtar gerekmez ama User-Agent ZORUNLU (fetch-lib UA kullanıyor).
// Modeller: assets-raw/<kategori>/<id>_ph/ (gltf + dokular). HDRI: skies-raw/<id>/.
import fs from "node:fs";
import path from "node:path";
import { ASSETS_ROOT, SKIES_ROOT, sleep, fetchJson, downloadTo, writeMeta } from "./fetch-lib.mjs";

const API = "https://api.polyhaven.com";
const DELAY = 400;
const MODEL_LIMIT = 80;   // doğa modeli üst sınırı
const HDRI_LIMIT = 28;    // gökyüzü sayısı (çeşitlilik: gündüz/gün batımı/gece/bulutlu...)
const RES = "1k";         // model doku çözünürlüğü (oyun için yeterli)
const HDRI_RES = "2k";

// Poly Haven kategorisi -> bizim klasör
// Kelime sınırlı eşleşme: "Rockingchair" → rocks olmasın (alt çizgi/boşluk/tire ayraç sayılır)
const NATURE_MAP = [
  { match: /(^|[\s_-])(tree|pine|oak)s?([\s_-]|$)/i,                cat: "trees" },
  { match: /(^|[\s_-])(rock|boulder|stone|cliff)s?([\s_-]|$)/i,     cat: "rocks" },
  { match: /(^|[\s_-])(plant|flower|grass|bush|shrub|fern)s?([\s_-]|$)/i, cat: "plants" },
];

let dl = 0, skip = 0, fail = 0;

function natureCat(info) {
  const hay = [info.name, ...(info.categories || []), ...(info.tags || [])].join(" ");
  for (const m of NATURE_MAP) if (m.match.test(hay)) return m.cat;
  return null;
}

async function fetchModels() {
  const all = await fetchJson(`${API}/assets?type=models`);
  await sleep(DELAY);
  let got = 0;
  for (const [id, info] of Object.entries(all)) {
    if (got >= MODEL_LIMIT) break;
    const cat = natureCat(info);
    if (!cat) continue;

    const dir = path.join(ASSETS_ROOT, cat, `${id}_ph`);
    if (fs.existsSync(path.join(dir, "meta.json"))) { skip++; got++; continue; }

    try {
      const files = await fetchJson(`${API}/files/${id}`);
      await sleep(DELAY);
      const g = files.gltf?.[RES]?.gltf;
      if (!g?.url) { continue; }

      fs.mkdirSync(dir, { recursive: true });
      await downloadTo(g.url, path.join(dir, "model.gltf"));
      // Bağlı dosyalar (bin + dokular) göreli yolla birlikte
      const inc = files.gltf?.[RES]?.include || {};
      for (const [rel, f] of Object.entries(inc)) {
        if (f?.url) { await downloadTo(f.url, path.join(dir, rel)); await sleep(120); }
      }
      writeMeta(dir, {
        id,
        title: info.name || id,
        source: "polyhaven",
        sourceUrl: `https://polyhaven.com/a/${id}`,
        license: "CC0",
        attribution: "",
        author: (info.authors && Object.keys(info.authors).join(", ")) || "",
        family: "polyhaven",
        category: cat,
        style: "realistic",
        tags: (info.tags || []).slice(0, 12),
        file: "model.gltf",
      });
      dl++; got++;
      console.log(`  ✓ model ${cat}/${id}`);
    } catch (e) {
      fail++;
      console.warn(`  ✗ model ${id}: ${e.message}`);
      try { fs.rmSync(dir, { recursive: true, force: true }); } catch {}
    }
  }
}

// HDRI'yi Türkçe bir "hava durumu" etiketine oturt (Unity menüsünde gruplanır)
function skyMood(info) {
  const hay = [info.name, ...(info.categories || []), ...(info.tags || [])].join(" ").toLowerCase();
  if (/night|moon|star|dark/.test(hay)) return "Gece";
  if (/sunset|sunrise|dusk|dawn|golden|evening/.test(hay)) return "Gün batımı";
  if (/overcast|cloudy|storm|rain|fog|mist/.test(hay)) return "Bulutlu";
  if (/clear|sunny|blue sky|noon|midday|day/.test(hay)) return "Açık gündüz";
  return "Gündüz";
}

async function fetchHdris() {
  // GÖKYÜZÜ HDRI'ları — "skies" kategorisi (outdoor'da bina/iç mekân karışıyordu)
  let all = {};
  try { all = await fetchJson(`${API}/assets?type=hdris&categories=skies`); } catch {}
  await sleep(DELAY);
  if (Object.keys(all).length < HDRI_LIMIT) {
    try {
      const extra = await fetchJson(`${API}/assets?type=hdris&categories=outdoor`);
      all = { ...extra, ...all };
      await sleep(DELAY);
    } catch {}
  }
  // SADECE "PURE SKY": zemini olmayan, saf gökyüzü panoramaları.
  // (Normal 360° HDRI'lar çimen/asfalt zemin içerdiği için skybox olarak kullanılınca
  //  arazinin altına ikinci bir zemin basıyordu.)
  const isPureSky = ([id, info]) =>
    /puresky/i.test(id) || /pure sky/i.test(info?.name || "");
  const pure = Object.entries(all).filter(isPureSky);
  const pool = pure.length >= 8 ? pure : Object.entries(all); // hiç yoksa hepsini al (yedek)
  console.log(`  Pure Sky adayı: ${pure.length} / toplam ${Object.keys(all).length}`);

  // Çeşitlilik: her hava durumundan dengeli seç
  const buckets = {};
  for (const [id, info] of pool) {
    const mood = skyMood(info);
    (buckets[mood] ||= []).push([id, info]);
  }
  const ordered = [];
  for (let i = 0; ordered.length < HDRI_LIMIT; i++) {
    let added = false;
    for (const list of Object.values(buckets)) {
      if (i < list.length) { ordered.push(list[i]); added = true; }
      if (ordered.length >= HDRI_LIMIT) break;
    }
    if (!added) break;
  }

  let got = 0;
  for (const [id, info] of ordered) {
    if (got >= HDRI_LIMIT) break;
    const dir = path.join(SKIES_ROOT, id);
    if (fs.existsSync(path.join(dir, "meta.json"))) { skip++; got++; continue; }
    try {
      const files = await fetchJson(`${API}/files/${id}`);
      await sleep(DELAY);
      const h = files.hdri?.[HDRI_RES]?.hdr;
      if (!h?.url) continue;
      await downloadTo(h.url, path.join(dir, `${id}_${HDRI_RES}.hdr`));
      writeMeta(dir, {
        id,
        title: info.name || id,
        source: "polyhaven",
        sourceUrl: `https://polyhaven.com/a/${id}`,
        license: "CC0",
        category: "hdri",
        mood: skyMood(info), // Gece | Gün batımı | Bulutlu | Açık gündüz | Gündüz
        tags: (info.tags || []).slice(0, 12),
        file: `${id}_${HDRI_RES}.hdr`,
      });
      dl++; got++;
      console.log(`  ✓ hdri ${id}`);
    } catch (e) {
      fail++;
      console.warn(`  ✗ hdri ${id}: ${e.message}`);
      try { fs.rmSync(dir, { recursive: true, force: true }); } catch {}
    }
  }
}

async function run() {
  fs.mkdirSync(ASSETS_ROOT, { recursive: true });
  fs.mkdirSync(SKIES_ROOT, { recursive: true });
  console.log(`Poly Haven fetcher başladı\n### doğa modelleri`);
  await fetchModels();
  console.log(`### HDRI gökyüzleri`);
  await fetchHdris();
  console.log(`\n=== BİTTİ ===\nİndirilen: ${dl} · Atlanan(var): ${skip} · Hata: ${fail}`);
}

run();
