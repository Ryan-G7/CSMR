"""Source-driven Blender -> glTF/URP bridge. No model-specific aesthetic presets.

Cycles evaluates unsupported colour/roughness/normal input graphs on a UV tile.
View-dependent mixes travel as response LUTs; the paint's three authored BSDF
lobes travel separately, since flattening them to glTF metal/roughness loses them.
The authored .blend is read in a disposable background process and never saved over.
"""
import bpy
import hashlib
import json
import math
import shutil
from pathlib import Path
import numpy as np


def plain(value):
    if isinstance(value, (np.ndarray, np.generic)): return value.tolist()
    try: return list(value)
    except TypeError: return value


def resolve(socket, parents=()):
    if not socket.is_output and not socket.is_linked:
        return plain(socket.default_value)
    output = socket if socket.is_output else socket.links[0].from_socket
    node = output.node
    if node.type == 'GROUP_INPUT':
        return resolve(next(s for s in parents[-1].inputs if s.identifier == output.identifier), parents[:-1])
    if node.type == 'GROUP':
        end = next(n for n in node.node_tree.nodes if n.type == 'GROUP_OUTPUT' and n.is_active_output)
        return resolve(next(s for s in end.inputs if s.identifier == output.identifier), parents + (node,))
    if node.type == 'REROUTE': return resolve(node.inputs[0], parents)
    if node.type in {'RGB', 'VALUE'}: return plain(output.default_value)
    return (output, parents)


def is_expression(value): return isinstance(value, tuple) and len(value) == 2 and hasattr(value[0], 'node')


def scalar(value):
    value=np.asarray(value)
    return float(value) if value.ndim==0 else float(np.dot(value[:3], [.2126,.7152,.0722]))


def rgba(value):
    value=np.asarray(value)
    return np.asarray([float(value)]*3+[1]) if value.ndim==0 else value


def evaluate(value, cosine=1):
    if not is_expression(value): return np.asarray(value, dtype=float)
    socket, parents = value
    n = socket.node
    def inp(name): return evaluate(resolve(next(s for s in n.inputs if s.name==name or s.identifier==name), parents), cosine)
    if n.type == 'LAYER_WEIGHT':
        blend = float(inp('Blend'))
        if socket.name == 'Facing':
            exponent = 2 * blend if blend < .5 else 1 / max(2 * (1 - blend), 1e-6)
            return np.asarray(1 - cosine ** exponent)
    if n.type == 'VALTORGB':
        return np.asarray(n.color_ramp.evaluate(scalar(inp('Fac'))))
    if n.type in {'INVERT', 'INVERT_COLOR'}:
        color = rgba(inp('Color')); result=color * (1-inp('Fac')) + (1-color) * inp('Fac')
        result[3]=color[3]
        return result
    if n.type == 'HUE_SAT':
        import colorsys
        color = inp('Color'); h,s,v = colorsys.rgb_to_hsv(*color[:3])
        transformed = np.array((*colorsys.hsv_to_rgb((h+float(inp('Hue'))-.5)%1,
                                    s*float(inp('Saturation')),v*float(inp('Value'))),color[3]))
        return color * (1-inp('Fac')) + transformed * inp('Fac')
    if n.type == 'MIX':
        # Blender stores float, vector and colour sockets together; use active identifiers.
        def active(identifier): return evaluate(resolve(next(s for s in n.inputs if s.identifier==identifier), parents), cosine)
        factor=active('Factor_Float'); return active('A_Color')*(1-factor)+active('B_Color')*factor
    raise ValueError('Expression needs Cycles baking: '+n.name+' ('+n.type+')')


def uniform(value):
    try: return plain(evaluate(value))
    except ValueError: return None


class Baker:
    def __init__(self, project):
        self.records=[]
        self.hidden={o:o.hide_render for o in bpy.data.objects}
        for o in self.hidden: o.hide_render=True
        bpy.ops.mesh.primitive_plane_add(size=1, location=(.5,.5,0))
        self.plane=bpy.context.object
        self.plane.name='__source_material_bake_tile'
        # Object coordinates on this tile cover the same [0,1] domain as UVs.
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
        self.scene=bpy.context.scene
        self.scene.render.engine='CYCLES'
        self.scene.cycles.samples=8
        self.scene.render.bake.margin=0
        self.directory=project/'Assets/Models/AMGSource'
        self.directory.mkdir(parents=True,exist_ok=True)

    def lift(self, value):
        socket,parents=value
        for group in reversed(parents):
            tree=group.node_tree
            item=tree.interface.new_socket(name='__export_'+str(len(tree.interface.items_tree)),in_out='OUTPUT',socket_type={'VALUE':'NodeSocketFloat','RGBA':'NodeSocketColor','VECTOR':'NodeSocketVector'}[socket.type])
            out=next(n for n in tree.nodes if n.type=='GROUP_OUTPUT' and n.is_active_output)
            tree.links.new(socket,out.inputs[item.identifier])
            socket=group.outputs[item.identifier]
        return socket

    def bake(self, material, value, channel, normal=False, external=False):
        name=material.name.replace(' ','_').replace('.','_')+'_'+channel
        image=bpy.data.images.new(name,width=1024,height=1024,alpha=False)
        image.colorspace_settings.name='Non-Color' if channel!='Base_Color' else 'sRGB'
        tree=material.node_tree
        end=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
        original=end.inputs['Surface'].links[0].from_socket
        sample=tree.nodes.new('ShaderNodeTexImage');sample.image=image
        tree.nodes.active=sample
        if normal:
            node=tree.nodes.new('ShaderNodeBsdfPrincipled')
            tree.links.new(self.lift(value),node.inputs['Normal'])
        else:
            node=tree.nodes.new('ShaderNodeEmission')
            tree.links.new(self.lift(value),node.inputs['Color'])
        tree.links.new(node.outputs[0],end.inputs['Surface'])
        self.plane.data.materials.clear();self.plane.data.materials.append(material)
        bpy.ops.object.select_all(action='DESELECT');self.plane.select_set(True)
        bpy.context.view_layer.objects.active=self.plane
        bpy.ops.object.bake(type='NORMAL' if normal else 'EMIT')
        tree.links.new(original,end.inputs['Surface'])
        tree.nodes.remove(node);tree.nodes.remove(sample)
        if external:
            image.filepath_raw=str(self.directory/(name+'.png'));image.file_format='PNG';image.save()
        else:image.pack()
        self.records.append({'material':material.name,'channel':channel,'method':'Cycles NORMAL' if normal else 'Cycles EMIT','domain':'UV tile [0,1]; object/generated graphs evaluated on unit tile'})
        return image

    def apply(self, material, target, name, value, normal=False):
        constant=uniform(value) if not normal else None
        if constant is not None:
            target.inputs[name].default_value=constant
            return
        image=self.bake(material,value,name.replace(' ','_'),normal=normal)
        tex=material.node_tree.nodes.new('ShaderNodeTexImage');tex.image=image
        if normal:
            nm=material.node_tree.nodes.new('ShaderNodeNormalMap')
            material.node_tree.links.new(tex.outputs['Color'],nm.inputs['Color'])
            material.node_tree.links.new(nm.outputs['Normal'],target.inputs[name])
        else:material.node_tree.links.new(tex.outputs['Color'],target.inputs[name])

    def close(self):
        # Release the Cycles bake session before removing its temporary object.
        self.scene.render.engine='BLENDER_EEVEE'
        bpy.data.objects.remove(self.plane,do_unlink=True)
        for o,hidden in self.hidden.items():o.hide_render=hidden


def export_materials(project):
    # Blender displays unassigned faces with its default Principled surface.
    # Make that implicit source default explicit so glTF has no null slots.
    missing = [o for o in bpy.data.objects if o.type == 'MESH' and
               (not o.data.materials or any(m is None for m in o.data.materials))]
    if missing:
        default = bpy.data.materials.new('Blender Default Surface')
        default.use_nodes = True
        for obj in missing:
            if not obj.data.materials: obj.data.materials.append(default)
            for i, material in enumerate(obj.data.materials):
                if material is None: obj.data.materials[i] = default
    baker=Baker(project)
    report={'material_conversion':[], 'view_materials':[]}
    for material in list(bpy.data.materials):
        if not material.use_nodes:continue
        # Group instances may have different values (e.g. black and teal leather).
        def private_groups(tree):
            for node in tree.nodes:
                if node.type=='GROUP':
                    node.node_tree=node.node_tree.copy();private_groups(node.node_tree)
        private_groups(material.node_tree)
        tree=material.node_tree
        out=next(n for n in tree.nodes if n.type=='OUTPUT_MATERIAL' and n.is_active_output)
        shader,parents=resolve(out.inputs['Surface']);source=shader.node
        def input_value(node,name):return resolve(node.inputs[name],parents)
        target=tree.nodes.new('ShaderNodeBsdfPrincipled')
        record={'material':material.name,'source_shader':source.type}
        if material.name=='Black Carpaint':
            assert source.type=='MIX_SHADER', 'Carpaint graph changed: review the source BSDF topology'
            group=parents[-1]
            nodes=group.node_tree.nodes
            flake=nodes['Glossy BSDF.001'];principled=nodes['Principled BSDF'];coat=nodes['Glossy BSDF']
            assert source.inputs[0].links[0].from_node.type=='FRESNEL'
            inner=source.inputs[1].links[0].from_node
            assert inner.type=='MIX_SHADER' and inner.inputs[1].links[0].from_node==principled and inner.inputs[2].links[0].from_node==flake
            assert source.inputs[2].links[0].from_node==coat
            paint={'color_linear':uniform(input_value(flake,'Color')),
                   'principled_color_linear':uniform(input_value(principled,'Base Color')),
                   'principled_metallic':uniform(input_value(principled,'Metallic')),
                   'principled_roughness':uniform(input_value(principled,'Roughness')),
                   'principled_mix':1-float(uniform(input_value(inner,0))),
                   'coat_color_linear':uniform(input_value(coat,'Color')),
                   'coat_roughness':uniform(input_value(coat,'Roughness')),
                   'coat_ior':uniform(input_value(source.inputs[0].links[0].from_node,'IOR'))}
            assert all(v is not None for v in paint.values()),'A source paint uniform became procedural; extend bridge before export'
            for node,key in [(flake,'flake'),(coat,'coat')]:
                for channel in ['Roughness','Normal']:
                    value=input_value(node,channel)
                    if is_expression(value):
                        image=baker.bake(material,value,key+'_'+channel,normal=channel=='Normal',external=True)
                        paint[key+'_'+channel.lower()+'_texture']=str(Path(image.filepath_raw).relative_to(project)).replace('\\','/')
            target.inputs['Base Color'].default_value=paint['color_linear']
            target.inputs['Metallic'].default_value=paint['principled_metallic']
            target.inputs['Roughness'].default_value=paint['principled_roughness']
            report['source_paint']=paint
            record['method']='Authored GGX / Principled / Fresnel layers reconstructed in URP; glTF contains source Principled fallback'
        elif source.type=='BSDF_PRINCIPLED':
            for socket in source.inputs:
                if socket.name not in target.inputs or not hasattr(socket,'default_value'):continue
                value=input_value(source,socket.name)
                if not is_expression(value):
                    target.inputs[socket.name].default_value=value
                elif socket.name in {'Base Color','Roughness','Metallic','Alpha','Emission Color','Emission Strength','Normal','Coat Weight','Coat Roughness'}:
                    baker.apply(material,target,socket.name,value,normal=socket.name=='Normal')
                else:raise ValueError('Unsupported connected Principled input: '+material.name+'/'+socket.name)
            record['method']='Authored Principled values and Cycles-evaluated input textures'
        elif source.type=='BSDF_GLOSSY':
            baker.apply(material,target,'Base Color',input_value(source,'Color'))
            baker.apply(material,target,'Roughness',input_value(source,'Roughness'))
            target.inputs['Metallic'].default_value=1 # GGX reflection lobe, no diffuse component.
            record['method']='Authored Glossy colour/roughness -> metallic GGX lobe'
        elif source.type in {'EMISSION','MIX_SHADER','BSDF_GLASS'}:
            # View response is sampled from source node values, not hand-selected opacity/emission.
            emission=source.type=='EMISSION'
            reflection_color=[1,1,1,1]; roughness=0
            if source.type=='MIX_SHADER':
                branches=[resolve(source.inputs[i],parents) for i in (1,2)]
                types=[v[0].node.type for v in branches]
                assert set(types)=={'BSDF_TRANSPARENT','BSDF_GLOSSY'},(material.name,types)
                glossy=next(v for v in branches if v[0].node.type=='BSDF_GLOSSY')
                transparent=next(v for v in branches if v[0].node.type=='BSDF_TRANSPARENT')
                reflection_color=uniform(resolve(glossy[0].node.inputs['Color'],glossy[1]))
                if reflection_color is None:
                    image=baker.bake(material,resolve(glossy[0].node.inputs['Color'],glossy[1]),'Reflection',external=True)
                    reflection_color=[1,1,1,1]
                    record['reflection_texture']=str(Path(image.filepath_raw).relative_to(project)).replace('\\','/')
                roughness=uniform(resolve(glossy[0].node.inputs['Roughness'],glossy[1]))
                transmitted=uniform(resolve(transparent[0].node.inputs['Color'],transparent[1]))
                assert transmitted is not None
            pixels=[]
            for i in range(256):
                cosine=i/255
                if emission:
                    color=evaluate(input_value(source,'Color'),cosine)*float(evaluate(input_value(source,'Strength'),cosine))
                    pixels.extend([*color[:3],1])
                else:
                    if source.type=='BSDF_GLASS':
                        ior=float(uniform(input_value(source,'IOR')))
                        g=math.sqrt(ior*ior+cosine*cosine-1)
                        f=.5*((g-cosine)/(g+cosine))**2*(1+((cosine*(g+cosine)-1)/(cosine*(g-cosine)+1))**2)
                        reflection_color=uniform(input_value(source,'Color'));roughness=uniform(input_value(source,'Roughness'))
                        alpha=f
                    else:
                        factor=scalar(evaluate(input_value(source,0),cosine))
                        f=factor if types[1]=='BSDF_GLOSSY' else 1-factor
                        alpha=1-(1-f)*float(np.mean(transmitted[:3]))
                    pixels.extend([f,f,f,alpha])
            image=bpy.data.images.new(material.name+'_view',width=256,height=1,alpha=True,float_buffer=True)
            image.colorspace_settings.name='Non-Color';image.pixels.foreach_set(pixels)
            image.filepath_raw=str(baker.directory/(material.name.replace(' ','_')+'_view.exr'))
            scene=bpy.context.scene;scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_depth='16';scene.render.image_settings.color_mode='RGBA'
            image.save_render(image.filepath_raw,scene=scene)
            info={'name':material.name,'emission':emission,'color_linear':reflection_color,'roughness':roughness,
                  'response_texture':str(Path(image.filepath_raw).relative_to(project)).replace('\\','/')}
            if 'reflection_texture' in record:info['reflection_texture']=record['reflection_texture']
            report['view_materials'].append(info)
            if emission:
                target.inputs['Base Color'].default_value=(0,0,0,1)
                target.inputs['Emission Color'].default_value=tuple(pixels[-4:-1])+ (1,)
                target.inputs['Emission Strength'].default_value=1
            else:
                target.inputs['Base Color'].default_value=reflection_color
                target.inputs['Metallic'].default_value=1
                target.inputs['Roughness'].default_value=roughness
                target.inputs['Alpha'].default_value=pixels[-1]
                material.surface_render_method='DITHERED'
            record['method']='Source view response LUT (transparent tint reduced to scalar transmittance in URP)'
        else:raise ValueError('Unhandled source shader: '+material.name+'/'+source.type)
        tree.links.new(target.outputs['BSDF'],out.inputs['Surface'])
        report['material_conversion'].append(record)
    report['material_bakes']=baker.records
    baker.close()
    return report


def export_environment(project):
    source=project.parent/'brown_photostudio_02_2k.hdr'
    directory=project/'Assets/Environment';directory.mkdir(parents=True,exist_ok=True)
    destination=directory/source.name
    if not source.exists(): source=destination
    if not source.exists():raise FileNotFoundError('Missing HDR environment: '+str(source))
    if source.resolve() != destination.resolve(): shutil.copyfile(source,destination)
    image=bpy.data.images.load(str(source),check_existing=False)
    w,h=image.size
    assert w==2*h and w>=1024,(w,h)
    pixels=np.asarray(image.pixels[:],dtype=np.float32).reshape(h,w,4)
    assert np.all(np.isfinite(pixels)) and np.max(pixels[:,:,:3])>1,'HDR data was clipped or invalid'
    samples=[]
    for y in range(32):
        for x in range(64):
            color=pixels[y*h//32:(y+1)*h//32,x*w//64:(x+1)*w//64,:3].mean(axis=(0,1))
            v=(y+.5)/32;u=(x+.5)/64;theta=(1-v)*math.pi;phi=(.5-u)*2*math.pi
            samples.append({'direction':[math.sin(theta)*math.cos(phi),math.cos(theta),math.sin(theta)*math.sin(phi)],
                            'radiance':color.tolist(),'solid_angle':2*math.pi/64*(math.cos(math.pi*y/32)-math.cos(math.pi*(y+1)/32))})
    data={'source':source.name,'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'strength':1,'rotation_degrees':0,'samples':samples}
    (directory/'environment.json').write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
    return {k:v for k,v in data.items() if k!='samples'}
