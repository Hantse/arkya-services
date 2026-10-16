"""Read-only Blender and glTF checks, with a compact validation report."""
import hashlib
import json
from pathlib import Path
import struct

import bmesh
import bpy
import numpy as np


ROOT=Path(__file__).resolve().parents[1]


def mesh_fingerprint(obj):
    positions=np.empty(len(obj.data.vertices)*3,dtype=np.float32);obj.data.vertices.foreach_get('co',positions)
    loops=np.empty(len(obj.data.loops),dtype=np.int32);obj.data.loops.foreach_get('vertex_index',loops)
    digest=hashlib.sha256(positions.tobytes()+loops.tobytes())
    for layer in obj.data.uv_layers:
        uv=np.empty(len(layer.data)*2,dtype=np.float32);layer.data.foreach_get('uv',uv);digest.update(uv.tobytes())
    return digest.hexdigest(),positions.reshape((-1,3))


def main():
    manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
    assert len(manifest['assets'])==12,'Expected four shapes in three materials'
    shapes={};results=[]
    for asset in manifest['assets']:
        bpy.ops.wm.open_mainfile(filepath=str(ROOT/asset['blend']))
        objects=[o for o in bpy.data.objects if o.type=='MESH' and o.get('Arkya_family')=='cailloux']
        assert len(objects)==1
        obj=objects[0];digest,points=mesh_fingerprint(obj)
        if asset['shape'] in shapes:assert shapes[asset['shape']]==digest,'Material variants changed geometry/UV'
        else:shapes[asset['shape']]=digest
        assert np.isfinite(points).all()
        low=points.min(axis=0);high=points.max(axis=0)
        assert abs(float(low[2]))<1e-5 and np.max(np.abs((low+high)[:2]))<1e-5
        triangles=sum(len(p.vertices)-2 for p in obj.data.polygons)
        assert triangles==asset['triangles'] and triangles<10000
        bm=bmesh.new();bm.from_mesh(obj.data)
        assert all(edge.is_manifold for edge in bm.edges),'Open or non-manifold geometry'
        pending=set(bm.verts);component_count=0
        while pending:
            component_count+=1;stack=[pending.pop()]
            while stack:
                vertex=stack.pop()
                for edge in vertex.link_edges:
                    neighbor=edge.other_vert(vertex)
                    if neighbor in pending:pending.remove(neighbor);stack.append(neighbor)
        bm.free();assert component_count==1,'Disconnected stone geometry'
        packed=all(image.packed_file for image in bpy.data.images if image.source=='FILE')
        assert packed,'Unpacked image dependency'
        blob=(ROOT/asset['glb']).read_bytes();length=struct.unpack_from('<I',blob,12)[0]
        assert struct.unpack_from('<I',blob,8)[0]==len(blob)
        gltf=json.loads(blob[20:20+length]);assert len(gltf['meshes'])==1
        material=gltf['materials'][0];pbr=material['pbrMetallicRoughness']
        assert 'baseColorTexture' in pbr and 'metallicRoughnessTexture' in pbr and 'normalTexture' in material
        assert all('uri' not in item for item in gltf.get('buffers',[])+gltf.get('images',[]))
        assert (ROOT/asset['preview']).exists(),'Missing preview'
        results.append({'id':asset['id'],'geometry_uv_sha256':digest,'packed_images':bool(packed),
                        'gltf_meshes':len(gltf['meshes']),'gltf_embedded_images':len(gltf['images']),
                        'triangles':asset['triangles'],'pivot_ground_zero':True,'finite_positions':True,
                        'pbr_maps_present':True,'external_dependencies':False,
                        'closed_manifold':True,'connected_components':component_count})
    report={'family':'cailloux','asset_count':len(results),'material_variants_share_exact_geometry_and_uv':True,
            'comparison_present':(ROOT/'comparaison-tailles.png').exists(),'assets':results}
    (ROOT/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('CAILLOUX_VALIDATED',json.dumps({'count':len(results),'comparison':report['comparison_present']}),flush=True)


if __name__=='__main__':main()
