using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Eklenen görsele tıklayınca açılan büyük önizleme penceresi.
    /// Görsel, pencereye sığacak şekilde oranı korunarak çizilir.
    /// </summary>
    public class NovaImagePreview : EditorWindow
    {
        private Texture2D _tex;

        public static void Show(Texture2D tex, string title)
        {
            if (tex == null) return;
            var w = CreateInstance<NovaImagePreview>();
            w._tex = tex;
            w.titleContent = new GUIContent(string.IsNullOrEmpty(title) ? "Görsel" : title);

            // Ekrana sığacak makul bir başlangıç boyutu
            float max = 900f;
            float k = Mathf.Min(1f, max / Mathf.Max(tex.width, tex.height));
            w.minSize = new Vector2(240, 180);
            w.position = new Rect(200, 120, tex.width * k + 20, tex.height * k + 40);
            w.ShowUtility();
        }

        private void OnGUI()
        {
            if (_tex == null) { Close(); return; }

            var area = new Rect(10, 10, position.width - 20, position.height - 40);
            float k = Mathf.Min(area.width / _tex.width, area.height / _tex.height);
            var rect = new Rect(
                area.x + (area.width - _tex.width * k) * 0.5f,
                area.y + (area.height - _tex.height * k) * 0.5f,
                _tex.width * k, _tex.height * k);

            EditorGUI.DrawPreviewTexture(rect, _tex, null, ScaleMode.ScaleToFit);
            EditorGUI.LabelField(new Rect(10, position.height - 24, position.width - 20, 18),
                $"{_tex.width} × {_tex.height} px — kapatmak için Esc", EditorStyles.miniLabel);

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) Close();
        }
    }
}
