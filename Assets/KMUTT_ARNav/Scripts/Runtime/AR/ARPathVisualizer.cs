using System.Collections.Generic;
using UnityEngine;

namespace KMUTT.ARNav
{
    /// <summary>
    /// Draws the active floor's route: pooled chevrons ahead of the user, a destination pin or a
    /// stairs/elevator marker at the end, and an optional floor line. All spawned objects are children of the
    /// BuildingMap, so they inherit its ARAnchor and stay glued to the real hallway.
    /// </summary>
    public class ARPathVisualizer : MonoBehaviour
    {
        public BuildingMap map;

        [Header("Prefabs (KMUTT > AR Map Tools > Create Marker Prefabs)")]
        public GameObject arrowPrefab;
        public GameObject destinationPrefab;
        public GameObject transitionPrefab;

        [Header("Layout")]
        public float arrowSpacing = 1.0f;
        [Tooltip("Height of arrows above the floor. ~0.05 = painted on floor, ~0.9 = floating at hip height.")]
        public float arrowHeight = 0.25f;
        [Tooltip("Only show arrows within this distance ahead of the user (keeps the view clean and cheap).")]
        public float visibleAhead = 12f;
        public float hideBehind = 0.6f;
        public int maxArrows = 24;

        [Header("Animation")]
        public float pulseSpeed = 2.5f;
        public float pulseScale = 0.15f;
        public float pinBobHeight = 0.08f;

        [Header("Optional floor line")]
        public LineRenderer pathLine;

        readonly List<Transform> arrowPool = new List<Transform>();
        readonly List<(Vector3 pos, Vector3 dir, float s)> samples = new List<(Vector3, Vector3, float)>();
        List<Vector3> polyline;
        Transform endMarker;
        Vector3 endMarkerBase;
        bool visible;

        Transform Root => map.transform;

        /// <summary>Show a route polyline (map space, floor-level points). endIsTransition picks the end marker.</summary>
        public void ShowRoute(List<Vector3> mapPolyline, bool endIsTransition)
        {
            polyline = mapPolyline;
            samples.Clear();
            if (polyline == null || polyline.Count < 2) { Hide(); return; }

            float s = 0f;
            var res = PathSmoother.Resample(polyline, arrowSpacing, arrowSpacing * 0.5f);
            // cumulative arc length of each sample
            for (int i = 0; i < res.Count; i++)
            {
                s = i == 0 ? arrowSpacing * 0.5f : s + arrowSpacing;
                samples.Add((res[i].pos, res[i].dir, s));
            }

            // End marker
            var prefab = endIsTransition ? transitionPrefab : destinationPrefab;
            if (endMarker != null) Destroy(endMarker.gameObject);
            if (prefab != null)
            {
                endMarker = Instantiate(prefab, Root).transform;
                endMarkerBase = polyline[polyline.Count - 1];
                endMarker.localPosition = endMarkerBase;
                endMarker.localRotation = Quaternion.identity;
            }

            if (pathLine != null)
            {
                pathLine.useWorldSpace = false;
                pathLine.transform.SetParent(Root, false);
                pathLine.positionCount = polyline.Count;
                for (int i = 0; i < polyline.Count; i++) pathLine.SetPosition(i, polyline[i] + Vector3.up * 0.02f);
                pathLine.enabled = true;
            }
            visible = true;
        }

        public void Hide()
        {
            visible = false;
            foreach (var a in arrowPool) a.gameObject.SetActive(false);
            if (endMarker != null) endMarker.gameObject.SetActive(false);
            if (pathLine != null) pathLine.enabled = false;
        }

        /// <summary>Call every frame with the user's map-space position to update which arrows are shown.</summary>
        public void Tick(Vector3 userMap)
        {
            if (!visible || polyline == null) return;
            float userS = Polyline.Project(polyline, userMap, out _, out _);

            int used = 0;
            float t = Time.time;
            for (int i = 0; i < samples.Count && used < maxArrows; i++)
            {
                var smp = samples[i];
                if (smp.s < userS + hideBehind || smp.s > userS + visibleAhead) continue;
                var arrow = GetArrow(used++);
                arrow.localPosition = smp.pos + Vector3.up * arrowHeight;
                Vector3 flat = new Vector3(smp.dir.x, 0, smp.dir.z);
                if (flat.sqrMagnitude > 1e-6f) arrow.localRotation = Quaternion.LookRotation(flat, Vector3.up);
                // travelling pulse: arrows light up in sequence toward the destination
                float wave = 0.5f + 0.5f * Mathf.Sin(t * pulseSpeed - smp.s * 1.2f);
                arrow.localScale = Vector3.one * (1f + pulseScale * wave);
            }
            for (int i = used; i < arrowPool.Count; i++) arrowPool[i].gameObject.SetActive(false);

            if (endMarker != null)
            {
                endMarker.gameObject.SetActive(true);
                endMarker.localPosition = endMarkerBase + Vector3.up * (pinBobHeight * (0.5f + 0.5f * Mathf.Sin(t * 2f)));
                endMarker.Rotate(Vector3.up, 45f * Time.deltaTime, Space.Self);
            }
        }

        Transform GetArrow(int i)
        {
            while (arrowPool.Count <= i)
            {
                if (arrowPrefab == null) { Debug.LogError("[ARPathVisualizer] Arrow prefab not assigned"); enabled = false; break; }
                var go = Instantiate(arrowPrefab, Root);
                go.name = $"NavArrow_{arrowPool.Count}";
                arrowPool.Add(go.transform);
            }
            var a = arrowPool[Mathf.Min(i, arrowPool.Count - 1)];
            // If the map was re-anchored the pool must follow it
            if (a.parent != Root) a.SetParent(Root, false);
            a.gameObject.SetActive(true);
            return a;
        }
    }

    public static class Polyline
    {
        /// <summary>Projects p onto the polyline in XZ. Returns arc length at the projection; outputs distance and point.</summary>
        public static float Project(IReadOnlyList<Vector3> pts, Vector3 p, out float distance, out Vector3 closest)
        {
            distance = float.MaxValue; closest = pts.Count > 0 ? pts[0] : p;
            float bestS = 0f, acc = 0f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 a = pts[i], b = pts[i + 1];
                Vector2 a2 = new Vector2(a.x, a.z), b2 = new Vector2(b.x, b.z), p2 = new Vector2(p.x, p.z);
                Vector2 ab = b2 - a2;
                float len = ab.magnitude;
                float tt = len > 1e-5f ? Mathf.Clamp01(Vector2.Dot(p2 - a2, ab) / (len * len)) : 0f;
                Vector2 c = a2 + ab * tt;
                float d = Vector2.Distance(p2, c);
                if (d < distance) { distance = d; bestS = acc + tt * len; closest = Vector3.Lerp(a, b, tt); }
                acc += len;
            }
            return bestS;
        }

        public static float Length(IReadOnlyList<Vector3> pts)
        {
            float L = 0; for (int i = 0; i < pts.Count - 1; i++) L += Vector3.Distance(pts[i], pts[i + 1]); return L;
        }
    }
}
