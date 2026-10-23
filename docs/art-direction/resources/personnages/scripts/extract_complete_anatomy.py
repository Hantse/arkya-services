"""Preserve a complete, posed Rain body before its presentation clothing masks.

The archived game study had permanently applied its jeans mask. The new open
sandals and skirt require actual skin surfaces, including knees and feet.
Uses only the already installed, credited Blender Studio source.
"""
import bpy
import math
import sys
from pathlib import Path
from mathutils import Matrix

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(Path(__file__).resolve().parent))
import generate_characters as studies

source=Path("D:/Tools/BlenderPortable/character-sources/rain-v3/Rain v3.3/rain_v3.2.blend")
bpy.ops.wm.open_mainfile(filepath=str(source),use_scripts=False)
bpy.context.preferences.filepaths.save_version=0
if bpy.context.object and bpy.context.object.mode!="OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")
rig=bpy.data.objects["RIG-rain"]
rig.pose.bones["Properties_IKFK"]["ik_arm_left"]=0.
rig.pose.bones["Properties_IKFK"]["ik_arm_right"]=0.
rig.update_tag();bpy.context.view_layer.update()
for side,degrees in (("L",68),("R",-64)):
    b=rig.pose.bones["FK-Upperarm."+side]
    m=b.matrix.copy();p=b.head.copy()
    b.matrix=Matrix.Translation(p) @ Matrix.Rotation(math.radians(degrees),4,"Y") @ Matrix.Translation(-p) @ m
    bpy.context.view_layer.update()
body=bpy.data.objects["GEO-rain-body"]
for mod in list(body.modifiers):
    if mod.type in ("MASK","VERTEX_WEIGHT_MIX"):
        body.modifiers.remove(mod)
    elif mod.type=="SUBSURF":
        mod.levels=1;mod.render_levels=1
body.hide_set(False);body.hide_viewport=False
studies.select(body)
bpy.ops.object.convert(target="MESH")
body=bpy.context.object
body.animation_data_clear()
world=body.matrix_world.copy();body.parent=None;body.matrix_world=world
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
studies.bake_skin(body)
studies.simplify(body,18000)
for o in list(bpy.data.objects):
    if o!=body:bpy.data.objects.remove(o,do_unlink=True)
body.name="CompleteRainBody"
body.data.materials.clear()
mat=bpy.data.materials.new("Skin");mat.use_nodes=True;mat.diffuse_color=(1,1,1,1)
mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value=(1,1,1,1)
body.data.materials.append(mat)
for p in body.data.polygons:p.material_index=0;p.use_smooth=True
for _ in range(3):
    bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
bpy.context.scene["attribution"]="Rain by Blender Studio / Blender Foundation, CC BY 4.0. Complete-body mask removal, static pose, neutral vertex colour bake and reduction by Arkya. See ../CREDITS.md."
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/"game"/"complete-rain-anatomy.blend"),compress=True)
print("COMPLETE_RAIN_BODY",len(body.data.vertices),studies.triangles(body))
