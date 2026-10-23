"""Independently re-import the delivery FBX files and verify deforming skins."""
import bpy, json, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]/'game'
reports=[]
for slug in ('human-a','human-b','elf-a','elf-b'):
    authored=json.loads((ROOT/f'{slug}.validation.json').read_text(encoding='utf-8'))
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(ROOT/f'{slug}.fbx'),use_anim=True,colors_type='LINEAR')
    rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE'];assert len(rigs)==1
    rig=rigs[0];assert len(rig.data.bones)==26
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    assert {o.name for o in meshes}=={'CharacterMesh','HairMain','HairLong','HairBraid'}
    errors=[];worst=0;max_influences=0;bounds=[]
    for o in meshes:
        assert all(len(p.vertices)==3 for p in o.data.polygons),(slug,o.name,'non-triangle')
        assert all(p.area>0 for p in o.data.polygons),(slug,o.name,'degenerate triangle')
        expected_faces=next(m['triangles'] for m in authored['meshes'] if m['name']==o.name)
        assert len(o.data.polygons)==expected_faces,(slug,o.name,'triangle loss')
        assert o.data.color_attributes.get('Painted'),o.name
        assert len([a for a in o.data.color_attributes if a.name=='Painted'])==1,o.name
        assert any(m.type=='ARMATURE' and m.object==rig for m in o.modifiers),o.name
        for v in o.data.vertices:
            weights=[g.weight for g in v.groups if g.weight>1e-6]
            assert weights,o.name
            max_influences=max(max_influences,len(weights));worst=max(worst,abs(sum(weights)-1))
            assert all(math.isfinite(x) for x in v.co)
            bounds.append(o.matrix_world@v.co)
    assert max_influences<=4 and worst<1e-5
    actions={a.name.split('|')[-1]:a for a in bpy.data.actions if not a.name.startswith('Scene')}
    assert {'Idle','Walk','Run'}<=actions.keys(),list(actions)
    sampled=[]
    main=bpy.data.objects['CharacterMesh']
    for name in ('Idle','Walk','Run'):
        action=actions[name];rig.animation_data.action=action
        if action.slots:rig.animation_data.action_slot=action.slots[0]
        start,end=action.frame_range
        bpy.context.scene.frame_set(int(start))
        ev=main.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        first=[v.co.copy() for v in mesh.vertices];ev.to_mesh_clear()
        bpy.context.scene.frame_set(int(start+(end-start)*.25))
        ev=main.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        movement=max((main.matrix_world.to_3x3()@(v.co-first[v.index])).length for v in mesh.vertices);ev.to_mesh_clear()
        assert movement>.002,(slug,name,movement)
        bpy.context.scene.frame_set(int(end))
        ev=main.evaluated_get(bpy.context.evaluated_depsgraph_get());mesh=ev.to_mesh()
        loop_error=max((main.matrix_world.to_3x3()@(v.co-first[v.index])).length for v in mesh.vertices);ev.to_mesh_clear()
        assert loop_error<.0001,(slug,name,loop_error)
        sampled.append({'name':name,'durationSeconds':float((end-start)/bpy.context.scene.render.fps),'maxVertexDisplacementMeters':movement,'loopPositionErrorMeters':loop_error})
    low=[min(p[k] for p in bounds) for k in range(3)];high=[max(p[k] for p in bounds) for k in range(3)]
    assert abs(low[2])<1e-4,(slug,low)
    # Body height remains the appearance scale reference; decorative high hair
    # and crown geometry must survive export at its independently measured size.
    expected=authored.get('boundsBlenderZUp',[None,[None,None,1.85 if slug.startswith('elf') else 1.75]])[1][2]
    assert abs(high[2]-expected)<.001,(slug,high,expected)
    assert set(authored['boneNames'])=={b.name for b in rig.data.bones}
    reports.append({'id':slug,'fbxRoundTrip':'passed','bones':len(rig.data.bones),'meshes':len(meshes),'maxInfluences':max_influences,'maxWeightSumError':worst,'bodyReferenceHeightMeters':authored.get('bodyReferenceHeightMeters',1.85 if slug.startswith('elf') else 1.75),'boundsBlenderZUp':[low,high],'uniquePaintedAttribute':True,'explicitTrianglesPreserved':True,'degenerateTriangles':0,'clips':sampled})
(ROOT/'fbx-roundtrip-validation.json').write_text(json.dumps({'schemaVersion':1,'results':reports},indent=2),encoding='utf-8')
print(json.dumps(reports))
