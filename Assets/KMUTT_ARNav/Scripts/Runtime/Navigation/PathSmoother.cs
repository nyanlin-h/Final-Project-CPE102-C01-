using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>
    /// "String pulling": removes zig-zags from a grid route by skipping waypoints that have a clear,
    /// wide-enough line of sight against the wall colliders. Works in map space; physics is queried in world space.
    /// </summary>
    public static class PathSmoother
    {
        public static List<Vector3> Smooth(IReadOnlyList<Vector3> mapPoints, Transform mapRoot, LayerMask wallMask,
                                           float probeHeight = 1.0f, float clearance = 0.25f)
        {
            var result = new List<Vector3>();
            if (mapPoints == null || mapPoints.Count == 0) return result;
            result.Add(mapPoints[0]);
            if (mapPoints.Count <= 2)
            {
                for (int i = 1; i < mapPoints.Count; i++) result.Add(mapPoints[i]);
                return result;
            }

            int anchor = 0;
            while (anchor < mapPoints.Count - 1)
            {
                int furthest = anchor + 1;
                for (int j = mapPoints.Count - 1; j > anchor + 1; j--)
                {
                    if (Mathf.Abs(mapPoints[j].y - mapPoints[anchor].y) > 0.3f) continue; // never skip across floors/ramps
                    if (Clear(mapPoints[anchor], mapPoints[j], mapRoot, wallMask, probeHeight, clearance)) { furthest = j; break; }
                }
                result.Add(mapPoints[furthest]);
                anchor = furthest;
            }
            return result;
        }

        static bool Clear(Vector3 aMap, Vector3 bMap, Transform mapRoot, LayerMask mask, float h, float r)
        {
            Vector3 a = mapRoot.TransformPoint(aMap + Vector3.up * h);
            Vector3 b = mapRoot.TransformPoint(bMap + Vector3.up * h);
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-3f) return true;
            return !Physics.SphereCast(a, r, d / len, out _, len, mask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Evenly spaced samples along a polyline (for placing arrows).</summary>
        public static List<(Vector3 pos, Vector3 dir)> Resample(IReadOnlyList<Vector3> pts, float spacing, float startOffset = 0.5f)
        {
            var outList = new List<(Vector3, Vector3)>();
            if (pts == null || pts.Count < 2) return outList;
            float carry = startOffset;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector3 seg = b - a;
                float len = seg.magnitude;
                if (len < 1e-4f) continue;
                Vector3 dir = seg / len;
                float t = carry;
                while (t < len)
                {
                    outList.Add((a + dir * t, dir));
                    t += spacing;
                }
                carry = t - len;
            }
            return outList;
        }
    }
}
