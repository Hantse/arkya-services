"""Render representative bind/animation frames without editing game sources."""
import argparse, bpy, math, sys
from pathlib import Path
from mathutils import Vector
ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--model',default='human-a');parser.add_argument('--clip',default='Walk');parser.add_argument('--frame',type=int,default=10);parser.add_argument('--hair',choices=['short','long','braid'],default='short')
parser.add_argument('--output',default='');parser.add_argument('--portrait',action='store_true')
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'game'/f'{args.model}.blend'),use_scripts=False)
rig=bpy.data.objects['Armature'];rig.animation_data.action=bpy.data.actions[args.clip]
scene=bpy.context.scene;scene.frame_set(args.frame)
for n,style in [('HairLong','long'),('HairBraid','braid')]:bpy.data.objects[n].hide_render=args.hair!=style
colors={'Skin':(.60,.35,.21,1),'Hair':(.10,.052,.025,1),'Iris':(.09,.24,.13,1),'OutfitAccent':(.13,.25,.18,1)}
for m in bpy.data.materials:
    if not m.use_nodes:continue
    bs=m.node_tree.nodes.get('Principled BSDF')
    if not bs:continue
    col=m.node_tree.nodes.new('ShaderNodeVertexColor');col.layer_name='Painted'
    mul=m.node_tree.nodes.new('ShaderNodeMixRGB');mul.blend_type='MULTIPLY';mul.inputs[0].default_value=1
    mul.inputs[1].default_value=colors.get(m.name,bs.inputs['Base Color'].default_value)
    m.node_tree.links.new(col.outputs['Color'],mul.inputs[2]);m.node_tree.links.new(mul.outputs[0],bs.inputs['Base Color'])
world=bpy.data.worlds.new('Rig review world');scene.world=world;world.use_nodes=True
world.node_tree.nodes['Background'].inputs['Color'].default_value=(.09,.12,.14,1)
world.node_tree.nodes['Background'].inputs['Strength'].default_value=.65
for name,loc,power,size in [('Key',(-3,-4,5),600,4),('Fill',(4,-1,3),280,4),('Rim',(1,4,3),600,3)]:
    data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.025));floor=bpy.context.object
m=bpy.data.materials.new('Floor');m.diffuse_color=(.05,.065,.07,1);floor.data.materials.append(m)
camd=bpy.data.cameras.new('ReviewCamera');cam=bpy.data.objects.new('ReviewCamera',camd);scene.collection.objects.link(cam)
cam.location=(2.8,-6,2.7);target=Vector((0,0,.95));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();camd.type='ORTHO';camd.ortho_scale=2.35;scene.camera=cam
if args.portrait:
    target=Vector((0,0,1.55 if args.model.startswith('human') else 1.64));cam.location=target+Vector((1.7,-6,.6))
    cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler();camd.ortho_scale=.72
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=800;scene.render.resolution_y=900;scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG'
(ROOT/'game'/'previews').mkdir(exist_ok=True)
scene.render.filepath=str(ROOT/'game'/'previews'/(args.output or f'{args.model}-{args.clip.lower()}-{args.frame}{"-"+args.hair if args.hair!="short" else ""}.png'))
bpy.ops.render.render(write_still=True)
