"""Static fantasy adaptation of Rain by Blender Studio (CC BY 4.0).

Run with the official rain_v3.2.blend opened using --disable-autoexec.
No downloaded rig scripts are executed. Keep the accompanying CREDITS.md.
"""
import bpy
import math
from pathlib import Path
from mathutils import Vector, Matrix

OUT = Path(__file__).resolve().parents[1] / 'assets' / 'rain-ancients.blend'

def material(name, color, rough=.5, metal=0, grain=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Roughness'].default_value = rough
    p.inputs['Metallic'].default_value = metal
    if grain:
        n = m.node_tree.nodes.new('ShaderNodeTexNoise')
        n.inputs['Scale'].default_value = 170
        b = m.node_tree.nodes.new('ShaderNodeBump')
        b.inputs['Strength'].default_value = .13
        b.inputs['Distance'].default_value = grain
        m.node_tree.links.new(n.outputs['Fac'], b.inputs['Height'])
        m.node_tree.links.new(b.outputs['Normal'], p.inputs['Normal'])
    return m

def mesh(name, vertices, faces, mat, subdiv=1, thickness=.004):
    d = bpy.data.meshes.new(name)
    d.from_pydata(vertices, [], faces)
    d.materials.append(mat)
    o = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(o)
    for p in d.polygons: p.use_smooth = True
    if subdiv:
        m = o.modifiers.new('Soft tailored surface', 'SUBSURF')
        m.levels = m.render_levels = subdiv
    if thickness:
        m = o.modifiers.new('Fabric thickness', 'SOLIDIFY')
        m.thickness = thickness
    return o

def tube(name, points, radius, mat):
    d = bpy.data.curves.new(name, 'CURVE')
    d.dimensions = '3D'
    d.resolution_u = 16
    d.bevel_depth = radius
    d.bevel_resolution = 4
    s = d.splines.new('POLY')
    s.points.add(len(points)-1)
    for p, co in zip(s.points, points): p.co = (*co, 1)
    o = bpy.data.objects.new(name, d)
    bpy.context.collection.objects.link(o)
    d.materials.append(mat)
    return o

def ellipsoid(name, loc, scale, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=32, ring_count=20, location=loc)
    o=bpy.context.object; o.name=name; o.scale=scale
    o.data.materials.append(mat)
    for p in o.data.polygons: p.use_smooth=True
    return o

def wrap(name, rings, mat, start=0, end=math.tau, steps=64, thickness=.006):
    vs=[]; fs=[]
    for z,rx,ry,cy in rings:
        for i in range(steps+1):
            a=start+(end-start)*i/steps
            vs.append((rx*math.sin(a), cy-ry*math.cos(a), z))
    for j in range(len(rings)-1):
        for i in range(steps):
            q=j*(steps+1)+i; fs.append((q,q+1,q+steps+2,q+steps+1))
    return mesh(name,vs,fs,mat,1,thickness)

def prepare():
    src=Path(bpy.data.filepath).parent
    for image in bpy.data.images:
        if image.source in {'FILE','TILED'} and image.filepath:
            image.filepath=bpy.path.abspath(image.filepath)
    rig=bpy.data.objects['RIG-rain']
    prop=rig.pose.bones['Properties_IKFK']
    prop['ik_arm_left']=0.0; prop['ik_arm_right']=0.0
    rig.update_tag(); bpy.context.view_layer.update()
    for side,angle in [('L',68),('R',-64)]:
        b=rig.pose.bones['FK-Upperarm.'+side]
        pivot=b.head.copy()
        b.matrix=Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(angle),4,'Y') @ Matrix.Translation(-pivot) @ b.matrix
    bpy.context.view_layer.update()
    # The joints are recorded in world space for fitted leather cuffs.
    joints={s:[rig.matrix_world @ rig.pose.bones[n+'.'+s].head for n in ['FK-Forearm','FK-Hand']] for s in ['L','R']}
    deps=bpy.context.evaluated_depsgraph_get()
    baked=[]
    for o in list(bpy.context.scene.objects):
        if o.type!='MESH' or not o.name.startswith('GEO-rain') or o.hide_render or o.name.endswith('viewport'): continue
        for mod in o.modifiers:
            if mod.type=='SUBSURF': mod.levels=min(mod.render_levels,2)
        bpy.context.view_layer.update()
        source_materials=[slot.material for slot in o.material_slots]
        ev=o.evaluated_get(deps)
        data=bpy.data.meshes.new_from_object(ev,preserve_all_data_layers=True,depsgraph=deps)
        data.materials.clear()
        for mat in source_materials: data.materials.append(mat)
        obj=bpy.data.objects.new('Rain_'+o.name.removeprefix('GEO-rain-'),data)
        obj.matrix_world=o.matrix_world.copy(); baked.append(obj)
    # Serialize the evaluated meshes before resetting the source rig dependency graph.
    for m in bpy.data.materials:
        m.animation_data_clear()
        if m.node_tree:m.node_tree.animation_data_clear()
    for group in bpy.data.node_groups:group.animation_data_clear()
    collection=bpy.data.collections.new('Arkya_Hero')
    for o in baked: collection.objects.link(o)
    stage=Path('D:/Tools/BlenderPortable/character-sources/rain-static-stage.blend')
    bpy.data.libraries.write(str(stage),{collection})
    print('RAIN_STATIC_WRITTEN',flush=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    with bpy.data.libraries.load(str(stage),link=False) as (available,loaded): loaded.collections=['Arkya_Hero']
    collection=loaded.collections[0]
    bpy.context.scene.collection.children.link(collection)
    baked=list(collection.objects)
    bpy.context.view_layer.active_layer_collection=bpy.context.view_layer.layer_collection.children[collection.name]
    sage=material('Arkya_Sage_woven_tunic',(.095,.19,.12),.7,grain=.001)
    leather=material('Arkya_Chestnut_leather',(.12,.048,.024),.48,grain=.001)
    dark=material('Arkya_Dark_soft_leather',(.035,.048,.029),.64,grain=.001)
    plum=material('Arkya_Plum_cloak',(.125,.035,.083),.72,grain=.0015)
    gold=material('Arkya_Antique_bronze',(.49,.25,.07),.3,.72)
    silver=material('Arkya_Silver_hair',(.49,.57,.58),.32)
    # Retain the source facial shaders, UVs, eyes, brows and skin textures.
    for o in baked:
        if o.name in {'Rain_top'}: o.data.materials.clear(); o.data.materials.append(sage)
        if o.name in {'Rain_jeans'}: o.data.materials.clear(); o.data.materials.append(dark)
        if o.name in {'Rain_scarf'}: o.data.materials.clear(); o.data.materials.append(plum)
        if o.name in {'Rain_shoes'}: o.data.materials.clear(); o.data.materials.append(leather)
        if o.name.startswith('Rain_hair_'): 
            o.data.materials.clear(); o.data.materials.append(silver)
        if o.name in {'Rain_hairband'}: o.data.materials.clear(); o.data.materials.append(gold)
    wrap('Ranger fitted leather corset',[(.87,.174,.14,0),(.89,.175,.142,0),(.95,.15,.14,0),(1.02,.148,.14,0),(1.04,.15,.14,0)],leather)
    wrap('Waist belt',[(.88,.179,.147,0),(.885,.18,.148,0),(.922,.17,.149,0),(.928,.169,.148,0)],dark)
    for z in [.89,.918]:
        tube('Belt stitched rim',[(.181*math.sin(a),-.15*math.cos(a),z) for a in [i*math.tau/96 for i in range(97)]],.0017,gold)
    ellipsoid('Amber belt clasp',(0,-.158,.902),(.032,.009,.03),gold)
    # Four curved tunic panels with a walking slit, overlapping soft leather lining.
    for i,(a,b) in enumerate([(-1.4,-.12),(.12,1.4),(1.7,3.05),(3.23,4.58)]):
        wrap('Layered tunic panel '+str(i),[(.9,.18,.145,0),(.88,.185,.15,0),(.72,.225,.162,.014),(.58,.238,.171,.02),(.565,.234,.17,.02)],sage,a,b,24)
        points=[]
        for n in range(25):
            t=a+(b-a)*n/24;points.append((.239*math.sin(t),.02-.173*math.cos(t),.579))
        tube('Tunic embroidered hem',points,.0025,gold)
    # A folded mantle, attached at the shoulders and opening at the front.
    verts=[]; faces=[]
    for j in range(22):
        t=j/21
        for i in range(49):
            a=1.25+(math.tau-2.5)*i/48
            r=.16+.15*t+.012*math.sin(a*11)*math.sin(math.pi*t)
            verts.append((r*math.sin(a),.07-(.12+.08*t)*math.cos(a)+.06*t,1.28-.74*t+.012*math.sin(a*10)*t))
    for j in range(21):
        for i in range(48):
            q=j*49+i;faces.append((q,q+1,q+50,q+49))
    mesh('Plum ranger mantle with folds',verts,faces,plum,2,.006)
    for a in [1.25,math.tau-1.25]:
        pts=[]
        for j in range(35):
            t=j/34;r=.16+.15*t+.012*math.sin(a*11)*math.sin(math.pi*t)
            pts.append((r*math.sin(a),.07-(.12+.08*t)*math.cos(a)+.06*t,1.28-.74*t+.012*math.sin(a*10)*t))
        tube('Mantle bronze piping',pts,.0027,gold)
    ellipsoid('Leaf mantle clasp',(.09,-.105,1.265),(.032,.012,.046),gold)
    amber=material('Arkya_Amber_gem',(.7,.23,.015),.2,.2)
    ellipsoid('Amber clasp stone',(.09,-.117,1.265),(.017,.008,.027),amber)
    # Long boots follow the existing lower leg positions.
    for side in [-1,1]:
        o=wrap('High leather boot',[(.09,.062,.067,.007),(.12,.064,.067,.007),(.25,.052,.057,0),(.39,.057,.065,-.008),(.42,.06,.069,-.008)],leather)
        o.location.x=side*.078
        c=wrap('Turned boot cuff',[(.395,.060,.07,-.008),(.40,.064,.073,-.008),(.426,.064,.073,-.008),(.43,.060,.07,-.008)],dark);c.location.x=side*.078
        tube('Boot central stitch',[(side*.078,-.068,.13),(side*.078,-.061,.25),(side*.078,-.08,.41)],.0017,gold)
    for side,(elbow,wrist) in joints.items():
        direction=(elbow-wrist).normalized(); center=wrist+direction*.085
        q=Vector((0,0,1)).rotation_difference(direction)
        o=wrap('Leather forearm bracer '+side,[(-.055,.038,.038,0),(-.05,.041,.041,0),(.06,.05,.05,0),(.065,.046,.046,0)],leather)
        o.location=center;o.rotation_mode='QUATERNION';o.rotation_quaternion=q
    # Parent the entire authored adaptation to a single feet-origin root.
    root=bpy.data.objects.new('Hero_Root',None);collection.objects.link(root)
    for o in list(collection.objects):
        if o!=root:o.parent=root
    root.scale=(1.12,)*3
    bpy.context.view_layer.update()
    # Apply the common scale to mesh coordinates for portable append placement.
    for o in list(collection.objects):
        if o!=root:
            world=o.matrix_world.copy();o.parent=None;o.matrix_world=world
    root.scale=(1,)*3
    for o in list(collection.objects):
        if o!=root:o.parent=root
    root['credit']='Rain — Blender Studio / Blender Foundation, CC BY 4.0. Static pose and fantasy outfit adaptation for Arkya.'
    root['source']='https://studio.blender.org/characters/rain/v2/'
    root['license']='https://creativecommons.org/licenses/by/4.0/'
    bpy.context.scene['asset_description']='Detailed static study; not a game-ready rig or original character sculpt.'
    for t in list(bpy.data.texts): bpy.data.texts.remove(t)
    text=bpy.data.texts.new('CREDITS — Rain.txt');text.write(root['credit']+'\n'+root['source']+'\n'+root['license'])
    bpy.ops.file.pack_all()
    OUT.parent.mkdir(parents=True,exist_ok=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT))
    print('RAIN_HERO_READY',str(OUT),len(collection.objects))

if __name__=='__main__': prepare()
