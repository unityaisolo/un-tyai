// Nova Asset Pipeline — ortak glTF geometri analizi.
// ingest.mjs ve validate.mjs buradan import eder (tek doğruluk kaynağı).
// Node transform'ları (matrix/TRS) uygulanmış DÜNYA-uzayı bounds hesaplar;
// koleksiyon/sahne GLB'lerini tespit eder.
import fs from "node:fs";

export function readGltfJson(file) {
  if (/\.gltf$/i.test(file)) return JSON.parse(fs.readFileSync(file, "utf8"));
  const buf = fs.readFileSync(file);
  const chunkLen = buf.readUInt32LE(12);
  return JSON.parse(buf.slice(20, 20 + chunkLen).toString("utf8"));
}

// ---- Matris yardımcıları (glTF column-major) ----
const matIdentity = () => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
function matMultiply(a, b) {
  const r = new Array(16);
  for (let c = 0; c < 4; c++)
    for (let rw = 0; rw < 4; rw++) {
      let s = 0;
      for (let k = 0; k < 4; k++) s += a[k * 4 + rw] * b[c * 4 + k];
      r[c * 4 + rw] = s;
    }
  return r;
}
function matFromTRS(t = [0, 0, 0], q = [0, 0, 0, 1], s = [1, 1, 1]) {
  const [x, y, z, w] = q;
  const x2 = x + x, y2 = y + y, z2 = z + z;
  const xx = x * x2, xy = x * y2, xz = x * z2;
  const yy = y * y2, yz = y * z2, zz = z * z2;
  const wx = w * x2, wy = w * y2, wz = w * z2;
  return [
    (1 - (yy + zz)) * s[0], (xy + wz) * s[0], (xz - wy) * s[0], 0,
    (xy - wz) * s[1], (1 - (xx + zz)) * s[1], (yz + wx) * s[1], 0,
    (xz + wy) * s[2], (yz - wx) * s[2], (1 - (xx + yy)) * s[2], 0,
    t[0], t[1], t[2], 1,
  ];
}
const nodeMatrix = (n) => (n.matrix ? n.matrix : matFromTRS(n.translation, n.rotation, n.scale));
const xformPoint = (m, p) => [
  m[0] * p[0] + m[4] * p[1] + m[8] * p[2] + m[12],
  m[1] * p[0] + m[5] * p[1] + m[9] * p[2] + m[13],
  m[2] * p[0] + m[6] * p[1] + m[10] * p[2] + m[14],
];

/**
 * Dünya-uzayı geometri analizi.
 * @returns {{size:{x,y,z}, tris:number, pivotBottom:boolean, meshNodes:number, topBoxes:Array}}
 */
export function geometry(file) {
  const j = readGltfJson(file);
  const acc = j.accessors || [];

  // Mesh başına YEREL bbox + üçgen sayısı
  const meshBox = (j.meshes || []).map((mesh) => {
    const mn = [Infinity, Infinity, Infinity], mx = [-Infinity, -Infinity, -Infinity];
    let tris = 0;
    for (const prim of mesh.primitives || []) {
      const pi = prim.attributes ? prim.attributes.POSITION : undefined;
      if (pi != null && acc[pi] && acc[pi].min && acc[pi].max)
        for (let i = 0; i < 3; i++) { mn[i] = Math.min(mn[i], acc[pi].min[i]); mx[i] = Math.max(mx[i], acc[pi].max[i]); }
      if (prim.indices != null && acc[prim.indices]) tris += acc[prim.indices].count / 3;
      else if (pi != null && acc[pi]) tris += acc[pi].count / 3;
    }
    return { mn, mx, tris };
  });

  const min = [Infinity, Infinity, Infinity], max = [-Infinity, -Infinity, -Infinity];
  let tris = 0, meshNodes = 0;
  const topBoxes = []; // sahne kökündeki her dalın ayrı bbox'ı (koleksiyon tespiti için)
  const nodes = j.nodes || [];

  function walk(idx, parent, topBox) {
    const n = nodes[idx];
    if (!n) return;
    const m = matMultiply(parent, nodeMatrix(n));
    if (n.mesh != null && meshBox[n.mesh] && meshBox[n.mesh].mn[0] !== Infinity) {
      meshNodes++;
      tris += meshBox[n.mesh].tris;
      const { mn, mx } = meshBox[n.mesh];
      for (const cx of [mn[0], mx[0]])
        for (const cy of [mn[1], mx[1]])
          for (const cz of [mn[2], mx[2]]) {
            const p = xformPoint(m, [cx, cy, cz]);
            for (let i = 0; i < 3; i++) {
              min[i] = Math.min(min[i], p[i]); max[i] = Math.max(max[i], p[i]);
              if (topBox) { topBox.min[i] = Math.min(topBox.min[i], p[i]); topBox.max[i] = Math.max(topBox.max[i], p[i]); }
            }
          }
    }
    for (const c of n.children || []) walk(c, m, topBox);
  }

  const scene = (j.scenes && j.scenes[j.scene ?? 0]) || { nodes: nodes.map((_, i) => i) };
  for (const rootIdx of scene.nodes || []) {
    const tb = { min: [Infinity, Infinity, Infinity], max: [-Infinity, -Infinity, -Infinity] };
    walk(rootIdx, matIdentity(), tb);
    if (tb.min[0] !== Infinity) topBoxes.push(tb);
  }

  const has = min[0] !== Infinity;
  const size = has
    ? { x: +(max[0] - min[0]).toFixed(3), y: +(max[1] - min[1]).toFixed(3), z: +(max[2] - min[2]).toFixed(3) }
    : { x: 0, y: 0, z: 0 };
  const pivotBottom = has && Math.abs(min[1]) < Math.max(0.05, size.y * 0.02);
  return { size, tris: Math.round(tris), pivotBottom, meshNodes, topBoxes };
}

// ---- Koleksiyon / interior tespiti ----
export const INTERIOR_NAME = /\b(interiors?|room)\b/i;
export const COLLECTION_NAME = /\b(diorama|scene|playset|kit|pack|collection|assets?|woods|village|island|world|trees|houses|bushes|plants|rocks|buildings|cottages|cabins|cars|trucks|vehicles|props)\b/i;

/** Koleksiyon mu? İsim ipucu VEYA çok sayıda ayrık kök dal + dağınık taban alanı. */
export function isCollection(title, geo) {
  if (COLLECTION_NAME.test(title || "") || INTERIOR_NAME.test(title || "")) return true;
  if (geo.topBoxes && geo.topBoxes.length >= 6) {
    const area = (b) => Math.max(0, b.max[0] - b.min[0]) * Math.max(0, b.max[2] - b.min[2]);
    let largest = 0;
    for (const b of geo.topBoxes) largest = Math.max(largest, area(b));
    const full = geo.size.x * geo.size.z;
    if (largest > 0 && full / largest >= 4) return true;
  }
  return false;
}
