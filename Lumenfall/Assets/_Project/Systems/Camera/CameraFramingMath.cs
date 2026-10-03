using System;

/// <summary>Deterministic framing rules; no engine lifecycle or native calls.</summary>
public static class CameraFramingMath
{
    /// <summary>Clamps an anchor to cell centers, collapsing excessive inset to room center.</summary>
    public static float ClampCoordinate(float coordinate, int minimumCell, int cellCount, float cellSize, float insetCells)
    {
        if (cellCount < 1 || !IsFinite(cellSize) || cellSize <= 0f || !IsFinite(coordinate) || !IsFinite(insetCells))
            throw new ArgumentOutOfRangeException(nameof(cellCount), "Framing requires finite coordinates and a positive grid size.");
        float maximumInset = (cellCount - 1) * 0.5f;
        float inset = Math.Min(Math.Max(0f, insetCells), maximumInset);
        float minimum = (minimumCell + inset) * cellSize;
        float maximum = (minimumCell + cellCount - 1 - inset) * cellSize;
        return Math.Min(Math.Max(coordinate, minimum), maximum);
    }

    public static bool ShouldSnap(float distanceSquared, float threshold)
    {
        return IsFinite(distanceSquared) && IsFinite(threshold) && threshold > 0f && distanceSquared >= threshold * threshold;
    }

    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
