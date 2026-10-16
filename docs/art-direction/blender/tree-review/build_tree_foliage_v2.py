"""Connected, sculpted broadleaf canopy; mesh-only geometry and glTF vertex colour."""

import math
import random

import bpy
from mathutils import Vector


UP = Vector((0.0, 0.0, 1.0))
PALETTE = ((.024, .071, .043), (.042, .122, .057),
           (.076, .194, .077), (.145, .275, .105),
           (.18, .32, .10), (.24, .38, .12))


def _unit(value):
    value = Vector(value)
    return value.normalized() if value.length > 1e-8 else UP.copy()


def _frame(direction):
    direction = _unit(direction)
    axis = UP if abs(direction.z) < .95 else Vector((0, 1, 0))
    side = axis.cross(direction).normalized()
    return side, direction.cross(side).normalized()


def _curve(start, control, end, steps=5):
    return [start * (1-t)**2 + control * (2*t*(1-t)) + end*t*t
            for t in (i/steps for i in range(steps+1))]


def _sample(path, t):
    value = max(0, min(.999999, t)) * (len(path)-1)
    index = int(value)
    return path[index].lerp(path[index+1], value-index)


def _tube(vertices, faces, path, radius, sides=5):
    start = len(vertices)
    previous = None
    for index, point in enumerate(path):
        direction = _unit(path[min(index+1, len(path)-1)] - path[max(index-1, 0)])
        side, normal = _frame(direction)
        if previous is not None and side.dot(previous) < 0:
            side = -side
            normal = -normal
        previous = side
        t = index/(len(path)-1)
        width = radius * (1-.88*t)
        for ring in range(sides):
            angle = math.tau*ring/sides
            vertices.append(tuple(point + width*(side*math.cos(angle)+normal*math.sin(angle))))
        if index:
            for ring in range(sides):
                a = start+(index-1)*sides+ring
                b = start+(index-1)*sides+(ring+1)%sides
                faces.append((a, b, b+sides, a+sides))
    faces.append(tuple(start+ring for ring in reversed(range(sides))))
    end = start+(len(path)-1)*sides
    faces.append(tuple(end+ring for ring in range(sides)))


def _colour(point, low, high, variation):
    height = max(0, min(1, (point.z-low)/max(.1, high-low)))
    broad = .07*math.sin(point.x*.95+point.y*.7) + .045*math.sin(point.y*1.2-point.z*.8)
    value = max(.05, min(.96, .14+.72*height+broad+variation))
    index = min(len(PALETTE)-2, int(value*(len(PALETTE)-1)))
    alpha = value*(len(PALETTE)-1)-index
    return tuple(PALETTE[index][c]*(1-alpha)+PALETTE[index+1][c]*alpha for c in range(3))


def _leaf(vertices, faces, colours, base, direction, length, width, roll, colour):
    axis = _unit(direction)
    side, normal = _frame(axis)
    side, normal = (side*math.cos(roll)+normal*math.sin(roll),
                    normal*math.cos(roll)-side*math.sin(roll))
    # Ten broad, soft facets retain a small leaf silhouette and a raised central fold.
    outline = ((0, 0), (.16, -.28), (.34, -.49), (.57, -.50), (.81, -.30),
               (1, 0), (.81, .30), (.57, .50), (.34, .49), (.16, .28))
    first = len(vertices)
    for t, edge in outline:
        bow = length*(.10*math.sin(math.pi*t)-.095*t*t)
        point = base+axis*(length*t)+side*(width*edge)+normal*bow
        vertices.append(tuple(point))
        brightness = .91 + .12*t
        colours.append(tuple(min(1, c*brightness) for c in colour)+(1.0,))
    center = base+axis*(length*.46)+normal*(length*.075+width*.10)
    vertices.append(tuple(center))
    colours.append(tuple(min(1, c*1.07) for c in colour)+(1.0,))
    for index in range(10):
        faces.append((first+10, first+index, first+(index+1)%10))


def _material(name, colour, roughness, vertex_colour=False):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*colour, 1)
    material.use_nodes = True
    material.use_backface_culling = False
    shader = material.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value = (*colour, 1)
    shader.inputs['Metallic'].default_value = 0
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Specular IOR Level'].default_value = .23
    if vertex_colour:
        attribute = material.node_tree.nodes.new('ShaderNodeVertexColor')
        attribute.layer_name = 'COLOR_0'
        material.node_tree.links.new(attribute.outputs['Color'], shader.inputs['Base Color'])
    return material


def _mesh_object(name, vertices, faces, material, collection, colours=None):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    if colours is not None:
        attribute = mesh.color_attributes.new(name='COLOR_0', type='BYTE_COLOR', domain='POINT')
        attribute.data.foreach_set('color', [component for colour in colours for component in colour])
        mesh.color_attributes.active_color = attribute
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    mesh.materials.append(material)
    return obj


def build_foliage(anchors):
    """Build real connected sprays; one leaf mesh and one twig mesh per terminal."""
    anchors = list(anchors)
    if not anchors:
        return {'leaf_objects': [], 'twig_objects': [], 'leaf_count': 0}
    collection = bpy.data.collections.new('Hero Tree | layered living canopy')
    bpy.context.scene.collection.children.link(collection)
    leaves_material = _material('Hero Tree | matte jade leaves, vertex colour', (.10, .24, .08), .74, True)
    twig_material = _material('Hero Tree | warm matte young wood', (.115, .052, .022), .87)
    low = min(item['center'][2] for item in anchors)-.55
    high = max(item['center'][2] for item in anchors)+1.15
    sprays = 10
    leaf_objects, twig_objects = [], []
    leaf_count = 0
    for anchor_index, anchor in enumerate(anchors):
        randomizer = random.Random(int(anchor.get('seed', anchor_index))+84721)
        center = Vector(anchor['center'])
        direction = _unit(anchor['direction'])
        # Terminal spacing is intentionally overlapped so the seven scaffold crowns read as large lobes.
        scale = .85+.35*float(anchor.get('scale', 1))
        side, normal = _frame(direction)
        vertices, faces, colours = [], [], []
        twig_vertices, twig_faces = [], []
        main_end = center+direction*(.45*scale)+UP*.11
        main = _curve(center-direction*.055, center+direction*.20, main_end)
        _tube(twig_vertices, twig_faces, main, .026, 6)
        phase = randomizer.uniform(0, math.tau)
        for spray in range(sprays):
            angle = phase+spray*2.399963229728653
            outward = side*math.cos(angle)+normal*math.sin(angle)
            growth = _unit(outward*.86+direction*randomizer.uniform(.05, .42)+UP*.17)
            origin = _sample(main, .07+.87*((spray*.61803398875)%1))
            reach = randomizer.uniform(.52, .74)*scale
            endpoint = origin+growth*reach
            endpoint.z += randomizer.uniform(-.08, .14)
            spray_curve = _curve(origin, origin+growth*(reach*.52)+UP*.075, endpoint)
            _tube(twig_vertices, twig_faces, spray_curve, .012*scale, 5)
            branch_side, branch_normal = _frame(growth)
            for branch in range(4):
                t = .21+.185*branch
                branch_origin = _sample(spray_curve, t)
                sign = -1 if branch%2 else 1
                twig_direction = _unit(growth*.50 + branch_side*(sign*.76) + branch_normal*.10 + UP*randomizer.uniform(-.22, .22))
                twig_length = randomizer.uniform(.23, .35)*scale
                twig_end = branch_origin+twig_direction*twig_length
                twig_end.z -= .035*(branch%2)
                path = _curve(branch_origin, branch_origin+twig_direction*(twig_length*.58)+UP*.025, twig_end, 3)
                _tube(twig_vertices, twig_faces, path, .0055, 5)
                leaf_side, leaf_normal = _frame(twig_direction)
                for blade in range(7):
                    leaf_base = _sample(path, .10+.125*blade)
                    leaf_sign = -1 if blade%2 else 1
                    leaf_direction = _unit(twig_direction*.44+leaf_side*(leaf_sign*.87)+UP*randomizer.uniform(-.18, .32))
                    length = randomizer.uniform(.19, .28)
                    width = randomizer.uniform(.10, .155)
                    colour = _colour(leaf_base, low, high, randomizer.uniform(-.025, .025))
                    _leaf(vertices, faces, colours, leaf_base, leaf_direction, length, width,
                          randomizer.uniform(-.70, .70), colour)
                    leaf_count += 1
            # Tender tip leaves overlap the end of every visible spray.
            for sign in (-1, 1):
                leaf_base = _sample(spray_curve, .94)
                leaf_direction = _unit(growth*.84+branch_side*(sign*.43)+UP*.12)
                colour = _colour(leaf_base, low, high, .025)
                _leaf(vertices, faces, colours, leaf_base, leaf_direction,
                      randomizer.uniform(.20, .27), .115, randomizer.uniform(-.45, .45), colour)
                leaf_count += 1
        leaf_obj = _mesh_object(f'Hero Tree | living canopy {anchor_index+1:02d}', vertices,
                                faces, leaves_material, collection, colours)
        leaf_obj['leaf_count'] = sprays*30
        leaf_obj['design'] = 'Connected folded leaves; cool interior to warm sunlit crown; no solid foliage blobs'
        leaf_objects.append(leaf_obj)
        twig_objects.append(_mesh_object(f'Hero Tree | connected twigs {anchor_index+1:02d}',
                                         twig_vertices, twig_faces, twig_material, collection))
    return {'leaf_objects': leaf_objects, 'twig_objects': twig_objects,
            'leaf_count': leaf_count, 'leaf_triangles': leaf_count*10,
            'anchor_count': len(anchors), 'material_count': 2}
