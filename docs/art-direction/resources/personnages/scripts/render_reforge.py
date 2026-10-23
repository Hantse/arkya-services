"""Render repeatable colour, clay, portrait and animation review plates."""
import argparse
import bpy
import math
import sys
from pathlib import Path
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser()
parser.add_argument("--model",default="human-b")
parser.add_argument("--hair",default="braid")
parser.add_argument("--clip",default="Idle")
parser.add_argument("--frame",type=int,default=1)
parser.add_argument("--angle",type=float,default=16)
parser.add_argument("--portrait",action="store_true")
parser.add_argument("--clay",action="store_true")
parser.add_argument("--cull-back",action="store_true")
parser.add_argument("--arms-up",action="store_true")
parser.add_argument("--output",default="")
args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
bpy.ops.wm.open_mainfile(filepath=str(ROOT/"game"/f"{args.model}.blend"),use_scripts=False)
scene=bpy.context.scene
rig=bpy.data.objects["Armature"]
rig.animation_data.action=bpy.data.actions[args.clip]
scene.frame_set(args.frame)
if args.arms_up:
    from mathutils import Quaternion
    rig.animation_data.action=None
    for side,sign in (("Left",1),("Right",-1)):
        bone=rig.pose.bones[side+"UpperArm"]
        axis=rig.data.bones[bone.name].matrix_local.to_3x3().inverted()@Vector((0,1,0))
        rotation=Quaternion(axis,math.radians(-sign*110))
        bone.rotation_mode="QUATERNION";bone.rotation_quaternion=rotation
    bpy.context.view_layer.update()
for name,style in (("HairLong","long"),("HairBraid","braid")):
    bpy.data.objects[name].hide_render=args.hair!=style
colors={"Skin":(.60,.35,.23,1),"Hair":(.378,.027,.122,1),
        "Iris":(.035,.30,.18,1),"OutfitAccent":(.0075,.328,.266,1)}
for m in bpy.data.materials:
    if not m.use_nodes:continue
    bs=m.node_tree.nodes.get("Principled BSDF")
    if not bs:continue
    if args.clay:
        bs.inputs["Base Color"].default_value=(.38,.40,.39,1)
        bs.inputs["Metallic"].default_value=0
        bs.inputs["Roughness"].default_value=.68
        bs.inputs["Emission Strength"].default_value=0
    else:
        col=m.node_tree.nodes.new("ShaderNodeVertexColor");col.layer_name="Painted"
        mix=m.node_tree.nodes.new("ShaderNodeMixRGB");mix.blend_type="MULTIPLY"
        mix.inputs[0].default_value=1
        mix.inputs[1].default_value=colors.get(m.name,bs.inputs["Base Color"].default_value)
        m.node_tree.links.new(col.outputs["Color"],mix.inputs[2])
        m.node_tree.links.new(mix.outputs[0],bs.inputs["Base Color"])
    if args.cull_back:
        geom=m.node_tree.nodes.new("ShaderNodeNewGeometry")
        transparent=m.node_tree.nodes.new("ShaderNodeBsdfTransparent")
        cull=m.node_tree.nodes.new("ShaderNodeMixShader")
        m.node_tree.links.new(geom.outputs["Backfacing"],cull.inputs[0])
        m.node_tree.links.new(bs.outputs["BSDF"],cull.inputs[1])
        m.node_tree.links.new(transparent.outputs["BSDF"],cull.inputs[2])
        m.node_tree.links.new(cull.outputs[0],m.node_tree.nodes.get("Material Output").inputs["Surface"])
world=bpy.data.worlds.new("Reference jade studio");scene.world=world;world.use_nodes=True
world.node_tree.nodes["Background"].inputs["Color"].default_value=(.045,.11,.09,1)
world.node_tree.nodes["Background"].inputs["Strength"].default_value=.45
for name,loc,power,size in [("Key",(-3,-4,5),520,3),("Fill",(4,-2,3),240,3),("Rim",(1,3,4),650,2.5)]:
    data=bpy.data.lights.new(name,"AREA");data.energy=power;data.shape="DISK";data.size=size
    o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=loc
    o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat("-Z","Y").to_euler()
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015))
floor=bpy.context.object
m=bpy.data.materials.new("Jade studio floor");m.use_nodes=True
m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(.018,.045,.037,1)
m.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value=.88
floor.data.materials.append(m)
data=bpy.data.cameras.new("ReviewCamera");cam=bpy.data.objects.new("ReviewCamera",data)
scene.collection.objects.link(cam);scene.camera=cam
target=Vector((-.035,0,1.04))
data.type="ORTHO";data.ortho_scale=2.21
if args.model.startswith("elf"):
    target.z*=1.85/1.75;data.ortho_scale*=1.85/1.75
if args.portrait:
    target=Vector((0,-.02,1.64 if args.model.startswith("human") else 1.735))
    data.ortho_scale=.66
a=math.radians(args.angle)
cam.location=target+Vector((math.sin(a)*6,-math.cos(a)*6,.35))
cam.rotation_euler=(target-cam.location).to_track_quat("-Z","Y").to_euler()
scene.render.engine="CYCLES";scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=850;scene.render.resolution_y=1050;scene.render.resolution_percentage=100
scene.view_settings.view_transform="AgX"
scene.render.image_settings.file_format="PNG"
scene.render.filepath=str(ROOT/"game"/"previews"/(args.output or f"{args.model}-reforge{'-portrait' if args.portrait else ''}{'-clay' if args.clay else ''}.png"))
bpy.ops.render.render(write_still=True)
