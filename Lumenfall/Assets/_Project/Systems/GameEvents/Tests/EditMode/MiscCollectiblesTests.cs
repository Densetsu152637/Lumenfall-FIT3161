using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Story;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class MiscCollectiblesTests
    {
        private static GameEventCatalog Events()
        {
            List<GameEventDatabaseDefinition> databases = new List<GameEventDatabaseDefinition>();
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>("GameEvents"))
            {
                Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
                databases.Add(database);
            }
            return new GameEventCatalog(databases);
        }

        [Test]
        public void EveryCurioHasANameADescriptionAndAPickupEvent()
        {
            GameEventCatalog events = Events();
            GameEventRuntime runtime = new GameEventRuntime(events);
            Assert.That(MiscCollectibles.Items.Select(item => item.Key), Is.Unique);
            foreach (MiscCollectibles.Item item in MiscCollectibles.Items)
            {
                Assert.That(MiscCollectibles.TryGetText(runtime, item.Key, out string name, out string description), Is.True, item.Key);
                Assert.That(name, Is.Not.Empty, item.Key);
                Assert.That(description, Is.Not.Empty, item.Key);

                string found = MiscCollectibles.FoundId(item.Key);
                GameEventDefinition pickup = events.Events.Values.SingleOrDefault(candidate => candidate.id == found);
                Assert.That(pickup, Is.Not.Null, found + " needs a pickup event.");
                Assert.That(pickup.actions.Any(action => action.type == "SetFlag" && action.key == MiscCollectibles.FlagFor(item.Key) && action.boolValue), Is.True, found);
                Assert.That(pickup.actions.Any(action => action.type == "StartDialogue" && action.key == found), Is.True, found);
            }
        }

        [Test]
        public void PickingOneUpPutsItInTheJournal()
        {
            GameEventRuntime runtime = new GameEventRuntime(Events());
            const string key = "rubber_ducky";
            Assert.That(MiscCollectibles.IsFound(runtime, key), Is.False);
            Assert.That(runtime.TriggerEvent(MiscCollectibles.FoundId(key)).Succeeded, Is.True);
            Assert.That(MiscCollectibles.IsFound(runtime, key), Is.True);
            Assert.That(runtime.ActiveDialogue?.Definition.id, Is.EqualTo(MiscCollectibles.FoundId(key)), "Picking it up shows its description.");
        }

        [Test]
        public void EveryCurioIsOnARealMap()
        {
            string[] maps = UnityEditor.AssetDatabase.FindAssets("t:WorldAreaAsset")
                .Select(guid => UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid)))
                .Where(area => area != null && area.definition != null).Select(area => area.definition.id).ToArray();
            foreach (MiscCollectibles.Item item in MiscCollectibles.Items)
                Assert.That(maps, Has.Member(item.MapId), item.Key);
        }
    }
}
