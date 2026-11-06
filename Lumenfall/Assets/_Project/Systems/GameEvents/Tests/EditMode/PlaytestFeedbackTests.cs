using System.Collections.Generic;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Startup;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// Fixes from the November playtest: movement locks that could outlive whatever held them, a paused clock that
    /// survived into the next play, and the wayfinding HUD (the door it routes through, the quest-complete banner and the
    /// one-time tips). See docs/implementation/playtest-feedback-20261105.md.
    /// </summary>
    public sealed class PlaytestFeedbackTests
    {
        private readonly List<Object> created = new List<Object>();
        private GameEventRuntime runtime;

        [SetUp]
        public void SetUp()
        {
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
        }

        [TearDown]
        public void TearDown()
        {
            runtime.ResetState();
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }

        [Test]
        public void LoadingASaveClearsEveryMovementLockAndADestroyedOwnerHoldsNone()
        {
            GridMover mover = NewInactive<GridMover>("lock-test-player");
            object sign = new object();
            mover.SetMovementLocked(sign, true);
            mover.SetMovementLocked(true);
            Assert.That(mover.IsMovementLocked, Is.True);
            mover.ClearMovementLocks();
            Assert.That(mover.IsMovementLocked, Is.False, "A save or new game owes nothing to whatever held the player before.");

            // An overlay destroyed while it held the player can never release its lock, so it no longer counts.
            GameObject overlay = new GameObject("lock-test-overlay");
            created.Add(overlay);
            Transform owner = overlay.transform;
            mover.SetMovementLocked(owner, true);
            Assert.That(mover.IsMovementLocked, Is.True);
            Object.DestroyImmediate(overlay);
            Assert.That(mover.IsMovementLocked, Is.False);
        }

        [Test]
        public void TheNorthGateJokeReleasesItsOwnLockEvenWhenAnotherOwnerWasHoldingThePlayer()
        {
            // The gate used to skip unlocking when the player was already held, leaving them stuck once that hold ended.
            GridMover mover = NewInactive<GridMover>("dog-test-player");
            AnnoyingDogGate gate = NewInactive<AnnoyingDogGate>("dog-test-gate");
            object dialogue = new object();
            mover.SetMovementLocked(dialogue, true);
            Assert.That(gate.HandleBlocked(null, mover), Is.True, "The first try plays the loading-bar joke.");
            mover.SetMovementLocked(dialogue, false);
            Assert.That(mover.IsMovementLocked, Is.True, "The joke holds the player while it plays.");
            typeof(AnnoyingDogGate).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gate, new object[] { false });
            Assert.That(mover.IsMovementLocked, Is.False, "Finishing the joke frees the player.");
        }

        [Test]
        public void TheMenuNeverHandsBackAStoppedClock()
        {
            Assert.That(StartupMenu.TimeScaleToRestore(1f), Is.EqualTo(1f));
            Assert.That(StartupMenu.TimeScaleToRestore(0.5f), Is.EqualTo(0.5f), "A deliberate slow-motion scale is kept.");
            Assert.That(StartupMenu.TimeScaleToRestore(0f), Is.EqualTo(1f),
                "A menu that opens on a clock left stopped by an interrupted pause starts it again when it closes.");
        }

        [Test]
        public void TheProjectStartsEveryPlayWithTheClockRunning()
        {
            // The Editor once saved a paused time scale into the project settings, so every play began frozen.
            Object[] settings = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset");
            Assert.That(settings, Is.Not.Empty);
            UnityEditor.SerializedProperty timeScale = new UnityEditor.SerializedObject(settings[0]).FindProperty("m_TimeScale");
            Assert.That(timeScale, Is.Not.Null);
            Assert.That(timeScale.floatValue, Is.EqualTo(1f));
        }

        [Test]
        public void TheArrowRoutesThroughTheNearestOpenDoorTowardAnotherArea()
        {
            GridWorld world = NewInactive<GridWorld>("route-test-grid");
            WorldAreaAsset campus = Area("campus"), dormitory = Area("B01.Ground"), library = Area("B04.Ground"), hall = Area("B03.Ground");
            PortalContentAsset toDormitory = Link(campus, dormitory), toLibrary = Link(campus, library), libraryToHall = Link(library, hall);
            PortalContentAsset lockedToHall = Link(campus, hall);
            lockedToHall.blocked = true;

            QuestGuidanceHud hud = NewInactive<QuestGuidanceHud>("route-test-hud");
            List<GridPortal> doors = (List<GridPortal>)Field(hud, "doors");
            foreach (PortalContentAsset content in new[] { toDormitory, toLibrary, lockedToHall })
            {
                GridPortal door = NewInactive<GridPortal>("door-" + content.destination.area.definition.id);
                door.Configure(content, world);
                doors.Add(door);
            }

            object[] args = { campus, "B03.Ground", world, null };
            GridPortal chosen = (GridPortal)typeof(QuestGuidanceHud).GetMethod("FindDoorToward", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, args);
            Assert.That(chosen, Is.Not.Null);
            Assert.That(chosen.Content, Is.SameAs(toLibrary), "The locked door is skipped; the way to the hall runs through the Library.");
            Assert.That(args[3], Is.EqualTo("B04.Ground"), "The caption names the next area: \"via the door to Library\".");

            args = new object[] { campus, "B01.Ground", world, null };
            chosen = (GridPortal)typeof(QuestGuidanceHud).GetMethod("FindDoorToward", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, args);
            Assert.That(chosen.Content, Is.SameAs(toDormitory));
        }

        [Test]
        public void CompletingAQuestAnnouncesTheSpellAndTheRoomKeyProgress()
        {
            QuestGuidanceHud hud = NewInactive<QuestGuidanceHud>("banner-test-hud");
            Field(hud, "subscribedRuntime", runtime);
            Assert.That(runtime.Catalog.TryGetQuest("stella_first_light", out QuestDefinition quest), Is.True);
            runtime.StartQuest("stella_first_light");
            runtime.CompleteQuest("stella_first_light");
            Invoke(hud, "OnQuestStatusChanged", quest, QuestStatus.Active, QuestStatus.Completed);

            var banners = (Queue<(string title, string body)>)Field(hud, "pendingBanners");
            Assert.That(banners.Count, Is.EqualTo(1));
            (string title, string body) = banners.Peek();
            Assert.That(title, Is.EqualTo("First Light"));
            Assert.That(body, Does.Contain("Spell learned: Ampere"));
            Assert.That(body, Does.Contain("Room key: 1 of 6 favours done"), "Testers did not know the favours stood between them and their room.");

            Invoke(hud, "OnQuestStatusChanged", quest, QuestStatus.Completed, QuestStatus.Completed);
            Assert.That(banners.Count, Is.EqualTo(1), "A quest is announced once.");
        }

        [Test]
        public void EachTipIsShownOncePerPlaythrough()
        {
            QuestGuidanceHud hud = NewInactive<QuestGuidanceHud>("tip-test-hud");
            Field(hud, "subscribedRuntime", runtime);
            var tips = (Queue<string>)Field(hud, "pendingTips");

            Invoke(hud, "OnFlagChanged", "spell.unlocked.Ampere", true);
            Assert.That(tips.Count, Is.EqualTo(1));
            Assert.That(tips.Peek(), Does.Contain("to choose a spell").And.Contain("to cast it"), "The first spell explains how to select and cast.");
            Invoke(hud, "OnFlagChanged", "spell.unlocked.Weave", true);
            Assert.That(tips.Count, Is.EqualTo(1), "Later spells do not repeat it.");
            Assert.That(runtime.GetFlag("tip.seen.first-spell"), Is.True, "The tip is remembered in the save, so a load does not bring it back.");

            Invoke(hud, "OnFlagChanged", "spell.unlocked.Vis", false);
            Invoke(hud, "OnFlagChanged", "story.something.else", true);
            Assert.That(tips.Count, Is.EqualTo(1), "Only learning a spell counts.");
        }

        private T NewInactive<T>(string name) where T : Component
        {
            GameObject host = new GameObject(name);
            host.SetActive(false);
            created.Add(host);
            return host.AddComponent<T>();
        }

        private WorldAreaAsset Area(string id)
        {
            WorldAreaAsset area = ScriptableObject.CreateInstance<WorldAreaAsset>();
            area.definition = new MapDefinition { id = id, width = 10, height = 10 };
            created.Add(area);
            return area;
        }

        // A one-way door from one area to another, added to the first area's portals.
        private PortalContentAsset Link(WorldAreaAsset from, WorldAreaAsset to)
        {
            PortalContentAsset arrival = ScriptableObject.CreateInstance<PortalContentAsset>();
            arrival.area = to;
            PortalContentAsset door = ScriptableObject.CreateInstance<PortalContentAsset>();
            door.area = from;
            door.destination = arrival;
            created.Add(arrival);
            created.Add(door);
            var portals = new List<PortalContentAsset>(from.portals) { door };
            from.portals = portals.ToArray();
            return door;
        }

        private static object Field(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

        private static void Field(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void Invoke(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    }
}
