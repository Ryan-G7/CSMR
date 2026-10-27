import bpy, numpy as np, os, sys, json, math, hashlib
from pathlib import Path
scene=bpy.context.scene;p=Path(__file__).resolve().parents[1];directory=p/'Assets/Environment/VR'
with Path(bpy.data.filepath).open('rb') as source_file:source_sha=hashlib.file_digest(source_file,'sha256').hexdigest()
if source_sha!=json.loads((directory/'carroom.json').read_text(encoding='utf-8'))['source_sha256']:
 raise RuntimeError('Source .blend does not match the exported room')
for obj in list(bpy.data.collections['Mercedes Benz GTR(Rigged)'].all_objects):obj.hide_render=True
camera=bpy.data.objects.new('__room_probe',bpy.data.cameras.new('__room_probe'));scene.collection.objects.link(camera);scene.camera=camera
camera.location=(0,0,1.2);camera.rotation_euler=(math.pi/2,0,-math.pi/2);camera.data.type='PANO';camera.data.panorama_type='EQUIRECTANGULAR'
scene.render.film_transparent=False;scene.render.resolution_x=2048;scene.render.resolution_y=1024;scene.render.resolution_percentage=100;scene.cycles.samples=32;scene.cycles.use_denoising=True
prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
for d in prefs.devices:d.use=d.type=='OPTIX'
scene.cycles.device='GPU';scene.render.image_settings.file_format='OPEN_EXR';scene.render.image_settings.color_mode='RGB';scene.render.image_settings.color_depth='16';scene.render.filepath=str(directory/'RoomInterior.exr')
bpy.ops.render.render(write_still=True)
im=bpy.data.images.load(str(directory/'RoomInterior.exr'));w,h=im.size;data=np.empty(w*h*4,np.float32);im.pixels.foreach_get(data);data=data.reshape(h,w,4)
samples=[]
for y in range(32):
 for x in range(64):
  c=data[y*h//32:(y+1)*h//32,x*w//64:(x+1)*w//64,:3].mean(axis=(0,1));theta=(1-(y+.5)/32)*math.pi;phi=((x+.5)/64-.5)*2*math.pi
  samples.append(dict(direction=[-math.cos(phi)*math.sin(theta),math.cos(theta),math.sin(phi)*math.sin(theta)],radiance=c.tolist(),solid_angle=2*math.pi/64*(math.cos(math.pi*y/32)-math.cos(math.pi*(y+1)/32))))
(directory/'interior-lighting.json').write_text(json.dumps(dict(source_sha256=source_sha,samples=samples)),encoding='utf-8')
scene.render.image_settings.file_format='PNG';im.save_render(str(p/'tmp/room-interior-reference.png'),scene=scene)
print('SOURCE_ROOM_PROBE',data[:,:,:3].mean(axis=(0,1)),data[:,:,:3].max(),flush=True)
sys.stdout.flush();os._exit(0)
