"""Bake planted, in-place quadruped gaits and export measurable contact traces.

The rig is +X forward / Z up. Limb lengths, rolls and attachment positions come
from its rest bones. Call ``bake_locomotion`` before applying the final object
scale, and pass that final scale as ``config['meters_per_unit']``.

For the anatomy sources, pass the complete ``locomotionConfig`` from the matching
``{species}-rig.json``. Export the returned actions with their own durations and
retain the non-deforming ``Contact.*`` bones in FBX/glTF. They provide portable
sole samples after an importer changes bone axes. Runtime travel speed is
``expected_speed_mps * animation_playback_rate``; these clips are in-place.
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector


@dataclass(frozen=True)
class Limb:
    id: str
    upper: str
    lower: str
    foot: str
    toe: str
    pole_forward: float
    sole_rest: tuple[float, float, float] | None = None

    @property
    def marker(self):
        return f"Contact.{self.id}"


DEFAULT_LIMBS = tuple(
    Limb(f"{kind}.{side}", f"{kind}Upper.{side}", f"{kind}Lower.{side}",
         f"{kind}Foot.{side}", f"{kind}Toe.{side}", -1.0 if kind == "Fore" else 1.0)
    for kind in ("Fore", "Hind") for side in ("L", "R")
)

DEFAULT_BODY = {name.lower(): name for name in ("Root", "Hips", "Spine", "Chest", "Neck", "Head")}


def _phase(time, duration, offset):
    cycle = time / duration + offset
    epoch = math.floor(cycle + 1e-10)
    phase = cycle - epoch
    if abs(phase) < 1e-9:
        phase = 0.0
    return phase, epoch


def _smooth5(value):
    return value * value * value * (10 + value * (-15 + 6 * value))


def foot_trajectory(time, motion, offset, lateral_sign=1.0, hind=False):
    """Return (offset XYZ, contact, step epoch, phase, foot pitch, toe pitch).

    Stance velocity is exactly -speed. Swing matches that velocity and zero
    acceleration at both ends, so the contact does not snap at the loop seam.
    """
    if motion["name"] == "Idle":
        return Vector((0, 0, 0)), True, 0, 0.0, 0.0, 0.0
    phase, epoch = _phase(time, motion["duration"], offset)
    duty = motion["duty"]
    stride = motion["speed"] * motion["duration"] * duty
    if phase < duty:
        x = stride * (0.5 - phase / duty)
        hock_pitch = (-0.25 + 0.70 * _smooth5(phase / duty)) if hind else 0.0
        return Vector((x, 0, 0)), True, epoch, phase, hock_pitch, 0.0
    swing = (phase - duty) / (1 - duty)
    end_velocity = -motion["speed"] * motion["duration"] * (1 - duty)
    x = -stride / 2 + end_velocity * swing + (stride - end_velocity) * _smooth5(swing)
    arc = max(0.0, math.sin(math.pi * swing)) ** 3
    z = motion["clearance"] * arc
    y = lateral_sign * motion["swing_outward"] * arc
    hock_pitch = 0.45 - 0.70 * _smooth5(swing) - 0.85 * arc if hind else -0.20 * arc
    return (Vector((x, y, z)), False, epoch, phase, hock_pitch, -0.13 * arc)


def solve_two_bone(hip, target, upper_length, lower_length, pole):
    """Analytic hinge solution. A reach residual is reported, never hidden."""
    delta = target - hip
    requested = delta.length
    if requested < 1e-9:
        raise ValueError("A leg target coincides with its hip")
    distance = min(upper_length + lower_length - 1e-7,
                   max(abs(upper_length - lower_length) + 1e-7, requested))
    direction = delta / requested
    bend = pole - direction * pole.dot(direction)
    if bend.length < 1e-7:
        bend = Vector((0, 1, 0)) - direction * direction.y
    bend.normalize()
    along = (distance * distance + upper_length * upper_length - lower_length * lower_length) / (2 * distance)
    height = math.sqrt(max(0.0, upper_length * upper_length - along * along))
    knee = hip + direction * along + bend * height
    reachable_target = hip + direction * distance
    return knee, reachable_target, abs(requested - distance)


def _limbs_from_config(config, rig):
    values = config.get("limbs")
    if values:
        return tuple(Limb(**value) if isinstance(value, dict) else value for value in values)
    if "FrontUpper.L" in rig.data.bones:
        return tuple(Limb(limb.id, *(getattr(limb, part).replace("Fore", "Front")
                                    for part in ("upper", "lower", "foot", "toe")),
                          limb.pole_forward) for limb in DEFAULT_LIMBS)
    return DEFAULT_LIMBS


def _ensure_markers(rig, limbs, soles, size):
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for limb in limbs:
        bone = rig.data.edit_bones.get(limb.marker) or rig.data.edit_bones.new(limb.marker)
        bone.head = soles[limb.id]
        bone.tail = soles[limb.id] + Vector((0, 0, size))
        bone.parent = rig.data.edit_bones[limb.toe]
        bone.use_connect = False
        bone.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")


def _rotate(rig, name, angle, axis=(0, 1, 0)):
    if not name or name not in rig.pose.bones:
        return
    local_axis = rig.data.bones[name].matrix_local.to_quaternion().inverted() @ Vector(axis)
    rig.pose.bones[name].rotation_quaternion = Quaternion(local_axis.normalized(), angle)


def _body_pose(rig, body, motion, fraction, leg_length):
    phase = 2 * math.pi * fraction
    idle = motion["name"] == "Idle"
    run = motion["name"] == "Run"
    if idle:
        lift = motion["bob"] * (0.5 - 0.5 * math.cos(phase))
        sway = leg_length * 0.003 * math.sin(phase)
        pitch = 0.004 * math.sin(phase)
        spine_pitch = 0.003 * math.sin(phase - 0.4)
    else:
        lift = motion["bob"] * (0.5 + 0.5 * math.cos(phase - 2 * math.pi * 0.90)) if run else motion["bob"] * (0.5 - 0.5 * math.cos(2 * phase))
        sway = leg_length * (0.009 if run else 0.025) * math.sin(phase - math.pi / 4)
        pitch = 0.035 * math.sin(phase - math.pi / 2) if run else 0.012 * math.sin(2 * phase)
        spine_pitch = (0.025 if run else 0.009) * math.sin(phase - 0.5)
    hips = body["hips"]
    displacement = Vector((0, sway, -motion["drop"] + lift))
    rig.pose.bones[hips].location = rig.data.bones[hips].matrix_local.to_3x3().inverted() @ displacement
    roll = (0.006 if idle else 0.012) * math.sin(phase)
    world_rotation = Quaternion((0, 1, 0), pitch) @ Quaternion((1, 0, 0), roll)
    rest_rotation = rig.data.bones[hips].matrix_local.to_quaternion()
    rig.pose.bones[hips].rotation_quaternion = rest_rotation.inverted() @ world_rotation @ rest_rotation
    _rotate(rig, body.get("spine"), spine_pitch)
    _rotate(rig, body.get("chest"), -spine_pitch * 0.45)
    _rotate(rig, body.get("neck"), -pitch * 0.55 - spine_pitch * 0.35)
    _rotate(rig, body.get("head"), -pitch * 0.45 - spine_pitch * 0.20)
    head = body.get("head")
    if head and head in rig.pose.bones:
        local_yaw = rig.data.bones[head].matrix_local.to_quaternion().inverted() @ Vector((0, 0, 1))
        rig.pose.bones[head].rotation_quaternion @= Quaternion(local_yaw, (0.025 if idle else 0.010) * math.sin(phase - 0.6))
    for side, sign in (("L", 1), ("R", -1)):
        _rotate(rig, "Ear." + side, 0.015 * math.sin(2 * phase + sign * 0.45), (1, 0, 0))
    for index, bone in enumerate(name for name in rig.pose.bones.keys() if name.startswith("Tail")):
        _rotate(rig, bone, (0.018 if idle else 0.025 if not run else 0.040) * math.sin(phase - index * 0.38 - 0.7), (0, 0, 1))


def _segment_matrix(bone, head, tail, rotation_delta=None):
    rest_direction = bone.tail_local - bone.head_local
    direction = tail - head
    delta = rotation_delta if rotation_delta is not None else rest_direction.rotation_difference(direction)
    rotation = delta @ bone.matrix_local.to_quaternion()
    return Matrix.LocRotScale(head, rotation, Vector((1, 1, 1)))


def _pose_segment(rig, name, head, tail, desired_matrices, rotation_delta=None):
    bone = rig.data.bones[name]
    desired = _segment_matrix(bone, head, tail, rotation_delta)
    if bone.parent:
        parent_pose = desired_matrices.get(bone.parent.name, rig.pose.bones[bone.parent.name].matrix)
        basis = bone.matrix_local.inverted() @ bone.parent.matrix_local @ parent_pose.inverted() @ desired
    else:
        basis = bone.matrix_local.inverted() @ desired
    rig.pose.bones[name].matrix_basis = basis
    desired_matrices[name] = desired


def _limb_targets(rig, limb, sole_rest, displacement, foot_pitch, toe_pitch):
    foot = rig.data.bones[limb.foot]
    toe = rig.data.bones[limb.toe]
    sole = sole_rest + displacement
    toe_rotation = Quaternion((0, 1, 0), toe_pitch)
    foot_rotation = Quaternion((0, 1, 0), foot_pitch)
    toe_tail = sole - toe_rotation @ (sole_rest - toe.tail_local)
    toe_head = toe_tail - toe_rotation @ (toe.tail_local - toe.head_local)
    ankle = toe_head - foot_rotation @ (foot.tail_local - foot.head_local)
    return sole, ankle, toe_head, toe_tail


def _pose_limb(rig, limb, sole_rest, displacement, foot_pitch, toe_pitch):
    sole, ankle, toe_head, toe_tail = _limb_targets(rig, limb, sole_rest, displacement, foot_pitch, toe_pitch)
    hip = rig.pose.bones[limb.upper].head.copy()
    knee, actual_ankle, error = solve_two_bone(
        hip, ankle, rig.data.bones[limb.upper].length,
        rig.data.bones[limb.lower].length, Vector((limb.pole_forward, 0, 0)))
    # A residual remains visible in the resulting marker and fails validation.
    shift = actual_ankle - ankle
    desired = {}
    _pose_segment(rig, limb.upper, hip, knee, desired)
    _pose_segment(rig, limb.lower, knee, actual_ankle, desired)
    _pose_segment(rig, limb.foot, actual_ankle, toe_head + shift, desired, Quaternion((0, 1, 0), foot_pitch))
    _pose_segment(rig, limb.toe, toe_head + shift, toe_tail + shift, desired, Quaternion((0, 1, 0), toe_pitch))
    return error, sole


def _motion_specs(rig, limbs, soles, body, leg_length, config, species="wolf"):
    phases = {
        "Walk": {"Hind.L": 0.0, "Fore.L": 0.75, "Hind.R": 0.50, "Fore.R": 0.25},
        "Run": {"Hind.L": 0.0, "Hind.R": 0.92, "Fore.L": 0.56, "Fore.R": 0.48},
    }
    motions = [
        dict(name="Idle", duration=4.0, duty=1.0, speed=0.0, clearance=0.0, bob=leg_length * 0.006, swing_outward=0.0),
        dict(name="Walk", duration=1.0, duty=0.68, speed=leg_length * 0.70, clearance=leg_length * 0.075, bob=leg_length * 0.010, swing_outward=leg_length * 0.015),
        dict(name="Run", duration=0.60 if species == "fox" else 0.70, duty=0.26, speed=leg_length * 2.50, clearance=leg_length * 0.16, bob=leg_length * 0.040, swing_outward=leg_length * 0.018),
    ]
    overrides = config.get("motions", {})
    for motion in motions:
        motion.update(overrides.get(motion["name"], {}))
        fps = config.get("fps", 60)
        motion["duration"] = round(motion["duration"] * fps) / fps
        if motion["duration"] * fps < 5 or motion["speed"] < 0 or motion["clearance"] < 0:
            raise ValueError("Motion needs at least five frames and nonnegative speed/clearance")
        if motion["name"] != "Idle" and not 0 < motion["duty"] < 1:
            raise ValueError("Moving gait duty factor must be between zero and one")
        motion["phases"] = {limb.id: phases.get(motion["name"], {}).get(limb.id, 0.0) for limb in limbs} | motion.get("phases", {})
        motion["stance_centers"] = {
            limb.id: 0.0 if motion["name"] == "Idle" else
            0.5 * (rig.data.bones[limb.upper].head_local.x - rig.data.bones[limb.foot].head_local.x)
            for limb in limbs
        } | motion.get("stance_centers", {})
        if "drop" in motion:
            continue
        # Fit one stable body height to the actual hips, hock flex and stride.
        # Both maximum extension and minimum folded reach constrain the fit.
        lower_drop, upper_drop = 0.0, float("inf")
        motion["drop"] = 0.0
        sample_count = max(120, round(motion["duration"] * config.get("fps", 60)))
        for sample in range(sample_count + 1):
            fraction = sample / sample_count
            for pose in rig.pose.bones:
                pose.rotation_mode = "QUATERNION"
                pose.matrix_basis = Matrix.Identity(4)
            _body_pose(rig, body, motion, fraction, leg_length)
            bpy.context.view_layer.update()
            for limb in limbs:
                offset, _, _, _, pitch, toe_pitch = foot_trajectory(
                    fraction * motion["duration"], motion, motion["phases"][limb.id],
                    1 if soles[limb.id].y >= 0 else -1, limb.id.startswith("Hind"))
                offset.z += config.get("ground_height", 0.0) - soles[limb.id].z
                offset.x += motion["stance_centers"][limb.id]
                _, ankle, _, _ = _limb_targets(rig, limb, soles[limb.id], offset, pitch, toe_pitch)
                hip = rig.pose.bones[limb.upper].head
                upper = rig.data.bones[limb.upper].length
                lower = rig.data.bones[limb.lower].length
                maximum_reach = (upper + lower) * 0.985
                minimum_reach = abs(upper - lower) + leg_length * 0.012
                horizontal_squared = (hip.x - ankle.x) ** 2 + (hip.y - ankle.y) ** 2
                if horizontal_squared >= maximum_reach ** 2:
                    raise ValueError(f"{motion['name']}: stride exceeds {limb.id} reach; lower speed")
                height = hip.z - ankle.z
                lower_drop = max(lower_drop, height - math.sqrt(maximum_reach ** 2 - horizontal_squared))
                upper_drop = min(upper_drop, height - math.sqrt(max(0.0, minimum_reach ** 2 - horizontal_squared)))
        motion["drop"] = max(leg_length * 0.008, lower_drop + leg_length * 0.003)
        if motion["drop"] > upper_drop:
            raise ValueError(f"{motion['name']}: incompatible extension/compression limits ({motion['drop']:.4f} > {upper_drop:.4f}); adjust hock flex, clearance or speed")
    return motions


def _animate_magic(rig, paths, weights_fn, rest_joints, fraction):
    if not paths:
        return
    if weights_fn is None:
        raise ValueError("Animated surface paths require weights_fn(point, rest_joints)")
    for index, path in enumerate(paths):
        name = f"Flow{index + 1}"
        if name not in rig.pose.bones or len(path) < 2:
            continue
        phase = (fraction + index / max(1, len(paths))) % 1
        segment = min(len(path) - 2, int(phase * (len(path) - 1)))
        mix = phase * (len(path) - 1) - segment
        rest = Vector(path[segment]).lerp(Vector(path[segment + 1]), mix)
        point = Vector((0, 0, 0))
        for bone, weight in weights_fn(rest, rest_joints).items():
            point += (rig.pose.bones[bone].matrix @ rig.data.bones[bone].matrix_local.inverted() @ rest) * weight
        pose = rig.pose.bones[name]
        pose.location = rig.data.bones[name].matrix_local.to_3x3().inverted() @ point
        fade = math.sin(math.pi * phase) ** 2
        pose.scale = (fade, fade, fade)


def _linear_keys(action):
    curves = []
    if hasattr(action, "slots"):
        for slot in action.slots:
            for layer in action.layers:
                for strip in layer.strips:
                    bag = strip.channelbag(slot)
                    if bag:
                        curves.extend(bag.fcurves)
    elif hasattr(action, "fcurves"):
        curves.extend(action.fcurves)
    for curve in curves:
        for key in curve.keyframe_points:
            key.interpolation = "LINEAR"


def _validate_trace(clip, scale, ground, first_matrices, last_matrices):
    height_error = slip = 0.0
    frames = clip["frames"]
    for frame in frames:
        for foot in frame["feet"].values():
            if foot["contact"]:
                height_error = max(height_error, abs(foot["sole_position_units"][2] - ground) * scale)
    for previous, current in zip(frames, frames[1:]):
        for key, foot in current["feet"].items():
            old = previous["feet"][key]
            if foot["contact"] and old["contact"] and foot["contact_cycle"] == old["contact_cycle"]:
                delta = Vector(foot["virtual_world_position_m"]) - Vector(old["virtual_world_position_m"])
                slip = max(slip, delta.length)
    loop_position = loop_angle = 0.0
    for name, first in first_matrices.items():
        last = last_matrices[name]
        loop_position = max(loop_position, (first.translation - last.translation).length * scale)
        angle = first.to_quaternion().rotation_difference(last.to_quaternion()).angle
        loop_angle = max(loop_angle, math.degrees(min(angle, 2 * math.pi - angle)))
    # Fourth-order one-sided velocities distinguish a closed pose from a pop.
    # Lower orders misread the intentionally fast, smooth swing recovery.
    seam_velocity = 0.0
    step_seconds = 1 / clip["fps"]
    coefficients = (-25, 48, -36, 16, -3)
    for key in frames[0]["feet"]:
        start = [Vector(frame["feet"][key]["sole_position_units"]) for frame in frames[:5]]
        end = [Vector(frame["feet"][key]["sole_position_units"]) for frame in reversed(frames[-5:])]
        outgoing = sum((coefficient * position for coefficient, position in zip(coefficients, start)), Vector()) / (12 * step_seconds)
        incoming = -sum((coefficient * position for coefficient, position in zip(coefficients, end)), Vector()) / (12 * step_seconds)
        seam_velocity = max(seam_velocity, (outgoing - incoming).length * scale)
    checks = {
        "max_contact_slip_m": slip,
        "max_contact_height_error_m": height_error,
        "max_ik_error_m": max(frame["max_ik_error_units"] for frame in frames) * scale,
        "max_loop_position_error_m": loop_position,
        "max_loop_rotation_error_degrees": loop_angle,
        "max_foot_loop_velocity_error_mps": seam_velocity,
        "frame_count": len(frames),
    }
    checks["passed"] = (max(slip, height_error, checks["max_ik_error_m"], loop_position) < 0.0001
                        and loop_angle < 0.05
                        and seam_velocity < max(0.02, clip["expected_speed_mps"] * 0.05))
    return checks


def bake_locomotion(rig, rest_joints=None, species="wolf", config=None,
                    magic_paths=None, weights_fn=None, validation_path=None):
    """Return (Blender actions, report), raising on invalid contact validation.

    ``config`` accepts fps, meters_per_unit, ground_height, body bone names,
    limb dictionaries, and per-clip ``motions`` overrides. Motion distances
    and speeds are in armature rest units; only report values ending in ``_m``
    or ``_mps`` include meters_per_unit. No constraints or drivers are exported.
    """
    config = dict(config or {})
    fps = int(config.get("fps", 60))
    scale = float(config.get("meters_per_unit", 1.0))
    ground = float(config.get("ground_height", 0.0))
    if fps < 20 or scale <= 0:
        raise ValueError("Use fps >= 20 and a positive meters_per_unit")
    config["fps"] = fps
    config["ground_height"] = ground
    limbs = _limbs_from_config(config, rig)
    body = DEFAULT_BODY | config.get("body", {})
    required = [limb.upper for limb in limbs] + [limb.lower for limb in limbs] + [limb.foot for limb in limbs] + [limb.toe for limb in limbs] + [body["hips"]]
    missing = [name for name in required if name not in rig.data.bones]
    if missing:
        raise ValueError(f"Missing locomotion bones: {missing}")
    leg_length = sum(sum(rig.data.bones[name].length for name in (limb.upper, limb.lower, limb.foot)) for limb in limbs) / len(limbs)
    soles = {limb.id: Vector(limb.sole_rest) if limb.sole_rest else Vector((rig.data.bones[limb.toe].tail_local.x, rig.data.bones[limb.toe].tail_local.y, ground)) for limb in limbs}
    _ensure_markers(rig, limbs, soles, leg_length * 0.025)
    rig.animation_data_clear()
    motions = _motion_specs(rig, limbs, soles, body, leg_length, config, species)
    requested_names = {motion["name"] for motion in motions}
    rig.animation_data_clear()
    for action in list(bpy.data.actions):
        if action.name in requested_names:
            action.use_fake_user = False
            if action.users == 0:
                bpy.data.actions.remove(action)
            else:
                raise ValueError(f"Action {action.name} is owned by another object")
    scene = bpy.context.scene
    scene.render.fps = fps
    scene.render.fps_base = 1.0
    report = {
        "schemaVersion": 1, "species": species, "meters_per_unit": scale,
        "coordinate_system": {"up": "Z", "forward": "+X", "ground_height": ground},
        "root_bone": body["root"], "body_bones": body,
        "limbs": [{"id": limb.id, "contact_marker": limb.marker,
                   "bones": {key: getattr(limb, key) for key in ("upper", "lower", "foot", "toe")},
                   "sole_rest_armature_space": list(soles[limb.id]),
                   "sole_offset_bone_local": list(rig.data.bones[limb.toe].matrix_local.inverted() @ soles[limb.id])}
                  for limb in limbs],
        "clips": [],
    }
    actions = []
    for motion in motions:
        steps = int(round(motion["duration"] * fps))
        motion["duration"] = steps / fps
        action = bpy.data.actions.new(motion["name"])
        action.use_fake_user = True
        action["arkya_locomotion"] = True
        action["expected_speed_mps"] = motion["speed"] * scale
        rig.animation_data_create()
        rig.animation_data.action = action
        clip = {
            "name": motion["name"], "duration_seconds": motion["duration"], "fps": fps,
            "expected_speed_mps": motion["speed"] * scale,
            "speed_units_per_second": motion["speed"], "in_place": True,
            "gait": "breathing" if motion["name"] == "Idle" else "four_beat_walk" if motion["name"] == "Walk" else "transverse_gallop",
            "body_drop_units": motion["drop"],
            "legs": {limb.id: {"phase_offset": motion["phases"][limb.id],
                                 "duty_factor": motion["duty"],
                                 "stance_center_offset_units": motion["stance_centers"][limb.id],
                                 "swing_clearance_m": motion["clearance"] * scale}
                     for limb in limbs}, "frames": [],
        }
        first_matrices = None
        previous_quaternions = {}
        for frame in range(steps + 1):
            fraction = frame / steps
            scene.frame_set(frame)
            for pose in rig.pose.bones:
                pose.rotation_mode = "QUATERNION"
                pose.matrix_basis = Matrix.Identity(4)
            _body_pose(rig, body, motion, fraction, leg_length)
            bpy.context.view_layer.update()
            targets = {}
            max_ik_error = 0.0
            for limb in limbs:
                trajectory = foot_trajectory(frame / fps, motion, motion["phases"][limb.id],
                                             1 if soles[limb.id].y >= 0 else -1,
                                             limb.id.startswith("Hind"))
                displacement, contact, epoch, phase, foot_pitch, toe_pitch = trajectory
                displacement.z += ground - soles[limb.id].z
                displacement.x += motion["stance_centers"][limb.id]
                error, target = _pose_limb(rig, limb, soles[limb.id], displacement, foot_pitch, toe_pitch)
                max_ik_error = max(max_ik_error, error)
                targets[limb.id] = (contact, epoch, phase, target)
            bpy.context.view_layer.update()
            _animate_magic(rig, magic_paths, weights_fn, rest_joints, fraction)
            bpy.context.view_layer.update()
            sample = {"frame": frame, "time_seconds": frame / fps,
                      "max_ik_error_units": max_ik_error, "feet": {}}
            for limb in limbs:
                contact, epoch, phase, target = targets[limb.id]
                actual = rig.pose.bones[limb.marker].head.copy()
                virtual = (actual + Vector((motion["speed"] * frame / fps, 0, 0))) * scale
                sample["feet"][limb.id] = {
                    "contact": contact, "contact_cycle": epoch, "phase": phase,
                    "sole_position_units": [round(v, 9) for v in actual],
                    "virtual_world_position_m": [round(v, 9) for v in virtual],
                    "target_error_m": (actual - target).length * scale,
                }
            sample["max_ik_error_units"] = max(max_ik_error, max(foot["target_error_m"] / scale for foot in sample["feet"].values()))
            clip["frames"].append(sample)
            matrices = {pose.name: pose.matrix.copy() for pose in rig.pose.bones if not pose.name.startswith("Flow")}
            if first_matrices is None:
                first_matrices = matrices
            for pose in rig.pose.bones:
                previous = previous_quaternions.get(pose.name)
                if previous is not None and pose.rotation_quaternion.dot(previous) < 0:
                    pose.rotation_quaternion.negate()
                previous_quaternions[pose.name] = pose.rotation_quaternion.copy()
                for channel in ("location", "rotation_quaternion", "scale"):
                    pose.keyframe_insert(data_path=channel, frame=frame, group=pose.name)
        _linear_keys(action)
        clip["checks"] = _validate_trace(clip, scale, ground, first_matrices, matrices)
        report["clips"].append(clip)
        actions.append(action)
    report["passed"] = all(clip["checks"]["passed"] for clip in report["clips"])
    if validation_path:
        path = Path(validation_path)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    rig.animation_data.action = actions[0]
    rig["locomotion_contract"] = "In-place IK; Contact.* markers; +X forward; see per-species locomotion report"
    scene.frame_start = 0
    scene.frame_end = int(round(motions[0]["duration"] * fps))
    scene.frame_set(0)
    if not report["passed"] and config.get("raise_on_validation_error", True):
        failures = {clip["name"]: clip["checks"] for clip in report["clips"] if not clip["checks"]["passed"]}
        raise ValueError(f"Quadruped contact validation failed: {failures}")
    return actions, report
