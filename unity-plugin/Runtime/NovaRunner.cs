using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovaWorld
{
    /// <summary>
    /// 3D SONSUZ KOŞU kontrolcüsü (Subway Surfers tarzı). Oyuncu otomatik ileri koşar;
    /// A/D veya ok tuşlarıyla şerit değiştirir, Space ile ZIPLAR (alçak engelleri aşar),
    /// S/Aşağı ile KAYAR (üstten engellerin altından geçer). Zemin/engel/coin prosedürel
    /// üretilir, arkada geri dönüştürülür. Engele çarpınca oyun biter, coin toplayınca skor.
    /// Gerçek doku/model (groundMaterial, playerModel, coin, obstacles) atanabilir; boşsa
    /// primitive'lerle çalışır. Runtime-only: UnityEditor'a bağımlı DEĞİL.
    /// </summary>
    public class NovaRunner : MonoBehaviour
    {
        [Header("Koşu")]
        public float forwardSpeed = 10f;
        public float speedGain = 0.16f;
        public float maxSpeed = 28f;
        public float laneWidth = 2.4f;
        public int laneCount = 3;
        public float laneChangeSpeed = 12f;
        public float jumpHeight = 1.9f;
        public float gravity = -34f;
        public float slideTime = 0.65f;

        [Header("Track")]
        public float tileLength = 12f;
        public int visibleTiles = 14;
        public float groundWidth = 8.5f;
        [Range(0f, 1f)] public float obstacleChance = 0.6f;
        [Range(0f, 1f)] public float coinChance = 0.55f;

        [Header("Görsel (opsiyonel — boşsa primitive)")]
        public Material groundMaterial;   // zemin dokusu (editör textures-raw'dan yükler)
        public Material railMaterial;     // yan ray dokusu
        public GameObject playerModel;    // oyuncu gövdesi (katalog karakter)
        public GameObject coin;           // coin modeli
        public GameObject[] obstacles;    // engel modelleri

        [Header("Atmosfer")]
        public bool fog = true;
        public Color fogColor = new Color(0.62f, 0.72f, 0.82f);

        Transform _player, _body;
        Camera _cam;
        int _lane;
        float _velY;
        const float BaseY = 1f;
        bool _dead, _sliding;
        float _slideT, _lean;
        int _coins;
        float _distance, _spawnZ;
        readonly Queue<Segment> _segments = new Queue<Segment>();
        System.Random _rnd;

        enum Kind { Jump, Slide, Full }   // Jump=alçak(zıpla) · Slide=üstten(kay) · Full=tam blok(şerit değiştir)
        class Segment { public GameObject tile; public readonly List<Item> items = new List<Item>(); }
        class Item { public GameObject go; public int lane; public bool isCoin; public Kind kind; public bool taken; }

        void Start()
        {
            _rnd = new System.Random();
            _lane = laneCount / 2;
            _player = transform;

            // ---- Oyuncu gövdesi: BodyPivot (crouch/lean burada) > model (FitHeight burada) ----
            var pivot = new GameObject("BodyPivot");
            _body = pivot.transform;
            _body.SetParent(_player, false);
            _body.localPosition = Vector3.zero;

            if (playerModel != null)
            {
                var m = Instantiate(playerModel);
                m.SetActive(true);
                m.transform.SetParent(_body, false);
                m.transform.localPosition = Vector3.zero;
                StripColliders(m);
                FitHeight(m, 1.7f);
            }
            else
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.name = "Body";
                cap.transform.SetParent(_body, false);
                cap.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                cap.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                StripColliders(cap);
                Paint(cap, new Color(0.2f, 0.6f, 1f));
            }

            var pp = _player.position; pp.y = BaseY; pp.x = LaneX(_lane); _player.position = pp;

            // ---- Kamera ----
            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("NovaRunnerCam");
                _cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }

            // ---- Atmosfer ----
            if (fog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogStartDistance = tileLength * 4f;
                RenderSettings.fogEndDistance = tileLength * visibleTiles * 0.95f;
            }

            _spawnZ = -tileLength;
            for (int i = 0; i < visibleTiles; i++) SpawnSegment();
        }

        void Update()
        {
            if (_dead) { AnimateCoins(); HandleRestart(); return; }

            // ---- Giriş ----
            int move = 0; bool jump = false, slide = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) move = -1;
                if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) move = 1;
                jump = kb.spaceKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame;
                slide = kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) move = -1;
            if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) move = 1;
            jump = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);
            slide = Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.LeftControl);
#endif
            if (move != 0) { _lane = Mathf.Clamp(_lane + move, 0, laneCount - 1); _lean = move; }

            // ---- İleri hareket + hızlanma ----
            forwardSpeed = Mathf.Min(maxSpeed, forwardSpeed + speedGain * Time.deltaTime);
            var pos = _player.position;
            pos.z += forwardSpeed * Time.deltaTime;
            _distance += forwardSpeed * Time.deltaTime;

            // ---- Şerit geçişi ----
            pos.x = Mathf.MoveTowards(pos.x, LaneX(_lane), laneChangeSpeed * Time.deltaTime);

            // ---- Zıplama / yerçekimi ----
            bool grounded = pos.y <= BaseY + 0.001f && _velY <= 0f;
            if (grounded) { pos.y = BaseY; _velY = 0f; if (jump && !_sliding) _velY = Mathf.Sqrt(jumpHeight * -2f * gravity); }
            else _velY += gravity * Time.deltaTime;
            pos.y += _velY * Time.deltaTime;
            _player.position = pos;

            // ---- Kayma (slide) ----
            if (slide && grounded && !_sliding) { _sliding = true; _slideT = slideTime; }
            if (_sliding) { _slideT -= Time.deltaTime; if (_slideT <= 0f) _sliding = false; }

            // ---- Gövde animasyonu: eğilme (şerit) + kayarken alçalma ----
            if (_body != null)
            {
                _lean = Mathf.MoveTowards(_lean, 0f, Time.deltaTime * 3f);
                float crouch = _sliding ? 0.5f : 1f;
                _body.localScale = Vector3.Lerp(_body.localScale, new Vector3(1f, crouch, 1f), 12f * Time.deltaTime);
                _body.localRotation = Quaternion.Slerp(_body.localRotation,
                    Quaternion.Euler(_sliding ? 55f : 0f, 0f, _lean * -14f), 12f * Time.deltaTime);
            }

            // ---- Kamera takip ----
            if (_cam != null)
            {
                Vector3 want = pos + new Vector3(0f, 5.5f, -8f);
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, want, 1f - Mathf.Exp(-10f * Time.deltaTime));
                _cam.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
            }

            // ---- Track geri dönüşümü ----
            if (_segments.Count > 0 && pos.z - _segments.Peek().tile.transform.position.z > tileLength * 1.5f)
            { RecycleFront(); SpawnSegment(); }

            AnimateCoins();
            CheckItems(pos);
        }

        void AnimateCoins()
        {
            foreach (var seg in _segments)
                foreach (var it in seg.items)
                    if (it.isCoin && it.go != null && !it.taken)
                        it.go.transform.Rotate(0f, 180f * Time.deltaTime, 0f, Space.World);
        }

        void CheckItems(Vector3 pos)
        {
            foreach (var seg in _segments)
                foreach (var it in seg.items)
                {
                    if (it.go == null || it.taken) continue;
                    var ip = it.go.transform.position;
                    if (Mathf.Abs(ip.z - pos.z) > 0.9f) continue;
                    if (Mathf.Abs(ip.x - pos.x) > laneWidth * 0.5f) continue;
                    if (it.isCoin) { it.taken = true; it.go.SetActive(false); _coins++; continue; }

                    // Engel türüne göre kaçınma: Jump→zıpla, Slide→kay, Full→sadece şerit
                    bool avoided =
                        (it.kind == Kind.Jump && pos.y > BaseY + 0.9f) ||
                        (it.kind == Kind.Slide && _sliding);
                    if (!avoided) { Die(); return; }
                }
        }

        void SpawnSegment()
        {
            _spawnZ += tileLength;
            var seg = new Segment();

            // ---- Zemin ----
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.transform.position = new Vector3(0f, -0.25f, _spawnZ + tileLength * 0.5f);
            tile.transform.localScale = new Vector3(groundWidth, 0.5f, tileLength);
            ApplyTile(tile, groundMaterial, new Color(0.30f, 0.32f, 0.36f), groundWidth, tileLength);

            // ---- Yan raylar (track hissi) ----
            for (int s = -1; s <= 1; s += 2)
            {
                var rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.transform.SetParent(tile.transform, false);
                rail.transform.localPosition = new Vector3(s * (groundWidth * 0.5f + 0.15f) / groundWidth, 1.2f / 0.5f, 0f);
                rail.transform.localScale = new Vector3(0.35f / groundWidth, 1.8f / 0.5f, 1f);
                StripColliders(rail);
                ApplyTile(rail, railMaterial, new Color(0.18f, 0.20f, 0.24f), 1f, tileLength);
            }
            StripColliders(tile);
            seg.tile = tile;

            // ---- Engel + coin ----
            bool allow = _spawnZ > _player.position.z + tileLength * 3f;
            if (allow)
            {
                int openLane = _rnd.Next(laneCount); // en az bir şerit hep açık
                for (int lane = 0; lane < laneCount; lane++)
                {
                    if (lane == openLane) continue;
                    float z = _spawnZ + tileLength * (0.35f + 0.3f * (float)_rnd.NextDouble());
                    if (_rnd.NextDouble() < obstacleChance) seg.items.Add(MakeObstacle(lane, z));
                    else if (_rnd.NextDouble() < coinChance) seg.items.Add(MakeCoin(lane, z));
                }
                // Açık şeride coin dizisi (toplama hazzı)
                if (_rnd.NextDouble() < coinChance)
                {
                    for (int c = 0; c < 3; c++)
                        seg.items.Add(MakeCoin(openLane, _spawnZ + tileLength * (0.25f + 0.22f * c)));
                }
            }
            _segments.Enqueue(seg);
        }

        Item MakeObstacle(int lane, float z)
        {
            // 3 tür: Jump (alçak, zıpla) · Slide (üstten kiriş, kay) · Full (tam blok, şerit değiştir)
            double r = _rnd.NextDouble();
            Kind kind = r < 0.42 ? Kind.Jump : r < 0.75 ? Kind.Slide : Kind.Full;

            GameObject go;
            bool useModel = obstacles != null && obstacles.Length > 0 && kind != Kind.Slide;
            if (useModel && obstacles[_rnd.Next(obstacles.Length)] is GameObject tmpl && tmpl != null)
            {
                go = Instantiate(tmpl); go.SetActive(true);
                FitHeight(go, kind == Kind.Jump ? 0.9f : 2.4f);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (kind == Kind.Slide)
                {
                    go.transform.localScale = new Vector3(laneWidth * 0.9f, 0.5f, 0.6f); // üstten kiriş
                    Paint(go, new Color(0.85f, 0.55f, 0.2f));
                }
                else
                {
                    go.transform.localScale = new Vector3(laneWidth * 0.7f, kind == Kind.Full ? 2.6f : 0.9f, 0.8f);
                    Paint(go, kind == Kind.Full ? new Color(0.8f, 0.25f, 0.25f) : new Color(0.85f, 0.45f, 0.2f));
                }
            }
            StripColliders(go);
            float y = kind == Kind.Slide ? 2.1f : kind == Kind.Full ? 1.3f : 0.5f; // slide = havada kiriş
            go.transform.position = new Vector3(LaneX(lane), y, z);
            return new Item { go = go, lane = lane, isCoin = false, kind = kind };
        }

        Item MakeCoin(int lane, float z)
        {
            GameObject go;
            if (coin != null)
            {
                go = Instantiate(coin); go.SetActive(true);
                FitHeight(go, 0.7f);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.transform.localScale = new Vector3(0.5f, 0.06f, 0.5f);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                Paint(go, new Color(1f, 0.85f, 0.2f), emissive: true);
            }
            StripColliders(go);
            go.transform.position = new Vector3(LaneX(lane), 1.1f, z);
            return new Item { go = go, lane = lane, isCoin = true };
        }

        void RecycleFront()
        {
            var seg = _segments.Dequeue();
            if (seg.tile != null) Destroy(seg.tile);
            foreach (var it in seg.items) if (it.go != null) Destroy(it.go);
        }

        void Die()
        {
            _dead = true;
            Debug.Log($"[Nova Koşu] Oyun bitti! Mesafe: {Mathf.RoundToInt(_distance)} m · Coin: {_coins} · Skor: {Score()}");
        }

        void HandleRestart()
        {
            bool restart = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null) restart = kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            restart = Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space);
#endif
            if (!restart) return;
            while (_segments.Count > 0) RecycleFront();
            _dead = false; _sliding = false; _velY = 0f; _coins = 0; _distance = 0f;
            forwardSpeed = Mathf.Max(10f, forwardSpeed * 0.6f);
            _lane = laneCount / 2;
            _player.position = new Vector3(LaneX(_lane), BaseY, 0f);
            _spawnZ = -tileLength;
            for (int i = 0; i < visibleTiles; i++) SpawnSegment();
        }

        int Score() => Mathf.RoundToInt(_distance) + _coins * 10;
        float LaneX(int lane) => (lane - (laneCount - 1) * 0.5f) * laneWidth;

        // ---- Yardımcılar ----
        void ApplyTile(GameObject go, Material shared, Color fallback, float wx, float wz)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            if (shared != null)
            {
                var m = new Material(shared);
                // Kübün UV'si 0-1; dokuyu metre başına ~0.5 tekrarla döşe (esneme olmasın)
                var tex = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                if (m.HasProperty(tex)) m.SetTextureScale(tex, new Vector2(wx * 0.5f, wz * 0.5f));
                r.sharedMaterial = m;
            }
            else Paint(go, fallback);
        }

        static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
        }

        static void FitHeight(GameObject go, float targetH)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float h = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (h > 1e-4f) go.transform.localScale *= targetH / h;
        }

        static void Paint(GameObject go, Color c, bool emissive = false)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (emissive && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.9f); }
            r.sharedMaterial = m;
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            GUI.Label(new Rect(16, 12, 400, 30), $"Mesafe: {Mathf.RoundToInt(_distance)} m", st);
            GUI.Label(new Rect(16, 42, 400, 30), $"Coin: {_coins}   Skor: {Score()}", st);
            GUI.Label(new Rect(16, 72, 400, 24), "A/D şerit · Space zıpla · S kay", new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1,1,1,0.6f) } });
            if (_dead)
            {
                var big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.5f, 0.4f);
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 50), "OYUN BİTTİ", big);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.4f + 46, Screen.width, 30), "Yeniden başlamak için R veya Space", sub);
            }
        }
    }
}
