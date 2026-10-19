import ctypes as c,json,os
from pathlib import Path
p=Path('D:/Soft/AIXR Agent/resource/tool/api_layer/Release')
cookie=os.add_dll_directory(str(p)); sdk=c.CDLL(str(p/'jxrsdk.dll'))
f=sdk.jxr_hjxrGetInterface; f.argtypes=[c.c_uint,c.c_uint]; f.restype=c.c_void_p
t=f(5,0); result={'dll':str(p/'jxrsdk.dll'),'interfaceAvailable':bool(t)}
if t:
 ptr=c.cast(t,c.POINTER(c.c_void_p)); getver=c.CFUNCTYPE(c.c_char_p)(ptr[3]); result['sdkVersion']=getver().decode(errors='replace')
 init=c.CFUNCTYPE(c.c_void_p)(ptr[0]); destroy=c.CFUNCTYPE(None,c.c_void_p)(ptr[1]); io=c.CFUNCTYPE(c.c_int,c.c_void_p,c.c_int,c.c_void_p,c.c_void_p)(ptr[2]); session=init();result['sessionCreated']=bool(session)
 if session:
  result['note']='Standalone SDK init does not activate MR. Launch the OpenXR API layer before using WorldSync or SetLayerEnable. GET layer state is unimplemented in this installed DLL.';destroy(session)
Path('CarAR/hjxr-probe.json').write_text(json.dumps(result,indent=2)); print(json.dumps(result,indent=2),flush=True)
