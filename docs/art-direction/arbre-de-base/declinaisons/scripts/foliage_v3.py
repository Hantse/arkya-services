"""Painted foliage with real per-blade sky occlusion, without geometry changes.

apply_foliage(leaves, twigs, variant) accepts mystique, brule, or moussu.
COLOR_0 and one additional leaf UV channel carry all detail into glTF.
"""
from pathlib import Path
import hashlib
import math

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


ROOT=Path(__file__).resolve().parents[1]
TEXTURE_PATH=ROOT/'matieres-v3'/'source'/'leaf-painted-detail.png'
UV_NAME='Arkya_LeafPaint_UV'
PALETTES={
    'mystique':((.038,.102,.104),(.082,.228,.182),(.268,.420,.265)),
    'brule':((.068,.049,.067),(.225,.115,.052),(.445,.255,.093)),
    'moussu':((.043,.096,.074),(.105,.235,.115),(.285,.410,.145)),
}
TWIG_COLORS={'mystique':(.13,.094,.135,1),'brule':(.055,.046,.065,1),'moussu':(.195,.125,.072,1)}
_OCCLUSION_CACHE={}


def positions(obj):
    points=np.empty(len(obj.data.vertices)*3,dtype=np.float32)
    obj.data.vertices.foreach_get('co',points)
    return points.reshape((-1,3))


def world_positions(obj):
    points=positions(obj)
    matrix=np.asarray(obj.matrix_world,dtype=np.float32)
    return points@matrix[:3,:3].T+matrix[:3,3]


def sky_occlusion(leaves,twigs):
    points=world_positions(leaves)
    if len(points)%11:raise ValueError('Expected the approved 11-vertex leaf topology')
    wood=bpy.data.objects.get('Oak | continuous sculpted trunk roots and limbs')
    objects=([wood] if wood else [])+[twigs,leaves]
    digest=hashlib.sha256()
    for obj in objects:digest.update(world_positions(obj).tobytes())
    key=digest.hexdigest()
    if key in _OCCLUSION_CACHE:return _OCCLUSION_CACHE[key].copy()
    all_points=[];all_triangles=[];offset=0;leaf_first_triangle=0
    for obj in objects:
        verts=world_positions(obj)
        obj.data.calc_loop_triangles()
        triangles=np.empty(len(obj.data.loop_triangles)*3,dtype=np.int32)
        obj.data.loop_triangles.foreach_get('vertices',triangles)
        if obj==leaves:leaf_first_triangle=sum(len(item) for item in all_triangles)
        all_points.append(verts);all_triangles.append(triangles.reshape((-1,3))+offset)
        offset+=len(verts)
    vertices=np.concatenate(all_points);triangles=np.concatenate(all_triangles)
    bvh=BVHTree.FromPolygons(vertices.tolist(),triangles.tolist(),all_triangles=True)
    rays=[(Vector((0,0,1)),1.0)]
    for i in range(7):
        angle=math.tau*(i+.17)/7
        rays.append((Vector((math.cos(angle)*.76,math.sin(angle)*.76,.65)).normalized(),.85))
    for i in range(4):
        angle=math.tau*(i+.43)/4
        rays.append((Vector((math.cos(angle)*.94,math.sin(angle)*.94,.34)).normalized(),.45))
    weight=sum(w for _,w in rays)
    centers=points.reshape((-1,11,3))[:,10,:]
    occlusion=np.zeros(len(centers),dtype=np.float32)
    maximum_distance=2.8
    for index,center in enumerate(centers):
        location=Vector(center)
        own_first=leaf_first_triangle+index*10
        blocked=0.0
        for direction,importance in rays:
            start=location+direction*.018
            hit,normal,face,distance=bvh.ray_cast(start,direction,maximum_distance)
            # The folded blade itself must not count as a neighboring occluder.
            for _ in range(2):
                if face is None or not own_first<=face<own_first+10:break
                start=hit+direction*.018
                hit,normal,face,distance=bvh.ray_cast(start,direction,maximum_distance)
            if face is not None:
                blocked+=importance*(.62+.38*max(0,1-distance/maximum_distance))
        occlusion[index]=blocked/weight
    # Connected 30-blade sprays form coherent shade patches; local depth remains visible.
    if len(occlusion)%30==0:
        spray_mean=occlusion.reshape((-1,30)).mean(axis=1).repeat(30)
        occlusion=.72*occlusion+.28*spray_mean
    _OCCLUSION_CACHE[key]=occlusion.copy()
    print('FOLIAGE_V3_SKY_READY',len(centers),'blades',len(centers)*len(rays),'rays',flush=True)
    return occlusion


def painted_detail_texture():
    existing=bpy.data.images.get('Arkya V3 | painted leaf detail')
    if existing:return existing
    if TEXTURE_PATH.exists():
        image=bpy.data.images.load(str(TEXTURE_PATH),check_existing=True)
        image.name='Arkya V3 | painted leaf detail';image.colorspace_settings.name='sRGB';image.pack()
        return image
    TEXTURE_PATH.parent.mkdir(parents=True,exist_ok=True)
    material=bpy.data.materials.new('Temporary | procedural painted blade')
    material.use_nodes=True
    nodes,links=material.node_tree.nodes,material.node_tree.links
    nodes.clear()
    def math_node(operation,a,b=0):
        n=nodes.new('ShaderNodeMath');n.operation=operation
        for value,socket in [(a,n.inputs[0]),(b,n.inputs[1])]:
            if isinstance(value,(int,float)):socket.default_value=value
            else:links.new(value,socket)
        return n.outputs[0]
    def remap(value,low,high,start,end):
        n=nodes.new('ShaderNodeMapRange');n.clamp=True;n.interpolation_type='SMOOTHSTEP'
        for name,v in [('From Min',low),('From Max',high),('To Min',start),('To Max',end)]:n.inputs[name].default_value=v
        links.new(value,n.inputs['Value']);return n.outputs[0]
    coordinates=nodes.new('ShaderNodeTexCoord');separate=nodes.new('ShaderNodeSeparateXYZ')
    links.new(coordinates.outputs['UV'],separate.inputs[0]);u,v=separate.outputs['X'],separate.outputs['Y']
    center_distance=math_node('ABSOLUTE',math_node('SUBTRACT',v,.5))
    body=math_node('ADD',.855,math_node('MULTIPLY',u,.055))
    body=math_node('ADD',body,remap(v,.05,.95,-.025,.035))
    body=math_node('SUBTRACT',body,math_node('MULTIPLY',math_node('POWER',math_node('MULTIPLY',center_distance,2),2),.035))
    taper=math_node('SINE',math_node('MULTIPLY',u,math.pi))
    vein=math_node('MULTIPLY',remap(center_distance,.006,.022,1,0),taper)
    body=math_node('ADD',body,math_node('MULTIPLY',vein,.075))
    for start in (.24,.46,.67):
        cross=math_node('ABSOLUTE',math_node('SUBTRACT',u,math_node('ADD',start,math_node('MULTIPLY',center_distance,.72))))
        faint=math_node('MULTIPLY',remap(cross,.003,.015,1,0),remap(center_distance,.02,.40,1,0))
        body=math_node('ADD',body,math_node('MULTIPLY',faint,.018))
    emission=nodes.new('ShaderNodeEmission');output=nodes.new('ShaderNodeOutputMaterial')
    links.new(body,emission.inputs['Color']);links.new(emission.outputs[0],output.inputs['Surface'])
    mesh=bpy.data.meshes.new('Temporary leaf texture quad')
    mesh.from_pydata([(0,0,-100),(1,0,-100),(1,1,-100),(0,1,-100)],[],[(0,1,2,3)])
    uv=mesh.uv_layers.new()
    for loop,coord in zip(uv.data,[(0,0),(1,0),(1,1),(0,1)]):loop.uv=coord
    obj=bpy.data.objects.new(mesh.name,mesh);bpy.context.scene.collection.objects.link(obj)
    mesh.materials.append(material)
    image=bpy.data.images.new('Arkya V3 | painted leaf detail',256,256,alpha=False)
    image.colorspace_settings.name='sRGB'
    target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
    scene=bpy.context.scene
    previous=(scene.render.engine,scene.cycles.device,scene.cycles.samples)
    selected=list(bpy.context.selected_objects);active=bpy.context.view_layer.objects.active
    for other in selected:other.select_set(False)
    obj.select_set(True);bpy.context.view_layer.objects.active=obj
    scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1
    scene.render.bake.use_selected_to_active=False;scene.render.bake.margin=4
    bpy.ops.object.bake(type='EMIT')
    image.filepath_raw=str(TEXTURE_PATH);image.file_format='PNG';image.save();image.pack()
    bpy.data.objects.remove(obj,do_unlink=True);bpy.data.meshes.remove(mesh);bpy.data.materials.remove(material)
    for other in selected:other.select_set(True)
    bpy.context.view_layer.objects.active=active
    scene.render.engine,scene.cycles.device,scene.cycles.samples=previous
    return image


def apply_foliage(leaves,twigs,variant):
    if variant not in PALETTES:raise ValueError('Unknown foliage variant: '+variant)
    mesh=leaves.data
    points=positions(leaves)
    if len(points)%11:raise ValueError('Leaf vertex groups must contain exactly 11 points')
    occlusion=sky_occlusion(leaves,twigs)
    centers=points.reshape((-1,11,3))[:,10,:]
    z=centers[:,2];height=(z-z.min())/max(.01,float(np.ptp(z)))
    group_height=height.copy()
    if len(height)%300==0:group_height=height.reshape((-1,300)).mean(axis=1).repeat(300)
    visibility=np.clip(1-occlusion,0,1)
    shade=.35+.65*visibility**.84
    tone=np.clip(.10+.72*(.72*height+.28*group_height)+.075*visibility,0,1)
    palette=np.asarray(PALETTES[variant],dtype=np.float32)
    scaled=tone*(len(palette)-1);index=np.minimum(len(palette)-2,scaled.astype(np.int32));fraction=scaled-index
    base=palette[index]*(1-fraction[:,None])+palette[index+1]*fraction[:,None]
    cool=np.asarray((.76,.87,1.0) if variant!='brule' else (.88,.80,1.0))
    tint=cool[None,:]+(1-cool)[None,:]*visibility[:,None]
    colors=np.ones((len(points),4),dtype=np.float32)
    colors[:,:3]=(base*shade[:,None]*tint*1.08).repeat(11,axis=0)
    colors[10::11,:3]*=1.015
    attribute=mesh.color_attributes.get('COLOR_0')
    if attribute is None:attribute=mesh.color_attributes.new(name='COLOR_0',type='BYTE_COLOR',domain='POINT')
    attribute.data.foreach_set('color',colors.reshape(-1));mesh.color_attributes.active_color=attribute
    uv_layer=mesh.uv_layers.get(UV_NAME) or mesh.uv_layers.new(name=UV_NAME)
    outline=np.asarray([(0,.5),(.16,.22),(.34,.01),(.57,0),(.81,.20),
                        (1,.5),(.81,.80),(.57,1),(.34,.99),(.16,.78),(.46,.5)],dtype=np.float32)
    loop_indices=np.empty(len(mesh.loops),dtype=np.int32);mesh.loops.foreach_get('vertex_index',loop_indices)
    uv_layer.data.foreach_set('uv',outline[loop_indices%11].reshape(-1))
    image=painted_detail_texture()
    material=bpy.data.materials.new('Arkya V3 | '+variant+' | painted foliage with true occlusion')
    material.use_nodes=True;material.use_backface_culling=False
    nodes,links=material.node_tree.nodes,material.node_tree.links
    shader=nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value=.78
    shader.inputs['Specular IOR Level'].default_value=.18
    shader.inputs['Metallic'].default_value=0
    color=nodes.new('ShaderNodeVertexColor');color.layer_name='COLOR_0'
    texture=nodes.new('ShaderNodeTexImage');texture.image=image;texture.interpolation='Linear'
    uv=nodes.new('ShaderNodeUVMap');uv.uv_map=UV_NAME;links.new(uv.outputs[0],texture.inputs['Vector'])
    multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=1
    links.new(color.outputs['Color'],multiply.inputs[1]);links.new(texture.outputs['Color'],multiply.inputs[2])
    links.new(multiply.outputs[0],shader.inputs['Base Color'])
    mesh.materials.clear();mesh.materials.append(material)
    for polygon in mesh.polygons:polygon.material_index=0
    twig_material=twigs.data.materials[0].copy()
    twig_material.name='Arkya V3 | '+variant+' | warm living branchlets'
    twig_material.diffuse_color=TWIG_COLORS[variant]
    p=twig_material.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=TWIG_COLORS[variant];p.inputs['Roughness'].default_value=.84
    twigs.data.materials.clear();twigs.data.materials.append(twig_material)
    leaves['foliage_quality']='True 12-ray sky occlusion per blade; coherent crown groups; painted UV vein and blade gradient'
    leaves['foliage_v3_shadow_range']=[float(shade.min()),float(shade.max())]
    mesh.update()
    result={'leaf_count':len(centers),'sky_rays':len(centers)*12,
            'shade_range':[round(float(shade.min()),4),round(float(shade.max()),4)],
            'shade_mean':round(float(shade.mean()),4),'texture_size':[256,256],
            'texture_path':str(TEXTURE_PATH),'uv_channel':UV_NAME,
            'geometry_unchanged':True,'positions_sha256':hashlib.sha256(points.tobytes()).hexdigest()}
    print('FOLIAGE_V3_READY',variant,result,flush=True)
    return result
