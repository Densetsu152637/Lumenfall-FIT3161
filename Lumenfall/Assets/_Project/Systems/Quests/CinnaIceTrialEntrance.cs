using UnityEngine;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Shared identifiers for the authored Campus entrance to Cinna Cirra's ice trial.
    /// The actual portal is the P.C05 GridPortal stored in the Campus area prefab.
    /// </summary>
    public static class CinnaIceTrialEntrance
    {
        public const string PortalId = "P.C05";
        public static readonly Vector2Int EntranceCell = new Vector2Int(18, 45);
    }
}
