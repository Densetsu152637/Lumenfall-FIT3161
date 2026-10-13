using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
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

        private static NpcDefinition[] Cast()
        {
            NpcCatalogAsset catalog = Catalog();
            return AmbientCast.NpcIds.Select(id =>
            {
                NpcDefinition record = Array.Find(catalog.npcs, npc => npc.id == id);
                Assert.That(record, Is.Not.Null, id);
                return record;
            }).ToArray();
        }

        [Test]
        public void EachCastMemberUsesTheirNamedPrefab()
        {
            Dictionary<string, (string name, string prefab)> expected = new Dictionary<string, (string, string)>
            {
                { "prof_voltaire", ("Prof. Voltaire", "Teacher_02") },
                { "vex", ("Vex", "Student_04") },
                { "luna", ("Luna", "Student_01") },
                { "rowan", ("Rowan", "Student_02") },
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
        public void ReceptionistsAndOdetteAreKeptBack()
        {
            string[] reserved = { "Receptionist_01", "Receptionist_02", "Student_06" };
            Assert.That(Cast().Select(npc => npc.prefab.name).Intersect(reserved), Is.Empty);
        }

        [Test]
        public void EveryCastMemberHasSomethingToSay()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/ambient_cast");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));

            foreach (NpcDefinition record in Cast())
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
