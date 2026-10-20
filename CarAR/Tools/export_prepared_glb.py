"""Fresh Blender worker for prepare_amg.py; does not bake or save any .blend."""
import sys
import os
import json
import struct
from pathlib import Path
import bpy

output = Path(sys.argv[sys.argv.index('--') + 1])
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.gltf(filepath=str(output), export_format='GLB', use_selection=True,
    export_animations=False, export_yup=True, export_materials='EXPORT', export_extras=True,
    export_normals=True, export_tangents=True, export_apply=True)
assert output.exists(), 'glTF exporter did not produce an output'
data = output.read_bytes()
assert struct.unpack_from('<4sII', data) == (b'glTF', 2, len(data)), 'Invalid GLB header'
gltf = json.loads(data[20:20 + struct.unpack_from('<I', data, 12)[0]])
assert gltf['meshes'] and gltf['materials'], 'GLB has no mesh/material payload'
# Blender 5.2's native glTF/image cleanup crashes after a completed export on
# this Windows installation, even in a fresh process. This disposable worker has
# no unsaved user state. Bypass that teardown only AFTER export and validation;
# exceptions above retain --python-exit-code=1 and never publish the result.
sys.stdout.flush()
sys.stderr.flush()
os._exit(0)
