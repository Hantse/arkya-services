"""Directional pigment networks for Brise-Seve's Paint vertex-color atlas.

All coordinates are object-space metres, with Z up and -Y forward. Applied
mesh transforms keep the marks aligned when the authoring pieces are joined.
Only albedo and roughness are authored: no lights, normals or AO enter color.
"""

import math

import bpy


_PALETTE_TINTS = {
    'hide': (.865, .725, .555),
    'bark': (.890, .700, .390),
    'moss': (.775, .900, .440),
    'ivory': (.965, .920, .805),
}
# Keep the established pigment hue; individual marks vary by only +/-16%.
_PALETTES = {
    kind: [tuple(channel * shade for channel in tint)
           for shade in ((.92, .98, 1.02, 1.08) if kind == 'ivory' else (.84, .95, 1.05, 1.16))]
    for kind, tint in _PALETTE_TINTS.items()
}

_STROKES = {
    # About 2-6 cm wide and 8-15 cm long, with irregular Voronoi-cell variation.
    'hide': {'rotation': (27, -12, 8), 'scale': (33, 27.5, 7.7), 'warp': .035, 'distance': 'EUCLIDEAN', 'roughness': .84},
    'bark': {'rotation': (-37, 7, -8), 'scale': (49.5, 7.7, 41.25), 'warp': .025, 'distance': 'MANHATTAN', 'roughness': .87},
    'moss': {'rotation': (-29, 15, 9), 'scale': (30.25, 6.05, 38.5), 'warp': .030, 'distance': 'MANHATTAN', 'roughness': .90},
    'ivory': {'rotation': (12, -5, 6), 'scale': (49.5, 41.25, 6.875), 'warp': .018, 'distance': 'EUCLIDEAN', 'roughness': .60},
}

_SMALL_SURFACES = {
    'nose': {'scale': (8, 11, 9), 'range': (.77, 1.08), 'tint': (1.02, .97, .90), 'roughness': .73},
    'dark': {'scale': (5, 5, 7), 'range': (.86, 1.06), 'tint': (1.0, 1.0, 1.0), 'roughness': .88},
    'amber': {'scale': (9, 7, 2.8), 'range': (.62, 1.24), 'tint': (1.12, .94, .64), 'roughness': .30},
    'light': {'scale': (3, 3, 4), 'range': (.94, 1.04), 'tint': (1.0, 1.0, .97), 'roughness': .48},
}


def _node(nodes, kind, name, location):
    result = nodes.new(kind)
    result.name = name
    result.label = name
    result.location = location
    return result


def _ramp(nodes, links, name, source, stops, location):
    node = _node(nodes, 'ShaderNodeValToRGB', name, location)
    node.color_ramp.interpolation = 'LINEAR'
    elements = node.color_ramp.elements
    for index, (position, color) in enumerate(stops):
        element = elements[index] if index < 2 else elements.new(position)
        element.position = position
        element.color = (*color, 1.0)
    links.new(source, node.inputs['Fac'])
    return node.outputs['Color']


def _mix(nodes, links, name, mode, factor, first, second, location):
    node = _node(nodes, 'ShaderNodeMixRGB', name, location)
    node.blend_type = mode
    node.use_clamp = False
    node.inputs[0].default_value = factor
    for socket, value in [(node.inputs[1], first), (node.inputs[2], second)]:
        if isinstance(value, (tuple, list)):
            socket.default_value = (*value[:3], 1.0)
        else:
            links.new(value, socket)
    return node.outputs[0]


def _mapping(nodes, links, name, coordinate, scale, rotation, location):
    node = _node(nodes, 'ShaderNodeMapping', name + ' rotation', location)
    node.vector_type = 'POINT'
    node.inputs['Rotation'].default_value = tuple(math.radians(angle) for angle in rotation)
    links.new(coordinate, node.inputs['Vector'])
    # Rotate before stretching so the long brush axis follows the chosen flow.
    stretched = _node(nodes, 'ShaderNodeVectorMath', name + ' stretch', (location[0] + 180, location[1] - 120))
    stretched.operation = 'MULTIPLY'
    stretched.inputs[1].default_value = scale
    links.new(node.outputs['Vector'], stretched.inputs[0])
    return stretched.outputs['Vector']


def _noise(nodes, links, name, coordinate, scale, detail, location):
    node = _node(nodes, 'ShaderNodeTexNoise', name, location)
    node.noise_dimensions = '3D'
    node.inputs['Scale'].default_value = scale
    node.inputs['Detail'].default_value = detail
    node.inputs['Roughness'].default_value = .65
    links.new(coordinate, node.inputs['Vector'])
    return node


def _directional_pigment(nodes, links, kind, coordinate):
    setting = _STROKES[kind]
    drift = _noise(nodes, links, 'Broad pigment drift', coordinate, 2.1, 1.5, (-1050, -280))
    centered = _node(nodes, 'ShaderNodeVectorMath', 'Centered stroke displacement', (-820, -280))
    centered.operation = 'SUBTRACT'
    centered.inputs[1].default_value = (.5, .5, .5)
    links.new(drift.outputs['Color'], centered.inputs[0])
    amplitude = _node(nodes, 'ShaderNodeVectorMath', 'Irregular stroke curvature', (-620, -280))
    amplitude.operation = 'SCALE'
    amplitude.inputs['Scale'].default_value = setting['warp']
    links.new(centered.outputs['Vector'], amplitude.inputs[0])
    warped = _node(nodes, 'ShaderNodeVectorMath', 'Warped object coordinates', (-420, -280))
    warped.operation = 'ADD'
    links.new(coordinate, warped.inputs[0])
    links.new(amplitude.outputs['Vector'], warped.inputs[1])

    direction = _mapping(nodes, links, 'Brush direction and width', warped.outputs['Vector'],
                         setting['scale'], setting['rotation'], (-180, -280))
    cell = _node(nodes, 'ShaderNodeTexVoronoi', 'Uneven individual brush marks', (30, -180))
    cell.voronoi_dimensions = '3D'
    cell.feature = 'F1'
    cell.distance = setting['distance']
    cell.inputs['Scale'].default_value = 1.0
    cell.inputs['Randomness'].default_value = 1.0
    links.new(direction, cell.inputs['Vector'])

    # A mark keeps one pigment value across its area. Center-to-edge distance
    # gradients would make every painted patch look like an embossed wrinkle.
    random_value = _node(nodes, 'ShaderNodeRGBToBW', 'Flat pigment per brush mark', (260, -180))
    links.new(cell.outputs['Color'], random_value.inputs['Color'])
    pressure = _mix(nodes, links, 'Gentle variation across broad pigment fields', 'MIX', .12,
                    random_value.outputs['Val'], drift.outputs['Fac'], (540, -140))
    palette = _PALETTES[kind]
    pigment = _ramp(nodes, links, 'Warm and cool brush pigments', pressure,
                    list(zip((.20, .40, .60, .80), palette)), (790, -100))

    if kind == 'bark':
        # Sparse, interrupted dark incisions. They follow the warped stroke
        # direction; there is no sine wave or repeated comb of identical ribs.
        bristles = _noise(nodes, links, 'Sparse broken bark grain', direction, 1.25, .5, (260, -500))
        creases = _ramp(nodes, links, 'Interrupted bark fissures', bristles.outputs['Fac'], [
            (.22, (.45, .34, .22)), (.29, (.63, .52, .38)),
            (.35, (1.0, 1.0, 1.0)), (.72, (1.0, 1.0, 1.0)),
        ], (790, -430))
        pigment = _mix(nodes, links, 'Sparse grooves beneath broad paint', 'MULTIPLY', .28,
                       pigment, creases, (1050, -110))
    return pigment, setting['roughness']


def create_material(kind):
    """Create a new bakeable material using the existing linear Paint attribute.

    Read the linked socket on the Principled BSDF Base Color input for an Emit
    atlas bake. This material does not create images or modify source meshes.
    """
    if kind not in _STROKES and kind not in _SMALL_SURFACES:
        raise ValueError(f'Unknown Brise-Seve material kind: {kind!r}')

    material = bpy.data.materials.new(f'BriseSevePaint_{kind}')
    material.use_nodes = True
    material['arkya_paint_kind'] = kind
    material['arkya_paint_coordinates'] = 'Applied object coordinates in metres; Z up, -Y forward'
    material['arkya_paint_bake'] = 'Base Color only; no scene lighting or baked normal dependence'
    nodes, links = material.node_tree.nodes, material.node_tree.links
    nodes.clear()
    attribute = _node(nodes, 'ShaderNodeVertexColor', 'Authored Paint base', (-1300, 140))
    attribute.layer_name = 'Paint'
    coordinates = _node(nodes, 'ShaderNodeTexCoord', 'Object metres', (-1300, -280))
    coordinate = coordinates.outputs['Object']

    if kind in _STROKES:
        pigment, roughness = _directional_pigment(nodes, links, kind, coordinate)
    else:
        setting = _SMALL_SURFACES[kind]
        direction = _mapping(nodes, links, 'Subtle pigment direction', coordinate,
                             setting['scale'], (11, -7, 0), (-1050, -280))
        variation = _noise(nodes, links, 'Subtle pigment variation', direction, 1.0, 1.0, (-820, -280))
        low, high = setting['range']
        pigment = _ramp(nodes, links, 'Preserve small feature clarity', variation.outputs['Fac'], [
            (.24, tuple(channel * low for channel in setting['tint'])),
            (.76, tuple(channel * high for channel in setting['tint'])),
        ], (-570, -220))
        roughness = setting['roughness']

    color = _mix(nodes, links, 'Paint with directional pigment', 'MULTIPLY', 1.0,
                 attribute.outputs['Color'], pigment, (1540, 140))
    shader = _node(nodes, 'ShaderNodeBsdfPrincipled', 'Painted surface', (1770, 140))
    shader.inputs['Roughness'].default_value = roughness
    shader.inputs['Metallic'].default_value = 0.0
    shader.inputs['Specular IOR Level'].default_value = .24 if kind != 'amber' else .40
    links.new(color, shader.inputs['Base Color'])
    output = _node(nodes, 'ShaderNodeOutputMaterial', 'Material Output', (2080, 140))
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    material.diffuse_color = (.35, .30, .21, 1.0)
    material.roughness = roughness
    return material
