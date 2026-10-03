#!/usr/bin/env python3
"""Validate runtime-authoritative world content and source/baked parity, without Unity."""
from __future__ import annotations
import argparse
import json
import math
import re
from pathlib import Path
import yaml

DEFAULT_PROJECT = Path(__file__).resolve().parents[1] / 'Lumenfall'
OBJECT_DEFAULTS = dict(type='building', width=1, height=1, elevation=0, rotation=0, blocks=True, tags=[], compatibleWithSpells=True)
TARGET_DEFAULTS = dict(objectIds=[], requiredConnections=[], conditionMode='All', requiredPowered=False, requiredConnected=False, requiredRevealed=False, requiredFrozen=False, requiredAligned=False, requiredRotation=-1, requiredPositionX=-2147483648, requiredPositionY=-2147483648)
NPC_DEFAULTS = dict(displayName='', x=0, y=0, layerSymbol='', showWhenQuestCompleted='', hideWhenQuestCompleted='', dialogueIds=[], dialogue=[])
DIALOGUE_DEFAULTS = dict(questId='', requiredQuestStatus='', prerequisiteQuestId='', prerequisiteQuestStatus='', weight=1)
STATUSES = {'NotStarted', 'Active', 'Completed'}
STATUS_KEYS = {status.lower() for status in STATUSES}


def read_json(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def unity_documents(path, selected=None):
    # Strip Unity tags, then parse selected complete documents with SafeLoader.
    text = path.read_text(encoding='utf-8-sig')
    result = []
    for header, body in re.findall(r'^--- !u!(\d+) &[^\n]+\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S):
        if selected and not any(name in body for name in selected):
            continue
        doc = yaml.safe_load(body)
        if isinstance(doc, dict):
            result.append(next(iter(doc.values())))
    return result


def normalize(record, defaults):
    result = {key: record.get(key, value) for key, value in defaults.items()}
    result.update(record)
    for key, value in result.items():
        if value is None:
            result[key] = ''
    if 'dialogue' in result:
        result['dialogue'] = [normalize(d, DIALOGUE_DEFAULTS) for d in result['dialogue']]
    return result


def index(records, context, errors, key='id'):
    result = {}
    for record in records:
        identity = record.get(key)
        if not identity:
            errors.append(f'{context}: missing {key}')
        elif identity in result:
            errors.append(f'{context}: duplicate {key} {identity}')
        else:
            result[identity] = record
    return result


def parity(source, baked, fields, context, errors, defaults=None):
    source = normalize(source, defaults or {})
    baked = normalize(baked, defaults or {})
    for key in fields:
        if source.get(key) != baked.get(key):
            errors.append(f'{context}: source/baked {key} mismatch ({source.get(key)!r} != {baked.get(key)!r})')


def validate_dialogues(docs, npcs, errors):
    catalog = {kind: index([r for doc in docs for r in doc.get(kind, [])], kind, errors) for kind in ('quests', 'dialogues', 'events')}
    def reference(kind, value, context):
        if value and value not in catalog[kind]:
            errors.append(f'{context}: unknown {kind} reference {value}')
    def conditions(items, context):
        for condition in items:
            kind = condition.get('type', '').lower()
            if kind == 'queststatus':
                reference('quests', condition.get('key'), context)
                if str(condition.get('value', '')).lower() not in STATUS_KEYS:
                    errors.append(f'{context}: invalid QuestStatus {condition.get("value")}')
            elif kind == 'eventtriggered':
                reference('events', condition.get('key'), context)
    def actions(items, context):
        for action in items:
            kind = action.get('type', '').lower()
            target = {'startquest': 'quests', 'completequest': 'quests', 'startdialogue': 'dialogues', 'triggerevent': 'events'}.get(kind)
            if target:
                if not action.get('key'):
                    errors.append(f'{context}: {kind} needs a key')
                reference(target, action.get('key'), context)
    for qid, quest in catalog['quests'].items():
        for key in ('onStartedEventId', 'onCompletedEventId'):
            reference('events', quest.get(key), qid)
        metadata = {m.get('key'): m.get('value', '') for m in quest.get('metadata', [])}
        if str(metadata.get('optional', '')).lower() == 'true' and str(metadata.get('enabledByDefault', 'false')).lower() != 'false':
            errors.append(f'{qid}: optional content must be disabled by default')
        for prerequisite in re.split('[,;]', metadata.get('requiresQuest', '')):
            if prerequisite:
                required_id = prerequisite.split(':')[0]
                reference('quests', required_id, qid)
                required = catalog['quests'].get(required_id, {})
                required_metadata = {m.get('key'): m.get('value', '') for m in required.get('metadata', [])}
                if str(required_metadata.get('optional', '')).lower() == 'true' and str(metadata.get('optional', '')).lower() != 'true':
                    errors.append(f'{qid}: mandatory quest requires optional quest {required_id}')
    for eid, event in catalog['events'].items():
        conditions(event.get('conditions', []), eid)
        actions(event.get('actions', []), eid)
    for did, dialogue in catalog['dialogues'].items():
        nodes = index(dialogue.get('nodes', []), did + '/nodes', errors)
        if dialogue.get('startNodeId') not in nodes:
            errors.append(f'{did}: missing start node {dialogue.get("startNodeId")}')
        metadata = {m.get('key'): m.get('value', '') for m in dialogue.get('metadata', [])}
        lesson_quest = metadata.get('spellRewardQuestId')
        if lesson_quest:
            reference('quests', lesson_quest, did)
            if not metadata.get('spellReward'):
                errors.append(f'{did}: spellRewardQuestId requires spellReward')
        optional = str(metadata.get('optional', '')).lower() == 'true'
        if optional and str(metadata.get('enabledByDefault', 'false')).lower() != 'false':
            errors.append(f'{did}: optional content must be disabled by default')
        for nid, node in nodes.items():
            entries = [node] + node.get('choices', [])
            index(node.get('choices', []), f'{did}/{nid}/choices', errors)
            for entry in entries:
                if entry.get('nextNodeId') and entry['nextNodeId'] not in nodes:
                    errors.append(f'{did}/{nid}: missing next node {entry["nextNodeId"]}')
                conditions(entry.get('conditions', []), did)
                actions(entry.get('actions', []) + entry.get('onEnterActions', []), did)
                if metadata.get('ambient') and (entry.get('actions') or entry.get('onEnterActions')):
                    errors.append(f'{did}: ambient dialogue has progression actions')
        if metadata.get('spellReward') and not lesson_quest:
            errors.append(f'{did}: spell teaching reward lacks spellRewardQuestId Active gate')
    for npc in npcs:
        context = 'NPC ' + str(npc.get('id'))
        for key in ('showWhenQuestCompleted', 'hideWhenQuestCompleted'):
            reference('quests', npc.get(key), context)
        for did in npc.get('dialogueIds', []):
            reference('dialogues', did, context)
        for entry in npc.get('dialogue', []):
            if not entry.get('dialogueId'):
                errors.append(f'{context}: missing dialogueId')
            reference('dialogues', entry.get('dialogueId'), context)
            for prefix in ('', 'prerequisite'):
                qkey, statuskey = ('questId', 'requiredQuestStatus') if not prefix else ('prerequisiteQuestId', 'prerequisiteQuestStatus')
                reference('quests', entry.get(qkey), context)
                if entry.get(qkey) and str(entry.get(statuskey, '')).lower() not in STATUS_KEYS:
                    errors.append(f'{context}: invalid {statuskey} {entry.get(statuskey)}')
            if entry.get('weight', 1) <= 0:
                errors.append(f'{context}: dialogue weight must be positive')
    return catalog


def prefab_transforms(prefab, components):
    text = prefab.read_text(encoding='utf-8-sig')
    raw = {int(identity): (int(kind), body) for kind, identity, body in re.findall(r'^--- !u!(\d+) &(\d+)[^\n]*\n(.*?)(?=^--- !u!|\Z)', text, re.M | re.S)}
    owners = {c.get('m_GameObject', {}).get('fileID') for c in components}
    result = {}
    for kind, body in raw.values():
        if kind != 4:
            continue
        match = re.search(r'm_GameObject:\s*\{?\s*fileID:\s*(\d+)', body)
        owner = int(match[1]) if match else None
        if owner in owners:
            result[owner] = yaml.safe_load(body)['Transform']
    for owner in owners - result.keys():
        if owner not in raw:
            continue
        gameobject = yaml.safe_load(raw[owner][1]).get('GameObject', {})
        instance = gameobject.get('m_PrefabInstance', {}).get('fileID')
        if instance not in raw:
            continue
        doc = yaml.safe_load(raw[instance][1]).get('PrefabInstance', {})
        transform = dict(m_LocalPosition={}, m_LocalRotation={})
        for modification in doc.get('m_Modification', {}).get('m_Modifications', []):
            prop = modification.get('propertyPath', '')
            for key in ('m_LocalPosition', 'm_LocalRotation'):
                if prop.startswith(key + '.'):
                    transform[key][prop.rsplit('.', 1)[1]] = float(modification['value'])
        if transform['m_LocalPosition']:
            result[owner] = transform
    return result


def check_prefab_transforms(prefab, components, objects, errors, mid):
    components = list(components)
    transforms = prefab_transforms(prefab, components)
    found = set()
    for component in components:
        oid = component.get('objectId') or component.get('definition', {}).get('id')
        if oid not in objects:
            continue
        transform = transforms.get(component.get('m_GameObject', {}).get('fileID'))
        if transform is None:
            continue
        obj = objects[oid]
        position = transform.get('m_LocalPosition', {})
        expected = dict(x=obj['x'], y=obj.get('elevation', 0), z=obj['y'])
        if any(abs(position.get(axis, float('inf')) - value) > 0.001 for axis, value in expected.items()):
            errors.append(f'{mid}/{oid}: prefab Transform position differs from authored cell/elevation')
        rotation = transform.get('m_LocalRotation', {})
        half = math.radians(obj.get('rotation', 0)) / 2
        quaternion = dict(x=0, y=math.sin(half), z=0, w=math.cos(half))
        if not any(all(abs(rotation.get(axis, float('inf')) - sign * value) <= 0.001 for axis, value in quaternion.items()) for sign in (1, -1)):
            errors.append(f'{mid}/{oid}: prefab Transform rotation differs from authored yaw')
        found.add(oid)
    for oid in set(objects) - found:
        errors.append(f'{mid}/{oid}: prefab registration Transform missing')


def validate_content(project: Path):
    errors = []
    assets = project / 'Assets'
    resources = assets / 'Resources'
    areas_dir = assets / '_Project/World/Areas'
    if not areas_dir.is_dir():
        return ['Missing runtime World/Areas directory']
    guids = {}
    for meta in assets.rglob('*.meta'):
        match = re.search(r'^guid:\s*(\w{32})', meta.read_text(encoding='utf-8-sig'), re.M)
        if match:
            guids[match[1]] = meta.with_suffix('')
    def resolve(ref, context, required=False):
        guid = (ref or {}).get('guid')
        if guid and guid in guids:
            return guids[guid]
        if guid or required:
            errors.append(f'{context}: unresolved asset GUID {guid}')
        return None
    areas = {}
    area_assets = {}
    for path in sorted(areas_dir.glob('*.asset')):
        doc = unity_documents(path)[0]
        if 'definition' in doc:
            mid = doc['definition'].get('id')
            if mid in areas:
                errors.append(f'duplicate runtime map {mid}')
            areas[mid] = doc
            area_assets[path] = mid
    if not areas:
        errors.append('No runtime WorldArea assets found')
    source_maps = {}
    for path in sorted((resources / 'Maps').glob('*.json')):
        source = read_json(path)
        mid = source.get('id', path.stem)
        if mid in source_maps:
            # map.json is an intentional legacy alias only if its payload agrees.
            if path.stem != mid and path.stem != 'map':
                errors.append(f'duplicate source map {mid}: {path.name}')
            if source != source_maps[mid]:
                errors.append(f'{mid}: divergent duplicate source map alias {path.name}')
            if path.stem == mid:
                source_maps[mid] = source
        else:
            source_maps[mid] = source
    source_npcs = read_json(resources / 'NPCs/npcs.json').get('npcs', [])
    npc_doc = unity_documents(areas_dir / 'NpcCatalog.asset')[0]
    baked_npcs = npc_doc.get('npcs', [])
    npc_source = index(source_npcs, 'source NPCs', errors)
    npc_baked = index(baked_npcs, 'NpcCatalog', errors)
    for nid in npc_source.keys() | npc_baked.keys():
        if nid not in npc_source or nid not in npc_baked:
            errors.append(f'NPC {nid}: source/catalog entry missing')
            continue
        fields = sorted((set(npc_source[nid]) | set(npc_baked[nid]) | set(NPC_DEFAULTS)) - {'asset', 'prefab'})
        parity(npc_source[nid], npc_baked[nid], fields, f'NPC {nid}', errors, NPC_DEFAULTS)
        prefab = resolve(npc_baked[nid].get('prefab'), f'NPC {nid}/prefab', True)
        asset = npc_source[nid].get('asset')
        if asset and prefab != (resources / asset).with_suffix('.prefab'):
            errors.append(f'NPC {nid}: source resource and catalog prefab mismatch')
    for layer in read_json(resources / 'NPCs/npcs.json').get('layers', []):
        baked_layers = [entry for entry in npc_doc.get('layers', []) if entry.get('mapId') == layer.get('mapId')]
        if len(baked_layers) != 1:
            errors.append(f'NPC layer {layer.get("mapId")}: missing/duplicate catalog layer')
            continue
        parity(layer, baked_layers[0], ['mapId', 'tileFile', 'width', 'height'], 'NPC layer ' + layer['mapId'], errors)
        rows = (resources / 'NPCs' / layer['tileFile']).read_text(encoding='utf-8-sig').splitlines()
        baked_rows = [row or '' for row in baked_layers[0].get('rows', [])]
        while baked_rows and baked_rows[-1] == '':
            baked_rows.pop()
        if rows != baked_rows:
            errors.append(f'NPC layer {layer["mapId"]}: source/baked rows mismatch')
    docs = [read_json(p) for p in sorted(assets.rglob('Resources/GameEvents/*.json'))]
    catalog = validate_dialogues(docs, baked_npcs, errors)
    objects_by_map = {}
    live_portal_paths = set()
    tile_rows = {}
    def cell_valid(mid, x, y, context, width=1, height=1, blocked=False):
        definition = areas[mid]['definition']
        if width < 1 or height < 1 or x < 0 or y < 0 or x + width > definition.get('width', 0) or y + height > definition.get('height', 0):
            errors.append(f'{context}: cell/footprint outside runtime map {mid} ({x},{y}, {width}x{height})')
            return
        if blocked:
            rows = tile_rows.get(mid, [])
            if any(y1 < len(rows) and x1 < len(rows[y1]) and rows[y1][x1] in 'WBT' for y1 in range(y, y + height) for x1 in range(x, x + width)):
                errors.append(f'{context}: blocked tile at {x},{y} in {mid}')
            for obj in objects_by_map.get(mid, {}).values():
                if obj.get('blocks', True) and x < obj['x'] + obj.get('width', 1) and x + width > obj['x'] and y < obj['y'] + obj.get('height', 1) and y + height > obj['y']:
                    errors.append(f'{context}: blocked by object {obj["id"]}')
    def targets_valid(targets, mid, context):
        for target in targets:
            if not target.get('objectIds'):
                errors.append(f'{context}: target has no objectIds')
            for oid in target.get('objectIds', []):
                if oid not in objects_by_map[mid]:
                    errors.append(f'{context}: unknown object {oid} in {mid}')
            for connection in target.get('requiredConnections', []):
                endpoints = connection.split('|')
                if len(endpoints) != 2 or endpoints[0] == endpoints[-1] or any(oid not in objects_by_map[mid] for oid in endpoints):
                    errors.append(f'{context}: invalid connection endpoints {connection}')
            if target.get('requiredPositionX', -2147483648) != -2147483648 and target.get('requiredPositionY', -2147483648) != -2147483648:
                cell_valid(mid, target['requiredPositionX'], target['requiredPositionY'], context)
    for mid, area in areas.items():
        definition = area['definition']
        source = source_maps.get(mid)
        if source is None:
            errors.append(f'{mid}: missing source map fixture')
        else:
            parity(source, definition, ['width', 'height', 'tileFile', 'spawn', 'regions'], mid, errors)
        objects = index(definition.get('objects', []), mid + '/objects', errors)
        objects_by_map[mid] = objects
        for bid in ('B01', 'B02', 'B03', 'B04'):
            building = definition.get(bid) or {}
            if definition.get(bid + 'Prefab', {}).get('guid') and bid in ((source or {}).get('buildingPlacements') or {}):
                resolve(definition[bid + 'Prefab'], f'{mid}/{bid}/prefab', True)
                source_building = ((source or {}).get('buildingPlacements') or {}).get(bid)
                if source_building:
                    parity(source_building, building, ['x', 'y', 'width', 'height'], f'{mid}/{bid}', errors)
        source_objects = index((source or {}).get('objects', []), mid + '/source objects', errors)
        for oid in objects.keys() | source_objects.keys():
            if oid not in objects or oid not in source_objects:
                errors.append(f'{mid}/{oid}: source/baked object missing')
            else:
                parity(source_objects[oid], objects[oid], ['id', 'x', 'y', *OBJECT_DEFAULTS], f'{mid}/{oid}', errors, OBJECT_DEFAULTS)
        source_portals = index((source or {}).get('portals', []), mid + '/source portals', errors)
        baked_portals = index(definition.get('portals', []), mid + '/definition portals', errors)
        for pid in source_portals.keys() | baked_portals.keys():
            if pid not in source_portals or pid not in baked_portals:
                errors.append(f'{mid}/{pid}: source/baked portal missing')
            else:
                parity(source_portals[pid], baked_portals[pid], ['id', 'x', 'y', 'width', 'height', 'reserved'], f'{mid}/{pid}', errors, dict(width=1, height=1, reserved=False))
        targets = index(definition.get('puzzleTargets', []), mid + '/targets', errors)
        src_targets = index((source or {}).get('puzzleTargets', []), mid + '/source targets', errors)
        for tid in targets.keys() | src_targets.keys():
            if tid not in targets or tid not in src_targets:
                errors.append(f'{mid}/{tid}: source/baked puzzle target missing')
            else:
                parity(src_targets[tid], targets[tid], sorted(set(src_targets[tid]) | set(targets[tid]) | set(TARGET_DEFAULTS)), f'{mid}/{tid}', errors, TARGET_DEFAULTS)
        tile = resources / 'Maps' / definition.get('tileFile', '')
        if not tile.is_file():
            errors.append(f'{mid}: missing tile resource {tile.name}')
        else:
            rows = tile.read_text(encoding='utf-8-sig').splitlines()
            tile_rows[mid] = rows
            if len(rows) != definition.get('height') or any(len(row) != definition.get('width') for row in rows):
                errors.append(f'{mid}: tile dimensions differ from runtime map')
            if any(glyph not in '.WBgGpTX' for row in rows for glyph in row):
                errors.append(f'{mid}: unsupported tile glyph')
        for oid, obj in objects.items():
            cell_valid(mid, obj['x'], obj['y'], f'{mid}/{oid}', obj.get('width', 1), obj.get('height', 1))
            if obj.get('prefab', {}).get('guid'):
                resolve(obj['prefab'], f'{mid}/{oid}/prefab')
        spawn = definition.get('spawn') or {}
        cell_valid(mid, spawn.get('x', -1), spawn.get('y', -1), mid + '/spawn', blocked=True)
        targets_valid(targets.values(), mid, mid + '/targets')
        buildings = {}
        for bid in ('B01', 'B02', 'B03', 'B04'):
            if definition.get(bid + 'Prefab', {}).get('guid') and bid in ((source or {}).get('buildingPlacements') or {}):
                buildings[bid] = dict(id=bid, **definition[bid])
                cell_valid(mid, buildings[bid]['x'], buildings[bid]['y'], f'{mid}/{bid}', buildings[bid].get('width', 1), buildings[bid].get('height', 1))
        prefab = resolve(area.get('areaPrefab'), mid + '/areaPrefab', True)
        if not prefab or not prefab.is_file():
            continue
        components = unity_documents(prefab, ('WorldObjectRegistration', 'DynamicNpc', 'GridPortal'))
        registrations = index([c for c in components if 'objectId' in c], mid + '/registrations', errors, 'objectId')
        registered_objects = {**objects, **buildings}
        for oid in registrations.keys() | registered_objects.keys():
            reg, obj = registrations.get(oid), registered_objects.get(oid)
            if reg is None or obj is None:
                errors.append(f'{mid}/{oid}: definition/prefab registration missing')
                continue
            expected = dict(objectId=oid, cell=dict(x=obj['x'], y=obj['y']), size=dict(x=obj.get('width', 1), y=obj.get('height', 1)), spellCompatible=obj.get('compatibleWithSpells', True), tags=obj.get('tags', []))
            parity(expected, reg, expected.keys(), f'{mid}/{oid}/registration', errors)
        check_prefab_transforms(prefab, registrations.values(), registered_objects, errors, mid)
        live_portals = [c for c in components if 'portalId' in c]
        declared = {resolve(ref, mid + '/portal', True) for ref in area.get('portals', [])}
        actual = []
        for component in live_portals:
            path = resolve(component.get('content'), mid + '/GridPortal', True)
            actual.append(path)
            if path:
                live_portal_paths.add(path)
            if path not in declared:
                errors.append(f'{mid}: live GridPortal content absent from WorldArea.portals')
            if path and path.is_file():
                portal = unity_documents(path)[0]
                parity(dict(portalId=portal.get('id'), anchorCell=portal.get('cell'), footprintSize=portal.get('footprint')), component, ['portalId', 'anchorCell', 'footprintSize'], mid + '/GridPortal', errors)
                if portal.get('reserved') and (component.get('destinationMode', 0) != 0 or component.get('destinationPortal', {}).get('fileID', 0) != 0):
                    errors.append(f'{mid}/{portal.get("id")}: reserved live portal has an alternate travel mode')
                if component.get('useTransformAsAnchor') or component.get('footprintOffset') != dict(x=0, y=0):
                    errors.append(f'{mid}/{portal.get("id")}: live portal anchor semantics differ from content')
        for path in declared - set(actual):
            if not path or not path.is_file():
                continue
            portal = unity_documents(path)[0]
            glyph = definition.get('portalGlyph')
            x, y = portal.get('cell', {}).get('x', -1), portal.get('cell', {}).get('y', -1)
            rows = tile_rows.get(mid, [])
            has_glyph = 0 <= y < len(rows) and 0 <= x < len(rows[y]) and (not glyph or rows[y][x] == glyph[0])
            if not portal.get('reserved') and has_glyph:
                errors.append(f'{mid}: eligible portal lacks live GridPortal {path.name}')
        dynamic = index([c['definition'] for c in components if 'definition' in c], mid + '/DynamicNpc', errors)
        expected_npcs = {nid: npc for nid, npc in npc_baked.items() if npc.get('mapId') == mid}
        npc_positions = {}
        for nid, npc in expected_npcs.items():
            position = dict(npc)
            marker = npc.get('layerSymbol')
            shared = sum(n.get('layerSymbol') == marker for n in expected_npcs.values())
            if marker and shared == 1:
                rows = next((layer.get('rows', []) for layer in npc_doc.get('layers', []) if layer.get('mapId') == mid), [])
                matches = [(row.index(marker), y) for y, row in enumerate(rows) if row and marker in row]
                if len(matches) == 1:
                    position['x'], position['y'] = matches[0]
            npc_positions[nid] = position
        check_prefab_transforms(prefab, [c for c in components if 'definition' in c], npc_positions, errors, mid)
        for nid in dynamic.keys() | expected_npcs.keys():
            if nid not in dynamic or nid not in expected_npcs:
                errors.append(f'{mid}/{nid}: prefab/catalog NPC missing')
            else:
                parity(expected_npcs[nid], dynamic[nid], sorted(set(expected_npcs[nid]) | set(dynamic[nid]) | set(NPC_DEFAULTS)), f'{mid}/{nid}/DynamicNpc', errors, NPC_DEFAULTS)
    for npc in baked_npcs:
        mid = npc.get('mapId')
        if mid not in areas:
            errors.append(f'NPC {npc.get("id")}: unavailable runtime map {mid}')
        else:
            cell_valid(mid, npc.get('x', -1), npc.get('y', -1), 'NPC ' + npc['id'], blocked=True)
    portals = {}
    for path in sorted((areas_dir / 'Portals').glob('*.asset')):
        doc = unity_documents(path)[0]
        portals[path] = doc
    for mid, area in areas.items():
        for ref in area.get('portals', []):
            path = resolve(ref, mid + '/portal', True)
            if path not in portals:
                errors.append(f'{mid}: portal reference is not a PortalContentAsset')
                continue
            portal = portals[path]
            owner = resolve(portal.get('area'), path.name + '/area', True)
            if area_assets.get(owner) != mid:
                errors.append(f'{path.name}: portal owner does not match {mid}')
    active_portal_paths = {resolve(ref, mid + '/portal', True) for mid, area in areas.items() for ref in area.get('portals', [])}
    for path, portal in portals.items():
        if path not in live_portal_paths:
            continue
        owner = resolve(portal.get('area'), path.name + '/area', True)
        mid = area_assets.get(owner)
        if mid is None:
            errors.append(f'{path.name}: unavailable runtime area')
            continue
        cell = portal.get('cell', {})
        size = portal.get('footprint', {})
        cell_valid(mid, cell.get('x', -1), cell.get('y', -1), path.name, size.get('x', 1), size.get('y', 1))
        dest = resolve(portal.get('destination'), path.name + '/destination', not portal.get('reserved'))
        if portal.get('reserved'):
            if dest:
                errors.append(f'{path.name}: reserved portal has a destination')
            continue
        if dest not in portals:
            if dest:
                errors.append(f'{path.name}: destination is not a PortalContentAsset')
            continue
        target = portals[dest]
        if dest not in live_portal_paths:
            errors.append(f'{path.name}: destination has no live GridPortal {dest.name}')
        back = resolve(target.get('destination'), dest.name + '/destination')
        # Multi-cell duplicate endpoints may return to a sibling with the same logical id/area.
        reciprocal = portals.get(back, {})
        logical_id = lambda value: re.sub(r'\.\d+_\d+$', '', value or '')
        if logical_id(reciprocal.get('id')) != logical_id(portal.get('id')) or reciprocal.get('area') != portal.get('area'):
            errors.append(f'{path.name}: nonreciprocal runtime portal {dest.name}')
        dest_mid = area_assets.get(resolve(target.get('area'), dest.name + '/area', True))
        if dest_mid:
            offset = portal.get('arrivalOffset') or {}
            target_cell = target.get('cell') or {}
            cell_valid(dest_mid, target_cell.get('x', 0) + offset.get('x', 0), target_cell.get('y', 0) + offset.get('y', 0), path.name + '/arrival', size.get('x', 1), size.get('y', 1), blocked=True)
    puzzles = [p for path in sorted((resources / 'Quests').glob('*.json')) for p in read_json(path).get('puzzles', [])]
    index(puzzles, 'quest monitors', errors)
    for puzzle in puzzles:
        context = 'monitor ' + str(puzzle.get('id'))
        for kind, key in (('quests', 'questId'), ('events', 'completionEventId'), ('dialogues', 'successDialogueId')):
            value = puzzle.get(key)
            if (not value and key != 'successDialogueId') or (value and value not in catalog[kind]):
                errors.append(f'{context}: unknown {key} {value}')
        mid = puzzle.get('mapId')
        if mid not in areas:
            errors.append(f'{context}: unavailable runtime map {mid}')
        else:
            if not puzzle.get('targets'):
                errors.append(f'{context}: monitor has no targets')
            targets_valid(puzzle.get('targets', []), mid, context)
    # A completion reward alone cannot teach the spell needed to solve its own puzzle.
    for qid, quest in catalog['quests'].items():
        metadata = {entry.get('key'): entry.get('value', '') for entry in quest.get('metadata', [])}
        rewards = {v.strip().lower() for v in re.split('[,;]', metadata.get('spellReward', '')) if v.strip()}
        if not rewards:
            continue
        taught = set()
        for dialogue in catalog['dialogues'].values():
            lesson = {entry.get('key'): entry.get('value', '') for entry in dialogue.get('metadata', [])}
            if lesson.get('spellRewardQuestId') == qid:
                taught.update(v.strip().lower() for v in re.split('[,;]', lesson.get('spellReward', '')) if v.strip())
        if rewards - taught:
            errors.append(f'{qid}: completion spell reward lacks Active-gated teaching dialogue for {sorted(rewards - taught)}')
    return errors


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', nargs='?', type=Path, default=DEFAULT_PROJECT)
    args = parser.parse_args(argv)
    try:
        errors = validate_content(args.project.resolve())
    except (OSError, ValueError, TypeError, KeyError, yaml.YAMLError) as exc:
        errors = [f'Malformed or unavailable content: {exc}']
    for error in errors:
        print('FAIL: ' + error)
    print(f'Runtime content validation: {len(errors)} error(s)')
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
