from pathlib import Path
import json

root=Path(__file__).resolve().parents[1]; docs=root/'docs/maps/layouts'; res=root/'Lumenfall/Assets/Resources/NPCs'
spec={'campus':(92,54),'westForest':(50,38),'B01.Ground':(36,24),'B02.Ground':(36,24),'B03.Ground':(36,24),'B04.Ground':(36,24)}
for n,(w,h) in spec.items():
    a=(docs/(n+'.npcs.txt')).read_text().splitlines(); b=(res/(n+'.npcs.txt')).read_text().splitlines()
    assert len(a)==h and all(len(x)==w for x in a) and a==b, n
print('validated',len(spec),'NPC layers: exact shapes and mirrors')
