using System.Linq;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Background students and staff scattered across the maps so the academy feels lived in until the dorm and later
    /// story content exist. Each is an NPC catalog record (prefab, map, cell and dialogue) spawned through
    /// <see cref="StoryNpc"/>, so they appear whenever the player is on their map and need no area prefab changes.
    /// </summary>
    public sealed class AmbientCast : MonoBehaviour
    {
        /// <summary>Catalog ids of the cast, in no particular order.</summary>
        public static readonly string[] NpcIds = { "prof_voltaire", "vex", "luna", "juno", "rowan", "moss" };

        private static AmbientCast instance;

        private readonly StoryNpc[] cast = NpcIds.Select(id => new StoryNpc(id)).ToArray();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[Ambient Cast]");
            instance = host.AddComponent<AmbientCast>();
            DontDestroyOnLoad(host);
        }

        private void Update()
        {
            foreach (StoryNpc npc in cast) npc.Keep(true);
        }
    }
}
