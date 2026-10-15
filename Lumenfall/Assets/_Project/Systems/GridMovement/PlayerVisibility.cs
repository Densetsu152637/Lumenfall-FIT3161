using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// Hides the player on behalf of one or more owners, such as the startup menu while the world shows behind it and the
    /// opening arrival until the player appears. The player stays hidden while any owner still hides them. Only renderers
    /// this turned off are turned back on, so parts the game had switched off itself stay off. The player object, its
    /// movement and its collisions are untouched.
    /// </summary>
    public static class PlayerVisibility
    {
        private static readonly HashSet<object> owners = new HashSet<object>();
        private static readonly List<Renderer> hidden = new List<Renderer>();
        private static GridMover hiddenPlayer;
        private static bool showingForRender;

        /// <summary>True while the player is hidden (and not briefly shown for a thumbnail).</summary>
        public static bool IsHidden => owners.Count > 0 && !showingForRender;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            owners.Clear();
            hidden.Clear();
            hiddenPlayer = null;
            showingForRender = false;
        }

        public static void Hide(object owner)
        {
            if (owner == null) return;
            owners.Add(owner);
            Refresh();
        }

        public static void Show(object owner)
        {
            if (owner == null || !owners.Remove(owner) || owners.Count > 0) return;
            Restore();
        }

        /// <summary>
        /// Hides any player renderer switched on since the player was hidden, and follows a replaced player. Owners call
        /// this every frame while they hide the player.
        /// </summary>
        public static void Refresh()
        {
            if (owners.Count == 0) return;
            GridMover player = FindPlayer();
            if (player != hiddenPlayer)
            {
                Restore();
                hiddenPlayer = player;
            }
            if (player == null) return;
            foreach (Renderer part in player.GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled) continue;
                part.enabled = false;
                hidden.Add(part);
            }
        }

        /// <summary>
        /// Runs <paramref name="render"/> with the player visible, then hides them again; used so a save thumbnail taken
        /// while the menu is open still shows the player.
        /// </summary>
        public static void RenderVisible(Action render)
        {
            if (owners.Count == 0 || hidden.Count == 0)
            {
                render();
                return;
            }
            SetHiddenEnabled(true);
            showingForRender = true;
            try
            {
                render();
            }
            finally
            {
                showingForRender = false;
                SetHiddenEnabled(false);
            }
        }

        private static void Restore()
        {
            SetHiddenEnabled(true);
            hidden.Clear();
            hiddenPlayer = null;
        }

        private static void SetHiddenEnabled(bool enabled)
        {
            foreach (Renderer part in hidden)
                if (part != null) part.enabled = enabled;
        }

        // The startup menu disables GridPlayerInput, which still finds the component; NPCs with a GridMover have no input.
        private static GridMover FindPlayer()
        {
            GridPlayerInput input = UnityEngine.Object.FindAnyObjectByType<GridPlayerInput>();
            return input == null ? null : input.GetComponent<GridMover>();
        }
    }
}
