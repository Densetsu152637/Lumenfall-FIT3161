using System;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// The West Forest grew west and south into negative cells, so every original tile, NPC, quest cell and portal kept
    /// its coordinates. Saved objects there must still load, and Orio's records must agree on his new clearing.
    /// </summary>
    public sealed class WestForestExpansionTests
    {
        private const string ForestArea = "Assets/_Project/World/Areas/westForest.asset";

        [Test]
        public void SavedObjectsInTheNewNegativeCellsLoadAndOnesBeyondTheEdgeDoNot()
        {
            WorldAreaAsset forest = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(ForestArea);
            MapDefinition map = forest.definition;
            Assert.That(map.boundsMinX, Is.LessThan(0));
            Assert.That(map.boundsMinY, Is.LessThan(0));

            DataDrivenWorldLoader previous = DataDrivenWorldLoader.Instance;
            GameObject host = new GameObject("expansion-test-loader");
            host.SetActive(false);
            try
            {
                DataDrivenWorldLoader loader = host.AddComponent<DataDrivenWorldLoader>();
                typeof(DataDrivenWorldLoader).GetField("availableAreas", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(loader, new[] { forest });
                SetLoaderInstance(loader);

                Vector2Int inside = new Vector2Int(map.boundsMinX + 2, map.boundsMinY + 2);
                Vector2Int beyond = new Vector2Int(map.boundsMinX - 1, map.boundsMinY + 2);
                Assert.That(Validate(map.id, inside), Is.True, "An object at " + inside + " in the new south-west is a valid save.");
                Assert.That(Validate(map.id, beyond), Is.False, "An object past the forest's west edge is rejected.");
                Assert.That(Validate(map.id, new Vector2Int(map.boundsMinX + map.width - 1, 0)), Is.True, "The east edge is unchanged.");
                Assert.That(Validate(map.id, new Vector2Int(map.boundsMinX + map.width, 0)), Is.False);
            }
            finally
            {
                SetLoaderInstance(previous);
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OrioWaitsInTheWaystoneClearingInEveryRecord()
        {
            NpcCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            NpcDefinition orio = Array.Find(catalog.npcs, npc => npc != null && npc.id == "orio");
            Assert.That(orio, Is.Not.Null);
            Assert.That(orio.mapId, Is.EqualTo("westForest"));
            Assert.That(new Vector2Int(orio.x, orio.y), Is.EqualTo(new Vector2Int(-12, 24)));
            Vector2Int fallback = (Vector2Int)typeof(OrioDaisRoom).GetField("FallbackOrioCell", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Assert.That(fallback, Is.EqualTo(new Vector2Int(orio.x, orio.y)), "The dais room's fallback for Orio matches the catalog.");
        }

        private static bool Validate(string mapId, Vector2Int cell)
        {
            MapStateSnapshot snapshot = new MapStateSnapshot
            {
                mapId = mapId,
                objects = new[] { new WorldObjectSnapshot { id = "expansion-test-relic", mapId = mapId, cell = cell, size = Vector2Int.one } },
            };
            return (bool)typeof(WorldStateService).GetMethod("ValidateSnapshot", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { snapshot });
        }

        private static void SetLoaderInstance(DataDrivenWorldLoader loader) =>
            typeof(DataDrivenWorldLoader).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).SetValue(null, loader);
    }
}
