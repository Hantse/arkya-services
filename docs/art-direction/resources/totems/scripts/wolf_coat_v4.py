"""Paint the wolf's own atlas while retaining the authored fur and facial detail."""


def paint_wolf(nodes, links, texture, palette, color, position_attribute="SourcePosition", refined_brow=False, swept_cheeks=False):
    def math(operation, a, b):
        node=nodes.new('ShaderNodeMath')
        node.operation=operation
        for i,value in enumerate((a,b)):
            if isinstance(value,(int,float)): node.inputs[i].default_value=value
            else: links.new(value,node.inputs[i])
        return node.outputs[0]

    def smooth(a,b,source):
        node=nodes.new('ShaderNodeMapRange')
        node.interpolation_type='SMOOTHSTEP'
        node.clamp=True
        links.new(source,node.inputs['Value'])
        node.inputs['From Min'].default_value=a
        node.inputs['From Max'].default_value=b
        return node.outputs[0]

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
    coordinates=nodes.new('ShaderNodeAttribute')
    coordinates.attribute_name=position_attribute
    xyz=nodes.new('ShaderNodeSeparateXYZ')
    links.new(coordinates.outputs['Vector'],xyz.inputs[0])
    x,y,z=xyz.outputs['X'],xyz.outputs['Y'],xyz.outputs['Z']
    side=math('ABSOLUTE',y,0)
    brow=math('MULTIPLY',smooth(.475,.49,x),math('SUBTRACT',1,smooth(.56,.58,x)))
    brow=math('MULTIPLY',brow,smooth(.018,.03,side))
    brow=math('MULTIPLY',brow,math('SUBTRACT',1,smooth(.095,.115,side)))
    brow_height=math('SUBTRACT',z,math('MULTIPLY',math('SUBTRACT',side,.052),.45))
    # Cover the source brow's lower end without changing the separate eye islands.
    brow_floor=(.938,.950) if refined_brow else (.958,.972)
    brow=math('MULTIPLY',brow,smooth(brow_floor[0],brow_floor[1],brow_height))
    brow=math('MULTIPLY',brow,math('SUBTRACT',1,smooth(1.008,1.022,brow_height)))
    # Replace the source's drawn human eyebrow with the adjacent forehead pigment.
    forehead=nodes.new('ShaderNodeMixRGB')
    links.new(brow,forehead.inputs[0])
    links.new(texture,forehead.inputs[1])
    forehead.inputs[2].default_value=color('#5d432b')
    links.new(forehead.outputs[0],luminance.inputs[0])
    body=math('SUBTRACT',1,smooth(.28,.42,x))
    floor=math('MAXIMUM',luminance.outputs[0],.038)
    # Soften the source's dark saddle outline while preserving the facial ink.
    value=math('ADD',luminance.outputs[0],math('MULTIPLY',body,math('SUBTRACT',floor,luminance.outputs[0])))
    fur = ramp(value, [
        (0.0, palette[9]), (.012, palette[0]), (.025, palette[1]),
        (.055, palette[2]), (.10, palette[3]), (.22, palette[4]),
        (.43, palette[5]), (.63, palette[6]), (1.0, palette[6]),
    ])
    jaw=math('MULTIPLY',smooth(.37,.45,x),math('SUBTRACT',1,smooth(.65,.70,x)))
    chin_height=math('ADD',z,math('MULTIPLY',math('SUBTRACT',x,.58),.10))
    jaw=math('MULTIPLY',jaw,math('SUBTRACT',1,smooth(.846,.860,chin_height)))
    jaw=math('MULTIPLY',jaw,math('SUBTRACT',1,smooth(.065,.085,side)))
    softened=nodes.new('ShaderNodeMixRGB')
    links.new(math('MULTIPLY',jaw,.98),softened.inputs[0])
    links.new(fur,softened.inputs[1])
    softened.inputs[2].default_value=color(palette[5])
    fur=softened.outputs[0]
    # Taper cheek pigment along the muzzle instead of ending at a vertical edge.
    muzzle=math('MULTIPLY',smooth(.55,.585,x),math('SUBTRACT',1,smooth(.665,.690,x)))
    muzzle=math('MULTIPLY',muzzle,smooth(.009,.025,side))
    cheek_line=math('ADD',z,math('MULTIPLY',math('SUBTRACT',x,.58),.34))
    muzzle=math('MULTIPLY',muzzle,math('SUBTRACT',1,smooth(.892,.914,cheek_line)))
    muzzle=math('MULTIPLY',muzzle,smooth(.815,.842,z))
    muzzle=math('MULTIPLY',muzzle,smooth(.012,.024,luminance.outputs[0]))
    whisker=nodes.new('ShaderNodeMixRGB')
    links.new(math('MULTIPLY',muzzle,.88),whisker.inputs[0])
    links.new(fur,whisker.inputs[1])
    whisker.inputs[2].default_value=color(palette[5])
    fur=whisker.outputs[0]
    if swept_cheeks:
        # Carry the pale cheek diagonally toward the outer eye and rear ruff.
        cheek=math('MULTIPLY',smooth(.40,.45,x),math('SUBTRACT',1,smooth(.60,.65,x)))
        cheek=math('MULTIPLY',cheek,smooth(.045,.080,side))
        height=math('SUBTRACT',math('ADD',z,math('MULTIPLY',math('SUBTRACT',x,.53),.12)),
                    math('MULTIPLY',math('SUBTRACT',side,.04),.65))
        cheek=math('MULTIPLY',cheek,math('SUBTRACT',1,smooth(.925,.946,height)))
        cheek=math('MULTIPLY',cheek,smooth(.845,.870,z))
        cheek=math('MULTIPLY',cheek,math('SUBTRACT',1,smooth(.965,.990,z)))
        surface=nodes.new('ShaderNodeAttribute');surface.attribute_name='SourcePosition'
        surface_xyz=nodes.new('ShaderNodeSeparateXYZ')
        links.new(surface.outputs['Vector'],surface_xyz.inputs[0])
        surface_height=math('SUBTRACT',math('ADD',surface_xyz.outputs['Z'],
            math('MULTIPLY',math('SUBTRACT',surface_xyz.outputs['X'],.58),.16)),
            math('MULTIPLY',math('ABSOLUTE',surface_xyz.outputs['Y'],0),.28))
        cheek=math('MULTIPLY',cheek,math('SUBTRACT',1,smooth(.662,.683,surface_height)))
        cheek_color=ramp(luminance.outputs[0],[(0,palette[4]),(.18,palette[5]),(.63,palette[6])])
        swept=nodes.new('ShaderNodeMixRGB')
        links.new(math('MULTIPLY',cheek,.92),swept.inputs[0])
        links.new(fur,swept.inputs[1]);links.new(cheek_color,swept.inputs[2])
        fur=swept.outputs[0]
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
