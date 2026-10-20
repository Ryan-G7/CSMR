import bpy, json
from mathutils import Vector
from pathlib import Path

result = []
names = ['Cube.002', 'Cube.007', 'Cube.009', 'Cube.010', 'Cube.011', 'Cube.017', 'Cube.029', 'Cube.063',
         'Cube.070', 'Cube.074', 'Cube.076', 'Cube.077', 'Cube.080', 'Cube.083', 'Cube.088', 'Cube.106',
         'Cube.139', 'Cube.140', 'Cube.141', 'Cube.142']
deps = bpy.context.evaluated_depsgraph_get()
for name in names:
    obj = bpy.data.objects[name]
    # Mirror topology only; subdivision does not alter connected components.
    for modifier in obj.modifiers:
        if modifier.type == 'SUBSURF': modifier.show_viewport = False
    bpy.context.view_layer.update()
    ev = obj.evaluated_get(deps)
    mesh = ev.to_mesh()
    parent = list(range(len(mesh.vertices)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for edge in mesh.edges:
        a, b = (find(i) for i in edge.vertices)
        parent[b] = a
    groups = {}
    for vertex in mesh.vertices: groups.setdefault(find(vertex.index), []).append(obj.matrix_world @ vertex.co)
    components = []
    for vs in groups.values():
        if len(vs) < 4: continue
        components.append({'n': len(vs), 'min': [round(min(v[i] for v in vs), 4) for i in range(3)],
                           'max': [round(max(v[i] for v in vs), 4) for i in range(3)]})
    result.append({'name': name, 'components': sorted(components, key=lambda c: -c['n'])})
    ev.to_mesh_clear()
Path(__file__).resolve().parents[1].joinpath('tmp/amg-components.json').write_text(json.dumps(result, indent=2))
for item in result:
    print(item['name'], len(item['components']), item['components'][:8])
