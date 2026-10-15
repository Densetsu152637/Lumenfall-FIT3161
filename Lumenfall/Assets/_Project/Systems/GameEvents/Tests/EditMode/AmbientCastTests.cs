using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Player;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class AmbientCastTests
    {
        private static NpcCatalogAsset Catalog()
        {
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            return catalog;
        }

        // Placed by hand in the Dormitory prefab, rather than spawned by AmbientCast.
        private static readonly string[] DormitoryIds = { "marigold", "thistle", "rowan" };
        private const string DormitoryPrefabPath = "Assets/_Project/World/Areas/Prefabs/Dormitory.prefab";

        private static NpcDefinition[] Records(IEnumerable<string> ids)
        {
            NpcCatalogAsset catalog = Catalog();
            return ids.Select(id =>
            {
                NpcDefinition record = Array.Find(catalog.npcs, npc => npc.id == id);
                Assert.That(record, Is.Not.Null, id);
                return record;
            }).ToArray();
        }

        private static NpcDefinition[] Cast() => Records(AmbientCast.NpcIds);

        [Test]
        public void EachCastMemberUsesTheirNamedPrefab()
        {
            Dictionary<string, (string name, string prefab)> expected = new Dictionary<string, (string, string)>
            {
                { "prof_voltaire", ("Prof. Voltaire", "Teacher_02") },
                { "vex", ("Vex", "Student_04") },
                { "luna", ("Luna", "Student_01") },
                { "juno", ("Juno", "Student_03") },
                { "moss", ("Moss", "Student_05") }
            };
            foreach (NpcDefinition record in Cast())
            {
                Assert.That(record.displayName, Is.EqualTo(expected[record.id].name), record.id);
                Assert.That(record.prefab, Is.Not.Null, record.id);
                Assert.That(record.prefab.name, Is.EqualTo(expected[record.id].prefab), record.id);
                Assert.That(record.layerSymbol, Is.Empty, record.id + " is spawned at runtime, not baked into an area.");
            }
        }

        [Test]
        public void VexAndVoltaireAreInTheLibraryAndSomeoneIsInTheForest()
        {
            NpcDefinition[] cast = Cast();
            Assert.That(cast.Single(npc => npc.id == "vex").mapId, Is.EqualTo("B04.Ground"));
            Assert.That(cast.Single(npc => npc.id == "prof_voltaire").mapId, Is.EqualTo("B04.Ground"));
            Assert.That(cast.Any(npc => npc.mapId == "westForest" && npc.prefab.name.StartsWith("Student_")), Is.True,
                "At least one student should be in the forest.");
        }

        [Test]
        public void OdetteIsKeptBack()
        {
            Assert.That(Cast().Concat(Records(DormitoryIds)).Select(npc => npc.prefab.name), Has.No.Member("Student_06"));
        }

        [Test]
        public void BothReceptionistsStandBehindTheDormitoryCounter()
        {
            foreach (NpcDefinition receptionist in Records(new[] { "marigold", "thistle" }))
            {
                Assert.That(receptionist.mapId, Is.EqualTo("B01.Ground"), receptionist.id);
                // The counter runs along row 7 from x 6 to 13; they stand on row 8 behind it, within talking range of row 6.
                Assert.That(receptionist.y, Is.EqualTo(8), receptionist.id);
                Assert.That(receptionist.x, Is.InRange(7, 13), receptionist.id);
            }
        }

        [Test]
        public void TheDormitoryPeopleAreInItsPrefabAtTheirCatalogCells()
        {
            Dictionary<string, (string name, string prefab)> expected = new Dictionary<string, (string, string)>
            {
                { "marigold", ("Marigold", "Receptionist_01") },
                { "thistle", ("Thistle", "Receptionist_02") },
                { "rowan", ("Rowan", "Student_02") }
            };
            GameObject dormitory = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(DormitoryPrefabPath);
            Assert.That(dormitory, Is.Not.Null);
            DynamicNpc[] placed = dormitory.GetComponentsInChildren<DynamicNpc>(true);
            foreach (NpcDefinition record in Records(DormitoryIds))
            {
                Assert.That(AmbientCast.NpcIds, Has.No.Member(record.id), record.id + " is baked into the prefab, so it must not also spawn.");
                Assert.That(record.mapId, Is.EqualTo("B01.Ground"), record.id);
                Assert.That(record.displayName, Is.EqualTo(expected[record.id].name), record.id);
                Assert.That(record.prefab.name, Is.EqualTo(expected[record.id].prefab), record.id);
                DynamicNpc npc = placed.SingleOrDefault(candidate => candidate.Definition != null && candidate.Definition.id == record.id);
                Assert.That(npc, Is.Not.Null, record.id + " is missing from the Dormitory prefab.");
                Vector3 local = dormitory.transform.InverseTransformPoint(npc.transform.position);
                Assert.That(new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.z)), Is.EqualTo(new Vector2Int(record.x, record.y)),
                    record.id + " is not on their catalog cell; move them in Dormitory.prefab or change the cell in NpcCatalog.asset.");
                Assert.That(npc.GetComponentInChildren<GridObstacle>(true), Is.Not.Null, record.id + " should block their cell.");
            }
        }

        [Test]
        public void RowanWaitsInTheLobbyInFrontOfTheCounter()
        {
            NpcDefinition rowan = Records(new[] { "rowan" }).Single();
            Assert.That(new Vector2Int(rowan.x, rowan.y), Is.EqualTo(new Vector2Int(12, 6)));
        }

        [Test]
        public void EveryCastMemberHasSomethingToSay()
        {
            List<GameEventDatabaseDefinition> databases = new List<GameEventDatabaseDefinition>();
            foreach (string file in new[] { "GameEvents/ambient_cast", "GameEvents/dormitory" })
            {
                TextAsset asset = Resources.Load<TextAsset>(file);
                Assert.That(asset, Is.Not.Null, file);
                Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
                databases.Add(database);
            }
            GameEventCatalog catalog = new GameEventCatalog(databases);
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            foreach (NpcDefinition record in Cast().Concat(Records(DormitoryIds)))
            {
                string dialogueId = record.dialogue.Single().dialogueId;
                Assert.That(catalog.TryGetDialogue(dialogueId, out DialogueDefinition dialogue), Is.True, record.id);
                foreach (DialogueNodeDefinition node in dialogue.nodes)
                {
                    if (PlayerProfile.IsPlayerSpeaker(node.speaker))
                        Assert.That(node.text, Does.StartWith("<i>").And.EndWith("</i>"), $"{dialogueId}/{node.id}");
                    else
                        // Matching the catalog name is what gives each of them a portrait.
                        Assert.That(node.speaker, Is.EqualTo(record.displayName), $"{dialogueId}/{node.id}");
                }
            }
        }
    }
}
