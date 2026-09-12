import json, re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
data = json.loads((root/'docs/maps/academy-layouts.json').read_text(encoding='utf-8'))
source = (root/'docs/location-plan.md').read_text(encoding='utf-8')
doc_ids = set(re.findall(r'\|\s*([A-Z][0-9]{2})\s*\|', source)) | set(re.findall(r'^##\s+(B0[1-4]):', source, re.M))
got = {a['id'] for a in data['areas']}
assert doc_ids == got, f'inventory mismatch missing={doc_ids-got} extra={got-doc_ids}'
assert data['schema'] == 'lumenfall.tile-layout/1'
by = {a['id']: a for a in data['areas']}
for a in data['areas']:
    assert len(a['tiles']) == a['height'] and all(len(r) == a['width'] for r in a['tiles'])
    assert a['tiles'][a['spawn']['y']][a['spawn']['x']] in '.+'
    for p in a['portals']:
        x,y=p['cell']['x'],p['cell']['y']; assert 0<=x<a['width'] and 0<=y<a['height']
        assert a['tiles'][y][x] in '.+'
        assert p['destination']['areaId'] in by
        target=by[p['destination']['areaId']]
        q=next((q for q in target['portals'] if q['destination']['areaId']==a['id']), None)
        assert q, f"missing reciprocal {a['id']} -> {p['destination']['areaId']}"
print(f"validated {len(got)} documented IDs, {sum(len(a['portals']) for a in data['areas'])} reciprocal portals")
