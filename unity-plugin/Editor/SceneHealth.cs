using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// E2 — SAHNE SAĞLIK BOTU. "Son %10 cila, zamanın %50'si" acısına tek tık cevap:
    /// kayıp script, kayıp materyal, sıfır/absürt ölçek, sıfır boyutlu collider, origin'den
    /// kaçmış objeler, ışık/kamera eksikliği, poligon yükü. Rapor + onaylı otomatik düzeltme.
    /// (Unity AI'da yok — bizim farklılaştırıcımız.)
    /// </summary>
    public static class SceneHealth
    {
        public static string ScanAndReport(bool offerFix = true)
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene();
            sb.AppendLine(NovaLocale.T("health.scanTitle", scene.name));

            int missingScripts = 0, nullMats = 0, badScale = 0, zeroCol = 0, farAway = 0;
            int colorless = 0, offTerrain = 0, floating = 0;
            int emptyGO = 0, nullMesh = 0;               // GENEL: boş nesne, kayıp mesh
            long totalTris = 0;
            int renderers = 0;
            var missingList = new List<GameObject>();
            var colorlessFiles = new HashSet<string>();
            var badColliders = new List<GameObject>();   // sıfır boyutlu collider objeleri
            var strayObjects = new List<GameObject>();   // arazi dışına kaçmış Nova objeleri
            var floaters = new List<(GameObject go, float drop)>(); // havada asılı kalmış objeler
            var bigTextures = new HashSet<Texture>();     // GENEL: >2048 px içe aktarılmış dokular
            var terrain = Object.FindAnyObjectByType<Terrain>();

            foreach (var root in scene.GetRootGameObjects())
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
            {
                var go = tr.gameObject;

                // 1) Kayıp (missing) script bileşenleri
                int m = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (m > 0) { missingScripts += m; missingList.Add(go); }

                // 2) Kayıp materyal
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    renderers++;
                    foreach (var mat in r.sharedMaterials)
                        if (mat == null) { nullMats++; Debug.LogWarning("[Nova Sağlık] Kayıp materyal: " + Path(tr), go); break; }
                    var mf = go.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) totalTris += mf.sharedMesh.triangles.Length / 3;

                    // RENKSİZ MODEL: dokusu yok + base color beyaz → sahnede bembeyaz görünür
                    var mark = go.GetComponentInParent<NovaWorld.NovaPlaced>();
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;
                        Texture t = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                                  : mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                        Color baseCol = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                                      : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                        if (t == null && baseCol.r > 0.92f && baseCol.g > 0.92f && baseCol.b > 0.92f)
                        {
                            colorless++;
                            if (mark != null && !string.IsNullOrEmpty(mark.assetFile)) colorlessFiles.Add(mark.assetFile);
                            Debug.LogWarning("[Nova Sağlık] Renksiz (beyaz) model: " + Path(tr), go);
                            break;
                        }
                    }

                    // ARAZİ DIŞINDA KALAN NOVA OBJESİ (yanlış yere yerleşmiş)
                    if (mark != null && terrain != null)
                    {
                        var tp = terrain.transform.position; var ts = terrain.terrainData.size;
                        var p = tr.position;
                        if (p.x < tp.x - 5f || p.z < tp.z - 5f || p.x > tp.x + ts.x + 5f || p.z > tp.z + ts.z + 5f)
                        { offTerrain++; strayObjects.Add(go); Debug.LogWarning("[Nova Sağlık] Arazi dışında: " + Path(tr), go); }
                    }
                }

                // 3) Sıfır / absürt ölçek
                var s = tr.lossyScale;
                float mx = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                float mn = Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
                if (mn <= 1e-6f || mx > 10000f)
                { badScale++; Debug.LogWarning($"[Nova Sağlık] Şüpheli ölçek {s}: " + Path(tr), go); }

                // 4) Sıfır boyutlu collider
                var bc = go.GetComponent<BoxCollider>();
                if (bc != null && (bc.size.x <= 0f || bc.size.y <= 0f || bc.size.z <= 0f))
                { zeroCol++; badColliders.Add(go); Debug.LogWarning("[Nova Sağlık] Sıfır boyutlu BoxCollider: " + Path(tr), go); }

                // 5) Origin'den kaçmış obje (float hassasiyeti bozulur)
                if (tr.position.magnitude > 50000f)
                { farAway++; Debug.LogWarning($"[Nova Sağlık] Origin'den çok uzak ({tr.position}): " + Path(tr), go); }

                // 7) GENEL: kayıp mesh (MeshFilter var ama mesh null → görünmez, kafa karıştırır)
                var mfChk = go.GetComponent<MeshFilter>();
                if (mfChk != null && mfChk.sharedMesh == null)
                { nullMesh++; Debug.LogWarning("[Nova Sağlık] Kayıp mesh: " + Path(tr), go); }

                // 8) GENEL: boş nesne (yalnız Transform + çocuğu yok → sahne dağınıklığı)
                if (go.GetComponents<Component>().Length == 1 && tr.childCount == 0)
                    emptyGO++;

                // 9) GENEL: dev doku (>2048 px içe aktarılmış → build şişer, mobilde kasar)
                if (r != null)
                    foreach (var mat in r.sharedMaterials)
                    {
                        if (mat == null) continue;
                        foreach (var texName in new[] { "_BaseMap", "_MainTex", "_BumpMap", "_EmissionMap" })
                        {
                            if (!mat.HasProperty(texName)) continue;
                            var tex = mat.GetTexture(texName);
                            if (tex != null && (tex.width > 2048 || tex.height > 2048)) bigTextures.Add(tex);
                        }
                    }
            }

            // 5b) HAVADA ASILI OBJE — zemine oturması gerekirken gökyüzünde duran nesneler.
            // (Ekranda "uçan kaya/çalı" olarak görünür; oyuncu için en göze batan hata.)
            if (terrain != null)
            {
                foreach (var mark in Object.FindObjectsByType<NovaWorld.NovaPlaced>(FindObjectsInactive.Include))
                {
                    if (mark == null) continue;
                    var rends = mark.GetComponentsInChildren<Renderer>();
                    if (rends.Length == 0) continue;
                    var bb = rends[0].bounds;
                    foreach (var r in rends) bb.Encapsulate(r.bounds);

                    float groundY = terrain.SampleHeight(bb.center) + terrain.transform.position.y;
                    float gap = bb.min.y - groundY;
                    if (gap > 1.0f)
                    {
                        floating++;
                        floaters.Add((mark.gameObject, gap));
                        Debug.LogWarning($"[Nova Sağlık] Havada asılı ({gap:0.0} m yukarıda): " + Path(mark.transform), mark.gameObject);
                    }
                }
            }

            // 6) Sahne geneli
            bool hasLight = false, hasCam = false;
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) if (l.enabled) { hasLight = true; break; }
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude)) if (c.enabled) { hasCam = true; break; }
            // GENEL: çoklu AudioListener — Unity sürekli uyarır, ses karışır
            int audioListeners = 0;
            var extraListeners = new List<AudioListener>();
            foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude))
            {
                if (!al.enabled) continue;
                audioListeners++;
                if (audioListeners > 1) extraListeners.Add(al);
            }

            sb.AppendLine(NovaLocale.T("health.missingScripts", missingScripts, missingScripts > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.missingMaterials", nullMats, nullMats > 0 ? NovaLocale.T("health.pinkHint") : " ✓"));
            sb.AppendLine(NovaLocale.T("health.suspiciousScale", badScale, badScale > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.zeroCollider", zeroCol, zeroCol > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.farFromOrigin", farAway, farAway > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.colorlessModels", colorless, colorless > 0 ? NovaLocale.T("health.colorlessHint", colorlessFiles.Count) : " ✓"));
            sb.AppendLine(NovaLocale.T("health.offTerrain", offTerrain, offTerrain > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.floating", floating, floating > 0 ? NovaLocale.T("health.floatingHint") : " ✓"));
            sb.AppendLine(NovaLocale.T("health.nullMesh", nullMesh, nullMesh > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.emptyGO", emptyGO, emptyGO > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.multiAudio", audioListeners, audioListeners > 1 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.bigTextures", bigTextures.Count, bigTextures.Count > 0 ? " ⚠" : " ✓"));
            sb.AppendLine(NovaLocale.T("health.lightCamera",
                hasLight ? NovaLocale.T("health.present") : NovaLocale.T("health.missingDark"),
                hasCam ? NovaLocale.T("health.present") : NovaLocale.T("health.missing")));
            sb.AppendLine(NovaLocale.T("health.rendererTris", renderers, totalTris.ToString("N0"), totalTris > 2_000_000 ? NovaLocale.T("health.highForMobile") : ""));
            sb.AppendLine(NovaLocale.T("health.timeFooter", System.DateTime.Now.ToString("HH:mm:ss")));
            sb.Append(NovaLocale.T("health.detailsInConsole"));

            // ---- ONARIM: tespit yetmez, düzeltir ----
            var fixList = new List<string>();
            if (missingScripts > 0) fixList.Add(NovaLocale.T("health.fixMissingScripts", missingScripts));
            if (colorless > 0) fixList.Add(NovaLocale.T("health.fixColorless", colorless));
            if (zeroCol > 0) fixList.Add(NovaLocale.T("health.fixColliders", zeroCol));
            if (offTerrain > 0) fixList.Add(NovaLocale.T("health.fixOffTerrain", offTerrain));
            if (floating > 0) fixList.Add(NovaLocale.T("health.fixFloating", floating));
            if (extraListeners.Count > 0) fixList.Add(NovaLocale.T("health.fixAudio", extraListeners.Count));
            if (bigTextures.Count > 0) fixList.Add(NovaLocale.T("health.fixTextures", bigTextures.Count));

            if (offerFix && fixList.Count > 0 &&
                EditorUtility.DisplayDialog(NovaLocale.T("dialog.sceneHealthRepair.title"),
                    NovaLocale.T("dialog.sceneHealthRepair.body", string.Join("\n", fixList)),
                    NovaLocale.T("dialog.repair.apply"), NovaLocale.T("dialog.repair.later")))
            {
                int rScripts = 0, rColor = 0, rCol = 0, rOff = 0, rFloat = 0;

                foreach (var go in missingList)
                {
                    Undo.RegisterCompleteObjectUndo(go, "Nova: Kayıp script temizliği");
                    rScripts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
                foreach (var root2 in scene.GetRootGameObjects())
                    rColor += NovaMeshFix.Repair(root2, verbose: true);
                foreach (var bad in badColliders)
                {
                    if (bad == null) continue;
                    Undo.DestroyObjectImmediate(bad);
                    rCol++;
                }
                foreach (var stray in strayObjects)
                {
                    if (stray == null) continue;
                    Undo.DestroyObjectImmediate(stray);
                    rOff++;
                }

                // Havada asılı objeleri sil değil, ZEMİNE İNDİR (kullanıcının içeriği korunur)
                foreach (var (go, drop) in floaters)
                {
                    if (go == null) continue;
                    Undo.RecordObject(go.transform, "Nova: Zemine indir");
                    go.transform.position -= new Vector3(0f, drop, 0f);
                    rFloat++;
                }

                // Fazla AudioListener'ları kapat (ilki kalsın — "çift listener" uyarısı biter)
                int rAudio = 0;
                foreach (var al in extraListeners)
                {
                    if (al == null) continue;
                    Undo.RecordObject(al, "Nova: Fazla AudioListener kapat");
                    al.enabled = false;
                    rAudio++;
                }

                // Dev dokuları 2048'e indir (import ayarı — build küçülür, mobil rahatlar)
                int rTex = 0;
                foreach (var tex in bigTextures)
                {
                    var path = AssetDatabase.GetAssetPath(tex);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (AssetImporter.GetAtPath(path) is TextureImporter ti && ti.maxTextureSize > 2048)
                    {
                        ti.maxTextureSize = 2048;
                        ti.SaveAndReimport();
                        rTex++;
                    }
                }

                sb.AppendLine();
                sb.Append(NovaLocale.T("health.repairedSummary", rScripts, rColor, rCol, rOff, rFloat));
                if (rAudio > 0 || rTex > 0) sb.Append(NovaLocale.T("health.repairedExtra", rAudio, rTex));
                Debug.Log($"[Nova Sağlık] Onarım: {rScripts} script, {rColor} renk, {rCol} collider, {rOff} yabancı obje, {rFloat} zemine indirildi, {rAudio} ses kapatıldı, {rTex} doku küçültüldü.");
            }

            return sb.ToString();
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
