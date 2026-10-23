"""Build the independent CC0 NewDLC wolf, preserving its canine head volumes.

Blender --background --python build_wolf_anatomy.py -- [--render] [--revision 4|5|6|7|8|9|10]
Writes an isolated V10 draft by default. Revisions 4 through 9 remain reproducible.
The licensed original and fox assets stay intact.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector
from mathutils.kdtree import KDTree

BASE = Path(__file__).resolve().parents[1]
SOURCE = BASE / 'sources/newdlc/dog2.blend'
OUT = BASE / 'assets/anatomy/draft-wolf-v10'
RENDERS = BASE.parents[3] / 'output/totem-wolf-v10/anatomy'


def smooth(a, b, x):
    t = max(0., min(1., (x-a)/(b-a)))
    return t*t*(3-2*t)


def select(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def connected_parts(mesh):
    adj = [set() for _ in mesh.vertices]
    for e in mesh.edges:
        a, b = e.vertices
        adj[a].add(b); adj[b].add(a)
    seen = set(); parts = []
    for i in range(len(adj)):
        if i in seen:
            continue
        pending = [i]; part = []; seen.add(i)
        while pending:
            k = pending.pop(); part.append(k)
            for n in adj[k]:
                if n not in seen:
                    seen.add(n); pending.append(n)
        parts.append(part)
    return sorted(parts, key=len, reverse=True)


def import_source():
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE))
    mesh = next(o for o in bpy.data.objects if o.type == 'MESH')
    rig = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    for ob in list(bpy.data.objects):
        if ob not in (mesh, rig):
            bpy.data.objects.remove(ob, do_unlink=True)
    for ob in (mesh, rig):
        ob.animation_data_clear(); ob.hide_render=False; ob.hide_set(False)
    for pb in rig.pose.bones:
        pb.matrix_basis.identity(); pb.custom_shape=None
    for action in list(bpy.data.actions):
        bpy.data.actions.remove(action)
    bpy.context.view_layer.update()
    # The original mesh and armature have different import transforms.
    rotate = Matrix.Rotation(math.pi/2, 4, 'Z')
    mesh_matrix = rotate @ mesh.matrix_world
    rig_matrix = rotate @ rig.matrix_world
    mesh.parent=None; mesh.matrix_parent_inverse.identity()
    mesh.data.transform(mesh_matrix); rig.data.transform(rig_matrix)
    mesh.matrix_world=Matrix.Identity(4); rig.matrix_world=Matrix.Identity(4)
    # Entering Edit Mode rebuilds pose-channel rest matrices after Armature.transform.
    select(rig);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()
    mesh.name='TotemFur'; mesh.data.name='WolfV4Coat'; rig.name='Armature'
    for modifier in list(mesh.modifiers):
        if modifier.type != 'ARMATURE':
            mesh.modifiers.remove(modifier)
    # Hair cards are disconnected from the closed skull. Teeth and eye islands
    # are retained; removing only complete card islands cannot puncture it.
    parts=connected_parts(mesh.data)
    hair=[]
    for part in parts[1:]:
        names={mesh.vertex_groups[g.group].name for i in part for g in mesh.data.vertices[i].groups}
        if names & {'b_LeftEye','b_RightEye'} or len(part)==12:
            continue
        hair.extend(part)
    bm=bmesh.new(); bm.from_mesh(mesh.data); bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[bm.verts[i] for i in hair], context='VERTS')
    bm.to_mesh(mesh.data); bm.free()
    eye=mesh.data.attributes.new('EyeMask','FLOAT','POINT')
    for v,d in zip(mesh.data.vertices,eye.data):
        d.value=float(any(mesh.vertex_groups[g.group].name in ('b_LeftEye','b_RightEye') for g in v.groups))
    # Only the upper ear shells shorten; forehead, eyes and muzzle are unchanged.
    for v in mesh.data.vertices:
        if v.co.z > 2.79 and abs(v.co.y) > .13:
            v.co.z = 2.79 + (v.co.z-2.79)*.75
    return rig,mesh,len(hair)


def lower_tail(rig, mesh):
    """Apply a calm jaw and relaxed four-joint tail, then bake their rest pose."""
    jaw=rig.pose.bones['b_Jaw']
    jaw.rotation_mode='QUATERNION'
    axis=rig.data.bones['b_Jaw'].matrix_local.to_quaternion().inverted()@Vector((0,1,0))
    jaw.rotation_quaternion=Quaternion(axis.normalized(),math.radians(-5.5))
    bpy.context.view_layer.update()
    previous=None
    for i, degrees in enumerate((-34,-42,-47,-50),1):
        name=f'b_Tail{i:02d}'; bone=rig.data.bones[name]
        head=previous if previous is not None else bone.head_local.copy()
        angle=math.radians(degrees)
        direction=Vector((-math.cos(angle),0,math.sin(angle)))
        tail=head+direction*bone.length
        rotation=(bone.tail_local-bone.head_local).rotation_difference(direction)
        rig.pose.bones[name].matrix=Matrix.LocRotScale(head,rotation@bone.matrix_local.to_quaternion(),Vector((1,1,1)))
        bpy.context.view_layer.update()
        previous=tail
    select(mesh)
    modifier=next(m for m in mesh.modifiers if m.type=='ARMATURE')
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    select(rig); bpy.ops.object.mode_set(mode='POSE'); bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    modifier=mesh.modifiers.new('Wolf skin','ARMATURE'); modifier.object=rig


def build_contract(rig, mesh):
    source={b.name:(b.head_local.copy(),b.tail_local.copy()) for b in rig.data.bones}
    mapping={'b_Root':'Root','b_Hip':'Hips','b_Spine01':'Spine','b_Spine02':'Spine.02',
             'b_Spine03':'Chest','b_Neck':'Neck','b_Head':'Head','b_Jaw':'Head',
             'b_LeftEye':'Head','b_RightEye':'Head'}
    joints={
        'Root':(Vector((0,0,0)),Vector((0,0,.1)),None),
        'Hips':(*source['b_Hip'],'Root'),
        'Spine':(*source['b_Spine01'],'Hips'),
        'Spine.02':(*source['b_Spine02'],'Spine'),
        'Chest':(*source['b_Spine03'],'Spine.02'),
        'Neck':(*source['b_Neck'],'Chest'),
        'Head':(*source['b_Head'],'Neck'),
    }
    for side,long,sgn in (('L','Left',1),('R','Right',-1)):
        mapping[f'b_{long}Shoulder']='Chest'
        for kind,old_parts in [('Front',('UpperArm','ForeArm','Hand01','Hand02')),('Hind',('Leg01','Leg02','Foot01','Foot02'))]:
            for i,(part,old) in enumerate(zip(('Upper','Lower','Foot','Toe'),old_parts)):
                name=f'{kind}{part}.{side}'; old=f'b_{long}{old}'
                mapping[old]=name
                joints[name]=(*source[old],('Chest' if kind=='Front' else 'Hips') if i==0 else f'{kind}{("Upper","Lower","Foot")[i-1]}.{side}')
            mapping[f'b_{long}{"Hand" if kind=="Front" else "Foot"}03']=f'{kind}Toe.{side}'
        # Anatomical ear bases are on the actual shell, not the centre of the skull.
        joints['Ear.'+side]=(Vector((1.25,sgn*.205,2.76)),Vector((1.25,sgn*.29,3.10)),'Head')
    for i in range(1,5):
        name=f'Tail.{i:02d}';mapping[f'b_Tail{i:02d}']=name
        joints[name]=(*source[f'b_Tail{i:02d}'],'Hips' if i==1 else f'Tail.{i-1:02d}')
    original_names={g.index:g.name for g in mesh.vertex_groups}
    weights=[]
    for vertex in mesh.data.vertices:
        weight={}
        for g in vertex.groups:
            name=mapping[original_names[g.group]]
            weight[name]=weight.get(name,0)+g.weight
        weights.append(weight)
    select(rig); bpy.ops.object.mode_set(mode='EDIT')
    for b in list(rig.data.edit_bones):
        rig.data.edit_bones.remove(b)
    for name,(head,tail,parent) in joints.items():
        bone=rig.data.edit_bones.new(name);bone.head=head;bone.tail=tail
        bone.use_deform=name!='Root';bone.align_roll(Vector((0,1,0)))
        if parent:bone.parent=rig.data.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    for bone in rig.pose.bones:bone.rotation_mode='QUATERNION'
    # Source arm/hand and leg/foot groups overlap far past the joint. Redistribute
    # their aggregate influence along the actual chain, and isolate distal skin.
    chains=[]
    for side in ('L','R'):
        for kind in ('Front','Hind'):
            names=[f'{kind}{part}.{side}' for part in ('Upper','Lower','Foot','Toe')]
            pts=[joints[n][0] for n in names]+[joints[names[-1]][1]]
            lengths=[(pts[i+1]-pts[i]).length for i in range(4)]
            chains.append((names,pts,lengths))
    for vertex,weight in zip(mesh.data.vertices,weights):
        p=vertex.co
        for names,pts,lengths in chains:
            total=sum(weight.pop(n,0) for n in names)
            if total<1e-6:continue
            # Below elbow/knee no inherited torso deformation should move a foot.
            if p.z < pts[1].z-.06 and total>.20:
                weight.clear();total=1.
            near=[];accum=0.
            for j in range(4):
                d=pts[j+1]-pts[j];t=max(0.,min(1.,(p-pts[j]).dot(d)/d.length_squared))
                near.append(((p-(pts[j]+d*t)).length_squared,accum+t*lengths[j]));accum+=lengths[j]
            s=min(near)[1];amount=[1.,0.,0.,0.]
            for j,blend in enumerate((.055,.030,.025)):
                boundary=sum(lengths[:j+1]);t=smooth(boundary-blend,boundary+blend,s)
                amount[j]*=1-t;amount[j+1]=t
            for n,value in zip(names,amount):
                if value>1e-7:weight[n]=total*value
        ear=smooth(2.78,3.05,p.z)*smooth(.12,.21,abs(p.y))
        if ear and weight.get('Head',0):
            value=weight['Head']*ear;weight['Head']-=value
            weight['Ear.'+('L' if p.y>0 else 'R')]=value
    mesh.vertex_groups.clear()
    for name in joints:mesh.vertex_groups.new(name=name)
    for index,weight in enumerate(weights):
        strongest=[(n,w) for n,w in sorted(weight.items(),key=lambda item:-item[1])[:4] if w>1e-7]
        total=sum(w for _,w in strongest)
        if not strongest:strongest=[('Spine',1.)];total=1.
        for name,value in strongest:mesh.vertex_groups[name].add([index],value/total,'REPLACE')


def lupine_head(mesh):
    """Continuous V5 head refinement; torso, limbs, tail and rig stay unchanged."""
    for vertex in mesh.data.vertices:
        p=vertex.co.copy();x,y,z=p;sign=1 if y>=0 else -1
        if x<1.05 or z<2.15:
            continue
        # Eyeball and surrounding orbital surface follow one smooth field.
        center=Vector((1.51089,sign*.14877,2.65320));delta=p-center
        radius=math.sqrt((delta.x/.19)**2+(delta.y/.15)**2+(delta.z/.18)**2)
        orbit=1-smooth(.66,1.42,radius)
        q=p+Vector((delta.x*-.12,delta.y*-.12,delta.z*-.30))*orbit
        # Raise the outer corner with its eyelid and eyeball, retaining openness.
        q.z+=.45*sign*delta.y*orbit
        # Preserve the strong muzzle root and progressively taper its terminal third.
        muzzle=smooth(1.48,1.94,x)
        q.x+=.075*muzzle
        q.y*=1-.20*muzzle
        q.z=2.45+(q.z-2.45)*(1-.23*muzzle)
        # Fill the stop gradually while leaving the rounded cranial vault intact.
        bridge=math.exp(-((x-1.69)/.145)**2)*smooth(2.48,2.65,z)
        q.z+=.067*bridge*(1-smooth(.10,.24,abs(y)))
        forehead=math.exp(-((x-1.48)/.13)**2)*smooth(2.74,2.88,z)
        q.z-=.025*forehead*(1-smooth(.10,.23,abs(y)))
        # Compact the shells above their bases, with no global size renormalization.
        ear=smooth(.11,.17,abs(y))*smooth(.95,1.12,x)*(1-smooth(1.50,1.65,x))
        q.z-=max(0.,z-2.76)*.43*ear
        tip=smooth(3.00,3.15,z)*ear
        q.x=1.25+(q.x-1.25)*(1-.28*tip)
        q.y=sign*.29+(q.y-sign*.29)*(1-.32*tip)
        # The nose's height reduces independently of the closed lips below it.
        nose=smooth(1.87,1.94,x)*smooth(2.39,2.43,z)*(1-smooth(2.53,2.58,z))
        q.z-=.10*(q.z-2.47)*nose
        # A small upper-cheek volume behind the eye supports a wolf silhouette.
        cheek=math.exp(-((x-1.25)/.16)**2-((z-2.57)/.16)**2)
        q.y*=1+.055*cheek
        vertex.co=q


def refine_wedge_and_shoulders(mesh):
    """V6 narrows the snout cross-section and adds restrained anterior mass."""
    torso_groups={g.index for g in mesh.vertex_groups if g.name in ('Chest','Neck','Spine.02')}
    eyes=mesh.data.attributes['EyeMask']
    for vertex in mesh.data.vertices:
        p=vertex.co.copy();x,y,z=p
        # A continuous cross-section change keeps nose, lips and jaw attached.
        # Its posterior limit is ahead of the eyes, leaving the V5 gaze intact.
        if eyes.data[vertex.index].value<.5 and x>1.62:
            taper=smooth(1.62,2.015,x)*smooth(2.10,2.25,z)*(1-smooth(2.58,2.73,z))
            p.y*=1-.33*taper
            p.z=2.42+(p.z-2.42)*(1-.43*taper)
            mandible=(1-smooth(2.34,2.43,z))*smooth(1.58,1.82,x)
            p.z+=.022*mandible
            p.y*=1-.07*mandible
        # Skin ownership excludes the limbs; the falloffs taper into the back
        # and neck rather than introducing a visible shoulder pad.
        ownership=sum(g.weight for g in vertex.groups if g.group in torso_groups)
        region=(smooth(-.10,.22,x)*(1-smooth(1.16,1.42,x))
                *smooth(1.20,1.60,z)*(1-smooth(2.15,2.46,z)))
        shoulder=region*ownership
        p.y*=1+.30*shoulder
        chest=(smooth(.25,.58,x)*(1-smooth(1.08,1.30,x))
               *smooth(1.07,1.38,z)*(1-smooth(1.85,2.12,z))*ownership)
        p.x+=.15*chest
        sternum=math.exp(-((x-.65)/.38)**2-((z-1.38)/.24)**2)*ownership
        sternum*=smooth(1.08,1.30,z)*(1-smooth(1.85,2.05,z))*smooth(.1,.32,x)*(1-smooth(1.1,1.35,x))
        p.z-=.075*sternum
        vertex.co=p


def rebuild_white_wolf(mesh,rig,clean_head):
    """V7 rebuilds the head/ruff silhouette around the standing white reference."""
    eyes=mesh.data.attributes['EyeMask']
    names={g.index:g.name for g in mesh.vertex_groups}
    # The V4 skull is the clean anatomical source. V5/V6 remain the paint
    # reference only; their accumulated muzzle warps are not inherited here.
    for vertex,prior in zip(mesh.data.vertices,clean_head):
        restore=smooth(.98,1.22,prior.x)*smooth(2.12,2.40,prior.z)
        vertex.co=vertex.co.lerp(prior,restore)
    centers={}
    for sign in (-1,1):
        points=[v.co.copy() for v in mesh.data.vertices
                if eyes.data[v.index].value>.9 and v.co.y*sign>0]
        centers[sign]=sum(points,Vector())/len(points)
    neck=rig.data.bones['Neck'].head_local.copy()
    head=rig.data.bones['Head'].head_local.copy()
    neck_transform=(Matrix.Translation((0,0,-.07))@Matrix.Translation(neck)
                    @Matrix.Rotation(math.radians(45),4,'Y')@Matrix.Translation(-neck))
    moved_head=neck_transform@head
    head_transform=(Matrix.Translation(moved_head)@Matrix.Rotation(math.radians(-20),4,'Y')
                    @Matrix.Translation(-moved_head)@neck_transform)
    ear_transforms={}
    for side in ('L','R'):
        pivot=head_transform@rig.data.bones['Ear.'+side].head_local
        ear_transforms['Ear.'+side]=(Matrix.Translation(pivot)@Matrix.Rotation(math.radians(-20),4,'Y')
                                     @Matrix.Translation(-pivot)@head_transform)

    def sculpt(point,eye=False):
        p=point.copy();x,y,z=p;sign=1 if y>=0 else -1
        center=centers[sign];delta=p-center
        radius=math.sqrt((delta.x/.19)**2+(delta.y/.17)**2+(delta.z/.17)**2)
        orbit=1. if eye else 1-smooth(.68,1.38,radius)
        p-=delta*(.44*orbit)
        p.y-=sign*.012*orbit
        p.z+=.035*sign*delta.y*orbit
        if eye:
            return p
        # Replace the rounded frontal dome with a long, supported cranial slope.
        vault=(smooth(1.10,1.28,x)*(1-smooth(1.54,1.70,x))
               *smooth(2.66,2.84,z)*(1-smooth(.10,.22,abs(y))))
        p.z-=.080*vault*(1-orbit)
        temple=math.exp(-((x-1.39)/.24)**2-((z-2.69)/.20)**2)
        p.y*=1-.06*temple*(1-orbit)
        # A single modest taper keeps a volumetric nose and supported jaw.
        taper=smooth(1.58,1.95,x)*(1-orbit)
        p.y*=1-.24*taper
        p.z=2.44+(p.z-2.44)*(1-.16*taper)
        # Fill the source stop slightly, without a local ridge at the root.
        bridge=math.exp(-((x-1.65)/.18)**2)*smooth(2.48,2.68,z)
        p.z+=.025*bridge*(1-smooth(.10,.24,abs(y)))*(1-orbit)
        lip=math.exp(-((x-1.61)/.14)**2-((z-2.40)/.10)**2)*smooth(.07,.14,abs(y))
        p.y*=1-.10*lip
        p.z-=.018*lip
        # Ear shells keep their height; taper the whole upper shell into a point.
        ear=(smooth(.12,.19,abs(y))*smooth(.98,1.12,x)*(1-smooth(1.47,1.60,x)))
        p.z-=max(0.,z-2.76)*.43*ear
        height=smooth(2.76,2.98,p.z)*ear
        p.x=1.245+(p.x-1.245)*(1-.62*height)
        p.y=sign*.285+(p.y-sign*.285)*(1-.60*height)
        p.z+=.035*height
        # Three connected cheek planes become soft sculpted fur tips after subdivision.
        cheek=(smooth(.13,.23,abs(y))*smooth(2.26,2.44,z)*(1-smooth(2.70,2.82,z)))
        ridge=(.065*math.exp(-((x-1.20)/.12)**2-((z-2.58)/.13)**2)
               +.055*math.exp(-((x-1.12)/.15)**2-((z-2.40)/.10)**2))
        p.y+=sign*ridge*cheek
        p.x-=.075*cheek*math.exp(-((x-1.16)/.18)**2)
        return p

    for vertex in mesh.data.vertices:
        original=vertex.co.copy();x,y,z=original
        weights={names[g.group]:g.weight for g in vertex.groups}
        cranial=sum(weights.get(n,0) for n in ('Head','Ear.L','Ear.R'))
        neck_weight=weights.get('Neck',0)*smooth(1.46,1.94,z)
        p=sculpt(original,eyes.data[vertex.index].value>.5) if x>.98 and z>2.12 else original.copy()
        # The collet grows continuously from thorax through nape, not at the humeri.
        upper=(smooth(1.62,1.95,z)*(1-smooth(2.64,2.80,z))
               *smooth(.30,.65,x)*(1-smooth(.98,1.22,x)))
        ownership=sum(weights.get(n,0) for n in ('Neck','Chest','Spine.02','Head'))
        p.y*=1+.22*upper*ownership
        crest=math.exp(-((x-.68)/.36)**2-((z-2.12)/.30)**2)*ownership
        p.z+=.08*crest*smooth(1.55,1.90,z)*smooth(.05,.30,x)
        nape=math.exp(-((x-.91)/.22)**2-((z-2.36)/.26)**2)*ownership
        p.x-=.05*nape*smooth(1.55,1.90,z)*smooth(.05,.30,x)
        # Apply the new rest carriage with the existing skin partition.
        q=p*(1-cranial-neck_weight)+(neck_transform@p)*neck_weight
        q+=(head_transform@p)*weights.get('Head',0)
        for name,transform in ear_transforms.items():q+=(transform@p)*weights.get(name,0)
        vertex.co=q
    # Relax only the compressed throat transition after the rest carriage.
    # The muzzle, eye islands, shoulder articulation and lower thorax are outside it.
    neighbors=[[] for _ in mesh.data.vertices]
    for edge in mesh.data.edges:
        a,b=edge.vertices;neighbors[a].append(b);neighbors[b].append(a)
    throat=[]
    for v in mesh.data.vertices:
        x,y,z=v.co
        throat.append(smooth(1.0,1.18,x)*(1-smooth(1.60,1.76,x))
                      *smooth(1.45,1.56,z)*(1-smooth(1.86,1.98,z)))
    for _ in range(5):
        previous=[v.co.copy() for v in mesh.data.vertices]
        for vertex,amount,adjacent in zip(mesh.data.vertices,throat,neighbors):
            if amount and adjacent:
                average=sum((previous[i] for i in adjacent),Vector())/len(adjacent)
                vertex.co=previous[vertex.index].lerp(average,.38*amount)
    forehead=head_transform@sculpt(Vector((1.46,0,2.84)))
    mesh['V7ForeheadRaw']=list(forehead)
    mesh['V7EyeCentersRaw']=[list(head_transform@sculpt(centers[s],True)) for s in (-1,1)]
    select(rig);bpy.ops.object.mode_set(mode='EDIT')
    for name,transform in {'Neck':neck_transform,'Head':head_transform,**ear_transforms}.items():
        rig.data.edit_bones[name].transform(transform)
    bpy.ops.object.mode_set(mode='OBJECT')
    mesh.data.update();mesh.update_tag();bpy.context.view_layer.update()


def normalize_and_smooth(rig,mesh,revision=4):
    select(mesh)
    subdiv=mesh.modifiers.new('Painted surface subdivision','SUBSURF');subdiv.levels=1;subdiv.render_levels=1
    bpy.ops.object.modifier_move_up(modifier=subdiv.name)
    reference=None
    scale_reference=None
    if revision>=5:
        clean_head=[v.co.copy() for v in mesh.data.vertices]
        bpy.context.view_layer.update()
        evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
        reference_mesh=evaluated.to_mesh()
        reference=[v.co.copy() for v in reference_mesh.vertices]
        evaluated.to_mesh_clear()
        scale_reference=reference
        lupine_head(mesh)
        if revision>=6:
            mesh.data.update();mesh.update_tag();bpy.context.view_layer.update()
            evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
            reference_mesh=evaluated.to_mesh()
            reference=[v.co.copy() for v in reference_mesh.vertices]
            evaluated.to_mesh_clear()
            refine_wedge_and_shoulders(mesh)
        if revision>=7:
            mesh.data.update();mesh.update_tag();bpy.context.view_layer.update()
            evaluated=mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
            reference_mesh=evaluated.to_mesh()
            reference=[v.co.copy() for v in reference_mesh.vertices]
            evaluated.to_mesh_clear()
            paint=mesh.data.attributes.new('PaintPosition','FLOAT_VECTOR','POINT')
            for v,d in zip(mesh.data.vertices,paint.data):d.vector=v.co
            rebuild_white_wolf(mesh,rig,clean_head)
            select(mesh)
    bpy.ops.object.modifier_apply(modifier=subdiv.name)
    if revision>=7:
        # Subdivision can spread displacement across the region border. The
        # lower thorax and all limbs retain the exact V6 reference positions.
        for vertex,prior in zip(mesh.data.vertices,reference):
            region=smooth(1.43,1.70,prior.z)*smooth(-.34,.15,prior.x)
            vertex.co=prior+(vertex.co-prior)*region
    for v in mesh.data.vertices:
        groups=sorted([(g.group,g.weight) for g in v.groups],key=lambda item:-item[1])[:4]
        total=sum(w for _,w in groups)
        for g in list(v.groups):mesh.vertex_groups[g.group].remove([v.index])
        for index,value in groups:mesh.vertex_groups[index].add([v.index],value/total,'REPLACE')
    for poly in mesh.data.polygons:poly.use_smooth=True
    original=scale_reference if scale_reference is not None else [v.co for v in mesh.data.vertices]
    lo=min(p.z for p in original);hi=max(p.z for p in original)
    scale=1.10/(hi-lo)
    matrix=Matrix.Scale(scale,4)@Matrix.Translation((0,0,-lo))
    mesh.data.transform(matrix);rig.data.transform(matrix)
    if revision>=7:
        for d,prior in zip(mesh.data.attributes['PaintPosition'].data,reference):d.vector=matrix@prior
    select(rig);bpy.ops.object.mode_set(mode='EDIT')
    rig.data.edit_bones['Root'].head=(0,0,0);rig.data.edit_bones['Root'].tail=(0,0,.1)
    bpy.ops.object.mode_set(mode='OBJECT')
    # Coordinate field intentionally matches final rest geometry in meters.
    attr=mesh.data.attributes.new('SourcePosition','FLOAT_VECTOR','POINT')
    for v,d in zip(mesh.data.vertices,attr.data):d.vector=v.co
    rig.data.bones['Root'].use_deform=False
    rig.show_in_front=True;rig.data.display_type='OCTAHEDRAL'
    mesh.parent=rig;mesh.matrix_parent_inverse=Matrix.Identity(4)
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.unit_settings.scale_length=1
    return matrix,[matrix@p for p in reference] if reference is not None else None


def finish_v8_point(point,paint,eye=False):
    """Small bounded surface adjustments on the successful V7 rest shape."""
    p=point.copy();x,y,z=p;sign=1 if y>=0 else -1
    if eye or x<.44 or z<.56 or x>.72 or z>.81:
        return p
    orbit=Vector((.65157,sign*.04797,.70798))
    orbital_distance=(p-orbit).length
    if orbital_distance<.040:
        return p
    def patch(cx,cy,cz,rx,ry,rz):
        r=math.sqrt(((x-cx)/rx)**2+((abs(y)-cy)/ry)**2+((z-cz)/rz)**2)
        return 1-smooth(.20,1.,r)
    dome=patch(.620,0,.757,.070,.085,.060)*smooth(.727,.755,z)
    p.z-=.008*dome;p.x-=.002*dome
    # The upper cheek defines the fur edge; its lower transition fades into the throat.
    upper=patch(.558,.105,.690,.065,.060,.043)
    lower=patch(.518,.112,.643,.075,.075,.050)
    lateral=smooth(.055,.095,abs(y))
    p.y+=sign*(.009*upper+.002*lower)*lateral
    p.x-=(.005*upper+.001*lower)*lateral
    # Move both lips together at the commissure to avoid opening the closed mouth.
    corner=(1-smooth(.2,1.,math.sqrt(((paint.x-.552)/.040)**2
            +((abs(paint.y)-.067)/.040)**2+((paint.z-.846)/.034)**2)))
    p.z-=.0038*corner;p.y-=sign*.002*corner
    lower_lip=(1-smooth(.2,1.,math.sqrt(((paint.x-.586)/.045)**2
               +((abs(paint.y)-.040)/.040)**2+((paint.z-.823)/.018)**2)))
    p.y-=sign*.0018*lower_lip
    return point.lerp(p,smooth(.040,.055,orbital_distance))


def finish_v8(mesh):
    before=[v.co.copy() for v in mesh.data.vertices]
    paint=mesh.data.attributes['PaintPosition'];eyes=mesh.data.attributes['EyeMask']
    for v in mesh.data.vertices:
        v.co=finish_v8_point(v.co,paint.data[v.index].vector,eyes.data[v.index].value>.01)
        mesh.data.attributes['SourcePosition'].data[v.index].vector=v.co
    changed=[v.index for v in mesh.data.vertices if (v.co-before[v.index]).length>1e-8]
    protected=[v.index for v in mesh.data.vertices if before[v.index].x<.44 or before[v.index].z<.56]
    return {'comparison':'V7 to V8; localized forehead, cheek/ruff and mouth finish only',
            'changedVertices':len(changed),
            'maximumDisplacementM':max((v.co-before[v.index]).length for v in mesh.data.vertices),
            'maximumBodyDisplacementM':max((mesh.data.vertices[i].co-before[i]).length for i in protected),
            'maximumEyeIslandDisplacementM':max((v.co-before[v.index]).length for v in mesh.data.vertices if eyes.data[v.index].value>.01),
            'rig':'V7 rest skeleton unchanged','normalization':'V7 metric scale retained'}


def refine_v9_point(point,paint,eye=False):
    """Rebalance the V8 face into a continuous wedge and rearward cheek planes."""
    p=point.copy();x,y,z=p;sign=1 if y>=0 else -1
    if eye or x<.48 or z<.55 or z>.805:
        return p
    orbit=Vector((.65157,sign*.04797,.70798))
    orbital_distance=(p-orbit).length
    guard=smooth(.038,.075,orbital_distance)
    if not guard:
        return p
    # A broad dorsal plane removes the high muzzle-root bulge. The lower
    # chanfrein, lips and nose stay supported rather than becoming a concavity.
    dorsal=(smooth(.590,.625,x)*(1-smooth(.740,.765,x))
            *smooth(.615,.670,z)*(1-smooth(.035,.090,abs(y))))
    root=math.exp(-((x-.657)/.032)**2)
    bridge=math.exp(-((x-.710)/.026)**2)
    p.z+=(.010*bridge-.014*root)*dorsal
    # Progressively converge both sections, retaining a solid terminal nose.
    terminal=smooth(.665,.752,x)*(1-smooth(.69,.75,z))
    axis_z=.640-.667*(x-.675)
    p.y*=1-.14*terminal
    p.z=axis_z+(p.z-axis_z)*(1-.08*terminal)
    # Flatten the forward cushions and carry the cheek edge toward the ruff.
    anterior=(smooth(.520,.615,x)*(1-smooth(.695,.760,x))
              *smooth(.555,.620,z)*(1-smooth(.675,.765,z)))
    lateral=smooth(0.,.140,abs(y))
    p.y*=1-.23*anterior
    rear=(smooth(.480,.540,x)*(1-smooth(.570,.660,x))
          *smooth(.580,.650,z)*(1-smooth(.700,.785,z))*lateral)
    p.x-=.016*rear
    p.y*=1-.10*rear
    # Both lips move together; the tall hooked commissure becomes shorter.
    corner=1-smooth(.15,1.,math.sqrt(((paint.x-.552)/.046)**2
             +((abs(paint.y)-.067)/.044)**2+((paint.z-.846)/.039)**2))
    p.z-=.002*corner;p.x+=.001*corner
    return point.lerp(p,guard)


def refine_v9(mesh):
    before=[v.co.copy() for v in mesh.data.vertices]
    paint=mesh.data.attributes['PaintPosition'];eyes=mesh.data.attributes['EyeMask']
    for v in mesh.data.vertices:
        v.co=refine_v9_point(v.co,paint.data[v.index].vector,eyes.data[v.index].value>.01)
    # Diffuse the displacement, not the anatomy, so dense orbital topology does
    # not turn a smooth regional change into small folds around fixed eyelids.
    neighbors=[[] for _ in before]
    for edge in mesh.data.edges:
        a,b=edge.vertices;neighbors[a].append(b);neighbors[b].append(a)
    protected=[]
    for i,p in enumerate(before):
        orbit=Vector((.65157,(1 if p.y>=0 else -1)*.04797,.70798))
        protected.append(eyes.data[i].value>.01 or p.x<.48 or p.z<.55 or p.z>.805 or (p-orbit).length<.038)
    delta=[v.co-before[v.index] for v in mesh.data.vertices]
    for _ in range(7):
        previous=[d.copy() for d in delta]
        for i,adjacent in enumerate(neighbors):
            if protected[i]:delta[i]=Vector()
            elif adjacent:
                delta[i]=previous[i].lerp(sum((previous[j] for j in adjacent),Vector())/len(adjacent),.55)
    for v in mesh.data.vertices:
        v.co=before[v.index]+delta[v.index]
        mesh.data.attributes['SourcePosition'].data[v.index].vector=v.co
    displacement=[(v.co-before[v.index]).length for v in mesh.data.vertices]
    return {'comparison':'V8 to V9; continuous dorsal muzzle plane and rearward cheek balance',
            'changedVertices':sum(d>1e-8 for d in displacement),
            'maximumDisplacementM':max(displacement),
            'maximumBodyDisplacementM':max(displacement[i] for i,p in enumerate(before) if p.x<.48 or p.z<.55),
            'maximumEyeIslandDisplacementM':max(displacement[i] for i,d in enumerate(eyes.data) if d.value>.01),
            'rig':'V8 rest skeleton unchanged','normalization':'V8 metric scale retained'}


def reinforce_v10(mesh,reference_v8):
    """Restore a supported face and strengthen the adult wolf's thorax and collet."""
    before=[v.co.copy() for v in mesh.data.vertices]
    eyes=mesh.data.attributes['EyeMask']
    names={g.index:g.name for g in mesh.vertex_groups}
    torso_names={'Hips','Spine','Spine.02','Chest','Neck'}
    for v,base in zip(mesh.data.vertices,reference_v8):
        p=base.copy();x,y,z=p;sign=1 if y>=0 else -1
        # Recover V8 muzzle/jaw support while keeping the rear cheek balance.
        cheek=(smooth(.050,.105,abs(y))*(1-smooth(.590,.680,x))
               *smooth(.575,.640,z)*(1-smooth(.735,.790,z)))
        keep=.18+.82*cheek
        p.x+=(before[v.index].x-p.x)*keep
        p.y+=(before[v.index].y-p.y)*keep
        p.z=before[v.index].z
        if eyes.data[v.index].value<.01 and x>.615 and z<.72:
            orbit=Vector((.65157,sign*.04797,.70798))
            guard=smooth(.038,.075,(base-orbit).length)
            base_muzzle=smooth(.615,.670,x)*(1-smooth(.720,.765,x))*guard
            axis_z=.640-.667*(x-.675)
            p.z=axis_z+(p.z-axis_z)*(1+.12*base_muzzle)
            # Both lips and nose follow one broad section change; no mouth opening.
            nose=smooth(.708,.752,x)*(1-smooth(.650,.705,z))*guard
            p.y*=1+.13*nose
            p.z=.594+(p.z-.594)*(1-.15*nose)
            p.x=.742+(p.x-.742)*(1-.07*nose)
            p.z+=.017*smooth(.657,.748,x)*(1-smooth(.670,.730,z))*guard
        if eyes.data[v.index].value<.01 and .56<x<.70 and z>.735:
            dome=(smooth(.560,.610,x)*(1-smooth(.660,.700,x))
                  *smooth(.735,.785,z)*(1-smooth(.025,.090,abs(y))))
            p.z-=.004*dome
        head_point=p.copy()
        weights={names[g.group]:g.weight for g in v.groups}
        torso=sum(weights.get(n,0.) for n in torso_names)
        upper=sum(w for n,w in weights.items() if n.startswith(('FrontUpper.','HindUpper.')))
        ownership=min(1.,torso+upper)
        # Deep ribs, breast and collet share broad falloffs rather than isolated pads.
        ribs=(smooth(-.430,-.240,x)*(1-smooth(.300,.480,x))
              *smooth(.355,.490,z)*(1-smooth(.780,.860,z))*ownership)
        p.y*=1+.30*ribs
        p.z=.630+(p.z-.630)*(1+.25*ribs*torso)
        breast=(smooth(.070,.200,x)*(1-smooth(.355,.490,x))
                *smooth(.350,.480,z)*(1-smooth(.650,.770,z))*ownership)
        p.x+=.035*breast
        p.z-=.022*breast*(1-smooth(.560,.700,z))
        collet=(smooth(.190,.310,x)*(1-smooth(.430,.520,x))
                *smooth(.490,.620,z)*(1-smooth(.790,.850,z))*torso)
        p.y*=1+.20*collet
        p.z+=.027*collet*smooth(.630,.770,z)
        thigh=(smooth(-.660,-.510,x)*(1-smooth(-.350,-.220,x))
               *smooth(.400,.520,z)*(1-smooth(.690,.790,z))*ownership)
        p.y*=1+.19*thigh
        p.z+=.012*thigh*smooth(.540,.680,z)
        body_delta=p-head_point
        p=head_point+body_delta*.85
        p.y=head_point.y+body_delta.y*.60
        v.co=p
    # Smooth only the added displacement; fixed feet and eye islands remain exact.
    neighbors=[[] for _ in before]
    for e in mesh.data.edges:
        a,b=e.vertices;neighbors[a].append(b);neighbors[b].append(a)
    delta=[v.co-before[v.index] for v in mesh.data.vertices]
    preserved_shells=[v.index for v in mesh.data.vertices
                      if any(names[g.group].startswith(('Ear.','Tail.')) and g.weight>.001 for g in v.groups)]
    tree=KDTree(len(preserved_shells))
    for i,index in enumerate(preserved_shells):tree.insert(before[index],i)
    tree.balance()
    guard=[smooth(.008,.065,tree.find(p)[2]) for p in before]
    for i in range(len(delta)):delta[i]*=guard[i]
    fixed=[eyes.data[i].value>.01 or p.z<.350 or p.z>.840 or p.x<-.690 or guard[i]==0 for i,p in enumerate(before)]
    for _ in range(5):
        previous=[d.copy() for d in delta]
        for i,adjacent in enumerate(neighbors):
            if fixed[i]:delta[i]=Vector()
            elif adjacent:delta[i]=previous[i].lerp(sum((previous[j] for j in adjacent),Vector())/len(adjacent),.45)
    for v in mesh.data.vertices:
        delta[v.index]*=guard[v.index]
        v.co=before[v.index]+delta[v.index]
        mesh.data.attributes['SourcePosition'].data[v.index].vector=v.co
    displacement=[d.length for d in delta]
    return {'comparison':'V9 to V10; fuller thorax, breast, collet and upper thighs; supported V8-based muzzle',
            'changedVertices':sum(d>1e-8 for d in displacement),'maximumDisplacementM':max(displacement),
            'maximumLowerLimbDisplacementM':max(displacement[i] for i,p in enumerate(before) if p.z<.350),
            'maximumEyeIslandDisplacementM':max(displacement[i] for i,d in enumerate(eyes.data) if d.value>.01),
            'rig':'V9 rest skeleton unchanged','normalization':'V9 metric scale retained'}


def material(mesh):
    mat=bpy.data.materials.new('TotemFurSourceV4');mat.use_nodes=True
    shader=mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value=.85;shader.inputs['Specular IOR Level'].default_value=.15
    image=bpy.data.images.load(str(SOURCE.parent/'dog2Color.png'),check_existing=False)
    image.name='WolfV4_NewDLC_Diffuse';image.pack()
    texture=mat.node_tree.nodes.new('ShaderNodeTexImage');texture.name='WolfV4 diffuse';texture.image=image
    mat.node_tree.links.new(texture.outputs['Color'],shader.inputs['Base Color'])
    mesh.data.materials.clear();mesh.data.materials.append(mat)
    for p in mesh.data.polygons:p.material_index=0
    return image


def metadata(rig,mesh,image,removed,matrix):
    points=[v.co for v in mesh.data.vertices]
    bounds={key:[fn(p[i] for p in points) for i in range(3)] for key,fn in [('min',min),('max',max)]}
    limbs=[]
    for side,sign in [('L',1),('R',-1)]:
        for kind in ('Front','Hind'):
            groups={mesh.vertex_groups[f'{kind}{part}.{side}'].index for part in ('Foot','Toe')}
            foot=[v.co for v in mesh.data.vertices if sum(g.weight for g in v.groups if g.group in groups)>.75]
            floor=min(p.z for p in foot);sole=[p for p in foot if p.z<floor+.012]
            point=Vector((sum(p.x for p in sole)/len(sole),sum(p.y for p in sole)/len(sole),floor))
            limbs.append({'id':('Fore' if kind=='Front' else 'Hind')+'.'+side,
                          'upper':kind+'Upper.'+side,'lower':kind+'Lower.'+side,'foot':kind+'Foot.'+side,
                          'toe':kind+'Toe.'+side,'pole_forward':-1 if kind=='Front' else 1,'sole_rest':list(point)})
    mesh.data.calc_loop_triangles()
    return {'species':'wolf','schemaVersion':2,'artRevision':4,'units':'meters','forwardAxis':'+X','upAxis':'+Z',
        'source':'sources/newdlc/dog2.blend','sourceSHA256':hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'sourceCredit':'3d wolf by NewDLC, CC0 1.0; mesh, rig, coats, animations and magical markings adapted for Arkya. Source: https://opengameart.org/content/3d-wolf',
        'provenance':{'author':'NewDLC','title':'3d wolf','url':'https://opengameart.org/content/3d-wolf','license':'CC0 1.0','notice':'sources/newdlc/README.md'},
        'coatProfile':'wolf-v4','coatImage':image.name,
        'anatomy':{'hip':list(rig.data.bones['Hips'].head_local),'shoulder':list(rig.data.bones['Chest'].head_local),
                   'neck':list(rig.data.bones['Neck'].head_local),'forehead':list(matrix@Vector((1.46,0,2.84)))},
        'rig':{'name':rig.name,'bones':len(rig.data.bones),'root':'Root','hips':'Hips','head':'Head',
               'joints':{b.name:{'head':list(b.head_local),'tail':list(b.tail_local),'parent':b.parent.name if b.parent else None} for b in rig.data.bones}},
        'triangles':len(mesh.data.loop_triangles),'vertices':len(mesh.data.vertices),'bounds':bounds,
        'dimensions':dict(zip(('lengthM','widthM','heightM'),[bounds['max'][i]-bounds['min'][i] for i in range(3)])),
        'locomotionConfig':{'meters_per_unit':1,'limbs':limbs},
        'modifications':['Independent canine source; original skull, muzzle and jaw volumes retained',
            f'Removed {removed} vertices from disconnected fringe hair cards; retained closed skull and eye islands',
            'Modestly shorter ear shells','Jaw closed 5.5 degrees at its source articulation for a calm rest expression',
            'Relaxed four-bone tail applied as rest','29-bone normalized locomotion contract',
            'Localized limb weights, normalized maximum four influences','One smooth subdivision with UVs retained',
            'EyeMask and meter-space SourcePosition attributes for painted coat']}


def render_review(mesh,revision=4):
    renders=BASE.parents[3]/f'output/totem-wolf-v{revision}/anatomy'
    renders.mkdir(parents=True,exist_ok=True)
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=20;scene.cycles.use_denoising=True
    scene.render.resolution_x=1280;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new('Wolf V4 neutral studio');scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.055,.065,.08,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.65
    points=[v.co for v in mesh.data.vertices];lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)])
    center=(lo+hi)/2;size=max(hi-lo)
    for name,offset,power in [('Key',(1,-2,3),650),('Fill',(2,2,1.5),300),('Rim',(-2,1,2),700)]:
        data=bpy.data.lights.new(name,'AREA');obj=bpy.data.objects.new(name,data);scene.collection.objects.link(obj)
        obj.location=center+Vector(offset)*size;data.energy=power*size*size;data.size=size*2
        obj.rotation_euler=(center-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Wolf V4 review');cam=bpy.data.objects.new('Wolf V4 review',data);scene.collection.objects.link(cam)
    data.type='ORTHO';scene.camera=cam;scene.view_settings.view_transform='AgX'
    face=Vector((.51,0,.92))
    if revision>=7:
        clay=bpy.data.materials.new('V7 clay review');clay.use_nodes=True
        shader=clay.node_tree.nodes.get('Principled BSDF')
        shader.inputs['Roughness'].default_value=.78
        shader.inputs['Specular IOR Level'].default_value=.18
        attr=clay.node_tree.nodes.new('ShaderNodeAttribute');attr.attribute_name='EyeMask'
        mix=clay.node_tree.nodes.new('ShaderNodeMixRGB')
        mix.inputs[1].default_value=(.32,.34,.36,1);mix.inputs[2].default_value=(.065,.07,.075,1)
        clay.node_tree.links.new(attr.outputs['Fac'],mix.inputs[0])
        clay.node_tree.links.new(mix.outputs[0],shader.inputs['Base Color'])
        mesh.data.materials.clear();mesh.data.materials.append(clay)
        face=Vector((.64,0,.72))
    for name,direction,target,zoom in [('profile',(0,-3,.10),center,size*1.18),('three-quarter',(1.8,-3,.5),center,size*1.18),
                                       ('face',(3,0,.12),face,.65),('face-three-quarter',(2,-3,.35),face,.72),
                                       ('head-profile',(0,-3,.0),face,.65)]:
        data.ortho_scale=zoom;cam.location=target+Vector(direction)*size
        cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
        path=renders/f'wolf-v{revision}-{name}.png'
        temporary=path.with_name(path.stem+'.pending.png')
        scene.render.filepath=str(temporary);bpy.ops.render.render(write_still=True)
        temporary.replace(path)


def head_measurements(mesh,reference,matrix,revision=5):
    after=[v.co.copy() for v in mesh.data.vertices]
    regions={
        'muzzle':lambda p:p.x>1.55 and 2.15<p.z<2.73,
        'terminalMuzzle':lambda p:p.x>1.80 and 2.15<p.z<2.65,
        'earShells':lambda p:1.05<p.x<1.5 and abs(p.y)>.13 and p.z>2.78,
        'leftEye':lambda p:False,
    }
    if revision>=6:
        regions.update({'upperShoulders':lambda p:.22<p.x<1.15 and 1.60<p.z<2.16,
                        'chest':lambda p:.25<p.x<1.2 and 1.10<p.z<1.85,
                        'muzzleEnd':lambda p:p.x>1.94 and 2.15<p.z<2.65})
    inverse=matrix.inverted();raw=[inverse@p for p in reference]
    records={}
    for name,predicate in regions.items():
        indices=[i for i,p in enumerate(raw) if predicate(p)]
        if name=='leftEye':
            indices=[v.index for v in mesh.data.vertices if v.co.y>0 and mesh.data.attributes['EyeMask'].data[v.index].value>.9]
        def bounds(points):
            low=[min(points[i][a] for i in indices) for a in range(3)]
            high=[max(points[i][a] for i in indices) for a in range(3)]
            return {'min':low,'max':high,'sizeM':[high[a]-low[a] for a in range(3)]}
        records[name]={'vertices':len(indices),'before':bounds(reference),'after':bounds(after)}
    if revision>=7:
        outside=[i for i,p in enumerate(reference) if p.z<.48 or p.x<-.12]
        records['maximumDisplacementOutsideHeadNeckM']=max((after[i]-reference[i]).length for i in outside)
        records['comparison']='V6 to V7; head, neck and upper thorax authorized'
    elif revision>=6:
        def authorized(p):
            return ((-.20<=p.x<=.53 and .34<=p.z<=.94)
                    or (.49<=p.x<=.72 and .75<=p.z<=.97))
        outside=[i for i,p in enumerate(reference) if not authorized(p)]
        records['maximumDisplacementOutsideAuthorizedRegionsM']=max((after[i]-reference[i]).length for i in outside)
        records['authorizedRegionsMeters']={'upperThoraxChestNeck':{'x':[-.20,.53],'z':[.34,.94]},
                                           'muzzleAndCheekBlend':{'x':[.49,.72],'z':[.75,.97]}}
        records['comparison']='V5 to V6; head and upper shoulders/chest/neck are authorized regions'
    else:
        outside=[i for i,p in enumerate(raw) if p.x<.95 or p.z<2.05]
        records['maximumDisplacementOutsideHeadM']=max((after[i]-reference[i]).length for i in outside)
    records['normalization']='V4 normalization retained; shortened ears do not enlarge the body'
    return records


def build(render=False,output_directory=None,revision=10):
    output=Path(output_directory).resolve() if output_directory else (BASE/f'assets/anatomy/draft-wolf-v{revision}')
    output.mkdir(parents=True,exist_ok=True)
    rig,mesh,removed=import_source();lower_tail(rig,mesh);build_contract(rig,mesh)
    matrix,reference=normalize_and_smooth(rig,mesh,revision)
    refinement=finish_v8(mesh) if revision>=8 else None
    reference_v8=[v.co.copy() for v in mesh.data.vertices] if revision>=10 else None
    wedge_refinement=refine_v9(mesh) if revision>=9 else None
    adult_refinement=reinforce_v10(mesh,reference_v8) if revision>=10 else None
    image=material(mesh)
    info=metadata(rig,mesh,image,removed,matrix)
    info['artRevision']=revision
    if revision>=5:
        info['headRefinement']=head_measurements(mesh,reference,matrix,revision)
        if revision<7:
            info['modifications'].extend(['V5: progressive muzzle elongation and terminal taper',
                'V5: softer stop, smaller orbital opening and compact ear shells',
                'V5: unchanged V4 body, limb, tail positions and skeleton'])
    if revision==6:
        info['modifications'].extend(['V6: converging muzzle width and height with a thinner mandibular silhouette; no added length',
                                     'V6: broader upper thorax and fuller chest with torso-weight falloffs; joint positions retained'])
    if revision>=7:
        info['coatProfile']='wolf-v7'
        info['anatomy']['forehead']=list(matrix@Vector(mesh['V7ForeheadRaw']))
        info['anatomy']['eyeCenters']=[list(matrix@Vector(p)) for p in mesh['V7EyeCentersRaw']]
        info['modifications'].extend(['V7: head rebuilt from clean V4 anatomy with one moderate taper; V5/V6 muzzle warps discarded',
                                     'V7: V6 upper thorax retained; lower limbs, feet, tail and their joint positions preserved',
                                     'V7: white standing wolf reference; lower forward head carried in a continuous oblique collet',
                                     'V7: Head, Neck and Ear rest pivots follow the new head carriage',
                                     'V7: smaller inset orbital complex, triangular ears, integrated cheek and nape relief',
                                     'V7: volumetric nose, quiet mouth corners and relaxed throat transition',
                                     'V7: PaintPosition retains V6 coat coordinates; SourcePosition is final metric rest geometry'])
    if revision>=8:
        info['coatProfile']='wolf-v8'
        info['headRefinement']['comparison']='V6 to V8; V7 structural rebuild followed by localized V8 surface finish'
        old_forehead=Vector(info['anatomy']['forehead'])
        paint_forehead=Vector((.5121,0,.9987))
        info['anatomy']['forehead']=list(finish_v8_point(old_forehead,paint_forehead))
        info['surfaceRefinement']=refinement
        info['modifications'].extend(['V8: localized flatter forehead, defined upper cheek with a soft throat transition, and quieter closed mouth corners',
                                     'V8: V7 carriage, muzzle/truffle, eye islands, rest skeleton, lower body, UVs and weights retained'])
    if revision>=9:
        info['coatProfile']='wolf-v9'
        info['headRefinement']['comparison']='V6 to V9; V7 structural rebuild, V8 finish and V9 muzzle/cheek refinement'
        info['anatomy']['forehead']=list(refine_v9_point(Vector(info['anatomy']['forehead']),Vector((.5121,0,.9987))))
        info['muzzleRefinement']=wedge_refinement
        info['modifications'].extend(['V9: flatter continuous chanfrein with a supported progressive wedge and compact volumetric nose',
                                     'V9: flatter anterior cheeks, rearward upper cheek planes and a shorter closed commissure',
                                     'V9: V8 body, shoulder mass, head carriage, eyes, ears, complete rig, UVs and weights preserved'])
    if revision>=10:
        info['coatProfile']='wolf-v10'
        info['headRefinement']['comparison']='V6 to V10; structural V7 rebuild with V10 adult body and supported V8-based muzzle'
        info['bodyRefinement']=adult_refinement
        info['modifications'].extend(['V10: fuller rib cage, deeper breast and continuous nape/collet with balanced upper-thigh mass',
                                     'V10: V8 muzzle and mandibular support restored while retaining rearward V9 cheek planes',
                                     'V10: broader transverse nose, closed mouth, no added muzzle length',
                                     'V10: rest skeleton, lower limbs, feet, eyes, ears, UVs, weights and PaintPosition preserved'])
    (output/'wolf-rig.json').write_text(json.dumps(info,indent=2),encoding='utf-8')
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(output/'wolf.blend'))
    print(f'WOLF_V{revision}_DRAFT_READY',json.dumps({'path':str(output/'wolf.blend'),'dimensions':info['dimensions'],'triangles':info['triangles']}),flush=True)
    if render:render_review(mesh,revision)
    return info


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--render',action='store_true');parser.add_argument('--output-dir');parser.add_argument('--revision',type=int,choices=(4,5,6,7,8,9,10),default=10)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    build(args.render,args.output_dir,args.revision)
