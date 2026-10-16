"""Arkya mushrooms: sculpted continuous caps, curved stems and portable painted atlases."""
import argparse
import json
import math
import random
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
SIZE = 2048
SHAPES = {
    'boutons': {'label': 'Colonie de boutons', 'height': .18, 'description': 'Cinq boutons dodus à différents stades, groupés en arc ouvert.'},
    'amanite': {'label': 'Amanite bombée', 'height': .40, 'description': 'Grand chapeau penché, rebord charnu et petit compagnon décalé.'},
    'chanterelles': {'label': 'Chanterelles en éventail', 'height': .60, 'description': 'Trois coupes ondulées et évasées, lamelles descendantes et pieds incurvés.'},
    'parasol': {'label': 'Parasol ancien', 'height': .95, 'description': 'Grand parasol asymétrique à bosse centrale, bord souple et jeunes pousses latérales.'},
}
PALETTES = {
    'corail': {'label': 'Corail crème', 'swatch': '#cd6f52', 'description': 'Chapeaux corail chaleureux, quelques marques crème et pieds ivoire ombrés de terre rose.',
        'crown': (.40,.075,.035), 'rim': (.65,.23,.10), 'shadow': (.19,.028,.035), 'mark': (.86,.69,.41), 'stem': (.66,.52,.32), 'root': (.20,.105,.065), 'gill': (.40,.24,.15)},
    'sous-bois': {'label': 'Miel des sous-bois', 'swatch': '#bb914c', 'description': 'Brun noisette, miel, liserés ocre et pieds crème; la teinte reste mate et peinte.',
        'crown': (.21,.105,.040), 'rim': (.57,.33,.105), 'shadow': (.11,.052,.028), 'mark': (.71,.51,.23), 'stem': (.58,.44,.24), 'root': (.16,.085,.036), 'gill': (.35,.23,.105)},
    'mystique': {'label': 'Mystique boréal', 'swatch': '#7e71ad', 'description': 'Violet feutré, ombres pétrole et accents turquoise limités au rebord et aux marques.',
        'crown': (.18,.065,.26), 'rim': (.045,.32,.29), 'shadow': (.040,.043,.11), 'mark': (.24,.60,.48), 'stem': (.32,.35,.40), 'root': (.075,.05,.12), 'gill': (.11,.24,.24)},
}


def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1)
    return t*t*(3-2*t)


def write_image(path, pixels, color=True):
    image=bpy.data.images.new(path.stem,SIZE,SIZE,alpha=False)
    image.colorspace_settings.name='sRGB' if color else 'Non-Color'
    rgba=np.ones((SIZE,SIZE,4),dtype=np.float32)
    values=pixels if pixels.ndim==3 else pixels[:,:,None]
    if color:values=np.where(values<=.0031308,values*12.92,1.055*np.power(np.maximum(values,0),1/2.4)-.055)
    rgba[:,:,:3]=values;image.pixels.foreach_set(rgba.ravel());image.filepath_raw=str(path);image.file_format='PNG';image.save();bpy.data.images.remove(image)


def make_textures(palette, force=False):
    folder=ROOT/'textures'/palette;folder.mkdir(parents=True,exist_ok=True)
    if not force and all((folder/f'{palette}-{channel}.png').exists() for channel in ('basecolor','roughness','emission')):return
    p=PALETTES[palette]
    y,x=np.mgrid[0:SIZE,0:SIZE].astype(np.float32);x/=SIZE;y/=SIZE
    color=np.zeros((SIZE,SIZE,3),dtype=np.float32);rough=np.full((SIZE,SIZE),.70,dtype=np.float32);emission=np.zeros_like(color)
    # Four generous islands: cap above, underside, stem and collar. All gradients use linear colour.
    u=(x-.25)/.235;v=(y-.25)/.235;r=np.sqrt(u*u+v*v);angle=np.arctan2(v,u)
    edge=smooth(.70,1.02,r)*.92
    top=np.asarray(p['crown'])[None,None,:]*(1-edge[:,:,None])+np.asarray(p['rim'])[None,None,:]*edge[:,:,None]
    # Unequal broad colour fields, with softer brush strokes subordinate to the cap's mass.
    patches=.075*np.sin(angle*3.0+r*3.5)+.035*np.cos(angle*7-r*4)+.025*np.sin(u*12+v*7)
    top*= (1+patches)[:,:,None]
    shade=np.exp(-(((u+.35)/.65)**2+((v-.34)/.80)**2))*.18
    top=top*(1-shade[:,:,None])+np.asarray(p['shadow'])[None,None,:]*shade[:,:,None]
    # Each large mark has an irregular soft silhouette; the unequal distribution avoids a polka-dot grid.
    marks=np.zeros_like(r)
    for cx,cy,rx,ry,phase in [(-.22,-.31,.17,.115,1.1),(.35,-.13,.115,.15,2.8),(.08,.31,.145,.11,.7),(-.48,.30,.105,.085,3.1),(.53,.46,.08,.115,2),(-.60,-.26,.07,.08,1.8),(.18,-.69,.09,.06,2.4)]:
        dx=(u-cx)/rx;dy=(v-cy)/ry;a=np.arctan2(dy,dx);d=np.sqrt(dx*dx+dy*dy)*(1+.14*np.sin(a*3+phase)+.045*np.cos(a*7-phase))
        marks=np.maximum(marks,1-smooth(.75,1.06,d))
    markcolor=np.asarray(p['mark'])[None,None,:]*(.96+.035*np.sin(u*13+v*19))[:,:,None]
    if palette=='sous-bois':marks*=.48
    top=top*(1-marks[:,:,None]*.94)+markcolor*marks[:,:,None]*.94
    mask=(x<.5)&(y<.5);color[mask]=top[mask];rough[mask]=(.68+.045*patches-.04*marks)[mask]
    if palette=='mystique':
        emission[mask]=((smooth(.88,1.02,r)*.007+marks*.015)[:,:,None]*np.asarray((.10,.65,.45))[None,None,:])[mask]
    # Warm ivory underside with radial planes matching 24 sculpted lamellae.
    v=(y-.75)/.235;r=np.sqrt(u*u+v*v);angle=np.arctan2(v,u)
    lamella=(.5+.5*np.cos(angle*24))**3
    underside=np.asarray(p['gill'])[None,None,:]*(.80+.22*lamella+.15*smooth(.10,.88,r))[:,:,None]
    mask=(x<.5)&(y>=.5);color[mask]=underside[mask];rough[mask]=.77
    # Stem: broad silky streaks and a cool occlusion wash below the cap, no grain/noise.
    u=(x-.53)/.44;v=(y-.03)/.44
    lift=smooth(0,.36,v)
    stem=np.asarray(p['root'])[None,None,:]*(1-lift[:,:,None])+np.asarray(p['stem'])[None,None,:]*lift[:,:,None]
    stroke=.075*np.sin(u*12+v*1.8)+.022*np.sin(u*33-v*4)
    stem*= (1+stroke-.20*smooth(.79,1.02,v))[:,:,None]
    mask=(x>=.5)&(y<.5);color[mask]=stem[mask];rough[mask]=.77
    # Flared collar and foot use the same restrained cream with large vertical folds.
    u=(x-.53)/.44;v=(y-.53)/.44
    collar=np.asarray(p['stem'])[None,None,:]*(.92+.08*np.sin(u*math.tau*12)+.10*v)[:,:,None]
    mask=(x>=.5)&(y>=.5);color[mask]=collar[mask];rough[mask]=.79
    write_image(folder/f'{palette}-basecolor.png',np.clip(color,0,1))
    write_image(folder/f'{palette}-roughness.png',np.clip(rough,0,1),False)
    write_image(folder/f'{palette}-emission.png',np.clip(emission,0,1))
    print('MUSHROOM_TEXTURE_READY',palette,flush=True)


def material(palette):
    mat=bpy.data.materials.new('Arkya | '+PALETTES[palette]['label']+' | painted 2K');mat.use_nodes=True;p=mat.node_tree.nodes.get('Principled BSDF')
    p.inputs['Metallic'].default_value=0;p.inputs['Roughness'].default_value=.73;p.inputs['Specular IOR Level'].default_value=.23;p.inputs['Emission Strength'].default_value=1
    for channel,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
        image=bpy.data.images.load(str(ROOT/'textures'/palette/f'{palette}-{channel}.png'),check_existing=True);image.colorspace_settings.name='Non-Color' if channel=='roughness' else 'sRGB';image.pack()
        node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=image;mat.node_tree.links.new(node.outputs['Color'],p.inputs[socket])
    return mat


class Sculpture:
    def __init__(self):self.vertices=[];self.faces=[];self.uv=[]
    def add(self,point):self.vertices.append(tuple(point));return len(self.vertices)-1
    def face(self,indices,uv):self.faces.append(tuple(indices));self.uv.append(uv)
    def radial_uv(self,r,theta,under=False):return (.25+.235*r*math.cos(theta),(.75 if under else .25)+.235*r*math.sin(theta))
    def cap(self,centre,radius,rise,style,seed,tilt=(0,0),ratio=.91):
        rng=random.Random(seed);phase=rng.random()*math.tau;n=96;start=len(self.vertices)
        rotation=Matrix.Rotation(tilt[0],3,'X')@Matrix.Rotation(tilt[1],3,'Y')
        origin=Vector(centre)
        def tex(r,theta,under=False):return self.radial_uv(r,theta if under else theta+phase,under)
        # A closed revolved profile with a rolled lip; all rings remain gently noncircular.
        top_r=[.045,.13,.23,.34,.45,.56,.66,.75,.83,.90,.95,.982,1.0]
        profiles=[]
        for r in top_r:
            if style=='button':z=rise*math.sqrt(max(0,1-r*r))
            elif style=='funnel':z=rise*(.10+.90*r**1.7)
            elif style=='parasol':z=rise*(.36*(1-r)**.5+.64*math.exp(-(r/.31)**2))
            else:z=rise*(1-r*r)**.67
            profiles.append((r,z,False))
        profiles.extend([(1.006,-radius*.018,False),(.994,-radius*.05,True),(.97,-radius*.074,True)])
        for r in [.90,.80,.69,.57,.45,.33,.22,.11,.045]:
            z=-radius*.075+radius*.10*(1-r)
            if style=='funnel':z=rise*(.10+.90*r**1.7)-radius*(.045+.030*r)
            profiles.append((r,z,True))
        if style=='funnel':
            # Shift the rolled edge/underside to the same high rim as the upper funnel.
            for i in range(len(top_r),len(top_r)+3):
                r,z,under=profiles[i]
                profiles[i]=(r,z+rise,under)
        for ri,(r,z,under) in enumerate(profiles):
            for j in range(n):
                theta=j*math.tau/n
                wave=.045*math.sin(theta*3+phase)+.022*math.sin(theta*5-phase)+.015*math.cos(theta*2+.4)
                if style=='funnel':wave+=.075*math.sin(theta*6+phase)
                rad=radius*r*(1+wave*r**.8)
                bump=radius*(.025*math.sin(theta*3+phase)+.012*math.sin(theta*7-.4))*r**1.4
                if style=='funnel':bump+=radius*.065*math.sin(theta*5+phase)*r*r
                gill=0
                if under and r<.97:
                    gill=-radius*.020*(.5+.5*math.cos(theta*24))**2*math.sin(r*math.pi)**.65
                local=Vector((rad*math.cos(theta),rad*math.sin(theta)*ratio,z+bump+gill))
                self.add(origin+rotation@local)
        for ri in range(len(profiles)-1):
            r0,z0,u0=profiles[ri];r1,z1,u1=profiles[ri+1]
            for j in range(n):
                nxt=(j+1)%n;t0=j*math.tau/n;t1=(j+1)*math.tau/n
                indices=[start+ri*n+j,start+ri*n+nxt,start+(ri+1)*n+nxt,start+(ri+1)*n+j]
                under=u0 or u1
                self.face(indices,[tex(r0,t0,under),tex(r0,t1,under),tex(r1,t1,under),tex(r1,t0,under)])
        for end in (0,len(profiles)-1):
            r,z,under=profiles[end]
            tip_z=(rise if style!='funnel' else rise*.10) if end==0 else (rise*.10-radius*.045 if style=='funnel' else radius*.025)
            center=self.add(origin+rotation@Vector((0,0,tip_z)))
            for j in range(n):
                nxt=(j+1)%n;t0=j*math.tau/n;t1=(j+1)*math.tau/n
                self.face([start+end*n+j,start+end*n+nxt,center],[tex(r,t0,under),tex(r,t1,under),tex(0,0,under)])

    def stem(self,base,height,radius,bend,seed,collar=False):
        rng=random.Random(seed);phase=rng.random()*6;segments=20;rings=20;start=len(self.vertices);origin=Vector(base)
        def centre(t):return origin+Vector((bend[0]*math.sin(t*math.pi*.5),bend[1]*t*t,height*t))
        for ring in range(rings):
            t=ring/(rings-1);c=centre(t)
            tangent=(centre(min(1,t+.001))-centre(max(0,t-.001))).normalized();a=Vector((1,0,0));a=(a-tangent*a.dot(tangent)).normalized();b=tangent.cross(a)
            width=radius*(.85+.42*math.exp(-((t-.08)/.20)**2)-.14*math.sin(t*math.pi)+.09*t)
            for j in range(segments):
                theta=j*math.tau/segments;w=width*(1+.025*math.sin(theta*5+phase+t*2))
                self.add(c+a*(math.cos(theta)*w)+b*(math.sin(theta)*w))
        for ring in range(rings-1):
            for j in range(segments):
                nxt=(j+1)%segments;t0=ring/(rings-1);t1=(ring+1)/(rings-1);u0=j/segments;u1=(j+1)/segments
                self.face([start+ring*segments+j,start+ring*segments+nxt,start+(ring+1)*segments+nxt,start+(ring+1)*segments+j],[(.53+.44*u0,.03+.44*t0),(.53+.44*u1,.03+.44*t0),(.53+.44*u1,.03+.44*t1),(.53+.44*u0,.03+.44*t1)])
        for ring in [0,rings-1]:
            t=ring/(rings-1);center=self.add(centre(t))
            for j in range(segments):
                nxt=(j+1)%segments
                self.face([start+ring*segments+j,start+ring*segments+nxt,center],[(.55,.04+.42*t),(.95,.04+.42*t),(.75,.04+.42*t)])
        if collar:self.collar(centre(.62),radius,seed)
        return centre(1)

    def collar(self,centre,radius,seed):
        n=48;start=len(self.vertices)
        profile=[(.82,.90),(1.04,.70),(1.65,.22),(2.02,-.04),(2.10,-.20),(1.92,-.31),(1.50,-.10),(1.0,.43)]
        for k,(r,z) in enumerate(profile):
            for j in range(n):
                a=j*math.tau/n;fold=.07*math.sin(a*12+.6);rr=radius*r*(1+fold)
                self.add(Vector(centre)+Vector((rr*math.cos(a),rr*math.sin(a),radius*z+radius*.11*math.sin(a*5))))
        for k in range(len(profile)):
            nk=(k+1)%len(profile)
            for j in range(n):
                nj=(j+1)%n
                self.face([start+k*n+j,start+k*n+nj,start+nk*n+nj,start+nk*n+j],[(.53+.44*j/n,.53+.44*k/len(profile)),(.53+.44*(j+1)/n,.53+.44*k/len(profile)),(.53+.44*(j+1)/n,.53+.44*(k+1)/len(profile)),(.53+.44*j/n,.53+.44*(k+1)/len(profile))])


def mushroom(shape,mat):
    sculpture=Sculpture()
    # base, stem height, stem radius, bend, cap radius/rise/style/tilt/ellipse, collar
    if shape=='boutons':
        specs=[((-.07,.02,0),.082,.024,(.013,-.004),.081,.072,'button',(.05,-.10),.92,False),((.075,.04,0),.105,.026,(-.013,.008),.088,.077,'button',(-.08,.14),.94,False),((-.008,-.065,0),.051,.020,(.008,-.006),.061,.058,'button',(.08,.13),.89,False),((.14,-.07,0),.035,.014,(.002,-.003),.042,.040,'button',(.1,-.09),.95,False),((-.135,-.056,0),.039,.014,(-.008,0),.045,.045,'button',(-.08,-.08),.89,False)]
    elif shape=='amanite':
        specs=[((.018,.015,0),.265,.045,(-.028,.014),.232,.130,'dome',(.10,-.10),.90,True),((.205,-.08,0),.100,.023,(.006,-.012),.096,.070,'button',(.06,.17),.90,False)]
    elif shape=='chanterelles':
        specs=[((-.11,.025,0),.38,.037,(-.08,.02),.225,.115,'funnel',(.08,-.18),.85,False),((.13,.06,0),.43,.044,(.08,.045),.255,.125,'funnel',(-.12,.22),.91,False),((.04,-.15,0),.25,.032,(.03,-.08),.175,.085,'funnel',(.16,.12),.87,False)]
    else:
        specs=[((0,.04,0),.78,.054,(-.095,.045),.49,.16,'parasol',(.095,-.12),.89,True),((.28,-.12,0),.28,.031,(.065,-.035),.18,.12,'dome',(.06,.22),.88,True),((-.30,-.075,0),.17,.027,(-.035,-.025),.13,.11,'button',(.13,-.12),.94,False)]
    for i,(base,h,w,bend,r,rise,style,tilt,ratio,collar) in enumerate(specs):
        top=sculpture.stem(base,h,w,bend,801+i*73,collar)
        sculpture.cap(top,r,rise,style,390+i*107,tilt,ratio)
    mesh=bpy.data.meshes.new('Sculpted caps, rolled lips, lamellae and curved stems');mesh.from_pydata(sculpture.vertices,[],sculpture.faces);mesh.materials.append(mat)
    layer=mesh.uv_layers.new(name='Painted_Mushroom_UV')
    for polygon,coords in zip(mesh.polygons,sculpture.uv):
        for loop,coord in zip(polygon.loop_indices,coords):layer.data[loop].uv=coord
    bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bmesh.ops.triangulate(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
    for polygon in mesh.polygons:polygon.use_smooth=True
    low=Vector(tuple(min(v.co[i] for v in mesh.vertices) for i in range(3)));high=Vector(tuple(max(v.co[i] for v in mesh.vertices) for i in range(3)))
    factor=SHAPES[shape]['height']/(high.z-low.z);offset=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
    for v in mesh.vertices:v.co=(v.co-offset)*factor
    obj=bpy.data.objects.new(SHAPES[shape]['label'],mesh);collection=bpy.data.collections.new('Arkya_Mushrooms');bpy.context.scene.collection.children.link(collection);collection.objects.link(obj)
    obj['mushroom_count']=len(specs);obj['root_pivot']='XY centre, Z floor';obj['family']='champignons'
    return obj,(high-low)*factor


def studio(obj,dims,path,draft=False,gpu=False):
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=12 if draft else 32
    scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU';scene.render.threads_mode='FIXED';scene.render.threads=8
    if gpu:
        preferences=bpy.context.preferences.addons['cycles'].preferences;preferences.compute_device_type='OPTIX';preferences.get_devices()
        for device in preferences.devices:device.use=device.type=='OPTIX'
    scene.render.resolution_x=760 if draft else 1100;scene.render.resolution_y=scene.render.resolution_x;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Neutral studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    extent=max(dims);h=dims.z
    bpy.ops.mesh.primitive_plane_add(size=extent*200,location=(0,0,-.002));floor=bpy.context.object;floor.name='Studio floor | not exported'
    mat=bpy.data.materials.new('Studio sage');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.23,.265,.235,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
    for name,pos,energy,size in [('Broad key',(-3,-4,5),650,4),('Fill',(4,-1,2.4),300,3),('Soft rim',(1,3,4),480,3)]:
        light=bpy.data.lights.new(name,'AREA');light.energy=energy*extent**2;light.shape='DISK';light.size=size*extent
        o=bpy.data.objects.new(name,light);scene.collection.objects.link(o);o.location=Vector(pos)*extent;o.rotation_euler=(Vector((0,0,h*.40))-o.location).to_track_quat('-Z','Y').to_euler()
    camera_data=bpy.data.cameras.new('Preview camera');camera=bpy.data.objects.new('Preview camera',camera_data);scene.collection.objects.link(camera);scene.camera=camera
    camera.location=(extent*1.55,-extent*4,extent*1.95);target=Vector((0,0,h*.43));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();camera_data.type='ORTHO';camera_data.ortho_scale=max(h,dims.x,dims.y)*1.27
    scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def build(shape,palette,args):
    bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
    obj,dims=mushroom(shape,material(palette));stem=shape+'-'+palette;folder=ROOT/'assets'/stem;folder.mkdir(parents=True,exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
    bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_lights=False,export_cameras=False,export_tangents=True)
    triangles=sum(len(p.vertices)-2 for p in obj.data.polygons);assert triangles<=30000,(stem,triangles)
    scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene['arkya_resource']='champignons';scene['source_script']='scripts/generate_mushrooms.py'
    text=bpy.data.texts.new('Arkya provenance');text.write('Original sculpted continuous mushroom caps with rolled edges and raised underside lamellae, closed curved tapered stems, optional flared collar. Deterministic geometry across palettes. Painted procedural 2K atlas, source script scripts/generate_mushrooms.py. Opaque PBR, no external asset, no alpha card, no ground pedestal. Metres, XY-centred ground pivot. Artistic status: proposed, awaiting user review.')
    if args.render:studio(obj,dims,folder/(stem+'-preview.png'),args.draft,args.gpu)
    bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
    record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],'glb':(folder/(stem+'.glb')).relative_to(ROOT).as_posix(),'blend':(folder/(stem+'.blend')).relative_to(ROOT).as_posix(),'preview':(folder/(stem+'-preview.png')).relative_to(ROOT).as_posix() if (folder/(stem+'-preview.png')).exists() else None,'height':round(float(dims.z),4),'width':round(float(dims.x),4),'depth':round(float(dims.y),4),'triangles':triangles,'mushrooms':obj['mushroom_count'],'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol'}
    (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
    path=ROOT/'manifest.json';old=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {};records=[a for a in old.get('assets',[]) if a['id']!=stem]+[record]
    manifest={'family':'champignons','artistic_status':'proposed','shapes':[dict(id=k,**v) for k,v in SHAPES.items()],'materials':[{'id':k,'label':v['label'],'swatch':v['swatch'],'description':v['description']} for k,v in PALETTES.items()],'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material'])))}
    path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8');print('MUSHROOM_READY',json.dumps(record,ensure_ascii=False),flush=True)


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--shape',choices=['all']+list(SHAPES),default='amanite');parser.add_argument('--material',choices=['all']+list(PALETTES),default='corail')
    parser.add_argument('--render',action='store_true');parser.add_argument('--draft',action='store_true');parser.add_argument('--gpu',action='store_true');parser.add_argument('--rebake',action='store_true');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    bpy.ops.wm.read_factory_settings(use_empty=True);palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
    for palette in palettes:make_textures(palette,args.rebake)
    for shape in shapes:
        for palette in palettes:build(shape,palette,args)


if __name__=='__main__':main()
