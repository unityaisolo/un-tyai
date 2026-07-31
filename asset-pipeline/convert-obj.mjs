import { createRequire } from "module";
const require = createRequire(import.meta.url);
const obj2gltf = require("obj2gltf");
import fs from "fs";
import path from "path";

const folders = process.argv.slice(2);
for (const folder of folders) {
  const root = path.join("assets-raw", folder);
  if (!fs.existsSync(root)) { console.log("YOK:", folder); continue; }
  // recursively find .obj
  const objs = [];
  (function walk(d){ for(const e of fs.readdirSync(d,{withFileTypes:true})){
    const p=path.join(d,e.name);
    if(e.isDirectory()) walk(p);
    else if(/\.obj$/i.test(e.name)) objs.push(p);
  }})(root);
  // output into a GLB_converted folder at pack root
  const outDir = path.join(root, "GLB_converted");
  fs.mkdirSync(outDir, {recursive:true});
  let ok=0, fail=0;
  for (const o of objs) {
    const name = path.basename(o, path.extname(o));
    const out = path.join(outDir, name + ".glb");
    try {
      const glb = await obj2gltf(o, { binary:true });
      fs.writeFileSync(out, Buffer.from(glb));
      ok++;
    } catch(e) { fail++; console.log("  x", name, e.message.split("\n")[0]); }
  }
  console.log(`[${folder}] OBJ:${objs.length} -> GLB:${ok} (hata:${fail})`);
}
