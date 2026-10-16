"""Import each delivered FBX into a fresh Blender scene and evaluate real deformation."""
import bpy
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def evaluated_meshes(meshes):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    points = {}
    for obj in meshes:
        evaluated = obj.evaluated_get(depsgraph)
        mesh = evaluated.to_mesh()
        points[obj.name] = [evaluated.matrix_world @ v.co for v in mesh.vertices]
        evaluated.to_mesh_clear()
    return points


def check_species(species):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    source_path = ROOT / 'assets' / f'{species}.fbx'
    source_sha256 = hashlib.sha256(source_path.read_bytes()).hexdigest()
    bpy.ops.import_scene.fbx(filepath=str(source_path),
                             use_anim=True, colors_type='LINEAR')
    rigs = [obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE']
    assert len(rigs) == 1, (species, len(rigs))
    rig = rigs[0]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    assert meshes and len(rig.data.bones) >= 20
    worst_sum = 0
    influences = 0
    for obj in meshes:
        assert obj.data.uv_layers, obj.name
        assert any(mod.type == 'ARMATURE' and mod.object == rig for mod in obj.modifiers), obj.name
        for vertex in obj.data.vertices:
            assert all(math.isfinite(x) for x in vertex.co)
            weights = [group.weight for group in vertex.groups if group.weight > 1e-6]
            assert weights, (obj.name, vertex.index)
            influences = max(influences, len(weights))
            worst_sum = max(worst_sum, abs(sum(weights) - 1))
    assert influences <= 4 and worst_sum < .001, (influences, worst_sum)
    actions = {action.name.split('|')[-1]: action for action in bpy.data.actions}
    assert {'Idle', 'Walk', 'Run'} <= actions.keys(), list(actions)
    if rig.animation_data is None:
        rig.animation_data_create()
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    animation_reports = []
    for name in ('Idle', 'Walk', 'Run'):
        action = actions[name]
        rig.animation_data.action = action
        if action.slots:
            rig.animation_data.action_slot = action.slots[0]
        start, end = action.frame_range
        bpy.context.scene.frame_set(int(start), subframe=start % 1)
        first = evaluated_meshes(meshes)
        mesh_movement = {key: 0 for key in first}
        max_movement = 0
        lowest = math.inf
        highest = -math.inf
        for fraction in (.125, .25, .375, .5, .625, .75, .875):
            frame = start + (end - start) * fraction
            bpy.context.scene.frame_set(int(frame), subframe=frame % 1)
            sample = evaluated_meshes(meshes)
            for key in first:
                assert len(sample[key]) == len(first[key])
                movement = max((a-b).length for a, b in zip(first[key], sample[key]))
                mesh_movement[key] = max(mesh_movement[key], movement)
                max_movement = max(max_movement, movement)
                lowest = min(lowest, min(p.z for p in sample[key]))
                highest = max(highest, max(p.z for p in sample[key]))
        bpy.context.scene.frame_set(int(end), subframe=end % 1)
        last = evaluated_meshes(meshes)
        loop_error = max((a-b).length for key in first for a, b in zip(first[key], last[key]))
        assert any('Fur' in key and value > .001 for key, value in mesh_movement.items()), 'Only particles move'
        assert max_movement > .001, (species, name, max_movement)
        assert loop_error < .001, (species, name, loop_error)
        assert highest - lowest < 5, (species, name, 'Exploded deformation')
        animation_reports.append({'name': name, 'sampleCount': 9,
                                  'maxVertexDisplacementMeters': max_movement,
                                  'maxLoopVertexErrorMeters': loop_error,
                                  'meshMaxDisplacementMeters': mesh_movement,
                                  'sampledMinZMeters': lowest,
                                  'sampledMaxZMeters': highest})
    return {'species': species, 'sha256': source_sha256, 'bones': len(rig.data.bones), 'meshes': len(meshes),
            'maxInfluences': influences, 'maxWeightSumError': worst_sum,
            'animations': animation_reports}


reports = [check_species(species) for species in ('wolf', 'fox')]
report = {'schemaVersion': 1, 'status': 'passed',
          'method': 'Fresh Blender FBX reimport; evaluated skinned vertex samples',
          'models': reports}
(ROOT / 'fbx-roundtrip-verification.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(json.dumps(report))
