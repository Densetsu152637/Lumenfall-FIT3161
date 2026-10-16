using System;
using System.Collections.Generic;
using System.Linq;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class CinnaIceTrialTests
    {
        private const string AreaPath = "Assets/_Project/World/Areas/C03.IceTrial.asset";
        private const string Quest = "cinna_cold_feet_warm_idea";
        private const string Dialogue = "cinna_cold_feet_warm_idea_dialogue";
        private const string Lesson = "spell.lesson.Glaciate";
        private const string Learned = "spell.unlocked.Glaciate";
        // One of the shortest solutions, from the pool entrance: seventeen slides that light all nine switches, in their
        // numbered order, using every snow patch and crystal pillar on the way. Ten routes of this length exist.
        internal static readonly Vector2Int[] SolutionRoute =
        {
            Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left, Vector2Int.up, Vector2Int.right,
            Vector2Int.right, Vector2Int.up, Vector2Int.right, Vector2Int.up, Vector2Int.up, Vector2Int.left,
            Vector2Int.down, Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.right
        };
        private static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        private readonly List<GameObject> objects = new List<GameObject>();
        private WorldStateService state;
        private GameEventRuntime Runtime => GameEventManager.Instance.Runtime;

        [SetUp]
        public void SetUp()
        {
            Runtime.ResetState();
            Create("grid").AddComponent<GridWorld>().ConfigureBounds(12, 14);
            state = Create("state").AddComponent<WorldStateService>();
            EditModeLifecycle.SetWorldInstance(state);
            state.BeginMapRebuild(CinnaIceTrialRoom.MapId);
        }

        [TearDown]
        public void TearDown()
        {
            Runtime.ResetState();
            state.ResetAllState();
            GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = objects.Count - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void AuthoredPoolOpensOntoTheChasmAndTheRouteSolvesIt()
        {
            Layout layout = Layout.Load();
            Assert.That(layout.Switches, Has.Count.EqualTo(9));
            Assert.That(layout.Pillars, Has.Count.EqualTo(3));
            Assert.That(layout.Stops, Has.Count.EqualTo(3));
            foreach (Vector2Int cell in layout.Switches.Keys.Concat(layout.Pillars).Concat(layout.Stops))
                Assert.That(layout.Pool.Contains(cell), Is.True, $"{cell} must sit inside the pool.");
            Assert.That(layout.Final, Is.EqualTo(new Vector2Int(11, 8)), "The seal sits on the east ledge.");
            Assert.That(layout.Entrance, Is.EqualTo(new Vector2Int(5, 2)), "The entrance is the gap in the south wall.");
            // Every pool edge leads to more pool, a pillar or wall, the seal, the entrance or the chasm.
            foreach (Vector2Int cell in layout.Pool)
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = cell + direction;
                    Assert.That(layout.Pool.Contains(next) || layout.Blocked.Contains(next) || layout.Drops.Contains(next) ||
                        next == layout.Final || next == layout.Entrance, Is.True, $"Pool edge {cell} leaks to {next}.");
                }
            for (int y = 3; y <= 12; y++)
            {
                Assert.That(layout.Drops.Contains(new Vector2Int(0, y)), Is.True, $"The west edge is open at row {y}.");
                if (y != 8) Assert.That(layout.Drops.Contains(new Vector2Int(11, y)), Is.True, $"The east edge is open at row {y}.");
            }
            for (int x = 1; x <= 10; x++) Assert.That(layout.Drops.Contains(new Vector2Int(x, 13)), Is.True, $"The north edge is open at column {x}.");

            Vector2Int position = layout.Entrance;
            int mask = 0;
            foreach (Vector2Int direction in SolutionRoute)
            {
                Assert.That(layout.TrySlide(position, direction, out position), Is.True, $"The route is blocked at {position}.");
                Assert.That(layout.Drops.Contains(position), Is.False, $"The route falls into the chasm at {position}.");
                if (layout.Switches.TryGetValue(position, out int bit)) mask ^= 1 << bit;
            }
            Assert.That(position, Is.EqualTo(layout.Final));
            Assert.That(mask, Is.EqualTo((1 << 9) - 1), "The route lands on every switch an odd number of times.");
            Assert.That(layout.ShortestSolution(), Is.EqualTo(SolutionRoute.Length), "No shorter route exists.");
            Vector2Int first;
            Assert.That(layout.TrySlide(layout.Entrance, Vector2Int.up, out first) && layout.Stops.Contains(first), Is.True,
                "The first slide from the entrance stops safely on a snow patch.");
        }

        [Test]
        public void MonitorAreaTargetAndCatalogAgreeOnTheTrial()
        {
            Layout layout = Layout.Load();
            string[] switchIds = layout.Area.definition.objects.Where(o => o.tags.Contains(CinnaIceTrialRoom.SwitchTag)).Select(o => o.id).ToArray();
            QuestPuzzleDefinition monitor = JsonUtility.FromJson<QuestPuzzleDatabase>(Resources.Load<TextAsset>("Quests/quest_puzzle_definitions").text)
                .puzzles.Single(p => p.questId == Quest);
            Assert.That(monitor.mapId, Is.EqualTo(CinnaIceTrialRoom.MapId));
            Assert.That(monitor.targets.All(t => t.requiredPowered), Is.True);
            Assert.That(monitor.targets.SelectMany(t => t.objectIds), Is.EquivalentTo(switchIds.Append(CinnaIceTrialRoom.FinalSwitchId)));
            PuzzleTargetDefinition areaTarget = layout.Area.definition.puzzleTargets.Single();
            Assert.That(areaTarget.requiredPowered, Is.True);
            Assert.That(areaTarget.objectIds, Is.EquivalentTo(switchIds.Append(CinnaIceTrialRoom.FinalSwitchId)));
            MapObjectDefinition relic = layout.Area.definition.objects.Single(o => o.id == CinnaIceTrialRoom.RelicId);
            Assert.That(relic.compatibleWithSpells && relic.tags.Contains("anchored"), Is.True);
            Assert.That(relic.tags.Contains("vis-stand:" + CinnaAlignmentRelic.RelicId), Is.True, "The pedestal is where the carried relic is placed.");

            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            NpcDefinition cinna = catalog.npcs.Single(n => n.id == "cinna_cirra_trial");
            Assert.That(cinna.mapId, Is.EqualTo(CinnaIceTrialRoom.MapId));
            Assert.That(cinna.prefab, Is.Not.Null);
            Assert.That(cinna.prefab.name, Is.EqualTo("Cinna Cirra"), "Cinna is spawned with their own model, like Katrina's.");
            Vector2Int cinnaCell = new Vector2Int(cinna.x, cinna.y);
            Assert.That(layout.Blocked.Contains(cinnaCell) || layout.Pool.Contains(cinnaCell), Is.False, "Cinna stands in the open lobby.");
        }

        [Test]
        public void LessonLendsGlaciateAndOpeningTheSealTeachesIt()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name, out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventRuntime runtime = new GameEventRuntime(new GameEventCatalog(new[] { database }));
            runtime.StartQuest("cinna_out_of_alignment");
            runtime.CompleteQuest("cinna_out_of_alignment");
            DialogueSession session = runtime.StartDialogue(Dialogue);
            session.SelectChoice("accept");
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.GetFlag(Lesson), Is.False, "The lesson is taught only when Cinna finishes speaking.");
            while (!session.IsCompleted) Assert.That(session.Continue(), Is.True);
            Assert.That(runtime.GetFlag(Lesson), Is.True, "Cinna teaches Glaciate for the trial.");
            Assert.That(runtime.GetFlag(Learned), Is.False, "Glaciate is only learned when the seal opens.");
            Assert.That(runtime.TriggerEvent("complete_" + Quest).Succeeded, Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.GetFlag(Learned), Is.True);
        }

        [Test]
        public void GlaciateFreezesTheAnchoredRelicInPlace()
        {
            Register(CinnaIceTrialRoom.RelicId, new Vector2Int(3, 1), true, "glaciate", "anchored");
            state.EndMapRebuild();
            SpellCaster caster = Create("player").AddComponent<SpellCaster>();
            Runtime.SetFlag(Lesson, true);
            Assert.That(caster.Cast(SpellId.Glaciate, CinnaIceTrialRoom.RelicId, Vector2Int.right), Is.True);
            Assert.That(state.TryGetObject(CinnaIceTrialRoom.RelicId, out WorldObjectSnapshot relic), Is.True);
            Assert.That(relic.frozen, Is.True);
            Assert.That(relic.cell, Is.EqualTo(new Vector2Int(3, 1)), "An anchored relic freezes without sliding.");
            Assert.That(caster.Cast(SpellId.Glaciate, CinnaIceTrialRoom.RelicId, Vector2Int.right), Is.True, "A second cast thaws it.");
            state.TryGetObject(CinnaIceTrialRoom.RelicId, out relic);
            Assert.That(relic.frozen, Is.False);
            Assert.That(relic.cell, Is.EqualTo(new Vector2Int(3, 1)));
            Assert.That(caster.Cast(SpellId.Glaciate, CinnaIceTrialRoom.RelicId, Vector2Int.right), Is.True, "And it can be frozen again.");
            state.TryGetObject(CinnaIceTrialRoom.RelicId, out relic);
            Assert.That(relic.frozen, Is.True);
        }

        [Test]
        public void SwitchesToggleOnlyOnFrozenIceAndTheSealNeedsEverySwitch()
        {
            Register(CinnaIceTrialRoom.RelicId, new Vector2Int(3, 1), true, "glaciate", "anchored");
            Register("switch.a", new Vector2Int(2, 12), false, CinnaIceTrialRoom.SwitchTag);
            Register("switch.b", new Vector2Int(8, 12), false, CinnaIceTrialRoom.SwitchTag);
            Register(CinnaIceTrialRoom.FinalSwitchId, new Vector2Int(11, 8), false, CinnaIceTrialRoom.FinalTag);
            state.EndMapRebuild();

            Assert.That(CinnaIceTrialRoom.Land(state, "switch.a"), Is.Null, "Nothing toggles before the pool is frozen.");
            Assert.That(CinnaIceTrialRoom.ResetSwitches(state), Is.False);
            state.SetProperty(CinnaIceTrialRoom.RelicId, x => x.frozen = true);
            Assert.That(CinnaIceTrialRoom.Land(state, "switch.a"), Does.Contain("lit (1 of 2)"));
            Assert.That(CinnaIceTrialRoom.Land(state, "switch.a"), Does.Contain("dimmed"));
            Assert.That(Powered("switch.a"), Is.False, "Landing on a lit switch dims it.");
            CinnaIceTrialRoom.Land(state, "switch.a");
            Assert.That(CinnaIceTrialRoom.Land(state, CinnaIceTrialRoom.FinalSwitchId), Does.Contain("1 of 2"));
            Assert.That(Powered(CinnaIceTrialRoom.FinalSwitchId), Is.False);
            Assert.That(CinnaIceTrialRoom.DescribeStep(state), Does.Contain("1 of 2 lit"));

            CinnaIceTrialRoom.Land(state, "switch.b");
            Assert.That(CinnaIceTrialRoom.DescribeStep(state), Does.Contain("seal"));
            Assert.That(CinnaIceTrialRoom.Land(state, CinnaIceTrialRoom.FinalSwitchId), Does.Contain("blazes open"));
            Assert.That(Powered(CinnaIceTrialRoom.FinalSwitchId), Is.True);
            Assert.That(CinnaIceTrialRoom.Land(state, "switch.b"), Is.Null, "Switches hold once the seal is open.");
            Assert.That(Powered("switch.b"), Is.True);

            Assert.That(CinnaIceTrialRoom.ResetSwitches(state), Is.True);
            Assert.That(Powered("switch.a") || Powered("switch.b") || Powered(CinnaIceTrialRoom.FinalSwitchId), Is.False);
            Assert.That(state.TryGetObject(CinnaIceTrialRoom.RelicId, out WorldObjectSnapshot relic) && relic.frozen, Is.True, "Reset keeps the pool frozen.");
        }

        private bool Powered(string id) => state.TryGetObject(id, out WorldObjectSnapshot item) && item.powered;

        private void Register(string id, Vector2Int cell, bool compatible, params string[] tags)
        {
            state.RegisterObject(id, Create(id), cell, Vector2Int.one, compatible, tags);
        }

        private GameObject Create(string name)
        {
            GameObject item = new GameObject(name);
            objects.Add(item);
            return item;
        }

        /// <summary>The authored trial as a cell model following GridMover's ice rules.</summary>
        private sealed class Layout
        {
            public WorldAreaAsset Area;
            public readonly HashSet<Vector2Int> Pool = new HashSet<Vector2Int>();
            public readonly HashSet<Vector2Int> Blocked = new HashSet<Vector2Int>();
            public readonly Dictionary<Vector2Int, int> Switches = new Dictionary<Vector2Int, int>();
            public readonly HashSet<Vector2Int> Drops = new HashSet<Vector2Int>();
            public readonly HashSet<Vector2Int> Pillars = new HashSet<Vector2Int>();
            public readonly HashSet<Vector2Int> Stops = new HashSet<Vector2Int>();
            public Vector2Int Final;
            public Vector2Int Entrance;

            public static Layout Load()
            {
                Layout layout = new Layout { Area = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(AreaPath) };
                Assert.That(layout.Area, Is.Not.Null, AreaPath);
                foreach (MapObjectDefinition item in layout.Area.definition.objects)
                {
                    Vector2Int cell = new Vector2Int(item.x, item.y);
                    if (item.type == CinnaIceTrialRoom.PoolType)
                    {
                        for (int x = 0; x < item.width; x++)
                            for (int y = 0; y < item.height; y++)
                                layout.Pool.Add(cell + new Vector2Int(x, y));
                    }
                    else if (item.type == CinnaIceTrialRoom.EntranceType) layout.Entrance = cell;
                    else if (item.type == CinnaIceTrialRoom.DropType)
                    {
                        for (int x = 0; x < item.width; x++)
                            for (int y = 0; y < item.height; y++)
                                layout.Drops.Add(cell + new Vector2Int(x, y));
                    }
                    else if (item.type == CinnaIceTrialRoom.StopType) layout.Stops.Add(cell);
                    else if (item.tags.Contains(CinnaIceTrialRoom.SwitchTag)) layout.Switches[cell] = layout.Switches.Count;
                    else if (item.tags.Contains(CinnaIceTrialRoom.FinalTag)) layout.Final = cell;
                    else if (item.blocks)
                    {
                        if (item.type == CinnaIceTrialRoom.PillarType) layout.Pillars.Add(cell);
                        for (int x = 0; x < item.width; x++)
                            for (int y = 0; y < item.height; y++)
                                layout.Blocked.Add(cell + new Vector2Int(x, y));
                    }
                }
                return layout;
            }

            private bool Passable(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Area.definition.width &&
                cell.y < Area.definition.height && !Blocked.Contains(cell);

            private bool Ice(Vector2Int cell) => Pool.Contains(cell) && !Switches.ContainsKey(cell) && !Stops.Contains(cell) && !Blocked.Contains(cell);

            // Matches GridMover: a step that starts or ends on ice keeps going while the mover stands on ice.
            public bool TrySlide(Vector2Int from, Vector2Int direction, out Vector2Int stop)
            {
                stop = from;
                while (Passable(stop + direction))
                {
                    stop += direction;
                    if (!Ice(stop)) break;
                }
                return stop != from;
            }

            public int ShortestSolution()
            {
                int full = (1 << Switches.Count) - 1;
                var distance = new Dictionary<(Vector2Int, int), int> { [(Entrance, 0)] = 0 };
                var queue = new Queue<(Vector2Int, int)>();
                queue.Enqueue((Entrance, 0));
                while (queue.Count > 0)
                {
                    (Vector2Int cell, int mask) = queue.Dequeue();
                    if (cell == Final && mask == full) return distance[(cell, mask)];
                    foreach (Vector2Int direction in Directions)
                    {
                        // A fall returns to the entrance with every switch dark, so it never shortens a route.
                        if (!TrySlide(cell, direction, out Vector2Int stop) || Drops.Contains(stop)) continue;
                        int next = Switches.TryGetValue(stop, out int bit) ? mask ^ (1 << bit) : mask;
                        if (distance.ContainsKey((stop, next))) continue;
                        distance[(stop, next)] = distance[(cell, mask)] + 1;
                        queue.Enqueue((stop, next));
                    }
                }
                return -1;
            }
        }
    }
}
