using System;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// DENETÇİ — build biter bitmez sahneyi tarar ve tutarsızlıkları OTOMATİK düzeltir:
    /// beklenenden kat kat büyük objeleri küçültür/kaldırır, havada asılı kalanları yere indirir.
    /// Her müdahale Console'a asset dosya adıyla yazılır → hangi asset sorunluysa katalogdan elenir.
    /// </summary>
    public static class SceneLint
    {
        public static string Audit(GameObject root, Action<string> log)
        {
            int shrunk = 0, removed = 0, grounded = 0;
            // KRİTİK: Terrain denetlenen kökün içinde olmayabilir (dekor kökü gibi) —
            // sahnedeki Terrain'e düş. Yoksa dekor objeleri y=0'a çekilip GÖMÜLÜYORDU.
            var terrain = root.GetComponentInChildren<Terrain>();
            if (terrain == null) terrain = UnityEngine.Object.FindAnyObjectByType<Terrain>();

            foreach (var mark in root.GetComponentsInChildren<NovaWorld.NovaPlaced>())
            {
                if (mark == null) continue;
                var go = mark.gameObject;
                var rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float anchor = Mathf.Max(mark.targetSize, 1f);

                // 1) DEV OBJE: çıpanın 8 katından büyükse umutsuz → kaldır; 3 katından büyükse küçült
                if (maxDim > anchor * 8f)
                {
                    Debug.LogWarning($"[Nova Denetçi] KALDIRILDI (dev, {maxDim:0}m > {anchor * 8f:0}m): {mark.assetFile} ({mark.role})");
                    if (mark.linkedCollider != null) UnityEngine.Object.DestroyImmediate(mark.linkedCollider);
                    UnityEngine.Object.DestroyImmediate(go);
                    removed++;
                    continue;
                }
                if (maxDim > anchor * 3f)
                {
                    float s = anchor / maxDim;
                    go.transform.localScale *= s;
                    Debug.LogWarning($"[Nova Denetçi] KÜÇÜLTÜLDÜ ({maxDim:0}m → {anchor:0}m): {mark.assetFile} ({mark.role})");
                    shrunk++;
                    rends = go.GetComponentsInChildren<Renderer>();
                    b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                }

                // 2) UÇAN / GÖMÜK: zemin yüksekliğine oturt.
                // Eğimli arazide taban izinin 4 köşesinden EN DÜŞÜK zemin alınır — yerleştirmeyle
                // aynı kural; merkezden örnekleme yamaçtaki her objeyi boşuna oynatıyordu (92 düzeltme).
                float groundY = 0f;
                if (terrain != null)
                {
                    float ty = terrain.transform.position.y;
                    groundY = terrain.SampleHeight(new Vector3(b.min.x, 0f, b.min.z)) + ty;
                    groundY = Mathf.Min(groundY, terrain.SampleHeight(new Vector3(b.max.x, 0f, b.min.z)) + ty);
                    groundY = Mathf.Min(groundY, terrain.SampleHeight(new Vector3(b.min.x, 0f, b.max.z)) + ty);
                    groundY = Mathf.Min(groundY, terrain.SampleHeight(new Vector3(b.max.x, 0f, b.max.z)) + ty);
                }
                float dy = b.min.y - groundY;
                if (dy > 1.2f || dy < -1.2f)
                {
                    go.transform.position -= new Vector3(0f, dy, 0f);
                    grounded++;
                }
            }

            string report = $"Denetçi: {shrunk} küçültüldü · {removed} kaldırıldı · {grounded} yere oturtuldu";
            if (shrunk + removed + grounded > 0)
                Debug.Log($"[Nova Denetçi] {report} — detaylar yukarıdaki uyarılarda. Tekrarlayan suçluları assets-raw'dan silip 'npm run build' ile katalogdan düşürebilirsin.");
            log?.Invoke(report);
            return report;
        }
    }
}
