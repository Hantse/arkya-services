import bpy,pathlib,math,json,os
from mathutils import Vector,Quaternion
ROOT=pathlib.Path(os.environ.get('ARKYA_CREATURE_BUILD_DIR',str(pathlib.Path(__file__).resolve().parent)))/'prepared'
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'WildWolf.blend'))
arm=bpy.data.objects['WildWolfRig'];obj=bpy.data.objects['WildWolfBody'];arm.animation_data_clear()
for name,angle in [('Thigh.L',24),('Thigh.R',-24),('Calf.L',18),('Calf.R',12),('UpperArm.L',-25),('UpperArm.R',25),('Forearm.L',25),('Forearm.R',10),('Chest',8)]:
 bone=arm.pose.bones[name];bind=bone.bone.matrix_local.to_quaternion();bone.rotation_mode='QUATERNION';bone.rotation_quaternion=bind.inverted()@Quaternion((1,0,0),math.radians(angle))@bind
bpy.context.view_layer.update();evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=evaluated.to_mesh();deltas=[(v.co-obj.data.vertices[i].co).length for i,v in enumerate(mesh.vertices)];print('SKIN_PROOF',json.dumps({'vertices':len(deltas),'moving_vertices':sum(d>.002 for d in deltas),'maximum_displacement_m':max(deltas),'finite':all(math.isfinite(d) for d in deltas),'minimum_weight':min(sum(g.weight for g in v.groups) for v in obj.data.vertices)}));evaluated.to_mesh_clear()
scene=bpy.context.scene;scene.world=bpy.data.worlds.new('ReviewWorld');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.06,.07,.09,1)
scene.render.engine='CYCLES';scene.cycles.samples=16;scene.render.resolution_x=900;scene.render.resolution_y=900;scene.render.resolution_percentage=100
camera_data=bpy.data.cameras.new('ReviewCamera');camera=bpy.data.objects.new('ReviewCamera',camera_data);scene.collection.objects.link(camera);camera.location=(3.6,-6,2.4);focus=Vector((0,0,1.1));camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=3.0;scene.camera=camera
for name,location,power,size in [('Key',(-3,-4,5),700,4),('Fill',(3,-2,3),450,3),('Rim',(0,3,4),900,2)]:
 data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=location;light.rotation_euler=(focus-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(ROOT/'WildWolf-skin-check.png');bpy.ops.render.render(write_still=True)
report=json.loads((ROOT/'WildWolf-provenance.json').read_text());report['skin_validation']={'moving_vertices':sum(d>.002 for d in deltas),'maximum_displacement_m':max(deltas),'finite':all(math.isfinite(d) for d in deltas),'minimum_weight':min(sum(g.weight for g in v.groups) for v in obj.data.vertices),'maximum_influences':max(len(v.groups) for v in obj.data.vertices)};(ROOT/'WildWolf-provenance.json').write_text(json.dumps(report,indent=2),encoding='utf8')
