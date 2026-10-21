"""Read-only scene inventory; run in a disposable Blender background process."""
import bpy, json, os, sys
from pathlib import Path
from mathutils import Vector

scene = bpy.context.scene
objects = []
for o in scene.objects:
    record = dict(name=o.name, type=o.type, hidden=o.hide_render,
                  location=list(o.matrix_world.translation), scale=list(o.scale),
                  collections=[c.name for c in o.users_collection])
    if o.type == 'MESH':
        corners = [o.matrix_world @ Vector(c) for c in o.bound_box]
        record.update(vertices=len(o.data.vertices), polygons=len(o.data.polygons),
                      bounds=[[min(c[i] for c in corners) for i in range(3)],
                              [max(c[i] for c in corners) for i in range(3)]],
                      materials=[s.material.name if s.material else None for s in o.material_slots],
                      modifiers=[dict(type=m.type, name=m.name) for m in o.modifiers])
    elif o.type == 'LIGHT':
        record.update(light_type=o.data.type, energy=o.data.energy, color=list(o.data.color),
                      rotation=list(o.rotation_euler), matrix=[list(row) for row in o.matrix_world],
                      shape=getattr(o.data,'shape',''), size=getattr(o.data,'size',0), size_y=getattr(o.data,'size_y',0))
    elif o.type == 'CAMERA':
        record.update(rotation=list(o.rotation_euler), lens=o.data.lens)
    objects.append(record)
materials = []
for m in bpy.data.materials:
    materials.append(dict(name=m.name, nodes=[dict(name=n.name,type=n.type,
        image=n.image.name if n.type=='TEX_IMAGE' and n.image else None,
        inputs={s.name:list(s.default_value) if s.type in ('RGBA','VECTOR') else s.default_value
                for s in n.inputs if hasattr(s,'default_value') and s.type in ('RGBA','VECTOR','VALUE') and not s.is_linked})
        for n in m.node_tree.nodes] if m.use_nodes else []))
result = dict(source=bpy.data.filepath, camera=scene.camera.name if scene.camera else None, units=scene.unit_settings.scale_length,
              engine=scene.render.engine, view=scene.view_settings.view_transform,
              exposure=scene.view_settings.exposure, look=scene.view_settings.look,
              world=[dict(name=n.name,type=n.type,image=n.image.name if n.type=='TEX_ENVIRONMENT' and n.image else None,
                          inputs={s.name:list(s.default_value) if s.type in ('RGBA','VECTOR') else s.default_value
                                  for s in n.inputs if hasattr(s,'default_value') and s.type in ('RGBA','VECTOR','VALUE') and not s.is_linked})
                     for n in scene.world.node_tree.nodes] if scene.world and scene.world.use_nodes else [],
              objects=objects, materials=materials,
              images=[dict(name=i.name,path=i.filepath,size=list(i.size),packed=bool(i.packed_file)) for i in bpy.data.images])
out=Path(sys.argv[sys.argv.index('--')+1]) if '--' in sys.argv else Path(__file__).resolve().parents[1]/'tmp/vr-inventory.json'
out.parent.mkdir(parents=True,exist_ok=True)
out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print('VR_INVENTORY',out,len(objects),len(materials),flush=True)
os._exit(0)
