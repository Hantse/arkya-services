"""Read-only Blender/glTF checks for the tree variation library."""
import json
import math
import struct
import sys
from pathlib import Path

import bpy

ROOT = Path(__file__).resolve().parents[1]


def inspect_glb(file):
    raw = file.read_bytes()
    magic, version, size = struct.unpack_from('<4sII', raw)
    assert magic == b'glTF' and version == 2 and size == len(raw), file
    json_size, json_type = struct.unpack_from('<II', raw, 12)
    assert json_type == 0x4E4F534A
    data = json.loads(raw[20:20+json_size])
    binary_header = 20+json_size
    binary_size, binary_type = struct.unpack_from('<II', raw, binary_header)
    assert binary_type == 0x004E4942
    binary_start = binary_header+8
    assert binary_start+binary_size == len(raw)
    assert not any(b.get('uri') for b in data.get('buffers', []))
    assert not any(image.get('uri') for image in data.get('images', []))
    for view in data.get('bufferViews', []):
        assert view.get('buffer',0) == 0
        assert 0 <= view.get('byteOffset',0) <= binary_size
        assert view.get('byteOffset',0)+view['byteLength'] <= binary_size
    triangles = 0
    checked = set()
    for mesh in data.get('meshes', []):
        for primitive in mesh['primitives']:
            assert primitive.get('mode',4) == 4
            index = primitive['attributes']['POSITION']
            accessor = data['accessors'][index]
            triangles += data['accessors'][primitive['indices']]['count']//3 if 'indices' in primitive else accessor['count']//3
            if index in checked: continue
            checked.add(index)
            assert accessor['componentType'] == 5126 and accessor['type'] == 'VEC3'
            view = data['bufferViews'][accessor['bufferView']]
            start = binary_start+view.get('byteOffset',0)+accessor.get('byteOffset',0)
            stride = view.get('byteStride',12)
            for i in range(accessor['count']):
                assert all(math.isfinite(x) for x in struct.unpack_from('<3f',raw,start+i*stride))
    foliage = None
    if 'matieres-v3' in file.parts:
        leaf_mesh = next(m for m in data['meshes'] if 'canopy' in m['name'])
        primitive = leaf_mesh['primitives'][0]
        assert 'TEXCOORD_0' in primitive['attributes']
        accessor = data['accessors'][primitive['attributes']['COLOR_0']]
        components = 4 if accessor['type'] == 'VEC4' else 3
        code, width, maximum = {5126: ('f', 4, 1), 5123: ('H', 2, 65535), 5121: ('B', 1, 255)}[accessor['componentType']]
        view = data['bufferViews'][accessor['bufferView']]
        start = binary_start+view.get('byteOffset',0)+accessor.get('byteOffset',0)
        stride = view.get('byteStride',components*width)
        divisor = maximum if accessor.get('normalized') else 1
        colors = [struct.unpack_from('<'+str(components)+code,raw,start+i*stride)[:3]
                  for i in range(0,accessor['count'],37)]
        channel_means = [sum(c[j]/divisor for c in colors)/len(colors) for j in range(3)]
        spread = max(max(c)/divisor for c in colors)-min(min(c)/divisor for c in colors)
        assert max(channel_means) < .65, ('Missing foliage tint', channel_means)
        assert spread > .025, ('Missing foliage shading', spread)
        leaf_material = data['materials'][primitive['material']]['pbrMetallicRoughness']
        assert 'baseColorTexture' in leaf_material
        foliage = {'vertex_color_mean':channel_means,'color_range':spread,'texture_and_uv_present':True}
    mats = data.get('materials',[])
    return {'bytes':len(raw),'meshes':len(data.get('meshes',[])), 'materials':len(mats),
            'triangles':triangles,'embedded_images':len(data.get('images',[])),
            'normal_maps':sum('normalTexture' in m for m in mats),
            'roughness_maps':sum('metallicRoughnessTexture' in m.get('pbrMetallicRoughness',{}) for m in mats),
            'emission_maps':sum('emissiveTexture' in m for m in mats),
            'external_dependencies':0,'positions_finite':True,'refined_foliage':foliage}


def inspect_blend(file):
    bpy.ops.wm.open_mainfile(filepath=str(file),use_scripts=False)
    images = []
    for image in bpy.data.images:
        if image.source not in {'FILE','TILED'}: continue
        packed = bool(image.packed_file or image.packed_files)
        assert packed, (str(file),image.name,image.filepath)
        images.append({'name':image.name,'size':list(image.size),'packed':packed})
    assert len(bpy.data.libraries) == 0
    trees = [o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.startswith('Oak |')]
    assert len(trees)==3,(str(file),[o.name for o in trees])
    return {'opened':True,'tree_meshes':len(trees),'packed_images':images,'linked_libraries':0}


def main():
    material_folder = 'matieres-v3' if '--refined' in sys.argv else 'matieres-v2' if '--painted' in sys.argv else None
    files = sorted(ROOT.glob(material_folder+'/*/*.glb')) if material_folder else sorted(ROOT.glob('formes/*/*.glb')) + sorted(ROOT.glob('matieres/*/*.glb'))
    expected = 3 if material_folder else 7
    assert len(files)==expected, [str(f) for f in files]
    rows=[]
    for glb in files:
        blend=glb.with_suffix('.blend')
        assert blend.exists()
        row={'asset':glb.relative_to(ROOT).as_posix(),'glb':inspect_glb(glb),'blend':inspect_blend(blend)}
        assert row['glb']['meshes']==3
        assert row['glb']['triangles']==429204
        assert row['glb']['normal_maps'] >= 1
        if any(part.startswith('matieres') for part in glb.parts):
            assert row['glb']['roughness_maps'] >= 1
            assert row['glb']['emission_maps'] >= 1
        rows.append(row)
        print('ASSET_VALIDATED',row['asset'],flush=True)
    report = ROOT/(material_folder+'/validation.json' if material_folder else 'validation.json')
    report.write_text(json.dumps({'success':True,'assets':rows},indent=2,ensure_ascii=False),encoding='utf-8')
    print('LIBRARY_VALIDATED',len(rows),flush=True)


if __name__=='__main__': main()
