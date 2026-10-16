"""Reproduce the documented source study pose and save its anatomical joints.

No downloaded rig Python is executed. This only reads the credited sources.
"""
import bpy, json, math
from pathlib import Path
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[1]
SOURCES=Path('D:/Tools/BlenderPortable/character-sources')

def rotate(bone,axis,deg):
    m=bone.matrix.copy();p=bone.head.copy()
    bone.matrix=Matrix.Translation(p) @ Matrix.Rotation(math.radians(deg),4,axis) @ Matrix.Translation(-p) @ m
    bpy.context.view_layer.update()

results={}
for silhouette in ('a','b'):
    snow=silhouette=='a'
    source=SOURCES/('snow-v4/Snow/snow_v4.2.blend' if snow else 'rain-v3/Rain v3.3/rain_v3.2.blend')
    bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
    rig=bpy.data.objects['RIG-Snow' if snow else 'RIG-rain']
    if snow:
        if rig.animation_data:
            rig.animation_data.action=None
            for track in list(rig.animation_data.nla_tracks):rig.animation_data.nla_tracks.remove(track)
        rig.pose.bones['Properties']['ik_left_upperarm']=0.
        rig.pose.bones['Properties']['ik_right_upperarm']=0.
        rig.pose.bones['Properties_Character_Snow']['Quality']=2
    else:
        rig.pose.bones['Properties_IKFK']['ik_arm_left']=0.
        rig.pose.bones['Properties_IKFK']['ik_arm_right']=0.
    rig.update_tag();bpy.context.view_layer.update()
    for side,deg in [('L',68),('R',-71 if snow else -64)]:
        rotate(rig.pose.bones[('FK-UpperArm.' if snow else 'FK-Upperarm.')+side],'Y',deg)
    if snow:
        for side,deg in [('L',-14),('R',-9)]:rotate(rig.pose.bones['FK-Forearm.'+side],'X',deg)
        for side,deg in [('L',-5),('R',5)]:rotate(rig.pose.bones['FK-Wrist.'+side],'Z',deg)
        for side in ('L','R'):
            for finger in ('Index','Middle','Ring','Pinky'):
                for segment,deg in ((1,8),(2,14),(3,9)):
                    b=rig.pose.bones.get(f'FK-Finger_{finger}{segment}.{side}')
                    if b:b.rotation_mode='XYZ';b.rotation_euler.x=math.radians(deg)
    rig.update_tag();bpy.context.view_layer.update()
    used={'FK-Spine','FK-RibCage','FK-Neck','FK-Head'}
    prefixes=['FK-UpperArm.','FK-Forearm.','FK-Wrist.','DEF-Thigh_1.','DEF-Knee_1.','DEF-Foot.'] if snow else ['FK-Upperarm.','FK-Forearm.','FK-Hand.','DEF-Thigh1.','DEF-Shin1.','DEF-Foot.']
    used.update(prefix+side for prefix in prefixes for side in ('L','R'))
    bones={b.name:{'head':list(rig.matrix_world@b.head),'tail':list(rig.matrix_world@b.tail)} for b in rig.pose.bones if b.name in used}
    body=bpy.data.objects['GEO-snow-body' if snow else 'GEO-rain-body']
    for mod in body.modifiers:
        if mod.type=='SUBSURF':mod.levels=min(2,mod.render_levels)
    bpy.context.view_layer.update()
    ev=body.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
    points=[ev.matrix_world@v.co for v in mesh.vertices]
    bounds=[[min(p[k] for p in points) for k in range(3)],[max(p[k] for p in points) for k in range(3)]]
    ev.to_mesh_clear()
    results[silhouette]={'source':str(source),'bodyBounds':bounds,'bones':bones}
(ROOT/'game'/'source-joints.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
