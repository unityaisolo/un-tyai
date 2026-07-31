// Nova Asset Pipeline — fetcher ortak yardımcıları (bağımlılık yok, Node 18+).
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

export const HERE = path.dirname(fileURLToPath(import.meta.url));
export const ASSETS_ROOT = path.join(HERE, "assets-raw");
export const TEXTURES_ROOT = path.join(HERE, "textures-raw");
export const SKIES_ROOT = path.join(HERE, "skies-raw");
export const UA = "NovaAssetFetcher/1.0 (indie gamedev; CC0/CC-BY only; contact: nova)";

export const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
export const slug = (s) =>
  (s || "asset").toLowerCase().normalize("NFKD").replace(/[^\w]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 40) || "asset";

export function readEnv(key) {
  try {
    const txt = fs.readFileSync(path.join(HERE, ".env"), "utf8");
    for (const line of txt.split(/\r?\n/)) {
      const m = line.match(/^\s*([A-Z_]+)\s*=\s*(.*)\s*$/);
      if (m && m[1] === key) return m[2].trim();
    }
  } catch {}
  return null;
}

// CC0 ve CC-BY kabul; NC/ND/SA/RF asla.
export function licenceOk(lic) {
  if (!lic) return false;
  const l = String(lic).toLowerCase();
  if (/nc|nd|sa/.test(l)) return false;
  return /^cc0/.test(l) || /^cc[\s-]?by/.test(l) || l === "by" || l === "cc0";
}

export async function fetchJson(url, headers = {}) {
  const res = await fetch(url, { headers: { "User-Agent": UA, ...headers } });
  if (!res.ok) throw new Error(`HTTP ${res.status} ${url}`);
  return res.json();
}

export async function downloadBuffer(url, headers = {}) {
  const res = await fetch(url, { headers: { "User-Agent": UA, ...headers } });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return Buffer.from(await res.arrayBuffer());
}

export async function downloadTo(url, dest, headers = {}) {
  const buf = await downloadBuffer(url, headers);
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.writeFileSync(dest, buf);
  return buf.length;
}

// ---- Mini ZIP açıcı (store + deflate; node:zlib ile, harici bağımlılık yok) ----
// Central Directory'yi tarar; her girdiyi {name, data} olarak döndürür.
export function unzipBuffer(buf) {
  // EOCD (End of Central Directory) imzasını sondan tara: 0x06054b50
  let eocd = -1;
  const minEocd = Math.max(0, buf.length - 65557);
  for (let i = buf.length - 22; i >= minEocd; i--) {
    if (buf.readUInt32LE(i) === 0x06054b50) { eocd = i; break; }
  }
  if (eocd < 0) throw new Error("ZIP: EOCD bulunamadı");
  const count = buf.readUInt16LE(eocd + 10);
  let off = buf.readUInt32LE(eocd + 16); // central directory offset

  const entries = [];
  for (let n = 0; n < count; n++) {
    if (buf.readUInt32LE(off) !== 0x02014b50) break; // central dir file header
    const method = buf.readUInt16LE(off + 10);
    const compSize = buf.readUInt32LE(off + 20);
    const nameLen = buf.readUInt16LE(off + 28);
    const extraLen = buf.readUInt16LE(off + 30);
    const commentLen = buf.readUInt16LE(off + 32);
    const localOff = buf.readUInt32LE(off + 42);
    const name = buf.slice(off + 46, off + 46 + nameLen).toString("utf8");

    // Local header'dan gerçek veri başlangıcını bul (local extra farklı olabilir)
    const lNameLen = buf.readUInt16LE(localOff + 26);
    const lExtraLen = buf.readUInt16LE(localOff + 28);
    const dataStart = localOff + 30 + lNameLen + lExtraLen;
    const raw = buf.slice(dataStart, dataStart + compSize);

    if (!name.endsWith("/")) {
      const data = method === 8 ? zlib.inflateRawSync(raw) : method === 0 ? raw : null;
      if (data) entries.push({ name, data });
    }
    off += 46 + nameLen + extraLen + commentLen;
  }
  return entries;
}

/** ZIP buffer'ını klasöre aç; yazılan dosya yollarını döndürür. */
export function extractZip(buf, destDir, filter = null) {
  const written = [];
  for (const e of unzipBuffer(buf)) {
    if (filter && !filter(e.name)) continue;
    const safe = e.name.replace(/\\/g, "/").split("/").filter((p) => p && p !== "..").join(path.sep);
    const dest = path.join(destDir, safe);
    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.writeFileSync(dest, e.data);
    written.push(dest);
  }
  return written;
}

export function writeMeta(dir, meta) {
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, "meta.json"), JSON.stringify(meta, null, 2));
}
