"""Author the Brise-Seve from the approved concept, in Blender 4.5.

No external meshes are used. Run in an isolated background Blender process.
"""
import argparse
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils.noise import noise_vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
random.seed(26)
PARTS = []
COLORS = {
    'hide': ('29170f', '78462a'), 'bark': ('4c2c18', 'bb8844'),
    'moss': ('152b1c', '63822f'), 'ivory': ('927544', 'ead9a8'),
    'nose': ('261e1a', '513c2a'), 'dark': ('100f0e', '241b16'),
    'amber': ('a34308', 'fbb139'), 'light': ('e5d9af', 'fff4cb'),
}


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def linear(hex_color):
    rgb = [int(hex_color[i:i+2], 16)/255 for i in (0, 2, 4)]
    return [x/12.92 if x <= .04045 else ((x+.055)/1.055)**2.4 for x in rgb]


def paint(obj, kind, bone=None, region=None):
    if bone:
        obj['bind_bone'] = bone
    if region:
        obj['skin_region'] = region
    obj['surface'] = kind
    low, high = [linear(c) for c in COLORS[kind]]
    attr = obj.data.color_attributes.new(name='Paint', type='FLOAT_COLOR', domain='CORNER')
    for poly in obj.data.polygons:
        poly.use_smooth = True
        for li in poly.loop_indices:
            vertex = obj.data.vertices[obj.data.loops[li].vertex_index]
            pos = obj.matrix_world @ vertex.co
            n = vertex.normal
            pigment = noise_vector(Vector((pos.x*10, pos.y*7, pos.z*19)))[0]
            broad = noise_vector(pos*3.8)[1]
            factor = .49 + .25*n.z + .14*broad + .095*pigment
            if kind == 'bark':
                factor += .13*math.sin(pos.z*42 + pos.y*22 + 1.2*math.sin(pos.x*15))
            if kind == 'moss':
                factor += .065*math.sin(pos.y*30 + pos.z*18)
            if obj.get('swept') and kind in ('bark','moss','ivory'):
                vi=obj.data.loops[li].vertex_index
                t=(vi//obj['sides'])/(obj['rings']-1)
                u=(vi%obj['sides'])/obj['sides']
                grain=math.sin(u*math.tau*(5 if kind=='bark' else 3)+.55*math.sin(t*11))
                groove=max(0,grain)**8
                factor -= (.28 if kind=='bark' else .14)*groove
                factor += .13*math.sin(math.pi*t)
            factor = min(.94, max(.10, factor))
            color = [a+(b-a)*factor for a,b in zip(low,high)]
            if kind == 'hide':
                # A broad cream bib and underside, not baked scene lighting.
                bib = math.exp(-((pos.x/.36)**4 + ((pos.y+.80)/.41)**4 + ((pos.z-.70)/.38)**4))
                belly = .45*math.exp(-((pos.x/.35)**4 + ((pos.y-.08)/.76)**4 + ((pos.z-.53)/.24)**4))
                tint = min(.82, max(bib*.84, belly))
                cream = linear('c7a776')
                color = [c*(1-tint)+d*tint for c,d in zip(color,cream)]
            attr.data[li].color = (*color, 1)
    PARTS.append(obj)
    return obj


def ellipsoid(name, location, scale, kind=None, bone=None, segments=28, rings=18):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location)
    obj=bpy.context.object
    obj.name=name
    obj.scale=scale
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if kind:
        paint(obj,kind,bone)
    return obj


def capsule(name, start, end, width, depth=None):
    a,b=Vector(start),Vector(end)
    midpoint=(a+b)*.5
    bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=midpoint)
    obj=bpy.context.object
    obj.name=name
    obj.rotation_euler=(b-a).to_track_quat('Z','Y').to_euler()
    obj.scale=(width,depth or width,(b-a).length*.5+width*.52)
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    return obj


def catmull(points, t):
    position=t*(len(points)-1)
    i=min(int(position),len(points)-2)
    u=position-i
    a=points[max(i-1,0)]; b=points[i]; c=points[i+1]; d=points[min(i+2,len(points)-1)]
    return .5*((2*b)+(-a+c)*u+(2*a-5*b+4*c-d)*u*u+(-a+3*b-3*c+d)*u*u*u)


def sweep(name, points, radii, kind, bone, normal=(0,0,1), depth=.5, rings=19, sides=12):
    """Closed tapered sculpted lock, plate or tusk; no intersecting ribbon cards."""
    points=[Vector(p) for p in points]
    verts=[]; faces=[]
    for j in range(rings):
        t=j/(rings-1)
        center=catmull(points,t)
        tangent=(catmull(points,min(1,t+.002))-catmull(points,max(0,t-.002))).normalized()
        up=Vector(normal)
        if abs(tangent.dot(up))>.96: up=Vector((0,1,0))
        across=tangent.cross(up).normalized()
        up=across.cross(tangent).normalized()
        index=min(int(t*(len(radii)-1)),len(radii)-2)
        f=t*(len(radii)-1)-index
        radius=radii[index]*(1-f)+radii[index+1]*f
        radius=max(.0015,radius)
        for k in range(sides):
            angle=2*math.pi*k/sides
            ridge=1+.17*math.cos(3*angle) if kind in ('bark','moss') else 1
            # Wide intentional fluting gives the painted plates sculpted structure.
            if kind == 'bark': ridge += .09*math.cos(7*angle+t*4)
            v=center+across*(radius*math.cos(angle))+up*(radius*depth*math.sin(angle)*ridge)
            verts.append(tuple(v))
    for j in range(rings-1):
        for k in range(sides):
            a=j*sides+k;b=j*sides+(k+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.append(tuple(reversed(range(sides))))
    faces.append(tuple((rings-1)*sides+k for k in range(sides)))
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    obj['swept']=True;obj['sides']=sides;obj['rings']=rings
    select([obj])
    bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    return paint(obj,kind,bone)


def anatomy():
    flesh=[]
    for name,loc,scale in [
        ('Barrel',(0,.17,1.01),(.50,.83,.50)),
        ('Thorax',(0,-.39,1.07),(.57,.56,.57)),
        ('ShoulderRidge',(0,-.28,1.33),(.43,.47,.37)),
        ('Hips',(0,.72,1.00),(.44,.42,.43)),
        ('Neck',(0,-.73,1.13),(.46,.42,.43)),
        ('Skull',(0,-1.04,1.15),(.385,.35,.325)),
        ('Muzzle',(0,-1.30,.99),(.29,.33,.205)),
        ('Jaw',(0,-1.15,.83),(.31,.31,.15)),
    ]:
        flesh.append(ellipsoid(name,loc,scale))
    for side,s in [('L',1),('R',-1)]:
        flesh.append(ellipsoid('OrbitalRidge.'+side,(s*.295,-1.235,1.28),(.14,.14,.12)))
        flesh.append(ellipsoid('Shoulder.'+side,(s*.43,-.42,1.02),(.28,.31,.38)))
        flesh.append(capsule('ForeUpper.'+side,(s*.44,-.42,.98),(s*.49,-.50,.56),.205,.225))
        flesh.append(capsule('ForeLower.'+side,(s*.49,-.50,.58),(s*.48,-.72,.19),.145,.16))
        flesh.append(ellipsoid('ForeFoot.'+side,(s*.48,-.78,.145),(.205,.28,.135)))
        flesh.append(ellipsoid('Haunch.'+side,(s*.36,.69,.91),(.27,.33,.34)))
        flesh.append(capsule('HindUpper.'+side,(s*.40,.67,.98),(s*.44,.50,.69),.205,.22))
        flesh.append(capsule('HindLower.'+side,(s*.44,.50,.70),(s*.43,1.00,.41),.14,.15))
        flesh.append(capsule('HindMetatarsal.'+side,(s*.43,1.00,.42),(s*.43,.82,.17),.105,.12))
        flesh.append(ellipsoid('HindFoot.'+side,(s*.43,.74,.145),(.18,.25,.135)))
    select(flesh);bpy.ops.object.join();body=bpy.context.object;body.name='LivingBody'
    rem=body.modifiers.new('Continuous anatomy','REMESH');rem.mode='VOXEL';rem.voxel_size=.027;rem.use_smooth_shade=True
    bpy.ops.object.modifier_apply(modifier=rem.name)
    smooth=body.modifiers.new('Sculpt transitions','SMOOTH');smooth.factor=.90;smooth.iterations=7
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    sub=body.modifiers.new('Sculpt polish','SUBSURF');sub.levels=1
    bpy.ops.object.modifier_apply(modifier=sub.name)
    dec=body.modifiers.new('Surface economy','DECIMATE');dec.ratio=.46
    bpy.ops.object.modifier_apply(modifier=dec.name)
    paint(body,'hide',region='body')
    return body


def details():
    # Broad forehead plates descend into the muzzle, leaving the eye sockets readable.
    sweep('ForeheadShield',[(0,-.78,1.57),(0,-1.01,1.53),(0,-1.30,1.36),(0,-1.57,1.10)],[.025,.20,.18,.06],'bark','Head',normal=(0,-.6,.8),depth=.37,sides=16)
    sweep('CrownPlate',[(0,-.67,1.54),(0,-.78,1.68),(0,-.87,1.81)],[.14,.10,.002],'bark','Head',normal=(0,-1,.5),depth=.40)
    for side,s in [('L',1),('R',-1)]:
        sweep('Brow.'+side,[(s*.12,-.91,1.56),(s*.29,-1.09,1.50),(s*.38,-1.27,1.40),(s*.27,-1.44,1.27)],[.02,.13,.095,.005],'bark','Head',normal=(s*.6,-.6,.6),depth=.35)
        sweep('CheekShield.'+side,[(s*.37,-.98,1.27),(s*.47,-1.15,1.12),(s*.38,-1.39,.97)],[.03,.13,.015],'bark','Head',normal=(s*.8,-.4,.1),depth=.38)
        sweep('SnoutShield.'+side,[(s*.12,-1.26,1.36),(s*.23,-1.44,1.20),(s*.22,-1.59,1.05)],[.095,.09,.009],'bark','Head',normal=(s*.5,-.5,.5),depth=.30)
        sweep('TemplePlate.'+side,[(s*.21,-.79,1.48),(s*.38,-.91,1.49),(s*.51,-.97,1.54)],[.10,.14,.003],'bark','Head',normal=(s*.8,-.2,.6),depth=.28)
        # Tusk sweep contains the curve in geometry, not painted onto a flat card.
        sweep('RootTusk.'+side,[(s*.29,-1.23,.87),(s*.48,-1.40,.85),(s*.63,-1.48,1.01),(s*.64,-1.47,1.25),(s*.58,-1.46,1.48)],[.13,.125,.10,.065,.0015],'ivory','Head',normal=(0,-1,0),depth=.88,rings=30,sides=16)
        ellipsoid('ResinRoot.'+side,(s*.32,-1.29,.91),(.065,.075,.09),'amber','Head',20,12)
        # Ears are tapered solid volumes, not sheets.
        sweep('Ear.'+side,[(s*.32,-.78,1.39),(s*.45,-.65,1.57),(s*.45,-.63,1.73)],[.09,.095,.003],'hide','Head',normal=(0,-1,.2),depth=.36)
        sweep('EarInset.'+side,[(s*.34,-.813,1.44),(s*.43,-.70,1.57),(s*.445,-.66,1.66)],[.035,.055,.002],'nose','Head',normal=(0,-1,.2),depth=.20)
        # Eyes sit under the brow, with an inset dark rim and a forward-looking iris.
        eye=Vector((s*.365,-1.327,1.295));out=Vector((s*.64,-.75,.10)).normalized()
        def eye_layer(name,offset,scale,kind):
            obj=ellipsoid(name,eye+out*offset,scale,None,segments=24,rings=16)
            # All ellipsoids were applied in world coords; rotate only their surface around center.
            center=eye+out*offset;q=out.to_track_quat('Z','Y')
            for v in obj.data.vertices: v.co=center+q@(v.co-center)
            paint(obj,kind,'Head')
        eye_layer('EyeSocket.'+side,0,(.086,.064,.030),'dark')
        eye_layer('EyeGold.'+side,.023,(.059,.047,.021),'amber')
        eye_layer('Pupil.'+side,.043,(.024,.034,.010),'dark')
        ellipsoid('EyeGlint.'+side,eye+out*.054+Vector((-.009,-.008,.015)),(.008,.008,.008),'light','Head',12,8)
        # Three strongly separated toes and blunt claws per paw.
        for fore,y,x,bone in [(True,-.92,.48,'ForePaw.'+side),(False,.59,.43,'HindToe.'+side)]:
            for k in [-1,0,1]:
                xx=s*x+k*.105
                sweep(('ForeClaw' if fore else 'HindClaw')+side+str(k),[(xx,y+.035,.16),(xx,y-.065,.12),(xx,y-.145,.045)],[.074,.073,.008],'ivory',bone,normal=(0,0,1),depth=.72,rings=12,sides=10)
            # Small upward bark cuff with exposed joints for deformation.
            sweep(('ForeCuff' if fore else 'HindCuff')+side,[(s*x,y+.16,.18),(s*x,y+.18,.31),(s*x,y+.20,.42)],[.14,.16,.012],'bark',bone,normal=(0,-1,.3),depth=.26)
    ellipsoid('Nose',(0,-1.639,.999),(.24,.065,.125),'nose','Head')
    for s in [-1,1]:
        ellipsoid('Nostril',(s*.11,-1.695,1.021),(.050,.012,.034),'dark','Head',20,12)
    sweep('LowerLip',[(-.21,-1.45,.859),(0,-1.51,.834),(.21,-1.45,.859)],[.017,.023,.017],'nose','Jaw',normal=(0,-1,0),depth=.60,sides=8)
    # Back armor: three staggered plates per side, visually subordinated to the head.
    for i in range(4):
        y=-.40+i*.33;z=1.69-i*.065
        for s in [-1,1]:
            sweep('BackPlate',[(s*.08,y-.17,z),(s*.32,y+.02,z-.04),(s*.53,y+.33,z-.20)],[.03,.24,.003],'bark','Chest' if i<2 else 'Spine',normal=(s*.35,0,1),depth=.22)
    # Sculpted moss mane: broad interlocking locks wrapping the neck and shoulders.
    for s in [-1,1]:
        for i in range(4):
            y=-.69+i*.19;offset=.04*math.sin(i*2.2+s)
            sweep('ManeLock',[(s*.08,y,1.70),(s*.30,y+.09,1.79+offset),(s*.55,y+.24,1.54),(s*.62,y+.46,1.22+offset)],[.02,.17,.16,.0015],'moss','Chest',normal=(s*.5,-.15,.8),depth=.40)
        for i in range(3):
            y=-.68+i*.18
            sweep('CollarLock',[(s*.41,y,1.47),(s*.62,y+.10,1.30),(s*.66,y+.34,1.03+i*.025)],[.01,.15,.002],'moss','Chest',normal=(s,0,.1),depth=.34)
    for i in range(4):
        y=-.42+i*.25
        sweep('CrestLock',[(0,y-.12,1.69-i*.03),(0,y+.08,1.88-i*.07),(0,y+.48,1.77-i*.08)],[.02,.16,.0015],'moss','Chest' if i<2 else 'Spine',normal=(0,0,1),depth=.50)
    sweep('TailCore',[(0,1.03,1.10),(0,1.26,1.08),(0,1.46,.94),(0,1.49,.87)],[.12,.13,.10,.004],'hide','Tail01',normal=(0,0,1),depth=1)
    for s in [-1,0,1]:
        sweep('TailTuft',[(s*.04,1.22,1.14),(s*.09,1.42,1.11),(s*.08,1.57,.94)],[.035,.10,.002],'moss','Tail02',normal=(0,0,1),depth=.55)


def working_material():
    mat=bpy.data.materials.new('Painted working material');mat.use_nodes=True
    nodes=mat.node_tree.nodes;links=mat.node_tree.links
    bsdf=nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.72
    attr=nodes.new('ShaderNodeVertexColor');attr.layer_name='Paint'
    links.new(attr.outputs['Color'],bsdf.inputs['Base Color'])
    return mat


def atlas(mesh, size):
    select([mesh]);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.05,island_margin=.012,area_weight=.30)
    bpy.ops.object.mode_set(mode='OBJECT')
    mat=mesh.data.materials[0];nodes=mat.node_tree.nodes;links=mat.node_tree.links
    image=bpy.data.images.new('BriseSeve_BaseColor',width=size,height=size,alpha=False)
    image.colorspace_settings.name='sRGB'
    target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
    attr=next(n for n in nodes if n.type=='VERTEX_COLOR')
    output=nodes.get('Material Output');em=nodes.new('ShaderNodeEmission')
    links.new(attr.outputs['Color'],em.inputs['Color']);links.new(em.outputs[0],output.inputs['Surface'])
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.margin=12
    bpy.ops.object.bake(type='EMIT')
    path=ROOT/'textures'/'brise-seve-basecolor.png';image.filepath_raw=str(path);image.file_format='PNG';image.save();image.pack()
    nodes.remove(em);nodes.remove(attr)
    bsdf=nodes.get('Principled BSDF');links.new(target.outputs['Color'],bsdf.inputs['Base Color']);links.new(bsdf.outputs[0],output.inputs['Surface'])
    mat.name='BriseSeve_Painted'
    return image


def stage():
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.resolution_x=1440;scene.render.resolution_y=1080;scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('Review ambient')
    scene.world.use_nodes=True
    scene.world.node_tree.nodes.get('Background').inputs['Color'].default_value=(.22,.27,.25,1)
    scene.world.node_tree.nodes.get('Background').inputs['Strength'].default_value=.55
    scene.view_settings.view_transform='AgX'
    groundmat=bpy.data.materials.new('Review ground');groundmat.diffuse_color=(.17,.205,.18,1);groundmat.use_nodes=True
    groundmat.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.17,.205,.18,1)
    groundmat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.85
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.015));ground=bpy.context.object;ground.name='ReviewGround';ground.data.materials.append(groundmat)
    for name,loc,power,size,col in [('Key',(-3,-4,6),780,5,(1,.88,.70)),('Fill',(4,-1,3),420,4,(.66,.81,1)),('Rim',(1,4,5),1000,3,(1,.88,.68))]:
        data=bpy.data.lights.new(name,'AREA');data.energy=power;data.shape='DISK';data.size=size;data.color=col
        obj=bpy.data.objects.new(name,data);bpy.context.collection.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector((0,0,.8))-obj.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.camera_add(location=(3.6,-5.3,2.65));camera=bpy.context.object;camera.name='ReviewCamera';camera.data.type='ORTHO';camera.data.ortho_scale=3.95
    camera.rotation_euler=(Vector((0,-.07,.85))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
    return camera


def render(name, camera, location=None):
    if location:
        camera.location=location;camera.rotation_euler=(Vector((0,-.05,.85))-camera.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.scene.render.filepath=str(ROOT/'renders'/name)
    bpy.ops.render.render(write_still=True)


def build(args):
    for folder in ('assets','models','textures','renders','reference'):(ROOT/folder).mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system='METRIC';bpy.context.scene.render.fps=30
    anatomy();details()
    material=working_material()
    for part in PARTS: part.data.materials.append(material)
    rig=None;report={}
    if not args.lookdev:
        from rig_animation import build_rig
        result=build_rig(PARTS)
        if isinstance(result,tuple):rig,report=result
        else:rig=result
    select(PARTS);bpy.ops.object.join();mesh=bpy.context.object;mesh.name='BriseSeve_LOD0'
    # Joining keeps semantic vertex groups and the painted corner colors.
    if not args.lookdev: atlas(mesh,2048)
    camera=stage()
    if args.lookdev:
        bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'assets'/'brise-seve-lookdev.blend'))
        render('lookdev.png',camera)
        return
    # Select one active action for the saved scene, keeping all actions available for exports.
    if rig and rig.animation_data:
        rig.animation_data.action=bpy.data.actions.get('Idle')
    bpy.context.scene.frame_set(1)
    mesh.data.calc_loop_triangles()
    points=[mesh.matrix_world@Vector(c) for c in mesh.bound_box]
    dims=[max(p[i] for p in points)-min(p[i] for p in points) for i in range(3)]
    stats={'vertices':len(mesh.data.vertices),'triangles':len(mesh.data.loop_triangles),'materials':len(mesh.data.materials),'dimensionsM':dims}
    print('BRISE_SEVE_STATS '+json.dumps(stats))
    (ROOT/'geometry-report.json').write_text(json.dumps(stats,indent=2),encoding='utf-8')
    if report:(ROOT/'animation-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    # The GLB is the independent interactive review asset.
    select([mesh,rig])
    bpy.ops.export_scene.gltf(filepath=str(ROOT/'models'/'brise-seve.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_skins=True,export_morph=False,export_def_bones=False,export_force_sampling=True)
    lods=[mesh]
    rig.data.pose_position='REST'
    for i,ratio in [(1,.48),(2,.20)]:
        lod=mesh.copy();lod.data=mesh.data.copy();bpy.context.collection.objects.link(lod);lod.name='BriseSeve_LOD'+str(i)
        select([lod]);dec=lod.modifiers.new('Preserve silhouette LOD','DECIMATE');dec.ratio=ratio
        bpy.ops.object.modifier_apply(modifier=dec.name)
        bpy.ops.object.vertex_group_limit_total(limit=4)
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
        lods.append(lod)
    rig.data.pose_position='POSE'
    select(lods+[rig])
    bpy.ops.export_scene.fbx(filepath=str(ROOT/'assets'/'brise-seve.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=True,path_mode='COPY',embed_textures=False)
    for lod in lods[1:]:lod.hide_render=True;lod.hide_set(True)
    select([mesh,rig]);bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'assets'/'brise-seve.blend'))
    render('hero.png',camera)
    render('profile.png',camera,(5,0,1.95))
    render('rear.png',camera,(3.4,4.5,2.6))
    render('front.png',camera,(0,-5,2.0))


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--lookdev',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    build(args)
