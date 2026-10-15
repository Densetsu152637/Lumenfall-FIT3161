using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using Lumenfall.Story;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class QuestHintOverlayTests
    {
        private static readonly Vector2Int[] BoardCells =
        {
            new Vector2Int(19, 19), new Vector2Int(26, 14), new Vector2Int(23, 6),
            new Vector2Int(15, 6), new Vector2Int(12, 14)
        };
        private static readonly int[] ToggleMasks = { 0b11101, 0b11010, 0b10100, 0b01100, 0b10010 };
        private readonly List<GameObject> created = new List<GameObject>();
        private GameEventRuntime runtime;
        private WorldStateService world;
        private QuestHintOverlay overlay;
        private bool hadHintPreference;
        private int originalHintPreference;

        [SetUp]
        public void SetUp()
        {
            hadHintPreference = PlayerPrefs.HasKey("lumenfall.quest-hints-enabled");
            originalHintPreference = PlayerPrefs.GetInt("lumenfall.quest-hints-enabled", 1);
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            CompleteOnboarding();
            world = WorldStateService.Instance;
            if (world == null)
            {
                GameObject host = new GameObject("quest-hint-test-world");
                created.Add(host);
                world = host.AddComponent<WorldStateService>();
                EditModeLifecycle.SetWorldInstance(world);
            }
            QuestHintOverlay.SetHintsEnabled(true);
            overlay = UnityEngine.Object.FindAnyObjectByType<QuestHintOverlay>();
            if (overlay == null)
            {
                GameObject host = new GameObject("quest-hint-test-overlay");
                created.Add(host);
                overlay = host.AddComponent<QuestHintOverlay>();
                typeof(QuestHintOverlay).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, overlay);
                TextAsset definitions = Resources.Load<TextAsset>("Quests/quest_puzzle_definitions");
                typeof(QuestHintOverlay).GetField("puzzleDatabase", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(overlay, JsonUtility.FromJson<QuestPuzzleDatabase>(definitions.text));
            }
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                runtime?.ResetState();
                world?.ResetAllState();
                if (world != null) GameSaveManager.Instance.UnregisterProvider(world.SaveKey);
                for (int i = created.Count - 1; i >= 0; i--)
                    if (created[i] != null)
                    {
                        foreach (SpellCaster caster in created[i].GetComponents<SpellCaster>()) EditModeLifecycle.Call(caster, "OnDisable");
                        foreach (QuestPuzzleMonitor monitor in created[i].GetComponents<QuestPuzzleMonitor>()) EditModeLifecycle.Call(monitor, "OnDisable");
                        UnityEngine.Object.DestroyImmediate(created[i]);
                    }
                created.Clear();
            }
            finally
            {
                if (hadHintPreference) PlayerPrefs.SetInt("lumenfall.quest-hints-enabled", originalHintPreference);
                else PlayerPrefs.DeleteKey("lumenfall.quest-hints-enabled");
                PlayerPrefs.Save();
            }
        }

        [Test]
        public void FirstLightHintFindsASolutionFromEveryBoardStateAndMonitorRequiresAllFive()
        {
            BeginMap("B02.Ground");
            RegisterFirstLightBoard();
            stateEnd();
            runtime.StartQuest("stella_first_light");

            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("First Light"));
            Assert.That(ReadField<string>("tracker"), Is.EqualTo("Relics lit: 0/5"));
            Assert.That(ReadField<string>("hint"), Does.Contain("select Ampere"));

            for (int initial = 0; initial < 32; initial++)
            {
                SetBoard(initial);
                int current = initial;
                int steps = 0;
                while (current != 31 && steps++ < 32)
                {
                    Assert.That(StellaStringThemTogetherPuzzle.TryGetFirstLightStep(out string instruction, out Vector2Int target), Is.True, $"board={current}");
                    Assert.That(instruction, Does.Contain("Next: cast Ampere on relic"));
                    int switchIndex = Array.IndexOf(BoardCells, target);
                    Assert.That(switchIndex, Is.GreaterThanOrEqualTo(0), $"board={current} target={target}");
                    current ^= ToggleMasks[switchIndex];
                    SetBoard(current);
                }
                Assert.That(current, Is.EqualTo(31), $"starting board={initial}");
                Assert.That(StellaStringThemTogetherPuzzle.TryGetFirstLightStep(out string solved, out _), Is.True);
                Assert.That(solved, Does.Contain("All five relics are lit"));
            }

            SetBoard(0);
            GameObject placeholder = Register("relic.stella.power", new Vector2Int(7, 2), false);
            world.SetProperty("relic.stella.power", state => state.powered = true);
            runtime.StartQuest("stella_first_light");
            QuestPuzzleMonitor monitor = UnityEngine.Object.FindAnyObjectByType<QuestPuzzleMonitor>();
            if (monitor == null)
            {
                GameObject monitorHost = new GameObject("quest-hint-test-monitor");
                created.Add(monitorHost);
                monitor = monitorHost.AddComponent<QuestPuzzleMonitor>();
                EditModeLifecycle.Call(monitor, "Awake");
                EditModeLifecycle.Call(monitor, "OnEnable");
            }
            RunMonitor(monitor);
            Assert.That(runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active), "The legacy placeholder cannot complete First Light.");
            for (int i = 0; i < BoardCells.Length; i++) world.SetProperty("stella.string-together.relic." + (i + 1), s => s.powered = true);
            RunMonitor(monitor);
            Assert.That(runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetFlag("stella.first_light.ready_to_report"), Is.True);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Is.EqualTo("Return to Stella."));
            Assert.That(placeholder, Is.Not.Null);
        }

        [Test]
        public void OnlyAcceptedQuestsReplaceTheExploreHint()
        {
            BeginMap("B02.Ground");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Explore the campus and find your Dormmates"));
            Assert.That(ReadField<string>("hint"), Does.Contain("Stella").And.Contain("Cinna Cirra").And.Contain("Katrina")
                .And.Contain("Orio").And.Contain("Rune dais"));

            runtime.StartQuest("orio_patch_notes");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Patch Notes"));
            Assert.That(ReadField<string>("hint"), Does.Contain("west forest"));

            runtime.StartQuest("cinna_out_of_alignment");
            BeginMap("campus");
            stateEnd();
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Out of Alignment"));

            runtime.ResetState();
            CompleteOnboarding();
            BeginMap("B02.Ground");
            stateEnd();
            runtime.StartQuest("stella_first_light");
            runtime.CompleteQuest("stella_first_light");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Explore the campus and find your Dormmates"));
            Assert.That(ReadField<string>("hint"), Does.Contain("Stella"), "The next Stella quest has not been accepted yet.");
        }

        [Test]
        public void OnboardingHintsFollowStoryProgressAndCurrentMap()
        {
            runtime.ResetState();
            BeginMap(LateToOrientationScene.MapId);
            stateEnd();
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Walk around this mysterious forest"));
            Assert.That(ReadField<string>("hint"), Does.Contain("W, A, S, D"));

            runtime.SetFlag(LateToOrientationScene.MetFlag, true);
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Talk to the student calling you"));
            Assert.That(ReadField<string>("hint"), Does.Contain("red hat").And.Contain("Press E"));

            runtime.SetFlag(LayOfTheLandScene.MetFlag, true);
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Speak to Professor Bann"));
            Assert.That(ReadField<string>("hint"), Does.Contain("fountain").And.Contain("Green"));

            runtime.SetFlag(SettlingInScene.BannMetFlag, true);
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Go to Dormitory and check your room"));
            Assert.That(ReadField<string>("hint"), Does.Contain("Dormitory is below Kit"));

            BeginMap(SettlingInScene.MapId);
            stateEnd();
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Talk with the Receptionist"));
            Assert.That(ReadField<string>("hint"), Does.Contain("ginger").And.Contain("glasses"));

            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Explore the campus and find your Dormmates"));
        }

        [Test]
        public void MostRecentlySpokenQuestGiverControlsHintAndCompletedPeopleLeaveExploreList()
        {
            BeginMap("campus");
            stateEnd();
            runtime.StartQuest("stella_first_light");
            runtime.StartQuest("orio_patch_notes");

            QuestHintOverlay.RecordQuestConversation(runtime, "stella");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("First Light"));

            QuestHintOverlay.RecordQuestConversation(runtime, "orio");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Patch Notes"));

            runtime.CompleteQuest("orio_patch_notes");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("First Light"), "Another accepted quest should take over.");

            runtime.CompleteQuest("stella_first_light");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Explore the campus and find your Dormmates"));
            Assert.That(ReadField<string>("hint"), Does.Not.Contain("Orio").And.Contain("Stella"),
                "Orio's completed quest removes his line, while Stella still has a second quest available.");
        }

        [Test]
        public void OutOfAlignmentExplainsVisThenReturnsToCinnaAndMovesHisExploreLocation()
        {
            BeginMap("campus");
            stateEnd();
            runtime.StartQuest("cinna_out_of_alignment");
            QuestHintOverlay.RecordQuestConversation(runtime, "cinna_cirra_orientation");

            RefreshHint();
            string instructions = ReadField<string>("hint");
            Assert.That(instructions, Does.StartWith("Lift the relic beside Cinna Cirra"));
            Assert.That(instructions, Does.Contain("\n1. Use Q").And.Contain("2. Select Vis")
                .And.Contain("3. Right click or press F").And.Contain("4. Hold it").And.Contain("5. Face the stand"));
            Assert.That(instructions, Does.Contain("lower part of the campus"));

            runtime.SetFlag(SpellCaster.VisDeliveredFlagPrefix + CinnaAlignmentRelic.RelicId, true);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Is.EqualTo("Go back to Cinna Cirra."));

            runtime.CompleteQuest("cinna_out_of_alignment");
            RefreshHint();
            Assert.That(ReadField<string>("title"), Is.EqualTo("Explore the campus and find your Dormmates"));
            Assert.That(ReadField<string>("hint"), Does.Contain("Cinna Cirra</b> now waits near the Ice Trial area")
                .And.Contain("lower edge of campus"));
        }

        [Test]
        public void SolvedRuneDaisTellsPlayerToReturnToOrio()
        {
            runtime.StartQuest(OrioRuneDiscPuzzle.QuestId);
            runtime.SetFlag("spell.unlocked.Rotara", true);
            QuestHintOverlay.RecordQuestConversation(runtime, "orio");
            BeginMap(OrioRuneDiscPuzzle.PuzzleMapId);
            foreach (string disc in OrioRuneDiscPuzzle.DiscIds)
            {
                RegisterTagged(disc, Vector2Int.zero, OrioRuneDiscPuzzle.DiscTag);
                world.SetProperty(disc, item => item.rotation = OrioRuneDiscPuzzle.TargetRotation);
            }
            RegisterTagged(OrioRuneDiscPuzzle.AnchorId, Vector2Int.one, "rotara");
            stateEnd();

            RefreshHint();
            Assert.That(runtime.GetQuestStatus(OrioRuneDiscPuzzle.QuestId), Is.EqualTo(QuestStatus.Active));
            Assert.That(ReadField<string>("hint"), Is.EqualTo("Return to Orio."));
        }

        [Test]
        public void LibraryKeepsakeUsesItsAuthoredMarkerInsteadOfARequiredCodePosition()
        {
            BeginMap("B04.Ground");
            world.RegisterPuzzleTargets(new[]
            {
                new PuzzleTargetDefinition { objectIds = new[] { "katrina.item.library" }, requiredRevealed = true }
            });
            Register("katrina.item.library", new Vector2Int(7, 24), true);
            stateEnd();
            runtime.StartQuest("katrina_something_borrowed");

            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Not.Contain("Return the revealed keepsake to cell"));

            QuestPuzzleDefinition databasePuzzle = LoadPuzzle("katrina_something_borrowed");
            QuestPuzzleTarget monitorTarget = databasePuzzle.targets[0];
            QuestPuzzleTarget merged = (QuestPuzzleTarget)InvokeStatic("MergeCurrentMapTarget", monitorTarget, world);
            Assert.That(monitorTarget.requiredPositionX, Is.EqualTo(int.MinValue));
            Assert.That(monitorTarget.requiredPositionY, Is.EqualTo(int.MinValue));
            object[] args = { merged, ReadObject("katrina.item.library"), false };
            Vector2Int destination = (Vector2Int)InvokeStaticWithArgs("ResolveDestination", args);
            Assert.That(destination, Is.EqualTo(new Vector2Int(7, 24)));
            Assert.That(args[2], Is.False);

            BeginMap("B02.Ground");
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Travel to Library"));
            Assert.That(ReadField<string>("hint"), Does.Not.Contain("7, 24"));
        }

        [Test]
        public void FoundKeepsakeHintPointsToKatrinaWhileReturnRemainsPending()
        {
            BeginMap("B04.Ground");
            stateEnd();
            runtime.StartQuest("katrina_something_borrowed");
            Assert.That(runtime.TriggerEvent("found_katrina_library_item").Succeeded, Is.True);
            Assert.That(runtime.TriggerEvent("found_katrina_garden_item").Succeeded, Is.True);
            Assert.That(runtime.TriggerEvent("found_katrina_stream_item").Succeeded, Is.True);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Return them to Katrina on campus"));
            Assert.That(runtime.GetQuestStatus("katrina_something_borrowed"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetFlag("inventory.keyitem.katrina-keepsake"), Is.True);

            BeginMap("campus");
            stateEnd();
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Return them to Katrina on campus"));
        }

        [Test]
        public void ColdFeetPointsToTheTrialPortalThenFollowsTheIceTrial()
        {
            GridWorld grid = NewGrid();
            NewPlayer(grid, "cold-feet-player");
            BeginMap("campus");
            stateEnd();
            runtime.StartQuest("cinna_cold_feet_warm_idea");
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Freezing Pond area").And.Not.Contain("47, 45"));

            BeginMap(CinnaIceTrialRoom.MapId);
            RegisterTagged(CinnaIceTrialRoom.RelicId, new Vector2Int(3, 1), "glaciate", "anchored");
            RegisterTagged("switch.cinna.ice.01", new Vector2Int(2, 12), CinnaIceTrialRoom.SwitchTag);
            RegisterTagged(CinnaIceTrialRoom.FinalSwitchId, new Vector2Int(11, 8), CinnaIceTrialRoom.FinalTag);
            stateEnd();
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Cast Glaciate on the relic on its stand"));

            world.SetProperty(CinnaIceTrialRoom.RelicId, state => state.frozen = true);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("0 of 1 lit"));

            world.SetProperty("switch.cinna.ice.01", state => state.powered = true);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Slide into the seal"));

            world.SetProperty(CinnaIceTrialRoom.FinalSwitchId, state => state.powered = true);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Does.Contain("Talk to Cinna Cirra"));
        }

        private void RegisterTagged(string id, Vector2Int cell, params string[] tags)
        {
            GameObject sceneObject = new GameObject(id);
            created.Add(sceneObject);
            world.RegisterObject(id, sceneObject, cell, Vector2Int.one, false, tags);
        }

        [Test]
        public void SettingsRemainReachableWhenHintsAreOffAndRestoreOnlyOwnedPlayerInput()
        {
            GridWorld grid = NewGrid();
            GridPlayerInput owned = NewPlayer(grid, "owned-player");
            GridPlayerInput alreadyDisabled = NewPlayer(grid, "disabled-player");
            alreadyDisabled.enabled = false;
            runtime.SetFlag("spell.unlocked.Ampere", true);
            GameObject spellHost = new GameObject("quest-hint-test-spell");
            created.Add(spellHost);
            SpellCaster caster = spellHost.AddComponent<SpellCaster>();
            EditModeLifecycle.Call(caster, "OnEnable");
            BeginMap("campus");
            GameObject spellTarget = new GameObject("quest-hint-test-spell-target");
            created.Add(spellTarget);
            world.RegisterObject("hint-test-relic", spellTarget, Vector2Int.one, Vector2Int.one, true, Array.Empty<string>());
            stateEnd();
            Assert.That(caster.Cast(SpellId.Ampere, "hint-test-relic", Vector2Int.right), Is.True);
            Assert.That(((WorldObjectSnapshot)ReadObject("hint-test-relic")).powered, Is.True);
            world.SetProperty("hint-test-relic", state => state.powered = false);

            GameObject npcHost = new GameObject("quest-hint-test-npc");
            created.Add(npcHost);
            DynamicNpc npc = npcHost.AddComponent<DynamicNpc>();
            npc.Configure(new NpcDefinition
            {
                id = "hint-test-npc",
                dialogueIds = new[] { "stella_first_light_dialogue" }
            }, "stella_first_light_dialogue");
            DialogueSession dialogue = npc.Interact();
            Assert.That(dialogue, Is.Not.Null, "The configured NPC must work while settings are closed.");
            dialogue.End();

            QuestHintOverlay.SetHintsEnabled(false);
            Assert.That(QuestHintOverlay.HintsEnabled, Is.False);
            InvokeInstance("SetSettingsOpen", true);
            Assert.That(QuestHintOverlay.IsSettingsOpen, Is.True);
            Assert.That(owned.enabled, Is.False);
            Assert.That(alreadyDisabled.enabled, Is.False);
            RefreshHint();
            Assert.That(ReadField<string>("hint"), Is.Null);
            Assert.That(caster.Cast(SpellId.Ampere, "hint-test-relic", Vector2Int.right), Is.False);
            Assert.That(((WorldObjectSnapshot)ReadObject("hint-test-relic")).powered, Is.False);
            Assert.That(npc.Interact(), Is.Null);
            Assert.That(runtime.ActiveDialogue, Is.Null);
            InvokeInstance("SetSettingsOpen", false);
            Assert.That(QuestHintOverlay.IsSettingsOpen, Is.False);
            Assert.That(owned.enabled, Is.True);
            Assert.That(alreadyDisabled.enabled, Is.False);
            Assert.That(OverlayInputGate.Owns(overlay), Is.False);
            Assert.That(OverlayInputGate.IsBlocked, Is.True, "Closing input stays consumed for the rest of its frame.");
            Assert.That(caster.Cast(SpellId.Ampere, "hint-test-relic", Vector2Int.right), Is.False);
            Assert.That(npc.Interact(), Is.Null);
            Assert.That(runtime.ActiveDialogue, Is.Null);

            AfterOverlayTransitionFrame(() =>
            {
                Assert.That(caster.Cast(SpellId.Ampere, "hint-test-relic", Vector2Int.right), Is.True);
                DialogueSession resumedDialogue = npc.Interact();
                Assert.That(resumedDialogue, Is.Not.Null);
                resumedDialogue.End();

                InvokeInstance("SetSettingsOpen", true);
                InvokeInstance("OnDisable");
                Assert.That(QuestHintOverlay.IsSettingsOpen, Is.False);
                Assert.That(owned.enabled, Is.True);
            });
        }

        private static void AfterOverlayTransitionFrame(Action assertions)
        {
            // A synchronous EditMode test cannot advance Time.frameCount. Simulate only the
            // expired transition frame, preserving every owner and restoring the bookkeeping afterward.
            var locks = (OverlayLockSet)typeof(OverlayInputGate)
                .GetField("Locks", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            FieldInfo transition = typeof(OverlayLockSet).GetField("transitionFrame", BindingFlags.Instance | BindingFlags.NonPublic);
            int originalFrame = (int)transition.GetValue(locks);
            try
            {
                transition.SetValue(locks, Time.frameCount - 1);
                assertions();
            }
            finally { transition.SetValue(locks, originalFrame); }
        }

        private void BeginMap(string mapId)
        {
            world.ResetAllState();
            world.BeginMapRebuild(mapId);
        }

        private void stateEnd() => world.EndMapRebuild();

        private void CompleteOnboarding()
        {
            runtime.SetFlag(LateToOrientationScene.MetFlag, true);
            runtime.SetFlag(LayOfTheLandScene.MetFlag, true);
            runtime.SetFlag(SettlingInScene.BannMetFlag, true);
            runtime.SetFlag(SettlingInScene.NotReadyFlag, true);
        }

        private void RegisterFirstLightBoard()
        {
            for (int i = 0; i < BoardCells.Length; i++) Register("stella.string-together.relic." + (i + 1), BoardCells[i], false);
        }

        private GameObject Register(string id, Vector2Int cell, bool revealed)
        {
            GameObject sceneObject = new GameObject(id);
            created.Add(sceneObject);
            world.RegisterObject(id, sceneObject, cell, Vector2Int.one, false, Array.Empty<string>());
            if (revealed) world.SetProperty(id, state => state.revealed = true);
            return sceneObject;
        }

        private void SetBoard(int mask)
        {
            for (int i = 0; i < BoardCells.Length; i++)
            {
                string id = "stella.string-together.relic." + (i + 1);
                bool powered = (mask & (1 << i)) != 0;
                world.SetProperty(id, state => state.powered = powered);
            }
        }

        private GridWorld NewGrid()
        {
            GameObject host = new GameObject("quest-hint-test-grid");
            created.Add(host);
            GridWorld grid = host.AddComponent<GridWorld>();
            grid.ConfigureBounds(80, 80);
            return grid;
        }

        private GridPlayerInput NewPlayer(GridWorld grid, string name)
        {
            GameObject player = new GameObject(name);
            created.Add(player);
            player.transform.SetParent(grid.transform, false);
            player.AddComponent<GridMover>();
            GridPlayerInput input = player.AddComponent<GridPlayerInput>();
            EditModeLifecycle.Call(input, "Awake");
            return input;
        }

        private void RefreshHint() => InvokeInstance("RefreshHint");

        private void InvokeInstance(string name, params object[] args)
        {
            typeof(QuestHintOverlay).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(overlay, args);
        }

        private object ReadObject(string id)
        {
            Assert.That(world.TryGetObject(id, out WorldObjectSnapshot state), Is.True);
            return state;
        }

        private T ReadField<T>(string name) => (T)typeof(QuestHintOverlay)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay);

        private static object InvokeStatic(string name, params object[] args) => typeof(QuestHintOverlay)
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        private static object InvokeStaticWithArgs(string name, object[] args) => typeof(QuestHintOverlay)
            .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        private static QuestPuzzleDefinition LoadPuzzle(string questId)
        {
            TextAsset asset = Resources.Load<TextAsset>("Quests/quest_puzzle_definitions");
            QuestPuzzleDatabase database = JsonUtility.FromJson<QuestPuzzleDatabase>(asset.text);
            foreach (QuestPuzzleDefinition puzzle in database.puzzles)
                if (puzzle.questId == questId) return puzzle;
            Assert.Fail("No puzzle definition for " + questId);
            return null;
        }

        private static void RunMonitor(QuestPuzzleMonitor monitor)
        {
            typeof(QuestPuzzleMonitor).GetField("nextCheck", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(monitor, 0f);
            typeof(QuestPuzzleMonitor).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(monitor, null);
        }
    }

    // Synchronous EditMode tests do not dispatch ordinary MonoBehaviour startup callbacks.
    internal static class EditModeLifecycle
    {
        // Skip the play-only DontDestroyOnLoad call while supplying the authoritative service.
        internal static void SetWorldInstance(WorldStateService world) => typeof(WorldStateService)
            .GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).SetValue(null, world);

        internal static void Call(MonoBehaviour component, string method) => component.GetType()
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
    }
}
