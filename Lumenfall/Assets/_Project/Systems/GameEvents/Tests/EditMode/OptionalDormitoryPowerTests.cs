using System;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class OptionalDormitoryPowerTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private WorldStateService state;
        private WorldStateService previousState;
        private GridWorld grid;
        private GridMover player;
        private SpellCaster caster;
        private OptionalDormitoryPowerActivity activity;
        private GameEventRuntime runtime;
        private const string Source = OptionalDormitoryPowerActivity.SourceId;
        private const string Sink = OptionalDormitoryPowerActivity.SinkId;
        private const string Flag = OptionalDormitoryPowerActivity.CompletionFlag;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1;
            SetOverlay(false);
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            grid = Create("grid").AddComponent<GridWorld>();
            grid.ConfigureBounds(20, 20);
            previousState = WorldStateService.Instance;
            state = Create("world").AddComponent<WorldStateService>();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, state);
            BuildMap();
            GameObject actor = Create("actor");
            actor.transform.position = grid.CellToWorld(new Vector2Int(3, 2));
            player = actor.AddComponent<GridMover>();
            Assert.That(player.TryTeleportTo(grid, new Vector2Int(3, 2)), Is.True);
            caster = actor.AddComponent<SpellCaster>();
            Call(caster, "OnEnable");
            activity = Create("optional-power").AddComponent<OptionalDormitoryPowerActivity>();
            Unlock();
            activity.Configure(true, state, caster, player);
        }

        [TearDown]
        public void TearDown()
        {
            SetOverlay(false);
            Time.timeScale = 1;
            Call(activity, "OnDisable");
            Call(caster, "OnDisable");
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, previousState);
            GameEventManager.Instance.Runtime.ResetState();
        }

        [Test]
        public void DefaultDisabledAndMissingSpellOrMapNeverMutateState()
        {
            OptionalDormitoryPowerActivity dormant = Create("dormant").AddComponent<OptionalDormitoryPowerActivity>();
            Assert.That(dormant.TryBegin(), Is.False);
            Assert.That(dormant.Status, Is.EqualTo(DormitoryPowerStatus.Disabled));
            Assert.That(dormant.TryReplay(), Is.False);
            runtime.SetFlag("spell.unlocked.Weave", false);
            Assert.That(activity.TryBegin(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.MissingSpells));
            Unlock();
            state.BeginMapRebuild("campus");
            state.EndMapRebuild();
            Assert.That(activity.TryBegin(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.WrongMap));
            Assert.That(runtime.GetFlag(Flag), Is.False);
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.NotStarted));
        }

        [Test]
        public void ActualAmpereThenWeaveCastsCompleteOnceAndDoNotGrantQuestOrInventoryReward()
        {
            int awards = 0;
            Action<string, bool> count = (key, value) => { if (key == Flag && value) awards++; };
            runtime.FlagChanged += count;
            try
            {
                Assert.That(activity.TryBegin(), Is.True);
                Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.True);
                Assert.That(runtime.GetFlag(Flag), Is.False);
                Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.True);
                Assert.That(state.IsConnected(Source, Sink), Is.True);
                Assert.That(runtime.GetFlag(Flag), Is.True);
                Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.Restored));
                Assert.That(activity.TryBegin(), Is.False);
                activity.Evaluate();
                Assert.That(awards, Is.EqualTo(1));
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.NotStarted));
                Assert.That(runtime.GetFlag("inventory.keyitem.dormitory-key"), Is.False);
                Assert.That(runtime.GetFlag("inventory.keyitem.katrina-keepsake"), Is.False);
            }
            finally { runtime.FlagChanged -= count; }
        }

        [Test]
        public void ExactCableAndPoweredSourceRequiredAndSourceOffFails()
        {
            Assert.That(activity.TryBegin(), Is.True);
            Register("unrelated", new Vector2Int(2, 3), null);
            state.AddConnection(Source, "unrelated");
            state.SetProperty(Source, value => value.powered = true);
            Assert.That(activity.Evaluate(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.CableMissing));
            state.AddConnection(Source, Sink);
            state.SetProperty(Source, value => value.powered = false);
            Assert.That(activity.Evaluate(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.SourceOff));
            Assert.That(runtime.GetFlag(Flag), Is.False);
        }

        [Test]
        public void LegacySavedStrengthCannotBypassLiveDistanceAndBoundaryIsAccepted()
        {
            activity.TryBegin();
            state.SetProperty(Source, value => value.powered = true);
            state.AddConnection(Source, Sink);
            float savedStrength = state.GetConnectionStrength(Source, Sink);
            state.SetProperty(Sink, value => value.cell = new Vector2Int(9, 2));
            Assert.That(state.GetConnectionStrength(Source, Sink), Is.EqualTo(savedStrength));
            Assert.That(activity.Evaluate(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.InsufficientPower));
            Assert.That(runtime.GetFlag(Flag), Is.False);
            state.SetProperty(Sink, value => value.cell = new Vector2Int(6, 2));
            Assert.That(activity.Evaluate(), Is.True, "Four-cell cable has exact live strength 0.2.");
        }

        [Test]
        public void SolvedWorldWithoutExplicitBeginDoesNotAward()
        {
            state.SetProperty(Source, value => value.powered = true);
            state.AddConnection(Source, Sink);
            Assert.That(activity.Evaluate(), Is.False);
            Assert.That(runtime.GetFlag(Flag), Is.False);
            Assert.That(activity.TryBegin(), Is.True);
            Assert.That(runtime.GetFlag(Flag), Is.True);
        }

        [Test]
        public void CastRejectsWrongSpellTargetDistanceOverlayAndWeaveBeforePower()
        {
            activity.TryBegin();
            Assert.That(activity.TryCast(SpellId.Ampere, Sink), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.InvalidCast));
            Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.CastFailed));
            SetOverlay(true);
            Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.InputBlocked));
            SetOverlay(false);
            Assert.That(player.TryTeleportTo(grid, new Vector2Int(10, 10)), Is.True);
            Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.OutOfRange));
            state.TryGetObject(Source, out WorldObjectSnapshot source);
            Assert.That(source.powered, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrAmbiguousDedicatedTagsRejectAuthoring(bool ambiguous)
        {
            state.SetProperty(Source, value => value.tags = ambiguous
                ? new[] { OptionalDormitoryPowerActivity.AuthoringTag, "weave-with:" + Sink, "weave-with:unrelated" }
                : new[] { "weave-with:" + Sink });
            Assert.That(activity.TryBegin(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.InvalidAuthoring));
            Assert.That(runtime.GetFlag(Flag), Is.False);
        }

        [Test]
        public void ResetClearsOnlyOwnCablePowerAndEffectsWhilePreservingOtherFlagsAndCables()
        {
            Register("other.a", new Vector2Int(6, 6), null);
            Register("other.b", new Vector2Int(7, 6), null);
            state.AddConnection("other.a", "other.b");
            state.SetProperty("other.a", value => value.powered = true);
            runtime.SetFlag("main.route.key-owned", true);
            activity.TryBegin();
            activity.TryCast(SpellId.Ampere, Source);
            state.AddConnection(Source, Sink);
            Assert.That(activity.TryReset(), Is.True);
            Assert.That(state.IsConnected(Source, Sink), Is.False);
            Assert.That(state.IsConnected("other.a", "other.b"), Is.True);
            Assert.That(runtime.GetFlag("main.route.key-owned"), Is.True);
            state.TryGetObject(Source, out WorldObjectSnapshot source);
            state.TryGetObject("other.a", out WorldObjectSnapshot other);
            Assert.That(source.powered, Is.False);
            Assert.That(other.powered, Is.True);
            Assert.That(state.AcknowledgeEffect("lightning:" + Source + ":on"), Is.False);
            Assert.That(runtime.GetFlag(Flag), Is.False);
            Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.True);
            Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.True);
        }

        [Test]
        public void PartialSaveRestoreRequiresReentryAndContinuesThroughExistingWorldProvider()
        {
            activity.TryBegin();
            activity.TryCast(SpellId.Ampere, Source);
            string world = state.CaptureStateJson();
            string events = runtime.ExportStateJson(false);
            activity.TryReset();
            Assert.That(state.RestoreStateJson(world), Is.True);
            Assert.That(runtime.ImportStateJson(events), Is.True);
            Assert.That(activity.SessionStarted, Is.False);
            Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.False);
            Assert.That(activity.TryBegin(), Is.True);
            Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.True);
            Assert.That(runtime.GetFlag(Flag), Is.True);
        }

        [Test]
        public void CompletedSaveRebuildAndReplayPreserveOneTimeFlag()
        {
            Assert.That(activity.TryReplay(), Is.False);
            activity.TryBegin();
            activity.TryCast(SpellId.Ampere, Source);
            activity.TryCast(SpellId.Weave, Sink);
            string world = state.CaptureStateJson();
            string events = runtime.ExportStateJson(false);
            state.ResetAllState();
            BuildMap();
            Assert.That(state.RestoreStateJson(world), Is.True);
            Assert.That(runtime.ImportStateJson(events), Is.True);
            Assert.That(activity.TryBegin(), Is.False);
            Assert.That(activity.TryReset(), Is.False);
            Assert.That(activity.TryReplay(), Is.True);
            Assert.That(runtime.GetFlag(Flag), Is.True);
            Assert.That(state.IsConnected(Source, Sink), Is.False);
            activity.TryCast(SpellId.Ampere, Source);
            activity.TryCast(SpellId.Weave, Sink);
            Assert.That(runtime.GetFlag(Flag), Is.True);
            Assert.That(activity.SessionStarted, Is.False);
        }

        [Test]
        public void RuntimeReplacementAndDisableEndSessionWithoutStaleRestoreSubscriptions()
        {
            activity.TryBegin();
            GameEventRuntime old = runtime;
            GameEventManager.Instance.ReloadDefinitions();
            runtime = GameEventManager.Instance.Runtime;
            Unlock();
            activity.Evaluate();
            Assert.That(activity.SessionStarted, Is.False);
            activity.TryBegin();
            old.ResetState();
            Assert.That(activity.SessionStarted, Is.True, "Old runtime must be unsubscribed.");
            activity.enabled = false;
            Call(activity, "OnDisable");
            runtime.ResetState();
            Assert.That(activity.SessionStarted, Is.False);
            activity.enabled = true;
            Call(activity, "OnEnable");
            Unlock();
            Assert.That(activity.TryBegin(), Is.True);
        }

        [Test]
        public void MismatchedActorConfigurationCannotCastUsingProxyDistance()
        {
            SpellCaster otherCaster = Create("wrong-actor").AddComponent<SpellCaster>();
            activity.Configure(true, state, otherCaster, player);
            Assert.That(activity.TryBegin(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.Unavailable));
            Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.False);
            state.TryGetObject(Source, out WorldObjectSnapshot source);
            Assert.That(source.powered, Is.False);
        }

        private static void Call(object component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
        }

        private static void SetOverlay(bool value)
        {
            // The shared overlay gate has an internal setter; simulate its owner without
            // widening the production API solely for tests.
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, value);
        }
        private GameObject Create(string name) { GameObject result = new GameObject(name); objects.Add(result); return result; }
        private void Unlock()
        {
            runtime.SetFlag("spell.unlocked.Ampere", true);
            runtime.SetFlag("spell.unlocked.Weave", true);
        }
        private void BuildMap()
        {
            state.BeginMapRebuild(OptionalDormitoryPowerActivity.MapId);
            Register(Source, new Vector2Int(2, 2), new[] { OptionalDormitoryPowerActivity.AuthoringTag, "weave-with:" + Sink });
            Register(Sink, new Vector2Int(4, 2), new[] { OptionalDormitoryPowerActivity.AuthoringTag, "weave-with:" + Source });
            state.EndMapRebuild();
        }
        private void Register(string id, Vector2Int cell, string[] tags)
        {
            GameObject go = Create(id);
            go.transform.position = grid.CellToWorld(cell);
            state.RegisterObject(id, go, cell, Vector2Int.one, true, tags);
        }
    }
}
