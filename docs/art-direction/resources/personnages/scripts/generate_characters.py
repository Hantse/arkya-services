"""Static character creation studies adapted from credited Blender Studio Rain/Snow.

Run Blender 4.5 with --background --disable-autoexec --python this_file.
Preserved V3 study sources are read only. Outputs are review assets, not game rigs.
"""
import argparse
import bpy
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parents[1] / 'blender' / 'assets'
STAGING = ROOT.parents[3] / 'artifacts' / 'characters' / 'staging'
CREDIT = 'Adapted from Rain and Snow by Blender Studio / Blender Foundation, CC BY 4.0. Arkya modifications: static pose, costumes, proportions, surface paint, elf ears, hairstyles and optimization. See CREDITS.md.'
M = {}


def select(obj):
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj


def material(name, color=(1,1,1), roughness=.72, metallic=0):
    m=bpy.data.materials.new(name);m.use_nodes=True
    bs=m.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value=(*color,1)
    bs.inputs['Roughness'].default_value=roughness
    bs.inputs['Metallic'].default_value=metallic
    m.diffuse_color=(*color,1)
    m.use_backface_culling=False
    return m


def setup_materials():
    global M
    for m in list(bpy.data.materials):
        if m.users==0:bpy.data.materials.remove(m)
    M={
        'Skin':material('Skin',roughness=.76),
        'Hair':material('Hair',roughness=.63),
        'Iris':material('Iris',roughness=.33),
        'OutfitAccent':material('OutfitAccent',roughness=.86),
        'Leather':material('Leather',(.155,.073,.036),.70),
        'DarkLeather':material('DarkLeather',(.028,.037,.033),.82),
        'Metal':material('Metal',(.42,.24,.075),.48,.48),
        'Cloth':material('Cloth',(.135,.183,.13),.92),
        'Ivory':material('Ivory',(.67,.63,.48),.72,.05),
        'Gem':material('Gem',(.035,.28,.25),.31,.12),
        'EyesWhite':material('EyesWhite',(.79,.76,.67),.36),
        'Pupil':material('Pupil',(.003,.006,.008),.28),
        'Mouth':material('Mouth',(.042,.014,.012),.88),
        'Thread':material('Thread',(.38,.28,.14),.87),
    }


def triangles(obj):
    obj.data.calc_loop_triangles()
    return len(obj.data.loop_triangles)


def bake_skin(obj):
    select(obj)
    attr=obj.data.color_attributes.new(name='Painted',type='FLOAT_COLOR',domain='CORNER')
    obj.data.color_attributes.active_color=attr
    obj.data.color_attributes.render_color_index=len(obj.data.color_attributes)-1
    scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1
    scene.render.bake.target='VERTEX_COLORS'
    scene.render.bake.use_pass_direct=False;scene.render.bake.use_pass_indirect=False
    scene.render.bake.use_pass_color=True
    bpy.ops.object.bake(type='DIFFUSE')
    samples=[tuple(c.color[:3]) for c in attr.data]
    # Channel-wise normalization removes the source complexion but preserves lip
    # warmth, painted occlusion and subtle facial colour changes for live tinting.
    base=[sorted(c[k] for c in samples)[int(len(samples)*.80)] for k in range(3)]
    for c in attr.data:
        c.color=tuple(max(.11,min(1,(max(0,c.color[k])/max(.01,base[k]))**.68*.92)) for k in range(3))+(1,)
    for other_name in list(obj.data.color_attributes.keys()):
        if other_name!='Painted':obj.data.color_attributes.remove(obj.data.color_attributes[other_name])
    obj.data.color_attributes.active_color=obj.data.color_attributes['Painted']
    obj.data.color_attributes.render_color_index=0


def painted(obj, strength=.07):
    if obj.data.color_attributes.get('Painted'):return
    for old in list(obj.data.color_attributes):obj.data.color_attributes.remove(old)
    attr=obj.data.color_attributes.new(name='Painted',type='FLOAT_COLOR',domain='POINT')
    for v,c in zip(obj.data.vertices,attr.data):
        p=v.co
        broad=math.sin(p.x*20+p.z*7)*math.sin(p.y*15-p.z*10)
        brush=math.sin(p.x*70+p.z*13)*math.sin(p.y*45+p.z*32)
        value=.90+strength*(.72*broad+.28*brush)
        c.color=(value,value,value,1)
    obj.data.color_attributes.active_color=attr
    obj.data.color_attributes.render_color_index=0


def assign(obj,key):
    obj.data.materials.clear();obj.data.materials.append(M[key])
    for p in obj.data.polygons:p.material_index=0


def simplify(obj, target):
    count=triangles(obj)
    if count<=target:return
    select(obj)
    mod=obj.modifiers.new('Review mesh reduction','DECIMATE')
    mod.ratio=target/count
    mod.use_collapse_triangulate=True
    bpy.ops.object.modifier_apply(modifier=mod.name)


def eye_materials(obj):
    # Both source eyeballs contain surface rings; classify their frontal disks.
    obj.data.materials.clear()
    for name in ('EyesWhite','Iris','Pupil'):obj.data.materials.append(M[name])
    positions=[v.co for v in obj.data.vertices]
    centers={}
    for side in (-1,1):
        points=[p for p in positions if p.x*side>0]
        centers[side]=Vector(tuple((min(p[k] for p in points)+max(p[k] for p in points))/2 for k in range(3)))
    for p in obj.data.polygons:
        mid=sum((obj.data.vertices[i].co for i in p.vertices),Vector())/len(p.vertices)
        c=centers[1 if mid.x>0 else -1]
        r=(max(v.z for v in positions)-min(v.z for v in positions))/2
        rho=math.sqrt((mid.x-c.x)**2+(mid.z-c.z)**2)/r
        p.material_index=(2 if rho<.21 else 1 if rho<.51 else 0) if mid.y<c.y else 0
    painted(obj,.025)


def classify(old, name):
    text=(old+' '+name).lower()
    if 'hair' in text or 'eyebrow' in text:return 'Hair'
    if 'eyelash' in text:return 'Pupil'
    if 'coral' in text or 'plum' in text or 'shirt' in text or name=='Rain_scarf':return 'OutfitAccent'
    if any(x in text for x in ('bronze','gold','buckle','binding','eyelet','rivet')):return 'Metal'
    if any(x in text for x in ('ivory','enamel')):return 'Ivory'
    if 'amber' in text or 'crystal' in text:return 'Gem'
    if 'thread' in text or 'stitch' in text or 'embroider' in text:return 'Thread'
    if 'sage' in text or 'green' in text or 'woven' in text:return 'Cloth'
    if any(x in text for x in ('dark','sole','pants','jeans','petrol','snow.shoe')):return 'DarkLeather'
    return 'Leather'


def prepare(silhouette):
    source='snow-ether-v3' if silhouette=='a' else 'rain-ancients-v3'
    bpy.ops.wm.open_mainfile(filepath=str(SOURCE/f'{source}.blend'))
    bpy.context.preferences.filepaths.save_version=0
    bpy.context.scene['credit']=CREDIT
    skin=[]
    for o in list(bpy.context.scene.objects):
        low=o.name.lower()
        if o.type not in {'MESH','CURVE'}:
            continue
        if any(x in low for x in ('gums','teeth','tongue','cornea','eye_dots','ponytail','hairband')):
            bpy.data.objects.remove(o,do_unlink=True);continue
        # Source stitch meshes are far denser than necessary for a creation view.
        if any(x in low for x in ('blanket stitch','fine stitches','hand stitch','fine bracer hand','toe hand stitches')):
            bpy.data.objects.remove(o,do_unlink=True);continue
        if o.type=='CURVE':
            o.data.resolution_u=4;o.data.bevel_resolution=1
        for mod in o.modifiers:
            if mod.type=='SUBSURF':mod.levels=min(mod.levels,1);mod.render_levels=mod.levels
        world=o.matrix_world.copy()
        select(o);bpy.ops.object.convert(target='MESH')
        o=bpy.context.object;o.parent=None;o.matrix_world=world
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
        if low.endswith('-head') or low.endswith('_head'):
            o.name='Head';skin.append(o)
        elif low.endswith('-body') or low.endswith('_body'):
            o.name='Body';skin.append(o)
        elif low.endswith('-eyes') or low.endswith('_eyes'):o.name='Eyes'
        elif 'hair_base' in low or 'hair_main' in low:o.name='HairMain'
    for obj in list(bpy.context.scene.objects):
        if obj.type!='MESH':bpy.data.objects.remove(obj,do_unlink=True)
    for obj in skin:
        # Bake before reduction to retain source colour variation in new vertices.
        bake_skin(obj)
    setup_materials()
    for obj in list(bpy.context.scene.objects):
        old=obj.data.materials[0].name if obj.data.materials else ''
        if obj in skin:
            assign(obj,'Skin');target=15000 if obj.name=='Head' else 7000
        elif obj.name=='Eyes':
            eye_materials(obj);target=3000
        else:
            assign(obj,classify(old,obj.name));painted(obj)
            low=obj.name.lower()
            if obj.name=='HairMain':target=6000
            elif 'eyebrow' in low:target=650
            elif any(x in low for x in ('cloak','cape | coral')):target=3500
            elif any(x in low for x in ('shirt','jeans','pants','shoes','top','vest')):target=2200
            elif any(x in low for x in ('boot','bracer','shaft','tunic')):target=800
            else:target=250
        simplify(obj,target)
        for p in obj.data.polygons:p.use_smooth=True
    # Preserve objects and names for editing but discard unused packed source images.
    for img in list(bpy.data.images):
        if img.users==0:bpy.data.images.remove(img)
    return source


def tube_lock(name,path,widths,depths,material_key='Hair',twist=0):
    verts=[];faces=[];n=12
    for i,co in enumerate(path):
        co=Vector(co)
        tangent=(Vector(path[min(i+1,len(path)-1)])-Vector(path[max(0,i-1)])).normalized()
        axis=tangent.cross(Vector((0,1,0)))
        if axis.length<.1:axis=tangent.cross(Vector((1,0,0)))
        axis.normalize();other=tangent.cross(axis).normalized()
        for j in range(n):
            a=j*math.tau/n+twist*i
            p=co+axis*(widths[i]*math.cos(a))+other*(depths[i]*math.sin(a))
            verts.append(tuple(p))
    for i in range(len(path)-1):
        for j in range(n):
            a=i*n+j;b=i*n+(j+1)%n;faces.append((a,b,b+n,a+n))
    faces.extend([tuple(range(n-1,-1,-1)),tuple((len(path)-1)*n+j for j in range(n))])
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(verts,[],faces);mesh.update()
    obj=bpy.data.objects.new(name,mesh);bpy.context.scene.collection.objects.link(obj)
    assign(obj,material_key);painted(obj,.11)
    for p in mesh.polygons:p.use_smooth=True
    return obj


def add_hair(style,silhouette):
    if style=='short':return
    head=bpy.data.objects['Head']
    top=max(v.co.z for v in head.data.vertices)
    if style=='long':
        # Broad tapered locks layer over the back and turn inwards at their tips.
        for j in range(7):
            u=(j-3)/3
            path=[];w=[];d=[]
            for i in range(23):
                t=i/22
                x=u*(.070+.065*math.sin(math.pi*t*.85))+.009*math.sin(t*6+j)
                y=.060+.105*math.sin(t*math.pi*.68)+.025*t
                z=top-.045-.52*t+.022*abs(u)
                path.append((x,y,z));w.append((.027+.014*math.sin(math.pi*t))*(1-.88*t**5));d.append(.017*(1-.8*t**5))
            tube_lock(f'HairLong_Back_{j}',path,w,d)
        for side in (-1,1):
            path=[];w=[];d=[]
            for i in range(24):
                t=i/23
                path.append((side*(.086+.072*t-.024*t*t),-.025-.027*math.sin(math.pi*t),top-.085-.39*t))
                w.append(.028*(1-.91*t**4));d.append(.016*(1-.82*t**3))
            tube_lock(f'HairLong_Side_{side}',path,w,d)
    else:
        # Three interlaced volumes with alternating depth create a readable braid.
        for strand in range(3):
            path=[];w=[];d=[]
            for i in range(76):
                t=i/75;a=t*math.tau*5+strand*math.tau/3
                radius=.020*(1-.78*t)
                path.append((math.cos(a)*radius,.095+.065*math.sin(t*math.pi*.7)+math.sin(a)*radius*.7,top-.055-.60*t))
                w.append(.017*(1-.62*t));d.append(.013*(1-.55*t))
            tube_lock(f'HairBraid_Strand_{strand}',path,w,d)
        for t in (.03,.94):
            p=(0,.095+.065*math.sin(t*math.pi*.7),top-.055-.60*t)
            path=[(p[0]+.031*(1-.5*t)*math.cos(i*math.tau/32),p[1]+.024*(1-.5*t)*math.sin(i*math.tau/32),p[2]) for i in range(33)]
            tube_lock(f'HairBraid_Clasp_{t}',path,[.004]*33,[.004]*33,'Metal')


def elf_shape(silhouette):
    # Change the existing ear silhouette continuously; no pasted-on ear props.
    head=bpy.data.objects['Head']
    ear_center=1.66 if silhouette=='a' else 1.625
    ear_root=.094 if silhouette=='a' else .103
    for v in head.data.vertices:
        p=v.co; ax=abs(p.x)
        if ax>ear_root and p.y>-.055 and abs(p.z-ear_center)<.105:
            t=max(0,min(1,(ax-ear_root)/.035))
            p.x+=math.copysign(.105*t**2.2,p.x)
            p.z+=(ear_center+.105-p.z)*t**3*.92
            p.y=p.y*(1-.65*t**3)+.015*t
    for obj in bpy.context.scene.objects:
        if obj.type!='MESH':continue
        for v in obj.data.vertices:
            # Subtle full-silhouette distinction, including neck and costume.
            v.co.x*=.92;v.co.y*=.95


def normalize(height):
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    points=[v.co for o in meshes for v in o.data.vertices]
    low=min(p.z for p in points);high=max(p.z for p in points)
    scale=height/(high-low)
    for o in meshes:
        for v in o.data.vertices:
            v.co.z-=low;v.co*=scale
    return meshes


def export_variant(race,silhouette,style,source):
    add_hair(style,silhouette)
    if race=='elf':elf_shape(silhouette)
    height=1.85 if race=='elf' else 1.75
    meshes=normalize(height)
    for obj in meshes:
        for old_name in list(obj.data.color_attributes.keys()):
            if old_name!='Painted':obj.data.color_attributes.remove(obj.data.color_attributes[old_name])
        attr=obj.data.color_attributes.get('Painted')
        if attr:
            obj.data.color_attributes.active_color=attr
            obj.data.color_attributes.render_color_index=0
            for item in attr.data:item.color=tuple(max(0,min(1,c)) for c in item.color)
    slug=f'{race}-{silhouette}-{style}'
    root=bpy.data.objects.new('Character',None);bpy.context.scene.collection.objects.link(root)
    root['attribution']=CREDIT;root['race']=race;root['silhouette']=silhouette;root['hairstyle']=style
    root['referenceHeightMeters']=height;root['reviewOnly']=True
    for o in meshes:o.parent=root
    bpy.context.scene['asset_description']='Static editable character creation art study. No skeleton, animation, LOD or gameplay integration.'
    bpy.context.scene['attribution']=CREDIT
    # Unused source nodes/images are removed so the delivered editable file is compact.
    for _ in range(3):bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
    blend=ROOT/'assets'/f'{slug}.blend';glb=ROOT/'models'/f'{slug}.glb'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.export_scene.gltf(filepath=str(glb),export_format='GLB',use_selection=True,
        export_apply=True,export_yup=True,export_extras=True,export_materials='EXPORT',
        export_vertex_color='NAME',export_vertex_color_name='Painted',export_all_vertex_colors=False,export_animations=False,
        export_cameras=False,export_lights=False)
    # glTF stays neutral for the live selector. The editable Blender source opens
    # with an example appearance and explicitly displays its baked vertex paint.
    preview={'Skin':(.53,.31,.19),'Hair':(.085,.038,.017),'Iris':(.09,.24,.13),'OutfitAccent':(.12,.23,.18)}
    for mat in {m for o in meshes for m in o.data.materials if m}:
        bs=mat.node_tree.nodes.get('Principled BSDF')
        base=preview.get(mat.name,tuple(bs.inputs['Base Color'].default_value[:3]))
        col=mat.node_tree.nodes.new('ShaderNodeVertexColor');col.layer_name='Painted'
        mix=mat.node_tree.nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=1
        mix.inputs[1].default_value=(*base,1);mix.label='Editable appearance tint'
        mat.node_tree.links.new(col.outputs['Color'],mix.inputs[2]);mat.node_tree.links.new(mix.outputs[0],bs.inputs['Base Color'])
        mat.diffuse_color=(*base,1)
    bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    points=[v.co for o in meshes for v in o.data.vertices]
    bounds=[[min(p[k] for p in points) for k in range(3)],[max(p[k] for p in points) for k in range(3)]]
    result={'id':slug,'race':race,'silhouette':silhouette,'hair':style,'glb':f'models/{slug}.glb','blend':f'assets/{slug}.blend','triangles':sum(triangles(o) for o in meshes),'heightMeters':height,'boundsBlenderZUp':bounds,'materials':sorted({m.name for o in meshes for m in o.data.materials if m}),'source':source,'vertexPainted':True}
    print('CHARACTER_EXPORTED',json.dumps(result),flush=True)
    return result


def main():
    for folder in ('models','assets'): (ROOT/folder).mkdir(exist_ok=True,parents=True)
    STAGING.mkdir(exist_ok=True,parents=True)
    parser=argparse.ArgumentParser();parser.add_argument('--only',default='');parser.add_argument('--reuse-staging',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    results=[]
    # Prepared neutral base is reused, preserving source textures only in bake.
    for silhouette in ('a','b'):
        stage=STAGING/f'base-{silhouette}.blend'
        if args.reuse_staging and stage.exists():
            source='snow-ether-v3' if silhouette=='a' else 'rain-ancients-v3'
        else:
            source=prepare(silhouette)
            bpy.ops.wm.save_as_mainfile(filepath=str(stage),compress=True)
        variants=[('human','short'),('elf','braid'),('human','long'),('human','braid'),('elf','short'),('elf','long')]
        for race,style in variants:
            slug=f'{race}-{silhouette}-{style}'
            if args.only and slug not in args.only.split(','):continue
            bpy.ops.wm.open_mainfile(filepath=str(stage))
            M.clear();M.update({k:bpy.data.materials[k] for k in ('Skin','Hair','Iris','OutfitAccent','Leather','DarkLeather','Metal','Cloth','Ivory','Gem','EyesWhite','Pupil','Mouth','Thread') if bpy.data.materials.get(k)})
            results.append(export_variant(race,silhouette,style,source))
            (ROOT/'metadata.json').write_text(json.dumps({'schemaVersion':1,'sourceAttribution':CREDIT,'models':results},indent=2),encoding='utf-8')


if __name__=='__main__':main()
