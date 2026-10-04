using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;

namespace Lumenfall.Spells
{
    /// Applies the seven documented spells to live world state. The spell-wheel key (Q by default) or middle mouse opens the spell wheel,
    /// the wheel selects a spell, and right mouse (or the interact key at a mechanism) casts at the nearest compatible object.
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
        private GameEventRuntime subscribedRuntime;
        private static readonly SpellId[] AllSpells = (SpellId[])Enum.GetValues(typeof(SpellId));
        private const string SpellFlagPrefix = "spell.unlocked.";
        private readonly Dictionary<SpellId, Texture2D> icons = new Dictionary<SpellId, Texture2D>();
        public SpellId SelectedSpell => selectedSpell;
        public bool SpellWheelOpen => spellWheelOpen;
        public string LastSearchFeedback { get; private set; }
        private float searchFeedbackUntil;
        public event Action<string> SearchFeedbackChanged;
        public event Action<SpellId, string, bool> CastCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Bootstrap()
        { GridMover player = FindAnyObjectByType<GridMover>(); if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>(); }
        private void OnEnable()
        { SubscribeProgression(); RefreshSelection(); }
        // Starting a new game rebuilds the event runtime, so follow the live instance rather than the one seen at enable time.
        private void SubscribeProgression()
        { GameEventRuntime runtime = GameEventManager.Instance?.Runtime; if (runtime == null || runtime == subscribedRuntime) return; UnsubscribeProgression(); subscribedRuntime = runtime; Lumenfall.Quests.RotaraGearMechanism.RegisterQuestConditions(runtime); runtime.QuestStatusChanged += OnQuestStatusChanged; runtime.DialogueEnded += OnDialogueEnded; runtime.StateRestored += OnProgressionRestored; OnProgressionRestored(); }
        private void UnsubscribeProgression()
        { if (subscribedRuntime == null) return; subscribedRuntime.QuestStatusChanged -= OnQuestStatusChanged; subscribedRuntime.DialogueEnded -= OnDialogueEnded; subscribedRuntime.StateRestored -= OnProgressionRestored; subscribedRuntime = null; }
        // Saves made while rewards were missed still hold the completed quests, so re-derive their spell rewards.
        private void OnProgressionRestored()
        {
            if (subscribedRuntime != null)
                foreach (QuestDefinition quest in subscribedRuntime.Catalog.Quests.Values)
                    if (quest != null && subscribedRuntime.GetQuestStatus(quest.id) == QuestStatus.Completed) GrantSpellRewards(quest.metadata);
            RefreshSelection();
        }
        private void OnQuestStatusChanged(QuestDefinition quest, QuestStatus previous, QuestStatus current)
        { if (current == QuestStatus.Completed && quest != null) GrantSpellRewards(quest.metadata); RefreshSelection(); }
        private void OnDialogueEnded(DialogueSession session)
        {
            if (session != null && session.IsCompleted)
            {
                string lessonQuest = MetadataValue(session.Definition?.metadata, "spellRewardQuestId");
                if (!string.IsNullOrWhiteSpace(lessonQuest))
                {
                    if (subscribedRuntime?.GetQuestStatus(lessonQuest) == QuestStatus.Active)
                        GrantSpellRewards(session.Definition.metadata);
                }
                else
                {
                    string reward = MetadataValue(session.Definition?.metadata, "spellReward");
                    if (TryParseSpell(reward, out SpellId spell) &&
                        (spell != SpellId.Ampere || subscribedRuntime?.GetQuestStatus("stella_first_light") == QuestStatus.Active))
                        subscribedRuntime?.SetFlag(SpellFlagPrefix + spell, true);
                }
            }
            RefreshSelection();
        }
        private void RefreshSelection() { SpellId[] spells = AvailableSpells; if (spells.Length > 0 && Array.IndexOf(spells, selectedSpell) < 0) selectedSpell = spells[0]; }
        private void Update()
        {
            SubscribeProgression();
            if (Time.timeScale <= 0f || InputBlockedByOverlay)
            {
                CloseSpellWheel();
                return;
            }
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if ((mouse != null && mouse.middleButton.wasPressedThisFrame) || KeyBindings.WasPressed(GameAction.SpellWheel))
            {
                if (spellWheelOpen) CloseSpellWheel();
                else OpenSpellWheel();
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
                else if (selectedSpell == SpellId.LostEchoes) SearchFeedback("No new trace within two cells. Search near Katrina's garden alcove.");
                CloseSpellWheel();
            }

            if (keyboard == null) return;
            Vector2 move = KeyBindings.MoveVector(); Vector2Int inputDirection = new Vector2Int((int)move.x, (int)move.y); if (inputDirection != Vector2Int.zero) lastDirection = inputDirection;
            if (KeyBindings.WasPressed(GameAction.Spell1)) SelectSpell(SpellId.Ampere);
            if (KeyBindings.WasPressed(GameAction.Spell2)) SelectSpell(SpellId.Weave);
            if (KeyBindings.WasPressed(GameAction.Spell3)) SelectSpell(SpellId.Gravitate);
            if (KeyBindings.WasPressed(GameAction.Spell4)) SelectSpell(SpellId.Rotara);
            if (KeyBindings.WasPressed(GameAction.Spell5)) SelectSpell(SpellId.Glaciate);
            if (KeyBindings.WasPressed(GameAction.Spell6)) SelectSpell(SpellId.Vis);
            if (KeyBindings.WasPressed(GameAction.Spell7)) SelectSpell(SpellId.LostEchoes);
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
        private void OpenSpellWheel()
        {
            if (spellWheelOpen || InputBlockedByOverlay || AvailableSpells.Length == 0) return;
            spellWheelOpen = true;
            wheelMovementLockOwned = true;
            GetComponent<GridMover>()?.SetMovementLocked(true);
        }
        /// Casts the selected spell at a specific object, as when the player presses F at a mechanism.
        public bool CastSelectedAt(string targetId)
        { CloseSpellWheel(); return !string.IsNullOrEmpty(targetId) && Cast(selectedSpell, targetId, DirectionFromPlayer()); }
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
            if (Time.unscaledTime < searchFeedbackUntil)
                GUI.Box(new Rect(20f, Screen.height - 74f, Mathf.Min(620f, Screen.width - 40f), 48f), LastSearchFeedback);
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
                if (GUI.Button(item, string.Empty)) { selectedSpell = spells[i]; GUI.color = previous; CloseSpellWheel(); return; }
                Texture2D icon = LoadIcon(spells[i]);
                if (icon != null) GUI.DrawTexture(new Rect(item.x + 4f, item.y + 3f, 26f, 26f), icon, ScaleMode.ScaleToFit, true);
                GUI.Label(new Rect(item.x + 34f, item.y + 7f, item.width - 36f, 22f), spells[i].ToString());
                GUI.color = previous;
            }
            GUI.Label(new Rect(center.x - 165f, center.y + 160f, 330f, 22f), "Click or scroll: select   " + KeyBindings.GetKeyLabel(GameAction.SpellWheel) + ": close   " + KeyBindings.GetKeyLabel(GameAction.Interact) + ": use on a mechanism");
        }
        public static string GetIconResourcePath(SpellId spell) { return "SpellIcons/" + spell; }
        private Texture2D LoadIcon(SpellId spell)
        { if (!icons.TryGetValue(spell, out Texture2D icon)) icons[spell] = icon = Resources.Load<Texture2D>(GetIconResourcePath(spell)); return icon; }
        public bool Cast(SpellId spell, string targetId, Vector2Int direction)
        {
            if (!IsSpellUnlocked(spell) || InputBlockedByOverlay) return false;
            WorldStateService state = WorldStateService.Instance; if (state == null || !state.IsReady || !state.TryGetObject(targetId, out WorldObjectSnapshot target) || !target.spellCompatible) return false;
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
                case SpellId.Rotara:
                    if (!Array.Exists(target.tags ?? Array.Empty<string>(), tag => string.Equals(tag, "rotara", StringComparison.OrdinalIgnoreCase)) ||
                        float.IsNaN(target.rotation) || float.IsInfinity(target.rotation)) break;
                    changed = state.SetProperty(targetId, x => x.rotation = Lumenfall.Quests.RotaraGearMechanism.NextQuarterTurn(x.rotation));
                    break;
                case SpellId.Glaciate: changed = FreezeSlide(state, targetId, ClampDirection(direction)); break;
                case SpellId.Vis: changed = state.SetProperty(targetId, x => { x.aligned = true; x.rotation = Mathf.Round(x.rotation / 90f) * 90f; }); break;
                case SpellId.LostEchoes:
                    GridMover player = GetComponent<GridMover>();
                    changed = player != null && state.TryRevealObject(targetId, player.CurrentCell, interactionRadius);
                    SearchFeedback(changed ? "An echo reveals a hidden point nearby." :
                        target.revealed ? "This point is already revealed. Search for another nearby trace." :
                        "No trace here. Stand within two cells of a hidden Lost Echoes point.");
                    break;
            }
            CastCompleted?.Invoke(spell, targetId, changed); return changed;
        }
        private void GrantSpellRewards(GameMetadataEntry[] metadata)
        {
            string reward = MetadataValue(metadata, "spellReward");
            foreach (string value in (reward ?? string.Empty).Split(',', ';'))
                if (TryParseSpell(value, out SpellId spell)) subscribedRuntime?.SetFlag(SpellFlagPrefix + spell, true);
        }
        private static bool TryParseSpell(string value, out SpellId spell) { return Enum.TryParse((value ?? string.Empty).Trim(), true, out spell) && Enum.IsDefined(typeof(SpellId), spell); }
        private static string MetadataValue(GameMetadataEntry[] metadata, string key) { foreach (GameMetadataEntry item in metadata ?? Array.Empty<GameMetadataEntry>()) if (item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase)) return item.value; return null; }
        private static bool WeaveToPowered(WorldStateService state, string targetId)
        {
            string intended = null;
            if (state.TryGetObject(targetId, out WorldObjectSnapshot target))
                foreach (string tag in target.tags ?? Array.Empty<string>())
                    if (tag.StartsWith("weave-with:", StringComparison.OrdinalIgnoreCase)) { intended = tag.Substring("weave-with:".Length); break; }
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
                if (item.id != targetId && item.powered && (string.IsNullOrWhiteSpace(intended) || string.Equals(item.id, intended, StringComparison.Ordinal)))
                    return state.AddConnection(targetId, item.id);
            return false;
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
        private void SearchFeedback(string message)
        { LastSearchFeedback = message; searchFeedbackUntil = Time.unscaledTime + 5f; SearchFeedbackChanged?.Invoke(message); }
        private string FindNearestTarget()
        { WorldStateService state = WorldStateService.Instance; GridMover player = GetComponent<GridMover>(); if (state == null || player == null) return null; string best = null; int distance = interactionRadius + 1; foreach (WorldObjectSnapshot item in state.SnapshotObjects()) { int d = Mathf.Abs(item.cell.x - player.CurrentCell.x) + Mathf.Abs(item.cell.y - player.CurrentCell.y); if (d <= interactionRadius && d < distance && item.spellCompatible && (selectedSpell != SpellId.LostEchoes || (!item.revealed && WorldStateService.IsLostEchoesTarget(item)))) { distance = d; best = item.id; } } return best; }
        private Vector2Int DirectionFromPlayer() { GridMover player = GetComponent<GridMover>(); Vector2Int movement = player == null ? Vector2Int.zero : (player.DestinationCell - player.CurrentCell); return movement == Vector2Int.zero ? lastDirection : movement; }
        private static Vector2Int ClampDirection(Vector2Int value) { if (value == Vector2Int.zero) return Vector2Int.up; return new Vector2Int(Mathf.Clamp(value.x, -1, 1), Mathf.Clamp(value.y, -1, 1)); }
    }
}
