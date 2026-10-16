"""Derive normalized, weighted companion anatomy from the licensed Bake3D fox.

Run with Blender --background --python scripts/build_anatomy.py -- [wolf|fox].
Only writes the anatomy sources; final coats, VFX and clips are built separately.
"""
import bpy
import json
import math
import hashlib
import sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

BASE = Path(__file__).resolve().parents[1]
SOURCE = BASE / 'sources/bake3d/fox-original.glb'
OUT = BASE / 'assets/anatomy'
RENAMES = {'motion_root':'Root','hips':'Hips','spine0':'Spine',
           'spine1':'Spine.02','chest':'Chest','neck':'Neck','head':'Head',
           'earL':'Ear.L','earR':'Ear.R'}
for side in ('L','R'):
    for suffix, kind in (('0','Front'),('1','Hind')):
        for old, new in (('thigh','Upper'),('shin','Lower'),('foot','Foot'),('toe','Toe')):
            RENAMES[old+side+suffix] = kind+new+'.'+side
for i in range(4):
    RENAMES['tail'+str(i)] = 'Tail.'+str(i+1).zfill(2)


def smooth(a, b, x):
    t = max(0., min(1., (x-a)/(b-a)))
    return t*t*(3-2*t)


def neutral_import():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    rig = next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
    coat = next(o for o in bpy.context.scene.objects if o.type=='MESH' and len(o.data.vertices)>1000)
    for obj in list(bpy.data.objects):
        if obj not in (rig,coat):
            bpy.data.objects.remove(obj,do_unlink=True)
    for obj in (rig,coat):
        obj.animation_data_clear()
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
        bone.custom_shape=None
    bpy.context.view_layer.update()
    rot = Matrix.Rotation(math.pi/2,4,'Z')
    coat.data.transform(rot)
    rig.data.transform(rot)
    for bone in rig.data.bones:
        bone.name=RENAMES[bone.name]
    for group in coat.vertex_groups:
        if group.name in RENAMES: group.name=RENAMES[group.name]
    rig.name='Armature';coat.name='TotemFur';coat.data.name='TotemCoat'
    coat.data.materials[0].name='TotemFur'
    for mat in coat.data.materials:
        for node in mat.node_tree.nodes:
            if node.type=='BSDF_PRINCIPLED':
                node.inputs['Roughness'].default_value=.79
                node.inputs['Specular IOR Level'].default_value=.22
    return rig,coat


def prepare_wolf_regions(coat,points):
    """Tail ownership prevents a deformation of the adjacent haunch surface."""
    global _wolf_tail_values,_wolf_tail_tree,_wolf_tail_samples
    tail_groups={g.index for g in coat.vertex_groups if g.name.startswith('Tail.')}
    _wolf_tail_values={};_wolf_tail_tree=KDTree(len(points));_wolf_tail_samples=[]
    for index,(vertex,point) in enumerate(zip(coat.data.vertices,points)):
        value=sum(g.weight for g in vertex.groups if g.group in tail_groups)
        _wolf_tail_values[tuple(point)]=value
        _wolf_tail_tree.insert(point,index);_wolf_tail_samples.append(value)
    _wolf_tail_tree.balance()


def wolf_tail_weight(point):
    value=_wolf_tail_values.get(tuple(point))
    if value is not None:return value
    neighbors=_wolf_tail_tree.find_n(point,4)
    total=sum(1/max(distance,.0001)**2 for _,_,distance in neighbors)
    return sum(_wolf_tail_samples[index]/max(distance,.0001)**2 for _,index,distance in neighbors)/total


def wolf_shape(point):
    """Broad cranium, substantial muzzle, compact ears and a low tapered brush."""
    x,y,z=point
    sign=1 if y>=0 else -1
    # Contract the eye opening locally while retaining the source eyelid and UVs.
    eye_center=Vector((.268,sign*.059,.494))
    delta=point-eye_center
    radius=math.sqrt((delta.x/.044)**2+(delta.y/.036)**2+(delta.z/.047)**2)
    eye=1-smooth(.35,1.,radius)
    q=point.lerp(eye_center+Vector((delta.x*.93,delta.y*.82,delta.z*.70)),eye)
    hx,hy,hz=q
    head=smooth(.310,.450,z)*smooth(.015,.115,x)
    body=Vector(((x+.06)*1.38-.06,y*1.23,z*1.08))
    # Compress the forehead vertically and widen the zygomatic mass.
    headpoint=Vector((.285+(hx-.223)*.88,hy*1.28,.425+(hz-.445)*.70))
    p=body.lerp(headpoint,head)
    muzzle=smooth(.265,.320,x)*(1-smooth(.475,.510,z))
    mouth_correction=.013*math.exp(-((x-.350)/.031)**2)*(1-smooth(.440,.468,z))
    muzzlepoint=Vector((.285+(hx-.223)*.93,
                        hy*(1.44+.36*smooth(.325,.395,x)),
                        .423+(hz+mouth_correction-.442)*1.25))
    p=p.lerp(muzzlepoint,muzzle)
    # A full lower jaw and nape replace the fox's thin cheek/neck transition.
    jaw=smooth(.235,.275,x)*(1-smooth(.365,.410,x))*(1-smooth(.418,.452,z))
    p.z-=.003*jaw
    ruff=smooth(.055,.135,x)*(1-smooth(.245,.280,x))*smooth(.275,.345,z)*(1-smooth(.455,.510,z))
    p.y*=1+.13*ruff
    p.x-=.022*ruff*(1-smooth(.17,.24,x))
    p.x+=.018*ruff*smooth(.17,.23,x)*(1-smooth(.38,.44,z))
    neck_fill=math.exp(-((x-.190)/.045)**2-((z-.394)/.034)**2)
    p.y*=1+.12*neck_fill
    p.x+=.010*neck_fill
    thorax=smooth(.015,.075,x)*(1-smooth(.18,.24,x))*smooth(.16,.25,z)*(1-smooth(.34,.40,z))
    p.y*=1+.04*thorax
    # Flatten the topline and keep the pelvis leaner than the shoulder girdle.
    back=smooth(.285,.355,z)*(1-smooth(.015,.13,x))*smooth(-.19,-.12,x)
    p.z=p.z*(1-back)+(.376+(z-.345)*.40)*back
    haunch=(1-smooth(-.035,.045,x))*smooth(-.18,-.12,x)*smooth(.11,.19,z)*(1-smooth(.29,.35,z))
    p.y*=1-.17*haunch
    # Broad-set ear shells: low rounded tips, rather than tall fox triangles.
    ear=smooth(.495,.550,z)*smooth(.026,.060,abs(y))
    earpoint=Vector((.2736+(x-.210)*.65,
                     sign*.089+(y-sign*.070)*.70,
                     .4635+(z-.500)*.45))
    p=p.lerp(earpoint,ear)
    # Thicker distal limbs and paws, without widening the space between them.
    limb=(1-smooth(.20,.30,z))*smooth(.012,.035,abs(y))
    side=1 if y>=0 else -1
    p.y += (y-side*.050)*.27*limb
    foot=1-smooth(.035,.07,z)
    legcenter=.174 if x>.055 else -.104
    p.x += (x-legcenter)*.20*foot
    # Reorient the source tail along its actual diagonal, then taper and lower it.
    tail=wolf_tail_weight(point)
    dx,dz=x+.140,z-.264
    along=-.940*dx+.341*dz
    radial=.341*dx+.940*dz
    transition=smooth(.025,.16,along)
    radial_scale=.52*(1-transition)+.40*transition
    width_scale=.70*(1-transition)+.55*transition
    target=Vector((-.1704-along*.820*1.12-radial*.572*radial_scale,
                   y*width_scale,.28512-along*.572*1.12+radial*.820*radial_scale))
    p=p.lerp(target,tail)
    return p


def render_wolf_review(coat,species):
    """Three neutral grey views for anatomical review; source material restored."""
    originals=list(coat.data.materials)
    original_image=next(n.image for m in originals for n in m.node_tree.nodes if n.type=='TEX_IMAGE')
    material=bpy.data.materials.new('Wolf review grey');material.use_nodes=True
    nodes=material.node_tree.nodes;links=material.node_tree.links
    shader=nodes.get('Principled BSDF');shader.inputs['Roughness'].default_value=.85
    tex=nodes.new('ShaderNodeTexImage');tex.image=original_image
    grey=nodes.new('ShaderNodeRGBToBW');links.new(tex.outputs['Color'],grey.inputs[0])
    ramp=nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position=.02;ramp.color_ramp.elements[0].color=(.055,.065,.080,1)
    ramp.color_ramp.elements[1].position=.85;ramp.color_ramp.elements[1].color=(.55,.60,.63,1)
    links.new(grey.outputs[0],ramp.inputs[0]);links.new(ramp.outputs[0],shader.inputs['Base Color'])
    coat.data.materials.clear();coat.data.materials.append(material)
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
    scene.render.resolution_x=1400;scene.render.resolution_y=1050;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Wolf review studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.045,.055,.070,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    points=[v.co for v in coat.data.vertices]
    lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)])
    center=(lo+hi)*.5;size=max(hi-lo)
    for name,delta,power in [('Key',(1,-2,3),650),('Fill',(2,2,1.5),300),('Rim',(-2,1,2),700)]:
        data=bpy.data.lights.new(name,'AREA');obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
        obj.location=center+Vector(delta)*size;data.energy=power*size*size;data.size=size*2
        obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Wolf review');cam=bpy.data.objects.new('Wolf review',data);bpy.context.collection.objects.link(cam)
    data.type='ORTHO';data.ortho_scale=size*1.25;scene.camera=cam;scene.view_settings.view_transform='AgX'
    for name,direction in [('profile',(0,-3,.10)),('three-quarter',(1.75,-3,.60)),('front',(3,0,.08))]:
        cam.location=center+Vector(direction)*size
        cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
        scene.render.filepath=str(BASE/'renders'/f'wolf-v3-draft-{name}.png')
        bpy.ops.render.render(write_still=True)
    coat.data.materials.clear()
    for original in originals:coat.data.materials.append(original)
    for obj in list(scene.objects):
        if obj.type in ('CAMERA','LIGHT'):bpy.data.objects.remove(obj,do_unlink=True)


def rebuild_rig(rig,coat,species,transform):
    """Keep skin ownership, but place real shoulder, wrist, toe and ear pivots."""
    # These points are measured in the normalized source rest mesh, before scaling.
    joints={
        'Root':((0,0,0),(0,0,.08),None),
        'Hips':((-.061,0,.286),(0,0,.286),'Root'),
        'Spine':((0,0,.286),(.07,0,.292),'Hips'),
        'Spine.02':((.07,0,.292),(.14,0,.290),'Spine'),
        'Chest':((.14,0,.290),(.202,0,.369),'Spine.02'),
        'Neck':((.202,0,.369),(.223,0,.445),'Chest'),
        'Head':((.223,0,.445),(.280,0,.472),'Neck'),
    }
    for side,sgn in (('L',1),('R',-1)):
        joints['Ear.'+side]=((.211,sgn*.071,.503),(.211,sgn*.105,.631),'Head')
        front=[(.141,sgn*.042,.262),(.140,sgn*.039,.174),(.146,sgn*.035,.045),(.158,sgn*.039,.014),(.180,sgn*.043,.011)]
        hind=[(-.070,sgn*.050,.278),(-.044,sgn*.048,.178),(-.117,sgn*.039,.110),(-.107,sgn*.042,.030),(-.079,sgn*.045,.012)]
        for kind,points,parent in (('Front',front,'Chest'),('Hind',hind,'Hips')):
            for i,part in enumerate(('Upper','Lower','Foot','Toe')):
                name=kind+part+'.'+side
                joints[name]=(points[i],points[i+1],parent if i==0 else kind+('Upper','Lower','Foot')[i-1]+'.'+side)
    tail=[(-.140,0,.264),(-.219,0,.258),(-.298,0,.261),(-.368,0,.316),(-.439,0,.371)]
    for i in range(4):
        joints['Tail.'+str(i+1).zfill(2)]=(tail[i],tail[i+1],'Hips' if i==0 else 'Tail.'+str(i).zfill(2))
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    for bone in list(rig.data.edit_bones): rig.data.edit_bones.remove(bone)
    for name,(head,tail,parent) in joints.items():
        bone=rig.data.edit_bones.new(name)
        bone.head=transform(Vector(head));bone.tail=transform(Vector(tail))
        bone.use_deform=name!='Root'
        if name=='Root': bone.head=(0,0,0);bone.tail=(0,0,.1)
        if parent:bone.parent=rig.data.edit_bones[parent]
        bone.align_roll(Vector((0,1,0)))
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front=True
    rig.data.display_type='OCTAHEDRAL'
    for posebone in rig.pose.bones:posebone.rotation_mode='QUATERNION'
    return joints


def repair_weights(coat,source_points,source_joints):
    names={g.index:g.name for g in coat.vertex_groups}
    weights=[{names[g.group]:g.weight for g in v.groups} for v in coat.data.vertices]
    chains=[]
    for side in ('L','R'):
        for kind in ('Front','Hind'):
            names4=[kind+part+'.'+side for part in ('Upper','Lower','Foot','Toe')]
            pts=[Vector(source_joints[n][0]) for n in names4]+[Vector(source_joints[names4[-1]][1])]
            lengths=[(pts[i+1]-pts[i]).length for i in range(4)]
            chains.append((names4,pts,lengths))
    for vertex,p,w in zip(coat.data.vertices,source_points,weights):
        # Below the elbow/knee, geometry is separated; imported torso influence
        # must not drag a planted foot when the spine breathes or pitches.
        if p.z<.165 and (p.x>.08 or -.16<p.x<.02):
            kind='Front' if p.x>.08 else 'Hind'
            w={kind+'Lower.'+('L' if p.y>0 else 'R'):1.}
        for names4,pts,lengths in chains:
            total=sum(w.pop(n,0) for n in names4)
            if total<1e-6:continue
            near=[];accum=0
            for j in range(4):
                d=pts[j+1]-pts[j]
                t=max(0,min(1,(p-pts[j]).dot(d)/d.length_squared))
                near.append(((p-(pts[j]+d*t)).length_squared,accum+t*lengths[j]))
                accum+=lengths[j]
            s=min(near)[1];boundaries=[sum(lengths[:j+1]) for j in range(3)]
            parts=[1.,0.,0.,0.]
            for j,(boundary,blend) in enumerate(zip(boundaries,(.018,.010,.008))):
                t=smooth(boundary-blend,boundary+blend,s)
                parts[j]=1-t if j==0 else parts[j]*(1-t)
                parts[j+1]=t
            for n,value in zip(names4,parts):
                if value>1e-6:w[n]=total*value
        # The source ear pivots sat in the middle of the skull. Localize them.
        ear_total=w.pop('Ear.L',0)+w.pop('Ear.R',0)
        w['Head']=w.get('Head',0)+ear_total
        ear_factor=smooth(.492,.545,p.z)*smooth(.025,.055,abs(p.y))
        if ear_factor:
            amount=w.get('Head',0)*ear_factor
            w['Head']=w.get('Head',0)-amount
            w['Ear.L' if p.y>0 else 'Ear.R']=amount
        w={n:value for n,value in sorted(w.items(),key=lambda item:-item[1])[:4] if value>1e-6}
        total=sum(w.values())
        if total<=0:w={'Spine':1};total=1
        weights[vertex.index]={n:value/total for n,value in w.items()}
    coat.vertex_groups.clear()
    for name in RENAMES.values():coat.vertex_groups.new(name=name)
    for index,w in enumerate(weights):
        for name,value in w.items():coat.vertex_groups[name].add([index],value,'REPLACE')


def render_source(coat,species):
    scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
    scene.render.resolution_x=1400;scene.render.resolution_y=1050;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Anatomy Studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.045,.060,.083,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
    points=[v.co for v in coat.data.vertices]
    lo=Vector([min(p[i] for p in points) for i in range(3)])
    hi=Vector([max(p[i] for p in points) for i in range(3)])
    center=(lo+hi)*.5;size=max(hi-lo)
    for name,delta,power in [('Key',(1.6,-2.4,3),650),('Fill',(2,2,1.2),300),('Rim',(-2,1,2.5),850)]:
        data=bpy.data.lights.new(name,'AREA');obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj)
        obj.location=center+Vector(delta)*size;data.energy=power*size*size;data.size=size*2
        obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('AnatomyCamera');cam=bpy.data.objects.new('AnatomyCamera',data);bpy.context.collection.objects.link(cam)
    cam.location=center+Vector((1.7,-2.6,1.1))*size
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    data.type='ORTHO';data.ortho_scale=size*1.3;scene.camera=cam
    scene.view_settings.view_transform='AgX'
    scene.render.filepath=str(BASE/'renders'/f'{species}-anatomy-v2.png')
    bpy.ops.render.render(write_still=True)
    for obj in list(scene.objects):
        if obj.type in ('CAMERA','LIGHT'):bpy.data.objects.remove(obj,do_unlink=True)


def build(species):
    OUT.mkdir(parents=True,exist_ok=True)
    rig,coat=neutral_import()
    source_points=[v.co.copy() for v in coat.data.vertices]
    if species=='wolf':
        prepare_wolf_regions(coat,source_points)
        source_position=coat.data.attributes.new('SourcePosition','FLOAT_VECTOR','POINT')
        for datum,point in zip(source_position.data,source_points):datum.vector=point
    shape=wolf_shape if species=='wolf' else lambda p:p.copy()
    shaped=[shape(p) for p in source_points]
    minz=min(p.z for p in shaped);maxz=max(p.z for p in shaped)
    scale=(1.10 if species=='wolf' else .65)/(maxz-minz)
    def transform(p):
        q=shape(p);q.z-=minz;return q*scale
    for vertex,p in zip(coat.data.vertices,source_points):vertex.co=transform(p)
    joints=rebuild_rig(rig,coat,species,transform)
    repair_weights(coat,source_points,joints)
    # Dissolve exporter vertex splits before gentle, UV-preserving simplification.
    bpy.context.view_layer.objects.active=coat;rig.select_set(False);coat.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.remove_doubles(threshold=.000001)
    bpy.ops.object.mode_set(mode='OBJECT')
    decimate=coat.modifiers.new('Game mesh reduction','DECIMATE');decimate.ratio=.48
    bpy.ops.object.modifier_apply(modifier=decimate.name)
    for vertex in coat.data.vertices:
        strongest=sorted([(g.group,g.weight) for g in vertex.groups],key=lambda item:-item[1])[:4]
        total=sum(value for _,value in strongest)
        for group in list(vertex.groups):coat.vertex_groups[group.group].remove([vertex.index])
        for index,value in strongest:coat.vertex_groups[index].add([vertex.index],value/total,'REPLACE')
    for face in coat.data.polygons:face.use_smooth=True
    for mat in coat.data.materials:
        for node in mat.node_tree.nodes:
            if node.type=='TEX_IMAGE' and node.image:node.image.pack()
    coat.data.calc_loop_triangles()
    points=[v.co for v in coat.data.vertices]
    bounds={key:[fn(p[i] for p in points) for i in range(3)] for key,fn in [('min',min),('max',max)]}
    limbs=[]
    for side,sign in [('L',1),('R',-1)]:
        for kind in ('Front','Hind'):
            # The center of the plantar surface, measured from the rest mesh.
            xs=(.13,.24) if kind=='Front' else (-.15,-.055)
            ids=[i for i,p in enumerate(source_points) if xs[0]<p.x<xs[1] and p.z<.025 and p.y*sign>.02]
            soles=[transform(source_points[i]) for i in ids]
            sole=Vector((sum(p.x for p in soles)/len(soles),sum(p.y for p in soles)/len(soles),min(p.z for p in soles)))
            limbs.append({'id':('Fore' if kind=='Front' else 'Hind')+'.'+side,
                          'upper':kind+'Upper.'+side,'lower':kind+'Lower.'+side,
                          'foot':kind+'Foot.'+side,'toe':kind+'Toe.'+side,
                          'pole_forward':-1 if kind=='Front' else 1,'sole_rest':list(sole)})
    metadata={
        'species':species,'schemaVersion':2,'units':'meters','forwardAxis':'+X','upAxis':'+Z',
        'source':'sources/bake3d/fox-original.glb','sourceSHA256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'anatomy':{'hip':list(rig.data.bones['Hips'].head_local),'shoulder':list(rig.data.bones['Chest'].head_local),'neck':list(rig.data.bones['Neck'].head_local)},
        'rig':{'name':'Armature','bones':len(rig.data.bones),'root':'Root','hips':'Hips','head':'Head',
               'joints':{b.name:{'head':list(b.head_local),'tail':list(b.tail_local),'parent':b.parent.name if b.parent else None} for b in rig.data.bones}},
        'triangles':len(coat.data.loop_triangles),'vertices':len(coat.data.vertices),'bounds':bounds,
        'dimensions':{'lengthM':bounds['max'][0]-bounds['min'][0],'widthM':bounds['max'][1]-bounds['min'][1],'heightM':bounds['max'][2]-bounds['min'][2]},
        'locomotionConfig':{'meters_per_unit':1,'limbs':limbs},
        'modifications':['Normalized axes and scale','Anatomical limb and ear pivots','Regional skin weight repair','UV-preserving mesh simplification']}
    if species=='wolf':
        metadata['artRevision']=3
        metadata['modifications'].extend(['Wolf cranium and muzzle sculpt','Compact wide-set ears','Low tapered tail with continuous hip attachment','SourcePosition attribute retained for coat painting'])
    (OUT/f'{species}-rig.json').write_text(json.dumps(metadata,indent=2))
    render_source(coat,species)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/f'{species}.blend'))
    print('ANATOMY_READY',json.dumps({'species':species,'dimensions':metadata['dimensions'],'triangles':metadata['triangles']}))


if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    for species in args or ('wolf','fox'):build(species)
