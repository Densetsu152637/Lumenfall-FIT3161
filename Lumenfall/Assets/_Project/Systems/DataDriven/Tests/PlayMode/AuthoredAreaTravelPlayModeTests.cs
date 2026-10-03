using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using Lumenfall.GameEvents;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.World
{
    public sealed class AuthoredAreaTravelPlayModeTests
    {
        private readonly List<Object> assets = new List<Object>();
        private Scene testScene;
        private string previousSlot;
        private float previousTimeScale;
        private GameAutosave autosave;
        private bool autosaveWasEnabled;
        private GridWorld world;
        private GridMover player;
        private DataDrivenWorldLoader loader;
        private WorldAreaAsset sourceArea;
        private WorldAreaAsset destinationArea;
        private PortalContentAsset source;
        private PortalContentAsset destination;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousSlot = GameSaveOverlay.CurrentSlot;
            previousTimeScale = Time.timeScale;
            string isolatedSlot = "travel-test-" + System.Guid.NewGuid().ToString("N");
            Assert.That(GameSaveManager.Instance.HasSave(isolatedSlot), Is.False);
            GameSaveOverlay.SetCurrentSlot(isolatedSlot);
            autosave = Object.FindAnyObjectByType<GameAutosave>();
            if (autosave != null)
            {
                autosaveWasEnabled = autosave.enabled;
                autosave.StopAllCoroutines();
                autosave.enabled = false;
            }
            foreach (Lumenfall.Startup.StartupMenu startup in Object.FindObjectsByType<Lumenfall.Startup.StartupMenu>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                startup.StopAllCoroutines();
                Object.Destroy(startup.gameObject);
            }
            Scene[] oldScenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            testScene = SceneManager.CreateScene("AuthoredAreaTravelTests");
            SceneManager.SetActiveScene(testScene);
            foreach (Scene scene in oldScenes) yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
            WorldStateService.Instance.ResetAllState();
            world = new GameObject("Test grid").AddComponent<GridWorld>();
            player = new GameObject("Test player").AddComponent<GridMover>();
            player.gameObject.AddComponent<GridPlayerInput>().enabled = false;
            SetField(player, "walkMoveDuration", 0f);
            sourceArea = CreateArea("source", new Vector2Int(1, 1));
            destinationArea = CreateArea("destination", new Vector2Int(9, 9));
            source = CreatePortal(sourceArea, "out", new Vector2Int(4, 4), new Vector2Int(3, 1));
            destination = CreatePortal(destinationArea, "return", new Vector2Int(2, 2), Vector2Int.one);
            source.destination = destination; destination.destination = source;
            GameObject host = new GameObject("Test loader");
            host.SetActive(false);
            loader = host.AddComponent<DataDrivenWorldLoader>();
            SetField(loader, "world", world);
            SetField(loader, "startingArea", sourceArea);
            SetField(loader, "availableAreas", new[] { destinationArea });
            host.SetActive(true);
            loader.Load(sourceArea, null);
            Assert.That(loader.CurrentArea, Is.SameAs(sourceArea));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (loader != null) loader.Clear();
            Scene replacement = SceneManager.CreateScene("AfterAuthoredAreaTravelTests");
            SceneManager.SetActiveScene(replacement);
            yield return SceneManager.UnloadSceneAsync(testScene);
            foreach (Object asset in assets) if (asset != null) Object.Destroy(asset);
            assets.Clear();
            WorldStateService.Instance.ResetAllState();
            // Discard cancelled coroutine handles and subscriptions before restoring save selection.
            GameObject autosaveHost = autosave == null ? null : autosave.gameObject;
            if (autosave != null) Object.Destroy(autosave);
            yield return null;
            if (autosaveHost != null) autosaveHost.AddComponent<GameAutosave>().enabled = autosaveWasEnabled;
            GameSaveOverlay.SetCurrentSlot(previousSlot);
            Time.timeScale = previousTimeScale;
        }

        [Test]
        public void RemoteWrongAreaAndDisabledSourceCannotTravel()
        {
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.False, "The mover is at spawn, away from the source portal.");
            Assert.That(player.TryTeleportTo(world, source.cell), Is.True);
            WorldAreaAsset actualArea = source.area;
            source.area = destinationArea;
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.False);
            source.area = actualArea;
            LivePortal(source).enabled = false;
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.False);
            Assert.That(loader.CurrentArea, Is.SameAs(sourceArea));
            Assert.That(world.GetMoverAt(player.CurrentCell), Is.SameAs(player));
        }

        [Test]
        public void ReservedEndpointsRejectAndReservedSurfaceDoesNotOpenWalls()
        {
            Assert.That(player.TryTeleportTo(world, source.cell), Is.True);
            destination.reserved = true;
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.False);
            destination.reserved = false;
            GridPortal portal = LivePortal(source);
            source.reserved = true;
            portal.RefreshFootprint();
            Assert.That(world.GetPortalAt(source.cell), Is.Null);
            Assert.That(portal.TryEnter(player), Is.False);
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.False);
            Assert.That(player.TryTeleportTo(world, Vector2Int.one), Is.True);
            GameObject wall = new GameObject("Reserved threshold");
            wall.transform.position = world.CellToWorld(source.cell);
            wall.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            Assert.That(world.IsCellBlocked(source.cell), Is.True, "Reserved portal metadata must not override the threshold obstacle.");
        }

        [Test]
        public void ClosedRegionObstacleFollowsAreaActivationAndSurvivesRevisit()
        {
            sourceArea.definition.regions = new[]
            {
                new MapRegionDefinition { id = "closed", name = "Closed", walkable = false,
                    rect = new MapRectDefinition { x = 6, y = 7, width = 2, height = 1 } }
            };
            ReservedAreaRegions regions = sourceArea.areaPrefab.AddComponent<ReservedAreaRegions>();
            SetField(regions, "area", sourceArea);
            Assert.That(loader.TryResetToStartingState(), Is.True);
            Assert.That(world.IsCellBlocked(new Vector2Int(6, 7)), Is.True);
            Assert.That(world.IsCellBlocked(new Vector2Int(7, 7)), Is.True);
            Assert.That(player.TryTeleportTo(world, source.cell), Is.True);
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.True);
            Assert.That(world.IsCellBlocked(new Vector2Int(6, 7)), Is.False,
                "Inactive source regions must not block cells in the destination map.");
            Assert.That(loader.SwitchArea(destination, Vector2Int.zero), Is.True);
            Assert.That(world.IsCellBlocked(new Vector2Int(6, 7)), Is.True,
                "Cached area activation must re-register existing region obstacles.");
        }

        [Test]
        public void MissingLiveDestinationRollsBackWithoutPublishingTargetMap()
        {
            Object.DestroyImmediate(destinationArea.areaPrefab.GetComponentInChildren<GridPortal>(true));
            AssertRejectedTravelPreservesSource();
        }

        [Test]
        public void BlockedArrivalDoesNotVisitDestinationSpawnOrLeakWorldState()
        {
            source.arrivalOffset = Vector2Int.up;
            AddObstacle(destinationArea, destination.cell + Vector2Int.up);
            AssertRejectedTravelPreservesSource();
        }

        [Test]
        public void ArchivedPuzzleObstacleIsPreparedBeforeArrivalValidation()
        {
            source.arrivalOffset = Vector2Int.up;
            AddPuzzleObstacle(destinationArea, new Vector2Int(3, 3));
            loader.Load(destinationArea, null);
            Assert.That(WorldStateService.Instance.SetProperty("test.wall", snapshot => snapshot.cell = destination.cell + Vector2Int.up), Is.True);
            loader.Load(sourceArea, null);
            AssertRejectedTravelPreservesSource();
        }

        [Test]
        public void NewGameUsesFreshAuthoredObjectsAndClearsPuzzleHistory()
        {
            AddPuzzleObstacle(sourceArea, new Vector2Int(3, 3));
            Assert.That(loader.TryResetToStartingState(), Is.True);
            Assert.That(WorldStateService.Instance.SetProperty("test.wall", snapshot =>
            { snapshot.cell = new Vector2Int(7, 7); snapshot.powered = true; }), Is.True);
            WorldStateService.Instance.QueueEffect("old-game-effect");
            Assert.That(loader.TryResetToStartingState(), Is.True);
            Assert.That(WorldStateService.Instance.TryGetObject("test.wall", out WorldObjectSnapshot reset), Is.True);
            Assert.That(reset.cell, Is.EqualTo(new Vector2Int(3, 3)));
            Assert.That(reset.powered, Is.False);
            Assert.That(world.IsCellBlocked(new Vector2Int(3, 3)), Is.True);
            Assert.That(world.IsCellBlocked(new Vector2Int(7, 7)), Is.False);
            Assert.That(WorldStateService.Instance.CaptureStateJson(), Does.Not.Contain("old-game-effect"));
        }

        [Test]
        public void OccupiedAndReservedArrivalCellsRejectTravel()
        {
            source.arrivalOffset = Vector2Int.up;
            Vector2Int arrival = destination.cell + Vector2Int.up;
            GridMover occupant = new GameObject("Other mover").AddComponent<GridMover>();
            Assert.That(occupant.TryTeleportTo(world, arrival), Is.True);
            AssertRejectedTravelPreservesSource();
            Assert.That(occupant.TryTeleportTo(world, arrival + Vector2Int.up), Is.True);
            SetField(occupant, "walkMoveDuration", 10f);
            Assert.That(occupant.TryMove(Vector2Int.down), Is.True);
            AssertRejectedTravelPreservesSource();
        }

        [UnityTest]
        public IEnumerator OffsetClampsToLiveFootprintAndArrivalDoesNotBounce()
        {
            Assert.That(player.TryTeleportTo(world, source.cell + new Vector2Int(2, 0)), Is.True);
            int teleports = 0;
            player.Teleported += (_, __) => teleports++;
            Assert.That(loader.SwitchArea(source, new Vector2Int(2, 0)), Is.True);
            Assert.That(player.CurrentCell, Is.EqualTo(destination.cell), "The one-cell destination clamps a wide source entry offset.");
            Assert.That(teleports, Is.EqualTo(1), "Travel must never teleport via the destination spawn first.");
            GridPortal target = LivePortal(destination);
            destination.footprint = new Vector2Int(2, 1);
            target.Configure(destination, world);
            Assert.That(player.TryMove(Vector2Int.right), Is.True);
            yield return null;
            Assert.That(loader.CurrentArea, Is.SameAs(destinationArea), "Moving within the arrival portal must not bounce back.");
            Assert.That(player.TryMove(Vector2Int.up), Is.True);
            yield return null;
            Assert.That(player.TryMove(Vector2Int.down), Is.True);
            yield return null;
            Assert.That(loader.CurrentArea, Is.SameAs(sourceArea), "Leaving then re-entering releases arrival suppression.");
        }

        [Test]
        public void MalformedAndBlockedSavedCellPreserveAreaCellAndReadiness()
        {
            Assert.That(loader.RestoreStateJson("not json"), Is.False);
            Assert.That(loader.RestoreStateJson("{\"mapId\":\"unknown\",\"playerX\":2,\"playerY\":3}"), Is.False);
            AddObstacle(destinationArea, new Vector2Int(3, 3));
            Vector2Int before = player.CurrentCell;
            int teleports = 0;
            player.Teleported += (_, __) => teleports++;
            Assert.That(loader.RestoreStateJson("{\"mapId\":\"destination\",\"playerX\":3,\"playerY\":3}"), Is.False);
            Assert.That(loader.CurrentArea, Is.SameAs(sourceArea));
            Assert.That(player.CurrentCell, Is.EqualTo(before));
            Assert.That(teleports, Is.Zero);
            Assert.That(WorldStateService.Instance.CurrentMapId, Is.EqualTo("source"));
            Assert.That(WorldStateService.Instance.IsReady, Is.True);
        }

        [UnityTest]
        public IEnumerator RestoreBeforeStartIsNotOverwrittenAndFailedInitialRestoreRestoresBounds()
        {
            loader.Clear();
            WorldStateService.Instance.ResetAllState();
            SetField(world, "restrictToBounds", false);
            SetField(world, "boundsMin", new Vector2Int(-7, -8));
            SetField(world, "boundsSize", new Vector2Int(30, 31));
            Assert.That(loader.RestoreStateJson("{\"mapId\":\"destination\",\"playerX\":-1,\"playerY\":3}"), Is.False);
            Assert.That(loader.CurrentArea, Is.Null);
            Assert.That(world.RestrictToBounds, Is.False);
            Assert.That(world.BoundsMin, Is.EqualTo(new Vector2Int(-7, -8)));
            Assert.That(world.BoundsSize, Is.EqualTo(new Vector2Int(30, 31)));
            Assert.That(WorldStateService.Instance.IsReady, Is.False);
            Assert.That(loader.RestoreStateJson("{\"mapId\":\"destination\",\"playerX\":3,\"playerY\":3}"), Is.True);
            yield return null;
            Assert.That(loader.CurrentArea, Is.SameAs(destinationArea));
            Assert.That(player.CurrentCell, Is.EqualTo(new Vector2Int(3, 3)));
        }

        [Test]
        public void InvalidNewGameSpawnKeepsCurrentBackdropAndWorldHistory()
        {
            Assert.That(player.TryTeleportTo(world, source.cell), Is.True);
            Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.True);
            string previousState = WorldStateService.Instance.CaptureStateJson();
            Vector2Int previousCell = player.CurrentCell;
            sourceArea.definition.spawn.x = -1;
            Assert.That(loader.TryResetToStartingState(), Is.False);
            Assert.That(loader.CurrentArea, Is.SameAs(destinationArea));
            Assert.That(player.CurrentCell, Is.EqualTo(previousCell));
            Assert.That(WorldStateService.Instance.CaptureStateJson(), Is.EqualTo(previousState));
        }

        private void AssertRejectedTravelPreservesSource()
        {
            Assert.That(player.TryTeleportTo(world, source.cell), Is.True);
            Vector3 position = player.transform.position;
            string before = WorldStateService.Instance.CaptureStateJson();
            var rebuilt = new List<string>();
            System.Action<MapRebuildEvent> listener = change => rebuilt.Add(change.MapId);
            WorldStateService.Instance.MapRebuilt += listener;
            int teleports = 0;
            player.Teleported += (_, __) => teleports++;
            try { Assert.That(loader.SwitchArea(source, Vector2Int.zero), Is.False); }
            finally { WorldStateService.Instance.MapRebuilt -= listener; }
            Assert.That(loader.CurrentArea, Is.SameAs(sourceArea));
            Assert.That(player.CurrentCell, Is.EqualTo(source.cell));
            Assert.That(player.transform.position, Is.EqualTo(position));
            Assert.That(world.GetMoverAt(source.cell), Is.SameAs(player));
            Assert.That(player.IsMovementLocked, Is.False);
            Assert.That(teleports, Is.Zero);
            Assert.That(rebuilt, Is.Empty, "A rejected target must not publish quest-visible map rebuilds.");
            Assert.That(WorldStateService.Instance.CaptureStateJson(), Is.EqualTo(before));
        }

        private WorldAreaAsset CreateArea(string id, Vector2Int spawn)
        {
            WorldAreaAsset area = ScriptableObject.CreateInstance<WorldAreaAsset>();
            assets.Add(area);
            area.definition = new MapDefinition { id = id, width = 12, height = 12,
                spawn = new SpawnDefinition { x = spawn.x, y = spawn.y } };
            area.areaPrefab = new GameObject(id + " template");
            area.areaPrefab.SetActive(false);
            return area;
        }

        private PortalContentAsset CreatePortal(WorldAreaAsset area, string id, Vector2Int cell, Vector2Int size)
        {
            PortalContentAsset content = ScriptableObject.CreateInstance<PortalContentAsset>();
            assets.Add(content);
            content.id = id; content.area = area; content.cell = cell; content.footprint = size;
            GameObject glyph = new GameObject(id);
            glyph.transform.SetParent(area.areaPrefab.transform, false);
            glyph.AddComponent<GridPortal>().Configure(content, world);
            return content;
        }

        private void AddObstacle(WorldAreaAsset area, Vector2Int cell)
        {
            GameObject wall = new GameObject("Blocked arrival");
            wall.transform.SetParent(area.areaPrefab.transform, false);
            wall.transform.position = world.CellToWorld(cell);
            wall.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
        }

        private GameObject AddPuzzleObstacle(WorldAreaAsset area, Vector2Int cell)
        {
            GameObject wall = new GameObject("Puzzle obstacle");
            wall.transform.SetParent(area.areaPrefab.transform, false);
            wall.transform.position = world.CellToWorld(cell);
            wall.AddComponent<GridObstacle>().Configure(world, Vector2Int.one);
            wall.AddComponent<WorldObjectRegistration>().Configure("test.wall", cell, Vector2Int.one, true, new string[0]);
            return wall;
        }

        private static GridPortal LivePortal(PortalContentAsset content) =>
            Object.FindObjectsByType<GridPortal>(FindObjectsInactive.Exclude).Single(portal => portal.Content == content);

        private static void SetField(Object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
