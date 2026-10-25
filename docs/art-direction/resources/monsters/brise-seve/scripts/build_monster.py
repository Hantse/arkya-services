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
    'hide': ('392114', '98582e'), 'bark': ('633719', 'cc9345'),
    'moss': ('182710', '738a29'), 'ivory': ('ad8c53', 'f4dfaf'),
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
            factor = .62 + .16*n.z + .16*broad + .055*pigment
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
                bib = math.exp(-((pos.x/.43)**4 + ((pos.y+.85)/.42)**4 + ((pos.z-.73)/.42)**4))
                belly = .45*math.exp(-((pos.x/.35)**4 + ((pos.y-.08)/.76)**4 + ((pos.z-.53)/.24)**4))
                tint = min(.94, max(bib*.96, belly))
                cream = linear('dfbc85')
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


def blade(name, points, widths, kind, bone, normal=(0,0,1), thickness=.045, rings=21):
    """Closed broad plate with a shaped ridge, chipped edges and tapered silhouette."""
    path=[Vector(p) for p in points]; verts=[]; faces=[]; columns=9
    for surface in (1,-1):
        for j in range(rings):
            t=j/(rings-1);center=catmull(path,t)
            tangent=(catmull(path,min(1,t+.002))-catmull(path,max(0,t-.002))).normalized()
            up=Vector(normal).normalized()
            across=tangent.cross(up).normalized();up=across.cross(tangent).normalized()
            n=min(int(t*(len(widths)-1)),len(widths)-2);f=t*(len(widths)-1)-n
            width=max(.002,widths[n]*(1-f)+widths[n+1]*f)
            taper=min(1,width/(max(widths) if kind=='moss' else .055))
            for k in range(columns):
                u=2*k/(columns-1)-1
                irregular=1+.025*math.sin(j*1.85+k*.4)+.012*math.sin(j*4.5)
                ridge=(.17+.83*(1-abs(u)))*thickness*taper
                if surface<0: ridge=-thickness*.23*taper
                else: ridge+=.0015*taper*math.cos(u*17+math.sin(t*8))
                verts.append(tuple(center+across*(u*width*irregular)+up*ridge))
    stride=rings*columns
    for surf in (0,1):
        for j in range(rings-1):
            for k in range(columns-1):
                a=surf*stride+j*columns+k
                face=(a,a+1,a+columns+1,a+columns)
                faces.append(face if surf==0 else tuple(reversed(face)))
    perimeter=list(range(columns))+[j*columns+columns-1 for j in range(1,rings)]+list(range(stride-2,stride-columns-1,-1))+[j*columns for j in range(rings-2,0,-1)]
    for a,b in zip(perimeter,perimeter[1:]+perimeter[:1]):faces.append((a,b,b+stride,a+stride))
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(obj)
    select([obj]);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False);bpy.ops.object.mode_set(mode='OBJECT')
    return paint(obj,kind,bone)


def anatomy():
    flesh=[]
    for name,loc,scale in [
        ('Barrel',(0,.17,1.01),(.50,.83,.50)),
        ('Thorax',(0,-.39,1.07),(.62,.57,.60)),
        ('ShoulderRidge',(0,-.28,1.33),(.43,.47,.37)),
        ('Hips',(0,.72,1.00),(.44,.42,.43)),
        ('Neck',(0,-.73,1.13),(.48,.43,.43)),
        ('Skull',(0,-1.04,1.15),(.385,.35,.325)),
        ('SkullCrest',(0,-.89,1.40),(.32,.28,.26)),
        ('Muzzle',(0,-1.30,.99),(.29,.33,.205)),
        ('Jaw',(0,-1.15,.83),(.31,.31,.15)),
    ]:
        flesh.append(ellipsoid(name,loc,scale))
    for side,s in [('L',1),('R',-1)]:
        flesh.append(ellipsoid('OrbitalRidge.'+side,(s*.32,-1.24,1.28),(.16,.15,.12)))
        flesh.append(ellipsoid('Shoulder.'+side,(s*.44,-.42,1.00),(.30,.34,.40)))
        flesh.append(capsule('ForeUpper.'+side,(s*.44,-.42,.98),(s*.49,-.50,.56),.205,.225))
        flesh.append(capsule('ForeLower.'+side,(s*.49,-.50,.58),(s*.48,-.72,.19),.185,.20))
        flesh.append(ellipsoid('ForeFoot.'+side,(s*.48,-.80,.15),(.245,.30,.14)))
        flesh.append(ellipsoid('Haunch.'+side,(s*.36,.69,.91),(.27,.33,.34)))
        flesh.append(capsule('HindUpper.'+side,(s*.40,.67,.98),(s*.44,.50,.69),.205,.22))
        flesh.append(capsule('HindLower.'+side,(s*.44,.50,.70),(s*.43,1.00,.41),.165,.18))
        flesh.append(capsule('HindMetatarsal.'+side,(s*.43,1.00,.42),(s*.43,.82,.17),.13,.14))
        flesh.append(ellipsoid('HindFoot.'+side,(s*.43,.74,.15),(.21,.27,.14)))
    select(flesh);bpy.ops.object.join();body=bpy.context.object;body.name='LivingBody'
    rem=body.modifiers.new('Continuous anatomy','REMESH');rem.mode='VOXEL';rem.voxel_size=.027;rem.use_smooth_shade=True
    bpy.ops.object.modifier_apply(modifier=rem.name)
    smooth=body.modifiers.new('Sculpt transitions','SMOOTH');smooth.factor=.90;smooth.iterations=7
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    sub=body.modifiers.new('Sculpt polish','SUBSURF');sub.levels=1
    bpy.ops.object.modifier_apply(modifier=sub.name)
    dec=body.modifiers.new('Surface economy','DECIMATE');dec.ratio=.46
    bpy.ops.object.modifier_apply(modifier=dec.name)
    for vertex in body.data.vertices:
        vertex.co.z=max(.02,vertex.co.z)
    paint(body,'hide',region='body')
    return body


def details():
    # Fitted shield volumes retain a continuous face plane and an angry brow.
    blade('NasalShield',[(0,-.70,2.15),(0,-.80,1.76),(0,-.95,1.63),(0,-1.23,1.42),(0,-1.57,1.13)],
          [.002,.17,.23,.20,.12],'bark','Head',normal=(0,-.65,.76),thickness=.070,rings=31)
    for side,s in [('L',1),('R',-1)]:
        blade('Brow.'+side,[(s*.13,-1.37,1.34),(s*.34,-1.39,1.405),(s*.50,-1.18,1.49)],
              [.060,.090,.002],'bark','Head',normal=(s*.3,-.8,.5),thickness=.05)
        blade('CheekShield.'+side,[(s*.34,-1.40,1.21),(s*.47,-1.26,1.25),(s*.56,-1.05,1.22)],
              [.070,.090,.002],'bark','Head',normal=(s*.8,-.5,.25),thickness=.045)
        blade('SnoutShield.'+side,[(s*.12,-1.27,1.39),(s*.245,-1.45,1.22),(s*.235,-1.61,1.10)],
              [.08,.09,.022],'bark','Head',normal=(s*.5,-.6,.6),thickness=.035)
        blade('TemplePlate.'+side,[(s*.18,-1.04,1.51),(s*.30,-.86,1.65),(s*.36,-.63,1.86)],
              [.070,.095,.002],'bark','Head',normal=(s*.65,-.4,.7),thickness=.045)
        sweep('RootTusk.'+side,[(s*.30,-1.22,.93),(s*.48,-1.44,.89),(s*.68,-1.50,1.05),(s*.69,-1.50,1.30),(s*.63,-1.50,1.58)],
              [.15,.145,.10,.06,.0015],'ivory','Head',normal=(0,-1,0),depth=.90,rings=34,sides=16)
        ellipsoid('ResinRoot.'+side,(s*.38,-1.37,.98),(.078,.057,.10),'amber','Head',20,12)
        blade('Ear.'+side,[(s*.32,-.73,1.47),(s*.49,-.62,1.65),(s*.57,-.48,1.85)],
              [.11,.13,.002],'hide','Head',normal=(0,-1,.2),thickness=.05)
        eye=Vector((s*.399,-1.345,1.302));out=Vector((s*.64,-.75,.10)).normalized()
        def eye_layer(name,offset,scale,kind):
            center=eye+out*offset
            obj=ellipsoid(name,center,scale,None,segments=24,rings=16)
            q=out.to_track_quat('Z','Y')
            for v in obj.data.vertices:v.co=center+q@(v.co-center)
            paint(obj,kind,'Head')
        eye_layer('EyeSocket.'+side,0,(.070,.047,.014),'dark')
        eye_layer('EyeGold.'+side,.010,(.046,.033,.009),'amber')
        eye_layer('Pupil.'+side,.021,(.014,.025,.006),'dark')
        ellipsoid('EyeGlint.'+side,eye+out*.027+Vector((-.006,-.006,.009)),(.005,.005,.005),'light','Head',12,8)
        for fore,y,x,bone in [(True,-.99,.48,'ForePaw.'+side),(False,.55,.43,'HindToe.'+side)]:
            for k in [-1,0,1]:
                xx=s*x+k*.125
                sweep(('ForeClaw' if fore else 'HindClaw')+side+str(k),
                      [(xx,y+.05,.15),(xx,y-.055,.10),(xx,y-.125,.04)],
                      [.078,.076,.011],'ivory',bone,normal=(0,0,1),depth=.64,rings=12,sides=10)
            for k in [-2,-1,0,1,2]:
                xx=s*x+k*.083
                blade(('ForeCuff' if fore else 'HindCuff')+side+str(k),
                      [(xx,y+.04,.22),(xx,y+.18,.33),(xx,y+.32,.46)],
                      [.035,.095,.002],'moss',bone,normal=(0,-1,.5),thickness=.025)
    nose=ellipsoid('Nose',(0,-1.66,1.04),(.232,.115,.125),segments=40,rings=24)
    for s in [-1,1]:
        cutter=ellipsoid('NostrilCarver',(s*.105,-1.752,1.06),(.054,.078,.039),segments=24,rings=16)
        select([nose]);boolean=nose.modifiers.new('Recessed nostril','BOOLEAN');boolean.operation='DIFFERENCE';boolean.object=cutter
        bpy.ops.object.modifier_apply(modifier=boolean.name);bpy.data.objects.remove(cutter,do_unlink=True)
        ellipsoid('NostrilDepth',(s*.105,-1.693,1.059),(.045,.015,.030),'dark','Head',20,12)
    paint(nose,'nose','Head')
    sweep('LowerLip',[(-.21,-1.45,.859),(0,-1.51,.834),(.21,-1.45,.859)],
          [.017,.023,.017],'nose','Jaw',normal=(0,-1,0),depth=.60,sides=8)
    # A domed collar with backward-swept, curved locks and exposed shoulder plates.
    ellipsoid('ManeUndercoat',(0,-.36,1.58),(.53,.56,.30),'moss','Chest')
    for s in [-1,1]:
        for i in range(5):
            y=-.68+i*.24;z=1.76-i*.075;bone='Chest' if i<3 else 'Spine'
            blade('UpperMane',[(s*.05,y,z-.05),(s*.25,y+.12,z+.18),(s*.47,y+.35,z+.08),(s*.57,y+.76,z-.06)],
                  [.025,.19,.16,.002],'moss',bone,normal=(s*.4,0,1),thickness=.13)
            blade('MiddleMane',[(s*.28,y-.03,z-.03),(s*.51,y+.10,z-.02),(s*.62,y+.26,z-.21),(s*.68,y+.44,z-.49)],
                  [.025,.18,.14,.002],'moss',bone,normal=(s*.75,0,.6),thickness=.105)
            if i<4:
                blade('LowerMane',[(s*.48,y-.01,z-.24),(s*.64,y+.13,z-.35),(s*.70,y+.31,z-.49),(s*.68,y+.42,z-.68)],
                      [.025,.13,.10,.002],'moss',bone,normal=(s,0,.3),thickness=.075)
        for i in range(3):
            y=-.51+i*.37;z=1.80-i*.10
            blade('ShoulderBark',[(s*.25,y-.13,z),(s*.53,y+.07,z-.07),(s*.70,y+.24,z-.31),(s*.74,y+.41,z-.61)],
                  [.018,.19,.15,.002],'bark','Chest' if i<2 else 'Spine',normal=(s*.7,0,.7),thickness=.07)
        for i in range(3):
            y=-.85+i*.19
            blade('NeckFringe',[(s*.37,y,1.57),(s*.56,y+.07,1.49),(s*.63,y+.16,1.29),(s*.64,y+.28,1.04)],
                  [.015,.14,.13,.002],'moss','Chest',normal=(s,-.2,.25),thickness=.080)
    for i in range(5):
        y=-.69+i*.25;z=1.80-i*.06
        blade('CrestLock',[(0,y-.10,z),(0,y+.10,z+.25),(0,y+.43,z+.24),(0,y+.86,z+.19)],
              [.025,.16,.12,.002],'moss','Chest' if i<3 else 'Spine',normal=(0,0,1),thickness=.13)
    sweep('TailCore',[(0,1.03,1.10),(0,1.26,1.08),(0,1.46,.94),(0,1.49,.87)],
          [.12,.13,.10,.004],'hide','Tail01',normal=(0,0,1),depth=1)
    for s in [-1,0,1]:
        blade('TailTuft',[(s*.04,1.22,1.14),(s*.09,1.42,1.11),(s*.08,1.62,.94)],
              [.035,.10,.002],'moss','Tail02',normal=(0,0,1),thickness=.04)


def atlas(mesh, size):
    select([mesh]);bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.05,island_margin=.006,area_weight=.30)
    bpy.ops.object.mode_set(mode='OBJECT')
    image=bpy.data.images.new('BriseSeve_BaseColor',width=size,height=size,alpha=False)
    image.colorspace_settings.name='sRGB'
    for mat in mesh.data.materials:
        nodes=mat.node_tree.nodes;links=mat.node_tree.links
        target=nodes.new('ShaderNodeTexImage');target.image=image;nodes.active=target
        bsdf=next(n for n in nodes if n.type=='BSDF_PRINCIPLED');output=nodes.get('Material Output')
        source=bsdf.inputs['Base Color'].links[0].from_socket
        emission=nodes.new('ShaderNodeEmission')
        links.new(source,emission.inputs['Color']);links.new(emission.outputs[0],output.inputs['Surface'])
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.margin=8
    bpy.ops.object.bake(type='EMIT')
    path=ROOT/'textures'/'brise-seve-basecolor.png';image.filepath_raw=str(path);image.file_format='PNG';image.save();image.pack()
    mat=bpy.data.materials.new('BriseSeve_Painted');mat.use_nodes=True
    nodes=mat.node_tree.nodes;links=mat.node_tree.links
    target=nodes.new('ShaderNodeTexImage');target.image=image
    bsdf=nodes.get('Principled BSDF');bsdf.inputs['Roughness'].default_value=.82
    links.new(target.outputs['Color'],bsdf.inputs['Base Color'])
    mesh.data.materials.clear();mesh.data.materials.append(mat)
    for polygon in mesh.data.polygons:polygon.material_index=0
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
    bpy.ops.object.camera_add(location=(3.6,-5.3,2.25));camera=bpy.context.object;camera.name='ReviewCamera';camera.data.type='ORTHO';camera.data.ortho_scale=4.05
    camera.rotation_euler=(Vector((0,-.07,.96))-camera.location).to_track_quat('-Z','Y').to_euler();scene.camera=camera
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
    from painted_material import create_material
    materials={kind:create_material(kind) for kind in COLORS}
    for part in PARTS:part.data.materials.append(materials[part['surface']])
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
    bpy.ops.export_scene.gltf(filepath=str(ROOT/'models'/'brise-seve.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_skins=True,export_morph=False,export_def_bones=False,export_force_sampling=True,export_vertex_color='NONE')
    lods=[mesh]
    rig.data.pose_position='REST'
    for i,ratio in [(1,.35),(2,.14)]:
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
    if not args.skip_renders:
        render('hero.png',camera)
        render('profile.png',camera,(5,0,1.95))
        render('rear.png',camera,(3.4,4.5,2.6))
        render('front.png',camera,(0,-5,2.0))


if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--lookdev',action='store_true')
    parser.add_argument('--skip-renders',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    build(args)
