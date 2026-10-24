"""Render review images from the final Brise-Seve source without modifying it.

Blender 4.5: blender -b --threads 4 --python render_review.py
The three views use 1024x768/12 samples; the four-pose sheet uses 640x480/16.
"""
from pathlib import Path
import sys

import bpy
from mathutils import Vector

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))
from validate_monster import _render_sheet


def main():
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "assets" / "brise-seve.blend"))
    scene = bpy.context.scene
    rig = bpy.data.objects["BriseSeveRig"]
    action = bpy.data.actions["Idle"]
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    scene.frame_set(0)
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 1024, 768
    scene.render.resolution_percentage = 100
    camera = scene.camera
    hero_matrix = camera.matrix_world.copy()
    for name, location in (("hero.png", None), ("profile.png", (5, 0, 1.95)),
                           ("rear.png", (3.4, 4.5, 2.6))):
        if location:
            camera.location = location
            camera.rotation_euler = (Vector((0, -.05, .85)) - camera.location).to_track_quat("-Z", "Y").to_euler()
        else:
            camera.matrix_world = hero_matrix
        scene.render.filepath = str(ROOT / "renders" / name)
        bpy.ops.render.render(write_still=True)
        print("REVIEW_RENDER " + name, flush=True)
    camera.matrix_world = hero_matrix
    _render_sheet(ROOT / "renders" / "animation-contact-sheet.png")
    for name in ("idle", "walk", "charge", "death"):
        temporary = ROOT / "renders" / f"validation-pose-{name}.png"
        temporary.unlink(missing_ok=True)
    print("REVIEW_RENDER animation-contact-sheet.png; intermediate pose PNGs removed", flush=True)


if __name__ == "__main__":
    main()
