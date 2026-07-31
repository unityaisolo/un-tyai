using UnityEngine;

namespace NovaWorld
{
    /// <summary>
    /// World Builder'ın yerleştirdiği her objeye takılan işaret: beklenen boyut + rol.
    /// SceneLint (denetçi) build sonrası bu bilgiyle dev/uçan/gömük objeleri tespit edip düzeltir.
    /// </summary>
    public class NovaPlaced : MonoBehaviour
    {
        public string role;
        public float targetSize;          // metre — bu rol için beklenen azami makul boyut çıpası
        public string assetFile;          // katalogdaki kaynak dosya (tutarsızlık raporunda görünür)
        public GameObject linkedCollider; // binanın ayrı collider objesi — obje silinirse bu da silinmeli
    }
}
