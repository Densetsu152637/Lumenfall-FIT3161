import copy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import yaml
from validate_authored_content import validate_content, validate_dialogues


class ContentFixture:
    def __init__(self, root):
        self.root = root
        self.assets = root / 'Assets'
        self.areas = self.assets / '_Project/World/Areas'
        self.resources = self.assets / 'Resources'
        self.area_guid, self.prefab_guid, self.portal_guid = ('1' * 32, '2' * 32, '3' * 32)
        self.map = dict(id='tiny', width=4, height=4, tileFile='tiny.txt', spawn=dict(x=0, y=0), regions=[], objects=[dict(id='relic', x=1, y=1, blocks=False)], puzzleTargets=[], portals=[])
        self.area = dict(definition=copy.deepcopy(self.map), areaPrefab=self.ref(self.prefab_guid), portals=[self.ref(self.portal_guid)])
        self.portal = dict(id='loop', area=self.ref(self.area_guid), cell=dict(x=3, y=3), footprint=dict(x=1, y=1), reserved=False, destination=self.ref(self.portal_guid), arrivalOffset=dict(x=0, y=-1))
        self.registration = dict(m_EditorClassIdentifier='WorldObjectRegistration', m_GameObject=dict(fileID=10), objectId='relic', cell=dict(x=1, y=1), size=dict(x=1, y=1), spellCompatible=True, tags=[])
        self.transform = dict(m_GameObject=dict(fileID=10), m_LocalPosition=dict(x=1, y=0, z=1), m_LocalRotation=dict(x=0, y=0, z=0, w=1))
        self.live_portal = dict(m_EditorClassIdentifier='GridPortal', portalId='loop', anchorCell=dict(x=3, y=3), footprintSize=dict(x=1, y=1), footprintOffset=dict(x=0, y=0), useTransformAsAnchor=False, content=self.ref(self.portal_guid))
        self.npcs, self.baked_npcs, self.extra_components = [], [], []
        self.events = dict(quests=[dict(id='quest')], dialogues=[dict(id='talk', startNodeId='a', nodes=[dict(id='a')])], events=[dict(id='complete', conditions=[dict(type='QuestStatus', key='quest', value='Active')], actions=[dict(type='CompleteQuest', key='quest')])])
        self.puzzles = [dict(id='monitor', mapId='tiny', questId='quest', completionEventId='complete', targets=[dict(id='target', objectIds=['relic'], requiredPowered=True)])]
        self.rows = '....\n....\n....\n....\n'
    @staticmethod
    def ref(guid):
        return dict(fileID=11400000, guid=guid, type=2)
    def json(self, path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value), encoding='utf-8')
    def unity(self, path, docs, guid=None):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n' + ''.join(f'--- !u!{kind} &{i+1}\n' + yaml.safe_dump({name: value}, sort_keys=False) for i, (kind, name, value) in enumerate(docs)), encoding='utf-8')
        if guid:
            path.with_name(path.name + '.meta').write_text('guid: ' + guid + '\n', encoding='utf-8')
    def save(self):
        self.json(self.resources / 'Maps/tiny.json', self.map)
        (self.resources / 'Maps/tiny.txt').write_text(self.rows, encoding='utf-8')
        self.json(self.resources / 'NPCs/npcs.json', dict(npcs=self.npcs))
        self.json(self.resources / 'GameEvents/content.json', self.events)
        self.json(self.resources / 'Quests/content.json', dict(puzzles=self.puzzles))
        self.unity(self.areas / 'tiny.asset', [(114, 'MonoBehaviour', self.area)], self.area_guid)
        self.unity(self.areas / 'NpcCatalog.asset', [(114, 'MonoBehaviour', dict(npcs=self.baked_npcs))])
        self.unity(self.areas / 'Portals/loop.asset', [(114, 'MonoBehaviour', self.portal)], self.portal_guid)
        self.unity(self.areas / 'Prefabs/tiny.prefab', [(114, 'MonoBehaviour', self.registration), (4, 'Transform', self.transform), (114, 'MonoBehaviour', self.live_portal)] + self.extra_components, self.prefab_guid)
        return validate_content(self.root)


class AuthoredContentTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.fixture = ContentFixture(Path(self.temp.name))
    def assert_failure(self, fragment):
        errors = self.fixture.save()
        self.assertTrue(any(fragment in error for error in errors), errors)
    def test_valid_minimal_runtime_and_cli_exit(self):
        self.assertEqual([], self.fixture.save())
        script = Path(__file__).resolve().parents[1] / 'validate_authored_content.py'
        result = subprocess.run([sys.executable, str(script), str(self.fixture.root)], capture_output=True, text=True, cwd=self.temp.name)
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.fixture.transform['m_LocalPosition']['x'] = 2
        self.fixture.save()
        result = subprocess.run([sys.executable, str(script), str(self.fixture.root)], capture_output=True, text=True)
        self.assertEqual(1, result.returncode)
        self.assertIn('Transform position', result.stdout)
    def test_baked_metadata_and_transform_mismatches(self):
        self.fixture.registration['tags'] = ['wrong']
        self.assert_failure('registration: source/baked tags mismatch')
        self.fixture.transform['m_LocalRotation']['y'] = 0.5
        self.assert_failure('Transform rotation')
    def test_out_of_bounds_footprint(self):
        self.fixture.map['objects'][0]['width'] = 4
        self.fixture.area['definition']['objects'][0]['width'] = 4
        self.assert_failure('cell/footprint outside')
    def test_blocked_spawn_and_portal_arrival(self):
        self.fixture.rows = 'W...\n....\n...W\n....\n'
        self.assert_failure('spawn: blocked tile')
        self.assert_failure('loop.asset/arrival: blocked tile')
    def test_bad_portal_guid_and_live_anchor(self):
        self.fixture.portal['destination'] = self.fixture.ref('9' * 32)
        self.assert_failure('unresolved asset GUID')
        self.fixture.live_portal['anchorCell']['x'] = 0
        self.assert_failure('GridPortal: source/baked anchorCell')
    def test_missing_live_destination_and_reserved_bypass(self):
        self.fixture.live_portal['content'] = {}
        self.assert_failure('eligible portal lacks live GridPortal')
        self.fixture.live_portal['content'] = self.fixture.ref(self.fixture.portal_guid)
        self.fixture.portal['reserved'] = True
        self.assert_failure('reserved portal has a destination')
    def test_required_monitor_references_and_endpoints(self):
        self.fixture.puzzles[0]['questId'] = ''
        self.fixture.puzzles[0]['targets'][0]['requiredConnections'] = ['relic|absent']
        self.assert_failure('unknown questId')
        self.assert_failure('invalid connection endpoints')
    def test_empty_monitor_targets_and_object_ids(self):
        self.fixture.puzzles[0]['targets'][0]['objectIds'] = []
        self.assert_failure('target has no objectIds')
        self.fixture.puzzles[0]['targets'] = []
        self.assert_failure('monitor has no targets')
    def test_duplicate_ids_and_alias_divergence(self):
        self.fixture.area['definition']['objects'].append(copy.deepcopy(self.fixture.map['objects'][0]))
        self.assert_failure('duplicate id relic')
        self.fixture.save()
        self.fixture.json(self.fixture.resources / 'Maps/map.json', dict(self.fixture.map, width=5))
        errors = validate_content(self.fixture.root)
        self.assertTrue(any('divergent duplicate source map alias' in e for e in errors), errors)
    def test_npc_source_catalog_dynamic_parity(self):
        source = dict(id='friend', mapId='tiny', x=2, y=2, asset='NPCs/friend.prefab', dialogue=[dict(dialogueId='talk')])
        baked = dict(source, prefab=self.fixture.ref('4' * 32))
        baked.pop('asset')
        self.fixture.npcs = [source]
        self.fixture.baked_npcs = [baked]
        self.fixture.unity(self.fixture.resources / 'NPCs/friend.prefab', [(1, 'GameObject', {})], '4' * 32)
        self.fixture.extra_components = [(114, 'MonoBehaviour', dict(m_EditorClassIdentifier='DynamicNpc', definition=dict(baked, x=3)))]
        self.assert_failure('DynamicNpc: source/baked x mismatch')
    def test_additive_dialogue_and_case_insensitive_references(self):
        self.fixture.events['dialogues'].append(dict(id='extra', startNodeId='start', nodes=[dict(id='start', onEnterActions=[dict(type='sTaRtDiAlOgUe', key='talk')])]))
        self.assertEqual([], self.fixture.save())
        self.fixture.events['dialogues'][-1]['nodes'][0]['onEnterActions'][0]['key'] = 'missing'
        self.assert_failure('unknown dialogues reference missing')
    def test_node_links_duplicate_dialogues_and_status(self):
        self.fixture.events['dialogues'][0]['nodes'][0]['nextNodeId'] = 'absent'
        self.fixture.events['dialogues'].append(copy.deepcopy(self.fixture.events['dialogues'][0]))
        self.fixture.events['events'][0]['conditions'][0]['value'] = 'Invalid'
        self.assert_failure('missing next node absent')
        self.assert_failure('duplicate id talk')
        self.assert_failure('invalid QuestStatus')
    def test_lesson_reward_gate_and_optional_default(self):
        self.fixture.events['dialogues'][0]['metadata'] = [dict(key='spellReward', value='Ampere'), dict(key='optional', value='true'), dict(key='enabledByDefault', value='true')]
        self.assert_failure('lacks spellRewardQuestId')
        self.assert_failure('optional content must be disabled')
    def test_completion_reward_cannot_replace_lesson(self):
        self.fixture.events['quests'][0]['metadata'] = [dict(key='spellReward', value='Ampere;Weave')]
        self.assert_failure('completion spell reward lacks Active-gated teaching')
        self.fixture.events['dialogues'][0]['metadata'] = [dict(key='spellReward', value='Ampere,Weave'), dict(key='spellRewardQuestId', value='quest')]
        self.assertEqual([], self.fixture.save())

    def test_additive_target_fields_must_be_baked(self):
        target = dict(id='puzzle', objectIds=['relic'], requiredMinimumPower=0.2)
        self.fixture.map['puzzleTargets'] = [target]
        self.fixture.area['definition']['puzzleTargets'] = [dict(id='puzzle', objectIds=['relic'])]
        self.assert_failure('requiredMinimumPower mismatch')
    def test_npc_layer_trailing_empty_unity_string(self):
        npc = dict(id='friend', mapId='tiny', x=2, y=2, layerSymbol='f', prefab=self.fixture.ref('4' * 32))
        self.fixture.npcs = [dict(id='friend', mapId='tiny', x=2, y=2, layerSymbol='f')]
        self.fixture.baked_npcs = [npc]
        self.fixture.unity(self.fixture.resources / 'NPCs/friend.prefab', [(1, 'GameObject', {})], '4' * 32)
        self.fixture.extra_components = [(114, 'MonoBehaviour', dict(m_EditorClassIdentifier='DynamicNpc', m_GameObject=dict(fileID=20), definition=npc)), (4, 'Transform', dict(m_GameObject=dict(fileID=20), m_LocalPosition=dict(x=2, y=0, z=2), m_LocalRotation=dict(x=0, y=0, z=0, w=1)))]
        self.fixture.save()
        layer = dict(mapId='tiny', tileFile='tiny.npcs.txt', width=4, height=4)
        self.fixture.json(self.fixture.resources / 'NPCs/npcs.json', dict(npcs=self.fixture.npcs, layers=[layer]))
        rows = ['....', '....', '..f.', '....']
        (self.fixture.resources / 'NPCs/tiny.npcs.txt').write_text('\n'.join(rows) + '\n')
        self.fixture.unity(self.fixture.areas / 'NpcCatalog.asset', [(114, 'MonoBehaviour', dict(npcs=[npc], layers=[dict(layer, rows=rows + [None])]))])
        self.assertEqual([], validate_content(self.fixture.root))
        self.fixture.extra_components[-1][2]['m_LocalPosition']['x'] = 1
        self.fixture.unity(self.fixture.areas / 'Prefabs/tiny.prefab', [(114, 'MonoBehaviour', self.fixture.registration), (4, 'Transform', self.fixture.transform), (114, 'MonoBehaviour', self.fixture.live_portal)] + self.fixture.extra_components, self.fixture.prefab_guid)
        self.assertTrue(any('friend: prefab Transform position' in e for e in validate_content(self.fixture.root)))
    def test_lowercase_quest_status_is_runtime_compatible(self):
        self.fixture.events['events'][0]['conditions'][0]['value'] = 'active'
        self.assertEqual([], self.fixture.save())
    def test_optional_quest_cannot_gate_mandatory_quest(self):
        self.fixture.events['quests'] = [dict(id='quest', metadata=[dict(key='requiresQuest', value='optional:Completed')]), dict(id='optional', metadata=[dict(key='optional', value='true')])]
        self.assert_failure('mandatory quest requires optional quest')
    def test_live_portal_requires_live_destination(self):
        self.fixture.save()
        destination = dict(self.fixture.portal, id='return', destination=self.fixture.ref(self.fixture.portal_guid))
        self.fixture.unity(self.fixture.areas / 'Portals/destination.asset', [(114, 'MonoBehaviour', destination)], '5' * 32)
        self.fixture.portal['destination'] = self.fixture.ref('5' * 32)
        self.assert_failure('destination has no live GridPortal')


if __name__ == '__main__':
    unittest.main()
