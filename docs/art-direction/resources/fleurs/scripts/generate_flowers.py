"""Original sculpted flowers and painted 2K atlases for Arkya's approved vegetation DA."""
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
SIZE = 2048
SHAPES = {
    'rosette': {'label': 'Rosette basse', 'height': .20, 'description': 'Fleur basse à cinq pétales en coupe, bouton latéral et feuilles en rosette.'},
    'bouquet': {'label': 'Bouquet de prairie', 'height': .45, 'description': 'Quatre fleurs de hauteurs et orientations différentes, une tige en bouton et feuilles alternées.'},
    'clochettes': {'label': 'Hampe de clochettes', 'height': .70, 'description': 'Hampe arquée portant quatre corolles pendantes, boutons terminaux et longues feuilles.'},
    'etoilee': {'label': 'Grand lis étoilé', 'height': 1.00, 'description': 'Grande corolle à six pétales recourbés, étamines saillantes et deux fleurs satellites.'},
}
PALETTES = {
    'corail': {'label': 'Corail abricot', 'swatch': '#dc716c', 'petal': [(.42,.095,.16),(.83,.25,.31),(.99,.57,.43)], 'leaf':[(.07,.17,.10),(.24,.39,.17),(.53,.65,.29)], 'heart':(.88,.48,.13)},
    'ivoire': {'label': 'Ivoire doré', 'swatch': '#e8ce8d', 'petal':[(.44,.29,.13),(.88,.74,.43),(.99,.94,.74)], 'leaf':[(.09,.17,.10),(.27,.39,.18),(.55,.63,.30)], 'heart':(.77,.40,.075)},
    'mystique': {'label': 'Mystique', 'swatch': '#a39bdb', 'petal':[(.21,.105,.36),(.55,.36,.71),(.55,.77,.82)], 'leaf':[(.10,.115,.20),(.17,.34,.32),(.34,.58,.48)], 'heart':(.33,.80,.67)},
}


def write_image(path, pixels, noncolor=False):
    image=bpy.data.images.new(path.stem,width=pixels.shape[1],height=pixels.shape[0],alpha=False)
    image.colorspace_settings.name='Non-Color' if noncolor else 'sRGB'
    image.pixels.foreach_set(pixels.astype(np.float32).ravel())
    image.filepath_raw=str(path);image.file_format='PNG';image.save();image.pack()
    return image


def paint(palette,force=False):
    folder=ROOT/'textures'/palette;folder.mkdir(parents=True,exist_ok=True)
    paths=[folder/(palette+'-'+suffix+'.png') for suffix in ('basecolor','roughness','emission')]
    if not force and all(p.exists() for p in paths):return
    x,y=np.meshgrid((np.arange(SIZE,dtype=np.float32)+.5)/SIZE,(np.arange(SIZE,dtype=np.float32)+.5)/SIZE)
    u=(x*4)%1;v=(y*4)%1;tile=np.floor(x*4).astype(int)+np.floor(y*4).astype(int)*4
    side=(u-.5)*2;pal=PALETTES[palette]
    rgb=np.zeros((SIZE,SIZE,3),dtype=np.float32);rough=np.full((SIZE,SIZE),.64,dtype=np.float32);emission=np.zeros_like(rgb)
    petal=(tile<6)|(tile==13)|(tile==12)
    # Large translucent-looking painted glazes are baked colour, not transparency.
    glaze=.035*np.sin(v*7.0+u*3.4+tile*.71)+.018*np.cos(u*8-v*3+tile)
    light=np.clip(.27+.43*v+.20*(1-np.abs(side))+glaze,0,1)
    dark,mid,tip=[np.array(c,dtype=np.float32) for c in pal['petal']]
    f=np.clip(light*1.5,0,1)[...,None]
    color=dark*(1-f)+mid*f
    edge=np.clip((v-.43)*1.1+.09*np.sin(u*7+tile),0,.65)[...,None]
    color=color*(1-edge)+tip*edge
    # A warm throat sits under the sculpted heart and spreads into each petal.
    warm=np.array((.96,.54,.25) if palette=='corail' else ((.89,.64,.25) if palette=='ivoire' else (.33,.65,.70)))
    throat=np.exp(-((v-.08)/.18)**2)[...,None]*.54
    color=color*(1-throat)+warm*throat
    vein=np.exp(-((u-.5)/.017)**2)*(.035+.025*v)
    for centre in (.29,.71):
        line=centre+(.5-v)*(.08 if centre<.5 else -.08)
        vein+=np.exp(-((u-line)/.012)**2)*.018*np.sin(v*math.pi)
    color*=1+vein[...,None]
    rgb[petal]=color[petal]
    leaf=(tile>=6)&(tile<=9)
    a,b,c=[np.array(co) for co in pal['leaf']]
    level=np.clip(.20+.50*v+.16*(1-np.abs(side))+.025*np.sin(v*7+u*3+tile),0,1)
    f=np.clip(level*1.7,0,1)[...,None];lc=a*(1-f)+b*f
    f=np.clip((level-.43)*1.65,0,1)[...,None];lc=lc*(1-f)+c*f
    veins=np.exp(-((u-.5)/.017)**2)*.075
    for pos in (.25,.44,.63,.80):veins+=np.exp(-((v-pos-np.abs(side)*.15)/.012)**2)*.023
    lc*=1+veins[...,None]
    rgb[leaf]=lc[leaf];rough[leaf]=.71-.055*level[leaf]
    stem=(tile==10)|(tile==14)|(tile==15)
    stemlevel=.47+.12*np.sin(u*math.pi)+.05*v
    sc=a*(1-stemlevel[...,None])+b*stemlevel[...,None]
    rgb[stem]=sc[stem];rough[stem]=.75
    heart=tile==11
    hc=np.array(pal['heart'])*(0.83+.17*np.sin(v*math.pi)+.04*np.cos(u*12)*np.sin(v*7))[...,None]
    rgb[heart]=hc[heart];rough[heart]=.56
    if palette=='mystique':
        emission[heart]=hc[heart]*.22
        edgeglow=np.clip((v-.8)*2.0,0,.07)[...,None]
        emission[petal]=(color*edgeglow)[petal]
    one=np.ones((SIZE,SIZE,1),dtype=np.float32)
    write_image(paths[0],np.concatenate([np.clip(rgb,0,1),one],axis=2))
    write_image(paths[1],np.concatenate([np.repeat(rough[...,None],3,axis=2),one],axis=2),True)
    write_image(paths[2],np.concatenate([np.clip(emission,0,1),one],axis=2))


def material(palette):
    mat=bpy.data.materials.new('Arkya flowers | '+palette);mat.use_nodes=True
    nodes=mat.node_tree.nodes;links=mat.node_tree.links;p=nodes.get('Principled BSDF')
    for suffix,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
        image=bpy.data.images.load(str(ROOT/'textures'/palette/(palette+'-'+suffix+'.png')),check_existing=True)
        if suffix=='roughness':image.colorspace_settings.name='Non-Color'
        image.pack();node=nodes.new('ShaderNodeTexImage');node.image=image;links.new(node.outputs['Color'],p.inputs[socket])
    p.inputs['Emission Strength'].default_value=.45 if palette=='mystique' else 0
    p.inputs['Specular IOR Level'].default_value=.24
    mat.diffuse_color=(*PALETTES[palette]['petal'][1],1)
    return mat


class Builder:
    def __init__(self):self.vertices=[];self.faces=[];self.uv=[]
    def face(self,ids,uv):self.faces.append(tuple(ids));self.uv.append(uv)
    @staticmethod
    def tex(tile,s,t):return ((tile%4+.05+s*.90)/4,(tile//4+.05+t*.90)/4)
    def tube(self,points,radius,tile=10,taper=.55,sides=9):
        points=[Vector(p) for p in points];off=len(self.vertices);n=sides
        for j,p in enumerate(points):
            axis=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
            side=axis.cross(Vector((0,1,0)))
            if side.length<.01:side=axis.cross(Vector((1,0,0)))
            side.normalize();normal=side.cross(axis).normalized();r=radius*(1-taper*j/(len(points)-1))
            for k in range(n):
                a=k/n*math.tau;self.vertices.append(tuple(p+r*(math.cos(a)*side+math.sin(a)*normal)))
        for j in range(len(points)-1):
            for k in range(n):
                self.face([off+j*n+k,off+j*n+(k+1)%n,off+(j+1)*n+(k+1)%n,off+(j+1)*n+k],[self.tex(tile,k/n,j/(len(points)-1)),self.tex(tile,(k+1)/n,j/(len(points)-1)),self.tex(tile,(k+1)/n,(j+1)/(len(points)-1)),self.tex(tile,k/n,(j+1)/(len(points)-1))])
        self.face(list(reversed(range(off,off+n))),[self.tex(tile,.5,.04)]*n)
        self.face(list(range(off+(len(points)-1)*n,off+len(points)*n)),[self.tex(tile,.5,.95)]*n)
    def curved_tube(self,points,radius,taper=.55):
        controls=[Vector(p) for p in points];smooth=[]
        for index in range(len(controls)-1):
            p0=controls[max(index-1,0)];p1=controls[index];p2=controls[index+1];p3=controls[min(index+2,len(controls)-1)]
            for t in (0,.2,.4,.6,.8):
                smooth.append(.5*((2*p1)+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t))
        smooth.append(controls[-1]);self.tube(smooth,radius,taper=taper)
    def petal(self,base,axis,side,normal,length,width,tile,cup=.34,curl=.06,leaf=False,asym=0):
        base=Vector(base);axis=Vector(axis).normalized();side=Vector(side).normalized();normal=Vector(normal).normalized()
        steps=15 if not leaf else 8;cols=9 if not leaf else 5;off=len(self.vertices)
        thickness=length*(.025 if not leaf else .020)
        for layer in (0,1):
            for j in range(steps):
                t=j/(steps-1)
                profile=.045+.955*math.sin(math.pi*t)**(.53 if not leaf else .85)
                if not leaf:profile*=.72+.48*t
                for k in range(cols):
                    s=(k/(cols-1)-.5)*2
                    # Spoon-shaped cross-section, upturned rim and an asymmetrical soft tip.
                    bend=length*(cup*t*t-curl*t**5)
                    fold=length*((.075 if not leaf else .035)*s*s*math.sin(math.pi*t)+(.017 if not leaf else .025)*(1-abs(s))*math.sin(math.pi*t))
                    ruffle=length*(.023*math.sin(t*8+s*3+asym)*abs(s)**3*math.sin(math.pi*t)) if not leaf else 0
                    skew=length*.035*math.sin(math.pi*t)*asym
                    p=base+axis*(length*t)+side*(width*.5*profile*s+skew)+normal*(bend+fold+ruffle-(thickness if layer else 0)*math.sin(math.pi*(.08+.84*t)))
                    self.vertices.append(tuple(p))
        for layer in (0,1):
            start=off+layer*steps*cols
            for j in range(steps-1):
                for k in range(cols-1):
                    ids=[start+j*cols+k,start+j*cols+k+1,start+(j+1)*cols+k+1,start+(j+1)*cols+k]
                    uv=[self.tex(tile,k/(cols-1),j/(steps-1)),self.tex(tile,(k+1)/(cols-1),j/(steps-1)),self.tex(tile,(k+1)/(cols-1),(j+1)/(steps-1)),self.tex(tile,k/(cols-1),(j+1)/(steps-1))]
                    if layer:ids.reverse();uv.reverse()
                    self.face(ids,uv)
        border=list(range(cols))+[j*cols+cols-1 for j in range(1,steps)]+list(range((steps-1)*cols+cols-2,(steps-1)*cols-1,-1))+[j*cols for j in range(steps-2,0,-1)]
        for a,b in zip(border,border[1:]+border[:1]):self.face([off+a,off+a+steps*cols,off+b+steps*cols,off+b],[self.tex(tile,.5,.75)]*4)
    def leaf(self,base,direction,length,width,tile,roll=0):
        axis=Vector(direction).normalized();side=axis.cross(Vector((0,0,1)))
        if side.length<.01:side=Vector((1,0,0))
        side.normalize();normal=side.cross(axis).normalized()
        side,normal=side*math.cos(roll)+normal*math.sin(roll),normal*math.cos(roll)-side*math.sin(roll)
        self.petal(base,axis,side,normal,length,width,tile,cup=.14,curl=.24,leaf=True)
    def ellipsoid(self,center,axis,radius,height,tile=11,segments=16,rings=8):
        center=Vector(center);axis=Vector(axis).normalized();side=axis.cross(Vector((0,1,0)))
        if side.length<.01:side=Vector((1,0,0))
        side.normalize();normal=axis.cross(side).normalized();off=len(self.vertices)
        self.vertices.append(tuple(center-axis*height))
        for j in range(1,rings):
            t=j/rings;theta=math.pi*t
            for k in range(segments):
                a=k/segments*math.tau
                self.vertices.append(tuple(center-axis*(height*math.cos(theta))+(side*math.cos(a)+normal*math.sin(a))*radius*math.sin(theta)))
        top=len(self.vertices);self.vertices.append(tuple(center+axis*height))
        for k in range(segments):
            self.face([off,off+1+(k+1)%segments,off+1+k],[self.tex(tile,.5,0),self.tex(tile,(k+1)/segments,1/rings),self.tex(tile,k/segments,1/rings)])
        for j in range(rings-2):
            for k in range(segments):
                self.face([off+1+j*segments+k,off+1+j*segments+(k+1)%segments,off+1+(j+1)*segments+(k+1)%segments,off+1+(j+1)*segments+k],[self.tex(tile,k/segments,(j+1)/rings),self.tex(tile,(k+1)/segments,(j+1)/rings),self.tex(tile,(k+1)/segments,(j+2)/rings),self.tex(tile,k/segments,(j+2)/rings)])
        for k in range(segments):self.face([off+1+(rings-2)*segments+k,off+1+(rings-2)*segments+(k+1)%segments,top],[self.tex(tile,k/segments,(rings-1)/rings),self.tex(tile,(k+1)/segments,(rings-1)/rings),self.tex(tile,.5,1)])
    def flower(self,center,normal,size,rng,kind='cup'):
        center=Vector(center);normal=Vector(normal).normalized();a=normal.cross(Vector((0,1,0)))
        if a.length<.01:a=Vector((1,0,0))
        a.normalize();b=normal.cross(a).normalized();count=6 if kind=='lily' else 5
        # Irregular staggered petals form a shallow bowl, never a flat circular sprite.
        for i in range(count):
            angle=i/count*math.tau+rng.uniform(-.10,.10)+.22
            radial=a*math.cos(angle)+b*math.sin(angle);side=normal.cross(radial)
            length=size*rng.uniform(.87,1.10);width=length*(.52 if kind=='lily' else .88)*rng.uniform(.90,1.12)
            self.petal(center+radial*size*.07+normal*(size*.015*(i%2)),radial,side,normal,length,width,i%6,cup=.62 if kind=='lily' else .42,curl=.57 if kind=='lily' else .04,asym=rng.uniform(-1,1))
            sepal_base=center-normal*size*.045
            self.petal(sepal_base,radial,side,-normal,size*.34,size*.16,6+(i%4),cup=.18,curl=.12,leaf=True)
        self.ellipsoid(center+normal*size*.027,normal,size*.19,size*.09,11)
        if kind=='lily':
            for i in range(6):
                angle=i/6*math.tau;offset=(a*math.cos(angle)+b*math.sin(angle))*size*.18
                end=center+normal*(size*.43+rng.uniform(-.035,.035)*size)+offset*1.5
                self.tube([center+offset*.25,center+normal*size*.22+offset*.7,end],size*.012,11,.12,7)
                self.ellipsoid(end,normal+offset*3,size*.029,size*.055,11,10,6)
        else:
            for i in range(9):
                angle=i/9*math.tau;offset=(a*math.cos(angle)+b*math.sin(angle))*size*.115
                self.ellipsoid(center+normal*size*.10+offset,normal,size*.028,size*.028,11,8,5)
    def bell(self,top,axis,length,radius,rng):
        top=Vector(top);axis=Vector(axis).normalized();a=axis.cross(Vector((0,1,0)))
        if a.length<.01:a=Vector((1,0,0))
        a.normalize();b=axis.cross(a).normalized();n=40;rows=11;off=len(self.vertices)
        # An open thick funnel, with five lobes curling apart at its lip.
        for inner in (0,1):
            for j in range(rows):
                t=j/(rows-1)
                for k in range(n):
                    ang=k/n*math.tau;lobes=.5+.5*math.cos(5*ang+.25)
                    r=radius*(.18+.77*math.sin(t*math.pi*.55)+.27*t**6*lobes)
                    r-=length*.025 if inner else 0
                    drop=length*(t-.12*t**6*(1-lobes))
                    p=top+axis*drop+(a*math.cos(ang)+b*math.sin(ang))*r
                    self.vertices.append(tuple(p))
        for inner in (0,1):
            start=off+inner*n*rows
            for j in range(rows-1):
                for k in range(n):
                    ids=[start+j*n+k,start+j*n+(k+1)%n,start+(j+1)*n+(k+1)%n,start+(j+1)*n+k]
                    uv=[self.tex(13,k/n,j/(rows-1)),self.tex(13,(k+1)/n,j/(rows-1)),self.tex(13,(k+1)/n,(j+1)/(rows-1)),self.tex(13,k/n,(j+1)/(rows-1))]
                    if inner:ids.reverse();uv.reverse()
                    self.face(ids,uv)
        for j in (0,rows-1):
            for k in range(n):self.face([off+j*n+k,off+j*n+(k+1)%n,off+n*rows+j*n+(k+1)%n,off+n*rows+j*n+k],[self.tex(13,.5,j/(rows-1))]*4)
        for i in range(3):
            ang=i/3*math.tau;offset=(a*math.cos(ang)+b*math.sin(ang))*radius*.22
            end=top+axis*length*.9+offset
            self.tube([top+axis*length*.15,top+axis*length*.5+offset*.6,end],length*.016,11,.05,7)
            self.ellipsoid(end,axis,length*.035,length*.045,11,10,6)
    def bud(self,point,axis,size,rng):
        point=Vector(point);axis=Vector(axis).normalized()
        self.ellipsoid(point,axis,size*.40,size*.68,12,16,10)
        side=axis.cross(Vector((0,1,0))).normalized();normal=axis.cross(side).normalized()
        for i in range(4):
            a=i/4*math.tau;radial=side*math.cos(a)+normal*math.sin(a)
            direction=(axis*.85+radial*.35).normalized()
            self.leaf(point-axis*size*.63,direction,size*.82,size*.27,6+i)
    def object(self,mat):
        mesh=bpy.data.meshes.new('Closed petals, stems, leaves and corollas');mesh.from_pydata(self.vertices,[],self.faces);mesh.update();mesh.materials.append(mat)
        layer=mesh.uv_layers.new(name='Painted_Atlas_UV')
        for face,uv in zip(mesh.polygons,self.uv):
            face.use_smooth=True
            for loop,co in zip(face.loop_indices,uv):layer.data[loop].uv=co
        bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
        obj=bpy.data.objects.new('Arkya | flowers',mesh)
        collection=bpy.data.collections.new('Arkya_Flowers');bpy.context.scene.collection.children.link(collection);collection.objects.link(obj)
        return obj


def geometry(shape,mat):
    rng=random.Random(840+list(SHAPES).index(shape)*29);mesh=Builder()
    def basal(count,length,width):
        for i in range(count):
            angle=i/count*math.tau+.24+rng.uniform(-.1,.1)
            mesh.leaf((math.cos(angle)*.009,math.sin(angle)*.009,.012),(math.cos(angle),math.sin(angle),.18+rng.random()*.30),length*rng.uniform(.8,1.1),width*rng.uniform(.85,1.12),6+i%4,rng.uniform(-.20,.20))
    def stem(end,radius=.008,base=(0,0,0),leaves=2):
        base=Vector(base);end=Vector(end);middle=base.lerp(end,.45)+Vector((-.025,.018,0))
        points=[]
        for t in (0,.2,.4,.6,.8,1):points.append(base*(1-t)**2+middle*2*t*(1-t)+end*t*t)
        mesh.tube(points,radius)
        for i in range(leaves):
            t=.28+i*.21;origin=base.lerp(end,t)+Vector((-.01,.008,0))
            angle=math.atan2(end.y,end.x)+i*2.9+.3
            mesh.leaf(origin,(math.cos(angle),math.sin(angle),.35+.1*i),.10+radius*4,.045+radius*1.6,6+i%4,.14*(-1 if i%2 else 1))
    if shape=='rosette':
        basal(8,.14,.072)
        stem((-.015,-.015,.105),.009,leaves=0)
        mesh.flower((-.015,-.015,.105),(.07,-.38,.92),.088,rng)
        stem((.085,.02,.085),.005,leaves=1)
        mesh.bud((.085,.02,.085),(.32,-.12,.94),.025,rng)
    elif shape=='bouquet':
        basal(7,.15,.059)
        for pos,axis,size in [((-.08,.035,.35),(-.24,-.34,.91),.095),((.065,.05,.41),(.28,-.20,.94),.091),((.14,-.045,.27),(.55,-.26,.79),.081),((-.085,-.09,.245),(-.38,-.43,.82),.073)]:
            stem(pos,.0065,leaves=2);mesh.flower(pos,axis,size,rng)
        stem((.04,-.035,.305),.005,leaves=1);mesh.bud((.04,-.035,.305),(.25,-.15,.96),.027,rng)
    elif shape=='clochettes':
        basal(7,.23,.065)
        main=[(0,0,0),(-.025,.005,.15),(-.035,.012,.31),(-.005,.015,.48),(.05,.02,.61),(.11,.025,.65)]
        mesh.curved_tube(main,.014,taper=.66)
        for i,(origin,top,axis,size) in enumerate([
          ((-.03,.012,.28),(-.13,-.005,.31),(-.26,-.23,-.94),.105),
          ((-.018,.015,.41),(.09,-.04,.43),(.27,-.30,-.91),.101),
          ((.015,.017,.52),(-.08,.004,.56),(-.32,-.15,-.94),.095),
          ((.075,.025,.63),(.16,.008,.63),(.34,-.28,-.89),.083)]):
            mesh.curved_tube([origin,Vector(origin).lerp(Vector(top),.5)+Vector((0,0,.045)),top],.0058,taper=.25)
            mesh.bell(top,axis,size,size*.50,rng)
            mesh.leaf(origin,(-.7 if i%2 else .7,.3,.25),.15,.052,6+i%4)
        mesh.bud((.115,.025,.665),(.45,.05,.89),.032,rng)
        mesh.curved_tube([(.06,.02,.60),(.07,.065,.65),(.08,.075,.68)],.0042)
        mesh.bud((.08,.075,.68),(.18,.10,.97),.021,rng)
    else:
        basal(7,.27,.073)
        stem((-.025,.015,.78),.015,leaves=4)
        mesh.flower((-.025,.015,.78),(.08,-.43,.90),.235,rng,'lily')
        for pos,axis,size in [((-.22,-.015,.47),(-.38,-.3,.87),.115),((.21,.055,.59),(.45,-.18,.87),.135)]:
            stem(pos,.007,base=(0,0,.19),leaves=2);mesh.flower(pos,axis,size,rng,'cup')
        stem((.10,.04,.78),.0055,base=(0,0,.48),leaves=1);mesh.bud((.10,.04,.78),(.23,-.10,.97),.044,rng)
    obj=mesh.object(mat);bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    modifier=obj.modifiers.new('Portable triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=modifier.name)
    vertices=obj.data.vertices;low=Vector(tuple(min(v.co[i] for v in vertices) for i in range(3)));high=Vector(tuple(max(v.co[i] for v in vertices) for i in range(3)))
    center=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z));scale=SHAPES[shape]['height']/(high.z-low.z)
    for v in vertices:v.co=(v.co-center)*scale
    return obj,(high-low)*scale


def studio(obj,dimensions,path,draft=False,gpu=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12 if draft else 32;scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU'
    if gpu:
        prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
        for device in prefs.devices:device.use=device.type=='OPTIX'
    scene.render.resolution_x=760 if draft else 1100;scene.render.resolution_y=scene.render.resolution_x;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Soft neutral studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.27,.32,.30,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.70
    h=dimensions.z;e=max(dimensions)
    bpy.ops.mesh.primitive_plane_add(size=e*200,location=(0,0,-.002));floor=bpy.context.object;floor.name='Studio floor | not exported'
    fm=bpy.data.materials.new('Sage studio floor');fm.use_nodes=True;fm.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.19,.215,.19,1);fm.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.88;floor.data.materials.append(fm)
    for name,location,energy,size in [('Warm softbox',(-e*3,-e*4,e*5),650*e*e,e*3),('Sky fill',(e*4,-e,e*3),290*e*e,e*3),('Rim light',(e,e*3,e*4),420*e*e,e*2.5)]:
        data=bpy.data.lights.new(name,'AREA');data.energy=energy;data.shape='DISK';data.size=size;light=bpy.data.objects.new(name,data);scene.collection.objects.link(light);light.location=location;light.rotation_euler=(Vector((0,0,h*.45))-light.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Preview camera');camera=bpy.data.objects.new('Preview camera',data);scene.collection.objects.link(camera);scene.camera=camera;camera.location=(e*2.1,-e*3.8,e*2.05);camera.rotation_euler=(Vector((0,0,h*.47))-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=max(h,dimensions.x)*1.40
    scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def build(shape,palette,args):
    bpy.ops.wm.read_factory_settings(use_empty=True);obj,dimensions=geometry(shape,material(palette))
    stem=shape+'-'+palette;folder=ROOT/'assets'/stem;folder.mkdir(parents=True,exist_ok=True)
    obj.name='Arkya | '+SHAPES[shape]['label'];obj['family']='fleurs';obj['shape']=shape;obj['material']=palette;obj['pivot']='ground centre'
    bpy.context.scene.unit_settings.system='METRIC';bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_animations=False,export_lights=False,export_cameras=False,export_tangents=True)
    triangles=sum(len(p.vertices)-2 for p in obj.data.polygons);assert triangles<=35000,(stem,triangles)
    if args.render:studio(obj,dimensions,folder/(stem+'-preview.png'),args.draft,args.gpu)
    provenance=bpy.data.texts.new('Arkya provenance');provenance.write('Original authored sculpted flowers and procedural painted atlases. References: approved Arkya tree V3, shrubs and grass. Closed curved petals and leaves; no alpha cards. Units metres, ground-centred pivot. Generator: scripts/generate_flowers.py. Artistic status: proposal, awaiting user validation.')
    bpy.context.preferences.filepaths.save_version=0;bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],'glb':f'assets/{stem}/{stem}.glb','blend':f'assets/{stem}/{stem}.blend','preview':f'assets/{stem}/{stem}-preview.png' if (folder/(stem+'-preview.png')).exists() else None,'height':round(float(dimensions.z),4),'width':round(float(dimensions.x),4),'depth':round(float(dimensions.y),4),'triangles':triangles,'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol'}
    (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    path=ROOT/'manifest.json';old=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {};records=[a for a in old.get('assets',[]) if a['id']!=stem]+[record]
    manifest={'family':'fleurs','status':'proposé','shapes':[dict(id=k,**v) for k,v in SHAPES.items()],'materials':[{'id':k,'label':v['label'],'swatch':v['swatch']} for k,v in PALETTES.items()],'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material'])))}
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8');print('FLOWER_READY',json.dumps(record,ensure_ascii=False),flush=True)


def main():
    p=argparse.ArgumentParser();p.add_argument('--shape',choices=['all']+list(SHAPES),default='bouquet');p.add_argument('--material',choices=['all']+list(PALETTES),default='corail');p.add_argument('--render',action='store_true');p.add_argument('--draft',action='store_true');p.add_argument('--gpu',action='store_true');p.add_argument('--repaint',action='store_true');args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
    for palette in palettes:paint(palette,args.repaint)
    for shape in shapes:
        for palette in palettes:build(shape,palette,args)


if __name__=='__main__':main()
