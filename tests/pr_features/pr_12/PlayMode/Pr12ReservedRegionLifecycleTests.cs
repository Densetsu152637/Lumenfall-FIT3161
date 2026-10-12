using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR12")]
    public sealed class Pr12ReservedRegionLifecycleTests
    {
        private GameObject host;
        private GameObject regionHost;
        private GridWorld grid;
        private WorldAreaAsset area;
        [SetUp]
        public void SetUp()
        {
            host = new GameObject("pr12-grid");
            grid = host.AddComponent<GridWorld>(); grid.ConfigureBounds(8, 8);
            regionHost = new GameObject("pr12-area"); regionHost.SetActive(false);
            regionHost.transform.SetParent(host.transform, false);
            area = ScriptableObject.CreateInstance<WorldAreaAsset>();
            area.definition = new MapDefinition { id = "test", width = 8, height = 8 };
            var regions = regionHost.AddComponent<ReservedAreaRegions>();
            typeof(ReservedAreaRegions).GetField("area", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(regions, area);
        }
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(host); Object.Destroy(area); yield return null;
        }
        private static MapRegionDefinition Region(string id, int x, int y, int width, int height, bool walkable = false) =>
            new MapRegionDefinition { id = id, name = id, walkable = walkable, rect = new MapRectDefinition { x = x, y = y, width = width, height = height } };
        [UnityTest]
        public IEnumerator ClosedFootprintBlocksEveryCellButWalkableRegionRemainsOpen()
        {
            area.definition.regions = new[] { Region("closed", 2, 2, 2, 3), Region("open", 5, 5, 2, 2, true) };
            regionHost.SetActive(true); yield return null;
            for (int x = 2; x < 4; x++) for (int y = 2; y < 5; y++) Assert.That(grid.IsCellBlocked(new Vector2Int(x, y)), Is.True);
            Assert.That(grid.IsCellBlocked(new Vector2Int(4, 2)), Is.False);
            Assert.That(grid.IsCellBlocked(new Vector2Int(5, 5)), Is.False);
        }
        [UnityTest]
        public IEnumerator DisableAndReenableReleasesAndRestoresSingleFootprint()
        {
            area.definition.regions = new[] { Region("closed", 2, 2, 2, 2) };
            regionHost.SetActive(true); yield return null;
            regionHost.SetActive(false); yield return null;
            Assert.That(grid.IsCellBlocked(new Vector2Int(2, 2)), Is.False);
            regionHost.SetActive(true); yield return null;
            Assert.That(grid.IsCellBlocked(new Vector2Int(2, 2)), Is.True);
            Assert.That(regionHost.GetComponentsInChildren<GridObstacle>().Length, Is.EqualTo(1));
            Object.Destroy(regionHost); yield return null;
            Assert.That(grid.IsCellBlocked(new Vector2Int(2, 2)), Is.False);
        }
        [UnityTest]
        public IEnumerator InvalidFootprintIsReportedWithoutDiscardingValidSibling()
        {
            area.definition.regions = new[] { Region("invalid", 7, 7, 2, 2), Region("valid", 1, 1, 1, 1) };
            LogAssert.Expect(LogType.Error, new Regex("Reserved region 'invalid' has an invalid footprint"));
            regionHost.SetActive(true); yield return null;
            Assert.That(grid.IsCellBlocked(new Vector2Int(7, 7)), Is.False);
            Assert.That(grid.IsCellBlocked(new Vector2Int(1, 1)), Is.True);
            Assert.That(regionHost.GetComponentsInChildren<GridObstacle>().Length, Is.EqualTo(1));
        }
    }
}
