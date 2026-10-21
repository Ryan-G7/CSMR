"""Split the source HDR sun from diffuse sky, preserving its energy and direction.

URP receives the sun as a shadow-casting light so the animated car casts dynamic
shadows; the remaining sky feeds static GI without counting the sun twice.
"""
import bpy, json, math, os, sys
from pathlib import Path
import numpy as np

directory=Path(__file__).resolve().parents[1]/'Assets/Environment/VR'
path=directory/'carroom.json'
report=json.loads(path.read_text(encoding='utf-8'))
image=bpy.data.images.load(str(directory/report['world_hdr']),check_existing=False)
w,h=image.size
pixels=np.empty(w*h*4,dtype=np.float32);image.pixels.foreach_get(pixels);pixels=pixels.reshape(h,w,4)
luminance=np.sum(pixels[:,:,:3]*[.2126,.7152,.0722],axis=2)
mask=luminance>32
theta=(1-(np.arange(h)+.5)/h)*math.pi
phi=((np.arange(w)+.5)/w-.5)*2*math.pi
solid_angle=(math.pi/h)*(2*math.pi/w)*np.sin(theta)[:,None]
residual=np.where(mask[:,:,None],np.maximum(0,pixels[:,:,:3]-32),0)
flux=(residual*solid_angle[:,:,None]).sum(axis=(0,1))
weights=np.sum(residual*[.2126,.7152,.0722],axis=2)*solid_angle
direction=np.array([-(weights*np.sin(theta)[:,None]*np.cos(phi)[None,:]).sum(),
                    (weights*np.cos(theta)[:,None]).sum(),
                    (weights*np.sin(theta)[:,None]*np.sin(phi)[None,:]).sum()])
if np.linalg.norm(direction)>0:direction/=np.linalg.norm(direction)
else:direction=np.array([0.,1.,0.])
report['sun_direction']=direction.tolist()
report['sun_color']=(flux/max(flux)).tolist() if max(flux)>0 else [1.,1.,1.]
report['sun_intensity']=float(max(flux)*report['world_strength']/math.pi)
pixels[:,:,:3]-=residual
sky=bpy.data.images.new('Diffuse sky without solar disc',width=w,height=h,alpha=False,float_buffer=True)
sky.colorspace_settings.name='Linear Rec.709';sky.pixels.foreach_set(pixels.ravel())
sky.filepath_raw=str(directory/'DiffuseSky.hdr');sky.file_format='HDR';sky.save()
samples=[]
for y in range(32):
    for x in range(64):
        color=pixels[y*h//32:(y+1)*h//32,x*w//64:(x+1)*w//64,:3].mean(axis=(0,1))
        t=(1-(y+.5)/32)*math.pi;p=((x+.5)/64-.5)*2*math.pi
        samples.append(dict(direction=[-math.cos(p)*math.sin(t),math.cos(t),math.sin(p)*math.sin(t)],radiance=color.tolist(),
                            solid_angle=2*math.pi/64*(math.cos(math.pi*y/32)-math.cos(math.pi*(y+1)/32))))
report['samples']=samples
report['diffuse_hdr']='DiffuseSky.hdr'
path.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('VR_SUN',report['sun_direction'],report['sun_intensity'],report['sun_color'],int(mask.sum()),float(luminance.max()),flush=True)
sys.stdout.flush();os._exit(0)
