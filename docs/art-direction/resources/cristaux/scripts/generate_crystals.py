"""Authored crystal formations with portable, CPU-baked painted PBR atlases."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import random
import struct
import sys

import bpy
import bmesh
from mathutils import Vector, Quaternion

ROOT=Path(__file__).resolve().parents[1]
TEXTURES=ROOT/'textures'
ASSETS=ROOT/'assets'
STONE_SOURCE=ROOT.parent/'sources'/'stone-painted.png'
SIZE=2048
SHAPES={
    'eclat':{'label':'Éclat','height':.55,'description':'Trois pointes compactes issues d’une même matrice.'},
    'bouquet':{'label':'Bouquet','height':1.1,'description':'Sept prismes disposés en éventail, avec un sommet dominant.'},
    'geode':{'label':'Géode ouverte','height':1.9,'description':'Coque minérale ouverte, cavité lisible et cristaux orientés vers la lumière.'},
    'maitresse':{'label':'Formation maîtresse','height':3.,'description':'Grande formation asymétrique, pointes satellites et racines minérales.'},
}
PALETTES={
    'azur':{'label':'Azur','description':'Cœur bleu profond, faces turquoise et pointes glacées.',
            'colors':[(.008,.027,.074,1),(.012,.10,.20,1),(.025,.26,.37,1),(.10,.48,.52,1),(.43,.77,.74,1)],'glow':(.08,.34,.38,1)},
    'amethyste':{'label':'Améthyste','description':'Ombres indigo, faces violettes et arêtes lilas.',
            'colors':[(.022,.009,.06,1),(.075,.024,.15,1),(.23,.065,.32,1),(.43,.17,.49,1),(.70,.47,.72,1)],'glow':(.28,.10,.38,1)},
    'ambre':{'label':'Ambre','description':'Cœur brun roux, faces miel et pointes dorées.',
            'colors':[(.065,.014,.005,1),(.20,.047,.010,1),(.46,.16,.022,1),(.72,.36,.07,1),(.90,.66,.27,1)],'glow':(.42,.18,.025,1)},
}


class Graph:
    def __init__(self,name):
        self.material=bpy.data.materials.new(name);self.material.use_nodes=True
        self.nodes=self.material.node_tree.nodes;self.links=self.material.node_tree.links
        self.p=self.nodes.get('Principled BSDF');self.out=self.nodes.get('Material Output')
    def node(self,type):return self.nodes.new(type)
    def connect(self,value,socket):
        if hasattr(value,'node'):self.links.new(value,socket)
        else:socket.default_value=(value,value,value,1) if socket.type=='RGBA' and isinstance(value,(int,float)) else value
    def math(self,op,a,b=0):
        n=self.node('ShaderNodeMath');n.operation=op
        self.connect(a,n.inputs[0]);self.connect(b,n.inputs[1]);return n.outputs[0]
    def remap(self,v,a,b,c=0,d=1):
        n=self.node('ShaderNodeMapRange');n.clamp=True;n.interpolation_type='SMOOTHSTEP'
        self.connect(v,n.inputs['Value'])
        for name,value in [('From Min',a),('From Max',b),('To Min',c),('To Max',d)]:n.inputs[name].default_value=value
        return n.outputs[0]
    def mix(self,f,a,b):
        n=self.node('ShaderNodeMixRGB');n.blend_type='MIX'
        self.connect(f,n.inputs[0]);self.connect(a,n.inputs[1]);self.connect(b,n.inputs[2]);return n.outputs[0]
    def ramp(self,v,colors):
        n=self.node('ShaderNodeValToRGB');self.connect(v,n.inputs[0]);r=n.color_ramp
        r.elements.remove(r.elements[1])
        for i,color in enumerate(colors):
            e=r.elements[0] if i==0 else r.elements.new(i/(len(colors)-1))
            e.position=i/(len(colors)-1);e.color=color
        r.interpolation='EASE';return n.outputs['Color']


def crystal_graph(palette):
    g=Graph('Arkya | painted '+palette+' | authoring')
    uv=g.node('ShaderNodeTexCoord');separate=g.node('ShaderNodeSeparateXYZ');g.connect(uv.outputs['UV'],separate.inputs[0])
    sx=g.math('MULTIPLY',separate.outputs['X'],4);sy=g.math('MULTIPLY',separate.outputs['Y'],4)
    u=g.math('FRACT',sx);v=g.math('FRACT',sy)
    tile=g.math('ADD',g.math('FLOOR',sx),g.math('MULTIPLY',g.math('FLOOR',sy),4))
    coord=g.node('ShaderNodeCombineXYZ');g.connect(g.math('MULTIPLY',u,2.3),coord.inputs[0]);g.connect(g.math('MULTIPLY',v,2.2),coord.inputs[1]);g.connect(g.math('MULTIPLY',tile,3.7),coord.inputs[2])
    noise=g.node('ShaderNodeTexNoise');g.connect(coord.outputs[0],noise.inputs['Vector']);noise.inputs['Scale'].default_value=1.7;noise.inputs['Detail'].default_value=1.2;noise.inputs['Roughness'].default_value=.42
    brush=g.math('MULTIPLY',g.math('SUBTRACT',noise.outputs['Fac'],.5),.34)
    face=g.math('MULTIPLY',g.math('SINE',g.math('MULTIPLY',tile,2.31)),.13)
    value=g.math('ADD',g.math('ADD',g.math('ADD',.10,g.math('MULTIPLY',g.remap(v,.06,.95),.66)),face),brush)
    diagonal_plane=g.math('ADD',u,g.math('MULTIPLY',v,.58))
    glaze=g.math('MULTIPLY',g.remap(diagonal_plane,.61,.65),g.remap(diagonal_plane,.87,.90,1,0))
    glaze=g.math('MULTIPLY',glaze,g.math('MULTIPLY',g.remap(v,.16,.23),g.remap(v,.70,.83,1,0)))
    value=g.math('ADD',value,g.math('MULTIPLY',glaze,.095))
    colors=PALETTES[palette]['colors'];color=g.ramp(value,colors)
    left=g.remap(u,.06,.09,1,0);right=g.remap(u,.90,.94,0,.65)
    edge=g.math('MAXIMUM',left,right)
    edge=g.math('MULTIPLY',edge,g.remap(v,.04,.30,.25,1))
    color=g.mix(g.math('MULTIPLY',edge,.64),color,colors[-1])
    # Two short angled strokes, restricted to four atlas faces, suggest internal fractures.
    crack_enabled=g.math('LESS_THAN',g.math('MODULO',tile,4),.5)
    diagonal=g.math('ADD',.35,g.math('MULTIPLY',g.math('SUBTRACT',v,.38),.52))
    diagonal=g.math('ADD',diagonal,g.math('MULTIPLY',g.math('SINE',g.math('MULTIPLY',v,30)),.008))
    distance=g.math('ABSOLUTE',g.math('SUBTRACT',u,diagonal))
    crack=g.remap(distance,.003,.011,1,0)
    crack=g.math('MULTIPLY',crack,g.math('MULTIPLY',g.remap(v,.27,.33),g.remap(v,.58,.66,1,0)))
    crack=g.math('MULTIPLY',crack,crack_enabled)
    color=g.mix(g.math('MULTIPLY',crack,.65),color,colors[-1])
    roughness=g.math('SUBTRACT',g.math('ADD',.36,g.math('MULTIPLY',noise.outputs['Fac'],.10)),g.math('MULTIPLY',edge,.085))
    glow=g.math('ADD',g.math('MULTIPLY',edge,.15),g.math('MULTIPLY',crack,.12))
    glow=g.math('ADD',glow,g.math('MULTIPLY',g.remap(v,.78,.98),.045))
    emission=g.mix(glow,(0,0,0,1),PALETTES[palette]['glow'])
    return g,{'basecolor':color,'roughness':roughness,'emission':emission}


def stone_graph():
    g=Graph('Arkya | painted host stone | authoring');coord=g.node('ShaderNodeTexCoord')
    if STONE_SOURCE.exists():
        image=bpy.data.images.load(str(STONE_SOURCE),check_existing=True);image.pack()
        tex=g.node('ShaderNodeTexImage');tex.image=image;g.connect(coord.outputs['UV'],tex.inputs['Vector'])
        color=g.mix(.25,tex.outputs['Color'],(.17,.18,.16,1))
    else:
        noise=g.node('ShaderNodeTexNoise');g.connect(coord.outputs['UV'],noise.inputs['Vector']);noise.inputs['Scale'].default_value=5;noise.inputs['Detail'].default_value=1.2
        color=g.ramp(noise.outputs['Fac'],[(.07,.085,.08,1),(.16,.18,.16,1),(.29,.30,.24,1),(.40,.39,.30,1)])
    return g,{'basecolor':color,'roughness':.83,'emission':(0,0,0,1)}


def bake_atlas(name,graph,channels,force=False):
    folder=TEXTURES/name;folder.mkdir(parents=True,exist_ok=True)
    if not force and all((folder/(name+'-'+key+'.png')).exists() for key in channels):return
    bpy.ops.mesh.primitive_plane_add(size=2,location=(0,0,-10));plane=bpy.context.object
    plane.name='Temporary atlas bake';plane.data.materials.append(graph.material)
    emitter=graph.node('ShaderNodeEmission');target=graph.node('ShaderNodeTexImage');graph.nodes.active=target
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=1
    scene.render.bake.margin=8;scene.render.bake.use_selected_to_active=False
    for key,value in channels.items():
        image=bpy.data.images.new(name+'-'+key,SIZE,SIZE,alpha=False)
        image.colorspace_settings.name='Non-Color' if key=='roughness' else 'sRGB'
        for link in list(emitter.inputs['Color'].links):graph.links.remove(link)
        graph.connect(value,emitter.inputs['Color']);graph.links.new(emitter.outputs[0],graph.out.inputs['Surface']);target.image=image
        bpy.ops.object.bake(type='EMIT')
        image.filepath_raw=str(folder/(name+'-'+key+'.png'));image.file_format='PNG';image.save();image.pack()
        print('ATLAS_READY',name,key,flush=True)
    bpy.data.objects.remove(plane,do_unlink=True)


def material(name):
    g=Graph('Arkya | '+name+' | painted PBR 2K')
    for key,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
        image=bpy.data.images.load(str(TEXTURES/name/(name+'-'+key+'.png')),check_existing=True)
        image.colorspace_settings.name='Non-Color' if key=='roughness' else 'sRGB';image.pack()
        texture=g.node('ShaderNodeTexImage');texture.image=image;g.connect(texture.outputs['Color'],g.p.inputs[socket])
    g.p.inputs['Emission Strength'].default_value=1
    g.p.inputs['Metallic'].default_value=.06 if name!='stone' else 0
    g.p.inputs['Specular IOR Level'].default_value=.30 if name!='stone' else .2
    g.p.inputs['Coat Weight'].default_value=.12 if name!='stone' else 0
    g.p.inputs['Coat Roughness'].default_value=.34
    return g.material


def apply_bevel(obj,width):
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    mod=obj.modifiers.new('Soft mineral edges','BEVEL');mod.width=width;mod.segments=2;mod.affect='EDGES'
    bpy.ops.object.modifier_apply(modifier=mod.name)
    triangulate=obj.modifiers.new('Portable triangulation','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=triangulate.name)
    normal=obj.modifiers.new('Weighted face normals','WEIGHTED_NORMAL');normal.keep_sharp=True;normal.weight=40
    for p in obj.data.polygons:p.use_smooth=True
    bpy.ops.object.modifier_apply(modifier=normal.name);obj.select_set(False)


def crystal(name,base,direction,length,radius,seed,mat,sides=6):
    rng=random.Random(seed);direction=Vector(direction).normalized();rotation=Vector((0,0,1)).rotation_difference(direction)
    angles=[math.tau*i/sides+rng.uniform(-.065,.065) for i in range(sides)]
    radii=[radius*rng.uniform(.82,1.12) for i in range(sides)]
    shoulder=[length*rng.uniform(.71,.83) for _ in range(sides)]
    vertices=[]
    for level in range(2):
        for i,a in enumerate(angles):
            r=radii[i]*(.91 if level==0 else 1.)
            p=Vector((math.cos(a)*r,math.sin(a)*r,0 if level==0 else shoulder[i]))
            vertices.append(tuple(rotation@p+Vector(base)))
    apex=Vector((radius*rng.uniform(-.14,.14),radius*rng.uniform(-.1,.1),length))
    vertices.append(tuple(rotation@apex+Vector(base)))
    faces=[];face_uv=[]
    for i in range(sides):
        j=(i+1)%sides;tile=(seed*3+i*5)%16;tx,ty=tile%4,tile//4
        def uv(u,v):return ((tx+.065+u*.87)/4,(ty+.065+v*.87)/4)
        faces.append((i,j,j+sides,i+sides));face_uv.append([uv(0,0),uv(1,0),uv(1,shoulder[j]/length),uv(0,shoulder[i]/length)])
        faces.append((i+sides,j+sides,2*sides));face_uv.append([uv(0,shoulder[i]/length),uv(1,shoulder[j]/length),uv(.50,1)])
    faces.append(tuple(reversed(range(sides))));face_uv.append([(.015,.015)]*sides)
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.materials.append(mat)
    layer=mesh.uv_layers.new(name='Painted_Face_UV')
    for polygon,coords in zip(mesh.polygons,face_uv):
        for loop,coord in zip(polygon.loop_indices,coords):layer.data[loop].uv=coord
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
    apply_bevel(obj,min(radius*.085,length*.012));return obj


def rock(name,location,scale,seed,mat):
    rng=random.Random(seed);bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2,radius=1,location=location);obj=bpy.context.object;obj.name=name
    for vertex in obj.data.vertices:
        x,y,z=vertex.co;wave=1+.085*math.sin(x*5+y*3+seed)+.055*math.cos(z*7-x*4)
        vertex.co=Vector((x*scale[0]*wave,y*scale[1]*wave,z*scale[2]*wave))
    obj.data.materials.append(mat)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
    apply_bevel(obj,min(scale)*.12);return obj


def geode_shell(mat):
    segs=18;rings=7;vertices=[]
    for side in (0,1):
        for ring in range(rings):
            theta=.04+(math.pi*.53-.04)*ring/(rings-1)
            for j in range(segs):
                angle=math.tau*j/segs;wave=1+.055*math.sin(angle*3+.8)+.04*math.sin(angle*7+ring)
                rx,ry,rz=((.83,.55,.89) if side==0 else (.68,.40,.74))
                x=math.sin(theta)*math.cos(angle)*rx*wave
                y=math.cos(theta)*ry-.015
                z=.79+math.sin(theta)*math.sin(angle)*rz*wave
                vertices.append((x,y,z))
    faces=[]
    for side in (0,1):
        offset=side*rings*segs
        for ring in range(rings-1):
            for j in range(segs):
                a=offset+ring*segs+j;b=offset+ring*segs+(j+1)%segs;c=b+segs;d=a+segs
                faces.append((a,b,c,d) if side==0 else (d,c,b,a))
    for j in range(segs):
        a=(rings-1)*segs+j;b=(rings-1)*segs+(j+1)%segs
        faces.append((b,a,a+rings*segs,b+rings*segs))
        faces.append((j,(j+1)%segs,(j+1)%segs+rings*segs,j+rings*segs))
    mesh=bpy.data.meshes.new('Weathered open geode crust');mesh.from_pydata(vertices,[],faces);mesh.materials.append(mat)
    obj=bpy.data.objects.new(mesh.name,mesh);bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(island_margin=.025);bpy.ops.object.mode_set(mode='OBJECT')
    apply_bevel(obj,.018);return obj


def shape_objects(shape,gem,stone):
    objects=[]
    # Position, axis, length, radius. Each configuration is authored separately.
    specs={
      'eclat':[((0,.02,.035),(-.05,.03,1),.53,.105),((-.11,-.02,.015),(-.38,-.12,1),.32,.075),((.105,.025,.02),(.43,.08,1),.24,.060)],
      'bouquet':[((-.03,.055,.07),(-.11,.08,1),1.03,.17),((-.22,.02,.04),(-.42,.02,1),.70,.14),((.20,.10,.045),(.38,.12,1),.79,.135),((.08,-.17,.04),(.12,-.42,1),.58,.13),((-.13,-.20,.035),(-.30,-.30,1),.39,.105),((.03,.24,.04),(.04,.35,1),.61,.11),((.29,-.12,.02),(.55,-.25,1),.36,.09)],
      'maitresse':[((-.24,.15,.10),(-.18,.06,1),2.92,.39),((.35,.12,.10),(.27,.13,1),2.04,.31),((-.59,.05,.06),(-.51,.03,1),1.38,.24),((.13,-.35,.08),(.08,-.27,1),1.50,.28),((-.34,-.39,.04),(-.23,-.43,1),.88,.20),((.64,-.21,.04),(.48,-.18,1),1.01,.20),((-.13,.54,.10),(-.05,.35,1),1.75,.25),((.65,.33,.06),(.54,.24,1),.68,.17),((-.67,-.20,.02),(-.62,-.30,1),.53,.14),((.19,-.63,.02),(.03,-.63,1),.61,.16)]}
    if shape=='geode':
        objects.append(geode_shell(stone));specs[shape]=[]
        for index,(angle,length,radius) in enumerate([(-2.65,.39,.095),(-2.1,.48,.11),(-1.55,.57,.13),(-.9,.52,.12),(-.35,.48,.105),(.30,.42,.10),(.75,.34,.085),(1.22,.39,.095),(1.85,.30,.085),(2.45,.37,.095)]):
            x=.64*math.cos(angle);z=.79+.67*math.sin(angle)
            axis=Vector((-x*.60,-.85,(.79-z)*.45+.30)).normalized()
            specs[shape].append(((x,.02,z),tuple(axis),length,radius))
        specs[shape]+=[((-.28,.15,.28),(-.15,-.50,1),.77,.14),((.25,.18,.35),(.15,-.65,1),.58,.13),((.02,.29,.78),(.05,-1,.18),.29,.09)]
    for i,(base,axis,length,radius) in enumerate(specs[shape]):objects.append(crystal('Crystal | %s | %02d'%(shape,i+1),base,axis,length,radius,37+i*17,gem,6 if i%3 else 5))
    if shape=='geode':stones=[((0,.13,.03),(.65,.43,.18)),((-.63,.03,.08),(.20,.27,.19)),((.60,.04,.05),(.22,.25,.16))]
    else:
        scale={'eclat':.25,'bouquet':.53,'maitresse':1.05}[shape]
        stones=[((0,.04,scale*.10),(scale*.77,scale*.55,scale*.20)),((-.51*scale,-.08,scale*.06),(scale*.36,scale*.32,scale*.15)),((.47*scale,.10,scale*.04),(scale*.34,scale*.30,scale*.14)),((.15,-scale*.43,scale*.03),(scale*.25,scale*.23,scale*.12))]
    for i,(location,scale) in enumerate(stones):objects.append(rock('Host stone | %02d'%(i+1),location,scale,109+i*31,stone))
    return objects


def bounds(objects):
    bpy.context.view_layer.update()
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    return Vector(tuple(min(p[i] for p in points) for i in range(3))),Vector(tuple(max(p[i] for p in points) for i in range(3)))


def setup_scene(objects,shape):
    low,high=bounds(objects);factor=SHAPES[shape]['height']/(high.z-low.z)
    offset=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
    root=bpy.data.objects.new('Resource_Root',None);bpy.context.scene.collection.objects.link(root)
    collection=bpy.data.collections.new('Arkya_Crystals');bpy.context.scene.collection.children.link(collection)
    for obj in objects:
        matrix=obj.matrix_world.copy()
        for v in obj.data.vertices:v.co=(matrix@v.co-offset)*factor
        obj.location=(0,0,0);obj.rotation_euler=(0,0,0);obj.scale=(1,1,1);obj.parent=root
        for old in list(obj.users_collection):old.objects.unlink(obj)
        collection.objects.link(obj)
    low,high=bounds(objects);return root,high-low


def preview(objects,dimensions,path,draft,gpu):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12 if draft else 32
    scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU'
    if gpu:
        preferences=bpy.context.preferences.addons['cycles'].preferences;preferences.compute_device_type='OPTIX';preferences.get_devices()
        for device in preferences.devices:device.use=device.type=='OPTIX'
    scene.render.resolution_x=700 if draft else 1000;scene.render.resolution_y=scene.render.resolution_x;scene.render.resolution_percentage=100
    if not scene.world:scene.world=bpy.data.worlds.new('Studio world')
    scene.world.color=(.25,.25,.25);scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
    h=dimensions.z;extent=max(dimensions.x,dimensions.y,h)
    bpy.ops.mesh.primitive_plane_add(size=extent*200,location=(0,0,-.004));plane=bpy.context.object;plane.name='Studio floor | not exported'
    mat=bpy.data.materials.new('Studio sage');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.20,.235,.215,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;plane.data.materials.append(mat)
    def area(name,location,energy,size):
        data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size
        obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj);obj.location=location;obj.rotation_euler=(Vector((0,0,h*.45))-obj.location).to_track_quat('-Z','Y').to_euler()
    area('Large warm key',(-extent*3,-extent*4,extent*5),650*extent**2,extent*4)
    area('Soft blue fill',(extent*4,-extent*1,extent*2.4),300*extent**2,extent*3)
    area('Edge light',(extent,extent*3,extent*4),480*extent**2,extent*3)
    data=bpy.data.cameras.new('Preview camera');camera=bpy.data.objects.new('Preview camera',data);scene.collection.objects.link(camera);scene.camera=camera
    camera.location=(extent*2.4,-extent*4,extent*2.4);target=Vector((0,0,h*.47));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=max(h,dimensions.x)*1.28
    scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def build(shape,palette,args):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objects=shape_objects(shape,material(palette),material('stone'));root,dimensions=setup_scene(objects,shape)
    stem=shape+'-'+palette;folder=ASSETS/stem;folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects+[root]:obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_lights=False,export_cameras=False,export_tangents=True,export_vertex_color='NAME',export_vertex_color_name='COLOR_0',export_all_vertex_colors=False)
    triangles=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in objects)
    assert triangles<20000,(stem,triangles)
    root['family']='cristaux';root['shape']=shape;root['palette']=palette;root['height_m']=float(dimensions.z)
    text=bpy.data.texts.new('Arkya provenance');text.write('Original procedural geometry and painted shader atlases created for Arkya. No external crystal model. Opaque metallic-roughness glTF; optional shared painted stone source. Generator: scripts/generate_crystals.py. Units: metres; Z-up in Blender, Y-up in GLB. Pivot: ground centre.')
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene['arkya_resource']='cristaux';scene['source_script']='scripts/generate_crystals.py'
    if args.render:preview(objects,dimensions,folder/(stem+'-preview.png'),args.draft,args.gpu)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],
        'glb':str((folder/(stem+'.glb')).relative_to(ROOT)).replace('\\','/'),'blend':str((folder/(stem+'.blend')).relative_to(ROOT)).replace('\\','/'),
        'preview':str((folder/(stem+'-preview.png')).relative_to(ROOT)).replace('\\','/') if (folder/(stem+'-preview.png')).exists() else None,
        'height':round(float(dimensions.z),4),'width':round(float(dimensions.x),4),'depth':round(float(dimensions.y),4),'triangles':triangles,
        'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol','bake':'Cycles CPU, emission channel transfer to basecolor/roughness/emission'}
    (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    manifest_path=ROOT/'manifest.json';old=json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {}
    records=[entry for entry in old.get('assets',[]) if entry['id']!=stem]+[record]
    manifest={'family':'cristaux','shapes':[dict(id=k,**v) for k,v in SHAPES.items()],
        'materials':[{'id':k,'label':v['label'],'description':v['description']} for k,v in PALETTES.items()],
        'assets':sorted(records,key=lambda entry:(list(SHAPES).index(entry['shape']),list(PALETTES).index(entry['material'])))}
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8');print('CRYSTAL_READY',json.dumps(record,ensure_ascii=False),flush=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--shape',choices=['all']+list(SHAPES),default='bouquet');parser.add_argument('--material',choices=['all']+list(PALETTES),default='azur')
    parser.add_argument('--render',action='store_true');parser.add_argument('--draft',action='store_true');parser.add_argument('--gpu',action='store_true');parser.add_argument('--rebake',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
    bpy.ops.wm.read_factory_settings(use_empty=True);TEXTURES.mkdir(parents=True,exist_ok=True)
    for palette in palettes:bake_atlas(palette,*crystal_graph(palette),force=args.rebake)
    bake_atlas('stone',*stone_graph(),force=args.rebake)
    for shape in shapes:
        for palette in palettes:build(shape,palette,args)


if __name__=='__main__':main()
