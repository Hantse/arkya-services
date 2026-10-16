"""Derive four connected tree silhouettes from the immutable approved oak.

Run with Blender --background --python generate_shapes.py. Use --no-render to
export only, or --only 01-jeune to regenerate one shape. All outputs stay in formes/.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Vector, Matrix


ROOT = Path(__file__).resolve().parents[1]
BASE = ROOT.parent / 'arkya-ancient-tree-v2.blend'
OUTPUT = ROOT / 'formes'
ASSET_NAMES = ('Oak | continuous sculpted trunk roots and limbs',
               'Oak | layered connected foliage', 'Oak | living branchlet network')
SPECS = (
    dict(id='01-jeune', title='Jeune', height=5.5, stem=.34, crown_x=.43, crown_y=.41,
         curve=.12, power=1.55, twist=-.035, bend=.11, leaf=.72,
         description='Jeune arbre fin, houppier compact et vertical, contreforts discrets.'),
    dict(id='02-etale', title='Étalé', height=8.0, stem=1.11, crown_x=1.49, crown_y=1.39,
         curve=.35, power=1.7, twist=.035, bend=.28, leaf=1.08,
         description='Port étalé, tronc court et large couronne en ombrelle.'),
    dict(id='03-elance', title='Élancé', height=14.0, stem=.77, crown_x=.69, crown_y=.70,
         curve=-.24, power=1.6, twist=-.055, bend=.38, leaf=.98,
         description='Tronc élancé, premières fourches hautes et houppier étroit.'),
    dict(id='04-venerable', title='Vénérable', height=16.0, stem=1.91, crown_x=1.76, crown_y=1.62,
         curve=.18, power=1.55, twist=.25, bend=1.43, leaf=1.40,
         description='Arbre massif, contreforts puissants, grande couronne courbe et modérément vrillée.'),
)


def read_positions(obj):
    values=np.empty(len(obj.data.vertices)*3,dtype=np.float64)
    obj.data.vertices.foreach_get('co',values)
    return values.reshape((-1,3))


def write_positions(obj,values):
    obj.data.vertices.foreach_set('co',np.asarray(values,dtype=np.float32).reshape(-1))
    obj.data.update()


def deform(points,spec,base_height):
    values=np.asarray(points,dtype=np.float64)
    t=np.clip(values[:,2]/base_height,0,1.1)
    transition=np.clip((values[:,2]-1.2)/3.0,0,1)
    transition=transition*transition*(3-2*transition)
    sx=spec['stem']+(spec['crown_x']-spec['stem'])*transition
    sy=spec['stem']+(spec['crown_y']-spec['stem'])*transition
    angle=spec['twist']*(.18*t+.82*t*t)
    x=values[:,0]*sx;y=values[:,1]*sy
    result=np.empty_like(values)
    result[:,0]=x*np.cos(angle)-y*np.sin(angle)+spec['bend']*(1.45*t*t-.45*t*t*t)
    result[:,1]=x*np.sin(angle)+y*np.cos(angle)+spec['bend']*.24*np.sin(math.pi*t)*t
    result[:,2]=spec['height']*((1-spec['curve'])*t+spec['curve']*t**spec['power'])
    return result


def deform_leaves(points,spec,base_height):
    if len(points)%11:raise ValueError('Approved leaf topology changed; expected 11 vertices per blade')
    blades=points.reshape((-1,11,3))
    bases=blades[:,0,:]
    mapped=deform(bases,spec,base_height)
    epsilon=.002
    jacobian=np.empty((len(bases),3,3))
    for axis in range(3):
        delta=np.zeros(3);delta[axis]=epsilon
        jacobian[:,:,axis]=(deform(bases+delta,spec,base_height)-deform(bases-delta,spec,base_height))/(2*epsilon)
    # Polar rotation retains each blade's proportions while its attachment follows the twig exactly.
    u,_,vh=np.linalg.svd(jacobian)
    rotation=u@vh
    local=(blades-bases[:,None,:])*spec['leaf']
    rotated=np.einsum('nij,nkj->nki',rotation,local)
    return (mapped[:,None,:]+rotated).reshape((-1,3))


def load_assets():
    bpy.ops.wm.open_mainfile(filepath=str(BASE))
    objects=[bpy.data.objects[name] for name in ASSET_NAMES]
    for obj in list(bpy.data.objects):
        if obj not in objects:bpy.data.objects.remove(obj,do_unlink=True)
    for obj in objects:
        obj.parent=None
        obj.matrix_world=Matrix.Identity(4)
    return objects


def configure():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=32
    scene.cycles.use_denoising=True
    scene.cycles.max_bounces=6
    scene.cycles.adaptive_threshold=.04
    prefs=bpy.context.preferences.addons['cycles'].preferences
    prefs.compute_device_type='OPTIX';prefs.refresh_devices()
    for device in prefs.devices:device.use=device.type=='OPTIX'
    scene.cycles.device='GPU'
    scene.render.resolution_x=1100;scene.render.resolution_y=1100
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.render.image_settings.color_mode='RGB'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    bpy.context.preferences.filepaths.save_version=0
    return scene


def solid_material(name,color):
    mat=bpy.data.materials.new(name);mat.use_nodes=True
    shader=mat.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=(*color,1)
    shader.inputs['Roughness'].default_value=.93
    return mat


def stage(height,points=None,comparison=False):
    scene=configure()
    world=bpy.data.worlds.new('Shape review | neutral light studio')
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.45,.48,.52,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55
    scene.world=world
    bpy.ops.mesh.primitive_plane_add(size=500,location=(0,0,-.045))
    floor=bpy.context.object;floor.name='Review only | neutral light floor'
    floor.data.materials.append(solid_material('Review only | warm gray',(.48,.49,.47)))
    target=Vector((0,0,height*.47))
    if points is not None:target.x=(points[:,0].min()+points[:,0].max())*.5
    for name,position,power,size,color in [
        ('Large warm key',(-.9*height,-1.1*height,1.6*height),31*height*height,.85*height,(1,.93,.84)),
        ('Soft blue fill',(.95*height,-.35*height,1.05*height),19*height*height,.9*height,(.82,.9,1)),
        ('Canopy rim',(.1*height,.9*height,1.5*height),32*height*height,.7*height,(1,.98,.9))]:
        light=bpy.data.lights.new(name,'AREA');light.energy=power;light.shape='DISK';light.size=size;light.color=color
        obj=bpy.data.objects.new(name,light);scene.collection.objects.link(obj);obj.location=position
        obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
    data=bpy.data.cameras.new('Shape review camera');data.type='ORTHO';data.clip_end=1000
    camera=bpy.data.objects.new(data.name,data);scene.collection.objects.link(camera)
    camera.location=target+Vector((height*1.5,-height*2.1,height*.83))
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    data.ortho_scale=height*1.25
    if points is not None:
        matrix=np.asarray(camera.rotation_euler.to_matrix())
        projected=points@matrix
        width=np.ptp(projected[:,0]);vertical=np.ptp(projected[:,1])
        data.ortho_scale=max(width,vertical)*1.15
    scene.camera=camera
    return scene,camera


def statistics(objects):
    allpoints=np.concatenate([read_positions(o) for o in objects])
    low=allpoints.min(axis=0);high=allpoints.max(axis=0)
    return {'dimensions_m':dict(zip(('width','depth','height'),[round(float(v),3) for v in high-low])),
            'bounds_blender_z_up':{'min':low.tolist(),'max':high.tolist()},
            'mesh_count':len(objects),'vertices':sum(len(o.data.vertices) for o in objects),
            'triangles':sum(len(p.vertices)-2 for o in objects for p in o.data.polygons)},allpoints


def select(objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:obj.hide_set(False);obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]


def generate(spec,no_render=False):
    folder=OUTPUT/spec['id'];folder.mkdir(parents=True,exist_ok=True)
    stem=folder/spec['id']
    objects=load_assets()
    original=[read_positions(o) for o in objects]
    base_height=max(float(p[:,2].max()) for p in original)
    leaf_colors=objects[1].data.color_attributes['COLOR_0']
    for obj,points in zip(objects,original):
        transformed=deform_leaves(points,spec,base_height) if obj==objects[1] else deform(points,spec,base_height)
        write_positions(obj,transformed)
    # Correct the requested height with a uniform final factor, preserving blade proportions.
    arrays=[read_positions(o) for o in objects]
    minimum=min(float(p[:,2].min()) for p in arrays)
    maximum=max(float(p[:,2].max()) for p in arrays)
    factor=spec['height']/(maximum-minimum)
    for obj,points in zip(objects,arrays):
        points[:,2]-=minimum
        write_positions(obj,points*factor)
        obj['Arkya_shape_variant']=spec['id']
        obj['source_asset']='arbre-de-base/arkya-ancient-tree-v2.blend'
    root=bpy.data.objects.new('Arkya | '+spec['title'],None);bpy.context.scene.collection.objects.link(root)
    for obj in objects:obj.parent=root
    info,points=statistics(objects)
    info.update({'id':spec['id'],'title':spec['title'],'description':spec['description'],
                 'source_asset':'../../../arkya-ancient-tree-v2.blend','deformation':spec,
                 'leaf_count':len(objects[1].data.vertices)//11,
                 'leaf_shape_preservation':'Each blade uses a local polar rotation and uniform size; its base follows the deformed twig exactly.',
                 'materials':'Approved base materials unchanged; packed bark 2K base color/normal, COLOR_0 leaf color and occlusion retained.',
                 'leaf_color_entries':len(leaf_colors.data),'gltf_up_axis':'Y','blender_up_axis':'Z'})
    select(objects+[root])
    bpy.ops.export_scene.gltf(filepath=str(stem.with_suffix('.glb')),export_format='GLB',
        use_selection=True,export_apply=True,export_materials='EXPORT',export_yup=True,
        export_animations=False,export_cameras=False,export_lights=False,export_tangents=True)
    info['glb_bytes']=stem.with_suffix('.glb').stat().st_size
    scene,camera=stage(spec['height'],points)
    for image in bpy.data.images:
        if image.type=='IMAGE' and image.size[0]:image.pack()
    bpy.ops.wm.save_as_mainfile(filepath=str(stem.with_suffix('.blend')),compress=True)
    stem.with_suffix('.json').write_text(json.dumps(info,indent=2,ensure_ascii=False),encoding='utf-8')
    print('SHAPE_READY',spec['id'],json.dumps(info['dimensions_m']),flush=True)
    if not no_render:
        scene.render.filepath=str(stem.parent/(stem.name+'-preview.png'))
        bpy.ops.render.render(write_still=True)
    return info


def text_object(body,location,camera,size,material):
    data=bpy.data.curves.new(body,'FONT');data.body=body;data.align_x='CENTER';data.align_y='CENTER';data.size=size
    obj=bpy.data.objects.new(body,data);bpy.context.scene.collection.objects.link(obj)
    obj.location=location;obj.rotation_euler=camera.rotation_euler
    data.materials.append(material)
    return obj


def comparison(no_render=False):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ordered=[('01-jeune','JEUNE',5.5),('02-etale','ÉTALÉ',8),('base','BASE',10.128),
             ('03-elance','ÉLANCÉ',14),('04-venerable','VÉNÉRABLE',16)]
    groups=[]
    rotation=Matrix.Rotation(-math.atan2(15,21),4,'Z')
    for shape_id,label,height in ordered:
        path=BASE if shape_id=='base' else OUTPUT/shape_id/(shape_id+'.blend')
        with bpy.data.libraries.load(str(path),link=False) as (source,target):
            target.objects=[name for name in source.objects if name in ASSET_NAMES]
        objects=[]
        for obj in target.objects:
            obj.parent=None;bpy.context.scene.collection.objects.link(obj)
            obj.matrix_world=rotation
            objects.append(obj)
        points=np.concatenate([np.asarray([obj.matrix_world@v.co for v in obj.data.vertices]) for obj in objects])
        groups.append({'id':shape_id,'label':label,'height':height,'objects':objects,'width':float(np.ptp(points[:,0]))})
    total=sum(g['width'] for g in groups)+2.0*(len(groups)-1)
    current=-total/2
    for group in groups:
        center=current+group['width']/2
        group['x']=center
        for obj in group['objects']:obj.location.x=center
        current+=group['width']+2.0
    scene,camera=stage(21,comparison=True)
    target=Vector((0,0,7.8));camera.location=(0,-65,26)
    camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
    scene.render.resolution_x=3000;scene.render.resolution_y=1200
    camera.data.ortho_scale=max(total*1.09,56)
    # A very broad source keeps the five trees on the same illumination scale.
    for obj in bpy.data.objects:
        if obj.type=='LIGHT':obj.data.size=45;obj.data.energy*=2.8
    ink=solid_material('Comparison | charcoal lettering',(.035,.05,.045))
    ink.node_tree.nodes.clear()
    output=ink.node_tree.nodes.new('ShaderNodeOutputMaterial')
    emission=ink.node_tree.nodes.new('ShaderNodeEmission')
    emission.inputs['Color'].default_value=(.014,.021,.018,1)
    ink.node_tree.links.new(emission.outputs[0],output.inputs['Surface'])
    for group in groups:
        text_object(group['label']+'  ·  '+f"{group['height']:.1f} m",(group['x'],-7,.65),camera,.55,ink)
    text_object('ARKYA  /  QUATRE SILHOUETTES ET LA BASE APPROUVÉE',(0,1,18.7),camera,.68,ink)
    text_object('Même échelle · mêmes matières · géométrie réelle',(0,1,17.65),camera,.42,ink)
    scene.render.filepath=str(OUTPUT/'comparaison-formes-meme-echelle.png')
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT/'comparaison-formes-meme-echelle.blend'),compress=True)
    if not no_render:bpy.ops.render.render(write_still=True)
    print('SHAPES_COMPARISON_READY',flush=True)


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--no-render',action='store_true')
    parser.add_argument('--only',choices=[s['id'] for s in SPECS])
    parser.add_argument('--comparison-only',action='store_true')
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    OUTPUT.mkdir(parents=True,exist_ok=True)
    before=hashlib.sha256(BASE.read_bytes()).hexdigest()
    results=[]
    if not args.comparison_only:
        for spec in SPECS:
            if not args.only or spec['id']==args.only:results.append(generate(spec,args.no_render))
    if not args.only:comparison(args.no_render)
    after=hashlib.sha256(BASE.read_bytes()).hexdigest()
    if before!=after:raise RuntimeError('Approved base file changed during generation')
    if results and not args.only:
        (OUTPUT/'formes.json').write_text(json.dumps({'base_sha256':before,'variants':results},indent=2,ensure_ascii=False),encoding='utf-8')
    print('SHAPES_COMPLETE_BASE_UNCHANGED',before,flush=True)


if __name__=='__main__':main()
