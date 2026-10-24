"""Author the Brise-Seve skeleton, smooth skin and baked in-place animations.

Blender 4.5; metres, Z up, -Y forward. ``build_rig(mesh_objects)`` returns
``(armature, JSON-serializable validation_report)``. Rigid pieces opt in with
``bind_bone``; the continuous body uses ``skin_region='body'``. All animation
is baked FK, with no constraints, drivers or gameplay root displacement.
"""
from __future__ import annotations

import math
import json
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

FPS = 60
GROUND = .02
SIDES = (("L", 1), ("R", -1))
LIMBS = tuple(f"{kind}.{side}" for kind in ("Fore", "Hind") for side, _ in SIDES)


def _smooth(value):
    x = max(0., min(1., value))
    return x * x * x * (10 + x * (-15 + 6 * x))


def _skeleton():
    joints = {
        "Root": ((0, 0, 0), (0, 0, .18), None),
        "Body": ((0, .65, 1.03), (0, .15, 1.10), "Root"),
        "Spine": ((0, .15, 1.10), (0, -.34, 1.16), "Body"),
        "Chest": ((0, -.34, 1.16), (0, -.63, 1.22), "Spine"),
        "Neck": ((0, -.63, 1.22), (0, -.85, 1.20), "Chest"),
        "Head": ((0, -.85, 1.20), (0, -1.43, .92), "Neck"),
        "Jaw": ((0, -1.02, .99), (0, -1.43, .85), "Head"),
        "Tail01": ((0, 1.04, 1.10), (0, 1.27, 1.04), "Body"),
        "Tail02": ((0, 1.27, 1.04), (0, 1.45, .90), "Tail01"),
    }
    soles = {}
    for side, sign in SIDES:
        def p(x, y, z):
            return (sign * x, y, z)
        joints.update({
            f"ForeUpper.{side}": (p(.42, -.40, 1.05), p(.49, -.50, .58), "Chest"),
            f"ForeLower.{side}": (p(.49, -.50, .58), p(.48, -.72, .12), f"ForeUpper.{side}"),
            f"ForePaw.{side}": (p(.48, -.72, .12), p(.48, -.91, .12), f"ForeLower.{side}"),
            f"ForeToe.{side}": (p(.48, -.91, .12), p(.48, -.98, .12), f"ForePaw.{side}"),
            f"HindUpper.{side}": (p(.40, .66, 1.03), p(.44, .50, .72), "Body"),
            f"HindLower.{side}": (p(.44, .50, .72), p(.43, 1., .42), f"HindUpper.{side}"),
            f"HindPaw.{side}": (p(.43, 1., .42), p(.43, .82, .12), f"HindLower.{side}"),
            f"HindToe.{side}": (p(.43, .82, .12), p(.43, .61, .12), f"HindPaw.{side}"),
        })
        for kind, x, y in (("Fore", .48, -.82), ("Hind", .43, .72)):
            marker = f"Contact.{kind}.{side}"
            soles[f"{kind}.{side}"] = Vector(p(x, y, GROUND))
            joints[marker] = (p(x, y, GROUND), p(x, y, GROUND + .05), f"{kind}Toe.{side}")
    data = bpy.data.armatures.new("BriseSeveSkeleton")
    rig = bpy.data.objects.new("BriseSeveRig", data)
    bpy.context.scene.collection.objects.link(rig)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, parent) in joints.items():
        bone = data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        bone.use_deform = not name.startswith("Contact.") and name != "Root"
        if parent:
            bone.parent = data.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    rig.show_in_front = True
    rig["animation_contract"] = "Generic, Root, in-place, metres, -Y forward, 60 fps; contact markers included"
    for pose in rig.pose.bones:
        pose.rotation_mode = "QUATERNION"
    return rig, soles


def _weight_point(point, rig):
    """Blend two axial and two limb influences with continuous support regions."""
    nodes = ((.65, "Body"), (.15, "Spine"), (-.36, "Chest"), (-.66, "Neck"), (-.84, "Head"))
    torso = {"Body" if point.y >= nodes[0][0] else "Head": 1.}
    for (back, a), (front, b) in zip(nodes, nodes[1:]):
        if front <= point.y <= back:
            mix = _smooth((back - point.y) / (back - front))
            torso = {a: 1 - mix, b: mix}
            break
    if point.y > .95:
        tail = _smooth((point.y - .95) / .25) * _smooth((point.z - .70) / .12)
        torso = {"Body": 1 - tail, "Tail01": tail}
    jaw = _smooth((-point.y - .91) / .22) * (1 - _smooth((point.z - .86) / .12))
    if jaw:
        torso = {"Head": 1 - jaw, "Jaw": jaw}
    side = "L" if point.x >= 0 else "R"
    leg_amount = (1 - _smooth((point.z - .60) / .46)) * _smooth((abs(point.x) - .17) / .18)
    # The muzzle can extend below the neck, but never belongs to a foreleg.
    leg_amount *= _smooth((point.y + 1.15) / .25)
    kind = "Fore" if point.y < .10 else "Hind"
    # Both limbs fade out at the belly, so their supports never switch abruptly.
    leg_amount *= (1 - _smooth((point.y + .10) / .20)) if kind == "Fore" else _smooth((point.y - .10) / .28)
    weights = {name: weight * (1 - leg_amount) for name, weight in torso.items()}
    if kind == "Fore":
        if point.z > .36:
            blend = _smooth((point.z - .42) / .32)
            parts = {"Upper": blend, "Lower": 1 - blend}
        else:
            blend = _smooth((point.z - .15) / .18)
            parts = {"Lower": blend, "Paw": 1 - blend}
    elif point.z > .57:
        blend = _smooth((point.z - .59) / .25)
        parts = {"Upper": blend, "Lower": 1 - blend}
    elif point.z > .31:
        blend = _smooth((point.z - .32) / .24)
        parts = {"Lower": blend, "Paw": 1 - blend}
    else:
        blend = _smooth((point.z - .16) / .14)
        parts = {"Paw": blend, "Toe": 1 - blend}
    weights.update({f"{kind}{name}.{side}": weight * leg_amount for name, weight in parts.items()})
    selected = list(weights.items())
    selected = [(name, weight) for name, weight in selected if weight > .0001]
    total = sum(weight for _, weight in selected)
    return [(name, weight / total) for name, weight in selected]


def _skin(meshes, rig):
    report = {"mesh_count": 0, "vertex_count": 0, "max_influences": 0,
              "max_weight_sum_error": 0., "unweighted_vertices": 0, "rigid_meshes": []}
    for obj in meshes:
        if obj.type != "MESH":
            continue
        rigid = obj.get("bind_bone")
        if rigid and rigid not in rig.data.bones:
            raise ValueError(f"{obj.name}: unknown attachment bone {rigid}")
        if not rigid and obj.get("skin_region") != "body":
            raise ValueError(f"{obj.name}: needs bind_bone or skin_region='body'")
        for group in list(obj.vertex_groups):
            obj.vertex_groups.remove(group)
        groups = {bone.name: obj.vertex_groups.new(name=bone.name) for bone in rig.data.bones if bone.use_deform}
        world = obj.matrix_world.copy()
        obj.parent = rig
        obj.matrix_world = world
        modifier = obj.modifiers.new("BriseSeveSkin", "ARMATURE")
        modifier.object = rig
        modifier.use_deform_preserve_volume = False
        for vertex in obj.data.vertices:
            weights = [(rigid, 1.)] if rigid else _weight_point(world @ vertex.co, rig)
            for name, weight in weights:
                groups[name].add([vertex.index], weight, "REPLACE")
            report["max_influences"] = max(report["max_influences"], len(weights))
            report["max_weight_sum_error"] = max(report["max_weight_sum_error"], abs(sum(w for _, w in weights) - 1))
            report["unweighted_vertices"] += not weights
        if rigid:
            report["rigid_meshes"].append({"mesh": obj.name, "bone": rigid})
        report["mesh_count"] += 1
        report["vertex_count"] += len(obj.data.vertices)
    return report


def _rotate(rig, name, angle, axis=(1, 0, 0)):
    rest = rig.data.bones[name].matrix_local.to_quaternion()
    rig.pose.bones[name].rotation_quaternion = rest.inverted() @ Quaternion(axis, angle) @ rest


def _body_pose(rig, motion, t):
    phase = math.tau * t
    name = motion["name"]
    shift, pitch, roll, neck, head = Vector(), 0., 0., 0., 0.
    if name == "Idle":
        shift.z = .006 * (1 - math.cos(phase))
        pitch = .004 * math.sin(phase)
        head = .012 * math.sin(phase - .3)
    elif name == "Walk":
        shift.z = .012 * (1 - math.cos(2 * phase))
        shift.x = .014 * math.sin(phase)
        pitch = .014 * math.sin(2 * phase)
        roll = .014 * math.sin(phase)
        head = -.016 * math.sin(2 * phase - .7)
    elif name == "Charge":
        shift.z = .035 * (1 + math.cos(phase - .9))
        pitch = .04 * math.sin(phase)
        neck, head = .17, .15 + .025 * math.sin(phase - .7)
    elif name == "ChargeAnticipation":
        crouch = _smooth(t)
        shift.z = -.09 * crouch
        shift.y = .04 * math.sin(math.pi * t)
        neck, head = .17 * crouch, .15 * crouch
    elif name == "Attack":
        windup = _smooth(t / .32)
        strike = _smooth((t - .32) / .18)
        recovery = _smooth((t - .55) / .45)
        head = -.20 * windup + .52 * strike - .32 * recovery
        neck = -.08 * windup + .25 * strike - .17 * recovery
        shift.y = .035 * windup - .16 * strike + .125 * recovery
        shift.z = -.06 * math.sin(math.pi * t) ** 2
        _rotate(rig, "Jaw", .12 * math.sin(math.pi * t) ** 2)
    elif name == "Hit":
        impact = math.sin(math.pi * min(1, t / .22)) if t < .22 else -.20 * math.sin(math.pi * (t - .22) / .78)
        shift.y, shift.z = .045 * impact, -.025 * abs(impact)
        head, roll = -.12 * impact, .035 * impact
    elif name == "Death":
        fall = _smooth((t - .12) / .68)
        shift = Vector((.30 * fall, .03 * fall, -.62 * fall))
        roll, pitch = 1.10 * fall, .12 * fall
        head, neck = .23 * fall, .12 * fall
        _rotate(rig, "Jaw", .13 * fall)
    shift.z -= motion["drop"]
    body_rest = rig.data.bones["Body"].matrix_local
    rig.pose.bones["Body"].location = body_rest.to_3x3().inverted() @ shift
    rest_rotation = body_rest.to_quaternion()
    rig.pose.bones["Body"].rotation_quaternion = rest_rotation.inverted() @ Quaternion((0, 1, 0), roll) @ Quaternion((1, 0, 0), pitch) @ rest_rotation
    _rotate(rig, "Spine", .008 * math.sin(phase) if motion["loop"] else 0)
    _rotate(rig, "Neck", neck - .3 * pitch)
    _rotate(rig, "Head", head - .5 * pitch)
    if name == "Idle":
        axis = rig.data.bones["Head"].matrix_local.to_quaternion().inverted() @ Vector((0, 0, 1))
        rig.pose.bones["Head"].rotation_quaternion @= Quaternion(axis, .022 * math.sin(phase))
    for i in (1, 2):
        _rotate(rig, f"Tail0{i}", (.05 if name == "Idle" else .075) * math.sin(phase - .45 * i) * (1 - _smooth(t) if name == "Death" else 1), (0, 0, 1))


def _foot_motion(motion, limb, t):
    """Metres in rig space; stance exactly cancels -Y world travel."""
    if motion["speed"] == 0:
        offset = Vector()
        contact = True
        if motion["name"] == "ChargeAnticipation" and limb == "Fore.R" and .15 < t < .65:
            scrape = (t - .15) / .5
            offset.y = .11 * math.sin(math.tau * scrape) * math.sin(math.pi * scrape) ** 2
            offset.z = .055 * math.sin(math.pi * scrape) ** 3
            contact = False
        return offset, contact, 0, 0.
    cycle = t + motion["phases"][limb]
    epoch = math.floor(cycle + 1e-10)
    phase = cycle - epoch
    duty = motion["duty"]
    stride = motion["speed"] * motion["duration"] * duty
    if phase < duty:
        forward = stride * (.5 - phase / duty)
        return Vector((0, -forward, 0)), True, epoch, 0.
    swing = (phase - duty) / (1 - duty)
    velocity = -motion["speed"] * motion["duration"] * (1 - duty)
    forward = -stride / 2 + velocity * swing + (stride - velocity) * _smooth(swing)
    arc = max(0., math.sin(math.pi * swing)) ** 3
    sign = 1 if limb.endswith("L") else -1
    return Vector((sign * .014 * arc, -forward, motion["clearance"] * arc)), False, epoch, -.18 * arc


def _limb_names(limb):
    kind, side = limb.split(".")
    return [f"{kind}{part}.{side}" for part in ("Upper", "Lower", "Paw", "Toe")]


def _targets(rig, limb, sole, offset, pitch):
    upper, lower, paw_name, toe_name = _limb_names(limb)
    paw, toe = rig.data.bones[paw_name], rig.data.bones[toe_name]
    rotation = pitch if isinstance(pitch, Quaternion) else Quaternion((1, 0, 0), pitch)
    toe_head = sole + offset + rotation @ (toe.head_local - sole)
    toe_tail = sole + offset + rotation @ (toe.tail_local - sole)
    ankle = toe_head - rotation @ (paw.tail_local - paw.head_local)
    return ankle, toe_head, toe_tail, rotation


def _solve(hip, target, upper, lower, pole):
    delta = target - hip
    requested = delta.length
    distance = min(upper + lower - 1e-7, max(abs(upper - lower) + 1e-7, requested))
    direction = delta.normalized()
    bend = pole - direction * pole.dot(direction)
    bend.normalize()
    along = (distance ** 2 + upper ** 2 - lower ** 2) / (2 * distance)
    knee = hip + direction * along + bend * math.sqrt(max(0., upper ** 2 - along ** 2))
    return knee, hip + direction * distance, abs(requested - distance)


def _pose_segment(rig, name, head, tail, matrices, delta=None):
    bone = rig.data.bones[name]
    if delta is None:
        delta = (bone.tail_local - bone.head_local).rotation_difference(tail - head)
    desired = Matrix.LocRotScale(head, delta @ bone.matrix_local.to_quaternion(), Vector((1, 1, 1)))
    parent_pose = matrices.get(bone.parent.name, rig.pose.bones[bone.parent.name].matrix)
    rig.pose.bones[name].matrix_basis = bone.matrix_local.inverted() @ bone.parent.matrix_local @ parent_pose.inverted() @ desired
    matrices[name] = desired


def _pose_limb(rig, limb, sole, offset, pitch):
    upper, lower, paw, toe = _limb_names(limb)
    ankle, toe_head, toe_tail, rotation = _targets(rig, limb, sole, offset, pitch)
    hip = rig.pose.bones[upper].head.copy()
    pole = Vector((0, 1 if limb.startswith("Fore") else -1, 0))
    if isinstance(pitch, Quaternion):
        pole = pitch @ pole
    knee, reached, error = _solve(hip, ankle, rig.data.bones[upper].length,
                                  rig.data.bones[lower].length, pole)
    shift = reached - ankle
    matrices = {}
    _pose_segment(rig, upper, hip, knee, matrices)
    _pose_segment(rig, lower, knee, reached, matrices)
    _pose_segment(rig, paw, reached, toe_head + shift, matrices, rotation)
    _pose_segment(rig, toe, toe_head + shift, toe_tail + shift, matrices, rotation)
    return error


def _reset(rig):
    for pose in rig.pose.bones:
        pose.matrix_basis = Matrix.Identity(4)


def _motions():
    result = []
    for name, duration, loop in (("Idle", 4., True), ("Walk", 1.2, True), ("Charge", .65, True),
                                  ("ChargeAnticipation", 1., False), ("Attack", .9, False),
                                  ("Hit", .5, False), ("Death", 1.4, False)):
        motion = dict(name=name, duration=duration, loop=loop, speed=0., duty=1., clearance=0., drop=.025, phases={})
        if name == "Walk":
            motion.update(speed=.70, duty=.68, clearance=.095,
                          phases={"Hind.L": 0., "Fore.L": .75, "Hind.R": .5, "Fore.R": .25})
        elif name == "Charge":
            motion.update(speed=2.8, duty=.28, clearance=.20,
                          phases={"Hind.L": 0., "Hind.R": .93, "Fore.L": .55, "Fore.R": .47})
        result.append(motion)
    return result


def _fit_body_height(rig, motion, soles):
    if motion["name"] == "Death":
        return
    required = .025
    motion["drop"] = 0.
    for sample in range(121):
        t = sample / 120
        _reset(rig)
        _body_pose(rig, motion, t)
        bpy.context.view_layer.update()
        for limb in LIMBS:
            offset, _, _, pitch = _foot_motion(motion, limb, t)
            if motion["speed"]:
                offset.y += .15 if limb.startswith("Fore") else -.05
            ankle, _, _, _ = _targets(rig, limb, soles[limb], offset, pitch)
            upper, lower, _, _ = _limb_names(limb)
            hip = rig.pose.bones[upper].head
            maximum = (rig.data.bones[upper].length + rig.data.bones[lower].length) * .985
            horizontal = (hip.x - ankle.x) ** 2 + (hip.y - ankle.y) ** 2
            if horizontal >= maximum ** 2:
                raise ValueError(f"{motion['name']} exceeds {limb} stride reach")
            required = max(required, hip.z - ankle.z - math.sqrt(maximum ** 2 - horizontal) + .004)
    motion["drop"] = required


def _curves(action):
    for slot in action.slots:
        for layer in action.layers:
            for strip in layer.strips:
                bag = strip.channelbag(slot)
                if bag:
                    yield from bag.fcurves


def _mesh_floor(meshes):
    minimum = float("inf")
    graph = bpy.context.evaluated_depsgraph_get()
    for obj in meshes:
        if obj.type != "MESH":
            continue
        evaluated = obj.evaluated_get(graph)
        mesh = evaluated.to_mesh()
        minimum = min(minimum, min(((evaluated.matrix_world @ vertex.co).z for vertex in mesh.vertices), default=float("inf")))
        evaluated.to_mesh_clear()
    return minimum


def _bake(rig, soles, meshes=()):
    scene = bpy.context.scene
    scene.render.fps, scene.render.fps_base = FPS, 1.
    rig.animation_data_create()
    reports, actions = [], []
    for motion in _motions():
        rig.animation_data.action = None
        _fit_body_height(rig, motion, soles)
        action = bpy.data.actions.new(motion["name"])
        action.use_fake_user = True
        action["expected_speed_mps"] = motion["speed"]
        action["loop"] = motion["loop"]
        rig.animation_data.action = action
        steps = round(motion["duration"] * FPS)
        clip = dict(name=motion["name"], duration_seconds=steps / FPS, fps=FPS,
                    loop=motion["loop"], expected_speed_mps=motion["speed"],
                    in_place=True, body_drop_m=motion["drop"], frames=[])
        quaternions, first, last = {}, None, None
        max_ik = max_height = max_slip = max_target_error = max_root_travel = 0.
        previous_feet = {}
        for frame in range(steps + 1):
            scene.frame_set(frame)
            t = frame / steps
            _reset(rig)
            _body_pose(rig, motion, t)
            bpy.context.view_layer.update()
            targets = {}
            for limb in LIMBS:
                offset, contact, epoch, pitch = _foot_motion(motion, limb, t)
                if motion["speed"]:
                    offset.y += .15 if limb.startswith("Fore") else -.05
                if motion["name"] == "Death":
                    fall = _smooth((t - .12) / .68)
                    rest = rig.data.bones["Body"].matrix_local
                    deformed = rig.pose.bones["Body"].matrix @ rest.inverted() @ soles[limb]
                    offset = deformed - soles[limb]
                    offset.z = max(GROUND, soles[limb].z + offset.z) - soles[limb].z
                    contact = t < .12
                    pitch = rig.pose.bones["Body"].matrix.to_quaternion() @ rest.to_quaternion().inverted()
                error = _pose_limb(rig, limb, soles[limb], offset, pitch)
                max_ik = max(max_ik, error)
                targets[limb] = (contact, epoch, soles[limb] + offset)
            bpy.context.view_layer.update()
            if motion["name"] == "Death" and t >= .12 and meshes:
                # Fit the authored volume to the ground once planted IK releases.
                lift = max(0., .001 - _mesh_floor(meshes))
                if lift:
                    correction = Vector((0, 0, lift))
                    rig.pose.bones["Body"].location += rig.data.bones["Body"].matrix_local.to_3x3().inverted() @ correction
                    targets = {limb: (contact, epoch, target + correction)
                               for limb, (contact, epoch, target) in targets.items()}
                    bpy.context.view_layer.update()
            feet = {}
            for limb, (contact, epoch, target) in targets.items():
                actual = rig.pose.bones["Contact." + limb].head.copy()
                virtual = actual + Vector((0, -motion["speed"] * frame / FPS, 0))
                target_error = (actual - target).length
                max_target_error = max(max_target_error, target_error)
                if contact:
                    max_height = max(max_height, abs(actual.z - GROUND))
                previous = previous_feet.get(limb)
                if contact and previous and previous[0] and previous[1] == epoch:
                    max_slip = max(max_slip, (virtual - previous[2]).length)
                previous_feet[limb] = (contact, epoch, virtual)
                feet[limb] = dict(position_m=list(actual), contact=contact, target_error_m=target_error)
            clip["frames"].append(dict(frame=frame, feet=feet))
            matrices = {pose.name: pose.matrix.copy() for pose in rig.pose.bones}
            max_root_travel = max(max_root_travel, matrices["Root"].translation.length)
            first = first or matrices
            last = matrices
            for pose in rig.pose.bones:
                old = quaternions.get(pose.name)
                if old is not None and pose.rotation_quaternion.dot(old) < 0:
                    pose.rotation_quaternion.negate()
                quaternions[pose.name] = pose.rotation_quaternion.copy()
                for channel in ("location", "rotation_quaternion", "scale"):
                    pose.keyframe_insert(data_path=channel, frame=frame, group=pose.name)
        for curve in _curves(action):
            for point in curve.keyframe_points:
                point.interpolation = "LINEAR"
        loop_error = max((first[name].translation - last[name].translation).length for name in first) if motion["loop"] else None
        loop_angle = max(math.degrees(first[name].to_quaternion().rotation_difference(last[name].to_quaternion()).angle)
                         for name in first) if motion["loop"] else None
        clip["checks"] = dict(max_contact_height_error_m=max_height, max_contact_slip_m=max_slip,
                              max_ik_error_m=max_ik, max_sole_target_error_m=max_target_error,
                              max_root_travel_m=max_root_travel, max_loop_position_error_m=loop_error,
                              max_loop_rotation_error_degrees=loop_angle)
        clip["checks"]["passed"] = (max(max_height, max_slip, max_ik, max_target_error, max_root_travel) < .0002
                                     and (loop_error is None or loop_error < .0002)
                                     and (loop_angle is None or loop_angle < .05))
        reports.append(clip)
        actions.append(action)
    rig.animation_data.action = None
    for action in actions:
        track = rig.animation_data.nla_tracks.new()
        track.name = action.name
        strip = track.strips.new(action.name, 0, action)
        strip.name = action.name
        strip.extrapolation = "NOTHING"
        track.mute = True
    rig.animation_data.action = actions[0]
    scene.frame_start, scene.frame_end = 0, round(4 * FPS)
    scene.frame_set(0)
    return actions, reports


def _validate_deformation(rig, meshes, actions):
    """Inspect actual evaluated skin bounds, finite vertices and moving geometry."""
    scene = bpy.context.scene
    result = dict(finite=True, evaluated_vertex_samples=0, max_sampled_edge_stretch=0., clips=[])
    rest_edges = {}
    for obj in meshes:
        if obj.type != "MESH" or obj.get("skin_region") != "body":
            continue
        edge_step = max(1, len(obj.data.edges) // 2000)
        samples = []
        for edge in list(obj.data.edges)[::edge_step]:
            a, b = edge.vertices
            length = (obj.data.vertices[a].co - obj.data.vertices[b].co).length
            if length > .0001:
                samples.append((a, b, length))
        rest_edges[obj.name] = samples
    for action in actions:
        rig.animation_data.action = action
        minimum, maximum = Vector((1e10,) * 3), Vector((-1e10,) * 3)
        for fraction in (0., .25, .5, .75, 1.):
            scene.frame_set(round(action.frame_range[1] * fraction))
            graph = bpy.context.evaluated_depsgraph_get()
            for obj in meshes:
                if obj.type != "MESH":
                    continue
                evaluated = obj.evaluated_get(graph)
                mesh = evaluated.to_mesh()
                for a, b, length in rest_edges.get(obj.name, []):
                    if a < len(mesh.vertices) and b < len(mesh.vertices):
                        stretch = (mesh.vertices[a].co - mesh.vertices[b].co).length / length
                        if stretch > result["max_sampled_edge_stretch"]:
                            result["max_sampled_edge_stretch"] = stretch
                            result["worst_edge"] = dict(mesh=obj.name, action=action.name, frame=scene.frame_current,
                                                        vertices=[a, b], rest_length_m=length,
                                                        rest_positions=[list(obj.data.vertices[v].co) for v in (a, b)],
                                                        deformed_positions=[list(mesh.vertices[v].co) for v in (a, b)],
                                                        weights=[{obj.vertex_groups[g.group].name: g.weight for g in obj.data.vertices[v].groups} for v in (a, b)])
                for vertex in mesh.vertices:
                    point = evaluated.matrix_world @ vertex.co
                    result["finite"] &= all(math.isfinite(v) for v in point)
                    for axis in range(3):
                        minimum[axis] = min(minimum[axis], point[axis])
                        maximum[axis] = max(maximum[axis], point[axis])
                result["evaluated_vertex_samples"] += len(mesh.vertices)
                evaluated.to_mesh_clear()
        result["clips"].append(dict(name=action.name, bounds_min_m=list(minimum), bounds_max_m=list(maximum)))
    rig.animation_data.action = actions[0]
    scene.frame_set(0)
    return result


def build_rig(mesh_objects):
    meshes = list(mesh_objects)
    rig, soles = _skeleton()
    skin = _skin(meshes, rig)
    actions, clips = _bake(rig, soles, meshes)
    report = dict(schemaVersion=1, coordinate_system="metres; Z up; -Y forward", root_bone="Root",
                  skin=skin, clips=clips, skeleton={bone.name: dict(head=list(bone.head_local), tail=list(bone.tail_local),
                      parent=bone.parent.name if bone.parent else None, deform=bone.use_deform) for bone in rig.data.bones},
                  deformation=_validate_deformation(rig, meshes, actions))
    report["passed"] = (all(clip["checks"]["passed"] for clip in clips) and report["deformation"]["finite"]
                        and report["deformation"]["max_sampled_edge_stretch"] < 5 and not skin["unweighted_vertices"])
    if not report["passed"]:
        failures = {clip["name"]: clip["checks"] for clip in clips if not clip["checks"]["passed"]}
        (Path(__file__).resolve().parents[1] / "animation-failure-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        raise ValueError(f"Brise-Seve rig validation failed: clips={failures}, skin={skin}, deformation={report['deformation']}")
    return rig, report
