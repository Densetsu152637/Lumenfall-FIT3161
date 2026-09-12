"""Generate authoritative single-storey academy tile maps."""
from pathlib import Path
import json,re
ROOT=Path(__file__).resolve().parents[1]; SRC=ROOT/'docs/location-plan.md'; OUT=ROOT/'docs/maps'
LEGEND={'#':{'name':'wall','walkable':False,'collision':True},'.':{'name':'floor','walkable':True,'collision':False},'+':{'name':'door','walkable':True,'collision':False},'f':{'name':'fixture','walkable':False,'collision':True}}
def inventory():
 t=SRC.read_text(encoding='utf8'); d={}
 for i,n in re.findall(r'^## (B0[1-4]):\s*([^\n]+)',t,re.M): d[i]={'id':i,'name':n.strip(),'kind':'building','floor':'Ground','access':'Chapter 1'}
 for i,n,p,a in re.findall(r'^\|\s*([A-Z][0-9]{2})\s*\|\s*([^|]+)\|\s*([^|]+)\|[^\n]*?\|\s*(Chapter 1|Optional|Reserved)\s*\|',t,re.M):
  names={'W08':'Teaching support zone','W09':'Seminar zone','W10':'Advanced seminar zone','W11':'Faculty preparation zone','N09':'Administration zone','N10':'Administration landing zone','N11':"Head's office zone",'N12':'Staff meeting zone','D04':'Residential key door zone','D07':'Group living zone','D08':'Player sleeping room','D09':"Stella's sleeping zone",'D10':"Cinna Cirra's sleeping zone",'D11':"Orio's sleeping zone",'D12':"Katrina's sleeping zone",'D13':'Shared washroom zone','D14':'Linen storage zone'}
  d[i]={'id':i,'name':names.get(i,n.strip()),'kind':'outdoor' if i[0] in 'FAC' else 'room','floor':'Ground','access':a}
 return [d[k] for k in sorted(d)]
def blank(w,h): return [['#']*w]+[['#']+['.']*(w-2)+['#'] for _ in range(h-2)]+[['#']*w]
def region_map(w,h,regions): return {'width':w,'height':h,'tiles':[''.join(x) for x in blank(w,h)],'regions':regions,'spawn':{'x':1,'y':1}}
def make_building(b,ids):
 regs=[{'id':b,'name':b,'rect':{'x':1,'y':1,'width':34,'height':22},'walkable':True}]
 for n,i in enumerate(ids): regs.append({'id':i,'name':i,'rect':{'x':2+(n%4)*8,'y':2+(n//4)*5,'width':6,'height':3},'walkable':True})
 return {'width':36,'height':24,'tiles':[''.join(x) for x in blank(36,24)],'regions':regs,'spawn':{'x':2,'y':2}}
def build():
 areas=inventory(); by={a['id']:a for a in areas}; placements={'B01':(12,56,20,14),'B02':(10,12,26,20),'B03':(40,8,30,18),'B04':(72,48,20,24)}
 cre=[{'id':i,'name':n,'rect':dict(zip(('x','y','width','height'),r)),'walkable':True} for i,n,r in [('A01','East gate forecourt',(43,35,14,10)),('C01','Campus commons',(30,28,40,28)),('C02','Orientation terrace',(34,18,18,8)),('C03','Orientation trial strip',(15,30,12,8)),('C04','Garden walk',(60,55,18,10)),('C05','Campus path loop',(25,25,50,40))]]
 cre += [{'id':b,'name':b,'rect':dict(zip(('x','y','width','height'),r)),'walkable':True} for b,r in placements.items()]
 campus=region_map(100,80,cre); campus.update({'buildingPlacements':{b:dict(zip(('x','y','width','height'),r)) for b,r in placements.items()},'gateStates':{'north':'locked','south':'locked','west':'locked','east':'open'}})
 forest=region_map(48,30,[{'id':i,'name':by[i]['name'],'rect':dict(zip(('x','y','width','height'),r)),'walkable':True} for i,r in zip(('F01','F02','F03','F04','F05','F06'),[(2,12,8,6),(8,12,24,6),(30,12,12,6),(22,5,10,7),(14,18,12,7),(22,2,8,4)])])
 owner={'S':'B01','W':'B02','N':'B03','D':'B04'}
 fmap={b+'.Ground':make_building(b,[a['id'] for a in areas if a['kind']=='room' and owner[a['id'][0]]==b]) for b in placements}
 for a in areas: a.update({'floor':'Ground','mapId':'eastForest' if a['id'].startswith('F') else ('campus' if a['kind'] in ('outdoor','building') else owner[a['id'][0]]+'.Ground'),'regionId':a['id'],'portals':[]})
 links=[('F01','F02','eastForest',(9,15),(8,15)),('F02','F03','eastForest',(31,15),(30,15)),('F02','F04','eastForest',(20,12),(25,11)),('F02','F05','eastForest',(15,17),(18,18)),('F04','F06','eastForest',(25,5),(25,4)),('F03','A01','campus',(41,15),(43,40)),('A01','C01','campus',(50,35),(50,35)),('C01','C02','campus',(42,28),(42,26)),('C01','C03','campus',(30,34),(27,34)),('C01','C04','campus',(60,50),(60,55)),('C01','C05','campus',(35,40),(35,39))]
 for b,(x,y,w,h) in placements.items(): links.append(('C05',b,b+'.Ground',(x+13,y),(x+8,y+2)))
 for s,t,dm,c1,c2 in links:
  for a,c,d,tm in ((by[s],c1,t,dm),(by[t],c2,s,by[s]['mapId'])): a['portals'].append({'id':f'P.{a["id"]}.{d}','cell':{'x':c[0],'y':c[1]},'destination':{'mapId':tm,'regionId':d,'cell':{'x':(c2 if a is by[s] else c1)[0],'y':(c2 if a is by[s] else c1)[1]}},'state':'open','requires':None})
 return {'schema':'lumenfall.tile-layout/2','coordinateSystem':'rows north-to-south; Unity (x,z)=(x,y); cellSize=1','tileLegend':LEGEND,'worldMaps':{'campus':campus,'eastForest':forest},'floorMaps':fmap,'areas':areas}
def main():
 d=build(); root=OUT/'layouts'; root.mkdir(parents=True,exist_ok=True); maps={**d['worldMaps'],**d['floorMaps']}; (root/'legend.json').write_text(json.dumps(LEGEND,indent=2)+'\n')
 manifest={'schema':d['schema'],'coordinateSystem':d['coordinateSystem'],'legendFile':'legend.json','maps':{},'areas':d['areas']}
 for k,m in maps.items(): (root/f'{k}.txt').write_text('\n'.join(m['tiles'])+'\n'); meta={x:y for x,y in m.items() if x!='tiles'}; meta['tileFile']=f'{k}.txt'; (root/f'{k}.json').write_text(json.dumps(meta,indent=2)+'\n'); manifest['maps'][k]=f'{k}.json'
 (root/'index.json').write_text(json.dumps(manifest,indent=2)+'\n'); (OUT/'academy-layouts.md').write_text('# Academy tile layouts\n\nAll academy interiors are open ground-level halls.\n')
if __name__=='__main__': main()
