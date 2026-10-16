"""Read files to validate topology budgets, metres, UVs and embedded dependencies."""
import json
from pathlib import Path
import struct
import bpy

ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
assert len(manifest['assets'])==12
results=[]
for record in manifest['assets']:
    path=ROOT/record['glb'];blob=path.read_bytes();assert blob[:4]==b'glTF'
    length,_=struct.unpack_from('<II',blob,12);gltf=json.loads(blob[20:20+length]);binary=28+length
    assert not any('uri' in item for item in gltf.get('buffers',[])+gltf.get('images',[]))
    images=[]
    for item in gltf['images']:
        view=gltf['bufferViews'][item['bufferView']];start=binary+view.get('byteOffset',0)
        assert blob[start:start+8]==b'\x89PNG\r\n\x1a\n'
        dimensions=struct.unpack_from('>II',blob,start+16);assert dimensions==(2048,2048);images.append(dimensions)
    for mesh in gltf['meshes']:
        for primitive in mesh['primitives']:assert 'TEXCOORD_0' in primitive['attributes']
    for material in gltf['materials']:
        assert material.get('alphaMode','OPAQUE')=='OPAQUE'
        pbr=material['pbrMetallicRoughness'];assert 'baseColorTexture' in pbr and 'metallicRoughnessTexture' in pbr
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/record['blend']))
    objects=list(bpy.data.collections['Arkya_Crystals'].objects)
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    low=[min(p[i] for p in points) for i in range(3)];high=[max(p[i] for p in points) for i in range(3)]
    assert abs(low[2])<.0001
    assert abs(low[0]+high[0])<.0001 and abs(low[1]+high[1])<.0001
    dimensions=[high[i]-low[i] for i in range(3)]
    for actual,key in zip(dimensions,['width','depth','height']):assert abs(actual-record[key])<.0001
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
    assert tris==record['triangles'] and tris<20000
    assert all(o.data.uv_layers and not o.modifiers for o in objects)
    assert not bpy.data.libraries
    assert all(i.packed_file for i in bpy.data.images if i.source=='FILE')
    assert record.get('preview') and (ROOT/record['preview']).exists()
    result={'id':record['id'],'triangles':tris,'height_m':round(dimensions[2],4),'pivot_ground_centred':True,
        'uv_present':True,'texture_size':[2048,2048],'glb_embedded_images':len(images),'glb_external_dependencies':False,
        'blend_images_packed':True,'opaque_materials':True,'glb_bytes':len(blob)}
    results.append(result);print('CRYSTAL_VERIFIED',record['id'],flush=True)
(ROOT/'validation.json').write_text(json.dumps({'assets':results,'asset_count':len(results),'passed':True},ensure_ascii=False,indent=2),encoding='utf-8')
print('CRYSTAL_LIBRARY_VALID',len(results),flush=True)
