"""Inspect the exported Brise-Seve in Blender without changing its source.

Run after build_monster.py:
  blender -b --python validate_monster.py -- [--fbx-roundtrip] [--render-sheet]

Integrity failures are separate from visible-surface observations. Exact marker
contacts alone do not certify that the skinned paws touch the ground.
"""
from __future__ import annotations

import argparse
import json
import math
import struct
import sys
from pathlib import Path

import bpy
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
EXPECTED = {"Idle": 4., "Walk": 1.2, "Charge": .65, "ChargeAnticipation": 1.,
            "Attack": .9, "Hit": .5, "Death": 1.4}
FEET = ("Fore.L", "Fore.R", "Hind.L", "Hind.R")


def _name(action):
    return action.name.rsplit("|", 1)[-1]


def _coordinates(obj, graph=None):
    evaluated = obj.evaluated_get(graph or bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    values = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", values)
    values = values.reshape((-1, 3))
    matrix = np.array(evaluated.matrix_world, dtype=np.float64)
    values = values @ matrix[:3, :3].T + matrix[:3, 3]
    evaluated.to_mesh_clear()
    return values


def _rest_positions(obj):
    values = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get("co", values)
    matrix = np.array(obj.matrix_world, dtype=np.float64)
    return values.reshape((-1, 3)) @ matrix[:3, :3].T + matrix[:3, 3]


def _find_asset():
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    if len(rigs) != 1:
        raise ValueError(f"Expected one armature, found {[obj.name for obj in rigs]}")
    rig = rigs[0]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"
              and any(modifier.type == "ARMATURE" and modifier.object == rig for modifier in obj.modifiers)]
    lod0 = next((obj for obj in meshes if "LOD0" in obj.name), None)
    if not lod0:
        raise ValueError(f"Missing skinned LOD0: {[obj.name for obj in meshes]}")
    return rig, meshes, lod0


def _geometry(rig, meshes, lod0):
    errors, observations, results = [], [], []
    for obj in meshes:
        obj.data.calc_loop_triangles()
        positions = _rest_positions(obj)
        groups = {group.index: group.name for group in obj.vertex_groups}
        max_weights = 0
        max_sum_error = 0.
        unweighted = 0
        invalid_weights = 0
        for vertex in obj.data.vertices:
            weights = [group.weight for group in vertex.groups
                       if group.weight > 1e-7 and groups[group.group] in rig.data.bones]
            max_weights = max(max_weights, len(weights))
            max_sum_error = max(max_sum_error, abs(sum(weights) - 1.))
            unweighted += not weights
            invalid_weights += any(not math.isfinite(weight) or weight < 0 or weight > 1.00001 for weight in weights)
        uv_layer = obj.data.uv_layers.active
        uvs_finite = False
        uv_bounds = None
        if uv_layer:
            values = np.empty(len(uv_layer.data) * 2, dtype=np.float64)
            uv_layer.data.foreach_get("uv", values)
            values = values.reshape((-1, 2))
            uvs_finite = bool(np.isfinite(values).all())
            uv_bounds = [values.min(axis=0).tolist(), values.max(axis=0).tolist()]
        record = dict(name=obj.name, vertices=len(obj.data.vertices), triangles=len(obj.data.loop_triangles),
                      materials=[material.name if material else None for material in obj.data.materials],
                      bounds_min_m=positions.min(axis=0).tolist(), bounds_max_m=positions.max(axis=0).tolist(),
                      dimensions_m=np.ptp(positions, axis=0).tolist(), finite_vertices=bool(np.isfinite(positions).all()),
                      uv_layers=len(obj.data.uv_layers), finite_uvs=uvs_finite, uv_bounds=uv_bounds,
                      max_influences=max_weights, max_weight_sum_error=max_sum_error,
                      unweighted_vertices=unweighted, invalid_weight_vertices=invalid_weights)
        results.append(record)
        if not record["finite_vertices"] or not uvs_finite:
            errors.append(f"{obj.name}: missing UVs or nonfinite geometry/UVs")
        if max_weights > 4 or max_sum_error > .0001 or unweighted or invalid_weights:
            errors.append(f"{obj.name}: skin integrity failed ({max_weights} influences; sum error {max_sum_error:g})")
        if len(obj.data.materials) > 4:
            observations.append(f"{obj.name}: {len(obj.data.materials)} material slots")
    rest = _rest_positions(lod0)
    groups = {group.index: group.name for group in lod0.vertex_groups}
    sole_indices = {}
    for foot in FEET:
        kind, side = foot.split(".")
        allowed = {f"{kind}Paw.{side}", f"{kind}Toe.{side}"}
        candidates = [vertex.index for vertex in lod0.data.vertices
                      if rest[vertex.index, 2] < .26
                      and sum(group.weight for group in vertex.groups if groups[group.group] in allowed) > .8]
        if candidates:
            minimum = rest[candidates, 2].min()
            candidates = [index for index in candidates if rest[index, 2] <= minimum + .025]
        sole_indices[foot] = np.array(candidates, dtype=np.int32)
        if len(candidates) < 3:
            errors.append(f"{foot}: could not identify a meaningful skinned sole region")
    return results, sole_indices, errors, observations


def _motion(rig, lod0, sole_indices, source_report):
    scene = bpy.context.scene
    fps = scene.render.fps / scene.render.fps_base
    rig.animation_data_create()
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    actions = {_name(action): action for action in bpy.data.actions if _name(action) in EXPECTED}
    errors = [f"Missing action {name}" for name in EXPECTED if name not in actions]
    observations, reports = [], []
    traces = {clip["name"]: clip for clip in source_report.get("clips", [])}
    signature_indices = np.linspace(0, len(lod0.data.vertices) - 1, min(500, len(lod0.data.vertices)), dtype=np.int32)
    rest = _rest_positions(lod0)
    edge_indices = np.array([list(edge.vertices) for edge in lod0.data.edges], dtype=np.int32)
    edge_lengths = np.linalg.norm(rest[edge_indices[:, 0]] - rest[edge_indices[:, 1]], axis=1)
    valid_edges = edge_lengths > 1e-6
    edge_indices, edge_lengths = edge_indices[valid_edges], edge_lengths[valid_edges]
    for name in EXPECTED:
        if name not in actions:
            continue
        action = actions[name]
        rig.animation_data.action = action
        if len(action.slots) == 1:
            rig.animation_data.action_slot = action.slots[0]
        start, end = map(float, action.frame_range)
        duration = (end - start) / fps
        trace = traces.get(name, {})
        speed = float(trace.get("expected_speed_mps", .70 if name == "Walk" else 2.8 if name == "Charge" else 0.))
        frame_count = max(1, round(end - start))
        minimum, maximum = np.full(3, np.inf), np.full(3, -np.inf)
        finite = True
        root_translation = 0.
        first_signature = None
        displacement = 0.
        max_edge_stretch = 1.
        min_edge_ratio = 1.
        worst_edge = None
        max_edges_over_two = 0
        max_edges_over_three = 0
        max_p99_stretch = 1.
        loop_displacement = None
        prior = {}
        feet = {foot: dict(sample_count=len(indices), contact_samples=0, min_contact_surface_z_m=None,
                           max_contact_surface_z_m=None, max_planted_surface_slip_per_frame_m=0.,
                           max_marker_height_error_m=0.) for foot, indices in sole_indices.items()}
        pose_samples = []
        for index in range(frame_count + 1):
            frame = start + (end - start) * index / frame_count
            scene.frame_set(int(frame), subframe=frame % 1)
            positions = _coordinates(lod0)
            finite &= bool(np.isfinite(positions).all())
            minimum = np.minimum(minimum, positions.min(axis=0))
            maximum = np.maximum(maximum, positions.max(axis=0))
            stretched_lengths = np.linalg.norm(positions[edge_indices[:, 0]] - positions[edge_indices[:, 1]], axis=1)
            ratios = stretched_lengths / edge_lengths
            min_edge_ratio = min(min_edge_ratio, float(ratios.min()))
            max_edges_over_two = max(max_edges_over_two, int((ratios > 2).sum()))
            max_edges_over_three = max(max_edges_over_three, int((ratios > 3).sum()))
            max_p99_stretch = max(max_p99_stretch, float(np.quantile(ratios, .99)))
            if ratios.max() > max_edge_stretch:
                maximum_index = int(ratios.argmax())
                max_edge_stretch = float(ratios[maximum_index])
                pair = edge_indices[maximum_index]
                worst_edge = dict(frame=frame, vertices=pair.tolist(), rest_positions_m=rest[pair].tolist(),
                                  evaluated_positions_m=positions[pair].tolist(), rest_length_m=float(edge_lengths[maximum_index]),
                                  evaluated_length_m=float(stretched_lengths[maximum_index]))
            signature = positions[signature_indices]
            if first_signature is None:
                first_signature = signature.copy()
            displacement = max(displacement, float(np.linalg.norm(signature - first_signature, axis=1).max()))
            if index == frame_count:
                loop_displacement = float(np.linalg.norm(signature - first_signature, axis=1).max())
            root = rig.pose.bones.get("Root")
            if root:
                root_translation = max(root_translation, (rig.matrix_world @ root.head).length)
            trace_frames = trace.get("frames", [])
            reference = trace_frames[round(index / frame_count * (len(trace_frames) - 1))] if trace_frames else None
            time = duration * index / frame_count
            for foot, indices in sole_indices.items():
                if not len(indices):
                    continue
                contact = reference["feet"][foot]["contact"] if reference else name == "Idle"
                points = positions[indices]
                center = points.mean(axis=0)
                virtual = center + np.array((0, -speed * time, 0))
                if contact:
                    record = feet[foot]
                    height = float(points[:, 2].min())
                    record["contact_samples"] += 1
                    record["min_contact_surface_z_m"] = height if record["min_contact_surface_z_m"] is None else min(record["min_contact_surface_z_m"], height)
                    record["max_contact_surface_z_m"] = height if record["max_contact_surface_z_m"] is None else max(record["max_contact_surface_z_m"], height)
                    if foot in prior and prior[foot][0]:
                        record["max_planted_surface_slip_per_frame_m"] = max(record["max_planted_surface_slip_per_frame_m"], float(np.linalg.norm(virtual - prior[foot][1])))
                    marker = rig.pose.bones.get("Contact." + foot)
                    if marker:
                        record["max_marker_height_error_m"] = max(record["max_marker_height_error_m"], abs((rig.matrix_world @ marker.head).z - .02))
                prior[foot] = (contact, virtual)
            if index in {0, frame_count // 4, frame_count // 2, frame_count * 3 // 4, frame_count}:
                pose_samples.append(dict(frame=round(frame, 5), min_surface_z_m=float(positions[:, 2].min()),
                                         body_position_m=list(rig.matrix_world @ rig.pose.bones["Body"].head)))
        is_loop = name in ("Idle", "Walk", "Charge")
        result = dict(name=name, frame_range=[start, end], duration_seconds=duration, fps=fps,
                      evaluated_frames=frame_count + 1, finite_vertices=finite,
                      bounds_min_m=minimum.tolist(), bounds_max_m=maximum.tolist(),
                      max_pose_displacement_m=displacement, loop_pose_error_m=loop_displacement if is_loop else None,
                      max_sampled_edge_stretch=max_edge_stretch,
                      evaluated_edges_per_frame=len(edge_indices), min_evaluated_edge_ratio=min_edge_ratio,
                      max_p99_edge_stretch=max_p99_stretch, max_edges_over_two=max_edges_over_two,
                      max_edges_over_three=max_edges_over_three,
                      max_fraction_edges_over_two=max_edges_over_two / len(edge_indices),
                      max_fraction_edges_over_three=max_edges_over_three / len(edge_indices), worst_edge=worst_edge,
                      max_root_translation_m=root_translation, actual_skin_contacts=feet, representative_poses=pose_samples)
        reports.append(result)
        if not finite or abs(duration - EXPECTED[name]) > 1 / fps + .001 or displacement < .001 or root_translation > .001:
            errors.append(f"{name}: invalid range, static pose, nonfinite skin or moving Root")
        if is_loop and loop_displacement > .003:
            errors.append(f"{name}: evaluated skin loop seam {loop_displacement:.5f}m")
        if minimum[2] < -.02:
            errors.append(f"{name}: actual geometry penetrates the floor by {-minimum[2]:.4f}m")
        if max_edge_stretch > 3:
            errors.append(f"{name}: evaluated skin stretch reaches {max_edge_stretch:.2f}x (limit 3x)")
        elif max_edge_stretch > 2:
            observations.append(f"{name}: localized skin stretch reaches {max_edge_stretch:.2f}x; inspect joint silhouette")
        for foot, record in feet.items():
            if record["min_contact_surface_z_m"] is not None and record["min_contact_surface_z_m"] < -.02:
                observations.append(f"{name}/{foot}: planted skin below floor ({record['min_contact_surface_z_m']:.4f}m)")
            if record["max_contact_surface_z_m"] is not None and record["max_contact_surface_z_m"] > .05:
                observations.append(f"{name}/{foot}: planted skin floats ({record['max_contact_surface_z_m']:.4f}m)")
            if record["max_planted_surface_slip_per_frame_m"] > .005:
                observations.append(f"{name}/{foot}: planted skin slips {record['max_planted_surface_slip_per_frame_m']:.4f}m/frame")
    if "Idle" in actions:
        rig.animation_data.action = actions["Idle"]
        if len(actions["Idle"].slots) == 1:
            rig.animation_data.action_slot = actions["Idle"].slots[0]
        scene.frame_set(round(actions["Idle"].frame_range[0]))
    return reports, errors, observations


def _inspect(source_report):
    rig, meshes, lod0 = _find_asset()
    geometry, soles, errors, observations = _geometry(rig, meshes, lod0)
    motion, motion_errors, motion_observations = _motion(rig, lod0, soles, source_report)
    errors.extend(motion_errors)
    observations.extend(motion_observations)
    return dict(armature=rig.name, bone_count=len(rig.data.bones), meshes=geometry, clips=motion,
                integrity_passed=not errors, integrity_errors=errors, surface_observations=observations)


def _glb(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from("<4sII", data)
    if magic != b"glTF" or version != 2 or length != len(data):
        raise ValueError("Invalid GLB header")
    offset, document = 12, None
    while offset < len(data):
        size, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        if kind == 0x4E4F534A:
            document = json.loads(data[offset:offset + size])
        offset += size
    if document is None:
        raise ValueError("Missing GLB JSON chunk")
    animations = []
    for animation in document.get("animations", []):
        times = [document["accessors"][sampler["input"]] for sampler in animation["samplers"]]
        animations.append(dict(name=animation.get("name"), duration_seconds=max(item.get("max", [0])[0] for item in times), channels=len(animation["channels"])))
    errors = [f"Missing GLB clip {name}" for name in EXPECTED if name not in {animation["name"] for animation in animations}]
    for mesh in document.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            attributes = primitive.get("attributes", {})
            if "JOINTS_0" not in attributes or "WEIGHTS_0" not in attributes:
                errors.append(f"GLB mesh {mesh.get('name')} lacks skin attributes")
            if "JOINTS_1" in attributes:
                errors.append(f"GLB mesh {mesh.get('name')} exceeds four skin influences")
    return dict(bytes=len(data), meshes=len(document.get("meshes", [])), materials=len(document.get("materials", [])),
                skins=len(document.get("skins", [])), animations=animations, integrity_passed=not errors, integrity_errors=errors)


def _render_sheet(path):
    scene = bpy.context.scene
    rig, _, _ = _find_asset()
    if not scene.camera:
        return None
    actions = {_name(action): action for action in bpy.data.actions}
    scene.render.resolution_x, scene.render.resolution_y = 640, 480
    scene.render.resolution_percentage = 100
    if scene.render.engine == "CYCLES":
        scene.cycles.samples = 16
    frames = []
    for name, fraction in (("Idle", .25), ("Walk", .18), ("Charge", .42), ("Death", .95)):
        action = actions[name]
        rig.animation_data.action = action
        if len(action.slots) == 1:
            rig.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        scene.frame_set(round(start + (end - start) * fraction))
        temporary = path.with_name(f"validation-pose-{name.lower()}.png")
        scene.render.filepath = str(temporary)
        bpy.ops.render.render(write_still=True)
        image = bpy.data.images.load(str(temporary), check_existing=False)
        pixels = np.empty(640 * 480 * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        frames.append(pixels.reshape((480, 640, 4)))
        bpy.data.images.remove(image)
    # Blender pixel buffers start at bottom-left; top row is Idle, Walk.
    sheet = np.concatenate((np.concatenate(frames[2:], axis=1), np.concatenate(frames[:2], axis=1)), axis=0)
    image = bpy.data.images.new("BriseSeveMotionReview", width=1280, height=960, alpha=True)
    image.pixels.foreach_set(sheet.reshape(-1))
    image.filepath_raw = str(path)
    image.file_format = "PNG"
    image.save()
    return dict(path=str(path), top_row=["Idle", "Walk"], bottom_row=["Charge", "Death"])


def main(args):
    global ROOT
    if args.asset_root:
        ROOT = args.asset_root.resolve()
    source_report = json.loads((ROOT / "animation-report.json").read_text(encoding="utf-8"))
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / "assets" / "brise-seve.blend"))
    report = dict(schemaVersion=1, scope="DCC geometry, evaluated animation and runtime-format inspection; not gameplay certification",
                  source=_inspect(source_report), glb=_glb(ROOT / "models" / "brise-seve.glb"))
    if args.fbx_roundtrip:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(ROOT / "assets" / "brise-seve.fbx"), use_anim=True)
        report["fbx_roundtrip"] = _inspect(source_report)
    report["integrity_passed"] = all(section["integrity_passed"] for key, section in report.items()
                                      if key in ("source", "glb", "fbx_roundtrip"))
    target = ROOT / "validation-report.json"
    target.write_text(json.dumps(report, indent=2, allow_nan=False), encoding="utf-8")
    if args.render_sheet:
        bpy.ops.wm.open_mainfile(filepath=str(ROOT / "assets" / "brise-seve.blend"))
        report["contact_sheet"] = _render_sheet(ROOT / "renders" / "animation-contact-sheet.png")
        target.write_text(json.dumps(report, indent=2, allow_nan=False), encoding="utf-8")
    summary = {key: {"integrity_passed": value["integrity_passed"], "errors": value["integrity_errors"],
                     "surface_observations": value.get("surface_observations", [])}
               for key, value in report.items() if key in ("source", "glb", "fbx_roundtrip")}
    print("BRISE_SEVE_VALIDATION " + json.dumps(summary))
    if not report["integrity_passed"]:
        raise ValueError(f"Brise-Seve integrity validation failed; see {target}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--fbx-roundtrip", action="store_true")
    parser.add_argument("--render-sheet", action="store_true")
    parser.add_argument("--asset-root", type=Path, help="Inspect an isolated asset copy instead of the delivered source")
    main(parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []))
