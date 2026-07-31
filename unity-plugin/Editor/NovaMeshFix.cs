using System.Collections.Generic;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// GLB import sonrası renk onarımı. Poly Pizza / Quaternius modellerinin çoğu rengi
    /// DOKUDA değil VERTEX RENGİNDE taşır; URP Lit vertex rengini yok saydığı için modeller
    /// bembeyaz görünür. Burada mesh'in baskın vertex rengi hesaplanıp materyalin base color'ına
    /// yazılır — model kendi rengine kavuşur (yaprak yeşil, gövde kahve...).
    /// </summary>
    public static class NovaMeshFix
    {
        public static int Repair(GameObject go, bool verbose = false)
        {
            int fixedCount = 0;
            var cache = new Dictionary<Material, Material>();

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;

                var colors = mesh.colors;
                if (colors == null || colors.Length == 0) continue; // vertex rengi yok → dokusu vardır

                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    // Materyal zaten dokuluysa veya beyaz değilse dokunma
                    Texture tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap")
                                : m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    if (tex != null) continue;
                    Color baseCol = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor")
                                  : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    if (baseCol.r < 0.92f || baseCol.g < 0.92f || baseCol.b < 0.92f) continue; // beyaz değil

                    if (!cache.TryGetValue(m, out var repaired))
                    {
                        // Bu alt-mesh'in üçgenlerinden baskın vertex rengini çıkar
                        Color avg = AverageColor(mesh, colors, i);
                        repaired = new Material(m) { name = m.name + "_vc" };
                        if (repaired.HasProperty("_BaseColor")) repaired.SetColor("_BaseColor", avg);
                        if (repaired.HasProperty("_Color")) repaired.SetColor("_Color", avg);
                        if (repaired.HasProperty("_Smoothness")) repaired.SetFloat("_Smoothness", 0.08f);
                        if (repaired.HasProperty("_Glossiness")) repaired.SetFloat("_Glossiness", 0.08f);
                        cache[m] = repaired;
                    }
                    mats[i] = repaired;
                    changed = true;
                }
                if (changed) { r.sharedMaterials = mats; fixedCount++; }
            }

            if (verbose && fixedCount > 0)
                Debug.Log($"[Nova Renk Onarım] {go.name}: {fixedCount} parça vertex renginden boyandı.");
            return fixedCount;
        }

        /// <summary>Alt-mesh'in kullandığı vertex'lerin ortalama rengi (aykırı beyazlar hariç).</summary>
        private static Color AverageColor(Mesh mesh, Color[] colors, int subMesh)
        {
            try
            {
                var idx = subMesh < mesh.subMeshCount ? mesh.GetTriangles(subMesh) : mesh.triangles;
                if (idx == null || idx.Length == 0) return AverageAll(colors);
                float r = 0f, g = 0f, b = 0f; int n = 0;
                int step = Mathf.Max(1, idx.Length / 900); // hızlı örnekleme
                for (int i = 0; i < idx.Length; i += step)
                {
                    int vi = idx[i];
                    if (vi < 0 || vi >= colors.Length) continue;
                    var c = colors[vi];
                    r += c.r; g += c.g; b += c.b; n++;
                }
                return n > 0 ? new Color(r / n, g / n, b / n) : AverageAll(colors);
            }
            catch { return AverageAll(colors); }
        }

        private static Color AverageAll(Color[] colors)
        {
            float r = 0f, g = 0f, b = 0f;
            int step = Mathf.Max(1, colors.Length / 900), n = 0;
            for (int i = 0; i < colors.Length; i += step) { r += colors[i].r; g += colors[i].g; b += colors[i].b; n++; }
            return n > 0 ? new Color(r / n, g / n, b / n) : Color.white;
        }
    }
}
