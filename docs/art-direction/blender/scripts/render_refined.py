"""Rebuild and render v2 studies from preserved v1 scenes and credited hero assets."""
import argparse
import importlib.util
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector

sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parents[1]

def module(name):
    spec=importlib.util.spec_from_file_location(name,BASE/'scripts'/f'{name}.py')
    mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod);return mod

def camera(name,loc,target,lens):
    d=bpy.data.cameras.new(name);d.lens=lens;d.clip_end=400
    o=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(o)
    o.location=loc;o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o

def append_hero(path):
    with bpy.data.libraries.load(str(path),link=False) as (available,loaded):loaded.collections=['Arkya_Hero']
    collection=loaded.collections[0];bpy.context.scene.collection.children.link(collection)
    return collection,collection.objects.get('Hero_Root')

def config(draft=False):
    scene=bpy.context.scene
    scene.render.engine='CYCLES';scene.cycles.samples=24 if draft else 128
    scene.cycles.use_denoising=True;scene.cycles.max_bounces=8
    scene.cycles.adaptive_threshold=.035 if draft else .015
    scene.cycles.transparent_max_bounces=12;scene.cycles.sample_clamp_indirect=5
    prefs=bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type='OPTIX';prefs.refresh_devices()
    for d in prefs.devices:d.use=d.type=='OPTIX'
    scene.cycles.device='GPU'
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGB'
    scene.render.film_transparent=False
    scene.render.use_persistent_data=True
    bpy.context.preferences.filepaths.save_version=0

def main():
    p=argparse.ArgumentParser();p.add_argument('--scene',choices=['ether','ancients'],required=True)
    p.add_argument('--draft',action='store_true');p.add_argument('--portrait',action='store_true')
    p.add_argument('--reuse',action='store_true');args=p.parse_args(sys.argv[sys.argv.index('--')+1:])
    stem,refine,asset,prefix,loc=('02-cites-ether','refine_cites_ether','snow-ether','Traveler',(-.7,-5.4,1.25)) if args.scene=='ether' else ('03-terres-anciens','refine_terres_anciens','rain-ancients','Ranger',(.4,-6.7,.143))
    destination=Path('D:/Tools/BlenderPortable/arkya-previews') if args.draft else BASE
    if args.portrait:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        coll,root=append_hero(BASE/'assets'/f'{asset}.blend')
        w=bpy.data.worlds.new('Portrait studio');w.use_nodes=True
        w.node_tree.nodes['Background'].inputs['Color'].default_value=(.055,.075,.10,1)
        w.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
        bpy.context.scene.world=w
        for pos,power,size,color in [((2,-3.5,3.5),300,3,(1,.86,.7)),((-3,-1,2.2),180,3,(.68,.83,1)),((1,2.5,3),380,2,(1,.78,.48))]:
            d=bpy.data.lights.new('Portrait softbox','AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color
            o=bpy.data.objects.new('Portrait softbox',d);bpy.context.scene.collection.objects.link(o);o.location=pos
            o.rotation_euler=(Vector((0,0,1))-o.location).to_track_quat('-Z','Y').to_euler()
        # A full body portrait retains the feet and the head for judging anatomy.
        cam=camera('Camera_Character',(1.8,-5,2.15),(0,0,1.03),75)
        bpy.context.scene.view_settings.view_transform='AgX';bpy.context.scene.view_settings.look='AgX - Medium High Contrast'
        bpy.context.scene.camera=cam
        suffix='character-v2'
    elif args.reuse:
        bpy.ops.wm.open_mainfile(filepath=str(BASE/f'{stem}-v2.blend'))
        bpy.context.scene.camera=bpy.data.objects['Camera_Refined'];suffix='v2-render'
    else:
        bpy.ops.wm.open_mainfile(filepath=str(BASE/f'{stem}.blend'))
        notes=module(refine).refine_scene()
        for o in list(bpy.context.scene.objects):
            if o.name.startswith(prefix):bpy.data.objects.remove(o,do_unlink=True)
        coll,root=append_hero(BASE/'assets'/f'{asset}.blend')
        root.location=loc;root.rotation_euler.z=math.radians(160 if args.scene=='ether' else 165)
        bpy.context.scene.camera=bpy.data.objects['Camera_Refined']
        bpy.context.scene['Arkya_v2_notes']=json.dumps(notes)
        bpy.context.scene['Arkya_character_credit']='Rain/Snow by Blender Studio / Blender Foundation, CC BY 4.0. See CREDITS.md. Posed and adapted for this study.'
        suffix='v2-render'
    scene=bpy.context.scene;config(args.draft)
    scene.render.resolution_x=(720 if args.draft else 1200) if args.portrait else (1080 if args.draft else 2160)
    scene.render.resolution_y=(960 if args.draft else 1600) if args.portrait else (720 if args.draft else 1440)
    scene.render.filepath=str(destination/f'{stem}-{suffix}.png')
    if not args.portrait:
        bpy.ops.file.pack_all()
        bpy.ops.wm.save_as_mainfile(filepath=str((destination if args.draft else BASE)/f'{stem}-v2.blend'),compress=True)
    bpy.ops.render.render(write_still=True)
    info={'scene':args.scene,'portrait':args.portrait,'engine':'Cycles','samples':scene.cycles.samples,'resolution':[scene.render.resolution_x,scene.render.resolution_y],
          'blender':bpy.app.version_string,'objects':len(scene.objects),'meshes':sum(o.type=='MESH' for o in scene.objects),'camera':scene.camera.name,
          'images':[{'name':i.name,'packed':bool(i.packed_file or len(i.packed_files)),'source':i.source} for i in bpy.data.images if i.source in {'FILE','TILED'}]}
    (destination/f'{stem}-{suffix}-info.json').write_text(json.dumps(info,indent=2),encoding='utf-8')
    print('REFINED_RENDER_COMPLETE',json.dumps(info),flush=True)

if __name__=='__main__':main()
