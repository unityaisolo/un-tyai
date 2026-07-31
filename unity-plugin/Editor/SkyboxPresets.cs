using System;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Hazır gökyüzü presetleri. Unity'nin Procedural skybox shader'ı ile (sıfır asset) gündüz/gün batımı/
    /// gece/bulutlu atmosferi kurar, güneş yönü/rengi + ortam ışığını ayarlar. RenderSettings üzerinden uygular.
    /// </summary>
    public static class SkyboxPresets
    {
        public enum Sky { Day = 0, Sunset = 1, Night = 2, Overcast = 3, Dawn = 4, Horror = 5 }

        public static readonly string[] Names = { "Gündüz", "Gün batımı", "Gece", "Bulutlu", "Şafak", "Sisli / Korku" };
        private static readonly string[] Keys = { "sky.day", "sky.sunset", "sky.night", "sky.overcast", "sky.dawn", "sky.horror" };

        /// <summary>Seçili dilde preset adları (menü için).</summary>
        public static string[] LocalizedNames()
        {
            var r = new string[Keys.Length];
            for (int i = 0; i < Keys.Length; i++) r[i] = NovaLocale.T(Keys[i]);
            return r;
        }

        public static void Apply(int index, Action<string> log = null)
        {
            Apply((Sky)Mathf.Clamp(index, 0, Names.Length - 1), log);
        }

        public static void Apply(Sky sky, Action<string> log = null)
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null) { log?.Invoke(NovaLocale.T("sky.proceduralShaderMissing")); return; }

            var mat = new Material(shader) { name = "NovaSky_" + sky };
            mat.SetFloat("_SunSize", 0.04f);
            mat.SetFloat("_SunSizeConvergence", 5f);

            var sun = FindOrCreateSun();
            Color ambient;
            float sunIntensity;
            Quaternion sunRot;
            bool fog = false;                       // E3: atmosferik sis desteği
            Color fogColor = Color.gray;
            float fogDensity = 0.01f;

            switch (sky)
            {
                case Sky.Dawn:
                    mat.SetColor("_SkyTint", new Color(0.85f, 0.62f, 0.66f));
                    mat.SetColor("_GroundColor", new Color(0.30f, 0.26f, 0.28f));
                    mat.SetFloat("_AtmosphereThickness", 1.35f);
                    mat.SetFloat("_Exposure", 1.1f);
                    sunRot = Quaternion.Euler(6f, 110f, 0f);
                    sun.color = new Color(1.0f, 0.78f, 0.62f);
                    sunIntensity = 0.8f;
                    ambient = new Color(0.45f, 0.40f, 0.44f);
                    fog = true; fogColor = new Color(0.78f, 0.66f, 0.66f); fogDensity = 0.004f; // hafif sabah pusu
                    break;
                case Sky.Horror:
                    mat.SetColor("_SkyTint", new Color(0.16f, 0.19f, 0.17f));
                    mat.SetColor("_GroundColor", new Color(0.07f, 0.08f, 0.07f));
                    mat.SetFloat("_AtmosphereThickness", 0.7f);
                    mat.SetFloat("_Exposure", 0.5f);
                    sunRot = Quaternion.Euler(25f, 160f, 0f);
                    sun.color = new Color(0.55f, 0.60f, 0.55f);
                    sunIntensity = 0.25f;
                    ambient = new Color(0.14f, 0.16f, 0.14f);
                    fog = true; fogColor = new Color(0.22f, 0.26f, 0.23f); fogDensity = 0.028f; // yoğun ürkütücü sis
                    break;
                case Sky.Sunset:
                    mat.SetColor("_SkyTint", new Color(0.92f, 0.55f, 0.30f));
                    mat.SetColor("_GroundColor", new Color(0.22f, 0.18f, 0.16f));
                    mat.SetFloat("_AtmosphereThickness", 1.6f);
                    mat.SetFloat("_Exposure", 1.15f);
                    sunRot = Quaternion.Euler(8f, 20f, 0f);
                    sun.color = new Color(1.0f, 0.62f, 0.36f);
                    sunIntensity = 0.9f;
                    ambient = new Color(0.42f, 0.32f, 0.28f);
                    break;
                case Sky.Night:
                    mat.SetColor("_SkyTint", new Color(0.10f, 0.13f, 0.22f));
                    mat.SetColor("_GroundColor", new Color(0.05f, 0.06f, 0.09f));
                    mat.SetFloat("_AtmosphereThickness", 0.5f);
                    mat.SetFloat("_Exposure", 0.4f);
                    sunRot = Quaternion.Euler(-12f, 200f, 0f);
                    sun.color = new Color(0.55f, 0.62f, 0.85f);
                    sunIntensity = 0.15f;
                    ambient = new Color(0.10f, 0.12f, 0.18f);
                    break;
                case Sky.Overcast:
                    mat.SetColor("_SkyTint", new Color(0.62f, 0.64f, 0.66f));
                    mat.SetColor("_GroundColor", new Color(0.30f, 0.31f, 0.32f));
                    mat.SetFloat("_AtmosphereThickness", 1.2f);
                    mat.SetFloat("_Exposure", 1.0f);
                    sunRot = Quaternion.Euler(55f, 120f, 0f);
                    sun.color = new Color(0.86f, 0.87f, 0.90f);
                    sunIntensity = 0.55f;
                    ambient = new Color(0.55f, 0.56f, 0.58f);
                    break;
                default: // Day
                    mat.SetColor("_SkyTint", new Color(0.50f, 0.60f, 0.85f));
                    mat.SetColor("_GroundColor", new Color(0.37f, 0.35f, 0.34f));
                    mat.SetFloat("_AtmosphereThickness", 1.0f);
                    mat.SetFloat("_Exposure", 1.3f);
                    sunRot = Quaternion.Euler(50f, -30f, 0f);
                    sun.color = new Color(1.0f, 0.96f, 0.88f);
                    sunIntensity = 1.15f;
                    ambient = new Color(0.52f, 0.54f, 0.57f);
                    break;
            }

            sun.transform.rotation = sunRot;
            sun.intensity = sunIntensity;

            RenderSettings.skybox = mat;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog;
            if (fog)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
            }
            DynamicGI.UpdateEnvironment();

            log?.Invoke("Atmosfer: " + Names[(int)sky]);
        }

        static Light FindOrCreateSun()
        {
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional) return l;

            var go = new GameObject("NovaSun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            Undo.RegisterCreatedObjectUndo(go, "Nova: Güneş");
            return light;
        }
    }
}
