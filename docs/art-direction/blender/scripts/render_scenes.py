"""Build and render Arkya's editable Blender art-direction studies."""

import argparse
import importlib.util
import json
import sys
from pathlib import Path

import bpy

sys.dont_write_bytecode = True


def arguments():
    parser = argparse.ArgumentParser()
    parser.add_argument("--scene", choices=("ether", "ancients"), required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--reuse", help="Load a saved study instead of rebuilding its geometry")
    parser.add_argument("--draft", action="store_true")
    parser.add_argument("--draft-camera", choices=("main", "overview", "path"), default="main")
    parser.add_argument("--cpu", action="store_true")
    parser.add_argument("--samples", type=int, default=128)
    parser.add_argument("--width", type=int, default=1800)
    parser.add_argument("--height", type=int, default=1200)
    return parser.parse_args(sys.argv[sys.argv.index("--") + 1:])


def configure_render(scene, args):
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 24 if args.draft else args.samples
    scene.cycles.use_denoising = True
    scene.cycles.adaptive_threshold = 0.035 if args.draft else 0.015
    scene.cycles.max_bounces = 8
    scene.cycles.diffuse_bounces = 3
    scene.cycles.glossy_bounces = 4
    scene.cycles.transmission_bounces = 6
    scene.cycles.transparent_max_bounces = 8
    scene.cycles.sample_clamp_indirect = 5.0
    devices = []
    scene.cycles.device = "CPU"
    if not args.cpu:
        prefs = bpy.context.preferences.addons["cycles"].preferences
        for backend in ("OPTIX", "CUDA"):
            try:
                prefs.compute_device_type = backend
                prefs.refresh_devices()
                available = [d for d in prefs.devices if d.type == backend]
                if not available:
                    continue
                for device in prefs.devices:
                    device.use = device.type == backend
                devices = [d.name for d in available]
                scene.cycles.device = "GPU"
                break
            except Exception as error:
                print(f"GPU backend {backend}: {error}", flush=True)
    scene.render.resolution_x = 900 if args.draft else args.width
    scene.render.resolution_y = 600 if args.draft else args.height
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    scene.render.use_persistent_data = True
    return devices or ["CPU"]


def main():
    args = arguments()
    destination = Path(args.output).resolve()
    destination.mkdir(parents=True, exist_ok=True)
    temporary = Path("D:/Tools/BlenderPortable/render-temp")
    temporary.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.filepaths.temporary_directory = str(temporary)
    bpy.context.preferences.filepaths.save_version = 0

    module_name, stem = {
        "ether": ("build_cites_ether", "02-cites-ether"),
        "ancients": ("build_terres_anciens", "03-terres-anciens"),
    }[args.scene]
    source = Path(__file__).resolve().parent / f"{module_name}.py"
    if args.reuse:
        bpy.ops.wm.open_mainfile(filepath=str(Path(args.reuse).resolve()))
        notes = json.loads(bpy.context.scene.get("arkya_study_notes", "{}"))
    else:
        module_spec = importlib.util.spec_from_file_location(module_name, source)
        module = importlib.util.module_from_spec(module_spec)
        module_spec.loader.exec_module(module)
        print(f"Building {module_name}", flush=True)
        notes = module.build_scene()
        bpy.context.scene["arkya_study_notes"] = json.dumps(notes, default=str)
    scene = bpy.context.scene
    devices = configure_render(scene, args)
    main_camera = bpy.data.objects.get("Camera_Main")
    overview_camera = bpy.data.objects.get("Camera_Overview")
    path_camera = bpy.data.objects.get("Camera_Path")
    if main_camera is None or main_camera.type != "CAMERA":
        raise RuntimeError("Scene must provide Camera_Main")
    if overview_camera is None or overview_camera.type != "CAMERA":
        raise RuntimeError("Scene must provide Camera_Overview")
    scene.camera = main_camera
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == "VIEW_3D":
                area.spaces.active.region_3d.view_perspective = "CAMERA"
                area.spaces.active.clip_end = 500.0

    summary = {
        "blender_version": bpy.app.version_string,
        "scene": args.scene,
        "engine": scene.render.engine,
        "devices": devices,
        "samples": scene.cycles.samples,
        "resolution": [scene.render.resolution_x, scene.render.resolution_y],
        "objects": len(scene.objects),
        "mesh_objects": sum(o.type == "MESH" for o in scene.objects),
        "unique_meshes": len(bpy.data.meshes),
        "materials": len(bpy.data.materials),
        "cameras": [camera.name for camera in (main_camera, overview_camera, path_camera) if camera],
        "source": source.name,
        "notes": notes,
        "external_images": [im.filepath for im in bpy.data.images if im.source == "FILE"],
    }
    if args.draft:
        selected_camera = {"main": main_camera, "overview": overview_camera, "path": path_camera}[args.draft_camera]
        if selected_camera is None:
            raise RuntimeError(f"Missing draft camera: {args.draft_camera}")
        scene.camera = selected_camera
        suffix = "draft" if args.draft_camera == "main" else f"draft-{args.draft_camera}"
        scene.render.filepath = str(destination / f"{stem}-{suffix}.png")
        bpy.ops.wm.save_as_mainfile(filepath=str(destination / f"{stem}-draft.blend"), compress=True)
        bpy.ops.render.render(write_still=True)
    else:
        scene.render.filepath = str(destination / f"{stem}-render.png")
        blend_file = destination / f"{stem}.blend"
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_file), compress=True)
        bpy.ops.render.render(write_still=True)
        scene.camera = overview_camera
        scene.render.filepath = str(destination / f"{stem}-overview.png")
        bpy.ops.render.render(write_still=True)
        if path_camera:
            scene.camera = path_camera
            scene.render.filepath = str(destination / f"{stem}-path.png")
            bpy.ops.render.render(write_still=True)
        scene.camera = main_camera
        scene.render.filepath = str(destination / f"{stem}-render.png")
        bpy.ops.wm.save_as_mainfile(filepath=str(blend_file), compress=True)
    with open(destination / f"{stem}-{'draft' if args.draft else 'render'}-info.json", "w", encoding="utf-8") as handle:
        json.dump(summary, handle, ensure_ascii=False, indent=2, default=str)
    print("ARKYA_RENDER_COMPLETE " + json.dumps(summary, default=str), flush=True)


if __name__ == "__main__":
    main()
