using System.Collections.Generic;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// FAZ 2 — Organik şehir düzeni (saf veri, sahneye dokunmaz; test edilebilir).
    /// Izgara YOK: kıvrımlı ana arterler + onlardan dallanan ikincil/üçüncül yollar,
    /// yol boyunca dizilen değişken lotlar, Perlin tabanlı zonlama (merkez/konut/park/kenar).
    /// Kural: her lot yola bakar; occupancy grid çakışmayı engeller.
    /// </summary>
    public static class CityLayout
    {
        public class Map
        {
            public float Size;
            public List<List<Vector2>> Roads = new List<List<Vector2>>();
            public List<float> RoadWidths = new List<float>();
            public int ArteryCount;
            public List<Lot> Lots = new List<Lot>();
        }

        public struct Lot
        {
            public Vector2 Center;
            public Vector2 Facing;   // lotun baktığı yön (yola doğru)
            public string Zone;      // core | residential | edge | park
            public float Width, Depth;
        }

        private const float CELL = 2f; // occupancy hücresi (m)

        public static Map Generate(float size, float density, float greenery, int seed)
        {
            var rnd = new System.Random(seed);
            int n = Mathf.CeilToInt(size / CELL);
            var occ = new byte[n, n]; // 0 boş · 1 yol · 2 lot
            var map = new Map { Size = size };
            float nz = (float)rnd.NextDouble() * 97f;
            var center = new Vector2(size / 2f, size / 2f);

            // ---- 1) ANA ARTERLER: haritayı kat eden kıvrımlı yollar ----
            int nArt = size < 380f ? 2 : 3;
            map.ArteryCount = nArt;
            for (int a = 0; a < nArt; a++)
            {
                bool horiz = a % 2 == 0;
                float t0 = 0.30f + (float)rnd.NextDouble() * 0.40f;
                float t1 = 0.30f + (float)rnd.NextDouble() * 0.40f;
                Vector2 s = horiz ? new Vector2(0f, size * t0) : new Vector2(size * t0, 0f);
                Vector2 e = horiz ? new Vector2(size, size * t1) : new Vector2(size * t1, size);
                var path = Wobble(s, e, size * 0.09f, nz + a * 13.7f);
                map.Roads.Add(path);
                map.RoadWidths.Add(7f);
                Stamp(occ, path, 3.5f);
            }

            // ---- 2) İKİNCİL + ÜÇÜNCÜL YOLLAR: arterlerden dallan ----
            for (int a = 0; a < nArt; a++)
                BranchAlong(map, occ, map.Roads[a], level: 1, rnd, nz, size);

            // ---- 3) LOTLAR: her yolun iki yanına, zonlamayla ----
            for (int r = 0; r < map.Roads.Count; r++)
            {
                float half = map.RoadWidths[r] * 0.5f;
                foreach (var (p, dir) in ArcWalk(map.Roads[r], 13f))
                {
                    var perp = new Vector2(-dir.y, dir.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float lotW = 10f + (float)rnd.NextDouble() * 3f;   // değişken lot → organik his
                        float lotD = 12f + (float)rnd.NextDouble() * 3f;
                        Vector2 lc = p + perp * side * (half + 2.5f + lotD * 0.5f); // yol kenarından 2.5 m pay
                        if (lc.x < 8f || lc.y < 8f || lc.x > size - 8f || lc.y > size - 8f) continue;

                        // ZONLAMA: merkez uzaklığı + Perlin (organik park boşlukları)
                        float d = (lc - center).magnitude / (size * 0.5f);
                        float pn = Mathf.PerlinNoise(lc.x * 0.012f + nz, lc.y * 0.012f + nz);
                        string zone = pn < 0.28f + greenery * 0.22f ? "park"
                                    : d < 0.32f ? "core"
                                    : d < 0.72f ? "residential" : "edge";

                        // Yoğunluk: kenara doğru seyrekleşir, bazı lotlar bilinçli BOŞ kalır
                        double keep = zone == "core" ? 0.95
                                    : zone == "residential" ? density
                                    : zone == "park" ? 0.8
                                    : density * 0.45;
                        if (rnd.NextDouble() > keep) continue;

                        // Kontrol %85 boyutla (eğri yolda eksen-hizalı kutu fazla katı olmasın);
                        // rezervasyon tam boyla (komşu lotlar üst üste binmesin).
                        if (!RectFree(occ, lc, lotW * 0.85f, lotD * 0.85f)) continue;
                        StampRect(occ, lc, lotW, lotD, 2);

                        map.Lots.Add(new Lot
                        {
                            Center = lc,
                            Facing = -perp * side, // yola doğru bak
                            Zone = zone,
                            Width = lotW,
                            Depth = lotD,
                        });
                    }
                }
            }

            return map;
        }

        // Arterden dallanan yollar; ikinciller de %35 ihtimalle üçüncül dal atar.
        private static void BranchAlong(Map map, byte[,] occ, List<Vector2> road, int level,
            System.Random rnd, float nz, float size)
        {
            if (level > 2) return;
            float pitch = level == 1 ? 34f : 44f;
            int side = rnd.NextDouble() < 0.5 ? -1 : 1;

            foreach (var (p, dir) in ArcWalk(road, pitch + (float)rnd.NextDouble() * 22f))
            {
                side = -side; // sokaklar iki yana dönüşümlü
                if (rnd.NextDouble() < 0.25) continue; // her noktadan dal çıkmasın — düzensizlik iyi

                var perp = new Vector2(-dir.y, dir.x) * side;
                float len = level == 1 ? 50f + (float)rnd.NextDouble() * 70f : 30f + (float)rnd.NextDouble() * 40f;
                Vector2 s = p + perp * 3f;
                Vector2 e = p + perp * len;
                e.x = Mathf.Clamp(e.x, 6f, size - 6f);
                e.y = Mathf.Clamp(e.y, 6f, size - 6f);

                var path = Wobble(s, e, len * 0.14f, nz + p.x * 0.31f + p.y * 0.17f);
                path = TrimAtRoad(path, occ, 10f); // başka yola çarpınca kavşak yap, orada dur
                if (PathLength(path) < 24f) continue;

                map.Roads.Add(path);
                map.RoadWidths.Add(5f);
                Stamp(occ, path, 2.5f);

                if (level == 1 && len > 70f && rnd.NextDouble() < 0.35)
                    BranchAlong(map, occ, path, 2, rnd, nz, size);
            }
        }

        // ---- Geometri yardımcıları ----

        // s→e arasında Perlin sapmalı kıvrımlı yol (uçlar sabit kalır)
        private static List<Vector2> Wobble(Vector2 s, Vector2 e, float amp, float no)
        {
            var pts = new List<Vector2>();
            Vector2 d = e - s;
            float len = d.magnitude;
            if (len < 1f) { pts.Add(s); pts.Add(e); return pts; }
            Vector2 dir = d / len;
            var perp = new Vector2(-dir.y, dir.x);
            int m = Mathf.Max(4, Mathf.CeilToInt(len / 12f));
            for (int i = 0; i <= m; i++)
            {
                float t = i / (float)m;
                float w = (Mathf.PerlinNoise(no, t * 2.3f + no) * 2f - 1f) * amp * Mathf.Sin(t * Mathf.PI);
                pts.Add(s + d * t + perp * w);
            }
            return pts;
        }

        /// <summary>Polyline üzerinde sabit aralıklarla (nokta, yön) üretir.</summary>
        public static IEnumerable<(Vector2 p, Vector2 dir)> ArcWalk(List<Vector2> path, float pitch)
        {
            float acc = pitch * 0.5f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i];
                float seg = (b - a).magnitude;
                if (seg < 1e-4f) continue;
                Vector2 dir = (b - a) / seg;
                while (acc <= seg)
                {
                    yield return (a + dir * acc, dir);
                    acc += pitch;
                }
                acc -= seg;
            }
        }

        public static float PathLength(List<Vector2> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++) l += (path[i] - path[i - 1]).magnitude;
            return l;
        }

        // Yol şeridini occupancy'e bas — GERÇEK dairesel yarıçap.
        // (Kare damga yolu ±5 m işaretleyip lotların tamamını reddettiriyordu → "1 bina" hatası.)
        private static void Stamp(byte[,] occ, List<Vector2> path, float r)
        {
            int n = occ.GetLength(0);
            float r2 = (r + CELL * 0.4f) * (r + CELL * 0.4f);
            foreach (var (p, _) in ArcWalk(path, 1.2f))
            {
                int ci = Mathf.RoundToInt(p.x / CELL), cj = Mathf.RoundToInt(p.y / CELL);
                int rr = Mathf.CeilToInt(r / CELL) + 1;
                for (int j = cj - rr; j <= cj + rr; j++)
                for (int i = ci - rr; i <= ci + rr; i++)
                {
                    if (i < 0 || j < 0 || i >= n || j >= n) continue;
                    float dx = i * CELL - p.x, dy = j * CELL - p.y;
                    if (dx * dx + dy * dy <= r2) occ[i, j] = 1;
                }
            }
        }

        private static bool RectFree(byte[,] occ, Vector2 c, float w, float d)
        {
            int n = occ.GetLength(0);
            int i0 = Mathf.FloorToInt((c.x - w / 2f) / CELL), i1 = Mathf.CeilToInt((c.x + w / 2f) / CELL);
            int j0 = Mathf.FloorToInt((c.y - d / 2f) / CELL), j1 = Mathf.CeilToInt((c.y + d / 2f) / CELL);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                if (i < 0 || j < 0 || i >= n || j >= n) return false;
                if (occ[i, j] != 0) return false;
            }
            return true;
        }

        private static void StampRect(byte[,] occ, Vector2 c, float w, float d, byte val)
        {
            int n = occ.GetLength(0);
            int i0 = Mathf.FloorToInt((c.x - w / 2f) / CELL), i1 = Mathf.CeilToInt((c.x + w / 2f) / CELL);
            int j0 = Mathf.FloorToInt((c.y - d / 2f) / CELL), j1 = Mathf.CeilToInt((c.y + d / 2f) / CELL);
            for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
                if (i >= 0 && j >= 0 && i < n && j < n) occ[i, j] = val;
        }

        // Dal başka bir yola değince orada kes (kavşak oluşur); ilk 'skip' metre muaf.
        private static List<Vector2> TrimAtRoad(List<Vector2> path, byte[,] occ, float skip)
        {
            int n = occ.GetLength(0);
            var outPts = new List<Vector2> { path[0] };
            float walked = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i];
                float seg = (b - a).magnitude;
                int steps = Mathf.Max(1, Mathf.CeilToInt(seg / 2f));
                for (int st = 1; st <= steps; st++)
                {
                    Vector2 p = Vector2.Lerp(a, b, st / (float)steps);
                    walked += seg / steps;
                    int ci = Mathf.RoundToInt(p.x / CELL), cj = Mathf.RoundToInt(p.y / CELL);
                    bool onRoad = ci >= 0 && cj >= 0 && ci < n && cj < n && occ[ci, cj] == 1;
                    outPts.Add(p);
                    if (walked > skip && onRoad) return outPts; // kavşağa bağlandı
                }
            }
            return outPts;
        }
    }
}
