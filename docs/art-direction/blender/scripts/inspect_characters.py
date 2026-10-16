import bpy
import json
import sys
from pathlib import Path

out = Path('D:/Tools/BlenderPortable/character-sources')
kind = sys.argv[sys.argv.index('--') + 1]
summary = {'objects': [], 'collections': list(bpy.data.collections.keys()), 'materials': list(bpy.data.materials.keys()), 'texts': list(bpy.data.texts.keys())}
for obj in bpy.context.scene.objects:
    if obj.type in {'MESH', 'ARMATURE'}:
        item = {'name':obj.name, 'type':obj.type, 'location':list(obj.location), 'dimensions':list(obj.dimensions), 'hide_render':obj.hide_render, 'collections':[c.name for c in obj.users_collection]}
        if obj.type=='ARMATURE':
            item['bones']=[{'name':b.name,'head':list(b.head),'tail':list(b.tail),'parent':b.parent.name if b.parent else None} for b in obj.data.bones]
        else:
            item['materials']=[m.name if m else None for m in obj.data.materials]
            item['modifiers']=[(m.name,m.type) for m in obj.modifiers]
        summary['objects'].append(item)
summary['images']=[{'name':i.name,'path':i.filepath,'source':i.source} for i in bpy.data.images]
(out/f'{kind}-inventory.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print('INVENTORY',kind,len(summary['objects']))
