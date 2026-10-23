"""Read-only audit of the source head openings."""
import bpy,bmesh,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
for model in ('human-a','human-b'):
    bpy.ops.wm.open_mainfile(filepath=str(root/'assets'/f'{model}-short.blend'),use_scripts=False)
    obj=bpy.data.objects['Head'];bm=bmesh.new();bm.from_mesh(obj.data)
    boundary={e for e in bm.edges if e.is_boundary};loops=[]
    while boundary:
        edge=boundary.pop();stack=[edge];component={edge}
        while stack:
            current=stack.pop()
            for vertex in current.verts:
                for other in vertex.link_edges:
                    if other in boundary:
                        boundary.remove(other);component.add(other);stack.append(other)
        vertices={v for e in component for v in e.verts}
        loops.append({'edges':len(component),'vertices':len(vertices),
                      'bounds':[[min(v.co[k] for v in vertices) for k in range(3)],
                                [max(v.co[k] for v in vertices) for k in range(3)]]})
    print('HEAD_BOUNDARY',model,json.dumps(loops),flush=True);bm.free()
