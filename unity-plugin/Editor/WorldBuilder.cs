using System;
using UnityEditor;
using UnityEngine;

namespace UnityAI
{
    /// <summary>
    /// World Builder v0 — deterministik prosedürel kasaba. Izgara → yol ağı → parseller → binalar → ağaçlar.
    /// Primitive'lerle (LLM/dış asset gerektirmez); collider'lı (gezilebilir). İleride primitive'ler
    /// gerçek modüler CC0 asset'lerle değişecek, plan bir LLM'den gelecek. Bu v0 yerleştirme motorunu kanıtlar.
    /// </summary>
    public static class WorldBuilder
    {
        public static GameObject BuildTown(int grid, int seed, float density, Action<string> log)
        {
            grid = Mathf.Clamp(grid, 2, 24);
            density = Mathf.Clamp01(density);
            var rnd = new System.Random(seed);

            const float cell = 8f;   // hücre boyutu (m)
            const float road = 2.6f; // yol genişliği
            float plot = cell - road;
            float total = grid * cell;

            var root = new GameObject($"NovaTown_{seed}");
            Undo.RegisterCreatedObjectUndo(root, "Nova: Kasaba kur");

            var mGround = SolidMat(new Color(0.30f, 0.34f, 0.28f));
            var mRoad = SolidMat(new Color(0.11f, 0.11f, 0.12f));
            var mRoof = SolidMat(new Color(0.36f, 0.20f, 0.18f));
            var mTrunk = SolidMat(new Color(0.34f, 0.23f, 0.14f));
            var mLeaf = SolidMat(new Color(0.20f, 0.44f, 0.22f));
            var mBuild = new[]
            {
                SolidMat(new Color(0.74f, 0.72f, 0.68f)),
                SolidMat(new Color(0.64f, 0.56f, 0.48f)),
                SolidMat(new Color(0.55f, 0.61f, 0.68f)),
                SolidMat(new Color(0.71f, 0.63f, 0.55f)),
            };

            // Zemin
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform);
            ground.transform.localScale = new Vector3(total + cell, 0.4f, total + cell);
            ground.transform.localPosition = new Vector3(total / 2f, -0.2f, total / 2f);
            SetMat(ground, mGround);

            // Yol ızgarası
            for (int i = 0; i <= grid; i++)
            {
                var rz = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rz.name = "Road_Z" + i; rz.transform.SetParent(root.transform);
                rz.transform.localScale = new Vector3(total + cell, 0.06f, road);
                rz.transform.localPosition = new Vector3(total / 2f, 0.03f, i * cell);
                SetMat(rz, mRoad);

                var rx = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rx.name = "Road_X" + i; rx.transform.SetParent(root.transform);
                rx.transform.localScale = new Vector3(road, 0.06f, total + cell);
                rx.transform.localPosition = new Vector3(i * cell, 0.03f, total / 2f);
                SetMat(rx, mRoad);
            }

            int buildings = 0, trees = 0;
            for (int x = 0; x < grid; x++)
            {
                for (int z = 0; z < grid; z++)
                {
                    float cx = x * cell + cell / 2f;
                    float cz = z * cell + cell / 2f;

                    if (rnd.NextDouble() < density)
                    {
                        float w = plot * (0.55f + 0.35f * (float)rnd.NextDouble());
                        float d = plot * (0.55f + 0.35f * (float)rnd.NextDouble());
                        float h = 3f + (float)rnd.NextDouble() * 13f;

                        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        b.name = "Building"; b.transform.SetParent(root.transform);
                        b.transform.localScale = new Vector3(w, h, d);
                        b.transform.localPosition = new Vector3(cx, h / 2f, cz);
                        SetMat(b, mBuild[rnd.Next(mBuild.Length)]);

                        var roofGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        roofGo.name = "Roof"; roofGo.transform.SetParent(b.transform);
                        roofGo.transform.localScale = new Vector3(1.06f, 0.06f, 1.06f);
                        roofGo.transform.localPosition = new Vector3(0f, 0.5f + 0.03f, 0f);
                        SetMat(roofGo, mRoof);
                        buildings++;
                    }
                    else
                    {
                        var t = new GameObject("Tree");
                        t.transform.SetParent(root.transform);
                        t.transform.localPosition = new Vector3(cx, 0f, cz);

                        var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        trunk.transform.SetParent(t.transform);
                        trunk.transform.localScale = new Vector3(0.4f, 1.2f, 0.4f);
                        trunk.transform.localPosition = new Vector3(0f, 1.2f, 0f);
                        SetMat(trunk, mTrunk);

                        var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                        leaf.transform.SetParent(t.transform);
                        leaf.transform.localScale = Vector3.one * (2f + (float)rnd.NextDouble() * 1.6f);
                        leaf.transform.localPosition = new Vector3(0f, 3f, 0f);
                        SetMat(leaf, mLeaf);
                        trees++;
                    }
                }
            }

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            log?.Invoke($"Kasaba kuruldu: {buildings} bina · {trees} ağaç · {grid}x{grid} ızgara. (Play'de gezilebilir)");
            return root;
        }

        private static Material SolidMat(Color c)
        {
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            var sh = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        private static void SetMat(GameObject go, Material m)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = m;
        }
    }
}
