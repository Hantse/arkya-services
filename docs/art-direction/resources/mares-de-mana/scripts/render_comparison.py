"""All three mana silhouettes in blue, one orthographic camera and actual metre scale."""
import json
from pathlib import Path
import sys
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import generate_mana_pool as base
import generate_mana_shapes as shapes

shapes.verify_reference()
manifest = json.loads((ROOT / 'manifest.json').read_text(encoding='utf-8'))
records = [next(a for a in manifest['assets'] if a['shape'] == shape and a['material'] == 'bleu') for shape in ['allongee', 'mare', 'lobee']]
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene; scene.render.fps = 24; scene.frame_start = 1; scene.frame_end = 145
gap = .58; total = sum(a['width'] for a in records) + gap * 2
cursor = -total / 2; centres = []; geometry = []
for record in records:
    centre = cursor + record['width'] / 2; centres.append(centre)
    with bpy.data.libraries.load(str(ROOT / record['blend']), link=False) as (available, loaded):
        loaded.objects = available.objects
    parent = bpy.data.objects.new('Same-scale placement | ' + record['shape'], None); scene.collection.objects.link(parent); parent.location.x = centre
    for obj in loaded.objects:
        if obj.type == 'MESH' and not obj.name.startswith('Preview floor'):
            scene.collection.objects.link(obj); obj.parent = parent; geometry.append(obj)
    cursor += record['width'] + gap
camera = base.studio(geometry, False)
scene.frame_set(43)
camera.location = (0, -11, 10); camera.rotation_euler = (Vector((0, -.16, .18)) - camera.location).to_track_quat('-Z', 'Y').to_euler(); camera.data.ortho_scale = total + .5
for obj in scene.objects:
    if obj.type == 'LIGHT':
        obj.location.x *= 1.9; obj.location.y *= 1.3; obj.location.z *= 1.4; obj.data.energy *= 3.2; obj.data.size *= 1.6
        obj.rotation_euler = (Vector((0, 0, .2)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
mat = base.basic_material('Readable comparison labels', (.045, .075, .095), 1)
for record, centre in zip(records, centres):
    label = bpy.data.curves.new('Comparison label', 'FONT'); label.align_x = 'CENTER'; label.size = .125; label.space_line = 1.12
    name = record['label'].split(' · ')[0]
    label.body = name + '\n' + ('%.2f × %.2f m' % (record['width'], record['depth'])).replace('.', ',')
    obj = bpy.data.objects.new('Label | ' + record['shape'], label); scene.collection.objects.link(obj)
    obj.location = (centre, -1.62, .10); obj.rotation_euler = camera.rotation_euler; label.materials.append(mat)
scene.render.resolution_x = 2400; scene.render.resolution_y = 1100; scene.render.resolution_percentage = 100
scene.render.filepath = str(ROOT / 'comparaison-tailles.png'); bpy.ops.render.render(write_still=True)
bpy.context.preferences.filepaths.save_version = 0; bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'comparaison-tailles.blend'))
manifest['comparison'] = 'comparaison-tailles.png'; (ROOT / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('MANA_COMPARISON_READY', shapes.verify_reference(), flush=True)
