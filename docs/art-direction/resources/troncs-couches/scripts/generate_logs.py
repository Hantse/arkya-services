"""Closed sculpted fallen timber, UV-painted from Arkya's approved bark source."""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'references/arbre-peint-v3/source/painted-bark.png'
SIZE=2048
SHAPES={
 'billot':{'label':'Billot court','length':.9,'description':'Section trapue, deux coupes bombées à cernes décentrés et écorce arrondie.'},
 'noueux':{'label':'Tronc noueux','length':1.8,'description':'Tronc légèrement cambré, ventre irrégulier et moignon latéral intégré.'},
 'creux':{'label':'Tronc creux','length':2.8,'description':'Tube de bois épais, vraie cavité traversante et lèvres irrégulières.'},
 'fourchu':{'label':'Grand tronc fourchu','length':4.2,'description':'Grand bois ancien divisé en deux branches, avec extrémités rompues sculptées.'},
}
PALETTES={
 'moussu':{'label':'Ancien moussu','swatch':'#8d7948','description':'Écorce miel, cernes chauds et nappes de mousse peintes sur le dessus.','wood':(.48,.29,.115),'shadow':(.19,.075,.025),'rim':(.62,.42,.20)},
 'mystique':{'label':'Mystique','swatch':'#74648a','description':'Bois brun violet, creux bleutés et accents turquoise contenus.','wood':(.25,.18,.31),'shadow':(.075,.055,.12),'rim':(.43,.36,.48)},
 'brule':{'label':'Bois brûlé','swatch':'#4d4245','description':'Charbon violet brun, cœur calciné et braises fines au fond de quelques fibres.','wood':(.13,.078,.060),'shadow':(.035,.022,.027),'rim':(.24,.15,.095)},
}


def smooth(a,b,x):
 t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)


def write_image(path,pixels,color=True):
 im=bpy.data.images.new(path.stem,SIZE,SIZE,alpha=False);im.colorspace_settings.name='sRGB' if color else 'Non-Color'
 rgba=np.ones((SIZE,SIZE,4),np.float32);values=pixels if pixels.ndim==3 else pixels[:,:,None]
 if color:values=np.where(values<=.0031308,values*12.92,1.055*np.power(np.maximum(values,0),1/2.4)-.055)
 rgba[:,:,:3]=values;im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(path);im.file_format='PNG';im.save();bpy.data.images.remove(im)


def textures(palette,force=False):
 folder=ROOT/'textures'/palette;folder.mkdir(parents=True,exist_ok=True)
 if not force and all((folder/f'{palette}-{c}.png').exists() for c in ('basecolor','roughness','emission')):return
 source=bpy.data.images.load(str(SOURCE),check_existing=True);w,h=source.size;pix=np.empty(w*h*4,np.float32);source.pixels.foreach_get(pix);pix=pix.reshape(h,w,4)[:,:,:3]
 # The source file has painted colour; convert the stored sRGB pixels before palette work.
 pix=np.where(pix<=.04045,pix/12.92,((pix+.055)/1.055)**2.4)
 yy,xx=np.mgrid[0:SIZE,0:SIZE].astype(np.float32);x=xx/SIZE;y=yy/SIZE
 u=np.clip((x-.015)/.65,0,1);v=np.clip((y-.015)/.97,0,1)
 sx=np.minimum(w-1,(u*(w-1)).astype(np.int32));sy=np.minimum(h-1,(v*(h-1)).astype(np.int32));bark=pix[sy,sx]
 luminance=bark[:,:,0]*.30+bark[:,:,1]*.59+bark[:,:,2]*.11
 source_luminance=pix[:,:,0]*.30+pix[:,:,1]*.59+pix[:,:,2]*.11
 low,high=np.percentile(source_luminance,[3,97]);grain=np.clip((luminance-low)/(high-low),0,1)
 grooves=1-smooth(.13,.34,grain)
 accent_region=np.zeros_like(u)
 for cx,cy,rx,ry in [(.32,.22,.12,.10),(.62,.48,.13,.15),(.43,.78,.11,.14)]:
  island=np.exp(-(((u-cx)/rx)**2+((v-cy)/ry)**2))
  accent_region=np.maximum(accent_region,smooth(.25,.72,island))
 fiber_accent=grooves*accent_region
 if palette=='moussu':
  bark=bark*np.asarray((1.06,1.04,.90))[None,None,:]
 elif palette=='mystique':
  bark=luminance[:,:,None]*np.asarray((.78,.69,1.05))[None,None,:]+np.asarray((.015,.012,.028))[None,None,:]
 else:
  bark=luminance[:,:,None]*np.asarray((.28,.245,.27))[None,None,:]+np.asarray((.012,.010,.012))[None,None,:]
 # Broad irregular patches crown the cylinder; the bottom UV seam is kept quiet.
 crown=np.exp(-((u-.5)/(.13+.025*np.sin(v*17)))**4)
 islands=smooth(.02,.25,np.sin(v*13+u*5)*.30+np.cos(v*21-u*10)*.15+.10)
 moss=np.clip(crown*islands*.88,0,.9)
 emission=np.zeros((SIZE,SIZE,3),np.float32)
 if palette=='moussu':
  green=np.asarray((.13,.22,.040))[None,None,:]*(.75+luminance*.9)[:,:,None]
  bark=bark*(1-moss[:,:,None])+green*moss[:,:,None]
 elif palette=='mystique':
  glow=fiber_accent
  bark=bark*(1-glow[:,:,None]*.5)+np.asarray((.045,.30,.27))[None,None,:]*glow[:,:,None]*.5
  emission=glow[:,:,None]*np.asarray((.008,.075,.06))[None,None,:]
 else:
  glow=fiber_accent*(.65+.35*smooth(.05,.3,np.sin(v*18+u*9)))
  bark=bark*(1-glow[:,:,None]*.7)+np.asarray((.40,.065,.009))[None,None,:]*glow[:,:,None]*.7
  emission=glow[:,:,None]*np.asarray((.10,.011,.001))[None,None,:]
 color=bark.astype(np.float32);rough=np.full((SIZE,SIZE),.78,np.float32)
 p=PALETTES[palette]
 # Two complete radial wood islands, deliberately off-centre and slightly wavy.
 for cy,phase in [(.25,.5),(.75,2.2)]:
  a=(x-.837)/.145;b=(y-cy)/.225;dx=a+.10;dy=b-.08;r=np.sqrt(dx*dx+dy*dy);ang=np.arctan2(dy,dx)
  warped=r*(1+.024*np.sin(ang*3+phase)+.018*np.sin(ang*7-phase))+.014*np.sin(ang*2+r*8)
  rings=np.exp(-(np.sin(warped*math.pi*6.0)/.16)**2)
  ring_edge=np.exp(-(np.sin((warped-.016)*math.pi*6.0)/.26)**2)
  radial=.09*np.sin(ang*3+.4)+.04*np.cos(ang*7+r*8)
  wood=np.asarray(p['wood'])[None,None,:]*(1+radial-.12*r)[:,:,None]
  warm_heart=np.exp(-(((dx+.17)/.64)**2+((dy-.05)/.77)**2))
  wood*= (1-.10*warm_heart)[:,:,None]
  wood+=np.asarray(p['rim'])[None,None,:]*warm_heart[:,:,None]*np.asarray((.10,.035,.012))[None,None,:]
  wood=wood*(1-rings[:,:,None]*.55)+np.asarray(p['shadow'])[None,None,:]*rings[:,:,None]*.55
  wood+=np.asarray(p['rim'])[None,None,:]*ring_edge[:,:,None]*.09
  cracks=np.zeros_like(r)
  for angle,start,end in [(.75,.62,1.25),(3.30,.55,1.20),(5.15,.80,1.20)]:
   ad=np.arctan2(np.sin(ang-angle-.025*np.sin(r*18)),np.cos(ang-angle-.025*np.sin(r*18)))
   crack=(1-smooth(.007,.017,np.abs(ad)))*smooth(start,start+.09,r)*(1-smooth(end,end+.08,r));cracks=np.maximum(cracks,crack)
  wood=wood*(1-cracks[:,:,None]*.80)+np.asarray(p['shadow'])[None,None,:]*cracks[:,:,None]*.80
  edge=smooth(.88,1.04,np.sqrt(a*a+b*b));wood*= (1-.24*edge)[:,:,None]
  mask=(x>=.685)&((y<.5) if cy==.25 else (y>=.5));color[mask]=wood[mask];rough[mask]=.83;emission[mask]=0
 write_image(folder/f'{palette}-basecolor.png',np.clip(color,0,1))
 write_image(folder/f'{palette}-roughness.png',rough,False)
 write_image(folder/f'{palette}-emission.png',emission)
 print('LOG_TEXTURE_READY',palette,flush=True)


def material(palette):
 mat=bpy.data.materials.new('Arkya | '+PALETTES[palette]['label']+' | Painted wood 2K');mat.use_nodes=True;p=mat.node_tree.nodes.get('Principled BSDF')
 p.inputs['Metallic'].default_value=0;p.inputs['Specular IOR Level'].default_value=.22;p.inputs['Emission Strength'].default_value=1
 for channel,socket in [('basecolor','Base Color'),('roughness','Roughness'),('emission','Emission Color')]:
  im=bpy.data.images.load(str(ROOT/'textures'/palette/f'{palette}-{channel}.png'),check_existing=True);im.colorspace_settings.name='Non-Color' if channel=='roughness' else 'sRGB';im.pack()
  node=mat.node_tree.nodes.new('ShaderNodeTexImage');node.image=im;mat.node_tree.links.new(node.outputs['Color'],p.inputs[socket])
 return mat


class Timber:
 def __init__(self):self.vertices=[];self.faces=[];self.uv=[]
 def point(self,p):self.vertices.append(tuple(p));return len(self.vertices)-1
 def face(self,indices,uv):self.faces.append(tuple(indices));self.uv.append(uv)
 def tube(self,points,radii,seed=1,hollow=False,broken=False):
  controls=[Vector(p) for p in points];steps=36;n=64;phase=seed*.731
  def centre(t):
   # Catmull-Rom passes through the laid-out organic centreline.
   q=t*(len(controls)-1);i=min(len(controls)-2,int(q));f=q-i
   a=controls[max(0,i-1)];b=controls[i];c=controls[i+1];d=controls[min(len(controls)-1,i+2)]
   return .5*((2*b)+(-a+c)*f+(2*a-5*b+4*c-d)*f*f+(-a+3*b-3*c+d)*f*f*f)
  def radius(t):
   q=t*(len(radii)-1);i=min(len(radii)-2,int(q));f=q-i;return radii[i]*(1-f)+radii[i+1]*f
  def frame(t):
   c=centre(t);axis=(centre(min(1,t+.001))-centre(max(0,t-.001))).normalized();side=Vector((0,1,0));side=(side-axis*side.dot(axis)).normalized();up=axis.cross(side);return c,axis,side,up
  def rim_offset(theta,end):
   base=.025*math.sin(theta*3+phase)+.013*math.sin(theta*7+phase*.7)
   if broken and end:base+=.12*(max(0,math.cos(theta*5+phase))**4)-.025
   return base*radius(0 if end==0 else 1)
  def vertex(t,theta,scale=1,axial=0):
   c,axis,side,up=frame(t);r=radius(t);lobes=1+.047*math.sin(theta*5+phase+t*.9)+.022*math.cos(theta*9-phase+t*3)
   shoulder=.96+.04*min(1,t/.027,(1-t)/.027)
   rr=r*scale*lobes*shoulder;y=math.cos(theta)*rr;z=math.sin(theta)*rr*.88
   # Quiet flattened underside seats the timber instead of balancing it on a point.
   if z < -.72*r*scale:z=-.72*r*scale+(z+.72*r*scale)*.18
   shift=r*.03*math.sin(theta*3+t*5+phase)*math.sin(t*math.pi)
   return c+side*y+up*z+axis*(axial+shift)
  rings=[]
  for i in range(steps+1):
   t=i/steps;ring=[]
   for j in range(n):
    theta=j*math.tau/n-math.pi/2;ax=0
    if i==0:ax=rim_offset(theta,0)
    if i==steps:ax=rim_offset(theta,1)
    ring.append(self.point(vertex(t,theta,1,ax)))
   rings.append(ring)
  for i in range(steps):
   for j in range(n):
    k=(j+1)%n;self.face([rings[i][j],rings[i][k],rings[i+1][k],rings[i+1][j]],[(.015+.65*j/n,.015+.97*i/steps),(.015+.65*(j+1)/n,.015+.97*i/steps),(.015+.65*(j+1)/n,.015+.97*(i+1)/steps),(.015+.65*j/n,.015+.97*(i+1)/steps)])
  inner=[]
  if hollow:
   for i in range(steps+1):
    t=i/steps;ring=[]
    for j in range(n):
     theta=j*math.tau/n-math.pi/2;ax=rim_offset(theta,i//steps) if i in (0,steps) else 0
     ring.append(self.point(vertex(t,theta,.64+.035*math.sin(t*math.pi),ax)))
    inner.append(ring)
   for i in range(steps):
    for j in range(n):
     k=(j+1)%n;self.face([inner[i][j],inner[i+1][j],inner[i+1][k],inner[i][k]],[(.015+.65*j/n,.015+.97*i/steps),(.015+.65*j/n,.015+.97*(i+1)/steps),(.015+.65*(j+1)/n,.015+.97*(i+1)/steps),(.015+.65*(j+1)/n,.015+.97*i/steps)])
  for end in (0,1):
   t=float(end);cy=.25 if end==0 else .75
   def uv(rad,theta):return (.837+.145*rad*math.cos(theta),cy+.225*rad*math.sin(theta))
   prev=rings[-1 if end else 0];prev_scale=1
   scales=[.975,.94,.89,.80,.70,.64] if hollow else [.975,.94,.89,.78,.64,.50,.36,.22,.10]
   for ri,scale in enumerate(scales):
    last=ri==len(scales)-1
    ring=inner[-1 if end else 0] if hollow and last else []
    if not (hollow and last):
     for j in range(n):
      theta=j*math.tau/n-math.pi/2
      offset=(1 if end else -1)*radius(t)*(.016*math.sin(math.pi*(1-scale))-.028*(1-scale))
      ax=rim_offset(theta,end)*(scale**.7)+offset
      ring.append(self.point(vertex(t,theta,scale,ax)))
    for j in range(n):
     k=(j+1)%n;a=j*math.tau/n-math.pi/2;b=(j+1)*math.tau/n-math.pi/2
     self.face([prev[j],prev[k],ring[k],ring[j]],[uv(prev_scale,a),uv(prev_scale,b),uv(scale,b),uv(scale,a)])
    prev=ring;prev_scale=scale
   if not hollow:
    idx=self.point(centre(t))
    for j in range(n):
     k=(j+1)%n;a=j*math.tau/n-math.pi/2;b=(j+1)*math.tau/n-math.pi/2
     self.face([prev[j],prev[k],idx],[uv(prev_scale,a),uv(prev_scale,b),uv(0,0)])
  mesh=bpy.data.meshes.new('Rounded bark, shaped cut wood');mesh.from_pydata(self.vertices,[],self.faces)
  uv_layer=mesh.uv_layers.new(name='Painted_Wood_UV')
  for p,uv in zip(mesh.polygons,self.uv):
   for loop,co in zip(p.loop_indices,uv):uv_layer.data[loop].uv=co
  bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
  obj=bpy.data.objects.new('Sculpted timber',mesh);bpy.context.scene.collection.objects.link(obj)
  for p in mesh.polygons:p.use_smooth=True
  return obj


def log_mesh(shape,mat):
 if shape=='billot':specs=[([(-.45,0,.21),(0,.025,.205),(.45,0,.21)],[.235,.25,.22],11,False,False)]
 elif shape=='noueux':
  specs=[([(-.9,0,.25),(-.43,-.035,.27),(.15,.025,.31),(.9,.08,.22)],[.27,.31,.27,.21],23,False,False),([(.05,.04,.25),(.10,.23,.38),(.18,.40,.51)],[.21,.15,.105],31,False,True)]
 elif shape=='creux':specs=[([(-1.4,0,.35),(-.65,.025,.36),(.2,.075,.39),(1.4,.11,.35)],[.39,.43,.39,.35],47,True,True)]
 else:
  specs=[([(-2.1,0,.4),(-1.2,-.025,.42),(-.25,.03,.42),(.6,-.12,.34),(2.1,-.52,.23)],[.46,.47,.43,.29,.19],59,False,True),([(-.75,.015,.38),(-.12,.28,.45),(.70,.72,.43),(1.75,1.05,.33)],[.40,.34,.24,.16],67,False,True),([(-1.1,-.08,.35),(-1.18,-.34,.49),(-1.35,-.48,.65)],[.16,.12,.075],79,False,True)]
 parts=[]
 for points,radii,seed,hollow,broken in specs:
  part=Timber().tube(points,radii,seed,hollow,broken);part.data.materials.append(mat);parts.append(part)
 obj=parts[0];bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 for part in parts[1:]:
  modifier=obj.modifiers.new('Integrated branch junction','BOOLEAN');modifier.operation='UNION';modifier.solver='EXACT';modifier.object=part;bpy.ops.object.modifier_apply(modifier=modifier.name);bpy.data.objects.remove(part,do_unlink=True)
 mesh=obj.data;bm=bmesh.new();bm.from_mesh(mesh);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bmesh.ops.triangulate(bm,faces=bm.faces);bm.to_mesh(mesh);bm.free()
 for p in mesh.polygons:p.use_smooth=True
 low=Vector(tuple(min(v.co[i] for v in mesh.vertices) for i in range(3)));high=Vector(tuple(max(v.co[i] for v in mesh.vertices) for i in range(3)));factor=SHAPES[shape]['length']/(high.x-low.x);offset=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
 for vertex in mesh.vertices:vertex.co=(vertex.co-offset)*factor
 collection=bpy.data.collections.new('Arkya_Fallen_Logs');bpy.context.scene.collection.children.link(collection)
 for c in list(obj.users_collection):c.objects.unlink(obj)
 collection.objects.link(obj);obj.name=SHAPES[shape]['label'];obj['family']='troncs-couches';obj['root_pivot']='XY centre, Z floor';obj['wood_type']='hollow through bore' if shape=='creux' else 'closed wood';return obj,(high-low)*factor


def studio(obj,dims,path,draft=False,gpu=False):
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=16 if draft else 32;scene.cycles.use_denoising=True;scene.cycles.device='GPU' if gpu else 'CPU';scene.render.threads_mode='FIXED';scene.render.threads=6
 if gpu:
  pref=bpy.context.preferences.addons['cycles'].preferences;pref.compute_device_type='OPTIX';pref.get_devices()
  for d in pref.devices:d.use=d.type=='OPTIX'
 scene.render.resolution_x=700 if draft else 1100;scene.render.resolution_y=scene.render.resolution_x;scene.render.resolution_percentage=100
 scene.world=bpy.data.worlds.new('Neutral studio');scene.world.use_nodes=True;scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.30,.35,.33,1);scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
 extent=max(dims);h=dims.z;bpy.ops.mesh.primitive_plane_add(size=extent*200,location=(0,0,-.002));floor=bpy.context.object;floor.name='Studio floor | not exported'
 mat=bpy.data.materials.new('Studio sage');mat.use_nodes=True;mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.23,.265,.235,1);mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.9;floor.data.materials.append(mat)
 for name,pos,energy,size in [('Broad key',(-3,-4,5),650,4),('Fill',(4,-1,2.4),300,3),('Soft rim',(1,3,4),480,3)]:
  light=bpy.data.lights.new(name,'AREA');light.energy=energy*extent**2;light.shape='DISK';light.size=size*extent;o=bpy.data.objects.new(name,light);scene.collection.objects.link(o);o.location=Vector(pos)*extent;o.rotation_euler=(Vector((0,0,h*.4))-o.location).to_track_quat('-Z','Y').to_euler()
 data=bpy.data.cameras.new('Preview camera');cam=bpy.data.objects.new('Preview camera',data);scene.collection.objects.link(cam);scene.camera=cam
 cam.location=(extent*2.55,-extent*3.5,extent*2.25);cam.rotation_euler=(Vector((0,0,h*.42))-cam.location).to_track_quat('-Z','Y').to_euler();data.type='ORTHO';data.ortho_scale=extent*1.18
 scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(path);bpy.ops.render.render(write_still=True)


def build(shape,palette,args):
 bpy.ops.wm.read_factory_settings(use_empty=True);bpy.context.preferences.filepaths.save_version=0
 obj,dims=log_mesh(shape,material(palette));stem=shape+'-'+palette;folder=ROOT/'assets'/stem;folder.mkdir(parents=True,exist_ok=True)
 bpy.ops.object.select_all(action='DESELECT');obj.select_set(True);bpy.context.view_layer.objects.active=obj
 triangles=sum(len(p.vertices)-2 for p in obj.data.polygons);assert triangles<=35000,(stem,triangles)
 bm=bmesh.new();bm.from_mesh(obj.data);assert all(e.is_manifold for e in bm.edges),(stem,'Open wood wall');assert all(f.calc_area()>1e-13 for f in bm.faces),(stem,'Degenerate face');bm.free()
 bpy.ops.export_scene.gltf(filepath=str(folder/(stem+'.glb')),export_format='GLB',use_selection=True,export_apply=True,export_materials='EXPORT',export_yup=True,export_animations=False,export_lights=False,export_cameras=False,export_tangents=True)
 scene=bpy.context.scene;scene.unit_settings.system='METRIC';scene['arkya_resource']='troncs-couches';scene['source_script']='scripts/generate_logs.py'
 text=bpy.data.texts.new('Arkya provenance');text.write('Original procedural sculpted fallen logs. Closed rounded bark and wood cut ends, eccentric painted growth rings and genuine through cavity for hollow shape. Integrated boolean branch junctions. Approved V3 bark source reused read-only; painted palette atlas with localized moss and restrained emission. Source SHA256: '+hashlib.sha256(SOURCE.read_bytes()).hexdigest()+'. Artistic status is recorded in manifest.json. Metres, XY-centred floor pivot.')
 if args.render:studio(obj,dims,folder/(stem+'-preview.png'),args.draft,args.gpu)
 bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(folder/(stem+'.blend')))
 record={'id':stem,'shape':shape,'material':palette,'label':SHAPES[shape]['label']+' · '+PALETTES[palette]['label'],'glb':(folder/(stem+'.glb')).relative_to(ROOT).as_posix(),'blend':(folder/(stem+'.blend')).relative_to(ROOT).as_posix(),'preview':(folder/(stem+'-preview.png')).relative_to(ROOT).as_posix() if (folder/(stem+'-preview.png')).exists() else None,'height':round(dims.z,4),'width':round(dims.x,4),'depth':round(dims.y,4),'triangles':triangles,'texture_resolution':[SIZE,SIZE],'opaque':True,'pivot':'centre au sol'}
 (folder/(stem+'.json')).write_text(json.dumps(record,ensure_ascii=False,indent=2),encoding='utf-8')
 path=ROOT/'manifest.json';old=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {};records=[a for a in old.get('assets',[]) if a['id']!=stem]+[record]
 manifest={'family':'troncs-couches','artistic_status':old.get('artistic_status','proposed'),'shapes':[dict(id=k,height=next((a['height'] for a in records if a['shape']==k),.35 if k=='billot' else .7),**v) for k,v in SHAPES.items()],'materials':[{'id':k,'label':v['label'],'swatch':v['swatch'],'description':v['description']} for k,v in PALETTES.items()],'assets':sorted(records,key=lambda a:(list(SHAPES).index(a['shape']),list(PALETTES).index(a['material'])))}
 path.write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8');print('FALLEN_LOG_READY',json.dumps(record,ensure_ascii=False),flush=True)


def main():
 parser=argparse.ArgumentParser();parser.add_argument('--shape',choices=['all']+list(SHAPES),default='noueux');parser.add_argument('--material',choices=['all']+list(PALETTES),default='moussu');parser.add_argument('--render',action='store_true');parser.add_argument('--draft',action='store_true');parser.add_argument('--gpu',action='store_true');parser.add_argument('--rebake',action='store_true');args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
 bpy.ops.wm.read_factory_settings(use_empty=True);palettes=list(PALETTES) if args.material=='all' else [args.material];shapes=list(SHAPES) if args.shape=='all' else [args.shape]
 for p in palettes:textures(p,args.rebake)
 for shape in shapes:
  for p in palettes:build(shape,p,args)


if __name__=='__main__':main()
