using System;
using System.Collections.Generic;
using UnityEngine;

// Pure geometry: reject boundary contact and concave cut-outs, not just small planes.
public static class PlacementFootprintUtility
{
    private const float Epsilon = 0.0001f;

    public static bool ContainsPoint(IReadOnlyList<Vector2> boundary, Vector2 point) =>
        boundary != null && boundary.Count >= 3 && Inside(boundary, point);

    public static bool Fits(IReadOnlyList<Vector2> boundary, IReadOnlyList<Vector2> footprint)
    {
        if (boundary == null || boundary.Count < 3 || footprint == null || footprint.Count != 4)
            return false;
        for (int i = 0; i < footprint.Count; i++)
        {
            if (!Inside(boundary, footprint[i])) return false;
            Vector2 a = footprint[i], b = footprint[(i + 1) % footprint.Count];
            for (int j = 0; j < boundary.Count; j++)
                if (Intersects(a, b, boundary[j], boundary[(j + 1) % boundary.Count])) return false;
        }
        return true;
    }

    private static float Cross(Vector2 a, Vector2 b, Vector2 p) =>
        (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

    private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p) =>
        Math.Abs(Cross(a, b, p)) <= Epsilon &&
        p.x >= Math.Min(a.x, b.x) - Epsilon && p.x <= Math.Max(a.x, b.x) + Epsilon &&
        p.y >= Math.Min(a.y, b.y) - Epsilon && p.y <= Math.Max(a.y, b.y) + Epsilon;

    private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float ac = Cross(a, b, c), ad = Cross(a, b, d);
        float ca = Cross(c, d, a), cb = Cross(c, d, b);
        return ((ac > 0f && ad < 0f || ac < 0f && ad > 0f) &&
                (ca > 0f && cb < 0f || ca < 0f && cb > 0f)) ||
               OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b);
    }

    private static bool Inside(IReadOnlyList<Vector2> polygon, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[j], b = polygon[i];
            if (OnSegment(a, b, p)) return false;
            if ((a.y > p.y) != (b.y > p.y) &&
                p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }
}
