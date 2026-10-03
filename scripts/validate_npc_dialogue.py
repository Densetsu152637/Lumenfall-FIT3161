#!/usr/bin/env python3
"""Validate authored quest/dialogue semantics without fixing catalogue cardinalities."""
import argparse
from pathlib import Path
from validate_authored_content import DEFAULT_PROJECT, STATUSES, read_json, validate_dialogues


def validate_npc_dialogue(project):
    resources = project / 'Assets/Resources'
    docs = [read_json(p) for p in sorted((project / 'Assets').rglob('Resources/GameEvents/*.json'))]
    database = read_json(resources / 'NPCs/npcs.json')
    npcs = database.get('npcs', [])
    errors = []
    catalog = validate_dialogues(docs, npcs, errors)
    for qid in catalog['quests']:
        dialogue = catalog['dialogues'].get(qid + '_dialogue')
        if not dialogue:
            continue  # Story/optional quests can use a different authored dialogue graph.
        nodes = {node['id']: node for node in dialogue.get('nodes', [])}
        start = nodes.get(dialogue.get('startNodeId'), {})
        choices = {choice.get('id'): choice for choice in start.get('choices', [])}
        for choice_id, status in (('accept', 'NotStarted'), ('active', 'Active'), ('done', 'Completed')):
            choice = choices.get(choice_id)
            if not choice or not any(c.get('type', '').lower() == 'queststatus' and c.get('key') == qid and str(c.get('value', '')).lower() == status.lower() and not c.get('negate') for c in choice.get('conditions', [])):
                errors.append(f'{qid}: missing {choice_id} branch guarded by {status}')
        if not any(a.get('type', '').lower() == 'startquest' and a.get('key') == qid for a in choices.get('accept', {}).get('actions', [])):
            errors.append(f'{qid}: accept branch does not start its quest')
        event = catalog['events'].get('complete_' + qid)
        if not event or not any(c.get('type', '').lower() == 'queststatus' and c.get('key') == qid and str(c.get('value', '')).lower() == 'active' and not c.get('negate') for c in event.get('conditions', [])):
            errors.append(f'{qid}: completion event lacks Active guard')
        if not event or not any(a.get('type', '').lower() == 'completequest' and a.get('key') == qid for a in event.get('actions', [])):
            errors.append(f'{qid}: completion event does not complete its quest')
    for npc in npcs:
        if not npc.get('layerSymbol'):
            continue
        layers = [layer for layer in database.get('layers', []) if layer.get('mapId') == npc.get('mapId')]
        if len(layers) != 1:
            errors.append(f'{npc["id"]}: expected one NPC layer')
            continue
        rows = (resources / 'NPCs' / layers[0]['tileFile']).read_text(encoding='utf-8-sig').splitlines()
        matches = [(x, y) for y, row in enumerate(rows) for x, glyph in enumerate(row) if glyph == npc['layerSymbol']]
        if not matches:
            errors.append(f'{npc["id"]}: layer symbol absent')
        source = (resources / 'Maps' / (npc['mapId'] + '.txt')).read_text(encoding='utf-8-sig').splitlines()
        for x, y in matches:
            if y >= len(source) or x >= len(source[y]) or source[y][x] in 'WBT':
                errors.append(f'{npc["id"]}: layer marker on blocked/out-of-bounds source cell {x},{y}')
    return errors, catalog


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('project', nargs='?', type=Path, default=DEFAULT_PROJECT)
    args = parser.parse_args(argv)
    try:
        errors, catalog = validate_npc_dialogue(args.project.resolve())
    except (OSError, ValueError, KeyError, TypeError) as exc:
        errors, catalog = [f'Malformed or missing dialogue content: {exc}'], {}
    for error in errors:
        print('FAIL: ' + error)
    if catalog:
        print('Validated ' + ', '.join(f'{len(catalog[kind])} {kind}' for kind in ('quests', 'dialogues', 'events')))
    return int(bool(errors))


if __name__ == '__main__':
    raise SystemExit(main())
