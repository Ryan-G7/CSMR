"""Bake the source's shaded diffuse color on the final Unity UV2 atlas.

Unity exports evaluated geometry and final atlas coordinates first. Cycles uses
the original .blend materials/world; the result stays scene linear (EXR).
No source .blend is saved or changed.
"""
import bpy, json, math, os, sys, struct, hashlib
from pathlib import Path
import numpy as np

project=Path(__file__).resolve().parents[1]
directory=project/'Assets/Environment/VR'
manifest=json.loads((project/'tmp/vr-bake-geometry.json').read_text(encoding='utf-8'))
with Path(bpy.data.filepath).open('rb') as source_file:
    if hashlib.file_digest(source_file,'sha256').hexdigest()!=manifest['source_sha256']:
        raise RuntimeError('Source .blend does not match exported room geometry')
scene=bpy.context.scene
original_materials={m.name:m for m in bpy.data.materials}
for obj in list(scene.objects):bpy.data.objects.remove(obj,do_unlink=True)
objects=[]
with (project/'tmp/vr-bake-geometry.bin').open('rb') as file:
    for record in manifest['meshes']:
        file.seek(record['offset']);count=record['vertices']
        positions=np.fromfile(file,np.float32,count*3).reshape(-1,3)
        normals=np.fromfile(file,np.float32,count*3).reshape(-1,3)
        uv=np.fromfile(file,np.float32,count*2).reshape(-1,2)
        uv2=np.fromfile(file,np.float32,count*2).reshape(-1,2)
        indices=[];material_ids=[]
        submeshes=struct.unpack('<i',file.read(4))[0]
        for submesh in range(submeshes):
            total=struct.unpack('<i',file.read(4))[0]
            triangles=np.fromfile(file,np.int32,total).reshape(-1,3)
            indices.append(triangles);material_ids.extend([submesh]*len(triangles))
        triangles=np.concatenate(indices)
        # Exact inverse of the glTFast Blender -> Unity basis (-x,z,-y).
        positions=positions[:,[0,2,1]]*[-1,-1,1];normals=normals[:,[0,2,1]]*[-1,-1,1]
        # Unity's imported indices, after this basis conversion, oppose its
        # authored shading normals. Cycles needs CCW geometric front faces.
        triangles=triangles[:,[0,2,1]]
        geometric=np.cross(positions[triangles[:,1]]-positions[triangles[:,0]],positions[triangles[:,2]]-positions[triangles[:,0]])
        if np.sum(np.einsum('ij,ij->i',geometric,normals[triangles[:,0]]))<0:
            raise RuntimeError('Bake mesh winding opposes source normals: '+record['name'])
        mesh=bpy.data.meshes.new(record['name']);mesh.from_pydata(positions.tolist(),[],triangles.tolist())
        for name in record['materials']:mesh.materials.append(original_materials[name])
        mesh.polygons.foreach_set('material_index',material_ids)
        mesh.polygons.foreach_set('use_smooth',[True]*len(triangles))
        vertices=triangles.ravel()
        mesh.uv_layers.new(name='UVMap').data.foreach_set('uv',uv[vertices].ravel())
        st=record['st'];uv2=uv2*np.array(st[:2])+np.array(st[2:])
        mesh.uv_layers.new(name='Lightmap').data.foreach_set('uv',uv2[vertices].ravel())
        mesh.uv_layers.active_index=1;mesh.uv_layers['UVMap'].active_render=True
        mesh.normals_split_custom_set(normals[vertices].tolist())
        obj=bpy.data.objects.new(record['name'],mesh);scene.collection.objects.link(obj)
        obj['atlas']=record['atlas'];objects.append(obj)

# Source materials read UVMap; the bake writes the separately selected Lightmap.
for material in original_materials.values():
    if not material.use_nodes:continue
    tree=material.node_tree
    for node in list(tree.nodes):
        if node.type=='TEX_COORD':
            uv=tree.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap'
            for link in list(node.outputs['UV'].links):tree.links.new(uv.outputs[0],link.to_socket)
        elif node.type=='TEX_IMAGE' and not node.inputs['Vector'].is_linked:
            uv=tree.nodes.new('ShaderNodeUVMap');uv.uv_map='UVMap';tree.links.new(uv.outputs[0],node.inputs['Vector'])

scene.render.engine='CYCLES';scene.cycles.samples=256;scene.cycles.use_denoising=True
preferences=bpy.context.preferences.addons['cycles'].preferences;preferences.compute_device_type='OPTIX';preferences.get_devices()
for device in preferences.devices:device.use=device.type=='OPTIX'
scene.cycles.device='GPU'
scene.render.bake.use_pass_direct=True;scene.render.bake.use_pass_indirect=True;scene.render.bake.use_pass_color=True
scene.render.bake.margin=16;scene.render.bake.use_clear=True
# Keep the validated atlas placement, but bake eight times the Unity allocation
# in each dimension. Fine rock charts need enough texels and padding at seams.
atlas_scale=8
for index in range(len(manifest['atlases'])):
    group=[obj for obj in list(scene.objects) if obj['atlas']==index]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in group:obj.select_set(True)
    bpy.context.view_layer.objects.active=group[0]
    if len(group)>1:bpy.ops.object.join()
objects=list(scene.objects)
for index,atlas in enumerate(manifest['atlases']):
    image=bpy.data.images.new('Cycles diffuse transport %d'%index,width=atlas['width']*atlas_scale,height=atlas['height']*atlas_scale,alpha=False,float_buffer=True)
    image.colorspace_settings.name='Linear Rec.709'
    selected=[obj for obj in objects if obj['atlas']==index]
    bpy.ops.object.select_all(action='DESELECT')
    for obj in selected:obj.select_set(True);obj.data.uv_layers.active_index=1
    bpy.context.view_layer.objects.active=selected[0]
    for material in {s.material for obj in selected for s in obj.material_slots if s.material}:
        node=material.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;material.node_tree.nodes.active=node
    print('VR_CYCLES_BAKE',index,atlas,len(selected),flush=True)
    bpy.ops.object.bake(type='DIFFUSE',uv_layer='Lightmap')
    image.filepath_raw=str(directory/('CyclesLightmap-%d.exr'%index));image.file_format='OPEN_EXR';image.save()
    # Let Unity use native HDR lightmap import, with no display transform baked in.
(directory/'cycles-lighting.json').write_text(json.dumps(dict(
    source_sha256=manifest['source_sha256'],gltf_sha256=manifest['gltf_sha256'],
    layout_sha256=manifest['layout_sha256'],atlas_count=len(manifest['atlases']),samples=256,atlas_scale=atlas_scale,baked_diffuse_color=True)),encoding='utf-8')
print('VR_CYCLES_BAKE_COMPLETE',flush=True)
sys.stdout.flush();sys.stderr.flush();os._exit(0)
