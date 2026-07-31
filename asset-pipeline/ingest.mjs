// Nova Asset Pipeline — katalog üretici (v3: metadata sözleşmesi + quality gate).
// Kullanım: node ingest.mjs [assets-kök] [çıktı.json]
// Akış: fetch-*.mjs → validate.mjs (quality işaretler) → ingest.mjs (katalog + CREDITS.md)
//
// v3 yenilikleri:
//  - quality !== "ok" olanlar kataloğa GİRMEZ (validate.mjs işaretler; silme yok)
//  - role (ince rol), realTarget (metre hedef), family (pack/sanatçı ailesi), connectors (yol)
//  - CC-BY atıfları CREDITS.md'ye
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { geometry, isCollection } from "./gltf-geometry.mjs";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ROOT = process.argv[2] || path.join(HERE, "assets-raw");
const OUT = process.argv[3] || path.join(HERE, "catalog.json");
const CREDITS = path.join(HERE, "CREDITS.md");

// İnce kategori (klasör/meta) -> World Builder'ın kaba kategorisi
const COARSE = {
  trees: "nature", bushes: "nature", plants: "nature", rocks: "nature", nature: "nature",
  houses: "building", apartments: "building", shops: "building", civic: "building",
  landmarks: "building", building: "building",
  roads: "road", road: "road",
  streetlights: "prop", benches: "prop", signs: "prop", fences: "prop",
  fountains: "prop", props: "prop", prop: "prop", "street-furniture": "prop",
  vehicles: "vehicle", vehicle: "vehicle",
  characters: "character", character: "character",
};

// ---- Rol + gerçek-dünya hedef boy (metre) — Faz 1 yerleştirici bunu kullanacak ----
// realTarget: binalar/ağaçlar/lambalar için YÜKSEKLİK, araç/bank/yol için UZUN KENAR hedefi.
const ROLE_TARGET = {
  house: 8, shop: 7, civic: 14, tower: 18,
  tree: 7, bush: 1.5, flower: 0.6, rock: 1.5,
  road_straight: 12, road_curve: 12, road_t: 12, road_cross: 12, rail: 12,
  lamp: 4.5, bench: 1.5, sign: 2.5, fence: 1.2, fountain: 3,
  car: 4.5, truck: 7, prop: 1.2, character: 1.8, misc: 1,
};

// ---- Bina TEMASI: harita tipiyle eşleşir (modern şehirde ağaç-ev/kulübe olmasın) ----
const THEME_MODERN = /(skyscraper|office|apartment|\bbuilding\b|store|shop|bakery|diner|gas.?station|hotel|cinema|hospital|school|fire.?station|police|mall|bank|cafe|restaurant|town.?house|row.?house)/;
const THEME_RURAL = /(cabin|cottage|barn|hut|farm|windmill|\bmill\b|\binn\b|tavern|chalet|shed|stable|\bwell\b|village)/;
const THEME_FANTASY = /(castle|wizard|temple|ruin|medieval|fort|keep|dungeon|cathedral|chapel)/;
const BUILDING_ROLES = new Set(["house", "shop", "civic", "tower"]);

function themeFor(role, title) {
  if (!BUILDING_ROLES.has(role)) return "generic";
  const t = (title || "").toLowerCase();
  if (THEME_FANTASY.test(t)) return "fantasy";
  if (THEME_RURAL.test(t)) return "rural";
  if (THEME_MODERN.test(t)) return "modern";
  return "generic";
}

const ROAD_CONNECTORS = {
  road_straight: ["N", "S"],
  road_curve: ["N", "E"],
  road_t: ["N", "E", "W"],
  road_cross: ["N", "E", "S", "W"],
};

function roleFor(fine, title) {
  const t = (title || "").toLowerCase();

  // 1) GÜÇLÜ İSİM İPUÇLARI — klasör yanlış olsa bile düzelt.
  // (Arama kirliliği: "civic" klasöründe ambulans, "shops"ta kahve fincanı vs.)
  // Yerde yatan dal/kütük/enkaz: ağaç DEĞİL — dik ölçekleme bunları devleştiriyor → misc
  if (/(branch|bark|debris|\blog\b|trunk|stump|firewood|driftwood|twig)/.test(t)) return "misc";
  // Otobüs durağı: "bus" kelimesi ARAÇ regex'ine yakalanıp yola park ediliyordu → sokak propu
  if (/bus.?stop/.test(t)) return "prop";
  // Asılı fener: "lantern" lamba sanılıp 4.5 m'ye büyütülüyordu (dev fener hatası) → misc
  if (/lantern/.test(t)) return "misc";
  // Ağaç-ev/kuş evi vb: "house" içerir ama bina değildir → misc
  if (/(tree.?house|bird.?house|dog.?house|doll.?house|bee.?hive)/.test(t)) return "misc";
  if (/(ambulance|police|taxi|\bbus\b|\bvan\b|truck|lorry|\bcar\b|jeep|humvee|forklift|tram|train|coupe|sedan|suv|pickup|camaro|gtr|hilux|royce)/.test(t))
    return /(truck|bus|van|lorry|tram|train|forklift|humvee)/.test(t) ? "truck" : "car";
  if (/rail|train.?track|railway/.test(t)) return "rail";
  if (/(fence|picket|guardrail|railing|\bwall\b|\bbarrier\b)/.test(t)) return "fence";
  if (/(billboard|signpost|road.?sign|stop.?sign|street.?sign|town.?sign|\bsign\b|traffic.?light|stoplight|traffic.?cone)/.test(t)) return "sign";
  if (/(street.?lamp|lamp.?post|street.?light|streetlight|light.?pole)/.test(t)) return "lamp";
  if (/\bbench\b/.test(t)) return "bench";
  if (/(fountain|statue|\bwell\b)/.test(t)) return "fountain";
  if (/\btower\b/.test(t)) return "tower";
  if (/\bcross\b/.test(t) && !/cross.?walk|crossing|crossroad|intersection/.test(t)) return "misc";
  // Sokak propu (şehir dekorunda KULLANILIR)
  if (/(trash|garbage|dumpster|hydrant|mailbox|postbox|manhole|bus.?stop|\bcone\b|barrel|crate|\bbin\b)/.test(t)) return "prop";
  // Ev-içi/mutfak/ofis eşyası + diorama karoları: sokağa KOYULMAZ → misc (katalogda kalır, builder kullanmaz)
  if (/(\bcup\b|\bmug\b|\bcan\b|desk|chair|\bseat\b|menu|kitchen|counter|shelf|\bcap\b|envelope|extinguisher|alarm|\bbag\b|\bpot\b|propane|\btank\b|coffee|sushi|pizza|food|drink|bottle|plate|lunch|dining|\bbed\b|sofa|\btable\b|mugs)/.test(t)) return "misc";
  if (/(platform|garden|diorama|backyard|patio|playground)/.test(t)) return "misc";

  // 2) Klasör (fine kategori) bazlı
  switch (fine) {
    case "trees": return "tree";
    case "bushes": return "bush";
    case "plants": return "flower";
    case "rocks": return "rock";
    case "houses": return "house";
    case "apartments": return /skyscraper|office|tower/.test(t) ? "tower" : "house";
    case "shops": return "shop";
    case "civic":
    case "landmarks": return /tower/.test(t) ? "tower" : "civic";
    case "roads":
      if (/curv|corner|bend|turn/.test(t)) return "road_curve";
      if (/intersect|crossroad|4.?way/.test(t)) return "road_cross";
      if (/t.?junction|3.?way/.test(t)) return "road_t";
      return "road_straight"; // crosswalk dahil (düz parça üstünde zebra)
    case "streetlights": return "lamp";
    case "benches": return "bench";
    case "signs": return "sign";
    case "fences": return "fence";
    case "fountains": return "fountain";
    case "vehicles": return /truck|lorry|van|bus|ambulance/.test(t) ? "truck" : "car";
    case "characters": return "character";
    // Prop klasörleri: isim tahmini YAPMA — klasör ne diyorsa o.
    // (BUG vakası: "Street Electrical Box" → /tree/ deseni "s-TREE-t" içindeki
    //  tree'yi yakalayıp elektrik kutusunu ağaç yapmıştı.)
    case "props":
    case "street-furniture": return "prop";
  }
  // Bilinmeyen klasör: isimden tahmin — \b sınırları ŞART ("street" tree değildir!)
  if (/\b(trees?|pine|oak|palm|spruce|birch|fir)\b/.test(t)) return "tree";
  if (/\b(rocks?|boulder|stone)\b/.test(t)) return "rock";
  if (/\b(house|cottage|cabin)\b/.test(t)) return "house";
  if (/\b(car|truck)\b/.test(t)) return "car";
  return "prop";
}

// Eski yapı için isim-tabanlı sınıflandırma (yedek)
const CATS = {
  nature: ["tree","bush","rock","grass","plant","flower","log","stump","mushroom","hedge","fern","cactus","palm","leaf","hill","moss","vine"],
  terrain: ["ground","river","water","dirt","sand","tile","path","lake","pond","dock"],
  building: ["house","wall","roof","door","window","tower","castle","hut","shop","building","stair","floor","pillar","column","gate","brick","structure","barn","silo","inn","mill","church","cottage","cabin","apartment"],
  road: ["road","street","pavement","sidewalk","bridge","curb","crossing","lane","highway"],
  prop: ["barrel","crate","box","lamp","bench","sign","table","chair","chest","pot","lantern","well","cart","ladder","fence","statue","fountain","light","banner","bollard"],
  vehicle: ["car","truck","boat","bike","wagon","plane","ship","train","tram"],
  character: ["character","npc","human","enemy","robot","animal","knight","soldier","alien"],
};
function classifyByName(text) {
  for (const [cat, kws] of Object.entries(CATS))
    for (const kw of kws) if (text.includes(kw)) return { category: cat, type: kw };
  return { category: "misc", type: "unknown" };
}

const slug = (s) => (s || "").toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "");

function walk(dir, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else out.push(p);
  }
  return out;
}

// Eski yapı: en üst klasörün pack.json'u
function packConfig(file) {
  const rel = path.relative(ROOT, file);
  const top = rel.split(path.sep)[0];
  let cfg = { pack: top, style: "unknown", license: "UNKNOWN", source: "", themes: [] };
  const cfgPath = path.join(ROOT, top, "pack.json");
  if (fs.existsSync(cfgPath)) {
    try { cfg = { ...cfg, ...JSON.parse(fs.readFileSync(cfgPath, "utf8")) }; } catch {}
  }
  return cfg;
}

function readMeta(glbFile) {
  const metaPath = path.join(path.dirname(glbFile), "meta.json");
  if (!fs.existsSync(metaPath)) return null;
  try { return JSON.parse(fs.readFileSync(metaPath, "utf8")); } catch { return null; }
}

function main() {
  if (!fs.existsSync(ROOT)) { console.error("Klasör yok:", ROOT); process.exit(1); }
  const files = walk(ROOT).filter((f) => /\.(glb|gltf)$/i.test(f));
  console.log(`${files.length} GLB/glTF bulundu (kök: ${ROOT}).`);
  const out = [];
  const seen = new Set();
  const seenSource = new Set(); // aynı kaynak asset'i iki klasörde (civic+landmarks) → bir kez
  let dupes = 0;
  const skippedQuality = [];
  const skippedCollection = [];
  const creditsBySource = {};
  let n = 0, metaCount = 0;

  for (const file of files) {
    const nameNoExt = path.basename(file).replace(/\.(glb|gltf)$/i, "");
    const rel = path.relative(ROOT, file).replace(/\\/g, "/");
    let geo = { size: { x: 0, y: 0, z: 0 }, tris: 0, pivotBottom: false, meshNodes: 0, topBoxes: [] };
    try { geo = geometry(file); } catch (e) { console.warn("okunamadı:", rel, e.message); }

    const meta = readMeta(file);
    let entry;

    if (meta) {
      metaCount++;

      // KÜRATÖR KAPISI: validate.mjs "ok" demediyse kataloğa girmez (dosya silinmez).
      if (meta.quality && meta.quality !== "ok") {
        skippedQuality.push(`${rel} [${meta.quality}/${meta.rejectReason || "?"}]`);
        continue;
      }

      // Kaynak bazlı tekilleştirme: aynı model iki klasöre inmiş olabilir
      if (meta.id) {
        const srcKey = `${meta.source || "?"}:${meta.id}`;
        if (seenSource.has(srcKey)) { dupes++; continue; }
        seenSource.add(srcKey);
      }

      const fine = (meta.category || "").toLowerCase();
      let category = COARSE[fine] || "prop";
      const nm = (meta.title || nameNoExt).toLowerCase();
      // Yanlış "building"e düşen propları düzelt (ör. "shopping cart" -> prop)
      if (category === "building" && /(shopping.?cart|trolley|\bcart\b|\bbag\b|\bsign\b|\bbarrel\b|\bcrate\b|\bbox\b|lantern|\blamp\b|\bbench\b|\bfence\b|\bbarrier\b|interior|shelf)/.test(nm)) category = "prop";
      const pack = meta.source || "polypizza";
      entry = {
        pack,
        style: meta.style || "low-poly",
        license: meta.license || "UNKNOWN",
        source: meta.sourceUrl || meta.source || "",
        author: meta.author || "",
        attribution: meta.attribution || "",
        family: meta.family || (meta.author ? `${pack}-${slug(meta.author)}` : pack),
        category,
        fine,
        type: fine || "generic",
        tags: Array.isArray(meta.tags) ? meta.tags.slice(0, 12) : [],
        themes: [],
        name: meta.title || nameNoExt,
        triangles: meta.triangles || geo.tris,
      };
    } else {
      // Eski yapı yedeği (pack.json)
      const cfg = packConfig(file);
      let { category, type } = classifyByName(nameNoExt.toLowerCase());
      if (category === "misc" && cfg.category) { category = cfg.category; type = "generic"; }
      entry = {
        pack: cfg.pack, style: cfg.style, license: cfg.license, source: cfg.source,
        author: "", attribution: "",
        family: cfg.pack,
        category, fine: "", type,
        tags: Array.from(new Set(rel.toLowerCase().split(/[^a-z0-9]+/).filter((w) => w.length > 2).concat([category, type]))).slice(0, 12),
        themes: Array.isArray(cfg.themes) ? cfg.themes : [],
        name: nameNoExt,
        triangles: geo.tris,
      };
    }

    // validate.mjs çalıştırılmadıysa son savunma hattı: koleksiyon GLB'leri eleme
    if (!meta?.quality && isCollection(entry.name, geo)) {
      skippedCollection.push(`${rel} (koleksiyon: "${entry.name}")`);
      continue;
    }

    let role = roleFor(entry.fine || "", entry.name);

    // TUTARLILIK KAPISI: doğa rolü (tree/bush/rock/flower) yalnız doğa klasöründen gelebilir.
    // İsim tahmini şaşarsa (ör. prop klasöründeki bir asset kendini "tree" sanırsa) rol
    // klasöre göre düzeltilir ve asset review'a düşer — dev fener/elektrik kutusu vakaları
    // görsel denetime kalmadan kaynakta ölür.
    const NATURE_ROLES = new Set(["tree", "bush", "rock", "flower"]);
    const NATURE_FINE = new Set(["trees", "bushes", "plants", "rocks", "nature", ""]);
    let roleConflict = false;
    if (NATURE_ROLES.has(role) && !NATURE_FINE.has(entry.fine || "")) {
      roleConflict = true;
      role = COARSE[entry.fine] === "prop" ? "prop" : "misc";
      console.warn(`  ⚠ rol çelişkisi: ${rel} — klasör '${entry.fine}' ama isim doğa rolü öneriyordu → ${role} + review`);
    }

    const realTarget = meta?.realTarget ?? ROLE_TARGET[role] ?? 1.5; // asset override > rol varsayılanı
    const connectors = ROAD_CONNECTORS[role] || null;
    const theme = themeFor(role, entry.name);

    let uid = slug(`${entry.pack}_${entry.category}_${entry.name}`);
    if (seen.has(uid)) { let k = 2; while (seen.has(`${uid}_${k}`)) k++; uid = `${uid}_${k}`; }
    seen.add(uid);

    // CC-BY atıfları CREDITS.md'ye
    if (/cc[-\s]?by/i.test(entry.license) && entry.attribution) {
      (creditsBySource[entry.pack] ||= new Set()).add(entry.attribution);
    }

    out.push({
      id: uid,
      name: entry.name, file: rel,
      pack: entry.pack, style: entry.style, license: entry.license, source: entry.source,
      author: entry.author, attribution: entry.attribution,
      family: entry.family,
      category: entry.category, type: entry.type,
      role, realTarget, connectors, theme,
      tags: entry.tags, themes: entry.themes,
      sizeMeters: geo.size, footprint: { x: geo.size.x, z: geo.size.z },
      unitScale: meta?.unitScale ?? 1, // 1=metre · 0.01=cm ihracı · 0.001=mm · gerçek boyut = sizeMeters * unitScale
      pivotBottom: geo.pivotBottom, triangles: entry.triangles,
      meshNodes: geo.meshNodes,
      // Birim şüphesi: transform sonrası bile > 100 m ise kaynak birimi bozuk (cm/mm ihraç).
      suspectUnits: Math.max(geo.size.x, geo.size.y, geo.size.z) > 100,
      quality: meta?.quality || "ok",
      format: path.extname(file).slice(1).toLowerCase(),
      review: entry.license === "UNKNOWN" || entry.category === "misc" || roleConflict,
    });
    if (++n % 100 === 0) console.log(`  ${n}/${files.length}`);
  }

  fs.writeFileSync(OUT, JSON.stringify(out, null, 2));

  // CREDITS.md — CC-BY atıfları (kaynak bazlı gruplu)
  let credits = "# Asset Credits\n\nBu projede kullanılan CC-BY lisanslı varlıkların atıfları.\nCC0 varlıklar atıf gerektirmez ama kaynaklar: Poly Pizza, Sketchfab, ambientCG, Poly Haven.\n";
  for (const [src, set] of Object.entries(creditsBySource)) {
    credits += `\n## ${src}\n\n` + [...set].map((c) => "- " + c).join("\n") + "\n";
  }
  fs.writeFileSync(CREDITS, credits);

  const review = out.filter((a) => a.review).length;
  const byCat = {}, byRole = {}, byFam = {};
  for (const a of out) {
    byCat[a.category] = (byCat[a.category] || 0) + 1;
    byRole[a.role] = (byRole[a.role] || 0) + 1;
    byFam[a.family] = (byFam[a.family] || 0) + 1;
  }
  console.log(`\nBitti: ${out.length} asset -> ${OUT}`);
  console.log(`meta.json'lu: ${metaCount} · Gözden geçir (review): ${review} · Tekil kaynak kopyası elendi: ${dupes}`);
  console.log(`Küratör elemesi (quality): ${skippedQuality.length}`);
  for (const s of skippedQuality.slice(0, 30)) console.log("  - " + s);
  if (skippedQuality.length > 30) console.log(`  ... +${skippedQuality.length - 30} daha`);
  console.log(`Koleksiyon elemesi (validate'siz yedek): ${skippedCollection.length}`);
  for (const s of skippedCollection) console.log("  - " + s);
  const suspect = out.filter((a) => a.suspectUnits).length;
  console.log(`Birim şüpheli (>100 m): ${suspect}`);
  console.log("Kategori:", JSON.stringify(byCat));
  console.log("Rol:", JSON.stringify(byRole));
  console.log(`Aile sayısı: ${Object.keys(byFam).length} · CREDITS.md güncellendi`);
}

main();
