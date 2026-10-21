"""Extract portable PBR inputs into native URP textures, including HDR emission.

Run in a disposable Blender worker after GLB export. glTFast does not currently
import KHR_materials_emissive_strength; a native material bridge avoids silently
discarding the room's 54 / 108 / 600 strength fixtures.
"""
import bpy, json, struct, os, sys
from pathlib import Path
import numpy as np

directory=Path(__file__).resolve().parents[1]/'Assets/Environment/VR'
textures=directory/'Textures';textures.mkdir(exist_ok=True)
payload=(directory/'CarRoom.glb').read_bytes()
length=struct.unpack_from('<I',payload,12)[0]
gltf=json.loads(payload[20:20+length]);binary=memoryview(payload)[28+length:]
paths=[]
for i,image in enumerate(gltf['images']):
    view=gltf['bufferViews'][image['bufferView']];offset=view.get('byteOffset',0)
    name=image['name'].replace(' ','_').replace('.','_')+'.png'
    path=textures/name;path.write_bytes(binary[offset:offset+view['byteLength']]);paths.append(name)

def texture(info):
    if not info:return ''
    return paths[gltf['textures'][info['index']]['source']]

records=[]
for material in gltf['materials']:
    pbr=material.get('pbrMetallicRoughness',{})
    normal=material.get('normalTexture',{})
    transform=pbr.get('baseColorTexture',normal).get('extensions',{}).get('KHR_texture_transform',{})
    strength=material.get('extensions',{}).get('KHR_materials_emissive_strength',{}).get('emissiveStrength',1)
    emission=np.array(material.get('emissiveFactor',[0,0,0]))*strength
    metallic=pbr.get('metallicFactor',1);roughness=pbr.get('roughnessFactor',1)
    packed=texture(pbr.get('metallicRoughnessTexture'))
    metallic_smoothness=''
    if packed:
        image=bpy.data.images.load(str(textures/packed),check_existing=False);image.colorspace_settings.name='Non-Color'
        w,h=image.size;pixels=np.empty(w*h*4,np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(-1,4)
        converted=np.ones_like(pixels);converted[:,0]=pixels[:,2]*metallic;converted[:,1:3]=0;converted[:,3]=1-pixels[:,1]*roughness
        result=bpy.data.images.new(material['name']+'_MetalSmooth',width=w,height=h,alpha=True)
        result.colorspace_settings.name='Non-Color';result.pixels.foreach_set(converted.ravel())
        metallic_smoothness=material['name'].replace('.','_')+'_MetalSmooth.png'
        result.filepath_raw=str(textures/metallic_smoothness);result.file_format='PNG';result.save()
        bpy.data.images.remove(image);bpy.data.images.remove(result)
    records.append(dict(name=material['name'],base_color=pbr.get('baseColorFactor',[1,1,1,1]),
        base_map=texture(pbr.get('baseColorTexture')),normal_map=texture(normal),normal_scale=normal.get('scale',1),
        metallic=metallic,smoothness=1-roughness,metal_smooth_map=metallic_smoothness,
        emission=emission.tolist(),emission_map=texture(material.get('emissiveTexture')),
        scale=transform.get('scale',[1,1]),offset=transform.get('offset',[0,0])))
(directory/'materials.json').write_text(json.dumps(dict(materials=records),indent=2),encoding='utf-8')
print('VR_NATIVE_MATERIALS',len(records),flush=True)
sys.stdout.flush();sys.stderr.flush();os._exit(0)
