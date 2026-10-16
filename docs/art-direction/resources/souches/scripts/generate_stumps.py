"""Arkya stumps: continuous buttress roots, softly carved cuts and painted V3 bark."""
import argparse, hashlib, json, math, random, sys
from pathlib import Path
import bpy, bmesh
import numpy as np
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'references/arbre-peint-v3/source/painted-bark.png'
SIZE=2048
SHAPES={
 'basse':{'label':'Souche basse','height':.35,'description':'Coupe large et basse, six racines en contreforts et cernes décentrés.'},
 'tordue':{'label':'Souche tordue','height':.65,'description':'Tronc courbé, coupe oblique et ancien départ de branche latéral.'},
 'creuse':{'label':'Souche creuse','height':.95,'description':'Large ouverture réelle, paroi épaisse et cavité profonde au fond fermé.'},
 'brisee':{'label':'Vieille souche brisée','height':1.35,'description':'Silhouette haute fendue en pans arrondis, entaille et racines irrégulières.'}}
PALETTES={
 'moussu':{'label':'Ancien moussu','swatch':'#836b3d','description':'Écorce miel et brun, mousse verte localisée, bois de coupe chaud.'},
 'mystique':{'label':'Mystique','swatch':'#6b596a','description':'Brun violet feutré, creux froids et accents cyan contenus.'},
 'brule':{'label':'Bois brûlé','swatch':'#49434b','description':'Charbon aux ombres colorées, coupe sombre et quelques fines braises.'}}

def smooth(a,b,x):
 t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

def ramp(x,stops):
 out=np.empty(x.shape+(3,),np.float32)
 for ch in range(3):out[...,ch]=np.interp(x,[p[0] for p in stops],[p[1][ch] for p in stops])
 return out

def image_save(path,pixels,color=True):
 im=bpy.data.images.new(path.stem,SIZE,SIZE,alpha=False);im.colorspace_settings.name='sRGB' if color else 'Non-Color'
 out=np.ones((SIZE,SIZE,4),np.float32);v=pixels if pixels.ndim==3 else pixels[...,None]
 if color:v=np.where(v<=.0031308,v*12.92,1.055*np.maximum(v,0)**(1/2.4)-.055)
 out[...,:3]=v;im.pixels.foreach_set(out.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();bpy.data.images.remove(im)

def make_textures(palette,force=False):
 folder=ROOT/'textures'/palette;folder.mkdir(parents=True,exist_ok=True)
 if not force and all((folder/f'{part}-{c}.png').exists() for part in ('bark','cut') for c in ('basecolor','roughness','emission')):return
 im=bpy.data.images.load(str(SOURCE));im.scale(SIZE,SIZE);pixels=np.empty(SIZE*SIZE*4,np.float32);im.pixels.foreach_get(pixels);bpy.data.images.remove(im)
 rgb=pixels.reshape(SIZE,SIZE,4)[...,:3];linear=np.where(rgb<=.04045,rgb/12.92,((rgb+.055)/1.055)**2.4)
 lum=linear@np.array([.2126,.7152,.0722]);low,high=np.percentile(lum,[3,97]);grain=np.clip((lum-low)/(high-low),0,1)
 y,x=np.mgrid[0:SIZE,0:SIZE].astype(np.float32);x/=SIZE;y/=SIZE
 # The source's broad ribbons remain intact; accents follow sparse low grain regions.
 region=np.maximum(np.exp(-(((x-.22)/.105)**2+((y-.26)/.24)**2)),np.exp(-(((x-.70)/.085)**2+((y-.64)/.23)**2)))
 accent=(1-smooth(.10,.30,grain))*region
 if palette=='moussu':
  color=ramp(grain,[(0,(.065,.041,.023)),(.22,(.13,.076,.035)),(.48,(.25,.155,.065)),(.76,(.41,.285,.12)),(1,(.55,.41,.21))])
  mossfield=.49+.17*np.sin(x*math.tau*3+.75)+.08*np.cos(y*9+x*14)+.11*(1-y)+grain*.10
  moss=smooth(.65,.78,mossfield)*(.95-.6*smooth(.32,.88,y))
  mosscolor=ramp(grain,[(0,(.035,.069,.027)),(.45,(.12,.205,.065)),(1,(.28,.345,.15))]);color=color*(1-moss[...,None])+mosscolor*moss[...,None]
  rough=.76+.12*moss;emission=np.zeros_like(color)
 elif palette=='mystique':
  color=ramp(grain,[(0,(.038,.024,.041)),(.20,(.075,.043,.066)),(.46,(.155,.089,.112)),(.72,(.265,.167,.17)),(1,(.39,.285,.24))])
  color=color*(1-accent[...,None]*.86)+np.array((.055,.32,.26))*accent[...,None]*.86
  rough=.87-grain*.18;emission=accent[...,None]*np.array((.012,.085,.060))
 else:
  color=ramp(grain,[(0,(.017,.020,.029)),(.22,(.035,.042,.061)),(.48,(.073,.079,.102)),(.76,(.14,.14,.16)),(1,(.23,.20,.18))])
  color=color*(1-accent[...,None]*.65)+np.array((.33,.12,.025))*accent[...,None]*.65
  rough=.87-grain*.13;emission=accent[...,None]*np.array((.13,.028,.004))
 for name,data,c in [('basecolor',color,True),('roughness',rough,False),('emission',emission,True)]:image_save(folder/f'bark-{name}.png',data,c)
 # Off-centred annual rings, broad washes and only three tapered radial checks.
 u=(x-.46)/.47;v=(y-.53)/.47;angle=np.arctan2(v,u);r=np.sqrt((u*1.06)**2+(v*.96)**2)
 warped=r*(1+.025*np.sin(angle*3+1.4)+.014*np.sin(angle*7-r*8))+.009*np.sin(angle*2+r*9)
 phase=warped*math.tau*7.6+.30*np.sin(warped*7)
 rings=(.5+.5*np.cos(phase))**13
 lip=(.5+.5*np.cos(phase-.35))**20
 wash=.045*np.sin(u*5+v*3)+.025*np.sin(u*17-v*9)
 edge=smooth(.82,1.01,r)
 if palette=='moussu':wood=np.array((.56,.34,.135));dark=np.array((.28,.125,.045));light=np.array((.67,.46,.23))
 elif palette=='mystique':wood=np.array((.34,.24,.28));dark=np.array((.13,.075,.15));light=np.array((.46,.36,.39))
 else:wood=np.array((.13,.115,.11));dark=np.array((.035,.027,.034));light=np.array((.23,.18,.15))
 color=wood[None,None,:]*(1+wash[...,None])
 color=color*(1-rings[...,None]*.36)+dark*rings[...,None]*.36
 color=color*(1-lip[...,None]*.12)+light*lip[...,None]*.12
 color=color*(1-edge[...,None]*.17)+dark*edge[...,None]*.17
 cracks=np.zeros_like(r)
 for a,start in [(2.6,.64),(-.6,.50),(-2.0,.76)]:
  da=np.arctan2(np.sin(angle-a-.035*np.sin(r*11)),np.cos(angle-a-.035*np.sin(r*11)))
  width=.008*smooth(start,.98,r);line=np.exp(-(da/(width+.0001))**2)*smooth(start,start+.09,r)
  cracks=np.maximum(cracks,line)
 color=color*(1-cracks[...,None]*.76)+dark*cracks[...,None]*.76
 emission=np.zeros_like(color)
 if palette=='mystique':emission=cracks[...,None]*np.array((.008,.035,.027))
 elif palette=='brule':emission=cracks[...,None]*np.array((.045,.008,.001))
 image_save(folder/'cut-basecolor.png',color);image_save(folder/'cut-roughness.png',np.full_like(r,.81),False);image_save(folder/'cut-emission.png',emission)
 print('STUMP_TEXTURE_READY',palette,flush=True)

def material(palette,part):
 mat=bpy.data.materials.new('Arkya | '+PALETTES[palette]['label']+' | '+part+' 2K');mat.use_nodes=True;p=mat.node_tree.nodes.get('Principled BSDF')
 p.inputs['Metallic'].default_value=0;p.inputs['Specular IOR Level'].default_value=.21;p.inputs['Emission Strength'].default_value=1
 for channel,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
  im=bpy.data.images.load(str(ROOT/'textures'/palette/f'{part}-{channel}.png'),check_existing=True);im.colorspace_settings.name='Non-Color' if channel=='roughness' else 'sRGB';im.pack()
  tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im;mat.node_tree.links.new(tex.outputs['Color'],p.inputs[socket])
 return mat

class Sculpture:
 def __init__(self):self.vertices=[];self.faces=[];self.uv=[];self.mats=[]
 def add(self,p):self.vertices.append(tuple(p));return len(self.vertices)-1
 def face(self,inds,uv,mat=0):self.faces.append(tuple(inds));self.uv.append(uv);self.mats.append(mat)
 def bridge(self,a,b,ua,ub,mat=0):
  n=len(a)
  for j in range(n):self.face((a[j],a[(j+1)%n],b[(j+1)%n],b[j]),(ua[j],ua[j+1],ub[j+1],ub[j]),mat)
 def cutuv(self,r,a):return (.5+.475*r*math.cos(a),.5+.475*r*math.sin(a))
 def stump(self,shape):
  h=SHAPES[shape]['height'];cfg={'basse':(.29,.26,.13),'tordue':(.27,.28,.16),'creuse':(.40,.30,.20),'brisee':(.42,.36,.24)}[shape]
  radius,rootlength,roott=cfg;n=128;phase={'basse':.4,'tordue':1.2,'creuse':2.1,'brisee':2.8}[shape]
  hollow=shape in ('creuse','brisee')
  def centre(t):
   if shape=='tordue':return Vector((.14*math.sin(t*1.9),-.055*t*t,h*t))
   if shape=='brisee':return Vector((-.08*t+.035*math.sin(t*3),.06*t*t,h*t))
   return Vector((.025*math.sin(t*2),-.015*t,h*t))
  def top(a):
   z=.013*math.sin(a*3+phase)+.008*math.sin(a*5-.4)
   if shape=='tordue':z+=.095*math.cos(a+.5)
   if shape=='brisee':z+=.11*math.sin(a*3+.3)+.06*math.sin(a*5-1)-.22*math.exp(-((math.atan2(math.sin(a+.9),math.cos(a+.9)))/.43)**2)
   if shape=='creuse':z+=.04*math.sin(a*2+.3)+.018*math.sin(a*5)
   return z
  def rad(t,a):
   twist=a+.20*t*math.sin(phase)
   basic=radius*(1-.16*t+.045*math.sin(t*5))*(1+.065*math.sin(twist*3+phase)+.035*math.sin(twist*5-.7)+.018*math.sin(twist*9+t*2))
   lobes=0
   for k in range(6):
    rootangle=k*math.tau/6+.10*math.sin(k*3.4+phase);delta=math.atan2(math.sin(a-rootangle),math.cos(a-rootangle))
    lobes+=math.exp(-(delta/(.17+.025*math.sin(k*2)))**2)*(1+.16*math.sin(k*4+phase))
   return basic+rootlength*lobes*math.exp(-((t/roott)**1.16))
  rings=[];uvrings=[]
  heights=[0,.009,.025]+[.045+i*.905/30 for i in range(31)]+[.978,.992,1]
  for t in heights:
   row=[]
   for j in range(n):
    a=j*math.tau/n;c=centre(t);r=rad(t,a)*(1-.025*(1-t/.009) if t<.009 else 1)
    z=c.z+top(a)*t**4
    row.append(self.add((c.x+r*math.cos(a),c.y+r*math.sin(a),z)))
   uv=[(j/n,.05+.90*t) for j in range(n+1)]
   if rings:self.bridge(rings[-1],row,uvrings[-1],uv)
   rings.append(row);uvrings.append(uv)
  bottom=self.add((0,0,0))
  for j in range(n):self.face((bottom,rings[0][(j+1)%n],rings[0][j]),((.5,.5),self.cutuv(1,(j+1)*math.tau/n),self.cutuv(1,j*math.tau/n)),1)
  # Rounded lip travels from bark around the cut into an actual cavity when present.
  previous=rings[-1];previousuv=[self.cutuv(1,j*math.tau/n) for j in range(n+1)]
  cutratios=[1.002,.998,.986,.965,.93,.88,.82,.76] if hollow else [1.002,.998,.986,.965,.93]+[.89-i*.85/25 for i in range(26)]
  for ri,rfrac in enumerate(cutratios):
   row=[];uv=[self.cutuv(rfrac,j*math.tau/n) for j in range(n+1)]
   for j in range(n):
    a=j*math.tau/n;c=centre(1);r=rad(1,a)*rfrac
    bevel=.014*math.sin(min(1,max(0,(1.012-rfrac)/.065))*math.pi*.5)
    z=h+top(a)*((.94+.06*rfrac) if hollow else rfrac)+bevel
    if not hollow:z-=.006*(1-rfrac)+.0012*math.sin(rfrac*math.tau*7.6)
    row.append(self.add((c.x+r*math.cos(a),c.y+r*math.sin(a),z)))
   if ri<2:
    self.bridge(previous,row,[(j/n,.95+ri*.004) for j in range(n+1)],[(j/n,.954+ri*.004) for j in range(n+1)],0)
   else:self.bridge(previous,row,previousuv,uv,1)
   previous=row;previousuv=uv
  if hollow:
   # Concave wall with undercut at the rim and a closed dark bowl at the bottom.
   for step in range(1,20):
    t=step/19;row=[];uv=[(j/n,.95-.87*t) for j in range(n+1)]
    for j in range(n):
     a=j*math.tau/n;c=centre(1-t*.68);rr=rad(1,a)*(.76-.045*math.sin(t*math.pi)-.19*t**5)
     z=(h+top(a))*(1-t)+h*.18*t
     row.append(self.add((c.x+rr*math.cos(a),c.y+rr*math.sin(a),z)))
    self.bridge(previous,row,[(j/n,.95-.87*(step-1)/19) for j in range(n+1)],uv,0);previous=row
   for k in range(1,12):
    f=1-k/12;row=[];uv=[self.cutuv(f,j*math.tau/n) for j in range(n+1)]
    for j in range(n):
     a=j*math.tau/n;c=centre(.32);rr=rad(1,a)*.57*f;z=h*.18-.045*math.sin((1-f)*math.pi*.5)
     row.append(self.add((c.x+rr*math.cos(a),c.y+rr*math.sin(a),z)))
    self.bridge(previous,row,[self.cutuv(f+1/12,j*math.tau/n) for j in range(n+1)],uv,1);previous=row;previousuv=uv
   c=centre(.32);center=self.add((c.x,c.y,h*.18-.045))
  else:
   c=centre(1);center=self.add((c.x,c.y,h+.014-.006))
  for j in range(n):self.face((previous[j],previous[(j+1)%n],center),(previousuv[j],previousuv[j+1],(.5,.5)),1)
  if shape=='tordue':self.branch((.15,.07,.32),(.40,.24,.53),.105,phase)
  elif shape=='brisee':self.branch((-.20,.06,.60),(-.48,.17,.84),.13,phase)
 def branch(self,start,end,radius,phase):
  a=Vector(start);b=Vector(end);axis=(b-a).normalized();side=axis.cross(Vector((0,1,0))).normalized();across=axis.cross(side).normalized();n=64;previous=None
  rings=[]
  for k in range(19):
   t=k/18;centre=a+(b-a)*t+Vector((0,0,.035*math.sin(t*math.pi)));r=radius*(1-.37*t);row=[]
   for j in range(n):
    theta=j*math.tau/n;rr=r*(1+.055*math.sin(theta*5+t*2));row.append(self.add(centre+rr*(side*math.cos(theta)+across*math.sin(theta))))
   uv=[(.08+j/n*.78,.24+t*.57) for j in range(n+1)]
   if previous:self.bridge(previous,row,pv,uv)
   previous=row;pv=uv;rings.append(row)
  c=self.add(a)
  for j in range(n):self.face((c,rings[0][(j+1)%n],rings[0][j]),((.5,.5),self.cutuv(1,(j+1)*math.tau/n),self.cutuv(1,j*math.tau/n)),1)
  previous=rings[-1];previousuv=[self.cutuv(1,j*math.tau/n) for j in range(n+1)]
  for k in range(1,14):
   f=1-k/14;row=[];uv=[self.cutuv(f,j*math.tau/n) for j in range(n+1)]
   for j in range(n):
    theta=j*math.tau/n;rr=radius*.63*f*(1+.055*math.sin(theta*5+2));row.append(self.add(b+(side*math.cos(theta)+across*math.sin(theta))*rr+axis*.004*math.sin(k/13*math.pi)))
   self.bridge(previous,row,previousuv,uv,1);previous=row;previousuv=uv
  c=self.add(b)
  for j in range(n):self.face((previous[j],previous[(j+1)%n],c),(previousuv[j],previousuv[j+1],(.5,.5)),1)

def stump(shape,palette):
 s=Sculpture();s.stump(shape);mesh=bpy.data.meshes.new('Sculpted stump continuous roots');mesh.from_pydata(s.vertices,[],s.faces);mesh.update()
 bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
 # Recalculated normals preserve polygons and their corners for explicit UV assignment.
 layer=mesh.uv_layers.new(name='PaintedUV')
 for poly,face,uv,mi in zip(mesh.polygons,s.faces,s.uv,s.mats):
  lookup={vi:coord for vi,coord in zip(face,uv)}
  for li in poly.loop_indices:layer.data[li].uv=lookup[mesh.loops[li].vertex_index]
  poly.material_index=mi;poly.use_smooth=True
 for part in ('bark','cut'):mesh.materials.append(material(palette,part))
 verts=np.array([v.co[:] for v in mesh.vertices]);low=verts.min(0);high=verts.max(0);scale=SHAPES[shape]['height']/(high[2]-low[2]);offset=np.array([(low[0]+high[0])/2,(low[1]+high[1])/2,low[2]])
 for vertex in mesh.vertices:vertex.co=(Vector(vertex.co)-Vector(offset))*scale
 collection=bpy.data.collections.new('Arkya_Stumps');bpy.context.scene.collection.children.link(collection)
 obj=bpy.data.objects.new(SHAPES[shape]['label'],mesh);collection.objects.link(obj);bpy.context.view_layer.update()
 return obj,obj.dimensions.copy()

def studio(obj,dims,path,draft=False,gpu=False):
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16 if draft else 32;scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU';scene.render.threads_mode='FIXED';scene.render.threads=6
 if gpu:
  prefs=bpy.context.preferences.addons['cycles'].preferences;prefs.compute_device_type='OPTIX';prefs.get_devices()
  for device in prefs.devices:device.use=device.type=='OPTIX'
 scene.render.resolution_x=700 if draft else 1100;scene.render.resolution_y=scene.render.resolution_x;scene.render.resolution_percentage=100
 scene.world=bpy.data.worlds.new('Neutral studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
 extent=max(dims);h=dims.z;bpy.ops.mesh.primitive_plane_add(size=extent*200,location=(0,0,-.002));floor=bpy.context.object;floor.name='Studio floor | not exported'
 mat=bpy.data.materials.new('Studio sage');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.23,.265,.235,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
 for name,pos,energy,size in [('Broad key',(-3,-4,5),650,4),('Fill',(4,-1,2.4),300,3),('Soft rim',(1,3,4),480,3)]:
  data=bpy.data.lights.new(name,'AREA');data.energy=energy*extent**2;data.shape='DISK';data.size=size*extent;o=bpy.data.objects.new(name,data);scene.collection.objects.link(o);o.location=Vector(pos)*extent;o.rotation_euler=(Vector((0,0,h*.4))-o.location).to_track_quat('-Z','Y').to_euler()
 data=bpy.data.cameras.new('Preview camera');camera=bpy.data.objects.new('Preview camera',data);scene.collection.objects.link(camera);scene.camera=camera
 camera.location=(extent*1.7,-extent*3.8,extent*2.5);target=Vector((0,0,h*.41));camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=max(dims)*1.28
 scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)

def build(shape,palette,args):
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
 obj,dims=stump(shape,palette);stem=shape+'-'+palette;folder=ROOT/'assets'/stem;folder.mkdir(parents=True,exist_ok=True)
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_lights=False,export_cameras=False,export_tangents=True)
 triangles=sum(len(p.vertices)-2 for p in obj.data.polygons);assert triangles<=35000,(stem,triangles)
 scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene['arkya_resource']='souches';scene['source_script']='scripts/generate_stumps.py'
 text=bpy.data.texts.new('Arkya provenance');text.write('Sculpted continuous stump and buttress roots with soft cut, real cavity on hollow/broken variants. Deterministic geometry/UV across palettes. Bark derived from approved V3 source (read-only), separate painted 2K end grain. Source script scripts/generate_stumps.py. Opaque PBR, two materials, metre scale, XY-centred ground pivot. Artistic status proposed; awaiting user review.')
 if args.render:studio(obj,dims,folder/(stem+'-preview.png'),args.draft,args.gpu)
 bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
 record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],'glb':(folder/(stem+'.glb')).relative_to(ROOT).as_posix(),'blend':(folder/(stem+'.blend')).relative_to(ROOT).as_posix(),'preview':(folder/(stem+'-preview.png')).relative_to(ROOT).as_posix() if (folder/(stem+'-preview.png')).exists() else None,'height':round(float(dims.z),4),'width':round(float(dims.x),4),'depth':round(float(dims.y),4),'triangles':triangles,'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol'}
 (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
 path=ROOT/'manifest.json';old=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {};records=[a for a in old.get('assets',[]) if a['id']!=stem]+[record]
 manifest={'family':'souches','artistic_status':'proposed','shapes':[dict(id=k,**v) for k,v in SHAPES.items()],'materials':[dict(id=k,**v) for k,v in PALETTES.items()],'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material'])))}
 path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8');print('STUMP_READY',json.dumps(record,ensure_ascii=False),flush=True)

def main():
 p=argparse.ArgumentParser();p.add_argument('--shape',choices=['all']+list(SHAPES),default='tordue');p.add_argument('--material',choices=['all']+list(PALETTES),default='moussu');p.add_argument('--render',action='store_true');p.add_argument('--draft',action='store_true');p.add_argument('--gpu',action='store_true');p.add_argument('--rebake',action='store_true');args=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
 palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
 for palette in palettes:make_textures(palette,args.rebake)
 for shape in shapes:
  for palette in palettes:build(shape,palette,args)

if __name__=='__main__':main()
