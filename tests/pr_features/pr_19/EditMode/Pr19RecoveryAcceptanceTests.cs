using Lumenfall.GameEvents;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR19")]
    public sealed class Pr19RecoveryAcceptanceTests
    {
        private GameObject owner; private WorldStateService world; private GameEventRuntime runtime; private OptionalRecoveryRules rules;
        [SetUp] public void SetUp()
        {
            // Inactive host deliberately avoids Awake and save-provider/singleton registration.
            owner = new GameObject("PR19 isolated state"); owner.SetActive(false); world = owner.AddComponent<WorldStateService>();
            runtime = new GameEventRuntime(new GameEventCatalog(new GameEventDatabaseDefinition[0]));
            runtime.SetFlag("spell.unlocked.LostEchoes", true);
            world.BeginMapRebuild("campus");
            world.RegisterObject(OptionalRecoveryRules.TargetOne, null, Vector2Int.zero, Vector2Int.one, true, new[] { "hidden", "lost-echoes", "optional-recovery" });
            world.RegisterObject(OptionalRecoveryRules.TargetTwo, null, Vector2Int.right, Vector2Int.one, true, new[] { "hidden", "lost-echoes", "optional-recovery" });
            world.EndMapRebuild(); rules = new OptionalRecoveryRules(runtime, world, "campus", Vector2Int.zero, Vector2Int.right);
        }
        [TearDown] public void TearDown() { Object.DestroyImmediate(owner); }
        [Test] public void SuspensionRetainsPartialProgressAndResumeRequiresObservingBothTargets()
        {
            Assert.That(rules.TryBegin(true), Is.True);
            world.SetProperty(OptionalRecoveryRules.TargetOne, s => s.revealed = true); rules.Observe(true); rules.Suspend();
            world.SetProperty(OptionalRecoveryRules.TargetTwo, s => s.revealed = true);
            Assert.That(rules.Observe(true), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.ProgressFlag(OptionalRecoveryRules.TargetOne)), Is.True);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.InventoryFlag), Is.False);
            Assert.That(rules.TryBegin(true), Is.True); Assert.That(rules.Observe(true), Is.True);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.SearchActiveFlag), Is.False);
        }
        [Test] public void RestoreUsesSavedProgressAsAuthorityOverStaleWorldReveals()
        {
            runtime.SetFlag(OptionalRecoveryRules.ProgressFlag(OptionalRecoveryRules.TargetTwo), true);
            world.SetProperty(OptionalRecoveryRules.TargetOne, s => { s.revealed = true; s.powered = true; });
            rules.RestoreSearch();
            world.TryGetObject(OptionalRecoveryRules.TargetOne, out var first); world.TryGetObject(OptionalRecoveryRules.TargetTwo, out var second);
            Assert.That(first.revealed, Is.False); Assert.That(first.powered, Is.True); Assert.That(second.revealed, Is.True);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.RewardFlag), Is.False);
        }
        [Test] public void MissingRequiredTagPreventsStartAndCompletionEvenWhenBothAreRevealed()
        {
            world.SetProperty(OptionalRecoveryRules.TargetOne, s => { s.tags = new[] { "hidden", "lost-echoes" }; s.revealed = true; });
            world.SetProperty(OptionalRecoveryRules.TargetTwo, s => s.revealed = true);
            Assert.That(rules.TryBegin(true), Is.False);
            runtime.SetFlag(OptionalRecoveryRules.StartedFlag, true); runtime.SetFlag(OptionalRecoveryRules.SearchActiveFlag, true);
            Assert.That(rules.Observe(true), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.InventoryFlag), Is.False);
            Assert.That(rules.Status(true), Does.Contain("unavailable"));
        }
    }
}
