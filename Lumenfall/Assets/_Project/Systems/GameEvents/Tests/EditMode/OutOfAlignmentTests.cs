using System;
using System.Reflection;
using Lumenfall.Inventory;
using Lumenfall.GameEvents;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class OutOfAlignmentTests
    {
        private const string Quest = "cinna_out_of_alignment";
        private const string Relic = CinnaAlignmentRelic.RelicId;
        private const string Stand = CinnaIceTrialRoom.RelicId;
        private const string Trial = CinnaIceTrialRoom.MapId;
        private const string Delivered = SpellCaster.VisDeliveredFlagPrefix + Relic;
        // A generic in-place Vis object, kept to cover the vis-target rules.
        private const string Practice = "vis.practice";
        private static readonly Vector2Int Start = new Vector2Int(23, 53);
        private static readonly Vector2Int StandCell = new Vector2Int(3, 1);
        private static readonly string[] RelicTags = { "vis", "vis-lift", "vis-destination:" + Trial };
        private static readonly string[] StandTags = { "glaciate", "anchored", "vis-stand:" + Relic };
        private GameEventRuntime runtime;
        private GameObject worldHost, casterHost, gridHost, relicHost, standHost, markerHost, departureHost;
        private WorldStateService world;
        private WorldStateService previousWorld;
        private GridWorld grid;
        private SpellCaster caster;
        private float previousTimeScale;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1;
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            worldHost = new GameObject("alignment-world");
            previousWorld = WorldStateService.Instance;
            world = worldHost.AddComponent<WorldStateService>();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, world);
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, false);
            gridHost = new GameObject("alignment-grid");
            grid = gridHost.AddComponent<GridWorld>();
            grid.ConfigureBounds(54, 92);
            casterHost = new GameObject("alignment-caster");
            caster = casterHost.AddComponent<SpellCaster>();
            Call(caster, "OnEnable");
        }

        [TearDown]
        public void TearDown()
        {
            if (departureHost != null) { Call(departureHost.GetComponent<CinnaTrialDeparture>(), "OnDestroy"); Object.DestroyImmediate(departureHost); }
            for (CinnaAlignmentRelic built = Object.FindAnyObjectByType<CinnaAlignmentRelic>(FindObjectsInactive.Include); built != null;
                 built = Object.FindAnyObjectByType<CinnaAlignmentRelic>(FindObjectsInactive.Include))
            {
                Call(built, "OnDestroy");
                Object.DestroyImmediate(built.gameObject);
            }
            foreach (GameObject host in new[] { markerHost, relicHost, standHost }) if (host != null) Object.DestroyImmediate(host);
            Call(caster, "OnDisable");
            Object.DestroyImmediate(casterHost);
            Object.DestroyImmediate(gridHost);
            Object.DestroyImmediate(worldHost);
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, previousWorld);
            runtime.ResetState();
            Time.timeScale = previousTimeScale;
        }

        private static object Call(object component, string method, params object[] args)
        {
            return component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, args.Length == 0 ? null : args);
        }

        private static MapDefinition Area(string path) =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAreaAsset>("Assets/_Project/World/Areas/" + path).definition;

        // Registers the campus relic directly, the way CinnaAlignmentRelic does, plus an unrelated object.
        private void BuildCampus(Vector2Int? start = null)
        {
            if (relicHost == null) relicHost = new GameObject("alignment-relic");
            world.BeginMapRebuild("campus");
            world.RegisterObject(Relic, relicHost, start ?? Start, Vector2Int.one, true, RelicTags, 0f);
            world.RegisterObject("unrelated", null, Vector2Int.zero, Vector2Int.one, true, new[] { "ampere" });
            world.EndMapRebuild();
        }
        private void BuildTrial()
        {
            if (standHost == null) standHost = new GameObject("alignment-stand");
            world.BeginMapRebuild(Trial);
            world.RegisterObject(Stand, standHost, StandCell, Vector2Int.one, false, StandTags);
            world.EndMapRebuild();
        }
        private void BuildMap(string mapId) { world.BeginMapRebuild(mapId); world.EndMapRebuild(); }
        private void BuildPractice(string[] tags = null, float authoredAngle = 0)
        {
            world.BeginMapRebuild("campus");
            world.RegisterObject(Practice, null, new Vector2Int(40, 22), Vector2Int.one, true,
                tags ?? new[] { "vis", "vis-target:90" }, authoredAngle);
            world.EndMapRebuild();
        }
        private WorldObjectSnapshot State(string id = Relic)
        {
            Assert.That(world.TryGetObject(id, out WorldObjectSnapshot snapshot), Is.True);
            return snapshot;
        }
        private static PuzzleTargetDefinition PracticeTarget()
        { return new PuzzleTargetDefinition { objectIds = new[] { Practice }, requiredAligned = true, requiredRotation = 90 }; }
        private void Lift()
        {
            runtime.SetFlag("spell.unlocked.Vis", true);
            Assert.That(caster.Cast(SpellId.Vis, Relic, Vector2Int.up), Is.True);
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic));
        }
        private void ReleaseOutOfReach() => Call(caster, "ReleaseHeldRelicOutOfReach");

        // ---- Lesson ----

        [Test]
        public void VisLessonRequiresTerminalReadAndRecoversAfterAbandonment()
        {
            DialogueSession lesson = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            lesson.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.False);
            lesson = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            lesson.SelectChoice("accept");
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.False);
            lesson.End();
            lesson = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            lesson.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.False);
            lesson = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            lesson.SelectChoice("active");
            lesson.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.True);
            string save = runtime.ExportStateJson(false);
            runtime.ResetState();
            runtime.ImportStateJson(save);
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.True);
        }

        [Test]
        public void CompletedDialogueCannotGrantLessonReward()
        {
            runtime.StartQuest(Quest);
            runtime.CompleteQuest(Quest);
            runtime.SetFlag("spell.unlocked.Vis", false);
            DialogueSession lesson = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            lesson.SelectChoice("done");
            lesson.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.False);
        }

        // ---- Authored data ----

        [Test]
        public void AuthoredRelicIsLiftableAndTheTrialPedestalIsItsStand()
        {
            MapDefinition campus = Area("campus.asset");
            MapObjectDefinition relic = Array.Find(campus.objects, o => o != null && o.id == Relic);
            Assert.That(relic, Is.Not.Null);
            Assert.That(new Vector2Int(relic.x, relic.y), Is.EqualTo(Start));
            Assert.That(relic.tags, Is.EqualTo(RelicTags));
            Assert.That(relic.blocks && relic.compatibleWithSpells, Is.True);
            Assert.That(Array.Exists(campus.puzzleTargets, t => t.id == "quest.cinna.out-of-alignment"), Is.False, "Out of Alignment completes through Cinna, not a map target.");
            QuestPuzzleDatabase database = JsonUtility.FromJson<QuestPuzzleDatabase>(Resources.Load<TextAsset>("Quests/quest_puzzle_definitions").text);
            Assert.That(Array.Exists(database.puzzles, p => p.questId == Quest), Is.False);

            MapDefinition trial = Area("C03.IceTrial.asset");
            MapObjectDefinition stand = Array.Find(trial.objects, o => o != null && o.id == Stand);
            Assert.That(new Vector2Int(stand.x, stand.y), Is.EqualTo(StandCell));
            Assert.That(stand.tags, Does.Contain("vis-stand:" + Relic));
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            NpcDefinition trialCinna = Array.Find(catalog.npcs, n => n.id == "cinna_cirra_trial");
            Assert.That(trialCinna.mapId, Is.EqualTo(Trial), "Cinna waits for the player in the trial lobby.");
        }

        [Test]
        public void CampusRegistersTheRelicUntilItIsDelivered()
        {
            MapDefinition campus = Area("campus.asset");
            world.BeginMapRebuild("campus");
            CinnaAlignmentRelic.RegisterForMap(campus, grid);
            world.EndMapRebuild();
            Assert.That(State().cell, Is.EqualTo(Start));
            Assert.That(State().tags, Is.EqualTo(RelicTags));
            Assert.That(world.TryGetSceneObject(Relic, out GameObject view), Is.True);
            Assert.That(view.GetComponent<GridObstacle>(), Is.Not.Null, "The resting relic blocks its tile.");

            runtime.SetFlag(Delivered, true);
            world.BeginMapRebuild("campus");
            CinnaAlignmentRelic.RegisterForMap(campus, grid);
            world.EndMapRebuild();
            Assert.That(world.TryGetObject(Relic, out _), Is.False, "A delivered relic has left campus.");
            Assert.That(view.activeInHierarchy, Is.False);
        }

        // ---- Lift and place ----

        [Test]
        public void LiftingRemembersTheLastPlacementButRemovesItsGroundFootprint()
        {
            MapDefinition campus = Area("campus.asset");
            world.BeginMapRebuild("campus");
            CinnaAlignmentRelic.RegisterForMap(campus, grid);
            world.EndMapRebuild();
            CinnaAlignmentRelic presentation = Object.FindAnyObjectByType<CinnaAlignmentRelic>(FindObjectsInactive.Include);
            Call(presentation, "Advance", 0f); // Subscribe to this test's caster before the lift event.
            Assert.That(world.TryGetSceneObject(Relic, out GameObject groundObject), Is.True);
            Assert.That(grid.IsCellAvailable(Start), Is.False, "The resting relic blocks its tile.");
            int changes = 0;
            world.StateChanged += _ => changes++;
            Lift();
            Assert.That(State().cell, Is.EqualTo(Start));
            Assert.That(changes, Is.EqualTo(0), "Carrying is not a world-state change.");
            Assert.That(groundObject.activeSelf, Is.False, "The carried relic has no ground object or obstacle.");
            Assert.That(grid.IsCellAvailable(Start), Is.True, "Lifting immediately frees the previous tile.");
            Assert.That(caster.Cast(SpellId.Vis, Relic, Vector2Int.up), Is.False, "Only one relic can be held.");
            Assert.That(caster.LastCastFeedback, Does.Contain("already holding"));

            Assert.That(caster.TryPlaceHeldRelic(Start + Vector2Int.up), Is.True);
            Assert.That(groundObject.activeSelf, Is.True, "Placing restores the ground object.");
            Assert.That(grid.IsCellAvailable(Start + Vector2Int.up), Is.False, "The placed relic blocks only its new tile.");
            Assert.That(grid.IsCellAvailable(Start), Is.True, "The old tile stays free after placement.");
        }

        [Test]
        public void PlacementNeedsOpenGroundOnTheHomeMap()
        {
            BuildCampus(Vector2Int.zero);
            Lift();
            Assert.That(caster.CanPlaceHeldRelicAt(new Vector2Int(-1, 0)), Is.False);
            Assert.That(caster.TryPlaceHeldRelic(new Vector2Int(-1, 0)), Is.False, "Outside the map is not walkable.");
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic), "A refused placement keeps holding the relic.");
            Assert.That(caster.LastCastFeedback, Does.Contain("open ground"));
            Assert.That(caster.CanPlaceHeldRelicAt(new Vector2Int(1, 0)), Is.True);
            Assert.That(caster.TryPlaceHeldRelic(new Vector2Int(1, 0)), Is.True);
            Assert.That(caster.HeldRelic, Is.Null);
            Assert.That(State().cell, Is.EqualTo(new Vector2Int(1, 0)));
            Assert.That(relicHost.transform.position, Is.EqualTo(grid.CellToWorld(new Vector2Int(1, 0))));
        }

        [Test]
        public void PlacingBackOnItsOwnTileIsAllowed()
        {
            BuildCampus();
            Lift();
            Assert.That(caster.TryPlaceHeldRelic(Start), Is.True);
            Assert.That(State().cell, Is.EqualTo(Start));
            Assert.That(caster.HeldRelic, Is.Null);
        }

        [Test]
        public void RelicReturnsToItsLastPlacementOutsideItsTwoMaps()
        {
            BuildCampus();
            Lift();
            Vector2Int moved = Start + Vector2Int.up;
            Assert.That(caster.TryPlaceHeldRelic(moved), Is.True);
            Lift();
            BuildMap("B02.Ground");
            ReleaseOutOfReach();
            Assert.That(caster.HeldRelic, Is.Null, "Entering a building releases the relic.");
            Assert.That(caster.LastCastFeedback, Does.Contain("last set it down"));
            BuildCampus();
            Assert.That(State().cell, Is.EqualTo(moved), "It waits at the last safe placement.");

            Lift();
            BuildTrial();
            ReleaseOutOfReach();
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic), "The relic can be carried into the trial.");
            BuildCampus();
            ReleaseOutOfReach();
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic), "And carried back out again.");
        }

        [Test]
        public void InTheTrialTheRelicOnlyGoesOntoItsStandAndThenLeavesCampus()
        {
            BuildCampus();
            Lift();
            BuildTrial();
            ReleaseOutOfReach();
            Assert.That(caster.CanPlaceHeldRelicAt(StandCell + Vector2Int.right), Is.False);
            Assert.That(caster.TryPlaceHeldRelic(StandCell + Vector2Int.right), Is.False);
            Assert.That(caster.LastCastFeedback, Does.Contain("stand"));
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic));
            Assert.That(runtime.GetFlag(Delivered), Is.False);

            Assert.That(caster.CanPlaceHeldRelicAt(StandCell), Is.True);
            Assert.That(caster.TryPlaceHeldRelic(StandCell), Is.True);
            Assert.That(caster.HeldRelic, Is.Null);
            Assert.That(runtime.GetFlag(Delivered), Is.True);
            Assert.That(State(Stand).aligned, Is.True);

            world.BeginMapRebuild("campus");
            CinnaAlignmentRelic.RegisterForMap(Area("campus.asset"), grid);
            world.EndMapRebuild();
            Assert.That(world.TryGetObject(Relic, out _), Is.False, "The relic has left campus for good.");
        }

        [Test]
        public void LoadingASaveEmptiesTheHands()
        {
            BuildCampus();
            Lift();
            string worldSave = world.CaptureStateJson(), eventSave = runtime.ExportStateJson(false);
            runtime.ImportStateJson(eventSave);
            Assert.That(caster.HeldRelic, Is.Null);
            world.ResetAllState();
            world.RestoreStateJson(worldSave);
            BuildCampus();
            Assert.That(State().cell, Is.EqualTo(Start));
        }

        [Test]
        public void VisRejectsLockedOrOverlayInput()
        {
            BuildCampus();
            Assert.That(caster.Cast(SpellId.Vis, Relic, Vector2Int.up), Is.False, "Vis is not learned yet.");
            runtime.SetFlag("spell.unlocked.Vis", true);
            runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(caster.Cast(SpellId.Vis, Relic, Vector2Int.up), Is.False);
            Assert.That(caster.HeldRelic, Is.Null);
            runtime.ActiveDialogue.End();
            Lift();
            runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(caster.TryPlaceHeldRelic(Start + Vector2Int.up), Is.False);
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic));
        }

        // ---- Reset ----

        [Test]
        public void ActiveResetReleasesAndReturnsTheRelicToItsStart()
        {
            BuildCampus();
            runtime.StartQuest(Quest);
            runtime.SetFlag("unrelated-progress", true);
            world.SetProperty("unrelated", x => x.powered = true);
            Lift();
            Assert.That(caster.TryPlaceHeldRelic(Start + Vector2Int.up), Is.True);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.True);
            Assert.That(State().cell, Is.EqualTo(Start));
            Assert.That(relicHost.transform.position, Is.EqualTo(grid.CellToWorld(Start)));
            Lift();
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.True, "Reset also lets go of a held relic.");
            Assert.That(caster.HeldRelic, Is.Null);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False, "Nothing to reset.");
            Assert.That(runtime.GetFlag("unrelated-progress"), Is.True);
            Assert.That(State("unrelated").powered, Is.True);
        }

        [Test]
        public void ResetRejectsInactiveCompletedWrongMapAndOverlay()
        {
            BuildCampus();
            world.SetProperty(Relic, x => x.cell = Start + Vector2Int.up);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            runtime.StartQuest(Quest);
            runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            runtime.ActiveDialogue.End();
            runtime.CompleteQuest(Quest);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            Assert.That(State().cell, Is.EqualTo(Start + Vector2Int.up));
            runtime.ResetState();
            runtime.StartQuest(Quest);
            BuildTrial();
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
        }

        // ---- Report and Cinna leaving for the trial ----

        private CinnaTrialDeparture CreateDeparture()
        {
            departureHost = new GameObject("alignment-departure");
            CinnaTrialDeparture departure = departureHost.AddComponent<CinnaTrialDeparture>();
            Call(departure, "Awake");
            Call(departure, "Subscribe");
            return departure;
        }

        [Test]
        public void ReportNeedsTheRelicOnItsStand()
        {
            runtime.StartQuest(Quest);
            DialogueSession dialogue = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(dialogue.SelectChoice("report"), Is.False, "Nothing to report yet.");
            Assert.That(dialogue.SelectChoice("active"), Is.True);
            dialogue.End();
            runtime.SetFlag(Delivered, true);
            dialogue = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(dialogue.SelectChoice("active"), Is.False, "Once delivered, Cinna only takes the report.");
            Assert.That(dialogue.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetFlag(CinnaTrialDeparture.DepartureFlag), Is.True, "Cinna asks to meet at the trial.");
        }

        [Test]
        public void DepartureFadesOutCompletesAtBlackThenFadesIn()
        {
            CinnaTrialDeparture departure = CreateDeparture();
            Action<float> advance = dt => Call(departure, "Advance", dt);
            runtime.StartQuest(Quest);
            runtime.SetFlag(Delivered, true);
            DialogueSession dialogue = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(dialogue.SelectChoice("report"), Is.True);
            dialogue.Continue();
            Assert.That(CinnaTrialDeparture.InProgress, Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active), "Cinna leaves, and the quest completes, off screen.");
            runtime.SetFlag("spell.unlocked.Vis", true);
            BuildCampus();
            Assert.That(caster.Cast(SpellId.Vis, Relic, Vector2Int.up), Is.False, "No casting during the fade.");

            advance(0.3f);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
            advance(0.4f);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.GetFlag(CinnaTrialDeparture.DepartureFlag), Is.False);
            advance(0.4f);
            Assert.That(CinnaTrialDeparture.InProgress, Is.True, "Still fading back in.");
            advance(1f);
            Assert.That(CinnaTrialDeparture.InProgress, Is.False);
            Assert.That(runtime.HasEventTriggered(CinnaTrialDeparture.CompletionEventId), Is.True);
        }

        [Test]
        public void DepartureIgnoresOtherEndings()
        {
            CreateDeparture();
            runtime.StartQuest(Quest);
            DialogueSession dialogue = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            dialogue.SelectChoice("active");
            dialogue.Continue();
            Assert.That(CinnaTrialDeparture.InProgress, Is.False, "A hint ends without Cinna leaving.");
            runtime.SetFlag(Delivered, true);
            dialogue = runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            dialogue.SelectChoice("report");
            dialogue.End();
            Assert.That(CinnaTrialDeparture.InProgress, Is.False, "Closing the dialogue early does not send Cinna off.");
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
        }

        // ---- Relic visuals (stepped by hand through Advance) ----

        private CinnaAlignmentRelic RegisterRelic()
        {
            world.BeginMapRebuild("campus");
            CinnaAlignmentRelic.RegisterForMap(Area("campus.asset"), grid);
            world.EndMapRebuild();
            return Object.FindAnyObjectByType<CinnaAlignmentRelic>();
        }
        private static void Advance(CinnaAlignmentRelic relic, float deltaTime) => Call(relic, "Advance", deltaTime);
        private static Transform Visual(CinnaAlignmentRelic relic) => relic.transform.Find("Relic Visual");

        [Test]
        public void LiftDipsThenRisesToHoverAndPlacingFloatsItDown()
        {
            CinnaAlignmentRelic relic = RegisterRelic();
            Transform visual = Visual(relic);
            Vector3 start = grid.CellToWorld(Start);
            Advance(relic, 0f);
            Assert.That(visual.position, Is.EqualTo(start));
            Lift();
            Advance(relic, 0.04f);
            Assert.That(visual.position.y, Is.LessThan(start.y), "Vis takes the weight with a small dip.");
            Advance(relic, 0.4f);
            Advance(relic, 0.01f);
            Vector3 offset = visual.position - casterHost.transform.position;
            Assert.That(offset.z, Is.EqualTo(0.85f).Within(0.1f), "It hovers in front of the caster, who faces up by default.");
            Assert.That(offset.y, Is.EqualTo(0.55f).Within(0.1f), "At chest height, clear of the character's hat.");
            typeof(SpellCaster).GetField("lastDirection", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(caster, Vector2Int.right);
            Advance(relic, 1f);
            offset = visual.position - casterHost.transform.position;
            Assert.That(offset.x, Is.EqualTo(0.85f).Within(0.1f), "Turning swings it round to stay in front.");
            Assert.That(offset.z, Is.EqualTo(0f).Within(0.1f));
            // Walking forward one tile in a single frame must not leave the relic trailing into the player's face.
            casterHost.transform.position += Vector3.right;
            Advance(relic, 0.016f);
            offset = visual.position - casterHost.transform.position;
            Assert.That(offset.x, Is.EqualTo(0.85f).Within(0.1f), "The relic keeps its distance ahead of a moving player.");

            Vector2Int target = Start + Vector2Int.up;
            Assert.That(caster.TryPlaceHeldRelic(target), Is.True);
            Advance(relic, 0.15f);
            Assert.That(Vector3.Distance(visual.position, grid.CellToWorld(target)), Is.GreaterThan(0.01f), "Still floating down.");
            Advance(relic, 0.2f);
            Assert.That(visual.position, Is.EqualTo(grid.CellToWorld(target)));
        }

        [Test]
        public void LooseRelicWobblesOnItsTile()
        {
            CinnaAlignmentRelic relic = RegisterRelic();
            Transform visual = Visual(relic);
            Advance(relic, 0.5f);
            Assert.That(visual.position, Is.EqualTo(grid.CellToWorld(Start)));
            float tilt = Quaternion.Angle(visual.rotation, Quaternion.identity);
            Assert.That(tilt, Is.GreaterThan(0.1f).And.LessThan(4f));
        }

        [Test]
        public void RefusedPlacementShudders()
        {
            CinnaAlignmentRelic relic = RegisterRelic();
            Transform visual = Visual(relic);
            Advance(relic, 0f);
            Lift();
            Advance(relic, 1f);
            Advance(relic, 0.001f);
            Vector3 settled = visual.position;
            Assert.That(caster.TryPlaceHeldRelic(new Vector2Int(-5, -5)), Is.False);
            Advance(relic, 0.05f);
            Assert.That(Mathf.Abs(visual.position.x - settled.x), Is.GreaterThan(0.005f), "The relic shakes in Vis's hold.");
            Assert.That(caster.HeldRelic, Is.EqualTo(Relic));
        }

        [Test]
        public void DeliveryFloatsOntoTheStandAndRingsOut()
        {
            CinnaAlignmentRelic relic = RegisterRelic();
            Transform visual = Visual(relic);
            GameObject ring = relic.transform.Find("Seat Shockwave").gameObject;
            Advance(relic, 0f);
            Lift();
            Advance(relic, 1f);
            if (standHost == null) standHost = new GameObject("alignment-stand");
            world.BeginMapRebuild(Trial);
            world.RegisterObject(Stand, standHost, StandCell, Vector2Int.one, false, StandTags);
            CinnaAlignmentRelic.RegisterForMap(Area("C03.IceTrial.asset"), grid);
            world.EndMapRebuild();
            Advance(relic, 0.01f);
            Assert.That(visual.gameObject.activeSelf, Is.True, "The held relic follows the player into the trial.");

            Assert.That(caster.TryPlaceHeldRelic(StandCell), Is.True);
            Advance(relic, 0.4f);
            Vector3 stand = grid.CellToWorld(StandCell, CinnaIceTrialRoom.StandHeight);
            Assert.That(visual.position, Is.EqualTo(stand));
            Assert.That(ring.activeSelf, Is.True, "A shockwave spreads from the stand.");
            Advance(relic, 0.2f);
            Assert.That(ring.GetComponent<LineRenderer>().GetPosition(0).x, Is.GreaterThan(0.45f));
            Advance(relic, 1f);
            Assert.That(ring.activeSelf, Is.False);
            Assert.That(visual.position, Is.EqualTo(stand), "The relic rests on the stand.");
        }

        [Test]
        public void RelicIsHiddenOnUnrelatedMapsWhenNotHeld()
        {
            CinnaAlignmentRelic relic = RegisterRelic();
            Transform visual = Visual(relic);
            world.BeginMapRebuild("westForest");
            CinnaAlignmentRelic.RegisterForMap(new MapDefinition { id = "westForest" }, grid);
            world.EndMapRebuild();
            Advance(relic, 0.01f);
            Assert.That(visual.gameObject.activeSelf, Is.False);
        }

        // ---- In-place Vis (objects without vis-lift) ----

        [TestCase("vis-target:90", 90, true)]
        [TestCase("vis-target:450", 90, true)]
        [TestCase("vis-target:-270", 90, true)]
        [TestCase("vis-target:45", 0, false)]
        [TestCase("vis-target:bad", 0, false)]
        [TestCase("vis-target:", 0, false)]
        [TestCase("vis-target", 0, false)]
        public void InPlaceVisHonoursExplicitQuarterTurnAndRejectsInvalidAuthoring(string tag, int expected, bool succeeds)
        {
            runtime.SetFlag("spell.unlocked.Vis", true);
            BuildPractice(new[] { "vis", tag });
            Assert.That(caster.Cast(SpellId.Vis, Practice, Vector2Int.up), Is.EqualTo(succeeds));
            Assert.That(State(Practice).rotation, Is.EqualTo(expected));
            Assert.That(State(Practice).aligned, Is.EqualTo(succeeds));
            Assert.That(world.EvaluatePuzzleTarget(PracticeTarget()), Is.EqualTo(succeeds));
        }

        [TestCase("vis-target:180")]
        [TestCase("vis-target:bad")]
        [TestCase("vis-target:90")]
        public void InPlaceVisRejectsMultipleTargetDeclarationsRegardlessOfOrder(string second)
        {
            runtime.SetFlag("spell.unlocked.Vis", true);
            foreach (string[] tags in new[] { new[] { "vis", "vis-target:90", second }, new[] { "vis", second, "vis-target:90" } })
            {
                BuildPractice(tags);
                Assert.That(caster.Cast(SpellId.Vis, Practice, Vector2Int.up), Is.False);
                Assert.That(State(Practice).aligned, Is.False);
            }
        }

        [Test]
        public void CachedPrefabRegistrationRetainsOriginalResetAngle()
        {
            markerHost = new GameObject("cached-marker");
            WorldObjectRegistration registration = markerHost.AddComponent<WorldObjectRegistration>();
            registration.Configure(Practice, new Vector2Int(40, 22), Vector2Int.one, true, new[] { "vis", "vis-target:90" });
            Call(registration, "Awake");
            world.BeginMapRebuild("campus");
            markerHost.SetActive(false);
            markerHost.SetActive(true); Call(registration, "OnEnable");
            world.EndMapRebuild();
            runtime.SetFlag("spell.unlocked.Vis", true);
            caster.Cast(SpellId.Vis, Practice, Vector2Int.up);
            world.BeginMapRebuild("westForest"); world.EndMapRebuild();
            world.BeginMapRebuild("campus");
            markerHost.SetActive(false);
            markerHost.SetActive(true); Call(registration, "OnEnable");
            world.EndMapRebuild();
            Assert.That(State(Practice).rotation, Is.EqualTo(90));
            Assert.That(world.ResetAlignmentObject(Practice), Is.True);
            Assert.That(State(Practice).rotation, Is.EqualTo(0));
            Assert.That(markerHost.transform.eulerAngles.y, Is.EqualTo(0));
            caster.Cast(SpellId.Vis, Practice, Vector2Int.up);
            world.ResetAllState();
            world.BeginMapRebuild("campus");
            markerHost.SetActive(false);
            markerHost.SetActive(true); Call(registration, "OnEnable");
            world.EndMapRebuild();
            Assert.That(State(Practice).rotation, Is.EqualTo(0));
            Assert.That(markerHost.transform.eulerAngles.y, Is.EqualTo(0));
        }

        // ---- Hand-off to Cold Feet ----

        [Test]
        public void TrialHandoffUsesOnlyNextQuestDialogueAndKeepsVisibilityGates()
        {
            NpcCatalogAsset catalog = null;
            foreach (NpcCatalogAsset candidate in UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/World/Areas/NpcCatalog.asset")) catalog = candidate;
            Assert.That(catalog, Is.Not.Null);
            NpcDefinition trial = Array.Find(catalog.npcs, n => n.id == "cinna_cirra_trial");
            Assert.That(trial.showWhenQuestCompleted, Is.EqualTo(Quest));
            Assert.That(trial.dialogue, Has.Length.EqualTo(3));
            foreach (NpcDialogueDefinition item in trial.dialogue)
                Assert.That(item.dialogueId, Is.EqualTo("cinna_cold_feet_warm_idea_dialogue"));
            NpcDefinition orientation = Array.Find(catalog.npcs, n => n.id == "cinna_cirra_orientation");
            Assert.That(orientation.hideWhenQuestCompleted, Is.EqualTo(Quest));
            Lumenfall.Story.NpcIntroductions.MarkMet("cinna_cirra_trial");
            markerHost = new GameObject("trial-npc");
            DynamicNpc npc = markerHost.AddComponent<DynamicNpc>();
            npc.Configure(trial, null);
            npc.RefreshVisibility();
            Assert.That(markerHost.activeSelf, Is.False);
            runtime.StartQuest(Quest); runtime.CompleteQuest(Quest);
            npc.RefreshVisibility();
            Assert.That(markerHost.activeSelf, Is.True);
            for (int i = 0; i < 12; i++)
            {
                DialogueSession session = npc.Interact();
                Assert.That(session.Definition.id, Is.EqualTo("cinna_cold_feet_warm_idea_dialogue"));
                session.End();
            }
        }
    }
}
