"""Render the two delivered editable sources together at their actual relative scale."""
import bpy
from mathutils import Vector
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.render.resolution_x = 1800
scene.render.resolution_y = 1100
scene.render.resolution_percentage = 100
scene.render.image_settings.file_format = 'PNG'
scene.view_settings.view_transform = 'AgX'

for species, position in [('wolf', (-.92, .23, 0)), ('fox', (.82, -.20, 0))]:
    with bpy.data.libraries.load(str(ROOT / 'assets' / f'{species}.blend'), link=False) as (available, selected):
        selected.objects = [name for name in available.objects if name in ('Armature', 'TotemFur', 'TotemMagic', 'TotemSpark')]
    assert len(selected.objects) == 4
    for obj in selected.objects:
        scene.collection.objects.link(obj)
        if obj.type == 'ARMATURE':
            obj.location = position
scene.frame_set(0)

world = bpy.data.worlds.new('Totem comparison world')
scene.world = world
world.use_nodes = True
background = world.node_tree.nodes.get('Background')
background.inputs[0].default_value = (.10, .14, .19, 1)
background.inputs[1].default_value = .48
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -.015))
floor = bpy.context.object
material = bpy.data.materials.new('Ink slate studio')
material.use_nodes = True
bsdf = material.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Base Color'].default_value = (.025, .041, .055, 1)
bsdf.inputs['Roughness'].default_value = .9
floor.data.materials.append(material)

def light(name, position, energy, size, color):
    data = bpy.data.lights.new(name, 'AREA')
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    obj.location = position
    obj.rotation_euler = (Vector((0, 0, .7)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data.energy = energy
    data.shape = 'DISK'
    data.size = size
    data.color = color

light('Warm key', (1, -4, 6), 650, 5, (1, .86, .72))
light('Blue fill', (-4, -1, 3), 290, 4, (.6, .8, 1))
light('Soft rim', (0, 4, 4), 720, 4, (.67, .94, 1))
camera_data = bpy.data.cameras.new('Totems together')
camera = bpy.data.objects.new('Totems together', camera_data)
scene.collection.objects.link(camera)
camera.location = (2.2, -8, 2.3)
camera.rotation_euler = (Vector((-.12, 0, .52)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 3.7
scene.camera = camera
scene.render.filepath = str(ROOT / 'renders' / 'totems-comparison.png')
bpy.ops.render.render(write_still=True)
