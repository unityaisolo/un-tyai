import type { ToolSchema } from "./providers/types.js";

// Unity aksiyonlarının kanonik şemaları. Unity plugin de aynı isimleri implemente eder.
// Faz 1: 10 temel aksiyon. "destructive" işareti onay gerektiren aksiyonları belirtir.

const vec3 = {
  type: "array",
  items: { type: "number" },
  minItems: 3,
  maxItems: 3,
} as const;

export interface UnityToolSchema extends ToolSchema {
  destructive?: boolean;
}

export const TOOLS: UnityToolSchema[] = [
  {
    name: "CreateGameObject",
    description: "Sahnede yeni bir GameObject oluşturur. Primitive verilirse o şekilde oluşturur.",
    parameters: {
      type: "object",
      properties: {
        name: { type: "string", description: "Nesne adı" },
        primitive: {
          type: "string",
          enum: ["None", "Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"],
          description: "Primitive tipi; None ise boş GameObject",
        },
        position: { ...vec3, description: "[x,y,z] dünya konumu" },
        parentPath: { type: "string", description: "Ebeveyn nesnenin hiyerarşi yolu (opsiyonel)" },
      },
      required: ["name"],
    },
  },
  {
    name: "DeleteGameObject",
    description: "Hiyerarşi yolundaki GameObject'i siler.",
    destructive: true,
    parameters: {
      type: "object",
      properties: {
        path: { type: "string", description: "Silinecek nesnenin hiyerarşi yolu (örn: 'Enemies/Goblin')" },
      },
      required: ["path"],
    },
  },
  {
    name: "SetTransform",
    description: "Bir GameObject'in position/rotation/scale değerlerini ayarlar.",
    parameters: {
      type: "object",
      properties: {
        path: { type: "string", description: "Hedef nesnenin hiyerarşi yolu" },
        position: { ...vec3, description: "Yeni konum (opsiyonel)" },
        rotation: { ...vec3, description: "Euler açıları (opsiyonel)" },
        scale: { ...vec3, description: "Yeni ölçek (opsiyonel)" },
      },
      required: ["path"],
    },
  },
  {
    name: "AddComponent",
    description: "Bir GameObject'e bileşen (component) ekler. Örn: Rigidbody, BoxCollider, Light.",
    parameters: {
      type: "object",
      properties: {
        path: { type: "string", description: "Hedef nesnenin hiyerarşi yolu" },
        componentType: { type: "string", description: "Bileşen tip adı (örn: 'Rigidbody')" },
      },
      required: ["path", "componentType"],
    },
  },
  {
    name: "SetComponentProperty",
    description: "Bir bileşenin bir alanını/özelliğini ayarlar. Örn: Rigidbody.mass = 5.",
    parameters: {
      type: "object",
      properties: {
        path: { type: "string", description: "Hedef nesnenin hiyerarşi yolu" },
        componentType: { type: "string", description: "Bileşen tip adı" },
        property: { type: "string", description: "Alan/özellik adı" },
        value: { description: "Yeni değer (sayı, string, bool veya [x,y,z])" },
      },
      required: ["path", "componentType", "property", "value"],
    },
  },
  {
    name: "CreatePrimitive",
    description: "Hızlı primitive oluşturma kısayolu.",
    parameters: {
      type: "object",
      properties: {
        primitive: {
          type: "string",
          enum: ["Cube", "Sphere", "Capsule", "Cylinder", "Plane", "Quad"],
        },
        name: { type: "string", description: "Opsiyonel ad" },
        position: { ...vec3, description: "[x,y,z] konum" },
      },
      required: ["primitive"],
    },
  },
  {
    name: "InstantiatePrefab",
    description: "Assets içindeki bir prefab'ı sahneye örnekler.",
    parameters: {
      type: "object",
      properties: {
        prefabPath: { type: "string", description: "Prefab asset yolu (örn: 'Assets/Prefabs/Enemy.prefab')" },
        position: { ...vec3, description: "[x,y,z] konum" },
      },
      required: ["prefabPath"],
    },
  },
  {
    name: "ReadSceneHierarchy",
    description: "Aktif sahnedeki GameObject hiyerarşisini okur. Değişiklik yapmaz.",
    parameters: {
      type: "object",
      properties: {
        maxDepth: { type: "number", description: "Maksimum derinlik (opsiyonel)" },
      },
    },
  },
  {
    name: "ReadConsoleLogs",
    description: "Unity konsolundaki son log/uyarı/hata mesajlarını okur. Değişiklik yapmaz.",
    parameters: {
      type: "object",
      properties: {
        types: {
          type: "array",
          items: { type: "string", enum: ["Log", "Warning", "Error"] },
          description: "Filtrelenecek log tipleri (opsiyonel)",
        },
        limit: { type: "number", description: "Maksimum mesaj sayısı (varsayılan 50)" },
      },
    },
  },
  {
    name: "Generate3DModel",
    description:
      "Kullanıcı bir 3D model/nesne/karakter istediğinde BUNU KULLAN — 'yapamam' deme. " +
      "Model üretilir ve 3D Stüdyo sekmesinde önizlemeye düşer; üretim 15-60 sn sürer ve " +
      "sonuç sana bildirilir, o zaman kullanıcıya haber ver. " +
      "prompt'u SEN yaz: kullanıcının isteğini İNGİLİZCE, kısa ama betimleyici tek cümleye çevir " +
      "(nesne + stil + malzeme/renk + belirgin detay). Örn: kullanıcı 'ortaçağ kılıcı' derse " +
      "prompt: 'medieval longsword, steel blade, leather-wrapped grip, game-ready asset'.",
    destructive: true,
    parameters: {
      type: "object",
      properties: {
        prompt: {
          type: "string",
          description: "İNGİLİZCE, betimleyici model açıklaması (örn: 'sci-fi combat droid, matte metal, glowing blue visor')",
        },
        imageUrl: { type: "string", description: "Görselden üretim için görsel URL'si (opsiyonel)" },
        name: { type: "string", description: "Sahnedeki nesne adı" },
        position: { ...vec3, description: "[x,y,z] konum" },
      },
      required: ["prompt"],
    },
  },
  {
    name: "ReadScript",
    description: "Assets içindeki bir C# script dosyasının mevcut içeriğini okur (düzenleme öncesi). Değişiklik yapmaz.",
    parameters: {
      type: "object",
      properties: {
        path: { type: "string", description: "Assets'e göre yol (örn: 'Assets/Scripts/Player.cs')" },
      },
      required: ["path"],
    },
  },
  {
    name: "WriteScript",
    description: "Bir C# script için değişiklik önerir. Kullanıcıya Kod sekmesinde diff olarak sunulur, onaylanınca yazılır.",
    destructive: true,
    parameters: {
      type: "object",
      properties: {
        path: { type: "string", description: "Assets'e göre yol (örn: 'Assets/Scripts/PlayerController.cs')" },
        content: { type: "string", description: "Dosyanın tam C# içeriği" },
      },
      required: ["path", "content"],
    },
  },
  // ---- Sahne / arazi / asset yönetimi (Dünya sekmesindeki her şey sohbetten de yapılabilir) ----
  {
    name: "ListPlacedAssets",
    description:
      "Sahnede Nova ile yerleştirilmiş assetlerin envanterini döner (dosya, rol, adet). " +
      "Kullanıcı 'şunu kaldır/değiştir' dediğinde ÖNCE bunu çağır ki neyin var olduğunu bilesin.",
    parameters: { type: "object", properties: {} },
  },
  {
    name: "RemovePlacedAssets",
    description:
      "Sahneden asset kaldırır. 'match' asset dosya adının veya nesne adının bir parçasıdır " +
      "(örn 'palm'), 'role' ise tür (tree, rock, bush, prop). Ctrl+Z ile geri alınabilir.",
    destructive: true,
    parameters: {
      type: "object",
      properties: {
        match: { type: "string", description: "Asset/nesne adında geçen metin (örn: 'palm-tree')" },
        role: { type: "string", description: "Rol filtresi: tree | bush | rock | prop | misc" },
      },
    },
  },
  {
    name: "BuildTerrain",
    description:
      "Araziyi yeniden üretir (biome değiştirme, büyütme, yoğunluk). Kullanıcı 'araziyi çöl yap', " +
      "'daha tepelik olsun', 'ırmak ekle' dediğinde kullan. Mevcut arazinin yerine yenisi kurulur.",
    destructive: true,
    parameters: {
      type: "object",
      properties: {
        biome: { type: "string", description: "plains | forest | valley | hills | coast | desert | snow | swamp | canyon | volcanic" },
        size: { type: "number", description: "Kare kenarı, metre (100-2000, varsayılan 400)" },
        density: { type: "number", description: "Bitki yoğunluğu 0-1 (varsayılan 0.6)" },
        river: { type: "boolean", description: "Irmak açılsın mı" },
        lake: { type: "boolean", description: "Göl açılsın mı" },
        trees: { type: "boolean" },
        rocks: { type: "boolean" },
        bushes: { type: "boolean" },
      },
      required: ["biome"],
    },
  },
  {
    name: "DecorateArea",
    description:
      "Sahnede seçili noktanın (veya SceneView bakış merkezinin) çevresine temalı dekor döşer. " +
      "Kullanıcı 'buraya kamp alanı kur', 'bu bölgeyi süsle', 'yol kenarına çit ve lamba döşe' " +
      "dediğinde BUNU kullan. Plan beyin tarafından çıkarılır, assetler katalogdan seçilir, " +
      "zemine oturtulur; tek Undo ile geri alınır.",
    parameters: {
      type: "object",
      properties: {
        prompt: {
          type: "string",
          description: "Dekor isteğinin kısa tarifi (Türkçe olabilir; ör. 'kamp alanı', 'çiçekli bahçe, çitli')",
        },
        radius: { type: "number", description: "Yerleştirme yarıçapı metre (4-60, varsayılan 15)" },
      },
      required: ["prompt"],
    },
  },
  {
    name: "EditDecor",
    description:
      "Var olan dekoru DÜZENLER (DecorateArea ile döşenen). Kullanıcı 'dekoru kaldır', 'bunu " +
      "çeşitle / başka türlü dene', 'bu ağacı/kayayı başkasıyla değiştir' derse BUNU kullan. " +
      "action: 'clear' (kaldır — scope 'near'=yakındaki, 'all'=hepsi), 'vary' (son dekoru yeni " +
      "tohumla yeniden döşe), 'replace' (SEÇİLİ parçayı aynı rolden farklısıyla değiştir).",
    parameters: {
      type: "object",
      properties: {
        action: { type: "string", enum: ["clear", "vary", "replace"], description: "Yapılacak düzenleme" },
        scope: { type: "string", enum: ["near", "all"], description: "clear için: yakındaki mi hepsi mi (varsayılan near)" },
      },
      required: ["action"],
    },
  },
  {
    name: "BuildGameTemplate",
    description:
      "Oynanabilir oyun şablonu kurar. Kullanıcı 'arena yap', 'dalga savunması', 'düşman dalgaları', " +
      "'platform oyunu', 'zıplama oyunu', 'yarış/drift oyunu', 'kule savunma/tower defense' derse BUNU kullan. " +
      "type seçenekleri: 'arena' (FPS dalga savunması — düşman dalgaları, sol tık ateş), " +
      "'platformer' (prosedürel platformlarda zıplama+coin), 'racer' (prosedürel pist + drift, tur süresi), " +
      "'towerdefense' (yol kenarına kule kur, dalgaları durdur). " +
      "Sonsuz koşu için ayrı BuildRunner aracı var.",
    parameters: {
      type: "object",
      properties: {
        type: { type: "string", enum: ["arena", "platformer", "racer", "towerdefense"], description: "Kurulacak şablon" },
        play: { type: "boolean", description: "Kurulumdan sonra hemen Play moduna gir (varsayılan false)" },
      },
      required: ["type"],
    },
  },
  {
    name: "BuildRunner",
    description:
      "3D SONSUZ KOŞU oyunu şablonu kurar (Subway Surfers tarzı). Kullanıcı 'sonsuz koşu yap', " +
      "'runner oyunu kur', 'koşu oyunu' derse BUNU kullan. Oyuncu + takip kamerası + prosedürel " +
      "engel/coin + skor kurulur; assetler katalogdan gelir. play=true ise hemen Play moduna girer.",
    parameters: {
      type: "object",
      properties: {
        play: { type: "boolean", description: "Kurulumdan sonra hemen Play moduna gir (varsayılan false)" },
      },
    },
  },
  {
    name: "PrepareForPlay",
    description:
      "Üretilen dünyayı OYNANABİLİR hale getirir. Kullanıcı 'oynanabilir yap', 'oyuna hazırla', " +
      "'NavMesh oluştur', 'spawn noktası koy', 'minimap al' derse BUNU kullan. Seçili adımları uygular: " +
      "NavMesh bake (NPC gezintisi), oyuncu spawn noktası (güvenli konum), üstten minimap PNG'si.",
    parameters: {
      type: "object",
      properties: {
        navmesh: { type: "boolean", description: "NavMesh bake edilsin mi (varsayılan true)" },
        spawn: { type: "boolean", description: "Oyuncu spawn noktası konsun mu (varsayılan true)" },
        minimap: { type: "boolean", description: "Üstten minimap PNG'si üretilsin mi (varsayılan true)" },
      },
    },
  },
  {
    name: "MigrateToURP",
    description:
      "URP göç asistanı. Kullanıcı 'URP'ye geçmek istiyorum', 'materyaller pembe/bozuk görünüyor', " +
      "'shaderları URP yap' derse BUNU kullan. convert=false (varsayılan) önce bir TARAMA raporu döner " +
      "(kaç Standard/pembe/özel materyal var). Kullanıcı onaylarsa convert=true ile Standard ve pembe " +
      "materyalleri URP/Lit'e çevirir (renk/doku/metallic/normal/emisyon eşlenir, Ctrl+Z geri alır). " +
      "Önce convert=false ile tara, raporu göster, sonra kullanıcı isterse convert=true çağır.",
    parameters: {
      type: "object",
      properties: {
        convert: {
          type: "boolean",
          description: "false=yalnız tara ve raporla (varsayılan) · true=çevir (onay diyaloğu açılır)",
        },
      },
    },
  },
  {
    name: "ScanScene",
    description:
      "Genel sahne sağlık/optimizasyon taraması — HERHANGİ bir Unity sahnesinde çalışır (Nova ile " +
      "üretilmemiş olsa da). Kullanıcı 'sahnemde sorun var mı', 'sahneyi kontrol et', 'optimize et', " +
      "'neden kasıyor', 'temizle' derse BUNU kullan. Tespit: kayıp script/materyal/mesh, bozuk ölçek, " +
      "sıfır collider, boş nesne, çoklu AudioListener, dev doku (>2048px), havada asılı/renksiz modeller, " +
      "ışık/kamera eksikliği, poligon yükü. repair=true verilirse güvenli düzeltmeleri (Ctrl+Z ile geri " +
      "alınabilir) onaya sunar.",
    parameters: {
      type: "object",
      properties: { repair: { type: "boolean", description: "Bulduklarını onarmayı öner" } },
    },
  },
  {
    // Tahmin etmek yerine SOR. Belirsiz bir istekte (hangi dosya? hangi karakter?
    // 2D mi 3D mi?) bu aracı çağır; kullanıcı cevaplayana kadar tur durur.
    name: "AskUser",
    description:
      "İstek belirsizse kullanıcıya soru sorar ve cevabını bekler. Varsayım yapıp yanlış kod " +
      "yazmak yerine bunu kullan. Kısa ve tek bir soru sor; mümkünse seçenek ver.",
    parameters: {
      type: "object",
      properties: {
        question: { type: "string", description: "Kullanıcıya sorulacak tek, net soru" },
        options: {
          type: "array",
          items: { type: "string" },
          description: "Opsiyonel hazır seçenekler (örn: ['2D', '3D'])",
        },
      },
      required: ["question"],
    },
  },
];

export function toolByName(name: string): UnityToolSchema | undefined {
  return TOOLS.find((t) => t.name === name);
}
