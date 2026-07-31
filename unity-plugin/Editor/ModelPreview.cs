using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// Editör içi 3D önizleyici. Üretilen modeli izole bir önizleme sahnesinde render eder.
    /// Kullanıcı sürükleyerek döndürür, tekerlekle yakınlaşır. Model önizlemede kaldığı
    /// sürece kullanıcının Hierarchy'sinde GÖRÜNMEZ; sadece 'Sahneye ekle' denince klonlanır.
    /// </summary>
    public class ModelPreview : IDisposable
    {
        private PreviewRenderUtility _prev;
        private GameObject _model;         // önizleme sahnesindeki gizli kök
        private GameObject _floor;         // altıgen desenli "sonsuz oda" zemini
        private Texture2D _floorTex;
        private Material _floorMat;
        private GameObject _backdrop;      // degrade arka plan küresi (stüdyo cyc)
        private Texture2D _bgTex;
        private Material _bgMat;
        private string _glbUrl;
        private string _name;

        // Tema ile uyumlu sahne renkleri (koyu lacivert + cyan)
        private static readonly Color RoomBg = new Color(0.016f, 0.027f, 0.051f, 1f);   // #04070D
        private static readonly Color HexLine = new Color(0.133f, 0.827f, 0.933f, 1f);  // #22D3EE

        private float _yaw = 25f;
        private float _pitch = -12f;
        private float _distance = 3f;
        private Bounds _bounds = new Bounds(Vector3.zero, Vector3.one);

        public bool HasModel => _model != null;
        public string ModelName => _name;
        public string GlbUrl => _glbUrl;

        private void EnsureUtility()
        {
            if (_prev != null) return;
            _prev = new PreviewRenderUtility();
            _prev.camera.fieldOfView = 30f;
            _prev.camera.nearClipPlane = 0.01f;
            _prev.camera.farClipPlane = 1000f;
            _prev.camera.clearFlags = CameraClearFlags.SolidColor;
            _prev.camera.backgroundColor = RoomBg; // arka plan zeminle kaynaşır -> sonsuz oda hissi
            _prev.lights[0].intensity = 1.25f;
            _prev.lights[0].color = new Color(1f, 0.98f, 0.95f);
            _prev.lights[0].transform.rotation = Quaternion.Euler(40f, 40f, 0f);
            if (_prev.lights.Length > 1)
            {
                // Cyan rim ışık: modelin kenarlarına teknolojik parıltı verir
                _prev.lights[1].intensity = 0.85f;
                _prev.lights[1].color = new Color(0.45f, 0.85f, 1f);
                _prev.lights[1].transform.rotation = Quaternion.Euler(-25f, -145f, 0f);
            }
            _prev.ambientColor = new Color(0.16f, 0.2f, 0.26f, 1f);
            CreateBackdrop();
            CreateHexFloor();
        }

        // Dev bir kürenin içi: alt/üst koyu, ufukta hafif cyan ışıma -> premium stüdyo hissi.
        private void CreateBackdrop()
        {
            if (_backdrop != null) return;
            _bgTex = GenerateBackdropTexture(256);
            var shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _bgMat = new Material(shader) { mainTexture = _bgTex, hideFlags = HideFlags.HideAndDontSave };

            _backdrop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var bc = _backdrop.GetComponent<Collider>();
            if (bc != null) UnityEngine.Object.DestroyImmediate(bc);
            _backdrop.name = "NovaBackdrop";
            // Negatif X ölçeği yüzeyleri tersine çevirir: kürenin İÇİ görünür olur.
            _backdrop.transform.localScale = new Vector3(-300f, 300f, 300f);
            _backdrop.GetComponent<MeshRenderer>().sharedMaterial = _bgMat;
            SetHideFlagsRecursive(_backdrop, HideFlags.HideAndDontSave);
            _prev.AddSingleGO(_backdrop);
        }

        // Dikey degrade: zemin çok koyu -> ufukta cyan'lı lacivert ışıma -> üstte koyu.
        private static Texture2D GenerateBackdropTexture(int height)
        {
            var tex = new Texture2D(4, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            Color deep = new Color(0.008f, 0.014f, 0.028f, 1f);   // taban / tepe
            Color horizon = new Color(0.045f, 0.12f, 0.19f, 1f);  // ufuk (cyan'lı)
            Color upper = new Color(0.014f, 0.028f, 0.055f, 1f);  // üst yarı

            var px = new Color[4 * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height; // 0 alt kutup, 1 üst kutup
                Color c;
                if (v < 0.42f)
                    c = Color.Lerp(deep, horizon, Mathf.Pow(v / 0.42f, 2.4f));
                else if (v < 0.55f)
                    c = Color.Lerp(horizon, upper, Mathf.SmoothStep(0f, 1f, (v - 0.42f) / 0.13f));
                else
                    c = Color.Lerp(upper, deep, Mathf.SmoothStep(0f, 1f, (v - 0.55f) / 0.45f));
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        // Altıgen ızgara desenli, kenarlara doğru karararak kaybolan zemin.
        private void CreateHexFloor()
        {
            if (_floor != null) return;
            _floorTex = GenerateHexTexture(512, 13f);
            var shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");
            _floorMat = new Material(shader) { mainTexture = _floorTex, hideFlags = HideFlags.HideAndDontSave };

            _floor = GameObject.CreatePrimitive(PrimitiveType.Plane); // 10x10, +Y'ye bakar
            var col = _floor.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            _floor.name = "NovaHexFloor";
            _floor.transform.localScale = new Vector3(6f, 1f, 6f); // 60x60 birim
            _floor.GetComponent<MeshRenderer>().sharedMaterial = _floorMat;
            SetHideFlagsRecursive(_floor, HideFlags.HideAndDontSave);
            _prev.AddSingleGO(_floor);
        }

        // Kenarlarda şeffaflaşan, döşenebilir altıgen çizgi deseni üretir.
        private static Texture2D GenerateHexTexture(int size, float cells)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var px = new Color[size * size];
            Vector2 r = new Vector2(1f, 1.7320508f);
            Vector2 h = r * 0.5f;
            Color fill = new Color(RoomBg.r * 1.6f, RoomBg.g * 1.7f, RoomBg.b * 1.8f, 1f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    Vector2 p = new Vector2(u, v) * cells;

                    // En yakın altıgen hücre merkezine göre konum (offset'li çift ızgara)
                    Vector2 a = new Vector2(Mathf.Repeat(p.x, r.x), Mathf.Repeat(p.y, r.y)) - h;
                    Vector2 b = new Vector2(Mathf.Repeat(p.x - h.x, r.x), Mathf.Repeat(p.y - h.y, r.y)) - h;
                    Vector2 gv = a.sqrMagnitude < b.sqrMagnitude ? a : b;

                    // Altıgen kenara uzaklık
                    Vector2 q = new Vector2(Mathf.Abs(gv.x), Mathf.Abs(gv.y));
                    float hexD = Mathf.Max(Vector2.Dot(q, new Vector2(0.5f, 0.8660254f)), q.x);
                    float edge = 0.5f - hexD;

                    float line = Mathf.Clamp01(1f - edge / 0.045f);          // kenar çizgisi
                    line = line * line;

                    // Merkezden kenara radyal kaybolma -> "sonsuz oda"
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float fade = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Sqrt(dx * dx + dy * dy) * 2f, 2.2f));

                    Color c = Color.Lerp(fill, HexLine, line * 0.85f);
                    c.a = fade * (0.16f + 0.6f * line);
                    px[y * size + x] = c;
                }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public void SetModel(GameObject go, string name, string glbUrl)
        {
            EnsureUtility();
            ClearModel();
            _model = go;
            _name = string.IsNullOrEmpty(name) ? "GeneratedModel" : name;
            _glbUrl = glbUrl;
            SetHideFlagsRecursive(_model, HideFlags.HideAndDontSave);
            _prev.AddSingleGO(_model);           // izole önizleme sahnesine taşı
            _bounds = ComputeBounds(_model);
            // Zemini modelin tabanına hizala
            if (_floor != null)
                _floor.transform.position = new Vector3(_bounds.center.x, _bounds.min.y - 0.002f, _bounds.center.z);
            FrameModel();
        }

        private void FrameModel()
        {
            float radius = Mathf.Max(0.05f, _bounds.extents.magnitude);
            _distance = radius / Mathf.Sin(Mathf.Deg2Rad * _prev.camera.fieldOfView * 0.5f) * 1.3f;
            _yaw = 25f;
            _pitch = -12f;
        }

        public void ClearModel()
        {
            if (_model != null) { UnityEngine.Object.DestroyImmediate(_model); _model = null; }
            _glbUrl = null;
            _name = null;
        }

        // IMGUIContainer.onGUIHandler içinden çağrılır.
        public void OnGUI(Rect rect)
        {
            if (rect.width < 4 || rect.height < 4) return;
            HandleInput(rect);   // fare/klavye olayları her event tipinde işlenmeli
            EnsureUtility();     // model olmasa da "sonsuz oda" render edilir

            // ⚠ KRİTİK: PreviewRenderUtility YALNIZCA Repaint olayında render edilebilir.
            // Layout/MouseMove/ScrollWheel gibi olaylarda BeginPreview çağırmak, editörün
            // aktif render hedefini bizim geçici RenderTexture'ımıza kilitler; sonuç:
            // Unity'nin geri kalanı SİYAH kalır ve arayüz donar. (Gözlenen çökme buydu.)
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            UpdateCamera();
            Texture tex = null;
            _prev.BeginPreview(rect, GUIStyle.none);
            try { _prev.Render(true); }
            finally { tex = _prev.EndPreview(); }   // hata olsa bile durumu geri ver
            if (tex != null) GUI.DrawTexture(rect, tex, ScaleMode.StretchToFill, false);

            if (_model == null)
            {
                var line = new Rect(rect.x, rect.center.y - 10f, rect.width, 20f);
                EditorGUI.LabelField(line, "Aşağıdan üret — model burada belirir, çevirip incele",
                    EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void UpdateCamera()
        {
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var dir = rot * Vector3.forward;
            _prev.camera.transform.position = _bounds.center - dir * _distance;
            _prev.camera.transform.rotation = rot;
            _prev.camera.nearClipPlane = Mathf.Max(0.001f, _distance * 0.05f);
            _prev.camera.farClipPlane = Mathf.Max(650f, _distance * 20f); // arka plan küresi (r=300) hep görünür
        }

        // Etkileşim oldu mu? true dönerse pencere MarkDirtyRepaint çağırmalı.
        private bool _dirty;
        public bool ConsumeDirty() { bool d = _dirty; _dirty = false; return d; }

        private void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (e == null) return;
            if (e.type == EventType.MouseDrag && e.button == 0 && rect.Contains(e.mousePosition))
            {
                _yaw += e.delta.x * 0.5f;
                _pitch = Mathf.Clamp(_pitch + e.delta.y * 0.5f, -89f, 89f);
                _dirty = true;
                e.Use();
            }
            else if (e.type == EventType.ScrollWheel && rect.Contains(e.mousePosition))
            {
                _distance = Mathf.Clamp(_distance * (1f + e.delta.y * 0.05f), 0.05f, 1000f);
                _dirty = true;
                e.Use();
            }
        }

        // Beğenilen modeli gerçek sahneye klonlar (önizleme kalır).
        public GameObject InstantiateIntoScene(Vector3 pos)
        {
            if (_model == null) return null;
            var clone = UnityEngine.Object.Instantiate(_model);
            clone.name = _name;
            SetHideFlagsRecursive(clone, HideFlags.None);
            var active = SceneManager.GetActiveScene();
            if (active.IsValid()) SceneManager.MoveGameObjectToScene(clone, active);
            clone.transform.position = pos;
            Undo.RegisterCreatedObjectUndo(clone, "UnityAI: 3D modeli sahneye ekle");
            Selection.activeGameObject = clone;
            return clone;
        }

        private static void SetHideFlagsRecursive(GameObject go, HideFlags flags)
        {
            go.hideFlags = flags;
            foreach (Transform t in go.transform) SetHideFlagsRecursive(t.gameObject, flags);
        }

        private static Bounds ComputeBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        // Modelin şu anki dünya-yüksekliği (m). Boyut eşleştirme için.
        public float CurrentHeight()
        {
            return _bounds.size.y;
        }

        // Modeli hedef yüksekliğe (m) ölçekler — üretilen boyu korumak için.
        public void ScaleToHeight(float meters)
        {
            if (_model == null || meters <= 0f) return;
            float cur = _bounds.size.y;
            if (cur <= 0.0001f) return;
            ScaleBy(meters / cur);
        }

        // ---- Anında (ücretsiz) düzenlemeler ----
        public void ScaleBy(float factor)
        {
            if (_model == null || factor <= 0f) return;
            _model.transform.localScale *= factor;
            _bounds = ComputeBounds(_model);
            FrameModel();
        }

        // Tüm materyallerin ana rengini boyar (URP _BaseColor / Standard _Color).
        public void SetColor(Color c)
        {
            if (_model == null) return;
            foreach (var r in _model.GetComponentsInChildren<Renderer>())
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                }
            }
        }

        // Önizlemedeki modelin istatistiklerini çıkarır (sağ panel için).
        public struct Stats
        {
            public int Vertices;
            public int Triangles;
            public int Materials;
            public int Textures;
            public Vector3 Size;
        }

        public Stats ComputeStats()
        {
            var st = new Stats { Size = _bounds.size };
            if (_model == null) return st;

            var mfs = _model.GetComponentsInChildren<MeshFilter>();
            for (int k = 0; k < mfs.Length; k++)
            {
                var mesh = mfs[k].sharedMesh;
                if (mesh == null) continue;
                st.Vertices += mesh.vertexCount;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    st.Triangles += (int)(mesh.GetIndexCount(sm) / 3);
            }

            var rends = _model.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < rends.Length; i++)
            {
                var mm = rends[i].sharedMaterials;
                for (int j = 0; j < mm.Length; j++)
                {
                    var m = mm[j];
                    if (m == null) continue;
                    st.Materials++;
                    var names = m.GetTexturePropertyNames();
                    for (int t = 0; t < names.Length; t++)
                        if (m.GetTexture(names[t]) != null) st.Textures++;
                }
            }
            return st;
        }

        public void Dispose()
        {
            ClearModel();
            if (_floor != null) { UnityEngine.Object.DestroyImmediate(_floor); _floor = null; }
            if (_floorMat != null) { UnityEngine.Object.DestroyImmediate(_floorMat); _floorMat = null; }
            if (_floorTex != null) { UnityEngine.Object.DestroyImmediate(_floorTex); _floorTex = null; }
            if (_backdrop != null) { UnityEngine.Object.DestroyImmediate(_backdrop); _backdrop = null; }
            if (_bgMat != null) { UnityEngine.Object.DestroyImmediate(_bgMat); _bgMat = null; }
            if (_bgTex != null) { UnityEngine.Object.DestroyImmediate(_bgTex); _bgTex = null; }
            if (_prev != null) { _prev.Cleanup(); _prev = null; }
        }
    }
}
