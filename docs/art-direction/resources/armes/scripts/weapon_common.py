"""Shared sculpting, pigments and portable exports for Arkya weapon concepts."""
import argparse
import hashlib
import importlib
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'models'
PALETTE = {
    'steel': ((.13, .20, .29), 'Steel'),
    'edge': ((.68, .78, .84), 'Steel'),
    'gold': ((.48, .25, .065), 'Gold'),
    'wood': ((.16, .059, .018), 'Organic'),
    'leather': ((.048, .017, .009), 'Organic'),
    'ivory': ((.79, .71, .53), 'Organic'),
    'cord': ((.24, .15, .08), 'Organic'),
    'jade': ((.008, .22, .15), 'Jade'),
    'crystal': ((.008, .38, .33), 'Crystal'),
    'amber': ((.93, .39, .036), 'Crystal'),
}
SURFACES = {'Steel': (.55, .38), 'Gold': (.7, .34), 'Organic': (0., .72),
            'Jade': (.12, .29), 'Crystal': (.16, .24)}


def material(kind):
    surface = PALETTE[kind][1]
    name = 'Arkya_Weapon_' + surface
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    attr = mat.node_tree.nodes.new('ShaderNodeVertexColor')
    attr.layer_name = 'Painted'
    mat.node_tree.links.new(attr.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Metallic'].default_value, bsdf.inputs['Roughness'].default_value = SURFACES[surface]
    bsdf.inputs['Coat Weight'].default_value = .2 if surface in ('Jade', 'Crystal') else 0
    if surface == 'Crystal':
        mat.node_tree.links.new(attr.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = .24
    return mat


def paint(obj, kind):
    color = PALETTE[kind][0]
    attr = obj.data.color_attributes.get('Painted')
    if attr:
        obj.data.color_attributes.remove(attr)
    attr = obj.data.color_attributes.new(name='Painted', type='FLOAT_COLOR', domain='CORNER')
    obj.data.color_attributes.active_color = attr
    for loop in obj.data.loops:
        p = obj.data.vertices[loop.vertex_index].co
        n = obj.data.vertices[loop.vertex_index].normal
        broad = math.sin(p.x * 19 + p.z * 8) * math.cos(p.y * 17 - p.z * 11)
        value = .87 + .17 * broad + .075 * n.x + .10 * n.z
        if kind == 'wood':
            grain = math.sin(p.x * 210 + math.sin(p.z * 16) * 1.5 + p.y * 135)
            value += .16 * grain + .07 * math.sin(p.x * 71 + p.z * 5)
        elif kind == 'leather':
            value += .06 * math.sin(p.z * 160 + p.x * 14)
        elif kind in ('crystal', 'jade', 'amber'):
            value += .24 * math.sin(p.x * 41 + p.z * 17) + .12 * n.z
        elif kind in ('steel', 'edge', 'gold'):
            value += .07 * math.sin(p.z * 22 - p.x * 34)
        attr.data[loop.index].color = (*[min(.98, max(.002, c * value)) for c in color], 1.)
    if not obj.data.uv_layers:
        uv = obj.data.uv_layers.new(name='PaintedUV')
        for loop in obj.data.loops:
            p = obj.data.vertices[loop.vertex_index].co
            uv.data[loop.index].uv = ((p.x + 1) / 2, (p.z + 1) / 3)


def mesh(name, verts, faces, kind, bevel=0, smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.collection.objects.link(obj)
    data.materials.append(material(kind))
    obj['pigment'] = kind
    bm = bmesh.new()
    bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(data)
    bm.free()
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    if bevel:
        mod = obj.modifiers.new('Sculpted bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        bpy.ops.object.modifier_apply(modifier=mod.name)
    for polygon in data.polygons:
        polygon.use_smooth = smooth
    if smooth or bevel:
        mod = obj.modifiers.new('Weighted planes', 'WEIGHTED_NORMAL')
        mod.keep_sharp = True
        mod.weight = 45
        bpy.ops.object.modifier_apply(modifier=mod.name)
    data.update()
    paint(obj, kind)
    return obj


def profile(name, pointsXZ, depth, kind, y=0, bevel=.004):
    points = list(pointsXZ)
    if len(points) > 2 and Vector(points[0]) == Vector(points[-1]):
        points.pop()
    count = len(points)
    vertices = [(x, y + side * depth / 2, z) for side in (-1, 1) for x, z in points]
    faces = [tuple(reversed(range(count))), tuple(range(count, count * 2))]
    faces += [(i, (i + 1) % count, (i + 1) % count + count, i + count) for i in range(count)]
    return mesh(name, vertices, faces, kind, bevel=min(bevel, depth * .35), smooth=False)


def curve(pointsXYZ, steps=8):
    pts = [Vector(p) for p in pointsXYZ]
    result = []
    for i in range(len(pts) - 1):
        a, b, c, d = pts[max(0, i-1)], pts[i], pts[i+1], pts[min(len(pts)-1, i+2)]
        for k in range(steps):
            t = k / steps
            result.append(tuple(.5 * ((2*b) + (-a+c)*t + (2*a-5*b+4*c-d)*t*t + (-a+3*b-3*c+d)*t*t*t)))
    result.append(tuple(pts[-1]))
    return result


def tube(name, pointsXYZ, radii, kind, sides=12, smooth=True):
    pts = [Vector(p) for p in pointsXYZ]
    if isinstance(radii, (int, float)):
        radii = [radii] * len(pts)
    if len(radii) != len(pts):
        raise ValueError(name + ': radius/point count mismatch')
    verts = []
    for i, p in enumerate(pts):
        direction = pts[min(i+1, len(pts)-1)] - pts[max(0, i-1)]
        if direction.length < 1e-8:
            direction = Vector((0, 0, 1))
        direction.normalize()
        reference = Vector((0, 1, 0)) if abs(direction.y) < .9 else Vector((1, 0, 0))
        u = direction.cross(reference).normalized()
        v = direction.cross(u).normalized()
        radius = radii[i]
        rx, ry = (radius, radius) if isinstance(radius, (int, float)) else radius
        for j in range(sides):
            angle = 2 * math.pi * j / sides
            verts.append(tuple(p + u * rx * math.cos(angle) + v * ry * math.sin(angle)))
    faces = [tuple(reversed(range(sides)))]
    for i in range(len(pts)-1):
        for j in range(sides):
            a, b = i*sides+j, i*sides+(j+1)%sides
            faces.append((a, b, b+sides, a+sides))
    faces.append(tuple((len(pts)-1)*sides+j for j in range(sides)))
    return mesh(name, verts, faces, kind, smooth=smooth)


def sphere(name, centerXYZ, scaleXYZ, kind, segments=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=centerXYZ)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scaleXYZ
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    obj.data.materials.append(material(kind))
    obj['pigment'] = kind
    for face in obj.data.polygons:
        face.use_smooth = True
    paint(obj, kind)
    return obj


def gem(name, centerXYZ, width, height, depth, kind='crystal'):
    x, y, z = centerXYZ
    contour = [(0, height/2), (width*.46, height*.14), (width*.5, -height*.12), (0, -height/2), (-width*.5, -height*.12), (-width*.46, height*.14)]
    verts = [(x+px, y, z+pz) for px,pz in contour]
    verts += [(x+px*.53, y-depth*.5, z+pz*.53) for px,pz in contour]
    verts += [(x+px*.5, y+depth*.5, z+pz*.5) for px,pz in contour]
    faces = [tuple(range(6,12)), tuple(reversed(range(12,18)))]
    for i in range(6):
        j=(i+1)%6
        faces += [(i,j,j+6,i+6),(j,i,i+12,j+12)]
    return mesh(name, verts, faces, kind)


def band(name, z, radius, height, kind='gold', x=0, y=0):
    return tube(name, [(x,y,z-height/2),(x,y,z-height*.34),(x,y,z+height*.34),(x,y,z+height/2)],
                [radius*.94,radius,radius,radius*.94], kind, sides=20)


def grip(name, zmin, zmax, radius=.022, x=0, y=0):
    shaft = tube(name, [(x,y,zmin),(x,y,(zmin+zmax)/2),(x,y,zmax)], [radius*.93,radius,radius*.93], 'leather', sides=16)
    turns = max(3, (zmax-zmin)/.031)
    steps = int(turns*18)
    points = []
    for i in range(steps+1):
        t=i/steps
        angle=t*turns*math.tau
        points.append((x+math.cos(angle)*radius,y+math.sin(angle)*radius,zmin+t*(zmax-zmin)))
    tube(name+'_LeatherSeam', points, .0015, 'leather', sides=5)
    return shaft


def bounds(objects):
    coords = [obj.matrix_world @ v.co for obj in objects for v in obj.data.vertices]
    low = [min(v[i] for v in coords) for i in range(3)]
    high = [max(v[i] for v in coords) for i in range(3)]
    return low, high


def triangulate(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('Portable triangles','TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=mod.name)


def inspect_mesh(obj):
    data=obj.data
    data.calc_loop_triangles()
    bm=bmesh.new()
    bm.from_mesh(data)
    result={'vertices':len(data.vertices),'triangles':len(data.loop_triangles),
            'boundaryEdges':sum(e.is_boundary for e in bm.edges),
            'nonManifoldEdges':sum(not e.is_manifold for e in bm.edges),
            'degenerateFaces':sum(f.calc_area()<1e-12 for f in bm.faces),
            'materials':len(data.materials),'vertexColors':bool(data.color_attributes),
            'finite':all(math.isfinite(c) for v in data.vertices for c in v.co)}
    bm.free()
    return result


def studio(objects, path, width=900, height=1100, yaw=11):
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.render.resolution_x=width
    scene.render.resolution_y=height
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    if scene.world is None:
        scene.world=bpy.data.worlds.new('PreviewWorld')
    scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.26,.26,.26,1)
    scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.65
    scene.view_settings.view_transform='AgX'
    low,high=bounds(objects)
    center=Vector(((low[0]+high[0])/2,0,(low[2]+high[2])/2))
    size=max((high[2]-low[2])*1.17,(high[0]-low[0])*height/width*1.24)
    bpy.ops.object.camera_add(location=(center.x+math.sin(math.radians(yaw))*size*4,-math.cos(math.radians(yaw))*size*4,center.z+size*.16))
    cam=bpy.context.object
    cam.name='PreviewCamera'
    cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
    cam.data.type='ORTHO'
    cam.data.ortho_scale=size
    scene.camera=cam
    for name,pos,power,light_size in [('Key',(-3,-4,5),700,4),('Fill',(3,-1,2),450,3),('Rim',(1,3,4),800,3)]:
        bpy.ops.object.light_add(type='AREA',location=(center.x+pos[0],pos[1],center.z+pos[2]))
        light=bpy.context.object
        light.name='Preview'+name
        light.data.energy=power
        light.data.shape='DISK'
        light.data.size=light_size
        light.rotation_euler=(center-light.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,low[2]-.006))
    floor=bpy.context.object
    floor.name='PreviewFloor'
    floor_mat=bpy.data.materials.new('Preview warm gray')
    floor_mat.diffuse_color=(.48,.49,.49,1)
    floor.data.materials.append(floor_mat)
    scene.render.filepath=str(path)
    bpy.ops.render.render(write_still=True)


def build_export(code, module, render=True):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.scene.unit_settings.system='METRIC'
    record=importlib.import_module(module).build(code)
    parts=[o for o in bpy.context.scene.objects if o.type=='MESH']
    if not parts:
        raise ValueError('Empty geometry '+code)
    preserved=[o for o in parts if o.get('preserve_lod',False)]
    reducible=[o for o in parts if o not in preserved]
    if not reducible:
        raise ValueError('No reducible geometry '+code)
    # Keep the editable construction separately, then combine only the interchange meshes.
    folder=OUT/code
    folder.mkdir(parents=True,exist_ok=True)
    root=bpy.data.objects.new(code,None)
    bpy.context.scene.collection.objects.link(root)
    for obj in parts:
        obj.parent=root
    markers=[]
    for name,pos in record.get('markers',{}).items():
        marker=bpy.data.objects.new(name,None)
        bpy.context.scene.collection.objects.link(marker)
        marker.parent=root
        marker.location=pos
        markers.append(marker)
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/(code+'.blend')))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in reducible:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=reducible[0]
    bpy.ops.object.join()
    lod0=bpy.context.object
    lod0.name=code+'_LOD0'
    # Joining keeps the active object's origin: bake it back to the explicit grip frame.
    bpy.context.scene.cursor.location=(0,0,0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    triangulate(lod0)
    lods=[lod0]
    for level,ratio in ((1,.52),(2,.24)):
        obj=lod0.copy()
        obj.data=lod0.data.copy()
        bpy.context.scene.collection.objects.link(obj)
        obj.name=code+'_LOD'+str(level)
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active=obj
        mod=obj.modifiers.new('Silhouette LOD','DECIMATE')
        mod.ratio=ratio
        mod.use_collapse_triangulate=True
        bpy.ops.object.modifier_apply(modifier=mod.name)
        triangulate(obj)
        lods.append(obj)
    # Tiny closed parts such as a bowstring must retain volume at every LOD.
    # Global decimation can reduce them to coincident opposite triangles,
    # which interchange importers correctly discard as duplicate geometry.
    preserved_names=[obj.name for obj in preserved]
    for lod in lods:
        if not preserved:
            break
        bpy.ops.object.select_all(action='DESELECT')
        lod.select_set(True)
        for source in preserved:
            copy=source.copy()
            copy.data=source.data.copy()
            bpy.context.scene.collection.objects.link(copy)
            copy.matrix_world=source.matrix_world.copy()
            copy.select_set(True)
        bpy.context.view_layer.objects.active=lod
        bpy.ops.object.join()
        triangulate(lod)
    for source in preserved:
        bpy.data.objects.remove(source,do_unlink=True)
    low,high=bounds([lod0])
    checks=[inspect_mesh(o) for o in lods]
    if any(not c['finite'] or not c['vertexColors'] for c in checks):
        raise ValueError('Invalid mesh '+code)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in [root,*markers,*lods]:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.ops.export_scene.fbx(filepath=str(folder/(code+'.fbx')),use_selection=True,
        object_types={'MESH','EMPTY'},apply_scale_options='FBX_SCALE_UNITS',global_scale=1,
        axis_forward='-Z',axis_up='Y',bake_anim=False,use_mesh_modifiers=True,
        mesh_smooth_type='FACE',add_leaf_bones=False,colors_type='LINEAR')
    for obj in lods[1:]:
        obj.select_set(False)
        obj.hide_render=True
    bpy.ops.export_scene.gltf(filepath=str(folder/(code+'.glb')),export_format='GLB',
        use_selection=True,export_yup=True,export_apply=True,export_animations=False,
        export_cameras=False,export_lights=False,export_materials='EXPORT')
    record.update({'code':code,'units':'meters','pivot':'primary grip center',
        'blenderAxes':{'up':'+Z','front':'-Y'},'unityAxes':{'up':'+Y','front':'+Z'},
        'boundsBlender':{'min':low,'max':high},'dimensionsBlender':[high[i]-low[i] for i in range(3)],
        'lods':checks,'surfaceMaterials':sorted({m.name for m in lod0.data.materials}),
        'lodPreservedParts':preserved_names,
        'formats':['blend','fbx','glb'],'paint':'Authored vertex pigments; five shared surface materials; no textures required'})
    record['sha256']={ext:hashlib.sha256((folder/(code+'.'+ext)).read_bytes()).hexdigest() for ext in record['formats']}
    (folder/(code+'.json')).write_text(json.dumps(record,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')
    if render:
        studio([lod0],folder/(code+'-preview.png'))
    print('WEAPON_EXPORTED '+json.dumps({'code':code,'triangles':[c['triangles'] for c in checks],'dimensions':record['dimensionsBlender']}),flush=True)
    return record


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--codes',nargs='+',required=True)
    parser.add_argument('--no-render',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    for code in args.codes:
        module='swords' if code.startswith('E') else 'axes' if code.startswith('H') else 'support_weapons'
        build_export(code,module,not args.no_render)


if __name__=='__main__':
    sys.path.insert(0,str(Path(__file__).resolve().parent))
    # Modules import weapon_common: keep one module instance when invoked as a script.
    sys.modules['weapon_common']=sys.modules[__name__]
    main()
