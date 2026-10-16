"""Validate saved v3 artifacts with Blender, without executing embedded scripts."""
import bpy
import json
import struct
from pathlib import Path
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view

base=Path(__file__).resolve().parents[1]
result={'blender':bpy.app.version_string,'scenes':[],'images':[]}
for stem in ['02-cites-ether','03-terres-anciens']:
    bpy.ops.wm.open_mainfile(filepath=str(base/f'{stem}-v3.blend'),use_scripts=False)
    scene=bpy.context.scene;hero=bpy.data.collections['Arkya_Hero'];root=hero.objects['Hero_Root']
    deps=bpy.context.evaluated_depsgraph_get();points=[]
    for o in hero.all_objects:
        if o.type in {'MESH','CURVE'} and not o.hide_render:
            evaluated=o.evaluated_get(deps)
            # Curve bounds include control handles; measure the rendered surface.
            surface=evaluated.to_mesh()
            if surface:points.extend(evaluated.matrix_world@v.co for v in surface.vertices)
            evaluated.to_mesh_clear()
    low=min(p.z for p in points);high=max(p.z for p in points)
    feet=world_to_camera_view(scene,scene.camera,Vector((root.location.x,root.location.y,low)))
    head=world_to_camera_view(scene,scene.camera,Vector((root.location.x,root.location.y,high)))
    images=[im for im in bpy.data.images if im.source in {'FILE','TILED'}]
    missing=[im.name for im in images if not (im.packed_file or im.packed_files)]
    item={'file':stem+'-v3.blend','camera':scene.camera.name,'resolution':[scene.render.resolution_x,scene.render.resolution_y],
          'samples':scene.cycles.samples,'hero_objects':len(hero.objects),'hero_height_m':round(high-low,4),
          'hero_feet_above_root_m':round(low-root.location.z,5),'hero_projection':{'feet':[round(feet.x,4),round(feet.y,4)],'head':[round(head.x,4),round(head.y,4)]},
          'packed_images':len(images),'unpacked_images':missing,'attribution':scene.get('Arkya_character_credit','')}
    assert item['camera']=='Camera_V3' and item['samples']==160,item
    assert item['resolution']==[2560,1440] and not missing,item
    assert 1.7<item['hero_height_m']<2.1 and -.025<item['hero_feet_above_root_m']<.025,item
    assert 0<feet.y<head.y<1 and 'CC BY 4.0' in item['attribution'],item
    result['scenes'].append(item)
    for suffix,expected in [('v3-render',(2560,1440)),('character-v3',(1350,1800))]:
        path=base/f'{stem}-{suffix}.png'
        with path.open('rb') as f:header=f.read(24)
        assert header[:8]==b'\x89PNG\r\n\x1a\n'
        size=struct.unpack('>II',header[16:24]);assert size==expected,(path,size)
        result['images'].append({'file':path.name,'size':list(size),'bytes':path.stat().st_size})
(base/'v3-validation.json').write_text(json.dumps(result,indent=2,ensure_ascii=False),encoding='utf-8')
print('V3_VALIDATED',json.dumps(result),flush=True)
