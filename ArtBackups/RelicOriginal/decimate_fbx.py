"""Decimate the single-mesh relic FBX to a target triangle count, keeping object, mesh and material names
(Unity derives the mesh fileID from them), and export with the original's units and axes.

Usage: blender -b --factory-startup --python decimate_fbx.py -- <input.fbx> <output.fbx> <target_tris>
"""
import math
import sys
import bpy

args = sys.argv[sys.argv.index("--") + 1:]
src, dst, target = args[0], args[1], int(args[2])

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)

meshes = [o for o in bpy.data.objects if o.type == "MESH"]
assert len(meshes) == 1, f"expected one mesh object, found {len(meshes)}"
obj = meshes[0]
names = (obj.name, obj.data.name, [m.name for m in obj.data.materials])
before = len(obj.data.polygons)

bpy.ops.object.select_all(action="DESELECT")
obj.select_set(True)
bpy.context.view_layer.objects.active = obj

# Custom split normals from the dense scan cannot survive collapse; drop them and rebuild shading afterwards.
if obj.data.has_custom_normals:
    bpy.ops.mesh.customdata_custom_splitnormals_clear()

mod = obj.modifiers.new("Decimate", "DECIMATE")
mod.decimate_type = "COLLAPSE"
mod.ratio = min(1.0, target / max(1, before))
mod.use_collapse_triangulate = True
bpy.ops.object.modifier_apply(modifier=mod.name)

# Smooth the stone surface but keep hard creases where the carved faces turn sharply.
bpy.ops.object.shade_smooth_by_angle(angle=math.radians(50))
for leftover in list(obj.modifiers):
    bpy.ops.object.modifier_apply(modifier=leftover.name)

assert (obj.name, obj.data.name, [m.name for m in obj.data.materials]) == names, "names changed during decimation"
print("DECIMATED", names[0], "tris", before, "->", len(obj.data.polygons))

bpy.ops.export_scene.fbx(
    filepath=dst,
    use_selection=True,
    object_types={"MESH"},
    use_mesh_modifiers=True,
    mesh_smooth_type="OFF",
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    global_scale=1.0,
    axis_forward="-Z",
    axis_up="Y",
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="AUTO",
    embed_textures=False,
)
print("EXPORTED", dst)
