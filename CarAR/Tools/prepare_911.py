import json,struct,numpy as np
from pathlib import Path
p=Path(__file__).resolve().parents[1]/'Assets/Models/911.glb'
b=p.read_bytes(); size=struct.unpack_from('<I',b,12)[0]; d=json.loads(b[20:20+size]); binary=b[28+size:]
def acc(i):
 a=d['accessors'][i]; v=d['bufferViews'][a['bufferView']]; dt={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]; nc={'VEC3':3,'SCALAR':1,'VEC2':2,'VEC4':4}[a['type']]
 return np.ndarray((a['count'],nc),dtype=dt,buffer=binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',np.dtype(dt).itemsize*nc),np.dtype(dt).itemsize))

# Derived display rig: keep every original triangle and material, partition only indices.
# This is a spatial approximation, not a manufacturer-authored mechanical rig.
import copy,hashlib
out=copy.deepcopy(d)
blob=bytearray(binary)
def add_indices(indices):
 while len(blob)%4: blob.append(0)
 offset=len(blob); data=np.asarray(indices,dtype='<u4').tobytes(); blob.extend(data)
 vi=len(out['bufferViews']); out['bufferViews'].append({'buffer':0,'byteOffset':offset,'byteLength':len(data),'target':34963})
 ai=len(out['accessors']); out['accessors'].append({'bufferView':vi,'componentType':5125,'count':len(indices),'type':'SCALAR','min':[int(np.min(indices))],'max':[int(np.max(indices))]})
 return ai
root=76
panels={}
for name,pivot in [('DoorLeft',[-.77,-.05,.70]),('DoorRight',[.74,-.05,.70]),('FrontTrunk',[-.015,.23,.86])]:
 index=len(out['nodes']); out['nodes'].append({'name':name,'translation':pivot,'children':[]}); out['nodes'][root]['children'].append(index); panels[name]=(index,pivot)
counts={name:0 for name in panels}
for ni,n in enumerate(d['nodes']):
 if 'mesh' not in n: continue
 mi=n['mesh']; pr=d['meshes'][mi]['primitives'][0]
 vs=acc(pr['attributes']['POSITION']); ix=acc(pr['indices']).ravel().reshape(-1,3); centers=vs[ix].mean(axis=1)
 x,y,z=centers.T
 # Door skins, handles, mirrors and forward side glass, excluding fixed rear quarter glass.
 side=(abs(x+.015)>.69)&(y>-.385)&(y<.55)&(z>-.50)&(z<.77)
 # Narrow upper glass follows the opening door, not the rear fixed window.
 glass=np.isin(mi,[3,7])&(abs(x+.015)>.53)&(y>.20)&(z>-.43)&(z<.55)
 hood=np.isin(mi,[6,9,10])&(abs(x+.015)<.66)&(y>-.075)&(y<.25)&(z>.86)&(z<2.08)
 masks={'DoorLeft':(side|glass)&(x<-.015),'DoorRight':(side|glass)&(x>=-.015),'FrontTrunk':hood}
 used=np.zeros(len(ix),dtype=bool)
 for name,mask in masks.items():
  mask &= ~used
  if not mask.any(): continue
  used |= mask; indices=ix[mask].ravel(); counts[name]+=int(mask.sum())
  newpr=copy.deepcopy(pr); newpr['indices']=add_indices(indices)
  newmi=len(out['meshes']); out['meshes'].append({'name':name+'_'+n['name'],'primitives':[newpr]})
  pi,pivot=panels[name]; ci=len(out['nodes']); out['nodes'].append({'name':name+'_'+n['name'],'mesh':newmi,'translation':[-a for a in pivot]}); out['nodes'][pi]['children'].append(ci)
 remaining=ix[~used].ravel()
 if len(remaining): out['meshes'][mi]['primitives'][0]['indices']=add_indices(remaining)
 else: del out['nodes'][ni]['mesh']
 # Prove lossless partition: no triangle is duplicated or discarded.
 assert len(remaining)//3+int(used.sum())==len(ix)
assert all(v>1000 for v in counts.values()),counts
out['buffers'][0]['byteLength']=len(blob)
out.setdefault('asset',{})['extras']={'CarAR':'Approximate spatial panel split from original 911.glb. Original geometry and materials retained; not a mechanical CAD rig.'}
j=json.dumps(out,separators=(',',':')).encode(); j+=b' '*((-len(j))%4); blob+=b'\0'*((-len(blob))%4)
result=struct.pack('<III',0x46546c67,2,28+len(j)+len(blob))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(blob),0x004e4942)+blob
output=p.with_name('911-interactive.glb'); output.write_bytes(result)
print('Created',output,counts,'original sha256',hashlib.sha256(b).hexdigest())
