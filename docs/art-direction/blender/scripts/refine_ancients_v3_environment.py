"""Art-directed woodland V3 refinement of the already loaded V2 scene.

Preserves the hero collection and leaves saving/rendering to the caller.
Uses the original procedural helpers, never image plates or external assets.
"""
import bpy
import math
import random
import importlib.util
from pathlib import Path
from mathutils import Vector, Matrix, Euler


def refine_scene():
    spec=importlib.util.spec_from_file_location('ancients_v2_helpers',Path(__file__).with_name('refine_terres_anciens.py'))
    R=importlib.util.module_from_spec(spec)
    spec.loader.exec_module(R)
    R.setup_helpers()
    B=R.BASE
    randomizer=random.Random(901)
    initial=set(bpy.context.scene.objects)
    scene=bpy.context.scene

    def hide(obj):
        obj.hide_render=True
        obj.hide_set(True)

    def ground(x,y):
        return max(B.ground_z(x,y),R.terrain_height(x,y)) if y<11.7 and abs(x)<10 else R.terrain_height(x,y)

    def center(obj):
        if obj.type not in {'MESH','CURVE'}:return obj.location.copy()
        return sum((obj.matrix_world@Vector(corner) for corner in obj.bound_box),Vector())/8

    def newmat(name,col,rough=.8,metal=0):
        mat=B.material(name,col,rough,metal)
        B.MATS[name]=mat
        return mat

    newmat('v3_bark_light',(.28,.22,.17),.87)
    newmat('v3_bark_crevice',(.07,.06,.065),.95)
    newmat('v3_canvas_sage',(.18,.235,.16),.83)
    newmat('v3_path_stone',(.31,.29,.23),.90)
    newmat('v3_plaster',(.54,.43,.29),.81)
    newmat('v3_window_dark',(.04,.048,.036),.63)
    amber=newmat('v3_window_amber',(.96,.47,.12),.46)
    ps=amber.node_tree.nodes.get('Principled BSDF')
    ps.inputs['Emission Color'].default_value=(1,.45,.10,1)
    ps.inputs['Emission Strength'].default_value=.8

    # Broader bark shapes replace the fur-like fine-grained surface.
    for name in ['bark','bark_ridge','v2_tree_brown']:
        R.material_nodes(bpy.data.materials[name],
            [(.18,(.065,.055,.064)),(.40,(.16,.125,.13)),(.64,(.28,.235,.20)),(.84,(.39,.34,.26))],
            1.75,(2.6,2.6,.16),.47,.16,.87,37,.035)

    # The right stall becomes a smaller, angled shelter recessed into the grove.
    pivot=Vector((5.7,4.0,.65))
    transform=(Matrix.Translation(Vector((1.0,2.4,.12))) @ Matrix.Translation(pivot)
               @ Matrix.Rotation(math.radians(-13),4,'Z')
               @ Matrix.Diagonal((.84,.92,.90,1.0)) @ Matrix.Translation(-pivot))
    for obj in list(initial):
        if obj.name.startswith('Wayfarer atelier'):
            obj.matrix_world=transform@obj.matrix_world
        elif obj.name.startswith(('V2 | glazed apothecary jar','V2 | apothecary jar neck','V2 | cork stopper',
                                  'V2 | herbs gathered on counter')):
            c=center(obj)
            if c.x>3.8 and 1.5<c.y<6.6:
                obj.matrix_world=transform@obj.matrix_world
    for obj in scene.objects:
        if obj.name.startswith('Wayfarer atelier') and 'aubergine canopy' in obj.name:
            obj.data.materials[0]=B.MATS['v3_canvas_sage']
    # Remove the toy-like row of pebble posts and redraw the soil transition.
    for obj in list(initial):
        if obj.name.startswith(('V2 | fine trail gravel','Worn pale woodland trail',
                                'V2 | footpath continues into the inhabited forest')):
            hide(obj)
        if obj.name.startswith('Trail | softened inset stepping stone'):
            obj.location.z-=.025
            obj.rotation_euler.z+=randomizer.uniform(-.7,.7)
            obj.scale.x*=randomizer.uniform(.80,1.25)
            obj.scale.y*=randomizer.uniform(.85,1.18)

    trail=newmat('v3_feathered_woodland_trail',(.40,.29,.16),.94)
    nodes,links=trail.node_tree.nodes,trail.node_tree.links
    p=nodes.get('Principled BSDF')
    out=nodes.get('Material Output')
    coords=nodes.new('ShaderNodeTexCoord')
    tex=nodes.new('ShaderNodeTexNoise')
    tex.inputs['Scale'].default_value=1.65
    tex.inputs['Detail'].default_value=4.0
    links.new(coords.outputs['Object'],tex.inputs['Vector'])
    palette=nodes.new('ShaderNodeValToRGB')
    palette.color_ramp.elements[0].position=.28
    palette.color_ramp.elements[0].color=(.13,.085,.040,1)
    palette.color_ramp.elements[1].position=.72
    palette.color_ramp.elements[1].color=(.49,.365,.195,1)
    links.new(tex.outputs['Fac'],palette.inputs['Fac'])
    links.new(palette.outputs['Color'],p.inputs['Base Color'])
    bump=nodes.new('ShaderNodeBump');bump.inputs['Strength'].default_value=.24;bump.inputs['Distance'].default_value=.025
    links.new(tex.outputs['Fac'],bump.inputs['Height']);links.new(bump.outputs['Normal'],p.inputs['Normal'])
    grit=nodes.new('ShaderNodeTexNoise')
    grit.inputs['Scale'].default_value=79
    grit.inputs['Detail'].default_value=2.0
    grit.inputs['Roughness'].default_value=.75
    links.new(coords.outputs['Object'],grit.inputs['Vector'])
    grains=nodes.new('ShaderNodeBump')
    grains.inputs['Strength'].default_value=.48
    grains.inputs['Distance'].default_value=.028
    links.new(grit.outputs['Fac'],grains.inputs['Height'])
    links.new(bump.outputs['Normal'],grains.inputs['Normal'])
    links.new(grains.outputs['Normal'],p.inputs['Normal'])
    flecks=nodes.new('ShaderNodeMixRGB');flecks.blend_type='MULTIPLY';flecks.inputs[0].default_value=.30
    links.new(palette.outputs['Color'],flecks.inputs[1]);links.new(grit.outputs['Fac'],flecks.inputs[2])
    links.new(flecks.outputs[0],p.inputs['Base Color'])
    mask=nodes.new('ShaderNodeAttribute');mask.attribute_name='TrailMask'
    noise_mul=nodes.new('ShaderNodeMath');noise_mul.operation='MULTIPLY';noise_mul.inputs[1].default_value=.75
    links.new(tex.outputs['Fac'],noise_mul.inputs[0])
    subtract=nodes.new('ShaderNodeMath');subtract.operation='SUBTRACT';subtract.inputs[0].default_value=1.25
    links.new(noise_mul.outputs[0],subtract.inputs[1])
    alpha=nodes.new('ShaderNodeMath');alpha.operation='MULTIPLY';alpha.use_clamp=True
    density=nodes.new('ShaderNodeMath');density.operation='MULTIPLY';density.inputs[1].default_value=1.55
    links.new(mask.outputs['Fac'],density.inputs[0]);links.new(density.outputs[0],alpha.inputs[0]);links.new(subtract.outputs[0],alpha.inputs[1])
    transparent=nodes.new('ShaderNodeBsdfTransparent')
    mix=nodes.new('ShaderNodeMixShader')
    links.new(alpha.outputs[0],mix.inputs[0]);links.new(transparent.outputs[0],mix.inputs[1])
    links.new(p.outputs[0],mix.inputs[2]);links.new(mix.outputs[0],out.inputs['Surface'])

    for start,end in [(-32,-3.65),(.75,57)]:
        vs=[];fs=[];weights=[]
        count=int((end-start)/.30)+1
        for row in range(count):
            y=start+(end-start)*row/(count-1)
            cx=.13*math.sin(y*.45)+.28*math.sin(y*.13)
            width=2.40+.20*math.sin(y*.61)+.12*math.sin(y*.19)
            for col in range(25):
                u=col/12-1
                x=cx+u*width
                z=ground(x,y)+.034+.006*math.sin(y*8.1+x*3.8)+.007*math.sin(x*5.1+y*1.4)
                vs.append((x,y,z))
                t=max(0,min(1,(abs(u)-.49)/.51))
                weights.append(1-t*t*(3-2*t))
        for row in range(count-1):
            for col in range(24):
                k=row*25+col
                fs.append((k,k+1,k+26,k+25))
        ob=B.mesh_object('V3 | naturally worn soil with feathered margins',vs,fs,trail)
        attr=ob.data.attributes.new('TrailMask','FLOAT','POINT')
        for el,weight in zip(attr.data,weights):el.value=weight

    # Small gravel is irregular and mostly buried, never arranged as a border.
    vs=[];fs=[]
    for i in range(180):
        y=randomizer.uniform(-13,14)
        x=randomizer.uniform(-2.25,2.25)
        if -3.7<y<.9 or (abs(x)<.50 and -9<y<-6.7):continue
        if randomizer.random()<.60 and abs(x)<1.2:continue
        r=randomizer.uniform(.025,.12)
        z=ground(x,y)+.020
        k=len(vs)
        sides=8
        angle=randomizer.uniform(0,math.tau)
        for row,rr,zz in [(0,r,-.045),(1,r*.85,.012),(2,r*.30,.027)]:
            for j in range(sides):
                a=angle+j*math.tau/sides
                jitter=randomizer.uniform(.77,1.13)
                vs.append((x+math.cos(a)*rr*jitter,y+math.sin(a)*rr*.68*jitter,z+zz))
        for row in range(2):
            for j in range(sides):
                a=k+row*sides+j;b=k+row*sides+(j+1)%sides
                fs.append((a,b,b+sides,a+sides))
        fs.append(tuple(k+16+j for j in range(sides)))
    B.mesh_object('V3 | scattered embedded weathered gravel',vs,fs,'v3_path_stone')

    # A loose edge of grasses breaks the transition into irregular inhabited banks.
    vs=[];fs=[];indices=[]
    for i in range(310):
        y=randomizer.uniform(-13,16)
        if -3.7<y<1.0:continue
        side=randomizer.choice([-1,1])
        x=side*randomizer.uniform(1.15,2.65)+.20*math.sin(y*.45)
        if randomizer.random()>.66+.26*math.sin(y*.9):continue
        for j in range(randomizer.randint(3,7)):
            pos=(x+randomizer.uniform(-.12,.12),y+randomizer.uniform(-.12,.12),ground(x,y)+.022)
            q=Euler((randomizer.uniform(-.25,.25),-randomizer.uniform(.70,1.4),randomizer.uniform(0,math.tau))).to_quaternion()
            R.leaf(vs,fs,pos,randomizer.uniform(.15,.36),randomizer.uniform(.022,.050),q,indices,randomizer.choice([0,1,1,2]))
    ob=B.mesh_object('V3 | grasses encroach in irregular bank patches',vs,fs,'v2_leaf_deep')
    for name in ['v2_leaf_green','v2_leaf_sage']:ob.data.materials.append(B.MATS[name])
    for p,i in zip(ob.data.polygons,indices):p.material_index=i

    # Raise and subtly twist the living arch, removing its horizontal bridge-like graft.
    tree=bpy.data.objects.get('V2 | sculpted continuous ancient tree gateway')
    if tree:
        for v in tree.data.vertices:
            weight=math.exp(-(v.co.x/3.0)**2)*math.exp(-((v.co.z-8.35)/1.22)**2)
            v.co.z+=1.15*weight
            v.co.y+=.32*weight*math.sin(v.co.x*.6)
        tree.data.update()
        for ob in list(initial):
            if ob.type not in {'MESH','CURVE'} or ob.hide_render or ob==tree:continue
            if not ob.name.startswith(('V2 | ancient foliage','Ancient arch | crown ivy')):continue
            c=center(ob)
            if abs(c.x)<1.8 and 7<c.y<9.3 and 8<c.z<10.2:
                ob.location.z+=.85
        # Long, attached bark plates follow the main load-bearing flow of the wood.
        deps=bpy.context.evaluated_depsgraph_get()
        evaluated=tree.evaluated_get(deps)
        for side in [-1,1]:
            for stripe in range(6):
                points=[]
                for k in range(18):
                    t=k/17
                    z=.55+t*7.7
                    x=side*(4.75-2.8*t*t)+(stripe-2.5)*(.17+.08*(1-t))
                    hit,position,normal,index=evaluated.ray_cast(Vector((x,-15,z)),Vector((0,1,0)))
                    if hit:
                        points.append(tuple(position+normal*.022))
                if len(points)>4:
                    widths=[.015+.045*math.sin(math.pi*i/(len(points)-1)) for i in range(len(points))]
                    B.organic_tube('V3 | broad flowing bark plate',points,widths,
                                   'v3_bark_light' if stripe%3 else 'v3_bark_crevice',sides=8,steps=3,ridged=.05)

    # A warm raised lodge creates a visible destination beyond the bridge.
    bx,by=-.20,26.0
    bz=ground(bx,by)+.25
    width,depth=7.7,5.2
    for i in range(26):
        B.bevel_box('V3 | destination terrace boards',(bx-width/2+(i+.5)*width/26,by,bz+.25),
                    (width/26-.015,depth,.18),'wood_plank',.025)
    B.bevel_box('V3 | destination shaded backwall',(bx,by+1.9,bz+2.7),(6.9,.20,4.9),'wood_dark',.05)
    for side in [-1,1]:
        x=bx+side*3.1
        B.organic_tube('V3 | lodge branching structural frame',[(x,by-1.8,bz+.3),(x,by-1.8,bz+3.5),
                    (bx+side*1.7,by-1.8,bz+5.1),(bx,by-1.8,bz+5.7)],
                    [.24,.19,.15,.08],'wood_light',sides=18,steps=7)
        B.bevel_box('V3 | lodge warm plaster infill',(bx+side*2.1,by+1.74,bz+2.6),(2.6,.16,4.25),'v3_plaster',.06)
        for wx in [bx+side*1.5,bx+side*2.65]:
            B.bevel_box('V3 | lodge glowing narrow window',(wx,by+1.62,bz+2.75),(.65,.12,1.65),'v3_window_amber',.12)
            B.bevel_box('V3 | lodge window central mullion',(wx,by+1.53,bz+2.75),(.045,.055,1.65),'wood_dark',.01)
            B.bevel_box('V3 | lodge window cross rail',(wx,by+1.52,bz+2.85),(.65,.055,.055),'wood_dark',.01)
    # Curved layered roof in long wooden shingles, with an asymmetric overhang.
    for side in [-1,1]:
        for row in range(6):
            near=row/6;far=(row+1)/6
            xx1=bx+side*(.02+near*4.8)
            xx2=bx+side*(.02+far*4.8)
            z1=bz+5.9-2.0*near+.18*math.sin(near*math.pi)
            z2=bz+5.9-2.0*far+.18*math.sin(far*math.pi)
            for col in range(13):
                yy=by-3.0+(col+.5)*.48+(row%2)*.11
                vertices=[(xx1,yy-.23,z1+.035),(xx2,yy-.23,z2),(xx2,yy+.23,z2+.025),(xx1,yy+.23,z1+.02)]
                roof=B.mesh_object('V3 | individually lapped lodge roof shingles',vertices,[(0,1,2,3)],
                                    'cloth' if row%2 else 'v3_canvas_sage')
                roof.modifiers.new('Shingle crafted thickness','SOLIDIFY').thickness=.045
                bevel=roof.modifiers.new('Rounded shingle end','BEVEL');bevel.width=.025;bevel.segments=2
    B.organic_tube('V3 | carved sweeping lodge roof ridge',[(bx,by-3.1,bz+5.85),(bx-.1,by,bz+6.0),
                  (bx+.2,by+3.6,bz+6.25)],[.12,.11,.035],'wood_light',sides=16,steps=8)
    # Broad steps are fitted to the existing gently rising path.
    lower=ground(bx,by-5.4)
    for i in range(6):
        y=by-5.2+i*.42
        z=lower+(bz+.27-lower)*(i+1)/6
        B.bevel_box('V3 | destination lodge welcoming step',(bx,y,z-.08),(3.2,.60,.18),'wood_plank',.035)
    for side in [-1,1]:
        for yy in [by-2.6,by-.5]:
            B.beam('V3 | lodge terrace post',(bx+side*3.6,yy,bz+.3),(bx+side*3.6,yy,bz+1.4),.095,'wood_light')
        B.organic_tube('V3 | lodge low living balustrade',[(bx+side*3.6,by-2.6,bz+1.32),
                     (bx+side*3.6,by-1.5,bz+1.24),(bx+side*3.6,by-.5,bz+1.32)],
                     [.07,.07,.07],'wood_light',sides=12,steps=5)
        B.lantern('V3 | lodge invitation lantern',(bx+side*2.7,by-1.95,bz+2.35),.82)
    B.banner('V3 | grove house branching banner',(bx,by-1.95,bz+4.65),1.05,1.45)

    # Loosely draped vines and irregular canopies support the diagonal silhouettes.
    for side in [-1,1]:
        for j in range(3):
            x=side*(4.8+j*.23)
            B.organic_tube('V3 | long naturally hanging guardian vine',[(x,5.15,6.8-j*.40),
                          (x-side*.35,5.02,5.45-j*.40),(x+side*.12,5.22,3.80-j*.3)],
                          [.038,.025,.006],'moss',sides=8,steps=7)
        for x,y,z,size in [(side*9,19,8,(3.2,2.5,1.6)),(side*14,29,11,(4.1,3,1.8)),
                            (side*4.8,27,8.5,(1.7,1.5,.85))]:
            R.clustered_foliage('V3 | irregular canopy framing the destination',(x,y,z),size,360,.88)

    # A few soft mid-height silhouettes close the empty sky gaps beyond the foreground trunks.
    newmat('v3_distant_foliage',(.15,.245,.16),.86)
    newmat('v3_distant_foliage_light',(.235,.33,.215),.85)
    for i,(x,y,z,size) in enumerate([(-16,18,6.7,(5.4,3.6,2.5)),(17,19,7.4,(5.5,3.8,2.6)),
                    (-19,34,10.5,(6.2,4.3,2.8)),(21,36,11.5,(6.6,4.2,3.2)),
                    (-17,52,15,(6.4,4.5,3.7)),(18,55,16,(6.7,4.4,3.5))]):
        trunk_z=ground(x,y)
        B.organic_tube('V3 | distant understorey branching silhouette',[(x+1.2,y+.5,trunk_z),
                      (x+.5,y+.1,z-2.0),(x,y,z+.4)],[.38,.22,.045],'v2_tree_brown',sides=14,steps=6)
        canopy=R.clustered_foliage('V3 | soft far foliage closes horizon gaps',(x,y,z),size,3000,.93)
        canopy.data.materials.clear()
        canopy.data.materials.append(B.MATS['v3_distant_foliage'])
        canopy.data.materials.append(B.MATS['v3_distant_foliage_light'])
        for p in canopy.data.polygons:p.material_index=1 if p.material_index in [2,3] else 0

    # Color separation: warm sun through the grove, cool readable shadows.
    for obj in scene.objects:
        if obj.type!='LIGHT':continue
        if obj.data.type=='AREA':obj.data.energy*=.24
        elif obj.data.type=='POINT':obj.data.energy*=.60
        elif obj.data.type=='SUN':
            obj.data.energy=3.0
            obj.data.angle=math.radians(3.1)
            obj.data.color=(1.0,.78,.47)
            obj.rotation_euler=(Vector((0,2,0))-Vector((-11,17,25))).to_track_quat('-Z','Y').to_euler()
    B.area_light('V3 | blue frontal woodland bounce',(5,-12,9),(0,2,2),620,(.50,.69,1.0),11)
    B.area_light('V3 | warm open sky over hero',(-5,-6,8),(0,-4,1),780,(1.0,.86,.62),6)
    B.area_light('V3 | sunlit lodge clearing',(-4,20,15),(-.2,26,3.5),2400,(1.0,.80,.47),7)
    B.area_light('V3 | golden side rim in the arch',(5,10,12),(0,5,3),1100,(1,.73,.37),5)
    world=bpy.data.worlds.new('V3 | clear woodland atmosphere')
    world.use_nodes=True
    nd=world.node_tree.nodes;lk=world.node_tree.links
    bg=nd.get('Background')
    bg.inputs['Strength'].default_value=.12
    sky=nd.new('ShaderNodeTexSky');sky.sky_type='NISHITA';sky.sun_disc=False
    sky.sun_elevation=math.radians(38);sky.sun_rotation=math.radians(125)
    sky.altitude=.4;sky.air_density=1.1;sky.dust_density=.6
    lk.new(sky.outputs['Color'],bg.inputs['Color'])
    scene.world=world
    fog=bpy.data.materials.get('V2 | distant blue woodland atmosphere')
    if fog:
        for node in fog.node_tree.nodes:
            if node.type=='PRINCIPLED_VOLUME':
                node.inputs['Density'].default_value=.0032
                node.inputs['Color'].default_value=(.40,.61,.72,1)
                node.inputs['Anisotropy'].default_value=.38

    cam=B.camera('Camera_V3',(2.7,-17.5,2.8),(0,9,4),28)
    cam.data.clip_end=400
    scene.render.resolution_x=2560;scene.render.resolution_y=1440
    scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=.05
    hero_pos=[0.0,-8.0,ground(0,-8)+.037]
    from bpy_extras.object_utils import world_to_camera_view
    scene.camera=cam
    bpy.context.view_layer.update()
    foot=world_to_camera_view(scene,cam,Vector(hero_pos))
    head=world_to_camera_view(scene,cam,Vector(hero_pos)+Vector((0,0,2.05)))
    return {'environment':'Terres des Anciens V3','camera':'Camera_V3','hero_position':hero_pos,
            'hero_projection':{'feet_xy':[foot.x,foot.y],'head_xy':[head.x,head.y],
                               'height_fraction':head.y-foot.y},
            'new_objects':len(set(scene.objects)-initial),
            'notes':['Warm raised lodge at the path destination and staged golden/cool lighting',
                     'Feathered natural soil margins, embedded stones and irregular grass banks',
                     'Recessed asymmetric second stall; raised living arch and flowing bark plates'],
            'character_edits':False}
