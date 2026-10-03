using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;

namespace Lumenfall.Spells
{
    /// Applies the seven documented spells to live world state. Middle mouse opens the spell wheel,
    /// the wheel selects a spell, and right mouse casts at the nearest compatible object.
    [DisallowMultipleComponent]
    public sealed class SpellCaster : MonoBehaviour
    {
        [SerializeField] private SpellId selectedSpell = SpellId.Ampere;
        [SerializeField, Min(1)] private int interactionRadius = 2;
        [Tooltip("Optional designer allow-list. Empty uses authored quest rewards.")]
        [SerializeField] private SpellId[] unlockedSpells = Array.Empty<SpellId>();
        private Vector2Int lastDirection = Vector2Int.up;
        private bool spellWheelOpen;
        private bool wheelMovementLockOwned;
        private static readonly SpellId[] AllSpells = (SpellId[])Enum.GetValues(typeof(SpellId));
        private const string SpellFlagPrefix = "spell.unlocked.";
        private readonly Dictionary<SpellId, Texture2D> icons = new Dictionary<SpellId, Texture2D>();
        public SpellId SelectedSpell => selectedSpell;
        public bool SpellWheelOpen => spellWheelOpen;
        public string LastCastFeedback { get; private set; }
        private float feedbackUntil;
        public event Action<SpellId, string, bool> CastCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Bootstrap()
        { GridMover player = FindAnyObjectByType<GridMover>(); if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>(); }
        private void OnEnable()
        { SubscribeProgression(); RefreshSelection(); }
        private void SubscribeProgression()
        { GameEventRuntime runtime = GameEventManager.Instance?.Runtime; if (runtime == null) return; runtime.QuestStatusChanged += OnQuestStatusChanged; runtime.DialogueEnded += OnDialogueEnded; runtime.StateRestored += RefreshSelection; }
        private void UnsubscribeProgression()
        { GameEventRuntime runtime = GameEventManager.Instance?.Runtime; if (runtime == null) return; runtime.QuestStatusChanged -= OnQuestStatusChanged; runtime.DialogueEnded -= OnDialogueEnded; runtime.StateRestored -= RefreshSelection; }
        private void OnQuestStatusChanged(QuestDefinition quest, QuestStatus previous, QuestStatus current)
        { if (current == QuestStatus.Completed && quest != null) GrantSpellRewards(quest.metadata); RefreshSelection(); }
        private void OnDialogueEnded(DialogueSession session)
        {
            if (session != null && session.IsCompleted)
            {
                string reward = MetadataValue(session.Definition?.metadata, "spellReward");
                string rewardQuestId = MetadataValue(session.Definition?.metadata, "spellRewardQuestId");
                GameEventRuntime runtime = GameEventManager.Instance.Runtime;
                bool lessonActive = string.IsNullOrWhiteSpace(rewardQuestId) || runtime.GetQuestStatus(rewardQuestId) == QuestStatus.Active;
                foreach (string value in (reward ?? string.Empty).Split(',', ';'))
                    if (lessonActive && TryParseSpell(value, out SpellId spell) &&
                        (spell != SpellId.Ampere || runtime.GetQuestStatus("stella_first_light") == QuestStatus.Active) &&
                        (spell != SpellId.Weave || (runtime.GetQuestStatus("stella_string_them_together") == QuestStatus.Active &&
                            runtime.GetQuestStatus("stella_first_light") == QuestStatus.Completed)))
                        runtime.SetFlag(SpellFlagPrefix + spell, true);
            }
            RefreshSelection();
        }
        private void RefreshSelection() { SpellId[] spells = AvailableSpells; if (spells.Length > 0 && Array.IndexOf(spells, selectedSpell) < 0) selectedSpell = spells[0]; }
        private void Update()
        {
            if (Time.timeScale <= 0f || InputBlockedByOverlay)
            {
                CloseSpellWheel();
                return;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.middleButton.wasPressedThisFrame)
            {
                spellWheelOpen = !spellWheelOpen;
                wheelMovementLockOwned = spellWheelOpen;
                GetComponent<GridMover>()?.SetMovementLocked(spellWheelOpen);
            }
            if (mouse != null && spellWheelOpen)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) SelectSpellByOffset(scroll > 0f ? -1 : 1);
            }

            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
            {
                string target = FindNearestTarget();
                if (!string.IsNullOrEmpty(target)) Cast(selectedSpell, target, DirectionFromPlayer());
                CloseSpellWheel();
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            Vector2Int inputDirection = new Vector2Int((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)); if (inputDirection != Vector2Int.zero) lastDirection = inputDirection;
            if (keyboard.digit1Key.wasPressedThisFrame) SelectSpell(SpellId.Ampere);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectSpell(SpellId.Weave);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectSpell(SpellId.Gravitate);
            if (keyboard.digit4Key.wasPressedThisFrame) SelectSpell(SpellId.Rotara);
            if (keyboard.digit5Key.wasPressedThisFrame) SelectSpell(SpellId.Glaciate);
            if (keyboard.digit6Key.wasPressedThisFrame) SelectSpell(SpellId.Vis);
            if (keyboard.digit7Key.wasPressedThisFrame) SelectSpell(SpellId.LostEchoes);
        }

        private SpellId[] AvailableSpells
        {
            get
            {
                List<SpellId> available = new List<SpellId>();
                foreach (SpellId spell in AllSpells)
                {
                    bool allowed = IsProgressionUnlocked(spell) && (unlockedSpells == null || unlockedSpells.Length == 0 || Array.IndexOf(unlockedSpells, spell) >= 0);
                    if (allowed) available.Add(spell);
                }
                return available.ToArray();
            }
        }
        public bool IsSpellUnlocked(SpellId spell) { return Array.IndexOf(AvailableSpells, spell) >= 0; }
        private bool IsProgressionUnlocked(SpellId spell)
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime != null && runtime.GetFlag(SpellFlagPrefix + spell);
        }

        private void SelectSpell(SpellId spell)
        { if (Array.IndexOf(AvailableSpells, spell) >= 0) selectedSpell = spell; }

        private void SelectSpellByOffset(int offset)
        {
            SpellId[] spells = AvailableSpells;
            if (spells.Length == 0) return;
            int index = Array.IndexOf(spells, selectedSpell);
            if (index < 0) index = 0;
            selectedSpell = spells[(index + offset % spells.Length + spells.Length) % spells.Length];
        }

        private bool InputBlockedByOverlay => InventoryUiState.IsOpen || GameEventManager.Instance?.Runtime?.ActiveDialogue != null;
        private void CloseSpellWheel()
        {
            if (!spellWheelOpen) return;
            spellWheelOpen = false;
            // The inventory may have taken ownership of the same movement lock earlier
            // in this frame. Release only the lock owned by the wheel in that case.
            if (wheelMovementLockOwned && !InventoryUiState.IsOpen &&
                GameEventManager.Instance?.Runtime?.ActiveDialogue == null)
                GetComponent<GridMover>()?.SetMovementLocked(false);
            wheelMovementLockOwned = false;
        }
        private void OnDisable()
        { UnsubscribeProgression(); CloseSpellWheel(); }

        private void OnGUI()
        {
            if (InputBlockedByOverlay) return;
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(LastCastFeedback))
                GUI.Box(new Rect(Screen.width * 0.5f - 240f, Screen.height - 70f, 480f, 42f), LastCastFeedback);
            if (!spellWheelOpen) return;
            SpellId[] spells = AvailableSpells;
            if (spells.Length == 0) return;

            float radius = 112f;
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            GUI.Box(new Rect(center.x - 155f, center.y - 155f, 310f, 310f), "Spell wheel");
            GUI.Box(new Rect(center.x - 48f, center.y - 18f, 96f, 36f), selectedSpell.ToString());
            for (int i = 0; i < spells.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / spells.Length - Mathf.PI * 0.5f;
                Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                Rect item = new Rect(position.x - 52f, position.y - 16f, 104f, 32f);
                Color previous = GUI.color;
                GUI.color = spells[i] == selectedSpell ? new Color(0.55f, 1f, 0.65f, 1f) : Color.white;
                GUI.Box(item, string.Empty);
                Texture2D icon = LoadIcon(spells[i]);
                if (icon != null) GUI.DrawTexture(new Rect(item.x + 4f, item.y + 3f, 26f, 26f), icon, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(item.x + 34f, item.y + 7f, item.width - 36f, 22f), spells[i].ToString());
                GUI.color = previous;
            }
            GUI.Label(new Rect(center.x - 145f, center.y + 160f, 290f, 22f), "Scroll: select   Right click: cast   Middle click: close");
        }
        public static string GetIconResourcePath(SpellId spell) { return "SpellIcons/" + spell; }
        private Texture2D LoadIcon(SpellId spell)
        { if (!icons.TryGetValue(spell, out Texture2D icon)) icons[spell] = icon = Resources.Load<Texture2D>(GetIconResourcePath(spell)); return icon; }
        public bool Cast(SpellId spell, string targetId, Vector2Int direction)
        {
            if (!IsSpellUnlocked(spell) || InputBlockedByOverlay) return false;
            WorldStateService state = WorldStateService.Instance; if (state == null || !state.IsReady || !state.TryGetObject(targetId, out WorldObjectSnapshot target) || !target.spellCompatible) return false;
            int previousCableCount = spell == SpellId.Weave ? CountConnections(state, targetId) : 0;
            bool changed = false;
            switch (spell)
            {
                case SpellId.Ampere:
                    bool nextPower = !target.powered;
                    changed = state.SetProperty(targetId, x => x.powered = nextPower);
                    if (changed) state.QueueEffect("lightning:" + targetId + ":" + (nextPower ? "on" : "off"));
                    break;
                case SpellId.Weave: changed = WeaveToPowered(state, targetId); break;
                case SpellId.Gravitate: changed = state.TryMoveObject(targetId, ClampDirection(direction)); break;
                case SpellId.Rotara: changed = state.SetProperty(targetId, x => x.rotation = (Mathf.Round(x.rotation / 90f) * 90f + 90f) % 360f); break;
                case SpellId.Glaciate: changed = FreezeSlide(state, targetId, ClampDirection(direction)); break;
                case SpellId.Vis: changed = state.SetProperty(targetId, x => { x.aligned = true; x.rotation = Mathf.Round(x.rotation / 90f) * 90f; }); break;
                case SpellId.LostEchoes: changed = Reveal(state, targetId); break;
            }
            if (spell == SpellId.Weave)
            {
                bool added = CountConnections(state, targetId) > previousCableCount;
                LastCastFeedback = changed ? (added ? "Cable connected. Keep the power relic on." : "Cable removed. Cast Weave again to reconnect.") : "No live cable: power the relic and keep endpoints within four tiles.";
                feedbackUntil = Time.unscaledTime + 4f;
            }
            CastCompleted?.Invoke(spell, targetId, changed); return changed;
        }
        private static int CountConnections(WorldStateService state, string targetId)
        {
            int count = 0;
            foreach (WorldObjectSnapshot other in state.SnapshotObjects()) if (state.IsConnected(targetId, other.id)) count++;
            return count;
        }
        private void GrantSpellRewards(GameMetadataEntry[] metadata)
        {
            string reward = MetadataValue(metadata, "spellReward");
            foreach (string value in (reward ?? string.Empty).Split(',', ';'))
                if (TryParseSpell(value, out SpellId spell)) GameEventManager.Instance.Runtime.SetFlag(SpellFlagPrefix + spell, true);
        }
        private static bool TryParseSpell(string value, out SpellId spell) { return Enum.TryParse((value ?? string.Empty).Trim(), true, out spell) && Enum.IsDefined(typeof(SpellId), spell); }
        private static string MetadataValue(GameMetadataEntry[] metadata, string key) { foreach (GameMetadataEntry item in metadata ?? Array.Empty<GameMetadataEntry>()) if (item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase)) return item.value; return null; }
        private static bool WeaveToPowered(WorldStateService state, string targetId)
        {
            string intended = null;
            if (state.TryGetObject(targetId, out WorldObjectSnapshot target))
                foreach (string tag in target.tags ?? Array.Empty<string>())
                    if (tag.StartsWith("weave-with:", StringComparison.OrdinalIgnoreCase)) { intended = tag.Substring("weave-with:".Length); break; }
            if (!string.IsNullOrWhiteSpace(intended))
            {
                // Recasting removes the authored cable even if its source is now dark or too far away.
                if (state.IsConnected(targetId, intended)) return state.RemoveConnection(targetId, intended);
                if (!state.TryGetObject(intended, out WorldObjectSnapshot partner) || (!partner.powered && !target.powered)) return false;
                return state.AddConnection(targetId, intended);
            }
            WorldObjectSnapshot nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
            {
                if (item.id == targetId || !item.powered) continue;
                float distance = Vector2Int.Distance(target.cell, item.cell);
                if (distance < nearestDistance) { nearest = item; nearestDistance = distance; }
            }
            if (nearest == null) return false;
            return state.IsConnected(targetId, nearest.id) ? state.RemoveConnection(targetId, nearest.id) : state.AddConnection(targetId, nearest.id);
        }
        private static bool FreezeSlide(WorldStateService state, string id, Vector2Int direction)
        {
            bool changed = state.SetProperty(id, x => x.frozen = true);
            Vector2Int? goal = null;
            if (state.TryGetObject(id, out WorldObjectSnapshot target))
                foreach (string tag in target.tags ?? Array.Empty<string>())
                    if (tag.StartsWith("slide-goal:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = tag.Substring("slide-goal:".Length).Split(','); int x, y;
                        if (parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y)) goal = new Vector2Int(x, y);
                    }
            for (int i = 0; i < 128; i++)
            {
                if (goal.HasValue && state.TryGetObject(id, out WorldObjectSnapshot current) && current.cell == goal.Value) break;
                if (!state.TryMoveObject(id, direction)) break;
                changed = true;
            }
            return changed;
        }
        private static bool Reveal(WorldStateService state, string id)
        { bool changed = state.SetProperty(id, x => x.revealed = true); foreach (WorldObjectSnapshot item in state.SnapshotObjects()) if (Array.Exists(item.tags ?? Array.Empty<string>(), t => string.Equals(t, "hidden", StringComparison.OrdinalIgnoreCase))) changed |= state.SetProperty(item.id, x => x.revealed = true); return changed; }
        private string FindNearestTarget()
        { WorldStateService state = WorldStateService.Instance; GridMover player = GetComponent<GridMover>(); if (state == null || player == null) return null; string best = null; int distance = interactionRadius + 1; foreach (WorldObjectSnapshot item in state.SnapshotObjects()) { int d = Mathf.Abs(item.cell.x - player.CurrentCell.x) + Mathf.Abs(item.cell.y - player.CurrentCell.y); if (d <= interactionRadius && d < distance && item.spellCompatible) { distance = d; best = item.id; } } return best; }
        private Vector2Int DirectionFromPlayer() { GridMover player = GetComponent<GridMover>(); Vector2Int movement = player == null ? Vector2Int.zero : (player.DestinationCell - player.CurrentCell); return movement == Vector2Int.zero ? lastDirection : movement; }
        private static Vector2Int ClampDirection(Vector2Int value) { if (value == Vector2Int.zero) return Vector2Int.up; return new Vector2Int(Mathf.Clamp(value.x, -1, 1), Mathf.Clamp(value.y, -1, 1)); }
    }
}
