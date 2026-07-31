using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovaWorld
{
    /// <summary>
    /// FPS ARENA / DALGA SAVUNMASI. Oyuncu (NovaFirstPerson) haritada gezerken düşman
    /// dalgaları gelir; sol tıkla ateş edilir, düşmanlar yaklaşınca hasar verir.
    /// Her dalgada düşman sayısı ve hızı artar. Düşman modeli atanmazsa kapsül kullanılır.
    ///
    /// NavMesh GEREKTİRMEZ: düşmanlar zemine raycast ile oturarak oyuncuya yürür.
    /// Böylece AI Navigation paketi kurulu olmasa da çalışır. Runtime-only.
    /// </summary>
    public class NovaArena : MonoBehaviour
    {
        [Header("Dalga")]
        public int startEnemies = 4;
        public int enemiesPerWave = 2;      // her dalgada artış
        public float waveDelay = 4f;        // dalgalar arası bekleme
        public float spawnRadius = 35f;     // oyuncudan uzaklık
        public int maxAlive = 20;           // performans tavanı

        [Header("Düşman")]
        public GameObject enemyModel;       // katalogdan (yoksa kapsül)
        public float enemySpeed = 3.2f;
        public float speedPerWave = 0.25f;
        public float enemyHealth = 30f;
        public float touchDamage = 12f;     // saniyede, temas halinde
        public float enemyHeight = 1.8f;

        [Header("Oyuncu")]
        public float playerHealth = 100f;
        public float fireDamage = 25f;
        public float fireRate = 0.18f;      // saniye/atış
        public float range = 120f;

        Transform _player;
        Camera _cam;
        float _hp, _nextFire, _waveTimer;
        int _wave, _score;
        bool _dead, _waveActive;
        readonly List<Enemy> _enemies = new List<Enemy>();
        System.Random _rnd;

        class Enemy { public GameObject go; public float hp; public float touchCd; }

        void Start()
        {
            _rnd = new System.Random();
            _hp = playerHealth;
            _cam = Camera.main;
            var fp = FindAnyObjectByType<NovaFirstPerson>();
            _player = fp != null ? fp.transform : (_cam != null ? _cam.transform : transform);
            _waveTimer = 2f; // ilk dalga öncesi nefes
        }

        void Update()
        {
            if (_player == null) return;
            if (_dead) { HandleRestart(); return; }

            // ---- Dalga yönetimi ----
            if (!_waveActive)
            {
                _waveTimer -= Time.deltaTime;
                if (_waveTimer <= 0f) StartWave();
            }
            else if (_enemies.Count == 0)
            {
                _waveActive = false;
                _waveTimer = waveDelay;
                _score += 50 * _wave;   // dalga bonusu
            }

            UpdateEnemies();
            HandleFire();
        }

        void StartWave()
        {
            _wave++;
            _waveActive = true;
            int n = Mathf.Min(maxAlive, startEnemies + (_wave - 1) * enemiesPerWave);
            for (int i = 0; i < n; i++) SpawnEnemy();
        }

        void SpawnEnemy()
        {
            // Oyuncunun etrafında halka; zemine raycast ile otur
            float ang = (float)(_rnd.NextDouble() * System.Math.PI * 2);
            float dist = spawnRadius * (0.7f + 0.3f * (float)_rnd.NextDouble());
            var p = _player.position + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
            if (Physics.Raycast(p + Vector3.up * 200f, Vector3.down, out var hit, 500f)) p = hit.point;

            GameObject go;
            if (enemyModel != null)
            {
                go = Instantiate(enemyModel);
                go.SetActive(true);
                FitHeight(go, enemyHeight);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.transform.localScale = new Vector3(0.8f, enemyHeight * 0.5f, 0.8f);
                Paint(go, new Color(0.8f, 0.25f, 0.25f));
            }
            StripColliders(go);                      // çarpışmayı biz yönetiyoruz
            go.name = "NovaEnemy";
            go.transform.position = p + Vector3.up * 0.1f;
            _enemies.Add(new Enemy { go = go, hp = enemyHealth });
        }

        void UpdateEnemies()
        {
            float speed = enemySpeed + (_wave - 1) * speedPerWave;
            for (int i = _enemies.Count - 1; i >= 0; i--)
            {
                var e = _enemies[i];
                if (e.go == null) { _enemies.RemoveAt(i); continue; }

                var to = _player.position - e.go.transform.position;
                to.y = 0f;
                float d = to.magnitude;

                if (d > 1.4f)
                {
                    var dir = to.normalized;
                    var next = e.go.transform.position + dir * speed * Time.deltaTime;
                    // Zemine otur (yokuşlarda süzülmesin)
                    if (Physics.Raycast(next + Vector3.up * 50f, Vector3.down, out var gh, 200f))
                        next.y = gh.point.y + 0.1f;
                    e.go.transform.position = next;
                    e.go.transform.rotation = Quaternion.LookRotation(dir);
                }
                else
                {
                    // Temas hasarı (saniyede bir kez)
                    e.touchCd -= Time.deltaTime;
                    if (e.touchCd <= 0f) { e.touchCd = 1f; Damage(touchDamage); }
                }
            }
        }

        void HandleFire()
        {
            bool fire = false;
#if ENABLE_INPUT_SYSTEM
            var ms = Mouse.current;
            if (ms != null) fire = ms.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            fire = Input.GetMouseButton(0);
#endif
            if (!fire || _cam == null) return;
            _nextFire -= Time.deltaTime;
            if (_nextFire > 0f) return;
            _nextFire = fireRate;

            // Ekran merkezinden ışın: en yakın düşmanı vur (collider'sız olduğu için manuel test)
            var ro = _cam.transform.position;
            var rd = _cam.transform.forward;
            Enemy best = null; float bestT = range;
            foreach (var e in _enemies)
            {
                if (e.go == null) continue;
                var c = e.go.transform.position + Vector3.up * enemyHeight * 0.5f;
                var oc = c - ro;
                float t = Vector3.Dot(oc, rd);
                if (t < 0f || t > bestT) continue;
                float miss = (oc - rd * t).magnitude;
                if (miss > 0.9f) continue;           // isabet yarıçapı
                best = e; bestT = t;
            }
            if (best == null) return;

            best.hp -= fireDamage;
            if (best.hp <= 0f)
            {
                if (best.go != null) Destroy(best.go);
                _enemies.Remove(best);
                _score += 10;
            }
        }

        void Damage(float amount)
        {
            _hp -= amount;
            if (_hp <= 0f) { _hp = 0f; _dead = true; Debug.Log($"[Nova Arena] Oyun bitti — dalga {_wave}, skor {_score}"); }
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
            _enemies.Clear();
            _hp = playerHealth; _wave = 0; _score = 0; _dead = false;
            _waveActive = false; _waveTimer = 2f;
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
            GUI.Label(new Rect(16, 12, 400, 28), $"Can: {Mathf.CeilToInt(_hp)}", st);
            GUI.Label(new Rect(16, 38, 400, 28), $"Dalga: {_wave}   Düşman: {_enemies.Count}   Skor: {_score}", st);
            if (!_waveActive && !_dead)
                GUI.Label(new Rect(16, 64, 400, 24), $"Sonraki dalga: {Mathf.CeilToInt(Mathf.Max(0f, _waveTimer))} sn",
                    new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(1, 1, 1, 0.7f) } });

            // Nişangâh
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(1, 1, 1, 0.8f);
            GUI.DrawTexture(new Rect(cx - 6, cy - 1, 12, 2), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1, cy - 6, 2, 12), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_dead)
            {
                var big = new GUIStyle(GUI.skin.label)
                { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.45f, 0.4f);
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 50), "OYUN BİTTİ", big);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.4f + 44, Screen.width, 28),
                    $"Dalga {_wave} · Skor {_score} — yeniden başlamak için R", sub);
            }
        }
    }
}
