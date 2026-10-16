"""Arkya: authored curved solid grass ribbons and painted portable PBR atlases."""
import argparse
import json
import math
from pathlib import Path
import random
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
TEXTURES = ROOT / 'textures'
SHAPES = {
    'rase': {'label': 'Touffe rase', 'height': .22, 'description': 'Coussin étalé de brins courts, périphérie basse et centre souple.'},
    'eventail': {'label': 'Touffe éventail', 'height': .45, 'description': 'Gerbe ouverte, rubans arqués et pointes de hauteurs décalées.'},
    'courbee': {'label': 'Touffe courbée', 'height': .70, 'description': 'Brins souples regroupés en trois vagues inclinées vers un côté.'},
    'haute': {'label': 'Hautes herbes', 'height': 1.05, 'description': 'Gerbe élancée à plusieurs étages, cœur vertical et rubans extérieurs retombants.'},
}
PALETTES = {
    'prairie': {'label': 'Prairie verte', 'swatch': '#7b9c4e', 'description': 'Racines vert sauge, corps vert tendre et pointes citronnées.',
                'colors': [( .040, .085, .038), (.090, .230, .045), (.34, .46, .105)]},
    'doree': {'label': 'Herbe dorée', 'swatch': '#c3a761', 'description': 'Base terre d’ombre, corps ocre olive et pointes dorées.',
              'colors': [(.105, .069, .030), (.36, .260, .080), (.70, .56, .25)]},
    'mystique': {'label': 'Mystique', 'swatch': '#4ca89c', 'description': 'Racines prune, rubans bleu pétrole et pointes turquoise à lueur contenue.',
                  'colors': [(.075, .035, .080), (.027, .185, .170), (.22, .48, .35)]},
}
SIZE = 2048


def smoothstep(a, b, x):
    t = np.clip((x-a)/(b-a), 0, 1)
    return t*t*(3-2*t)


def write_image(path, pixels, color=True):
    image = bpy.data.images.new(path.stem, SIZE, SIZE, alpha=False)
    image.colorspace_settings.name = 'sRGB' if color else 'Non-Color'
    rgba = np.ones((SIZE, SIZE, 4), dtype=np.float32)
    values = pixels if pixels.ndim == 3 else pixels[:, :, None]
    if color:
        values = np.where(values <= .0031308, values*12.92, 1.055*np.power(np.maximum(values,0),1/2.4)-.055)
    rgba[:, :, :3] = values
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(path); image.file_format = 'PNG'; image.save(); image.pack()
    bpy.data.images.remove(image)


def make_textures(palette, force=False):
    folder = TEXTURES / palette; folder.mkdir(parents=True, exist_ok=True)
    if not force and all((folder / f'{palette}-{channel}.png').exists() for channel in ('basecolor', 'roughness', 'emission')):
        return
    y, x = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32)
    x /= SIZE; y /= SIZE
    tile_x = np.floor(x*4); tile_y = np.floor(y*4)
    tile = tile_x+tile_y*4
    u = ((x*4)%1-.055)/.89; v = ((y*4)%1-.055)/.89
    root, body, tip = [np.asarray(c, dtype=np.float32) for c in PALETTES[palette]['colors']]
    grow = smoothstep(.015, .38, v)
    color = root[None, None, :]*(1-grow[:, :, None])+body[None, None, :]*grow[:, :, None]
    bright = smoothstep(.38, 1.08, v)*.91
    color = color*(1-bright[:, :, None])+tip[None, None, :]*bright[:, :, None]
    # Broad glazes follow the blade, with narrow veins subordinate to the volume.
    glaze = .052*np.sin(u*13+np.sin(v*5+tile)*.70+tile*2.41)
    glaze += .028*np.sin(u*27+v*2.8+tile*3.12)
    glaze += .018*np.sin(v*19+u*7+tile*.73)
    glaze += .060*np.sin(tile*2.31)
    vein = np.exp(-((u-.505-.012*np.sin(v*9))/.020)**2)*.050
    edge = (np.exp(-(u/.06)**2)+np.exp(-((u-1)/.065)**2))*.025
    color *= (1+glaze+vein+edge)[:, :, None]
    # Four diffuse oblique strokes add painted variation without a repeated hard stripe.
    for index in range(4):
        position = .24+index*.17 + (u-.5)*.22
        stroke = np.exp(-((v-position)/.014)**2)*np.exp(-((u-.5)/.44)**2)
        color *= (1+.013*stroke)[:, :, None]
    rough = np.clip(.73 + .045*np.sin(u*9+v*3+tile) - vein*.15, .65, .84)
    emission = np.zeros((SIZE, SIZE, 3), dtype=np.float32)
    if palette == 'mystique':
        glow = smoothstep(.79, 1.03, v) * .017
        emission = glow[:, :, None]*np.asarray((.10, .55, .40), dtype=np.float32)[None, None, :]
    write_image(folder/f'{palette}-basecolor.png', np.clip(color, 0, 1))
    write_image(folder/f'{palette}-roughness.png', rough, False)
    write_image(folder/f'{palette}-emission.png', emission)
    print('TEXTURES_READY', palette, flush=True)


def material(palette):
    mat = bpy.data.materials.new('Arkya | '+PALETTES[palette]['label']+' | painted 2K')
    mat.use_nodes = True; p = mat.node_tree.nodes.get('Principled BSDF')
    p.inputs['Metallic'].default_value = 0
    p.inputs['Roughness'].default_value = .74
    p.inputs['Specular IOR Level'].default_value = .22
    p.inputs['Emission Strength'].default_value = 1
    for channel, socket in [('basecolor', 'Base Color'), ('roughness', 'Roughness'), ('emission', 'Emission Color')]:
        image = bpy.data.images.load(str(TEXTURES/palette/f'{palette}-{channel}.png'), check_existing=True)
        image.colorspace_settings.name = 'Non-Color' if channel == 'roughness' else 'sRGB'; image.pack()
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image = image
        mat.node_tree.links.new(tex.outputs['Color'], p.inputs[socket])
    return mat


def blade_geometry(vertices, faces, face_uvs, base, phi, height, width, reach, droop, twist, tile, seed):
    rng = random.Random(seed)
    rings = 16
    outward = Vector((math.cos(phi), math.sin(phi), 0))
    across = Vector((-math.sin(phi), math.cos(phi), 0))
    origin = Vector(base)
    start = len(vertices)
    section = [(-.5, 0), (-.30, .065), (0, .13), (.30, .065), (.5, 0), (.30, -.025), (0, -.035), (-.30, -.025)]
    tex_u = [0, .20, .5, .80, 1, .80, .5, .20]
    tx, ty = tile%4, tile//4
    def uv(u, v): return ((tx+.055+.89*u)/4, (ty+.055+.89*v)/4)
    skew = rng.uniform(-.13, .13)
    def centre(t):
        return origin + outward*(reach*t*t) + across*(width*skew*math.sin(t*math.pi)) + Vector((0, 0, height*(t-droop*t**5)))
    for ring in range(rings):
        t = ring/(rings+.1)
        w = width*(.43+.72*math.sin(min(t/.55, 1)*math.pi/2))*(1-t)**.65
        if t>.68: w *= 1-.42*((t-.68)/.32)
        c = centre(t)
        tangent = (centre(min(1,t+.001))-centre(max(0,t-.001))).normalized()
        a = across*math.cos(t*twist) + outward*math.sin(t*twist)
        a = (a-tangent*a.dot(tangent)).normalized()
        normal = a.cross(tangent).normalized()
        for side, fold in section:
            point = c+a*(side*w)+normal*(fold*w)
            vertices.append(tuple(point))
    tip = len(vertices); vertices.append(tuple(centre(1)))
    for ring in range(rings-1):
        for side in range(8):
            next_side = (side+1)%8
            faces.append((start+ring*8+side, start+ring*8+next_side, start+(ring+1)*8+next_side, start+(ring+1)*8+side))
            face_uvs.append([uv(tex_u[side],ring/(rings+.1)),uv(tex_u[next_side],ring/(rings+.1)),uv(tex_u[next_side],(ring+1)/(rings+.1)),uv(tex_u[side],(ring+1)/(rings+.1))])
    for side in range(8):
        next_side = (side+1)%8
        faces.append((start+(rings-1)*8+side, start+(rings-1)*8+next_side, tip))
        face_uvs.append([uv(tex_u[side],(rings-1)/(rings+.1)),uv(tex_u[next_side],(rings-1)/(rings+.1)),uv(.5,1)])
    faces.append(tuple(start+i for i in reversed(range(8))));face_uvs.append([uv(tex_u[i],0) for i in reversed(range(8))])


def grass(shape, mat):
    seed = 174+[ *SHAPES].index(shape)*113
    rng = random.Random(seed)
    vertices=[]; faces=[]; face_uvs=[]
    groups = []
    if shape == 'rase':
        groups = [(-.16,-.05,.72,10),(.10,-.06,.85,12),(-.015,.08,1.0,14),(.20,.075,.60,8)]
    elif shape == 'eventail':
        groups = [(-.085,-.015,.83,9),(.05,.02,1.,13),(.14,.055,.65,8)]
    elif shape == 'courbee':
        groups = [(-.17,.07,.85,11),(-.03,-.02,1.,13),(.14,.05,.72,12)]
    else:
        groups = [(-.13,.065,.80,11),(.0,.06,1.,15),(.15,-.035,.68,12),(-.045,-.1,.62,10)]
    index=0
    for group_index,(cx,cy,scale,count) in enumerate(groups):
        for n in range(count):
            phi = (n*2.399963+group_index*.70+rng.uniform(-.25,.25))%math.tau
            outer = n/count
            root_radius = .022+.028*outer
            root = (cx+math.cos(phi)*root_radius,cy+math.sin(phi)*root_radius,0)
            if shape == 'rase':
                height = .24*scale*rng.uniform(.65,1.05)
                width = rng.uniform(.025,.039)*scale
                reach = rng.uniform(.10,.19)*(1.1-scale*.25)
                droop = rng.uniform(.20,.34)
            elif shape == 'eventail':
                height = .49*scale*rng.uniform(.65,1.05)
                width = rng.uniform(.030,.048)*scale
                reach = rng.uniform(.12,.27)*(1.1-scale*.20)
                droop = rng.uniform(.11,.24)
                # A gently flattened fan makes a readable silhouette without being a plane.
                phi = math.atan2(math.sin(phi)*.58,math.cos(phi))
                if group_index==1 and n in (0,5,9):
                    reach*=1.12;droop=.31
            elif shape == 'courbee':
                height = .76*scale*rng.uniform(.70,1.05)
                width = rng.uniform(.034,.052)*scale
                phi = .23+math.sin(phi)*.72
                reach = rng.uniform(.28,.47)
                droop = rng.uniform(.13,.26)
            else:
                height = 1.10*scale*rng.uniform(.65,1.05)
                width = rng.uniform(.035,.059)*scale
                reach = rng.uniform(.12,.33)*(1.25-scale*.50)
                droop = rng.uniform(.065,.18)
            blade_geometry(vertices,faces,face_uvs,root,phi,height,width,reach,droop,rng.uniform(-.50,.50),index%16,seed+index*7)
            index+=1
    # Short emerging blades bind the roots into a tuft without adding a ground mound.
    extra={'rase':0,'eventail':12,'courbee':8,'haute':8}[shape]
    for n in range(extra):
        phi=n*2.399963+.38
        root=(rng.uniform(-.065,.13),rng.uniform(-.055,.065),0)
        height=SHAPES[shape]['height']*rng.uniform(.24,.46)
        width=rng.uniform(.027,.043)
        reach=height*rng.uniform(.35,.65)
        if shape=='courbee':phi=.23+math.sin(phi)*.80
        blade_geometry(vertices,faces,face_uvs,root,phi,height,width,reach,.24,rng.uniform(-.4,.4),index%16,seed+index*7)
        index+=1
    mesh=bpy.data.meshes.new('Sculpted curved blades');mesh.from_pydata(vertices,[],faces);mesh.materials.append(mat)
    layer=mesh.uv_layers.new(name='Painted_Blade_UV')
    for polygon,coords in zip(mesh.polygons,face_uvs):
        for loop,coord in zip(polygon.loop_indices,coords):layer.data[loop].uv=coord
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bmesh.ops.triangulate(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    for p in mesh.polygons:p.use_smooth=True
    low=Vector(tuple(min(v.co[i] for v in mesh.vertices) for i in range(3)))
    high=Vector(tuple(max(v.co[i] for v in mesh.vertices) for i in range(3)))
    factor=SHAPES[shape]['height']/(high.z-low.z)
    offset=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
    for v in mesh.vertices:v.co=(v.co-offset)*factor
    obj=bpy.data.objects.new(SHAPES[shape]['label'],mesh)
    collection=bpy.data.collections.new('Arkya_Grass');bpy.context.scene.collection.children.link(collection);collection.objects.link(obj)
    obj['blade_count']=index;obj['root_pivot']='XY centre, Z floor';obj['family']='herbes'
    dims=(high-low)*factor
    return obj,dims


def studio(obj,dims,path,draft=False,gpu=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12 if draft else 40
    scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU';scene.render.threads_mode='FIXED';scene.render.threads=8
    if gpu:
        preferences=bpy.context.preferences.addons['cycles'].preferences;preferences.compute_device_type='OPTIX';preferences.get_devices()
        for device in preferences.devices:device.use=device.type=='OPTIX'
    scene.render.resolution_x=750 if draft else 1100;scene.render.resolution_y=scene.render.resolution_x;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Neutral studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    extent=max(dims);h=dims.z
    bpy.ops.mesh.primitive_plane_add(size=extent*200,location=(0,0,-.002));floor=bpy.context.object;floor.name='Studio floor | not exported'
    mat=bpy.data.materials.new('Studio sage');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.23,.265,.235,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
    for name,pos,energy,size in [('Broad key',(-3,-4,5),650,4),('Fill',(4,-1,2.4),300,3),('Soft rim',(1,3,4),480,3)]:
        light=bpy.data.lights.new(name,'AREA');light.energy=energy*extent**2;light.shape='DISK';light.size=size*extent
        o=bpy.data.objects.new(name,light);scene.collection.objects.link(o);o.location=Vector(pos)*extent;o.rotation_euler=(Vector((0,0,h*.40))-o.location).to_track_quat('-Z','Y').to_euler()
    camera_data=bpy.data.cameras.new('Preview camera');camera=bpy.data.objects.new('Preview camera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
    camera.location=(extent*1.65,-extent*4,extent*2.0);target=Vector((0,0,h*.43));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.type='ORTHO';camera_data.ortho_scale=max(h,dims.x,dims.y)*1.24
    scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def build(shape,palette,args):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
    mat=material(palette);obj,dimensions=grass(shape,mat)
    stem=shape+'-'+palette;folder=ROOT/'assets'/stem;folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_lights=False,export_cameras=False,export_tangents=True)
    triangles=sum(len(p.vertices)-2 for p in obj.data.polygons)
    assert triangles<15000,(stem,triangles)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene['arkya_resource']='herbes';scene['source_script']='scripts/generate_grass.py'
    text=bpy.data.texts.new('Arkya provenance');text.write('Original parametric closed curved ribbons and painted atlases authored for Arkya. No external models, no alpha cards. Editable mesh with UVs; soft geometric midrib. Generator: scripts/generate_grass.py. Metres; ground-centred pivot. Procedural 2K albedo/roughness/emission source images packed.')
    if args.render:studio(obj,dimensions,folder/(stem+'-preview.png'),args.draft,args.gpu)
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],'glb':(folder/(stem+'.glb')).relative_to(ROOT).as_posix(),'blend':(folder/(stem+'.blend')).relative_to(ROOT).as_posix(),'preview':(folder/(stem+'-preview.png')).relative_to(ROOT).as_posix() if (folder/(stem+'-preview.png')).exists() else None,'height':round(float(dimensions.z),4),'width':round(float(dimensions.x),4),'depth':round(float(dimensions.y),4),'triangles':triangles,'blades':obj['blade_count'],'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol'}
    (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    manifest_path=ROOT/'manifest.json';old=json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {}
    records=[a for a in old.get('assets',[]) if a['id']!=stem]+[record]
    manifest={'family':'herbes','shapes':[dict(id=k,**v) for k,v in SHAPES.items()],'materials':[{'id':k,'label':v['label'],'swatch':v['swatch'],'description':v['description']} for k,v in PALETTES.items()],'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material'])))}
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
    print('GRASS_READY',json.dumps(record,ensure_ascii=False),flush=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--shape',choices=['all']+list(SHAPES),default='eventail');parser.add_argument('--material',choices=['all']+list(PALETTES),default='prairie')
    parser.add_argument('--render',action='store_true');parser.add_argument('--draft',action='store_true');parser.add_argument('--gpu',action='store_true');parser.add_argument('--rebake',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    bpy.ops.wm.read_factory_settings(use_empty=True)
    palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
    for palette in palettes:make_textures(palette,args.rebake)
    for shape in shapes:
        for palette in palettes:build(shape,palette,args)


if __name__=='__main__':main()
