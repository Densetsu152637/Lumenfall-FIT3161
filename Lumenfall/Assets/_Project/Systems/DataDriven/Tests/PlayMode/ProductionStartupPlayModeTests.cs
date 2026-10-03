using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using Lumenfall.Startup;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Lumenfall.Tests.World
{
    /// <summary>Exercises production startup preparation without creating, replacing or marking save files.</summary>
    // Unity runs these PlayMode scene fixtures serially; each fixture isolates save selection and autosave.
    public sealed class ProductionStartupPlayModeTests
    {
        private string previousSlot;
        private float previousTimeScale;
        private bool autosaveWasEnabled;
        private GameAutosave autosave;
        private StartupMenu menu;
        private DataDrivenWorldLoader loader;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSlot = GameSaveOverlay.CurrentSlot;
            previousTimeScale = Time.timeScale;
            string isolatedSlot = "startup-test-" + Guid.NewGuid().ToString("N");
            Assert.That(GameSaveManager.Instance.HasSave(isolatedSlot), Is.False);
            GameSaveOverlay.SetCurrentSlot(isolatedSlot);
            autosave = Object.FindAnyObjectByType<GameAutosave>();
            if (autosave != null)
            {
                autosaveWasEnabled = autosave.enabled;
                autosave.StopAllCoroutines();
                autosave.enabled = false;
            }
            CancelBackdropInitialization();
            SceneManager.sceneLoaded += OnSceneLoaded;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            menu = Object.FindAnyObjectByType<StartupMenu>();
            loader = DataDrivenWorldLoader.Instance;
            Assert.That(menu, Is.Not.Null, "The build entry scene must wire the startup authority.");
            Assert.That(loader, Is.Not.Null);
            Assert.That(menu.TryPrepareNewGame(), Is.True);
            yield return null;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CancelBackdropInitialization();

        private static void CancelBackdropInitialization()
        {
            // sceneLoaded precedes Start and the menu's yielded initialization. No disk backdrop
            // is restored, and the nonexistent slot keeps autosave notifications read-only.
            foreach (StartupMenu startup in Object.FindObjectsByType<StartupMenu>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                startup.StopAllCoroutines();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (menu != null)
            {
                menu.TryPrepareNewGame();
                Object.Destroy(menu.gameObject);
            }
            // Recreate the existing autosave component to discard any cancelled pending coroutine
            // handle before restoring the user's selected slot. No slot is ever saved by this fixture.
            GameObject autosaveHost = autosave == null ? null : autosave.gameObject;
            if (autosave != null) Object.Destroy(autosave);
            yield return null;
            if (autosaveHost != null)
                autosaveHost.AddComponent<GameAutosave>().enabled = autosaveWasEnabled;
            GameSaveOverlay.SetCurrentSlot(previousSlot);
            Time.timeScale = previousTimeScale;
        }

        [UnityTest]
        public IEnumerator NewGamePreparationTwiceRestoresAuthoredSpawnAndSameAuthorities()
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            GridMover player = Object.FindAnyObjectByType<GridMover>();
            Vector2Int spawn = new Vector2Int(21, 5);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                runtime.SetFlag("startup-test.progress", true);
                runtime.SetCounter("startup-test.counter", 9);
                Assert.That(player.TryTeleportTo(player.World, spawn + Vector2Int.right), Is.True);
                Assert.That(menu.TryPrepareNewGame(), Is.True);
                yield return null;
                Assert.That(GameEventManager.Instance.Runtime, Is.SameAs(runtime));
                Assert.That(runtime.GetFlag("startup-test.progress"), Is.False);
                Assert.That(runtime.GetCounter("startup-test.counter"), Is.Zero);
                Assert.That(loader.LoadedMap.id, Is.EqualTo("westForest"));
                Assert.That(player.CurrentCell, Is.EqualTo(spawn));
                Assert.That(WorldStateService.Instance.IsReady, Is.True);
                Assert.That(WorldStateService.Instance.CurrentMapId, Is.EqualTo("westForest"));
                AssertAuthorities(player);
            }
        }

        [UnityTest]
        public IEnumerator PuzzleMonitoringAndSpellRewardsStillWorkAfterRepeatedNewGames()
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            SpellCaster caster = Object.FindAnyObjectByType<GridMover>().GetComponent<SpellCaster>();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Assert.That(menu.TryPrepareNewGame(), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
                Assert.That(runtime.StartQuest("orio_patch_notes"), Is.True);
                Assert.That(WorldStateService.Instance.SetProperty("mechanism.orio.rotate",
                    snapshot => snapshot.rotation = 90f), Is.True);
                // Start the same quest immediately after reset. The monitor must clear its fired
                // cache through StateRestored; it never observes a NotStarted frame in this loop.
                float deadline = Time.realtimeSinceStartup + 2f;
                while (runtime.GetQuestStatus("orio_patch_notes") != QuestStatus.Completed &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(runtime.GetQuestStatus("orio_patch_notes"), Is.EqualTo(QuestStatus.Completed));
                Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True,
                    "The existing caster must receive completion rewards on the preserved runtime.");
                Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator SceneReloadKeepsOneStartupAndRegistersTheReplacementLoader()
        {
            GameSaveManager saves = GameSaveManager.Instance;
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            yield return null;
            loader = DataDrivenWorldLoader.Instance;
            Assert.That(menu.TryPrepareNewGame(), Is.True);
            Assert.That(StartupMenu.IsOpen, Is.True, "Scene reload must retain the startup save guard.");
            Assert.That(GameSaveManager.Instance, Is.SameAs(saves));
            Assert.That(GameEventManager.Instance.Runtime, Is.SameAs(runtime));
            AssertAuthorities(Object.FindAnyObjectByType<GridMover>());
            var providers = (Dictionary<string, ISaveStateProvider>)typeof(GameSaveManager)
                .GetField("providers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(saves);
            Assert.That(providers[loader.SaveKey], Is.SameAs(loader),
                "Reloaded scene must replace the old save provider, retaining a single authoritative loader.");
            Assert.That(providers[WorldStateService.Instance.SaveKey], Is.SameAs(WorldStateService.Instance));
        }

        [UnityTest]
        public IEnumerator StartupAutosaveGuardLastsUntilTheOwningMenuIsDestroyed()
        {
            Assert.That(StartupMenu.IsOpen, Is.True);
            menu.enabled = false;
            Assert.That(menu.TryPrepareNewGame(), Is.True);
            Assert.That(StartupMenu.IsOpen, Is.True,
                "Disabling presentation must not release the menu's startup/save ownership.");
            loader.enabled = false;
            try
            {
                Assert.That(menu.TryPrepareNewGame(), Is.False);
                Assert.That(StartupMenu.IsOpen, Is.True,
                    "Rejected preparation must continue guarding the selected save slot.");
            }
            finally { loader.enabled = true; }

            GameObject duplicate = new GameObject("startup-menu-duplicate-test");
            duplicate.AddComponent<StartupMenu>();
            yield return null;
            Assert.That(duplicate == null, Is.True);
            Assert.That(StartupMenu.IsOpen, Is.True,
                "Destroying a duplicate must not release the surviving menu's guard.");
            Assert.That(Time.timeScale, Is.Zero);

            Object.Destroy(menu.gameObject);
            yield return null;
            Assert.That(StartupMenu.IsOpen, Is.False,
                "Autosave can resume only after the owning startup menu has been destroyed.");
        }

        [Test]
        public void UnavailableLoaderRejectsPreparationWithoutClearingProgress()
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            runtime.SetFlag("startup-test.retained", true);
            loader.enabled = false;
            try
            {
                Assert.That(menu.TryPrepareNewGame(), Is.False);
                Assert.That(runtime.GetFlag("startup-test.retained"), Is.True);
            }
            finally { loader.enabled = true; }
        }

        private static void AssertAuthorities(GridMover player)
        {
            Assert.That(Object.FindObjectsByType<GridMover>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GridPlayerInput>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GridWorld>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<CameraFollow2_5D>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<DataDrivenWorldLoader>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<DialogueOverlay>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<SpellCaster>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(player.GetComponent<SpellCaster>(), Is.Not.Null);
            Assert.That(Object.FindObjectsByType<QuestPuzzleMonitor>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameEventManager>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameSaveManager>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GameSaveOverlay>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<WorldStateService>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<StartupMenu>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }
    }
}
