"""Four grass silhouettes at their actual relative sizes in one camera."""
import json
from pathlib import Path
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
records=[next(a for a in manifest['assets'] if a['shape']==s['id'] and a['material']=='prairie') for s in manifest['shapes']]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
total=sum(a['width'] for a in records)+.23*(len(records)-1)
cursor=-total/2;centres=[]
for record in records:
    with bpy.data.libraries.load(str(ROOT/record['blend']),link=False) as (available,loaded):loaded.collections=['Arkya_Grass']
    collection=loaded.collections[0];scene.collection.children.link(collection)
    centre=cursor+record['width']/2;centres.append(centre)
    for obj in collection.objects:obj.location.x+=centre
    cursor+=record['width']+.23
scene.render.engine='CYCLES';scene.cycles.samples=40;scene.cycles.use_denoising=True
scene.render.threads_mode='FIXED';scene.render.threads=8
gpu='--gpu' in sys.argv;scene.cycles.device='GPU' if gpu else 'CPU'
if gpu:
    preferences=bpy.context.preferences.addons['cycles'].preferences;preferences.compute_device_type='OPTIX';preferences.get_devices()
    for device in preferences.devices:device.use=device.type=='OPTIX'
scene.world=bpy.data.worlds.new('Neutral world');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.70
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.002));floor=bpy.context.object
mat=bpy.data.materials.new('Sage floor');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.23,.265,.235,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
for name,pos,energy,size in [('Key',(-4,-5,6),2800,6),('Fill',(5,-2,4),1600,5),('Rim',(1,4,6),2200,5)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=pos;obj.rotation_euler=(Vector((0,0,.45))-obj.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Same scale camera');camera=bpy.data.objects.new('Same scale camera',data);scene.collection.objects.link(camera);scene.camera=camera
camera.location=(0,-7,3.1);camera.rotation_euler=(Vector((0,0,.44))-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=total+.42
mat=bpy.data.materials.new('Dark sage labels');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.028,.05,.032,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=1
for record,centre in zip(records,centres):
    font=bpy.data.curves.new('Size label','FONT');font.body=record['label'].split(' · ')[0]+'\n'+str(record['height']).replace('.',',')+' m';font.align_x='CENTER';font.size=.065;font.space_line=1.05
    obj=bpy.data.objects.new('Label '+record['shape'],font);scene.collection.objects.link(obj);obj.location=(centre,-.66,.08);obj.rotation_euler=camera.rotation_euler;font.materials.append(mat)
scene.render.resolution_x=2100;scene.render.resolution_y=1050;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(ROOT/'comparaison-tailles.png');bpy.ops.render.render(write_still=True)
bpy.context.preferences.filepaths.save_version=0;bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'comparaison-tailles.blend'))
print('GRASS_COMPARISON_READY',flush=True)
