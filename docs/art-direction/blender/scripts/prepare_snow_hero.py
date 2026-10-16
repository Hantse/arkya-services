"""Build a static, attributed Ether guard from Blender Studio's CC-BY Snow.

Run with Blender --background --disable-autoexec --python this_file.
No bundled character Python code is executed. The source rig is evaluated with
Blender's own constraints, then removed from the self-contained output asset.
"""

import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path('D:/Tools/BlenderPortable/character-sources/snow-v4/Snow/snow_v4.2.blend')
OUTPUT = ROOT / 'assets' / 'snow-ether.blend'


def material(name, color, metallic=0.0, roughness=0.45):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*color, 1)
    mat.use_nodes = True
    node = mat.node_tree.nodes.get('Principled BSDF')
    node.inputs['Base Color'].default_value = (*color, 1)
    node.inputs['Metallic'].default_value = metallic
    node.inputs['Roughness'].default_value = roughness
    return mat


def tint_source(name, color, metallic=None):
    mat = bpy.data.materials.get(name)
    if not mat or not mat.use_nodes:
        return
    for node in mat.node_tree.nodes:
        if node.type == 'BSDF_PRINCIPLED':
            socket = node.inputs['Base Color']
            for link in list(socket.links):
                mat.node_tree.links.remove(link)
            socket.default_value = (*color, 1)
            if metallic is not None:
                for link in list(node.inputs['Metallic'].links):
                    mat.node_tree.links.remove(link)
                node.inputs['Metallic'].default_value = metallic
    mat.diffuse_color = (*color, 1)


def mesh_object(name, verts, faces, mat, subdiv=0, thickness=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    data.materials.append(mat)
    for poly in data.polygons:
        poly.use_smooth = True
    if subdiv:
        modifier = obj.modifiers.new('Tailored smooth surface', 'SUBSURF')
        modifier.levels = subdiv
        modifier.render_levels = subdiv
    if thickness:
        modifier = obj.modifiers.new('Fabrication thickness', 'SOLIDIFY')
        modifier.thickness = thickness
        modifier.offset = 0
        modifier = obj.modifiers.new('Soft crafted edges', 'BEVEL')
        modifier.width = 0.0025
        modifier.segments = 3
    return obj


def curve(name, points, radius, mat, cyclic=False):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.resolution_u = 12
    data.bevel_resolution = 4
    data.bevel_depth = radius
    spline = data.splines.new('BEZIER')
    spline.bezier_points.add(len(points)-1)
    for b, p in zip(spline.bezier_points, points):
        b.co = p
        b.handle_left_type = 'AUTO'
        b.handle_right_type = 'AUTO'
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    data.materials.append(mat)
    return obj


def front_surface(x, z, offset=0.018):
    hit, normal, index, distance = shirt_tree.ray_cast(Vector((x, -1.0, z)), Vector((0, 1, 0)), 2.0)
    if hit is None:
        raise RuntimeError(f'Armor contour misses the shirt: {x:.4f}, {z:.4f}')
    return (x, hit.y-offset, z)


def fitted_plate(name, outline, mat):
    center = Vector((sum(p[0] for p in outline)/len(outline), sum(p[1] for p in outline)/len(outline)))
    verts = [front_surface(center.x, center.y)]
    faces = []
    count = len(outline)
    for level in (0.28, 0.58, 0.84, 0.96, 1.0):
        for point in outline:
            p = center.lerp(Vector(point), level)
            verts.append(front_surface(p.x, p.y))
    for i in range(count):
        faces.append((0, 1+i, 1+(i+1)%count))
    for ring in range(4):
        start = 1+ring*count
        next_start = start+count
        for i in range(count):
            j = (i+1)%count
            faces.append((start+i, next_start+i, next_start+j, start+j))
    obj = mesh_object(name, verts, faces, mat, subdiv=1, thickness=0.012)
    curve(name+' | bronze binding', [front_surface(x,z,0.026) for x,z in outline], .004, bronze, True)
    return obj


def sleeve(name, start, end, profile, mat, angle=(-math.pi, math.pi), trim=True):
    start, end = Vector(start), Vector(end)
    axis = (end-start).normalized()
    front = Vector((0,-1,0))
    front = (front-axis*front.dot(axis)).normalized()
    lateral = axis.cross(front).normalized()
    # Each side's cap rises outwards, away from the torso.
    if lateral.x * start.x < 0:
        lateral.negate()
    segments = 40
    verts, faces = [], []
    for t, radius in profile:
        center = start.lerp(end, t)
        for i in range(segments+1):
            a = angle[0]+(angle[1]-angle[0])*i/segments
            radial = front*math.cos(a)+lateral*math.sin(a)
            verts.append(center+radial*radius)
    for j in range(len(profile)-1):
        for i in range(segments):
            a = j*(segments+1)+i
            faces.append((a,a+1,a+segments+2,a+segments+1))
    obj = mesh_object(name, verts, faces, mat, subdiv=1, thickness=.009)
    if trim:
        for j in (0,len(profile)-1):
            pts = verts[j*(segments+1):(j+1)*(segments+1)]
            curve(name+' | binding '+str(j), pts, .0035, bronze)
    return obj


def rotate_world(bone, axis, degrees):
    matrix = bone.matrix.copy()
    pivot = bone.head.copy()
    bone.matrix = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(degrees), 4, axis) @ Matrix.Translation(-pivot) @ matrix
    bpy.context.view_layer.update()


bpy.ops.wm.open_mainfile(filepath=str(SOURCE), use_scripts=False)
rig = bpy.data.objects['RIG-Snow']
if rig.animation_data:
    rig.animation_data.action = None
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
props = rig.pose.bones['Properties']
props['ik_left_upperarm'] = 0.0
props['ik_right_upperarm'] = 0.0
rig.pose.bones['Properties_Character_Snow']['Quality'] = 2
rig.update_tag()
bpy.context.view_layer.update()
rotate_world(rig.pose.bones['FK-UpperArm.L'], 'Y', 68)
rotate_world(rig.pose.bones['FK-UpperArm.R'], 'Y', -71)
rotate_world(rig.pose.bones['FK-Forearm.L'], 'X', -14)
rotate_world(rig.pose.bones['FK-Forearm.R'], 'X', -9)
rotate_world(rig.pose.bones['FK-Wrist.L'], 'Z', -5)
rotate_world(rig.pose.bones['FK-Wrist.R'], 'Z', 5)

# Slight finger flex avoids the flat, splayed hand of a bind pose.
for side in ('L', 'R'):
    for finger in ('Index','Middle','Ring','Pinky'):
        for segment, angle in ((1,8),(2,14),(3,9)):
            bone = rig.pose.bones.get(f'FK-Finger_{finger}{segment}.{side}')
            if bone:
                bone.rotation_mode='XYZ'
                bone.rotation_euler.x = math.radians(angle)
rig.update_tag()
bpy.context.view_layer.update()

bone_points = {}
for side in ('L','R'):
    bone_points[side] = {
        'shoulder': tuple(rig.pose.bones[f'FK-UpperArm.{side}'].head),
        'elbow': tuple(rig.pose.bones[f'FK-Forearm.{side}'].head),
        'wrist': tuple(rig.pose.bones[f'FK-Wrist.{side}'].head),
    }

# Resolve original paths before the output blend changes its relative base.
for img in bpy.data.images:
    if img.source in {'FILE','TILED'} and img.filepath:
        name = Path(img.filepath.replace('\\','/')).name
        if not name:
            continue
        img.filepath = str(SOURCE.parent/'textures'/name)
        try:
            img.reload()
            img.pack()
        except RuntimeError as exc:
            raise RuntimeError(f'Cannot pack source texture {name}') from exc

tint_source('snow.shirt', (.022,.105,.128))
tint_source('snow.pants', (.012,.036,.046))
tint_source('snow.shoe', (.025,.036,.038), .03)

collection = bpy.data.collections.new('Arkya_Hero')
bpy.context.scene.collection.children.link(collection)
depsgraph = bpy.context.evaluated_depsgraph_get()
keep = []
allowed = [obj for obj in bpy.data.objects if obj.type=='MESH' and obj.name.startswith(('GEO-snow-','GEO-snow_eye_corneas')) and not obj.hide_render]
for obj in allowed:
    if 'helper' in obj.name or 'deformer' in obj.name or obj.name.endswith(('eye_anim','shoes_parts')):
        continue
    for modifier in obj.modifiers:
        if modifier.type=='SUBSURF':
            modifier.levels = min(2, modifier.render_levels)
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(depsgraph)
    data = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
    data.materials.clear()
    for slot in obj.material_slots:
        if slot.material:
            data.materials.append(slot.material)
    baked = bpy.data.objects.new(obj.name.replace('GEO-snow','Ether Guard'),data)
    collection.objects.link(baked)
    baked.matrix_world = obj.matrix_world
    keep.append(baked)

for obj in list(bpy.data.objects):
    if obj not in keep:
        bpy.data.objects.remove(obj,do_unlink=True)
for text in list(bpy.data.texts):
    bpy.data.texts.remove(text)
for col in list(bpy.data.collections):
    if col != collection:
        bpy.data.collections.remove(col)

ivory = material('Arkya | ivory ceramic enamel',(.72,.68,.55),.12,.40)
bronze = material('Arkya | brushed pale bronze',(.43,.235,.073),.72,.30)
coral = material('Arkya | coral woven wool',(.42,.069,.045),0,.80)
leather = material('Arkya | petrol boot leather',(.025,.06,.065),.02,.44)
teal = material('Arkya | ether turquoise crystal',(.02,.34,.39),.28,.2)

shirt = next(obj for obj in keep if obj.name.endswith('-shirt'))
shirt_tree = BVHTree.FromPolygons([shirt.matrix_world@v.co for v in shirt.data.vertices], [p.vertices[:] for p in shirt.data.polygons])

fitted_plate('Cuirass | continuous tailored enamel', [(-.07,1.379),(0,1.35),(.07,1.379),(.16,1.345),(.18,1.29),(.145,1.21),(.121,1.14),(.10,1.085),(0,1.055),(-.10,1.085),(-.121,1.14),(-.145,1.21),(-.18,1.29),(-.16,1.345)],ivory)
curve('Cuirass | raised center seam',[front_surface(0,1.25,.026),front_surface(0,1.15,.026),front_surface(0,1.067,.026)],.0025,bronze)

# The open-ring emblem is a physical bronze inlay, with a small enamel core.
emblem = []
for i in range(33):
    a = math.radians(35+290*i/32)
    x,z = .035*math.cos(a), 1.302+.035*math.sin(a)
    emblem.append(front_surface(x,z,.040))
curve('Cuirass | open-ring Ether insignia',emblem,.006,bronze)
curve('Cuirass | inset turquoise focus',[front_surface(0,1.288,.042),front_surface(0,1.315,.042)],.008,teal)

for side,points in bone_points.items():
    sleeve('Pauldron '+side,points['shoulder'],points['elbow'],[(.01,.068),(.08,.092),(.24,.098),(.40,.085),(.45,.079)],ivory,(-.12,math.pi+.12))
    sleeve('Vambrace '+side,points['elbow'],points['wrist'],[(.32,.047),(.36,.049),(.73,.038),(.79,.037)],ivory)

# A wide belt follows the torso volume and supports the lower armor.
verts,faces=[],[]
for z in (.965,.971,1.009,1.015):
    for i in range(64):
        a=2*math.pi*i/64
        radial=Vector((math.cos(a),math.sin(a),0))
        hit,normal,index,distance=shirt_tree.ray_cast(radial+Vector((0,0,z)),-radial,2)
        if hit is None:
            raise RuntimeError('Belt fit failed')
        verts.append(hit+radial*.012)
for j in range(3):
    for i in range(64):
        a=j*64+i;b=j*64+(i+1)%64
        faces.append((a,b,b+64,a+64))
mesh_object('Waist | fitted leather belt',verts,faces,leather,subdiv=1,thickness=.006)
curve('Waist | bronze buckle',[front_surface(-.03,.979,.025),front_surface(-.03,1.002,.025),front_surface(.03,1.002,.025),front_surface(.03,.979,.025)],.005,bronze,True)

for side in (-1,1):
    verts,faces=[],[]
    for j in range(13):
        t=j/12
        for i in range(9):
            u=i/8
            x=side*(.021+u*(.12+.025*t))
            y=-.134-.010*math.sin(u*math.pi*2)*t-.005*t
            z=.978-.32*t+.032*t*u
            verts.append((x,y,z))
    for j in range(12):
        for i in range(8):
            a=j*9+i
            faces.append((a,a+1,a+10,a+9))
    mesh_object('Tunic | divided front panel '+str(side),verts,faces,leather,subdiv=2,thickness=.004)
    curve('Tunic | narrow bronze hem '+str(side),verts[-9:],.0025,bronze)

# Smooth, closed footwear uppers fit over the source trousers and retain its feet.
for side in (-1,1):
    x=side*.083
    sleeve('Boot shaft '+str(side),(x,.014,.07),(x,.006,.51),[(0,.056),(.06,.051),(.19,.047),(.45,.056),(.70,.069),(.94,.071),(1,.068)],leather,trim=False)
    sleeve('Boot cuff '+str(side),(x,.014,.07),(x,.006,.51),[(.90,.075),(.94,.077),(1,.075)],ivory)

# Short cape: collar anchors, broad cloth folds and a weighted hem.
verts,faces=[],[]
rows,cols=22,20
for j in range(rows+1):
    t=j/rows
    width=.168+.105*math.sin(t*math.pi/2)
    for i in range(cols+1):
        u=-1+2*i/cols
        z=1.418-.60*t-.035*(1-u*u)*math.sin(t*math.pi)
        y=.091+.12*t+.020*math.cos(u*math.pi*4)*(.2+.8*t)
        verts.append((u*width,y,z))
for j in range(rows):
    for i in range(cols):
        a=j*(cols+1)+i
        faces.append((a,a+1,a+cols+2,a+cols+1))
mesh_object('Cape | coral shoulder mantle',verts,faces,coral,subdiv=2,thickness=.004)
curve('Cape | weighted bronze hem',verts[-(cols+1):],.0035,bronze)
for side in (-1,1):
    points=[(side*.155,-.088,1.385),(side*.17,-.026,1.438),(side*.166,.056,1.445),(side*.165,.098,1.414)]
    band_verts=[]
    for x,y,z in points:
        band_verts.extend([(x-.014,y,z),(x+.014,y,z)])
    mesh_object('Cape | flat shoulder fastening '+str(side),band_verts,[(0,1,3,2),(2,3,5,4),(4,5,7,6)],coral,subdiv=2,thickness=.004)

# Apply every generated modifier and curve; the asset is independent of the rig.
bpy.context.view_layer.update()
depsgraph=bpy.context.evaluated_depsgraph_get()
for obj in list(collection.objects):
    if obj.type=='CURVE' or obj.modifiers:
        evaluated=obj.evaluated_get(depsgraph)
        data=bpy.data.meshes.new_from_object(evaluated,preserve_all_data_layers=True,depsgraph=depsgraph)
        name=obj.name
        matrix=obj.matrix_world.copy()
        bpy.data.objects.remove(obj,do_unlink=True)
        baked=bpy.data.objects.new(name,data)
        collection.objects.link(baked)
        baked.matrix_world=matrix

all_meshes=[obj for obj in collection.objects if obj.type=='MESH']
coords=[obj.matrix_world@v.co for obj in all_meshes for v in obj.data.vertices]
min_z=min(v.z for v in coords)
max_z=max(v.z for v in coords)
scale=1.88/(max_z-min_z)
for obj in all_meshes:
    world=obj.matrix_world.copy()
    for vertex in obj.data.vertices:
        vertex.co=world@vertex.co
        vertex.co.z-=min_z
        vertex.co*=scale
    obj.matrix_world=Matrix.Identity(4)
    for poly in obj.data.polygons:
        poly.use_smooth=True

root=bpy.data.objects.new('Hero_Root',None)
collection.objects.link(root)
root.empty_display_type='PLAIN_AXES'
root.empty_display_size=.15
for obj in all_meshes:
    obj.parent=root

attribution='Snow v4.2 — Blender Studio. Source: https://studio.blender.org/characters/snow/v4/\nLicensed Creative Commons Attribution 4.0: https://creativecommons.org/licenses/by/4.0/\nArkya adaptation: static relaxed pose, recolored textiles, original fitted Ether armor and coral mantle.\nNo Blender Studio endorsement is implied. Original rig Python was not executed.\n'
text=bpy.data.texts.new('ATTRIBUTION — Snow and Arkya')
text.write(attribution)
scene=bpy.context.scene
scene['asset_attribution']=attribution
scene['asset_source_url']='https://studio.blender.org/characters/snow/v4/'
scene['asset_license']='CC-BY-4.0'
scene['asset_height_m']=1.88
scene['asset_facing']='-Y; feet at Z=0'
scene['asset_type']='Static look-development character, not game-ready rig'
root['attribution']=attribution
root['height_m']=1.88
root['facing']='-Y'
scene.render.engine='BLENDER_EEVEE_NEXT'
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
bpy.context.view_layer.objects.active=root

# Remove orphaned source drivers, helpers, and unused source textures.
bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
for img in bpy.data.images:
    if img.source in {'FILE','TILED'} and img.filepath and not img.packed_file and not img.packed_files:
        img.pack()
        if not img.packed_file and not img.packed_files:
            raise RuntimeError('Unpacked image remains: '+img.name+' '+img.filepath)
OUTPUT.parent.mkdir(parents=True,exist_ok=True)
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT),compress=True)
coords=[obj.matrix_world@v.co for obj in all_meshes for v in obj.data.vertices]
bounds=[[min(v[i] for v in coords),max(v[i] for v in coords)] for i in range(3)]
print('ARKYA_HERO_METADATA',json.dumps({'file':str(OUTPUT),'mesh_objects':len(all_meshes),'vertices':sum(len(o.data.vertices) for o in all_meshes),'bounds':bounds,'height_m':1.88,'facing':'-Y','textures':len(bpy.data.images),'source':'Blender Studio Snow v4.2','license':'CC-BY-4.0'}))
