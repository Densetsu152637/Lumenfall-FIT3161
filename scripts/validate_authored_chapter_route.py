"""Validate the live authored Chapter 1 route without starting Unity (stdlib only)."""
from pathlib import Path
from collections import deque
import argparse, re

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'Lumenfall'
AREAS = PROJECT / 'Assets/_Project/World/Areas'
RESERVED = {'B01.Ground': {'S04','S05','S06'}, 'B02.Ground': {'W05','W08','W09','W10','W11'},
            'B03.Ground': {'N03','N05','N08','N09','N10','N11','N12'},
            'B04.Ground': {'D05','D06','D09','D10','D11','D12','D13','D14'}, 'westForest': {'F06'}}

def read(path): return path.read_text(encoding='utf-8-sig')
def field(body, key, default=''):
    match = re.search(r'^  '+re.escape(key)+r':\s*([^\n]*)', body, re.M)
    return match[1].strip() if match else default

def vector(value):
    return {name:float(number) for name,number in re.findall(r'(\w+):\s*(-?[\d.eE+]+)', value)}
def ref(value):
    match = re.search(r'guid: ([a-f0-9]{32})', value)
    return match[1] if match else None

def documents(text):
    matches = list(re.finditer(r'^--- !u!(\d+) &(\d+).*\n', text, re.M))
    return {match[2]:(int(match[1]),text[match.end():matches[i+1].start() if i+1<len(matches) else len(text)])
            for i,match in enumerate(matches)}

def rectangle(x,y,width,height):
    return {(x+dx,y+dy) for dx in range(width) for dy in range(height)}

def reachable(start, goal, size, blocked):
    visited={start}; pending=deque([start])
    while pending:
        cell=pending.popleft()
        if cell == goal:return True
        for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)):
            nextcell=(cell[0]+dx,cell[1]+dy)
            if nextcell not in visited and nextcell not in blocked and 0<=nextcell[0]<size[0] and 0<=nextcell[1]<size[1]:
                visited.add(nextcell);pending.append(nextcell)
    return False

def validate(root=ROOT):
    areas=root/'Lumenfall/Assets/_Project/World/Areas'; errors=[]; maps={}; portals={}; portal_paths={}
    for path in (areas/'Portals').glob('*.asset'):
        body=next(iter(documents(read(path)).values()))[1]
        content={key:field(body,key) for key in ('id','area','cell','footprint','reserved','destination','arrivalOffset')}
        guid=ref('guid: '+re.search(r'guid: (\w+)',read(Path(str(path)+'.meta')))[1])
        portals[guid]=content;portal_paths[guid]=path
    for path in areas.glob('*.asset'):
        text=read(path)
        match=re.search(r'^    id: (.+)$',text,re.M)
        if not match:continue
        mapid=match[1]; guid=re.search(r'guid: (\w+)',read(Path(str(path)+'.meta')))[1]
        width=int(re.search(r'^    width: (\d+)',text,re.M)[1]);height=int(re.search(r'^    height: (\d+)',text,re.M)[1])
        prefab=areas/'Prefabs'/(mapid+'.prefab')
        if not prefab.exists():continue
        docs=documents(read(prefab)); transforms={}; live={}; blocked=set()
        for fileid,(tag,body) in docs.items():
            if tag==4:transforms[field(body,'m_GameObject')]=body
        # GridObstacle anchors are transform-based. This static check follows parent translations;
        # rotated/scaled imported nested prefabs require Play Mode acceptance and are reported separately.
        transform_by_id={fileid:body for fileid,(tag,body) in docs.items() if tag==4}
        def position(body):
            pos=vector(field(body,'m_LocalPosition')); match=re.search(r'fileID: (\d+)',field(body,'m_Father'));parent=match[1] if match else '0'
            if parent in transform_by_id:
                base=position(transform_by_id[parent]);pos={axis:pos.get(axis,0)+base.get(axis,0) for axis in ('x','y','z')}
            return pos
        for fileid,(tag,body) in docs.items():
            if tag!=114:continue
            kind=field(body,'m_EditorClassIdentifier')
            if field(body,'m_Enabled','1')=='0':continue
            if kind.endswith('GridObstacle'):
                anchor=vector(field(body,'anchorCell')); offset=vector(field(body,'footprintOffset')); footprint=vector(field(body,'footprintSize'))
                if field(body,'useTransformAsAnchor')=='1':
                    tr=transforms.get(field(body,'m_GameObject'));pos=position(tr) if tr else {}
                    anchor={'x':round(pos.get('x',0)),'y':round(pos.get('z',0))}
                blocked |= rectangle(int(anchor.get('x',0)+offset.get('x',0)),int(anchor.get('y',0)+offset.get('y',0)),int(footprint.get('x',1)),int(footprint.get('y',1)))
            if kind.endswith('GridPortal'):
                content_guid=ref(field(body,'content'))
                if content_guid not in portals:errors.append(f'{mapid}: live portal has missing content {content_guid}');continue
                content=portals[content_guid];live[content_guid]=content
                if vector(field(body,'anchorCell'))!=vector(content['cell']):errors.append(f'{mapid}/{content["id"]}: prefab/content cell mismatch')
                if vector(field(body,'footprintSize'))!=vector(content['footprint']):errors.append(f'{mapid}/{content["id"]}: prefab/content footprint mismatch')
                if ref(content['area'])!=guid:errors.append(f'{mapid}/{content["id"]}: content belongs to another area')
        region_blocks=re.findall(r'    - id: ([^\n]+)\n      name: ([^\n]+)\n      rect:\n        x: (\d+)\n        y: (\d+)\n        width: (\d+)\n        height: (\d+)\n      walkable: ([01])',text)
        closed={rid for rid,name,x,y,w,h,walkable in region_blocks if walkable=='0'}
        for rid in RESERVED.get(mapid,set())-closed:errors.append(f'{mapid}: reserved region {rid} remains walkable')
        for rid,name,x,y,w,h,walkable in region_blocks:
            if walkable=='0':blocked |= rectangle(int(x),int(y),int(w),int(h))
        if RESERVED.get(mapid) and not any(field(body,'m_EditorClassIdentifier').endswith('ReservedAreaRegions') for tag,body in docs.values() if tag==114):
            errors.append(f'{mapid}: no live reserved-region enforcement')
        for content in live.values():
            if content['reserved']=='0':
                c=vector(content['cell']);f=vector(content['footprint']);blocked -= rectangle(int(c['x']),int(c['y']),int(f['x']),int(f['y']))
        spawn=re.search(r'    spawn:\n      x: (\d+)\n      y: (\d+)',text)
        spawn=(int(spawn[1]),int(spawn[2])) if spawn else None
        maps[guid]={'id':mapid,'size':(width,height),'live':live,'blocked':blocked,'spawn':spawn}
    count=0;edges=set()
    for area in maps.values():
        spawn=area['spawn'];size=area['size']
        if spawn is None or spawn in area['blocked'] or not(0<=spawn[0]<size[0] and 0<=spawn[1]<size[1]):
            errors.append(f'{area["id"]}: blocked/out-of-bounds authored spawn {spawn}')
        elif area['id']=='westForest':
            for portal in area['live'].values():
                if portal['reserved']=='0':
                    cell=vector(portal['cell']);goal=(int(cell['x']),int(cell['y']))
                    if not reachable(spawn,goal,size,area['blocked']):errors.append(f'westForest: starting spawn cannot reach {portal["id"]}')
    for area_guid,area in maps.items():
        for guid,portal in area['live'].items():
            if portal['reserved']=='1':continue
            destguid=ref(portal['destination']);dest=portals.get(destguid)
            if dest is None:errors.append(f'{area["id"]}/{portal["id"]}: missing destination');continue
            target=maps.get(ref(dest['area']))
            if target is None or destguid not in target['live']:errors.append(f'{area["id"]}/{portal["id"]}: destination not live');continue
            if dest['reserved']=='1' or ref(dest['destination'])!=guid:errors.append(f'{area["id"]}/{portal["id"]}: destination reserved or nonreciprocal');continue
            sourcecell=vector(portal['cell']);sourcefoot=vector(portal['footprint']);destcell=vector(dest['cell']);destfoot=vector(dest['footprint']);offset=vector(portal['arrivalOffset'])
            for dx in range(int(sourcefoot['x'])):
                for dy in range(int(sourcefoot['y'])):
                    arrival=(int(destcell['x']+min(dx,destfoot['x']-1)+offset['x']),int(destcell['y']+min(dy,destfoot['y']-1)+offset['y']))
                    goal=(int(destcell['x']),int(destcell['y']))
                    if arrival in target['blocked'] or not(0<=arrival[0]<target['size'][0] and 0<=arrival[1]<target['size'][1]):errors.append(f'{area["id"]}/{portal["id"]}: blocked/out-of-bounds arrival {arrival}')
                    elif not reachable(arrival,goal,target['size'],target['blocked']):errors.append(f'{area["id"]}/{portal["id"]}: arrival cannot reach return portal')
            edges.add((area['id'],target['id']));count+=1
    for target in ('westForest','B01.Ground','B02.Ground','B04.Ground'):
        for edge in (('campus',target),(target,'campus')):
            if edge not in edges:errors.append(f'Required route missing: {edge[0]} -> {edge[1]}')
    return errors,len(maps),count


def validate_scene(root=ROOT):
    scene=root/'Lumenfall/Assets/_Project/Scenes/SampleScene.unity'
    errors=[]
    if not scene.exists():return ['Production SampleScene is missing']
    text=read(scene);docs=documents(text)
    document_count=len(re.findall(r'^--- !u!',text,re.M))
    if len(docs)!=document_count:errors.append('SampleScene has duplicate serialized file IDs')
    bodies=[body for tag,body in docs.values() if tag==114]
    for name in ('GridWorld','GridMover','GridPlayerInput','CameraFollow2_5D','DataDrivenWorldLoader','DialogueOverlay','SpellCaster','StartupMenu'):
        count=sum(field(body,'m_EditorClassIdentifier').split('.')[-1].split('::')[-1]==name for body in bodies)
        if count!=1:errors.append(f'SampleScene requires exactly one {name}, found {count}')
    loader=next((body for body in bodies if field(body,'m_EditorClassIdentifier').endswith('DataDrivenWorldLoader')),None)
    if loader is not None:
        area_guid=ref(field(loader,'startingArea'))
        start=root/'Lumenfall/Assets/_Project/World/Areas/westForest.asset.meta'
        if not start.exists() or area_guid!=re.search(r'guid: (\w+)',read(start))[1]:errors.append('Production starting area is not the authored westForest')
        for name in ('world','contentRoot'):
            match=re.search(r'fileID: (\d+)',field(loader,name))
            if match is None or match[1]=='0' or match[1] not in docs:errors.append(f'Production loader {name} is not wired to a live scene reference')
    build=root/'Lumenfall/ProjectSettings/EditorBuildSettings.asset'
    if not build.exists() or not re.search(r'- enabled: 1\n    path: Assets/_Project/Scenes/SampleScene.unity',read(build)):
        errors.append('SampleScene is not an enabled build entry')
    return errors

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--root',type=Path,default=ROOT);args=parser.parse_args()
    errors,maps,edges=validate(args.root)
    errors.extend(validate_scene(args.root))
    for error in errors:print('ERROR:',error)
    print(f'Checked {maps} live authored areas, {edges} active reciprocal portal edges and reserved region enforcement.')
    print('Static obstacle translations only; imported nested mesh/terrain collision and Play Mode remain unverified.')
    raise SystemExit(bool(errors))
