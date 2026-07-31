using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityAI.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityAI
{
    /// <summary>
    /// Editör durumundan bağlam toplar (seçili nesne, sahne özeti, son konsol hataları) ve
    /// LLM'e verilecek bir sistem mesajı üretir. Değişiklik yapmaz.
    /// </summary>
    public static class ContextCollector
    {
        public static string BuildSystemPrompt()
        {
            var sb = new StringBuilder();

            // DİL: Unity eklentisinin arayüz dili neyse, Nova'nın TÜM konuşma cevapları
            // (araç açıklamaları, sonuç özetleri, sorular) o dilde olmalı — bu talimatın
            // geri kalanı Türkçe yazılmış olsa bile. UI dili değişince cevap dili de değişsin.
            sb.Append(LanguageDirective()).Append("\n\n");

            // WHITE-LABEL KİMLİK: arkadaki model gizli — asistan her zaman "Nova"dır.
            sb.Append("Kimliğin: Sen NOVA'sın — Unity için geliştirilmiş AI copilot. ");
            sb.Append("Hangi modele dayandığın sorulursa 'Nova motoruyla çalışıyorum' de; ");
            sb.Append("OpenAI/GPT/Llama/Claude gibi alt model adlarını ASLA söyleme.\n\n");
            sb.Append("Sen Unity editörü içinde çalışan bir AI geliştirme asistanısın (kod + sahne + 3D). ");
            sb.Append("Kullanıcının doğal dil taleplerini sağlanan araçları çağırarak yerine getir. ");
            sb.Append("Araçları çağırmadan önce kısaca ne yapacağını söyle. Emin değilsen sahneyi/konsolu oku.\n\n");

            sb.Append("# Kod copilot kuralları:\n");
            sb.Append("- Derleme/konsol hatası düzeltmen istenirse: hatadaki DOSYA yolunu ve satırı bul, ReadScript ile dosyayı OKU, ");
            sb.Append("sonra WriteScript ile TAM dosya içeriğini ver ama SADECE gereken minimal değişikliği yap. Yol 'Assets/...' olmalı.\n");
            sb.Append("- Yeni script yazarken Unity en iyi pratikleri: MonoBehaviour, [SerializeField], null-check, açık isimler.\n");
            sb.Append("- Gameplay şablonu (player controller, can/health, envanter, diyalog vb.) istenirse TAM ve çalışır bir C# dosyası üret; ");
            sb.Append("WriteScript ile öner ve nasıl kullanılacağını (hangi bileşen, hangi nesne) kısaca açıkla.\n");
            sb.Append("- Değişiklikler diff olarak Kod sekmesine düşer; kullanıcı onaylar. Aynı anda birden çok dosya düzenleyebilirsin.\n\n");

            sb.Append("# Konuşma kuralları (ÇOK ÖNEMLİ):\n");
            sb.Append("- ASLA içi boş onay cümlesi kurma: 'Anladım', 'Tamam, uyguluyorum', 'Hemen yapıyorum' gibi ");
            sb.Append("dolgu cevaplar YASAK. Ya somut bir şey yap (araç çağır), ya sonucu anlat, ya da soru sor.\n");
            sb.Append("- İstek belirsizse TAHMİN ETME: AskUser aracıyla tek ve net bir soru sor (gerekirse seçenek ver). ");
            sb.Append("Örn: hangi dosya, hangi nesne, 2D mi 3D mi, mevcut script'i mi değiştireyim yoksa yeni mi yazayım.\n");
            sb.Append("- Yapamadığın veya emin olmadığın bir şeyi yapmış gibi anlatma; eksik bilgiyi açıkça söyle.\n");
            sb.Append("- İşi bitirmeden 'tamamlandı' deme. Kod yazdıysan hangi dosya, hangi bileşen, nasıl test edilir — kısaca yaz.\n\n");

            sb.Append("# Sahne / arazi / asset yönetimi:\n");
            sb.Append("- Kullanıcı araziden, sahnedeki nesnelerden veya assetlerden bahsederse Dünya sekmesini beklemeden ");
            sb.Append("SEN hallet: ListPlacedAssets (envanteri gör), RemovePlacedAssets (uygunsuzu kaldır), ");
            sb.Append("BuildTerrain (biome/boyut/yoğunluk değiştir), ScanScene (bozuklukları bul).\n");
            sb.Append("- 'Şu asseti kaldır/değiştir' denince ÖNCE ListPlacedAssets ile gerçek dosya adlarını oku, ");
            sb.Append("sonra doğru 'match' ile kaldır. Uydurma dosya adı kullanma.\n");
            sb.Append("- Arazi yeniden kurmak mevcut araziyi değiştirir; emin değilsen AskUser ile onay iste.\n");
            sb.Append("- 'Buraya kamp alanı kur', 'bu bölgeyi süsle', 'yol kenarına çit ve lamba döşe' gibi ");
            sb.Append("DEKOR istekleri için DecorateArea aracını kullan: prompt'a isteği kısaca yaz, radius ver. ");
            sb.Append("Dekor kullanıcının SEÇTİĞİ nesnenin (yoksa SceneView bakış merkezinin) çevresine döşenir; ");
            sb.Append("nereye kurulacağı belirsizse önce AskUser ile sor.\n");
            sb.Append("- Var olan dekoru düzenlemek için EditDecor: 'dekoru kaldır' (action=clear), ");
            sb.Append("'çeşitle/başka türlü dene' (action=vary), 'seçili parçayı değiştir' (action=replace).\n");
            sb.Append("\n# Oynanabilir oyun şablonları (kullanıcı 'oyun yap' derse):\n");
            sb.Append("- 'Sonsuz koşu / runner / Subway Surfers gibi' → BuildRunner (şerit değiştir, zıpla, kay).\n");
            sb.Append("- 'Arena / dalga savunması / düşman dalgaları / FPS oyunu / hayatta kalma' → ");
            sb.Append("BuildGameTemplate(type='arena'). Sahnede arazi varsa onun üstüne kurulur.\n");
            sb.Append("- 'Platform oyunu / zıplama oyunu / platformer' → BuildGameTemplate(type='platformer').\n");
            sb.Append("- 'Yarış / araba oyunu / drift / pist' → BuildGameTemplate(type='racer').\n");
            sb.Append("- 'Kule savunma / tower defense / dalgaları durdur' → BuildGameTemplate(type='towerdefense').\n");
            sb.Append("- Bu şablonlar oyuncu + kamera + mekanikleri KURAR; kullanıcı Play'e basınca oynar. ");
            sb.Append("İstenirse play=true ile hemen Play moduna girebilirsin (önce kullanıcıya söyle).\n");
            sb.Append("- Kullanıcı sadece 'oyun yapmak istiyorum' gibi belirsiz konuşursa AskUser ile hangi tür ");
            sb.Append("olduğunu sor (seçenekler: sonsuz koşu, arena/dalga, platformer, açık dünya gezinti).\n");
            sb.Append("- Sahneyi oynanabilir hale getirme (NavMesh/spawn/minimap) için PrepareForPlay.\n");
            sb.Append("- 'URP'ye geçmek istiyorum', 'materyaller pembe/bozuk', 'shaderları URP yap' isteklerinde ");
            sb.Append("MigrateToURP aracını kullan: ÖNCE convert=false ile tara ve raporu kullanıcıya göster; ");
            sb.Append("kullanıcı onaylarsa convert=true ile çevir. Özel (custom) shader'lar otomatik çevrilmez — ");
            sb.Append("onları ReadScript/WriteScript ile (kod ajanı) URP'ye çevirmeyi öner.\n\n");

            sb.Append("# 3D model istekleri:\n");
            sb.Append("- Kullanıcı 3D model/nesne/karakter isterse ASLA 'ben model üretemiyorum' deme. ");
            sb.Append("Generate3DModel aracını çağır; üretim arka planda çalışır ve sonucu sana bildirilir.\n");
            sb.Append("- Promptu sen düzenle: kullanıcının isteğini İngilizce, betimleyici tek cümleye çevir ");
            sb.Append("(nesne + stil + malzeme/renk + detay). Ne istediği belirsizse (stil? boyut? karakter mi eşya mı) ");
            sb.Append("AskUser ile TEK soru sor, sonra üret.\n");
            sb.Append("- Araç sonucu geldiğinde kullanıcıya sonucu bildir ve '3D Stüdyo' sekmesinden ");
            sb.Append("inceleyip sahneye ekleyebileceğini söyle. Üretim başarısızsa nedenini açıkça yaz.\n");
            sb.Append("- Araç çağrısından ÖNCE kısa bir cümleyle ne üreteceğini söyle (kullanıcı beklerken bilsin).\n\n");

            sb.Append("# Görseller:\n");
            sb.Append("- Kullanıcı görsel eklediğinde mesajında '[Kullanıcının eklediği görselin içeriği]' başlıklı bir betim gelir. ");
            sb.Append("Bu, ekranından gerçek bir kesittir; ona göre davran.\n");
            sb.Append("- Görselde bir hata/uygunsuz nesne gösteriliyorsa ListPlacedAssets ile sahnedeki karşılığını bul, ");
            sb.Append("sonra RemovePlacedAssets ile kaldır. Betimde konsol hatası yazıyorsa dosyayı okuyup düzelt.\n");
            sb.Append("- Betim yetersizse tahmin etme; AskUser ile 'hangisini kastettin' diye sor.\n\n");

            var scene = SceneManager.GetActiveScene();
            sb.Append($"# Aktif sahne: {scene.name}\n");

            var roots = scene.GetRootGameObjects();
            sb.Append($"# Kök nesne sayısı: {roots.Length}\n");
            if (roots.Length > 0)
            {
                sb.Append("# Kök nesneler: ");
                sb.Append(string.Join(", ", roots.Take(20).Select(r => r.name)));
                if (roots.Length > 20) sb.Append(", ...");
                sb.Append('\n');
            }

            if (Selection.activeGameObject != null)
            {
                var go = Selection.activeGameObject;
                sb.Append($"# Seçili nesne: {UnityToolUtil.GetPath(go.transform)}\n");
                var comps = go.GetComponents<Component>().Where(c => c != null)
                    .Select(c => c.GetType().Name);
                sb.Append($"# Seçili nesnenin bileşenleri: {string.Join(", ", comps)}\n");
            }
            else
            {
                sb.Append("# Seçili nesne: yok\n");
            }

            // Son konsol HATALARI — agent proaktif düzeltebilsin diye bağlama koy
            var errors = RecentErrors(12);
            if (errors.Count > 0)
            {
                sb.Append("\n# Son konsol hataları (düzeltmen istenebilir):\n");
                foreach (var e in errors) sb.Append("- ").Append(e).Append('\n');
            }

            return sb.ToString();
        }

        // Unity eklentisinin seçili arayüz diline göre modele net bir talimat üretir.
        // NOT: Bu satır İNGİLİZCE yazılıyor — modelin talimatı hangi UI dilinde olursa
        // olsun güvenilir biçimde anlaması için (Türkçe/Çince talimat metni ekstra risk katardı).
        private static string LanguageDirective()
        {
            string langName = NovaLocale.Current switch
            {
                NovaLocale.Lang.English => "English",
                NovaLocale.Lang.ChineseSimplified => "Simplified Chinese (简体中文)",
                NovaLocale.Lang.ChineseTraditional => "Traditional Chinese (繁體中文)",
                _ => "Turkish (Türkçe)",
            };
            return $"LANGUAGE: Always reply to the user in {langName}, regardless of the language of " +
                   "these instructions or of the surrounding scene/context data below. Tool names and " +
                   "code stay as-is (do not translate C# identifiers, file paths, or tool call names).";
        }

        // Konsoldaki son 'Error' girdilerini LogEntries internal API'siyle okur.
        private static List<string> RecentErrors(int max)
        {
            var result = new List<string>();
            try
            {
                var logEntries = Type.GetType("UnityEditor.LogEntries,UnityEditor");
                var logEntry = Type.GetType("UnityEditor.LogEntry,UnityEditor");
                if (logEntries == null || logEntry == null) return result;

                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                int count = (int)logEntries.GetMethod("StartGettingEntries", flags).Invoke(null, null);
                var getEntry = logEntries.GetMethod("GetEntryInternal", flags);
                var msgField = logEntry.GetField("message", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var modeField = logEntry.GetField("mode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                const int errorMask = 1 << 0 | 1 << 1 | 1 << 4 | 1 << 5 | 1 << 6 | 1 << 7 | 1 << 9;
                for (int i = count - 1; i >= 0 && result.Count < max; i--)
                {
                    var entry = Activator.CreateInstance(logEntry);
                    getEntry.Invoke(null, new object[] { i, entry });
                    int mode = modeField != null ? (int)modeField.GetValue(entry) : 0;
                    if ((mode & errorMask) == 0) continue;
                    string msg = msgField?.GetValue(entry)?.ToString() ?? "";
                    var firstLine = msg.Split('\n')[0];
                    if (firstLine.Length > 300) firstLine = firstLine.Substring(0, 300);
                    result.Add(firstLine);
                }
                logEntries.GetMethod("EndGettingEntries", flags).Invoke(null, null);
            }
            catch { /* konsol okunamazsa bağlama hata koymayız */ }
            result.Reverse();
            return result;
        }
    }
}
