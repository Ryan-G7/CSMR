import json,struct,numpy as np
from pathlib import Path
p=Path(__file__).resolve().parents[1]/'Assets/Models/911.glb'
b=p.read_bytes(); size=struct.unpack_from('<I',b,12)[0]; d=json.loads(b[20:20+size]); binary=b[28+size:]
def acc(i):
 a=d['accessors'][i]; v=d['bufferViews'][a['bufferView']]; dt={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]; nc={'VEC3':3,'SCALAR':1,'VEC2':2,'VEC4':4}[a['type']]
 return np.ndarray((a['count'],nc),dtype=dt,buffer=binary,offset=v.get('byteOffset',0)+a.get('byteOffset',0),strides=(v.get('byteStride',np.dtype(dt).itemsize*nc),np.dtype(dt).itemsize))
for mi,m in enumerate(d['meshes']):
 pr=m['primitives'][0]; vs=acc(pr['attributes']['POSITION']); ix=acc(pr['indices']).ravel().reshape(-1,3)
 _,inv=np.unique(np.round(vs,5),axis=0,return_inverse=True); parent=list(range(inv.max()+1))
 def root(i):
  while parent[i]!=i: parent[i]=parent[parent[i]]; i=parent[i]
  return i
 for tri in inv[ix]:
  a=root(tri[0]); parent[root(tri[1])]=a; parent[root(tri[2])]=a
 groups={}
 for ti,tri in enumerate(ix): groups.setdefault(root(inv[tri[0]]),[]).append(ti)
 comps=[]
 for ts in groups.values():
  if len(ts)<60: continue
  v=vs[ix[ts].ravel()]; comps.append((len(ts),np.round(v.min(0),3).tolist(),np.round(v.max(0),3).tolist()))
 print(mi,m['name'],'triangles',len(ix),'components',len(groups),'large',sorted(comps,reverse=True)[:24])
