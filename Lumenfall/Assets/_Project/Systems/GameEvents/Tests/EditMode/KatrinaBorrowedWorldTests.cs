using System.Collections.Generic;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class KatrinaBorrowedWorldTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private WorldStateService state;
        private GridWorld grid;
        private GameEventRuntime Runtime => GameEventManager.Instance.Runtime;
        private const string Keepsake = "keepsake.katrina.hidden";
        private static readonly Vector2Int Cell = new Vector2Int(31, 42);
        private static readonly string[] EchoTags = { "hidden", "lost-echoes" };

        [SetUp]
        public void SetUp()
        {
            Runtime.ResetState();
            grid = Create("grid").AddComponent<GridWorld>(); grid.ConfigureBounds(100, 60);
            state = Create("state").AddComponent<WorldStateService>(); state.BeginMapRebuild("campus");
        }
        [TearDown]
        public void TearDown()
        {
            Runtime.ResetState();
            GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void RevealHonorsManhattanBoundaryTargetCompatibilityAndRepeat()
        {
            Register(Keepsake, Cell, EchoTags);
            Register("other", Cell + Vector2Int.right, EchoTags);
            Register("hidden-only", Cell, new[] { "hidden" });
            Register("visible", Cell, new[] { "lost-echoes" });
            GameObject incompatible = Create("incompatible");
            state.RegisterObject("incompatible", incompatible, Cell, Vector2Int.one, false, EchoTags);
            state.EndMapRebuild();
            Assert.That(state.TryRevealObject(Keepsake, Cell + new Vector2Int(2, 1), 2), Is.False);
            Assert.That(state.TryRevealObject("hidden-only", Cell, 2), Is.False);
            Assert.That(state.TryRevealObject("visible", Cell, 2), Is.False);
            Assert.That(state.TryRevealObject("incompatible", Cell, 2), Is.False);
            Assert.That(state.TryRevealObject(Keepsake, Cell + new Vector2Int(1, 1), 2), Is.True);
            Assert.That(state.TryGetObject("other", out WorldObjectSnapshot other) && other.revealed, Is.False);
            Assert.That(state.TryRevealObject(Keepsake, Cell, 2), Is.False);
        }

        [Test]
        public void DirectCastRejectsDistantAndUnhiddenTargetsAndReportsSearchFeedback()
        {
            Register(Keepsake, Cell, EchoTags);
            Register("visible", new Vector2Int(1, 0), null);
            GameObject player = Create("player"); player.AddComponent<GridMover>();
            SpellCaster caster = player.AddComponent<SpellCaster>();
            Runtime.SetFlag("spell.unlocked.LostEchoes", true); state.EndMapRebuild();
            Assert.That(caster.Cast(SpellId.LostEchoes, Keepsake, Vector2Int.up), Is.False);
            Assert.That(caster.LastSearchFeedback, Does.Contain("within two cells"));
            Assert.That(caster.Cast(SpellId.LostEchoes, "visible", Vector2Int.up), Is.False);
            Assert.That(state.TryGetObject(Keepsake, out WorldObjectSnapshot target) && target.revealed, Is.False);
        }

        [Test]
        public void InitiallyHiddenRenderersRestoreRevealWithoutEnablingAuthoredDisabledRenderer()
        {
            GameObject target = Register(Keepsake, Cell, EchoTags, render: true);
            Renderer visible = target.GetComponent<Renderer>();
            Assert.That(visible.enabled, Is.False);
            GameObject child = Create("disabled-renderer"); child.transform.SetParent(target.transform);
            Renderer disabled = child.AddComponent<MeshRenderer>(); disabled.enabled = false;
            state.BeginMapRebuild("campus");
            state.RegisterObject(Keepsake, target, Cell, Vector2Int.one, true, EchoTags); state.EndMapRebuild();
            Assert.That(state.TryRevealObject(Keepsake, Cell, 2), Is.True);
            Assert.That(visible.enabled, Is.True);
            Assert.That(disabled.enabled, Is.False);
            Assert.That(target.activeSelf, Is.True);
        }

        [Test]
        public void CachedUnrevealedAreaReturnsThenRevealAndNewGameRemainVisibleWhenRevealed()
        {
            GameObject target = Register(Keepsake, Cell, EchoTags, render: true); state.EndMapRebuild();
            state.BeginMapRebuild("eastForest"); state.EndMapRebuild();
            state.BeginMapRebuild("campus"); state.RegisterObject(Keepsake, target, Cell, Vector2Int.one, true, EchoTags); state.EndMapRebuild();
            Assert.That(target.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(state.TryRevealObject(Keepsake, Cell, 2), Is.True);
            Assert.That(target.GetComponent<Renderer>().enabled, Is.True);
            state.ResetAllState(); state.BeginMapRebuild("campus");
            state.RegisterObject(Keepsake, target, Cell, Vector2Int.one, true, EchoTags); state.EndMapRebuild();
            Assert.That(target.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(state.TryRevealObject(Keepsake, Cell, 2), Is.True);
            Assert.That(target.GetComponent<Renderer>().enabled, Is.True);
        }

        [Test]
        public void SavedAndArchivedRevealsRestorePresentationOnNewAndCachedObjects()
        {
            GameObject target = Register(Keepsake, Cell, EchoTags, render: true); state.EndMapRebuild();
            state.TryRevealObject(Keepsake, Cell, 2); string saved = state.CaptureStateJson().Replace("\"tags\":[\"hidden\",\"lost-echoes\"]", "\"tags\":[]");
            state.BeginMapRebuild("eastForest"); state.EndMapRebuild();
            state.BeginMapRebuild("campus"); state.RegisterObject(Keepsake, target, Cell, Vector2Int.one, true, EchoTags); state.EndMapRebuild();
            Assert.That(target.GetComponent<Renderer>().enabled, Is.True);
            state.ResetAllState(); state.BeginMapRebuild("campus");
            GameObject rebuilt = Register(Keepsake, Cell, EchoTags, render: true);
            Assert.That(state.RestoreStateJson(saved), Is.True); state.EndMapRebuild();
            Assert.That(rebuilt.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(state.TryGetObject(Keepsake, out WorldObjectSnapshot restored) && WorldStateService.IsLostEchoesTarget(restored), Is.True, "Authored tags survive a legacy save with absent tags.");
            Assert.That(state.TryRevealObject(Keepsake, Cell, 2), Is.False);
        }

        [Test]
        public void NonHiddenPresentationRemainsOwnedByItsExistingRendererController()
        {
            GameObject visible = Register("visible", Cell, null, render: true); state.EndMapRebuild();
            visible.GetComponent<Renderer>().enabled = false;
            state.SetProperty("visible", value => value.powered = true);
            Assert.That(visible.GetComponent<Renderer>().enabled, Is.False);
        }

        [Test]
        public void MonitorRejectsWrongMapPositionUnrelatedRevealAndInactiveQuestThenCompletesOnce()
        {
            QuestPuzzleMonitor monitor = Create("monitor").AddComponent<QuestPuzzleMonitor>();
            Register("other", Cell, EchoTags); state.EndMapRebuild(); state.TryRevealObject("other", Cell, 2);
            Runtime.StartQuest("katrina_something_borrowed");
            Assert.That(monitor.EvaluatePuzzles(), Is.Zero);
            state.BeginMapRebuild("eastForest"); Register(Keepsake, Cell, EchoTags); state.EndMapRebuild(); state.TryRevealObject(Keepsake, Cell, 2);
            Assert.That(monitor.EvaluatePuzzles(), Is.Zero);
            state.BeginMapRebuild("campus"); Register(Keepsake, Cell + Vector2Int.right, EchoTags); state.EndMapRebuild(); state.TryRevealObject(Keepsake, Cell, 2);
            Assert.That(monitor.EvaluatePuzzles(), Is.Zero);
            state.SetProperty(Keepsake, value => value.cell = Cell);
            Runtime.ResetState(); Assert.That(monitor.EvaluatePuzzles(), Is.Zero);
            Runtime.StartQuest("katrina_something_borrowed");
            Assert.That(monitor.EvaluatePuzzles(), Is.EqualTo(1));
            Assert.That(Runtime.GetFlag("inventory.keyitem.katrina-keepsake"), Is.True);
            Assert.That(Runtime.ActiveDialogue.Definition.id, Is.EqualTo("katrina_keepsake_recovered"));
            Runtime.ActiveDialogue.Continue(); Assert.That(monitor.EvaluatePuzzles(), Is.Zero);
            string saved = Runtime.ExportStateJson(false); Runtime.ResetState(); Runtime.ImportStateJson(saved);
            Assert.That(monitor.EvaluatePuzzles(), Is.Zero);
        }
        private GameObject Create(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private GameObject Register(string id, Vector2Int cell, string[] tags, bool render = false)
        {
            GameObject go = Create(id); go.transform.position = grid.CellToWorld(cell);
            if (render) go.AddComponent<MeshRenderer>();
            state.RegisterObject(id, go, cell, Vector2Int.one, true, tags); return go;
        }
    }
}
