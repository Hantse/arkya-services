"""Versioned third-pass Blender renders. V1 and V2 remain unchanged."""
import argparse
import importlib.util
import json
import math
import sys
from pathlib import Path
import bpy
from mathutils import Vector,Matrix

sys.dont_write_bytecode=True
BASE=Path(__file__).resolve().parents[1]
PREVIEWS=Path('D:/Tools/BlenderPortable/arkya-previews')
def load_module(name):
    spec=importlib.util.spec_from_file_location(name,BASE/'scripts'/f'{name}.py')
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

def camera(name,loc,target,lens):
    d=bpy.data.cameras.new(name);d.lens=lens;d.clip_end=500
    o=bpy.data.objects.new(name,d);bpy.context.scene.collection.objects.link(o);o.location=loc
    o.rotation_euler=(Vector(target)-o.location).to_track_quat('-Z','Y').to_euler();return o

def append_hero(path):
    with bpy.data.libraries.load(str(path),link=False) as (source,loaded):loaded.collections=['Arkya_Hero']
    col=loaded.collections[0];bpy.context.scene.collection.children.link(col)
    return col,col.objects['Hero_Root']

def place_instance(col,root,name,position,angle,scale=1):
    o=bpy.data.objects.new(name,None);o.instance_type='COLLECTION';o.instance_collection=col
    bpy.context.scene.collection.objects.link(o)
    target=Matrix.Translation(Vector(position)) @ Matrix.Rotation(math.radians(angle),4,'Z') @ Matrix.Scale(scale,4)
    bpy.context.view_layer.update();o.matrix_world=target @ root.matrix_world.inverted()
    return o

def portrait_lighting():
    s=bpy.context.scene
    world=bpy.data.worlds.new('V3 portrait cool ambient');world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.035,.055,.085,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.38;s.world=world
    for position,power,size,color in [((-2.8,-3.8,4),420,2.5,(1,.84,.67)),((2.4,-2,2.1),150,3,(.6,.78,1)),((1.6,2.3,3.2),600,2,(.79,.89,1))]:
        d=bpy.data.lights.new('V3 portrait softbox','AREA');d.energy=power;d.size=size;d.color=color
        o=bpy.data.objects.new(d.name,d);s.collection.objects.link(o);o.location=position
        o.rotation_euler=(Vector((0,0,1.05))-o.location).to_track_quat('-Z','Y').to_euler()
    # A matte ground gives the boots weight and a visible contact shadow.
    profile=[(-30,-.006),(-29,-.006),(-2,-.006),(2,-.006),(3.5,-.006),(4,0),(5,.4),(5.8,1.2),(6.2,2.2),(6.4,3.5),(6.4,12),(6.4,13)]
    verts=[(x,y,z) for y,z in profile for x in [-25,-24,24,25]]
    faces=[]
    for j in range(len(profile)-1):
        for i in range(3):
            q=j*4+i;faces.append((q,q+1,q+5,q+4))
    data=bpy.data.meshes.new('Seamless studio backdrop');data.from_pydata(verts,[],faces);data.update()
    floor=bpy.data.objects.new('Portrait seamless charcoal cyclorama',data);s.collection.objects.link(floor)
    for p in data.polygons:p.use_smooth=True
    sub=floor.modifiers.new('Continuous cove','SUBSURF');sub.levels=sub.render_levels=2
    m=bpy.data.materials.new('V3 charcoal studio floor');m.use_nodes=True
    p=m.node_tree.nodes['Principled BSDF'];p.inputs['Base Color'].default_value=(.022,.032,.047,1);p.inputs['Roughness'].default_value=.76
    floor.data.materials.append(m)
    s.view_settings.view_transform='AgX';s.view_settings.look='AgX - Medium High Contrast';s.view_settings.exposure=0
    s.camera=camera('Camera_Character_V3',(1.65,-5.2,2.1),(0,0,.99),82)

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--scene',choices=['ether','ancients'],required=True)
    parser.add_argument('--draft',action='store_true');parser.add_argument('--portrait',action='store_true')
    parser.add_argument('--environment-only',action='store_true');parser.add_argument('--reuse',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    stem,asset,environment=('02-cites-ether','snow-ether','refine_ether_v3_environment') if args.scene=='ether' else ('03-terres-anciens','rain-ancients','refine_ancients_v3_environment')
    notes={}
    if args.portrait:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        col,root=append_hero(BASE/'assets'/f'{asset}-v3.blend');portrait_lighting()
    elif args.reuse:
        bpy.ops.wm.open_mainfile(filepath=str(BASE/f'{stem}-v3.blend'),use_scripts=False)
        bpy.context.scene.camera=bpy.data.objects['Camera_V3']
    else:
        bpy.ops.wm.open_mainfile(filepath=str(BASE/f'{stem}-v2.blend'),use_scripts=False)
        notes=load_module(environment).refine_scene()
        old=bpy.data.collections['Arkya_Hero']
        if not args.environment_only:
            for o in list(old.all_objects):bpy.data.objects.remove(o,do_unlink=True)
            bpy.data.collections.remove(old)
            col,root=append_hero(BASE/'assets'/f'{asset}-v3.blend')
        else:col,root=old,old.objects['Hero_Root']
        root.location=notes.get('hero_position',[-.6,-7.6,1.25] if args.scene=='ether' else [0,-8,.2])
        root.rotation_euler.z=math.radians(155)
        if not args.environment_only:
            if args.scene=='ether':
                for name,pos,angle,scale in [('Market visitor',(2.8,2.4,1.25),-58,.93),('Gallery sentry',(-3.1,3.6,1.25),35,.98)]:
                    place_instance(col,root,name,pos,angle,scale)
            else:
                positions=notes.get('npc_positions',[])
                for i,pos in enumerate(positions):place_instance(col,root,'Woodland resident '+str(i),pos,40+i*125,.94)
        bpy.context.scene.camera=bpy.data.objects.get('Camera_V3',bpy.context.scene.camera)
        bpy.context.scene['Arkya_V3_notes']=json.dumps(notes)
        bpy.context.scene['Arkya_character_credit']='Rain/Snow by Blender Studio / Blender Foundation, CC BY 4.0; adapted costumes, proportions and materials. See CREDITS.md.'
        bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
    scene=bpy.context.scene;load_module('render_refined').config(args.draft)
    scene.cycles.samples=32 if args.draft else 160
    scene.cycles.adaptive_threshold=.028 if args.draft else .012
    scene.render.resolution_x=(750 if args.draft else 1350) if args.portrait else (1280 if args.draft else 2560)
    scene.render.resolution_y=(1000 if args.draft else 1800) if args.portrait else (720 if args.draft else 1440)
    destination=PREVIEWS if args.draft else BASE
    suffix='character-v3' if args.portrait else 'v3-render'
    scene.render.filepath=str(destination/f'{stem}-{suffix}.png')
    if not args.portrait:
        bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(destination/f'{stem}-v3.blend'),compress=True)
    bpy.ops.render.render(write_still=True)
    images=[{'name':im.name,'packed':bool(im.packed_file or im.packed_files),'source':im.source} for im in bpy.data.images if im.source in {'FILE','TILED'}]
    info={'scene':args.scene,'version':3,'portrait':args.portrait,'blender':bpy.app.version_string,'engine':'Cycles','samples':scene.cycles.samples,'resolution':[scene.render.resolution_x,scene.render.resolution_y],'camera':scene.camera.name,'objects':len(scene.objects),'textures':images,'notes':notes}
    (destination/f'{stem}-{suffix}-info.json').write_text(json.dumps(info,indent=2),encoding='utf-8')
    print('V3_RENDER_COMPLETE',json.dumps(info),flush=True)

if __name__=='__main__':main()
