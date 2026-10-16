"""Original summoned gathering tools; painted atlases and editable, portable meshes."""
import argparse
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

ROOT = Path(__file__).resolve().parents[1]
TEXTURES = ROOT / 'textures' / 'azur'
ASSETS = ROOT / 'assets'
SIZE = 2048
FPS = 24
DURATION = 4
CLIP = 'SummonedToolIdle'
TOOL_SCALE = .75
SHAPES = {
    'hache': {'label': 'Hache invoquée', 'description': 'Large lame en croissant, manche courbe et cœur de mana.'},
    'pioche': {'label': 'Pioche invoquée', 'description': 'Bec effilé et panne courte, manche gainé et éclats de mana suspendus.'},
    'faucille': {'label': 'Faucille invoquée', 'description': 'Croissant à tranchant intérieur et pointe recourbée, prise courte gainée et mana suspendue.'},
}
PALETTES = {
    'azur': {'label':'Mana azur','swatch':'#59b8c9'},
    'orange': {'label':'Mana orange','swatch':'#ee9b29',
               'dark':(.43,.11,.018),'mid':(.93,.39,.045),'iris':(1.0,.70,.13),'core':(1.0,.75,.31)},
    'vert': {'label':'Mana vert','swatch':'#3ac17c',
             'dark':(.025,.29,.14),'mid':(.08,.59,.30),'iris':(.16,.80,.38),'core':(.37,1.0,.61)},
}
SOCKET_CENTRES = {
    'hache':[(.245,.755),(.395,.785),(.525,.715)],
    'pioche':[(-.36,.765),(-.185,.813),(.245,.762)],
    'faucille':[(.1886,.357),(.3174,.3995),(.437,.4675)],
}
SOCKET_INNER_RADIUS = .031
SOCKET_FLOOR_DEPTH = .006
SOCKET_LIP_DEPTH = .039


def smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def ramp(value, colors):
    value = np.clip(value, 0, 1) * (len(colors) - 1)
    index = np.minimum(value.astype(np.int32), len(colors) - 2)
    factor = value - index
    palette = np.asarray(colors, dtype=np.float32)
    return palette[index] * (1 - factor[..., None]) + palette[index + 1] * factor[..., None]


def field(seed, grid_x, grid_y, u, v):
    rng = np.random.default_rng(seed)
    grid = rng.uniform(0, 1, (grid_y + 1, grid_x + 1)).astype(np.float32)
    x = (u % 1) * grid_x
    y = np.clip(v, 0, .99999) * grid_y
    ix = x.astype(np.int32)
    iy = y.astype(np.int32)
    fx = x - ix
    fy = y - iy
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)
    return ((grid[iy, ix] * (1 - fx) + grid[iy, ix + 1] * fx) * (1 - fy)
            + (grid[iy + 1, ix] * (1 - fx) + grid[iy + 1, ix + 1] * fx) * fy)


def save_png(name, rgb, noncolor=False, folder=None):
    rgb = np.clip(rgb, 0, 1)
    if rgb.ndim == 2:
        rgb = np.repeat(rgb[..., None], 3, axis=2)
    rgba = np.concatenate((rgb, np.ones((SIZE, SIZE, 1), dtype=np.float32)), axis=2).astype(np.float32)
    image = bpy.data.images.new(name, width=SIZE, height=SIZE, alpha=False)
    image.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str((folder or TEXTURES) / (name + '.png'))
    image.file_format = 'PNG'
    image.save()
    bpy.data.images.remove(image)


def make_textures(force=False):
    TEXTURES.mkdir(parents=True, exist_ok=True)
    names = ['steel', 'bronze', 'wood', 'leather', 'mana']
    if all((TEXTURES / (n + '-basecolor.png')).exists() and (TEXTURES / (n + '-roughness.png')).exists() for n in names):
        return
    v, u = np.mgrid[0:1:complex(SIZE), 0:1:complex(SIZE)].astype(np.float32)
    broad = field(21, 9, 12, u + v * .13, v)
    strokes = field(83, 48, 8, u + .22 * v, v)
    fine = field(104, 175, 25, u + .18 * v, v)
    value = .27 + .21 * v + .16 * broad + .035 * strokes + .012 * fine
    rng = random.Random(515)
    for _ in range(25):
        cx, cy = rng.uniform(.04, .81), rng.uniform(.1, .90)
        width, height = rng.uniform(.018, .065), rng.uniform(.08, .23)
        transverse = u - cx - (v-cy)*rng.uniform(.1,.35) + (fine-.5)*.008
        longitudinal = v - cy
        mask = smooth(-width,-width+.009,transverse)*(1-smooth(width-.009,width,transverse))
        mask *= smooth(-height,-height+.016,longitudinal)*(1-smooth(height-.016,height,longitudinal))
        value += mask*rng.uniform(-.075,.09)
    steel = ramp(value, [(.045, .073, .102), (.105, .16, .20), (.21, .29, .32), (.39, .46, .46), (.60, .66, .63)])
    # Deliberate broad, slightly broken brush planes; the right atlas strip paints the bevel.
    plane = smooth(.035, .055, np.sin((u + v * .24) * 16)) * smooth(.12, .20, v) * (1 - smooth(.66, .91, v))
    steel *= (1 + plane[..., None] * .045)
    edge = smooth(.85, .87, u)
    bright = ramp(.40 + .26 * v + .15 * strokes, [(.20, .29, .33), (.46, .58, .59), (.73, .78, .72)])
    steel = steel * (1 - edge[..., None]) + bright * edge[..., None]
    save_png('steel-basecolor', steel)
    save_png('steel-roughness', .43 + broad * .14 + strokes * .045, True)

    bronze = ramp(.18 + .52 * field(12, 9, 13, u, v) + .14 * strokes + .13 * v,
                  [(.050, .036, .022), (.16, .105, .050), (.30, .215, .11), (.48, .365, .19), (.64, .49, .28)])
    save_png('bronze-basecolor', bronze)
    save_png('bronze-roughness', .52 + .14 * broad, True)

    # Long painted fibres follow the handle rather than wrapping a bark photograph over it.
    grain_u = u + .022 * np.sin(v * 8 + u * 6) + .011 * np.sin(v * 19 + u * 13)
    grain = field(304, 32, 4, grain_u, v)
    broad_wood = field(305, 8, 7, grain_u, v)
    line = smooth(.72, .90, field(306, 79, 8, grain_u, v))
    wood = ramp(.16 + .42 * broad_wood + .23 * grain - .10 * line + .10 * v,
                [(.025, .016, .013), (.065, .035, .022), (.135, .076, .042), (.24, .143, .074), (.39, .255, .13)])
    save_png('wood-basecolor', wood)
    save_png('wood-roughness', .74 + .15 * grain, True)

    leather_noise = field(401, 12, 20, u, v)
    leather = ramp(.30 + .36 * leather_noise + .18 * strokes,
                   [(.023, .018, .023), (.063, .043, .044), (.13, .082, .069), (.23, .152, .105), (.33, .235, .16)])
    seam = (1 - smooth(.025, .075, np.minimum(u, 1 - u))) * .13
    leather = leather * (1 + seam[..., None])
    save_png('leather-basecolor', leather)
    save_png('leather-roughness', .76 + .12 * leather_noise, True)

    mana_value = .25 + .54 * v + .10 * broad + .06 * strokes
    mana = ramp(mana_value, [(.005, .024, .06), (.008, .095, .17), (.021, .25, .34), (.08, .46, .53), (.42, .73, .72)])
    streak = smooth(.81, .95, u) + (1 - smooth(.04, .13, u)) * .6
    mana = mana * (1 - streak[..., None] * .25) + np.array([.29, .63, .68]) * streak[..., None] * .25
    save_png('mana-basecolor', mana)
    save_png('mana-roughness', .30 + .12 * broad, True)
    print('TOOL_TEXTURES_READY', flush=True)


def make_palette_textures(palette, force=False):
    if palette=='azur':return
    folder=ROOT/'textures'/palette
    folder.mkdir(parents=True,exist_ok=True)
    if not force and all((folder/('mana-'+c+'.png')).exists() for c in ('basecolor','roughness')):return
    v,u=np.mgrid[0:1:complex(SIZE),0:1:complex(SIZE)].astype(np.float32)
    broad=field(21,9,12,u+v*.13,v)
    strokes=field(83,48,8,u+.22*v,v)
    value=.25+.54*v+.10*broad+.06*strokes
    p=PALETTES[palette]
    dark,mid,iris,light=[np.asarray(p[k],dtype=np.float32) for k in ('dark','mid','iris','core')]
    colors=[dark*.12,dark*.45+mid*.15,mid*.55,mid*.85+iris*.15,light*.80]
    color=ramp(value,colors)
    streak=smooth(.81,.95,u)+(1-smooth(.04,.13,u))*.6
    color=color*(1-streak[...,None]*.25)+light*.78*streak[...,None]*.25
    save_png('mana-basecolor',color,folder=folder)
    save_png('mana-roughness',.30+.12*broad,True,folder=folder)
    print('MANA_PALETTE_READY',palette,flush=True)


def material(name,palette='azur'):
    mat = bpy.data.materials.new('Arkya | ' + name + ' | painted 2K')
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    for channel, socket in [('basecolor', 'Base Color'), ('roughness', 'Roughness')]:
        folder=ROOT/'textures'/palette if name=='mana' else TEXTURES
        image = bpy.data.images.load(str(folder / (name + '-' + channel + '.png')), check_existing=True)
        image.colorspace_settings.name = 'Non-Color' if channel == 'roughness' else 'sRGB'
        image.pack()
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = image
        mat.node_tree.links.new(tex.outputs['Color'], node.inputs[socket])
        if name == 'mana' and channel == 'basecolor':
            mat.node_tree.links.new(tex.outputs['Color'], node.inputs['Emission Color'])
    node.inputs['Metallic'].default_value = {'steel': .52, 'bronze': .55, 'mana': .08}.get(name, 0)
    node.inputs['Specular IOR Level'].default_value = .30
    if name == 'mana':
        node.inputs['Emission Strength'].default_value = .95
        node.inputs['Coat Weight'].default_value = .12
        node.inputs['Coat Roughness'].default_value = .34
    return mat


def mesh_object(name, vertices, faces, mat, uvs=None, smooth_faces=False):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.materials.append(mat)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    layer = mesh.uv_layers.new(name='Painted_UV')
    for face_index, polygon in enumerate(mesh.polygons):
        polygon.use_smooth = smooth_faces
        for corner, loop_index in enumerate(polygon.loop_indices):
            if uvs is not None:
                layer.data[loop_index].uv = uvs[face_index][corner]
            else:
                p = vertices[mesh.loops[loop_index].vertex_index]
                layer.data[loop_index].uv = ((p[0] + .8) / 1.6 * .75 + .05, (p[2] + .65) / 1.9)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return obj


def finish(obj, bevel=0, segments=2, weighted=True):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if bevel:
        modifier = obj.modifiers.new('Shaped soft edge', 'BEVEL')
        modifier.width = bevel
        modifier.segments = segments
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    modifier = obj.modifiers.new('Portable triangles', 'TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    if weighted:
        for polygon in obj.data.polygons:
            polygon.use_smooth = True
        modifier = obj.modifiers.new('Weighted broad faces', 'WEIGHTED_NORMAL')
        modifier.keep_sharp = True
        modifier.weight = 45
        bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)
    return obj


def catmull_closed(points, samples=3, sharp=()):
    pts = [Vector(p) for p in points]
    out = []
    for index in range(len(pts)):
        p0, p1, p2, p3 = [pts[(index + offset) % len(pts)] for offset in (-1, 0, 1, 2)]
        for j in range(samples):
            t = j / samples
            tangent1 = Vector((0,0)) if index in sharp else (p2-p0)*.5
            tangent2 = Vector((0,0)) if (index+1)%len(pts) in sharp else (p3-p1)*.5
            out.append((2*t*t*t-3*t*t+1)*p1+(t*t*t-2*t*t+t)*tangent1+(-2*t*t*t+3*t*t)*p2+(t*t*t-t*t)*tangent2)
    return out


def shaped_head(shape, mat):
    if shape == 'faucille':
        return sickle_head(mat)
    if shape == 'hache':
        controls = [(-.10,.82),(.11,.82),(.28,.88),(.44,.99),(.56,1.025),
                    (.62,.90),(.68,.74),(.695,.57),(.66,.43),(.57,.31),(.475,.275),
                    (.43,.43),(.31,.52),(.16,.55),(-.10,.59)]
        center = Vector((.28,.68))
        width = .14
    else:
        controls = [(-.765,.515),(-.66,.665),(-.48,.80),(-.25,.88),(-.045,.90),
                    (.17,.86),(.36,.78),(.47,.675),(.43,.605),(.28,.675),
                    (.10,.72),(-.085,.735),(-.28,.70),(-.49,.615),(-.68,.535)]
        center = Vector((-.08,.755))
        width = .14
    outline = catmull_closed(controls, 4, sharp=(0,) if shape=='pioche' else ())
    count = len(outline)
    vertices = []
    # Ring profile makes broad planar cheeks, a round shoulder and a lit cutting bevel.
    levels = [(-width*.50,.91),(-width*.48,.96),(-width*.28,1.0),
              (width*.28,1.0),(width*.48,.96),(width*.50,.91)]
    for level, (y, scale) in enumerate(levels):
        for p in outline:
            q = center + (p-center)*scale
            taper = 1 if shape == 'hache' else .12 + .88 * max(0,1-abs(q.x)/.79)
            shaped_y = y*taper
            if shape=='hache' and level in (2,3):
                cut=smooth_scalar(.35,.58,q.x)
                shaped_y=y*(1-cut)+math.copysign(.003,y)*cut
            vertices.append((q.x, shaped_y, q.y))
    faces = []
    face_uv = []
    minx, maxx = min(p.x for p in outline), max(p.x for p in outline)
    minz, maxz = min(p.y for p in outline), max(p.y for p in outline)
    def uv(p):
        return (.045 + .74*(p[0]-minx)/(maxx-minx), .08 + .84*(p[2]-minz)/(maxz-minz))
    faces.append(tuple(reversed(range(count))))
    face_uv.append([uv(vertices[i]) for i in reversed(range(count))])
    for level in range(len(levels)-1):
        for i in range(count):
            j=(i+1)%count
            faces.append((level*count+i,level*count+j,(level+1)*count+j,(level+1)*count+i))
            face_uv.append([(.89+.07*level/(len(levels)-1),.08+.84*i/count),(.89+.07*level/(len(levels)-1),.08+.84*j/count),(.89+.07*(level+1)/(len(levels)-1),.08+.84*j/count),(.89+.07*(level+1)/(len(levels)-1),.08+.84*i/count)])
    faces.append(tuple((len(levels)-1)*count+i for i in range(count)))
    face_uv.append([uv(vertices[i]) for i in faces[-1]])
    obj = mesh_object('Forged crescent | painted cheeks and cutting bevel' if shape=='hache' else 'Forged pick | arched point and short adze', vertices, faces, mat, face_uv)
    return finish(obj, weighted=True)


def sickle_rail():
    curves=[[(0,.39),(.24,.39),(.56,.51),(.59,.77)],
            [(.59,.77),(.64,1.02),(.40,1.20),(.15,1.12)],
            [(.15,1.12),(-.03,1.07),(-.14,.95),(-.17,.82)]]
    rail=[]
    for segment,controls in enumerate(curves):
        p0,p1,p2,p3=[Vector(p) for p in controls]
        for step in range(25 if segment==2 else 24):
            t=step/24
            p=(1-t)**3*p0+3*(1-t)**2*t*p1+3*(1-t)*t*t*p2+t**3*p3
            tangent=3*(1-t)**2*(p1-p0)+6*(1-t)*t*(p2-p1)+3*t*t*(p3-p2)
            p=Vector((p.x*.92,p.y*.85))
            tangent=Vector((tangent.x*.92,tangent.y*.85)).normalized()
            progress=(segment+t)/3
            width=.082+.016*math.sin(progress*math.pi)
            width*=1-.985*smooth_scalar(.57,1,progress)
            rail.append((p,tangent,width,progress))
    return rail


def sickle_head(mat):
    # Cross-sections follow the open arc, so no polygon can bridge its hollow centre.
    rail=sickle_rail()
    profile=[(-1,0),(-.86,-.80),(-.70,-1),(.66,-1),(.86,-.58),(1,0),
             (.86,.58),(.66,1),(-.70,1),(-.86,.80)]
    count=len(profile)
    vertices=[]
    for p,tangent,width,progress in rail:
        normal=Vector((-tangent.y,tangent.x))
        thickness=.060*(1-.97*smooth_scalar(.60,1,progress))
        for across,depth in profile:
            v=p+normal*(across*width)
            vertices.append((v.x,depth*thickness,v.y))
    faces=[];uvs=[]
    for j in range(len(rail)-1):
        for k in range(count):
            next_k=(k+1)%count
            face=(j*count+k,j*count+next_k,(j+1)*count+next_k,(j+1)*count+k)
            faces.append(face)
            if k in (3,4,5,6):
                uvs.append([(.89,.08+.84*j/(len(rail)-1)),(.97,.08+.84*j/(len(rail)-1)),
                            (.97,.08+.84*(j+1)/(len(rail)-1)),(.89,.08+.84*(j+1)/(len(rail)-1))])
            else:
                uvs.append([(.08+.70*(vertices[v][0]+.25)/1.0,.08+.84*(vertices[v][2]-.23)/.85) for v in face])
    faces.extend([tuple(reversed(range(count))),tuple((len(rail)-1)*count+k for k in range(count))])
    uvs.extend([[(.2,.2)]*count,[(.9,.9)]*count])
    return finish(mesh_object('Forged sickle | swept crescent and inner cutting edge',vertices,faces,mat,uvs),weighted=True)


def handle_center(z):
    return Vector((.025*math.sin((z+.62)*4.3)-.016*z, .007*math.sin(z*3), z))


def handle_radius(z):
    return 1.10*(.034 + .009 * smooth_scalar(-.25,.73,z) + .008*math.exp(-((z+.57)/.065)**2))


def smooth_scalar(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)))
    return t*t*(3-2*t)


def sleeve(name, bottom, top, mat, radius_add=0, segments=20, rings=26, shape_power=1, lip=0):
    vertices=[]
    for j in range(rings):
        t=j/(rings-1)
        z=bottom+(top-bottom)*t
        centre=handle_center(z)
        radius=handle_radius(z)+radius_add+lip*(abs(2*t-1)**8)
        for k in range(segments):
            a=math.tau*k/segments
            x=math.copysign(abs(math.cos(a))**shape_power,math.cos(a))
            y=math.copysign(abs(math.sin(a))**shape_power,math.sin(a))
            variation=1+.018*math.sin(a*3+z*5)
            vertices.append(tuple(centre+Vector((radius*x*variation,radius*y*.84*variation,0))))
    faces=[];uvs=[]
    for j in range(rings-1):
        for k in range(segments):
            faces.append((j*segments+k,j*segments+(k+1)%segments,(j+1)*segments+(k+1)%segments,(j+1)*segments+k))
            uvs.append([(k/segments,j/(rings-1)),((k+1)/segments,j/(rings-1)),((k+1)/segments,(j+1)/(rings-1)),(k/segments,(j+1)/(rings-1))])
    faces.extend([tuple(reversed(range(segments))),tuple((rings-1)*segments+k for k in range(segments))])
    uvs.extend([[(.5+.4*math.cos(math.tau*k/segments),.5+.4*math.sin(math.tau*k/segments)) for k in reversed(range(segments))],[(.5+.4*math.cos(math.tau*k/segments),.5+.4*math.sin(math.tau*k/segments)) for k in range(segments)]])
    obj=mesh_object(name,vertices,faces,mat,uvs,smooth_faces=True)
    return finish(obj,weighted=False)


def tube(name, points, radius, mat, sides=8, caps=True):
    points=[Vector(p) for p in points]
    vertices=[]
    for j,p in enumerate(points):
        direction=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
        side=direction.cross(Vector((0,1,0)))
        if side.length<.01: side=direction.cross(Vector((1,0,0)))
        side.normalize();other=direction.cross(side).normalized()
        for k in range(sides):
            a=math.tau*k/sides
            vertices.append(tuple(p+(side*math.cos(a)+other*math.sin(a))*radius))
    faces=[];uvs=[]
    for j in range(len(points)-1):
        for k in range(sides):
            faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
            uvs.append([(k/sides,j/(len(points)-1)),((k+1)/sides,j/(len(points)-1)),((k+1)/sides,(j+1)/(len(points)-1)),(k/sides,(j+1)/(len(points)-1))])
    if caps:
        faces.extend([tuple(reversed(range(sides))),tuple((len(points)-1)*sides+k for k in range(sides))])
        uvs.extend([[(.5,.15)]*sides,[(.5,.85)]*sides])
    return finish(mesh_object(name,vertices,faces,mat,uvs,smooth_faces=True),weighted=False)


def grip_wrap(mat,bottom=-.36,top=.25,turns=7.6):
    points=[]
    for i in range(185):
        t=i/184
        z=bottom+(top-bottom)*t
        a=t*turns*math.tau+.4
        c=handle_center(z)
        radius=handle_radius(z)+.005
        points.append(tuple(c+Vector((radius*math.cos(a),radius*.85*math.sin(a),0))))
    return tube('Leather | softly raised hand-wound seam',points,.0033,mat,sides=6)


def bezel(name,x,z,y,side,mat):
    outline=catmull_closed([(-.015,-.119),(.071,-.045),(.076,.030),(.015,.119),(-.071,.045),(-.076,-.030)],3)
    count=len(outline)
    vertices=[]
    for depth,scale in [(0,1),(.015,.93),(.021,.80)]:
        for p in outline: vertices.append((x+p.x*scale,y+side*depth,z+p.y*scale))
    faces=[]
    for j in range(2):
        for k in range(count): faces.append((j*count+k,j*count+(k+1)%count,(j+1)*count+(k+1)%count,(j+1)*count+k))
    faces.append(tuple(2*count+k for k in range(count)))
    faces.append(tuple(reversed(range(count))))
    return finish(mesh_object(name,vertices,faces,mat),weighted=True)


def core(name,x,z,y,side,mat):
    outline=[(-.009,-.095),(.048,-.035),(.052,.022),(.010,.095),(-.048,.035),(-.052,-.022)]
    vertices=[(x+px,y,z+pz) for px,pz in outline]
    vertices.extend([(x-.011,y+side*.027,z-.016),(x+.006,y+side*.027,z+.026)])
    faces=[(0,1,6),(1,2,7,6),(2,3,7),(3,4,7),(4,5,6,7),(5,0,6),tuple(reversed(range(6)))]
    uv=[]
    for i,face in enumerate(faces):
        uv.append([(.08+.84*(vertices[k][0]-x+.052)/.104,.06+.88*(vertices[k][2]-z+.095)/.19) for k in face])
    return finish(mesh_object(name,vertices,faces,mat,uv),bevel=.0015,segments=2,weighted=True)


def shard(name,origin,length,radius,mat,seed):
    rng=random.Random(seed)
    sides=5
    vertices=[]
    for j in range(2):
        for k in range(sides):
            a=math.tau*k/sides
            r=radius*(.72 if j==0 else 1)*(1+rng.uniform(-.08,.08))
            vertices.append((math.cos(a)*r,math.sin(a)*r,(-.40 if j==0 else .22)*length))
    vertices.extend([(.08*radius,.03*radius,length*.60),(-.03*radius,0,-length*.60)])
    faces=[];uv=[]
    for k in range(sides):
        j=(k+1)%sides
        faces.extend([(k,j,j+sides,k+sides),(k+sides,j+sides,2*sides),(j,k,2*sides+1)])
        uv.extend([[(.08,.18),(.92,.18),(.92,.72),(.08,.72)],[(.08,.72),(.92,.72),(.5,.98)],[(.92,.18),(.08,.18),(.5,.02)]])
    obj=finish(mesh_object(name,vertices,faces,mat,uv),bevel=.0016,segments=2,weighted=True)
    obj.location=origin
    return obj


def animate_shard(obj,index):
    origin=obj.location.copy()
    base_rotation=Vector((.10*(index-1),.22*(index-1),.20*(index+1)))
    for frame in range(1,FPS*DURATION+2,2):
        theta=(frame-1)/(FPS*DURATION)*math.tau+index*math.tau/3
        obj.location=origin+Vector((.010*math.sin(theta),.006*math.sin(theta+.8),.016*math.cos(theta)))
        obj.rotation_euler=base_rotation+Vector((.06*math.sin(theta),.10*math.sin(theta+.4),.10*math.cos(theta)))
        obj.keyframe_insert(data_path='location',frame=frame)
        obj.keyframe_insert(data_path='rotation_euler',frame=frame)
    obj.animation_data.action.name=CLIP+' | '+obj.name
    for curve in obj.animation_data.action.fcurves:
        for key in curve.keyframe_points:key.interpolation='LINEAR'


def runes(shape,mat,socket_count=0):
    paths = ([[(.205,.635),(.222,.664),(.242,.648),(.223,.621)],
              [(.275,.626),(.288,.665),(.312,.686)],
              [(.291,.649),(.321,.645)],
              [(.348,.650),(.370,.684),(.391,.668),(.374,.639)]] if shape=='hache' else
             [[(-.305,.775),(-.277,.797),(-.251,.784)],
              [(-.210,.803),(-.183,.822),(-.156,.805),(-.182,.785)],
              [(.186,.788),(.214,.779),(.227,.751)]])
    if shape=='faucille':
        paths=[]
        rail=sickle_rail()
        for sample in (31,39,46):
            p,tangent,width,progress=rail[sample]
            normal=Vector((-tangent.y,tangent.x))
            paths.append([tuple(p+tangent*along+normal*across) for along,across in
                          ((-.025,0),(0,.015),(.025,0),(0,-.015),(-.025,0))])
    objects=[]
    for side in [-1,1]:
        for index,path in enumerate(paths):
            if side<0 and any(math.hypot(x-cx,z-cz)<.061 for x,z in path for cx,cz in SOCKET_CENTRES[shape][:socket_count]):
                continue
            points=[]
            for x,z in path:
                thickness=.060 if shape=='faucille' else (.07 if shape=='hache' else .07*(.12+.88*max(0,1-abs(x)/.79)))
                if shape=='faucille':
                    progress=min(rail,key=lambda section:(section[0]-Vector((x,z))).length_squared)[3]
                    thickness*=1-.97*smooth_scalar(.60,1,progress)
                points.append((x,side*(thickness+.001),z))
            objects.append(tube('Mana inlay | '+('front' if side<0 else 'back')+' | %02d'%index,points,.0028,mat,sides=7))
    return objects


def socket_surface_y(shape,x):
    thickness=.060 if shape=='faucille' else (.07 if shape=='hache' else .07*(.12+.88*max(0,1-abs(x)/.79)))
    return -thickness-.0015


def empty_socket(shape,index,bronze,leather):
    x,z=SOCKET_CENTRES[shape][index-1]
    base_y=socket_surface_y(shape,x)
    profile=[(.045,0),(.048,.005),(.047,.033),(.043,SOCKET_LIP_DEPTH),
             (.034,SOCKET_LIP_DEPTH),(SOCKET_INNER_RADIUS,.034),(SOCKET_INNER_RADIUS,.009),(.028,SOCKET_FLOOR_DEPTH)]
    sides=32
    vertices=[(x+radius*math.cos(math.tau*k/sides),base_y-depth,z+radius*math.sin(math.tau*k/sides))
              for radius,depth in profile for k in range(sides)]
    faces=[];uvs=[]
    for j in range(len(profile)):
        next_j=(j+1)%len(profile)
        for k in range(sides):
            next_k=(k+1)%sides
            faces.append((j*sides+k,j*sides+next_k,next_j*sides+next_k,next_j*sides+k))
            # The rim is lighter; its inner wall receives the shaded part of the same painted bronze.
            t1=.75 if j in (2,3,4) else .22
            t2=.75 if next_j in (2,3,4) else .22
            uvs.append([(k/sides,t1),(next_k/sides,t1),(next_k/sides,t2),(k/sides,t2)])
    shell=finish(mesh_object('Socket_%02d | bronze hollow cup'%index,vertices,faces,bronze,uvs),weighted=True)
    vertices=[]
    for depth in (SOCKET_FLOOR_DEPTH,SOCKET_FLOOR_DEPTH-.002):
        for k in range(sides):
            a=math.tau*k/sides
            vertices.append((x+.028*math.cos(a),base_y-depth,z+.028*math.sin(a)))
    faces=[tuple(range(sides)),tuple(reversed(range(sides,2*sides)))]
    for k in range(sides):faces.append((k,(k+1)%sides,(k+1)%sides+sides,k+sides))
    uvs=[]
    for face in faces:uvs.append([(.02+(vertices[k][0]-x+.028)/.056*.08,.02+(vertices[k][2]-z+.028)/.056*.08) for k in face])
    bed=finish(mesh_object('Socket_%02d | empty recessed dark bed'%index,vertices,faces,leather,uvs),weighted=True)
    return [shell,bed]


def make_socket_marker(shape,index,root,collection):
    x,z=SOCKET_CENTRES[shape][index-1]
    y=socket_surface_y(shape,x)-SOCKET_FLOOR_DEPTH
    marker=bpy.data.objects.new('Socket_%02d'%index,None)
    collection.objects.link(marker)
    marker.parent=root
    marker.location=(Vector((x,y,z))-handle_center(0))*TOOL_SCALE
    marker.rotation_euler=(math.pi/2,0,0)
    marker.empty_display_type='ARROWS'
    marker.empty_display_size=.045
    marker['socket_index']=index
    marker['empty']=True
    marker['purpose']='Future crystal attachment: origin at recessed bed, local +Z outward.'
    marker['inner_diameter']=2*SOCKET_INNER_RADIUS*TOOL_SCALE
    marker['cavity_depth']=(SOCKET_LIP_DEPTH-SOCKET_FLOOR_DEPTH)*TOOL_SCALE
    return marker


def socket_record(marker):
    p=marker.location
    q=marker.rotation_euler.to_quaternion()
    return {'index':marker['socket_index'],'name':marker.name,'marker':marker.name,'empty':True,
            'position_blender':[round(v,7) for v in p],
            'rotation_blender_xyzw':[round(q.x,8),round(q.y,8),round(q.z,8),round(q.w,8)],
            'position_gltf':[round(p.x,7),round(p.z,7),round(-p.y,7)],
            'rotation_gltf_xyzw':[0,0,0,1],
            'inner_diameter':round(marker['inner_diameter'],6),'cavity_depth':round(marker['cavity_depth'],6),
            'origin':'centre du fond utile','axis':'local +Z sortant','units':'metres'}


def glb_socket_axes(path):
    """Empty attachment markers keep +Z outward in both authoring and delivery coordinates."""
    raw=path.read_bytes()
    json_size=struct.unpack_from('<I',raw,12)[0]
    document=json.loads(raw[20:20+json_size])
    changed=False
    for node in document['nodes']:
        if node.get('name','') in {'Socket_01','Socket_02','Socket_03'}:
            assert 'mesh' not in node and not node.get('children')
            node.pop('rotation',None)
            changed=True
    if changed:
        payload=json.dumps(document,separators=(',',':'),ensure_ascii=False).encode('utf-8')
        payload+=b' '*((-len(payload))%4)
        binary_chunk=raw[20+json_size:]
        size=12+8+len(payload)+len(binary_chunk)
        path.write_bytes(struct.pack('<4sII',b'glTF',2,size)+struct.pack('<II',len(payload),0x4E4F534A)+payload+binary_chunk)


def bounds(objects):
    bpy.context.view_layer.update()
    points=[obj.matrix_world@v.co for obj in objects if obj.type=='MESH' for v in obj.data.vertices]
    return Vector([min(p[i] for p in points) for i in range(3)]),Vector([max(p[i] for p in points) for i in range(3)])


def build_geometry(shape,palette='azur',socket_count=0):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene=bpy.context.scene
    scene.name=CLIP
    scene.render.fps=FPS
    scene.frame_start=1
    scene.frame_end=FPS*DURATION+1
    scene.unit_settings.system='METRIC'
    mats={name:material(name,palette) for name in ['steel','bronze','wood','leather','mana']}
    objects=[shaped_head(shape,mats['steel'])]
    is_sickle=shape=='faucille'
    objects.append(sleeve('Carved ash handle | gently bent continuous volume',-.275 if is_sickle else -.615,.43 if is_sickle else .855,mats['wood'],rings=30 if is_sickle else 42,segments=20))
    objects.append(sleeve('Leather | continuous fitted grip',-.225 if is_sickle else -.37,.13 if is_sickle else .26,mats['leather'],radius_add=.003,rings=17,segments=20))
    objects.append(grip_wrap(mats['leather'],-.215,.12,5.6) if is_sickle else grip_wrap(mats['leather']))
    ferrules=([('Pommel',-.295,-.245,.006,.003),('Grip lower ferrule',-.239,-.214,.005,.0015),('Grip upper ferrule',.114,.146,.005,.0015),('Head socket',.24,.43,.022,.003)] if is_sickle else
              [('Pommel',-.635,-.565,.006,.003),('Grip lower ferrule',-.386,-.354,.005,.0015),('Grip upper ferrule',.244,.280,.005,.0015),('Head socket',.61,.87,.022,.003)])
    for name,bottom,top,extra,lip in ferrules:
        objects.append(sleeve('Bronze | '+name,bottom,top,mats['bronze'],extra,segments=20,rings=7 if name!='Head socket' else 11,shape_power=.80,lip=lip))
    # A small pointed metal termination makes the handle read as intentionally authored.
    for side in [-1,1]:
        objects.append(bezel('Bronze | core seat '+str(side),.005,.340 if is_sickle else .746,side*.071,side,mats['bronze']))
        objects.append(core('Mana | contained core '+str(side),.005,.340 if is_sickle else .746,side*.093,side,mats['mana']))
    objects+=runes(shape,mats['mana'],socket_count)
    for index in range(1,socket_count+1):objects+=empty_socket(shape,index,mats['bronze'],mats['leather'])
    locations=([(-.17,-.013,.91),(-.205,.018,.72),(.082,.012,1.04)] if shape=='hache' else [(-.24,.014,1.03),(.105,-.018,1.01),(.35,.012,.945)])
    if is_sickle:locations=[(-.025,.014,1.145),(.225,-.018,1.195),(.425,.012,1.115)]
    for index,(origin,length,radius) in enumerate(zip(locations,[.10,.065,.075],[.023,.017,.019])):
        obj=shard('Orbiting mana shard | %02d'%(index+1),origin,length,radius,mats['mana'],index+44)
        animate_shard(obj,index)
        objects.append(obj)
    # Apply the portable gathering-tool scale to data and curves, keeping the attachment root identity.
    grip_offset=handle_center(0)
    for obj in objects:
        if obj.animation_data:
            for vertex in obj.data.vertices:vertex.co*=TOOL_SCALE
            obj.location=(obj.location-grip_offset)*TOOL_SCALE
            for curve in obj.animation_data.action.fcurves:
                if curve.data_path=='location':
                    offset=grip_offset[curve.array_index]
                    for key in curve.keyframe_points:
                        key.co[1]=(key.co[1]-offset)*TOOL_SCALE
                        key.handle_left[1]=(key.handle_left[1]-offset)*TOOL_SCALE
                        key.handle_right[1]=(key.handle_right[1]-offset)*TOOL_SCALE
        else:
            for vertex in obj.data.vertices:vertex.co=(vertex.co-grip_offset)*TOOL_SCALE
    root=bpy.data.objects.new('Tool_Root',None)
    scene.collection.objects.link(root)
    root.empty_display_type='ARROWS'
    root.empty_display_size=.18
    root['purpose']='Grip centre; attach this root to the hand socket.'
    root['axes']='Blender +Z toward head, head in XZ, front -Y; glTF +Y toward head, head in XY, front +Z.'
    root['artistic_status']='proposal'
    collection=bpy.data.collections.new('Arkya_Summoned_Tools')
    scene.collection.children.link(collection)
    for obj in objects+[root]:
        for old in list(obj.users_collection):old.objects.unlink(obj)
        collection.objects.link(obj)
        if obj!=root:obj.parent=root
    markers=[make_socket_marker(shape,index,root,collection) for index in range(1,socket_count+1)]
    root['socket_count']=socket_count
    scene.frame_set(1)
    return objects,root,markers


def configure_render(gpu=False):
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=48
    scene.cycles.use_denoising=True
    scene.cycles.device='CPU'
    if gpu:
        preferences=bpy.context.preferences.addons['cycles'].preferences
        preferences.compute_device_type='OPTIX'
        preferences.get_devices()
        devices=[d for d in preferences.devices if d.type=='OPTIX']
        if devices:
            for device in preferences.devices:device.use=device in devices
            scene.cycles.device='GPU'
    scene.world=bpy.data.worlds.new('Neutral sage studio')
    scene.world.use_nodes=True
    background=scene.world.node_tree.nodes['Background']
    background.inputs[0].default_value=(.30,.35,.33,1)
    background.inputs[1].default_value=.65
    scene.view_settings.view_transform='AgX'
    scene.render.image_settings.file_format='PNG'
    scene.render.resolution_percentage=100


def studio(objects,path,gpu=False,render_now=True):
    configure_render(gpu)
    scene=bpy.context.scene
    low,high=bounds(objects)
    centre=(low+high)/2
    extent=max(high-low)
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,low.z-.025))
    floor=bpy.context.object
    floor.name='Studio floor | excluded from export'
    mat=bpy.data.materials.new('Studio sage | preview only')
    mat.use_nodes=True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.20,.235,.215,1)
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9
    floor.data.materials.append(mat)
    for name,location,energy,size in [('Large warm key',(-3.3,-4.3,5.2),1050,4),('Soft fill',(3,-1.6,2.9),520,3),('Edge light',(1,3.3,4.5),900,3)]:
        data=bpy.data.lights.new(name,'AREA')
        data.energy=energy
        data.shape='DISK'
        data.size=size
        obj=bpy.data.objects.new(name,data)
        scene.collection.objects.link(obj)
        obj.location=location
        obj.rotation_euler=(centre-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Tool portrait | front three-quarter')
    camera=bpy.data.objects.new(data.name,data)
    scene.collection.objects.link(camera)
    scene.camera=camera
    camera.location=centre+Vector((1.55,-7,2.10))
    camera.rotation_euler=(centre-camera.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO'
    data.ortho_scale=extent*1.24
    scene.render.resolution_x=1100
    scene.render.resolution_y=1100
    scene.render.filepath=str(path)
    if render_now:bpy.ops.render.render(write_still=True)


def write_manifest(record=None):
    manifest_path=ROOT/'manifest.json'
    old=json.loads(manifest_path.read_text(encoding='utf-8')) if manifest_path.exists() else {}
    records=old.get('assets',[])
    for asset in records:
        asset.setdefault('socket_count',0)
        asset.setdefault('sockets',[])
    if record is not None:
        records=[a for a in records if a['id']!=record['id']]+[record]
    manifest={'family':'outils-invoques','artistic_status':'proposal',
              'description':'Outils de récolte peints, trois couleurs de mana et zéro à trois réceptacles vides pour de futurs cristaux.',
              'shapes':[dict(id=k,artistic_status='proposal',**v) for k,v in SHAPES.items()],
              'materials':[{'id':k,'label':v['label'],'swatch':v['swatch'],'description':'Acier ardoise, bois sombre, bronze patiné ; '+v['label'].lower()+'.'} for k,v in PALETTES.items()],
              'socket_counts':[0,1,2,3],
              'sockets_contract':{'empty':True,'units':'metres','origin':'centre du fond utile','axis':'local +Z sortant',
                                  'decorative_mana_core_counts_as_socket':False},
              'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material']),a['socket_count']))}
    manifest_path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for asset in records:
        path=ROOT/asset['glb']
        path.with_suffix('.json').write_text(json.dumps(asset,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')


def build(shape,palette,socket_count,args):
    stem=shape+'-'+palette+('-sockets-'+str(socket_count) if socket_count else '')
    folder=ASSETS/stem
    files=[folder/(stem+extension) for extension in ('.glb','.blend','-preview.png','.json')]
    protected=shape in ('hache','pioche') and palette=='azur' and socket_count==0
    if all(p.exists() for p in files) and (protected or not args.force):
        record=json.loads(files[3].read_text(encoding='utf-8'))
        record.setdefault('socket_count',0)
        record.setdefault('sockets',[])
        write_manifest(record)
        print('TOOL_PRESERVED' if protected else 'TOOL_ALREADY_COMPLETE',stem,flush=True)
        return
    if protected:raise RuntimeError('The original azure tool must already exist; never recreate its frozen delivery files.')
    objects,root,markers=build_geometry(shape,palette,socket_count)
    low,high=bounds(objects)
    dimensions=high-low
    folder.mkdir(parents=True,exist_ok=True)
    scene=bpy.context.scene
    scene['arkya_resource']='outils-invoques'
    scene['source_script']='scripts/generate_tools.py'
    root['shape']=shape
    root['palette']=palette
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects+[root]+markers:obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,
        export_apply=True,export_yup=True,export_animations=True,export_animation_mode='SCENE',
        export_anim_scene_split_object=False,export_anim_slide_to_zero=True,export_force_sampling=True,
        export_frame_range=True,export_frame_step=1,export_optimize_animation_size=False,
        export_materials='EXPORT',export_cameras=False,export_lights=False,export_tangents=True)
    glb_socket_axes(folder/(stem+'.glb'))
    triangles=sum(len(p.vertices)-2 for obj in objects for p in obj.data.polygons)
    assert triangles<=16000,(stem,triangles)
    text=bpy.data.texts.new('Arkya provenance')
    text.write('Original sculpted gathering-tool geometry and procedural painted 2K atlases, created for Arkya. No third-party geometry or art. Five opaque PBR materials; contained mana emission; 4-second shard motion. Tool_Root at grip centre, Z-up Blender/Y-up glTF. Empty bronze sockets: recessed dark bed, no inserted crystal. Socket markers originate at the bed with local +Z outward in each format. Orange/green mana palettes follow the approved mana pools; neutral materials are unchanged. Source: scripts/generate_tools.py. Artistic proposal awaiting user review. Presentation lights, camera and floor excluded from GLB.')
    if args.render:studio(objects,folder/(stem+'-preview.png'),args.gpu)
    scene.frame_set(1)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label']+' · '+str(socket_count)+' emplacement'+('s' if socket_count!=1 else ''),
            'artistic_status':'proposal','glb':'assets/'+stem+'/'+stem+'.glb',
            'blend':'assets/'+stem+'/'+stem+'.blend','preview':'assets/'+stem+'/'+stem+'-preview.png',
            'height':round(dimensions.z,4),'width':round(dimensions.x,4),'depth':round(dimensions.y,4),
            'triangles':triangles,'materials':5,'texture_resolution':[SIZE,SIZE],
            'animation':CLIP,'animation_duration':DURATION,'fps':FPS,
            'socket_count':socket_count,'sockets':[socket_record(marker) for marker in markers],
            'pivot':'centre de la prise, Tool_Root','axes_blender':'+Z vers la tête, face avant -Y',
            'axes_glb':'+Y vers la tête, face avant +Z','opaque':True}
    (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    write_manifest(record)
    print('SUMMONED_TOOL_READY',json.dumps(record,ensure_ascii=False),flush=True)


def render_sheet(records,columns,title,path,gpu=False,show_sizes=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    objects=[]
    labels=[]
    pitch=1.18
    rows=math.ceil(len(records)/columns)
    for index,record in enumerate(records):
        centre=(index%columns-(columns-1)/2)*pitch
        row_z=((rows-1)/2-index//columns)*1.67
        with bpy.data.libraries.load(str(ROOT/record['blend']),link=False) as (available,loaded):
            loaded.collections=['Arkya_Summoned_Tools']
        collection=loaded.collections[0]
        bpy.context.scene.collection.children.link(collection)
        meshes=[o for o in collection.objects if o.type=='MESH']
        root=next(o for o in collection.objects if o.type=='EMPTY' and o.parent is None)
        low,high=bounds(meshes)
        root.location=(centre-(low.x+high.x)/2,0,row_z)
        objects+=meshes
        short=SHAPES[record['shape']]['label'].replace(' invoquée','')
        if show_sizes:
            label=short+' · '+str(round(record['height']*100))+' cm'
        elif columns==4:
            label=short+' · '+str(record['socket_count'])+' emplacement'+('s' if record['socket_count']!=1 else '')
        else:label=short+' · '+PALETTES[record['material']]['label']
        labels.append((centre,row_z-.60,label))
    studio(objects,path,gpu,render_now=False)
    scene=bpy.context.scene
    bpy.data.objects['Studio floor | excluded from export'].hide_render=True
    scene.unit_settings.system='METRIC'
    scene.render.resolution_x=2400 if columns==4 else 2100
    scene.render.resolution_y=2500 if rows>=3 else (1200 if rows==1 else 1800)
    scene.camera.data.ortho_scale=5.4 if rows>=3 else (5.10 if columns==4 else 4.35)
    target=Vector((0,0,.12))
    scene.camera.location=target+Vector((0,-10,1.9))
    scene.camera.rotation_euler=(target-scene.camera.location).to_track_quat('-Z','Y').to_euler()
    label_mat=bpy.data.materials.new('Muted ivory lettering')
    label_mat.use_nodes=True
    node=label_mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value=(.52,.61,.57,1)
    node.inputs['Roughness'].default_value=1
    for x,z,text in labels:
        curve=bpy.data.curves.new('Comparison label','FONT')
        curve.body=text
        curve.align_x='CENTER'
        curve.size=.060
        obj=bpy.data.objects.new(text,curve)
        scene.collection.objects.link(obj)
        obj.location=(x,-.02,z)
        obj.rotation_euler=scene.camera.rotation_euler
        curve.materials.append(label_mat)
    scene['comparison']=title
    scene.frame_set(1)
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(path.with_suffix('.blend')))
    print('TOOL_COMPARISON_READY',path.name,flush=True)


def comparison(gpu=False):
    manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
    assets=manifest['assets']
    def choose(shape,palette,count):
        return next(a for a in assets if a['shape']==shape and a['material']==palette and a['socket_count']==count)
    sockets=[choose(shape,'azur',count) for shape in SHAPES for count in range(4)]
    colors=[choose(shape,palette,3) for shape in SHAPES for palette in PALETTES]
    sizes=[choose(shape,'azur',0) for shape in SHAPES]
    render_sheet(sockets,4,'Les quatre nombres de réceptacles, à la même échelle',ROOT/'comparaison-sockets.png',gpu)
    render_sheet(colors,3,'Les trois couleurs de mana avec trois réceptacles vides',ROOT/'comparaison-couleurs.png',gpu)
    render_sheet(sizes,3,'Les trois outils à la même échelle réelle',ROOT/'comparaison-tailles.png',gpu,show_sizes=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--shape',choices=['all']+list(SHAPES),default='all')
    parser.add_argument('--material',choices=['all']+list(PALETTES),default='all')
    parser.add_argument('--socket-count',choices=['all','0','1','2','3'],default='all')
    parser.add_argument('--render',action='store_true')
    parser.add_argument('--gpu',action='store_true')
    parser.add_argument('--textures',action='store_true')
    parser.add_argument('--comparison',action='store_true')
    parser.add_argument('--force',action='store_true')
    parser.add_argument('--only-comparisons',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    make_textures(args.textures)
    palettes=list(PALETTES) if args.material=='all' else [args.material]
    counts=[0,1,2,3] if args.socket_count=='all' else [int(args.socket_count)]
    write_manifest()
    for palette in palettes:make_palette_textures(palette,args.textures)
    if not args.only_comparisons:
        for shape in SHAPES if args.shape=='all' else [args.shape]:
            for palette in palettes:
                for socket_count in counts:build(shape,palette,socket_count,args)
    if args.comparison or args.only_comparisons:comparison(args.gpu)


if __name__=='__main__':main()
