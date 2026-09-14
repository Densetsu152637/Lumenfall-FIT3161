import bpy
import json
import sys
from pathlib import Path


def arg_after_double_dash():
    if "--" not in sys.argv:
        raise SystemExit("Expected FBX path after --")
    return Path(sys.argv[sys.argv.index("--") + 1]).resolve()


fbx_path = arg_after_double_dash()
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.wm.fbx_import(filepath=str(fbx_path), use_anim=False)

report = {
    "source": str(fbx_path),
    "objects": [],
}

for obj in bpy.context.scene.objects:
    entry = {
        "name": obj.name,
        "type": obj.type,
        "dimensions": [round(v, 6) for v in obj.dimensions],
        "location": [round(v, 6) for v in obj.location],
        "parent": obj.parent.name if obj.parent else None,
    }
    if obj.type == "ARMATURE":
        entry["bones"] = [
            {
                "name": bone.name,
                "parent": bone.parent.name if bone.parent else None,
                "head": [round(v, 6) for v in bone.head_local],
                "tail": [round(v, 6) for v in bone.tail_local],
            }
            for bone in obj.data.bones
        ]
    elif obj.type == "MESH":
        entry.update(
            {
                "vertices": len(obj.data.vertices),
                "polygons": len(obj.data.polygons),
                "materials": [slot.material.name if slot.material else None for slot in obj.material_slots],
                "vertex_groups": [group.name for group in obj.vertex_groups],
            }
        )
    report["objects"].append(entry)

print("CHIBI_REPORT_BEGIN")
print(json.dumps(report, indent=2))
print("CHIBI_REPORT_END")
