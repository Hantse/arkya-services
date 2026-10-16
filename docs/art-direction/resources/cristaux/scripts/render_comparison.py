"""One orthographic scene: four Azur formations at their true relative sizes."""
import json
import math
from pathlib import Path
import sys
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
records=[next(a for a in manifest['assets'] if a['shape']==shape and a['material']=='azur') for shape in ('eclat','bouquet','geode','maitresse')]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
total=sum(a['width'] for a in records)+.7*(len(records)-1)
cursor=-total/2
centres=[]
for record in records:
    with bpy.data.libraries.load(str(ROOT/record['blend']),link=False) as (available,loaded):loaded.collections=['Arkya_Crystals']
    collection=loaded.collections[0];scene.collection.children.link(collection)
    centre=cursor+record['width']/2;centres.append(centre)
    for obj in collection.objects:
        matrix=obj.matrix_world.copy();obj.parent=None;obj.matrix_world=matrix;obj.location.x+=centre
    cursor+=record['width']+.7

scene.render.engine='CYCLES';scene.cycles.samples=32;scene.cycles.use_denoising=True
gpu='--gpu' in sys.argv;scene.cycles.device='GPU' if gpu else 'CPU'
if gpu:
    p=bpy.context.preferences.addons['cycles'].preferences;p.compute_device_type='OPTIX';p.get_devices()
    for device in p.devices:device.use=device.type=='OPTIX'
scene.world=bpy.data.worlds.new('Soft neutral world');scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.005));plane=bpy.context.object
floor=bpy.data.materials.new('Sage floor');floor.use_nodes=True;floor.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.24,.275,.25,1);floor.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;plane.data.materials.append(floor)
for name,position,energy,size in [('Key',(-5,-7,9),6500,8),('Fill',(6,-2,6),3200,7),('Rim',(1,5,8),4500,6)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
    obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=position;obj.rotation_euler=(Vector((0,0,1.3))-obj.location).to_track_quat('-Z','Y').to_euler()
data=bpy.data.cameras.new('Same scale camera');camera=bpy.data.objects.new('Same scale camera',data);scene.collection.objects.link(camera);scene.camera=camera
camera.location=(0,-13,6.5);camera.rotation_euler=(Vector((0,0,1.15))-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=total+1.05
label_material=bpy.data.materials.new('Dark green labels');label_material.use_nodes=True
label_material.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.035,.065,.055,1);label_material.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=1
for record,centre in zip(records,centres):
    data=bpy.data.curves.new('Scale label','FONT');data.body=record['label'].split(' · ')[0]+'\n'+str(record['height']).replace('.',',')+' m';data.align_x='CENTER';data.size=.145;data.space_line=1.05
    obj=bpy.data.objects.new('Label '+record['shape'],data);scene.collection.objects.link(obj);obj.location=(centre,-1.45,.20);obj.rotation_euler=camera.rotation_euler;data.materials.append(label_material)
scene.render.resolution_x=1900;scene.render.resolution_y=1000;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ROOT/'comparaison-tailles.png');bpy.ops.render.render(write_still=True)
print('COMPARISON_READY',scene.render.filepath,flush=True)
