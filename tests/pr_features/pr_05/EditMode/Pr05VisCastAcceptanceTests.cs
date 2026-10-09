using System.Reflection;
using Lumenfall.GameEvents;
using Lumenfall.Inventory;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR05")]
    public sealed class Pr05VisCastAcceptanceTests
    {
        private GameObject host;
        private WorldStateService state;
        private SpellCaster caster;
        private object previousManager, previousWorld;
        private bool previousInventory;
        private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private static readonly FieldInfo ManagerInstance = typeof(GameEventManager).GetField("instance", StaticPrivate);
        private static readonly PropertyInfo WorldInstance = typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance));
        private static readonly PropertyInfo InventoryOpen = typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen));

        [SetUp] public void SetUp()
        {
            previousManager = ManagerInstance.GetValue(null);
            previousWorld = WorldInstance.GetValue(null);
            previousInventory = InventoryUiState.IsOpen;
            host = new GameObject("PR05 isolated cast environment"); host.SetActive(false);
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition data, out string error), Is.True, error);
            var runtime = new GameEventRuntime(new GameEventCatalog(new[] { data }));
            runtime.SetFlag("spell.unlocked.Vis", true);
            GameEventManager manager = host.AddComponent<GameEventManager>();
            typeof(GameEventManager).GetProperty(nameof(GameEventManager.Runtime)).SetValue(manager, runtime);
            ManagerInstance.SetValue(null, manager);
            state = host.AddComponent<WorldStateService>();
            WorldInstance.SetValue(null, state);
            InventoryOpen.SetValue(null, false);
            caster = host.AddComponent<SpellCaster>();
            state.BeginMapRebuild("vis-acceptance");
        }

        [TearDown] public void TearDown()
        {
            // Restore original objects rather than resetting unrelated runtime/save state.
            ManagerInstance.SetValue(null, previousManager);
            WorldInstance.SetValue(null, previousWorld);
            InventoryOpen.SetValue(null, previousInventory);
            if (host != null) Object.DestroyImmediate(host);
        }

        [TestCase("vis-target:-90", 270f)]
        [TestCase("vis-target:450", 90f)]
        [TestCase("VIS-TARGET:720", 0f)]
        public void AuthoredQuarterTurnNormalizesBeforeIdempotentCast(string tag, float expected)
        {
            state.RegisterObject("marker", null, Vector2Int.zero, Vector2Int.one, true, new[] { "vis", tag }, 180f);
            state.EndMapRebuild();
            Assert.That(caster.Cast(SpellId.Vis, "marker", Vector2Int.up), Is.True);
            Assert.That(state.TryGetObject("marker", out WorldObjectSnapshot aligned), Is.True);
            Assert.That(aligned.rotation, Is.EqualTo(expected));
            Assert.That(aligned.aligned, Is.True);
            Assert.That(caster.Cast(SpellId.Vis, "marker", Vector2Int.down), Is.False);
        }

        [Test] public void ConflictingAuthoredTargetsCannotMutateOrAlignMarker()
        {
            state.RegisterObject("marker", null, Vector2Int.zero, Vector2Int.one, true, new[] { "vis", "vis-target:90", "vis-target:180" }, 270f);
            state.EndMapRebuild();
            int changes = 0; state.StateChanged += _ => changes++;
            Assert.That(caster.Cast(SpellId.Vis, "marker", Vector2Int.up), Is.False);
            Assert.That(state.TryGetObject("marker", out WorldObjectSnapshot unchanged), Is.True);
            Assert.That(unchanged.rotation, Is.EqualTo(270f));
            Assert.That(unchanged.aligned, Is.False);
            Assert.That(changes, Is.Zero);
        }
    }
}
