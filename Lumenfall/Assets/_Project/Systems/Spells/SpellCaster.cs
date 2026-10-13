using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Quests;

namespace Lumenfall.Spells
{
    /// Applies the seven documented spells to live world state. The spell-wheel key (Q by default) or middle mouse opens the spell wheel,
    /// the wheel selects a spell, and right mouse or the interact key casts at the nearest compatible object.
    [DisallowMultipleComponent]
    public sealed class SpellCaster : MonoBehaviour
    {
        [SerializeField] private SpellId selectedSpell = SpellId.Ampere;
        [SerializeField, Min(1)] private int interactionRadius = 2;
        [Tooltip("Manhattan search radius of a Lost Echoes cast, separate from the targeting radius of other spells.")]
        [SerializeField, Min(1)] private int lostEchoesRadius = 4;
        [Tooltip("Optional designer allow-list. Empty uses authored quest rewards.")]
        [SerializeField] private SpellId[] unlockedSpells = Array.Empty<SpellId>();
        [Header("Cast Animation")]
        [Tooltip("Optional. When empty, the first Animator below the player is used.")]
        [SerializeField] private Animator castAnimator;
        [Tooltip("Trigger parameter sent whenever the player attempts to cast an unlocked spell.")]
        [SerializeField] private string castTrigger = "Cast";
        private Vector2Int lastDirection = Vector2Int.up;
        private bool spellWheelOpen;
        private bool wheelMovementLockOwned;
        private GameEventRuntime subscribedRuntime;
        private static readonly SpellId[] AllSpells = (SpellId[])Enum.GetValues(typeof(SpellId));
        private const string SpellFlagPrefix = "spell.unlocked.";
        private const string SpellLessonFlagPrefix = "spell.lesson.";
        private const string AlignmentQuestId = "cinna_out_of_alignment";
        private const string AlignmentObjectId = CinnaAlignmentRelic.RelicId;
        private readonly Dictionary<SpellId, Texture2D> icons = new Dictionary<SpellId, Texture2D>();
        private readonly SpellWheelView wheelView = new SpellWheelView();
        public SpellId SelectedSpell => selectedSpell;
        public bool SpellWheelOpen => spellWheelOpen;
        public string LastCastFeedback { get; private set; }
        /// The circuit object a Weave thread is currently held from, or null. The next Weave cast on another
        /// circuit object ties the thread to it.
        public string HeldThread { get; private set; }
        private string previousThreadNode;
        private float feedbackUntil;
        private float nextScrollAt;
        private const float ScrollCooldown = 0.06f;
        private const float ChipClearanceOverRotaraPanel = 104f;
        private const string CircuitTag = "circuit";
        private const string AnchoredTag = "anchored";
        public event Action<SpellId, string, bool> CastCompleted;
        /// <summary>Raised for every Lost Echoes search with the caster's cell, the search radius and how many objects were found.</summary>
        public event Action<Vector2Int, int, int> LostEchoesCast;
        public int LostEchoesRadius => lostEchoesRadius;
        private int lastLostEchoesFrame = -1;
        private const string VisLiftTag = "vis-lift";
        private const string VisDestinationPrefix = "vis-destination:";
        private const string VisStandPrefix = "vis-stand:";
        /// Progression flag prefix set once a carried relic is placed on its stand; the relic then leaves its home map.
        public const string VisDeliveredFlagPrefix = "vis.delivered.";
        /// The relic Vis is carrying, or null. Holding is not saved: the relic stays at its last safe placement.
        public string HeldRelic { get; private set; }
        private string heldRelicHomeMap;
        private string heldRelicDestinationMap;
        public event Action<string> VisLifted;
        /// A held relic was set down: its id, the tile, and whether it was delivered to its stand.
        public event Action<string, Vector2Int, bool> VisPlaced;
        /// A placement was refused: the relic id, the tile, and whether a blocked tile (rather than the wrong place) refused it.
        public event Action<string, Vector2Int, bool> VisPlaceRefused;
        /// A held relic was let go without being placed (area change, reset); it stays at its last safe placement.
        public event Action<string> VisHoldReleased;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Bootstrap()
        { GridMover player = FindAnyObjectByType<GridMover>(); if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>(); }
        private void OnEnable()
        { SubscribeProgression(); RefreshSelection(); }
        // Starting a new game rebuilds the event runtime, so follow the live instance rather than the one seen at enable time.
        private void SubscribeProgression()
        { GameEventRuntime runtime = GameEventManager.Instance?.Runtime; if (runtime == null || runtime == subscribedRuntime) return; UnsubscribeProgression(); subscribedRuntime = runtime; Lumenfall.Quests.OrioRuneDiscPuzzle.RegisterQuestConditions(runtime); runtime.QuestStatusChanged += OnQuestStatusChanged; runtime.DialogueEnded += OnDialogueEnded; runtime.StateRestored += OnProgressionRestored; OnProgressionRestored(); }
        private void UnsubscribeProgression()
        { if (subscribedRuntime == null) return; subscribedRuntime.QuestStatusChanged -= OnQuestStatusChanged; subscribedRuntime.DialogueEnded -= OnDialogueEnded; subscribedRuntime.StateRestored -= OnProgressionRestored; subscribedRuntime = null; }
        // Saves made while rewards were missed still hold the completed quests, so re-derive their spell rewards.
        private void OnProgressionRestored()
        {
            // A new game or a loaded save starts with empty hands; the relic is wherever world state has it.
            if (HeldRelic != null) ReleaseHeldRelic(null);
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
        private static bool CanGrantLessonReward(GameMetadataEntry[] metadata)
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            string questId = MetadataValue(metadata, "spellRewardQuestId");
            if (string.IsNullOrWhiteSpace(questId))
            {
                // Preserve the original First Light terminal lesson contract.
                if (!string.Equals(MetadataValue(metadata, "spellReward"), "Ampere", StringComparison.OrdinalIgnoreCase)) return false;
                questId = "stella_first_light";
            }
            if (runtime.GetQuestStatus(questId) != QuestStatus.Active) return false;
            string prerequisite = MetadataValue(metadata, "spellRewardPrerequisite");
            if (string.IsNullOrWhiteSpace(prerequisite)) return true;
            string[] parts = prerequisite.Split(':');
            return parts.Length == 2 && Enum.TryParse(parts[1], true, out QuestStatus status) && runtime.GetQuestStatus(parts[0]) == status;
        }
        private void RefreshSelection() { SpellId[] spells = AvailableSpells; if (spells.Length > 0 && Array.IndexOf(spells, selectedSpell) < 0) selectedSpell = spells[0]; }
        private void Update()
        {
            SubscribeProgression();
            // A held thread belongs to Weave on the current map; switching spells or maps drops it.
            if (HeldThread != null && (selectedSpell != SpellId.Weave || WorldStateService.Instance == null || !WorldStateService.Instance.TryGetObject(HeldThread, out _)))
                ClearHeldThread();
            ReleaseHeldRelicOutOfReach();
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
                // A short cooldown keeps smooth trackpad scrolling from racing through every spell.
                if (Mathf.Abs(scroll) > 0.01f && Time.unscaledTime >= nextScrollAt)
                {
                    SelectSpellByOffset(scroll > 0f ? -1 : 1);
                    wheelView.Scrolled();
                    nextScrollAt = Time.unscaledTime + ScrollCooldown;
                }
            }

            bool castPressed = (mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                               KeyBindings.WasPressed(GameAction.Interact);
            if (castPressed)
            {
                // Right mouse and the interact key share one cast path, so a single press never lifts and then
                // drops a Vis relic, or freezes and then thaws an anchored Glaciate target, in the same frame.
                CastSelectedFromInput();
                CloseSpellWheel();
            }

            if (keyboard == null) return;
            if (keyboard.rKey.wasPressedThisFrame && IsNearAlignmentMarker()) ResetActiveAlignmentPuzzle();
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
        /// <summary>True once the spell is permanently learned; lesson-only access during its quest does not count.</summary>
        public bool IsSpellLearned(SpellId spell)
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return IsSpellUnlocked(spell) && runtime != null && runtime.GetFlag(SpellFlagPrefix + spell);
        }
        private bool IsProgressionUnlocked(SpellId spell)
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime != null && (runtime.GetFlag(SpellFlagPrefix + spell) || runtime.GetFlag(SpellLessonFlagPrefix + spell));
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

        private bool InputBlockedByOverlay => Startup.StartupMenu.IsOpen || QuestHintOverlay.IsSettingsOpen ||InventoryUiState.IsOpen || CinnaTrialDeparture.InProgress ||
            GameEventManager.Instance?.Runtime?.ActiveDialogue != null;
        private void OpenSpellWheel()
        {
            if (spellWheelOpen || InputBlockedByOverlay || AvailableSpells.Length == 0) return;
            spellWheelOpen = true;
            wheelView.Open();
            wheelMovementLockOwned = true;
            GetComponent<GridMover>()?.SetMovementLocked(true);
        }
        private bool CastSelectedFromInput()
        {
            if (!IsSpellUnlocked(selectedSpell)) return false;
            TriggerCastAnimation();
            // Lost Echoes searches the area around the player, so it needs no target.
            if (selectedSpell == SpellId.LostEchoes) return CastLostEchoes();
            // Vis sets a held relic down on the tile ahead instead of looking for a target.
            if (selectedSpell == SpellId.Vis && HeldRelic != null)
                return TryGetPlacementCell(out Vector2Int cell) && TryPlaceHeldRelic(cell);
            string target = FindNearestTarget();
            if (!string.IsNullOrEmpty(target)) return Cast(selectedSpell, target, DirectionFromPlayer());
            return selectedSpell == SpellId.Weave && CancelHeldThread();
        }

        /// Casts the selected spell at a specific object and plays the same animation as player input.
        public bool CastSelectedAt(string targetId)
        {
            CloseSpellWheel();
            if (string.IsNullOrEmpty(targetId) || !IsSpellUnlocked(selectedSpell)) return false;
            TriggerCastAnimation();
            return Cast(selectedSpell, targetId, DirectionFromPlayer());
        }

        /// <summary>Lets other player-control scripts request the configured cast animation.</summary>
        public void TriggerCastAnimation()
        {
            if (castAnimator == null) castAnimator = GetComponentInChildren<Animator>(true);
            if (castAnimator == null || string.IsNullOrWhiteSpace(castTrigger)) return;
            int triggerHash = Animator.StringToHash(castTrigger);
            foreach (AnimatorControllerParameter parameter in castAnimator.parameters)
            {
                if (parameter.nameHash != triggerHash || parameter.type != AnimatorControllerParameterType.Trigger) continue;
                castAnimator.ResetTrigger(triggerHash);
                castAnimator.SetTrigger(triggerHash);
                return;
            }
        }

        /// <summary>
        /// Releases the active Weave thread. If it was advanced by the previous cast, the newest cable is removed too.
        /// A later cast on any circuit node starts a fresh thread from that node.
        /// </summary>
        public bool CancelHeldThread()
        {
            if (HeldThread == null) return false;
            WorldStateService state = WorldStateService.Instance;
            bool removed = state != null && !string.IsNullOrEmpty(previousThreadNode) &&
                           state.IsConnected(previousThreadNode, HeldThread) &&
                           state.RemoveConnection(previousThreadNode, HeldThread);
            ClearHeldThread();
            ShowFeedback(removed
                ? "Newest cable disconnected. Cast Weave on a relic to start again from it."
                : "Thread released. Cast Weave on a relic to start again from it.");
            return removed;
        }

        private void ClearHeldThread()
        {
            HeldThread = null;
            previousThreadNode = null;
        }
        private void CloseSpellWheel(SpellId? picked = null)
        {
            if (!spellWheelOpen) return;
            spellWheelOpen = false;
            wheelView.Close(picked);
            // The inventory may have taken ownership of the same movement lock earlier
            // in this frame. Release only the lock owned by the wheel in that case.
            if (wheelMovementLockOwned && !InventoryUiState.IsOpen &&
                GameEventManager.Instance?.Runtime?.ActiveDialogue == null)
                GetComponent<GridMover>()?.SetMovementLocked(false);
            wheelMovementLockOwned = false;
        }
        private void OnDisable()
        { UnsubscribeProgression(); CloseSpellWheel(); }
        private void OnDestroy() { wheelView.Dispose(); }

        private void OnGUI()
        {
            if (InputBlockedByOverlay) return;
            if (Time.unscaledTime < feedbackUntil && !string.IsNullOrEmpty(LastCastFeedback))
                GUI.Box(new Rect(Screen.width * 0.5f - 240f, Screen.height - 70f, 480f, 42f), LastCastFeedback);
            if (!wheelView.Visible)
            {
                // The chip only paints on repaint, so skip building the spell list for other events.
                if (Event.current.type == EventType.Repaint && AvailableSpells.Length > 0)
                    wheelView.DrawChip(selectedSpell, LoadIcon, KeyBindings.GetKeyLabel(GameAction.SpellWheel),
                        Lumenfall.Quests.RotaraGearMechanism.PanelVisible ? ChipClearanceOverRotaraPanel : 0f);
                return;
            }
            SpellId[] spells = AvailableSpells;
            if (spells.Length == 0) return;
            string hint = "Click or scroll: select   " + KeyBindings.GetKeyLabel(GameAction.SpellWheel) + ": close   Right click or " + KeyBindings.GetKeyLabel(GameAction.Interact) + ": cast";
            SpellId? picked = wheelView.Draw(spells, selectedSpell, LoadIcon, hint);
            if (picked.HasValue) { selectedSpell = picked.Value; CloseSpellWheel(picked); }
        }
        /// <summary>
        /// Casts Lost Echoes as an area search: every hidden, spell-compatible object within the interaction radius of
        /// the caster is revealed. Any object authored with the <c>hidden</c> tag is a target, so it is not tied to a quest.
        /// </summary>
        public bool CastLostEchoes()
        {
            CloseSpellWheel();
            if (!IsSpellUnlocked(SpellId.LostEchoes) || InputBlockedByOverlay) return false;
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !state.IsReady) return false;
            List<string> revealed = new List<string>();
            RevealHiddenAroundPlayer(state, revealed);
            foreach (string id in revealed) CastCompleted?.Invoke(SpellId.LostEchoes, id, true);
            return revealed.Count > 0;
        }
        // Results are shown by LostEchoesPresentation (pulse, glow and sound), so the search reports no text.
        private int RevealHiddenAroundPlayer(WorldStateService state, List<string> revealed = null)
        {
            // Mechanism handlers may also cast the selected spell on the same key press; search only once per frame.
            if (lastLostEchoesFrame == Time.frameCount) return 0;
            lastLostEchoesFrame = Time.frameCount;
            GridMover player = GetComponent<GridMover>();
            int found = player == null ? 0 : state.RevealHiddenAround(player.CurrentCell, lostEchoesRadius, revealed);
            if (player != null) LostEchoesCast?.Invoke(player.CurrentCell, lostEchoesRadius, found);
            return found;
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
                    if (WorldStateService.IsConductor(target)) { ShowFeedback("This relic holds no charge of its own. Weave it to a powered relic."); break; }
                    if (StellaStringThemTogetherPuzzle.TryApplyAmpereResonance(targetId))
                    {
                        changed = true;
                        ShowFeedback("The relic resonates with the others.");
                        break;
                    }
                    bool nextPower = !target.powered;
                    changed = state.SetProperty(targetId, x => x.powered = nextPower);
                    if (changed) state.QueueEffect("lightning:" + targetId + ":" + (nextPower ? "on" : "off"));
                    break;
                case SpellId.Weave: changed = HasTag(target, CircuitTag) ? WeaveCircuit(state, targetId) : WeaveToPowered(state, targetId); break;
                case SpellId.Gravitate: changed = state.TryMoveObject(targetId, ClampDirection(direction)); break;
                case SpellId.Rotara:
                    if (!Array.Exists(target.tags ?? Array.Empty<string>(), tag => string.Equals(tag, "rotara", StringComparison.OrdinalIgnoreCase)) ||
                        float.IsNaN(target.rotation) || float.IsInfinity(target.rotation)) break;
                    // A linked socket turns every disc it touches; the socket turns too, which drives its press feedback.
                    if (Lumenfall.Quests.OrioRuneDiscPuzzle.TryGetLinkedDiscs(state.CurrentMapId, targetId, out string[] linkedDiscs))
                        foreach (string disc in linkedDiscs)
                            if (state.TryGetObject(disc, out WorldObjectSnapshot linked) && !float.IsNaN(linked.rotation) && !float.IsInfinity(linked.rotation))
                                state.SetProperty(disc, x => x.rotation = Lumenfall.Quests.RotaraGearMechanism.NextQuarterTurn(x.rotation));
                    changed = state.SetProperty(targetId, x => x.rotation = Lumenfall.Quests.RotaraGearMechanism.NextQuarterTurn(x.rotation));
                    break;
                case SpellId.Glaciate:
                    changed = HasTag(target, AnchoredTag) && target.frozen ? ThawAnchored(state, targetId) : FreezeSlide(state, targetId, ClampDirection(direction));
                    break;
                case SpellId.Vis:
                    if (HasTag(target, VisLiftTag)) { changed = LiftRelic(state, target); break; }
                    if (TryGetVisRotation(target, out float visRotation) &&
                        (!target.aligned || !Mathf.Approximately(NormalizeRotation(target.rotation), visRotation)))
                        changed = state.SetProperty(targetId, x => { x.aligned = true; x.rotation = visRotation; });
                    break;
                case SpellId.LostEchoes:
                    changed = RevealHiddenAroundPlayer(state) > 0;
                    break;
            }
            if (spell == SpellId.Weave && !HasTag(target, CircuitTag))
            {
                bool added = CountConnections(state, targetId) > previousCableCount;
                ShowFeedback(changed ? (added ? "Cable connected. Keep the power relic on." : "Cable removed. Cast Weave again to reconnect.") : "No live cable: power the relic and keep endpoints within four tiles.");
            }
            CastCompleted?.Invoke(spell, targetId, changed); return changed;
        }
        private void ShowFeedback(string message) { LastCastFeedback = message; feedbackUntil = Time.unscaledTime + 4f; }
        private static bool HasTag(WorldObjectSnapshot item, string tag) => Array.Exists(item.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
        /// Circuit objects are wired by hand: the first cast holds a thread, each later cast ties it to the next
        /// object and keeps holding it there so a chain can be drawn relay by relay. Casting on a linked pair cuts it.
        private bool WeaveCircuit(WorldStateService state, string targetId)
        {
            if (HeldThread == null || !state.TryGetObject(HeldThread, out _))
            {
                HeldThread = targetId;
                previousThreadNode = null;
                ShowFeedback("Thread held. Cast Weave on another relic within four tiles to tie it.");
                return false;
            }
            string from = HeldThread;
            if (from == targetId) { ClearHeldThread(); ShowFeedback("Thread released."); return false; }
            if (state.IsConnected(from, targetId))
            {
                ClearHeldThread();
                ShowFeedback("Cable cut. Cast Weave on a relic to hold a new thread.");
                return state.RemoveConnection(from, targetId);
            }
            if (!state.AddConnection(from, targetId))
            {
                ShowFeedback("Too far: tie the thread to a relic within four tiles. Still holding the thread.");
                return false;
            }
            HeldThread = targetId;
            previousThreadNode = from;
            bool lit = state.TryGetObject(targetId, out WorldObjectSnapshot tied) && tied.powered;
            ShowFeedback(lit ? "Cable tied and carrying power. Keep weaving from here." : "Cable tied. It lights once the chain reaches a powered relic.");
            return true;
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
                if (TryParseSpell(value, out SpellId spell)) subscribedRuntime?.SetFlag(SpellFlagPrefix + spell, true);
        }
        private static bool TryParseSpell(string value, out SpellId spell) { return Enum.TryParse((value ?? string.Empty).Trim(), true, out spell) && Enum.IsDefined(typeof(SpellId), spell); }
        private static string MetadataValue(GameMetadataEntry[] metadata, string key) { foreach (GameMetadataEntry item in metadata ?? Array.Empty<GameMetadataEntry>()) if (item != null && string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase)) return item.value; return null; }
        private static bool TryGetVisRotation(WorldObjectSnapshot target, out float rotation)
        {
            rotation = NormalizeRotation(Mathf.Round(target.rotation / 90f) * 90f);
            if (!Array.Exists(target.tags ?? Array.Empty<string>(), tag => string.Equals(tag, "vis", StringComparison.OrdinalIgnoreCase))) return false;
            bool authored = false;
            foreach (string tag in target.tags ?? Array.Empty<string>())
            {
                if (tag == null || !tag.StartsWith("vis-target", StringComparison.OrdinalIgnoreCase)) continue;
                if (authored || !tag.StartsWith("vis-target:", StringComparison.OrdinalIgnoreCase) ||
                    !int.TryParse(tag.Substring("vis-target:".Length), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out int angle) || angle % 90 != 0) return false;
                rotation = ((angle % 360) + 360) % 360;
                authored = true;
            }
            return true;
        }
        private static float NormalizeRotation(float angle) { angle %= 360f; return angle < 0f ? angle + 360f : angle; }
        /// <summary>
        /// Lifts a <c>vis-lift</c> relic. World state does not move while it is carried: the relic stays registered at
        /// its last safe placement, so leaving its maps or loading a save simply leaves it there.
        /// </summary>
        private bool LiftRelic(WorldStateService state, WorldObjectSnapshot target)
        {
            if (HeldRelic != null) { ShowFeedback("Vis is already holding a relic. Set it down first."); return false; }
            HeldRelic = target.id;
            heldRelicHomeMap = state.CurrentMapId;
            heldRelicDestinationMap = TagValue(target, VisDestinationPrefix);
            VisLifted?.Invoke(target.id);
            return true;
        }
        /// <summary>The tile a held relic would be set down on: the one the player is facing.</summary>
        public bool TryGetPlacementCell(out Vector2Int cell)
        {
            GridMover player = GetComponent<GridMover>();
            Vector2Int facing = DirectionFromPlayer();
            // A diagonal facing keeps its horizontal part, so the relic always lands on a neighbouring tile.
            facing = facing.x != 0 ? new Vector2Int(Math.Sign(facing.x), 0) : new Vector2Int(0, Math.Sign(facing.y));
            cell = player == null ? default : player.CurrentCell + facing;
            return player != null && HeldRelic != null && facing != Vector2Int.zero;
        }
        /// <summary>
        /// Whether the held relic may be set down on <paramref name="cell"/>: its own stand on any map, or, on the map it
        /// was lifted from, open walkable ground that is not a portal. Elsewhere it may only go onto its stand.
        /// </summary>
        public bool CanPlaceHeldRelicAt(Vector2Int cell)
        {
            WorldStateService state = WorldStateService.Instance;
            if (HeldRelic == null || state == null || !state.IsReady) return false;
            if (FindStand(state, HeldRelic, cell) != null) return true;
            if (!SameMap(state.CurrentMapId, heldRelicHomeMap) || !state.TryGetObject(HeldRelic, out WorldObjectSnapshot current)) return false;
            if (cell == current.cell) return true;
            GridWorld world = FindAnyObjectByType<GridWorld>();
            return world != null && world.GetPortalAt(cell) == null && world.IsCellAvailable(cell);
        }
        /// <summary>Sets the held relic down on <paramref name="cell"/>, or refuses with feedback.</summary>
        public bool TryPlaceHeldRelic(Vector2Int cell)
        {
            WorldStateService state = WorldStateService.Instance;
            if (HeldRelic == null || InputBlockedByOverlay || state == null || !state.IsReady) return false;
            string relic = HeldRelic;
            WorldObjectSnapshot stand = FindStand(state, relic, cell);
            if (stand != null)
            {
                // Delivery is progression, not map state: the relic leaves its home map for good and the stand holds it.
                GameEventManager.Instance?.Runtime?.SetFlag(VisDeliveredFlagPrefix + relic, true);
                HeldRelic = null;
                state.SetProperty(stand.id, x => x.aligned = true);
                VisPlaced?.Invoke(relic, cell, true);
                return true;
            }
            if (!SameMap(state.CurrentMapId, heldRelicHomeMap))
            {
                RefusePlacement(relic, cell, false, "The relic belongs on its stand here. Face the stand and cast Vis.");
                return false;
            }
            if (!state.TryGetObject(relic, out WorldObjectSnapshot current)) { ReleaseHeldRelic(null); return false; }
            if (cell != current.cell)
            {
                GridWorld world = FindAnyObjectByType<GridWorld>();
                if (world == null || world.GetPortalAt(cell) != null) { RefusePlacement(relic, cell, true, "A relic cannot rest on a portal."); return false; }
                if (!state.TryMoveObject(relic, cell - current.cell)) { RefusePlacement(relic, cell, true, "That tile is not open ground. Face a walkable tile."); return false; }
            }
            HeldRelic = null;
            VisPlaced?.Invoke(relic, cell, false);
            return true;
        }
        private void RefusePlacement(string relic, Vector2Int cell, bool blocked, string message)
        { ShowFeedback(message); VisPlaceRefused?.Invoke(relic, cell, blocked); }
        private void ReleaseHeldRelic(string message)
        {
            if (HeldRelic == null) return;
            string relic = HeldRelic;
            HeldRelic = null;
            if (message != null) ShowFeedback(message);
            VisHoldReleased?.Invoke(relic);
        }
        // A carried relic travels only between the map it was lifted on and its destination. Anywhere else it is
        // released, and it is still at its last safe placement because world state never moved it.
        private void ReleaseHeldRelicOutOfReach()
        {
            WorldStateService state = WorldStateService.Instance;
            if (HeldRelic == null || state == null || !state.IsReady) return;
            bool home = SameMap(state.CurrentMapId, heldRelicHomeMap);
            if (home ? state.TryGetObject(HeldRelic, out _) : SameMap(state.CurrentMapId, heldRelicDestinationMap)) return;
            ReleaseHeldRelic("The relic slipped from Vis and settled back where you last set it down.");
        }
        private static WorldObjectSnapshot FindStand(WorldStateService state, string relic, Vector2Int cell)
        {
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
                if (item.cell == cell && HasTag(item, VisStandPrefix + relic)) return item;
            return null;
        }
        private static string TagValue(WorldObjectSnapshot item, string prefix)
        {
            foreach (string tag in item.tags ?? Array.Empty<string>())
                if (tag != null && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return tag.Substring(prefix.Length);
            return null;
        }
        private static bool SameMap(string a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        /// <summary>Releases a held relic and returns it to its authored start while the quest is Active on campus.</summary>
        public bool ResetActiveAlignmentPuzzle()
        {
            WorldStateService state = WorldStateService.Instance;
            if (Time.timeScale <= 0f || InputBlockedByOverlay ||
                GameEventManager.Instance.Runtime.GetQuestStatus(AlignmentQuestId) != QuestStatus.Active ||
                state == null || !state.IsReady || !string.Equals(state.CurrentMapId, "campus", StringComparison.OrdinalIgnoreCase)) return false;
            bool wasHeld = HeldRelic == AlignmentObjectId;
            if (wasHeld) ReleaseHeldRelic(null);
            return state.ResetAlignmentObject(AlignmentObjectId) || wasHeld;
        }
        private bool IsNearAlignmentMarker()
        {
            WorldStateService state = WorldStateService.Instance;
            GridMover player = GetComponent<GridMover>();
            return state != null && state.IsReady && state.CurrentMapId == "campus" && player != null &&
                state.TryGetObject(AlignmentObjectId, out WorldObjectSnapshot marker) &&
                (HeldRelic == AlignmentObjectId ||
                 Mathf.Abs(marker.cell.x - player.CurrentCell.x) + Mathf.Abs(marker.cell.y - player.CurrentCell.y) <= interactionRadius);
        }
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
        // Anchored objects toggle: a second Glaciate thaws them and undoes their freeze. Never mid-step, so whatever the
        // thaw changes underfoot (the ice trial puts its water back) can safely move the caster.
        private bool ThawAnchored(WorldStateService state, string id)
        {
            GridMover player = GetComponent<GridMover>();
            if (player != null && player.IsMoving) { ShowFeedback("Stop moving before you thaw it."); return false; }
            return state.SetProperty(id, x => x.frozen = false);
        }
        private static bool FreezeSlide(WorldStateService state, string id, Vector2Int direction)
        {
            bool changed = state.SetProperty(id, x => x.frozen = true);
            Vector2Int? goal = null;
            // Anchored objects, such as the ice-trial relic that freezes its pool, freeze in place.
            if (state.TryGetObject(id, out WorldObjectSnapshot target) && HasTag(target, AnchoredTag)) return changed;
            if (target != null)
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
        private string FindNearestTarget()
        { WorldStateService state = WorldStateService.Instance; GridMover player = GetComponent<GridMover>(); if (state == null || player == null) return null; string best = null; int distance = interactionRadius + 1; foreach (WorldObjectSnapshot item in state.SnapshotObjects()) { int d = Mathf.Abs(item.cell.x - player.CurrentCell.x) + Mathf.Abs(item.cell.y - player.CurrentCell.y); if (d <= interactionRadius && d < distance && item.spellCompatible && (selectedSpell != SpellId.LostEchoes || (!item.revealed && WorldStateService.IsLostEchoesTarget(item)))) { distance = d; best = item.id; } } return best; }
        /// <summary>The cardinal or diagonal direction the player last moved or is moving.</summary>
        public Vector2Int FacingDirection => DirectionFromPlayer();
        private Vector2Int DirectionFromPlayer() { GridMover player = GetComponent<GridMover>(); Vector2Int movement = player == null ? Vector2Int.zero : (player.DestinationCell - player.CurrentCell); return movement == Vector2Int.zero ? lastDirection : movement; }
        private static Vector2Int ClampDirection(Vector2Int value) { if (value == Vector2Int.zero) return Vector2Int.up; return new Vector2Int(Mathf.Clamp(value.x, -1, 1), Mathf.Clamp(value.y, -1, 1)); }
    }
}
