using System.Collections.Generic;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// Lost Echoes reveal hooks and world pickups: "on-reveal:<eventId>" and "on-pickup:<eventId>" tags trigger GameEvents.
    public sealed class LostEchoesRevealEventTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private WorldStateService state;
        private GridWorld grid;
        private GameEventRuntime Runtime => GameEventManager.Instance.Runtime;
        private const string Quest = "katrina_something_borrowed";
        private const string FoundEvent = "found_katrina_keepsake";
        private const string FoundFlag = "inventory.keyitem.katrina-keepsake";
        private static readonly Vector2Int Cell = new Vector2Int(5, 5);
        private static readonly string[] HookTags = { "hidden", "on-reveal:" + FoundEvent };
        private static readonly string[] PickupTags = { "hidden", "pickup", "on-pickup:" + FoundEvent };

        [SetUp]
        public void SetUp()
        {
            Runtime.ResetState();
            grid = Create("grid").AddComponent<GridWorld>(); grid.ConfigureBounds(20, 20);
            state = Create("state").AddComponent<WorldStateService>(); state.BeginMapRebuild("test-map");
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
        public void RevealTriggersTaggedEventAndRaisesObjectRevealedOnce()
        {
            Runtime.StartQuest(Quest);
            Register("echo", Cell, HookTags);
            state.EndMapRebuild();
            int raised = 0;
            state.ObjectRevealed += revealed => { if (revealed.id == "echo") raised++; };
            Assert.That(state.RevealHiddenAround(Cell, 2), Is.EqualTo(1));
            Assert.That(Runtime.HasEventTriggered(FoundEvent), Is.True);
            Assert.That(Runtime.GetFlag(FoundFlag), Is.True);
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(state.RevealHiddenAround(Cell, 2), Is.Zero);
            Assert.That(raised, Is.EqualTo(1), "An already revealed object does not fire again.");
        }

        [Test]
        public void TaggedEventStillHonoursItsOwnConditions()
        {
            Register("echo", Cell, HookTags);
            state.EndMapRebuild();
            Assert.That(state.TryRevealObject("echo", Cell, 2), Is.True, "The reveal itself succeeds.");
            Assert.That(Runtime.HasEventTriggered(FoundEvent), Is.False, "The event requires its quest to be Active.");
            Assert.That(Runtime.GetFlag(FoundFlag), Is.False);
        }

        [Test]
        public void RestoringARevealedObjectDoesNotFireItsEventAgain()
        {
            Register("echo", Cell, HookTags);
            state.EndMapRebuild();
            state.TryRevealObject("echo", Cell, 2);
            string saved = state.CaptureStateJson();
            Runtime.StartQuest(Quest);
            int raised = 0;
            state.ObjectRevealed += _ => raised++;
            state.ResetAllState(); state.BeginMapRebuild("test-map");
            Register("echo", Cell, HookTags);
            Assert.That(state.RestoreStateJson(saved), Is.True); state.EndMapRebuild();
            Assert.That(state.TryGetObject("echo", out WorldObjectSnapshot restored) && restored.revealed, Is.True);
            Assert.That(raised, Is.Zero);
            Assert.That(Runtime.HasEventTriggered(FoundEvent), Is.False);
        }

        [Test]
        public void UntaggedHiddenObjectsRevealWithoutTriggeringEvents()
        {
            Runtime.StartQuest(Quest);
            Register("plain", Cell, new[] { "hidden" });
            state.EndMapRebuild();
            Assert.That(state.RevealHiddenAround(Cell, 2), Is.EqualTo(1));
            Assert.That(Runtime.HasEventTriggered(FoundEvent), Is.False);
        }

        [Test]
        public void HiddenPickupCanBeCollectedBeforeOrAfterRevealAndFiresHooksOnce()
        {
            Runtime.StartQuest(Quest);
            GameObject item = Register("item", Cell, PickupTags, render: true);
            GameObject located = Register("located", Cell + new Vector2Int(0, 3), new[] { "hidden", "pickup" }, render: true);
            state.EndMapRebuild();
            int collected = 0, revealed = 0;
            state.ObjectCollected += _ => collected++;
            state.ObjectRevealed += _ => revealed++;
            Assert.That(state.TryCollectObject("item", Cell + new Vector2Int(1, 1), 1), Is.False, "Pickup needs the player within one cell.");
            Assert.That(state.TryCollectObject("item", Cell + Vector2Int.right, 1), Is.True, "A hidden pickup can be picked up without Lost Echoes.");
            Assert.That(state.TryGetObject("item", out WorldObjectSnapshot item1) && item1.revealed && item1.collected, Is.True, "Picking it up also finds it.");
            Assert.That(revealed, Is.EqualTo(1));
            Assert.That(Runtime.GetFlag(FoundFlag), Is.True);
            Assert.That(item.GetComponent<Renderer>().enabled, Is.False, "A collected object leaves the world.");
            Assert.That(state.TryCollectObject("item", Cell, 1), Is.False);
            Assert.That(state.RevealHiddenAround(Cell + new Vector2Int(0, 3), 2), Is.EqualTo(1), "Lost Echoes helps locate the other one.");
            Assert.That(located.GetComponent<Renderer>().enabled, Is.True);
            Assert.That(state.TryCollectObject("located", Cell + new Vector2Int(0, 3), 1), Is.True);
            Assert.That(revealed, Is.EqualTo(2), "An already revealed pickup is not revealed again.");
            Assert.That(collected, Is.EqualTo(2));
        }

        [Test]
        public void CollectedStateSurvivesRestoreWithoutFiringAgain()
        {
            GameObject item = Register("item", Cell, PickupTags, render: true);
            state.EndMapRebuild();
            state.RevealHiddenAround(Cell, 2);
            state.TryCollectObject("item", Cell, 1);
            string saved = state.CaptureStateJson();
            Runtime.StartQuest(Quest);
            int collected = 0;
            state.ObjectCollected += _ => collected++;
            state.ResetAllState(); state.BeginMapRebuild("test-map");
            state.RegisterObject("item", item, Cell, Vector2Int.one, true, PickupTags);
            Assert.That(state.RestoreStateJson(saved), Is.True); state.EndMapRebuild();
            Assert.That(state.TryGetObject("item", out WorldObjectSnapshot restored) && restored.collected, Is.True);
            Assert.That(item.GetComponent<Renderer>().enabled, Is.False);
            Assert.That(collected, Is.Zero);
            Assert.That(Runtime.HasEventTriggered(FoundEvent), Is.False);
        }

        [Test]
        public void ObjectsWithoutThePickupTagCannotBeCollected()
        {
            Register("plain", Cell, new[] { "hidden" });
            state.EndMapRebuild();
            state.RevealHiddenAround(Cell, 2);
            Assert.That(state.TryCollectObject("plain", Cell, 1), Is.False);
        }

        private GameObject Create(string name) { var go = new GameObject(name); objects.Add(go); return go; }
        private GameObject Register(string id, Vector2Int cell, string[] tags, bool render = false)
        {
            GameObject go = Create(id); go.transform.position = grid.CellToWorld(cell);
            if (render) go.AddComponent<MeshRenderer>();
            state.RegisterObject(id, go, cell, Vector2Int.one, true, tags);
            return go;
        }
    }
}
