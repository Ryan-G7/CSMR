"""Export AMG.blend to a portable, articulated GLB. Never save over the source.

Run: blender --background --factory-startup --threads 1 ../AMG.blend --python-exit-code 1 --python Tools/prepare_amg.py
The supplied car uses Mirror/Subdivision modifiers. Separate the already-disjoint
left/right islands of its door assemblies, without cutting any source triangles.
"""
import bpy
import json
import hashlib
import sys
import subprocess
import tempfile
import struct
from pathlib import Path
from mathutils import Vector, Matrix

PROJECT = Path(__file__).resolve().parents[1]
OUTPUT = PROJECT / 'Assets/Models/AMG-interactive.glb'
DOOR_SOURCES = {'Cube.002', 'Cube.135', 'Cube.013', 'Cube.017', 'Cube.083', 'Cube.106',
                'Cube.139', 'Cube.140', 'Cube.141', 'Cube.142'}
# Cube.092 contains two 109-vertex door trim islands AND centre-console pieces.
# Keep the console static; match only the audited disconnected trim geometry.
MIXED_DOOR_SOURCE = 'Cube.092'
# Audited against the current source's individual geometry: door cards, armrests,
# speaker grilles, window switches and inner seals. Roof/jamb seals, seat rails,
# seat controls and dashboard pieces are deliberately absent from this manifest.
DOOR_INTERIORS = {
    'DoorLeft': {f'Cube.{i:03d}' for i in [*range(129, 135), *range(325, 393), *range(427, 431)]},
    'DoorRight': {f'Cube.{i:03d}' for i in [*range(124, 129), *range(298, 325), *range(416, 419)]},
}
DOOR_INTERIOR_SOURCES = set.union(*DOOR_INTERIORS.values())
HATCH_SOURCES = {'Cube.007', 'Cube.009', 'Cube.010', 'Cube.011', 'Cube.029'}
HOOD_SOURCES = {'Cube.074'}
REPORT = {'source': 'AMG.blend', 'source_sha256': hashlib.sha256(Path(bpy.data.filepath).read_bytes()).hexdigest(),
          'units': 'metres', 'assemblies': {}, 'meshes': []}

def assembly(name, pivot=(0, 0, 0)):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = pivot
    REPORT['assemblies'][name] = {'blender_pivot': list(pivot), 'sources': set()}
    return obj

roots = {
    'DoorLeft': assembly('DoorLeft', (.84, -.39, .80)),
    'DoorRight': assembly('DoorRight', (-.846, -.39, .80)),
    'RearHatch': assembly('RearHatch', (-.003, 1.01, 1.28)),
    'Hood': assembly('Hood', (-.003, -.435, .92)),
    'Body': assembly('Body'),
}
for end in ('Ft', 'Bk'):
    for side in ('L', 'R'):
        key = 'Wheel' + end + side
        roots[key] = assembly(key)

# Blender owns appearance. Bake unsupported input graphs and export the authored
# layered paint separately; do not add application-specific colour/finish presets.
sys.path.insert(0, str(Path(__file__).resolve().parent))
from export_blender_materials import export_materials, export_environment
REPORT.update(export_materials(PROJECT))
REPORT['environment'] = export_environment(PROJECT)

# Bake modifiers into portable geometry. Two subdivision levels retain smooth
# body silhouettes; interior and dense wheel components use one for XR frame time.
objects = [o for o in list(bpy.data.objects) if o.type == 'MESH' and not o.name.startswith('WGT-')]
source_matrices = {o.name: o.matrix_world.copy() for o in objects}
for obj in list(bpy.data.objects):
    if obj.name.startswith('WGT-'): bpy.data.objects.remove(obj, do_unlink=True)
for obj in objects:
    for modifier in obj.modifiers:
        if modifier.type == 'SUBSURF':
            exterior = obj.name in DOOR_SOURCES | HATCH_SOURCES | HOOD_SOURCES | {'Cube.001', 'Cube.004', 'Cube.005', 'Cube.018', 'Cube.019'}
            modifier.levels = min(modifier.levels, 2 if exterior else 1)
            modifier.render_levels = modifier.levels
    world = source_matrices[obj.name]
    deps = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(deps)
    mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=deps)
    obj.modifiers.clear()
    obj.data = mesh
    obj.parent = None
    obj.matrix_world = Matrix.Identity(4)
    mesh.transform(world)
    mesh.update()

def island_objects(obj):
    """Use only pre-existing disconnected topology; retain UVs and normals."""
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.separate(type='LOOSE')
    bpy.ops.object.mode_set(mode='OBJECT')
    return list(bpy.context.selected_objects)

def bounds(obj):
    verts = [obj.matrix_world @ v.co for v in obj.data.vertices]
    return Vector([min(v[i] for v in verts) for i in range(3)]), Vector([max(v[i] for v in verts) for i in range(3)])

def attach(obj, key, source):
    world = obj.matrix_world.copy()
    obj.parent = roots[key]
    obj.matrix_world = world
    REPORT['assemblies'][key]['sources'].add(source)
    obj.name = source + '__' + key
    REPORT['meshes'].append({'name': obj.name, 'source': source, 'assembly': key,
                            'vertices': len(obj.data.vertices), 'polygons': len(obj.data.polygons)})

# Only verified door assemblies move. Mixed interior meshes also contain seats
# and dashboard parts; position-based guesses incorrectly attach them to doors.
for obj in objects:
    source = obj.name
    interior = next((key for key, sources in DOOR_INTERIORS.items() if source in sources), None)
    if interior is not None:
        lo, hi = bounds(obj)
        assert lo.x > .7 if interior == 'DoorLeft' else hi.x < -.7, (source, list(lo), list(hi))
        attach(obj, interior, source)
    elif source in DOOR_SOURCES:
        pieces = island_objects(obj)
        for piece in pieces:
            lo, hi = bounds(piece)
            assert lo.x > .5 or hi.x < -.5, (source, list(lo), list(hi))
            attach(piece, 'DoorLeft' if lo.x > .5 else 'DoorRight', source)
    elif source == MIXED_DOOR_SOURCE:
        counts = {'DoorLeft': 0, 'DoorRight': 0, 'Body': 0}
        for piece in island_objects(obj):
            lo, hi = bounds(piece)
            key = 'DoorLeft' if lo.x > .70 else 'DoorRight' if hi.x < -.72 else 'Body'
            if key != 'Body':
                assert len(piece.data.vertices) == 109 and -.025 < lo.y < 0 and .58 < hi.y < .59 and .55 < lo.z < hi.z < .61, (source, list(lo), list(hi))
            else:
                assert lo.x > -.22 and hi.x < .20, (source, list(lo), list(hi))
            attach(piece, key, source)
            counts[key] += 1
        assert counts == {'DoorLeft': 1, 'DoorRight': 1, 'Body': 2}, counts
    elif source in HATCH_SOURCES: attach(obj, 'RearHatch', source)
    elif source in HOOD_SOURCES: attach(obj, 'Hood', source)
    elif source.startswith(('mycar-wheel.', 'mycar-wheelbrake.')):
        tokens = source.split('.')
        attach(obj, 'Wheel' + tokens[1] + tokens[2], source)
    else: attach(obj, 'Body', source)

# The current authored file also contains the already-separated right shell
# Cube.135 and hundreds of loose interior details. Batch those static details by
# material; preserve panel/wheel assemblies and record all original source names.
groups = {}
for obj in list(bpy.data.objects):
    if obj.type == 'MESH':
        source = next(m['source'] for m in REPORT['meshes'] if m['name'] == obj.name)
        suffix = source.removeprefix('Cube.')
        detail = obj.parent.name == 'Body' and (source in {'Cube.063', 'Cube.076', 'Cube.077', 'Cube.080'} or
                    source.startswith('Cube.') and suffix.isdigit() and int(suffix) >= 143)
        materials = '_'.join(m.name if m else 'Default' for m in obj.data.materials)
        key = 'DoorInterior_' + materials if source in DOOR_INTERIOR_SOURCES else 'InteriorDetail_' + materials if detail else source
        groups.setdefault((key, obj.parent.name), []).append((obj, source))
REPORT['meshes'] = []
for (source, parent), pieces in groups.items():
    sources = sorted({name for _, name in pieces})
    if len(pieces) > 1:
        bpy.ops.object.select_all(action='DESELECT')
        for obj, _ in pieces: obj.select_set(True)
        bpy.context.view_layer.objects.active = pieces[0][0]
        bpy.ops.object.join()
    obj = pieces[0][0]
    obj.name = source + '__' + parent
    REPORT['meshes'].append({'name': obj.name, 'sources': sources, 'assembly': parent,
                            'vertices': len(obj.data.vertices), 'polygons': len(obj.data.polygons)})

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
lo = Vector([min(bounds(o)[0][i] for o in meshes) for i in range(3)])
hi = Vector([max(bounds(o)[1][i] for o in meshes) for i in range(3)])
REPORT['size_blender_xyz_m'] = list(hi - lo)
REPORT['exported_mesh_count'] = len(meshes)
REPORT['bounds_blender_min'] = list(lo)
REPORT['bounds_blender_max'] = list(hi)
REPORT['triangles'] = sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
for value in REPORT['assemblies'].values(): value['sources'] = sorted(value['sources'])
assert 4.4 < hi.y-lo.y < 5.0 and 1.8 < hi.x-lo.x < 2.3
assert all(REPORT['assemblies'][key]['sources'] for key in roots)
assert all(set(REPORT['assemblies'][key]['sources']) <= DOOR_SOURCES | DOOR_INTERIORS[key] | {MIXED_DOOR_SOURCE} for key in DOOR_INTERIORS)
assert all(DOOR_INTERIORS[key] <= set(REPORT['assemblies'][key]['sources']) for key in DOOR_INTERIORS)
assert set(REPORT['assemblies']['DoorLeft']['sources']) | set(REPORT['assemblies']['DoorRight']['sources']) == DOOR_SOURCES | DOOR_INTERIOR_SOURCES | {MIXED_DOOR_SOURCE}
assert 'source_paint' in REPORT, 'Source car paint material is missing'

# Blender cannot calculate tangent frames on every source n-gon. Triangulate
# through its native modifier while retaining authored split normals and UVs.
for obj in meshes:
    triangulate = obj.modifiers.new('Portable tangent triangulation', 'TRIANGULATE')
    triangulate.keep_custom_normals = True

bpy.ops.object.select_all(action='SELECT')
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
# Isolate Blender 5.2's native glTF/image teardown in a disposable worker. The
# intermediate is a disposable copy; the authored source is never saved over.
(PROJECT / 'tmp').mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='amg-export-', dir=PROJECT / 'tmp') as work:
    prepared = Path(work) / 'prepared.blend'
    result = Path(work) / 'prepared.glb'
    bpy.ops.wm.save_as_mainfile(filepath=str(prepared), copy=True, check_existing=False)
    subprocess.run([bpy.app.binary_path, '--background', '--factory-startup', '--threads', '1',
                    str(prepared), '--python-exit-code', '1', '--python',
                    str(Path(__file__).with_name('export_prepared_glb.py')), '--', str(result)], check=True)
    data = result.read_bytes()
    assert struct.unpack_from('<4sII', data) == (b'glTF', 2, len(data)), 'Invalid GLB header'
    gltf = json.loads(data[20:20 + struct.unpack_from('<I', data, 12)[0]])
    primitives = [p for mesh in gltf['meshes'] for p in mesh['primitives']]
    assert all('NORMAL' in p['attributes'] and 'material' in p for p in primitives)
    assert all('TANGENT' in p['attributes'] for p in primitives if 'TEXCOORD_0' in p['attributes'])
    REPORT['gltf_primitives'] = len(primitives)
    REPORT['gltf_materials'] = len(gltf['materials'])
    result.replace(OUTPUT)
OUTPUT.with_suffix('.json').write_text(json.dumps(REPORT, ensure_ascii=False, indent=2), encoding='utf-8')
print('AMG_EXPORT', REPORT['exported_mesh_count'], 'meshes', REPORT['triangles'], 'triangles', REPORT['size_blender_xyz_m'])
# Release bake/export data while Blender's image libraries are still loaded.
bpy.ops.wm.read_factory_settings(use_empty=True)
