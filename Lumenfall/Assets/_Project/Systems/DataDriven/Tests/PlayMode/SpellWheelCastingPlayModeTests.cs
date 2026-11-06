using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using Lumenfall.Startup;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.World
{
    public sealed class SpellWheelCastingPlayModeTests
    {
        [UnityTest]
        public IEnumerator PublicCastsCompleteBeforeWheelClosesAndPreserveSiblingOwnership()
        {
            StartupMenu menu = Object.FindAnyObjectByType<StartupMenu>();
            bool menuEnabled = menu != null && menu.enabled;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (menuEnabled && !StartupMenu.IsVisible && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(!menuEnabled || StartupMenu.IsVisible, Is.True);
            float previousTimeScale = Time.timeScale;
            GameObject host = null;
            object sibling = new object();
            WorldStateService state = WorldStateService.Instance;
            GridMover mover = null;
            try
            {
                if (menuEnabled) menu.enabled = false;
                QuestHintOverlay.CloseSettings();
                Time.timeScale = 1f;
                GameEventManager.Instance.ReloadDefinitions();
                GameEventRuntime runtime = GameEventManager.Instance.Runtime;
                runtime.SetFlag("spell.unlocked.Ampere", true);
                runtime.SetFlag("spell.unlocked.LostEchoes", true);
                state.ResetAllState();
                state.BeginMapRebuild("spell-wheel-regression");
                host = new GameObject("Spell wheel casting test");
                host.SetActive(false);
                host.AddComponent<GridWorld>();
                var player = new GameObject("Wheel player");
                player.transform.SetParent(host.transform, false);
                mover = player.AddComponent<GridMover>();
                SpellCaster caster = player.AddComponent<SpellCaster>();
                var hidden = new GameObject("Wheel hidden target");
                hidden.transform.SetParent(host.transform, false);
                var relic = new GameObject("Wheel power target");
                relic.transform.SetParent(host.transform, false);
                state.RegisterObject("wheel-hidden", hidden, Vector2Int.one, Vector2Int.one, true, new[] { "hidden" });
                state.RegisterObject("wheel-power", relic, Vector2Int.right, Vector2Int.one, true, new[] { "ampere" });
                state.EndMapRebuild();
                host.SetActive(true);
                mover.SetMovementLocked(sibling, true);
                yield return null;

                OpenWheel(caster);
                yield return null;
                Assert.That(caster.CastLostEchoes(), Is.True);
                Assert.That(state.TryGetObject("wheel-hidden", out WorldObjectSnapshot revealed) && revealed.revealed, Is.True);
                AssertClosed(caster, mover);
                yield return null;

                OpenWheel(caster);
                yield return null;
                Assert.That(caster.SelectedSpell, Is.EqualTo(SpellId.Ampere));
                Assert.That(caster.CastSelectedAt("wheel-power"), Is.True);
                Assert.That(state.TryGetObject("wheel-power", out WorldObjectSnapshot powered) && powered.powered, Is.True);
                AssertClosed(caster, mover);
                Assert.That(caster.CastSelectedAt("wheel-power"), Is.False, "The closing frame must block another consumer/cast.");
                Assert.That(state.TryGetObject("wheel-power", out powered) && powered.powered, Is.True);
                yield return null;

                OpenWheel(caster);
                yield return null;
                OverlayInputGate.Acquire(sibling);
                Assert.That(caster.CastSelectedAt("wheel-power"), Is.False, "A sibling overlay still blocks the wheel's cast.");
                AssertClosed(caster, mover);
                Assert.That(OverlayInputGate.Owns(sibling), Is.True);
                Assert.That(state.TryGetObject("wheel-power", out powered) && powered.powered, Is.True);

                OverlayInputGate.Release(sibling);
                yield return null;
                var despawnTarget = new GameObject("Wheel despawn reveal target");
                despawnTarget.transform.SetParent(host.transform, false);
                state.RegisterObject("wheel-despawn-hidden", despawnTarget, mover.CurrentCell + Vector2Int.up,
                    Vector2Int.one, true, new[] { "hidden" });
                Assert.That(state.TryGetObject("wheel-despawn-hidden", out WorldObjectSnapshot beforeDespawn), Is.True);
                Assert.That(beforeDespawn.revealed, Is.False);
                OpenWheel(caster);
                yield return null;
                Assert.That(caster.CastLostEchoes(), Is.True);
                Assert.That(state.TryGetObject("wheel-despawn-hidden", out WorldObjectSnapshot despawnRevealed) && despawnRevealed.revealed, Is.True);
                LostEchoesPresentation presentation = Object.FindAnyObjectByType<LostEchoesPresentation>();
                Assert.That(presentation, Is.Not.Null);
                var boundRoots = new List<GameObject>();
                var boundMaterials = new List<Material>();
                foreach (Transform effect in presentation.transform)
                {
                    if (effect.name != "Lost Echoes Cast Light" && effect.name != "Lost Echoes Hand Glow") continue;
                    boundRoots.Add(effect.gameObject);
                    Renderer renderer = effect.GetComponent<Renderer>();
                    if (renderer != null) boundMaterials.Add(renderer.sharedMaterial);
                }
                Assert.That(boundRoots.Count, Is.GreaterThanOrEqualTo(2), "A successful cast must start its player-bound flare.");
                Assert.That(boundMaterials.Count, Is.GreaterThanOrEqualTo(1));
                Object.Destroy(caster.gameObject);
                yield return null;
                yield return null; // Presentation update and deferred destruction must both complete.
                foreach (GameObject root in boundRoots)
                    Assert.That(root == null, Is.True, "Player-bound effects must end when their caster despawns.");
                foreach (Material material in boundMaterials)
                    Assert.That(material == null, Is.True, "Player-bound effects must release their owned materials.");
            }
            finally
            {
                if (mover != null) mover.SetMovementLocked(sibling, false);
                OverlayInputGate.Release(sibling);
                if (host != null) Object.DestroyImmediate(host);
                state.ResetAllState();
                GameEventManager.Instance.ReloadDefinitions();
                if (menu != null) menu.enabled = menuEnabled;
                Time.timeScale = previousTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator LearningASpellSelectsAndAnnouncesItOnce()
        {
            StartupMenu menu = Object.FindAnyObjectByType<StartupMenu>();
            bool menuEnabled = menu != null && menu.enabled;
            float previousTimeScale = Time.timeScale;
            GameObject host = null;
            WorldStateService state = WorldStateService.Instance;
            try
            {
                if (menuEnabled) menu.enabled = false;
                Time.timeScale = 1f;
                GameEventManager.Instance.ReloadDefinitions();
                GameEventRuntime runtime = GameEventManager.Instance.Runtime;
                runtime.SetFlag("spell.unlocked.Ampere", true);
                state.ResetAllState();
                state.BeginMapRebuild("spell-learn-regression");
                host = new GameObject("Spell learning test");
                host.SetActive(false);
                host.AddComponent<GridWorld>();
                var player = new GameObject("Learning player");
                player.transform.SetParent(host.transform, false);
                player.AddComponent<GridMover>();
                SpellCaster caster = player.AddComponent<SpellCaster>();
                state.EndMapRebuild();
                host.SetActive(true);
                yield return null;
                Assert.That(caster.AnnouncedSpell, Is.Null, "A spell already known when play starts is not news.");

                // A lesson makes the spell usable first; finishing it then unlocks the same spell.
                runtime.SetFlag("spell.lesson.Vis", true);
                Assert.That(caster.SelectedSpell, Is.EqualTo(SpellId.Vis), "A new spell is selected at once.");
                Assert.That(caster.AnnouncedSpell, Is.EqualTo(SpellId.Vis));
                runtime.SetFlag("spell.unlocked.Rotara", true);
                Assert.That(caster.AnnouncedSpell, Is.EqualTo(SpellId.Rotara), "The latest new spell is the one announced.");
                runtime.SetFlag("spell.unlocked.Vis", true);
                Assert.That(caster.SelectedSpell, Is.EqualTo(SpellId.Rotara), "Unlocking a spell already learned in its lesson is not news.");

                runtime.ResetState();
                Assert.That(caster.AnnouncedSpell, Is.Null, "A new game or load drops a pending announcement.");
            }
            finally
            {
                if (host != null) Object.DestroyImmediate(host);
                state.ResetAllState();
                GameEventManager.Instance.ReloadDefinitions();
                if (menu != null) menu.enabled = menuEnabled;
                Time.timeScale = previousTimeScale;
            }
        }

        private static void OpenWheel(SpellCaster caster)
        {
            // Open is an input-only seam; casts and all effects below use the real public gameplay APIs.
            typeof(SpellCaster).GetMethod("OpenSpellWheel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(caster, null);
            Assert.That(caster.SpellWheelOpen, Is.True);
            Assert.That(OverlayInputGate.Owns(caster), Is.True);
        }

        private static void AssertClosed(SpellCaster caster, GridMover mover)
        {
            Assert.That(caster.SpellWheelOpen, Is.False);
            Assert.That(OverlayInputGate.Owns(caster), Is.False);
            Assert.That(OverlayInputGate.IsBlocked, Is.True, "Wheel closing must retain current-frame input consumption.");
            Assert.That(mover.IsMovementLocked, Is.True, "Wheel closing must preserve the sibling movement owner.");
        }
    }
}
