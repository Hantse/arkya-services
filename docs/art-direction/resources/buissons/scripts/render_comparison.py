"""Render the four shrubs at the same metre scale."""
from pathlib import Path
import json
import math
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
scene.cycles.device='GPU' if '--gpu' in sys.argv else 'CPU'
if '--gpu' in sys.argv:
    prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
    for device in prefs.devices:device.use=device.type=='OPTIX'
rows=[next(a for a in manifest['assets'] if a['shape']==s['id'] and a['material']=='sylvestre') for s in manifest['shapes']]
total=sum(row['width'] for row in rows)+.38*(len(rows)-1)
cursor=-total/2;objects=[];centres=[]
for row in rows:
    with bpy.data.libraries.load(str(ROOT/row['blend']),link=False) as (source,target):
        target.objects=[name for name in source.objects if name.startswith('Arkya |')]
    obj=target.objects[0];scene.collection.objects.link(obj);obj.location.x=cursor+row['width']/2;centres.append(obj.location.x);cursor+=row['width']+.38;objects.append(obj)
scene.world=bpy.data.worlds.new('Neutral comparison world');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.29,.32,.29,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.70
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.005));floor=bpy.context.object
mat=bpy.data.materials.new('Sage floor');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.20,.23,.20,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
for name,loc,energy,size in [('Key',(-5,-7,8),2100,7),('Fill',(6,-3,6),1300,6),('Rim',(1,5,7),1800,6)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.size=size;light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=loc;light.rotation_euler=(Vector((0,0,.7))-light.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Same scale camera');camera=bpy.data.objects.new('Same scale camera',data);scene.collection.objects.link(camera);scene.camera=camera;camera.location=(0,-10,5);camera.rotation_euler=(Vector((0,0,.65))-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=total*1.13
textmat=bpy.data.materials.new('Legend ink');textmat.diffuse_color=(.025,.047,.032,1)
for row,x in zip(rows,centres):
    text=bpy.data.curves.new('Size legend','FONT');text.body=row['label'].split(' · ')[0]+'\n'+str(row['height']).replace('.',',')+' m';text.align_x='CENTER';text.size=.105;text.space_line=1.15
    obj=bpy.data.objects.new('Legend | '+row['shape'],text);scene.collection.objects.link(obj);obj.location=(x,-.90,.19);obj.rotation_euler=camera.rotation_euler;text.materials.append(textmat)
scene.render.resolution_x=2200;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ROOT/'comparaison-tailles.png')
bpy.ops.render.render(write_still=True);bpy.context.preferences.filepaths.save_version=0;bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'comparaison-tailles.blend'))
print('BUSH_COMPARISON_READY',flush=True)
