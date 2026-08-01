using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityAI
{
 /// <summary>
 /// A9 — URP GÖÇ ASİSTANI. 2026-02'de HDRP maintenance mode'a alındı; herkes URP'ye
 /// geçmek zorunda ama Standard/BIRP materyalleri URP'de "pembe" (bozuk) görünüyor.
 /// Bu araç: aktif pipeline'ı tespit eder, sahnedeki Standard/Legacy shader materyallerini
 /// ve pembe (kayıp/hatalı shader) materyalleri sayar → tek tık URP/Lit'e çevirir
 /// (renk/doku/metallic/normal/emisyon eşlenir), .mat asset'leri güncellenir.
 /// Özel shader'lar otomatik çevrilemez — kod ajanına devredilmek üzere raporlanır.
 /// </summary>
 public static class UrpMigrator
 {
 [MenuItem("UnityAI/URP Göç Asistanı — Tara")]
 private static void MenuScan() =>
 Debug.Log("[Nova URP]\n" + ScanAndReport());

 [MenuItem("UnityAI/URP Göç Asistanı — URP'ye Çevir")]
 private static void MenuMigrate()
 {
 int n = Migrate(confirm: true);
 if (n > 0) Debug.Log($"[Nova URP] {n} materyal çevrildi.");
 }

 public struct Report
 {
 public bool IsUrp;
 public string PipelineName;
 public int Standard; // Standard/Legacy/Mobile — otomatik çevrilebilir
 public int Pink; // shader kayıp/hatalı → çoğu URP'de bozuk Standard
 public int Custom; // tanınmayan özel shader — ajana devir
 public int AlreadyUrp; // zaten URP shader
 public HashSet<Material> Convertible;
 public HashSet<Material> CustomMats;
 }

 // Standard/Built-in ailesinden sayılan shader'lar (URP/Lit'e güvenle eşlenir)
 private static bool IsStandardFamily(string n) =>
 n == "Standard" || n == "Standard (Specular setup)" ||
 n.StartsWith("Legacy Shaders/") || n.StartsWith("Mobile/") ||
 n == "Autodesk Interactive" || n == "Diffuse" || n == "Bumped Diffuse";

 private static bool IsUrpShader(string n) => n.StartsWith("Universal Render Pipeline/");

 private static bool IsPink(Material m)
 {
 if (m.shader == null) return true;
 var n = m.shader.name;
 return n == "Hidden/InternalErrorShader" || n.Contains("InternalError");
 }

 /// <summary>Sahnedeki tüm renderer'lardan benzersiz materyalleri toplar.</summary>
 private static HashSet<Material> SceneMaterials()
 {
 var set = new HashSet<Material>();
 foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
 foreach (var r in root.GetComponentsInChildren<Renderer>(true))
 foreach (var m in r.sharedMaterials)
 if (m != null) set.Add(m);
 return set;
 }

 public static Report Scan()
 {
 var rep = new Report
 {
 Convertible = new HashSet<Material>(),
 CustomMats = new HashSet<Material>(),
 };
 var rpa = GraphicsSettings.currentRenderPipeline;
 rep.IsUrp = rpa != null && rpa.GetType().Name.Contains("Universal");
 rep.PipelineName = rpa == null ? "Built-in (BIRP)" : rpa.GetType().Name;

 foreach (var m in SceneMaterials())
 {
 if (IsPink(m)) { rep.Pink++; rep.Convertible.Add(m); continue; }
 string n = m.shader.name;
 if (IsUrpShader(n)) rep.AlreadyUrp++;
 else if (IsStandardFamily(n)) { rep.Standard++; rep.Convertible.Add(m); }
 else { rep.Custom++; rep.CustomMats.Add(m); }
 }
 return rep;
 }

 public static string ScanAndReport()
 {
 var r = Scan();
 var sb = new StringBuilder();
 sb.AppendLine($"URP Göç Taraması — aktif pipeline: {r.PipelineName}"
 + (r.IsUrp ? " ✓ URP aktif" : " ⚠ URP DEĞİL"));
 if (!r.IsUrp)
 sb.AppendLine(" → Not: Materyalleri URP/Lit'e çevirmek yalnız proje URP'ye geçtiğinde doğru görünür. "
 + "Önce Project Settings > Graphics'te bir URP Asset ata.");
 sb.AppendLine($"• Çevrilebilir (Standard/Legacy): {r.Standard}" + (r.Standard > 0 ? " → tek tık URP/Lit" : " ✓"));
 sb.AppendLine($"• Pembe / bozuk shader: {r.Pink}" + (r.Pink > 0 ? " → çoğu URP'de Standard; dönüşüm düzeltir" : " ✓"));
 sb.AppendLine($"• Özel shader (elle/ajanla): {r.Custom}" + (r.Custom > 0 ? " → Kod ajanına 'shader'ı URP'ye çevir' de" : " ✓"));
 sb.AppendLine($"• Zaten URP: {r.AlreadyUrp} ✓");
 int conv = r.Convertible.Count;
 sb.Append(conv > 0
 ? $"\n{conv} materyal çevrilmeye hazır. ' URP'ye çevir' ile uygula (Ctrl+Z geri alır)."
 : "\nÇevrilecek Standard materyal yok — sahne temiz görünüyor.");
 return sb.ToString();
 }

 /// <summary>
 /// Sahnedeki Standard/pembe materyalleri URP/Lit'e çevirir. Özellikleri eşler,
 /// .mat asset'lerini kaydeder, Undo destekler. Döner: çevrilen sayısı.
 /// </summary>
 public static int Migrate(bool confirm = true)
 {
 var r = Scan();
 if (r.Convertible.Count == 0) return 0;

 var lit = Shader.Find("Universal Render Pipeline/Lit");
 if (lit == null)
 {
 EditorUtility.DisplayDialog("URP bulunamadı",
 "'Universal Render Pipeline/Lit' shader'ı yok. URP paketi kurulu mu? "
 + "(Package Manager > Universal RP)", "Tamam");
 return 0;
 }

 if (confirm && !EditorUtility.DisplayDialog("URP'ye çevir",
 $"{r.Convertible.Count} materyal URP/Lit'e çevrilecek. Renk, doku, metallic, "
 + "normal ve emisyon eşlenecek. .mat dosyaları değişir (Ctrl+Z geri alır). Devam?",
 "Çevir", "Vazgeç"))
 return 0;

 int done = 0;
 foreach (var m in r.Convertible)
 {
 if (m == null) continue;
 Undo.RecordObject(m, "Nova: URP'ye çevir");
 ConvertToUrpLit(m, lit);
 EditorUtility.SetDirty(m);
 done++;
 }
 AssetDatabase.SaveAssets();
 Debug.Log($"[Nova URP] {done} materyal URP/Lit'e çevrildi."
 + (r.Custom > 0 ? $" · {r.Custom} özel shader elle/ajanla ele alınmalı." : ""));
 return done;
 }

 // Standard (veya pembe/kayıp) → URP/Lit özellik eşlemesi.
 private static void ConvertToUrpLit(Material m, Shader lit)
 {
 // Eski shader'dan mümkün olan değerleri OKU (shader değişince kaybolmadan önce)
 Color baseCol = ReadColor(m, "_BaseColor", "_Color");
 Texture baseMap = ReadTex(m, "_BaseMap", "_MainTex");
 Vector2 tiling = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
 Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
 float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
 float smooth = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness")
 : m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.5f;
 Texture normalMap = ReadTex(m, "_BumpMap", "_BumpMap");
 Texture metalMap = ReadTex(m, "_MetallicGlossMap", "_MetallicGlossMap");
 bool emissionOn = m.IsKeywordEnabled("_EMISSION");
 Color emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
 Texture emissionMap = ReadTex(m, "_EmissionMap", "_EmissionMap");

 // Shader'ı değiştir, sonra URP alanlarını yaz
 m.shader = lit;
 if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseCol);
 if (m.HasProperty("_BaseMap"))
 {
 m.SetTexture("_BaseMap", baseMap);
 m.SetTextureScale("_BaseMap", tiling);
 m.SetTextureOffset("_BaseMap", offset);
 }
 if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
 if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
 if (normalMap != null && m.HasProperty("_BumpMap"))
 {
 m.SetTexture("_BumpMap", normalMap);
 m.EnableKeyword("_NORMALMAP");
 }
 if (metalMap != null && m.HasProperty("_MetallicGlossMap"))
 {
 m.SetTexture("_MetallicGlossMap", metalMap);
 m.EnableKeyword("_METALLICSPECGLOSSMAP");
 }
 if (m.HasProperty("_EmissionColor"))
 {
 m.SetColor("_EmissionColor", emission);
 if (emissionMap != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emissionMap);
 if (emissionOn || emission.maxColorComponent > 0.001f)
 {
 m.EnableKeyword("_EMISSION");
 m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
 }
 }
 }

 private static Color ReadColor(Material m, string urp, string legacy) =>
 m.HasProperty(urp) ? m.GetColor(urp) : m.HasProperty(legacy) ? m.GetColor(legacy) : Color.white;

 private static Texture ReadTex(Material m, string urp, string legacy) =>
 m.HasProperty(urp) && m.GetTexture(urp) != null ? m.GetTexture(urp)
 : m.HasProperty(legacy) ? m.GetTexture(legacy) : null;
 }
}
