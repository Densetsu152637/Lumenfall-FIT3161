import json,re
from pathlib import Path
root=Path(__file__).resolve().parents[1]; d=root/'docs/maps/layouts'
idx=json.loads((d/'index.json').read_text()); legend=json.loads((d/idx['legendFile']).read_text()); src=(root/'docs/location-plan.md').read_text()
doc=set(re.findall(r'\|\s*([A-Z][0-9]{2})\s*\|',src))|set(re.findall(r'^##\s+(B0[1-4]):',src,re.M)); areas={a['id']:a for a in idx['areas']}; assert doc==set(areas)
maps={}
for k,f in idx['maps'].items():
 m=json.loads((d/f).read_text()); rows=(d/m['tileFile']).read_text().splitlines(); assert len(rows)==m['height'] and all(len(r)==m['width'] for r in rows); m['tiles']=rows; maps[k]=m
for a in areas.values():
 m=maps[a['mapId']]; assert any(r['id']==a['regionId'] for r in m['regions'])
 for p in a['portals']:
  x,y=p['cell']['x'],p['cell']['y']; assert 0<=x<m['width'] and 0<=y<m['height'] and legend[m['tiles'][y][x]]['walkable']; assert p['destination']['mapId'] in maps
assert set(idx['maps'])==set(maps)
# Flood-fill every map from its spawn; every portal endpoint and room representative must be reachable.
for name,m in maps.items():
 walk={(x,y) for y,row in enumerate(m['tiles']) for x,c in enumerate(row) if legend[c]['walkable']}
 start=(m.get('spawn') or {'x':1,'y':1}); start=(start['x'],start['y']); assert start in walk,name
 seen={start}; q=[start]
 while q:
  x,y=q.pop()
  for p in ((x+1,y),(x-1,y),(x,y+1),(x,y-1)):
   if p in walk-seen: seen.add(p); q.append(p)
 assert all(c in seen for a in areas.values() if a['mapId']==name for z in a['portals'] for c in [(z['cell']['x'],z['cell']['y'])]),name
 assert all((r['rect']['x'],r['rect']['y']) in seen for r in m['regions'] if r['walkable']),name
# Exact reciprocal endpoint pairs and map graph reachability.
for a in areas.values():
 for p in a['portals']:
  t=areas[p['destination']['regionId']]; back=next((q for q in t['portals'] if q['destination']['regionId']==a['id']),None); assert back
 graph={k:set() for k in maps}
 for a in areas.values():
  for p in a['portals']: graph[a['mapId']].add(p['destination']['mapId'])
 seen={'eastForest'}; q=['eastForest']
 while q:
  for n in graph[q.pop()]-seen: seen.add(n); q.append(n)
 assert seen==set(maps)
print(f'validated split manifest: {len(areas)} IDs, {len(maps)} maps, floodfill={len(maps)}, graph={len(seen)}')
