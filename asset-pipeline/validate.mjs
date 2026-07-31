// Nova küratör — her GLB/glTF'i analiz eder, meta.json'a quality + rejectReason yazar.
// Çalıştır:  node validate.mjs   (fetcher'lardan SONRA, ingest'ten ÖNCE)
// SİLMEZ, İŞARETLER. ingest.mjs quality !== "ok" olanları kataloğa almaz.
//
// Kurallar (spec):
//  - interior/room/scene/collection isimli veya çok-dallı dağınık GLB → reject (interior|cluster)
//  - yatay aspect (maxXZ/minXZ) > 6 → reject: degenerate  (yol/çit kategorileri muaf — onlar uzundur)
//  - toplam boyut > 40 m veya < 0.05 m → review: oversize|tiny
//  - geometri okunamadı / boş → reject: degenerate
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { geometry, isCollection, INTERIOR_NAME } from "./gltf-geometry.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = process.argv[2] || path.join(HERE, "assets-raw");

// Uzun-ince olması DOĞAL olan kategoriler (aspect kuralından muaf)
const ASPECT_EXEMPT = new Set(["roads", "fences", "road", "fence", "signs", "sign"]);
// İsmi gereği düz/ince objeler — aspect kuralından muaf
const FLAT_OK = /(sign|billboard|fence|picket|wall|banner|gate|barrier|guardrail|facade|crosswalk)/i;

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else out.push(p);
  }
  return out;
}

function main() {
  if (!fs.existsSync(ROOT)) { console.error("Klasör yok:", ROOT); process.exit(1); }
  const files = walk(ROOT).filter((f) => /\.(glb|gltf)$/i.test(f));
  console.log(`${files.length} GLB/glTF doğrulanıyor (kök: ${ROOT})\n`);

  const stats = { ok: 0, reject: 0, review: 0, noMeta: 0 };
  const reasons = {};
  let n = 0;

  for (const file of files) {
    const dir = path.dirname(file);
    const metaPath = path.join(dir, "meta.json");
    if (!fs.existsSync(metaPath)) { stats.noMeta++; continue; }

    let meta;
    try { meta = JSON.parse(fs.readFileSync(metaPath, "utf8")); }
    catch { stats.noMeta++; continue; }

    const title = meta.title || path.basename(file);
    const cat = (meta.category || "").toLowerCase();

    let quality = "ok", reason = "";
    let geo = null;
    try { geo = geometry(file); } catch (e) { quality = "reject"; reason = "degenerate"; }

    if (geo) {
      const { x, y, z } = geo.size;
      let maxDim = Math.max(x, y, z);

      // BİRİM ÇIKARIMI: cm/mm/inç ihraçları (ör. 52 "m"lik taksi) aslında sağlam asset.
      // Bir ölçek varsayımı objeyi 0.5–40 m bandına oturtuyorsa unitScale yaz ve OK say.
      let unitScale = 1;
      if (maxDim > 40) {
        for (const s of [0.01, 0.001, 0.0254]) {
          if (maxDim * s >= 0.5 && maxDim * s <= 40) { unitScale = s; break; }
        }
        if (unitScale !== 1) maxDim *= unitScale;
      }
      const horizMax = Math.max(x, z), horizMin = Math.max(Math.min(x, z), 1e-6);
      const flatOk = ASPECT_EXEMPT.has(cat) || FLAT_OK.test(title);

      if (maxDim <= 0) { quality = "reject"; reason = "degenerate"; }
      else if (INTERIOR_NAME.test(title)) { quality = "reject"; reason = "interior"; }
      else if (isCollection(title, geo)) { quality = "reject"; reason = "cluster"; }
      else if (!flatOk && horizMax / horizMin > 6) { quality = "reject"; reason = "degenerate"; }
      else if (maxDim > 40) { quality = "review"; reason = "oversize"; } // birim düzeltmesi bile kurtarmadı
      else if (maxDim < 0.05) { quality = "review"; reason = "tiny"; }

      // Gerçek dünya-uzayı ölçüleri meta'ya da yaz (ingest yine hesaplar ama görünürlük iyi)
      meta.sizeMeters = geo.size;
      meta.meshNodes = geo.meshNodes;
      meta.pivotBottom = geo.pivotBottom;
      meta.unitScale = unitScale; // 1 = metre; 0.01 = cm ihracı; 0.001 = mm; 0.0254 = inç
    }

    meta.quality = quality;
    if (reason) meta.rejectReason = reason;
    else delete meta.rejectReason;
    fs.writeFileSync(metaPath, JSON.stringify(meta, null, 2));

    stats[quality]++;
    if (reason) reasons[reason] = (reasons[reason] || 0) + 1;
    if (quality !== "ok")
      console.log(`  ${quality === "reject" ? "✗" : "?"} [${quality}/${reason}] ${path.relative(ROOT, file)} (${geo ? `${geo.size.x}x${geo.size.y}x${geo.size.z} m, ${geo.meshNodes} mesh` : "okunamadı"})`);
    if (++n % 100 === 0) console.log(`  ... ${n}/${files.length}`);
  }

  console.log(`\n=== BİTTİ ===`);
  console.log(`ok: ${stats.ok} · reject: ${stats.reject} · review: ${stats.review} · meta'sız: ${stats.noMeta}`);
  console.log("Sebepler:", JSON.stringify(reasons));
  console.log("\nSıradaki adım: node ingest.mjs  (yalnız quality=ok olanlar kataloğa girer)");
}

main();
