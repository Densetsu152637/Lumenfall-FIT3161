using System;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR10")]
    public sealed class Pr10OptionalPowerIsolationTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();
        private WorldStateService state;
        private WorldStateService previousState;
        private GridWorld grid;
        private GridMover player;
        private SpellCaster caster;
        private OptionalDormitoryPowerActivity activity;
        private GameEventRuntime runtime;
        private GameEventRuntime previousRuntime;
        private float previousTimeScale;
        private bool previousOverlay;
        private const string Source = OptionalDormitoryPowerActivity.SourceId;
        private const string Sink = OptionalDormitoryPowerActivity.SinkId;
        private const string Flag = OptionalDormitoryPowerActivity.CompletionFlag;

        [SetUp]
        public void SetUp()
        {
            previousTimeScale = Time.timeScale;
            previousOverlay = InventoryUiState.IsOpen;
            Time.timeScale = 1;
            SetOverlay(false);
            previousRuntime = GameEventManager.Instance.Runtime;
            runtime = new GameEventRuntime(previousRuntime.Catalog);
            typeof(GameEventManager).GetProperty(nameof(GameEventManager.Runtime)).SetValue(GameEventManager.Instance, runtime);
            grid = Create("grid").AddComponent<GridWorld>();
            grid.ConfigureBounds(20, 20);
            previousState = WorldStateService.Instance;
            GameObject stateHost = Create("world");
            stateHost.SetActive(false); // Keep the isolated provider out of the global save registry.
            state = stateHost.AddComponent<WorldStateService>();
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
            SetOverlay(previousOverlay);
            Time.timeScale = previousTimeScale;
            Call(activity, "OnDisable");
            Call(caster, "OnDisable");
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, previousState);
            typeof(GameEventManager).GetProperty(nameof(GameEventManager.Runtime)).SetValue(GameEventManager.Instance, previousRuntime);
        }

        [Test]
        public void ReconfigureEndsPartialAttemptWithoutChangingDurablePower()
        {
            Assert.That(activity.TryBegin(), Is.True);
            Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.True);
            activity.Configure(true, state, caster, player);
            Assert.That(activity.SessionStarted, Is.False);
            Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.False);
            state.TryGetObject(Source, out WorldObjectSnapshot source);
            Assert.That(source.powered, Is.True);
            Assert.That(runtime.GetFlag(Flag), Is.False);
            Assert.That(activity.TryBegin(), Is.True);
            Assert.That(activity.TryCast(SpellId.Weave, Sink), Is.True);
        }

        [Test]
        public void RepeatedStatusPollingNotifiesOnlyOnEffectiveFeedbackChange()
        {
            int changes = 0;
            activity.FeedbackChanged += (_, __) => changes++;
            Assert.That(activity.TryBegin(), Is.True);
            int began = changes;
            Assert.That(began, Is.GreaterThan(0));
            activity.RefreshStatus(); activity.Evaluate(); activity.RefreshStatus();
            Assert.That(changes, Is.EqualTo(began));
            Assert.That(activity.TryCast(SpellId.Ampere, Source), Is.True);
            Assert.That(changes, Is.GreaterThan(began));
        }

        [Test]
        public void InvalidSinkEndpointRejectsAttemptEvenWhenSourceIsValid()
        {
            state.SetProperty(Sink, value => value.spellCompatible = false);
            Assert.That(activity.TryBegin(), Is.False);
            Assert.That(activity.Status, Is.EqualTo(DormitoryPowerStatus.InvalidAuthoring));
            Assert.That(state.IsConnected(Source, Sink), Is.False);
            Assert.That(runtime.GetFlag(Flag), Is.False);
            state.SetProperty(Sink, value => value.spellCompatible = true);
            Assert.That(activity.TryBegin(), Is.True);
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
