"""Fitted woodland-ranger adaptation of the credited Rain static asset.

Open assets/rain-ancients.blend, then call refine_hero() and save_asset().
The source asset, source textures, credits and facial meshes are preserved.
No downloaded rig scripts are executed. Front is -Y and the feet are at Z=0.
"""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


BASE=Path(__file__).resolve().parents[1]
OUT=BASE/'assets'/'rain-ancients-v3.blend'
M={}
COLL=None
ROOT=None
TORSO=None
LEGS={}


def link(obj):
    COLL.objects.link(obj)
    obj.parent=ROOT
    return obj


def material(name,color,rough=.65,metal=0,grain=.0004,woven=False):
    mat=bpy.data.materials.new(name)
    mat.diffuse_color=(*color,1)
    mat.use_nodes=True
    nodes,links=mat.node_tree.nodes,mat.node_tree.links
    p=nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=rough
    p.inputs['Metallic'].default_value=metal
    if grain:
        tc=nodes.new('ShaderNodeTexCoord')
        broad=nodes.new('ShaderNodeTexNoise')
        broad.inputs['Scale'].default_value=17
        broad.inputs['Detail'].default_value=3
        links.new(tc.outputs['Object'],broad.inputs['Vector'])
        ramp=nodes.new('ShaderNodeValToRGB')
        ramp.color_ramp.elements[0].color=tuple(c*.68 for c in color)+(1,)
        ramp.color_ramp.elements[1].color=tuple(min(1,c*1.25) for c in color)+(1,)
        links.new(broad.outputs['Fac'],ramp.inputs[0])
        links.new(ramp.outputs[0],p.inputs['Base Color'])
        noise=nodes.new('ShaderNodeTexNoise')
        noise.inputs['Scale'].default_value=360 if woven else 220
        noise.inputs['Detail'].default_value=2
        links.new(tc.outputs['Object'],noise.inputs['Vector'])
        bump=nodes.new('ShaderNodeBump')
        bump.inputs['Strength'].default_value=.28 if woven else .19
        bump.inputs['Distance'].default_value=grain
        links.new(noise.outputs['Fac'],bump.inputs['Height'])
        links.new(bump.outputs[0],p.inputs['Normal'])
    if woven:
        p.inputs['Sheen Weight'].default_value=.35
        p.inputs['Sheen Roughness'].default_value=.7
    M[name]=mat
    return mat


def mesh(name,vertices,faces,mat,subdiv=1,thickness=.003):
    data=bpy.data.meshes.new(name+'Mesh')
    data.from_pydata(vertices,[],faces)
    data.materials.append(M.get(mat,mat))
    data.update()
    obj=link(bpy.data.objects.new(name,data))
    for face in data.polygons:face.use_smooth=True
    if subdiv:
        sub=obj.modifiers.new('Tailored smooth surface','SUBSURF')
        sub.levels=sub.render_levels=subdiv
    if thickness:
        solid=obj.modifiers.new('Real leather and cloth thickness','SOLIDIFY')
        solid.thickness=thickness
        solid.offset=0
    return obj


def curves(name,paths,radius,mat):
    data=bpy.data.curves.new(name,'CURVE')
    data.dimensions='3D'
    data.resolution_u=8
    data.bevel_depth=radius
    data.bevel_resolution=3
    for path in paths:
        if len(path)<2:continue
        spline=data.splines.new('POLY')
        spline.points.add(len(path)-1)
        for p,co in zip(spline.points,path):p.co=(*co,1)
    data.materials.append(M.get(mat,mat))
    return link(bpy.data.objects.new(name,data))


def tube(name,path,radius,mat):
    return curves(name,[path],radius,mat)


def sphere(name,loc,scale,mat):
    verts,faces=[],[]
    u,v=24,14
    for i in range(v+1):
        theta=math.pi*i/v
        for j in range(u):
            a=math.tau*j/u
            verts.append((loc[0]+scale[0]*math.sin(theta)*math.cos(a),
                          loc[1]+scale[1]*math.sin(theta)*math.sin(a),
                          loc[2]+scale[2]*math.cos(theta)))
    for i in range(v):
        for j in range(u):
            a=i*u+j;b=i*u+(j+1)%u
            faces.append((a,b,b+u,a+u))
    return mesh(name,verts,faces,mat,0,0)


def rounded_box(name,loc,size,mat,bevel=.007):
    x,y,z=(s/2 for s in size)
    v=[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
       (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)]
    f=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    obj=mesh(name,v,f,mat,0,0)
    obj.location=loc
    mod=obj.modifiers.new('Soft leather corners','BEVEL');mod.width=bevel;mod.segments=4
    obj.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return obj


def outline_stitches(path,step=.008,inset=0):
    segments=[]
    for i in range(len(path)-1):
        a,b=Vector(path[i]),Vector(path[i+1])
        length=(b-a).length
        if not length:continue
        count=max(1,int(length/step))
        for j in range(count):
            t=(j+.15)/count
            u=min(1,(j+.60)/count)
            segments.append([tuple(a.lerp(b,t)),tuple(a.lerp(b,u))])
    return segments


def torus_front(name,center,radius,minor,mat):
    points=[(center[0]+radius*math.cos(i*math.tau/24),center[1],
             center[2]+radius*math.sin(i*math.tau/24)) for i in range(25)]
    return tube(name,points,minor,mat)


def normalize_original_meshes():
    for obj in list(COLL.objects):
        if obj.type!='MESH':continue
        matrix=obj.matrix_world.copy()
        obj.data=obj.data.copy()
        for vertex in obj.data.vertices:vertex.co=matrix@vertex.co
        obj.matrix_world=Matrix.Identity(4)
        obj.parent=ROOT


def remove_old_costume():
    prefixes=('Ranger fitted leather corset','Waist belt','Belt stitched rim','Amber belt clasp',
              'Layered tunic panel','Tunic embroidered hem','Plum ranger mantle',
              'Mantle bronze piping','Leaf mantle clasp','Amber clasp stone',
              'High leather boot','Turned boot cuff','Boot central stitch')
    for obj in list(COLL.objects):
        if obj.name.startswith(prefixes) or obj.name=='Rain_shoes':
            bpy.data.objects.remove(obj,do_unlink=True)


def mature_proportions():
    head_names=('Rain_head','Rain_eye','Rain_hair','Rain_gums','Rain_tongue')
    anchor=Vector((0,-.01,1.45))
    for obj in COLL.objects:
        if obj.type!='MESH' or not (obj.name.startswith(head_names) or obj.name=='Rain_body'):
            continue
        for v in obj.data.vertices:
            t=max(0,min(1,(v.co.z-1.435)/.085))
            t=t*t*(3-2*t)
            v.co=anchor+(v.co-anchor)*(1-.10*t)
    skin=bpy.data.materials.get('MAT-rain.body')
    if skin and skin.node_tree:
        p=skin.node_tree.nodes.get('Principled BSDF')
        socket=p.inputs['Base Color']
        if socket.is_linked:
            source=socket.links[0].from_socket
            tint=skin.node_tree.nodes.new('ShaderNodeHueSaturation')
            tint.name='Arkya V3 warm natural complexion'
            tint.inputs['Hue'].default_value=.5
            tint.inputs['Saturation'].default_value=.95
            tint.inputs['Value'].default_value=.91
            skin.node_tree.links.new(source,tint.inputs['Color'])
            skin.node_tree.links.new(tint.outputs['Color'],socket)


def bvh_for(obj,side=None):
    vertices=[v.co.copy() for v in obj.data.vertices]
    polygons=[tuple(p.vertices) for p in obj.data.polygons
              if side is None or sum(vertices[i].x for i in p.vertices)/len(p.vertices)*side>0]
    return BVHTree.FromPolygons(vertices,polygons,all_triangles=False)


def torso_point(a,z,offset=.008):
    direction=Vector((math.sin(a),-math.cos(a),0))
    center=Vector((0,-.025,z))
    hit,normal,index,distance=TORSO.ray_cast(center+direction*.55,-direction,1.1)
    if hit is None:
        rx=.13 if z>1.1 else .16
        hit=center+Vector((math.sin(a)*rx,-math.cos(a)*.09,0))
    return hit+direction*offset


def front_y(x,z,offset=.008):
    hit,normal,index,distance=TORSO.ray_cast(Vector((x,-.8,z)),Vector((0,1,0)),1.6)
    return (hit.y if hit is not None else -.105)-offset


def vest():
    vertices,faces=[],[]
    na,nz=96,19
    for j in range(nz):
        t=j/(nz-1)
        for i in range(na+1):
            a=.12+(math.tau-.24)*i/na
            aa=min(a,math.tau-a)
            keys=[(0,1.285),(.42,1.36),(.76,1.425),(1.05,1.405),
                  (1.57,1.30),(2.0,1.36),(math.pi,1.40)]
            top=keys[-1][1]
            for k in range(len(keys)-1):
                if keys[k][0]<=aa<=keys[k+1][0]:
                    w=(aa-keys[k][0])/(keys[k+1][0]-keys[k][0])
                    top=keys[k][1]*(1-w)+keys[k+1][1]*w;break
            bottom=1.015-.018*abs(math.sin(a*2))
            z=bottom*(1-t)+top*t
            vertices.append(tuple(torso_point(a,z,.0085)))
    for j in range(nz-1):
        for i in range(na):
            a=j*(na+1)+i
            faces.append((a,a+1,a+na+2,a+na+1))
    mesh('V3 Ranger | fitted supple leather vest',vertices,faces,'leather',1,.004)
    borders=[]
    for j in (0,nz-1):borders.append([vertices[j*(na+1)+i] for i in range(na+1)])
    for i in (0,na):borders.append([vertices[j*(na+1)+i] for j in range(nz)])
    curves('V3 Ranger | vest bound leather edges',borders,.0026,'leather_dark')
    stitches=[]
    for path in borders:stitches+=outline_stitches(path,.008)
    curves('V3 Ranger | hand stitched vest seams',stitches,.0007,'thread')
    # Embossed vertical panel seams follow the real waist rather than a cylinder.
    for angle in (.55,1.1,2.3,math.tau-.55,math.tau-1.1,math.tau-2.3):
        path=[tuple(torso_point(angle,1.04+i*.255/24,.012)) for i in range(25)]
        tube('V3 Ranger | fitted panel piping',path,.0015,'leather_edge')
    # A narrow opening, metal eyelets and crossed waxed cord fasten the vest.
    left,right=[],[]
    for i in range(7):
        z=1.04+i*.035
        p1=torso_point(-.14,z,.016);p2=torso_point(.14,z,.016)
        p1.y-=.003;p2.y-=.003
        left.append(p1);right.append(p2)
        torus_front('V3 Ranger | bronze lacing eyelet',p1,.0048,.0012,'bronze')
        torus_front('V3 Ranger | bronze lacing eyelet',p2,.0048,.0012,'bronze')
    paths=[]
    for i in range(6):
        for a,b in [(left[i],right[i+1]),(right[i],left[i+1])]:
            mid=(a+b)/2;mid.y-=.004
            paths.append([tuple(a),tuple(mid),tuple(b)])
    curves('V3 Ranger | crossed waxed front lacing',paths,.00135,'thread_dark')


def belt_and_strap():
    verts,faces=[],[]
    for z in (1.005,1.008,1.040,1.043):
        for i in range(97):verts.append(tuple(torso_point(i*math.tau/96,z,.017)))
    for j in range(3):
        for i in range(96):
            a=j*97+i;faces.append((a,a+1,a+98,a+97))
    mesh('V3 Ranger | close fitted waist belt',verts,faces,'leather_dark',1,.004)
    for z in (1.010,1.038):
        path=[tuple(torso_point(i*math.tau/96,z,.020)) for i in range(97)]
        curves('V3 Ranger | saddle stitched belt',outline_stitches(path,.009),.0008,'thread')
    y=front_y(0,1.025,.025)
    buckle=[(-.026,y,1.005),(.026,y,1.005),(.028,y,1.046),(-.028,y,1.046),(-.026,y,1.005)]
    tube('V3 Ranger | forged belt buckle',buckle,.0032,'bronze')
    tube('V3 Ranger | buckle tongue',[(0,y-.003,1.024),(.024,y-.003,1.024)],.0018,'bronze')
    # Fitted diagonal ribbon carries actual equipment weight over the shoulder.
    vertices,faces=[],[]
    n=55
    for i in range(n):
        t=i/(n-1)
        z=1.432-.402*t
        x=-.101+.257*t
        for s in (-1,1):
            xx=x+s*.017
            vertices.append((xx,front_y(xx,z,.022),z))
    for i in range(n-1):
        a=i*2;faces.append((a,a+1,a+3,a+2))
    mesh('V3 Ranger | diagonal carrying strap',vertices,faces,'leather_dark',1,.006)
    for s in (0,1):
        path=[vertices[i*2+s] for i in range(n)]
        curves('V3 Ranger | strap hand stitches',outline_stitches(path,.009),.00075,'thread')
    # Small brass adjustment buckle sits flush against the diagonal band.
    z=1.235;x=-.101+.257*((1.432-z)/.402);y=front_y(x,z,.028)
    frame=[(x-.022,y,z-.022),(x+.022,y,z-.022),(x+.022,y,z+.022),
           (x-.022,y,z+.022),(x-.022,y,z-.022)]
    tube('V3 Ranger | carrying strap buckle',frame,.0025,'bronze')
    for sign,z in [(-1,1.015),(1,1.002)]:
        x=sign*.158;y=-.052
        pouch=rounded_box('V3 Ranger | fitted herb and tool pouch',(x,y-.018,z-.055),
                          (.085,.061,.115),'leather',.014)
        # Flap drapes over a rounded pouch rather than hovering in front.
        flap=rounded_box('V3 Ranger | pouch closure flap',(x,y-.052,z-.021),
                         (.086,.012,.060),'leather_dark',.012)
        sphere('V3 Ranger | pouch brass stud',(x,y-.060,z-.032),(.005,.003,.005),'bronze')
        outline=[(x-.033,y-.060,z+.003),(x+.033,y-.060,z+.003),
                 (x+.033,y-.060,z-.043),(x-.033,y-.060,z-.043)]
        curves('V3 Ranger | stitched pouch flap',outline_stitches(outline,.008),.0007,'thread')


def leaf_plate(name,side,origin,length,width,drop,mat,trim=True):
    x,y,z=origin
    vertices,faces=[],[]
    n,m=19,9
    for i in range(n):
        t=i/(n-1)
        w=width*(math.sin(math.pi*t)**.65)
        for j in range(m):
            s=(j/(m-1)-.5)*2
            vertices.append((x+side*length*t,
                              y+s*w*.5,
                              z-drop*t+.026*math.sin(math.pi*t)*(1-.75*s*s)))
    for i in range(n-1):
        for j in range(m-1):
            a=i*m+j;faces.append((a,a+1,a+1+m,a+m))
    mesh(name,vertices,faces,mat,1,.005)
    if trim:
        outline=[vertices[i*m] for i in range(n)]+[vertices[i*m+m-1] for i in range(n-1,-1,-1)]
        tube(name+' | narrow bronze binding',outline,.0018,'bronze')
        curves(name+' | seam stitches',outline_stitches(outline,.008),.00065,'thread')
    ridge=[vertices[i*m+m//2] for i in range(n)]
    tube(name+' | raised leaf midrib',ridge,.0025,'leather_edge')
    for i in (6,10,14):
        for edge in (0,m-1):
            tube(name+' | branching embossed vein',
                 [vertices[(i-2)*m+m//2],vertices[i*m+edge]],.0011,'leather_edge')


def pauldrons():
    for side in (-1,1):
        length=.145 if side==1 else .12
        leaf_plate('V3 Ranger | sculpted leaf shoulder cap',side,
                   (side*.095,-.008,1.451),length,.158,.082,'leather_green')
        leaf_plate('V3 Ranger | overlapping forward shoulder leaf',side,
                   (side*.128,-.043,1.416),length*.86,.115,.102,'leather')
        if side==1:
            leaf_plate('V3 Ranger | third asymmetric shoulder lamella',side,
                       (side*.166,.012,1.384),.105,.104,.108,'leather_green')
        # A visible fastening stud makes the overlapping plates read as equipment.
        sphere('V3 Ranger | shoulder leather rivet',(side*.148,-.061,1.434),
               (.005,.004,.005),'bronze')


def leg_point(side,a,z,offset=.008):
    centerx=side*(.080+.015*max(0,min(1,(z-.16)/.24)))
    cy=.007-.025*max(0,min(1,(z-.36)/.15))
    direction=Vector((math.sin(a),-math.cos(a),0))
    center=Vector((centerx,cy,z))
    hit,n,index,distance=LEGS[side].ray_cast(center+direction*.18,-direction,.36)
    if hit is None:
        rx=.038+.017*max(0,min(1,(z-.16)/.20))
        ry=.039+.017*max(0,min(1,(z-.16)/.20))
        hit=center+Vector((direction.x*rx,direction.y*ry,0))
    return hit+direction*offset


def foot(side):
    cx=side*.087
    profiles=[(-.230,.001,.050,.001),(-.223,.028,.049,.026),
              (-.201,.050,.053,.038),(-.151,.056,.059,.045),
              (-.092,.052,.072,.057),(-.030,.047,.080,.068),
              (.031,.043,.073,.060),(.052,.031,.067,.049),(.055,.007,.058,.025)]
    verts,faces=[],[]
    n=48
    for y,rx,cz,rz in profiles:
        for i in range(n):
            a=math.tau*i/n
            z=max(.013,cz+rz*math.cos(a))
            verts.append((cx+rx*math.sin(a),y,z))
    for j in range(len(profiles)-1):
        for i in range(n):
            a=j*n+i;b=j*n+(i+1)%n
            faces.append((a,b,b+n,a+n))
    faces.append(tuple(range(n-1,-1,-1)))
    faces.append(tuple((len(profiles)-1)*n+i for i in range(n)))
    mesh('V3 Ranger | sculpted continuous leather boot foot',verts,faces,'leather',2,0)
    # A thin layered welt and sole replace the original modern sneaker profile.
    outline=[]
    for y,rx,cz,rz in profiles[1:-1]:outline.append((cx-rx-.001,y,.016))
    for y,rx,cz,rz in reversed(profiles[1:-1]):outline.append((cx+rx+.001,y,.016))
    outline.append(outline[0])
    tube('V3 Ranger | sewn leather sole welt',outline,.0025,'leather_dark')
    vertices=[]
    for z in (.001,.018):
        vertices.extend((x,y,z) for x,y,unused in outline[:-1])
    n=len(outline)-1
    faces=[tuple(range(n-1,-1,-1)),tuple(range(n,n*2))]
    faces.extend((i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n))
    mesh('V3 Ranger | dark layered leather sole',vertices,faces,'sole',2,0)
    # Toe cap is a thin upper surface, with a shaped seam over the instep.
    verts,faces=[],[]
    front=profiles[:5]
    for y,rx,cz,rz in front:
        for j in range(17):
            a=-math.pi/2+math.pi*j/16
            verts.append((cx+rx*math.sin(a),y-.001,cz+rz*math.cos(a)+.004))
    for i in range(len(front)-1):
        for j in range(16):
            a=i*17+j;faces.append((a,a+1,a+18,a+17))
    mesh('V3 Ranger | fitted worn leather toe cap',verts,faces,'leather',1,.002)
    path=verts[-17:]
    tube('V3 Ranger | boot toe cap bound seam',path,.0016,'leather_edge')
    curves('V3 Ranger | toe hand stitches',outline_stitches(path,.008),.00075,'thread')


def fitted_bracers():
    body=bvh_for(bpy.data.objects['Rain_body'])
    for obj in list(COLL.objects):
        if not obj.name.startswith('Leather forearm bracer'):continue
        for vertex in obj.data.vertices:
            hit,normal,index,distance=body.find_nearest(vertex.co)
            if hit is not None and distance<.09:
                vertex.co=hit+normal*.009
        # The existing cuffs now follow the actual posed arms, with visible seams.
        count=65
        for ring in (0,3):
            path=[tuple(obj.data.vertices[ring*count+i].co) for i in range(count)]
            tube('V3 Ranger | fitted bracer bound rim',path,.002,'leather_dark')
            curves('V3 Ranger | fine bracer hand stitches',outline_stitches(path,.008),.0007,'thread')


def boots():
    for side in (-1,1):
        foot(side)
        verts,faces=[],[]
        zs=[.10,.115,.14,.19,.25,.32,.39,.46,.49,.498]
        n=64
        for z in zs:
            for i in range(n+1):verts.append(tuple(leg_point(side,i*math.tau/n,z,.009)))
        for j in range(len(zs)-1):
            for i in range(n):
                a=j*(n+1)+i;faces.append((a,a+1,a+n+2,a+n+1))
        mesh('V3 Ranger | anatomically fitted boot shaft',verts,faces,'leather',1,.004)
        for a in (-.55,.55,math.pi):
            path=[tuple(leg_point(side,a,.13+i*.35/30,.012)) for i in range(31)]
            curves('V3 Ranger | curved boot construction seam',outline_stitches(path,.009),.00075,'thread')
        for z in (.205,.410):
            verts,faces=[],[]
            for zz in (z-.013,z-.011,z+.011,z+.013):
                for i in range(65):verts.append(tuple(leg_point(side,i*math.tau/64,zz,.016)))
            for j in range(3):
                for i in range(64):
                    a=j*65+i;faces.append((a,a+1,a+66,a+65))
            mesh('V3 Ranger | fitted boot fastening strap',verts,faces,'leather_dark',1,.003)
            p=leg_point(side,side*.82,z,.022)
            frame=[(p.x-.013,p.y-.002,z-.012),(p.x+.013,p.y-.002,z-.012),
                   (p.x+.013,p.y-.002,z+.012),(p.x-.013,p.y-.002,z+.012),
                   (p.x-.013,p.y-.002,z-.012)]
            tube('V3 Ranger | small shin strap buckle',frame,.002,'bronze')
        # Curved leaf greave follows the changing shin radius and centre.
        vertices,faces=[],[]
        n,m=27,13
        for i in range(n):
            t=i/(n-1)
            z=.15+t*.39
            span=.72*math.sin(math.pi*t)**.57
            for j in range(m):
                a=(j/(m-1)-.5)*2*span
                p=leg_point(side,a,z,.017)
                p.y-=.008*math.sin(math.pi*t)*(1-abs(j/(m-1)-.5)*2)
                vertices.append(tuple(p))
        for i in range(n-1):
            for j in range(m-1):
                a=i*m+j;faces.append((a,a+1,a+m+1,a+m))
        mesh('V3 Ranger | pointed sculpted leather shin guard',vertices,faces,'leather_green',1,.004)
        outline=[vertices[i*m] for i in range(n)]+[vertices[i*m+m-1] for i in range(n-1,-1,-1)]
        tube('V3 Ranger | shin guard bound border',outline,.0015,'bronze')
        curves('V3 Ranger | stitched shin guard',outline_stitches(outline,.009),.0007,'thread')
        tube('V3 Ranger | embossed shin leaf midrib',[vertices[i*m+m//2] for i in range(n)],.002,'leather_edge')


def cloak_point(t,a):
    wave=(.007+.012*t)*math.sin(a*8+.7*t)+.004*t*math.sin(a*15-2*t)
    rx=.145+.165*t+wave
    ry=.09+.15*t+wave*.65
    z=1.445-.86*t-.045*math.sin(a)**2*(1-t)
    z+=t*t*(.025*math.cos(a*3)+.009*math.sin(a*10))
    return (rx*math.sin(a),-.015+.04*t-ry*math.cos(a),z)


def cloak():
    n,m=47,83
    vertices,faces=[],[]
    for i in range(n):
        t=i/(n-1)
        for j in range(m):
            a=1.10+(math.tau-2.20)*j/(m-1)
            vertices.append(cloak_point(t,a))
    for i in range(n-1):
        for j in range(m-1):
            a=i*m+j;faces.append((a,a+1,a+m+1,a+m))
    mesh('V3 Ranger | tailored folded plum travel cloak',vertices,faces,'plum',2,.0045)
    # A real second cloth strip forms the border, followed by small sewn stitches.
    for a0 in (1.10,math.tau-1.10-.05):
        vv,ff=[],[]
        for i in range(49):
            t=i/48
            for j in range(3):
                a=a0+j*.025
                p=Vector(cloak_point(t,a));p.y+=.002
                vv.append(tuple(p))
        for i in range(48):
            for j in range(2):
                q=i*3+j;ff.append((q,q+1,q+4,q+3))
        mesh('V3 Ranger | woven cloak side border',vv,ff,'plum_border',1,.001)
    hem=[]
    for j in range(161):
        a=1.1+(math.tau-2.2)*j/160
        p=Vector(cloak_point(.985,a));p.y+=.003
        hem.append(tuple(p))
    tube('V3 Ranger | cloak hand rolled hem',hem,.0032,'plum_border')
    curves('V3 Ranger | blanket stitched cloak hem',outline_stitches(hem,.012),.0008,'thread')
    for a in (1.125,math.tau-1.125):
        path=[tuple(Vector(cloak_point(i/100,a))+Vector((0,.003,0))) for i in range(101)]
        curves('V3 Ranger | side border blanket stitches',outline_stitches(path,.012),.0008,'thread')
    # Restrained branching embroidery on the back is legible from a game camera.
    def back(t,a=math.pi):
        p=Vector(cloak_point(t,a));p.y+=.006;return tuple(p)
    tube('V3 Ranger | embroidered ancient stem',[back(.28+i*.47/32) for i in range(33)],.0020,'thread')
    for t in (.43,.55,.67):
        for sign in (-1,1):
            tube('V3 Ranger | embroidered branch',
                 [back(t),back(t-.05,math.pi+sign*.12),back(t-.12,math.pi+sign*.22)],.0018,'thread')
    sphere('V3 Ranger | modest leaf cloak fastening',(.105,-.086,1.408),(.019,.009,.028),'bronze')
    sphere('V3 Ranger | inset amber fastening',(.105,-.094,1.408),(.010,.004,.016),'amber')


def tunic_tails():
    panels=[(-.60,.52,.65),(.60,.52,.62),(-1.55,.64,.67),(1.55,.64,.69),
            (-2.65,.61,.64),(2.65,.61,.67)]
    for center,half,lower in panels:
        vertices,faces=[],[]
        n,m=24,19
        for i in range(n):
            t=i/(n-1)
            for j in range(m):
                u=(j/(m-1)-.5)*2
                a=center+u*half*(1-.10*t)
                rx=.176+.075*t+.004*math.sin(u*4)*t
                ry=.124+.065*t
                z=1.015-(1.015-lower)*t+.07*abs(u)**1.6*t*t
                vertices.append((rx*math.sin(a),-.018+.018*t-ry*math.cos(a),z))
        for i in range(n-1):
            for j in range(m-1):
                a=i*m+j;faces.append((a,a+1,a+m+1,a+m))
        mesh('V3 Ranger | overlapping pointed woven tunic tail',vertices,faces,'sage',1,.003)
        border=[vertices[i*m] for i in range(n)]+vertices[-m:]+[vertices[i*m+m-1] for i in range(n-1,-1,-1)]
        tube('V3 Ranger | tunic bound embroidered edge',border,.0018,'sage_border')
        curves('V3 Ranger | tunic fine stitches',outline_stitches(border,.012),.00065,'thread')


def setup_materials():
    material('leather',(.115,.047,.020),.56,grain=.00045)
    material('leather_dark',(.043,.022,.014),.62,grain=.0005)
    material('leather_edge',(.205,.105,.041),.57,grain=.0002)
    material('leather_green',(.050,.070,.039),.57,grain=.0005)
    material('bronze',(.34,.20,.067),.31,.72,0)
    material('thread',(.39,.29,.145),.78,grain=0)
    material('thread_dark',(.055,.035,.018),.82,grain=0)
    material('sole',(.018,.016,.012),.85,grain=.0004)
    material('sage',(.073,.135,.073),.80,grain=.00045,woven=True)
    material('sage_border',(.025,.052,.029),.76,grain=.0003,woven=True)
    material('plum',(.105,.027,.069),.82,grain=.0004,woven=True)
    material('plum_border',(.046,.017,.037),.82,grain=.0004,woven=True)
    material('amber',(.48,.19,.027),.20,.18,0)
    for obj in COLL.objects:
        if obj.name=='Rain_top':obj.data.materials.clear();obj.data.materials.append(M['sage'])
        if obj.name=='Rain_scarf':obj.data.materials.clear();obj.data.materials.append(M['plum'])
        if obj.name.startswith('Leather forearm bracer'):
            obj.data.materials.clear();obj.data.materials.append(M['leather'])


def refine_hero():
    global ROOT,COLL,TORSO,LEGS
    COLL=bpy.data.collections['Arkya_Hero']
    ROOT=COLL.objects['Hero_Root']
    if ROOT.get('Arkya_ranger_revision')==3:
        raise RuntimeError('Open the preserved v2 asset before running this refinement again.')
    normalize_original_meshes()
    remove_old_costume()
    mature_proportions()
    setup_materials()
    TORSO=bvh_for(bpy.data.objects['Rain_top'])
    LEGS={side:bvh_for(bpy.data.objects['Rain_jeans'],side) for side in (-1,1)}
    vest()
    tunic_tails()
    belt_and_strap()
    pauldrons()
    boots()
    fitted_bracers()
    cloak()
    ROOT['Arkya_ranger_revision']=3
    ROOT['Arkya_ranger_revision_notes']='Fitted laced vest, leaf pauldrons, carrying equipment, sculpted boots, woven stitched cloak; head 10% smaller with blended neck.'
    bpy.context.scene['asset_description']='Detailed static forest-ranger study, adapted from credited Blender Studio Rain. Revision 3; not a game-ready rig.'
    return {'objects':len(COLL.objects),'head_scale':.90,'root':ROOT.name,
            'collection':COLL.name,'source_asset':'rain-ancients.blend',
            'output_asset':str(OUT),'credits_preserved':bool(ROOT.get('credit'))}


def save_asset():
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT),compress=True)
    return str(OUT)


def portrait(path,cpu=True):
    world=bpy.data.worlds.new('V3 Rain review studio')
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.055,.075,.10,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
    scene=bpy.context.scene;scene.world=world
    for loc,power,size,color in [((2,-3.5,3.5),300,3,(1,.86,.7)),
                                ((-3,-1,2.2),180,3,(.68,.83,1)),
                                ((1,2.5,3),380,2,(1,.78,.48))]:
        data=bpy.data.lights.new('V3 review softbox','AREA')
        data.energy=power;data.shape='DISK';data.size=size;data.color=color
        obj=bpy.data.objects.new(data.name,data);scene.collection.objects.link(obj)
        obj.location=loc;obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('V3 review portrait')
    data.lens=75
    obj=bpy.data.objects.new(data.name,data);scene.collection.objects.link(obj)
    obj.location=(1.8,-5,2.15)
    obj.rotation_euler=(Vector((0,0,1.03))-obj.location).to_track_quat('-Z','Y').to_euler()
    scene.camera=obj
    scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.cycles.use_denoising=True;scene.cycles.device='CPU'
    scene.render.threads_mode='FIXED';scene.render.threads=8
    if not cpu:
        prefs=bpy.context.preferences.addons['cycles'].preferences
        prefs.compute_device_type='OPTIX';prefs.refresh_devices()
        for d in prefs.devices:d.use=d.type=='OPTIX'
        scene.cycles.device='GPU'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.render.resolution_x=720;scene.render.resolution_y=960
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)


if __name__=='__main__':
    sys.dont_write_bytecode=True
    parser=argparse.ArgumentParser()
    parser.add_argument('--draft',action='store_true')
    parser.add_argument('--gpu',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    print('RAIN_V3_REFINEMENT',refine_hero(),flush=True)
    print('RAIN_V3_SAVED',save_asset(),flush=True)
    if args.draft:
        portrait(Path('D:/Tools/BlenderPortable/arkya-previews/rain-ranger-v3-draft.png'),not args.gpu)
