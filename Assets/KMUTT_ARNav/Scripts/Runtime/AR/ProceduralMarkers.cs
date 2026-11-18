using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>
    /// Low-poly marker meshes (≈50–150 tris each) so dozens of arrows stay cheap on mid-range phones.
    /// Used by the editor tool to bake prefabs; replace with your own art later if you like.
    /// </summary>
    public static class ProceduralMarkers
    {
        /// <summary>Flat chevron arrow lying in XZ, pointing +Z, pivot at center, ~0.44 m wide.</summary>
        public static Mesh CreateChevron(float thickness = 0.03f, float scale = 1f)
        {
            // Outline clockwise when seen from above (+Y)
            var o = new[]
            {
                new Vector2(0f, 0.20f), new Vector2(0.22f, -0.02f), new Vector2(0.22f, -0.16f),
                new Vector2(0f, 0.04f), new Vector2(-0.22f, -0.16f), new Vector2(-0.22f, -0.02f)
            };
            int[] capTris = { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };
            return Extrude(o, capTris, thickness, scale, "NavChevron");
        }

        /// <summary>Map-pin: downward cone with a sphere head. Tip at local origin (put it on the floor).</summary>
        public static Mesh CreatePin(float height = 0.55f, float headRadius = 0.13f, int segments = 16)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            float coneTop = height - headRadius;
            float coneR = headRadius * 0.75f;

            // Cone (tip at origin, opening upward) — side faces outward
            int tip = v.Count; v.Add(Vector3.zero);
            int ring = v.Count;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                v.Add(new Vector3(Mathf.Cos(a) * coneR, coneTop, Mathf.Sin(a) * coneR));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = ring + i, b = ring + (i + 1) % segments;
                t.Add(tip); t.Add(a); t.Add(b);
            }

            // UV sphere head
            int lat = segments / 2;
            int start = v.Count;
            Vector3 c = new Vector3(0, height - headRadius * 0.2f, 0);
            for (int y = 0; y <= lat; y++)
            {
                float phi = Mathf.PI * y / lat;
                for (int x = 0; x <= segments; x++)
                {
                    float th = 2f * Mathf.PI * x / segments;
                    v.Add(c + headRadius * new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)));
                }
            }
            int stride = segments + 1;
            for (int y = 0; y < lat; y++)
            for (int x = 0; x < segments; x++)
            {
                int i0 = start + y * stride + x, i1 = i0 + 1, i2 = i0 + stride, i3 = i2 + 1;
                t.Add(i0); t.Add(i1); t.Add(i2);
                t.Add(i1); t.Add(i3); t.Add(i2);
            }

            var m = new Mesh { name = "NavPin" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Flat ring (destination halo / floor-transition highlight), lying in XZ.</summary>
        public static Mesh CreateRing(float inner = 0.35f, float outer = 0.45f, int segments = 32)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * inner); v.Add(d * outer);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                // clockwise from above
                t.Add(a); t.Add(a + 2); t.Add(a + 1);
                t.Add(a + 1); t.Add(a + 2); t.Add(a + 3);
            }
            var m = new Mesh { name = "NavRing" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            return m;
        }

        static Mesh Extrude(Vector2[] outline, int[] capTris, float h, float s, string name)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            int n = outline.Length;

            // top cap (clockwise from above = faces +Y)
            int top = v.Count;
            foreach (var p in outline) v.Add(new Vector3(p.x * s, h, p.y * s));
            t.AddRange(System.Array.ConvertAll(capTris, i => i + top));

            // bottom cap (reversed)
            int bot = v.Count;
            foreach (var p in outline) v.Add(new Vector3(p.x * s, 0, p.y * s));
            for (int i = 0; i < capTris.Length; i += 3)
            { t.Add(bot + capTris[i]); t.Add(bot + capTris[i + 2]); t.Add(bot + capTris[i + 1]); }

            // sides: split verts for flat shading, both windings so they render regardless of culling
            for (int i = 0; i < n; i++)
            {
                var a = outline[i] * s; var b = outline[(i + 1) % n] * s;
                int k = v.Count;
                v.Add(new Vector3(a.x, 0, a.y)); v.Add(new Vector3(b.x, 0, b.y));
                v.Add(new Vector3(b.x, h, b.y)); v.Add(new Vector3(a.x, h, a.y));
                t.Add(k); t.Add(k + 1); t.Add(k + 2); t.Add(k); t.Add(k + 2); t.Add(k + 3);
                t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2);
            }

            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
