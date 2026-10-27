"""Export CarRoom-2 in a disposable Blender process; never save its source.

The existing articulated AMG replaces the source's static car. World mapping,
source view transform, emissive fixtures and high resolution PBR inputs travel
with the static room. Large decorative meshes are split for culling and UV2.
"""
import bpy
import hashlib
import json
import math
import os
import sys
from collections import defaultdict
from pathlib import Path
import numpy as np
from mathutils import Vector, Matrix

PROJECT = Path(__file__).resolve().parents[1]
OUT = PROJECT / 'Assets/Environment/VR'
OUT.mkdir(parents=True, exist_ok=True)
sys.path.insert(0, str(Path(__file__).parent))
from export_blender_materials import Baker, resolve, uniform

scene = bpy.context.scene
source = Path(bpy.data.filepath)
report = dict(source=source.name, source_path=str(source), source_sha256=hashlib.file_digest(source.open('rb'),'sha256').hexdigest(),
              units='metres', exposure=scene.view_settings.exposure,
              view_transform=scene.view_settings.view_transform, look=scene.view_settings.look,
              limitations=['Real-time glass has no path-traced refraction.',
                            'Material AO nodes are evaluated on a tile; geometry lighting is baked by Cycles on the final Unity UV2 atlas.',
                            'True displacement is retained only where present in evaluated geometry.'])
previous=json.loads((OUT/'carroom.json').read_text(encoding='utf-8')) if (OUT/'carroom.json').exists() else {}
reuse_bakes=previous.get('source_sha256')==report['source_sha256']

# Read the packed source HDR and apply its Mapping rotation. Exporting only the
# original pixels would silently lose the room's authored environment direction.
world_image = next(n.image for n in scene.world.node_tree.nodes if n.type=='TEX_ENVIRONMENT')
if not world_image.packed_file and not Path(bpy.path.abspath(world_image.filepath)).exists():
    raise FileNotFoundError('Missing source world HDR: '+world_image.filepath)
strength=next(n.inputs['Strength'].default_value for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
report['world_strength']=strength
report['world_hdr']='RoomWorld.hdr'
width,height=world_image.size
raw=np.empty(width*height*4,dtype=np.float32);world_image.pixels.foreach_get(raw);raw=raw.reshape(height,width,4)
w,h=2048,1024
yy,xx=np.meshgrid(np.arange(h),np.arange(w),indexing='ij')
theta=(1-(yy+.5)/h)*math.pi;phi=((xx+.5)/w-.5)*2*math.pi
directions=np.stack([np.cos(phi)*np.sin(theta),-np.sin(phi)*np.sin(theta),np.cos(theta)],axis=-1)
mapping=next((n for n in scene.world.node_tree.nodes if n.type=='MAPPING'),None)
if mapping:
    from mathutils import Euler
    rotation=np.array(Euler(mapping.inputs['Rotation'].default_value).to_matrix())
    directions=(directions*np.array(mapping.inputs['Scale'].default_value))@rotation.T+np.array(mapping.inputs['Location'].default_value)
directions/=np.linalg.norm(directions,axis=-1,keepdims=True)
u=((np.arctan2(-directions[:,:,1],directions[:,:,0])/(2*math.pi)+.5)*width-.5)%width
v=np.clip((1-np.arccos(np.clip(directions[:,:,2],-1,1))/math.pi)*height-.5,0,height-1.001)
x0=np.floor(u).astype(int);y0=np.floor(v).astype(int);fx=(u-x0)[:,:,None];fy=(v-y0)[:,:,None]
data=(raw[y0,x0]*(1-fx)+raw[y0,(x0+1)%width]*fx)*(1-fy)+(raw[y0+1,x0]*(1-fx)+raw[y0+1,(x0+1)%width]*fx)*fy
rotated=bpy.data.images.new('Room mapped world',width=w,height=h,alpha=False,float_buffer=True)
rotated.colorspace_settings.name='Linear Rec.709';rotated.pixels.foreach_set(data.astype(np.float32).ravel())
rotated.filepath_raw=str(OUT/report['world_hdr']);rotated.file_format='HDR';rotated.save()
world_image=rotated
del raw,xx,yy,directions,u,v,x0,y0,fx,fy

# Use Blender itself as the color-management authority. The LUT maps scene-linear
# RGB (log domain) through the authored exposure, AgX and contrast look.
N=32
domain=(-12.47393, 6.52607)
lut=bpy.data.images.new('__AgX_LUT',width=N*N,height=N,alpha=False,float_buffer=True)
lut.colorspace_settings.name='Linear Rec.709'
pixels=np.ones((N,N*N,4),dtype=np.float32)
for b in range(N):
    for g in range(N):
        for r in range(N): pixels[g,b*N+r,:3]=np.exp2(np.array([r,g,b])/(N-1)*(domain[1]-domain[0])+domain[0])
lut.pixels.foreach_set(pixels.ravel())
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='16'
lut.save_render(str(OUT/'AgX.png'),scene=scene)
bpy.data.images.remove(lut)
report['lut_size']=N; report['lut_min']=domain[0];report['lut_max']=domain[1]

# A linear world integral for runtime diffuse SH, in Unity's glTF handedness.
w,h=world_image.size
data=np.empty(w*h*4,dtype=np.float32);world_image.pixels.foreach_get(data);data=data.reshape(h,w,4)
samples=[]
for y in range(32):
    for x in range(64):
        c=data[y*h//32:(y+1)*h//32,x*w//64:(x+1)*w//64,:3].mean(axis=(0,1))
        theta=(1-(y+.5)/32)*math.pi;phi=((x+.5)/64-.5)*2*math.pi
        # Blender panorama direction is (cos(phi), -sin(phi), z). glTFast's
        # Blender-to-Unity basis is (-x, z, -y), verified against a Cycles sky render.
        samples.append(dict(direction=[-math.cos(phi)*math.sin(theta),math.cos(theta),math.sin(phi)*math.sin(theta)],
                            radiance=c.tolist(),solid_angle=2*math.pi/64*(math.cos(math.pi*y/32)-math.cos(math.pi*(y+1)/32))))
report['samples']=samples
del data

# Recover the rigid placement from actual corresponding hood vertices, avoiding
# guesses based on a screenshot or a newly centered bounding box.
target=bpy.data.objects['Cube.074']
target_vertices=np.array([target.matrix_world@v.co for v in target.data.vertices])
with bpy.data.libraries.load(str(PROJECT.parent/'AMG.blend'),link=False) as (available,loaded):
    loaded.objects=['Cube.074']
original=loaded.objects[0]
scene.collection.objects.link(original)
deps=bpy.context.evaluated_depsgraph_get()
evaluated=original.evaluated_get(deps)
mesh=bpy.data.meshes.new_from_object(evaluated,depsgraph=deps)
original_vertices=np.array([original.matrix_world@v.co for v in mesh.vertices])
assert len(original_vertices)==len(target_vertices),(len(original_vertices),len(target_vertices))
a=original_vertices-original_vertices.mean(axis=0);b=target_vertices-target_vertices.mean(axis=0)
u,s,vt=np.linalg.svd(a.T@b); rotation=vt.T@u.T
assert np.linalg.det(rotation)>.99
translation=target_vertices.mean(axis=0)-rotation@original_vertices.mean(axis=0)
error=float(np.max(np.linalg.norm(original_vertices@rotation.T+translation-target_vertices,axis=1)))
assert error<.002,('AMG placement is not a rigid transform',error)
basis=np.array([[-1,0,0],[0,0,1],[0,-1,0]])
unity_rotation=Matrix((basis@rotation@basis.T).tolist()).to_quaternion()
car_data=json.loads((PROJECT/'Assets/Models/AMG-interactive.json').read_text(encoding='utf-8'))
# Existing interactive car's child has been lifted by -source_min_z.
root_position=basis@(translation+rotation@np.array([0,0,car_data['bounds_blender_min'][2]]))
report.update(car_position=root_position.tolist(),car_rotation=[*unity_rotation[1:],unity_rotation[0]],car_fit_error=error,
              entry=[-4.3,0,3],entry_look=[.2,.85,0],
              viewpoints=[[-3.25,0,2.5],[2.8,0,2.6],[3.2,0,-2.7],[-2.9,0,-3.0]],
              navigation_center=[0,2,0],navigation_size=[14,6,14])
bpy.data.objects.remove(original,do_unlink=True);bpy.data.meshes.remove(mesh)

# A small source render provides a reproducible visual reference.
if '--reference' in sys.argv:
    camera=bpy.data.objects.new('__reference',bpy.data.cameras.new('__reference'))
    scene.collection.objects.link(camera);scene.camera=camera
    camera.location=(4.3,-3.0,1.65);camera.rotation_euler=(Vector((-.2,0,.85))-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.lens=21.61
    scene.render.resolution_x=1280;scene.render.resolution_y=800;scene.render.resolution_percentage=100
    scene.cycles.samples=64;scene.cycles.use_denoising=True
    preferences=bpy.context.preferences.addons['cycles'].preferences
    preferences.compute_device_type='OPTIX';preferences.get_devices()
    for device in preferences.devices:device.use=device.type=='OPTIX'
    scene.cycles.device='GPU';scene.render.filepath=str(PROJECT/'tmp/vr-blender-reference.png')
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(camera,do_unlink=True)

car_collection=bpy.data.collections['Mercedes Benz GTR(Rigged)']
for obj in list(car_collection.all_objects):bpy.data.objects.remove(obj,do_unlink=True)
for obj in list(scene.objects):
    if obj.hide_render or obj.type not in {'MESH','CURVE','FONT'}:bpy.data.objects.remove(obj,do_unlink=True)

# Keep source texel colors/normal maps; limit texture residency for stereo VR.
used_materials={s.material for o in scene.objects for s in o.material_slots if s.material}
used_images={n.image for m in used_materials if m.use_nodes for n in m.node_tree.nodes if n.type=='TEX_IMAGE' and n.image}
for image in used_images:
    if max(image.size)>4096:
        ratio=4096/max(image.size);image.scale(max(1,round(image.size[0]*ratio)),max(1,round(image.size[1]*ratio)));image.pack()
print('VR_TEXTURES',len(used_images),flush=True)

# Unsupported color adjustment graphs are evaluated by Cycles on a texture tile.
# Temporarily remove image-coordinate mapping for baking, then restore the same
# mapping on the resulting texture. This preserves authored texture repetition.
class SceneBaker(Baker):
    def __init__(self):
        super().__init__(PROJECT)
        self.directory=PROJECT/'tmp/vr-material-bakes';self.directory.mkdir(parents=True,exist_ok=True)
        self.scene.cycles.samples=1
        self.resolution=2048

baker=SceneBaker()
records=[]
for material in sorted(used_materials,key=lambda m:m.name):
    if not material.use_nodes:continue
    tree=material.node_tree
    output=next((n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output),None)
    if not output or not output.inputs['Surface'].is_linked:continue
    shader=output.inputs['Surface'].links[0].from_node
    if shader.type=='MIX_SHADER':
        # The source side wall mixes the same Principled surface twice.
        branches=[s.links[0].from_node for s in list(shader.inputs)[1:] if s.is_linked]
        if branches and all(n==branches[0] for n in branches):
            mix=shader;shader=branches[0];tree.links.new(shader.outputs[0],output.inputs['Surface'])
            if len(branches)==1:
                weight=1-mix.inputs[0].default_value if mix.inputs[1].is_linked else mix.inputs[0].default_value
                attenuation=tree.nodes.new('ShaderNodeMixRGB');attenuation.blend_type='MULTIPLY';attenuation.inputs[0].default_value=1
                color=shader.inputs['Base Color']
                if color.is_linked:tree.links.new(color.links[0].from_socket,attenuation.inputs[1])
                else:attenuation.inputs[1].default_value=color.default_value
                attenuation.inputs[2].default_value=(weight,weight,weight,1);tree.links.new(attenuation.outputs[0],color)
                shader.inputs['Specular IOR Level'].default_value*=weight
    if shader.type!='BSDF_PRINCIPLED':
        # Glass/translucent decorative sources retain a simple portable surface.
        # Car's authored multilayer and glass shaders use the existing audited bridge.
        source_shader=shader
        shader=tree.nodes.new('ShaderNodeBsdfPrincipled')
        if source_shader.type=='EMISSION':
            for old,new in [('Color','Emission Color'),('Strength','Emission Strength')]:
                value=source_shader.inputs[old]
                if value.is_linked:tree.links.new(value.links[0].from_socket,shader.inputs[new])
                else:shader.inputs[new].default_value=value.default_value
        elif source_shader.type in {'BSDF_TRANSLUCENT','BSDF_DIFFUSE'}:
            value=source_shader.inputs['Color']
            if value.is_linked:tree.links.new(value.links[0].from_socket,shader.inputs['Base Color'])
            else:shader.inputs['Base Color'].default_value=value.default_value
            if source_shader.type=='BSDF_TRANSLUCENT':shader.inputs['Roughness'].default_value=.65
        else:
            shader.inputs['Roughness'].default_value=.12;shader.inputs['Alpha'].default_value=.22
        tree.links.new(shader.outputs[0],output.inputs['Surface'])
    mapping=[]
    images=[n for n in tree.nodes if n.type=='TEX_IMAGE' and n.image]
    primary=next((n for n in images if any(t in n.image.name.lower() for t in ('albedo','basecolor','diffuse'))),images[0] if images else None)
    vector=primary.inputs['Vector'].links[0].from_socket if primary and primary.inputs['Vector'].is_linked else None
    # Preserve direct glTF-supported images, mapping, normal strength and constants.
    for channel in ['Base Color','Roughness','Metallic','Alpha','Emission Color','Emission Strength']:
        socket=shader.inputs[channel]
        if not socket.is_linked or socket.links[0].from_node.type in {'TEX_IMAGE','VALUE','RGB'}:continue
        value=resolve(socket)
        constant=uniform(value)
        if constant is not None:
            for link in list(socket.links):tree.links.remove(link)
            socket.default_value=constant;continue
        for node in images:
            for link in list(node.inputs['Vector'].links):
                mapping.append((link.from_socket,node.inputs['Vector']));tree.links.remove(link)
        print('VR_BAKE',material.name,channel,flush=True)
        cache=baker.directory/(material.name.replace(' ','_').replace('.','_')+'_'+channel.replace(' ','_')+'.png')
        if reuse_bakes and cache.exists():
            image=bpy.data.images.load(str(cache),check_existing=False)
            image.colorspace_settings.name='sRGB' if channel=='Base Color' else 'Non-Color'
        else:image=baker.bake(material,value,channel.replace(' ','_'),external=True)
        for start,end in mapping:tree.links.new(start,end)
        mapping.clear()
        node=tree.nodes.new('ShaderNodeTexImage');node.image=image
        if vector:tree.links.new(vector,node.inputs['Vector'])
        tree.links.new(node.outputs['Color'],socket)
        records.append(dict(material=material.name,channel=channel))
    # The source's normal textures pass through an RGB correction node group.
    # Flatten that group while retaining tangent-space normal mapping/strength.
    normal=shader.inputs['Normal']
    if normal.is_linked and normal.links[0].from_node.type=='NORMAL_MAP':
        normal_node=normal.links[0].from_node;socket=normal_node.inputs['Color']
        if socket.is_linked and socket.links[0].from_node.type!='TEX_IMAGE':
            value=resolve(socket)
            for node in images:
                for link in list(node.inputs['Vector'].links):
                    mapping.append((link.from_socket,node.inputs['Vector']));tree.links.remove(link)
            image=baker.bake(material,value,'Normal_Color',external=True)
            for start,end in mapping:tree.links.new(start,end)
            mapping.clear()
            node=tree.nodes.new('ShaderNodeTexImage');node.image=image
            if vector:tree.links.new(vector,node.inputs['Vector'])
            tree.links.new(node.outputs['Color'],socket)
    material.use_backface_culling=False
baker.close()
report['material_bakes']=records

def split_dense(obj):
    """Partition evaluated mesh by 2m cells, preserving every face, UV and normal."""
    mesh=obj.data
    if len(mesh.vertices)<=70000:return [obj]
    vertices=np.empty(len(mesh.vertices)*3,np.float32);mesh.vertices.foreach_get('co',vertices);vertices=vertices.reshape(-1,3)
    loop_vertices=np.empty(len(mesh.loops),np.int32);mesh.loops.foreach_get('vertex_index',loop_vertices)
    starts=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('loop_start',starts)
    counts=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('loop_total',counts)
    materials=np.empty(len(mesh.polygons),np.int32);mesh.polygons.foreach_get('material_index',materials)
    smooth=np.empty(len(mesh.polygons),bool);mesh.polygons.foreach_get('use_smooth',smooth)
    normals=np.empty(len(mesh.loops)*3,np.float32);mesh.corner_normals.foreach_get('vector',normals);normals=normals.reshape(-1,3)
    uv=np.empty(len(mesh.loops)*2,np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv);uv=uv.reshape(-1,2)
    cells=np.floor(vertices[loop_vertices[starts]]/2).astype(int)
    _,keys=np.unique(cells,axis=0,return_inverse=True)
    result=[]
    for key in np.unique(keys):
        ids=np.flatnonzero(keys==key)
        for batch in range(0,len(ids),16000):
            polygons=ids[batch:batch+16000]
            loop_ids=np.concatenate([np.arange(starts[i],starts[i]+counts[i]) for i in polygons])
            unique,remap=np.unique(loop_vertices[loop_ids],return_inverse=True)
            offsets=np.cumsum(np.r_[0,counts[polygons]])
            faces=[remap[offsets[i]:offsets[i+1]].tolist() for i in range(len(polygons))]
            part=bpy.data.meshes.new(obj.name+'_chunk');part.from_pydata(vertices[unique].tolist(),[],faces)
            for material in mesh.materials:part.materials.append(material)
            part.polygons.foreach_set('material_index',materials[polygons]);part.polygons.foreach_set('use_smooth',smooth[polygons])
            part.uv_layers.new(name='UVMap').data.foreach_set('uv',uv[loop_ids].ravel())
            part.normals_split_custom_set(normals[loop_ids].tolist())
            child=bpy.data.objects.new(obj.name+'_%03d'%len(result),part);scene.collection.objects.link(child);result.append(child)
    bpy.data.objects.remove(obj,do_unlink=True);bpy.data.meshes.remove(mesh)
    return result

# Evaluate curves, text, arrays and modifiers, retain world-space geometry, then
# partition dense rocks. Material primitives stay separate; spatial culling survives.
deps=bpy.context.evaluated_depsgraph_get();groups=defaultdict(list)
for obj in list(scene.objects):
    if obj.type not in {'MESH','CURVE','FONT'}:continue
    mesh=bpy.data.meshes.new_from_object(obj.evaluated_get(deps),preserve_all_data_layers=True,depsgraph=deps)
    if not mesh or not len(mesh.polygons):continue
    # All source materials use the active UV input. Canonicalize its name BEFORE
    # joining objects, otherwise unrelated source UV channel names become empty
    # secondary layers, break texture mapping and masquerade as lightmap UVs.
    active=next((uv for uv in mesh.uv_layers if uv.active_render),mesh.uv_layers.active)
    if active is not None:
        coordinates=np.empty(len(mesh.loops)*2,dtype=np.float32)
        active.data.foreach_get('uv',coordinates)
        while mesh.uv_layers:mesh.uv_layers.remove(mesh.uv_layers[-1])
        canonical=mesh.uv_layers.new(name='UVMap');canonical.data.foreach_set('uv',coordinates)
    for attribute in list(mesh.color_attributes):mesh.color_attributes.remove(attribute)
    matrix=obj.matrix_world.copy();mesh.transform(matrix)
    new=bpy.data.objects.new('VR_'+obj.name,mesh);scene.collection.objects.link(new)
    new['source_object']=obj.name
    # Keep architecture separate from decorative rocks. Joining by object origin
    # put every source mesh into one huge chunk and prevented lightmap UVs.
    for part in split_dense(new):groups[part.name].append(part)
    bpy.data.objects.remove(obj,do_unlink=True)
for key,objects in groups.items():
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    if len(objects)>1:bpy.ops.object.join()
    objects[0].name='Room_'+key
    modifier=objects[0].modifiers.new('Portable tangent triangulation','TRIANGULATE')
    if hasattr(modifier,'keep_custom_normals'):modifier.keep_custom_normals=True
report['static_chunks']=len(groups)
report['triangles']=sum(len(p.vertices)-2 for o in scene.objects if o.type=='MESH' for p in o.data.polygons)

# Purge unloaded source data so the disposable export copy stays small.
bpy.data.orphans_purge(do_recursive=True)
prepared=PROJECT/'tmp/vr-prepared.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(prepared),copy=True,check_existing=False)
(OUT/'carroom.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('VR_PREPARED',prepared,report['static_chunks'],report['triangles'],flush=True)
sys.stdout.flush();sys.stderr.flush();os._exit(0)
