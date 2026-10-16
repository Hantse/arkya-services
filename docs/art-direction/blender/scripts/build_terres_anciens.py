"""Arkya / Terres des Anciens: an entirely procedural Blender environment.

Run build_scene() in a clean Blender 4.5+ scene. The caller owns reset,
render settings, rendering and saving. No external assets are required.
All dimensions are metres. Front of the scene is negative Y; Z is up.
"""

import math
import random

import bpy
from mathutils import Euler, Quaternion, Vector


SEED = 2137
RNG = random.Random(SEED)
MATS = {}


def material(name, color, roughness=0.65, metallic=0.0, noise=0.0,
             emission=None, grain=None):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*color, 1)
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    bsdf = nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    if emission:
        bsdf.inputs['Emission Color'].default_value = (*emission[0], 1)
        bsdf.inputs['Emission Strength'].default_value = emission[1]
    if noise:
        tex = nodes.new('ShaderNodeTexNoise')
        tex.inputs['Scale'].default_value = 4.6
        tex.inputs['Detail'].default_value = 2.0
        tex.inputs['Roughness'].default_value = 0.62
        coord = nodes.new('ShaderNodeTexCoord')
        if grain:
            mapping = nodes.new('ShaderNodeVectorMath')
            mapping.operation = 'MULTIPLY'
            mapping.inputs[1].default_value = grain
            links.new(coord.outputs['Generated'], mapping.inputs[0])
            links.new(mapping.outputs['Vector'], tex.inputs['Vector'])
        else:
            links.new(coord.outputs['Generated'], tex.inputs['Vector'])
        ramp = nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].position = .16
        ramp.color_ramp.elements[0].color = tuple(max(0, c*(1-noise)) for c in color)+(1,)
        ramp.color_ramp.elements[1].position = .86
        ramp.color_ramp.elements[1].color = tuple(min(1, c*(1+noise)) for c in color)+(1,)
        links.new(tex.outputs['Fac'], ramp.inputs[0])
        links.new(ramp.outputs[0], bsdf.inputs['Base Color'])
        bump = nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value = .16
        bump.inputs['Distance'].default_value = .075 if grain else .035
        links.new(tex.outputs['Fac'], bump.inputs['Height'])
        links.new(bump.outputs[0], bsdf.inputs['Normal'])
    return mat


def assign(obj, mat):
    obj.data.materials.append(MATS[mat] if isinstance(mat, str) else mat)
    return obj


def smooth(obj):
    if obj.type == 'MESH':
        for face in obj.data.polygons:
            face.use_smooth = True
    return obj


def mesh_object(name, vertices, faces, mat, smooth_faces=True):
    mesh = bpy.data.meshes.new(name + 'Mesh')
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.collection.objects.link(obj)
    assign(obj, mat)
    if smooth_faces:
        smooth(obj)
    return obj


def bevel_box(name, location, scale, mat, bevel=.06, rotation=None):
    x,y,z=(s*.5 for s in scale)
    vertices=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
              (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    obj=mesh_object(name,vertices,faces,mat,False)
    obj.location=location
    if rotation:
        obj.rotation_euler = rotation
    mod = obj.modifiers.new('Soft crafted edges', 'BEVEL')
    mod.width = bevel
    mod.segments = 3
    mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True
    return obj


def sphere(name, location, scale, mat, segments=24, rings=16):
    vertices=[(0,0,1)]
    faces=[]
    for i in range(1,rings):
        theta=math.pi*i/rings
        for j in range(segments):
            a=math.tau*j/segments
            vertices.append((math.sin(theta)*math.cos(a),math.sin(theta)*math.sin(a),math.cos(theta)))
    bottom=len(vertices)
    vertices.append((0,0,-1))
    for j in range(segments):
        nxt=(j+1)%segments
        faces.append((0,1+j,1+nxt))
        for i in range(rings-2):
            a=1+i*segments+j
            b=1+i*segments+nxt
            faces.append((a,a+segments,b+segments,b))
        last=1+(rings-2)*segments
        faces.append((last+j,bottom,last+nxt))
    obj=mesh_object(name,vertices,faces,mat)
    obj.location=location
    obj.scale = scale
    return obj


def cylinder(name, location, radius, depth, mat, vertices=24, radius_top=None):
    sides=vertices
    top=radius if radius_top is None else radius_top
    verts=[(math.cos(math.tau*j/sides)*r,math.sin(math.tau*j/sides)*r,z)
           for r,z in ((radius,-depth*.5),(top,depth*.5)) for j in range(sides)]
    faces=[]
    for j in range(sides):
        n=(j+1)%sides
        faces.append((j,n,n+sides,j+sides))
    faces.append(tuple(range(sides-1,-1,-1)))
    faces.append(tuple(range(sides,sides*2)))
    obj=mesh_object(name,verts,faces,mat)
    obj.location=location
    obj.data.polygons[-1].use_smooth=False
    obj.data.polygons[-2].use_smooth=False
    mod = obj.modifiers.new('Rounded rims', 'BEVEL')
    mod.width = min(.06, radius*.18, depth*.15)
    mod.segments = 3
    obj.modifiers.new('Weighted normals', 'WEIGHTED_NORMAL')
    return obj


def beam(name, start, end, radius, mat, radius_top=None, vertices=16):
    a, b = Vector(start), Vector(end)
    obj = cylinder(name, (a+b)/2, radius, (b-a).length, mat,
                   vertices, radius_top)
    obj.rotation_euler = (b-a).to_track_quat('Z', 'Y').to_euler()
    return obj


def catmull_path(points, radii, steps=7):
    points = [Vector(p) for p in points]
    out, widths = [], []
    for i in range(len(points)-1):
        p0, p1 = points[max(i-1, 0)], points[i]
        p2, p3 = points[i+1], points[min(i+2, len(points)-1)]
        for k in range(steps):
            t = k/steps
            out.append(.5*((2*p1)+(-p0+p2)*t+
                            (2*p0-5*p1+4*p2-p3)*t*t+
                            (-p0+3*p1-3*p2+p3)*t*t*t))
            widths.append(radii[i]*(1-t)+radii[i+1]*t)
    out.append(points[-1])
    widths.append(radii[-1])
    return out, widths


def organic_tube(name, points, radii, mat, sides=16, steps=7, ridged=.03):
    pts, widths = catmull_path(points, radii, steps)
    vertices, faces = [], []
    for i, (p, radius) in enumerate(zip(pts, widths)):
        tangent = (pts[min(i+1, len(pts)-1)]-pts[max(i-1, 0)]).normalized()
        # Stable frame for the sculptural, mostly vertical roots and branches.
        axis = Vector((0, 1, 0)) if abs(tangent.y) < .93 else Vector((1, 0, 0))
        u = tangent.cross(axis).normalized()
        v = tangent.cross(u).normalized()
        for j in range(sides):
            a = math.tau*j/sides
            ripple = 1 + ridged*math.sin(a*5+i*.12) + ridged*.65*math.sin(a*3-i*.07)
            vertices.append(tuple(p + radius*ripple*(math.cos(a)*u+math.sin(a)*v)))
    for i in range(len(pts)-1):
        for j in range(sides):
            a, b = i*sides+j, i*sides+(j+1)%sides
            faces.append((a, b, b+sides, a+sides))
    faces.append(tuple(range(sides-1, -1, -1)))
    faces.append(tuple((len(pts)-1)*sides+j for j in range(sides)))
    return mesh_object(name, vertices, faces, mat)


def ring(name, center, radius, thickness, mat, rotation=None, segments=48):
    minor=10
    vertices,faces=[],[]
    for i in range(segments):
        a=math.tau*i/segments
        for j in range(minor):
            b=math.tau*j/minor
            r=radius+thickness*math.cos(b)
            vertices.append((r*math.cos(a),r*math.sin(a),thickness*math.sin(b)))
    for i in range(segments):
        for j in range(minor):
            faces.append((i*minor+j,((i+1)%segments)*minor+j,
                          ((i+1)%segments)*minor+(j+1)%minor,i*minor+(j+1)%minor))
    obj=mesh_object(name,vertices,faces,mat)
    obj.location=center
    if rotation:
        obj.rotation_euler = rotation
    return obj


def river_y(x):
    return -1.6 + .42*math.sin(x*.45)


def ground_z(x, y):
    height = .12 + .08*math.sin(x*.53+y*.4) + .075*math.cos(y*.81-x*.2)
    height += .45*(min(abs(x)/9, 1)**2)
    height += .48*math.exp(-((x+5.0)**2+(y-6.2)**2)/17)
    height += .34*math.exp(-((x-5.5)**2+(y-6.8)**2)/13)
    distance = abs(y-river_y(x))
    height -= .83*math.exp(-(distance/.95)**4)
    return height


def terrain():
    # Concentric elliptical rings preserve a continuous authored island contour.
    # Clipping a rectangular grid would leave visibly stepped perimeter teeth.
    verts,faces=[(0,1.4,ground_z(0,1.4))],[]
    segments,rings=224,48
    for r in range(1,rings+1):
        radius=r/rings
        for i in range(segments):
            a=math.tau*i/segments
            x,y=11*radius*math.cos(a),1.4+11.4*radius*math.sin(a)
            verts.append((x,y,ground_z(x,y)))
    for i in range(segments):
        nxt=(i+1)%segments
        faces.append((0,1+i,1+nxt))
        for r in range(rings-1):
            a=1+r*segments+i
            b=1+r*segments+nxt
            faces.append((a,a+segments,b+segments,b))
    obj = mesh_object('Living mossy forest floor', verts, faces, 'grass')
    mod = obj.modifiers.new('Continuous soil depth', 'SOLIDIFY')
    mod.thickness = .72
    mod = obj.modifiers.new('Soft bank edges', 'BEVEL')
    mod.width, mod.segments = .09, 2

    verts, faces = [], []
    for i in range(91):
        x = -10.8+i*.24
        center = river_y(x)
        for j in range(9):
            y = center+(j/8-.5)*2.45
            z = -.285 + .014*math.sin(x*3.5+y*9) + .009*math.sin(x*8-y*5)
            verts.append((x, y, z))
    for i in range(90):
        for j in range(8):
            a = i*9+j
            faces.append((a, a+9, a+10, a+1))
    mesh_object('Creek | sculpted water surface', verts, faces, 'water')
    for n in range(16):
        x = RNG.uniform(-9.7, 9.7)
        y = river_y(x)+RNG.uniform(-.6, .6)
        if abs(x) < 2.4:
            continue
        organic_tube('Soft moving water glint', [(x-.35,y,-.255),(x,y+.035,-.25),
                                                 (x+.5,y+.015,-.255)],
                     [.007,.016,.004], 'water_glint', sides=6, steps=4, ridged=0)

    for lower, upper in [(-9.4, -3.55), (.6, 12.0)]:
        vs, fs = [], []
        steps = int((upper-lower)/.24)+1
        for i in range(steps):
            y = lower + (upper-lower)*i/(steps-1)
            c = .22*math.sin(y*.51)
            w = 1.4 + .20*math.cos(y*.78)
            for j in range(9):
                x = c+(j/8-.5)*w*2
                vs.append((x,y,ground_z(x,y)+.018))
        for i in range(steps-1):
            for j in range(8):
                a=i*9+j
                fs.append((a,a+1,a+10,a+9))
        mesh_object('Worn pale woodland trail',vs,fs,'path')


def rock(name, pos, scale, moss=True):
    obj = sphere(name, pos, scale, 'rock', segments=16, rings=10)
    phase = RNG.uniform(0, 6)
    for v in obj.data.vertices:
        p = v.co
        p *= 1 + .085*math.sin(p.x*5.1+p.y*3+phase) + .065*math.sin(p.z*7-p.x*2)
    obj.rotation_euler = (RNG.uniform(-.2,.2), RNG.uniform(-.2,.2), RNG.uniform(0,6))
    mod = obj.modifiers.new('Weathered flowing stone', 'SUBSURF')
    mod.levels = 1
    if moss:
        cap = sphere('Cushion moss', (pos[0]-.05,pos[1],pos[2]+scale[2]*.61),
                     (scale[0]*.78,scale[1]*.73,scale[2]*.30), 'moss', 20, 10)
        cap.rotation_euler = obj.rotation_euler
    return obj


def leaf_mesh_add(vertices, faces, origin, length, width, rotation):
    origin = Vector(origin)
    matrix = rotation.to_matrix()
    start = len(vertices)
    # A raised central rib gives the leaves actual volume in silhouettes.
    for i in range(9):
        t = i/8
        blade_width = math.sin(math.pi*t)**.78 * width
        for side in (-1, 0, 1):
            local = Vector((t*length, side*blade_width*.5,
                            math.sin(math.pi*t)*length*(.12-.075*abs(side))+
                            t*t*length*.12))
            vertices.append(tuple(origin+matrix@local))
    for i in range(8):
        a = start+i*3
        faces.extend([(a,a+3,a+4,a+1),(a+1,a+4,a+5,a+2)])


def canopy(name, center, size, count=100):
    c = Vector(center)
    # The inner mass is hidden by overlapping leaves, keeping shadow readable.
    core = sphere(name+' | soft inner crown', c, (size[0]*.8,size[1]*.8,size[2]*.7),
                  'foliage_dark', 24, 12)
    for v in core.data.vertices:
        p=v.co
        p *= 1+.07*math.sin(p.x*9+p.y*4)*math.sin(p.z*6-p.y*5)
    vertices, faces, indices = [], [], []
    for i in range(count):
        a=RNG.uniform(0, math.tau)
        z=RNG.uniform(-.85, 1)
        r=math.sqrt(1-z*z)
        origin = c+Vector((math.cos(a)*r*size[0]*.89,
                           math.sin(a)*r*size[1]*.89,
                           z*size[2]*.85))
        angle=RNG.uniform(0, math.tau)
        rot=Euler((RNG.uniform(-.6,.6),RNG.uniform(-.8,.6),angle)).to_quaternion()
        before=len(faces)
        leaf_mesh_add(vertices,faces,origin,RNG.uniform(.55,.98),RNG.uniform(.32,.61),rot)
        indices.extend([RNG.choices([0,1,2],[.45,.35,.2])[0]]*(len(faces)-before))
    obj=mesh_object(name+' | sculptural leaves',vertices,faces,'foliage')
    obj.data.materials.append(MATS['foliage_light'])
    obj.data.materials.append(MATS['foliage_sage'])
    for face,idx in zip(obj.data.polygons,indices):
        face.material_index=idx
    return obj


def fern(name, pos, size=1):
    vertices, faces = [], []
    p=Vector(pos)
    for frond in range(7):
        angle=frond*math.tau/7+RNG.uniform(-.2,.2)
        forward=Vector((math.cos(angle),math.sin(angle),0))
        across=Vector((-math.sin(angle),math.cos(angle),0))
        length=size*RNG.uniform(.65,1.05)
        for n in range(1,8):
            t=n/8
            stem=p+forward*(length*t*.78)+Vector((0,0,length*(.92*t-.45*t*t)))
            for sign in (-1,1):
                d=(forward*.3+across*sign).normalized()
                rot=Vector((d.x,d.y,.13)).to_track_quat('X','Z')
                leaf_mesh_add(vertices,faces,stem,length*.30*(1-t)*1.5+.04,
                              .12*size*(1-t)+.03,rot)
    obj=mesh_object(name,vertices,faces,'fern')
    obj.data.materials.append(MATS['foliage_light'])
    for face in obj.data.polygons:
        if face.index%5==0:
            face.material_index=1
    return obj


def flowers(pos, size=1, count=7):
    vertices, faces=[],[]
    for i in range(count):
        x=pos[0]+RNG.uniform(-.45,.45)*size
        y=pos[1]+RNG.uniform(-.45,.45)*size
        z=ground_z(x,y)+RNG.uniform(.16,.38)*size
        for petal in range(5):
            angle=petal*math.tau/5
            rot=Euler((0,RNG.uniform(-.35,-.15),angle)).to_quaternion()
            leaf_mesh_add(vertices,faces,(x,y,z),.11*size,.095*size,rot)
    return mesh_object('Woodland wildflowers',vertices,faces,'flowers')


def grass_tufts():
    vertices, faces = [], []
    for i in range(480):
        x,y=RNG.uniform(-9.8,9.8), RNG.uniform(-8.3,11.5)
        if (x/10.4)**2+((y-1.2)/10.7)**2>1 or abs(x)<1.75:
            continue
        if abs(y-river_y(x))<1.2:
            continue
        for j in range(5):
            ox,oy=x+RNG.uniform(-.1,.1),y+RNG.uniform(-.1,.1)
            z=ground_z(ox,oy)
            rot=Euler((0,-RNG.uniform(.65,1.1),RNG.uniform(0,math.tau))).to_quaternion()
            leaf_mesh_add(vertices,faces,(ox,oy,z),RNG.uniform(.16,.38),.055,rot)
    return mesh_object('Groundcover | grouped curved grass blades',vertices,faces,'grass_blades')


def lantern(name, pos, scale=1, light=True):
    x,y,z=pos
    brass='brass'
    cylinder(name+' | hexagonal foot',(x,y,z-.32*scale),.26*scale,.11*scale,brass,6)
    cylinder(name+' | pagoda cap',(x,y,z+.37*scale),.34*scale,.19*scale,brass,6,.13*scale)
    cylinder(name+' | amber glass',(x,y,z+.015*scale),.19*scale,.58*scale,'amber',12)
    for i in range(6):
        a=i*math.tau/6
        dx,dy=.235*math.cos(a)*scale,.235*math.sin(a)*scale
        beam(name+' | wrought frame',(x+dx,y+dy,z-.27*scale),
             (x+dx*.87,y+dy*.87,z+.29*scale),.025*scale,brass,vertices=10)
    ring(name+' | hanging eye',(x,y,z+.54*scale),.085*scale,.021*scale,brass,
         rotation=(math.pi/2,0,0),segments=20)
    if light:
        data=bpy.data.lights.new(name+' | warm pool','POINT')
        data.energy=38*scale
        data.color=(1,.52,.18)
        data.shadow_soft_size=.6*scale
        obj=bpy.data.objects.new(data.name,data)
        bpy.context.collection.objects.link(obj)
        obj.location=(x,y-.02,z)


def lantern_post(x,y,z,side=1):
    organic_tube('Carved forked lantern post',[(x,y,z),(x-.08*side,y,z+1.4),
                  (x+.10*side,y,z+2.7),(x-.25*side,y,z+3.5)],
                 [.16,.135,.10,.045],'wood_light',sides=16)
    organic_tube('Lantern crook',[(x,y,z+2.2),(x+.48*side,y,z+2.95),
                                  (x+.92*side,y,z+3.1)],
                 [.11,.075,.025],'wood_light',sides=12)
    beam('Wrought lantern drop',(x+.85*side,y,z+3.1),(x+.85*side,y,z+2.87),
         .022,'brass',vertices=10)
    lantern('Path lantern',(x+.85*side,y,z+2.34),.8)
    for h in (.45,.52,2.14,2.21):
        ring('Flax binding',(x,y,z+h),.152,.028,'rope',segments=24)


def bridge():
    xmid=0
    y0,y1=-3.75,.75
    for i in range(23):
        t=i/22
        y=y0+(y1-y0)*t
        z=.36+.28*math.sin(math.pi*t)
        obj=bevel_box('Bridge | individually fitted larch plank',
                      (xmid+RNG.uniform(-.025,.025),y,z),
                      (3.8+RNG.uniform(-.12,.12),.184,.16),
                      'wood_plank' if i%4 else 'wood_light',.035)
        obj.rotation_euler.z=RNG.uniform(-.007,.007)
    for x in (-1.76,1.76):
        organic_tube('Bridge | curved load beam',[(x,y0,.2),(x,-2.6,.43),
                      (x,-1.5,.49),(x,-.4,.4),(x,y1,.2)],
                     [.16,.17,.17,.17,.16],'wood_dark',sides=14)
        for y in (y0,-2.25,-.75,y1):
            t=(y-y0)/(y1-y0)
            z=.37+.28*math.sin(math.pi*t)
            beam('Bridge | baluster',(x,y,z-.08),(x,y,z+1.07),.10,'wood_light')
            sphere('Bridge | worn post cap',(x,y,z+1.07),(.14,.14,.12),'wood_light',16,8)
            ring('Bridge | rope lashing',(x,y,z+.82),.112,.027,'rope',segments=20)
        for height,radius in ((1.02,.075),(.55,.027)):
            organic_tube('Bridge | flowing handrail' if height>1 else 'Bridge | lower rope',
                         [(x,y0,.37+height),(x,-2.25,.50+height),
                          (x,-.75,.53+height),(x,y1,.37+height)],
                         [radius]*4,'wood_light' if height>1 else 'rope',sides=12)
    for x in (-1.4,1.4):
        for y in (-3.3,.25):
            beam('Bridge | submerged piles',(x,y,-.65),(x,y,.4),.17,'wood_dark')


def banner(name, center, width=1, height=2):
    x,y,z=center
    vertices,faces=[],[]
    for j in range(15):
        t=j/14
        for i in range(11):
            u=i/10
            px=x+(u-.5)*width
            py=y+.09*math.sin(u*math.tau*1.6+t*3)+.035*t
            pz=z-t*height + max(0,(t-.73)/.27)*abs(u-.5)*width*.75
            vertices.append((px,py,pz))
    for j in range(14):
        for i in range(10):
            a=j*11+i
            faces.append((a,a+1,a+12,a+11))
    obj=mesh_object(name,vertices,faces,'cloth')
    mod=obj.modifiers.new('Heavy woven hem thickness','SOLIDIFY')
    mod.thickness=.018
    beam('Banner crossbar',(x-width*.65,y,z+.04),(x+width*.65,y,z+.04),.065,'wood_light')
    # Pale branching sigil, modeled as raised stitched curves.
    sy=y-.115
    organic_tube('Branching embroidery',[(x,sy,z-.28*height),(x,sy,z-.60*height),
                                        (x,sy,z-.84*height)], [.018]*3,'linen',sides=8,steps=4)
    for sign in (-1,1):
        for frac in (.43,.58):
            organic_tube('Branching embroidery twig',[(x,sy,z-frac*height),
                           (x+sign*width*.15,sy,z-(frac-.07)*height),
                           (x+sign*width*.25,sy,z-(frac-.19)*height)],
                         [.018,.016,.006],'linen',sides=8,steps=3)


def workshop(name, center, width=4.1, depth=3.5, scale=1):
    x,y,z=center
    w,d=width*scale,depth*scale
    # Elevated platform follows the tree's base, with fitted boards and braces.
    for i in range(16):
        xx=x-w*.5+(i+.5)*w/16
        bevel_box(name+' | raised floorboard',(xx,y,z+.2),(w/16-.018,d,.16),
                  'wood_plank' if i%3 else 'wood_light',.035)
    for xx in (x-w*.43,x+w*.43):
        beam(name+' | platform sill',(xx,y-d*.46,z+.02),(xx,y+d*.46,z+.02),.17,'wood_dark')
        for yy in (y-d*.4,y+d*.4):
            beam(name+' | rooted pillar',(xx,yy,z-.5),(xx,yy,z+3.25*scale),.18*scale,
                 'wood_light',.13*scale)
            organic_tube(name+' | curved roof brace',[(xx,yy,z+1.8*scale),
                       (xx+(x-xx)*.35,yy,z+2.95*scale),(x,yy,z+3.5*scale)],
                         [.11,.14,.10],'wood_light',sides=14)
    # Timber back wall, small arched recess, and horizontal display shelves.
    for i in range(14):
        xx=x-w*.44+(i+.5)*w*.88/14
        bevel_box(name+' | back slat',(xx,y+d*.39,z+1.55*scale),
                  (w*.88/14-.025,.12,2.55*scale),'wood_dark',.028)
    for h in (.8,1.45):
        bevel_box(name+' | apothecary shelf',(x,y+d*.26,z+h*scale),
                  (w*.75,.48,.10),'wood_light',.025)
    for i in range(7):
        xx=x+(i-3)*w*.10
        zz=z+1.52*scale
        cylinder(name+' | ceramic herb pot',(xx,y+d*.22,zz+.16),.14,.27,
                 'pot' if i%2 else 'cloth',vertices=20,radius_top=.11)
        sphere(name+' | aromatic herbs',(xx,y+d*.22,zz+.35),(.18,.17,.18),'foliage_sage',16,8)
    # Roof's shallow doubly curved shape and pale bent ribs are hand-authored.
    vertices,faces=[],[]
    for j in range(11):
        v=j/10
        yy=y-d*.66+v*d*1.2
        for i in range(25):
            u=i/24
            xx=x+(u-.5)*w*1.23
            zz=z+(2.70+.80*math.cos((u-.5)*math.pi))*scale
            zz+=.10*math.sin(v*math.pi)-.17*(1-v)
            vertices.append((xx,yy,zz))
    for j in range(10):
        for i in range(24):
            a=j*25+i
            faces.append((a,a+1,a+26,a+25))
    obj=mesh_object(name+' | curved aubergine canopy',vertices,faces,'cloth')
    sol=obj.modifiers.new('Canvas depth','SOLIDIFY')
    sol.thickness=.04
    for yy in (y-d*.66,y+d*.54):
        organic_tube(name+' | steam bent eave',[(x-w*.615,yy,z+2.65*scale),
                     (x-w*.31,yy,z+3.27*scale),(x,yy,z+3.5*scale),
                     (x+w*.31,yy,z+3.27*scale),(x+w*.615,yy,z+2.65*scale)],
                     [.075]*5,'wood_light',sides=14)
    for xx in (x-w*.615,x+w*.615):
        organic_tube(name+' | canopy side binding',[(xx,y-d*.66,z+2.65*scale),
                     (xx,y,z+2.82*scale),(xx,y+d*.54,z+2.82*scale)],
                     [.035]*3,'linen',sides=10)
    # A front scallop and gently sagging cords keep fabric visibly distinct from wood.
    for i in range(8):
        xx=x-w*.55+(i+.5)*w*1.1/8
        zz=z+(2.70+.8*math.cos(((i+.5)/8-.5)*math.pi))*scale-.18
        organic_tube(name+' | fabric hem',[(xx-w*.065,y-d*.67,zz),
                      (xx,y-d*.67,zz-.10),(xx+w*.065,y-d*.67,zz)],
                     [.018,.023,.018],'linen',sides=8,steps=4)
    bevel_box(name+' | carved sales counter',(x,y-d*.39,z+.95*scale),
              (w*.84,.65,.20),'wood_light',.07)
    for i in range(9):
        xx=x-w*.38+(i+.5)*w*.76/9
        bevel_box(name+' | counter face',(xx,y-d*.41,z+.58*scale),
                  (w*.76/9-.025,.16,.65*scale),'wood_plank',.025)
    for xx in (x-w*.42,x+w*.42):
        lantern(name+' | welcome lantern',(xx,y-d*.48,z+2.25*scale),.65)
    for step in range(2):
        bevel_box(name+' | front step',(x,y-d*.52-.32*step,z+.1-.16*step),
                  (w*.58,.45,.18),'wood_plank',.045)
    for dx,dy in [(-w*.47,-d*.15),(w*.49,-d*.1)]:
        barrel(name+' | provisions',(x+dx,y+dy,z+.36),.36,.7)


def barrel(name,pos,radius=.36,height=.7):
    x,y,z=pos
    obj=sphere(name+' | staves',(x,y,z+height*.5),(radius,radius,height*.6),'wood_plank',20,12)
    # The lid and bands retain a practical, crafted silhouette.
    cylinder(name+' | lid',(x,y,z+height*.98),radius*.77,.065,'wood_light',20)
    for h in (.23,.76):
        ring(name+' | forged hoop',(x,y,z+height*h),radius*.97,.035,'brass',segments=24)


def monumental_trees():
    for sign in (-1,1):
        x=sign*4.6
        pts=[(x,6.8,.15),(x+sign*.5,6.6,2.5),(x+sign*.12,6.9,5.1),
             (x-sign*.9,7.4,7.6),(x-sign*1.6,7.8,10.4),(x-sign*.9,8.3,12.2)]
        organic_tube('Ancient guardian | main twisted trunk',pts,
                     [1.55,1.31,1.12,.87,.67,.25],'bark',sides=32,steps=10,ridged=.10)
        inner=[(x+sign*.08,6.7,4.0),(x-sign*1.0,6.7,6.1),
               (x-sign*2.8,7.0,7.6),(sign*.4,7.5,8.3),(-sign*.65,8.05,8.62)]
        organic_tube('Ancient guardian | living pointed arch',inner,
                     [.9,.83,.6,.4,.10],'bark',sides=28,steps=9,ridged=.07)
        branches=[
            ([(x,6.9,5.8),(x+sign*1.7,6.4,7.2),(x+sign*3.7,5.9,8.2),
              (x+sign*4.4,6,9.5)],[.7,.5,.28,.06]),
            ([(x-sign*.4,7.3,8.4),(x+sign*.5,9.5,10.0),(x+sign*2.4,10.4,10.9)],
             [.52,.34,.08]),
            ([(x-sign*1.4,7.9,10.0),(x-sign*3.0,8.7,11.2),
              (x-sign*3.5,9.1,12.4)],[.43,.26,.07]),
        ]
        for b,r in branches:
            organic_tube('Ancient guardian | spreading limb',b,r,'bark',sides=20,ridged=.06)
        for angle,length in [(-2.8,3.4),(-1.9,3.4),(-.9,3.0),(.05,3.3),(.8,2.6),(2.0,3.2)]:
            angle=angle if sign<0 else math.pi-angle
            dx,dy=math.cos(angle),math.sin(angle)
            endx,endy=x+dx*length,6.8+dy*length
            organic_tube('Ancient guardian | buttress root',
                         [(x+dx*.4,6.8+dy*.4,2.0),(x+dx*1.3,6.8+dy*1.3,.8),
                          (x+dx*length*.75,6.8+dy*length*.75,.38),
                          (endx,endy,ground_z(endx,endy)+.02)],
                         [.58,.48,.27,.035],'bark',sides=20,steps=8,ridged=.07)
        # Raised longitudinal flutes read as carved bark, rather than noise alone.
        for k in range(7):
            a=math.tau*k/7
            line=[]
            for j,p in enumerate(pts[:-1]):
                rad=[1.5,1.27,1.08,.84,.64][j]
                line.append((p[0]+math.cos(a+j*.13)*rad,
                             p[1]+math.sin(a+j*.13)*rad,p[2]+.08))
            organic_tube('Ancient guardian | sculpted bark flute',line,
                         [.12,.10,.08,.055,.025],'bark_ridge',sides=10,steps=7,ridged=0)
        banner('Heraldry of the branching grove',(x-sign*.7,5.47,5.9),1.05,2.2)
        # Canopy clumps flank the portal, retaining a clear central sightline.
        foliage=[(x+sign*3.2,6.1,9.3,(2.1,1.8,1.15)),
                 (x+sign*4.1,7.5,10.0,(2.0,1.8,1.1)),
                 (x+sign*.8,9.6,11.6,(2.5,2,1.3)),
                 (x-sign*1.1,8.5,12.5,(2.2,1.8,1.3)),
                 (x-sign*2.7,9.2,12.8,(1.9,1.8,1.0)),
                 (x+sign*1.7,5.0,8.4,(1.6,1.5,.95))]
        for n,(cx,cy,cz,size) in enumerate(foliage):
            canopy('Ancient crown', (cx,cy,cz),size,120)
        for n in range(3):
            canopy('Clinging moss and ivy',(x+RNG.uniform(-.9,.9),5.7,
                                           2.2+n*1.6),(.8,.43,.55),45)
    # A living graft and small ivy cluster integrate the arch's two branches.
    sphere('Ancient arch | natural graft',(0,7.67,8.39),(.57,.45,.32),'bark',24,16)
    canopy('Ancient arch | crown ivy',(.05,7.80,8.72),(.76,.45,.28),55)
    # One slender supporting tree balances the near-left shop.
    organic_tube('Young ash | swaying trunk',[(-8,1.8,.2),(-8.4,2,2.7),
                  (-8.0,2.5,5.0),(-8.9,3,7.0)], [.48,.37,.25,.08],
                 'bark',sides=20,ridged=.07)
    organic_tube('Young ash | fork',[(-8.1,2.3,4.2),(-6.8,2,5.4),(-6.2,1.8,6.8)],
                 [.27,.16,.04],'bark',sides=16)
    canopy('Young ash foliage',(-8.7,2.9,7.1),(1.8,1.7,1.05),115)
    canopy('Young ash foliage',(-6.4,1.9,6.9),(1.5,1.2,.95),95)


def ground_decoration():
    # Deliberate large-to-small groups at path edges, rather than uniform noise.
    rock_groups=[(-3.5,-5.4,1.05),(-7.8,-3.8,1.2),(4.9,-4.7,.95),
                 (7.3,-2.1,1.0),(3.2,3.6,.75),(-3.0,3.9,.65),
                 (-8.1,7.0,1.15),(7.8,8.6,1.25),(-4.0,10.1,.75),(4.0,10.7,.7)]
    for x,y,s in rock_groups:
        rock('Weathered moss boulder',(x,y,ground_z(x,y)+s*.38),
             (s,s*.83,s*.65))
        for k in range(2):
            dx,dy=RNG.uniform(-1,1),RNG.uniform(-.8,.8)
            ss=s*RNG.uniform(.3,.5)
            xx,yy=x+dx,y+dy
            rock('Small bank stone',(xx,yy,ground_z(xx,yy)+ss*.4),(ss,ss*.82,ss*.65),False)
        fern('Royal fern',(x+.7,y-.65,ground_z(x+.7,y-.65)),s*.9)
        flowers((x-.6,y-.7,0),s*.95,8)
    for i in range(22):
        x=RNG.uniform(-9.5,9.5)
        if abs(x)<2.4:
            continue
        y=river_y(x)+RNG.choice([-1,1])*RNG.uniform(1,1.35)
        s=RNG.uniform(.22,.48)
        rock('Stream edge worn pebble',(x,y,ground_z(x,y)+s*.25),(s,s*.8,s*.5),i%3==0)
    for x,y in [(-2.6,-7.6),(2.7,-6.2),(-3.1,-3.7),(4.0,-3.5),
                (-2.6,1.7),(3.2,1.5),(4.3,8.4),(-3.5,8.8),(-8,-.6),(8.2,3)]:
        fern('Lush path fern',(x,y,ground_z(x,y)),RNG.uniform(.75,1.15))
        flowers((x+.3,y+.35,0),1.0,9)
    for y in (-8.6,-7.7,-6.5,-4.8,1.5,2.8,4.1,5.3,8.6,10.0):
        for k in range(2):
            x=RNG.uniform(-1.0,1.0)
            rock('Trail | softened inset stepping stone',
                 (x,y+RNG.uniform(-.1,.1),ground_z(x,y)+.025),
                 (RNG.uniform(.23,.42),RNG.uniform(.15,.28),.055),False)
    grass_tufts()


def ranger():
    # A deliberately simple, fully 3D scale figure, not a production character.
    x,y,z=-.45,-5.6,ground_z(-.45,-5.6)
    for side in (-1,1):
        px=x+side*.14
        beam('Ranger | leggings',(px,y,z+.16),(px,y,z+.94),.11,'ranger_dark',.13)
        bevel_box('Ranger | leather boots',(px,y-.07,z+.12),(.24,.38,.24),'ranger_dark',.075)
    sphere('Ranger | tunic',(x,y,z+1.15),(.34,.23,.44),'ranger_sage')
    sphere('Ranger | hood',(x,y+.02,z+1.76),(.24,.23,.28),'cloth')
    sphere('Ranger | shadowed face',(x,y+.17,z+1.75),(.15,.075,.17),'skin')
    for side in (-1,1):
        beam('Ranger | leather sleeve',(x+side*.26,y,z+1.42),
             (x+side*.40,y+.04,z+1.02),.105,'ranger_sage',.078)
        sphere('Ranger | gloves',(x+side*.42,y+.06,z+.96),(.085,.085,.11),'ranger_dark',16,10)
    # A folded cloth cape; the camera sees its back and the branching emblem.
    vertices,faces=[],[]
    for j in range(15):
        t=j/14
        for i in range(17):
            u=i/16-.5
            vertices.append((x+u*(.50+t*.54),y-.23-.12*t+.04*math.cos(u*math.pi*7),
                             z+1.53-t*1.04+.06*math.cos(u*math.pi*5)*t))
    for j in range(14):
        for i in range(16):
            a=j*17+i
            faces.append((a,a+1,a+18,a+17))
    cape=mesh_object('Ranger | sculpted flowing cape',vertices,faces,'ranger_sage')
    cape.modifiers.new('Cape cloth thickness','SOLIDIFY').thickness=.025
    cylinder('Ranger | leather belt',(x,y,z+1.05),.30,.09,'ranger_dark',24)
    sx=x+.62
    organic_tube('Ranger | forked walking staff',[(sx,y,z+.03),(sx-.07,y,z+.9),
                  (sx,y,z+1.8),(sx-.13,y,z+2.23)], [.035,.042,.03,.012],
                 'wood_light',sides=10)
    organic_tube('Ranger | staff fork',[(sx,y,z+1.75),(sx+.16,y,z+1.99),
                  (sx+.11,y,z+2.23)], [.025,.02,.008],'wood_light',sides=10)
    sphere('Ranger | amber focus',(sx,y,z+2.02),(.075,.07,.13),'amber',16,10)


def camera(name, position, target, focal=48):
    data=bpy.data.cameras.new(name)
    obj=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    obj.location=position
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    data.type='PERSP'
    data.lens=focal
    data.clip_end=250
    return obj


def area_light(name, pos, target, power, color, size):
    data=bpy.data.lights.new(name,'AREA')
    data.energy=power
    data.color=color
    data.shape='DISK'
    data.size=size
    obj=bpy.data.objects.new(name,data)
    bpy.context.collection.objects.link(obj)
    obj.location=pos
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj


def build_scene():
    RNG.seed(SEED)
    MATS.clear()
    specs={
        'bark':((.22,.135,.17),.81,0,.22,None,(2.1,2.1,.24)),
        'bark_ridge':((.32,.225,.23),.80,0,.12,None,(1,1,.14)),
        'wood_light':((.58,.43,.27),.55,0,.14,None,(1.5,1.5,.15)),
        'wood_dark':((.18,.105,.12),.76,0,.18,None,(1,1,.16)),
        'wood_plank':((.38,.25,.16),.66,0,.16,None,(1,1,.15)),
        'grass':((.23,.32,.17),.94,0,.28,None,None),
        'grass_blades':((.32,.40,.19),.85,0,0,None,None),
        'path':((.55,.43,.28),.92,0,.13,None,None),
        'rock':((.30,.31,.35),.83,0,.19,None,None),
        'moss':((.23,.36,.14),.92,0,.15,None,None),
        'foliage_dark':((.055,.16,.12),.8,0,.12,None,None),
        'foliage':((.16,.29,.18),.69,0,.1,None,None),
        'foliage_light':((.32,.43,.22),.66,0,.12,None,None),
        'foliage_sage':((.35,.47,.34),.76,0,.10,None,None),
        'fern':((.16,.36,.22),.69,0,0,None,None),
        'flowers':((.52,.31,.66),.66,0,0,None,None),
        'cloth':((.255,.16,.34),.87,0,.12,None,(6,6,6)),
        'linen':((.72,.60,.37),.8,0,0,None,None),
        'rope':((.43,.34,.20),.93,0,.15,None,None),
        'brass':((.39,.25,.10),.32,.65,.08,None,None),
        'amber':((1,.42,.075),.28,.05,0,((1,.29,.028),3.3),None),
        'pot':((.26,.35,.30),.38,0,.1,None,None),
        'water':((.035,.22,.23),.22,.28,.07,None,None),
        'water_glint':((.34,.60,.51),.25,.2,0,None,None),
        'ranger_sage':((.30,.40,.30),.82,0,.07,None,None),
        'ranger_dark':((.11,.095,.12),.67,0,0,None,None),
        'skin':((.39,.22,.13),.6,0,0,None,None),
        'backdrop':((.12,.19,.19),.85,0,0,None,None),
        'soil_edge':((.18,.16,.18),.86,0,.15,None,None),
    }
    for name,args in specs.items():
        MATS[name]=material(name,*args)
    terrain()
    # Low elliptical foundation gives the study a finished presentation edge.
    base=cylinder('Diorama | rounded earth foundation',(0,1.4,-.85),1,.72,'soil_edge',128)
    base.scale=(10.96,11.38,1)
    base.modifiers['Rounded rims'].width=.13
    bridge()
    monumental_trees()
    workshop('Herbalist atelier',(-5.8,.9,.5),4.1,3.4,1.0)
    workshop('Wayfarer atelier',(5.7,4.0,.65),3.7,3.0,.94)
    lantern_post(-2.5,-4.2,ground_z(-2.5,-4.2),1)
    lantern_post(2.6,2.3,ground_z(2.6,2.3),-1)
    ground_decoration()
    ranger()
    # Small trail marker has the same bifurcation as the architectural landmark.
    organic_tube('Trail marker | forked post',[(3.0,-5.0,.3),(3.1,-5.0,1.4),
                  (3.0,-5.0,2.2)], [.12,.11,.035],'wood_light')
    for h,angle in [(1.35,-.08),(1.75,.08)]:
        bevel_box('Trail marker | carved sign',(3.12,-5.08,h),
                  (1.25,.15,.28),'wood_dark',.10,rotation=(0,angle,0))
    # Calm studio floor is lit as an environment, with no image backdrop.
    bevel_box('Presentation floor',(0,0,-1.32),(2000,2000,.16),'backdrop',.05)
    world=bpy.data.worlds.new('Forest | cool skylight')
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.38,.53,.61,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.40
    bpy.context.scene.world=world
    area_light('Sun through high canopy',(-8,-5,18),(0,3,1),2600,(1,.83,.58),9)
    area_light('Broad cool skylight',(8,-4,13),(0,4,4),1700,(.63,.81,1),11)
    area_light('Warm canopy rim',(2,12,18),(0,5,6),2200,(1,.72,.39),8)
    sun_data=bpy.data.lights.new('Dappled afternoon sun','SUN')
    sun_data.energy=1.5
    sun_data.angle=.20
    sun_data.color=(1,.83,.61)
    sun=bpy.data.objects.new(sun_data.name,sun_data)
    bpy.context.collection.objects.link(sun)
    sun.rotation_euler=(.45,-.58,-.5)
    main=camera('Camera_Main',(22,-34,21),(0,3,4.7),43)
    camera('Camera_Overview',(23,-30,29),(0,2.4,4.2),46)
    camera('Camera_Path',(3.2,-16,3.2),(0,5,4.6),28)
    scene=bpy.context.scene
    scene.camera=main
    scene.render.resolution_x=1600
    scene.render.resolution_y=1000
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.exposure=.25
    scene['Arkya_Art_Direction']='03 — Terres des Anciens'
    scene['Arkya_Description']='Original procedural 3D woodland sanctuary; living tree arch, artisan stalls, creek bridge.'
    scene['Arkya_Generation_Seed']=SEED
    scene['Arkya_Source']='build_terres_anciens.py'
    return {'title':'Terres des Anciens', 'seed':SEED,
            'objects':len(scene.objects), 'main_camera':'Camera_Main',
            'notes':['Fully procedural mesh, material and lighting study.',
                     'Smooth sculptural trees and foliage; no external images or vendor assets.',
                     'The ranger is a scale mannequin, not a final game character.']}
