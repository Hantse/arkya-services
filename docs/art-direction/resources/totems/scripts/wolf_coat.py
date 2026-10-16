"""Wolf-specific painted mantle, facial mask and brush on the shared UV atlas."""


def paint_wolf(nodes, links, original, luminance, painted, palette, color):
    def node(kind):
        return nodes.new(kind)

    def math(operation, a, b):
        result = node('ShaderNodeMath')
        result.operation = operation
        for index, value in enumerate((a, b)):
            if isinstance(value, (int, float)):
                result.inputs[index].default_value = value
            else:
                links.new(value, result.inputs[index])
        return result.outputs[0]

    def smooth(a, b, value):
        t = math('DIVIDE', math('SUBTRACT', value, a), b-a)
        clamp = node('ShaderNodeClamp')
        links.new(t, clamp.inputs[0])
        t = clamp.outputs[0]
        return math('MULTIPLY', math('MULTIPLY', t, t), math('SUBTRACT', 3, math('MULTIPLY', 2, t)))

    def inverse(value):
        return math('SUBTRACT', 1, value)

    def mul(*values):
        result = values[0]
        for value in values[1:]:
            result = math('MULTIPLY', result, value)
        return result

    def tint(base, index, amount):
        mix = node('ShaderNodeMixRGB')
        mix.blend_type = 'MIX'
        mix.use_clamp = True
        links.new(amount, mix.inputs[0])
        links.new(base, mix.inputs[1])
        mix.inputs[2].default_value = color(palette[index])
        return mix.outputs[0]

    attribute = node('ShaderNodeAttribute')
    attribute.attribute_name = 'SourcePosition'
    xyz = node('ShaderNodeSeparateXYZ')
    links.new(attribute.outputs['Vector'], xyz.inputs[0])
    x, y, z = (xyz.outputs[key] for key in ('X', 'Y', 'Z'))
    side = math('ABSOLUTE', y, 0)
    # Source-space coordinates stay stable as the sculpted head and tail evolve.
    legs = inverse(smooth(.25, .31, z))
    painted = tint(painted, 3, legs)
    tail = inverse(smooth(-.20, -.145, x))
    painted = tint(painted, 2, mul(tail, .82))

    torso = mul(smooth(-.17, -.10, x), inverse(smooth(.14, .22, x)))
    saddle = mul(torso, smooth(.265, .355, z))
    painted = tint(painted, 1, mul(saddle, .78))
    shoulder = mul(smooth(.08, .15, x), inverse(smooth(.22, .27, x)), smooth(.27, .40, z))
    painted = tint(painted, 8, mul(shoulder, .28))

    # Replace the tiny fox eyebrow instead of leaving it visible through a tint.
    brow = mul(smooth(.23, .245, x), inverse(smooth(.298, .315, x)),
               smooth(.512, .526, z), inverse(smooth(.548, .558, z)),
               smooth(.022, .038, side))
    painted = tint(painted, 3, brow)
    crown = mul(smooth(.18, .23, x), inverse(smooth(.31, .36, x)),
                smooth(.49, .54, z), inverse(smooth(.555, .61, z)))
    painted = tint(painted, 2, mul(crown, .55))
    # Keep the original pupil, nose and painted highlights intact.
    cream = mul(smooth(.42, .8, luminance), inverse(tail), inverse(smooth(.46, .50, z)))
    painted = tint(painted, 6, mul(cream, .35))
    return painted
