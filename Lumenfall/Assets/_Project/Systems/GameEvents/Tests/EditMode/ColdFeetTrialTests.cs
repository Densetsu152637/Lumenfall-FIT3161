using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.Spells;
using Lumenfall.GridMovement;
using Lumenfall.DataDriven;
using Lumenfall.Quests;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class ColdFeetTrialTests
    {
        private const string Trial = "trial.cinna.ice", Quest = "cinna_cold_feet_warm_idea";
        private readonly List<GameObject> objects = new List<GameObject>();
        private GridWorld grid;
        private WorldStateService state;
        private SpellCaster caster;
        private GameEventRuntime runtime;
        private static readonly string[] Tags = { "glaciate", "slide-goal:24,34", "slide-lane:20,34,24,34" };
        [SetUp] public void SetUp()
        {
            GameEventManager.Instance.ReloadDefinitions(); runtime = GameEventManager.Instance.Runtime;
            grid = Create("grid", Vector2Int.zero).AddComponent<GridWorld>(); grid.ConfigureBounds(80,80);
            state = Create("state", Vector2Int.zero).AddComponent<WorldStateService>(); state.BeginMapRebuild("campus");
            RegisterTrial(); state.EndMapRebuild(); caster = Create("caster", new Vector2Int(19,35)).AddComponent<SpellCaster>();
        }
        [TearDown] public void TearDown()
        {
            runtime.ActiveDialogue?.End();
            GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i=objects.Count-1;i>=0;i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear(); GameEventManager.Instance.Runtime.ResetState();
        }
        private GameObject Create(string name, Vector2Int cell)
        { GameObject go = new GameObject(name); objects.Add(go); go.transform.position = new Vector3(cell.x,0,cell.y); return go; }
        private void RegisterTrial(Vector2Int? size = null)
        { GameObject go = Create(Trial,new Vector2Int(20,34)); state.RegisterObject(Trial,go,new Vector2Int(20,34),size ?? Vector2Int.one,true,Tags); }
        private WorldObjectSnapshot Snapshot() { Assert.That(state.TryGetObject(Trial,out WorldObjectSnapshot s),Is.True); return s; }
        private void Activate()
        { runtime.StartQuest("cinna_out_of_alignment"); runtime.CompleteQuest("cinna_out_of_alignment"); runtime.StartQuest(Quest); }
        private void Unlock() { Activate(); runtime.SetFlag("spell.unlocked.Glaciate",true); }
        private PuzzleTargetDefinition Target() => new PuzzleTargetDefinition { objectIds=new[]{Trial},requiredFrozen=true,requiredVisitedTraps=4,requiredUninterruptedSlide=true,requiredPositionX=24,requiredPositionY=34 };
        private void Step(int count) { for(int i=0;i<count;i++) Assert.That(state.TrySlideIceObject(Trial,Vector2Int.right),Is.True); }

        [Test] public void LessonRequiresPrerequisiteActiveQuestAndTerminalContinue()
        {
            DialogueSession unavailable = runtime.StartDialogue(Quest+"_dialogue");
            Assert.That(unavailable.AvailableChoices.Any(c=>c.id=="accept"),Is.False); unavailable.End();
            runtime.StartQuest("cinna_out_of_alignment"); runtime.CompleteQuest("cinna_out_of_alignment");
            DialogueSession abandoned=runtime.StartDialogue(Quest+"_dialogue"); abandoned.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.False);
            DialogueSession accepted=runtime.StartDialogue(Quest+"_dialogue"); Assert.That(accepted.SelectChoice("accept"),Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.False); accepted.End();
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.False);
            DialogueSession lesson=runtime.StartDialogue(Quest+"_dialogue"); Assert.That(lesson.SelectChoice("active"),Is.True);
            Assert.That(lesson.Continue(),Is.True); Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.True);
            string saved=runtime.ExportStateJson(false); runtime.ResetState(); Assert.That(runtime.ImportStateJson(saved),Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.True);
        }
        [Test] public void DirectLessonCannotGrantWithoutActiveQuestAndPrerequisite()
        {
            runtime.StartQuest(Quest);
            DialogueSession lesson=runtime.StartDialogue(Quest+"_dialogue"); lesson.SelectChoice("active"); lesson.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.False);
        }
        [Test] public void ReplacementRuntimeTeachesAndDisabledCasterRebindsOnEnable()
        {
            GameEventRuntime old=runtime; GameEventManager.Instance.ReloadDefinitions(); runtime=GameEventManager.Instance.Runtime;
            Activate(); DialogueSession lesson=runtime.StartDialogue(Quest+"_dialogue"); lesson.SelectChoice("active"); lesson.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.True); Assert.That(old.GetFlag("spell.unlocked.Glaciate"),Is.False);
            caster.enabled=false; runtime.ResetState(); caster.enabled=true; Activate();
            lesson=runtime.StartDialogue(Quest+"_dialogue"); lesson.SelectChoice("active"); lesson.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Glaciate),Is.True);
        }
        [Test] public void ValidCastVisitsEachTrapAndStopsAtGoalWithoutSecondCompletion()
        {
            Unlock(); List<Vector2Int> visited=new List<Vector2Int>(); state.StateChanged += s=>visited.Add(Snapshot().cell);
            Assert.That(caster.Cast(SpellId.Glaciate,Trial,Vector2Int.right),Is.True);
            Assert.That(visited,Is.EqualTo(new[]{new Vector2Int(21,34),new Vector2Int(22,34),new Vector2Int(23,34),new Vector2Int(24,34)}));
            Assert.That(state.EvaluatePuzzleTarget(Target()),Is.True); Assert.That(Snapshot().visitedTrapCount,Is.EqualTo(4));
            Assert.That(caster.Cast(SpellId.Glaciate,Trial,Vector2Int.right),Is.False); Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(24,34)));
            Assert.That(grid.IsCellAvailable(new Vector2Int(22,35)),Is.True);
        }
        [Test] public void FrozenGoalAndGenericMutationCannotForgeVisitProof()
        {
            state.SetProperty(Trial,s=>{s.frozen=true;s.cell=new Vector2Int(24,34);s.visitedTrapCount=4;s.slideInterrupted=false;});
            Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(20,34))); Assert.That(Snapshot().visitedTrapCount,Is.Zero);
            Assert.That(state.EvaluatePuzzleTarget(Target()),Is.False);
        }
        [TestCase(0,1)] [TestCase(-1,0)] [TestCase(1,1)] [TestCase(0,0)]
        public void WrongDirectionOrOffLaneRequiresReset(int x,int y)
        {
            Assert.That(state.TrySlideIceObject(Trial,new Vector2Int(x,y)),Is.False);
            Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(20,34))); Assert.That(Snapshot().slideInterrupted,Is.True);
            Assert.That(state.TrySlideIceObject(Trial,Vector2Int.right),Is.False);
            Assert.That(state.ResetIceSlide(Trial),Is.True); Step(4); Assert.That(state.EvaluatePuzzleTarget(Target()),Is.True);
        }
        [Test] public void GeneralMovementCannotSlideTrialOffLane()
        {
            Assert.That(state.TryMoveObject(Trial,Vector2Int.up),Is.False);
            Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(20,34))); Assert.That(Snapshot().slideInterrupted,Is.True);
        }
        [Test] public void BlockedCastClearsPartialSequenceAndResetRetries()
        {
            Unlock(); GameObject blocker=Create("blocker",new Vector2Int(23,34)); GridObstacle obstacle=blocker.AddComponent<GridObstacle>(); obstacle.Configure(grid,Vector2Int.one);
            Assert.That(caster.Cast(SpellId.Glaciate,Trial,Vector2Int.right),Is.True);
            Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(22,34))); Assert.That(Snapshot().visitedTrapCount,Is.Zero); Assert.That(Snapshot().slideInterrupted,Is.True);
            Assert.That(state.EvaluatePuzzleTarget(Target()),Is.False); obstacle.enabled=false;
            Assert.That(caster.ResetIceTrial(Trial),Is.True); Assert.That(Snapshot().frozen,Is.False);
            Assert.That(caster.Cast(SpellId.Glaciate,Trial,Vector2Int.right),Is.True); Assert.That(state.EvaluatePuzzleTarget(Target()),Is.True);
        }
        [Test] public void ResetFailsWithoutMutationWhenStartIsOccupied()
        {
            Step(2); GameObject actor=Create("occupant",new Vector2Int(20,34)); actor.AddComponent<GridMover>();
            Assert.That(state.ResetIceSlide(Trial),Is.False); Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(22,34))); Assert.That(Snapshot().visitedTrapCount,Is.EqualTo(2));
            actor.SetActive(false); Assert.That(state.ResetIceSlide(Trial),Is.True);
        }
        [Test] public void MovementRejectsOccupiedDestinationAndFootprintBlocker()
        {
            GameObject actor=Create("occupant",new Vector2Int(21,34)); actor.AddComponent<GridMover>();
            Assert.That(state.TrySlideIceObject(Trial,Vector2Int.right),Is.False); actor.SetActive(false);
            state.BeginMapRebuild("campus-wide"); RegisterTrial(new Vector2Int(2,1)); state.EndMapRebuild();
            GameObject blocker=Create("blocker",new Vector2Int(22,34)); blocker.AddComponent<GridObstacle>().Configure(grid,Vector2Int.one);
            Assert.That(state.TrySlideIceObject(Trial,Vector2Int.right),Is.False); Assert.That(Snapshot().cell,Is.EqualTo(new Vector2Int(20,34)));
        }
        [Test] public void PartialSaveRestoresAndMapArchiveResumesInOrder()
        {
            Step(2); string save=state.CaptureStateJson(); Step(2); Assert.That(state.RestoreStateJson(save),Is.True);
            Assert.That(Snapshot().visitedTrapCount,Is.EqualTo(2)); Step(2); Assert.That(state.EvaluatePuzzleTarget(Target()),Is.True);
            state.BeginMapRebuild("other"); state.EndMapRebuild(); state.BeginMapRebuild("campus"); RegisterTrial(); state.EndMapRebuild();
            Assert.That(state.EvaluatePuzzleTarget(Target()),Is.True);
        }
        [Test] public void InterruptedSaveRequiresResetAndLegacyGoalSaveHasNoProof()
        {
            Step(1); state.TrySlideIceObject(Trial,Vector2Int.left); string saved=state.CaptureStateJson(); state.ResetIceSlide(Trial);
            Assert.That(state.RestoreStateJson(saved),Is.True); Assert.That(Snapshot().slideInterrupted,Is.True); Assert.That(state.TrySlideIceObject(Trial,Vector2Int.right),Is.False);
            Assert.That(state.RestoreStateJson("{\"mapId\":\"campus\",\"objects\":[{\"id\":\"trial.cinna.ice\",\"cell\":{\"x\":24,\"y\":34},\"frozen\":true}]}"),Is.True);
            Assert.That(state.EvaluatePuzzleTarget(Target()),Is.False); Assert.That(Snapshot().visitedTrapCount,Is.Zero);
            Assert.That(Snapshot().slideInterrupted,Is.True); Assert.That(state.ResetIceSlide(Trial),Is.True);
        }
        [Test] public void MalformedSavedCountAndSavedTagsCannotAlterAuthoredLane()
        {
            string saved="{\"mapId\":\"campus\",\"objects\":[{\"id\":\"trial.cinna.ice\",\"cell\":{\"x\":24,\"y\":34},\"frozen\":true,\"visitedTrapCount\":999,\"tags\":[\"slide-lane:24,34,25,34\"]}]}";
            Assert.That(state.RestoreStateJson(saved),Is.True); Assert.That(state.EvaluatePuzzleTarget(Target()),Is.False);
            WorldObjectSnapshot copy=Snapshot(); copy.tags[2]="slide-lane:20,34,21,34";
            Assert.That(Snapshot().tags[2],Is.EqualTo(Tags[2])); state.SetProperty(Trial,s=>s.tags[2]="bad");
            Assert.That(Snapshot().tags[2],Is.EqualTo(Tags[2])); Assert.That(state.ResetIceSlide(Trial),Is.True); Step(4);
        }
        [Test] public void CompletedQuestCannotResetAndMonitorSuccessIsDurableAndIdempotent()
        {
            Unlock(); QuestPuzzleMonitor monitor=Create("monitor",Vector2Int.zero).AddComponent<QuestPuzzleMonitor>();
            monitor.EvaluatePuzzles(); Assert.That(runtime.GetQuestStatus(Quest),Is.EqualTo(QuestStatus.Active));
            Step(4); monitor.EvaluatePuzzles(); Assert.That(runtime.GetQuestStatus(Quest),Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.ActiveDialogue.Definition.id,Is.EqualTo("cinna_cold_feet_success_dialogue")); runtime.ActiveDialogue.Continue();
            string saved=runtime.ExportStateJson(false); runtime.ResetState(); runtime.ImportStateJson(saved); monitor.EvaluatePuzzles();
            Assert.That(runtime.ActiveDialogue,Is.Null); Assert.That(caster.ResetIceTrial(Trial),Is.False);
            monitor.enabled=false; monitor.enabled=true; monitor.EvaluatePuzzles(); Assert.That(runtime.ActiveDialogue,Is.Null);
        }
        [Test] public void CastRequiresActiveQuestAndIgnoresOtherSpellsOnLane()
        {
            runtime.SetFlag("spell.unlocked.Glaciate",true); Assert.That(caster.Cast(SpellId.Glaciate,Trial,Vector2Int.right),Is.False);
            Unlock(); runtime.SetFlag("spell.unlocked.Gravitate",true); Assert.That(caster.Cast(SpellId.Gravitate,Trial,Vector2Int.right),Is.False);
            Assert.That(Snapshot().visitedTrapCount,Is.Zero);
        }
        [TestCase("slide-lane:20,34,20,34")] [TestCase("slide-lane:20,34,24,35")] [TestCase("slide-lane:bad")] [TestCase("slide-lane:0,0,999,0")]
        public void InvalidAuthoringCannotParseOrMove(string tag)
        { Assert.That(IceSlideLane.TryParse(new[]{tag},out _),Is.False); }
    }
}
