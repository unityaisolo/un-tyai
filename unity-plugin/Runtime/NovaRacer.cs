using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovaWorld
{
    /// <summary>
    /// YARIŞ / DRIFT. Kapalı bir pist (prosedürel spline halka) üretir, üzerinde araç sürersin.
    /// Arcade fizik: gaz/fren, direksiyon, yan kayma (drift) ve hız kaybı. Tur süresi ölçülür,
    /// checkpoint'ler sırayla geçilir, en iyi tur kaydedilir. Pistten çıkarsan yavaşlarsın.
    /// Araç modeli atanmazsa kutu araç kullanılır. Runtime-only.
    /// </summary>
    public class NovaRacer : MonoBehaviour
    {
        [Header("Pist")]
        public float trackRadius = 90f;      // ana halka yarıçapı
        public float trackWidth = 14f;
        public int segments = 72;            // pist çözünürlüğü
        public float curviness = 0.35f;      // 0 = daire, 1 = çok kıvrımlı
        public int checkpointCount = 8;

        [Header("Araç")]
        public float accel = 26f;
        public float maxSpeed = 34f;
        public float reverseSpeed = 10f;
        public float brakeForce = 45f;
        public float steerSpeed = 105f;      // derece/sn (hıza göre ölçeklenir)
        public float grip = 5.5f;            // yüksek = az kayma
        public float driftGrip = 1.9f;       // el freniyle (Space) düşer → drift
        public float offTrackDrag = 14f;     // pist dışında yavaşlama

        [Header("Görsel (opsiyonel)")]
        public Material trackMaterial;
        public GameObject carModel;

        Transform _car, _body;
        Camera _cam;
        Vector3 _vel;                        // dünya uzayında hız
        float _heading;                      // araç yönü (derece)
        float _lapT, _bestLap = -1f;
        int _lap, _nextCp;
        bool _drifting;
        readonly List<Vector3> _center = new List<Vector3>();
        readonly List<Transform> _checkpoints = new List<Transform>();
        System.Random _rnd;

        void Start()
        {
            _rnd = new System.Random();
            BuildTrack();

            // ---- Araç ----
            _car = transform;
            var pivot = new GameObject("CarBody");
            _body = pivot.transform;
            _body.SetParent(_car, false);
            if (carModel != null)
            {
                var m = Instantiate(carModel);
                m.SetActive(true);
                m.transform.SetParent(_body, false);
                StripColliders(m);
                FitLongest(m, 4.2f);          // ~4 m araç
            }
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(_body, false);
                box.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                box.transform.localScale = new Vector3(1.8f, 1f, 4f);
                StripColliders(box);
                Paint(box, new Color(0.9f, 0.3f, 0.2f));
                var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cabin.transform.SetParent(_body, false);
                cabin.transform.localPosition = new Vector3(0f, 1.25f, -0.2f);
                cabin.transform.localScale = new Vector3(1.5f, 0.6f, 1.8f);
                StripColliders(cabin);
                Paint(cabin, new Color(0.2f, 0.25f, 0.3f));
            }

            // Başlangıç: ilk segmentin üstünde, pist yönüne dönük
            if (_center.Count > 1)
            {
                _car.position = _center[0] + Vector3.up * 0.2f;
                var fwd = (_center[1] - _center[0]).normalized;
                _heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            }

            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("NovaRacerCam");
                _cam = camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
        }

        void Update()
        {
            // ---- Giriş ----
            float throttle = 0f, steer = 0f; bool handbrake = false, reset = false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                throttle = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                         - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
                steer = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                      - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                handbrake = kb.spaceKey.isPressed;
                reset = kb.rKey.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            throttle = Input.GetAxisRaw("Vertical");
            steer = Input.GetAxisRaw("Horizontal");
            handbrake = Input.GetKey(KeyCode.Space);
            reset = Input.GetKeyDown(KeyCode.R);
#endif
            if (reset) { ResetToTrack(); return; }

            float speed = _vel.magnitude;

            // ---- Direksiyon: hız arttıkça daha az keskin ----
            float steerFactor = Mathf.Clamp01(speed / 8f);
            _heading += steer * steerSpeed * steerFactor * Time.deltaTime;
            var fwd = new Vector3(Mathf.Sin(_heading * Mathf.Deg2Rad), 0f, Mathf.Cos(_heading * Mathf.Deg2Rad));

            // ---- Gaz / fren ----
            if (throttle > 0f) _vel += fwd * accel * throttle * Time.deltaTime;
            else if (throttle < 0f)
            {
                // Geri veya fren (ileri gidiyorsa fren)
                if (Vector3.Dot(_vel, fwd) > 0.5f) _vel = Vector3.MoveTowards(_vel, Vector3.zero, brakeForce * Time.deltaTime);
                else _vel += fwd * accel * throttle * 0.6f * Time.deltaTime;
            }

            // ---- Kayma modeli: hızı ileri + yan bileşene ayır, yan bileşeni sürtünmeyle sil ----
            _drifting = handbrake && speed > 6f;
            float g = _drifting ? driftGrip : grip;
            var vFwd = fwd * Vector3.Dot(_vel, fwd);
            var vSide = _vel - vFwd;
            vSide = Vector3.MoveTowards(vSide, Vector3.zero, g * Time.deltaTime * 6f);
            _vel = vFwd + vSide;

            // Hız sınırı + doğal yavaşlama
            float lim = Mathf.Max(reverseSpeed, maxSpeed);
            if (_vel.magnitude > lim) _vel = _vel.normalized * lim;
            if (Mathf.Approximately(throttle, 0f)) _vel = Vector3.MoveTowards(_vel, Vector3.zero, 6f * Time.deltaTime);

            // ---- Pist dışı cezası ----
            if (DistanceToTrack(_car.position) > trackWidth * 0.5f)
                _vel = Vector3.MoveTowards(_vel, Vector3.zero, offTrackDrag * Time.deltaTime);

            // ---- Konum + zemine otur ----
            var next = _car.position + _vel * Time.deltaTime;
            if (Physics.Raycast(next + Vector3.up * 30f, Vector3.down, out var hit, 100f)) next.y = hit.point.y + 0.15f;
            _car.position = next;
            if (_body != null)
            {
                // Gövde: yöne dön + drift'te hafif yatır
                float lean = Vector3.Dot(vSide, Vector3.Cross(Vector3.up, fwd)) * 0.6f;
                _body.rotation = Quaternion.Slerp(_body.rotation,
                    Quaternion.Euler(0f, _heading, Mathf.Clamp(-lean, -14f, 14f)), 12f * Time.deltaTime);
            }

            // ---- Tur / checkpoint ----
            _lapT += Time.deltaTime;
            if (_checkpoints.Count > 0)
            {
                var cp = _checkpoints[_nextCp];
                if (cp != null && (cp.position - _car.position).sqrMagnitude < (trackWidth * 0.8f) * (trackWidth * 0.8f))
                {
                    _nextCp++;
                    if (_nextCp >= _checkpoints.Count)
                    {
                        _nextCp = 0; _lap++;
                        if (_bestLap < 0f || _lapT < _bestLap) _bestLap = _lapT;
                        _lapT = 0f;
                    }
                }
            }

            // ---- Kamera: aracın arkasından, hıza göre geriye çekilen ----
            if (_cam != null)
            {
                float back = 9f + speed * 0.15f;
                var want = _car.position - fwd * back + Vector3.up * (4.2f + speed * 0.05f);
                _cam.transform.position = Vector3.Lerp(_cam.transform.position, want, 1f - Mathf.Exp(-9f * Time.deltaTime));
                _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation,
                    Quaternion.LookRotation((_car.position + Vector3.up * 1.2f) - _cam.transform.position), 10f * Time.deltaTime);
            }
        }

        // ---- Pist üretimi: kıvrımlı kapalı halka ----
        void BuildTrack()
        {
            var root = new GameObject("Track");
            root.transform.SetParent(transform.parent != null ? transform.parent : null);

            // Yarıçapı açıya göre dalgalandır → oval/kıvrımlı pist (kapalı kalması için tam periyot)
            float p1 = (float)(_rnd.NextDouble() * 6.28), p2 = (float)(_rnd.NextDouble() * 6.28);
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                float r = trackRadius * (1f + curviness * (0.55f * Mathf.Sin(t * 2f + p1) + 0.35f * Mathf.Sin(t * 3f + p2)));
                _center.Add(new Vector3(Mathf.Cos(t) * r, 0f, Mathf.Sin(t) * r));
            }

            // Segment başına yol karosu (quad yerine ince kutu — collider'lı, araç üstünde durur)
            for (int i = 0; i < _center.Count; i++)
            {
                var a = _center[i];
                var b = _center[(i + 1) % _center.Count];
                var mid = (a + b) * 0.5f;
                var dir = (b - a);
                float len = dir.magnitude;
                if (len < 0.01f) continue;

                var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                piece.name = "TrackPiece";
                piece.transform.SetParent(root.transform);
                piece.transform.position = mid - Vector3.up * 0.1f;
                piece.transform.rotation = Quaternion.LookRotation(dir.normalized);
                piece.transform.localScale = new Vector3(trackWidth, 0.2f, len * 1.06f); // hafif bindirme
                if (trackMaterial != null)
                {
                    var m = new Material(trackMaterial);
                    var tex = m.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                    if (m.HasProperty(tex)) m.SetTextureScale(tex, new Vector2(trackWidth * 0.25f, len * 0.25f));
                    piece.GetComponent<Renderer>().sharedMaterial = m;
                }
                else Paint(piece, (i & 1) == 0 ? new Color(0.22f, 0.23f, 0.26f) : new Color(0.19f, 0.20f, 0.23f));
            }

            // Checkpoint'ler (görünmez tetikleyici; sadece konum tutar)
            for (int c = 0; c < checkpointCount; c++)
            {
                int idx = Mathf.RoundToInt(c / (float)checkpointCount * _center.Count) % _center.Count;
                var cp = new GameObject($"CP{c}");
                cp.transform.SetParent(root.transform);
                cp.transform.position = _center[idx] + Vector3.up * 1f;
                _checkpoints.Add(cp.transform);

                // Start/finish çizgisi görünür olsun
                if (c == 0)
                {
                    var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    line.name = "StartLine";
                    line.transform.SetParent(cp.transform, false);
                    var d = (_center[(idx + 1) % _center.Count] - _center[idx]).normalized;
                    line.transform.rotation = Quaternion.LookRotation(d);
                    line.transform.localPosition = Vector3.down * 0.85f;
                    line.transform.localScale = new Vector3(trackWidth, 0.06f, 1.2f);
                    StripColliders(line);
                    Paint(line, Color.white);
                }
            }
        }

        float DistanceToTrack(Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _center.Count; i++)
            {
                var a = _center[i]; var b = _center[(i + 1) % _center.Count];
                var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                float d = Vector3.Distance(new Vector3(p.x, 0f, p.z), a + ab * t);
                if (d < best) best = d;
            }
            return best;
        }

        void ResetToTrack()
        {
            // En yakın segmente ışınlan, pist yönüne dön
            int bi = 0; float best = float.MaxValue;
            for (int i = 0; i < _center.Count; i++)
            {
                float d = (new Vector3(_car.position.x, 0f, _car.position.z) - _center[i]).sqrMagnitude;
                if (d < best) { best = d; bi = i; }
            }
            _car.position = _center[bi] + Vector3.up * 0.3f;
            var fwd = (_center[(bi + 1) % _center.Count] - _center[bi]).normalized;
            _heading = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            _vel = Vector3.zero;
        }

        // ---- Yardımcılar ----
        static void StripColliders(GameObject go)
        { foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c); }

        static void FitLongest(GameObject go, float target)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;
            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float m = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (m > 1e-4f) go.transform.localScale *= target / m;
        }

        static void Paint(GameObject go, Color c)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            r.sharedMaterial = m;
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            st.normal.textColor = Color.white;
            float kmh = _vel.magnitude * 3.6f;
            GUI.Label(new Rect(16, 12, 420, 28), $"{kmh:0} km/s   Tur: {_lap}", st);
            GUI.Label(new Rect(16, 40, 420, 26),
                _bestLap > 0f ? $"Süre: {_lapT:0.0} sn   En iyi: {_bestLap:0.0} sn" : $"Süre: {_lapT:0.0} sn", st);
            GUI.Label(new Rect(16, 68, 500, 22), "WASD sür · Space el freni (drift) · R piste dön",
                new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1, 1, 1, 0.6f) } });
            if (_drifting)
                GUI.Label(new Rect(16, 90, 300, 26), "DRIFT!",
                    new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.8f, 0.2f) } });
        }
    }
}
