"""Build compact, skinned game characters from the preserved appearance studies.

Blender 4.5: --background --disable-autoexec --python rig_characters.py
All generated assets live in game/. The original studies remain read-only.
"""
import argparse
import bpy
import json
import math
import sys
from collections import defaultdict
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT/'game'
CREDIT = 'Rain and Snow by Blender Studio / Blender Foundation, CC BY 4.0. Arkya adaptations: fantasy costumes, painted surfaces, proportions, elf ears, hairstyles, compact rig and procedural locomotion. See ../CREDITS.md.'


def select(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def clamp(x):return max(0.,min(1.,x))


def smooth(x):
    t=clamp(x)
    return t*t*(3-2*t)


def skeleton(silhouette,race):
    source=json.loads((OUT/'source-joints.json').read_text())[silhouette]
    original=source['bodyBounds']
    body=bpy.data.objects['Body']
    points=[v.co for v in body.data.vertices]
    # Wrist/fingertip extents were untouched by the mature-head adjustment.
    sx=max(p.x for p in points)/original[1][0]
    sy=sx/.92*.95 if race=='elf' else sx
    sz=sx/.92 if race=='elf' else sx
    dz=min(p.z for p in points)-original[0][2]*sz

    def p(name,end='head'):
        v=Vector(source['bones'][name][end]);v.x*=sx;v.y*=sy;v.z=v.z*sz+dz
        return v

    snow=silhouette=='a'
    spine=p('FK-Spine');neck=p('FK-Neck');head=p('FK-Head')
    # Keep the game neck/head pivot inside the mature DA proportions.
    head.z=min(head.z,max(v.co.z for v in bpy.data.objects['Head'].data.vertices)-.16)
    joints={
        'Root':(Vector((0,0,0)),Vector((0,0,.1)),None),
        'Hips':(spine-Vector((0,0,.055)),spine,'Root'),
        'Spine':(spine,p('FK-RibCage'),'Hips'),
        'Chest':(p('FK-RibCage'),neck,'Spine'),
        'Neck':(neck,head,'Chest'),
        'Head':(head,Vector((head.x,head.y,1.75 if race=='human' else 1.85)),'Neck'),
    }
    for side,prefix in [('L','Left'),('R','Right')]:
        upper=p(('FK-UpperArm.' if snow else 'FK-Upperarm.')+side)
        elbow=p('FK-Forearm.'+side)
        wrist=p(('FK-Wrist.' if snow else 'FK-Hand.')+side)
        hand=p(('FK-Wrist.' if snow else 'FK-Hand.')+side,'tail')
        hip=p(('DEF-Thigh_1.' if snow else 'DEF-Thigh1.')+side)
        knee=p(('DEF-Knee_1.' if snow else 'DEF-Shin1.')+side)
        ankle=p('DEF-Foot.'+side)
        toe=p('DEF-Foot.'+side,'tail')
        joints.update({
            prefix+'Shoulder':(Vector((0,upper.y,upper.z)),upper,'Chest'),
            prefix+'UpperArm':(upper,elbow,prefix+'Shoulder'),
            prefix+'LowerArm':(elbow,wrist,prefix+'UpperArm'),
            prefix+'Hand':(wrist,hand,prefix+'LowerArm'),
            prefix+'UpperLeg':(hip,knee,'Hips'),
            prefix+'LowerLeg':(knee,ankle,prefix+'UpperLeg'),
            prefix+'Foot':(ankle,toe,prefix+'LowerLeg'),
            prefix+'Toes':(toe,toe+Vector((0,-.06,0)),prefix+'Foot'),
        })
    h=1.85 if race=='elf' else 1.75
    joints['CapeUpper']=(Vector((0,.09,h*.76)),Vector((0,.16,h*.55)),'Chest')
    joints['CapeLower']=(Vector((0,.16,h*.55)),Vector((0,.20,h*.31)),'CapeUpper')
    joints['HairUpper']=(Vector((0,.09,h*.89)),Vector((0,.14,h*.77)),'Head')
    joints['HairLower']=(Vector((0,.14,h*.77)),Vector((0,.17,h*.66)),'HairUpper')
    arm=bpy.data.armatures.new('ArkyaGameSkeleton')
    rig=bpy.data.objects.new('Armature',arm);bpy.context.scene.collection.objects.link(rig)
    select(rig);bpy.ops.object.mode_set(mode='EDIT')
    for name,(start,end,parent) in joints.items():
        b=arm.edit_bones.new(name);b.head=start;b.tail=end
        if parent:b.parent=arm.edit_bones[parent]
        b.use_deform=True
    bpy.ops.object.mode_set(mode='OBJECT')
    rig.show_in_front=True
    rig['attribution']=CREDIT
    rig['rigVersion']=1
    rig['animationContract']='Generic, Root, Idle/Walk/Run, in-place, 30 fps'
    return rig,joints


def remap_group(name):
    if not name.startswith('DEF-'):return None
    n=name[4:].lower()
    side='Left' if n.endswith('.l') else 'Right' if n.endswith('.r') else ''
    if 'upperarm' in n:return side+'UpperArm'
    if 'forearm' in n:return side+'LowerArm'
    if any(s in n for s in ('wrist','hand','finger','thumb','index','pinky','middle','ring')) and side:
        if 'eye' not in n:return side+'Hand'
    if 'shoulder' in n or 'clavicle' in n:return side+'Shoulder'
    if 'thigh' in n:return side+'UpperLeg'
    if 'knee' in n or 'shin' in n:return side+'LowerLeg'
    if 'ankle' in n:return side+'LowerLeg'
    if 'toe' in n:return side+'Foot'
    if 'foot' in n:return side+'Foot'
    if 'pelvis' in n or 'hip' in n:return 'Hips'
    if n in ('spine','spine1'):return 'Spine'
    if n in ('ribcage','chest','spine2','spine3'):return 'Chest'
    if n=='neck':return 'Neck'
    if n.startswith('scarf'):return 'Chest'
    if any(s in n for s in ('head','hair','ponytail','scarf','ey','lip','cheek','ear','chin','jaw','forehead','nose','nostril','temple')):return 'Head'
    return None


def axial_weights(p,points,names):
    # Smooth local transition across authored joint heights for costume surfaces.
    z=p.z
    for i in range(len(points)-1):
        upper,lower=points[i],points[i+1]
        if z>=lower:
            t=smooth((upper-z)/max(.001,upper-lower))
            return {names[i]:1-t,names[i+1]:t}
    return {names[-1]:1.}


def costume_weights(obj,p,joints,h):
    name=obj.name.lower();side='Left' if p.x>0 else 'Right'
    if name.startswith(('hairlong','hairbraid')):
        return axial_weights(p,[h*.90,h*.78,h*.66],['Head','HairUpper','HairLower'])
    if obj.name in ('HairMain','Eyes') or 'eyebrow' in name or 'eyelash' in name or 'hair_' in name:
        return {'Head':1.}
    if any(s in name for s in ('cape','cloak','mantle','embroidered ancient','embroidered branch')):
        return axial_weights(p,[h*.78,h*.61,h*.38],['Chest','CapeUpper','CapeLower'])
    if any(s in name for s in ('pauldron','shoulder')):
        return {side+'UpperArm':1.}
    if any(s in name for s in ('bracer','vambrace','forearm')):
        return {side+'LowerArm':1.}
    if any(s in name for s in ('boot foot','toe cap','sabaton','sole','shoe')):
        return {side+'Foot':1.}
    if any(s in name for s in ('greave','shin','boot','calf')):
        knee=joints[side+'LowerLeg'][0].z
        ankle=joints[side+'Foot'][0].z
        t=smooth((p.z-(ankle-.005))/.07)
        return {side+'Foot':1-t,side+'LowerLeg':t}
    if 'kneeliner' in name:
        knee=joints[side+'LowerLeg'][0].z
        t=smooth((p.z-knee+.07)/.14)
        return {side+'LowerLeg':1-t,side+'UpperLeg':t}
    if 'knee' in name:return {side+'LowerLeg':1.}
    if 'tasset' in name or name.startswith('tunic |'):
        return {side+'UpperLeg':1.}
    if any(s in name for s in ('tasset','tunic','skirt')):
        hip=joints[side+'UpperLeg'][0].z
        t=smooth((hip+.12-p.z)/.23)
        return {'Hips':1-t,side+'UpperLeg':t}
    if any(s in name for s in ('belt','waist','hip','clasp')):return {'Hips':1.}
    if p.z<h*.54:
        knee=joints[side+'LowerLeg'][0].z
        t=smooth((p.z-knee+.06)/.12)
        return {side+'LowerLeg':1-t,side+'UpperLeg':t}
    return axial_weights(p,[h*.77,h*.62,h*.51],['Chest','Spine','Hips'])


def bind(obj,rig,joints,h):
    names={g.index:remap_group(g.name) for g in obj.vertex_groups}
    weights=[];retained=0
    for v in obj.data.vertices:
        w=defaultdict(float)
        for g in v.groups:
            target=names[g.group]
            # A face is rigid at this rig's scope. Rain includes additional lid
            # correction groups whose names are deliberately not game bones.
            if obj.name=='Head' and obj.vertex_groups[g.group].name.startswith('DEF-') and target not in ('Chest','Neck'):
                target='Head'
            if target in joints:w[target]+=g.weight
        # The original hair bones have been merged into a compact hanging chain.
        if obj.name.startswith(('HairLong','HairBraid')) or obj.name=='HairMain':w={}
        if obj.name=='Rain_scarf':
            collar=smooth((v.co.z-joints['Neck'][0].z+.025)/.10)
            w={'Chest':1-collar,'Neck':collar}
        if sum(w.values())>.001:retained+=1
        else:w=costume_weights(obj,v.co,joints,h)
        keep=sorted(((k,v) for k,v in w.items() if v>.00001),key=lambda x:x[1],reverse=True)[:4]
        total=sum(v for k,v in keep)
        weights.append([(k,v/total) for k,v in keep])
    obj.vertex_groups.clear()
    groups={n:obj.vertex_groups.new(name=n) for n in joints}
    for i,w in enumerate(weights):
        for name,value in w:groups[name].add([i],value,'REPLACE')
    world=obj.matrix_world.copy();obj.parent=rig;obj.matrix_world=world
    mod=obj.modifiers.new('Game skinning','ARMATURE');mod.object=rig
    return retained


def reduce_mesh(obj):
    if obj.get('preserveCostumeSurface',False):return
    count=triangles(obj)
    # Spend the portrait budget on eyelids, face planes and sculpted hair locks.
    ratio=.96 if obj.name=='Head' else .80 if obj.name=='Body' else .82 if obj.name=='HairMain' else .72 if obj.name=='Eyes' else .49
    if obj.name.startswith(('HairLong','HairBraid')):ratio=.56
    if obj.name=='Rain_scarf':ratio=.90
    elif any(word in obj.name.lower() for word in ('stitch','binding','belt')):ratio=.85
    if count<160:return
    select(obj)
    mod=obj.modifiers.new('Game mesh reduction','DECIMATE');mod.ratio=ratio;mod.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=mod.name)


def restore_scarf(race):
    """The static study's 250-face scarf cannot hold a skinned collar shape."""
    old=bpy.data.objects.get('Rain_scarf')
    if old is None:return None
    material=old.data.materials[0]
    target_low=Vector(tuple(min(v.co[k] for v in old.data.vertices) for k in range(3)))
    target_high=Vector(tuple(max(v.co[k] for v in old.data.vertices) for k in range(3)))
    path=ROOT.parents[1]/'blender'/'assets'/'rain-ancients-v3.blend'
    with bpy.data.libraries.load(str(path),link=False) as (available,loaded):
        loaded.objects=['Rain_scarf']
    scarf=loaded.objects[0];bpy.context.scene.collection.objects.link(scarf)
    # Match the delivered study's fitted proportions, including its costume scale.
    select(scarf);bpy.ops.object.convert(target='MESH');scarf=bpy.context.object
    matrix=scarf.matrix_world.copy();scarf.parent=None;scarf.matrix_world=Matrix.Identity(4)
    source_points=[matrix@v.co for v in scarf.data.vertices]
    source_low=Vector(tuple(min(p[k] for p in source_points) for k in range(3)))
    source_high=Vector(tuple(max(p[k] for p in source_points) for k in range(3)))
    for v in scarf.data.vertices:
        p=source_points[v.index]
        v.co=Vector(tuple(target_low[k]+(p[k]-source_low[k])*(target_high[k]-target_low[k])/(source_high[k]-source_low[k]) for k in range(3)))
    scarf.data.update()
    for v in scarf.data.vertices:v.co+=v.normal*.0025
    scarf.data.materials.clear();scarf.data.materials.append(material)
    for attr in list(scarf.data.color_attributes):scarf.data.color_attributes.remove(attr)
    paint=scarf.data.color_attributes.new(name='Painted',type='FLOAT_COLOR',domain='POINT')
    for c in paint.data:c.color=(.92,.92,.92,1)
    scarf.data.color_attributes.active_color=paint
    for poly in scarf.data.polygons:poly.material_index=0;poly.use_smooth=True
    bpy.data.objects.remove(old,do_unlink=True);scarf.name='Rain_scarf'
    return scarf


def restore_shoulders(meshes):
    """Keep the shoulder shell and its fitted trim as one coherent surface."""
    originals={o.name:o for o in meshes if 'shoulder' in o.name.lower() and 'rivet' not in o.name.lower()}
    path=ROOT.parents[1]/'blender'/'assets'/'rain-ancients-v3.blend'
    names=list(originals)
    with bpy.data.libraries.load(str(path),link=False) as (available,loaded):
        loaded.objects=list(names)
    restored={}
    for name,obj in zip(names,loaded.objects):
        assert obj is not None,name
        bpy.context.scene.collection.objects.link(obj)
        if obj.type=='CURVE':obj.data.resolution_u=4;obj.data.bevel_resolution=1
        for mod in obj.modifiers:
            # The authored 19 x 9 grid already holds the leather curvature.
            if mod.type=='SUBSURF':mod.levels=0;mod.render_levels=0
        select(obj);bpy.ops.object.convert(target='MESH');obj=bpy.context.object
        matrix=obj.matrix_world.copy();obj.parent=None;obj.matrix_world=Matrix.Identity(4)
        for v in obj.data.vertices:v.co=matrix@v.co
        restored[name]=obj
    # This 216-triangle ridge was never reduced in the static study. Its bounds
    # recover the exact shared fitting transform, so shell and trim stay aligned.
    reference='V3 Ranger | sculpted leaf shoulder cap | raised leaf midrib'
    def bounds(obj):
        return [Vector(tuple(fn(v.co[k] for v in obj.data.vertices) for k in range(3))) for fn in (min,max)]
    source_low,source_high=bounds(restored[reference]);target_low,target_high=bounds(originals[reference])
    scale=Vector(tuple((target_high[k]-target_low[k])/(source_high[k]-source_low[k]) for k in range(3)))
    offset=target_low-Vector(tuple(source_low[k]*scale[k] for k in range(3)))
    for name,obj in restored.items():
        old=originals[name];material=old.data.materials[0]
        for v in obj.data.vertices:v.co=Vector(tuple(v.co[k]*scale[k]+offset[k] for k in range(3)))
        if ' | ' in name[len('V3 Ranger | '):]:
            # Lift the intact tube, rather than flattening its back vertices into
            # the shell. The original trim lay inside the 5 mm solidified leather.
            for v in obj.data.vertices:v.co.z+=.0035
        obj.data.materials.clear();obj.data.materials.append(material)
        for attr in list(obj.data.color_attributes):obj.data.color_attributes.remove(attr)
        paint=obj.data.color_attributes.new(name='Painted',type='FLOAT_COLOR',domain='POINT')
        for v,c in zip(obj.data.vertices,paint.data):
            p=v.co;value=.90+.07*(.72*math.sin(p.x*20+p.z*7)*math.sin(p.y*15-p.z*10)+.28*math.sin(p.x*70+p.z*13)*math.sin(p.y*45+p.z*32))
            c.color=(value,value,value,1)
        obj.data.color_attributes.active_color=paint
        for poly in obj.data.polygons:poly.material_index=0;poly.use_smooth=True
        obj['preserveCostumeSurface']=True
        meshes.remove(old);bpy.data.objects.remove(old,do_unlink=True);obj.name=name
        meshes.append(obj)
    print('SHOULDER_SURFACES_RESTORED',len(restored),flush=True)


def fit_tasset_lining(meshes):
    """The dark split tunic must sit behind the ivory thigh plates."""
    for obj in meshes:
        if obj.name.startswith('Tunic |'):
            for v in obj.data.vertices:v.co.y+=.022


def separate_costume_details(meshes):
    """Lift stitched and bound edges off their simplified supporting surfaces."""
    details=lambda obj:any(word in obj.name.lower() for word in ('stitch','binding','embro','midrib','rivet'))
    support=[o for o in meshes if not details(o) and o.name not in ('Head','Body','Eyes','HairMain') and not o.name.startswith(('HairLong','HairBraid'))]
    vertices=[];faces=[]
    for obj in support:
        offset=len(vertices);vertices.extend(v.co.copy() for v in obj.data.vertices)
        faces.extend(tuple(offset+i for i in p.vertices) for p in obj.data.polygons)
    tree=BVHTree.FromPolygons(vertices,faces)
    for obj in meshes:
        if not details(obj) or obj.get('preserveCostumeSurface',False):continue
        for v in obj.data.vertices:
            point,normal,_,distance=tree.find_nearest(v.co,.015)
            if point is not None and (v.co-point).dot(normal)<.0025:
                v.co=point+normal*.0025


def finish_painted_surfaces(meshes):
    """Bake restrained contact shade and material strokes into existing paint.

    The original face paint stays intact. No texture, material slot, rig or
    runtime cost is added; optional hairstyles never occlude one another.
    """
    vertices=[];faces=[]
    for obj in meshes:
        if obj.name.startswith(('HairLong','HairBraid')):continue
        offset=len(vertices);vertices.extend(obj.matrix_world@v.co for v in obj.data.vertices)
        faces.extend(tuple(offset+i for i in poly.vertices) for poly in obj.data.polygons)
    tree=BVHTree.FromPolygons(vertices,faces,all_triangles=False)
    directions=[]
    for i in range(12):
        z=math.sqrt((i+.5)/12);r=math.sqrt(1-z*z);a=i*2.39996323
        directions.append(Vector((r*math.cos(a),r*math.sin(a),z)))
    for obj in meshes:
        mesh=obj.data;paint=mesh.color_attributes.get('Painted')
        if not paint:continue
        material=mesh.materials[0].name if mesh.materials else ''
        skin=material=='Skin';eye=obj.name=='Eyes'
        strength=.18 if skin else .06 if eye else .48
        neighbors=[[] for _ in mesh.vertices]
        for edge in mesh.edges:
            a,b=edge.vertices;neighbors[a].append(b);neighbors[b].append(a)
        bounds_min=Vector(tuple(min(v.co[k] for v in mesh.vertices) for k in range(3)))
        bounds_max=Vector(tuple(max(v.co[k] for v in mesh.vertices) for k in range(3)))
        samples=[]
        for v in mesh.vertices:
            p=obj.matrix_world@v.co;n=(obj.matrix_world.to_3x3()@v.normal).normalized()
            tangent=n.cross(Vector((0,0,1)))
            if tangent.length<.1:tangent=n.cross(Vector((1,0,0)))
            tangent.normalize();bitangent=n.cross(tangent)
            occlusion=0.
            for ray in directions:
                direction=tangent*ray.x+bitangent*ray.y+n*ray.z
                hit,_,_,distance=tree.ray_cast(p+n*.0015,direction,.07)
                if hit is not None:occlusion+=(1-distance/.07)/len(directions)
            # Painted plane separation and worn edges use the rest geometry.
            # This deliberately stays broader than screen-space texture noise.
            pigment=math.sin(p.x*19+p.z*10)*math.sin(p.y*23-p.z*7)
            stroke=math.sin(p.z*86+p.x*12+math.sin(p.x*19)*2)
            curvature=0.
            for index in neighbors[v.index]:
                delta=mesh.vertices[index].co-v.co
                if delta.length>1e-6:curvature-=delta.normalized().dot(v.normal)/len(neighbors[v.index])
            convex=clamp(curvature*4);cavity=clamp(-curvature*5)
            plane=clamp(.5+.5*n.dot(Vector((-.25,-.45,.86))))
            amount=.012 if skin or eye else .10
            value=1-strength*occlusion+amount*(.72*pigment+.28*stroke)
            warmth=.003 if skin or eye else .035*pigment
            if not skin and not eye:
                value*=.76+.19*plane+.17*convex-.12*cavity
                warmth+=.065*plane+.045*convex-.07*(1-plane)
            if obj.name=='Head':
                z=(v.co.z-bounds_min.z)/max(.001,bounds_max.z-bounds_min.z)
                x=abs(v.co.x)/max(.001,bounds_max.x)
                cheek=math.exp(-((x-.52)/.24)**2-((z-.45)/.16)**2)*clamp(-n.y)
                samples.append((value+.025*cheek,value-.065*cheek,value-.075*cheek))
            else:
                samples.append((value+warmth,value,value-warmth))
        for i,c in enumerate(paint.data):
            index=mesh.loops[i].vertex_index if paint.domain=='CORNER' else i
            old=c.color;c.color=tuple(max(.035,min(1.,old[k]*samples[index][k])) for k in range(3))+(1,)
    print('PAINTED_SURFACES_FINISHED',flush=True)


def add_knee_liners(rig,joints):
    """The static male study removed hidden trousers below its boot cuffs.

    Restore only that hidden articulation strip so bending does not open holes.
    """
    result=[]
    for side in ('Left','Right'):
        knee=joints[side+'LowerLeg'][0];hip=joints[side+'UpperLeg'][0];ankle=joints[side+'Foot'][0]
        vertices=[];faces=[]
        for ring in range(13):
            t=ring/12;z=knee.z+.10-.21*t
            center=knee.lerp(hip,clamp((z-knee.z)/(hip.z-knee.z))) if z>=knee.z else knee.lerp(ankle,clamp((knee.z-z)/(knee.z-ankle.z)))
            radius=.045+.007*math.sin(math.pi*t)
            for j in range(20):
                a=j*math.tau/20;vertices.append((center.x+radius*math.cos(a),center.y+radius*math.sin(a),z))
        for r in range(12):
            for j in range(20):
                a=r*20+j;b=r*20+(j+1)%20;faces.append((a,b,b+20,a+20))
        mesh=bpy.data.meshes.new(side+'KneeLiner');mesh.from_pydata(vertices,[],faces);mesh.update()
        obj=bpy.data.objects.new(side+'KneeLiner',mesh);bpy.context.scene.collection.objects.link(obj)
        mesh.materials.append(bpy.data.materials['DarkLeather'])
        paint=mesh.color_attributes.new(name='Painted',type='FLOAT_COLOR',domain='POINT')
        for c in paint.data:c.color=(.85,.85,.85,1)
        mesh.color_attributes.active_color=paint
        for p in mesh.polygons:p.use_smooth=True
        result.append(obj)
    return result


def merge(name,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    obj=bpy.context.object;obj.name=name;obj.data.name=name
    # Consolidate duplicated material slots without changing paint or topology.
    mats=[];mapping={}
    for i,m in enumerate(obj.data.materials):
        if m not in mats:mats.append(m)
        mapping[i]=mats.index(m)
    indices=[mapping[p.material_index] for p in obj.data.polygons]
    obj.data.materials.clear()
    for m in mats:obj.data.materials.append(m)
    for p,i in zip(obj.data.polygons,indices):p.material_index=i
    return obj


def add_hair(silhouette,race):
    # Append only the extra locks; the scalp matches the preserved short variant.
    result=[]
    for style,prefix in [('long','HairLong'),('braid','HairBraid')]:
        path=ROOT/'assets'/f'{race}-{silhouette}-{style}.blend'
        with bpy.data.libraries.load(str(path),link=False) as (available,loaded):
            loaded.objects=[n for n in available.objects if n.startswith(prefix)]
        for o in loaded.objects:
            bpy.context.scene.collection.objects.link(o)
            o.parent=None
            for i,m in enumerate(o.data.materials):
                # Appended Blender files duplicate material datablocks.
                base=m.name.split('.')[0]
                if bpy.data.materials.get(base):o.data.materials[i]=bpy.data.materials[base]
            result.append(o)
    return result


def pose_rotation(rig,name,axis,angle):
    b=rig.pose.bones[name]
    local=rig.data.bones[name].matrix_local.to_3x3().inverted()@Vector(axis)
    q=Quaternion(local,angle)
    b.rotation_quaternion=b.rotation_quaternion@q


def create_animations(rig):
    scene=bpy.context.scene;scene.render.fps=30
    clips=[]
    for name,frames in [('Idle',120),('Walk',36),('Run',24)]:
        rig.animation_data_create()
        action=bpy.data.actions.new(name);rig.animation_data.action=action
        for f in range(frames+1):
            t=f/frames;phase=t*math.tau
            for b in rig.pose.bones:
                b.rotation_mode='QUATERNION';b.rotation_quaternion=Quaternion();b.location=Vector();b.scale=Vector((1,1,1))
            if name=='Idle':
                pose_rotation(rig,'Chest',(1,0,0),.010*math.sin(phase))
                pose_rotation(rig,'Head',(0,0,1),.025*math.sin(phase))
                rig.pose.bones['Hips'].location.y=.003*math.sin(phase)
                for side,s in [('Left',1),('Right',-1)]:
                    pose_rotation(rig,side+'LowerArm',(1,0,0),-.035-.012*math.sin(phase+s))
            else:
                run=name=='Run';stride=.31 if run else .20
                bob=-.04+(.012 if run else .006)*(1-math.cos(phase*2))
                hips_offset=Vector((0,0,bob))
                rig.pose.bones['Hips'].location=rig.data.bones['Hips'].matrix_local.to_3x3().inverted()@hips_offset
                pose_rotation(rig,'Spine',(1,0,0),.08 if run else .025)
                pose_rotation(rig,'Chest',(0,0,1),.06*math.sin(phase))
                for side,s in [('Left',1),('Right',-1)]:
                    q=phase+(0 if s==1 else math.pi)
                    upper=rig.data.bones[side+'UpperLeg'];lower=rig.data.bones[side+'LowerLeg']
                    hip=upper.head_local+hips_offset;knee=lower.head_local;ankle=rig.data.bones[side+'Foot'].head_local
                    # Analytic sagittal two-bone IK: planted stance foot, lifted
                    # swing foot, forward-facing knees and level armored shoes.
                    target=ankle+Vector((0,-stride*math.cos(q),(.19 if run else .10)*max(0,-math.sin(q))))
                    dy=target.y-hip.y;dz=target.z-hip.z
                    l1=upper.length;l2=lower.length;dist=min(math.hypot(dy,dz),l1+l2-.0001)
                    alpha=math.atan2(dy,-dz)
                    a1=math.acos(max(-1,min(1,(l1*l1+dist*dist-l2*l2)/(2*l1*dist))))
                    a2=math.acos(max(-1,min(1,(l2*l2+dist*dist-l1*l1)/(2*l2*dist))))
                    rest1=math.atan2(upper.tail_local.y-upper.head_local.y,upper.head_local.z-upper.tail_local.z)
                    rest2=math.atan2(lower.tail_local.y-lower.head_local.y,lower.head_local.z-lower.tail_local.z)
                    delta1=alpha-a1-rest1;delta2=alpha+a2-rest2-delta1
                    pose_rotation(rig,side+'UpperLeg',(1,0,0),delta1)
                    pose_rotation(rig,side+'LowerLeg',(1,0,0),delta2)
                    pose_rotation(rig,side+'Foot',(1,0,0),-(delta1+delta2))
                    pose_rotation(rig,side+'UpperArm',(1,0,0),(.48 if run else .25)*math.cos(q))
                    pose_rotation(rig,side+'LowerArm',(1,0,0),-.85 if run else -.18-.12*max(0,math.cos(q)))
            pose_rotation(rig,'CapeUpper',(1,0,0),-.025*math.sin(phase-.5))
            pose_rotation(rig,'CapeLower',(1,0,0),-.04*math.sin(phase-1.0))
            pose_rotation(rig,'HairUpper',(1,0,0),.013*math.sin(phase-.4))
            pose_rotation(rig,'HairLower',(1,0,0),.025*math.sin(phase-.8))
            for b in rig.pose.bones:
                b.keyframe_insert(data_path='rotation_quaternion',frame=f+1,group=b.name)
                b.keyframe_insert(data_path='location',frame=f+1,group=b.name)
        action.use_fake_user=True
        clips.append({'name':name,'frames':frames+1,'durationSeconds':frames/30,'inPlace':True,'loop':True})
    rig.animation_data.action=bpy.data.actions['Idle']
    scene.frame_start=1;scene.frame_end=121;scene.frame_set(1)
    return clips


def neutral_materials():
    for m in bpy.data.materials:
        if not m.use_nodes:continue
        bs=m.node_tree.nodes.get('Principled BSDF')
        if not bs:continue
        base=(1,1,1,1) if m.name in ('Skin','Hair','Iris','OutfitAccent') else m.diffuse_color
        for link in list(bs.inputs['Base Color'].links):m.node_tree.links.remove(link)
        bs.inputs['Base Color'].default_value=base
        m.diffuse_color=base


def validation(rig,meshes,clips,slug):
    worst=0;unbound=0;max_influences=0;vertices=0
    for o in meshes:
        for v in o.data.vertices:
            weights=[g.weight for g in v.groups if g.weight>1e-6]
            vertices+=1;unbound+=not weights;max_influences=max(max_influences,len(weights))
            worst=max(worst,abs(sum(weights)-1))
        assert o.data.color_attributes.get('Painted'),o.name
    assert not unbound and worst<1e-5 and max_influences<=4,(unbound,worst,max_influences)
    scene=bpy.context.scene
    animation_probes=[]
    main=bpy.data.objects['CharacterMesh']
    for clip in clips:
        rig.animation_data.action=bpy.data.actions[clip['name']]
        scene.frame_set(1)
        ev=main.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        first=[v.co.copy() for v in mesh.vertices];ev.to_mesh_clear()
        scene.frame_set(1+(clip['frames']-1)//4)
        ev=main.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        movement=max((v.co-first[v.index]).length for v in mesh.vertices)
        ev.to_mesh_clear()
        assert movement>.002,(clip['name'],movement)
        animation_probes.append({'clip':clip['name'],'maxVertexDisplacementMeters':movement})
    rig.animation_data.action=bpy.data.actions['Idle'];scene.frame_set(1)
    return {'id':slug,'attribution':CREDIT,'bones':len(rig.data.bones),'vertices':vertices,
        'trianglesAllHairOptions':sum(triangles(o) for o in meshes),
        'trianglesShort':sum(triangles(o) for o in meshes if not o.name.startswith(('HairLong','HairBraid'))),
        'maxInfluences':max_influences,'unweightedVertices':unbound,'maxWeightSumError':worst,
        'clips':clips,'animationDeformationProbes':animation_probes,
        'meshes':[{'name':o.name,'triangles':triangles(o),'materials':[m.name for m in o.data.materials]} for o in meshes],
        'boneNames':[b.name for b in rig.data.bones]}


def build(race,silhouette):
    slug=f'{race}-{silhouette}'
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/'assets'/f'{slug}-short.blend'),use_scripts=False)
    bpy.context.preferences.filepaths.save_version=0
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    for o in meshes:o.parent=None
    for o in list(bpy.context.scene.objects):
        if o.type!='MESH':bpy.data.objects.remove(o,do_unlink=True)
    meshes+=add_hair(silhouette,race)
    if silhouette=='b':
        old=bpy.data.objects.get('Rain_scarf')
        if old in meshes:meshes.remove(old)
        meshes.append(restore_scarf(race))
        restore_shoulders(meshes)
    rig,joints=skeleton(silhouette,race)
    if silhouette=='a':meshes+=add_knee_liners(rig,joints)
    retained=0
    for o in meshes:
        reduce_mesh(o)
    if silhouette=='a':fit_tasset_lining(meshes)
    separate_costume_details(meshes)
    for o in meshes:
        retained+=bind(o,rig,joints,1.85 if race=='elf' else 1.75)
    finish_painted_surfaces(meshes)
    merged=[]
    batches={
        'HairMain':[o for o in meshes if o.name=='HairMain'],
        'HairLong':[o for o in meshes if o.name.startswith('HairLong')],
        'HairBraid':[o for o in meshes if o.name.startswith('HairBraid')],
        'CharacterMesh':[o for o in meshes if not o.name.startswith(('HairLong','HairBraid')) and o.name!='HairMain'],
    }
    for name,batch in batches.items():
        merged.append(merge(name,batch))
    # Decimation interpolates weights; joining keeps all groups. Trim after both
    # operations so every exported vertex obeys the GPU four-influence budget.
    for o in merged:
        select(o)
        bpy.ops.object.vertex_group_limit_total(limit=4)
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    clips=create_animations(rig)
    neutral_materials()
    material_colors={m.name:list(m.diffuse_color) for o in merged for m in o.data.materials}
    (OUT/f'{slug}.materials.json').write_text(json.dumps(material_colors,indent=2),encoding='utf-8')
    report=validation(rig,merged,clips,slug)
    report['verticesUsingRemappedSourceWeights']=retained
    bpy.context.scene['attribution']=CREDIT
    bpy.context.scene['asset_description']='Arkya painted art pass v2; game rig v1. Generic in-place locomotion. Read game/README.md for limits.'
    for o in merged:o.hide_render=False
    for _ in range(2):bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/f'{slug}.blend'),compress=True)
    bpy.ops.object.select_all(action='DESELECT');rig.select_set(True)
    for o in merged:o.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=str(OUT/f'{slug}.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',global_scale=1.,axis_forward='-Z',axis_up='Y',
        use_mesh_modifiers=True,use_mesh_edges=False,mesh_smooth_type='FACE',colors_type='LINEAR',prioritize_active_color=True,
        add_leaf_bones=False,use_armature_deform_only=True,armature_nodetype='NULL',
        bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True,
        bake_anim_step=1.,bake_anim_simplify_factor=0.,path_mode='AUTO',embed_textures=False)
    (OUT/f'{slug}.validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('GAME_CHARACTER_EXPORTED '+json.dumps(report),flush=True)


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--only',default='')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    OUT.mkdir(exist_ok=True)
    for race,silhouette in [('human','a'),('human','b'),('elf','a'),('elf','b')]:
        if not args.only or f'{race}-{silhouette}' in args.only.split(','):build(race,silhouette)
