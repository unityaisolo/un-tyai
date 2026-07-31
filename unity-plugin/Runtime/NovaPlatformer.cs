using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovaWorld
{
    /// <summary>
    /// 3D PLATFORMER. Boşlukta prosedürel platform dizisi; oyuncu zıplayarak ilerler,
    /// toplanabilirleri alır. Düşerse başa döner (checkpoint = son platform).
    /// Üçüncü şahıs kamera. Platform/toplama modeli atanmazsa primitive kullanılır.
    /// Runtime-only: UnityEditor'a bağımlı DEĞİL.
    /// </summary>
    public class NovaPlatformer : MonoBehaviour
    {
        [Header("Hareket")]
        public float moveSpeed = 7f;
        public float jumpHeight = 2.6f;
        public float gravity = -26f;
        public float airControl = 0.7f;

        [Header("Platformlar")]
        public int visiblePlatforms = 14;
        public float gapMin = 3.5f, gapMax = 6.5f;
        public float sideSpread = 4.5f;      // yanal sapma
        public float heightSpread = 1.8f;    // yükseklik farkı
        public Vector3 platformSize = new Vector3(4f, 0.6f, 4f);
        [Range(0f, 1f)] public float coinChance = 0.6f;
        public bool movingPlatforms = true;

        [Header("Görsel (opsiyonel)")]
        public Material platformMaterial;
        public GameObject coinModel;
        public GameObject playerModel;

        CharacterController _cc;
        Camera _cam;
        Transform _body;
        float _velY, _spawnZ;
        int _coins, _reached;
        bool _dead;
        Vector3 _checkpoint;
        readonly List<Plat> _plats = new List<Plat>();
        System.Random _rnd;

        class Plat
        {
            public GameObject go, coin;
            public bool coinTaken, moves;
            public float phase, amp;
            public Vector3 basePos;
        }

        void Start()
        {
            _rnd = new System.Random();
            _cc = GetComponent<CharacterController>();
            if (_cc == null)
            {
                _cc = gameObject.AddComponent<CharacterController>();
                _cc.height = 1.7f; _cc.radius = 0.35f; _cc.center = new Vector3(0f, 0.85f, 0f);
            }

            // Gövde
            var pivot = new GameObject("BodyPivot");
            _body = pivot.transform;
            _body.SetParent(transform, false);
            if (playerModel != null)
            {
                var m = Instantiate(playerModel);
                m.SetActive(true);
                m.transform.SetParent(_body, false);
                StripColliders(m);
                FitHeight(m, 1.6f);
            }
            else
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                cap.transform.SetParent(_body, false);
                cap.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                cap.transform.localScale = new Vector3(0.7f, 0.85f, 0.7f);
                StripColliders(cap);
                Paint(cap, new Color(0.35f, 0.75f, 1f));
            }

            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("NovaPlatformerCam");
                _cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }

            // Başlangıç platformu + ilk diziler
            _spawnZ = 0f;
            SpawnPlatform(new Vector3(0f, 0f, 0f), first: true);
            for (int i = 0; i < visiblePlatforms; i++) SpawnNext();

            _checkpoint = new Vector3(0f, 1.2f, 0f);
            transform.position = _checkpoint;
        }

        void Update()
        {
            if (_dead) { HandleRestart(); return; }

            // ---- Giriş ----
            float h = 0f, v = 0f; bool jump = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                  - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                v = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                  - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                jump = kb.spaceKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            h = Input.GetAxisRaw("Horizontal");
            v = Input.GetAxisRaw("Vertical");
            jump = Input.GetKeyDown(KeyCode.Space);
#endif
            // ---- Hareket (kamera hep +Z'ye bakar; basit ve öngörülebilir) ----
            var dir = new Vector3(h, 0f, v);
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            bool grounded = _cc.isGrounded;
            float ctrl = grounded ? 1f : airControl;

            if (grounded)
            {
                _velY = -2f;
                if (jump) _velY = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            else _velY += gravity * Time.deltaTime;

            _cc.Move((dir * moveSpeed * ctrl + Vector3.up * _velY) * Time.deltaTime);

            if (dir.sqrMagnitude > 0.01f && _body != null)
                _body.rotation = Quaternion.Slerp(_body.rotation, Quaternion.LookRotation(dir), 12f * Time.deltaTime);

            // ---- Hareketli platformlar ----
            foreach (var p in _plats)
            {
                if (p.go == null || !p.moves) continue;
                var pos = p.basePos;
                pos.x += Mathf.Sin(Time.time * 0.8f + p.phase) * p.amp;
                p.go.transform.position = pos;
                if (p.coin != null) p.coin.transform.position = pos + Vector3.up * 1.4f;
            }

            // ---- Coin toplama ----
            foreach (var p in _plats)
            {
                if (p.coin == null || p.coinTaken) continue;
                p.coin.transform.Rotate(0f, 180f * Time.deltaTime, 0f, Space.World);
                if ((p.coin.transform.position - transform.position - Vector3.up).sqrMagnitude < 2.2f)
                { p.coinTaken = true; p.coin.SetActive(false); _coins++; }
            }

            // ---- İlerleme / geri dönüşüm ----
            if (_plats.Count > 0 && transform.position.z > _plats[0].go.transform.position.z + 12f)
            {
                Recycle();
                SpawnNext();
                _reached++;
                _checkpoint = _plats[0].go.transform.position + Vector3.up * 1.5f;
            }

            // ---- Düşme ----
            if (transform.position.y < -12f) Die();

            // ---- Kamera (üçüncü şahıs, sabit yön) ----
            if (_cam != null)
            {
                var want = transform.position + new Vector3(0f, 6f, -9f);
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, want, 1f - Mathf.Exp(-8f * Time.deltaTime));
                _cam.transform.rotation = Quaternion.Euler(24f, 0f, 0f);
            }
        }

        void SpawnNext()
        {
            float gap = Mathf.Lerp(gapMin, gapMax, (float)_rnd.NextDouble());
            float side = ((float)_rnd.NextDouble() * 2f - 1f) * sideSpread;
            float dy = ((float)_rnd.NextDouble() * 2f - 1f) * heightSpread;
            var last = _plats.Count > 0 ? _plats[_plats.Count - 1].go.transform.position : Vector3.zero;
            _spawnZ = last.z + gap + platformSize.z;
            SpawnPlatform(new Vector3(Mathf.Clamp(last.x + side, -12f, 12f),
                                      Mathf.Clamp(last.y + dy, -3f, 9f), _spawnZ));
        }

        void SpawnPlatform(Vector3 pos, bool first = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Platform";
            go.transform.position = pos;
            go.transform.localScale = first ? new Vector3(6f, platformSize.y, 6f) : platformSize;
            if (platformMaterial != null) go.GetComponent<Renderer>().sharedMaterial = platformMaterial;
            else Paint(go, first ? new Color(0.35f, 0.7f, 0.45f)
                                 : ((_plats.Count & 1) == 0 ? new Color(0.42f, 0.45f, 0.52f) : new Color(0.36f, 0.39f, 0.46f)));

            var p = new Plat { go = go, basePos = pos };
            // Bazı platformlar yanal salınsın (zorluk + canlılık)
            if (movingPlatforms && !first && _rnd.NextDouble() < 0.28)
            { p.moves = true; p.phase = (float)(_rnd.NextDouble() * 6.28); p.amp = 1.5f + 1.5f * (float)_rnd.NextDouble(); }

            // Coin
            if (!first && _rnd.NextDouble() < coinChance)
            {
                GameObject c;
                if (coinModel != null) { c = Instantiate(coinModel); c.SetActive(true); FitHeight(c, 0.7f); }
                else
                {
                    c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    c.transform.localScale = new Vector3(0.45f, 0.06f, 0.45f);
                    c.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    Paint(c, new Color(1f, 0.85f, 0.25f), emissive: true);
                }
                StripColliders(c);
                c.transform.position = pos + Vector3.up * 1.4f;
                p.coin = c;
            }
            _plats.Add(p);
        }

        void Recycle()
        {
            var p = _plats[0];
            if (p.go != null) Destroy(p.go);
            if (p.coin != null) Destroy(p.coin);
            _plats.RemoveAt(0);
        }

        void Die()
        {
            _dead = true;
            Debug.Log($"[Nova Platformer] Düştün! Platform: {_reached} · Coin: {_coins}");
        }

        void HandleRestart()
        {
            bool r = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null) r = kb.rKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            r = Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Space);
#endif
            if (!r) return;
            _dead = false; _velY = 0f;
            _cc.enabled = false;
            transform.position = _checkpoint;   // son ulaşılan platformdan devam
            _cc.enabled = true;
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

        static void Paint(GameObject go, Color c, bool emissive = false)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (emissive && m.HasProperty("_EmissionColor"))
            { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.9f); }
            r.sharedMaterial = m;
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            GUI.Label(new Rect(16, 12, 400, 28), $"Platform: {_reached}   Coin: {_coins}", st);
            GUI.Label(new Rect(16, 40, 460, 22), "WASD hareket · Space zıpla · R yeniden",
                new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1, 1, 1, 0.6f) } });
            if (_dead)
            {
                var big = new GUIStyle(GUI.skin.label)
                { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                big.normal.textColor = new Color(1f, 0.55f, 0.4f);
                GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width, 46), "DÜŞTÜN!", big);
                var sub = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
                sub.normal.textColor = Color.white;
                GUI.Label(new Rect(0, Screen.height * 0.4f + 42, Screen.width, 26),
                    "R veya Space ile son platformdan devam et", sub);
            }
        }
    }
}
