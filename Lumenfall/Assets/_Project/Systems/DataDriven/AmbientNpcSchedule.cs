using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;

namespace Lumenfall.DataDriven
{
    /// <summary>Event-counter phases are saved by GameEventRuntime; this adapter owns no save provider.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DynamicNpc), typeof(GridNpcNavigator))]
    public sealed class AmbientNpcSchedule : MonoBehaviour
    {
        private DynamicNpc npc;
        private GridNpcNavigator navigator;
        private GridMover mover;
        private float nextCheck;
        private void Awake()
        {
            npc = GetComponent<DynamicNpc>();
            navigator = GetComponent<GridNpcNavigator>();
            mover = GetComponent<GridMover>();
        }
        private void OnEnable() { nextCheck = 0; }
        private void OnDisable() { if (navigator != null) navigator.CancelNavigation(); }
        private void Update()
        {
            if (Time.timeScale <= 0) return;
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime.ActiveDialogue != null || InventoryUiState.IsOpen ||
                DataDrivenWorldLoader.Instance?.LoadedMap?.id != npc.Definition?.mapId ||
                !AmbientNpcPolicy.TryGetStop(npc.Definition, runtime, out AmbientNpcStopDefinition stop))
            {
                navigator.CancelNavigation();
                return;
            }
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            Vector2Int target = new Vector2Int(stop.x, stop.y);
            if (mover.CurrentCell != target && (!navigator.HasDestination || navigator.Destination != target))
                navigator.SetDestination(target); // Failed/blocked routes retry on the next bounded check.
        }
    }
}
