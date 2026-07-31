using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using NovaWorld;

namespace UnityAI
{
    /// <summary>
    /// Üretilen dünyada birinci-şahıs gezinti. NovaPlayer (CharacterController + kamera + NovaFirstPerson)
    /// oluşturur, dünyanın ortasında zemine oturtur ve Play moduna girer.
    /// </summary>
    public static class WorldExplorer
    {
        public const string PlayerName = "NovaPlayer";

        /// <summary>
        /// Oyuncuyu kurar ama Play'e GİRMEZ — harita kurulumunun sonunda çağrılır.
        /// Böylece kullanıcı Play'e bastığında boş sahne değil, gezilebilir bir dünya bulur.
        /// worldLabel HUD'da gösterilir (ör. "Ova · 400 m").
        /// </summary>
        public static void EnsurePlayer(string worldLabel, Action<string> log = null)
        {
            var existing = GameObject.Find(PlayerName);
            if (existing != null)
            {
                // Zaten var: sadece güvenli bir konuma taşı ve etiketi güncelle
                FindSpawn(out var sp, out var lk);
                existing.transform.position = sp;
                var d0 = lk - sp; d0.y = 0f;
                if (d0.sqrMagnitude > 0.01f) existing.transform.rotation = Quaternion.LookRotation(d0.normalized);
                var fp0 = existing.GetComponent<NovaFirstPerson>();
                if (fp0 != null) fp0.worldLabel = worldLabel;
                return;
            }
            Spawn(worldLabel, log);
        }

        public static void SpawnAndPlay(Action<string> log = null)
        {
            Spawn(null, log);
            if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        private static void Spawn(string worldLabel, Action<string> log)
        {
            var existing = GameObject.Find(PlayerName);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);

            var player = new GameObject(PlayerName);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var camGo = new GameObject("NovaCamera");
            camGo.transform.SetParent(player.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 1.65f, 0f);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.depth = 100f; // mevcut kameraların üstünde göster
            // çift AudioListener uyarısını önlemek için sahnedeki diğerlerini kapat
            foreach (var al in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude)) al.enabled = false;
            camGo.AddComponent<AudioListener>();

            var fp = player.AddComponent<NovaFirstPerson>();
            fp.worldLabel = worldLabel ?? "";   // Play HUD'unda görünür

            FindSpawn(out var spawn, out var lookAt);
            player.transform.position = spawn;
            var dir = lookAt - spawn; dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                player.transform.rotation = Quaternion.LookRotation(dir.normalized); // şehre dönük başla

            Undo.RegisterCreatedObjectUndo(player, "Nova: Gez oyuncusu");
            log?.Invoke(NovaLocale.T("explorer.playerAdded", spawn.x, spawn.y, spawn.z));
        }

        /// <summary>Dışarıdan (T7 Oyuna Hazırlık) da kullanılabilen güvenli spawn bulucu.</summary>
        public static void FindSpawnPoint(out Vector3 spawn, out Vector3 lookAt) => FindSpawn(out spawn, out lookAt);

        // Spawn: şehir MERKEZİ çevresindeki halkalarda yol üstüne inen, etrafı boş bir nokta bul;
        // oyuncu şehir merkezine dönük başlar (boş ufka bakma hatası biter).
        // NOT: Şehrin TÜM renderer bounds'una güvenme — tek bozuk asset bounds'u şişirebilir;
        // yalnızca Ground sınırları kullanılır.
        static void FindSpawn(out Vector3 spawn, out Vector3 lookAt)
        {
            spawn = new Vector3(0f, 2f, 0f);
            lookAt = spawn + Vector3.forward * 10f;

            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!go.name.StartsWith("NovaCity") && !go.name.StartsWith("NovaTown") && !go.name.StartsWith("NovaTerra")) continue;

                Bounds ground;
                var terr = go.GetComponentInChildren<Terrain>();
                var groundTr = go.transform.Find("Ground");
                var groundR = groundTr != null ? groundTr.GetComponent<Renderer>() : null;
                if (terr != null)
                    ground = new Bounds(terr.transform.position + terr.terrainData.size * 0.5f, terr.terrainData.size);
                else if (groundR != null) ground = groundR.bounds;
                else
                {
                    var rs = go.GetComponentsInChildren<Renderer>();
                    if (rs.Length == 0) break;
                    ground = rs[0].bounds;
                    foreach (var r in rs) ground.Encapsulate(r.bounds);
                }

                var c = ground.center;
                float y0 = ground.max.y + 200f;

                // Su varsa (arazi haritaları): su seviyesinin altına doğma
                float waterY = float.MinValue;
                var waterTr = go.transform.Find("Water");
                if (waterTr != null) waterY = waterTr.position.y;

                // ÖNCE gerçek yol geometrisi: merkeze en yakın "Road" şeridinin üstüne doğ.
                // (Tahmine dayalı halka aramasından daha sağlam — yol her build'de var.)
                Transform bestRoad = null; float bestD = float.MaxValue;
                foreach (var ch in go.GetComponentsInChildren<Transform>()) // konteynır altındakiler dahil
                {
                    if (ch.name != "Road") continue;
                    float d = (ch.position.x - c.x) * (ch.position.x - c.x) + (ch.position.z - c.z) * (ch.position.z - c.z);
                    if (d < bestD) { bestD = d; bestRoad = ch; }
                }
                if (bestRoad != null)
                {
                    var rp = bestRoad.position;
                    if (Physics.Raycast(new Vector3(rp.x, y0, rp.z), Vector3.down, out var rHit, 5000f)
                        && rHit.point.y <= ground.max.y + 2f
                        && !Physics.CheckCapsule(rHit.point + Vector3.up * 0.6f, rHit.point + Vector3.up * 1.5f, 0.3f))
                    {
                        spawn = rHit.point + Vector3.up * 1.1f;
                        lookAt = new Vector3(c.x, rHit.point.y, c.z);
                        return;
                    }
                    // Raycast bir şeye takıldıysa bile şeridin üstü güvenlidir
                    spawn = new Vector3(rp.x, rp.y + 1.2f, rp.z);
                    lookAt = new Vector3(c.x, rp.y, c.z);
                    return;
                }

                // Arazi haritalarında: DELİK olmayan, düz ve güvenli bir nokta ara.
                // (Terrain'de delik = collider yok = oyuncu düşer. Kullanıcı fırçayla delik
                // açmış olabilir; spawn'ı asla oraya koyma.)
                if (terr != null)
                {
                    var td = terr.terrainData;
                    var tp = terr.transform.position;
                    for (float f = 0.10f; f <= 0.45f; f += 0.07f)
                        for (int a = 0; a < 12; a++)
                        {
                            float ang = a * Mathf.PI / 6f;
                            float u = 0.5f + Mathf.Cos(ang) * f;
                            float v = 0.5f + Mathf.Sin(ang) * f;
                            if (u <= 0.02f || v <= 0.02f || u >= 0.98f || v >= 0.98f) continue;
                            if (td.GetSteepness(u, v) > 25f) continue;          // dik yamaca doğma
                            float wx = tp.x + u * td.size.x, wz = tp.z + v * td.size.z;
                            float wy = terr.SampleHeight(new Vector3(wx, 0f, wz)) + tp.y;
                            if (wy < waterY + 1f) continue;                      // suya doğma
                            var probe = new Vector3(wx, wy + 1.2f, wz);
                            // Delik testi: ayak altında gerçekten collider var mı?
                            if (!Physics.Raycast(probe, Vector3.down, out var gh, 6f)) continue;
                            if (Physics.CheckCapsule(probe + Vector3.up * 0.3f, probe + Vector3.up * 1.4f, 0.35f)) continue;
                            spawn = gh.point + Vector3.up * 1.1f;
                            lookAt = new Vector3(c.x, gh.point.y, c.z);
                            return;
                        }
                }

                // Yol yoksa: merkezden dışa doğru halkalar × 8 yön — boş bir nokta ara
                for (float f = 0.12f; f <= 0.5f; f += 0.12f)
                    for (int a = 0; a < 8; a++)
                    {
                        float ang = a * Mathf.PI / 4f;
                        float px = c.x + Mathf.Cos(ang) * ground.size.x * f;
                        float pz = c.z + Mathf.Sin(ang) * ground.size.z * f;
                        if (!Physics.Raycast(new Vector3(px, y0, pz), Vector3.down, out var hit, 5000f)) continue;
                        if (hit.point.y > ground.max.y + 2f) continue; // çatıya inme
                        if (hit.point.y < waterY + 0.5f) continue;     // suyun içine doğma
                        // Kapsül boşluk kontrolü: bina/araç içinde doğmayı önle
                        if (Physics.CheckCapsule(hit.point + Vector3.up * 0.6f, hit.point + Vector3.up * 1.5f, 0.3f)) continue;
                        spawn = hit.point + Vector3.up * 1.1f;
                        lookAt = new Vector3(c.x, hit.point.y, c.z);
                        return;
                    }

                // Hiç uygun nokta yoksa: merkezin üstü
                spawn = new Vector3(c.x, ground.max.y + 1.2f, c.z);
                lookAt = spawn + Vector3.forward * 10f;
                return;
            }
        }
    }
}
