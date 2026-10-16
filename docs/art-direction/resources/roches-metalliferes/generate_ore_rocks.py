"""Twelve painted ore-rock studies, authored geometry and embedded standard glTF PBR.

Geometry preparation is CPU-only. Pass --gpu explicitly for authorized bake/render runs.
"""
import argparse
from array import array
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parent
SOURCE=ROOT.parent/'sources'/'stone-painted.png'
SHAPES={
    'nodule':{'id':'nodule','label':'Nodule','height':.7,'description':'Compact rounded nodule with an exposed ore pocket'},
    'bloc-fendu':{'id':'bloc-fendu','label':'Bloc fendu','height':1.3,'description':'Two split rock masses with a metallic intrusion in the fracture'},
    'filon-oblique':{'id':'filon-oblique','label':'Filon oblique','height':2.2,'description':'Leaning layered slab with a diagonal band of embedded ore'},
    'affleurement':{'id':'affleurement','label':'Affleurement','height':3.2,'description':'Broad terraced outcrop with several exposed ore zones'}}
MATERIALS={
    'fer':{'id':'fer','label':'Fer acier','description':'Cool blue steel plates in slate grey rock'},
    'cuivre':{'id':'cuivre','label':'Cuivre patiné','description':'Warm copper with restrained green patina'},
    'or':{'id':'or','label':'Or stylisé','description':'Satin golden ore embedded in dark warm stone'}}


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]


def material(name,color,metallic=0,roughness=.8):
    mat=bpy.data.materials.new(name);mat.use_nodes=True;mat.diffuse_color=(*color,1)
    p=mat.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1);p.inputs['Metallic'].default_value=metallic
    p.inputs['Roughness'].default_value=roughness;p.inputs['Specular IOR Level'].default_value=.25
    return mat


def soften(obj,width):
    select([obj])
    bevel=obj.modifiers.new('Soft sculpted edges','BEVEL');bevel.width=width;bevel.segments=3
    bevel.affect='EDGES';bevel.limit_method='ANGLE';bevel.angle_limit=.28
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for polygon in obj.data.polygons:polygon.use_smooth=True
    weighted=obj.modifiers.new('Broad rock planes','WEIGHTED_NORMAL');weighted.keep_sharp=True;weighted.weight=40
    bpy.ops.object.modifier_apply(modifier=weighted.name)


def rock(name,position,dimensions,lean,seed,mat):
    rng=random.Random(seed);points=[]
    for level,(height,radius) in enumerate(((0,.78),(.22,1),(.65,.94),(1,.62))):
        phase=.05*math.sin(level*1.8+seed)
        for index in range(8):
            angle=index*math.tau/8+phase
            x=math.copysign(abs(math.cos(angle))**.65,math.cos(angle))
            y=math.copysign(abs(math.sin(angle))**.65,math.sin(angle))
            jitter=rng.uniform(.92,1.08)
            z=height if level==0 else height+rng.uniform(-.045,.045)
            points.append((position[0]+x*dimensions[0]*radius*.5*jitter+lean[0]*height,
                           position[1]+y*dimensions[1]*radius*.5*jitter+lean[1]*height,
                           position[2]+z*dimensions[2]))
    bm=bmesh.new()
    for point in points:bm.verts.new(point)
    bmesh.ops.convex_hull(bm,input=list(bm.verts),use_existing_faces=False)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    mesh=bpy.data.meshes.new(name);bm.to_mesh(mesh);bm.free();mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat);soften(obj,min(dimensions)*.075)
    return obj


def rock_bvh(objects):
    vertices=[];faces=[]
    for obj in objects:
        offset=len(vertices);vertices.extend(obj.matrix_world@v.co for v in obj.data.vertices)
        faces.extend(tuple(offset+i for i in p.vertices) for p in obj.data.polygons)
    return BVHTree.FromPolygons(vertices,faces)


def surface(tree,x,z,angle=0):
    normal=Vector((math.sin(angle),-math.cos(angle),0))
    tangent=Vector((math.cos(angle),math.sin(angle),0))
    origin=tangent*x+normal*12+Vector((0,0,z))
    point,normal,index,distance=tree.ray_cast(origin,-normal,30)
    return (point,normal) if point is not None else None


def ore_plate(name,point,normal,width,height,seed,mat,tree=None):
    rng=random.Random(seed);normal=normal.normalized()
    side=Vector((1,0,0));side-=normal*side.dot(normal)
    if side.length<.1:side=Vector((0,1,0));side-=normal*side.dot(normal)
    side.normalize();up=normal.cross(side).normalized()
    angle=rng.uniform(-.45,.45);side,up=side*math.cos(angle)+up*math.sin(angle),up*math.cos(angle)-side*math.sin(angle)
    thickness=min(width,height)*rng.uniform(.14,.19)
    center=point
    outline=[]
    corners=9
    for index in range(corners):
        a=index*math.tau/corners+rng.uniform(-.17,.17)
        radius=rng.uniform(.80,1.13)
        outline.append((math.cos(a)*width*.61*radius,math.sin(a)*height*.56*radius))
    vertices=[]
    for scale,depth in ((1,-thickness*.60),(.93,-thickness*.04)):
        for x,y in outline:
            contact=center+side*(x*scale)+up*(y*scale)
            if tree:
                projected,_,_,_=tree.ray_cast(contact+normal*1.5,-normal,3)
                if projected is not None and (projected-contact).length<min(width,height)*.18:contact=projected
            vertices.append(tuple(contact+normal*depth))
    top=[(x*.72,y*.72) for x,y in outline]
    crest=(width*.04,-height*.035,thickness*1.05)
    heights={0:thickness*.46,3:thickness*.24,6:thickness*.61}
    top_depths={}
    for first,last in ((0,3),(3,6),(6,0)):
        a=top[first];b=top[last]
        plane=np.linalg.solve(np.array([[a[0],a[1],1],[b[0],b[1],1],[crest[0],crest[1],1]]),
                              np.array([heights[first],heights[last],crest[2]]))
        for index in (range(first,last) if last else range(first,corners)):
            x,y=top[index];top_depths[index]=float(plane@[x,y,1])
    for index,(x,y) in enumerate(top):vertices.append(tuple(center+side*x+up*y+normal*top_depths[index]))
    crest_index=len(vertices);vertices.append(tuple(center+side*crest[0]+up*crest[1]+normal*crest[2]))
    faces=[tuple(reversed(range(corners))),
           (18,19,20,21,crest_index),(21,22,23,24,crest_index),(24,25,26,18,crest_index)]
    for ring in range(2):
        for i in range(corners):faces.append((ring*corners+i,ring*corners+(i+1)%corners,(ring+1)*corners+(i+1)%corners,(ring+1)*corners+i))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
    obj.data.materials.append(mat);soften(obj,min(width,height)*.018)
    return obj


def vein(name,hits,width,mat):
    vertices=[];faces=[]
    for index,(point,normal) in enumerate(hits):
        tangent=(hits[min(index+1,len(hits)-1)][0]-hits[max(0,index-1)][0]).normalized()
        side=normal.cross(tangent).normalized()*width
        center=point+normal*width*.12
        for offset,depth in ((-1,-.4),(1,-.4),(1,.45),(-1,.45)):
            vertices.append(tuple(center+side*offset+normal*width*depth))
        if index:
            for j in range(4):faces.append(((index-1)*4+j,(index-1)*4+(j+1)%4,index*4+(j+1)%4,index*4+j))
    faces.extend([(3,2,1,0),tuple((len(hits)-1)*4+i for i in range(4))])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj);mesh.materials.append(mat)
    soften(obj,width*.25)
    return obj


def geometry(shape):
    stone=material('Authoring | stone',(.19,.22,.25));ore=material('Authoring | exposed ore',(.47,.25,.10),1,.38)
    if shape=='nodule':
        blocks=[((0,0,0),(1.0,.88,.67),(.08,.03),11),((-.32,-.19,0),(.43,.52,.30),(-.04,0),12)]
        clusters=[([(-.19,.30),(-.10,.38)],.36,.31,0),([(.22,.42)],.23,.20,0),([(-.13,.51)],.20,.17,.9)]
    elif shape=='bloc-fendu':
        blocks=[((-.39,.03,0),(.89,1.05,1.23),(-.10,.08),21),((.38,.10,0),(.73,.96,1.07),(.09,-.03),22),
                ((.05,-.30,0),(1.20,.66,.29),(.02,0),23)]
        clusters=[([(-.30,.80),(-.24,.96)],.57,.50,0),
                  ([(-.22,.35),(-.28,.45)],.42,.35,0),
                  ([(.40,.65),(.46,.75)],.43,.42,0),([(-.18,.55)],.24,.28,1.05)]
    elif shape=='filon-oblique':
        blocks=[((-.22,.10,0),(.93,1.03,2.18),(.78,.12),31),((-.66,.20,0),(.70,.88,1.1),(.29,.05),32),
                ((.03,-.20,0),(1.55,1.14,.34),(.05,0),33)]
        clusters=[([(-.27,.40),(-.18,.55)],.48,.42,0),
                  ([(.01,.99),(.10,1.16)],.54,.54,0),
                  ([(.34,1.69),(.45,1.83)],.46,.40,0),([(-.20,.69)],.26,.31,1.0)]
    else:
        blocks=[((.64,.28,0),(1.76,1.4,3.12),(.25,.18),41),((-.86,.13,0),(1.52,1.35,2.12),(-.15,.06),42),
                ((-.22,-.64,0),(2.16,1.08,1.10),(.06,0),43),((1.00,-.27,0),(1.21,1.12,1.68),(.18,0),44),
                ((-1.43,-.40,0),(.87,.84,.50),(-.04,0),45)]
        clusters=[([(-.78,1.18),(-.58,1.40)],.79,.69,0),
                  ([(.57,2.11),(.72,2.36)],.84,.77,0),
                  ([(-.31,.59),(-.06,.70)],.69,.43,0),
                  ([(.25,1.40),(.28,1.55)],.36,.36,1.05),([(.94,1.11)],.34,.26,0)]
    rocks=[rock('Stone mass %02d'%i,*spec,stone) for i,spec in enumerate(blocks)]
    tree=rock_bvh(rocks);ores=[];seed=700
    for positions,width,height,angle in clusters:
        hits=[]
        for index,(x,z) in enumerate(positions):
            hit=surface(tree,x,z,angle)
            if hit:
                seed+=13;point,normal=hit
                size=1 if index==0 else .42
                ores.append(ore_plate('Embedded ore plate %03d'%seed,point,normal,width*(.88+.20*math.sin(seed))*size,height*size,seed,ore,tree));hits.append(hit)
                if index%3==1:
                    chip=surface(tree,x+width*.29,z-height*.12,angle)
                    if chip:ores.append(ore_plate('Small ore edge %03d'%seed,*chip,width*.42,height*.33,seed+3,ore,tree))
        segments=[];segment=[]
        for first,last in zip(positions[:-1],positions[1:]):
            for step in range(8):
                t=step/8;x=first[0]*(1-t)+last[0]*t;z=first[1]*(1-t)+last[1]*t
                hit=surface(tree,x,z,angle)
                if hit and (not segment or (hit[0]-segment[-1][0]).length<width*.52):segment.append(hit)
                else:
                    if len(segment)>1:segments.append(segment)
                    segment=[hit] if hit else []
        if len(segment)>1:segments.append(segment)
        for index,segment in enumerate(segments):ores.append(vein('Anchored metallic seam %03d-%02d'%(seed,index),segment,width*.045,ore))
    objects=rocks+ores;select(objects);bpy.ops.object.join();obj=bpy.context.object
    obj.name='Ore Rock | '+SHAPES[shape]['label']
    points=np.array([v.co[:] for v in obj.data.vertices]);low=points.min(axis=0);high=points.max(axis=0)
    factor=SHAPES[shape]['height']/(high[2]-low[2]);offset=np.array([(low[0]+high[0])/2,(low[1]+high[1])/2,low[2]])
    points=(points-offset)*factor;obj.data.vertices.foreach_set('co',points.ravel());obj.data.update()
    obj.location=(0,0,0)
    select([obj]);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(63),island_margin=.014,correct_aspect=True,scale_to_bounds=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    obj['design']='Soft sculpted rock masses with irregular embedded metal plates and connected seams'
    obj['shape_id']=shape;obj['ore_plate_count']=len(ores)
    select([obj]);triangulate=obj.modifiers.new('Portable triangulation','TRIANGULATE');triangulate.keep_custom_normals=True
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    return obj


class Graph:
    def __init__(self,name):
        self.mat=material(name,(.2,.2,.2));self.nodes=self.mat.node_tree.nodes;self.links=self.mat.node_tree.links
        self.p=self.nodes.get('Principled BSDF')
    def node(self,kind):return self.nodes.new(kind)
    def put(self,value,socket):
        if hasattr(value,'node'):self.links.new(value,socket)
        else:socket.default_value=value
    def math(self,op,a,b=0):
        n=self.node('ShaderNodeMath');n.operation=op;self.put(a,n.inputs[0]);self.put(b,n.inputs[1]);return n.outputs[0]
    def ramp(self,value,colors):
        n=self.node('ShaderNodeValToRGB');n.color_ramp.interpolation='EASE'
        for element,(position,color) in zip(n.color_ramp.elements,(colors[0],colors[-1])):element.position=position;element.color=color
        for position,color in colors[1:-1]:n.color_ramp.elements.new(position).color=color
        self.put(value,n.inputs[0]);return n.outputs['Color']
    def mix(self,factor,a,b):
        n=self.node('ShaderNodeMixRGB');self.put(factor,n.inputs[0]);self.put(a,n.inputs[1]);self.put(b,n.inputs[2]);return n.outputs[0]
    def noise(self,vector,scale,detail=0):
        n=self.node('ShaderNodeTexNoise');n.inputs['Scale'].default_value=scale;n.inputs['Detail'].default_value=detail
        self.put(vector,n.inputs['Vector']);return n.outputs['Fac']


def authoring_materials(kind):
    stone=Graph('Painted rock | '+kind);ore=Graph('Painted metal | '+kind)
    image=bpy.data.images.load(str(SOURCE),check_existing=True);image.pack()
    pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels)
    samples=pixels.reshape((-1,4))[::19,:3];samples=np.where(samples<=.04045,samples/12.92,((samples+.055)/1.055)**2.4)
    lo,hi=np.percentile(samples@np.array([.2126,.7152,.0722]),[3,97])
    coords=stone.node('ShaderNodeTexCoord');mapping=stone.node('ShaderNodeVectorMath');mapping.operation='SCALE';mapping.inputs['Scale'].default_value=.83
    stone.put(coords.outputs['Object'],mapping.inputs[0])
    texture=stone.node('ShaderNodeTexImage');texture.image=image;texture.projection='BOX';texture.projection_blend=.28
    stone.put(mapping.outputs[0],texture.inputs['Vector'])
    gray=stone.node('ShaderNodeRGBToBW');stone.put(texture.outputs['Color'],gray.inputs[0])
    grain=stone.math('DIVIDE',stone.math('SUBTRACT',gray.outputs[0],float(lo)),float(hi-lo))
    grain=stone.math('ADD',.125,stone.math('MULTIPLY',grain,.75))
    palettes={'fer':((.028,.041,.054),(.080,.105,.127),(.155,.18,.20)),
              'cuivre':((.043,.044,.052),(.100,.110,.121),(.180,.185,.19)),
              'or':((.025,.035,.045),(.070,.085,.100),(.140,.155,.17))}
    palette=palettes[kind];color=stone.ramp(grain,[(.0,(*palette[0],1)),(.5,(*palette[1],1)),(1,(*palette[2],1))])
    roughness=stone.math('SUBTRACT',.94,stone.math('MULTIPLY',grain,.10))
    bump=stone.node('ShaderNodeBump');bump.inputs['Strength'].default_value=.22;bump.inputs['Distance'].default_value=.025
    stone.put(grain,bump.inputs['Height']);stone.put(bump.outputs[0],stone.p.inputs['Normal'])
    channels_stone={'basecolor':color,'roughness':roughness,'metallic':stone.math('ADD',0,0)}
    coords=ore.node('ShaderNodeTexCoord');broad=ore.noise(coords.outputs['Object'],4.2,0)
    palettes={'fer':((.20,.27,.32),(.39,.49,.56),(.63,.72,.76)),
              'cuivre':((.27,.085,.03),(.54,.23,.075),(.78,.43,.18)),
              'or':((.36,.17,.025),(.67,.39,.065),(.90,.68,.25))}
    p=palettes[kind];color=ore.ramp(broad,[(.2,(*p[0],1)),(.5,(*p[1],1)),(.8,(*p[2],1))])
    metallic=ore.math('ADD',1,0);roughness=ore.math('ADD',.39,ore.math('MULTIPLY',broad,.13))
    if kind=='cuivre':
        mask=ore.ramp(ore.noise(coords.outputs['Object'],2.8,0),[(.56,(0,0,0,1)),(.69,(.83,.83,.83,1))])
        color=ore.mix(mask,color,(.032,.19,.145,1));metallic=ore.math('SUBTRACT',1,ore.math('MULTIPLY',mask,.72))
        roughness=ore.mix(mask,roughness,(.64,.64,.64,1))
    bump=ore.node('ShaderNodeBump');bump.inputs['Strength'].default_value=.13;bump.inputs['Distance'].default_value=.012
    ore.put(broad,bump.inputs['Height']);ore.put(bump.outputs[0],ore.p.inputs['Normal'])
    channels_ore={'basecolor':color,'roughness':roughness,'metallic':metallic}
    for graph,channels in ((stone,channels_stone),(ore,channels_ore)):
        graph.put(channels['basecolor'],graph.p.inputs['Base Color']);graph.put(channels['roughness'],graph.p.inputs['Roughness']);graph.put(channels['metallic'],graph.p.inputs['Metallic'])
        graph.mat.use_fake_user=True
    return [(stone,channels_stone),(ore,channels_ore)]


def configure(gpu=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU'
    if gpu:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.refresh_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
        scene.cycles.device='GPU'
    scene.cycles.use_denoising=True;scene.cycles.samples=32;scene.cycles.max_bounces=6
    scene.view_settings.view_transform='AgX';scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=.15;bpy.context.preferences.filepaths.save_version=0


def bake(obj,kind,asset_id):
    textures=ROOT/'textures'/asset_id;textures.mkdir(parents=True,exist_ok=True)
    graphs=authoring_materials(kind)
    indices=[polygon.material_index for polygon in obj.data.polygons]
    obj.data.materials.clear()
    for graph,_ in graphs:obj.data.materials.append(graph.mat)
    for polygon,index in zip(obj.data.polygons,indices):polygon.material_index=index
    select([obj]);scene=bpy.context.scene;scene.cycles.samples=1
    scene.render.bake.use_clear=False;scene.render.bake.margin=20;scene.render.bake.use_selected_to_active=False
    targets=[];emitters=[]
    for graph,_ in graphs:
        target=graph.node('ShaderNodeTexImage');graph.nodes.active=target;targets.append(target)
        emitters.append(graph.node('ShaderNodeEmission'))
    images={}
    for channel in ('basecolor','normal','roughness','metallic'):
        image=bpy.data.images.new(asset_id+'-'+channel,2048,2048,alpha=False)
        image.colorspace_settings.name='sRGB' if channel=='basecolor' else 'Non-Color'
        image.generated_color={'basecolor':(.18,.19,.20,1),'normal':(.5,.5,1,1),'roughness':(.85,.85,.85,1),'metallic':(0,0,0,1)}[channel]
        for (graph,channels),target,emitter in zip(graphs,targets,emitters):
            target.image=image;output=graph.nodes.get('Material Output')
            if channel=='normal':graph.links.new(graph.p.outputs[0],output.inputs['Surface'])
            else:
                for link in list(emitter.inputs['Color'].links):graph.links.remove(link)
                graph.put(channels[channel],emitter.inputs['Color']);graph.links.new(emitter.outputs[0],output.inputs['Surface'])
        print('BAKE',asset_id,channel,flush=True);bpy.ops.object.bake(type='NORMAL' if channel=='normal' else 'EMIT')
        image.filepath_raw=str(textures/(channel+'.png'));image.file_format='PNG';image.save();image.pack();images[channel]=image
    for (graph,_),target,emitter in zip(graphs,targets,emitters):
        graph.links.new(graph.p.outputs[0],graph.nodes.get('Material Output').inputs['Surface']);graph.nodes.remove(target);graph.nodes.remove(emitter)
    final=Graph('Ore Rock | '+kind+' | baked PBR 2K')
    for channel,socket in [('basecolor','Base Color'),('roughness','Roughness'),('metallic','Metallic')]:
        texture=final.node('ShaderNodeTexImage');texture.image=images[channel];final.put(texture.outputs['Color'],final.p.inputs[socket])
    texture=final.node('ShaderNodeTexImage');texture.image=images['normal'];normal=final.node('ShaderNodeNormalMap');normal.inputs['Strength'].default_value=.8
    final.put(texture.outputs['Color'],normal.inputs['Color']);final.put(normal.outputs[0],final.p.inputs['Normal'])
    obj.data.materials.clear();obj.data.materials.append(final.mat)
    for polygon in obj.data.polygons:polygon.material_index=0


def stage(obj):
    scene=bpy.context.scene;points=np.array([v.co[:] for v in obj.data.vertices]);size=points.max(axis=0)-points.min(axis=0)
    extent=max(size);height=size[2]
    world=bpy.data.worlds.new('Neutral daylight');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.30,.34,.36,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55;scene.world=world
    for name,position,power,color in [('Soft key',(-3,-4,5),1100,(1,.91,.79)),('Sky fill',(4,-1,3),650,(.72,.86,1)),('Rim',(1,4,4),1200,(1,.96,.86))]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power*extent**2;data.shape='DISK';data.size=extent*3
        light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=Vector(position)*extent
        light.rotation_euler=(Vector((0,0,height*.45))-light.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=extent*200,location=(0,0,-.008*extent));floor=bpy.context.object;floor.name='Studio floor'
    floor.data.materials.append(material('Studio neutral',(.28,.31,.30),0,.94))
    data=bpy.data.cameras.new('Asset review');data.type='ORTHO';data.ortho_scale=max(size[0]*1.28,height*1.40,size[1]*1.4)
    camera=bpy.data.objects.new('Asset review',data);scene.collection.objects.link(camera)
    camera.location=Vector((2.7,-4.4,2.7))*extent
    camera.rotation_euler=(Vector((0,0,height*.46))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
    scene.render.resolution_x=1100;scene.render.resolution_y=1100;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
    return size


def save_manifest(entry):
    path=ROOT/'manifest.json'
    doc=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {'family':'roches-metalliferes','shapes':list(SHAPES.values()),'materials':list(MATERIALS.values()),'assets':[]}
    doc['assets']=[old for old in doc['assets'] if old['id']!=entry['id']]+[entry]
    order={key:i for i,key in enumerate(SHAPES)};materials={key:i for i,key in enumerate(MATERIALS)}
    doc['assets'].sort(key=lambda a:(order[a['shape']],materials[a['material']]))
    path.write_text(json.dumps(doc,ensure_ascii=False,indent=2),encoding='utf-8')


def generate(shape,kind,gpu=False,prepare=False,draft_cpu=False):
    bpy.ops.wm.read_factory_settings(use_empty=True);configure(gpu)
    obj=geometry(shape);asset_id=shape+'-'+kind
    folder=ROOT/'assets'/shape;folder.mkdir(parents=True,exist_ok=True)
    if prepare:
        size=stage(obj);bpy.ops.wm.save_as_mainfile(filepath=str(folder/(shape+'-geometry.blend')))
        print('GEOMETRY_READY',shape,'triangles',sum(len(p.vertices)-2 for p in obj.data.polygons),flush=True);return
    if draft_cpu:
        graphs=authoring_materials(kind);indices=[p.material_index for p in obj.data.polygons]
        obj.data.materials.clear()
        for graph,_ in graphs:obj.data.materials.append(graph.mat)
        for polygon,index in zip(obj.data.polygons,indices):polygon.material_index=index
        stage(obj);scene=bpy.context.scene;scene.cycles.samples=12
        scene.render.resolution_x=800;scene.render.resolution_y=800
        scene.render.filepath=str(folder/(asset_id+'-draft-cpu.png'));bpy.ops.render.render(write_still=True)
        print('CPU_DRAFT_READY',scene.render.filepath,flush=True);return
    if not gpu:raise RuntimeError('Final bake/render requires explicit --gpu authorization in this workflow')
    bake(obj,kind,asset_id);size=stage(obj);select([obj])
    glb=folder/(asset_id+'.glb');blend=folder/(asset_id+'.blend');preview=folder/(asset_id+'-preview.png')
    bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,export_apply=True,
                             export_materials='EXPORT',export_yup=True,export_animations=False,export_cameras=False,
                             export_lights=False,export_tangents=True,export_vertex_color='NAME',export_vertex_color_name='COLOR_0',export_all_vertex_colors=False)
    scene=bpy.context.scene;scene.cycles.samples=32;scene.render.filepath=str(preview);bpy.ops.render.render(write_still=True)
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    triangles=sum(len(p.vertices)-2 for p in obj.data.polygons)
    entry={'id':asset_id,'shape':shape,'material':kind,'label':SHAPES[shape]['label']+' · '+MATERIALS[kind]['label'],
           'glb':str(glb.relative_to(ROOT)).replace('\\','/'),'blend':str(blend.relative_to(ROOT)).replace('\\','/'),
           'preview':str(preview.relative_to(ROOT)).replace('\\','/'),'height':round(float(size[2]),4),
           'width':round(float(size[0]),4),'depth':round(float(size[1]),4),'triangles':triangles,
           'textures':'textures/'+asset_id,'texture_resolution':[2048,2048],
           'pivot':'XY bounding-box center, ground at Z=0','ore_geometry':'Embedded irregular plates and connected metal seams',
           'painted_source':'../sources/stone-painted.png'}
    assert triangles<20000
    blob=glb.read_bytes();length,_=struct.unpack_from('<II',blob,12);gltf=json.loads(blob[20:20+length])
    assert not any('uri' in item for item in gltf.get('images',[])+gltf.get('buffers',[]))
    entry['embedded_images']=len(gltf.get('images',[]));entry['sha256']=hashlib.sha256(blob).hexdigest()
    save_manifest(entry);print('ASSET_READY',asset_id,entry,flush=True)


def compare(gpu):
    if not gpu:raise RuntimeError('Comparison render requires --gpu')
    bpy.ops.wm.read_factory_settings(use_empty=True);configure(gpu)
    objects=[];cursor=0
    for shape in SHAPES:
        path=ROOT/'assets'/shape/(shape+'-fer.blend')
        with bpy.data.libraries.load(str(path),link=False) as (source,destination):
            destination.objects=[name for name in source.objects if name.startswith('Ore Rock |')]
        obj=destination.objects[0];bpy.context.scene.collection.objects.link(obj)
        bpy.context.view_layer.update()
        width=obj.dimensions.x;obj.location.x=cursor+width*.5;cursor+=width+.65;objects.append(obj)
    for obj in objects:obj.location.x-=cursor*.5-.325
    dummy=objects[-1];stage(dummy)
    scene=bpy.context.scene;camera=scene.camera;camera.location=(0,-16,8)
    camera.rotation_euler=(Vector((0,0,1.3))-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=cursor+.8
    label_mat=material('Comparison labels',(.065,.085,.08),0,1)
    for obj,shape in zip(objects,SHAPES):
        data=bpy.data.curves.new('Size label','FONT');data.body=SHAPES[shape]['label']+' · '+str(SHAPES[shape]['height']).replace('.',',')+' m'
        data.align_x='CENTER';data.size=.20
        label=bpy.data.objects.new('Size label',data);scene.collection.objects.link(label);label.location=(obj.location.x,-1.5,.03)
        label.rotation_euler=(math.pi/2,0,0);data.materials.append(label_mat)
    scene.render.resolution_x=1800;scene.render.resolution_y=1000;scene.cycles.samples=48
    scene.render.filepath=str(ROOT/'comparaison-tailles.png');bpy.ops.render.render(write_still=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--shape',choices=['all']+list(SHAPES),default='bloc-fendu')
    parser.add_argument('--material',choices=['all']+list(MATERIALS),default='cuivre')
    parser.add_argument('--prepare',action='store_true');parser.add_argument('--gpu',action='store_true');parser.add_argument('--compare',action='store_true')
    parser.add_argument('--draft-cpu',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    if args.compare:compare(args.gpu)
    else:
        for shape in SHAPES if args.shape=='all' else [args.shape]:
            for kind in MATERIALS if args.material=='all' and not args.prepare else [args.material if args.material!='all' else 'fer']:
                generate(shape,kind,args.gpu,args.prepare,args.draft_cpu)
