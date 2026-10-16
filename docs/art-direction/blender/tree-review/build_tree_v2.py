"""Build, texture, export and render the standalone Arkya hero oak."""
import importlib.util
import json
import math
import sys
from array import array
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parent
STEM = 'arkya-ancient-tree-v2'


def module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / (name + '.py'))
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def configure():
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 40
    scene.cycles.use_denoising = True
    scene.cycles.max_bounces = 6
    scene.cycles.adaptive_threshold = .03
    prefs = bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type = 'OPTIX'
    prefs.refresh_devices()
    for device in prefs.devices:
        device.use = device.type == 'OPTIX'
    scene.cycles.device = 'GPU'
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.resolution_percentage = 100
    scene.view_settings.view_transform = 'AgX'
    scene.view_settings.look = 'AgX - Medium High Contrast'
    bpy.context.preferences.filepaths.save_version = 0


def merge_meshes(objects, name):
    select(objects)
    bpy.ops.object.convert(target='MESH')
    if len(objects) > 1:
        bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    return obj


def bark_material():
    mat = bpy.data.materials.new('Oak | warm sculpted bark and sheltered moss')
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    p = nodes.get('Principled BSDF')
    p.inputs['Roughness'].default_value = .86
    p.inputs['Specular IOR Level'].default_value = .25
    coords = nodes.new('ShaderNodeTexCoord')
    mapping = nodes.new('ShaderNodeVectorMath'); mapping.operation = 'MULTIPLY'
    mapping.inputs[1].default_value = (1.4, 1.4, .24)
    links.new(coords.outputs['Object'], mapping.inputs[0])
    grain = nodes.new('ShaderNodeTexNoise')
    grain.inputs['Scale'].default_value = 2.1
    grain.inputs['Detail'].default_value = 2.4
    grain.inputs['Roughness'].default_value = .65
    links.new(mapping.outputs['Vector'], grain.inputs['Vector'])
    ramp = nodes.new('ShaderNodeValToRGB')
    colors = [(0.19,(.12,.067,.032,1)),(.37,(.19,.11,.052,1)),(.55,(.26,.165,.083,1)),(.75,(.33,.23,.13,1))]
    for e in list(ramp.color_ramp.elements)[2:]: ramp.color_ramp.elements.remove(e)
    ramp.color_ramp.elements[0].position = colors[0][0]
    ramp.color_ramp.elements[0].color = colors[0][1]
    ramp.color_ramp.elements[1].position = colors[-1][0]
    ramp.color_ramp.elements[1].color = colors[-1][1]
    for pos, color in colors[1:-1]: ramp.color_ramp.elements.new(pos).color = color
    links.new(grain.outputs['Fac'], ramp.inputs[0])
    split = nodes.new('ShaderNodeSeparateXYZ')
    links.new(coords.outputs['Object'], split.inputs[0])
    height = nodes.new('ShaderNodeMapRange')
    height.inputs['From Min'].default_value = .1
    height.inputs['From Max'].default_value = 2.6
    height.inputs['To Min'].default_value = .7
    height.inputs['To Max'].default_value = 0
    links.new(split.outputs['Z'], height.inputs['Value'])
    patches = nodes.new('ShaderNodeTexNoise')
    patches.inputs['Scale'].default_value = 2.3
    patches.inputs['Detail'].default_value = 2
    links.new(coords.outputs['Object'], patches.inputs['Vector'])
    moss = nodes.new('ShaderNodeMath'); moss.operation = 'MULTIPLY'
    links.new(height.outputs['Result'], moss.inputs[0]); links.new(patches.outputs['Fac'], moss.inputs[1])
    mixed = nodes.new('ShaderNodeMixRGB')
    links.new(moss.outputs[0], mixed.inputs[0]); links.new(ramp.outputs['Color'], mixed.inputs[1])
    mixed.inputs[2].default_value = (.11,.145,.038,1)
    occlusion = nodes.new('ShaderNodeAmbientOcclusion')
    occlusion.inputs['Distance'].default_value = 1.2
    occlusion.samples = 16
    shade = nodes.new('ShaderNodeMapRange')
    shade.inputs['To Min'].default_value = .45
    shade.inputs['To Max'].default_value = 1
    links.new(occlusion.outputs['AO'], shade.inputs['Value'])
    shaded = nodes.new('ShaderNodeMixRGB'); shaded.blend_type = 'MULTIPLY'
    shaded.inputs[0].default_value = 1
    links.new(mixed.outputs[0], shaded.inputs[1]); links.new(shade.outputs[0], shaded.inputs[2])
    links.new(shaded.outputs[0], p.inputs['Base Color'])
    fine = nodes.new('ShaderNodeTexNoise')
    fine.inputs['Scale'].default_value = 30
    fine.inputs['Detail'].default_value = 2
    links.new(mapping.outputs[0], fine.inputs['Vector'])
    bump = nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value = .3
    bump.inputs['Distance'].default_value = .025
    links.new(fine.outputs['Fac'], bump.inputs['Height']); links.new(bump.outputs[0], p.inputs['Normal'])
    return mat


def prepare_wood(objects, bake=True):
    select(objects)
    bpy.ops.object.convert(target='MESH')
    bpy.ops.object.join()
    wood = bpy.context.object
    wood.name = 'Oak | continuous sculpted trunk roots and limbs'
    wood.data.materials.clear(); wood.data.materials.append(bark_material())
    for p in wood.data.polygons:
        p.material_index = 0
        p.use_smooth = True
    if not bake:
        return wood
    select([wood])
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(68), island_margin=.008, correct_aspect=True, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    scene = bpy.context.scene
    scene.cycles.samples = 8
    scene.render.bake.margin = 12
    scene.render.bake.use_selected_to_active = False
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    original = wood.data.materials[0]
    node = original.node_tree.nodes.new('ShaderNodeTexImage')
    original.node_tree.nodes.active = node
    baked = {}
    for kind, label, color_space in [('DIFFUSE','color','sRGB'), ('NORMAL','normal','Non-Color')]:
        img = bpy.data.images.new('Oak_V2_Bark_' + label, 2048, 2048, alpha=False)
        img.colorspace_settings.name = color_space
        if kind == 'NORMAL': img.generated_color = (.5,.5,1,1)
        node.image = img
        bpy.ops.object.bake(type=kind)
        img.filepath_raw = str(ROOT / f'{STEM}-bark-{label}.png')
        img.file_format = 'PNG'; img.save(); img.pack()
        baked[label] = img
    mat = bpy.data.materials.new('Oak | baked bark 2K')
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    p = nodes.get('Principled BSDF')
    p.inputs['Roughness'].default_value = .86
    p.inputs['Specular IOR Level'].default_value = .25
    color = nodes.new('ShaderNodeTexImage'); color.image = baked['color']
    links.new(color.outputs['Color'], p.inputs['Base Color'])
    tex = nodes.new('ShaderNodeTexImage'); tex.image = baked['normal']
    normal = nodes.new('ShaderNodeNormalMap'); normal.inputs['Strength'].default_value = .8
    links.new(tex.outputs['Color'], normal.inputs['Color']); links.new(normal.outputs[0],p.inputs['Normal'])
    wood.data.materials.clear(); wood.data.materials.append(mat)
    return wood


def camera(position, target, name):
    data = bpy.data.cameras.new(name)
    data.type = 'ORTHO'; data.ortho_scale = 14.7; data.clip_end = 200
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = position
    obj.rotation_euler = (Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj


def stage():
    scene = bpy.context.scene
    world = bpy.data.worlds.new('Oak review | soft neutral daylight')
    world.use_nodes = True
    world.node_tree.nodes['Background'].inputs['Color'].default_value = (.25,.30,.34,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value = .5
    scene.world = world
    for name, position, energy, size, color in [
        ('Warm window',(-8,-10,16),3000,8,(1,.9,.74)),
        ('Cool sky',(9,-3,11),1700,9,(.72,.85,1)),
        ('Canopy rim',(2,8,15),3400,7,(1,.92,.72))]:
        data = bpy.data.lights.new(name,'AREA'); data.energy=energy; data.shape='DISK'; data.size=size; data.color=color
        obj = bpy.data.objects.new(name,data); scene.collection.objects.link(obj); obj.location=position
        obj.rotation_euler=(Vector((0,0,5))-obj.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.035))
    floor=bpy.context.object; floor.name='Review only | neutral ground'
    mat=bpy.data.materials.new('Review ground'); mat.use_nodes=True
    p=mat.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(.19,.22,.22,1); p.inputs['Roughness'].default_value=.94
    floor.data.materials.append(mat)
    scene.camera=camera((15,-21,13),(0,0,5),'Oak review front')
    scene.render.resolution_x=1500; scene.render.resolution_y=1500
    scene.cycles.samples=48
    return scene


def stats(objects):
    verts=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    low=[min(v[i] for v in verts) for i in range(3)]
    high=[max(v[i] for v in verts) for i in range(3)]
    return {'version':2,'dimensions_m':[round(high[i]-low[i],3) for i in range(3)],
            'mesh_count':len(objects),'vertices':len(verts),
            'triangles':sum(len(p.vertices)-2 for o in objects for p in o.data.polygons),
            'description':'New standalone stylized oak; reconstructed trunk, fused roots and ramifications, connected foliage. Not an extraction of scene V3.'}


def shade_canopy(objects, leaves):
    """Bake soft sky occlusion per blade for viewers without foliage self-shadows."""
    vertices, polygons = [], []
    for obj in objects:
        start = len(vertices)
        vertices.extend(obj.matrix_world @ v.co for v in obj.data.vertices)
        polygons.extend(tuple(start + i for i in face.vertices) for face in obj.data.polygons)
    bvh = BVHTree.FromPolygons(vertices, polygons)
    rays = [Vector((0,0,1))]
    for i in range(7):
        angle = i * math.tau / 7
        rays.append(Vector((math.cos(angle)*.83,math.sin(angle)*.83,.75)).normalized())
    attribute = leaves.data.color_attributes['COLOR_0']
    colors = array('f', [0]) * (len(attribute.data)*4)
    attribute.data.foreach_get('color', colors)
    mesh = leaves.data
    for first in range(0,len(mesh.vertices),11):
        center = mesh.vertices[first+10].co + Vector((0,0,.035))
        blocked = 0.0
        for ray in rays:
            hit, normal, index, distance = bvh.ray_cast(center,ray,2.5)
            if hit is not None:
                blocked += .55 + .45 * (1-distance/2.5)
        factor = 1 - .68 * blocked/len(rays)
        for i in range(first,first+11):
            for c in range(3): colors[i*4+c] *= factor
    attribute.data.foreach_set('color', colors)
    leaves['baked_sky_occlusion'] = 'Eight sky rays per connected blade; embedded in COLOR_0'
    print('CANOPY_OCCLUSION_READY',flush=True)


def main():
    draft='--draft' in sys.argv
    bpy.ops.wm.read_factory_settings(use_empty=True)
    configure()
    structure=module('build_tree_structure_v2').build_structure()
    print('STRUCTURE_READY',len(structure['foliage_anchors']),flush=True)
    wood=prepare_wood(structure['wood_objects'],bake=not draft)
    foliage=module('build_tree_foliage_v2').build_foliage(structure['foliage_anchors'])
    leaves = merge_meshes(foliage['leaf_objects'], 'Oak | layered connected foliage')
    twigs = merge_meshes(foliage['twig_objects'], 'Oak | living branchlet network')
    objects=[wood,leaves,twigs]
    select(objects); bpy.ops.object.convert(target='MESH')
    shade_canopy(objects,leaves)
    info=stats(objects)
    info['foliage_anchors']=len(structure['foliage_anchors'])
    info['leaves']=foliage.get('leaf_count')
    info['canopy_shading']='Soft sky occlusion baked into per-vertex color'
    for o in objects: o['Arkya_asset']='Ancient oak V2'
    select(objects)
    if not draft:
        bpy.ops.export_scene.gltf(filepath=str(ROOT/(STEM+'.glb')),export_format='GLB',use_selection=True,
             export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_cameras=False,export_lights=False,export_tangents=True)
        info['glb_bytes']=(ROOT/(STEM+'.glb')).stat().st_size
    print('TREE_GEOMETRY',json.dumps(info),flush=True)
    scene=stage()
    if draft: scene.render.resolution_percentage=70; scene.cycles.samples=24
    suffix='-draft' if draft else ''
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/(STEM+suffix+'.blend')),compress=True)
    (ROOT/(STEM+suffix+'.json')).write_text(json.dumps(info,indent=2,ensure_ascii=False),encoding='utf-8')
    for view, position in [('front',(15,-21,13)),('back',(-16,18,11))]:
        scene.camera.location=position
        scene.camera.rotation_euler=(Vector((0,0,5))-scene.camera.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(ROOT/(STEM+suffix+'-'+view+'.png'))
        bpy.ops.render.render(write_still=True)
    print('OAK_V2_COMPLETE',flush=True)


if __name__=='__main__': main()
