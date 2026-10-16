using System;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Three spells cast where no one would think to: Glaciate at the campus fountain, Rotara at the round table by the
    /// garden pavilion and Lost Echoes by the scorched shelf behind Prof. Voltaire in the Library. Juno's gossip hints at
    /// each in turn, round and round (fountain, table, Library), and Voltaire's lightning story hints at the shelf. Each one
    /// clicks a lock somewhere and is saved as a flag, in any order. Once all three have, the main hall door (out of
    /// cruxite as ever) has a small STAFF ONLY door beside it, which leads to the janitor's closet
    /// (<see cref="SecretRooms.ClosetId"/>), once: after the visit, that door is locked too. This component sits on the
    /// main hall door's blocked portal and is added to it when the campus loads.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StaffClosetSecret : MonoBehaviour, IBlockedPortalHandler
    {
        public const string MainHallPortalId = "P.C03";

        /// <summary>One of the three casts: where, which spell, and what the player is told.</summary>
        public readonly struct Lock
        {
            public readonly string Flag;
            public readonly SpellId Spell;
            public readonly string MapId;
            public readonly Vector2 Centre;
            public readonly float Reach;
            public readonly string Message;

            public Lock(string flag, SpellId spell, string mapId, float x, float y, float reach, string message)
            {
                Flag = flag;
                Spell = spell;
                MapId = mapId;
                Centre = new Vector2(x, y);
                Reach = reach;
                Message = message;
            }

            public bool Answers(SpellId spell, string mapId, Vector2Int cell) =>
                spell == Spell && string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase) && Vector2.Distance(cell, Centre) <= Reach;
        }

        public static readonly Lock[] Locks =
        {
            new Lock("secret.closet.fountain", SpellId.Glaciate, "campus", 26.25f, 45.5f, 3.6f,
                "The fountain's water freezes into the shape of a key, shines for a moment, and melts."),
            new Lock("secret.closet.table", SpellId.Rotara, "campus", 17f, 74f, 2.4f,
                "The round table spins with Rotara, faster and faster, then stops dead. Every teacup ring on it lines up."),
            new Lock("secret.closet.shelf", SpellId.LostEchoes, "B04.Ground", 7f, 18f, 3f,
                "Lost Echoes ripples along a scorched shelf. An echo whispers: \"Staff only.\""),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => SpellCaster.CastAttempted -= OnCastAttempted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SpellCaster.CastAttempted -= OnCastAttempted;
            SpellCaster.CastAttempted += OnCastAttempted;
        }

        public static bool AllUnlocked(GameEventRuntime runtime)
        {
            if (runtime == null) return false;
            foreach (Lock item in Locks) if (!runtime.GetFlag(item.Flag)) return false;
            return true;
        }

        /// <summary>Puts this handler on the main hall door when the campus loads.</summary>
        public static void RegisterForMap(string mapId, GameObject areaRoot)
        {
            if (areaRoot == null || !string.Equals(mapId, "campus", StringComparison.OrdinalIgnoreCase)) return;
            foreach (GridPortal portal in areaRoot.GetComponentsInChildren<GridPortal>(true))
                if (portal.Content != null && portal.Content.id == MainHallPortalId && portal.GetComponent<StaffClosetSecret>() == null)
                    portal.gameObject.AddComponent<StaffClosetSecret>();
        }

        private static void OnCastAttempted(SpellCaster.CastAttempt cast)
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (runtime == null) return;
            foreach (Lock item in Locks)
            {
                if (runtime.GetFlag(item.Flag) || !item.Answers(cast.Spell, cast.MapId, cast.Cell)) continue;
                runtime.SetFlag(item.Flag, true);
                string click = AllUnlocked(runtime) ? "Somewhere, a third lock clicks. Then a door creaks." : "Somewhere, a lock clicks.";
                PortalBlockedPopup.Show(item.Message + " " + click, "...?", 5f);
                cast.Handled = true;
                return;
            }
        }

        public bool HandleBlocked(GridPortal portal, GridMover mover)
        {
            GameEventRuntime runtime = GameEventManager.Instance != null ? GameEventManager.Instance.Runtime : null;
            if (!AllUnlocked(runtime)) return false;
            if (SecretRooms.HasVisited(runtime, SecretRooms.ClosetId))
            {
                PortalBlockedPopup.Show("Still out of cruxite. The STAFF ONLY door beside it is locked now, with a note: GONE MOPPING.", "Locked", 3.5f);
                return true;
            }
            PortalBlockedPopup.Show("The main hall is still out of cruxite. But beside it, a small door marked STAFF ONLY stands open.", "Staff only", 3.5f);
            SecretRooms.Enter(SecretRooms.ClosetId);
            return true;
        }
    }
}
