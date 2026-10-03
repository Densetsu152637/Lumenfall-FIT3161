using System;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;

namespace Lumenfall.Spells
{
    public enum DormitoryPowerStatus
    {
        Disabled, Unavailable, WrongMap, MissingSpells, InvalidAuthoring, OutOfRange,
        NotStarted, SourceOff, CableMissing, InsufficientPower, ReadyToComplete, Completed, Restored,
        InputBlocked, InvalidCast, CastFailed
    }

    /// <summary>
    /// Explicit opt-in bridge for provisional optional content. Existing world/event providers
    /// own all durable state; this component owns only the current interaction session.
    /// No bootstrap or scene wiring activates it by default.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OptionalDormitoryPowerActivity : MonoBehaviour
    {
        public const string MapId = "B04.Ground";
        public const string SourceId = "power.dorm.optional.source";
        public const string SinkId = "power.dorm.optional.sink";
        public const string CompletionFlag = "optional.dormitory-power.completed";
        public const string AuthoringTag = "optional-dormitory-power";
        public const float MinimumPower = 0.2f;

        [SerializeField] private bool activityEnabled;
        [SerializeField] private WorldStateService worldState;
        [SerializeField] private SpellCaster caster;
        [SerializeField] private GridMover player;
        [SerializeField, Min(1)] private int interactionRadius = 2;
        private GameEventRuntime subscribedRuntime;
        private WorldStateService subscribedWorld;
        private bool started;
        private bool replay;
        private bool resetting;
        private float nextCheck;
        public bool ActivityEnabled => activityEnabled;
        public bool SessionStarted => started;
        public DormitoryPowerStatus Status { get; private set; } = DormitoryPowerStatus.Disabled;
        public string Feedback { get; private set; } = "Optional dormitory power is disabled.";
        public event Action<DormitoryPowerStatus, string> FeedbackChanged;

        /// <summary>Explicit authoring entry point; attach to an approved interaction or UI.</summary>
        public void Configure(bool enableActivity, WorldStateService state, SpellCaster spellCaster, GridMover mover)
        {
            Unsubscribe();
            activityEnabled = enableActivity;
            worldState = state;
            caster = spellCaster;
            player = mover;
            started = replay = false;
            RefreshStatus();
        }

        private void OnEnable() { if (activityEnabled) RefreshStatus(); }
        private void OnDisable() { started = replay = false; Unsubscribe(); Report(DormitoryPowerStatus.Disabled, "Optional dormitory power is disabled."); }
        private void Update()
        {
            // Runtime replacement is resolved here and at each public operation. Completion is
            // deferred until Update so a multi-provider restore cannot award midway through load.
            if (!activityEnabled || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.1f;
            Evaluate();
        }

        private GameEventRuntime Runtime
        {
            get
            {
                if (!activityEnabled) return null;
                GameEventRuntime current = GameEventManager.Instance.Runtime;
                if (current != subscribedRuntime || worldState != subscribedWorld)
                {
                    Unsubscribe();
                    started = replay = false;
                    if (isActiveAndEnabled)
                    {
                        subscribedRuntime = current;
                        subscribedWorld = worldState;
                        if (subscribedRuntime != null) subscribedRuntime.StateRestored += OnRestored;
                        if (subscribedWorld != null) subscribedWorld.StateChanged += OnWorldChanged;
                    }
                }
                return current;
            }
        }

        private void Unsubscribe()
        {
            if (subscribedRuntime != null) subscribedRuntime.StateRestored -= OnRestored;
            if (subscribedWorld != null) subscribedWorld.StateChanged -= OnWorldChanged;
            subscribedRuntime = null;
            subscribedWorld = null;
        }
        private void OnRestored() { started = replay = false; RefreshStatus(); }
        private void OnWorldChanged(MapStateSnapshot snapshot) { if (!resetting) RefreshStatus(); }

        public bool TryBegin()
        {
            if (!ValidateAccess(out GameEventRuntime runtime)) return false;
            if (runtime.GetFlag(CompletionFlag))
                return Report(DormitoryPowerStatus.Completed, "Power was already restored. Choose replay for practice; no additional reward.");
            started = true;
            replay = false;
            Evaluate();
            return true;
        }

        /// <summary>Ampere targets the source; Weave targets the sink, using the existing caster.</summary>
        public bool TryCast(SpellId spell, string targetId)
        {
            if (!ValidateAccess(out _)) return false;
            if (!started) return Report(DormitoryPowerStatus.NotStarted, "Begin the optional power activity first.");
            if ((spell != SpellId.Ampere || targetId != SourceId) && (spell != SpellId.Weave || targetId != SinkId))
                return Report(DormitoryPowerStatus.InvalidCast, "Use Ampere on the source, then Weave on the dormitory sink.");
            worldState.TryGetObject(targetId, out WorldObjectSnapshot target);
            if (!Near(target.cell)) return Report(DormitoryPowerStatus.OutOfRange, "Move closer to the intended power object.");
            if (!caster.Cast(spell, targetId, Vector2Int.up))
                return Report(DormitoryPowerStatus.CastFailed, "The spell made no change. Check source power and cable distance, or reset to retry.");
            Evaluate();
            return true;
        }

        public bool TryReset()
        {
            if (!ValidateAccess(out GameEventRuntime runtime)) return false;
            if (!started || (runtime.GetFlag(CompletionFlag) && !replay))
                return Report(DormitoryPowerStatus.NotStarted, "Begin the activity, or choose replay after completion, before resetting.");
            ResetAttempt();
            RefreshStatus();
            return true;
        }

        public bool TryReplay()
        {
            if (!ValidateAccess(out GameEventRuntime runtime)) return false;
            if (!runtime.GetFlag(CompletionFlag))
                return Report(DormitoryPowerStatus.NotStarted, "Complete the optional activity before choosing replay.");
            started = replay = true;
            ResetAttempt();
            RefreshStatus();
            return true;
        }

        private void ResetAttempt()
        {
            resetting = true;
            try
            {
                worldState.RemoveConnection(SourceId, SinkId);
                worldState.SetProperty(SourceId, value => value.powered = false);
                worldState.AcknowledgeEffect("lightning:" + SourceId + ":on");
                worldState.AcknowledgeEffect("lightning:" + SourceId + ":off");
            }
            finally { resetting = false; }
        }

        /// <summary>Checks the live intended connection, never trusting saved cable strength.</summary>
        public bool Evaluate()
        {
            if (!ValidateAccess(out GameEventRuntime runtime)) return false;
            if (runtime.GetFlag(CompletionFlag) && !replay)
                return Report(DormitoryPowerStatus.Completed, "Optional dormitory power completed. Replay is available without another reward.");
            if (!started) return Report(DormitoryPowerStatus.NotStarted, "Begin optional power restoration. Learn Ampere and Weave through their lessons first.");
            worldState.TryGetObject(SourceId, out WorldObjectSnapshot source);
            worldState.TryGetObject(SinkId, out WorldObjectSnapshot sink);
            if (!source.powered) return Report(DormitoryPowerStatus.SourceOff, "Cast Ampere on the dormitory power source.");
            if (!worldState.IsConnected(SourceId, SinkId)) return Report(DormitoryPowerStatus.CableMissing, "Cast Weave on the dormitory sink to connect the intended source.");
            float livePower = 1f / (1f + Vector2Int.Distance(source.cell, sink.cell));
            if (livePower < MinimumPower)
                return Report(DormitoryPowerStatus.InsufficientPower, "The intended cable is too long for effective power. Restore its approved layout before retrying.");
            if (!runtime.GetFlag(CompletionFlag)) runtime.SetFlag(CompletionFlag, true);
            started = replay = false;
            Report(DormitoryPowerStatus.Restored, "Dormitory power restored. Optional completion is saved; no item or currency reward.");
            return true;
        }

        public void RefreshStatus()
        {
            if (!ValidateAccess(out GameEventRuntime runtime)) return;
            if (runtime.GetFlag(CompletionFlag) && !replay)
                Report(DormitoryPowerStatus.Completed, "Optional dormitory power completed. Replay is available without another reward.");
            else if (!started) Report(DormitoryPowerStatus.NotStarted, "Begin optional power restoration with Ampere and Weave.");
            else if (!worldState.TryGetObject(SourceId, out WorldObjectSnapshot source) || !source.powered)
                Report(DormitoryPowerStatus.SourceOff, "Cast Ampere on the dormitory power source.");
            else if (!worldState.IsConnected(SourceId, SinkId))
                Report(DormitoryPowerStatus.CableMissing, "Cast Weave on the dormitory sink to connect the intended source.");
            else if (worldState.TryGetObject(SinkId, out WorldObjectSnapshot sink) &&
                1f / (1f + Vector2Int.Distance(source.cell, sink.cell)) < MinimumPower)
                Report(DormitoryPowerStatus.InsufficientPower, "The intended cable is too long for effective power. Restore its approved layout before retrying.");
            else Report(DormitoryPowerStatus.ReadyToComplete, "The grid has live power. Completion will be checked after the current state update.");
        }

        private bool ValidateAccess(out GameEventRuntime runtime)
        {
            runtime = null;
            if (!activityEnabled || !isActiveAndEnabled) return Report(DormitoryPowerStatus.Disabled, "Optional dormitory power is disabled.");
            runtime = Runtime;
            if (worldState == null || !worldState.IsReady || caster == null || player == null || runtime == null || worldState != WorldStateService.Instance || caster.gameObject != player.gameObject || !caster.isActiveAndEnabled || !player.isActiveAndEnabled)
                return Report(DormitoryPowerStatus.Unavailable, "Power activity dependencies or loaded world are unavailable.");
            if (!string.Equals(worldState.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase))
            {
                started = replay = false;
                return Report(DormitoryPowerStatus.WrongMap, "Return to the approved B04 dormitory area to begin; this activity never grants access to D05.");
            }
            if (!caster.IsSpellUnlocked(SpellId.Ampere) || !caster.IsSpellUnlocked(SpellId.Weave))
                return Report(DormitoryPowerStatus.MissingSpells, "Learn Ampere and Weave through their main quest lessons before this optional activity.");
            if (!worldState.TryGetObject(SourceId, out WorldObjectSnapshot source) ||
                !worldState.TryGetObject(SinkId, out WorldObjectSnapshot sink) ||
                !AuthoredEndpoint(source, SinkId) || !AuthoredEndpoint(sink, SourceId))
                return Report(DormitoryPowerStatus.InvalidAuthoring, "The dedicated power source/sink and reciprocal authoring tags are missing or incompatible.");
            if (Time.timeScale <= 0f || InventoryUiState.IsOpen || runtime.ActiveDialogue != null)
                return Report(DormitoryPowerStatus.InputBlocked, "Close the current overlay or resume before interacting with dormitory power.");
            if (!Near(source.cell) && !Near(sink.cell))
                return Report(DormitoryPowerStatus.OutOfRange, "Move within reach of the optional dormitory power objects.");
            return true;
        }

        private bool Near(Vector2Int cell) => Mathf.Abs(cell.x - player.CurrentCell.x) + Mathf.Abs(cell.y - player.CurrentCell.y) <= Mathf.Max(1, interactionRadius);
        private static bool AuthoredEndpoint(WorldObjectSnapshot endpoint, string intended)
        {
            if (!endpoint.spellCompatible || !string.Equals(endpoint.mapId, MapId, StringComparison.OrdinalIgnoreCase)) return false;
            bool dedicated = false;
            int weaveTags = 0;
            bool exact = false;
            foreach (string tag in endpoint.tags ?? Array.Empty<string>())
            {
                dedicated |= tag == AuthoringTag;
                if (tag != null && tag.StartsWith("weave-with:", StringComparison.OrdinalIgnoreCase))
                {
                    weaveTags++;
                    exact |= tag.Substring("weave-with:".Length) == intended;
                }
            }
            return dedicated && exact && weaveTags == 1;
        }
        private bool Report(DormitoryPowerStatus status, string message)
        {
            if (Status != status || Feedback != message)
            {
                Status = status;
                Feedback = message;
                FeedbackChanged?.Invoke(status, message);
            }
            return false;
        }
    }
}
