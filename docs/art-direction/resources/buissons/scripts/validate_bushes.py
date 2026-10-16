"""Check all shrub files, packed textures, pivots, UVs and palette invariance."""
from pathlib import Path
import hashlib
import json
import runpy
import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[1]
shared=runpy.run_path(str(ROOT.parent/'scripts/validate_resources.py'))
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
assert len(manifest['assets'])==12
rows=[]
for asset in manifest['assets']:
    for key in ('glb','blend','preview'):assert (ROOT/asset[key]).is_file(),(asset['id'],key)
    glb=shared['glb_data'](ROOT/asset['glb'])
    assert glb['triangles']==asset['triangles'] and glb['triangles']<=45000
    blend=shared['blend_data'](ROOT/asset['blend'])
    obj=next(o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Arkya |'))
    coords=np.array([tuple(v.co) for v in obj.data.vertices]);lo=coords.min(axis=0);hi=coords.max(axis=0)
    assert abs(lo[2])<.00001 and np.max(np.abs((hi+lo)[:2]))<.00001,(asset['id'],'Pivot')
    assert abs((hi-lo)[2]-asset['height'])<.0001
    assert len(obj.data.uv_layers)==1
    uv=np.array([tuple(item.uv) for item in obj.data.uv_layers[0].data],dtype=np.float32)
    assert np.isfinite(uv).all() and np.min(uv)>=0 and np.max(uv)<=1
    uv_hash=hashlib.sha256(uv.tobytes()).hexdigest()
    rows.append({'id':asset['id'],'shape':asset['shape'],'glb':glb,'blend':blend,'uv_hash':uv_hash,'pivot_ground_centred':True,'dimensions_match':True})
for shape in manifest['shapes']:
    group=[r for r in rows if r['shape']==shape['id']];assert len(group)==3
    assert len({r['uv_hash'] for r in group})==1
    assert all(r['glb']['geometry_hashes']==group[0]['glb']['geometry_hashes'] for r in group)
assert (ROOT/'comparaison-tailles.png').exists()
report={'success':True,'asset_count':len(rows),'geometry_same_across_materials':True,'uv_same_across_materials':True,'assets':rows}
(ROOT/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print('BUSHES_VALIDATED',len(rows),flush=True)
