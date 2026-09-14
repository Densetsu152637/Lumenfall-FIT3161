import bpy
from pathlib import Path
from mathutils import Vector

root=Path.cwd(); src=root/'Lumenfall/Assets/_Project/Art/Characters/Orio/Orio_ChibiWitchRig.blend'
out=root/'Lumenfall/Assets/_Project/Art/Characters/Orio/Orio_Preview.png'
bpy.ops.wm.open_mainfile(filepath=str(src))

def look(obj, point): obj.rotation_euler=(Vector(point)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(0,-4.2,1.25)); cam=bpy.context.object; look(cam,(0,0,1.0)); bpy.context.scene.camera=cam
bpy.ops.object.light_add(type='AREA', location=(-2,-3,4)); bpy.context.object.data.energy=900; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=4; look(bpy.context.object,(0,0,1))
bpy.ops.object.light_add(type='AREA', location=(2,1,2.5)); bpy.context.object.data.energy=500; bpy.context.object.data.size=3; look(bpy.context.object,(0,0,1))
bpy.context.scene.render.engine='BLENDER_EEVEE'; bpy.context.scene.render.resolution_x=640; bpy.context.scene.render.resolution_y=640; bpy.context.scene.render.resolution_percentage=100
bpy.context.scene.render.image_settings.file_format='PNG'; bpy.context.scene.render.filepath=str(out)
if not bpy.context.scene.world: bpy.context.scene.world=bpy.data.worlds.new('PreviewWorld')
bpy.context.scene.world.color=(.055,.065,.09); bpy.context.scene.view_settings.look='AgX - Medium High Contrast'
bpy.ops.render.render(write_still=True)
print('ORIO_PREVIEW',out)
