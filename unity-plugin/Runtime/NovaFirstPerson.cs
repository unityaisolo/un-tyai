using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace NovaWorld
{
    /// <summary>
    /// Bağımsız birinci-şahıs kontrolcü — üretilen dünyada gezmek için.
    /// WASD: hareket · Fare: bak · Space: zıpla · Shift: koş · Esc: imleci bırak.
    /// Hem eski (Input Manager) hem yeni (Input System) girişini destekler; ayar gerektirmez.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class NovaFirstPerson : MonoBehaviour
    {
        [Header("Hareket")]
        public float walkSpeed = 6f;
        public float sprintSpeed = 11f;
        public float jumpHeight = 1.4f;
        public float gravity = -20f;

        [Header("Bakış")]
        public float mouseSensitivity = 2.5f;
        public float lookXLimit = 85f;

        [Header("HUD")]
        [Tooltip("Play'e basınca ekranda kontrol ipuçlarını göster (birkaç saniye sonra soluklaşır).")]
        public bool showControlsHud = true;
        [Tooltip("HUD'da gösterilecek başlık — kurucu tarafından ayarlanır (ör. 'Ova · 400 m').")]
        public string worldLabel = "";

        CharacterController _cc;
        Camera _cam;
        float _pitch;
        float _velY;
        bool _locked;
        bool _warned;
        Vector3 _spawn;
        float _hudT;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cam = GetComponentInChildren<Camera>();
        }

        void Start() { _spawn = transform.position; LockCursor(true); _hudT = 0f; }
        void OnDisable() { LockCursor(false); }

        /// <summary>
        /// Play modunda kontrol ipuçları. Kullanıcı Play'e bastığında "boş sahne" hissi
        /// yaşamasın; ne yapacağını ekranda görsün. İlk 8 saniye net, sonra soluklaşır.
        /// </summary>
        void OnGUI()
        {
            if (!showControlsHud) return;
            _hudT += Time.unscaledDeltaTime;
            float a = _hudT < 8f ? 1f : Mathf.Max(0.28f, 1f - (_hudT - 8f) / 3f);

            var title = new GUIStyle(GUI.skin.label)
            { fontSize = 18, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 1f, 1f, a) } };
            var line = new GUIStyle(GUI.skin.label)
            { fontSize = 13, normal = { textColor = new Color(1f, 1f, 1f, a * 0.85f) } };

            GUI.Label(new Rect(16, 12, 560, 26),
                string.IsNullOrEmpty(worldLabel) ? "Nova dünyası" : "Nova · " + worldLabel, title);
            GUI.Label(new Rect(16, 36, 560, 20), "WASD gez · Fare bak · Shift koş · Space zıpla · Esc imleci bırak", line);
            if (_hudT < 8f)
                GUI.Label(new Rect(16, 56, 560, 20), "(Haritadan düşersen otomatik spawn'a dönersin)", line);

            // Nişan noktası — kameranın nereye baktığı belli olsun
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(cx - 2, cy - 2, 4, 4), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        void LockCursor(bool on)
        {
            _locked = on;
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        void Update()
        {
            float h = 0f, v = 0f, lookX = 0f, lookY = 0f;
            bool sprint = false, jump = false, esc = false, click = false;

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var ms = Mouse.current;
            if (kb != null)
            {
                h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                sprint = kb.leftShiftKey.isPressed;
                jump = kb.spaceKey.wasPressedThisFrame;
                esc = kb.escapeKey.wasPressedThisFrame;
            }
            if (ms != null)
            {
                var d = ms.delta.ReadValue();
                lookX = d.x * mouseSensitivity * 0.06f;
                lookY = d.y * mouseSensitivity * 0.06f;
                click = ms.leftButton.wasPressedThisFrame;
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            h = Input.GetAxisRaw("Horizontal");
            v = Input.GetAxisRaw("Vertical");
            sprint = Input.GetKey(KeyCode.LeftShift);
            jump = Input.GetKeyDown(KeyCode.Space);
            esc = Input.GetKeyDown(KeyCode.Escape);
            lookX = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            lookY = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;
            click = Input.GetMouseButtonDown(0);
#else
            if (!_warned) { _warned = true; Debug.LogWarning("[Nova] Giriş sistemi bulunamadı."); }
            return;
#endif

            if (esc) LockCursor(!_locked);
            if (click && !_locked) LockCursor(true);

            if (_locked && _cam != null)
            {
                transform.Rotate(0f, lookX, 0f);
                _pitch = Mathf.Clamp(_pitch - lookY, -lookXLimit, lookXLimit);
                _cam.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }

            Vector3 dir = transform.right * h + transform.forward * v;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            float speed = sprint ? sprintSpeed : walkSpeed;

            if (_cc.isGrounded)
            {
                _velY = -1f;
                if (jump) _velY = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            else _velY += gravity * Time.deltaTime;

            Vector3 move = dir * speed + Vector3.up * _velY;
            _cc.Move(move * Time.deltaTime);

            // GÜVENLİK AĞI: haritadan düşersek spawn'a ışınlan (boşlukta kaybolma bitsin)
            if (transform.position.y < -30f)
            {
                Debug.LogWarning("[Nova] Oyuncu haritadan düştü — spawn'a dönülüyor. (Spawn: " + _spawn + ")");
                _cc.enabled = false;
                transform.position = _spawn + Vector3.up * 0.5f;
                _velY = 0f;
                _cc.enabled = true;
            }
        }
    }
}
