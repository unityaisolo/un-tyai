using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovaWorld
{
    /// <summary>
    /// KULE SAVUNMA. Kıvrımlı bir yol üretir; düşmanlar yolu takip ederek üsse yürür.
    /// Fareyle yol KENARINA kule yerleştirirsin (altın harcar), kuleler menzildeki
    /// düşmanlara ateş eder. Düşman üsse ulaşırsa can gider. Dalgalar giderek zorlaşır.
    ///
    /// Üstten (izometrik) kamera; NavMesh gerektirmez (yol noktalarını takip eder).
    /// Runtime-only: UnityEditor'a bağımlı DEĞİL.
    /// </summary>
    public class NovaTowerDefense : MonoBehaviour
    {
        [Header("Yol")]
        public int pathPoints = 10;
        public float pathLength = 120f;
        public float pathWidth = 6f;
        public float curviness = 22f;       // yanal sapma (m)

        [Header("Dalga")]
        public int startEnemies = 5;
        public int perWave = 3;
        public float spawnInterval = 0.9f;
        public float waveDelay = 6f;
        public float enemySpeed = 4f;
        public float speedPerWave = 0.35f;
        public float enemyHealth = 40f;
        public float healthPerWave = 12f;

        [Header("Ekonomi / Üs")]
        public int startGold = 240;      // ~6 kule ile başla (2 kule kilidi bitti)
        public int towerCost = 40;
        public int goldPerKill = 18;
        public int goldPerWave = 60;     // dalga tamamlama bonusu
        public int baseHealth = 10;

        [Header("Kamera (kuş bakışı)")]
        public float camHeight = 42f;
        public float camBack = 26f;      // hedefin gerisinde
        public float camPitch = 62f;     // 90 = tam tepeden
        public float camPanSpeed = 45f;  // WASD/ok tuşları
        public float camZoomSpeed = 260f;

        [Header("Kule")]
        public float towerRange = 18f;
        public float towerDps = 22f;
        public float towerFireRate = 0.5f;

        [Header("Görsel (opsiyonel)")]
        public Material pathMaterial;
        public GameObject enemyModel;
        public GameObject towerModel;

        Camera _cam;
        Vector3 _camTarget;              // kameranın odaklandığı yer düzlemi noktası
        int _gold, _hp, _wave, _score;
        float _waveTimer, _spawnTimer;
        int _toSpawn;
        bool _dead, _waveBonusPaid;
        readonly List<Vector3> _path = new List<Vector3>();
        readonly List<Enemy> _enemies = new List<Enemy>();
        readonly List<Tower> _towers = new List<Tower>();
        System.Random _rnd;

        class Enemy { public GameObject go; public float hp, maxHp; public int seg; }
        class Tower { public GameObject go; public float cd; }

        void Start()
        {
            _rnd = new System.Random();
            _gold = startGold; _hp = baseHealth;
            BuildPath();

            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("NovaTDCam");
                _cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            // Kuş bakışı: yolun ortasına odaklı, YAKIN başlangıç (uzaktan bakma sorunu bitti)
            _camTarget = _path.Count > 0 ? _path[_path.Count / 2] : Vector3.zero;
            ApplyCamera();

            _waveTimer = 3f;
        }

        void Update()
        {
            if (_dead) { HandleRestart(); return; }

            // ---- Dalga akışı ----
            if (_toSpawn > 0)
            {
                _spawnTimer -= Time.deltaTime;
                if (_spawnTimer <= 0f) { _spawnTimer = spawnInterval; SpawnEnemy(); _toSpawn--; }
            }
            else if (_enemies.Count == 0)
            {
                // Dalga temizlendi: bonus altın (bir kez) → kule kurmaya devam edebilsin
                if (_wave > 0 && !_waveBonusPaid)
                { _waveBonusPaid = true; _gold += goldPerWave; _score += 25 * _wave; }
                _waveTimer -= Time.deltaTime;
                if (_waveTimer <= 0f) StartWave();
            }

            UpdateEnemies();
            UpdateTowers();
            HandleCamera();
            HandlePlacement();
        }

        /// <summary>Kuş bakışı kamera: WASD/oklar ile kaydır, tekerlek ile yaklaş/uzaklaş.</summary>
        void HandleCamera()
        {
            float px = 0f, pz = 0f, zoom = 0f;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                px = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                   - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                pz = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                   - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            }
            var ms = Mouse.current;
            if (ms != null) zoom = ms.scroll.ReadValue().y * 0.02f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            px = Input.GetAxisRaw("Horizontal");
            pz = Input.GetAxisRaw("Vertical");
            zoom = Input.GetAxis("Mouse ScrollWheel") * 10f;
#endif
            if (Mathf.Abs(px) > 0.01f || Mathf.Abs(pz) > 0.01f)
            {
                _camTarget += new Vector3(px, 0f, pz) * camPanSpeed * Time.deltaTime;
                // Haritadan çok uzaklaşmasın
                float lim = pathLength * 0.75f;
                _camTarget.x = Mathf.Clamp(_camTarget.x, -lim, lim);
                _camTarget.z = Mathf.Clamp(_camTarget.z, -lim, lim);
            }
            if (Mathf.Abs(zoom) > 0.001f)
                camHeight = Mathf.Clamp(camHeight - zoom * camZoomSpeed * Time.deltaTime * 3f, 14f, 110f);

            ApplyCamera(smooth: true);
        }

        void ApplyCamera(bool smooth = false)
        {
            if (_cam == null) return;
            // Yükseklikle uyumlu geri mesafe → sabit bakış açısı
            float back = camBack * (camHeight / 42f);
            var want = _camTarget + new Vector3(0f, camHeight, -back);
            _cam.transform.position = smooth
                ? Vector3.Lerp(_cam.transform.position, want, 1f - Mathf.Exp(-10f * Time.deltaTime))
                : want;
            _cam.transform.rotation = Quaternion.Euler(camPitch, 0f, 0f);
        }

        void StartWave()
        {
            _wave++;
            _toSpawn = startEnemies + (_wave - 1) * perWave;
            _spawnTimer = 0f;
            _waveTimer = waveDelay;
            _waveBonusPaid = false;
        }

        void SpawnEnemy()
        {
            if (_path.Count < 2) return;
            GameObject go;
            if (enemyModel != null) { go = Instantiate(enemyModel); go.SetActive(true); FitHeight(go, 1.8f); }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);
                Paint(go, new Color(0.85f, 0.3f, 0.3f));
            }
            StripColliders(go);
            go.name = "TDEnemy";
            go.transform.position = _path[0] + Vector3.up * 0.9f;
            float hp = enemyHealth + (_wave - 1) * healthPerWave;
            _enemies.Add(new Enemy { go = go, hp = hp, maxHp = hp, seg = 0 });
        }

        void UpdateEnemies()
        {
            float speed = enemySpeed + (_wave - 1) * speedPerWave;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e.go == null) { _enemies.RemoveAt(i); continue; }

                // Yol noktalarını sırayla takip et
                if (e.seg >= _path.Count - 1)
                {
                    // Üsse ulaştı
                    Destroy(e.go); _enemies.RemoveAt(i);
                    _hp--;
                    if (_hp <= 0) { _hp = 0; _dead = true; Debug.Log($"[Nova TD] Üs düştü! Dalga {_wave}, skor {_score}"); }
                    continue;
                }
                var target = _path[e.seg + 1] + Vector3.up * 0.9f;
                var pos = e.go.transform.position;
                var to = target - pos;
                if (to.sqrMagnitude < 1.2f) { e.seg++; continue; }
                var dir = to.normalized;
                e.go.transform.position = pos + dir * speed * Time.deltaTime;
                e.go.transform.rotation = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.z));
            }
        }

        void UpdateTowers()
        {
            foreach (var t in _towers)
            {
                if (t.go == null) continue;
                t.cd -= Time.deltaTime;
                if (t.cd > 0f) continue;

                // Menzildeki EN İLERİ düşmanı vur (üsse en yakın)
                Enemy best = null; int bestSeg = -1;
                foreach (var e in _enemies)
                {
                    if (e.go == null) continue;
                    if ((e.go.transform.position - t.go.transform.position).sqrMagnitude > towerRange * towerRange) continue;
                    if (e.seg > bestSeg) { bestSeg = e.seg; best = e; }
                }
                if (best == null) continue;

                t.cd = towerFireRate;
                best.hp -= towerDps * towerFireRate;
                // Namluyu hedefe çevir
                var d = best.go.transform.position - t.go.transform.position;
                if (d.sqrMagnitude > 0.01f)
                    t.go.transform.rotation = Quaternion.LookRotation(new Vector3(d.x, 0f, d.z));

                if (best.hp <= 0f)
                {
                    Destroy(best.go);
                    _enemies.Remove(best);
                    _gold += goldPerKill; _score += 10;
                }
            }
        }

        void HandlePlacement()
        {
            bool click = false; Vector2 mp = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var ms = Mouse.current;
            if (ms != null) { click = ms.leftButton.wasPressedThisFrame; mp = ms.position.ReadValue(); }
#elif ENABLE_LEGACY_INPUT_MANAGER
            click = Input.GetMouseButtonDown(0); mp = Input.mousePosition;
#endif
            if (!click || _cam == null) return;
            if (_gold < towerCost) return;

            // Fare ışınını yer düzlemine (y=0) düşür
            var ray = _cam.ScreenPointToRay(mp);
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f) return;
            var p = ray.origin + ray.direction * t;

            // Yola ÇOK yakın veya çok uzak olmasın (kenara kurulur)
            float d = DistanceToPath(p);
            if (d < pathWidth * 0.55f || d > pathWidth * 2.6f) return;
            // Başka kuleye çakışmasın
            foreach (var tw in _towers)
                if (tw.go != null && (tw.go.transform.position - p).sqrMagnitude < 9f) return;

            GameObject go;
            if (towerModel != null) { go = Instantiate(towerModel); go.SetActive(true); FitHeight(go, 4f); }
            else
            {
                go = new GameObject("Tower");
                var baseCube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseCube.transform.SetParent(go.transform, false);
                baseCube.transform.localScale = new Vector3(1.4f, 1.2f, 1.4f);
                baseCube.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                StripColliders(baseCube);
                Paint(baseCube, new Color(0.35f, 0.45f, 0.7f));
                var barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                barrel.transform.SetParent(go.transform, false);
                barrel.transform.localScale = new Vector3(0.3f, 0.3f, 1.8f);
                barrel.transform.localPosition = new Vector3(0f, 2.2f, 0.7f);
                StripColliders(barrel);
                Paint(barrel, new Color(0.22f, 0.26f, 0.34f));
            }
            StripColliders(go);
            go.transform.position = new Vector3(p.x, 0f, p.z);
            _towers.Add(new Tower { go = go });
            _gold -= towerCost;
        }

        void BuildPath()
        {
            var root = new GameObject("TDPath");
            root.transform.SetParent(transform.parent != null ? transform.parent : null);

            // Z boyunca ilerleyen, yanal kıvrımlı yol
            float phase = (float)(_rnd.NextDouble() * 6.28);
            for (int i = 0; i < pathPoints; i++)
            {
                float u = i / (float)(pathPoints - 1);
                float z = -pathLength * 0.5f + u * pathLength;
                float x = Mathf.Sin(u * 3.2f + phase) * curviness + Mathf.Sin(u * 7.1f + phase * 2f) * curviness * 0.3f;
                _path.Add(new Vector3(x, 0f, z));
            }

            // Yol karoları
            for (int i = 0; i < _path.Count - 1; i++)
            {
                var a = _path[i]; var b = _path[i + 1];
                var dir = b - a; float len = dir.magnitude;
                if (len < 0.01f) continue;
                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "PathPiece";
                piece.transform.SetParent(root.transform);
                piece.transform.position = (a + b) * 0.5f - Vector3.up * 0.1f;
                piece.transform.rotation = Quaternion.LookRotation(dir.normalized);
                piece.transform.localScale = new Vector3(pathWidth, 0.2f, len * 1.05f);
                StripColliders(piece);
                if (pathMaterial != null) piece.GetComponent<Renderer>().sharedMaterial = pathMaterial;
                else Paint(piece, new Color(0.45f, 0.38f, 0.28f));
            }

            // Üs (yolun sonu) + giriş işareti
            var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseGo.name = "Base";
            baseGo.transform.SetParent(root.transform);
            baseGo.transform.position = _path[_path.Count - 1] + Vector3.up * 1.5f;
            baseGo.transform.localScale = new Vector3(6f, 3f, 6f);
            StripColliders(baseGo);
            Paint(baseGo, new Color(0.3f, 0.6f, 0.45f));

            var spawnGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            spawnGo.name = "SpawnGate";
            spawnGo.transform.SetParent(root.transform);
            spawnGo.transform.position = _path[0] + Vector3.up * 1f;
            spawnGo.transform.localScale = new Vector3(pathWidth, 2f, 0.6f);
            StripColliders(spawnGo);
            Paint(spawnGo, new Color(0.7f, 0.3f, 0.3f));

            // Zemin (yol dışı)
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "TDGround";
            ground.transform.SetParent(root.transform);
            ground.transform.position = Vector3.down * 0.2f;
            ground.transform.localScale = new Vector3(pathLength * 0.25f, 1f, pathLength * 0.25f);
            StripColliders(ground);
            Paint(ground, new Color(0.26f, 0.34f, 0.24f));
        }

        float DistanceToPath(Vector3 p)
        {
            float best = float.MaxValue;
            var flat = new Vector3(p.x, 0f, p.z);
            for (int i = 0; i < _path.Count - 1; i++)
            {
                var a = _path[i]; var ab = _path[i + 1] - a;
                float t = Mathf.Clamp01(Vector3.Dot(flat - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                float d = Vector3.Distance(flat, a + ab * t);
                if (d < best) best = d;
            }
            return best;
        }

        void HandleRestart()
        {
            bool r = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null) r = kb.rKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            r = Input.GetKeyDown(KeyCode.R);
#endif
            if (!r) return;
            foreach (var e in _enemies) if (e.go != null) Destroy(e.go);
            foreach (var t in _towers) if (t.go != null) Destroy(t.go);
            _enemies.Clear(); _towers.Clear();
            _gold = startGold; _hp = baseHealth; _wave = 0; _score = 0;
            _toSpawn = 0; _waveTimer = 3f; _dead = false; _waveBonusPaid = false;
            _camTarget = _path.Count > 0 ? _path[_path.Count / 2] : Vector3.zero;
            camHeight = 42f;
            ApplyCamera();
        }

        // ---- Yardımcılar ----
        static void StripColliders(GameObject go)
        { foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c); }

        static void FitHeight(GameObject go, float targetH)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float h = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (h > 1e-4f) go.transform.localScale *= targetH / h;
        }

        static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            r.sharedMaterial = m;
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            GUI.Label(new Rect(16, 12, 500, 28), $"Altın: {_gold}   Üs canı: {_hp}   Dalga: {_wave}", st);
            GUI.Label(new Rect(16, 40, 500, 26), $"Düşman: {_enemies.Count}   Kule: {_towers.Count}   Skor: {_score}", st);
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1, 1, 1, 0.65f) } };
            GUI.Label(new Rect(16, 68, 620, 22),
                $"Yol KENARINA tıkla → kule kur ({towerCost} altın) · WASD kamera · Tekerlek yakınlaş · R yeniden", hint);
            if (_toSpawn == 0 && _enemies.Count == 0 && !_dead)
                GUI.Label(new Rect(16, 88, 460, 22),
                    $"Sonraki dalga: {Mathf.CeilToInt(Mathf.Max(0f, _waveTimer))} sn  (+{goldPerWave} altın bonus alındı)", hint);
            if (_gold < towerCost)
                GUI.Label(new Rect(16, 108, 460, 22), "Altın yetersiz — düşman vur veya dalga bonusunu bekle", hint);

            if (_dead)
            {
                var big = new GUIStyle(GUI.skin.label)
                { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.45f, 0.4f);
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 46), "ÜS DÜŞTÜ", big);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.4f + 42, Screen.width, 26),
                    $"Dalga {_wave} · Skor {_score} — yeniden için R", sub);
            }
        }
    }
}
