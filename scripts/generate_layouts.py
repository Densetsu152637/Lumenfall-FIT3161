"""Generate authoritative single-storey academy tile maps."""
from pathlib import Path
import json,re
ROOT=Path(__file__).resolve().parents[1]; SRC=ROOT/'docs/location-plan.md'; OUT=ROOT/'docs/maps'
LEGEND={
 'W':{'name':'wall','walkable':True,'collision':True},
 'g':{'name':'grass','walkable':True,'collision':False},
 'G':{'name':'tall grass','walkable':True,'collision':False},
 'p':{'name':'path','walkable':True,'collision':False},
 'T':{'name':'tree','walkable':True,'collision':True,'footprint':{'width':2,'height':2}},
 'B':{'name':'building','walkable':True,'collision':True,'metaprogrammed':True},
}
def inventory():
 t=SRC.read_text(encoding='utf8'); d={}
 for i,n in re.findall(r'^## (B0[1-4]):\s*([^\n]+)',t,re.M): d[i]={'id':i,'name':n.strip(),'kind':'building','floor':'Ground','access':'Chapter 1'}
 for i,n,p,a in re.findall(r'^\|\s*([A-Z][0-9]{2})\s*\|\s*([^|]+)\|\s*([^|]+)\|[^\n]*?\|\s*(Chapter 1|Optional|Reserved)\s*\|',t,re.M):
  names={'W08':'Teaching support zone','W09':'Seminar zone','W10':'Advanced seminar zone','W11':'Faculty preparation zone','N09':'Administration zone','N10':'Administration landing zone','N11':"Head's office zone",'N12':'Staff meeting zone','D04':'Residential key door zone','D07':'Group living zone','D08':'Player sleeping room','D09':"Stella's sleeping zone",'D10':"Cinna Cirra's sleeping zone",'D11':"Orio's sleeping zone",'D12':"Katrina's sleeping zone",'D13':'Shared washroom zone','D14':'Linen storage zone'}
  d[i]={'id':i,'name':names.get(i,n.strip()),'kind':'outdoor' if i[0] in 'FAC' else 'room','floor':'Ground','access':a}
 return [d[k] for k in sorted(d)]
def blank(w,h,fill='g'): return [['W']*w]+[['W']+[fill]*(w-2)+['W'] for _ in range(h-2)]+[['W']*w]
def region_map(w,h,regions): return {'width':w,'height':h,'tiles':[''.join(x) for x in blank(w,h)],'regions':regions,'spawn':{'x':1,'y':1}}
def make_building(b,ids):
 regs=[{'id':b,'name':b,'rect':{'x':1,'y':1,'width':34,'height':22},'walkable':True}]
 for n,i in enumerate(ids): regs.append({'id':i,'name':i,'rect':{'x':2+(n%4)*8,'y':2+(n//4)*5,'width':6,'height':3},'walkable':True})
 tiles=blank(36,24)
 for x in range(2,34): tiles[3][x]=tiles[8][x]=tiles[13][x]=tiles[18][x]='W'
 for x in range(2,34): tiles[3][x]=tiles[8][x]=tiles[13][x]=tiles[18][x]='W'
 for x in (8,16,24):
  for y in range(3,19): tiles[y][x]='W'
 for x,y in ((8,3),(16,3),(24,3),(8,8),(16,8),(24,8),(8,13),(16,13),(24,13),(8,18),(16,18),(24,18)):
  tiles[y][x]='p'
 return {'width':36,'height':24,'tiles':[''.join(x) for x in tiles],'regions':regs,'spawn':{'x':2,'y':2}}
def build():
 areas=inventory(); by={a['id']:a for a in areas}; placements={'B01':(12,56,20,14),'B02':(10,12,26,20),'B03':(40,8,30,18),'B04':(72,48,20,24)}
 cre=[{'id':i,'name':n,'rect':dict(zip(('x','y','width','height'),r)),'walkable':True} for i,n,r in [('A01','West gate forecourt',(43,35,14,10)),('C01','Campus commons',(30,28,40,28)),('C02','Orientation terrace',(34,18,18,8)),('C03','Orientation trial strip',(15,30,12,8)),('C04','Garden walk',(60,55,18,10)),('C05','Campus path loop',(25,25,50,40))]]
 cre += [{'id':b,'name':b,'rect':dict(zip(('x','y','width','height'),r)),'walkable':True} for b,r in placements.items()]
 campus=region_map(100,80,cre); ct=[list(r) for r in campus['tiles']]; campus['tiles']=ct
 for b,(x,y,w,h) in placements.items():
  for yy in range(y,y+h):
   for xx in range(x,x+w): ct[yy][xx]='B'
  for b,(x,y,w,h) in placements.items():
   for xx in range(max(1,x-1),min(99,x+w+1)): ct[max(1,y-1)][xx]=ct[min(78,y+h)][xx]='p'
   for yy in range(max(1,y-1),min(79,y+h+1)): ct[yy][max(1,x-1)]=ct[yy][min(98,x+w)]='p'
 for x in range(4,96,8):
  for y in (4,72):
   if ct[y][x]=='g' and ct[y+1][x]=='g': ct[y][x]=ct[y+1][x]=ct[y][x+1]=ct[y+1][x+1]='T'
 for x in range(6,96,10):
  for y in range(10,70,12):
   if all(ct[y+dy][x+dx]=='g' for dy in (0,1) for dx in (0,1)): ct[y][x]=ct[y+1][x]=ct[y][x+1]=ct[y+1][x+1]='G'
 for x in range(1,99): ct[40][x]='p'
 for y in range(1,79): ct[y][50]='p'
 for x,y in ((43,40),(50,35),(42,28),(30,34),(60,50),(35,40),(25,56),(23,12),(53,8),(85,48),(20,58),(18,14),(48,10),(80,50)): ct[y][x]='p'
 campus.update({'buildingPlacements':{b:dict(zip(('x','y','width','height'),r)) for b,r in placements.items()},'gateStates':{'north':'locked','south':'locked','west':'locked','east':'open'}})
 forest=region_map(48,30,[{'id':i,'name':by[i]['name'],'rect':dict(zip(('x','y','width','height'),r)),'walkable':True} for i,r in zip(('F01','F02','F03','F04','F05','F06'),[(2,12,8,6),(8,12,24,6),(30,12,12,6),(22,5,10,7),(14,18,12,7),(22,2,8,4)])]); ft=[list(r) for r in forest['tiles']]; forest['tiles']=ft
 for y in range(1,29):
  for x in range(1,47):
   if (x*17+y*31)%13==0 and all(ft[y+dy][x+dx]=='g' for dy in (0,1) for dx in (0,1)): ft[y][x]=ft[y+1][x]=ft[y][x+1]=ft[y+1][x+1]='T'
 for x,y in ((9,15),(8,15),(31,15),(30,15),(20,12),(25,11),(15,17),(18,18),(25,5),(25,4),(41,15)): ft[y][x]='p'
 for y in range(12,19):
  for x in range(2,42):
   if ft[y][x]=='g' and (x+y)%7==0: ft[y][x]='G'
 # Remove any tree component damaged by paths/tall grass overlays; valid trees are exact 2x2 blocks.
 seen=set()
 for y in range(1,29):
  for x in range(1,47):
   if ft[y][x]!='T' or (x,y) in seen: continue
   todo=[(x,y)]; comp=set()
   while todo:
    q=todo.pop()
    if q in comp or ft[q[1]][q[0]]!='T': continue
    comp.add(q); seen.add(q)
    todo.extend((q[0]+dx,q[1]+dy) for dx,dy in ((1,0),(-1,0),(0,1),(0,-1)))
   if len(comp)!=4 or len({q[0] for q in comp})!=2 or len({q[1] for q in comp})!=2:
    for qx,qy in comp: ft[qy][qx]='g'
 # Add a spaced second pass after all overlays so the forest keeps readable tree cover.
 for y in range(2,27,5):
  for x in range(3,45,7):
   if all(ft[y+dy][x+dx]=='g' for dy in (0,1) for dx in (0,1)):
    ft[y][x]=ft[y+1][x]=ft[y][x+1]=ft[y+1][x+1]='T'
 owner={'S':'B01','W':'B02','N':'B03','D':'B04'}
 fmap={b+'.Ground':make_building(b,[a['id'] for a in areas if a['kind']=='room' and owner[a['id'][0]]==b]) for b in placements}
 for a in areas: a.update({'floor':'Ground','mapId':'westForest' if a['id'].startswith('F') else ('campus' if a['kind'] in ('outdoor','building') else owner[a['id'][0]]+'.Ground'),'regionId':a['id'],'portals':[]})
 links=[('F01','F02','westForest',(9,15),(8,15)),('F02','F03','westForest',(31,15),(30,15)),('F02','F04','westForest',(20,12),(25,11)),('F02','F05','westForest',(15,17),(18,18)),('F04','F06','westForest',(25,5),(25,4)),('F03','A01','campus',(41,15),(43,40)),('A01','C01','campus',(50,35),(50,35)),('C01','C02','campus',(42,28),(42,26)),('C01','C03','campus',(30,34),(27,34)),('C01','C04','campus',(60,50),(60,55)),('C01','C05','campus',(35,40),(35,39))]
 for b,(x,y,w,h) in placements.items(): links.append(('C05',b,b+'.Ground',(x+13,y),(x+8,y+2)))
 for s,t,dm,c1,c2 in links:
  for a,c,d,tm in ((by[s],c1,t,dm),(by[t],c2,s,by[s]['mapId'])): a['portals'].append({'id':f'P.{a["id"]}.{d}','cell':{'x':c[0],'y':c[1]},'destination':{'mapId':tm,'regionId':d,'cell':{'x':(c2 if a is by[s] else c1)[0],'y':(c2 if a is by[s] else c1)[1]}},'state':'open','requires':None})
 return {'schema':'lumenfall.tile-layout/2','coordinateSystem':'rows north-to-south; Unity (x,z)=(x,y); cellSize=1','tileLegend':LEGEND,'worldMaps':{'campus':campus,'westForest':forest},'floorMaps':fmap,'areas':areas}
def main():
 d=build(); root=OUT/'layouts'; root.mkdir(parents=True,exist_ok=True); maps={**d['worldMaps'],**d['floorMaps']}; (root/'legend.json').write_text(json.dumps(LEGEND,indent=2)+'\n')
 manifest={'schema':d['schema'],'coordinateSystem':d['coordinateSystem'],'legendFile':'legend.json','maps':{},'areas':d['areas']}
 for k,m in maps.items():
  m['tiles']=[''.join(row) for row in m['tiles']]
  (root/f'{k}.txt').write_text('\n'.join(m['tiles'])+'\n'); meta={x:y for x,y in m.items() if x!='tiles'}; meta['tileFile']=f'{k}.txt'; (root/f'{k}.json').write_text(json.dumps(meta,indent=2)+'\n'); manifest['maps'][k]=f'{k}.json'
 (root/'index.json').write_text(json.dumps(manifest,indent=2)+'\n'); (OUT/'academy-layouts.md').write_text('# Academy tile layouts\n\nAll academy interiors are open ground-level halls.\n')
if __name__=='__main__': main()
