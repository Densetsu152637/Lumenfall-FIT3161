using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class GameSaveRecoveryTests
    {
        private GameSaveManager manager;
        private GameEventRuntime runtime;
        private string previousEvents;
        private string slot;
        private readonly List<ISaveStateProvider> registered = new List<ISaveStateProvider>();
        private string PathForSlot => Path.Combine(manager.SaveDirectory, slot + ".json");
        [SetUp]
        public void SetUp()
        {
            manager = GameSaveManager.Instance;
            runtime = GameEventManager.Instance.Runtime;
            previousEvents = runtime.ExportStateJson(false);
            slot = "save-test-" + Guid.NewGuid().ToString("N");
            manager.ResetState(() => { });
            Directory.CreateDirectory(manager.SaveDirectory);
        }
        [TearDown]
        public void TearDown()
        {
            foreach (ISaveStateProvider provider in registered) manager.UnregisterProvider(provider);
            registered.Clear();
            manager.ResetState(() => runtime.ImportStateJson(previousEvents));
            foreach (string path in Directory.GetFiles(manager.SaveDirectory, slot + ".json*")) File.Delete(path);
        }
        private void Register(ISaveStateProvider provider) { Assert.That(manager.RegisterProvider(provider), Is.True); registered.Add(provider); }
        private void WriteV1(params Entry[] states) => File.WriteAllText(PathForSlot, JsonUtility.ToJson(new Envelope { schemaVersion = 1, savedAtUtc = "2026-01-01T00:00:00Z", eventStateJson = runtime.ExportStateJson(false), providers = states }));
        private static void ExpectFailure() => LogAssert.Expect(LogType.Error, new Regex(".+"));

        [Test]
        public void SchemaOneRestoresQuestAndUnlockedFlagsAndUpgradesOnlyOnSave()
        {
            runtime.ResetState();
            Assert.That(runtime.StartQuest("stella_first_light"), Is.True);
            runtime.SetFlag("spell.unlocked.Ampere", true);
            WriteV1();
            string oldBytes = File.ReadAllText(PathForSlot);
            runtime.ResetState();
            Assert.That(manager.InspectSlot(slot).SchemaVersion, Is.EqualTo(1));
            Assert.That(manager.Load(slot, false), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.True);
            Assert.That(File.ReadAllText(PathForSlot), Is.EqualTo(oldBytes));
            Assert.That(manager.Save(slot), Is.True);
            Assert.That(manager.InspectSlot(slot).SchemaVersion, Is.EqualTo(2));
            Assert.That(File.ReadAllText(PathForSlot + ".bak"), Is.EqualTo(oldBytes));
        }
        [TestCase(0)] [TestCase(3)] [TestCase(99)]
        public void UnsupportedVersionsDoNotChangeRuntimeOrFile(int version)
        {
            File.WriteAllText(PathForSlot, "{\"schemaVersion\":" + version + "}");
            string bytes = File.ReadAllText(PathForSlot);
            Assert.That(manager.InspectSlot(slot).Status, Is.EqualTo(SaveSlotStatus.Unsupported));
            ExpectFailure(); Assert.That(manager.Load(slot, false), Is.False);
            Assert.That(File.ReadAllText(PathForSlot), Is.EqualTo(bytes));
            Assert.That(runtime.ExportStateJson(false), Is.EqualTo(previousEvents));
            Assert.That(manager.AutosaveAllowed, Is.False);
        }
        [Test]
        public void SchemaTwoIntegrityFailureRejectsValidJsonTamperingWithoutChangingState()
        {
            runtime.SetCounter("save-test-integrity", 10);
            Assert.That(manager.Save(slot), Is.True);
            string bytes = File.ReadAllText(PathForSlot).Replace("10", "11");
            File.WriteAllText(PathForSlot, bytes);
            string current = runtime.ExportStateJson(false);
            Assert.That(manager.InspectSlot(slot).Message, Does.Contain("integrity"));
            ExpectFailure(); Assert.That(manager.Load(slot, false), Is.False);
            Assert.That(runtime.ExportStateJson(false), Is.EqualTo(current));
            Assert.That(File.ReadAllText(PathForSlot), Is.EqualTo(bytes));
        }
        [Test]
        public void MissingAndTruncatedAreDistinguishable()
        {
            Assert.That(manager.InspectSlot(slot).Status, Is.EqualTo(SaveSlotStatus.Missing));
            File.WriteAllText(PathForSlot, "{\"schemaVersion\":1,");
            Assert.That(manager.InspectSlot(slot).Status, Is.EqualTo(SaveSlotStatus.Truncated));
        }
        [Test]
        public void EmbeddedEmptyEventStateAndInvalidQuestStatusAreRejectedBeforeRestore()
        {
            foreach (string events in new[] { "{}", "{\"flags\":[],\"counters\":[],\"quests\":[{\"questId\":\"stella_first_light\",\"status\":\"99\"}],\"triggeredEventIds\":[]}" })
            {
                File.WriteAllText(PathForSlot, JsonUtility.ToJson(new Envelope { schemaVersion = 1, eventStateJson = events }));
                Assert.That(manager.InspectSlot(slot).Status, Is.EqualTo(SaveSlotStatus.Corrupt));
                Assert.That(runtime.ExportStateJson(false), Is.EqualTo(previousEvents));
            }
        }
        [Test]
        public void DuplicateProviderAndEscapedDuplicateMemberAreRejected()
        {
            WriteV1(new Entry { key = "duplicate", payload = "{}" }, new Entry { key = "duplicate", payload = "{}" });
            Assert.That(manager.InspectSlot(slot).CanLoad, Is.False);
            File.WriteAllText(PathForSlot, "{\"schemaVersion\":1,\"schema\\u0056ersion\":1}");
            Assert.That(manager.InspectSlot(slot).Status, Is.EqualTo(SaveSlotStatus.Corrupt));
        }
        [Test]
        public void UnknownProviderPayloadSurvivesLoadAndSave()
        {
            string payload = "{\"futureValue\":17}";
            WriteV1(new Entry { key = "future-system", payload = payload });
            Assert.That(manager.Load(slot, false), Is.True);
            Assert.That(manager.Save(slot), Is.True);
            Envelope saved = JsonUtility.FromJson<Envelope>(File.ReadAllText(PathForSlot));
            Assert.That(Array.Exists(saved.providers, p => p.key == "future-system" && p.payload == payload), Is.True);
        }
        [Test]
        public void RejectedProviderRollsBackEventsAndProviderAndPausesAutosave()
        {
            FakeProvider provider = new FakeProvider { SaveKey = "test-rollback", Value = 7 };
            Register(provider);
            runtime.SetFlag("spell.unlocked.Vis", true);
            string events = runtime.ExportStateJson(false);
            WriteV1(new Entry { key = provider.SaveKey, payload = "{\"value\":99}" });
            provider.RejectValue = 99;
            ExpectFailure(); Assert.That(manager.Load(slot, false), Is.False);
            Assert.That(provider.Value, Is.EqualTo(7));
            Assert.That(runtime.ExportStateJson(false), Is.EqualTo(events));
            Assert.That(manager.AutosaveAllowed, Is.False);
            Assert.That(manager.LastError, Does.Contain("Previous game state restored"));
        }
        [Test]
        public void CallbacksCannotSaveDeleteLoadOrChangeProvidersDuringRestore()
        {
            FakeProvider provider = new FakeProvider { SaveKey = "test-reentrant" };
            Register(provider); WriteV1(new Entry { key = provider.SaveKey, payload = "{\"value\":2}" });
            provider.OnRestore = () =>
            {
                Assert.That(manager.IsBusy, Is.True); Assert.That(manager.AutosaveAllowed, Is.False);
                Assert.That(manager.Save(slot), Is.False); Assert.That(manager.Load(slot, false), Is.False);
                Assert.That(manager.DeleteSave(slot), Is.False); Assert.That(manager.UnregisterProvider(provider), Is.False);
            };
            Assert.That(manager.Load(slot, false), Is.True);
            Assert.That(manager.AutosaveAllowed, Is.True);
        }
        [Test]
        public void RecoveryPreservesDamagedPrimaryAndRestoresBackup()
        {
            runtime.SetFlag("spell.unlocked.Glaciate", true); WriteV1();
            File.Copy(PathForSlot, PathForSlot + ".bak");
            File.WriteAllText(PathForSlot, "{damaged");
            runtime.SetFlag("spell.unlocked.Glaciate", false);
            Assert.That(manager.GetRecoveryCandidates(slot), Is.EqualTo(new[] { SaveRecoverySource.Previous }));
            Assert.That(manager.RecoverSave(slot, SaveRecoverySource.Previous), Is.True);
            Assert.That(runtime.GetFlag("spell.unlocked.Glaciate"), Is.True);
            string[] preserved = Directory.GetFiles(manager.SaveDirectory, slot + ".json.preserved-*");
            Assert.That(preserved.Length, Is.EqualTo(1)); Assert.That(File.ReadAllText(preserved[0]), Is.EqualTo("{damaged"));
            Assert.That(File.Exists(PathForSlot + ".bak"), Is.True);
        }
        [Test]
        public void InterruptedRecoveryFreesTemporaryPathAndAllowsNextSave()
        {
            WriteV1(); File.Move(PathForSlot, PathForSlot + ".tmp");
            Assert.That(manager.RecoverSave(slot, SaveRecoverySource.InterruptedWrite), Is.True);
            Assert.That(File.Exists(PathForSlot + ".tmp"), Is.False);
            Assert.That(Directory.GetFiles(manager.SaveDirectory, slot + ".json.preserved-*").Length, Is.EqualTo(1));
            Assert.That(manager.Save(slot), Is.True);
        }
        [Test]
        public void ExistingInterruptedWriteIsNotDeletedByRefusedSave()
        {
            WriteV1(); File.Move(PathForSlot, PathForSlot + ".tmp"); string bytes = File.ReadAllText(PathForSlot + ".tmp");
            ExpectFailure(); Assert.That(manager.Save(slot), Is.False);
            Assert.That(File.ReadAllText(PathForSlot + ".tmp"), Is.EqualTo(bytes));
        }
        [Test]
        public void RejectedRecoveryNeverPromotesCandidate()
        {
            FakeProvider provider = new FakeProvider { SaveKey = "test-recovery", Value = 8, RejectValue = 99 }; Register(provider);
            WriteV1(new Entry { key = provider.SaveKey, payload = "{\"value\":99}" }); File.Move(PathForSlot, PathForSlot + ".bak");
            File.WriteAllText(PathForSlot, "{damaged");
            ExpectFailure(); Assert.That(manager.RecoverSave(slot, SaveRecoverySource.Previous), Is.False);
            Assert.That(File.ReadAllText(PathForSlot), Is.EqualTo("{damaged")); Assert.That(provider.Value, Is.EqualTo(8));
            Assert.That(File.Exists(PathForSlot + ".bak"), Is.True); Assert.That(manager.AutosaveAllowed, Is.False);
        }
        [Test]
        public void RequiredGameplayProviderAndMapAgreementAreChecked()
        {
            FakeProvider loader = new FakeProvider { SaveKey = "data-driven-world" }; Register(loader);
            WriteV1(); Assert.That(manager.InspectSlot(slot).Message, Does.Contain("missing"));
            WriteV1(new Entry { key = "data-driven-world", payload = "{\"mapId\":\"campus\"}" }, new Entry { key = "spell-world-state", payload = "{\"mapId\":\"forest\"}" });
            Assert.That(manager.InspectSlot(slot).Message, Does.Contain("disagree"));
        }
        [Test]
        public void PhasedProvidersFinalizeAfterWorldAndUseSameOrderForRollback()
        {
            List<string> trace = new List<string>();
            PhasedProvider map = new PhasedProvider(trace); FakeProvider world = new FakeProvider { SaveKey = "spell-world-state", OnRestore = () => trace.Add("world") };
            Register(map); Register(world);
            WriteV1(new Entry { key = map.SaveKey, payload = "{\"mapId\":\"test\",\"value\":2}" }, new Entry { key = world.SaveKey, payload = "{\"mapId\":\"test\",\"value\":2}" });
            Assert.That(manager.Load(slot, false), Is.True); Assert.That(trace, Is.EqualTo(new[] { "map", "world", "player" }));
            trace.Clear(); map.Value = 1; world.Value = 1; map.RejectValue = 2; ExpectFailure(); Assert.That(manager.Load(slot, false), Is.False);
            Assert.That(trace, Is.EqualTo(new[] { "map", "world", "player", "map", "world", "player" }));
        }
        [Test]
        public void ProviderInstanceCannotUnregisterReplacementAndResetClearsUnknownState()
        {
            FakeProvider first = new FakeProvider { SaveKey = "test-owner" }, next = new FakeProvider { SaveKey = "test-owner" };
            Register(first); Assert.That(manager.RegisterProvider(first), Is.True); Assert.That(manager.RegisterProvider(next), Is.False);
            Assert.That(manager.UnregisterProvider(first), Is.True); Register(next); Assert.That(manager.UnregisterProvider(first), Is.False);
            WriteV1(new Entry { key = "old-unknown", payload = "{}" }); Assert.That(manager.Load(slot, false), Is.True);
            Assert.That(manager.ResetState(() => Assert.That(manager.Save(slot), Is.False)), Is.True);
            Assert.That(manager.AutosaveAllowed, Is.False); Assert.That(manager.Save(slot), Is.True);
            Assert.That(Array.Exists(JsonUtility.FromJson<Envelope>(File.ReadAllText(PathForSlot)).providers, p => p.key == "old-unknown"), Is.False);
        }
        [Serializable] private sealed class Envelope { public int schemaVersion; public string savedAtUtc; public string eventStateJson; public Entry[] providers = Array.Empty<Entry>(); }
        [Serializable] private sealed class Entry { public string key; public string payload; }
        [Serializable] private sealed class ValueState { public int value; }
        private class FakeProvider : ISaveStateProvider
        {
            public string SaveKey { get; set; } public int Value; public int RejectValue = int.MinValue; public Action OnRestore;
            public string CaptureStateJson() => JsonUtility.ToJson(new ValueState { value = Value });
            public virtual bool RestoreStateJson(string json) { OnRestore?.Invoke(); Value = JsonUtility.FromJson<ValueState>(json).value; return Value != RejectValue; }
        }
        private sealed class PhasedProvider : FakeProvider, ISaveStateRestorePhases
        {
            private readonly List<string> trace;
            public PhasedProvider(List<string> trace) { this.trace = trace; SaveKey = "data-driven-world"; Value = 1; }
            public bool PrepareRestoreStateJson(string json) { trace.Add("map"); return true; }
            public override bool RestoreStateJson(string json) { trace.Add("player"); return base.RestoreStateJson(json); }
        }
    }
}
