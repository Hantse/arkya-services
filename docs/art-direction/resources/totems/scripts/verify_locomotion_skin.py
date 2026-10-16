"""Rebake anatomy in memory and audit the evaluated fur mesh at 120 Hz.

Run with Blender, from any directory:
  blender --background --disable-autoexec --python-exit-code 1 \
    --python verify_locomotion_skin.py -- --species all

The source .blend files are never saved. Reports contain SHA-256 provenance for
the anatomy, rig configuration, locomotion module and this verifier. This checks
Blender skin deformation; FBX/glTF import and Unity playback need separate audits.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix

SCRIPT = Path(__file__).resolve()
ROOT = SCRIPT.parents[1]
sys.path.insert(0, str(SCRIPT.parent))
from totem_locomotion import bake_locomotion


def digest(path):
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(block)
    return checksum.hexdigest()


def input_record(path):
    return {"path": path.relative_to(ROOT).as_posix(), "sha256": digest(path)}


def foot_vertices(mesh_object, limbs, minimum_weight):
    result = {}
    for limb in limbs:
        names = {limb["bones"]["foot"], limb["bones"]["toe"]}
        groups = {group.index for group in mesh_object.vertex_groups if group.name in names}
        indices = [vertex.index for vertex in mesh_object.data.vertices
                   if sum(group.weight for group in vertex.groups if group.group in groups) >= minimum_weight]
        if not indices:
            raise ValueError(f"No foot skin vertices for {limb['id']} at weight {minimum_weight}")
        result[limb["id"]] = indices
    return result


def evaluated_points(mesh_object, depsgraph, meters_per_unit):
    evaluated = mesh_object.evaluated_get(depsgraph)
    mesh = evaluated.to_mesh()
    try:
        if len(mesh.vertices) != len(mesh_object.data.vertices):
            raise ValueError("Evaluated topology differs: foot vertex indices are no longer portable")
        return [(evaluated.matrix_world @ vertex.co) * meters_per_unit for vertex in mesh.vertices]
    finally:
        evaluated.to_mesh_clear()


def inspect_clip(rig, mesh_object, action, metadata, feet, sample_rate, scale, ground):
    rig.animation_data.action = action
    if action.slots:
        rig.animation_data.action_slot = action.slots[0]
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    start, end = action.frame_range
    duration = (end - start) / fps
    if abs(duration - metadata["duration_seconds"]) > 1e-7:
        raise ValueError(f"{action.name}: action/report duration mismatch")
    steps = math.ceil(duration * sample_rate)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    samples = []
    first_points = None
    minimum = math.inf
    foot_minima = {name: math.inf for name in feet}
    foot_motion = {name: 0.0 for name in feet}
    worst_sample = None
    for step in range(steps + 1):
        time = duration * step / steps
        frame = start + time * fps
        scene.frame_set(math.floor(frame), subframe=frame % 1)
        points = evaluated_points(mesh_object, depsgraph, scale)
        if not all(math.isfinite(coordinate) for point in points for coordinate in point):
            raise ValueError(f"{action.name}: nonfinite skinned vertex at {time}s")
        if first_points is None:
            first_points = points
        lowest_index = min(range(len(points)), key=lambda index: points[index].z)
        lowest = points[lowest_index].z
        if lowest < minimum:
            minimum = lowest
            worst_sample = {"time_seconds": time, "frame": frame, "vertex_index": lowest_index}
        sample = {"time_seconds": time, "frame": frame, "mesh_minimum_z_m": lowest, "feet": {}}
        for name, indices in feet.items():
            value = min(points[index].z for index in indices)
            foot_minima[name] = min(foot_minima[name], value)
            foot_motion[name] = max(foot_motion[name], max((points[index] - first_points[index]).length for index in indices))
            sample["feet"][name] = {"minimum_z_m": value}
        samples.append(sample)
    loop_error = max((first - last).length for first, last in zip(first_points, points))
    penetration = max(0.0, ground - minimum)
    checks = {
        "max_ground_penetration_m": penetration,
        "max_skin_loop_position_error_m": loop_error,
        "per_foot_max_vertex_displacement_m": foot_motion,
        "all_moving_limbs_deform": action.name == "Idle" or all(value > 0.01 for value in foot_motion.values()),
    }
    checks["passed"] = (penetration <= 0.002 and loop_error <= 0.001
                        and checks["all_moving_limbs_deform"] and metadata["checks"]["passed"])
    return {
        "name": action.name, "duration_seconds": duration,
        "sample_rate_hz": steps / duration, "sample_count": len(samples),
        "minimum_z_m": minimum, "per_foot_minimum_z_m": foot_minima,
        "worst_ground_sample": worst_sample, "locomotion_checks": metadata["checks"],
        "checks": checks, "samples": samples,
    }


def verify_species(species, sample_rate, output_directory):
    source = ROOT / "assets" / "anatomy" / f"{species}.blend"
    configuration = source.with_name(f"{species}-rig.json")
    inputs = {
        "anatomy": input_record(source), "rig_configuration": input_record(configuration),
        "locomotion_module": input_record(SCRIPT.with_name("totem_locomotion.py")),
        "verifier": input_record(SCRIPT),
    }
    bpy.ops.wm.open_mainfile(filepath=str(source))
    rig_data = json.loads(configuration.read_text(encoding="utf-8"))
    rig = bpy.data.objects[rig_data["rig"]["name"]]
    mesh_object = bpy.data.objects["TotemFur"]
    identity = Matrix.Identity(4)
    for obj in (rig, mesh_object):
        if max(abs(obj.matrix_world[row][column] - identity[row][column])
               for row in range(4) for column in range(4)) > 1e-6:
            raise ValueError(f"{obj.name}: verifier requires normalized anatomy transforms")
    actions, locomotion = bake_locomotion(rig, species=species, config=rig_data["locomotionConfig"])
    scale = locomotion["meters_per_unit"]
    ground = locomotion["coordinate_system"]["ground_height"] * scale
    feet = foot_vertices(mesh_object, locomotion["limbs"], 0.55)
    report = {
        "schemaVersion": 1, "species": species,
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "blender_version": bpy.app.version_string,
        "inputs": inputs,
        "method": "Rebake the immutable anatomy source in memory, then evaluate actual skinned vertices including interpolated subframes.",
        "scope": "Blender anatomy fur mesh; independent from exported FBX/glTF and Unity import validation.",
        "coordinate_system": {"up": "Z", "forward": "+X", "units": "meters", "ground_height_m": ground},
        "thresholds": {"ground_penetration_m": 0.002, "skin_loop_position_error_m": 0.001,
                       "moving_foot_vertex_displacement_m": 0.01, "foot_or_toe_weight": 0.55},
        "mesh": {"name": mesh_object.name, "vertices": len(mesh_object.data.vertices),
                 "per_foot_vertex_count": {name: len(indices) for name, indices in feet.items()}},
        "clips": [inspect_clip(rig, mesh_object, action, metadata, feet, sample_rate, scale, ground)
                  for action, metadata in zip(actions, locomotion["clips"])],
    }
    report["source_files_unchanged_during_verification"] = all(
        digest(ROOT / item["path"]) == item["sha256"] for item in inputs.values())
    report["passed"] = (report["source_files_unchanged_during_verification"]
                        and all(clip["checks"]["passed"] for clip in report["clips"]))
    output_directory.mkdir(parents=True, exist_ok=True)
    path = output_directory / f"{species}-skin-verification.json"
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print("SKIN_VERIFICATION", json.dumps({"species": species, "passed": report["passed"], "report": str(path),
          "max_penetration_m": max(clip["checks"]["max_ground_penetration_m"] for clip in report["clips"])}), flush=True)
    return report["passed"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--species", choices=("fox", "wolf", "all"), default="all")
    parser.add_argument("--sample-rate", type=int, default=120)
    parser.add_argument("--output-directory", type=Path, default=ROOT)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    if args.sample_rate < 120:
        parser.error("Use at least 120 Hz to cover the 60 Hz bake and its interpolated subframes")
    species = ("fox", "wolf") if args.species == "all" else (args.species,)
    outcomes = [verify_species(name, args.sample_rate, args.output_directory.resolve()) for name in species]
    if not all(outcomes):
        raise RuntimeError("Locomotion skin verification failed; see the emitted reports")


if __name__ == "__main__":
    main()
