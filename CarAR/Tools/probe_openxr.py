"""Read the active PC OpenXR runtime; does not change system settings."""
import ctypes as c,json,os
from pathlib import Path
loader=next((Path(__file__).resolve().parents[1]/'Library/PackageCache').glob('com.unity.xr.openxr*/RuntimeLoaders/windows/x64/openxr_loader.dll'))
xr=c.WinDLL(str(loader))
class Extension(c.Structure): _fields_=[('type',c.c_int),('next',c.c_void_p),('name',c.c_char*128),('version',c.c_uint)]
f=xr.xrEnumerateInstanceExtensionProperties; f.argtypes=[c.c_char_p,c.c_uint,c.POINTER(c.c_uint),c.POINTER(Extension)]; f.restype=c.c_int
n=c.c_uint(); code=f(None,0,c.byref(n),None); a=(Extension*n.value)()
for e in a:e.type=2
code=f(None,n,c.byref(n),a)
result={'extensionStatus':code,'extensions':[{'name':e.name.decode(),'version':e.version} for e in a]}
class App(c.Structure): _fields_=[('applicationName',c.c_char*128),('applicationVersion',c.c_uint),('engineName',c.c_char*128),('engineVersion',c.c_uint),('apiVersion',c.c_uint64)]
class Create(c.Structure): _fields_=[('type',c.c_int),('next',c.c_void_p),('flags',c.c_uint64),('app',App),('layerCount',c.c_uint),('layers',c.c_void_p),('extCount',c.c_uint),('exts',c.c_void_p)]
class GetSystem(c.Structure): _fields_=[('type',c.c_int),('next',c.c_void_p),('formFactor',c.c_int)]
class Props(c.Structure): _fields_=[('type',c.c_int),('next',c.c_void_p),('id',c.c_uint64),('vendorId',c.c_uint),('name',c.c_char*256),('maxHeight',c.c_uint),('maxWidth',c.c_uint),('maxLayers',c.c_uint),('orientation',c.c_uint),('position',c.c_uint)]
instance=c.c_void_p(); ci=Create(); ci.type=3; ci.app.applicationName=b'CarAR capability probe'; ci.app.apiVersion=(1<<48)|34
xr.xrCreateInstance.argtypes=[c.POINTER(Create),c.POINTER(c.c_void_p)]
result['createInstanceStatus']=xr.xrCreateInstance(c.byref(ci),c.byref(instance))
if result['createInstanceStatus']==0:
 system=c.c_uint64(); gi=GetSystem(); gi.type=4; gi.formFactor=1
 xr.xrGetSystem.argtypes=[c.c_void_p,c.POINTER(GetSystem),c.POINTER(c.c_uint64)]
 result['getSystemStatus']=xr.xrGetSystem(instance,c.byref(gi),c.byref(system))
 if result['getSystemStatus']==0:
  props=Props(); props.type=5
  xr.xrGetSystemProperties.argtypes=[c.c_void_p,c.c_uint64,c.POINTER(Props)]
  result['systemPropertiesStatus']=xr.xrGetSystemProperties(instance,system,c.byref(props)); result['systemName']=props.name.decode()
  xr.xrEnumerateEnvironmentBlendModes.argtypes=[c.c_void_p,c.c_uint64,c.c_int,c.c_uint,c.POINTER(c.c_uint),c.POINTER(c.c_int)]
  count=c.c_uint(); code=xr.xrEnumerateEnvironmentBlendModes(instance,system,2,0,c.byref(count),None)
  modes=(c.c_int*count.value)(); xr.xrEnumerateEnvironmentBlendModes(instance,system,2,count,c.byref(count),modes)
  result['environmentBlendModes']=[{1:'opaque',2:'additive',3:'alphaBlend'}.get(v,v) for v in modes]
 xr.xrDestroyInstance.argtypes=[c.c_void_p]; xr.xrDestroyInstance(instance)
output=Path(__file__).resolve().parents[1]/'device-capabilities.json'; output.write_text(json.dumps(result,indent=2)); print(json.dumps(result,indent=2),flush=True)
# Some vendor DLLs have unstable interpreter-unload hooks. All OpenXR objects were destroyed above.
os._exit(0)
