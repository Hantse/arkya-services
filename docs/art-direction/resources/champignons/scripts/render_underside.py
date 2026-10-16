"""Low-angle control render: ensure sculpted cap lamellae remain readable."""
from pathlib import Path
import bpy
from mathutils import Vector

ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'assets/amanite-corail/amanite-corail.blend'),use_scripts=False)
scene=bpy.context.scene
camera=scene.camera
camera.location=(.65,-1.5,-.60)
camera.rotation_euler=(Vector((0,0,.22))-camera.location).to_track_quat('-Z','Y').to_euler()
camera.data.ortho_scale=.69
bpy.data.objects['Studio floor | not exported'].hide_render=True
scene.cycles.device='CPU';scene.cycles.samples=16
scene.render.resolution_x=1000;scene.render.resolution_y=1000
scene.render.filepath=str(ROOT/'controle-sous-face.png')
bpy.ops.render.render(write_still=True)
print('UNDERSIDE_CONTROL_READY',flush=True)
