"""Thin, surface-bound spirit inlays with skin weights sampled from the coat."""
import math
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


class CoatSurface:
    def __init__(self, coat):
        self.coat = coat
        self.points = [v.co.copy() for v in coat.data.vertices]
        self.bvh = BVHTree.FromPolygons(self.points, [list(p.vertices) for p in coat.data.polygons])
        self.tree = KDTree(len(self.points))
        for i, p in enumerate(self.points):
            self.tree.insert(p, i)
        self.tree.balance()
        self.names = {g.index: g.name for g in coat.vertex_groups}

    def weights(self, point, unused=None):
        # Weighted nearest samples avoid a sudden skinning seam across an inlay.
        weights = {}
        for _, index, distance in self.tree.find_n(Vector(point), 3):
            factor = 1 / max(distance, .001) ** 3
            for group in self.coat.data.vertices[index].groups:
                name = self.names[group.group]
                weights[name] = weights.get(name, 0) + group.weight * factor
        weights = dict(sorted(weights.items(), key=lambda item: -item[1])[:4])
        total = sum(weights.values())
        return {name: weight / total for name, weight in weights.items()}

    def project(self, x, z, side):
        hit, normal, _, _ = self.bvh.ray_cast(Vector((x, side * 5, z)), Vector((0, -side, 0)))
        return (hit + normal * .0012, normal) if hit is not None else None


def curve(anchors, samples=64):
    """Catmull-Rom interpolation; separate controls author the flow silhouette."""
    anchors = [Vector(p) for p in anchors]
    result = []
    for j in range(samples):
        t = j / (samples - 1) * (len(anchors) - 1)
        index = min(len(anchors) - 2, int(t))
        t -= index
        a, b, c, d = (anchors[max(0, index - 1)], anchors[index],
                      anchors[index + 1], anchors[min(len(anchors) - 1, index + 2)])
        result.append(.5 * ((2*b) + (-a+c)*t + (2*a-5*b+4*c-d)*t*t + (-a+3*b-3*c+d)*t*t*t))
    return result


def make_markings(coat, rig, anatomy, species):
    """Anatomy is normalized world-space: hip, shoulder and neck 3D points."""
    surface = CoatSurface(coat)
    hip, shoulder, neck = [Vector(anatomy[key]) for key in ('hip', 'shoulder', 'neck')]
    length = shoulder.x - hip.x
    z = (hip.z + shoulder.z) * .5
    height = max(.1, shoulder.z)
    def p(x, y):
        return (hip.x + length*x, z + height*y)

    # Branching crescent motifs cluster at the shoulder. Their open ends follow
    # the direction of the coat rather than tracing parallel stripes on the ribs.
    designs = [
        [p(.15,.035),p(.38,.015),p(.64,.09),p(.88,.13),p(1.03,.09),p(.98,-.015),p(.81,-.06)],
        [p(.38,.015),p(.47,-.045),p(.66,-.085),p(.76,-.15)],
        [p(.66,.09),p(.70,.18),p(.82,.23),p(.98,.22)],
        [p(.06,.03),p(-.03,.11),p(.02,.18),p(.17,.21)],
    ]
    if species == 'fox':
        designs = [
            [p(.22,.02),p(.44,.055),p(.65,.14),p(.83,.17),p(.98,.12),p(.92,.055)],
            [p(.57,.105),p(.66,.025),p(.79,-.025),p(.89,-.12)],
            [p(.66,.14),p(.64,.23),p(.76,.27)],
            [p(.1,.045),p(.04,.12),p(.15,.17)],
        ]

    vertices, faces, uv_values, paths = [], [], [], []
    for side in (-1, 1):
        for index, design in enumerate(designs):
            projected = [surface.project(q.x, q.y, side) for q in curve(design)]
            projected = [q for q in projected if q is not None]
            if len(projected) < 8:
                continue
            path = [q[0] for q in projected]
            paths.append(path)
            start = len(vertices)
            for i, (point, normal) in enumerate(projected):
                t = i / (len(projected)-1)
                tangent = path[min(i+1,len(path)-1)]-path[max(i-1,0)]
                across = tangent.cross(normal).normalized()
                width = height * (.006 if index == 0 else .0033) * math.sin(math.pi*t)**.8
                vertices.extend((point-across*width, point+across*width))
                uv_values.extend(((t,0),(t,1)))
                if i:
                    a = start + 2*(i-1)
                    faces.append((a,a+1,a+3,a+2))
    # A compact forehead seal supplies an identifying focal point; the eyes keep
    # their painted pupils and highlights instead of becoming luminous discs.
    head=rig.data.bones.get('Head')
    if head:
        center=head.tail_local.z+height*.045
        half=height*.034
        source_positions=coat.data.attributes.get('SourcePosition') if species=='wolf' else None
        if anatomy.get('forehead'):
            center=anatomy['forehead'][2]
            half=height*.028
        elif source_positions:
            forehead=[v.co.z for v,p in zip(coat.data.vertices,source_positions.data)
                      if .235<p.vector.x<.30 and .515<p.vector.z<.545 and abs(p.vector.y)<.022]
            if forehead:
                center=sum(forehead)/len(forehead)
                half=height*.023
        outline=[(0,center+half),(-half*.54,center),(0,center-half),
                 (half*.54,center),(0,center+half)]
        projected=[]
        for q in curve(outline,48):
            hit,normal,_,_=surface.bvh.ray_cast(Vector((5,q.x,q.y)),Vector((-1,0,0)))
            if hit is not None:
                projected.append((hit+normal*.0013,normal))
        start=len(vertices)
        for i,(point,normal) in enumerate(projected):
            a=projected[max(0,i-1)][0]
            b=projected[min(len(projected)-1,i+1)][0]
            across=(b-a).cross(normal).normalized()
            vertices.extend((point-across*height*.0025,point+across*height*.0025))
            uv_values.extend(((i/(len(projected)-1),0),(i/(len(projected)-1),1)))
            if i:
                a=start+2*(i-1)
                faces.append((a,a+1,a+3,a+2))
    data = bpy.data.meshes.new('Spirit inlays')
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new('TotemMagic',data)
    bpy.context.collection.objects.link(obj)
    uv = data.uv_layers.new(name='FlowUV')
    for loop in data.loops:
        uv.data[loop.index].uv = uv_values[loop.vertex_index]
    bind_surface(obj, rig, surface)
    return obj, paths, surface


def bind_surface(obj, rig, surface):
    groups = {bone.name: obj.vertex_groups.new(name=bone.name) for bone in rig.data.bones}
    for vertex in obj.data.vertices:
        for name, weight in surface.weights(vertex.co).items():
            if name in groups:
                groups[name].add([vertex.index], weight, 'REPLACE')
    obj.parent = rig
    modifier = obj.modifiers.new('Coat skin', 'ARMATURE')
    modifier.object = rig


def make_sparks(rig, paths, size, root_name):
    paths = [paths[i] for i in (0,1,2,4,5,6) if i < len(paths)]
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.object.mode_set(mode='EDIT')
    for i in range(len(paths)):
        bone=rig.data.edit_bones.new(f'Flow{i+1}')
        bone.head=(0,0,0)
        bone.tail=(0,0,.01)
        bone.parent=rig.data.edit_bones[root_name]
    bpy.ops.object.mode_set(mode='OBJECT')
    objects=[]
    for i in range(len(paths)):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=8,ring_count=6,radius=size)
        obj=bpy.context.object
        obj.vertex_groups.new(name=f'Flow{i+1}').add(list(range(len(obj.data.vertices))),1,'REPLACE')
        for polygon in obj.data.polygons:
            polygon.use_smooth=True
        objects.append(obj)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join()
    obj=objects[0]
    obj.name='TotemSpark'
    obj.parent=rig
    modifier=obj.modifiers.new('Soul light motion','ARMATURE')
    modifier.object=rig
    return obj,paths
