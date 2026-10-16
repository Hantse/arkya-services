"""Four fallen logs shown at one world scale, using the delivered packed collections."""
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
records=[next(a for a in manifest['assets'] if a['shape']==s['id'] and a['material']=='moussu') for s in manifest['shapes']]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
total=sum(a['width'] for a in records)+.40*(len(records)-1);cursor=-total/2;centres=[]
for record in records:
 with bpy.data.libraries.load(str(ROOT/record['blend']),link=False) as (available,loaded):loaded.collections=['Arkya_Fallen_Logs']
 collection=loaded.collections[0];scene.collection.children.link(collection);centre=cursor+record['width']/2;centres.append(centre)
 for obj in collection.objects:obj.location.x+=centre
 cursor+=record['width']+.40
scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True;scene.render.threads_mode='FIXED';scene.render.threads=6
gpu='--gpu' in sys.argv;scene.cycles.device='GPU' if gpu else 'CPU'
if gpu:
 pref=bpy.context.preferences.addons['cycles'].preferences;pref.compute_device_type='OPTIX';pref.get_devices()
 for d in pref.devices:d.use=d.type=='OPTIX'
scene.world=bpy.data.worlds.new('Neutral world');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.70
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.002));floor=bpy.context.object
mat=bpy.data.materials.new('Sage floor');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.23,.265,.235,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
for name,pos,energy,size in [('Key',(-5,-7,9),2700,9),('Fill',(7,-2,7),1500,8),('Rim',(2,7,9),2100,7)]:
 data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size;obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=pos;obj.rotation_euler=(Vector((0,0,.45))-obj.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Same scale camera');camera=bpy.data.objects.new('Same scale camera',data);scene.collection.objects.link(camera);scene.camera=camera
camera.location=(0,-10,7.0);camera.rotation_euler=(Vector((0,0,.35))-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=total+.65
mat=bpy.data.materials.new('Dark sage labels');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.028,.05,.032,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=1
for record,centre in zip(records,centres):
 font=bpy.data.curves.new('Length label','FONT');font.body=record['label'].split(' · ')[0]+'\nL. '+str(record['width']).replace('.',',')+' m';font.align_x='CENTER';font.size=.15;font.space_line=1.05
 obj=bpy.data.objects.new('Label '+record['shape'],font);scene.collection.objects.link(obj);obj.location=(centre,-1.55,.30);obj.rotation_euler=camera.rotation_euler;font.materials.append(mat)
scene.render.resolution_x=2400;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ROOT/'comparaison-tailles.png');bpy.ops.render.render(write_still=True)
bpy.context.preferences.filepaths.save_version=0;bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'comparaison-tailles.blend'));print('FALLEN_LOG_COMPARISON_READY',flush=True)
