import bpy,bmesh,pathlib,json,math,hashlib,os
from mathutils import Vector,kdtree
ROOT=pathlib.Path(os.environ.get('ARKYA_CREATURE_BUILD_DIR',str(pathlib.Path(__file__).resolve().parent)))
OUT=ROOT/'prepared';OUT.mkdir(exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'wolf_reduced.blend'))
for image in bpy.data.images:
 if image.name.startswith('texture_pbr'):
  image.source='FILE';image.filepath=str(ROOT/('WolfRebakedNormal.png' if 'normal' in image.name else 'WolfRebakedAlbedo.png'));image.reload()
obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');obj.name='WildWolfBody';obj.data.name='WildWolfBody'
bpy.context.view_layer.objects.active=obj
bm=bmesh.new();bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(obj.data);bm.free()
for p in obj.data.polygons:p.use_smooth=True
low=min(v.co.z for v in obj.data.vertices);height=max(v.co.z for v in obj.data.vertices)-low;scale=2.2/height
def p(v):return Vector((v[0]*scale,v[1]*scale,(v[2]-low)*scale))
raw={
 'Root':((0,0,low),(0,0,low+.1),None),
 'Hips':((0,.055,-.12),(0,.055,-.025),'Root'),
 'Spine':((0,.055,-.025),(0,.035,.13),'Hips'),
 'Chest':((0,.035,.13),(0,-.025,.28),'Spine'),
 'Neck':((0,-.025,.28),(0,-.16,.32),'Chest'),
 'Head':((0,-.16,.32),(0,-.38,.27),'Neck'),
 'Tail':((0,.115,-.12),(0,.30,-.235),'Hips'),
 'TailTip':((0,.30,-.235),(0,.445,-.35),'Tail')}
for side,sign in [('L',1),('R',-1)]:
 raw.update({
  'UpperArm.'+side:((sign*.195,.035,.205),(sign*.255,.015,.02),'Chest'),
  'Forearm.'+side:((sign*.255,.015,.02),(sign*.265,-.015,-.125),'UpperArm.'+side),
  'Hand.'+side:((sign*.265,-.015,-.125),(sign*.255,-.06,-.265),'Forearm.'+side),
  'Thigh.'+side:((sign*.105,.055,-.12),(sign*.125,-.005,-.265),'Hips'),
  'Calf.'+side:((sign*.125,-.005,-.265),(sign*.13,.04,-.405),'Thigh.'+side),
  'Foot.'+side:((sign*.13,.04,-.405),(sign*.15,-.12,-.445),'Calf.'+side)})
arm_data=bpy.data.armatures.new('WildWolfRig');arm=bpy.data.objects.new('WildWolfRig',arm_data);bpy.context.collection.objects.link(arm)
bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);bpy.context.view_layer.objects.active=arm;bpy.ops.object.mode_set(mode='EDIT')
for name,(head,tail,parent) in raw.items():
 bone=arm_data.edit_bones.new(name);bone.head=p(head);bone.tail=p(tail)
 if parent:bone.parent=arm_data.edit_bones[parent]
bpy.ops.object.mode_set(mode='OBJECT')
arm_data.bones['Root'].use_deform=False
for vert in obj.data.vertices:
 vert.co=p(vert.co)
bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);arm.select_set(True);bpy.context.view_layer.objects.active=arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
weighted=[v for v in obj.data.vertices if v.groups];tree=kdtree.KDTree(len(weighted))
for v in weighted:tree.insert(v.co,v.index)
tree.balance()
for v in obj.data.vertices:
 if not v.groups:
  _,nearest,_=tree.find(v.co)
  for group in obj.data.vertices[nearest].groups:obj.vertex_groups[group.group].add([v.index],group.weight,'REPLACE')
bpy.context.view_layer.objects.active=obj
bpy.ops.object.vertex_group_limit_total(limit=4);bpy.ops.object.vertex_group_normalize_all(lock_active=False)
print('HEAT_WEIGHTS',len(obj.vertex_groups),sum(not v.groups for v in obj.data.vertices),flush=True)
for name,im in [(im.name,im) for im in bpy.data.images]:
 if name.startswith('texture_pbr'):
  if 'metallic' in name:continue
  im.scale(2048,2048);target='WildWolfNormal.png' if 'normal' in name else 'WildWolfAlbedo.png'
  im.filepath_raw=str(OUT/target);im.file_format='PNG';im.save()
  im.pack()
mat=obj.data.materials[0];mat.name='WildWolfSurface'
bsdf=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
for link in list(mat.node_tree.links):
 if link.to_socket in [bsdf.inputs['Roughness'],bsdf.inputs['Metallic'],bsdf.inputs['Specular Tint']]:mat.node_tree.links.remove(link)
bsdf.inputs['Roughness'].default_value=.82;bsdf.inputs['Metallic'].default_value=0;bsdf.inputs['Specular IOR Level'].default_value=.22
# A short clip exercises real skin deformation for import/validation; runtime poses observe server state.
arm.animation_data_create();action=bpy.data.actions.new('WolfIdle');arm.animation_data.action=action
for frame,amount in [(1,0),(16,.025),(31,0),(46,-.018),(61,0)]:
 bone=arm.pose.bones['Chest'];bone.rotation_mode='XYZ';bone.rotation_euler.x=amount;bone.keyframe_insert('rotation_euler',frame=frame)
bpy.context.scene.frame_start=1;bpy.context.scene.frame_end=61;bpy.context.scene.render.fps=30;bpy.context.scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT');arm.select_set(True);obj.select_set(True);bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(OUT/'WildWolf.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},apply_unit_scale=True,axis_forward='-Z',axis_up='Y',add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,path_mode='STRIP',use_mesh_modifiers=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'WildWolf.blend'))
scene=bpy.context.scene;scene.world=bpy.data.worlds.new('ReviewWorld');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.06,.07,.09,1)
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=1000;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
camera_data=bpy.data.cameras.new('ReviewCamera');camera=bpy.data.objects.new('ReviewCamera',camera_data);scene.collection.objects.link(camera);camera.location=(3.6,-6,2.4);focus=Vector((0,0,1.1));camera.rotation_euler=(focus-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.type='ORTHO';camera.data.ortho_scale=2.85;scene.camera=camera
for name,location,power,size in [('Key',(-3,-4,5),700,4),('Fill',(3,-2,3),450,3),('Rim',(0,3,4),900,2)]:
 data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=location;light.rotation_euler=(focus-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.filepath=str(OUT/'WildWolf-review.png');bpy.ops.render.render(write_still=True)
report={'source':'free_wild_wolf.zip/wild wolf.glb','source_sha256':hashlib.sha256((ROOT/'source/free_wild_wolf/wild wolf.glb').read_bytes()).hexdigest(),'source_vertices':890200,'source_triangles':1499612,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'height_m':2.2,'blender_version':bpy.app.version_string,'forward_blender':'-Y','up_blender':'+Z','export_forward':'-Z','export_up':'+Y','bones':{n:{'head':list(p(h)),'tail':list(p(t)),'parent':parent} for n,(h,t,parent) in raw.items()},'notes':'User-supplied original preserved. Arkya derivative: voxel consolidation (0.0028 source units), decimation, new UVs with 2K albedo and normal rebaked from original, authored 20-bone biped skeleton, heat weights capped at four influences and normalized. Tiny unweighted disconnected fragments inherit nearest weighted surface. Root does not deform. No third-party animation. WolfIdle is an authored deformation proof; runtime presentation uses bind-relative procedural poses.'}
(OUT/'WildWolf-provenance.json').write_text(json.dumps(report,indent=2),encoding='utf8');print('REPORT',json.dumps(report))
