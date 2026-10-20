"""Export AMG.blend to a portable, articulated GLB. Never save over the source.

Run: blender --background --factory-startup ../AMG.blend --python Tools/prepare_amg.py
The supplied car uses Mirror/Subdivision modifiers. Separate the already-disjoint
left/right islands of its door assemblies, without cutting any source triangles.
"""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector, Matrix

PROJECT = Path(__file__).resolve().parents[1]
OUTPUT = PROJECT / 'Assets/Models/AMG-interactive.glb'
DOOR_SOURCES = {'Cube.002', 'Cube.135', 'Cube.017', 'Cube.083', 'Cube.106',
                'Cube.139', 'Cube.140', 'Cube.141', 'Cube.142'}
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
REPORT = {'source': 'AMG.blend', 'units': 'metres', 'assemblies': {}, 'meshes': []}

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

# Translate unsupported Blender node groups into standard glTF PBR materials.
# Native principled materials keep their texture connections, including tyre normals.
def principled(mat, color, metallic, roughness, alpha=1, emission=None):
    mat.use_nodes = True
    mat.node_tree.nodes.clear()
    node = mat.node_tree.nodes.new('ShaderNodeBsdfPrincipled')
    out = mat.node_tree.nodes.new('ShaderNodeOutputMaterial')
    mat.node_tree.links.new(node.outputs['BSDF'], out.inputs['Surface'])
    node.inputs['Base Color'].default_value = (*color, alpha)
    node.inputs['Alpha'].default_value = alpha
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Roughness'].default_value = roughness
    if alpha < 1:
        mat.surface_render_method = 'DITHERED'
        # glTF exporter reads the Alpha input; Unity uses native transparent PBR.
    if emission:
        node.inputs['Emission Color'].default_value = (*emission, 1)
        node.inputs['Emission Strength'].default_value = 1
    return node

for mat in list(bpy.data.materials):
    if mat.name == 'Black Carpaint':
        group = mat.node_tree.nodes.get('Carpaint')
        assert group is not None and group.type == 'GROUP', 'Expected authored Carpaint shader group'
        color = tuple(group.inputs['Color'].default_value)
        assert len(color) == 4 and all(math.isfinite(c) and 0 <= c <= 1 for c in color), color
        matte = max(0, min(1, group.inputs['Mateness'].default_value))
        # Blender's procedural layered shader cannot travel through glTF. Preserve
        # its authored linear colour and describe one portable coated-metal finish.
        REPORT['paint'] = {'color_linear': list(color), 'metallic': .9,
                           'roughness': .2 + matte * .5, 'clearcoat': 1,
                           'clearcoat_roughness': .06 + matte * .2}
        node = principled(mat, color[:3], REPORT['paint']['metallic'], REPORT['paint']['roughness'], color[3])
        node.inputs['Coat Weight'].default_value = REPORT['paint']['clearcoat']
        node.inputs['Coat Roughness'].default_value = REPORT['paint']['clearcoat_roughness']
    elif mat.name in {'Leather Black', 'Leather Red'}:
        color = (.012, .012, .014) if mat.name == 'Leather Black' else (.025099, .304892, .327911)
        node = principled(mat, color, 0, .5)
        image = bpy.data.images.get('MAML_int_Leather_N.jpg.001')
        if image:
            texture = mat.node_tree.nodes.new('ShaderNodeTexImage'); texture.image = image
            image.colorspace_settings.name = 'Non-Color'
            normal = mat.node_tree.nodes.new('ShaderNodeNormalMap'); normal.inputs['Strength'].default_value = .35
            mat.node_tree.links.new(texture.outputs['Color'], normal.inputs['Color'])
            mat.node_tree.links.new(normal.outputs['Normal'], node.inputs['Normal'])
    elif mat.name == 'Carpat':
        node = principled(mat, (.022, .025, .028), 0, .95)
        image = bpy.data.images.get('Fabric_carpet_01_basecolor.jpg.001')
        if image:
            texture = mat.node_tree.nodes.new('ShaderNodeTexImage'); texture.image = image
            mat.node_tree.links.new(texture.outputs['Color'], node.inputs['Base Color'])
    elif mat.name == 'Windshield': principled(mat, (.16, .21, .23), .06, .09, .24)
    elif mat.name == 'HeadLight Glass': principled(mat, (.65, .69, .72), .05, .08, .12)
    elif mat.name in {'mirror', 'Mirror'}: principled(mat, (.72, .76, .8), 1, .06)
    elif 'Led Tube' in mat.name or mat.name == 'indicatores':
        color = (.7, .015, .01) if 'Tailight' in mat.name else (1, .32, .03) if mat.name == 'indicatores' else (.7, .8, 1)
        principled(mat, color, .1, .25, emission=tuple(c * .4 for c in color))
    elif mat.name == 'Rim':
        # The original rim uses black metal and roughness=1, which loses all shape in MR.
        node = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        node.inputs['Base Color'].default_value = (.12, .13, .14, 1)
        node.inputs['Roughness'].default_value = .25

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
assert all(set(REPORT['assemblies'][key]['sources']) <= DOOR_SOURCES | DOOR_INTERIORS[key] for key in DOOR_INTERIORS)
assert all(DOOR_INTERIORS[key] <= set(REPORT['assemblies'][key]['sources']) for key in DOOR_INTERIORS)
assert set(REPORT['assemblies']['DoorLeft']['sources']) | set(REPORT['assemblies']['DoorRight']['sources']) == DOOR_SOURCES | DOOR_INTERIOR_SOURCES
assert 'paint' in REPORT, 'Source car paint material is missing'

# Blender cannot calculate tangent frames on every source n-gon. Triangulate
# through its native modifier while retaining authored split normals and UVs.
for obj in meshes:
    triangulate = obj.modifiers.new('Portable tangent triangulation', 'TRIANGULATE')
    triangulate.keep_custom_normals = True

bpy.ops.object.select_all(action='SELECT')
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(OUTPUT), export_format='GLB', use_selection=True,
    export_animations=False, export_yup=True, export_materials='EXPORT', export_extras=True,
    export_normals=True, export_tangents=True, export_apply=True)
OUTPUT.with_suffix('.json').write_text(json.dumps(REPORT, ensure_ascii=False, indent=2), encoding='utf-8')
print('AMG_EXPORT', REPORT['exported_mesh_count'], 'meshes', REPORT['triangles'], 'triangles', REPORT['size_blender_xyz_m'])
