"""Refine the static Snow adaptation into an articulated Ether sentinel.

The CC-BY Blender Studio face/anatomy and packed textures are retained. This
script reads v2 and writes a separate v3 asset; it needs no external rig code.
"""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'assets' / 'snow-ether.blend'
OUTPUT = ROOT / 'assets' / 'snow-ether-v3.blend'
bpy.ops.wm.open_mainfile(filepath=str(SOURCE), use_scripts=False)
collection = bpy.data.collections['Arkya_Hero']
root = bpy.data.objects['Hero_Root']
ivory = bpy.data.materials['Arkya | ivory ceramic enamel']
bronze = bpy.data.materials['Arkya | brushed pale bronze']
leather = bpy.data.materials['Arkya | petrol boot leather']
coral = bpy.data.materials['Arkya | coral woven wool']
teal = bpy.data.materials['Arkya | ether turquoise crystal']


def surface_tree(obj):
    bpy.context.view_layer.update()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = obj.evaluated_get(depsgraph)
    data = evaluated.to_mesh()
    result = BVHTree.FromPolygons([evaluated.matrix_world @ v.co for v in data.vertices], [p.vertices[:] for p in data.polygons])
    evaluated.to_mesh_clear()
    return result


def mesh(name, vertices, faces, mat, thickness=0, bevel=0):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    obj.parent = root
    data.materials.append(mat)
    for poly in data.polygons:
        poly.use_smooth = True
    if thickness:
        modifier = obj.modifiers.new('Forged thickness', 'SOLIDIFY')
        modifier.thickness = thickness
        modifier.offset = 0
    if bevel:
        modifier = obj.modifiers.new('Crafted edge bevel', 'BEVEL')
        modifier.width = bevel
        modifier.segments = 3
    return obj


def line(name, points, radius, mat=bronze, cyclic=False):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.resolution_u = 8
    data.bevel_depth = radius
    data.bevel_resolution = 3
    spline = data.splines.new('BEZIER')
    spline.bezier_points.add(len(points)-1)
    for p, point in zip(spline.bezier_points, points):
        p.co = point
        p.handle_left_type = 'VECTOR' if cyclic else 'AUTO'
        p.handle_right_type = 'VECTOR' if cyclic else 'AUTO'
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, data)
    collection.objects.link(obj)
    obj.parent = root
    data.materials.append(mat)
    return obj


def remove_prefix(prefix):
    for obj in list(collection.objects):
        if obj.name.startswith(prefix):
            bpy.data.objects.remove(obj, do_unlink=True)


def project_front(tree, x, z, offset=.008):
    hit, normal, *_ = tree.ray_cast(Vector((x, -1, z)), Vector((0, 1, 0)), 2)
    if hit is None:
        hit, normal, *_ = tree.find_nearest(Vector((x, -.07, z)))
        if hit is None:
            raise RuntimeError('No contact surface for armor')
    return hit + Vector((0, -offset, 0))


def polygon_plate(name, points, mat=ivory, thickness=.009, rim=True, ridge=.003):
    points = [Vector(p) for p in points]
    center = sum(points, Vector()) / len(points)
    center.y -= ridge
    verts = [center]
    for point in points:
        verts.append(center.lerp(point, .84))
    verts.extend(points)
    count = len(points)
    faces = []
    for i in range(count):
        j = (i+1) % count
        faces.extend([(0,1+i,1+j), (1+i,1+count+i,1+count+j,1+j)])
    obj = mesh(name, verts, faces, mat, thickness, .002)
    if rim:
        line(name+' | narrow bronze rim', [p+Vector((0,-.004,0)) for p in points], .0026, bronze, True)
    return obj


def diamond(name, x, y, z, width, height):
    border = [(x,y,z+height),(x+width,y,z),(x,y,z-height),(x-width,y,z)]
    polygon_plate(name+' | bronze bezel', border, bronze, .007, False)
    inner = [(x,y-.007,z+height*.81),(x+width*.76,y-.007,z),(x,y-.007,z-height*.81),(x-width*.76,y-.007,z)]
    # Broad facets carry the crystal shape without a noisy material.
    data = [Vector((x,y-.019,z))]+[Vector(p) for p in inner]
    obj = mesh(name+' | turquoise crystal', data, [(0,1,2),(0,2,3),(0,3,4),(0,4,1)], teal, .003, .0007)
    for p in obj.data.polygons:
        p.use_smooth = False
    return obj


def sleeve_plate(name, start, end, profile, mat=ivory, angles=(-.05, math.pi+.05)):
    start, end = Vector(start), Vector(end)
    axis = (end-start).normalized()
    front = Vector((0,-1,0))
    front = (front-axis*front.dot(axis)).normalized()
    lateral = axis.cross(front).normalized()
    if lateral.x * start.x < 0:
        lateral.negate()
    segments = 30
    verts, faces = [], []
    for t, radius in profile:
        center = start.lerp(end, t)
        for i in range(segments+1):
            a = angles[0] + (angles[1]-angles[0])*i/segments
            radial = front*math.cos(a)+lateral*math.sin(a)
            verts.append(center+radial*radius)
    for j in range(len(profile)-1):
        for i in range(segments):
            a=j*(segments+1)+i
            faces.append((a,a+1,a+segments+2,a+segments+1))
    obj=mesh(name,verts,faces,mat,.011,.0025)
    line(name+' | lower metal binding',verts[-(segments+1):],.0035,bronze)
    return obj


def material_finish(mat, color, roughness, scale):
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    node = next(n for n in nodes if n.type=='BSDF_PRINCIPLED')
    noise=nodes.new('ShaderNodeTexNoise')
    noise.name='Arkya | restrained crafted variation'
    noise.inputs['Scale'].default_value=scale
    noise.inputs['Detail'].default_value=2
    ramp=nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position=.18
    ramp.color_ramp.elements[0].color=(*[c*.94 for c in color],1)
    ramp.color_ramp.elements[1].position=.82
    ramp.color_ramp.elements[1].color=(*[min(1,c*1.04) for c in color],1)
    links.new(noise.outputs['Fac'],ramp.inputs[0])
    links.new(ramp.outputs['Color'],node.inputs['Base Color'])
    mapping=nodes.new('ShaderNodeMapRange')
    mapping.inputs['From Min'].default_value=0
    mapping.inputs['From Max'].default_value=1
    mapping.inputs['To Min'].default_value=roughness-.045
    mapping.inputs['To Max'].default_value=roughness+.045
    links.new(noise.outputs['Fac'],mapping.inputs['Value'])
    links.new(mapping.outputs[0],node.inputs['Roughness'])
    bump=nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value=.10
    bump.inputs['Distance'].default_value=.00018
    links.new(noise.outputs['Fac'],bump.inputs['Height'])
    links.new(bump.outputs[0],node.inputs['Normal'])


# Preserve matching eyes, face and hair while moving toward adult proportions.
for obj in collection.objects:
    if obj.type=='MESH' and obj.name.startswith('Ether Guard'):
        for vertex in obj.data.vertices:
            p=vertex.co
            blend=max(0,min(1,(p.z-1.425)/.08))
            factor=1-.10*blend
            p.x*=factor
            p.y*=factor
            p.z=1.425+(p.z-1.425)*factor

material_finish(ivory,(.72,.68,.55),.38,58)
material_finish(bronze,(.43,.235,.073),.31,90)
material_finish(leather,(.025,.06,.065),.5,160)
material_finish(coral,(.42,.069,.045),.76,190)

# Shoulder caps retain their close-fitting base beneath three articulated lames.
for side,sign in (('L',1),('R',-1)):
    start=(sign*.190,.004,1.379)
    end=(sign*(.285 if sign>0 else .274),.022,1.137)
    base=bpy.data.objects['Pauldron '+side]
    base.data.materials[0]=leather
    for obj in list(collection.objects):
        if obj.name.startswith('Pauldron '+side+' | binding'):
            bpy.data.objects.remove(obj,do_unlink=True)
    sleeve_plate('V3 | '+side+' pauldron crown',start,end,[(-.05,.067),(.03,.103),(.15,.130),(.26,.128),(.30,.119)])
    sleeve_plate('V3 | '+side+' pauldron articulated middle',start,end,[(.25,.117),(.30,.122),(.42,.112),(.47,.104)])
    sleeve_plate('V3 | '+side+' pauldron articulated lower',start,end,[(.43,.101),(.48,.107),(.59,.092),(.63,.082)])

# Cuirass: a single focal gem and two geometric seams replace the tiny badge.
chest=bpy.data.objects['Cuirass | continuous tailored enamel']
chest_tree=surface_tree(chest)
remove_prefix('Cuirass | open-ring')
remove_prefix('Cuirass | inset turquoise')
remove_prefix('Cuirass | raised center')
for side in (-1,1):
    points=[project_front(chest_tree,side*x,z,.007) for x,z in ((.145,1.34),(.114,1.28),(.052,1.245),(.020,1.185))]
    line('V3 | cuirass inset chevron '+str(side),points,.0025,bronze)
    points=[project_front(chest_tree,side*x,z,.006) for x,z in ((.120,1.175),(.083,1.124),(.028,1.107))]
    line('V3 | lower cuirass engraved arc '+str(side),points,.0017,bronze)
p=project_front(chest_tree,0,1.264,.010)
diamond('V3 | sternum Ether focus',p.x,p.y,p.z,.030,.044)

# Front bracer panels are projected onto the existing forearm sleeves.
for side,sign in (('L',1),('R',-1)):
    obj=bpy.data.objects['Vambrace '+side]
    tree=surface_tree(obj)
    obj.data.materials[0]=leather
    center=Vector((.308 if sign>0 else -.286,1.022 if sign>0 else 1.014))
    axis=Vector((sign*.34,-.94)).normalized()
    cross=Vector((-axis.y,axis.x))
    outline=[]
    for u,v in ((-.023,-.060),(.023,-.060),(.032,-.033),(.027,.046),(0,.070),(-.027,.046),(-.032,-.033)):
        p=center+cross*u+axis*v
        outline.append(project_front(tree,p.x,p.y,.009))
    polygon_plate('V3 | articulated bracer front '+side,outline)
    p=project_front(tree,center.x,center.y+.019,.021)
    diamond('V3 | bracer inset '+side,p.x,p.y,p.z,.011,.018)
    for v in (-.046,.044):
        pts=[]
        for u in (-.030,0,.030):
            p=center+cross*u+axis*v
            pts.append(project_front(tree,p.x,p.y,.007))
        line('V3 | bracer leather fastening '+side+str(v),pts,.005,bronze)

# Three overlapping plates each side extend the waist silhouette into armor.
for side in (-1,1):
    for layer in range(3):
        z=.991-.087*layer
        outer=.162+.013*layer
        inside=.044+.006*layer
        outline=[(side*inside,-.162,z),(side*(outer-.018),-.132,z+.008),(side*(outer+.009),-.113,z-.055),(side*(outer-.002),-.131,z-.118),(side*(inside+.012),-.163,z-.102)]
        polygon_plate('V3 | hip tasset '+str(side)+' layer '+str(layer),outline,thickness=.009)
    # A pair of controlled suspended straps explains how the plates are attached.
    for x in (.078,.139):
        line('V3 | tasset suspension '+str(side)+str(x),[(side*x,-.166,1.007),(side*x,-.171,.970)],.007,leather)

# Knee and shin pieces use the real trouser/boot surfaces as their foundation.
pants_tree=surface_tree(bpy.data.objects['Ether Guard-pants'])
shoe_tree=surface_tree(bpy.data.objects['Ether Guard-shoes_base'])
for side in (-1,1):
    x=side*.084
    boot=bpy.data.objects['Boot shaft '+str(side)]
    boot_tree=surface_tree(boot)
    outline=[]
    for dx,z in ((-.040,.447),(.039,.447),(.052,.398),(.029,.215),(.018,.126),(0,.105),(-.021,.132),(-.040,.350)):
        outline.append(project_front(boot_tree,x+dx,z,.010))
    polygon_plate('V3 | sculpted greave '+str(side),outline,thickness=.010)
    line('V3 | greave center ridge '+str(side),[project_front(boot_tree,x,.425,.022),project_front(boot_tree,x,.310,.023),project_front(boot_tree,x,.151,.018)],.002,bronze)
    outline=[]
    for dx,z in ((0,.633),(.046,.601),(.053,.550),(.031,.503),(0,.482),(-.031,.503),(-.053,.550),(-.046,.601)):
        outline.append(project_front(pants_tree,x+dx,z,.015))
    polygon_plate('V3 | knee guard '+str(side),outline,thickness=.012,ridge=.011)
    for z in (.182,.388):
        pts=[]
        for i in range(21):
            a=math.pi*.05+math.pi*.90*i/20
            px=x+.055*math.cos(a)
            py=-.012-.058*math.sin(a)
            pts.append((px,py,z))
        line('V3 | boot fastening '+str(side)+str(z),pts,.006,leather)
    shoe=bpy.data.objects['Ether Guard-shoes_base']
    polygons=[p for p in shoe.data.polygons if p.center.x*side>0 and p.center.y<-.088 and p.center.z>.019]
    indices=sorted({i for p in polygons for i in p.vertices})
    remap={old:new for new,old in enumerate(indices)}
    vertices=[shoe.data.vertices[i].co+shoe.data.vertices[i].normal*.0035 for i in indices]
    faces=[[remap[i] for i in p.vertices] for p in polygons]
    mesh('V3 | enveloping sabaton '+str(side),vertices,faces,ivory,.005,.001)
    pts=[]
    for dx in (-.041,-.025,0,.025,.041):
        hit,normal,*_=shoe_tree.ray_cast(Vector((x+dx,-.091,.5)),Vector((0,0,-1)),1)
        if hit is not None:
            pts.append(hit+Vector((0,0,.007)))
    line('V3 | sabaton instep binding '+str(side),pts,.0025,bronze)

# Trouser fabric below the cuffs is fully concealed; remove it to prevent calf
# intersections on the back view instead of inflating the boot silhouette.
pants=bpy.data.objects['Ether Guard-pants']
bm=bmesh.new()
bm.from_mesh(pants.data)
bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.calc_center_median().z<.515],context='FACES')
bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
bm.to_mesh(pants.data)
bm.free()

# Low-contrast embroidery gives the cloak a recognizable gameplay back view.
thread=bpy.data.materials.new('Arkya | muted golden embroidery')
thread.use_nodes=True
node=thread.node_tree.nodes.get('Principled BSDF')
node.inputs['Base Color'].default_value=(.36,.20,.07,1)
node.inputs['Roughness'].default_value=.82
cape_tree=surface_tree(bpy.data.objects['Cape | coral shoulder mantle'])
def cape_point(x,z):
    hit,normal,*_=cape_tree.ray_cast(Vector((x,1,z)),Vector((0,-1,0)),2)
    if hit is None:
        raise RuntimeError('Embroidery misses cloak')
    return hit+Vector((0,.004,0))
for side in (-1,1):
    points=[]
    for i in range(14):
        t=.10+.79*i/13
        x=side*(.168+.105*math.sin(t*math.pi/2))*.88
        points.append(cape_point(x,1.43-.60*t))
    line('V3 | mantle stitched border '+str(side),points,.0018,thread)
points=[]
for i in range(31):
    angle=math.radians(35+290*i/30)
    points.append(cape_point(.065*math.cos(angle),1.17+.065*math.sin(angle)))
line('V3 | mantle open-ring embroidery',points,.0025,thread)
line('V3 | mantle central diamond embroidery',[cape_point(0,1.211),cape_point(.021,1.17),cape_point(0,1.129),cape_point(-.021,1.17)],.002,thread,True)

# Bake the additions so appending the asset needs no modifiers or support files.
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
        baked.parent=root
        baked.matrix_world=matrix

objects=[o for o in collection.objects if o.type=='MESH']
coords=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
min_z=min(v.z for v in coords)
max_z=max(v.z for v in coords)
scale=1.88/(max_z-min_z)
for obj in objects:
    world=obj.matrix_world.copy()
    for vertex in obj.data.vertices:
        vertex.co=world@vertex.co
        vertex.co.z-=min_z
        vertex.co*=scale
    obj.matrix_world=Matrix.Identity(4)

scene=bpy.context.scene
note='\nV3 adaptation: adult head proportions, articulated shoulder armor, fitted bracer fronts, segmented tassets, knee guards, greaves and restrained procedural material finish. Static look-development model.'
scene['asset_attribution']+=note
scene['asset_version']='v3'
root['attribution']+=note
scene['asset_height_m']=1.88
root['height_m']=1.88
for text in bpy.data.texts:
    if text.name.startswith('ATTRIBUTION'):
        text.write(note)
bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
for image in bpy.data.images:
    if image.source in {'FILE','TILED'} and not image.packed_file and not image.packed_files:
        image.pack()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT),compress=True)
coords=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
print('ARKYA_V3',json.dumps({'output':str(OUTPUT),'objects':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),'bounds':[[min(v[i] for v in coords),max(v[i] for v in coords)] for i in range(3)],'height_m':1.88,'facing':'-Y','textures':len(bpy.data.images)}))
