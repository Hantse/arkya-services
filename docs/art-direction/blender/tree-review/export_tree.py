"""Extract one complete, unmodified individual tree from the Arkya V3 scene.

Run in Blender with 03-terres-anciens-v3.blend already open. Only review
outputs are written; the source scene is never saved or modified on disk.
"""
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector, Matrix


OUTPUT=Path(__file__).resolve().parent
TREE_INDEX=19
SOURCE_BASE=(4.0,34.0)


def numbered(prefix,index):
    return prefix if index==0 else f'{prefix}.{index:03d}'


def chosen_names():
    names=[numbered('V2 | forest depth twisting trunk',TREE_INDEX)]
    names += [numbered('V2 | forest depth buttress roots',TREE_INDEX*3+i) for i in range(3)]
    names += [numbered('V2 | forest depth crown branch',TREE_INDEX*3+i) for i in range(3)]
    names += [numbered('V2 | layered forest crown | supporting twig',TREE_INDEX*6+i) for i in range(6)]
    names += [numbered('V2 | layered forest crown | dense twig-grown leaves',TREE_INDEX*2+i) for i in range(2)]
    return names


def extract():
    source=bpy.data.filepath
    names=chosen_names()
    missing=[n for n in names if n not in bpy.data.objects]
    if missing:raise RuntimeError('Missing tree parts: '+str(missing))
    collection=bpy.data.collections.new('Arkya_Ancient_Tree')
    bpy.context.scene.collection.children.link(collection)
    deps=bpy.context.evaluated_depsgraph_get()
    clones=[]
    for name in names:
        original=bpy.data.objects[name]
        evaluated=original.evaluated_get(deps)
        data=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=deps)
        material_indices=[p.material_index for p in data.polygons]
        data.materials.clear()
        for slot in original.material_slots:data.materials.append(slot.material)
        for polygon,index in zip(data.polygons,material_indices):polygon.material_index=index
        obj=bpy.data.objects.new('Extracted | '+name,data)
        collection.objects.link(obj)
        obj.matrix_world=original.matrix_world.copy()
        obj['source_object']=name
        clones.append(obj)
    for obj in list(bpy.data.objects):
        if obj not in clones:bpy.data.objects.remove(obj,do_unlink=True)
    for coll in list(bpy.data.collections):
        if coll!=collection and not len(coll.objects) and not len(coll.children):
            bpy.data.collections.remove(coll)
    bpy.context.view_layer.active_layer_collection=bpy.context.view_layer.layer_collection.children[collection.name]
    return source,names,collection,clones


def select_only(objects,active=None):
    for obj in bpy.context.view_layer.objects:obj.select_set(False)
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=active or objects[0]


def bake_bark(wood):
    select_only(wood,wood[0])
    bpy.ops.object.join()
    trunk=bpy.context.object
    trunk.name='Ancient Tree | bark, roots and branches'
    select_only([trunk])
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(64),island_margin=.018,
                             correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.device='CPU'
    scene.cycles.samples=8
    scene.render.threads_mode='FIXED';scene.render.threads=8
    scene.render.bake.margin=12
    scene.render.bake.use_pass_direct=False
    scene.render.bake.use_pass_indirect=False
    scene.render.bake.use_pass_color=True
    scene.render.bake.use_selected_to_active=False
    original_materials=list(trunk.data.materials)
    targets=[]
    for mat in original_materials:
        node=mat.node_tree.nodes.new('ShaderNodeTexImage')
        node.name='Review bake target'
        mat.node_tree.nodes.active=node
        targets.append(node)
    for kind,name,path,colorspace in [
        ('DIFFUSE','AncientTree_Bark_BaseColor',OUTPUT/'arkya-tree-bark-color.png','sRGB'),
        ('NORMAL','AncientTree_Bark_Normal',OUTPUT/'arkya-tree-bark-normal.png','Non-Color')]:
        image=bpy.data.images.new(name,1024,1024,alpha=False)
        image.colorspace_settings.name=colorspace
        if kind=='NORMAL':image.generated_color=(.5,.5,1,1)
        for node in targets:node.image=image
        bpy.ops.object.bake(type=kind)
        image.filepath_raw=str(path)
        image.file_format='PNG'
        image.save()
        image.pack()
    mat=bpy.data.materials.new('Bark | baked V3 color and normal')
    mat.use_nodes=True
    mat.diffuse_color=(.22,.17,.14,1)
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    shader=nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value=.87
    tex=nodes.new('ShaderNodeTexImage')
    tex.image=bpy.data.images['AncientTree_Bark_BaseColor']
    links.new(tex.outputs['Color'],shader.inputs['Base Color'])
    normaltex=nodes.new('ShaderNodeTexImage')
    normaltex.image=bpy.data.images['AncientTree_Bark_Normal']
    normal=nodes.new('ShaderNodeNormalMap')
    links.new(normaltex.outputs['Color'],normal.inputs['Color'])
    links.new(normal.outputs['Normal'],shader.inputs['Normal'])
    trunk.data.materials.clear();trunk.data.materials.append(mat)
    for p in trunk.data.polygons:p.material_index=0
    return trunk


def convert_leaf_materials(leaves):
    replacements={}
    for obj in leaves:
        for index,source in enumerate(list(obj.data.materials)):
            if source.name not in replacements:
                color=source.diffuse_color[:]
                rough=.58
                if source.use_nodes:
                    p=source.node_tree.nodes.get('Principled BSDF')
                    if p:
                        color=tuple(p.inputs['Base Color'].default_value)
                        rough=p.inputs['Roughness'].default_value
                mat=bpy.data.materials.new('Leaves | '+source.name)
                mat.use_nodes=True
                mat.diffuse_color=color
                p=mat.node_tree.nodes.get('Principled BSDF')
                p.inputs['Base Color'].default_value=color
                p.inputs['Roughness'].default_value=rough
                mat.use_backface_culling=False
                replacements[source.name]=mat
            obj.data.materials[index]=replacements[source.name]
    for i,obj in enumerate(leaves):obj.name=f'Ancient Tree | leaf crown {i+1}'


def center_and_measure(objects,collection):
    minimum=min((o.matrix_world@v.co).z for o in objects for v in o.data.vertices)
    offset=Vector((*SOURCE_BASE,minimum))
    for obj in objects:
        transform=obj.matrix_world.copy()
        for vertex in obj.data.vertices:vertex.co=transform@vertex.co-offset
        obj.matrix_world=Matrix.Identity(4)
    root=bpy.data.objects.new('Arkya_Ancient_Tree_Root',None)
    collection.objects.link(root)
    for obj in objects:obj.parent=root
    allverts=[v.co for o in objects for v in o.data.vertices]
    low=[min(p[i] for p in allverts) for i in range(3)]
    high=[max(p[i] for p in allverts) for i in range(3)]
    triangles=sum(len(p.vertices)-2 for o in objects for p in o.data.polygons)
    return root,{'dimensions_m':{k:round(high[i]-low[i],4) for i,k in enumerate(('width_x','depth_y','height_z'))},
                 'bounds_blender_z_up':{'min':low,'max':high},'mesh_count':len(objects),
                 'vertices':len(allverts),'triangles':triangles,
                 'source_translation_removed':list(offset)}


def review_stage(objects,metadata):
    scene=bpy.context.scene
    stage=bpy.data.collections.new('Review_Stage')
    scene.collection.children.link(stage)
    world=bpy.data.worlds.new('Neutral tree review world')
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.14,.18,.22,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55
    scene.world=world
    height=metadata['dimensions_m']['height_z']
    bounds=metadata['bounds_blender_z_up']
    target=Vector(((bounds['min'][0]+bounds['max'][0])/2,
                   (bounds['min'][1]+bounds['max'][1])/2,height*.52))
    for name,position,power,color,size in [
        ('Warm key',(-11,-13,19),2500,(1,.87,.68),9),
        ('Cool fill',(12,-5,14),1900,(.66,.82,1),9),
        ('Leaf rim',(-3,11,18),2800,(1,.91,.71),8)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.color=color;data.shape='DISK';data.size=size
        obj=bpy.data.objects.new(name,data);stage.objects.link(obj);obj.location=position
        obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Tree review camera');data.type='ORTHO';data.ortho_scale=max(height*1.19,13.8)
    camera=bpy.data.objects.new(data.name,data);stage.objects.link(camera)
    camera.location=target+Vector((16,-22,10))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.camera=camera
    scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=32
    scene.cycles.use_denoising=True;scene.cycles.max_bounces=6
    scene.render.threads_mode='FIXED';scene.render.threads=8
    scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA'
    scene.render.film_transparent=False
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.render.filepath=str(OUTPUT/'arkya-ancient-tree-preview.png')
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D':
                area.spaces.active.region_3d.view_distance=height*1.5
                area.spaces.active.region_3d.view_location=target
                area.spaces.active.shading.type='MATERIAL'
    return scene


def main():
    OUTPUT.mkdir(parents=True,exist_ok=True)
    source,names,collection,clones=extract()
    leaves=[o for o in clones if 'dense twig-grown leaves' in o['source_object']]
    wood=[o for o in clones if o not in leaves]
    bark=bake_bark(wood)
    convert_leaf_materials(leaves)
    objects=[bark]+leaves
    root,stats=center_and_measure(objects,collection)
    stats.update({'title':'Arkya — arbre du sous-bois des Terres des Anciens',
                  'source_scene':source,'source_tree_index':TREE_INDEX,
                  'source_objects':names,'parts':{'trunk':1,'buttress_roots':3,'branches':3,'twigs':6,'leaf_crowns':2},
                  'geometry':'Exact evaluated V3 geometry; no added branches or foliage and no severed arch.',
                  'materials':'V3 bark base color and tangent normals baked to 1024px textures; original five leaf colors.',
                  'units':'metres','gltf_up_axis':'Y','blender_up_axis':'Z'})
    root['source_scene']='03-terres-anciens-v3.blend'
    root['extraction_note']=stats['geometry']
    select_only(objects,bark)
    bpy.ops.export_scene.gltf(filepath=str(OUTPUT/'arkya-ancient-tree.glb'),export_format='GLB',
                             use_selection=True,export_apply=True,export_materials='EXPORT',
                             export_yup=True,export_animations=False,export_cameras=False,
                             export_lights=False,export_tangents=True)
    stats['glb_bytes']=(OUTPUT/'arkya-ancient-tree.glb').stat().st_size
    (OUTPUT/'arkya-ancient-tree.json').write_text(json.dumps(stats,indent=2,ensure_ascii=False),encoding='utf-8')
    print('TREE_GLB_READY',str(OUTPUT/'arkya-ancient-tree.glb'),json.dumps(stats),flush=True)
    scene=review_stage(objects,stats)
    # Unused source data are purged so the stand-alone .blend contains no hero or scenery.
    bpy.data.orphans_purge(do_recursive=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/'arkya-ancient-tree.blend'),compress=True)
    bpy.ops.render.render(write_still=True)
    print('TREE_REVIEW_COMPLETE',str(OUTPUT/'arkya-ancient-tree-preview.png'),flush=True)


if __name__=='__main__':
    sys.dont_write_bytecode=True
    main()
