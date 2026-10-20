"""Read the supplied Blender source without modifying it."""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

report = {"blender": bpy.app.version_string, "units": str(bpy.context.scene.unit_settings.system),
          "unit_scale": bpy.context.scene.unit_settings.scale_length, "objects": [], "materials": [], "images": []}
for obj in bpy.data.objects:
    corners = [obj.matrix_world @ Vector(v) for v in obj.bound_box] if obj.type == 'MESH' else []
    report["objects"].append({"name": obj.name, "type": obj.type, "parent": obj.parent.name if obj.parent else None,
        "position": list(obj.location), "rotation": list(obj.rotation_euler), "scale": list(obj.scale),
        "bounds_min": [min(v[i] for v in corners) for i in range(3)] if corners else None,
        "bounds_max": [max(v[i] for v in corners) for i in range(3)] if corners else None,
        "vertices": len(obj.data.vertices) if obj.type == 'MESH' else 0,
        "polygons": len(obj.data.polygons) if obj.type == 'MESH' else 0,
        "materials": [m.name if m else None for m in getattr(obj.data, 'materials', [])],
        "modifiers": [{"name": m.name, "type": m.type} for m in obj.modifiers],
        "animation": str(obj.animation_data), "hidden": obj.hide_render})
for mat in bpy.data.materials:
    report["materials"].append({"name": mat.name, "diffuse": list(mat.diffuse_color), "nodes":
        [{"name": n.name, "type": n.type, "inputs": {s.name: (list(s.default_value) if hasattr(s.default_value, '__len__') and not isinstance(s.default_value, str) else s.default_value)
          for s in n.inputs if hasattr(s, 'default_value') and s.type in {'VALUE', 'RGBA', 'VECTOR'}},
          "image": n.image.name if n.type == 'TEX_IMAGE' and n.image else None} for n in mat.node_tree.nodes] if mat.use_nodes else []})
for img in bpy.data.images:
    report["images"].append({"name": img.name, "path": img.filepath, "size": list(img.size), "packed": bool(img.packed_file)})
target = Path(__file__).resolve().parents[1] / 'tmp' / 'amg-inspection.json'
target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print('AMG_REPORT', str(target), len(report['objects']), len(report['materials']))
if '--inspect-only' in sys.argv:
    sys.exit(0)

# A solid inspection view makes the independent outer panels easy to audit.
from math import radians
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'OBJECT'
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.display.shading.background_type = 'WORLD'
scene.world.color = (.12, .12, .12)
scene.render.resolution_x, scene.render.resolution_y, scene.render.resolution_percentage = 1400, 850, 100
for obj in bpy.data.objects:
    obj.hide_render = obj.name.startswith('WGT-')
    obj.color = (.45, .48, .5, 1)
    if obj.name in {'Cube.002', 'Cube.017', 'Cube.106', 'Cube.139', 'Cube.140', 'Cube.141', 'Cube.142'}:
        obj.color = (1, .24, .05, 1)
    if obj.name in {'Cube.007', 'Cube.009', 'Cube.010', 'Cube.011', 'Cube.029'}:
        obj.color = (1, .75, .02, 1)
    if obj.name == 'Cube.074': obj.color = (.08, .45, 1, 1)
    if obj.name == 'Cube.063': obj.color = (.05, .9, .25, 1)
cam = bpy.data.objects.new('Inspection Camera', bpy.data.cameras.new('Inspection Camera'))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = 6
for name, position in [('side', (7, 0, 2.6)), ('top', (0, 0, 8)), ('rear', (4.5, 6, 3.8))]:
    cam.location = position
    cam.rotation_euler = (Vector((0, 0, .65)) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(target.with_name('amg-' + name + '.png'))
    bpy.ops.render.render(write_still=True)
