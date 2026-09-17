import bpy, math, sys
from pathlib import Path
from mathutils import Vector

ROOT = Path.cwd()
SRC = ROOT / 'Lumenfall/Assets/ThirdParty/StylizedChibiWitch/Mesh/Model_ChibiWitch.fbx'
OUT = ROOT / 'Lumenfall/Assets/_Project/Art/Characters/Orio'
OUT.mkdir(parents=True, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=str(SRC), use_anim=False)
arm = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
arm.name = 'Orio_Rig'
for o in list(bpy.data.objects):
    if o.type == 'MESH': bpy.data.objects.remove(o, do_unlink=True)

def mat(name, color, rough=.72, metallic=0.0):
    m=bpy.data.materials.new(name); m.diffuse_color=(*color,1)
    m.use_nodes=True; bs=m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=(*color,1); bs.inputs['Roughness'].default_value=rough; bs.inputs['Metallic'].default_value=metallic
    return m

M={
 'Skin':mat('Orio_Skin',(0.78,.54,.38)), 'Hair':mat('Orio_Hair',(.075,.028,.018)),
 'Tee':mat('Orio_SpellioTee',(.035,.045,.06)), 'Shirt':mat('Orio_TropicalTeal',(.02,.45,.43)),
 'Coral':mat('Orio_TropicalCoral',(.96,.25,.18)), 'Sun':mat('Orio_TropicalSun',(1,.67,.08)),
 'Shorts':mat('Orio_Shorts',(.06,.075,.10)), 'Sole':mat('Orio_FlipFlopSole',(.10,.18,.25)),
 'Strap':mat('Orio_FlipFlopStrap',(.95,.28,.12)), 'White':mat('Orio_EyeWhite',(.93,.91,.82)),
 'Eye':mat('Orio_Eyes',(.025,.018,.015)), 'Console':mat('Orio_Console',(.18,.16,.22),.48,.15),
 'Screen':mat('Orio_ConsoleScreen',(.05,.75,.95),.28,.05)
}

def bind(o,bone,material):
    o.name='Orio_'+o.name; o.data.materials.append(material)
    vg=o.vertex_groups.new(name=bone); vg.add(range(len(o.data.vertices)),1.0,'REPLACE')
    mod=o.modifiers.new('Orio Armature','ARMATURE'); mod.object=arm
    o.parent=arm; o.matrix_parent_inverse=arm.matrix_world.inverted(); return o

def uv(name,loc,scale,bone,material,seg=16,rings=8):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=rings, location=loc)
    o=bpy.context.object; o.name=name; o.scale=scale; bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    return bind(o,bone,material)

def cube(name,loc,scale,bone,material,bevel=.04,rot=(0,0,0)):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot); o=bpy.context.object; o.name=name; o.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel: mod=o.modifiers.new('Soft bevel','BEVEL'); mod.width=bevel; mod.segments=2
    return bind(o,bone,material)

def cyl(name,a,b,r,bone,material,verts=12):
    a,b=Vector(a),Vector(b); d=b-a
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=d.length, location=(a+b)/2)
    o=bpy.context.object; o.name=name; o.rotation_mode='QUATERNION'; o.rotation_quaternion=d.to_track_quat('Z','Y')
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); return bind(o,bone,material)

def bp(n): return arm.matrix_world @ arm.data.bones[n].head_local

# Head, ears, relaxed face and swept hair.
uv('Head',(0,-.005,1.49),(.31,.285,.32),'head',M['Skin'],20,12)
uv('Ear_L',(.305,0,1.49),(.045,.055,.075),'head',M['Skin']); uv('Ear_R',(-.305,0,1.49),(.045,.055,.075),'head',M['Skin'])
for x in (-.105,.105):
    uv('EyeWhite', (x,-.276,1.52),(.082,.018,.038),'head',M['White'],12,6)
    cube('RelaxedLid',(x,-.296,1.545),(.09,.012,.012),'head',M['Hair'],.008,rot=(0,0,-.07 if x<0 else .07))
    uv('Pupil',(x,-.300,1.515),(.024,.010,.026),'head',M['Eye'],10,6)
cube('Smirk',(0,-.302,1.405),(.065,.009,.009),'head',M['Hair'],.006,rot=(0,0,-.10))
# Hair cap + asymmetrical fringe + small beard/moustache.
uv('HairCap',(0,.035,1.67),(.325,.29,.18),'head',M['Hair'],20,10)
cube('Fringe_L',(-.145,-.265,1.65),(.14,.035,.10),'head',M['Hair'],.04,rot=(0,0,-.18))
cube('Fringe_R',(.13,-.267,1.675),(.13,.035,.07),'head',M['Hair'],.035,rot=(0,0,.12))
cube('Moustache',(0,-.300,1.432),(.075,.010,.012),'head',M['Hair'],.008)
cube('Beard',(0,-.286,1.36),(.105,.018,.045),'head',M['Hair'],.025)

# Tee, open tropical overshirt and floral accents.
uv('Torso',(0,0,1.02),(.30,.205,.35),'spine_03',M['Tee'],16,8)
cube('ShirtPanel_L',(-.185,-.202,1.05),(.115,.035,.30),'spine_03',M['Shirt'],.045,rot=(0,0,-.04))
cube('ShirtPanel_R',(.185,-.202,1.05),(.115,.035,.30),'spine_03',M['Shirt'],.045,rot=(0,0,.04))
for x,z,c in [(-.19,1.18,'Coral'),(.18,.95,'Sun'),(-.17,.86,'Sun'),(.20,1.24,'Coral')]:
    uv('TropicalPrint',(x,-.246,z),(.035,.012,.035),'spine_03',M[c],8,4)
# SPELLIO lightning/rune mark on tee.
for loc,rot in [((0,-.224,1.08),-.35),((.025,-.225,1.02),.35)]: cube('SpellioMark',loc,(.025,.01,.085),'spine_03',M['Sun'],.006,rot=(0,0,rot))

# Arms in source T-pose; short sleeves, forearms and hands.
for side in ('l','r'):
    u=bp('upperarm_'+side); e=bp('lowerarm_'+side); h=bp('hand_'+side)
    cyl('Sleeve_'+side,u,u+(e-u)*.42,.105,'upperarm_'+side,M['Shirt'])
    cyl('Arm_'+side,u+(e-u)*.38,e,.075,'upperarm_'+side,M['Skin'])
    cyl('Forearm_'+side,e,h,.072,'lowerarm_'+side,M['Skin'])
    uv('Hand_'+side,h,(.09,.075,.095),'hand_'+side,M['Skin'],12,6)

# Shorts, bare legs, feet, flip-flops.
cube('ShortsWaist',(0,0,.68),(.31,.20,.16),'pelvis',M['Shorts'],.07)
for s,x in [('l',.205),('r',-.205)]:
    cube('ShortsLeg_'+s,(x,0,.57),(.145,.19,.17),'thigh_'+s,M['Shorts'],.06)
    cyl('Leg_'+s,(x,0,.52),(x,0,.18),.105,'calf_'+s,M['Skin'])
    uv('Foot_'+s,(x,-.085,.105),(.13,.22,.075),'foot_'+s,M['Skin'],12,6)
    cube('FlipFlop_'+s,(x,-.10,.055),(.15,.245,.025),'foot_'+s,M['Sole'],.025)
    cyl('FlipStrapA_'+s,(x,-.25,.09),(x-.085,-.07,.105),.014,'foot_'+s,M['Strap'],8)
    cyl('FlipStrapB_'+s,(x,-.25,.09),(x+.085,-.07,.105),.014,'foot_'+s,M['Strap'],8)

# Lost-console motif: empty belt holster plus biscuit charm.
cube('ConsoleHolster',(.34,-.04,.72),(.08,.10,.12),'pelvis',M['Console'],.025)
uv('BiscuitCharm',(-.32,-.19,.73),(.055,.025,.055),'pelvis',M['Sun'],10,6)

# Name objects and set smooth shading.
for o in bpy.context.scene.objects:
    if o.type=='MESH':
        for p in o.data.polygons: p.use_smooth=True

bpy.context.scene['Character']='Orio (Nick)'
bpy.context.scene['DesignNotes']='Relaxed tropical SPELLIO fan; flip-flops required; compatible with ChibiWitch skeleton.'
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Orio_ChibiWitchRig.blend'))

bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
for o in bpy.context.scene.objects:
    if o.type=='MESH': o.select_set(True)
bpy.context.view_layer.objects.active=arm
kwargs=dict(filepath=str(OUT/'Orio_ChibiWitchRig.fbx'), selected_objects_only=True, apply_unit_scale=True,
            add_leaf_bones=False, bake_animation=False)
try:
    bpy.ops.wm.fbx_export(**kwargs)
except (AttributeError, RuntimeError):
    bpy.ops.preferences.addon_enable(module='io_scene_fbx')
    bpy.ops.export_scene.fbx(use_selection=True, filepath=kwargs['filepath'], apply_unit_scale=True, add_leaf_bones=False, bake_anim=False)
print('ORIO_CREATED', OUT)
