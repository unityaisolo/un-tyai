using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityAI
{
 /// <summary>
 /// ÇOK DİLLİ ARAYÜZ — Türkçe · English · 简体中文 · 繁體中文.
 /// Kullanım: NovaLocale.T("world.build"). Seçim EditorPrefs'te saklanır.
 /// Yeni metin eklerken: Map'e satır ekle → { "anahtar", new[]{ tr, en, zhHans, zhHant } }.
 /// </summary>
 public static class NovaLocale
 {
 public enum Lang { Turkce = 0, English = 1, ChineseSimplified = 2, ChineseTraditional = 3 }

 public static readonly string[] LangNames = { "Türkçe", "English", "简体中文", "繁體中文" };
 private const string PrefKey = "UnityAI.Lang";

 public static event Action Changed;

 public static Lang Current
 {
 get => (Lang)EditorPrefs.GetInt(PrefKey, 0);
 set { EditorPrefs.SetInt(PrefKey, (int)value); Changed?.Invoke(); }
 }

 public static string T(string key)
 {
 if (Map.TryGetValue(key, out var v))
 {
 int i = (int)Current;
 return i >= 0 && i < v.Length && !string.IsNullOrEmpty(v[i]) ? v[i] : v[0];
 }
 return key; // eksik anahtar görünür kalsın (geliştirme kolaylığı)
 }

 /// <summary>Biçimlendirilmiş yerelleştirme: T("tool.notFound", path) → "Nesne bulunamadı: Assets/x"gibi.
 /// Yerelleştirilmiş dizede {0}, {1}... yer tutucuları string.Format ile doldurulur.</summary>
 public static string T(string key, params object[] args)
 {
 string fmt = T(key);
 if (args == null || args.Length == 0) return fmt;
 try { return string.Format(fmt, args); }
 catch { return fmt; } // yer tutucu sayısı tutmazsa ham dizeyi göster, patlamasın
 }

 /// <summary>Katalog/meta'dan gelen Türkçe hava durumu etiketini yerelleştirir.</summary>
 public static string Mood(string trMood) => trMood switch
 {
 "Açık gündüz" => T("mood.clear"),
 "Gündüz" => T("mood.day"),
 "Gün batımı" => T("mood.sunset"),
 "Bulutlu" => T("mood.overcast"),
 "Gece" => T("mood.night"),
 _ => trMood,
 };

 // tr, en, 简体, 繁體
 private static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
 {
 // ---- Sekmeler / genel ----
 { "tab.chat", new[]{ "Kod Ajanı", "Code Agent", "代码智能体", "程式碼代理" } },
 { "tab.studio", new[]{ "3D Stüdyo", "3D Studio", "3D 工作室", "3D 工作室" } },
 { "tab.material", new[]{ "Malzeme", "Material", "材质", "材質" } },
 { "menu.settings", new[]{ "Ayarlar — API anahtarı", "Settings — API key", "设置 —— API 密钥", "設定 —— API 金鑰" } },
 { "menu.assetLib", new[]{ "Asset kütüphanesi…", "Asset library…", "素材库…", "素材庫…" } },
 { "tab.world", new[]{ "Dünya", "World", "世界", "世界" } },

 // ---- AYARLAR sekmesi: API anahtarları + rol bazlı model seçimi ----
 { "set.title", new[]{ "AYARLAR", "SETTINGS", "设置", "設定" } },
 { "set.headHint", new[]{ "anahtarı yapıştır, gerisi otomatik", "paste the key, the rest is automatic", "粘贴密钥，其余自动完成", "貼上金鑰，其餘自動完成" } },
 { "set.privacy", new[]{ "Anahtarlarını yalnızca bu bilgisayarda, şifreli tutuyoruz.",
 "Your keys are kept encrypted on this computer only.",
 "你的密钥仅以加密方式保存在本机。", "你的金鑰僅以加密方式保存在本機。" } },
 { "set.mode.pool", new[]{ "Mod: sunucu havuzu açık — kendi anahtarın yoksa sunucunun anahtarı kullanılır.",
 "Mode: server pool enabled — the server's key is used if you have none.",
 "模式：服务器共享池已启用 —— 若你没有密钥，将使用服务器的密钥。",
 "模式：伺服器共享池已啟用 —— 若你沒有金鑰，將使用伺服器的金鑰。" } },



 { "set.key.placeholder", new[]{ "anahtarı buraya yapıştır", "paste your key here", "在此粘贴你的密钥", "在此貼上你的金鑰" } },
 { "set.btn.connect", new[]{ "Bağla ve kullan", "Connect and use", "连接并使用", "連線並使用" } },
 { "set.activeTitle", new[]{ "ŞU AN KULLANILAN", "CURRENTLY IN USE", "当前使用中", "目前使用中" } },
 { "set.active", new[]{ "Model: {0}", "Model: {0}", "模型：{0}", "模型：{0}" } },
 { "set.active.none", new[]{ "Henüz servis bağlanmadı — yukarıdaki 3 adımı tamamla.",
 "No service connected yet — complete the 3 steps above.",
 "尚未连接服务 —— 请完成上面 3 个步骤。", "尚未連線服務 —— 請完成上面 3 個步驟。" } },
 { "set.custom.needUrl", new[]{ "Önce endpoint adresini yaz (https:// ile başlamalı).", "Enter the endpoint URL first (must start with https://).",
 "请先填写端点地址（须以 https:// 开头）。", "請先填寫端點位址（須以 https:// 開頭）。" } },
 { "set.loadFail", new[]{ "Ayarlar okunamadı: {0}", "Could not load settings: {0}", "无法读取设置：{0}", "無法讀取設定：{0}" } },
 { "set.serverDown", new[]{ "Sunucuya ulaşılamadı: {0} — backend çalışıyor mu? (npm run dev)",
 "Server unreachable: {0} — is the backend running? (npm run dev)",
 "无法连接服务器：{0} —— 后端是否在运行？(npm run dev)",
 "無法連線伺服器：{0} —— 後端是否在執行？(npm run dev)" } },

 { "set.key.tooShort", new[]{ "Anahtar çok kısa görünüyor — tamamını yapıştırdığından emin ol.",
 "That key looks too short — make sure you pasted all of it.",
 "密钥看起来太短 —— 请确认已完整粘贴。", "金鑰看起來太短 —— 請確認已完整貼上。" } },
 { "set.key.deleted", new[]{ "{0} anahtarı silindi.", "{0} key deleted.", "{0} 密钥已删除。", "{0} 金鑰已刪除。" } },
 { "set.key.delFail", new[]{ "Anahtar silinemedi: {0}", "Could not delete key: {0}", "无法删除密钥：{0}", "無法刪除金鑰：{0}" } },
 { "set.del.title", new[]{ "Anahtarı sil", "Delete key", "删除密钥", "刪除金鑰" } },
 { "set.del.body", new[]{ "'{0}' anahtarı kasadan silinecek. Bu sağlayıcıyı kullanan işler anahtar isteyecek. Devam?",
 "The '{0}' key will be removed from the vault. Jobs using this provider will ask for a key. Continue?",
 "将从密钥库中删除 '{0}' 密钥。使用该提供方的任务将要求密钥。是否继续？",
 "將從金鑰庫中刪除 '{0}' 金鑰。使用該提供方的工作將要求金鑰。是否繼續？" } },

 { "set.test.running", new[]{ "Bağlantı test ediliyor…", "Testing connection…", "正在测试连接…", "正在測試連線…" } },
 { "set.test.ok", new[]{ "✓ Çalışıyor — sağlayıcı: {0} · model: {1}", "✓ Works — provider: {0} · model: {1}",
 "✓ 正常 —— 提供方：{0} · 模型：{1}", "✓ 正常 —— 提供方：{0} · 模型：{1}" } },
 { "set.test.fail", new[]{ "✗ Test başarısız: {0}", "✗ Test failed: {0}", "✗ 测试失败：{0}", "✗ 測試失敗：{0}" } },
 { "set.btn.test", new[]{ "Bağlantıyı test et", "Test connection", "测试连接", "測試連線" } },
 { "set.btn.reload", new[]{ "Yenile", "Refresh", "刷新", "重新整理" } },
 { "set.btn.delKey", new[]{ "Sil", "Delete", "删除", "刪除" } },
 { "set.pasteKey", new[]{ "API ANAHTARINI YAPIŞTIR", "PASTE YOUR API KEY", "粘贴你的 API 密钥", "貼上你的 API 金鑰" } },
 { "set.autoNote", new[]{ "Hangi servis olduğunu anahtardan anlarız ve çalışan modeli seçeriz.", "We detect the service from the key and pick a working model.", "我们会从密钥识别服务并选择可用模型。", "我們會從金鑰識別服務並選擇可用模型。" } },
 { "set.detecting", new[]{ "Anahtar tanınıyor, uygun model aranıyor… (30 sn sürebilir)",
 "Detecting the key and finding a working model… (may take 30s)",
 "正在识别密钥并寻找可用模型…（可能需要 30 秒）",
 "正在識別金鑰並尋找可用模型…（可能需要 30 秒）" } },
 { "set.autoOk", new[]{ "✓ {0} bağlandı · model: {1} — Nova hazır.", "✓ {0} connected · model: {1} — Nova is ready.",
 "✓ 已连接 {0} · 模型：{1} —— Nova 就绪。", "✓ 已連線 {0} · 模型：{1} —— Nova 就緒。" } },
 { "set.autoFail", new[]{ "Bağlanamadı: {0}", "Could not connect: {0}", "无法连接：{0}", "無法連線：{0}" } },
 { "set.rejected", new[]{ "Denenip uygun bulunmayanlar:", "Tried but not suitable:", "已尝试但不适用：", "已嘗試但不適用：" } },
 { "set.oldBackend", new[]{ "Sunucu eski sürüm — backend'i yeniden başlat (npm run dev).",
 "Server is outdated — restart the backend (npm run dev).",
 "服务器版本过旧 —— 请重启后端 (npm run dev)。", "伺服器版本過舊 —— 請重啟後端 (npm run dev)。" } },
 { "set.ownServerTitle", new[]{ "Listede olmayan bir servis mi kullanıyorsun?", "Using a service that isn't listed?",
 "使用列表之外的服务？", "使用清單之外的服務？" } },
 { "set.ownServer", new[]{ "Servisini seç, adresi biz dolduralım. Yerel modeller (Ollama, LM Studio, vLLM) anahtar istemez.", "Pick your service, we'll fill the address. Local models (Ollama, LM Studio, vLLM) need no key.", "选择服务，地址由我们填写。本机模型（Ollama、LM Studio、vLLM）无需密钥。", "選擇服務，位址由我們填寫。本機模型（Ollama、LM Studio、vLLM）無需金鑰。" } },
 { "set.preset.label", new[]{ "Servis", "Service", "服务", "服務" } },
 { "set.preset.manual", new[]{ "Adresi kendim yazacağım", "I'll type the address myself", "我自己填写地址", "我自己填寫位址" } },
 { "set.addrNote", new[]{ "Adres genelde /v1 ile biter.", "Addresses usually end with /v1.", "地址通常以 /v1 结尾。", "位址通常以 /v1 結尾。" } },
 { "set.customBase", new[]{ "Sunucu adresi", "Server address", "服务器地址", "伺服器位址" } },
 { "app.tagline", new[]{ "Üret · Düzenle · Sahnele", "Create · Edit · Stage", "生成 · 编辑 · 布置", "生成 · 編輯 · 佈置" } },
 { "app.newchat", new[]{ "+ Yeni sohbet", "+ New chat", "+ 新对话", "+ 新對話" } },
 { "app.theme.dark", new[]{ "Koyu", "Dark", "深色", "深色" } },
 { "app.theme.light",new[]{ "Açık", "Light", "浅色", "淺色" } },
 { "app.language", new[]{ "Dil", "Language", "语言", "語言" } },

 // ---- Sohbet ----
 { "chat.scan", new[]{ "Sahneyi tara", "Scan scene", " 扫描场景", " 掃描場景" } },
 { "chat.attachimg", new[]{ "Görsel ekle", "Add image", " 添加图片", " 新增圖片" } },
 { "chat.attachshot",new[]{ "Ekranı çek", "Capture view", " 截取视图", " 擷取視圖" } },
 { "chat.attachclr", new[]{ "✕ Görselleri kaldır", "✕ Remove images", "✕ 移除图片", "✕ 移除圖片" } },
 { "chat.fix", new[]{ "Hataları düzelt", "Fix errors", " 修复错误", " 修復錯誤" } },
 { "chat.player", new[]{ "Player", "Player", " 角色控制", " 角色控制" } },
 { "chat.health", new[]{ "Can", "Health", " 生命值", " 生命值" } },
 { "chat.inventory", new[]{ "Envanter", "Inventory", " 物品栏", " 物品欄" } },
 { "chat.send", new[]{ "Gönder ➤", "Send ➤", "发送 ➤", "發送 ➤" } },
 { "chat.stop", new[]{ "■ Durdur", "■ Stop", "■ 停止", "■ 停止" } },
 { "chat.placeholder", new[]{ "Mesaj yaz…", "Write a message…", "输入消息…", "輸入訊息…" } },
 { "chat.empty", new[]{ "Nova hazır — ne yapalım?", "Nova is ready — what shall we do?", "Nova 已就绪 — 我们做什么？", "Nova 已就緒 — 我們做什麼？" } },
 { "chat.autoapprove", new[]{ "Nova kendi uygulasın (Ctrl+Z geri alır)", "Let Nova apply (Ctrl+Z undoes)", "让 Nova 直接执行（Ctrl+Z 撤销）", "讓 Nova 直接執行（Ctrl+Z 復原）" } },
 { "chat.council", new[]{ "Çift kontrol (daha yavaş, daha güvenli)", "Double-check (slower, safer)", "双重检查（更慢更稳）", "雙重檢查（較慢較穩）" } },
 { "chat.model", new[]{ "Model", "Model", "模型", "模型" } },

 // ---- Dünya paneli ----
 { "world.hint", new[]{ "biome seç → tek tıkla gezilebilir arazi", "pick a biome → walkable terrain in one click", "选择生物群系 → 一键生成可漫游地形", "選擇生態域 → 一鍵產生可漫遊地形" } },
 { "world.type", new[]{ "Arazi tipi", "Terrain type", "地形类型", "地形類型" } },
 { "world.prompt.ph", new[]{ "Haritanı tarif et (opsiyonel): 'kıvrımlı nehirli çam ormanı, hava bulutlu'...",
 "Describe your map (optional): 'pine forest with a winding river, overcast sky'...",
 "描述你的地图（可选）：'有蜿蜒河流的松林，阴天'…",
 "描述你的地圖（可選）：'有蜿蜒河流的松林，陰天'…" } },
 { "world.prompt.hint", new[]{ "Beyin tarifini plana çevirir, aşağıdaki seçimleri doldurur ve kurar.",
 "The brain turns your description into a plan, fills the controls below and builds.",
 "AI 将描述转为方案，填充下方选项并生成。",
 "AI 將描述轉為方案，填充下方選項並產生。" } },
 { "world.trees", new[]{ "Ağaçlar", "Trees", "树木", "樹木" } },
 { "world.bushes", new[]{ "Çalılar", "Bushes", "灌木", "灌木" } },
 { "world.rocks", new[]{ "Kayalar", "Rocks", "岩石", "岩石" } },
 { "world.river", new[]{ "Irmak", "River", "河流", "河流" } },
 { "world.lake", new[]{ "Göl", "Lake", "湖泊", "湖泊" } },
 { "world.addplayer", new[]{ "Play'e hazır (oyuncu ekle)", "Play-ready (add player)", "可直接游玩（添加玩家）", "可直接遊玩（新增玩家）" } },
 { "city.label", new[]{ "Şehir", "City", "城市", "城市" } },

 // ---- Asset kütüphanesi (yol çözümü / beta kurulumu) ----
 { "lib.menu.title", new[]{ "Nova Asset Kütüphanesi", "Nova Asset Library", "Nova 素材库", "Nova 素材庫" } },
 { "lib.missing.title", new[]{ "Asset kütüphanesi bulunamadı", "Asset library not found", "未找到素材库", "未找到素材庫" } },
 { "lib.missing.body", new[]{ "Nova'nın 3D model kütüphanesi bulunamadı.\n\nBeklenen konum: proje kökünde '{0}' klasörü.\n\n• Buluttan indir — kütüphaneyi otomatik kurar (önerilen)\n• Klasör seç — kütüphane zaten diskindeyse",
 "Nova's 3D model library was not found.\n\nExpected location: a '{0}' folder in your project root.\n\n• Download from cloud — sets it up automatically (recommended)\n• Pick folder — if the library is already on your disk",
 "未找到 Nova 的 3D 模型库。\n\n预期位置：项目根目录下的 '{0}' 文件夹。\n\n• 从云端下载 —— 自动安装（推荐）\n• 选择文件夹 —— 素材库已在本地时",
 "未找到 Nova 的 3D 模型庫。\n\n預期位置：專案根目錄下的 '{0}' 資料夾。\n\n• 從雲端下載 —— 自動安裝（推薦）\n• 選擇資料夾 —— 素材庫已在本機時" } },
 { "lib.missing.pick", new[]{ "Klasör seç…", "Pick folder…", "选择文件夹…", "選擇資料夾…" } },
 { "lib.pick.title", new[]{ "catalog.json içeren klasörü seç", "Select the folder containing catalog.json", "选择包含 catalog.json 的文件夹", "選擇包含 catalog.json 的資料夾" } },
 { "lib.notfound.title", new[]{ "catalog.json yok", "catalog.json missing", "缺少 catalog.json", "缺少 catalog.json" } },
 { "lib.notfound.body", new[]{ "Seçilen klasörde catalog.json bulunamadı. Kütüphane klasörünü seçtiğinden emin ol.",
 "No catalog.json in the selected folder. Make sure you picked the library folder.",
 "所选文件夹中没有 catalog.json。请确认选择了素材库文件夹。",
 "所選資料夾中沒有 catalog.json。請確認選擇了素材庫資料夾。" } },
 { "lib.status.ok", new[]{ "Kütüphane hazır ✓\n\n{0}\n\n{1} asset yüklü.", "Library ready ✓\n\n{0}\n\n{1} assets loaded.",
 "素材库就绪 ✓\n\n{0}\n\n已加载 {1} 个资源。", "素材庫就緒 ✓\n\n{0}\n\n已載入 {1} 個資源。" } },
 { "lib.status.none", new[]{ "Kütüphane bulunamadı.\n\nBeklenen konum:\n{0}", "Library not found.\n\nExpected location:\n{0}",
 "未找到素材库。\n\n预期位置：\n{0}", "未找到素材庫。\n\n預期位置：\n{0}" } },
 { "lib.notReady", new[]{ "⚠ Asset kütüphanesi yok — UnityAI ▸ Asset Kütüphanesi… ile klasörü seç.",
 "⚠ Asset library missing — use UnityAI ▸ Asset Library… to pick the folder.",
 "⚠ 缺少素材库 —— 使用 UnityAI ▸ 素材库… 选择文件夹。",
 "⚠ 缺少素材庫 —— 使用 UnityAI ▸ 素材庫… 選擇資料夾。" } },
 { "lib.noModels", new[]{ "⚠ catalog.json var ama 'assets-raw' klasörü yok: {0}", "⚠ catalog.json found but no 'assets-raw' folder: {0}",
 "⚠ 找到 catalog.json 但缺少 'assets-raw' 文件夹：{0}", "⚠ 找到 catalog.json 但缺少 'assets-raw' 資料夾：{0}" } },
 { "lib.reset.title", new[]{ "Kütüphane kaydını sıfırla", "Reset library setting", "重置素材库设置", "重設素材庫設定" } },
 { "lib.reset.body", new[]{ "Kayıtlı katalog yolu silinecek:\n{0}\n\nBu ayar Unity'de PROJE bazlı değil, KULLANICI bazlı tutulur — yani tüm projelerini etkiler.\n\nSıfırlarsan bir sonraki kurulumda yol yeniden aranır; yerel klasör bulunmazsa bulut akışı devreye girer (temiz kurulum testi).",
 "The saved catalog path will be cleared:\n{0}\n\nUnity stores this per USER, not per project — it affects all your projects.\n\nAfter resetting, the path is searched again on next use; if no local folder is found the cloud flow kicks in (clean-install test).",
 "将清除已保存的目录路径：\n{0}\n\nUnity 按用户而非按项目保存此设置 —— 会影响你的所有项目。\n\n重置后，下次使用时将重新搜索路径；若找不到本地文件夹，将启用云端流程（全新安装测试）。",
 "將清除已儲存的目錄路徑：\n{0}\n\nUnity 按使用者而非按專案儲存此設定 —— 會影響你的所有專案。\n\n重設後，下次使用時將重新搜尋路徑；若找不到本機資料夾，將啟用雲端流程（全新安裝測試）。" } },
 { "lib.reset.ok", new[]{ "Sıfırla", "Reset", "重置", "重設" } },
 { "lib.cloudBtn", new[]{ "Buluttan indir", "Download from cloud", "从云端下载", "從雲端下載" } },
 { "lib.downloading", new[]{ "Kütüphane indiriliyor… bitince işlemi yeniden dene.",
 "Downloading library… retry the action when it finishes.",
 "正在下载素材库…完成后请重试该操作。",
 "正在下載素材庫…完成後請重試該操作。" } },
 { "lib.lazyModels", new[]{ "Modeller yerelde yok — kullanıldıkça buluttan indirilecek.",
 "Models not local — they will be downloaded on demand.",
 "本地没有模型 —— 将按需从云端下载。",
 "本機沒有模型 —— 將按需從雲端下載。" } },

 // İndirme bitişi: kullanıcı asenkron işlemin sonucunu görmeli, yoksa boşlukta kalır.
 { "lib.dl.okTitle", new[]{ "Kütüphane hazır", "Library ready", "素材库就绪", "素材庫就緒" } },
 { "lib.dl.okBody", new[]{ "Katalog indirildi. Artık işlemi yeniden çalıştırabilirsin.\n\nModeller kullanıldıkça buluttan indirilir.",
 "Catalog downloaded. You can run the action again now.\n\nModels download from the cloud as they are used.",
 "目录已下载。现在可以重新执行该操作。\n\n模型将在使用时从云端下载。",
 "目錄已下載。現在可以重新執行該操作。\n\n模型將在使用時從雲端下載。" } },
 { "lib.dl.texTitle", new[]{ "Arazi dokuları indirilsin mi?", "Download terrain textures?", "是否下载地形纹理？", "是否下載地形紋理？" } },
 { "lib.dl.texBody",  new[]{
"Çim, kaya ve kum dokuları ayrı bir pakette. İndirmezsen arazi çalışır ama düz renk görünür.\n\nPaket birkaç yüz MB; bir kez inip diskte kalır.",
"Grass, rock and sand textures ship as a separate pack. Without it the terrain still works but looks flat-coloured.\n\nThe pack is a few hundred MB; it downloads once and stays on disk.",
"草地、岩石和沙地纹理为单独的包。不下载地形仍可用，但只有纯色。\n\n该包有数百 MB，只需下载一次。",
"草地、岩石和沙地紋理為單獨的包。不下載地形仍可用，但只有純色。\n\n該包有數百 MB，只需下載一次。" } },
 { "lib.dl.texYes",   new[]{ "İndir", "Download", "下载", "下載" } },
 { "lib.dl.failTitle", new[]{ "Kütüphane indirilemedi", "Library download failed", "素材库下载失败", "素材庫下載失敗" } },
 { "lib.dl.failBody", new[]{ "İndirme başarısız oldu.\n\nSebep: {0}\n\nEn sık nedeni: Nova sunucusu çalışmıyor. Terminalde 'cd backend' ve 'npm run dev' ile başlatıp tekrar dene.\n\nKütüphane zaten diskindeyse 'Klasör seç' ile gösterebilirsin.",
 "The download failed.\n\nReason: {0}\n\nMost common cause: the Nova server is not running. Start it with 'cd backend' and 'npm run dev', then try again.\n\nIf the library is already on disk, use 'Pick folder'.",
 "下载失败。\n\n原因：{0}\n\n最常见的原因：Nova 服务器未运行。请执行 'cd backend' 和 'npm run dev' 后重试。\n\n如果素材库已在磁盘上，请使用“选择文件夹”。",
 "下載失敗。\n\n原因：{0}\n\n最常見的原因：Nova 伺服器未執行。請執行 'cd backend' 和 'npm run dev' 後重試。\n\n如果素材庫已在磁碟上，請使用「選擇資料夾」。" } },

 // ---- Bulut indirme ----
 { "dl.noManifest", new[]{ "Bulut kütüphanesi yapılandırılmamış (sunucu manifesti yok).",
 "Cloud library not configured (no server manifest).",
 "未配置云端素材库（无服务器清单）。",
 "未配置雲端素材庫（無伺服器清單）。" } },
 { "dl.catalog", new[]{ "Katalog indiriliyor…", "Downloading catalog…", "正在下载目录…", "正在下載目錄…" } },
 { "dl.catalogOk", new[]{ "Katalog hazır · {0} asset.", "Catalog ready · {0} assets.", "目录就绪 · {0} 个资源。", "目錄就緒 · {0} 個資源。" } },
 { "dl.textures", new[]{ "Doku paketi indiriliyor…", "Downloading texture pack…", "正在下载纹理包…", "正在下載紋理包…" } },
 { "dl.texturesOk", new[]{ "Doku paketi hazır.", "Texture pack ready.", "纹理包就绪。", "紋理包就緒。" } },
 { "dl.noTextures", new[]{ "Doku paketi yayınlanmamış — arazi düz renk katmanlar kullanacak.",
 "No texture pack published — terrain will use flat colour layers.",
 "未发布纹理包 —— 地形将使用纯色图层。",
 "未發布紋理包 —— 地形將使用純色圖層。" } },
 { "dl.error", new[]{ "İndirme hatası: {0}", "Download error: {0}", "下载错误：{0}", "下載錯誤：{0}" } },
 { "dl.failDialog", new[]{ "Kütüphane indirilemedi. Sunucu adresini ve internet bağlantını kontrol et; ya da klasörü elle seç.",
 "Could not download the library. Check the server URL and your connection, or pick the folder manually.",
 "无法下载素材库。请检查服务器地址与网络连接，或手动选择文件夹。",
 "無法下載素材庫。請檢查伺服器位址與網路連線，或手動選擇資料夾。" } },
 { "dl.okDialog", new[]{ "Kütüphane hazır ✓\n\n{0} asset\nKonum: {1}\n\nModeller kullanıldıkça indirilir.",
 "Library ready ✓\n\n{0} assets\nLocation: {1}\n\nModels download on demand.",
 "素材库就绪 ✓\n\n{0} 个资源\n位置：{1}\n\n模型将按需下载。",
 "素材庫就緒 ✓\n\n{0} 個資源\n位置：{1}\n\n模型將按需下載。" } },

 { "lib.simpleGeometry", new[]{ "Sahne basit geometriyle kurulacak (asset kütüphanesi bağlı değil).",
 "Scene will be built with simple geometry (asset library not connected).",
 "将使用简单几何体构建场景（未连接素材库）。",
 "將使用簡單幾何體建構場景（未連接素材庫）。" } },
 { "lib.terrainNoPlants", new[]{ "Arazi kuruldu (bitki yok — asset kütüphanesi bağlı değil).",
 "Terrain built (no plants — asset library not connected).",
 "地形已生成（无植被 —— 未连接素材库）。",
 "地形已產生（無植被 —— 未連接素材庫）。" } },

 // ---- Dünya paneli başlığı + oyun şablonu grupları ----
 { "world.title", new[]{ "DÜNYA / OYUN ÜRETİCİ", "WORLD / GAME BUILDER", "世界 / 游戏生成器", "世界 / 遊戲產生器" } },
 { "world.headhint", new[]{ "oyun tipi seç → menüler şekillensin", "pick a game type → menus adapt", "选择游戏类型 → 菜单随之变化", "選擇遊戲類型 → 選單隨之變化" } },
 { "world.ready", new[]{ "Hazır.", "Ready.", "就绪。", "就緒。" } },
 { "game.playnow", new[]{ "Kurunca hemen oyna (Play)", "Play immediately after building", "生成后立即游玩", "產生後立即遊玩" } },
 { "runner.desc", new[]{ "Subway Surfers tarzı 3D sonsuz koşu. Prosedürel şerit, engeller ve coin'ler katalogdan; oyuncu + takip kamerası hazır kurulur.",
 "Subway Surfers-style 3D endless runner. Procedural lanes, obstacles and coins from the catalog; player + follow camera are set up for you.",
 "跑酷类 3D 无尽跑酷。程序化车道、障碍与金币来自素材库；玩家与跟随相机自动配置。",
 "跑酷類 3D 無盡跑酷。程序化車道、障礙與金幣來自素材庫；玩家與跟隨相機自動配置。" } },
 { "runner.controls", new[]{ "Kontroller: A/D şerit · Space zıpla · S kay · R yeniden başla",
 "Controls: A/D lane · Space jump · S slide · R restart",
 "操作：A/D 换道 · Space 跳跃 · S 滑行 · R 重新开始",
 "操作：A/D 換道 · Space 跳躍 · S 滑行 · R 重新開始" } },
 { "arena.desc", new[]{ "Haritanda düşman dalgaları. Sahnede Nova arazisi varsa onun üstüne kurulur; yoksa düz arena açılır. Her dalgada düşman sayısı ve hızı artar.",
 "Enemy waves on your map. Built on top of an existing Nova terrain, or a flat arena if there is none. Each wave brings more and faster enemies.",
 "地图上的敌人波次。若场景已有 Nova 地形则在其上构建，否则生成平坦竞技场。每一波敌人更多更快。",
 "地圖上的敵人波次。若場景已有 Nova 地形則在其上建立，否則產生平坦競技場。每一波敵人更多更快。" } },
 { "arena.controls", new[]{ "Kontroller: WASD hareket · Fare bak · Sol tık ateş · Space zıpla · R yeniden",
 "Controls: WASD move · Mouse look · Left click shoot · Space jump · R restart",
 "操作：WASD 移动 · 鼠标视角 · 左键射击 · Space 跳跃 · R 重开",
 "操作：WASD 移動 · 滑鼠視角 · 左鍵射擊 · Space 跳躍 · R 重開" } },
 { "plat.desc", new[]{ "Boşlukta prosedürel platform dizisi. Zıplayarak ilerle, coin topla; düşersen son platformdan devam edersin. Bazı platformlar yanal salınır.",
 "A procedural chain of floating platforms. Jump forward, collect coins; if you fall you resume from the last platform. Some platforms sway sideways.",
 "悬空的程序化平台序列。向前跳跃收集金币；坠落后从最后一个平台继续。部分平台会左右摆动。",
 "懸空的程序化平台序列。向前跳躍收集金幣；墜落後從最後一個平台繼續。部分平台會左右擺動。" } },
 { "plat.controls", new[]{ "Kontroller: WASD hareket · Space zıpla · R yeniden",
 "Controls: WASD move · Space jump · R restart",
 "操作：WASD 移动 · Space 跳跃 · R 重开",
 "操作：WASD 移動 · Space 跳躍 · R 重開" } },
 { "twod.soon", new[]{ "2D oyun şablonları (Flappy / platformer / 2D koşu) yakında. Bu tür ayrı bir 2D asset+fizik hattı gerektiriyor — yol haritasında.",
 "2D game templates (Flappy / platformer / 2D runner) are coming. They need a separate 2D asset + physics pipeline — on the roadmap.",
 "2D 游戏模板（Flappy / 平台 / 2D 跑酷）即将推出。需要独立的 2D 素材与物理管线 —— 已在路线图中。",
 "2D 遊戲範本（Flappy / 平台 / 2D 跑酷）即將推出。需要獨立的 2D 素材與物理管線 —— 已在路線圖中。" } },
 { "world.size", new[]{ "Boyut", "Size", "尺寸", "尺寸" } },
 { "world.density", new[]{ "Yoğunluk", "Density", "密度", "密度" } },
 { "world.sky", new[]{ "Gökyüzü", "Sky", "天空", "天空" } },
 { "world.relief", new[]{ "Engebe", "Relief", "起伏", "起伏" } },
 { "world.rivercurve", new[]{ "Nehir kıvrımı", "River curve", "河流弯曲", "河流彎曲" } },
 { "world.flowers", new[]{ "Çiçekler", "Flowers", "花草", "花草" } },
 { "world.path", new[]{ "Patika", "Path", "小径", "小徑" } },
 { "world.adv", new[]{ "Gelişmiş", "Advanced", " 高级", " 進階" } },
 { "world.treemul", new[]{ "Ağaç oranı ×", "Tree ratio ×", "树木比例 ×", "樹木比例 ×" } },
 { "world.rockmul", new[]{ "Kaya oranı ×", "Rock ratio ×", "岩石比例 ×", "岩石比例 ×" } },
 { "world.bushmul", new[]{ "Çalı oranı ×", "Bush ratio ×", "灌木比例 ×", "灌木比例 ×" } },
 { "world.seed", new[]{ "Seed", "Seed", "种子", "種子" } },
 { "world.atmo", new[]{ "Atmosfer", "Atmosphere", " 大气", " 大氣" } },
 { "world.fog", new[]{ "Sis", "Fog", "雾", "霧" } },
 { "world.fogdens", new[]{ "Sis yoğunluğu", "Fog density", "雾密度", "霧密度" } },
 { "world.wind", new[]{ "Rüzgâr", "Wind", "风", "風" } },
 { "world.sun", new[]{ "Güneş", "Sun", "太阳", "太陽" } },
 { "sun.auto", new[]{ "Gökyüzüne göre", "Follow sky", "跟随天空", "跟隨天空" } },
 { "sun.morning", new[]{ "Sabah", "Morning", "早晨", "早晨" } },
 { "sun.noon", new[]{ "Öğle", "Noon", "中午", "中午" } },
 { "sun.evening", new[]{ "Akşam / günbatımı", "Evening / sunset", "傍晚 / 日落", "傍晚 / 日落" } },
 { "world.build", new[]{ "Haritayı kur", "Generate map", " 生成地图", " 產生地圖" } },
 { "world.explore", new[]{ "Gez (FPS)", "Walk (FPS)", " 漫游（FPS）", " 漫遊（FPS）" } },
 { "world.save", new[]{ "Projeye kaydet", "Save to project", " 保存到项目", " 儲存到專案" } },
 { "world.qa", new[]{ "AI görsel denetim", "AI visual review", "AI 视觉检查", "AI 視覺檢查" } },
 { "world.prep", new[]{ "Oyna hazırla", "Prepare to play", " 准备游玩", " 準備遊玩" } },
 { "game.runner", new[]{ "Sonsuz Koşu kur", "Build Endless Runner", " 生成无尽跑酷", " 產生無盡跑酷" } },
 { "game.type", new[]{ "Oyun tipi", "Game type", "游戏类型", "遊戲類型" } },
 { "game.type.openworld", new[]{ "Açık Dünya (FPS)", "Open World (FPS)", " 开放世界（FPS）", " 開放世界（FPS）" } },
 { "game.type.runner", new[]{ "Sonsuz Koşu", "Endless Runner", " 无尽跑酷", " 無盡跑酷" } },
 { "game.type.arena", new[]{ "FPS Arena (dalga)", "FPS Arena (waves)", "FPS 竞技场（波次）", "FPS 競技場（波次）" } },
 { "game.type.platformer", new[]{ "3D Platformer", "3D Platformer", "3D 平台跳跃", "3D 平台跳躍" } },
 { "game.type.racer", new[]{ "Yarış / Drift", "Racing / Drift", " 竞速 / 漂移", " 競速 / 漂移" } },
 { "game.type.td", new[]{ "Kule Savunma", "Tower Defense", " 塔防", " 塔防" } },
 { "game.type.2d", new[]{ "2D (yakında)", "2D (soon)", "2D（即将推出）", "2D（即將推出）" } },
 { "game.racer", new[]{ "Yarış pisti kur", "Build Race Track", " 生成赛道", " 產生賽道" } },
 { "game.td", new[]{ "Kule savunma kur", "Build Tower Defense", " 生成塔防", " 產生塔防" } },
 { "game.type.racer.h", new[]{ "Prosedürel pistte sür, drift at, tur süresi kır.", "Drive a procedural track, drift, beat your lap time.", "在程序生成的赛道上驾驶、漂移、刷新圈速。", "在程序產生的賽道上駕駛、漂移、刷新圈速。" } },
 { "game.type.td.h", new[]{ "Yol kenarına kule kur, dalgaları üsse ulaşmadan durdur.", "Place towers along the path, stop the waves before they reach your base.", "在路径旁建塔，在敌人到达基地前拦住波次。", "在路徑旁建塔，在敵人到達基地前攔住波次。" } },
 { "racer.desc", new[]{ "Prosedürel kapalı pist + arcade araç fiziği. El freniyle drift atarsın, tur süresi ve en iyi turun ölçülür. Araç katalogdan gelir.",
 "A procedural closed circuit with arcade car physics. Handbrake to drift; lap time and your best lap are tracked. Car comes from the catalog.",
 "程序生成的闭环赛道 + 街机车辆物理。手刹漂移，记录圈速与最佳成绩。车辆来自素材库。",
 "程序產生的閉環賽道 + 街機車輛物理。手煞漂移，記錄圈速與最佳成績。車輛來自素材庫。" } },
 { "racer.controls", new[]{ "Kontroller: WASD sür · Space el freni (drift) · R piste dön",
 "Controls: WASD drive · Space handbrake (drift) · R back to track",
 "操作：WASD 驾驶 · Space 手刹（漂移）· R 回到赛道",
 "操作：WASD 駕駛 · Space 手煞（漂移）· R 回到賽道" } },
 { "td.desc", new[]{ "Düşmanlar kıvrımlı yolu takip ederek üsse yürür. Yol kenarına kule kurup dalgaları durdurursun; düşman vurdukça altın kazanırsın.",
 "Enemies follow a winding path toward your base. Place towers alongside it to stop the waves; kills earn gold.",
 "敌人沿蜿蜒路径走向基地。在路旁建塔拦截波次；击杀可获得金币。",
 "敵人沿蜿蜒路徑走向基地。在路旁建塔攔截波次；擊殺可獲得金幣。" } },
 { "td.controls", new[]{ "Kontroller: Yol kenarına sol tık → kule kur · WASD kamera kaydır · Fare tekerleği yakınlaş · R yeniden",
 "Controls: Left click beside the path → build tower · WASD pan camera · Scroll to zoom · R restart",
 "操作：在路旁左键点击 → 建塔 · WASD 平移镜头 · 滚轮缩放 · R 重开",
 "操作：在路旁左鍵點擊 → 建塔 · WASD 平移鏡頭 · 滾輪縮放 · R 重開" } },
 { "game.arena", new[]{ "Arena kur", "Build Arena", " 生成竞技场", " 產生競技場" } },
 { "game.platformer", new[]{ "Platformer kur", "Build Platformer", " 生成平台关卡", " 產生平台關卡" } },
 { "game.type.arena.h", new[]{ "Haritanda düşman dalgaları — nişan al, hayatta kal.", "Enemy waves on your map — aim and survive.", "地图上的敌人波次 —— 瞄准并生存。", "地圖上的敵人波次 —— 瞄準並生存。" } },
 { "game.type.platformer.h", new[]{ "Prosedürel platformlarda zıpla, coin topla.", "Jump across procedural platforms, collect coins.", "在程序生成的平台间跳跃收集金币。", "在程序生成的平台間跳躍收集金幣。" } },
 { "game.type.openworld.h", new[]{ "Biome/arazi tipi seç → gezilebilir 3D dünya.", "Pick a biome/terrain → walkable 3D world.", "选择生物群系/地形 → 可漫游 3D 世界。", "選擇生態域/地形 → 可漫遊 3D 世界。" } },
 { "game.type.runner.h", new[]{ "Subway Surfers tarzı 3D sonsuz koşu şablonu.", "Subway Surfers-style 3D endless runner template.", "跑酷类 3D 无尽跑酷模板。", "跑酷類 3D 無盡跑酷範本。" } },
 { "game.type.2d.h", new[]{ "2D şablonları yol haritasında (yakında).", "2D templates are on the roadmap (soon).", "2D 模板在路线图中（即将推出）。", "2D 範本在路線圖中（即將推出）。" } },
 { "prep.navmesh", new[]{ "NavMesh", "NavMesh", "导航网格", "導航網格" } },
 { "prep.spawn", new[]{ "Spawn noktası", "Spawn point", "出生点", "出生點" } },
 { "prep.minimap", new[]{ "Minimap", "Minimap", "小地图", "小地圖" } },
 { "world.status", new[]{ "Hazır.", "Ready.", "就绪。", "就緒。" } },

 // ---- Boyutlar ----
 { "size.small", new[]{ "Küçük", "Small", "小", "小" } },
 { "size.medium", new[]{ "Orta", "Medium", "中", "中" } },
 { "size.large", new[]{ "Büyük", "Large", "大", "大" } },

 // ---- Arazi tipleri ----
 { "map.plains", new[]{ "Ova (çayır)", "Plains (meadow)", "平原（草地）", "平原（草地）" } },
 { "map.forest", new[]{ "Orman", "Forest", "森林", "森林" } },
 { "map.valley", new[]{ "Dağ Vadisi (ırmaklı)", "Mountain Valley (river)", "山谷（有河流）", "山谷（有河流）" } },
 { "map.hills", new[]{ "Tepelik", "Hills", "丘陵", "丘陵" } },
 { "map.coast", new[]{ "Sahil", "Coast", "海岸", "海岸" } },
 { "map.desert", new[]{ "Çöl", "Desert", "沙漠", "沙漠" } },
 { "map.lakeside", new[]{ "Göl Kenarı", "Lakeside", "湖畔", "湖畔" } },
 { "map.plains.h", new[]{ "Düz yeşil çayır; seyrek ağaç, çalı.", "Flat green meadow; scattered trees and bushes.", "平坦的绿色草地；稀疏的树木和灌木。", "平坦的綠色草地；稀疏的樹木和灌木。" } },
 { "map.forest.h", new[]{ "Sık ağaçlı, engebeli orman.", "Dense, rolling forest.", "茂密起伏的森林。", "茂密起伏的森林。" } },
 { "map.valley.h", new[]{ "Etrafı dağlarla çevrili yeşil vadi; ortadan ırmak akar.", "Green valley ringed by mountains; a river runs through it.", "群山环绕的绿色山谷，河流穿行其中。", "群山環繞的綠色山谷，河流穿行其中。" } },
 { "map.hills.h", new[]{ "Yumuşak tepeler, dağınık ağaç ve kaya.", "Gentle hills with scattered trees and rocks.", "平缓的丘陵，散布着树木和岩石。", "平緩的丘陵，散布著樹木和岩石。" } },
 { "map.coast.h", new[]{ "Bir yanı deniz; kumsaldan yükselen kıyı.", "Sea on one side; shore rising from the beach.", "一侧是大海；海滩向内陆抬升。", "一側是大海；海灘向內陸抬升。" } },
 { "map.desert.h", new[]{ "Kumul sırtları; seyrek kaya.", "Dune ridges; sparse rocks.", "沙丘脊线；零星岩石。", "沙丘脊線；零星岩石。" } },
 { "map.lakeside.h", new[]{ "Ova ortasında göl; kıyısında yeşillik.", "A lake amid plains, greenery along its shore.", "平原中的湖泊，岸边绿意盎然。", "平原中的湖泊，岸邊綠意盎然。" } },
 { "map.snow", new[]{ "Karlı Dağlar", "Snowy Mountains", "雪山", "雪山" } },
 { "map.snow.h", new[]{ "Yüksek karlı zirveler; iğne yapraklı ağaçlar, buzlu kaya.", "High snowy peaks; conifers and icy rock.", "高耸的雪峰；针叶树与冰岩。", "高聳的雪峰；針葉樹與冰岩。" } },
 { "map.swamp", new[]{ "Bataklık", "Swamp", "沼泽", "沼澤" } },
 { "map.swamp.h", new[]{ "Alçak, ıslak, sığ suyla kaplı; söğüt/ölü ağaçlar.", "Low, wet, shallow water; willows and dead trees.", "低洼潮湿的浅水地；柳树和枯木。", "低窪潮濕的淺水地；柳樹和枯木。" } },
 { "map.canyon", new[]{ "Kanyon / Mesa", "Canyon / Mesa", "峡谷 / 台地", "峽谷 / 台地" } },
 { "map.canyon.h", new[]{ "Kırmızımsı kaya platoları ve keskin uçurumlar.", "Reddish rock plateaus and sharp cliffs.", "红色岩石台地与陡峭悬崖。", "紅色岩石台地與陡峭懸崖。" } },
 { "map.volcanic", new[]{ "Volkanik", "Volcanic", "火山", "火山" } },
 { "map.volcanic.h", new[]{ "Merkezde volkan konisi; kara bazalt, çorak arazi.", "Central volcano cone; black basalt, barren land.", "中央火山锥；黑色玄武岩、荒芜地貌。", "中央火山錐；黑色玄武岩、荒蕪地貌。" } },

 // ---- Gökyüzü presetleri ----
 { "sky.day", new[]{ "Gündüz", "Day", "白天", "白天" } },
 { "sky.sunset", new[]{ "Gün batımı", "Sunset", "日落", "日落" } },
 { "sky.night", new[]{ "Gece", "Night", "夜晚", "夜晚" } },
 { "sky.overcast", new[]{ "Bulutlu", "Overcast", "阴天", "陰天" } },
 { "sky.dawn", new[]{ "Şafak", "Dawn", "黎明", "黎明" } },
 { "sky.horror", new[]{ "Sisli / Korku", "Foggy / Horror", "雾气 / 恐怖", "霧氣 / 恐怖" } },
 { "mood.clear", new[]{ "Açık gündüz", "Clear day", "晴天", "晴天" } },
 { "mood.day", new[]{ "Gündüz", "Daytime", "白天", "白天" } },
 { "mood.sunset", new[]{ "Gün batımı", "Sunset", "日落", "日落" } },
 { "mood.overcast", new[]{ "Bulutlu", "Overcast", "阴天", "陰天" } },
 { "mood.night", new[]{ "Gece", "Night", "夜晚", "夜晚" } },

 // ---- Dekor ----
 { "decor.title", new[]{ "HIZLI DEKOR", "QUICK DECOR", "快速装饰", "快速裝飾" } },
 { "decor.hint", new[]{ "SceneView'da dekor istediğin bölgeye bak → preset seç → uygula. Ctrl+Z geri alır.",
 "Look at the target area in SceneView → pick a preset → apply. Ctrl+Z undoes it.",
 "在 SceneView 中对准目标区域 → 选择预设 → 应用。Ctrl+Z 可撤销。",
 "在 SceneView 中對準目標區域 → 選擇預設 → 套用。Ctrl+Z 可復原。" } },
 { "decor.preset", new[]{ "Preset", "Preset", "预设", "預設" } },
 { "decor.radius", new[]{ "Yarıçap (m)", "Radius (m)", "半径（米）", "半徑（公尺）" } },
 { "decor.apply", new[]{ "Uygula", "Apply", " 应用", " 套用" } },
 { "decor.forest", new[]{ "Orman köşesi", "Forest corner", "森林一角", "森林一角" } },
 { "decor.camp", new[]{ "Kamp alanı", "Campsite", "营地", "營地" } },
 { "decor.garden", new[]{ "Köy bahçesi", "Village garden", "乡村花园", "鄉村花園" } },
 { "decor.rocky", new[]{ "Kayalık", "Rocky patch", "岩石地", "岩石地" } },
 { "decor.meadow", new[]{ "Çiçek çayırı", "Flower meadow", "花草地", "花草地" } },

 // ---- Malzeme / Stüdyo başlıkları ----
 { "mat.title", new[]{ "MALZEME / TEXTURE", "MATERIAL / TEXTURE", "材质 / 贴图", "材質 / 貼圖" } },
 { "mat.generate", new[]{ "Üret + uygula ", "Generate + apply ", "生成并应用 ", "產生並套用 " } },
 { "mat.revert", new[]{ "↩ Geri al", "↩ Revert", "↩ 撤销", "↩ 復原" } },
 { "studio.title", new[]{ "3D STÜDYO", "3D STUDIO", "3D 工作室", "3D 工作室" } },
 { "studio.generate",new[]{ "Üret ", "Generate ", "生成 ", "產生 " } },
 { "studio.add", new[]{ "✓ Sahneye ekle", "✓ Add to scene", "✓ 添加到场景", "✓ 加入場景" } },
 { "studio.clear", new[]{ "Temizle", "Clear", "清除", "清除" } },

 // ==================================================================
 // ---- Sohbet: gönderen etiketleri (AppendMessage sender'ları) ----
 { "chat.role.you", new[]{ "Sen", "You", "你", "你" } },
 { "chat.role.system", new[]{ "Sistem", "System", "系统", "系統" } },
 { "chat.role.auditor", new[]{ "Denetçi", "Auditor", "审核员", "審核員" } },
 { "chat.role.error", new[]{ "Hata", "Error", "错误", "錯誤" } },
 { "chat.role.tool", new[]{ "Araç", "Tool", "工具", "工具" } },
 { "chat.role.asking", new[]{ "Nova soruyor", "Nova is asking", "Nova 在提问", "Nova 在提問" } },
 { "chat.role.thinking",new[]{ "Nova düşünüyor", "Nova is thinking", "Nova 正在思考", "Nova 正在思考" } },

 // ---- Sohbet: sistem/durum mesajları ----
 { "chat.msg.compileInterruptedResult", new[]{
 "Unity derleme yaptığı için bu adım yarıda kaldı.",
 "This step was interrupted because Unity recompiled.",
 "由于 Unity 重新编译，此步骤被中断。",
 "由於 Unity 重新編譯，此步驟被中斷。" } },
 { "chat.msg.compileInterrupted", new[]{
 "↻ Unity kodu derledi ve editörü yeniden yükledi. Konuşma korundu — yarım kalan adım varsa 'devam et' yazman yeterli.",
 "↻ Unity recompiled the code and reloaded the editor. The conversation was preserved — if a step was left unfinished, just type 'continue'.",
 "↻ Unity 重新编译了代码并重新加载了编辑器。对话已保留——如果有未完成的步骤，输入'继续'即可。",
 "↻ Unity 重新編譯了程式碼並重新載入了編輯器。對話已保留——如果有未完成的步驟，輸入「繼續」即可。" } },
 { "chat.msg.attachedSuffix", new[]{ "{0}\n{1} eklendi", "{0}\n{1} attached", "{0}\n已添加 {1}", "{0}\n已新增 {1}" } },
 { "chat.msg.stopModelWait", new[]{
 "⏹ Bekleme iptal edildi. Model üretimi sunucuda sürüyorsa 3D Stüdyo'da görünecek.",
 "⏹ Wait cancelled. If model generation is still running on the server, it will appear in the 3D Studio.",
 "⏹ 已取消等待。如果模型仍在服务器上生成，将出现在 3D 工作室中。",
 "⏹ 已取消等待。如果模型仍在伺服器上產生，將出現在 3D 工作室中。" } },
 { "chat.msg.stoppedByUser", new[]{ "⏹ Kullanıcı durdurdu.", "⏹ Stopped by user.", "⏹ 用户已停止。", "⏹ 使用者已停止。" } },
 { "chat.msg.optionsPrefix", new[]{ "Seçenekler: ", "Options: ", "选项：", "選項：" } },
 { "chat.msg.toolRejected", new[]{ "⨯ {0}: reddedildi", "⨯ {0}: rejected", "⨯ {0}：已拒绝", "⨯ {0}：已拒絕" } },
 { "chat.msg.userRejected", new[]{ "Kullanıcı reddetti", "User rejected", "用户已拒绝", "使用者已拒絕" } },
 { "chat.msg.turnLimit", new[]{
 "(Güvenlik sınırı: maksimum adım.)", "(Safety limit: maximum steps reached.)",
 "（安全限制：已达最大步数。）", "（安全限制：已達最大步數。）" } },
 { "chat.msg.toolResultLine", new[]{ "{0} {1}: {2}", "{0} {1}: {2}", "{0} {1}：{2}", "{0} {1}：{2}" } },

 // ---- Dialoglar ----
 { "dialog.confirmAction.title", new[]{ "Nova bunu yapmak istiyor", "Nova wants to do this", "Nova 想要执行此操作", "Nova 想要執行此操作" } },
 { "dialog.confirmAction.body", new[]{ "{0}\n\nCtrl+Z ile geri alabilirsin.", "{0}\n\nYou can undo with Ctrl+Z.", "{0}\n\n你可以用 Ctrl+Z 撤销。", "{0}\n\n你可以用 Ctrl+Z 復原。" } },
 { "dialog.continue", new[]{ "Devam et", "Continue", "继续", "繼續" } },
 { "dialog.cancel", new[]{ "Vazgeç", "Cancel", "取消", "取消" } },

 { "dialog.genImage.title", new[]{ "Görsel üret", "Generate image", "生成图片", "生成圖片" } },
 { "dialog.genImage.body", new[]{
 "'{0}' için bir görsel üretilecek (fal ile, ücretli). Devam edilsin mi?",
 "An image will be generated for '{0}' (via fal, paid). Continue?",
 "将为 '{0}' 生成一张图片（通过 fal，付费）。是否继续？",
 "將為 '{0}' 產生一張圖片（透過 fal，付費）。是否繼續？" } },

 { "dialog.rig.title", new[]{ "Rigleme", "Rigging", "绑定骨骼", "綁定骨骼" } },
 { "dialog.rig.body", new[]{
 "Bu model riglenip animasyon eklenecek (ücretli). Devam edilsin mi?",
 "This model will be rigged and animated (paid). Continue?",
 "此模型将被绑定骨骼并添加动画（付费）。是否继续？",
 "此模型將被綁定骨骼並新增動畫（付費）。是否繼續？" } },

 { "dialog.genModel.title", new[]{ "3D model üretilsin mi?", "Generate 3D model?", "生成 3D 模型？", "產生 3D 模型？" } },
 { "dialog.genModel.body", new[]{
 "'{0}' için 3D model üretilecek (ücretli, 15-60 sn sürer). Devam edilsin mi?",
 "A 3D model will be generated for '{0}' (paid, takes 15-60 s). Continue?",
 "将为 '{0}' 生成 3D 模型（付费，需 15-60 秒）。是否继续？",
 "將為 '{0}' 產生 3D 模型（付費，需 15-60 秒）。是否繼續？" } },

 { "dialog.sceneHealthRepair.title", new[]{ "Nova Sağlık — Onarım", "Nova Health — Repair", "Nova 健康 — 修复", "Nova 健康 — 修復" } },
 { "dialog.sceneHealthRepair.body", new[]{
 "Şunlar düzeltilebilir:\n\n{0}\n\nUygulansın mı? (Ctrl+Z ile geri alınabilir)",
 "The following can be fixed:\n\n{0}\n\nApply? (Ctrl+Z undoes it)",
 "以下问题可以修复：\n\n{0}\n\n是否应用？（Ctrl+Z 可撤销）",
 "以下問題可以修復：\n\n{0}\n\n是否套用？（Ctrl+Z 可復原）" } },
 { "dialog.repair.apply", new[]{ "Onar", "Repair", "修复", "修復" } },
 { "dialog.repair.later", new[]{ "Şimdilik kalsın", "Leave it for now", "暂时保留", "暫時保留" } },

 { "dialog.saveMap.title", new[]{ "Nova — Haritayı kaydet", "Nova — Save map", "Nova — 保存地图", "Nova — 儲存地圖" } },
 { "dialog.saveMap.body", new[]{
 "Varlıklar projeye yazıldı. Sahne henüz kaydedilmemiş; şimdi kaydedelim mi?\n(Kaydedilmezse Unity kapanınca harita sahneden kaybolur.)",
 "Assets were written to the project. The scene hasn't been saved yet; save it now?\n(If not saved, the map disappears from the scene when Unity closes.)",
 "资源已写入项目。场景尚未保存；现在保存吗？\n（如果不保存，Unity 关闭时地图将从场景中消失。）",
 "資源已寫入專案。場景尚未儲存；現在儲存嗎？\n（如果不儲存，Unity 關閉時地圖將從場景中消失。）" } },
 { "dialog.saveMap.save", new[]{ "Sahneyi kaydet", "Save scene", "保存场景", "儲存場景" } },
 { "dialog.saveMap.later",new[]{ "Sonra", "Later", "稍后", "稍後" } },

 // ---- Tooltip / durum ----
 { "tooltip.copyMessage", new[]{ "Bu mesajı panoya kopyala", "Copy this message to clipboard", "将此消息复制到剪贴板", "將此訊息複製到剪貼簿" } },
 { "tooltip.removeAttachment", new[]{ "Bu eki kaldır", "Remove this attachment", "移除此附件", "移除此附件" } },
 { "tooltip.newModelReady", new[]{ "Yeni 3D model hazır", "New 3D model ready", "新 3D 模型已就绪", "新 3D 模型已就緒" } },
 { "status.copied", new[]{ "Mesaj panoya kopyalandı.", "Message copied to clipboard.", " 消息已复制到剪贴板。", " 訊息已複製到剪貼簿。" } },
 { "status.thinking", new[]{ "Düşünüyor...", "Thinking...", "思考中…", "思考中…" } },
 { "status.busyWait", new[]{ "⏳ Nova çalışıyor — bitmesini bekle ya da ■ Durdur'a bas.", "⏳ Nova is working — wait for it to finish or press ■ Stop.", "⏳ Nova 正在处理——请等待完成或按 ■ 停止。", "⏳ Nova 正在處理——請等待完成或按 ■ 停止。" } },
 { "status.ready", new[]{ "Hazır", "Ready", "就绪", "就緒" } },
 { "status.newSession", new[]{ "Yeni oturum", "New session", "新会话", "新工作階段" } },
 { "status.waitingAnswer", new[]{ "Cevabın bekleniyor — yaz ve gönder.", "Waiting for your answer — type and send.", "等待你的回答——输入并发送。", "等待你的回答——輸入並傳送。" } },
 { "status.stopped", new[]{ "Durduruldu", "Stopped", "已停止", "已停止" } },
 { "status.stoppedStepLimit", new[]{ "Durduruldu (adım sınırı)", "Stopped (step limit)", "已停止（步数限制）", "已停止（步數限制）" } },
 { "status.error", new[]{ "Hata", "Error", "错误", "錯誤" } },
 { "status.timeoutNoResponse", new[]{ "zaman aşımı (sunucu yanıt vermedi)", "timeout (server did not respond)", "超时（服务器无响应）", "逾時（伺服器無回應）" } },

 // ---- Ekli belge/görsel akışı ----
 { "attach.noImageOnClipboard", new[]{
 "Panoda görsel yok. Ekran görüntüsü alıp tekrar dene (Win+Shift+S).",
 "No image on clipboard. Take a screenshot and try again (Win+Shift+S).",
 "剪贴板中没有图片。请截图后重试（Win+Shift+S）。",
 "剪貼簿中沒有圖片。請截圖後重試（Win+Shift+S）。" } },
 { "attach.docReadError", new[]{ "Belge okunamadı: {0}", "Could not read document: {0}", "无法读取文档：{0}", "無法讀取文件：{0}" } },
 { "attach.imageReadError", new[]{ "Görsel okunamadı: {0}", "Could not read image: {0}", "无法读取图片：{0}", "無法讀取圖片：{0}" } },
 { "attach.imageAddError", new[]{ "Görsel eklenemedi: {0}", "Could not add image: {0}", "无法添加图片：{0}", "無法新增圖片：{0}" } },
 { "attach.sceneViewClosed", new[]{
 "Scene penceresi açık değil — önce Scene görünümünü aç.",
 "The Scene window isn't open — open the Scene view first.",
 "场景窗口未打开——请先打开场景视图。",
 "場景視窗未開啟——請先開啟場景檢視。" } },
 { "attach.screenshotError", new[]{ "Ekran görüntüsü alınamadı: {0}", "Could not capture screenshot: {0}", "无法截图：{0}", "無法截圖：{0}" } },
 { "attach.imageEncodeError", new[]{ "Görsel kodlanamadı.", "Could not encode image.", "无法编码图片。", "無法編碼圖片。" } },

 // ---- Araç sonuç mesajları (Tools/*.cs) ----
 { "tool.notFoundObject", new[]{ "Nesne bulunamadı: {0}", "Object not found: {0}", "找不到物件：{0}", "找不到物件：{0}" } },
 { "tool.notFoundComponentType", new[]{ "Bileşen tipi bulunamadı: {0}", "Component type not found: {0}", "找不到组件类型：{0}", "找不到元件類型：{0}" } },
 { "tool.addComponentFailed", new[]{ "Bileşen eklenemedi: {0}", "Failed to add component: {0}", "无法添加组件：{0}", "無法新增元件：{0}" } },
 { "tool.componentAdded", new[]{ "'{0}' eklendi -> {1}", "'{0}' added -> {1}", "已添加 '{0}' -> {1}", "已新增 '{0}' -> {1}" } },
 { "tool.componentMissing", new[]{ "Bileşen yok: {0}", "Component missing: {0}", "缺少组件：{0}", "缺少元件：{0}" } },
 { "tool.memberNotFound", new[]{ "Alan/özellik bulunamadı: {0}", "Field/property not found: {0}", "找不到字段/属性：{0}", "找不到欄位/屬性：{0}" } },
 { "tool.objectDeleted", new[]{ "'{0}' silindi.", "'{0}' deleted.", "已删除 '{0}'。", "已刪除 '{0}'。" } },
 { "tool.transformUpdated", new[]{ "'{0}' transform güncellendi.", "'{0}' transform updated.", "已更新 '{0}' 的变换。", "已更新 '{0}' 的變換。" } },
 { "tool.invalidPrimitive", new[]{ "Geçersiz primitive: {0}", "Invalid primitive: {0}", "无效的基本体：{0}", "無效的基本體：{0}" } },
 { "tool.primitiveCreated", new[]{ "'{0}' ({1}) oluşturuldu.", "'{0}' ({1}) created.", "已创建 '{0}' ({1})。", "已建立 '{0}' ({1})。" } },
 { "tool.prefabNotFound", new[]{ "Prefab bulunamadı: {0}", "Prefab not found: {0}", "找不到预制体：{0}", "找不到預製體：{0}" } },
 { "tool.prefabInstantiated", new[]{ "'{0}' örneklendi.", "'{0}' instantiated.", "已实例化 '{0}'。", "已實例化 '{0}'。" } },
 { "tool.sceneEmpty", new[]{ "(sahne boş)", "(scene is empty)", "（场景为空）", "（場景為空）" } },
 { "tool.consoleEmpty", new[]{ "(konsol boş)", "(console is empty)", "（控制台为空）", "（控制台為空）" } },
 { "tool.logEntriesMissing", new[]{ "LogEntries API yok", "LogEntries API unavailable", "LogEntries API 不可用", "LogEntries API 不可用" } },
 { "tool.consoleReadError", new[]{ "Konsol okunamadı: {0}", "Could not read console: {0}", "无法读取控制台：{0}", "無法讀取控制台：{0}" } },
 { "tool.pathContentRequired", new[]{ "path ve content gerekli", "path and content are required", "需要 path 和 content", "需要 path 和 content" } },
 { "tool.pathMustStartAssets", new[]{ "Yol 'Assets/' ile başlamalı", "Path must start with 'Assets/'", "路径必须以 'Assets/' 开头", "路徑必須以 'Assets/' 開頭" } },
 { "tool.securityOutsideAssetsWrite", new[]{
 "Güvenlik: '{0}' Assets/ dışına çıkıyor ('../' yasak).",
 "Security: '{0}' escapes Assets/ ('../' not allowed).",
 "安全限制：'{0}' 超出了 Assets/ 目录（禁止使用 '../'）。",
 "安全限制：'{0}' 超出了 Assets/ 目錄（禁止使用 '../'）。" } },
 { "tool.changeProposed", new[]{
 "Değişiklik önerildi: {0}. Kod sekmesinde diff'i onayla.",
 "Change proposed: {0}. Approve the diff in the Code tab.",
 "已提出更改：{0}。请在代码标签中批准差异。",
 "已提出變更：{0}。請在程式碼標籤中核准差異。" } },
 { "tool.pathRequired", new[]{ "path gerekli", "path is required", "需要 path", "需要 path" } },
 { "tool.securityOutsideAssetsRead", new[]{
 "Güvenlik: '{0}' Assets/ dışına çıkıyor.", "Security: '{0}' escapes Assets/.",
 "安全限制：'{0}' 超出了 Assets/ 目录。", "安全限制：'{0}' 超出了 Assets/ 目錄。" } },
 { "tool.fileMissing", new[]{ "Dosya yok: {0}", "File not found: {0}", "文件不存在：{0}", "檔案不存在：{0}" } },
 { "tool.promptRequired", new[]{ "prompt gerekli", "prompt is required", "需要 prompt", "需要 prompt" } },
 { "tool.gen3dStarted", new[]{
 "3D model üretimi başlatıldı: '{0}'. Hazır olunca sahneye eklenecek (Console'da bildirilir).",
 "3D model generation started: '{0}'. It will be added to the scene when ready (reported in the Console).",
 "已开始生成 3D 模型：'{0}'。完成后将添加到场景中（在控制台中报告）。",
 "已開始產生 3D 模型：'{0}'。完成後將加入場景中（在主控台中報告）。" } },
 { "tool.listPlacedEmpty", new[]{
 "Sahnede Nova ile yerleştirilmiş asset yok.", "No assets placed by Nova in the scene.",
 "场景中没有 Nova 放置的资源。", "場景中沒有 Nova 放置的資源。" } },
 { "tool.listPlacedSummary", new[]{
 "Sahnede {0} yerleştirilmiş nesne, {1} farklı asset:\n{2}",
 "{0} placed objects in the scene, {1} distinct assets:\n{2}",
 "场景中有 {0} 个已放置物体，共 {1} 种不同资源：\n{2}",
 "場景中有 {0} 個已放置物體，共 {1} 種不同資源：\n{2}" } },
 { "tool.needMatchOrRole", new[]{
 "'match' (asset/nesne adı parçası) veya 'role' vermelisin.",
 "You must provide 'match' (part of an asset/object name) or 'role'.",
 "你必须提供 'match'（资源/物体名称的一部分）或 'role'。",
 "你必須提供 'match'（資源/物體名稱的一部分）或 'role'。" } },
 { "tool.noMatchingAsset", new[]{
 "Eşleşen asset bulunamadı (match='{0}', role='{1}'). Önce ListPlacedAssets ile bak.",
 "No matching asset found (match='{0}', role='{1}'). Check with ListPlacedAssets first.",
 "找不到匹配的资源（match='{0}'，role='{1}'）。请先使用 ListPlacedAssets 查看。",
 "找不到匹配的資源（match='{0}'，role='{1}'）。請先使用 ListPlacedAssets 查看。" } },
 { "tool.removedFromScene", new[]{
 "{0} nesne sahneden kaldırıldı (Ctrl+Z geri alır).",
 "{0} objects removed from the scene (Ctrl+Z undoes it).",
 "已从场景中移除 {0} 个物体（Ctrl+Z 可撤销）。",
 "已從場景中移除 {0} 個物體（Ctrl+Z 可復原）。" } },
 { "tool.unknownBiome", new[]{
 "Bilinmeyen biome '{0}'. Geçerli: {1}", "Unknown biome '{0}'. Valid: {1}",
 "未知的生物群系 '{0}'。有效值：{1}", "未知的生態域 '{0}'。有效值：{1}" } },
 { "tool.terrainBuilding", new[]{
 "Arazi kuruluyor: {0} · {1} m · yoğunluk {2:0.00} (ırmak={3}, göl={4}). Üretim arka planda; Console'da ilerlemeyi görürsün.",
 "Building terrain: {0} · {1} m · density {2:0.00} (river={3}, lake={4}). Generation runs in the background; watch progress in the Console.",
 "正在生成地形：{0} · {1} 米 · 密度 {2:0.00}（河流={3}，湖泊={4}）。生成在后台进行；请在控制台查看进度。",
 "正在產生地形：{0} · {1} 公尺 · 密度 {2:0.00}（河流={3}，湖泊={4}）。產生在背景進行；請在主控台查看進度。" } },
 { "tool.decorPromptRequired", new[]{
 "prompt gerekli (ör. 'kamp alanı', 'çitli çiçek bahçesi').",
 "prompt is required (e.g. 'campsite', 'fenced flower garden').",
 "需要 prompt（例如'营地'、'带围栏的花园'）。",
 "需要 prompt（例如「營地」、「帶圍欄的花園」）。" } },
 { "tool.decorStarted", new[]{
 "Dekorasyon başlatıldı: '{0}' (yarıçap {1:0} m — seçili nesnenin ya da SceneView bakışının çevresi). Yerleşim arka planda sürüyor; ilerleme Console'da. Bittiğinde tek Ctrl+Z ile geri alınabilir.",
 "Decoration started: '{0}' (radius {1:0} m — around the selected object or the SceneView's focal point). Placement continues in the background; watch progress in the Console. When done, a single Ctrl+Z undoes it.",
 "已开始装饰：'{0}'（半径 {1:0} 米——围绕选中物体或 SceneView 视角中心）。放置在后台继续进行；请在控制台查看进度。完成后一次 Ctrl+Z 即可撤销。",
 "已開始裝飾：'{0}'（半徑 {1:0} 公尺——圍繞選中物體或 SceneView 視角中心）。放置在背景繼續進行；請在主控台查看進度。完成後一次 Ctrl+Z 即可復原。" } },

 // ---- Sahne Sağlık (SceneHealth) ----
 { "health.scanTitle", new[]{ "Sahne taraması: {0}", "Scene scan: {0}", " 场景扫描：{0}", " 場景掃描：{0}" } },
 { "health.missingScripts", new[]{ "• Kayıp script: {0}{1}", "• Missing scripts: {0}{1}", "• 缺失脚本：{0}{1}", "• 缺失指令碼：{0}{1}" } },
 { "health.missingMaterials", new[]{ "• Kayıp materyal: {0}{1}", "• Missing materials: {0}{1}", "• 缺失材质：{0}{1}", "• 缺失材質：{0}{1}" } },
 { "health.pinkHint", new[]{ " ⚠ (pembe görünür)", " ⚠ (appears pink)", " ⚠（显示为粉色）", " ⚠（顯示為粉色）" } },
 { "health.suspiciousScale", new[]{ "• Şüpheli ölçek: {0}{1}", "• Suspicious scale: {0}{1}", "• 可疑缩放：{0}{1}", "• 可疑縮放：{0}{1}" } },
 { "health.zeroCollider", new[]{ "• Sıfır collider: {0}{1}", "• Zero-size colliders: {0}{1}", "• 零尺寸碰撞体：{0}{1}", "• 零尺寸碰撞體：{0}{1}" } },
 { "health.farFromOrigin", new[]{ "• Origin'den uzak: {0}{1}", "• Far from origin: {0}{1}", "• 远离原点：{0}{1}", "• 遠離原點：{0}{1}" } },
 { "health.colorlessModels", new[]{ "• Renksiz (beyaz) model: {0}{1}", "• Colorless (white) models: {0}{1}", "• 无色（白色）模型：{0}{1}", "• 無色（白色）模型：{0}{1}" } },
 { "health.colorlessHint", new[]{ " ⚠ ({0} farklı asset)", " ⚠ ({0} different assets)", " ⚠（{0} 种不同资源）", " ⚠（{0} 種不同資源）" } },
 { "health.offTerrain", new[]{ "• Arazi dışında obje: {0}{1}", "• Objects off-terrain: {0}{1}", "• 地形外的物体：{0}{1}", "• 地形外的物體：{0}{1}" } },
 { "health.floating", new[]{ "• Havada asılı obje: {0}{1}", "• Floating objects: {0}{1}", "• 悬浮的物体：{0}{1}", "• 懸浮的物體：{0}{1}" } },
 { "health.floatingHint", new[]{ " ⚠ (zemine indirilebilir)", " ⚠ (can be lowered to ground)", " ⚠（可放置到地面）", " ⚠（可放置到地面）" } },
 { "health.nullMesh", new[]{ "• Kayıp mesh (görünmez): {0}{1}", "• Missing mesh (invisible): {0}{1}", "• 缺失网格（不可见）：{0}{1}", "• 缺失網格（不可見）：{0}{1}" } },
 { "health.emptyGO", new[]{ "• Boş nesne (dağınıklık): {0}{1}", "• Empty objects (clutter): {0}{1}", "• 空物体（杂乱）：{0}{1}", "• 空物件（雜亂）：{0}{1}" } },
 { "health.multiAudio", new[]{ "• Aktif AudioListener: {0}{1}", "• Active AudioListeners: {0}{1}", "• 活动的 AudioListener：{0}{1}", "• 作用中的 AudioListener：{0}{1}" } },
 { "health.bigTextures", new[]{ "• Dev doku (>2048px): {0}{1}", "• Oversized textures (>2048px): {0}{1}", "• 超大纹理（>2048px）：{0}{1}", "• 超大紋理（>2048px）：{0}{1}" } },
 { "health.fixAudio", new[]{ "• {0} fazla AudioListener kapatılacak", "• {0} extra AudioListeners will be disabled", "• 将禁用 {0} 个多余的 AudioListener", "• 將停用 {0} 個多餘的 AudioListener" } },
 { "health.fixTextures", new[]{ "• {0} dev doku 2048px'e indirilecek", "• {0} oversized textures will be capped to 2048px", "• 将把 {0} 个超大纹理限制到 2048px", "• 將把 {0} 個超大紋理限制到 2048px" } },
 { "health.repairedExtra", new[]{ " · {0} ses kapatıldı · {1} doku küçültüldü", " · {0} audio disabled · {1} textures shrunk", " · 禁用 {0} 个音频 · 缩小 {1} 个纹理", " · 停用 {0} 個音訊 · 縮小 {1} 個紋理" } },
 { "health.lightCamera", new[]{ "• Işık: {0} · Kamera: {1}", "• Light: {0} · Camera: {1}", "• 光源：{0} · 摄像机：{1}", "• 光源：{0} · 攝影機：{1}" } },
 { "health.present", new[]{ "var ✓", "present ✓", "存在 ✓", "存在 ✓" } },
 { "health.missingDark", new[]{ "YOK ⚠ (sahne karanlık)", "MISSING ⚠ (scene is dark)", "缺失 ⚠（场景昏暗）", "缺失 ⚠（場景昏暗）" } },
 { "health.missing", new[]{ "YOK ⚠", "MISSING ⚠", "缺失 ⚠", "缺失 ⚠" } },
 { "health.rendererTris", new[]{ "• {0} renderer · ~{1} üçgen{2}", "• {0} renderers · ~{1} triangles{2}", "• {0} 个渲染器 · 约 {1} 个三角形{2}", "• {0} 個渲染器 · 約 {1} 個三角形{2}" } },
 { "health.highForMobile", new[]{ " ⚠ (mobil için yüksek)", " ⚠ (high for mobile)", " ⚠（对移动端较高）", " ⚠（對行動裝置較高）" } },
 { "health.timeFooter", new[]{ "Zaman: {0} · her tarama sahneyi baştan okur.", "Time: {0} · every scan reads the scene from scratch.", "时间：{0} · 每次扫描都会重新读取场景。", "時間：{0} · 每次掃描都會重新讀取場景。" } },
 { "health.detailsInConsole", new[]{ "Detaylar Console'da (satıra tıkla → obje seçilir).", "Details are in the Console (click a line to select the object).", "详情见控制台（点击一行可选中对象）。", "詳情見主控台（點擊一行可選中物件）。" } },
 { "health.fixMissingScripts", new[]{ "• {0} kayıp script temizlenecek", "• {0} missing scripts will be cleaned up", "• 将清理 {0} 个缺失脚本", "• 將清理 {0} 個缺失指令碼" } },
 { "health.fixColorless", new[]{ "• {0} beyaz model vertex renginden boyanacak", "• {0} white models will be tinted from vertex color", "• 将为 {0} 个白色模型着色", "• 將為 {0} 個白色模型著色" } },
 { "health.fixColliders", new[]{ "• {0} bozuk collider kaldırılacak", "• {0} broken colliders will be removed", "• 将移除 {0} 个损坏的碰撞体", "• 將移除 {0} 個損壞的碰撞體" } },
 { "health.fixOffTerrain", new[]{ "• {0} arazi dışı obje sahneden kaldırılacak", "• {0} off-terrain objects will be removed from the scene", "• 将从场景中移除 {0} 个地形外物体", "• 將從場景中移除 {0} 個地形外物體" } },
 { "health.fixFloating", new[]{ "• {0} havada asılı obje zemine indirilecek", "• {0} floating objects will be lowered to the ground", "• 将把 {0} 个悬浮物体降至地面", "• 將把 {0} 個懸浮物體降至地面" } },
 { "health.repairedSummary", new[]{
 "Onarıldı → {0} script · {1} renk · {2} collider · {3} yabancı obje · {4} obje zemine indirildi.",
 "Repaired → {0} scripts · {1} colors · {2} colliders · {3} stray objects · {4} objects lowered to ground.",
 " 已修复 → {0} 个脚本 · {1} 个颜色 · {2} 个碰撞体 · {3} 个异物 · {4} 个物体已降至地面。",
 " 已修復 → {0} 個指令碼 · {1} 個顏色 · {2} 個碰撞體 · {3} 個異物 · {4} 個物體已降至地面。" } },

 // ---- Dünya inşa/keşif genel durumları ----
 { "world.status.buildingType", new[]{ "{0} kuruluyor...", "Building {0}...", "正在生成 {0}…", "正在產生 {0}…" } },
 { "world.status.mapReadyExplore", new[]{ "{0} 'Dünyada gez (FPS)' ile gir.", "{0} Enter with 'Walk (FPS)'.", "{0}使用「漫游（FPS）」进入。", "{0}使用「漫遊（FPS）」進入。" } },
 { "world.status.mapBuildFailed", new[]{ "Harita kurulamadı: {0}", "Map build failed: {0}", "地图生成失败：{0}", "地圖產生失敗：{0}" } },
 { "world.status.cityBuildFailed", new[]{ "Şehir kurulamadı: {0}", "City build failed: {0}", "城市建造失败：{0}", "城市建造失敗：{0}" } },
 { "world.status.decorBuildFailed", new[]{ "Dekor kurulamadı: {0}", "Decoration build failed: {0}", "装饰生成失败：{0}", "裝飾產生失敗：{0}" } },
 { "world.status.alreadyBuilding", new[]{ "Zaten bir harita kuruluyor.", "A map is already being built.", "已经有一张地图正在生成。", "已經有一張地圖正在產生。" } },
 { "world.status.alreadyBuildingCity", new[]{ "Zaten bir şehir kuruluyor — bitmesini bekle.", "A city is already being built — wait for it to finish.", "已经有一座城市正在建造——请等待完成。", "已經有一座城市正在建造——請等待完成。" } },
 { "world.status.gltfastMissing", new[]{ "glTFast kurulu değil — import yapılamaz.", "glTFast is not installed — import is not possible.", "未安装 glTFast——无法导入。", "未安裝 glTFast——無法匯入。" } },
 { "world.status.gltfastMissingDecor", new[]{ "glTFast kurulu değil — dekor import edilemez.", "glTFast is not installed — decoration cannot be imported.", "未安装 glTFast——无法导入装饰。", "未安裝 glTFast——無法匯入裝飾。" } },

 // ---- Sohbet ek/menü aksiyonları ----
 { "menu.addImage", new[]{ "Görsel ekle…", "Add image…", "添加图片…", "新增圖片…" } },
 { "menu.addDoc", new[]{ "Belge ekle… (.cs .txt .md .json .log)", "Add document… (.cs .txt .md .json .log)", "添加文档…（.cs .txt .md .json .log）", "新增文件…（.cs .txt .md .json .log）" } },
 { "menu.sceneShot", new[]{ "Scene ekran görüntüsü al", "Capture Scene screenshot", "截取场景截图", "擷取場景截圖" } },
 { "menu.pasteImage", new[]{ "Panodan görsel yapıştır", "Paste image from clipboard", "从剪贴板粘贴图片", "從剪貼簿貼上圖片" } },
 { "menu.scanScene", new[]{ "Sahneyi tara (sağlık raporu)", "Scan scene (health report)", "扫描场景（健康报告）", "掃描場景（健康報告）" } },
 { "menu.fixConsole", new[]{ "Konsol hatalarını düzelt", "Fix console errors", "修复控制台错误", "修復控制台錯誤" } },
 { "menu.presetPlayer", new[]{ "Hazır görev/Karakter kontrolcüsü yaz", "Preset task/Write character controller", "预设任务/编写角色控制器", "預設任務/編寫角色控制器" } },
 { "menu.presetHealth", new[]{ "Hazır görev/Can (health) sistemi yaz", "Preset task/Write health system", "预设任务/编写生命值系统", "預設任務/編寫生命值系統" } },
 { "menu.presetInventory", new[]{ "Hazır görev/Envanter sistemi yaz", "Preset task/Write inventory system", "预设任务/编写物品栏系统", "預設任務/編寫物品欄系統" } },
 { "menu.clearChat", new[]{ "Sohbeti temizle", "Clear chat", "清除对话", "清除對話" } },
 { "attach.docAdded", new[]{ "{0} eklendi ({1} karakter).", "{0} added ({1} characters).", "已添加 {0}（{1} 字符）。", "已新增 {0}（{1} 個字元）。" } },
 { "attach.docTruncated", new[]{ "\n… (belge kısaltıldı)", "\n… (document truncated)", "\n…（文档已截断）", "\n…（文件已截斷）" } },
 { "attach.zoomHint", new[]{ " — büyütmek için tıkla", " — click to enlarge", " — 点击放大", " — 點擊放大" } },
 { "attach.imagesCount", new[]{ " {0} görsel", " {0} image(s)", " {0} 张图片", " {0} 張圖片" } },
 { "attach.docsCount", new[]{ " {0} belge", " {0} document(s)", " {0} 个文档", " {0} 個文件" } },
 { "chat.msg.unknown", new[]{ "bilinmeyen", "unknown", "未知", "未知" } },

 // ---- 3D model üretim akışı (sohbetten) ----
 { "gen3d.emptyPrompt", new[]{ "prompt boş — model üretilemedi.", "prompt is empty — model could not be generated.", "prompt 为空——无法生成模型。", "prompt 為空——無法產生模型。" } },
 { "dialog.genModel3D.body", new[]{
 "Nova şunu üretecek:\n\n{0}\n\n(Üretim ücretli olabilir.)",
 "Nova will generate:\n\n{0}\n\n(Generation may incur cost.)",
 "Nova 将生成：\n\n{0}\n\n（生成可能产生费用。）",
 "Nova 將產生：\n\n{0}\n\n（產生可能產生費用。）" } },
 { "dialog.generate", new[]{ "Üret", "Generate", "生成", "產生" } },
 { "gen3d.userRejected", new[]{ "Kullanıcı 3D model üretimini reddetti.", "User rejected 3D model generation.", "用户拒绝了 3D 模型生成。", "使用者拒絕了 3D 模型產生。" } },
 { "gen3d.inProgress", new[]{
 "3D model üretiliyor: {0}\nBu genelde 15–60 saniye sürer, bekle…",
 "Generating 3D model: {0}\nThis usually takes 15–60 seconds, please wait…",
 " 正在生成 3D 模型：{0}\n通常需要 15–60 秒，请稍候…",
 " 正在產生 3D 模型：{0}\n通常需要 15–60 秒，請稍候…" } },
 { "gen3d.statusGenerating", new[]{ "3D model üretiliyor…", "Generating 3D model…", "正在生成 3D 模型…", "正在產生 3D 模型…" } },
 { "gen3d.ready", new[]{ "3D model hazır: {0} ({1:0} sn)", "3D model ready: {0} ({1:0} s)", "3D 模型已就绪：{0}（{1:0} 秒）", "3D 模型已就緒：{0}（{1:0} 秒）" } },
 { "gen3d.failed", new[]{ "⨯ 3D model üretilemedi: {0}", "⨯ 3D model generation failed: {0}", "⨯ 3D 模型生成失败：{0}", "⨯ 3D 模型產生失敗：{0}" } },
 { "gen3d.readyToolResult", new[]{
 "3D model üretildi ve '3D Stüdyo' sekmesinde önizlemede: '{0}'. Kullanıcı oradan inceleyip 'Sahneye ekle' ile yerleştirebilir.",
 "The 3D model was generated and is previewed in the '3D Studio' tab: '{0}'. The user can review it there and place it with 'Add to scene'.",
 "3D 模型已生成，可在「3D 工作室」标签中预览：'{0}'。用户可在那里查看并通过「添加到场景」放置。",
 "3D 模型已產生，可在「3D 工作室」標籤中預覽：'{0}'。使用者可在那裡查看並透過「加入場景」放置。" } },
 { "gen3d.failedToolResult", new[]{ "3D model üretilemedi: {0}", "3D model generation failed: {0}", "3D 模型生成失败：{0}", "3D 模型產生失敗：{0}" } },

 // ---- 3D Stüdyo sekmesi ----
 { "studio.promptForImage", new[]{ "Görsel için prompt gir.", "Enter a prompt for the image.", "请输入图片的 prompt。", "請輸入圖片的 prompt。" } },
 { "dialog.genImage2.body", new[]{
 "Prompt'tan bir görsel üretir (küçük kredi). Sonra onu 3D'ye çevirebilirsin. Devam?",
 "Generates an image from the prompt (small credit cost). You can then convert it to 3D. Continue?",
 "根据 prompt 生成图片（少量费用）。之后可以将其转换为 3D。是否继续？",
 "根據 prompt 產生圖片（少量費用）。之後可以將其轉換為 3D。是否繼續？" } },
 { "studio.generatingImage", new[]{ "Görsel üretiliyor...", "Generating image...", "正在生成图片…", "正在產生圖片…" } },
 { "studio.imageReady", new[]{ "Görsel üretildi ✓ — Üret'e bas (görselden 3D).", "Image ready ✓ — press Generate (image to 3D).", "图片已生成 ✓ — 按「生成」（图片转 3D）。", "圖片已產生 ✓ — 按「產生」（圖片轉 3D）。" } },
 { "studio.needImage", new[]{ "Önce görsel yükle/üret ya da URL gir.", "Upload/generate an image first, or enter a URL.", "请先上传/生成图片或输入网址。", "請先上傳/產生圖片或輸入網址。" } },
 { "studio.enterPrompt", new[]{ "Prompt gir.", "Enter a prompt.", "请输入 prompt。", "請輸入 prompt。" } },
 { "studio.generating", new[]{ "Üretiliyor... (10-30 sn)", "Generating... (10-30 s)", "生成中…（10-30 秒）", "產生中…（10-30 秒）" } },
 { "studio.imageUploaded", new[]{ "Görsel yüklendi ✓ — Üret'e bas (görselden 3D).", "Image uploaded ✓ — press Generate (image to 3D).", "图片已上传 ✓ — 按「生成」（图片转 3D）。", "圖片已上傳 ✓ — 按「產生」（圖片轉 3D）。" } },
 { "studio.charAdded", new[]{ "✓ karakter (yürür) → sahnede", "✓ character (walking) → in scene", "✓ 角色（行走）→ 已在场景中", "✓ 角色（行走）→ 已在場景中" } },
 { "studio.charAddedStatus", new[]{ "Karakter eklendi — Play'e bas, yürür.", "Character added — press Play, it walks.", "角色已添加——按 Play 即可行走。", "角色已新增——按 Play 即可行走。" } },
 { "studio.savingAndPlacing", new[]{ "Projeye kaydediliyor + sahneye ekleniyor...", "Saving to project + adding to scene...", "正在保存到项目并添加到场景…", "正在儲存到專案並加入場景…" } },
 { "studio.persistentAdded", new[]{ "✓ kalıcı model → sahnede", "✓ persistent model → in scene", "✓ 持久模型 → 已在场景中", "✓ 持久模型 → 已在場景中" } },
 { "studio.addedTemp", new[]{ "✓ {0} → sahnede (geçici)", "✓ {0} → in scene (temporary)", "✓ {0} → 已在场景中（临时）", "✓ {0} → 已在場景中（臨時）" } },
 { "studio.addedTempStatus", new[]{ "Eklendi (geçici): {0}", "Added (temporary): {0}", "已添加（临时）：{0}", "已新增（臨時）：{0}" } },
 { "studio.cleared", new[]{ "Temizlendi.", "Cleared.", "已清除。", "已清除。" } },
 { "studio.statsPlaceholder", new[]{ "Model üret → bilgiler burada", "Generate a model → info appears here", "生成模型 → 信息将显示在此", "產生模型 → 資訊將顯示在此" } },
 { "studio.needModelFirst", new[]{ "Önce bir model üret.", "Generate a model first.", "请先生成一个模型。", "請先產生一個模型。" } },
 { "studio.noModelUrl", new[]{ "Model URL yok (yeniden üret).", "No model URL (regenerate).", "没有模型网址（请重新生成）。", "沒有模型網址（請重新產生）。" } },
 { "dialog.rig2.body", new[]{
 "Bu modeli rigleyip yürüme/koşma animasyonu ekler. İnsansı/humanoid model olmalı. ~2 kredi harcar. Devam?",
 "Rigs this model and adds walk/run animation. Must be a humanoid model. Costs ~2 credits. Continue?",
 "为此模型绑定骨骼并添加行走/跑步动画。必须是人形模型。约消耗 2 个额度。是否继续？",
 "為此模型綁定骨骼並新增行走/跑步動畫。必須是人形模型。約消耗 2 個額度。是否繼續？" } },
 { "dialog.rigConfirm", new[]{ "Rigle", "Rig", "绑定骨骼", "綁定骨骼" } },
 { "studio.rigging", new[]{ "Rigleniyor...", "Rigging...", "正在绑定骨骼…", "正在綁定骨骼…" } },
 { "studio.rigged", new[]{ "Riglendi ✓ — Sahneye ekle (yürür).", "Rigged ✓ — Add to scene (it walks).", "已绑定骨骼 ✓ — 添加到场景（可行走）。", "已綁定骨骼 ✓ — 加入場景（可行走）。" } },
 { "studio.errorPrefix", new[]{ "Hata: {0}", "Error: {0}", "错误：{0}", "錯誤：{0}" } },
 { "studio.stepEllipsis", new[]{ "{0}...", "{0}...", "{0}…", "{0}…" } },
 { "gen3d.stepInProgress", new[]{ "3D model üretiliyor: {0}\n{1}…", "Generating 3D model: {0}\n{1}…", " 正在生成 3D 模型：{0}\n{1}…", " 正在產生 3D 模型：{0}\n{1}…" } },
 { "gen3d.stepStatus", new[]{ "3D model: {0}…", "3D model: {0}…", "3D 模型：{0}…", "3D 模型：{0}…" } },
 { "studio.generatingOverlay", new[]{ "Üretiliyor · %{0}", "Generating · {0}%", "生成中 · {0}%", "產生中 · {0}%" } },
 { "studio.modelInfo", new[]{ "Model bilgisi", "Model info", "模型信息", "模型資訊" } },
 { "studio.size", new[]{ "Boyut", "Size", "尺寸", "尺寸" } },
 { "studio.lodNone", new[]{ "Yok (LOD0)", "None (LOD0)", "无（LOD0）", "無（LOD0）" } },
 { "studio.duration", new[]{ "Süre", "Duration", "耗时", "耗時" } },
 { "studio.durationVal", new[]{ "{0:0.#} sn", "{0:0.#} s", "{0:0.#} 秒", "{0:0.#} 秒" } },
 { "studio.rig", new[]{ "Rig", "Rig", "骨骼绑定", "骨骼綁定" } },
 { "studio.rigHumanoid", new[]{ "Humanoid ✓", "Humanoid ✓", "人形 ✓", "人形 ✓" } },
 { "studio.animation", new[]{ "Animasyon", "Animation", "动画", "動畫" } },
 { "studio.highForMobileHint", new[]{ "Mobil için yüksek — Düşük-poli üret", "High for mobile — generate low-poly", "对移动端较高——请生成低多边形版本", "對行動裝置較高——請產生低多邊形版本" } },
 { "code.apply", new[]{ "Uygula", "Apply", "应用", "套用" } },
 { "code.reject", new[]{ "Reddet", "Reject", "拒绝", "拒絕" } },

 // ---- Üretim adım etiketleri (3D Stüdyo overlay) ----
 { "step.promptSent", new[]{ "Prompt gönderildi", "Prompt sent", "已发送 Prompt", "已傳送 Prompt" } },
 { "step.modelBuilding", new[]{ "Model oluşturuluyor", "Building model", "正在生成模型", "正在產生模型" } },
 { "step.texturePrep", new[]{ "Texture hazırlanıyor", "Preparing texture", "正在准备贴图", "正在準備貼圖" } },
 { "step.unityImport", new[]{ "Unity'ye import", "Importing to Unity", "正在导入 Unity", "正在匯入 Unity" } },
 { "step.previewPrep", new[]{ "Önizleme hazırlanıyor", "Preparing preview", "正在准备预览", "正在準備預覽" } },
 { "step.modelSent", new[]{ "Model gönderildi", "Model sent", "已发送模型", "已傳送模型" } },
 { "step.rigging", new[]{ "Rigleniyor + animasyon", "Rigging + animation", "正在绑定骨骼 + 动画", "正在綁定骨骼 + 動畫" } },

 // ---- Dünya sekmesi: kurulum durumu ----
 { "world.status.typeBuilding", new[]{ "{0} kuruluyor...", "Building {0}...", "正在生成{0}…", "正在產生{0}…" } },
 { "tool.objectCreated", new[]{ "'{0}' oluşturuldu.", "'{0}' created.", "已创建 '{0}'。", "已建立 '{0}'。" } },

 // ---- Kalıcılık (TerrainPersistence) ----
 { "persist.noMap", new[]{ "Kaydedilecek harita yok — önce bir harita kur.", "No map to save — build a map first.", "没有可保存的地图——请先生成一张地图。", "沒有可儲存的地圖——請先產生一張地圖。" } },
 { "persist.savingTerrain", new[]{ "Kaydediliyor: arazi verisi...", "Saving: terrain data...", "正在保存：地形数据…", "正在儲存：地形資料…" } },
 { "persist.savingModels", new[]{ "Kaydediliyor: modeller ve malzemeler...", "Saving: models and materials...", "正在保存：模型和材质…", "正在儲存：模型和材質…" } },
 { "persist.sceneSaved", new[]{ " · sahne kaydedildi", " · scene saved", " · 场景已保存", " · 場景已儲存" } },
 { "persist.sceneNotSaved", new[]{ " · ⚠ SAHNE KAYDEDİLMEDİ (Ctrl+S)", " · ⚠ SCENE NOT SAVED (Ctrl+S)", " · ⚠ 场景未保存（Ctrl+S）", " · ⚠ 場景未儲存（Ctrl+S）" } },
 { "persist.saved", new[]{
 "Kaydedildi ✓ {0} · {1} mesh · {2} malzeme{3}", "Saved ✓ {0} · {1} meshes · {2} materials{3}",
 "已保存 ✓ {0} · {1} 个网格 · {2} 个材质{3}", "已儲存 ✓ {0} · {1} 個網格 · {2} 個材質{3}" } },
 { "persist.saveFailed", new[]{ "Kaydedilemedi: {0}", "Could not save: {0}", "无法保存：{0}", "無法儲存：{0}" } },


 // ---- Dekoratör (NovaDecorator) ----
 { "decor.planning", new[]{ "Dekor planlanıyor: {0}", "Planning decoration: {0}", " 正在规划装饰：{0}", " 正在規劃裝飾：{0}" } },
 { "decor.noAiPlan", new[]{ "⚠ Beyin plan veremedi — yerel tahmin: {0}", "⚠ Brain couldn't produce a plan — local guess: {0}", "⚠ AI 无法生成方案——使用本地推测：{0}", "⚠ AI 無法產生方案——使用本機推測：{0}" } },
 { "decor.alreadyRunning", new[]{ "Dekoratör zaten çalışıyor.", "Decorator is already running.", "装饰器已在运行。", "裝飾器已在執行。" } },
 { "decor.lookAtSceneView", new[]{ "SceneView'da dekor istediğin bölgeye bak, sonra tekrar dene.", "Look at the area you want to decorate in SceneView, then try again.", "请在 SceneView 中对准想要装饰的区域，然后重试。", "請在 SceneView 中對準想要裝飾的區域，然後重試。" } },
 { "decor.noSuitableRole", new[]{ "Katalogda uygun '{0}' yok — atlandı.", "No suitable '{0}' in the catalog — skipped.", "目录中没有合适的 '{0}'——已跳过。", "目錄中沒有合適的 '{0}'——已跳過。" } },
 { "decor.noSuitableAssets", new[]{ "⚠ Plan için katalogda uygun asset bulunamadı.", "⚠ No suitable assets found in the catalog for the plan.", "⚠ 目录中找不到符合方案的资源。", "⚠ 目錄中找不到符合方案的資源。" } },
 { "decor.nothingPlaced", new[]{
 "⚠ Hiç obje yerleşemedi — hedef noktanın altında zemin yok. SceneView'da arazinin üstüne bakıp tekrar dene.",
 "⚠ No objects could be placed — there's no ground under the target point. Look over the terrain in SceneView and try again.",
 "⚠ 没有物体能够放置——目标点下方没有地面。请在 SceneView 中对准地形上方后重试。",
 "⚠ 沒有物體能夠放置——目標點下方沒有地面。請在 SceneView 中對準地形上方後重試。" } },
 { "decor.ready", new[]{
 "Dekor hazır: {0} · {1} obje · {2} (Ctrl+Z tek adımda geri alır)",
 "Decoration ready: {0} · {1} object(s) · {2} (Ctrl+Z undoes it in one step)",
 "装饰完成：{0} · {1} 个物体 · {2}（Ctrl+Z 一步撤销）",
 "裝飾完成：{0} · {1} 個物體 · {2}（Ctrl+Z 一步復原）" } },
 { "decor.buildFailed", new[]{ "Dekor kurulamadı: {0}", "Decoration build failed: {0}", "装饰生成失败：{0}", "裝飾產生失敗：{0}" } },

 // ---- Arazi üretici (TerrainGen) ----
 { "terrain.shaping", new[]{ "Arazi şekillendiriliyor...", "Shaping terrain...", "正在塑造地形…", "正在塑造地形…" } },
 { "terrain.naturePalette", new[]{ "Doğa paleti hazırlanıyor (küratör beyin)...", "Preparing nature palette (curator brain)...", "正在准备自然元素调色板（AI 策展）…", "正在準備自然元素調色板（AI 策展）…" } },
 { "terrain.ready", new[]{
 "Harita hazır: {0} · {1:0} m · {2} doğa objesi · {3}.", "Map ready: {0} · {1:0} m · {2} nature object(s) · {3}.",
 "地图已就绪：{0} · {1:0} 米 · {2} 个自然物体 · {3}。", "地圖已就緒：{0} · {1:0} 公尺 · {2} 個自然物體 · {3}。" } },
 { "terrain.readyNoLib", new[]{
"Arazi kuruldu ({0} · {1:0} m) ama kütüphane hazır değildi — ağaç/kaya eklenmedi. Kütüphane inince 'Haritayı kur'a tekrar bas.",
"Terrain built ({0} · {1:0} m) but the library was not ready — no trees/rocks added. Press 'Build map' again once the download finishes.",
"地形已生成（{0} · {1:0} m），但素材库尚未就绪 —— 未添加树木/岩石。下载完成后请再次点击生成地图。",
"地形已生成（{0} · {1:0} m），但素材庫尚未就緒 —— 未新增樹木/岩石。下載完成後請再次點擊生成地圖。" } },
 { "terrain.readyNoGltf", new[]{
 "Harita hazır: {0} · {1:0} m (glTFast yok → bitki saçılamadı).", "Map ready: {0} · {1:0} m (glTFast missing → vegetation not scattered).",
 "地图已就绪：{0} · {1:0} 米（缺少 glTFast → 未能生成植被）。", "地圖已就緒：{0} · {1:0} 公尺（缺少 glTFast → 未能產生植被）。" } },
 { "terrain.importSkipped", new[]{ "Import atlandı ({0}): {1}", "Import skipped ({0}): {1}", "已跳过导入（{0}）：{1}", "已跳過匯入（{0}）：{1}" } },

 // ---- Şehir üretici (WorldBuilderAI) ----
 { "city.catalogReadError", new[]{ "Katalog okunamadı: {0}", "Could not read catalog: {0}", "无法读取目录：{0}", "無法讀取目錄：{0}" } },
 { "city.planReceived", new[]{ "Plan alındı ({0}).", "Plan received ({0}).", "已收到方案（{0}）。", "已收到方案（{0}）。" } },
 { "city.planUnparseable", new[]{ "Plan çözümlenemedi, yerel plana geçildi.", "Plan could not be parsed, switched to local plan.", "无法解析方案，已切换为本地方案。", "無法解析方案，已切換為本機方案。" } },
 { "city.serverUnreachable", new[]{ "Sunucuya ulaşılamadı ({0}), yerel plan kullanılıyor.", "Could not reach server ({0}), using local plan.", "无法连接服务器（{0}），使用本地方案。", "無法連線伺服器（{0}），使用本機方案。" } },
 { "city.noSuitableBuildings", new[]{ "Katalogda uygun bina bulunamadı.", "No suitable buildings found in the catalog.", "目录中找不到合适的建筑。", "目錄中找不到合適的建築。" } },
 { "city.styleFamilySummary", new[]{
 "Stil: {0} · Aile: {1} · {2} bina adayı", "Style: {0} · Family: {1} · {2} building candidate(s)",
 "风格：{0} · 系列：{1} · {2} 个候选建筑", "風格：{0} · 系列：{1} · {2} 個候選建築" } },
 { "city.mixedFamilyCount", new[]{ "karma ({0} aile)", "mixed ({0} families)", "混合（{0} 个系列）", "混合（{0} 個系列）" } },
 { "city.mixed", new[]{ "karma", "mixed", "混合", "混合" } },
 { "city.preparingPalette", new[]{ "Asset paleti hazırlanıyor...", "Preparing asset palette...", "正在准备资源调色板…", "正在準備資源調色板…" } },
 { "city.buildingImportFailed", new[]{ "Bina import edilemedi.", "Building import failed.", "建筑导入失败。", "建築匯入失敗。" } },
 { "city.ready", new[]{
 "Şehir hazır: {0} bina · {1} ağaç · {2}.", "City ready: {0} building(s) · {1} tree(s) · {2}.",
 "城市已建成：{0} 座建筑 · {1} 棵树 · {2}。", "城市已建成：{0} 座建築 · {1} 棵樹 · {2}。" } },
 { "city.organicPlanning", new[]{
 "Stil: {0} · Tema: {1} · Aile: {2} · organik plan hesaplanıyor...",
 "Style: {0} · Theme: {1} · Family: {2} · computing organic plan...",
 "风格：{0} · 主题：{1} · 系列：{2} · 正在计算有机布局…",
 "風格：{0} · 主題：{1} · 系列：{2} · 正在計算有機佈局…" } },
 { "city.roadNetwork", new[]{
 "Yol ağı: {0} yol · {1} lot. Beyin asset seti seçiyor...",
 "Road network: {0} road(s) · {1} lot(s). Brain is choosing the asset set...",
 "道路网络：{0} 条道路 · {1} 块地块。AI 正在选择资源集…",
 "道路網路：{0} 條道路 · {1} 塊地塊。AI 正在選擇資源集…" } },
 { "city.importingSelectedSet", new[]{ "Seçilen set import ediliyor...", "Importing the selected set...", "正在导入所选资源集…", "正在匯入所選資源集…" } },
 { "city.organicReady", new[]{
 "Organik şehir hazır: {0} yol · {1} bina · {2} park · {3}.", "Organic city ready: {0} road(s) · {1} building(s) · {2} park(s) · {3}.",
 "有机城市已建成：{0} 条道路 · {1} 座建筑 · {2} 座公园 · {3}。", "有機城市已建成：{0} 條道路 · {1} 座建築 · {2} 座公園 · {3}。" } },
 { "city.curatorBackendOld", new[]{
 "⚠ BEYİN DEVRE DIŞI: backend güncel değil (curate 404) — varsayılan set kullanılıyor!",
 "⚠ BRAIN DISABLED: backend is outdated (curate 404) — using default set!",
 "⚠ AI 已禁用：后端过旧（curate 404）——使用默认资源集！",
 "⚠ AI 已停用：後端過舊（curate 404）——使用預設資源集！" } },
 { "city.curatorPicked", new[]{ "Beyin seti seçti (AI) ✓", "Brain chose the set (AI) ✓", "AI 已选定资源集 ✓", "AI 已選定資源集 ✓" } },
 { "city.curatorFailed", new[]{
 "⚠ Beyin seçemedi ({0}) — varsayılan set. Backend terminaline bak.",
 "⚠ Brain couldn't choose ({0}) — default set. Check the backend terminal.",
 "⚠ AI 无法选择（{0}）——使用默认资源集。请查看后端终端。",
 "⚠ AI 無法選擇（{0}）——使用預設資源集。請查看後端終端機。" } },
 { "city.curatorUnreachable", new[]{
 "⚠ BEYİN DEVRE DIŞI ({0}) — varsayılan set!", "⚠ BRAIN DISABLED ({0}) — default set!",
 "⚠ AI 已禁用（{0}）——使用默认资源集！", "⚠ AI 已停用（{0}）——使用預設資源集！" } },

 // ---- ModelGenerator (3D üretim/rig/görsel hattı) ----
 { "gen.buildFailed", new[]{ "Üretim başarısız", "Generation failed", "生成失败", "產生失敗" } },
 { "gen.error3d", new[]{ "3D üretim hatası: {0}", "3D generation error: {0}", "3D 生成错误：{0}", "3D 產生錯誤：{0}" } },
 { "gen.noGlbUrl", new[]{ "GLB URL alınamadı", "Could not get GLB URL", "无法获取 GLB 网址", "無法取得 GLB 網址" } },
 { "gen.requestError3d", new[]{ "3D istek hatası: {0}", "3D request error: {0}", "3D 请求错误：{0}", "3D 請求錯誤：{0}" } },
 { "gen.rigFailed", new[]{ "Rigleme başarısız", "Rigging failed", "绑定骨骼失败", "綁定骨骼失敗" } },
 { "gen.rigError", new[]{ "Rigleme hatası: {0}", "Rigging error: {0}", "绑定骨骼错误：{0}", "綁定骨骼錯誤：{0}" } },
 { "gen.noRiggedUrl", new[]{ "Riglenmiş model URL alınamadı", "Could not get rigged model URL", "无法获取绑定骨骼后的模型网址", "無法取得綁定骨骼後的模型網址" } },
 { "gen.rigRequestError", new[]{ "Rig istek hatası: {0}", "Rig request error: {0}", "绑定骨骼请求错误：{0}", "綁定骨骼請求錯誤：{0}" } },
 { "gen.imageError", new[]{ "Görsel üretim hatası: {0}", "Image generation error: {0}", "图片生成错误：{0}", "圖片產生錯誤：{0}" } },
 { "gen.noImageUrl", new[]{ "Görsel URL alınamadı", "Could not get image URL", "无法获取图片网址", "無法取得圖片網址" } },
 { "gen.imageRequestError", new[]{ "Görsel istek hatası: {0}", "Image request error: {0}", "图片请求错误：{0}", "圖片請求錯誤：{0}" } },
 { "gen.importingToUnity", new[]{ "Unity'ye import ediliyor", "Importing to Unity", "正在导入 Unity", "正在匯入 Unity" } },
 { "gen.glbLoadFailed", new[]{ "GLB yüklenemedi", "Could not load GLB", "无法加载 GLB", "無法載入 GLB" } },
 { "gen.glbLoadFailedUrl", new[]{ "GLB yüklenemedi: {0}", "Could not load GLB: {0}", "无法加载 GLB：{0}", "無法載入 GLB：{0}" } },
 { "gen.instantiateFailed", new[]{ "Model örneklenemedi", "Could not instantiate model", "无法实例化模型", "無法實例化模型" } },
 { "gen.previewReady", new[]{
 "Önizleme hazır — çevirip incele, beğenirsen sahneye ekle.", "Preview ready — rotate to inspect, add to scene if you like it.",
 "预览已就绪——旋转查看，满意的话添加到场景。", "預覽已就緒——旋轉查看，滿意的話加入場景。" } },
 { "gen.importError", new[]{ "Import hatası", "Import error", "导入错误", "匯入錯誤" } },
 { "gen.importErrorMsg", new[]{ "Import hatası: {0}", "Import error: {0}", "导入错误：{0}", "匯入錯誤：{0}" } },
 { "gen.placedInScene", new[]{ "3D model sahneye eklendi: {0}", "3D model added to scene: {0}", "3D 模型已添加到场景：{0}", "3D 模型已加入場景：{0}" } },
 { "gen.riggedGlbLoadFailed", new[]{ "Riglenmiş GLB yüklenemedi", "Could not load rigged GLB", "无法加载绑定骨骼后的 GLB", "無法載入綁定骨骼後的 GLB" } },
 { "gen.riggedInstantiateFailed", new[]{ "Riglenmiş model örneklenemedi", "Could not instantiate rigged model", "无法实例化绑定骨骼后的模型", "無法實例化綁定骨骼後的模型" } },
 { "gen.riggedReady", new[]{
 "Riglendi ✓ — Sahneye ekle dersen yürüyen karakter eklenir.", "Rigged ✓ — Add to scene to place a walking character.",
 "已绑定骨骼 ✓ — 添加到场景即可放置可行走的角色。", "已綁定骨骼 ✓ — 加入場景即可放置可行走的角色。" } },
 { "gen.rigImportError", new[]{ "Rig import hatası", "Rig import error", "骨骼绑定导入错误", "骨骼綁定匯入錯誤" } },
 { "gen.rigImportErrorMsg", new[]{ "Rig import hatası: {0}", "Rig import error: {0}", "骨骼绑定导入错误：{0}", "骨骼綁定匯入錯誤：{0}" } },
 { "gen.animGlbLoadFailed", new[]{ "Animasyonlu GLB yüklenemedi", "Could not load animated GLB", "无法加载带动画的 GLB", "無法載入帶動畫的 GLB" } },
 { "gen.charPlaced", new[]{ "Karakter sahneye eklendi — Play'e bas, yürür.", "Character added to scene — press Play, it walks.", "角色已添加到场景——按 Play 即可行走。", "角色已加入場景——按 Play 即可行走。" } },
 { "gen.charInstantiateFailed", new[]{ "Karakter örneklenemedi", "Could not instantiate character", "无法实例化角色", "無法實例化角色" } },
 { "gen.animImportError", new[]{ "Animasyon import hatası: {0}", "Animation import error: {0}", "动画导入错误：{0}", "動畫匯入錯誤：{0}" } },
 { "gen.gltfastMissingStep", new[]{ "glTFast kurulu değil", "glTFast is not installed", "未安装 glTFast", "未安裝 glTFast" } },
 { "gen.gltfastMissingWithUrl", new[]{
 "glTFast kurulu değil (Package Manager → com.unity.cloud.gltfast). GLB: {0}",
 "glTFast is not installed (Package Manager → com.unity.cloud.gltfast). GLB: {0}",
 "未安装 glTFast（包管理器 → com.unity.cloud.gltfast）。GLB：{0}",
 "未安裝 glTFast（套件管理員 → com.unity.cloud.gltfast）。GLB：{0}" } },
 { "gen.gltfastMissingRigged", new[]{
 "glTFast kurulu değil. Riglenmiş GLB: {0}", "glTFast is not installed. Rigged GLB: {0}",
 "未安装 glTFast。绑定骨骼后的 GLB：{0}", "未安裝 glTFast。綁定骨骼後的 GLB：{0}" } },
 { "gen.gltfastMissingAnim", new[]{
 "glTFast kurulu değil. Animasyonlu GLB: {0}", "glTFast is not installed. Animated GLB: {0}",
 "未安装 glTFast。带动画的 GLB：{0}", "未安裝 glTFast。帶動畫的 GLB：{0}" } },

 // ---- MaterialMaker ----
 { "mat.generatingTexture", new[]{ "Texture üretiliyor (fal)...", "Generating texture (fal)...", "正在生成贴图（fal）…", "正在產生貼圖（fal）…" } },
 { "mat.genError", new[]{ "Üretim hatası: {0}", "Generation error: {0}", "生成错误：{0}", "產生錯誤：{0}" } },
 { "mat.noImageUrl", new[]{ "Görsel URL alınamadı", "Could not get image URL", "无法获取图片网址", "無法取得圖片網址" } },
 { "mat.requestError", new[]{ "İstek hatası: {0}", "Request error: {0}", "请求错误：{0}", "請求錯誤：{0}" } },
 { "mat.downloadError", new[]{ "İndirme hatası: {0}", "Download error: {0}", "下载错误：{0}", "下載錯誤：{0}" } },
 { "mat.applied", new[]{
 "Uygulandı ({0} yüzey): {1}. Beğenmezsen '↩ Geri al' veya Ctrl+Z.",
 "Applied ({0} surface(s)): {1}. If you don't like it, use '↩ Revert' or Ctrl+Z.",
 "已应用（{0} 个表面）：{1}。如果不满意，使用「↩ 撤销」或 Ctrl+Z。",
 "已套用（{0} 個表面）：{1}。如果不滿意，使用「↩ 復原」或 Ctrl+Z。" } },
 { "mat.saveApplyError", new[]{ "Kaydetme/uygulama hatası: {0}", "Save/apply error: {0}", "保存/应用错误：{0}", "儲存/套用錯誤：{0}" } },
 { "mat.nothingToRevert", new[]{ "Geri alınacak bir malzeme değişikliği yok.", "No material change to revert.", "没有可撤销的材质更改。", "沒有可復原的材質變更。" } },
 { "mat.reverted", new[]{ "Geri alındı: {0} yüzey orijinaline döndü.", "Reverted: {0} surface(s) restored to original.", "已撤销：{0} 个表面已恢复原状。", "已復原：{0} 個表面已恢復原狀。" } },

 // ---- Dünyada gezinti (WorldExplorer) ----
 { "explorer.playerAdded", new[]{
 "Oyuncu eklendi ({0:0.#}, {1:0.#}, {2:0.#}). WASD gez · fare bak · Space zıpla · Esc imleç.",
 "Player added ({0:0.#}, {1:0.#}, {2:0.#}). WASD to move · mouse to look · Space to jump · Esc for cursor.",
 "已添加玩家（{0:0.#}, {1:0.#}, {2:0.#}）。WASD 移动 · 鼠标视角 · Space 跳跃 · Esc 显示光标。",
 "已新增玩家（{0:0.#}, {1:0.#}, {2:0.#}）。WASD 移動 · 滑鼠視角 · Space 跳躍 · Esc 顯示游標。" } },

 // ---- Kalıcı model kaydetme (AssetSaver) ----
 { "persist.savedNotImported", new[]{
 "Kaydedildi ama import edilemedi (glTFast importer?): {0}", "Saved but could not be imported (glTFast importer?): {0}",
 "已保存但无法导入（glTFast 导入器？）：{0}", "已儲存但無法匯入（glTFast 匯入器？）：{0}" } },
 { "persist.permanentAdded", new[]{ "Kalıcı eklendi: {0}", "Permanently added: {0}", "已永久添加：{0}", "已永久新增：{0}" } },

 // ---- Gökyüzü (HdriSky / SkyboxPresets) ----
 { "sky.hdriNotFound", new[]{
 "HDRI bulunamadı — 'npm run fetch:polyhaven' ile gökyüzü indir.", "No HDRI found — run 'npm run fetch:polyhaven' to download skies.",
 "找不到 HDRI——运行 'npm run fetch:polyhaven' 下载天空。", "找不到 HDRI——執行 'npm run fetch:polyhaven' 下載天空。" } },
 { "sky.hdriImportFailed", new[]{ "HDRI import edilemedi: {0}", "Could not import HDRI: {0}", "无法导入 HDRI：{0}", "無法匯入 HDRI：{0}" } },
 { "sky.panoramicShaderMissing", new[]{
 "Skybox/Panoramic shader yok (URP paketini kontrol et).", "Skybox/Panoramic shader missing (check the URP package).",
 "缺少 Skybox/Panoramic 着色器（请检查 URP 套件）。", "缺少 Skybox/Panoramic 著色器（請檢查 URP 套件）。" } },
 { "sky.sunFixed", new[]{ "güneş sabit", "sun fixed", "太阳固定", "太陽固定" } },
 { "sky.sunAligned", new[]{ "ışık hizalandı ({0}° yükseklik)", "light aligned ({0}° elevation)", "光照已对齐（{0}° 仰角）", "光照已對齊（{0}° 仰角）" } },
 { "sky.applied", new[]{ "Gökyüzü: {0} · {1}", "Sky: {0} · {1}", "天空：{0} · {1}", "天空：{0} · {1}" } },
 { "sky.applyFailed", new[]{ "Gökyüzü uygulanamadı: {0}", "Could not apply sky: {0}", "无法应用天空：{0}", "無法套用天空：{0}" } },
 { "sky.proceduralShaderMissing", new[]{ "Skybox/Procedural shader bulunamadı.", "Skybox/Procedural shader not found.", "找不到 Skybox/Procedural 着色器。", "找不到 Skybox/Procedural 著色器。" } },

 // ---- Genel durum çubuğu ----
 { "status.readyAt", new[]{ "Hazır · {0}", "Ready · {0}", "就绪 · {0}", "就緒 · {0}" } },
 { "status.attachmentsReady", new[]{
 "{0} ek hazır — mesajınla birlikte gönderilecek.", "{0} attachment(s) ready — will be sent with your message.",
 "{0} 个附件已就绪——将随你的消息一起发送。", "{0} 個附件已就緒——將隨你的訊息一起傳送。" } },

 { "tool.unknownTool", new[]{ "Bilinmeyen araç: {0}", "Unknown tool: {0}", "未知工具：{0}", "未知工具：{0}" } },
 { "tool.threwError", new[]{ "{0} hata verdi: {1}", "{0} threw an error: {1}", "{0} 出错：{1}", "{0} 發生錯誤：{1}" } },
 };
 }
}
