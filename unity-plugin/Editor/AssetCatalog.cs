using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// Yerel varlık kataloğunu (asset-pipeline/catalog.json) okur ve World Builder için
    /// kategori/stil/tema filtreli seçim sağlar. GLB yolu = &lt;catalog klasörü&gt;/assets-raw/&lt;file&gt;.
    /// </summary>
    public static class AssetCatalog
    {
        [Serializable] public class Vec3 { public float x, y, z; }
        [Serializable] public class Foot { public float x, z; }

        [Serializable]
        public class Entry
        {
            public string id, name, file, pack, style, license, category, type, format;
            public string[] tags;
            public string[] themes;
            public Vec3 sizeMeters;
            public Foot footprint;
            public bool pivotBottom;
            public int triangles;
            // v3 katalog (metadata sözleşmesi)
            public string role;         // house | shop | civic | tower | tree | bush | ... | road_straight ...
            public string theme;        // modern | rural | fantasy | generic (binalarda harita tipiyle eşleşir)
            public float realTarget;    // metre cinsinden hedef boy (0 ise rol varsayılanı kullanılmalı)
            public string family;       // pack/sanatçı ailesi (görsel tutarlılık kilidi)
            public string[] connectors; // yol parçaları: hangi kenarlar bağlanır (N/E/S/W)
            public float unitScale;     // 1=metre, 0.01=cm ihracı... (bilgi amaçlı; yerleştirici bounds'tan normalize eder)
            public int meshNodes;
            public string quality;
        }

        [Serializable] private class Wrapper { public List<Entry> items; }

        private static List<Entry> _cache;
        private static string _cachePath;
        private static string _assetsRoot;

        public static string AssetsRoot => _assetsRoot;
        public static int Count => _cache?.Count ?? 0;

        public static List<Entry> Load(string path = null, bool force = false)
        {
            path = string.IsNullOrEmpty(path) ? UnityAIConfig.CatalogPath : path;
            if (!force && _cache != null && _cachePath == path) return _cache;
            if (!File.Exists(path)) throw new FileNotFoundException("catalog.json bulunamadı: " + path);

            string raw = File.ReadAllText(path);
            var wrap = JsonUtility.FromJson<Wrapper>("{\"items\":" + raw + "}");
            _cache = wrap != null && wrap.items != null ? wrap.items : new List<Entry>();
            _cachePath = path;
            _assetsRoot = Path.Combine(Path.GetDirectoryName(path), "assets-raw");
            return _cache;
        }

        // Katalog henüz yüklenmediyse _assetsRoot null olur — Path.Combine patlamasın.
        public static string AbsolutePath(Entry e) =>
            string.IsNullOrEmpty(_assetsRoot) || e == null || string.IsNullOrEmpty(e.file)
                ? null
                : Path.Combine(_assetsRoot, e.file.Replace('/', Path.DirectorySeparatorChar));

        public static string FileUri(Entry e)
        {
            var p = AbsolutePath(e);
            return string.IsNullOrEmpty(p) ? null : new Uri(p).AbsoluteUri;
        }

        public static List<string> Styles() =>
            Load().Select(e => e.style).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();

        public static List<string> Themes()
        {
            var set = new HashSet<string>();
            foreach (var e in Load())
                if (e.themes != null) foreach (var t in e.themes) if (!string.IsNullOrEmpty(t)) set.Add(t);
            return set.ToList();
        }

        /// <summary>ROL bazlı seçim (v3): verilen rollerden, stil uyumlu. Stil sonuç vermezse gevşetir.</summary>
        public static List<Entry> FilterRoles(IList<string> roles, string style)
        {
            var r = Load().Where(e => !string.IsNullOrEmpty(e.role) && roles.Contains(e.role)
                                      && e.sizeMeters != null && e.sizeMeters.x > 0f).ToList();
            if (!string.IsNullOrEmpty(style) && style != "any")
            {
                var s = r.Where(e => e.style == style).ToList();
                if (s.Count > 0) r = s;
            }
            return r;
        }

        /// <summary>Rol için hedef boy: katalog değeri, yoksa güvenli varsayılan.</summary>
        public static float TargetOf(Entry e, float fallback = 4f) =>
            e != null && e.realTarget > 0.01f ? e.realTarget : fallback;

        /// <summary>Kategori + stil + tema filtreli seçim. Sonuç boşsa filtreleri kademeli gevşetir.</summary>
        public static List<Entry> Filter(string category, string style, IList<string> themes)
        {
            var r = Load().Where(e => e.category == category && e.sizeMeters != null && e.sizeMeters.x > 0f).ToList();
            if (!string.IsNullOrEmpty(style) && style != "any")
            {
                var s = r.Where(e => e.style == style).ToList();
                if (s.Count > 0) r = s;
            }
            if (themes != null && themes.Count > 0)
            {
                var t = r.Where(e => e.themes != null && e.themes.Any(themes.Contains)).ToList();
                if (t.Count > 0) r = t;
            }
            return r;
        }
    }
}
