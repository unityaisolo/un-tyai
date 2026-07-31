// Sketchfab fetcher — gerçekçi/çeşit için CC0 & CC-BY modeller.
// Çalıştır:  node fetch-sketchfab.mjs
// Gerekli: .env içinde SKETCHFAB_TOKEN=xxxx (sketchfab.com/settings/password → API token).
//
// Akış: search (downloadable) → model detayından lisans doğrula (CC0/BY dışı asla) →
// /download endpoint'i → GLB varsa direkt, yoksa glTF zip'ini aç.
// Çıktı: assets-raw/<kategori>/<slug>_sf-<uid>/ + meta.json. Artımlı (varsa atlar).
import fs from "node:fs";
import path from "node:path";
import {
  ASSETS_ROOT, UA, sleep, slug, readEnv, fetchJson, downloadBuffer, extractZip, writeMeta,
} from "./fetch-lib.mjs";

const TOKEN = readEnv("SKETCHFAB_TOKEN");
if (!TOKEN) { console.error("SKETCHFAB_TOKEN .env'de yok. https://sketchfab.com/settings/password"); process.exit(1); }

const AUTH = { Authorization: `Token ${TOKEN}` };
const API = "https://api.sketchfab.com/v3";
const DELAY = 1500;             // API'ye nazik davran (429 rate-limit yememek için yüksek)
const MAX_ARCHIVE_MB = 60;      // dev arşivleri atla
const OK_LICENSES = new Set(["cc0", "by"]); // slug bazlı — NC/ND/SA asla

// Kategori -> arama terimleri (stil: realistic hedefli; sonuçlar karışık gelebilir,
// validate.mjs + ingest zaten süzecek).
const JOBS = [
  { cat: "houses",   terms: ["house building exterior", "residential house"], limit: 10 },
  { cat: "shops",    terms: ["storefront building", "shop exterior"],         limit: 8 },
  { cat: "civic",    terms: ["church building", "town hall"],                 limit: 6 },
  { cat: "vehicles", terms: ["car lowpoly game", "truck game ready"],         limit: 8 },
  { cat: "trees",    terms: ["realistic tree", "pine tree game"],             limit: 8 },
  { cat: "rocks",    terms: ["rock scan", "boulder game ready"],              limit: 8 },
  { cat: "props",    terms: ["street prop game", "trash can"],                limit: 6 },
];

let dl = 0, skip = 0, rej = 0, fail = 0;
const seen = new Set();

async function search(term, limit) {
  const url = `${API}/search?type=models&downloadable=true&archives_flavours=false&count=${Math.min(24, limit * 2)}&q=${encodeURIComponent(term)}`;
  const j = await fetchJson(url, AUTH);
  return j.results || [];
}

async function modelDetail(uid) {
  return fetchJson(`${API}/models/${uid}`, AUTH);
}

async function downloadModel(uid) {
  return fetchJson(`${API}/models/${uid}/download`, AUTH);
}

async function run() {
  fs.mkdirSync(ASSETS_ROOT, { recursive: true });
  console.log(`Sketchfab fetcher başladı → ${ASSETS_ROOT}\n`);

  for (const job of JOBS) {
    console.log(`### ${job.cat}`);
    let got = 0;
    for (const term of job.terms) {
      if (got >= job.limit) break;
      let results = [];
      try { results = await search(term, job.limit); } catch (e) { console.warn("  arama hatası:", e.message); }
      await sleep(DELAY);

      for (const r of results) {
        if (got >= job.limit) break;
        if (!r?.uid || seen.has(r.uid)) continue;
        seen.add(r.uid);

        const name = `${slug(r.name)}_sf-${r.uid.slice(0, 8)}`;
        const dir = path.join(ASSETS_ROOT, job.cat, name);
        if (fs.existsSync(path.join(dir, "meta.json"))) { skip++; got++; continue; }

        try {
          // Lisansı model detayından DOĞRULA (search sonucuna güvenme)
          const d = await modelDetail(r.uid);
          await sleep(DELAY);
          const licSlug = d?.license?.slug || "";
          if (!OK_LICENSES.has(licSlug)) { rej++; continue; }
          if ((d.archives?.gltf?.size || 0) > MAX_ARCHIVE_MB * 1024 * 1024) { rej++; continue; }

          const links = await downloadModel(r.uid);
          await sleep(DELAY);

          let file = null;
          fs.mkdirSync(dir, { recursive: true });
          if (links.glb?.url) {
            const buf = await downloadBuffer(links.glb.url);
            file = "model.glb";
            fs.writeFileSync(path.join(dir, file), buf);
          } else if (links.gltf?.url) {
            const buf = await downloadBuffer(links.gltf.url);
            if (buf.length > MAX_ARCHIVE_MB * 1024 * 1024) throw new Error("arşiv çok büyük");
            extractZip(buf, dir);
            const gltf = fs.readdirSync(dir).find((f) => /\.(glb|gltf)$/i.test(f));
            if (!gltf) throw new Error("arşivde glTF yok");
            file = gltf;
          } else throw new Error("indirme linki yok");

          const licLabel = licSlug === "cc0" ? "CC0" : "CC-BY 4.0";
          writeMeta(dir, {
            id: r.uid,
            title: d.name || r.name,
            source: "sketchfab",
            sourceUrl: `https://sketchfab.com/3d-models/${r.uid}`,
            license: licLabel,
            attribution: `"${d.name}" by ${d.user?.displayName || d.user?.username || "?"} (${licLabel}) https://sketchfab.com/3d-models/${r.uid}`,
            author: d.user?.username || "",
            family: `sf-${slug(d.user?.username || "unknown")}`, // aile = sanatçı
            category: job.cat,
            style: "realistic",
            tags: (d.tags || []).map((t) => t.slug || t.name || String(t)).slice(0, 12),
            triangles: d.faceCount || 0,
            file,
            bytes: fs.statSync(path.join(dir, file)).size,
          });
          dl++; got++;
          console.log(`  ✓ ${job.cat}/${name} (${licLabel})`);
        } catch (e) {
          fail++;
          console.warn(`  ✗ ${name}: ${e.message}`);
          try { fs.rmSync(dir, { recursive: true, force: true }); } catch {}
        }
      }
    }
  }

  console.log(`\n=== BİTTİ ===\nİndirilen: ${dl} · Atlanan(var): ${skip} · Reddedilen(lisans/boyut): ${rej} · Hata: ${fail}`);
}

run();
