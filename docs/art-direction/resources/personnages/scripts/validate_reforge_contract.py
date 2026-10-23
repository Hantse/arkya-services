"""Compare new skeleton rest matrices to the untouched study-derived rig."""
import bpy
import json
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import rig_characters

ROOT=Path(__file__).resolve().parents[1]
reports=[]
for slug in ("human-a","human-b","elf-a","elf-b"):
    race,silhouette=slug.split("-")
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/"assets"/f"{slug}-short.blend"),use_scripts=False)
    rig,_=rig_characters.skeleton(silhouette,race)
    source={b.name:(b.matrix_local.copy(),b.parent.name if b.parent else None) for b in rig.data.bones}
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/"game"/f"{slug}.blend"),use_scripts=False)
    current=bpy.data.objects["Armature"]
    assert set(source)=={b.name for b in current.data.bones}
    worst=0
    for bone in current.data.bones:
        matrix,parent=source[bone.name]
        assert parent==(bone.parent.name if bone.parent else None)
        worst=max(worst,max(abs(matrix[r][c]-bone.matrix_local[r][c]) for r in range(4) for c in range(4)))
    assert worst<1e-7,(slug,worst)
    meshes=[o for o in bpy.context.scene.objects if o.type=="MESH"]
    for o in meshes:
        assert len(o.data.color_attributes)==1
        assert o.data.color_attributes[0].name=="Painted"
        assert o.data.color_attributes[0].domain=="CORNER"
    for name in ("Skin","Hair","Iris","OutfitAccent"):
        assert tuple(bpy.data.materials[name].diffuse_color)==(1,1,1,1)
    reports.append({"id":slug,"originalRigRestMatrices":"identical",
                    "maximumMatrixElementError":worst,"parentHierarchy":"identical",
                    "uniqueCornerPaintedLayer":True,"customizableMaterialsNeutral":True})
(ROOT/"game"/"reforge-contract-validation.json").write_text(
    json.dumps({"schemaVersion":1,"results":reports},indent=2),encoding="utf-8")
print(json.dumps(reports))
