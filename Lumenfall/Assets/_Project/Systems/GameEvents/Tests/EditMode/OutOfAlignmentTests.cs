using System;
using System.Reflection;
using Lumenfall.GameEvents;
using Lumenfall.DataDriven;
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
        private const string Marker = "orientation.cinna.align";
        private GameEventRuntime runtime;
        private GameObject worldHost, casterHost, markerHost, monitorHost;
        private WorldStateService world;
        private SpellCaster caster;
        private float previousTimeScale;
        private Action<GameEventDefinition> completionHandler;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 1;
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            worldHost = new GameObject("alignment-world");
            world = worldHost.AddComponent<WorldStateService>();
            casterHost = new GameObject("alignment-caster");
            caster = casterHost.AddComponent<SpellCaster>();
        }

        [TearDown]
        public void TearDown()
        {
            if (completionHandler != null) runtime.EventTriggered -= completionHandler;
            completionHandler = null;
            if (monitorHost != null) Object.DestroyImmediate(monitorHost);
            if (markerHost != null) Object.DestroyImmediate(markerHost);
            GameSaveManager.Instance.UnregisterProvider(world.SaveKey);
            Object.DestroyImmediate(casterHost);
            Object.DestroyImmediate(worldHost);
            runtime.ResetState();
            Time.timeScale = previousTimeScale;
        }

        private void BuildCampus(string[] tags = null, float authoredAngle = 0)
        {
            world.BeginMapRebuild("campus");
            world.RegisterObject(Marker, null, new Vector2Int(40, 22), Vector2Int.one, true,
                tags ?? new[] { "vis", "vis-target:90" }, authoredAngle);
            world.RegisterObject("unrelated", null, Vector2Int.zero, Vector2Int.one, true, new[] { "ampere" });
            world.EndMapRebuild();
        }
        private WorldObjectSnapshot MarkerState()
        {
            Assert.That(world.TryGetObject(Marker, out WorldObjectSnapshot marker), Is.True);
            return marker;
        }
        private static PuzzleTargetDefinition Target(string id = Marker)
        { return new PuzzleTargetDefinition { objectIds = new[] { id }, requiredAligned = true, requiredRotation = 90 }; }
        private void Poll(QuestPuzzleMonitor monitor)
        {
            typeof(QuestPuzzleMonitor).GetField("nextCheck", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(monitor, float.MinValue);
            typeof(QuestPuzzleMonitor).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(monitor, null);
        }

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

        [TestCase("vis-target:90", 90, true)]
        [TestCase("vis-target:450", 90, true)]
        [TestCase("vis-target:-270", 90, true)]
        [TestCase("vis-target:45", 0, false)]
        [TestCase("vis-target:bad", 0, false)]
        [TestCase("vis-target:", 0, false)]
        [TestCase("vis-target", 0, false)]
        public void VisHonoursExplicitQuarterTurnAndRejectsInvalidAuthoring(string tag, int expected, bool succeeds)
        {
            runtime.SetFlag("spell.unlocked.Vis", true);
            BuildCampus(new[] { "vis", tag });
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.EqualTo(succeeds));
            Assert.That(MarkerState().rotation, Is.EqualTo(expected));
            Assert.That(MarkerState().aligned, Is.EqualTo(succeeds));
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.EqualTo(succeeds));
        }

        [TestCase("vis-target:180")]
        [TestCase("vis-target:bad")]
        [TestCase("vis-target:90")]
        public void VisRejectsMultipleTargetDeclarationsRegardlessOfOrder(string second)
        {
            runtime.SetFlag("spell.unlocked.Vis", true);
            foreach (string[] tags in new[] { new[] { "vis", "vis-target:90", second }, new[] { "vis", second, "vis-target:90" } })
            {
                BuildCampus(tags);
                Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.False);
                Assert.That(MarkerState().aligned, Is.False);
            }
        }

        [Test]
        public void VisRejectsIncompatibleObjectAndLockedOrOverlayInput()
        {
            BuildCampus(new[] { "ampere", "vis-target:90" });
            runtime.SetFlag("spell.unlocked.Vis", true);
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.False);
            world.ResetAllState();
            BuildCampus();
            runtime.SetFlag("spell.unlocked.Vis", false);
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.False);
            runtime.SetFlag("spell.unlocked.Vis", true);
            runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.False);
            Assert.That(MarkerState().aligned, Is.False);
        }

        [Test]
        public void PredicateRequiresExactObjectAlignedAndNormalizedNinetyDegrees()
        {
            BuildCampus();
            world.SetProperty(Marker, x => { x.rotation = 90; x.aligned = false; });
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.False);
            world.SetProperty(Marker, x => { x.rotation = 0; x.aligned = true; });
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.False);
            world.SetProperty(Marker, x => x.rotation = -270);
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.True);
            Assert.That(world.EvaluatePuzzleTarget(Target("another-marker")), Is.False);
            world.SetProperty(Marker, x => x.rotation = 90.25f);
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.False);
        }

        [Test]
        public void VisAndCompletionAreIdempotentAcrossSaveAndRebuild()
        {
            BuildCampus();
            runtime.StartQuest(Quest);
            runtime.SetFlag("spell.unlocked.Vis", true);
            monitorHost = new GameObject("alignment-monitor");
            QuestPuzzleMonitor monitor = monitorHost.AddComponent<QuestPuzzleMonitor>();
            int completions = 0, changes = 0;
            completionHandler = e => { if (e.id == "complete_cinna_out_of_alignment") completions++; };
            runtime.EventTriggered += completionHandler;
            world.StateChanged += _ => changes++;
            world.SetProperty(Marker, x => x.aligned = true);
            Poll(monitor);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.True);
            int afterCast = changes;
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.False);
            Assert.That(changes, Is.EqualTo(afterCast));
            Poll(monitor);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(completions, Is.EqualTo(1));
            string worldSave = world.CaptureStateJson(), eventSave = runtime.ExportStateJson(false);
            runtime.ResetState();
            world.ResetAllState();
            runtime.ImportStateJson(eventSave);
            world.RestoreStateJson(worldSave);
            BuildCampus();
            Poll(monitor);
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
        }

        [Test]
        public void LegacySaveRestoresProgressButKeepsCurrentTargetingAndCompatibility()
        {
            BuildCampus(new[] { "vis" });
            world.SetProperty(Marker, x => x.spellCompatible = false);
            world.SetProperty("unrelated", x => x.powered = true);
            string legacy = world.CaptureStateJson();
            world.ResetAllState();
            BuildCampus();
            Assert.That(world.RestoreStateJson(legacy), Is.True);
            Assert.That(MarkerState().tags, Is.EqualTo(new[] { "vis", "vis-target:90" }));
            Assert.That(MarkerState().spellCompatible, Is.True);
            runtime.SetFlag("spell.unlocked.Vis", true);
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.True);
            Assert.That(world.EvaluatePuzzleTarget(Target()), Is.True);
            Assert.That(world.TryGetObject("unrelated", out WorldObjectSnapshot unrelated), Is.True);
            Assert.That(unrelated.powered, Is.True);
        }

        [Test]
        public void ActiveResetRestoresAuthoredAngleAndPreservesUnrelatedProgressAcrossRetry()
        {
            BuildCampus(authoredAngle: 180);
            runtime.StartQuest(Quest);
            runtime.StartQuest("orio_patch_notes");
            runtime.SetFlag("unrelated-progress", true);
            runtime.SetFlag("spell.unlocked.Vis", true);
            world.SetProperty("unrelated", x => x.powered = true);
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.True);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.True);
            Assert.That(MarkerState().rotation, Is.EqualTo(180));
            Assert.That(MarkerState().aligned, Is.False);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetQuestStatus("orio_patch_notes"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetFlag("unrelated-progress"), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Vis), Is.True);
            world.TryGetObject("unrelated", out WorldObjectSnapshot unrelated);
            Assert.That(unrelated.powered, Is.True);
            string save = world.CaptureStateJson();
            world.ResetAllState();
            world.RestoreStateJson(save);
            BuildCampus(authoredAngle: 180);
            Assert.That(MarkerState().rotation, Is.EqualTo(180));
            Assert.That(MarkerState().aligned, Is.False);
            Assert.That(caster.Cast(SpellId.Vis, Marker, Vector2Int.up), Is.True);
        }

        [Test]
        public void ResetRejectsInactiveCompletedWrongMapAndOverlayWithoutMutation()
        {
            BuildCampus();
            world.SetProperty(Marker, x => { x.rotation = 90; x.aligned = true; });
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            runtime.StartQuest(Quest);
            runtime.StartDialogue("cinna_out_of_alignment_dialogue");
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            runtime.ActiveDialogue.End();
            runtime.CompleteQuest(Quest);
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            Assert.That(MarkerState().aligned, Is.True);
            runtime.ResetState();
            runtime.StartQuest(Quest);
            world.BeginMapRebuild("eastForest");
            world.RegisterObject(Marker, null, Vector2Int.zero, Vector2Int.one, true, new[] { "vis", "vis-target:90" });
            world.EndMapRebuild();
            world.SetProperty(Marker, x => { x.rotation = 90; x.aligned = true; });
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.False);
            Assert.That(MarkerState().aligned, Is.True);
        }

        [Test]
        public void CachedPrefabRegistrationRetainsOriginalResetAngle()
        {
            markerHost = new GameObject("cached-marker");
            WorldObjectRegistration registration = markerHost.AddComponent<WorldObjectRegistration>();
            registration.Configure(Marker, new Vector2Int(40, 22), Vector2Int.one, true, new[] { "vis", "vis-target:90" });
            world.BeginMapRebuild("campus");
            markerHost.SetActive(false); markerHost.SetActive(true);
            world.EndMapRebuild();
            runtime.StartQuest(Quest);
            runtime.SetFlag("spell.unlocked.Vis", true);
            caster.Cast(SpellId.Vis, Marker, Vector2Int.up);
            world.BeginMapRebuild("eastForest"); world.EndMapRebuild();
            world.BeginMapRebuild("campus");
            markerHost.SetActive(false); markerHost.SetActive(true);
            world.EndMapRebuild();
            Assert.That(MarkerState().rotation, Is.EqualTo(90));
            Assert.That(caster.ResetActiveAlignmentPuzzle(), Is.True);
            Assert.That(MarkerState().rotation, Is.EqualTo(0));
            Assert.That(markerHost.transform.eulerAngles.y, Is.EqualTo(0));
            caster.Cast(SpellId.Vis, Marker, Vector2Int.up);
            world.ResetAllState();
            world.BeginMapRebuild("campus");
            markerHost.SetActive(false); markerHost.SetActive(true);
            world.EndMapRebuild();
            Assert.That(MarkerState().rotation, Is.EqualTo(0));
            Assert.That(markerHost.transform.eulerAngles.y, Is.EqualTo(0));
        }

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
