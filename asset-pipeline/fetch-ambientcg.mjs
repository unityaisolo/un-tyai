// ambientCG fetcher — zemin/yol/duvar PBR dokuları (hepsi CC0, anahtar gerekmez).
// Çalıştır:  node fetch-ambientcg.mjs
// Çıktı: textures-raw/<AssetID>/ altına Color/Normal/Roughness/AO haritaları + meta.json.
// 1K-JPG paketi indirilir (oyun içi yeterli, küçük). Artımlı (varsa atlar).
import fs from "node:fs";
import path from "node:path";
import { TEXTURES_ROOT, sleep, fetchJson, downloadBuffer, unzipBuffer, writeMeta } from "./fetch-lib.mjs";

const API = "https://ambientcg.com/api/v2/full_json";
const DELAY = 400;

// Arama terimi -> bizim doku rolü (yol/zemin/duvar)
const JOBS = [
  { q: "asphalt",      role: "road",   limit: 4 },
  { q: "road",         role: "road",   limit: 4 },
  { q: "cobblestone",  role: "road",   limit: 3 },
  { q: "paving stones",role: "sidewalk", limit: 4 },
  { q: "grass",        role: "ground", limit: 8 },  // çim çeşidi = arazi gerçekçiliği
  { q: "ground",       role: "ground", limit: 6 },
  { q: "dirt",         role: "ground", limit: 4 },
  { q: "sand",         role: "ground", limit: 4 },
  { q: "rock",         role: "ground", limit: 4 },
  { q: "moss",         role: "ground", limit: 3 },
  { q: "forest floor", role: "ground", limit: 3 },
  { q: "concrete",     role: "wall",   limit: 3 },
  { q: "bricks",       role: "wall",   limit: 3 },
];

// 2K: yakın çekimde 1K bulanık kalıyordu (FPS'te yere bakınca fark ediliyor)
const RES = process.env.ACG_RES || "2K";
const WANTED = /(_Color|_NormalGL|_Roughness|_AmbientOcclusion)\.(jpg|png)$/i;

let dl = 0, skip = 0, fail = 0;
const seen = new Set();

async function run() {
  fs.mkdirSync(TEXTURES_ROOT, { recursive: true });
  console.log(`ambientCG fetcher başladı → ${TEXTURES_ROOT}\n`);

  for (const job of JOBS) {
    console.log(`### ${job.q} (${job.role})`);
    let j;
    try {
      j = await fetchJson(`${API}?type=Material&q=${encodeURIComponent(job.q)}&limit=${job.limit * 2}&include=downloadData`);
    } catch (e) { console.warn("  arama hatası:", e.message); continue; }
    await sleep(DELAY);

    let got = 0;
    for (const a of j.foundAssets || []) {
      if (got >= job.limit) break;
      const id = a.assetId;
      if (!id || seen.has(id)) continue;
      seen.add(id);

      const dir = path.join(TEXTURES_ROOT, id);
      if (fs.existsSync(path.join(dir, "meta.json"))) { skip++; got++; continue; }

      // 1K-JPG zip linkini bul
      let link = null;
      try {
        const folders = a.downloadFolders || {};
        for (const f of Object.values(folders)) {
          const cats = f.downloadFiletypeCategories || {};
          for (const c of Object.values(cats)) {
            for (const d of c.downloads || []) {
              if (new RegExp(`${RES}-JPG`, "i").test(d.attribute || "")) { link = d.downloadLink || d.fullDownloadPath; break; }
            }
            // İstenen çözünürlük yoksa 1K'ya düş
            if (!link)
              for (const d of c.downloads || [])
                if (/1K-JPG/i.test(d.attribute || "")) { link = d.downloadLink || d.fullDownloadPath; break; }
            if (link) break;
          }
          if (link) break;
        }
      } catch {}
      if (!link) { fail++; console.warn(`  ✗ ${id}: 1K-JPG linki yok`); continue; }

      try {
        const buf = await downloadBuffer(link);
        await sleep(DELAY);
        fs.mkdirSync(dir, { recursive: true });
        const maps = {};
        for (const e of unzipBuffer(buf)) {
          const base = path.basename(e.name);
          if (!WANTED.test(base)) continue;
          fs.writeFileSync(path.join(dir, base), e.data);
          if (/_Color\./i.test(base)) maps.color = base;
          else if (/_NormalGL\./i.test(base)) maps.normal = base;
          else if (/_Roughness\./i.test(base)) maps.roughness = base;
          else if (/_AmbientOcclusion\./i.test(base)) maps.ao = base;
        }
        if (!maps.color) throw new Error("Color haritası çıkmadı");
        writeMeta(dir, {
          id,
          title: a.displayName || id,
          source: "ambientcg",
          sourceUrl: `https://ambientcg.com/view?id=${id}`,
          license: "CC0",
          attribution: "",
          category: "texture",
          role: job.role, // road | sidewalk | ground | wall
          style: "realistic",
          tags: (a.tags || []).slice(0, 12),
          maps,
        });
        dl++; got++;
        console.log(`  ✓ ${id} (${Object.keys(maps).join(",")})`);
      } catch (e) {
        fail++;
        console.warn(`  ✗ ${id}: ${e.message}`);
        try { fs.rmSync(dir, { recursive: true, force: true }); } catch {}
      }
    }
  }

  console.log(`\n=== BİTTİ ===\nİndirilen: ${dl} · Atlanan(var): ${skip} · Hata: ${fail}`);
}

run();
