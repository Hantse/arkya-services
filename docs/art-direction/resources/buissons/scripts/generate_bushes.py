"""Authored shrub silhouettes with thick curved leaves and portable painted atlases."""
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
import numpy as np
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
SIZE = 2048
SHAPES = {
    'coussin': {'label': 'Coussin bas', 'height': .45, 'description': 'Port bas en trois nappes irrégulières, petites branches rampantes.'},
    'rond': {'label': 'Buisson rond', 'height': .85, 'description': 'Couronne arrondie asymétrique, étages de feuillage et ouvertures entre les rameaux.'},
    'etale': {'label': 'Buisson étalé', 'height': 1.25, 'description': 'Large éventail décentré, deux bras latéraux et feuillage retombant.'},
    'dresse': {'label': 'Buisson dressé', 'height': 1.8, 'description': 'Port vertical à plusieurs tiges, sommet décalé et rameaux secondaires ouverts.'},
}
PALETTES = {
    'sylvestre': {'label': 'Vert sylvestre', 'swatch': '#6c9157', 'colors': [(0.075,.18,.11),(.21,.35,.17),(.47,.62,.27)], 'bark': (.23,.17,.12)},
    'automnal': {'label': 'Automnal', 'swatch': '#bc784c', 'colors': [(.29,.095,.063),(.56,.24,.095),(.73,.47,.19)], 'bark': (.25,.14,.105)},
    'mystique': {'label': 'Mystique', 'swatch': '#739fa0', 'colors': [(.13,.12,.22),(.20,.37,.36),(.42,.59,.53)], 'bark': (.18,.12,.20)},
}


def write_image(path, pixels, noncolor=False):
    image = bpy.data.images.new(path.stem, width=pixels.shape[1], height=pixels.shape[0], alpha=False)
    image.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
    image.pixels.foreach_set(pixels.astype(np.float32).ravel())
    image.filepath_raw = str(path); image.file_format = 'PNG'; image.save(); image.pack()
    return image


def texture_set(palette,force=False):
    folder = ROOT/'textures'/palette; folder.mkdir(parents=True, exist_ok=True)
    base = folder/(palette+'-feuilles-basecolor.png')
    rough = folder/(palette+'-feuilles-roughness.png')
    bark = folder/(palette+'-rameaux-basecolor.png')
    if not force and base.exists() and rough.exists() and bark.exists(): return
    colors = PALETTES[palette]['colors']
    uv = (np.arange(SIZE, dtype=np.float32)+.5)/SIZE
    x,y = np.meshgrid(uv,uv)
    u=(x*4)%1; v=(y*4)%1; tile=np.floor(x*4)+np.floor(y*4)*4
    side = (u-.5)*2
    # Broad irregular glazes, a restrained central vein and paired side veins.
    broad = np.sin(u*4.1+v*5.5+tile*.9)*.028+np.sin(v*11+u*3+tile)*.018
    level = np.clip(.19+.57*v+.15*(1-np.abs(side))+broad+.065*np.sin(tile*2.3),0,1)
    shade=np.asarray(colors[0]);middle=np.asarray(colors[1]);light=np.asarray(colors[2])
    f=np.minimum(level*1.65,1)[...,None]
    rgb=shade[None,None,:]*(1-f)+middle[None,None,:]*f
    upper=np.clip((level-.38)*1.55,0,1)[...,None]
    rgb=rgb*(1-upper)+light[None,None,:]*upper
    ridge=np.exp(-((u-.5)/.017)**2)*.10
    veins=np.zeros_like(u)
    for t in (.24,.42,.61,.78):
        line=t+np.abs(side)*.19
        veins=np.maximum(veins,np.exp(-((v-line)/.009)**2)*(.75-.45*np.abs(side)))
    edge=np.exp(-((np.abs(side)-.94)/.045)**2)*.055
    rgb=rgb*(1+ridge[...,None]+veins[...,None]*.065+edge[...,None])
    if palette=='sylvestre':
        group=np.floor(tile/4).astype(np.int32)
        tones=np.array([[.88,.98,1.10],[1.10,1.07,.91],[.93,1.,1.05],[1.,1.,1.]])
        rgb*=tones[group]
    if palette=='mystique':
        purple=(tile%5==0)[...,None]
        plum=np.stack([rgb[:,:,0]*1.17,rgb[:,:,1]*.73,rgb[:,:,2]*1.09],axis=2)
        rgb=np.where(purple,plum,rgb)
    if palette=='automnal':
        red=(tile%4==0)[...,None]
        russet=rgb*np.array([1.02,.69,.70])
        rgb=np.where(red,russet,rgb)
    write_image(base,np.concatenate([np.clip(rgb,0,1),np.ones((SIZE,SIZE,1))],axis=2))
    r=np.clip(.70-.08*level+.015*np.sin(v*12+tile),.55,.78)
    write_image(rough,np.stack([r,r,r,np.ones_like(r)],axis=2),True)
    n=512;bx,by=np.meshgrid(np.linspace(0,1,n),np.linspace(0,1,n));wave=np.sin(bx*27+np.sin(by*7)*.5)*.09+np.sin(bx*68+by*3)*.025
    col=np.array(PALETTES[palette]['bark'])[None,None,:]*(1+wave[...,None])
    write_image(bark,np.concatenate([np.clip(col,0,1),np.ones((n,n,1))],axis=2))


def material(palette,bark=False):
    name=palette+(' | rameaux' if bark else ' | feuillage peint')
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    nodes=mat.node_tree.nodes;links=mat.node_tree.links;p=nodes.get('Principled BSDF')
    suffix='rameaux-basecolor' if bark else 'feuilles-basecolor'
    image=bpy.data.images.load(str(ROOT/'textures'/palette/(palette+'-'+suffix+'.png')),check_existing=True);image.pack()
    texture=nodes.new('ShaderNodeTexImage');texture.image=image;links.new(texture.outputs['Color'],p.inputs['Base Color'])
    if not bark:
        r=bpy.data.images.load(str(ROOT/'textures'/palette/(palette+'-feuilles-roughness.png')),check_existing=True);r.colorspace_settings.name='Non-Color';r.pack()
        rt=nodes.new('ShaderNodeTexImage');rt.image=r;links.new(rt.outputs['Color'],p.inputs['Roughness'])
    else:p.inputs['Roughness'].default_value=.83
    p.inputs['Specular IOR Level'].default_value=.24 if not bark else .18
    p.inputs['Subsurface Weight'].default_value=0
    mat.diffuse_color=(*PALETTES[palette]['colors'][1],1)
    return mat


class MeshBuilder:
    def __init__(self):self.vertices=[];self.faces=[];self.uv=[];self.materials=[]
    def face(self,verts,coords,mat=0):self.faces.append(tuple(verts));self.uv.append(coords);self.materials.append(mat)
    def leaf(self,base,direction,length,width,tile,roll=0,curve=.18):
        axis=Vector(direction).normalized();side=axis.cross(Vector((0,0,1)))
        if side.length<.01:side=Vector((1,0,0))
        side.normalize();normal=side.cross(axis).normalized()
        side2=side*math.cos(roll)+normal*math.sin(roll);normal2=normal*math.cos(roll)-side*math.sin(roll)
        side=side2;normal=normal2
        steps=(0,.16,.38,.64,.84,1);widths=(.025,.70,1,.92,.56,.018)
        offset=len(self.vertices);base=Vector(base);tx=tile%4;ty=tile//4
        def uv(s,t):return ((tx+.055+(s+1)*.445)/4,(ty+.055+t*.89)/4)
        for layer in (0,1):
            for t,w in zip(steps,widths):
                for s in (-1,0,1):
                    bend=length*(curve*math.sin(t*math.pi)*.6-.11*t*t)
                    fold=width*.16*(1-abs(s))*math.sin(math.pi*t)
                    p=base+axis*(length*t)+side*(width*.5*w*s)+normal*(bend+fold-(.0032*length if layer else 0))
                    self.vertices.append(tuple(p))
        for layer in (0,1):
            off=offset+layer*18
            for j in range(5):
                for k in range(2):
                    ids=[off+j*3+k,off+j*3+k+1,off+(j+1)*3+k+1,off+(j+1)*3+k]
                    coords=[uv(k-1,steps[j]),uv(k,steps[j]),uv(k,steps[j+1]),uv(k-1,steps[j+1])]
                    if layer:ids.reverse();coords.reverse()
                    self.face(ids,coords)
        boundary=[0,1,2,5,8,11,14,17,16,15,12,9,6,3]
        for a,b in zip(boundary,boundary[1:]+boundary[:1]):self.face([offset+a,offset+a+18,offset+b+18,offset+b],[uv(0,.5)]*4)
    def branch(self,points,radius):
        points=[Vector(p) for p in points];off=len(self.vertices);n=7
        for j,p in enumerate(points):
            axis=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
            side=axis.cross(Vector((0,1,0))).normalized();normal=side.cross(axis).normalized()
            r=radius*(1-.82*j/(len(points)-1))
            for k in range(n):
                a=k/n*math.tau;self.vertices.append(tuple(p+r*(math.cos(a)*side+math.sin(a)*normal)))
        for j in range(len(points)-1):
            for k in range(n):
                a=off+j*n+k;b=off+j*n+(k+1)%n;c=b+n;d=a+n
                self.face([a,b,c,d],[(k/n,j/(len(points)-1)),((k+1)/n,j/(len(points)-1)),((k+1)/n,(j+1)/(len(points)-1)),(k/n,(j+1)/(len(points)-1))],1)
        self.face(list(reversed(range(off,off+n))),[(.5,.5)]*n,1)
        self.face(list(range(off+(len(points)-1)*n,off+len(points)*n)),[(.5,.5)]*n,1)
    def object(self,mats):
        mesh=bpy.data.meshes.new('Sculpted leaf blades and branching stems');mesh.from_pydata(self.vertices,[],self.faces);mesh.update()
        for mat in mats:mesh.materials.append(mat)
        uv=mesh.uv_layers.new(name='Painted_Atlas_UV')
        for p,coords,mi in zip(mesh.polygons,self.uv,self.materials):
            p.material_index=mi;p.use_smooth=True
            for loop,co in zip(p.loop_indices,coords):uv.data[loop].uv=co
        bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(mesh);bm.free()
        obj=bpy.data.objects.new('Arkya Bush | sculpted leaves',mesh);bpy.context.scene.collection.objects.link(obj)
        return obj


def geometry(shape,mats):
    rng=random.Random(137+list(SHAPES).index(shape)*29);mesh=MeshBuilder()
    # These cluster centres define silhouettes; leaves follow paired shoots within each spray.
    configs={
      'coussin':([(-.22,-.15,.065),(.18,-.10,.075),(-.37,-.12,.16),(.02,-.22,.16),(.34,-.04,.20),(-.24,.18,.22),(.10,.20,.25),(-.07,.01,.30),(-.05,-.15,.17),(.13,0,.20)],.15,.090,4),
      'rond':([(-.22,-.14,.12),(.12,-.21,.14),(.15,.14,.16),(-.29,-.17,.28),(.26,-.18,.31),(.30,.19,.31),(-.27,.19,.32),(.02,-.23,.37),(-.08,-.13,.47),(-.22,-.03,.52),(.19,-.04,.56),(.04,.21,.52),(-.07,.05,.69),(.14,.16,.62)],.17,.10,4),
      'etale':([(-.28,-.19,.15),(.23,-.13,.19),(.03,.17,.24),(-.63,-.08,.38),(-.38,-.28,.43),(.03,-.30,.35),(.38,-.19,.42),(.66,.06,.50),(-.47,.20,.58),(-.15,.13,.67),(.23,.18,.65),(.51,.23,.63),(-.12,-.03,.84),(.18,.08,.80),(-.40,.03,.68),(-.17,-.21,.46),(.14,-.14,.56)],.20,.115,4),
      'dresse':([(-.13,-.14,.13),(.17,-.03,.20),(-.11,.16,.29),(-.21,-.20,.33),(.20,-.10,.46),(.28,.12,.67),(-.29,.12,.65),(-.22,-.10,.88),(.22,-.13,1.01),(.04,.23,1.06),(-.21,.08,1.22),(.07,-.06,1.40),(.20,.10,1.32),(-.07,.08,1.57)],.19,.106,5),
    }
    centres,leaf_len,leaf_width,rays=configs[shape]
    for ci,c in enumerate(centres):
        center=Vector(c);az=math.atan2(center.y,center.x)
        start=Vector((math.cos(az)*.025,math.sin(az)*.025,.005))
        branch_points=[start]
        for t in (.24,.49,.74,1.):
            bend=math.sin(t*math.pi)
            branch_points.append(start.lerp(center,t)+Vector((-.035*math.sin(ci)*bend,.025*math.cos(ci)*bend,.045*bend)))
        mesh.branch(branch_points,.014+center.z*.009)
        for ri in range(rays):
            a=az+(ri-(rays-1)*.5)*.89+rng.uniform(-.10,.10)
            horizontal=.82 if ri<rays-1 else .42
            axis=Vector((math.cos(a)*horizontal,math.sin(a)*horizontal,.45+ri*.055)).normalized()
            shoot_len=leaf_len*(1.55+rng.random()*.30)
            endpoint=center+axis*shoot_len
            mesh.branch([center,center+axis*shoot_len*.5+Vector((0,0,.02)),endpoint],.0048)
            lateral=Vector((-math.sin(a),math.cos(a),0))
            for ni,t in enumerate((.24,.51,.76)):
                for sign in (-1,1):
                    origin=center+axis*(shoot_len*(t+(.025 if sign==1 else 0)))
                    direction=(axis*.25+lateral*sign*.83+Vector((0,0,.27+(ni*.05)))).normalized()
                    length=leaf_len*rng.uniform(.80,1.15)*(1-.07*ni)
                    width=leaf_width*rng.uniform(.9,1.10)
                    tile=(ci%4)*4+(ri+ni+(0 if sign==1 else 2))%4
                    mesh.leaf(origin,direction,length,width,tile,rng.uniform(-.25,.25),.14+rng.random()*.20)
            mesh.leaf(endpoint,axis+Vector((0,0,.08)),leaf_len*.90,leaf_width*.84,(ci%4)*4+(ri+2)%4,.05,.18)
    obj=mesh.object(mats)
    bpy.context.view_layer.objects.active=obj;obj.select_set(True)
    modifier=obj.modifiers.new('Portable triangles','TRIANGULATE');bpy.ops.object.modifier_apply(modifier=modifier.name)
    vertices=obj.data.vertices;low=Vector(tuple(min(v.co[i] for v in vertices) for i in range(3)));high=Vector(tuple(max(v.co[i] for v in vertices) for i in range(3)))
    center=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z));scale=SHAPES[shape]['height']/(high.z-low.z)
    for v in vertices:v.co=(v.co-center)*scale
    dim=(high-low)*scale
    return obj,dim


def studio(obj,dimensions,path,draft=False,gpu=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12 if draft else 40;scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU'
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
    data=bpy.data.cameras.new('Preview camera');camera=bpy.data.objects.new('Preview camera',data);scene.collection.objects.link(camera);scene.camera=camera;camera.location=(e*2.1,-e*3.8,e*2.05);camera.rotation_euler=(Vector((0,0,h*.43))-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=max(h,dimensions.x)*1.25
    scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def build(shape,palette,args):
    bpy.ops.wm.read_factory_settings(use_empty=True);obj,dimensions=geometry(shape,[material(palette),material(palette,True)])
    stem=shape+'-'+palette;folder=ROOT/'assets'/stem;folder.mkdir(parents=True,exist_ok=True)
    obj.name='Arkya | '+SHAPES[shape]['label'];obj['family']='buissons';obj['shape']=shape;obj['material']=palette;obj['pivot']='ground centre'
    scene=bpy.context.scene;scene.unit_settings.system='METRIC'
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_animations=False,export_lights=False,export_cameras=False,export_tangents=True)
    triangles=sum(len(p.vertices)-2 for p in obj.data.polygons);assert triangles<45000,(stem,triangles)
    if args.render:studio(obj,dimensions,folder/(stem+'-preview.png'),args.draft,args.gpu)
    text=bpy.data.texts.new('Arkya provenance');text.write('Original authored shrub geometry and procedural painted leaf atlases created for Arkya. No external model. Reference: approved tree V3. Leaves are closed curved meshes with real silhouette. Units metres, ground-centred pivot. Generator: scripts/generate_bushes.py.')
    bpy.context.preferences.filepaths.save_version=0;bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],'glb':f'assets/{stem}/{stem}.glb','blend':f'assets/{stem}/{stem}.blend','preview':f'assets/{stem}/{stem}-preview.png' if (folder/(stem+'-preview.png')).exists() else None,'height':round(float(dimensions.z),4),'width':round(float(dimensions.x),4),'depth':round(float(dimensions.y),4),'triangles':triangles,'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol'}
    (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    path=ROOT/'manifest.json';old=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {};records=[a for a in old.get('assets',[]) if a['id']!=stem]+[record]
    manifest={'family':'buissons','shapes':[dict(id=k,**v) for k,v in SHAPES.items()],'materials':[{'id':k,'label':v['label'],'swatch':v['swatch']} for k,v in PALETTES.items()],'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material'])))}
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8');print('BUSH_READY',json.dumps(record,ensure_ascii=False),flush=True)


def main():
    p=argparse.ArgumentParser();p.add_argument('--shape',choices=['all']+list(SHAPES),default='rond');p.add_argument('--material',choices=['all']+list(PALETTES),default='sylvestre');p.add_argument('--render',action='store_true');p.add_argument('--draft',action='store_true');p.add_argument('--gpu',action='store_true');p.add_argument('--repaint',action='store_true');args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
    for palette in palettes:texture_set(palette,args.repaint)
    for shape in shapes:
        for palette in palettes:build(shape,palette,args)


if __name__=='__main__':main()
