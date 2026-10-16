"""Validate portable resources and closed stump surfaces without modifying sources."""
import hashlib
import json
from pathlib import Path
import runpy
import struct

import bpy
import bmesh
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
shared=runpy.run_path(str(ROOT.parent/'scripts'/'validate_resources.py'))
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
assert len(manifest['assets'])==12
assert len({(a['shape'],a['material']) for a in manifest['assets']})==12
provenance=json.loads((ROOT/'provenance.json').read_text(encoding='utf-8-sig'))
assert hashlib.sha256((ROOT/provenance['source']).read_bytes()).hexdigest().upper()==provenance['sha256']
results=[]
for asset in manifest['assets']:
    for key in ('glb','blend','preview'):assert (ROOT/asset[key]).is_file(),(asset['id'],key)
    png=(ROOT/asset['preview']).read_bytes()
    assert struct.unpack_from('>II',png,16)==(1100,1100)
    glb=shared['glb_data'](ROOT/asset['glb'])
    assert glb['triangles']==asset['triangles'] and glb['triangles']<=35000
    blend=shared['blend_data'](ROOT/asset['blend'])
    objects=list(bpy.data.collections['Arkya_Stumps'].objects)
    assert len(objects)==1
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    low=[min(p[i] for p in points) for i in range(3)];high=[max(p[i] for p in points) for i in range(3)]
    assert abs(low[2])<.00001 and abs(low[0]+high[0])<.00001 and abs(low[1]+high[1])<.00001
    for i,key in enumerate(('width','depth','height')):assert abs(high[i]-low[i]-asset[key])<.0001
    mesh=objects[0].data;assert len(mesh.uv_layers)==1 and len(mesh.materials)==2
    uv_data=np.empty(len(mesh.loops)*2,dtype=np.float32);mesh.uv_layers.active.data.foreach_get('uv',uv_data)
    uv_hash=hashlib.sha256(uv_data.tobytes()).hexdigest()
    bm=bmesh.new();bm.from_mesh(mesh)
    assert all(e.is_manifold for e in bm.edges),(asset['id'],'Open stump edge')
    assert all(f.calc_area()>1e-13 for f in bm.faces),(asset['id'],'Degenerate face')
    bm.free()
    assert all(i['size']==[2048,2048] for i in blend['packed_images'])
    blob=(ROOT/asset['glb']).read_bytes();size,_=struct.unpack_from('<II',blob,12);gltf=json.loads(blob[20:20+size])
    assert all(m.get('alphaMode','OPAQUE')=='OPAQUE' for m in gltf['materials'])
    result={'id':asset['id'],'shape':asset['shape'],'material':asset['material'],'glb':glb,'blend':blend,
            'pivot_ground_centred':True,'closed_manifold_surfaces':True,'opaque':True,'uv_hash':uv_hash}
    results.append(result);print('STUMP_VERIFIED',asset['id'],flush=True)
for shape in manifest['shapes']:
    variants=[r for r in results if r['shape']==shape['id']]
    assert len(variants)==3 and all(r['glb']['geometry_hashes']==variants[0]['glb']['geometry_hashes'] for r in variants)
    assert all(r['uv_hash']==variants[0]['uv_hash'] for r in variants)
assert (ROOT/'comparaison-tailles.png').is_file()
report={'passed':True,'asset_count':12,'geometry_same_across_materials':True,'uv_same_across_materials':True,'approved_bark_source_unchanged':True,'assets':results}
(ROOT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('STUMP_LIBRARY_VALID',len(results),flush=True)
