import bpy,pathlib,json,math,os
ROOT=pathlib.Path(os.environ.get('ARKYA_CREATURE_BUILD_DIR',str(pathlib.Path(__file__).resolve().parent)))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT/'source/free_wild_wolf/wild wolf.glb'))
source=next(o for o in bpy.context.scene.objects if o.type=='MESH');source.name='WolfHighSource'
bpy.context.view_layer.objects.active=source;source.select_set(True);bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
target=source.copy();target.data=source.data.copy();target.name='WolfCleanSurface';bpy.context.collection.objects.link(target)
bpy.ops.object.select_all(action='DESELECT');target.select_set(True);bpy.context.view_layer.objects.active=target
remesh=target.modifiers.new('Consolidate source surfaces','REMESH');remesh.mode='VOXEL';remesh.voxel_size=.0028;remesh.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=remesh.name)
print('REMESH',len(target.data.vertices),len(target.data.polygons),flush=True)
tri=target.modifiers.new('Triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=tri.name)
dec=target.modifiers.new('Runtime silhouette','DECIMATE');dec.ratio=17950/len(target.data.polygons);dec.use_collapse_triangulate=True;bpy.ops.object.modifier_apply(modifier=dec.name)
for poly in target.data.polygons:poly.use_smooth=True
print('DECIMATED',len(target.data.vertices),len(target.data.polygons),flush=True)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(66),island_margin=.008);bpy.ops.object.mode_set(mode='OBJECT')
material=bpy.data.materials.new('WolfBakedSurface');material.use_nodes=True;target.data.materials.clear();target.data.materials.append(material)
node=material.node_tree.nodes.new('ShaderNodeTexImage');albedo=bpy.data.images.new('texture_pbr_20250901',width=2048,height=2048);node.image=albedo;material.node_tree.nodes.active=node
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=8;scene.render.bake.use_selected_to_active=True;scene.render.bake.cage_extrusion=.012;scene.render.bake.max_ray_distance=.03;scene.render.bake.margin=12;scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False;scene.render.bake.use_pass_color=True
bpy.ops.object.select_all(action='DESELECT');source.select_set(True);target.select_set(True);bpy.context.view_layer.objects.active=target
print('BAKING ALBEDO',flush=True);bpy.ops.object.bake(type='DIFFUSE')
albedo.filepath_raw=str(ROOT/'WolfRebakedAlbedo.png');albedo.file_format='PNG';albedo.save()
normal=bpy.data.images.new('texture_pbr_20250901_normal',width=2048,height=2048);normal.colorspace_settings.name='Non-Color';node.image=normal
print('BAKING NORMAL',flush=True);bpy.ops.object.bake(type='NORMAL')
normal.filepath_raw=str(ROOT/'WolfRebakedNormal.png');normal.file_format='PNG';normal.save()
node.image=albedo;bsdf=next(n for n in material.node_tree.nodes if n.type=='BSDF_PRINCIPLED');material.node_tree.links.new(node.outputs['Color'],bsdf.inputs['Base Color'])
normal_node=material.node_tree.nodes.new('ShaderNodeTexImage');normal_node.image=normal;normal_map=material.node_tree.nodes.new('ShaderNodeNormalMap');material.node_tree.links.new(normal_node.outputs['Color'],normal_map.inputs['Color']);material.node_tree.links.new(normal_map.outputs['Normal'],bsdf.inputs['Normal']);bsdf.inputs['Roughness'].default_value=.82
bpy.data.objects.remove(source,do_unlink=True)
for image in list(bpy.data.images):
 if image not in [albedo,normal]:bpy.data.images.remove(image)
albedo.name='texture_pbr_20250901';normal.name='texture_pbr_20250901_normal'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'wolf_reduced.blend'))
print('READY',flush=True)
