"""Paint the wolf's own atlas while retaining the authored fur and facial detail."""


def paint_wolf(nodes, links, texture, palette, color):
    def ramp(source, stops):
        result = nodes.new('ShaderNodeValToRGB')
        result.color_ramp.interpolation = 'EASE'
        for element in list(result.color_ramp.elements)[1:]:
            result.color_ramp.elements.remove(element)
        for index, (position, tint) in enumerate(stops):
            element = result.color_ramp.elements[0] if index == 0 else result.color_ramp.elements.new(position)
            element.position = position
            element.color = color(tint)
        links.new(source, result.inputs[0])
        return result.outputs['Color']

    luminance = nodes.new('ShaderNodeRGBToBW')
    links.new(texture, luminance.inputs[0])
    fur = ramp(luminance.outputs[0], [
        (0.0, palette[9]), (.012, palette[0]), (.025, palette[1]),
        (.055, palette[2]), (.10, palette[3]), (.22, palette[4]),
        (.43, palette[5]), (.63, palette[6]), (1.0, palette[6]),
    ])
    # The separate eye islands keep a dark pupil and a muted amber iris.
    # Replacing the broad white sclera avoids the source's human expression.
    eyes = ramp(luminance.outputs[0], [
        (0, '#101820'), (.04, '#151c23'), (.18, '#705737'),
        (.45, '#a88e59'), (1, '#c5ad73'),
    ])
    mask = nodes.new('ShaderNodeAttribute')
    mask.attribute_name = 'EyeMask'
    mixed = nodes.new('ShaderNodeMixRGB')
    mixed.blend_type = 'MIX'
    mixed.use_clamp = True
    links.new(mask.outputs['Fac'], mixed.inputs[0])
    links.new(fur, mixed.inputs[1])
    links.new(eyes, mixed.inputs[2])
    return mixed.outputs[0]
