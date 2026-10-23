"""Read-only geometry audit of the regenerated shoulder plates."""
import bpy, bmesh, json, sys
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
import reforge_characters as api
import sculpt_reference as sculpt

root=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(root/'assets/human-b-short.blend'),use_scripts=False)
for obj in list(bpy.context.scene.objects):
    if obj.type!='MESH':bpy.data.objects.remove(obj,do_unlink=True)
rig,joints=api.riglib.skeleton('b','human')
api.SCALE=Vector((1,1,1));api.materials();sculpt.A=api
sculpt.costume('b',joints)
reports=[]
for obj in api.PARTS:
    if obj.name not in ('Jade raised wing pauldron','Jade pauldron lower leaf'):continue
    api.repair_generated_surface_normals([obj]);obj.data.calc_loop_triangles()
    bm=bmesh.new();bm.from_mesh(obj.data)
    tree=BVHTree.FromPolygons([v.co for v in obj.data.vertices],[tuple(p.vertices) for p in obj.data.polygons])
    front_count=obj['plateFrontFaceCount'];thickness=[];missing=[]
    back_tree=BVHTree.FromPolygons([v.co for v in obj.data.vertices],[tuple(p.vertices) for p in list(obj.data.polygons)[front_count:]])
    for poly in list(obj.data.polygons)[:front_count]:
        origin=poly.center+Vector((0,.00001,0))
        hit,normal,index,distance=back_tree.ray_cast(origin,Vector((0,1,0)),.1)
        if hit is None:missing.append(poly.index)
        else:thickness.append(distance)
    reports.append({'part':obj.name,'boundaryEdges':sum(e.is_boundary for e in bm.edges),
        'signedVolume':bm.calc_volume(signed=True),'frontCount':front_count,
        'frontNormalsY':[min(p.normal.y for p in list(obj.data.polygons)[:front_count]),max(p.normal.y for p in list(obj.data.polygons)[:front_count])],
        'frontWithoutBackHit':missing,'backThicknessRange':[min(thickness),max(thickness)],
        'frontCornerNormalsY':[min(obj.data.corner_normals[i].vector.y for p in list(obj.data.polygons)[:front_count] for i in p.loop_indices),max(obj.data.corner_normals[i].vector.y for p in list(obj.data.polygons)[:front_count] for i in p.loop_indices)]})
    bm.free()
print('PAULDRON_AUDIT',json.dumps(reports),flush=True)
