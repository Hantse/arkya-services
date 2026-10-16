"""Build the revised Arkya coats on the licensed, sculpted anatomy sources.

Blender --background --python build_totems_v2.py -- --species fox [--draft]
"""
import argparse
import json
import math
import sys
from datetime import datetime, timezone
from pathlib import Path

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
from spirit_markings import make_markings, make_sparks


def color(value):
    value = value.lstrip('#')
    def linear(v):
        return v/12.92 if v <= .04045 else ((v+.055)/1.055)**2.4
    return tuple(linear(int(value[i:i+2],16)/255) for i in (0,2,4)) + (1,)


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def coat_material(original, palette, species, profile=None):
    material = bpy.data.materials.new('Paint working')
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    def node(kind, name=None):
        result = nodes.new(kind)
        if name:
            result.name = name
        return result
    def math_node(operation, a, b):
        result = node('ShaderNodeMath')
        result.operation = operation
        for i, value in enumerate((a,b)):
            if isinstance(value,(int,float)):
                result.inputs[i].default_value = value
            else:
                links.new(value,result.inputs[i])
        return result.outputs[0]
    texture = node('ShaderNodeTexImage','Original painted coat')
    texture.image = original
    separate = node('ShaderNodeSeparateColor')
    links.new(texture.outputs['Color'], separate.inputs[0])
    # Preserve the muzzle, eyes, toes and cream bib from the authored atlas.
    warm = math_node('SUBTRACT',separate.outputs[0],separate.outputs[2])
    warm = math_node('SUBTRACT',warm,.035)
    mask = math_node('MULTIPLY',warm,5)
    grey = node('ShaderNodeRGBToBW')
    links.new(texture.outputs['Color'],grey.inputs[0])
    value = math_node('MULTIPLY',separate.outputs[0],.72)
    value = math_node('ADD',value,math_node('MULTIPLY',grey.outputs[0],.28))
    value = math_node('MULTIPLY',value,.78)
    ramp = node('ShaderNodeValToRGB')
    ramp.color_ramp.interpolation='EASE'
    stops=[(0,0),(.08,1),(.23,2),(.46,3),(.74,4),(1,5)]
    for extra in list(ramp.color_ramp.elements)[1:]:
        ramp.color_ramp.elements.remove(extra)
    for i,(position,index) in enumerate(stops):
        element=ramp.color_ramp.elements[0] if i==0 else ramp.color_ramp.elements.new(position)
        element.position=position
        element.color=color(palette[index])
    links.new(value,ramp.inputs[0])
    mix = node('ShaderNodeMixRGB')
    mix.blend_type='MIX'
    mix.use_clamp=True
    links.new(mask,mix.inputs[0])
    links.new(texture.outputs['Color'],mix.inputs[1])
    links.new(ramp.outputs['Color'],mix.inputs[2])
    painted = mix.outputs[0]
    if profile == 'wolf-v4':
        from wolf_coat_v4 import paint_wolf
        painted = paint_wolf(nodes,links,texture.outputs['Color'],palette,color)
    elif species == 'wolf':
        from wolf_coat import paint_wolf
        painted = paint_wolf(nodes,links,texture.outputs['Color'],grey.outputs[0],painted,palette,color)
    # Fine directional pigment variation, kept below the silhouette/readability budget.
    coords=node('ShaderNodeTexCoord')
    mapping=node('ShaderNodeVectorMath')
    mapping.operation='MULTIPLY'
    mapping.inputs[1].default_value=(13,95,66)
    links.new(coords.outputs['Generated'],mapping.inputs[0])
    noise=node('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value=1
    noise.inputs['Detail'].default_value=2
    links.new(mapping.outputs[0],noise.inputs['Vector'])
    pigment=math_node('ADD',math_node('MULTIPLY',noise.outputs['Fac'],.10),.94)
    brush=node('ShaderNodeMixRGB')
    brush.blend_type='MULTIPLY'
    brush.inputs[0].default_value=1
    links.new(painted,brush.inputs[1])
    links.new(pigment,brush.inputs[2])
    emission=node('ShaderNodeEmission')
    links.new(brush.outputs[0],emission.inputs['Color'])
    output=node('ShaderNodeOutputMaterial')
    links.new(emission.outputs[0],output.inputs['Surface'])
    return material


def bake_coat(coat, original, palette, path, species, profile=None):
    if species == 'wolf' and 'SourcePosition' not in coat.data.attributes:
        raise ValueError('Rebuild the wolf anatomy with its SourcePosition attribute before baking.')
    if profile == 'wolf-v4' and 'EyeMask' not in coat.data.attributes:
        raise ValueError('The wolf V4 anatomy must include its eye-island mask.')
    working=coat_material(original,palette,species,profile)
    coat.data.materials.clear()
    coat.data.materials.append(working)
    for polygon in coat.data.polygons:
        polygon.material_index=0
    image=bpy.data.images.new(path.stem,width=2048,height=2048,alpha=False)
    image.colorspace_settings.name='sRGB'
    target=working.node_tree.nodes.new('ShaderNodeTexImage')
    target.image=image
    working.node_tree.nodes.active=target
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=1
    scene.render.bake.margin=12
    select([coat])
    bpy.ops.object.bake(type='EMIT')
    image.filepath_raw=str(path)
    image.file_format='PNG'
    image.save()
    final=bpy.data.materials.new('TotemFur')
    final.use_nodes=True
    bs=final.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Roughness'].default_value=.86
    bs.inputs['Specular IOR Level'].default_value=.13
    tex=final.node_tree.nodes.new('ShaderNodeTexImage')
    tex.image=image
    final.node_tree.links.new(tex.outputs['Color'],bs.inputs['Base Color'])
    coat.data.materials.clear()
    coat.data.materials.append(final)
    return image


def magic_material(obj, palette, spark=False):
    material=bpy.data.materials.new('TotemSpark' if spark else 'TotemMagic')
    material.use_nodes=True
    material.diffuse_color=color(palette[10])
    bs=material.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=color(palette[10])
    bs.inputs['Emission Color'].default_value=color(palette[10])
    bs.inputs['Emission Strength'].default_value=2.1 if spark else 1.15
    bs.inputs['Roughness'].default_value=.8
    material.use_backface_culling=False
    obj.data.materials.clear()
    obj.data.materials.append(material)


def studio():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=48
    scene.cycles.use_denoising=True
    scene.render.resolution_x=1400
    scene.render.resolution_y=1100
    scene.render.resolution_percentage=100
    scene.render.film_transparent=False
    scene.view_settings.view_transform='AgX'
    scene.world=bpy.data.worlds.new('Totem studio')
    scene.world.use_nodes=True
    bg=scene.world.node_tree.nodes.get('Background')
    bg.inputs[0].default_value=(.14,.18,.23,1)
    bg.inputs[1].default_value=.40
    bpy.ops.mesh.primitive_plane_add(size=200)
    floor=bpy.context.object
    floor.name='Studio floor'
    mat=bpy.data.materials.new('Studio slate')
    mat.use_nodes=True
    bs=mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=(.024,.034,.044,1)
    bs.inputs['Roughness'].default_value=1
    floor.data.materials.append(mat)
    for name,location,energy,size,tint in [
        ('Warm key',(2,-3,4),260,4,(1,.88,.75)),
        ('Cool fill',(-3,-1,2),100,3,(.65,.78,1)),
        ('Rim',(-1,3,3),310,3,(.7,.88,1)),
    ]:
        data=bpy.data.lights.new(name,'AREA')
        obj=bpy.data.objects.new(name,data)
        bpy.context.collection.objects.link(obj)
        obj.location=location
        obj.rotation_euler=(Vector((0,0,.5))-obj.location).to_track_quat('-Z','Y').to_euler()
        data.energy=energy
        data.size=size
        data.color=tint
    data=bpy.data.cameras.new('Beauty camera')
    camera=bpy.data.objects.new('Beauty camera',data)
    bpy.context.collection.objects.link(camera)
    data.type='ORTHO'
    scene.camera=camera
    return camera


def frame_camera(camera,coat,side=False):
    points=[coat.matrix_world @ Vector(p) for p in coat.bound_box]
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    center=(lo+hi)*.5
    size=max(hi-lo)
    camera.location=center+Vector((.04,-3,.30) if side else (1.4,-3,.8))*size
    camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.ortho_scale=size*1.32


def render_head_views(camera,coat,prefix):
    groups={g.index for g in coat.vertex_groups if g.name=='Head' or g.name.startswith('Ear.')}
    points=[coat.matrix_world @ v.co for v in coat.data.vertices
            if sum(g.weight for g in v.groups if g.group in groups)>.65]
    if not points:
        return
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    center=(lo+hi)*.5
    size=max(hi-lo)
    camera.data.ortho_scale=size*1.48
    for suffix,direction in [('front',(3,0,.04)),('profile',(0,-3,.08)),('three-quarter',(2,-3,.22))]:
        camera.location=center+Vector(direction)*size
        camera.rotation_euler=(center-camera.location).to_track_quat('-Z','Y').to_euler()
        bpy.context.scene.render.filepath=str(ROOT/'renders'/f'{prefix}-head-{suffix}.png')
        bpy.ops.render.render(write_still=True)


def build(species,draft=False,lookdev=False,source_override=None):
    manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
    source=Path(source_override).resolve() if source_override else ROOT/'assets'/'anatomy'/f'{species}.blend'
    bpy.ops.wm.open_mainfile(filepath=str(source))
    rig=bpy.data.objects['Armature']
    coat=bpy.data.objects['TotemFur']
    metadata=json.loads(source.with_name(f'{species}-rig.json').read_text(encoding='utf-8'))
    locomotion=metadata.get('locomotionConfig',metadata.get('locomotion',{}))
    images=[node.image for mat in coat.data.materials for node in mat.node_tree.nodes
            if node.type=='TEX_IMAGE' and node.image]
    image_name=metadata.get('coatImage')
    original=next((image for image in images if image.name==image_name),images[0])
    profile=metadata.get('coatProfile')
    # Bind/project in rest pose; locomotion is baked only after the inlay exists.
    rig.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    magic,paths,surface=make_markings(coat,rig,metadata['anatomy'],species)
    if lookdev:
        entry=next(item for item in manifest['species'] if item['id']==species)
        variant=next(item for item in manifest['colorways'] if item['id']==entry['defaultColorway'])
        bake_coat(coat,original,variant['colors'],ROOT/'renders'/f'{species}-lookdev-basecolor.png',species,profile)
        magic_material(magic,variant['colors'])
        camera=studio()
        frame_camera(camera,coat)
        bpy.context.scene.render.filepath=str(ROOT/'renders'/f'{species}-lookdev-v2.png')
        bpy.ops.render.render(write_still=True)
        if profile=='wolf-v4':
            render_head_views(camera,coat,'wolf-lookdev')
        return
    sparks,paths=make_sparks(rig,paths,coat.dimensions.z*.0035,
                            locomotion.get('body',{}).get('root','Root'))
    from totem_locomotion import bake_locomotion
    actions,report=bake_locomotion(rig,species=species,config=locomotion,
                                  magic_paths=paths,weights_fn=surface.weights,
                                  validation_path=ROOT/f'{species}-locomotion.json')
    camera=studio()
    rig.animation_data.action=actions[0]
    bpy.context.scene.frame_set(0)
    frame_camera(camera,coat)
    entry=next(item for item in manifest['species'] if item['id']==species)
    entry['artRevision']=metadata.get('artRevision',2)
    entry['rig']={'bones':len(rig.data.bones),'maxInfluences':4}
    coat.data.calc_loop_triangles()
    magic.data.calc_loop_triangles()
    sparks.data.calc_loop_triangles()
    entry['triangles']=len(coat.data.loop_triangles)+len(magic.data.loop_triangles)+len(sparks.data.loop_triangles)
    entry['dimensions']={'lengthM':round(coat.dimensions.x,3),'widthM':round(coat.dimensions.y,3),'heightM':round(coat.dimensions.z,3)}
    entry['unity']=metadata.get('unity',{})
    entry['locomotion']={'report':f'{species}-locomotion.json',
        'clips':[{key:clip[key] for key in ('name','duration_seconds','expected_speed_mps','gait','checks')}
                 for clip in report['clips']]}
    entry['sourceCredit']=metadata.get('sourceCredit','Fox by Bake3D, CC BY 4.0; anatomy, rig, coats and animations modified for Arkya.')
    if metadata.get('provenance'):
        entry['provenance']=metadata['provenance']
    variants=sorted(manifest['colorways'],key=lambda value:value['id']!=entry['defaultColorway'])
    for index,variant in enumerate(variants):
        if draft and index:
            break
        print('COAT',species,variant['id'],flush=True)
        image=bake_coat(coat,original,variant['colors'],ROOT/'textures'/f"{species}-{variant['id']}-basecolor.png",species,profile)
        magic_material(magic,variant['colors'])
        magic_material(sparks,variant['colors'],spark=True)
        rig.animation_data.action=actions[0]
        bpy.context.scene.frame_set(0)
        select([rig,coat,magic,sparks])
        bpy.ops.export_scene.gltf(filepath=str(ROOT/'models'/f"{species}-{variant['id']}.glb"),
            export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',
            export_force_sampling=True,export_frame_range=False,export_skins=True,export_yup=True,export_extras=True)
        if index==0:
            bpy.ops.export_scene.fbx(filepath=str(ROOT/'assets'/f'{species}.fbx'),use_selection=True,
                object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,
                bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,
                path_mode='COPY',embed_textures=True,axis_forward='-Z',axis_up='Y')
            image.pack()
            bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'assets'/f'{species}.blend'))
            bpy.context.scene.cycles.samples=48
            for suffix,action,frame,side in [('beauty',actions[0],0,False),('side',actions[0],0,True),('walk',actions[1],15,True)]:
                rig.animation_data.action=action
                bpy.context.scene.frame_set(frame)
                frame_camera(camera,coat,side)
                bpy.context.scene.render.filepath=str(ROOT/'renders'/f'{species}-{suffix}.png')
                bpy.ops.render.render(write_still=True)
            if profile=='wolf-v4':
                rig.animation_data.action=actions[0]
                bpy.context.scene.frame_set(0)
                render_head_views(camera,coat,'wolf')
        print('EXPORTED',species,variant['id'],flush=True)
    validation={'species':species,'artRevision':entry['artRevision'],'bones':len(rig.data.bones),'meshes':{},
                'clips':entry['locomotion']['clips'],'variants':[]}
    for obj in (coat,magic,sparks):
        counts=[len([g for g in v.groups if g.weight>0]) for v in obj.data.vertices]
        error=max(abs(sum(g.weight for g in v.groups)-1) for v in obj.data.vertices)
        assert max(counts)<=4 and min(counts)>0 and error<1e-5, (obj.name,error)
        validation['meshes'][obj.name]={'vertices':len(obj.data.vertices),
            'triangles':len(obj.data.loop_triangles),'materials':len(obj.data.materials),
            'maxInfluences':max(counts),'maxWeightError':error}
    for variant in variants[:1] if draft else variants:
        validation['variants'].append({'id':variant['id'],'glb':f"models/{species}-{variant['id']}.glb",
            'texture':f"textures/{species}-{variant['id']}-basecolor.png"})
    (ROOT/f'{species}-validation.json').write_text(json.dumps(validation,indent=2),encoding='utf-8')
    manifest['revision']=datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%S%fZ-totems')
    manifest['generator']='scripts/build_totems_v2.py'
    manifest['artStatus']='Refonte à examiner'
    # Preserve the other animal's most recently exported metadata.
    latest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
    for i,item in enumerate(latest['species']):
        if item['id']==species:
            latest['species'][i]=entry
    for key in ('revision','generator','artStatus'):
        latest[key]=manifest[key]
    for animation in latest['animations']:
        durations={animal['id']:clip['duration_seconds'] for animal in latest['species']
                   for clip in animal.get('locomotion',{}).get('clips',[]) if clip['name']==animation['id']}
        animation.pop('duration',None)
        animation['durationBySpecies']=durations
    (ROOT/'manifest.json').write_text(json.dumps(latest,ensure_ascii=False,indent=2),encoding='utf-8')


if __name__=='__main__':
    parser=argparse.ArgumentParser()
    parser.add_argument('--species',required=True,choices=['wolf','fox'])
    parser.add_argument('--draft',action='store_true')
    parser.add_argument('--lookdev',action='store_true')
    parser.add_argument('--source',help='Alternative anatomy .blend for a lookdev preview only')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.source and not args.lookdev:
        parser.error('--source requires --lookdev')
    build(args.species,args.draft,args.lookdev,args.source)
