using System;
using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// <summary>
    /// Resolution scaling for the IMGUI overlays. Menus, prompts and panels are authored in pixels for a window about
    /// <see cref="ReferenceHeight"/> tall; on a taller window everything is scaled up so it keeps the same share of the
    /// screen, instead of shrinking on a 1440p or 4K monitor. Smaller windows keep the authored size.
    /// Wrap an OnGUI body in <c>using (UiScale.Begin())</c> and lay out against <see cref="Width"/> and <see cref="Height"/>.
    /// Scopes nest: an inner Begin does nothing, so shared helpers can be called from scaled and unscaled hosts alike.
    /// </summary>
    public static class UiScale
    {
        public const float ReferenceHeight = 900f;

        private static int depth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => depth = 0;

        /// <summary>The scale a scope applies for the current window.</summary>
        public static float Factor => Mathf.Max(1f, Screen.height / ReferenceHeight);

        /// <summary>The scale in effect right now: <see cref="Factor"/> inside a scope, otherwise 1.</summary>
        public static float Current => depth > 0 ? Factor : 1f;

        /// <summary>The window width in the current IMGUI units.</summary>
        public static float Width => Screen.width / Current;

        /// <summary>The window height in the current IMGUI units.</summary>
        public static float Height => Screen.height / Current;

        /// <summary>Converts a point in real window pixels (top-left origin) to the current IMGUI units.</summary>
        public static Vector2 FromPixels(Vector2 pixels) => pixels / Current;

        /// <summary>A point from <see cref="Camera.WorldToScreenPoint"/> (bottom-left origin) in the current IMGUI units.</summary>
        public static Vector2 FromScreenPoint(Vector3 screen) => new Vector2(screen.x, Screen.height - screen.y) / Current;

        public static Scope Begin()
        {
            if (depth > 0) return default;
            Matrix4x4 previous = GUI.matrix;
            float factor = Factor;
            depth++;
            if (!Mathf.Approximately(factor, 1f)) GUI.matrix = previous * Matrix4x4.Scale(new Vector3(factor, factor, 1f));
            return new Scope(previous);
        }

        /// <summary>
        /// Rotates later IMGUI drawing about <paramref name="pivot"/>. Unlike GUIUtility.RotateAroundPivot, the pivot stays
        /// correct while a scale is applied. Restore GUI.matrix afterwards.
        /// </summary>
        public static void RotateAroundPivot(float degrees, Vector2 pivot)
        {
            GUI.matrix = GUI.matrix * Matrix4x4.TRS(pivot, Quaternion.Euler(0f, 0f, degrees), Vector3.one) *
                Matrix4x4.TRS(-pivot, Quaternion.identity, Vector3.one);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly Matrix4x4 previous;
            private readonly bool owns;

            internal Scope(Matrix4x4 previous)
            {
                this.previous = previous;
                owns = true;
            }

            public void Dispose()
            {
                if (!owns) return;
                GUI.matrix = previous;
                depth = Mathf.Max(0, depth - 1);
            }
        }
    }
}
