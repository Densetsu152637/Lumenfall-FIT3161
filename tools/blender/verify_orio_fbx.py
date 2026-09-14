import bpy, sys
from pathlib import Path

fbx=Path(sys.argv[sys.argv.index('--')+1]).resolve()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=str(fbx), use_anim=False)
armatures=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
assert len(armatures)==1, f'Expected one armature, got {len(armatures)}'
assert len(armatures[0].data.bones)>=100, 'Skeleton is incomplete'
assert len(meshes)>=30, f'Expected outfit parts, got {len(meshes)}'
assert all(o.vertex_groups for o in meshes), 'A mesh has no skin weights'
materials={m.name for o in meshes for m in o.data.materials if m}
required={'Orio_Skin','Orio_Hair','Orio_TropicalTeal','Orio_SpellioTee','Orio_Shorts','Orio_FlipFlopSole'}
assert required.issubset(materials), f'Missing materials: {required-materials}'
print(f'ORIO_FBX_OK bones={len(armatures[0].data.bones)} meshes={len(meshes)} materials={len(materials)}')
