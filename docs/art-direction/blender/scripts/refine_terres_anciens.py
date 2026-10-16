"""Second-pass woodland environment for an opened Arkya 03 .blend scene.

Call refine_scene() after loading 03-terres-anciens.blend. The caller owns
character replacement, save and render. No external assets or dependencies.
"""
import importlib.util
import math
from pathlib import Path
import random

import bpy
from mathutils import Euler, Vector


RNG = random.Random(4471)
BASE = None


def setup_helpers():
    global BASE
    source = Path(__file__).with_name('build_terres_anciens.py')
    spec = importlib.util.spec_from_file_location('arkya_ancients_environment_base', source)
    BASE = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(BASE)
    for material in bpy.data.materials:
        BASE.MATS[material.name] = material
    BASE.RNG.seed(4471)


def hide(obj):
    obj.hide_render = True
    obj.hide_set(True)


def new_mat(name, color, roughness=.7, metallic=0):
    mat = BASE.material(name, color, roughness, metallic)
    BASE.MATS[name] = mat
    return mat


def material_nodes(mat, colors, scale, stretch, bump=.15, distance=.08,
                   roughness=.7, fine_scale=48, fine_strength=.07):
    mat.use_nodes = True
    nodes, links = mat.node_tree.nodes, mat.node_tree.links
    nodes.clear()
    output = nodes.new('ShaderNodeOutputMaterial')
    shader = nodes.new('ShaderNodeBsdfPrincipled')
    shader.inputs['Roughness'].default_value = roughness
    links.new(shader.outputs['BSDF'], output.inputs['Surface'])
    texcoord = nodes.new('ShaderNodeTexCoord')
    mapping = nodes.new('ShaderNodeVectorMath')
    mapping.operation = 'MULTIPLY'
    mapping.inputs[1].default_value = stretch
    links.new(texcoord.outputs['Object'], mapping.inputs[0])
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = scale
    noise.inputs['Detail'].default_value = 5
    noise.inputs['Roughness'].default_value = .72
    links.new(mapping.outputs['Vector'], noise.inputs['Vector'])
    ramp = nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements.remove(ramp.color_ramp.elements[1])
    for i, (position, color) in enumerate(colors):
        el = ramp.color_ramp.elements[0] if i == 0 else ramp.color_ramp.elements.new(position)
        el.position, el.color = position, (*color, 1)
    links.new(noise.outputs['Fac'], ramp.inputs[0])
    links.new(ramp.outputs[0], shader.inputs['Base Color'])
    sculpt = nodes.new('ShaderNodeBump')
    sculpt.inputs['Strength'].default_value = bump
    sculpt.inputs['Distance'].default_value = distance
    links.new(noise.outputs['Fac'], sculpt.inputs['Height'])
    fine = nodes.new('ShaderNodeTexNoise')
    fine.inputs['Scale'].default_value = fine_scale
    fine.inputs['Detail'].default_value = 2
    links.new(texcoord.outputs['Object'], fine.inputs[0])
    detail = nodes.new('ShaderNodeBump')
    detail.inputs['Strength'].default_value = fine_strength
    detail.inputs['Distance'].default_value = .023
    links.new(fine.outputs['Fac'], detail.inputs['Height'])
    links.new(sculpt.outputs['Normal'], detail.inputs['Normal'])
    links.new(detail.outputs['Normal'], shader.inputs['Normal'])
    return shader


def richer_materials():
    bark_colors = [(.20,(.065,.042,.043)),(.43,(.18,.115,.105)),
                   (.61,(.29,.205,.18)),(.80,(.38,.32,.24))]
    for name in ('bark','bark_ridge'):
        material_nodes(bpy.data.materials[name],bark_colors,3.9,(4.5,4.5,.34),
                       .68,.12,.82,69,.10)
    for name, stretch in [('wood_light',(3.0,3.0,.16)),('wood_dark',(4,4,.20)),
                           ('wood_plank',(.18,4,4))]:
        colors = [(.20,(.13,.064,.029)),(.45,(.29,.16,.073)),
                  (.65,(.49,.32,.145)),(.82,(.65,.48,.26))]
        if name == 'wood_dark':
            colors = [(p,tuple(c*.50 for c in col)) for p,col in colors]
        material_nodes(bpy.data.materials[name],colors,4.8,stretch,.42,.055,.58,92,.09)
    for name, colors in {
        'grass':[(.20,(.045,.085,.035)),(.44,(.15,.22,.075)),(.70,(.30,.36,.145))],
        'path':[(.18,(.16,.115,.068)),(.43,(.34,.26,.15)),(.70,(.53,.42,.26)),(.86,(.68,.55,.36))],
        'rock':[(.15,(.09,.10,.125)),(.45,(.235,.24,.26)),(.72,(.43,.43,.42))],
        'moss':[(.20,(.028,.07,.018)),(.45,(.09,.16,.035)),(.75,(.22,.30,.075))],
    }.items():
        material_nodes(bpy.data.materials[name],colors,5 if name=='path' else 3,
                       (1,1,1),.4 if name=='rock' else .3,.065,.88,80,.15)
    material_nodes(bpy.data.materials['cloth'],
                   [(.20,(.075,.038,.12)),(.48,(.20,.11,.30)),(.78,(.35,.23,.43))],
                   2,(1,1,1),.13,.025,.89,180,.22)
    cloth_shader=bpy.data.materials['cloth'].node_tree.nodes.get('Principled BSDF')
    cloth_shader.inputs['Sheen Weight'].default_value=.35
    cloth_shader.inputs['Sheen Roughness'].default_value=.6
    # Leaves are small, waxy and varied; restrained translucency softens sunlight.
    for name,col in [('v2_leaf_deep',(.035,.105,.045)),('v2_leaf_green',(.095,.23,.08)),
                     ('v2_leaf_sage',(.24,.34,.13)),('v2_leaf_gold',(.39,.41,.16)),
                     ('v2_leaf_young',(.26,.42,.16))]:
        mat=new_mat(name,col,.56)
        bsdf=mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Subsurface Weight'].default_value=.065
        bsdf.inputs['Subsurface Radius'].default_value=(.18,.30,.08)
        bsdf.inputs['Roughness'].default_value=.58
    new_mat('v2_earth',(.15,.20,.10),.9)
    material_nodes(BASE.MATS['v2_earth'],
                   [(.15,(.04,.07,.025)),(.45,(.13,.19,.065)),(.78,(.27,.31,.13))],
                   2.3,(1,1,1),.35,.09,.92,60,.15)
    new_mat('v2_tree_brown',(.22,.14,.09),.84)
    material_nodes(BASE.MATS['v2_tree_brown'],bark_colors,4,(4,4,.25),.6,.10,.8)
    new_mat('v2_bronze',(.30,.16,.045),.32,.72)
    new_mat('v2_stone_light',(.42,.40,.31),.8)
    new_mat('v2_gravel',(.34,.30,.22),.85)
    new_mat('v2_blossom',(.62,.40,.71),.68)


def apply_modifier(obj, modifier):
    bpy.context.view_layer.objects.active=obj
    for selected in list(bpy.context.selected_objects):
        selected.select_set(False)
    obj.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    obj.select_set(False)


def fuse_ancient_trees():
    objects=[o for o in bpy.context.scene.objects if o.type=='MESH' and
             (o.name.startswith('Ancient guardian |') or
              o.name.startswith('Ancient arch | natural graft'))]
    vertices,faces=[],[]
    for obj in objects:
        start=len(vertices)
        vertices.extend(tuple(obj.matrix_world@v.co) for v in obj.data.vertices)
        faces.extend(tuple(start+i for i in p.vertices) for p in obj.data.polygons)
    fused=BASE.mesh_object('V2 | sculpted continuous ancient tree gateway',vertices,faces,'bark')
    # Voxel union removes the visible socket-like branch/root intersections.
    modifier=fused.modifiers.new('Union of the living trunks and roots','REMESH')
    modifier.mode='VOXEL'
    modifier.voxel_size=.115
    modifier.use_smooth_shade=True
    modifier.use_remove_disconnected=False
    apply_modifier(fused,modifier)
    relax=fused.modifiers.new('Sculptural branch transitions','SMOOTH')
    relax.factor=.73
    relax.iterations=7
    apply_modifier(fused,relax)
    for obj in objects:
        hide(obj)
    tex=bpy.data.textures.new('V2 | broad bark relief',type='CLOUDS')
    tex.noise_scale=.33
    tex.noise_depth=2
    dis=fused.modifiers.new('Broad sculpted bark surface','DISPLACE')
    dis.texture=tex
    dis.texture_coords='GLOBAL'
    dis.strength=.13
    dis.mid_level=.5
    sub=fused.modifiers.new('Continuous sculpted surface','SUBSURF')
    sub.levels=sub.render_levels=1
    for p in fused.data.polygons:
        p.use_smooth=True
    # Narrow lifted plates provide readable bark forms at walking distance.
    for side in (-1,1):
        for k in range(13):
            angle=math.pi+math.tau*k/13
            phase=RNG.uniform(0,math.tau)
            points=[]
            for i in range(7):
                t=i/6
                z=.50+t*6.6
                radius=1.43-.49*t
                centerx=side*(4.6+.25*math.sin(t*math.pi)-.75*t*t)
                centery=6.65+t*.5
                points.append((centerx+math.cos(angle+.14*t)*radius,
                               centery+math.sin(angle+.14*t)*radius,z+.09*math.sin(phase+t*5)))
            BASE.organic_tube('V2 | narrow lifted bark plates',points,
                              [.015,.045,.060,.065,.055,.035,.004],'bark_ridge',
                              sides=8,steps=4,ridged=.12)
    return fused


def leaf(vertices,faces,origin,length,width,rotation,indices,matindex):
    start=len(vertices)
    matrix=rotation.to_matrix()
    origin=Vector(origin)
    for i in range(7):
        t=i/6
        w=(math.sin(math.pi*t)**.74)*width
        for s in (-1,0,1):
            point=Vector((t*length,s*w*.5,
                          math.sin(math.pi*t)*length*(.10-.06*abs(s))+.10*t*t*length))
            vertices.append(tuple(origin+matrix@point))
    for i in range(6):
        a=start+i*3
        faces.extend([(a,a+3,a+4,a+1),(a+1,a+4,a+5,a+2)])
        indices.extend([matindex,matindex])


def clustered_foliage(name,center,size,count=520,leaf_size=1):
    vertices,faces,indices=[],[],[]
    c=Vector(center)
    # Each twig owns an overlapping group of leaves; no opaque sphere is used.
    groups=max(7,int(count/28))
    for group in range(groups):
        theta=RNG.uniform(0,math.tau)
        z=RNG.uniform(-.72,.95)
        r=math.sqrt(1-z*z)*RNG.uniform(.35,1)
        end=c+Vector((math.cos(theta)*r*size[0],math.sin(theta)*r*size[1],z*size[2]))
        root=c+Vector((0,0,-size[2]*.55))
        if group%3==0:
            BASE.organic_tube(name+' | supporting twig',
                              [root,root*.45+end*.55,end],
                              [.025*leaf_size,.018*leaf_size,.004*leaf_size],
                              'wood_dark',sides=6,steps=3)
        direction=(end-root).normalized()
        for j in range(max(1,count//groups)):
            pos=end+Vector((RNG.gauss(0,.23)*size[0],RNG.gauss(0,.23)*size[1],
                            RNG.gauss(0,.23)*size[2]))
            angle=RNG.uniform(0,math.tau)
            rotation=Euler((RNG.uniform(-.8,.8),RNG.uniform(-.7,.6),angle)).to_quaternion()
            length=RNG.uniform(.27,.53)*leaf_size
            width=length*RNG.uniform(.42,.65)
            index=RNG.choices(range(5),[.13,.38,.26,.12,.11])[0]
            leaf(vertices,faces,pos,length,width,rotation,indices,index)
    obj=BASE.mesh_object(name+' | dense twig-grown leaves',vertices,faces,'v2_leaf_deep')
    for matname in ('v2_leaf_green','v2_leaf_sage','v2_leaf_gold','v2_leaf_young'):
        obj.data.materials.append(BASE.MATS[matname])
    for p,index in zip(obj.data.polygons,indices):
        p.material_index=index
    return obj


def replace_canopies():
    crowns=[o for o in list(bpy.context.scene.objects) if 'soft inner crown' in o.name]
    for obj in crowns:
        center=tuple(obj.location)
        size=tuple(obj.scale)
        # Original inner ellipsoids were smaller than their crowns.
        size=(size[0]*1.20,size[1]*1.20,size[2]*1.35)
        small=max(size)<1.1
        hide(obj)
        clustered_foliage('V2 | ancient foliage',center,size,
                          150 if small else 600,.62 if small else 1)
    for obj in list(bpy.context.scene.objects):
        if 'sculptural leaves' in obj.name:
            hide(obj)
    for obj in list(bpy.context.scene.objects):
        if 'aromatic herbs' in obj.name:
            center=tuple(obj.location)
            hide(obj)
            clustered_foliage('V2 | herbs gathered on counter',center,(.16,.14,.18),45,.30)


def terrain_height(x,y):
    h=BASE.ground_z(x,y)-.045
    if y>11:
        h+=min(2.4,(y-11)*.055)
        h+=.85*math.sin(x*.13+y*.10)**2*min((y-11)/15,1)
        h-=.8*math.exp(-(x/4.2)**2)*min((y-11)/12,1)
    return h


def continued_forest_floor():
    for obj in bpy.context.scene.objects:
        if obj.name.startswith(('Diorama |','Presentation floor')):
            hide(obj)
    vertices,faces=[],[]
    nx,ny=126,151
    for j in range(ny):
        y=-31+j*.8
        for i in range(nx):
            x=-50+i*.8
            vertices.append((x,y,terrain_height(x,y)))
    for j in range(ny-1):
        for i in range(nx-1):
            a=j*nx+i
            faces.append((a,a+1,a+1+nx,a+nx))
    BASE.mesh_object('V2 | continuous forest terrain beyond the diorama',vertices,faces,'v2_earth')
    # Path resumes on both sides of the original island, preserving the bridge.
    for y0,y1 in [(-27,-9.3),(11.7,58)]:
        vertices,faces=[],[]
        steps=int((y1-y0)/.35)+1
        for i in range(steps):
            t=i/(steps-1)
            y=y0+(y1-y0)*t
            c=.22*math.sin(y*.51) if y<13 else .9*math.sin((y-13)*.14)
            width=1.35+.16*math.sin(y*1.1)
            for j in range(9):
                u=j/8
                x=c+(u-.5)*width*2
                x+=(abs(u-.5)*2)**6*.12*math.sin(y*9)
                vertices.append((x,y,terrain_height(x,y)+.022))
        for i in range(steps-1):
            for j in range(8):
                a=i*9+j
                faces.append((a,a+1,a+10,a+9))
        BASE.mesh_object('V2 | footpath continues into the inhabited forest',vertices,faces,'path')
    # Break the original ribbon's mechanically clean borders.
    for obj in bpy.context.scene.objects:
        if obj.name.startswith('Worn pale woodland trail'):
            for i,v in enumerate(obj.data.vertices):
                if i%9 in (0,8):
                    v.co.x+=.08*math.sin(v.co.y*8)+.045*math.sin(v.co.y*17)
    for x,y,s in [(-10,13,2.2),(9,12,2.0),(-9,22,3.6),(10,24,3.3),
                  (-13,33,3.8),(14,40,3.6),(-16,8,2.4),(15,5,2.3)]:
        BASE.rock('V2 | large weathered forest outcrop',(x,y,terrain_height(x,y)+s*.6),
                   (s,s*.9,s*1.1),True)


def context_tree(x,y,height,radius,seed):
    local=random.Random(seed)
    z=terrain_height(x,y)
    leanx,leany=local.uniform(-1.3,1.3),local.uniform(-1,1)
    points=[(x,y,z),(x-.22,y+.15,z+height*.25),
            (x+leanx*.4,y+leany*.5,z+height*.58),
            (x+leanx,y+leany,z+height)]
    BASE.organic_tube('V2 | forest depth twisting trunk',points,
                      [radius,radius*.82,radius*.56,.14],
                      'v2_tree_brown',sides=14,steps=5,ridged=.13)
    for i in range(3):
        a=i*math.tau/3+seed*.13
        dx,dy=math.cos(a),math.sin(a)
        end=(x+dx*radius*2.9,y+dy*radius*2.9)
        BASE.organic_tube('V2 | forest depth buttress roots',
                          [(x,y,z+radius*1.5),(x+dx*radius*1.3,y+dy*radius*1.3,z+.4),
                           (end[0],end[1],terrain_height(*end)+.02)],
                          [radius*.45,radius*.26,.025],'v2_tree_brown',sides=10,steps=4)
    for i in range(3):
        a=local.uniform(0,math.tau)
        reach=local.uniform(2.5,4.0)
        bz=z+height*local.uniform(.64,.88)
        endpoint=(x+math.cos(a)*reach,y+math.sin(a)*reach,bz+1.8)
        BASE.organic_tube('V2 | forest depth crown branch',
                          [(x+leanx*.6,y+leany*.6,bz-1.6),
                           (x+math.cos(a)*reach*.55,y+math.sin(a)*reach*.55,bz+.7),endpoint],
                          [radius*.37,.22,.03],'v2_tree_brown',sides=10,steps=4)
        if i<2:
            clustered_foliage('V2 | layered forest crown',endpoint,
                              (2.3,2.0,.95),260,1.02)


def forest_depth():
    placements=[(-8.8,14.2,18,1.0),(8.7,15.6,19,1.1),
                (-5.8,23.0,20,1.25),(6.6,25,22,1.45),
                (-14,18,24,1.45),(15,22,25,1.6),
                (-11,34,25,1.4),(11,37,26,1.5),
                (-5,42,24,1.25),(5.8,47,27,1.3),
                (-20,34,28,1.6),(21,42,28,1.6),
                (-14,55,29,1.6),(15,59,28,1.4),
                (-24,55,28,1.5),(24,60,30,1.8),
                (-18,-2,20,1.1),(17,0,22,1.2),
                (-4.5,30,8.7,.38),(4.0,34,9.4,.42),
                (-8.0,48,24,1.1),(-2.5,56,27,1.4),
                (-13,63,25,1.1),(2.5,64,29,1.3)]
    for i,p in enumerate(placements):
        context_tree(*p,110+i)
    # Mid-storey growth fills the visible gap between tall trunks and the path.
    for x,y,z,size in [(-5.3,24.5,6.1,(2.2,1.9,1.2)),
                       (4.8,28,6.8,(2.5,2.1,1.4)),
                       (-3.8,35,4.5,(2.5,2.0,1.5)),
                       (1.7,39,5.3,(2.7,2.2,1.5)),
                       (-9,39,6.4,(2.5,2.1,1.4))]:
        clustered_foliage('V2 | mid-storey forest depth',(x,y,z),size,480,.95)
        BASE.organic_tube('V2 | mid-storey spreading limb',
                          [(x-.6,y+.4,terrain_height(x,y)),(x-.3,y+.2,z-1),(x,y,z+.1)],
                          [.25,.16,.03],'v2_tree_brown',sides=12,steps=5)
    # Visible settlement behind the gateway makes the path lead somewhere.
    for x,y in [(-4.0,18.7),(4.5,22.6)]:
        BASE.workshop('V2 | distant forest dwelling',(x,y,terrain_height(x,y)+.2),
                       4.4,3.5,.95)
    for x,y in [(-2.5,13.8),(2.8,18.6),(-2.7,25.6)]:
        BASE.lantern_post(x,y,terrain_height(x,y),-1 if x>0 else 1)
    # Rope-railed walkway extends the village's inhabitable architecture.
    for side in (-1,1):
        x=side*2.0
        for y in (11.4,14.2,17.0):
            z=terrain_height(x,y)
            BASE.beam('V2 | distant walkway carved post',(x,y,z),(x,y,z+1.1),.095,'wood_light')
        for y0,y1 in ((11.4,14.2),(14.2,17.0)):
            a=terrain_height(x,y0)+.96
            b=terrain_height(x,y1)+.96
            BASE.organic_tube('V2 | distant walkway sagging rope',
                              [(x,y0,a),(x,(y0+y1)/2,(a+b)/2-.12),(x,y1,b)],
                              [.032,.032,.032],'rope',sides=8,steps=5)


def fuller_understory():
    vertices,faces,indices=[],[],[]
    # Fine grass is grouped on margins and banks, never through the walking line.
    for i in range(1800):
        x,y=RNG.uniform(-15,15),RNG.uniform(-10,34)
        if abs(x)<1.8 or (abs(y-BASE.river_y(x))<1.3 and abs(x)<11):
            continue
        density=.72+.28*math.sin(x*.9+y*.43)
        if RNG.random()>density:
            continue
        z=max(terrain_height(x,y),BASE.ground_z(x,y)) if y<11 and abs(x)<10 else terrain_height(x,y)
        for j in range(4):
            origin=(x+RNG.uniform(-.10,.1),y+RNG.uniform(-.10,.1),z)
            rot=Euler((RNG.uniform(-.1,.1),-RNG.uniform(.65,1.25),RNG.uniform(0,math.tau))).to_quaternion()
            leaf(vertices,faces,origin,RNG.uniform(.15,.44),RNG.uniform(.025,.055),
                 rot,indices,RNG.choice([1,2,2,3]))
    obj=BASE.mesh_object('V2 | layered fine forest grasses',vertices,faces,'v2_leaf_deep')
    for name in ('v2_leaf_green','v2_leaf_sage','v2_leaf_gold','v2_leaf_young'):
        obj.data.materials.append(BASE.MATS[name])
    for p,i in zip(obj.data.polygons,indices):
        p.material_index=i
    for x,y,size in [(-3,-7,.9),(3.3,-6.8,1.3),(-3.5,-3.8,1.1),
                     (3.7,1.8,1.1),(-3,3.0,.9),(-7.5,4.6,1.2),
                     (7.7,6.4,1.3),(-7.8,10.8,1.1),(8.1,11.5,1.2),
                     (-4.3,14.5,1.0),(4,16.2,.8),(-6.7,-5.5,1.2),
                     (-9.8,-2.9,1.2),(9.3,-4.1,1.2)]:
        z=max(BASE.ground_z(x,y),terrain_height(x,y))
        clustered_foliage('V2 | layered woodland shrubs',(x,y,z+.35*size),
                          (.72*size,.62*size,.40*size),130,.57*size)
        BASE.fern('V2 | deep green understory fern',(x+.7,y-.55,z),size)
        BASE.flowers((x+.3,y-.6,0),size,12)
    # Small scatter is merged into one mesh to avoid hundreds of separate objects.
    vertices,faces=[],[]
    for i in range(260):
        y=RNG.uniform(-9.2,12)
        x=RNG.choice([-1,1])*RNG.uniform(1.35,1.85)
        if -3.8<y<1.1:
            continue
        z=BASE.ground_z(x,y)+.035
        sx,sy,sz=RNG.uniform(.025,.09),RNG.uniform(.025,.07),RNG.uniform(.02,.05)
        start=len(vertices)
        for ring in (-1,1):
            for j in range(6):
                a=math.tau*j/6
                vertices.append((x+math.cos(a)*sx,y+math.sin(a)*sy,z+ring*sz))
        for j in range(6):
            faces.append((start+j,start+(j+1)%6,start+(j+1)%6+6,start+j+6))
        faces.append(tuple(start+j+6 for j in range(6)))
    BASE.mesh_object('V2 | fine trail gravel',vertices,faces,'v2_gravel')


def refine_existing_crafts():
    stone_tex=bpy.data.textures.new('V2 | fractured softened stone',type='VORONOI')
    stone_tex.noise_scale=.42
    moss_tex=bpy.data.textures.new('V2 | uneven thick moss',type='CLOUDS')
    moss_tex.noise_scale=.17
    moss_tex.noise_depth=2
    for obj in list(bpy.context.scene.objects):
        if obj.type!='MESH' or obj.hide_render:
            continue
        if any(word in obj.name for word in ('boulder','bank stone','forest outcrop','worn pebble')):
            mod=obj.modifiers.new('Natural broken stone planes','DISPLACE')
            mod.texture=stone_tex
            mod.strength=.16
            mod.mid_level=.55
            mod.texture_coords='LOCAL'
        if obj.name.startswith('Cushion moss'):
            mod=obj.modifiers.new('Living moss relief','DISPLACE')
            mod.texture=moss_tex
            mod.strength=.16
            mod.mid_level=.5
        if 'aubergine canopy' in obj.name:
            for v in obj.data.vertices:
                v.co.z+=.030*math.sin(v.co.x*7.3+v.co.y*.5)*math.sin(v.co.y*2.3)
            sub=obj.modifiers.new('Supple hanging canvas folds','SUBSURF')
            sub.levels=sub.render_levels=2
        if obj.name.startswith('Heraldry of the branching grove'):
            sub=obj.modifiers.new('Soft woven fabric','SUBSURF')
            sub.levels=sub.render_levels=2
    # Apothecary counters receive varied jars and leaf bundles in place of repeated balls.
    for x,y,z in [(-5.8,.9,.5),(5.7,4.0,.65)]:
        for i in range(4):
            xx=x-1.1+i*.67
            yy=y-1.05
            scale=RNG.uniform(.13,.19)
            BASE.sphere('V2 | glazed apothecary jar',(xx,yy,z+1.15+scale),
                         (scale,scale,scale*1.35),'pot',24,16)
            BASE.cylinder('V2 | apothecary jar neck',(xx,yy,z+1.15+scale*2.1),
                           scale*.47,scale*.5,'pot',20)
            BASE.cylinder('V2 | cork stopper',(xx,yy,z+1.15+scale*2.4),
                           scale*.50,scale*.18,'wood_light',20)


def light_and_camera():
    for obj in bpy.context.scene.objects:
        if obj.type!='LIGHT':
            continue
        if obj.data.type=='AREA':
            obj.data.energy*=.35
        elif obj.data.type=='SUN':
            obj.data.energy=2.25
            obj.data.angle=.065
            obj.data.color=(1,.83,.58)
            obj.rotation_euler=(math.radians(33),math.radians(-31),math.radians(-28))
        elif obj.data.type=='POINT':
            obj.data.energy*=1.65
            obj.data.shadow_soft_size=.22
    world=bpy.context.scene.world
    world.use_nodes=True
    nodes,links=world.node_tree.nodes,world.node_tree.links
    ambient=nodes.get('Background')
    ambient.inputs['Color'].default_value=(.34,.47,.62,1)
    ambient.inputs['Strength'].default_value=.30
    sky=nodes.new('ShaderNodeBackground')
    sky.inputs['Strength'].default_value=.78
    coordinates=nodes.new('ShaderNodeTexCoord')
    separate=nodes.new('ShaderNodeSeparateXYZ')
    links.new(coordinates.outputs['Normal'],separate.inputs[0])
    ramp=nodes.new('ShaderNodeValToRGB')
    ramp.color_ramp.elements[0].position=0
    ramp.color_ramp.elements[0].color=(.65,.77,.84,1)
    ramp.color_ramp.elements[1].position=.8
    ramp.color_ramp.elements[1].color=(.22,.44,.69,1)
    links.new(separate.outputs['Z'],ramp.inputs[0])
    links.new(ramp.outputs['Color'],sky.inputs['Color'])
    ray=nodes.new('ShaderNodeLightPath')
    mix=nodes.new('ShaderNodeMixShader')
    links.new(ray.outputs['Is Camera Ray'],mix.inputs[0])
    links.new(ambient.outputs[0],mix.inputs[1])
    links.new(sky.outputs[0],mix.inputs[2])
    links.new(mix.outputs[0],nodes.get('World Output').inputs['Surface'])
    BASE.area_light('V2 | soft frontal forest bounce',(1,-9,8),(0,5,3),560,(.65,.79,1),10)
    BASE.area_light('V2 | golden light through upper trees',(-3,7,15),(0,0,0),950,(1,.78,.43),6)
    # Low density mist starts behind the portal and separates far tree layers.
    fog=new_mat('V2 | distant blue woodland atmosphere',(.1,.1,.1),1)
    nodes,links=fog.node_tree.nodes,fog.node_tree.links
    nodes.clear()
    out=nodes.new('ShaderNodeOutputMaterial')
    volume=nodes.new('ShaderNodeVolumePrincipled')
    volume.inputs['Density'].default_value=.006
    volume.inputs['Color'].default_value=(.52,.66,.75,1)
    volume.inputs['Anisotropy'].default_value=.32
    links.new(volume.outputs['Volume'],out.inputs['Volume'])
    obj=BASE.bevel_box('V2 | atmospheric depth volume',(0,48,15),(85,64,42),fog,.01)
    for mod in list(obj.modifiers):
        obj.modifiers.remove(mod)
    obj.display_type='WIRE'
    cam=BASE.camera('Camera_Refined',(3.2,-16,3.4),(0,5,3.6),30)
    cam.data.clip_end=300
    scene=bpy.context.scene
    scene.camera=cam
    scene.view_settings.view_transform='AgX'
    try:
        scene.view_settings.look='AgX - Medium High Contrast'
    except TypeError:
        pass
    scene.view_settings.exposure=.05
    scene.render.resolution_x=1800
    scene.render.resolution_y=1200
    scene.render.resolution_percentage=100


def refine_scene():
    RNG.seed(4471)
    setup_helpers()
    richer_materials()
    fused=fuse_ancient_trees()
    replace_canopies()
    continued_forest_floor()
    forest_depth()
    fuller_understory()
    refine_existing_crafts()
    light_and_camera()
    scene=bpy.context.scene
    scene['Arkya_Environment_Revision']='02 — sculpted woodland and forest depth'
    scene['Arkya_Environment_Revision_Source']='refine_terres_anciens.py'
    scene['Arkya_Environment_Notes']='Continuous remeshed trees; twig-grown leaves; extended inhabited forest; procedural bark, cloth and stone.'
    visible=[o for o in scene.objects if not o.hide_render]
    source_faces=sum(len(o.data.polygons) for o in visible if o.type=='MESH')
    return {'camera':'Camera_Refined','visible_objects':len(visible),
            'source_faces':source_faces,'fused_tree_faces':len(fused.data.polygons),
            'character_untouched':True,
            'description':'Sculpted continuous trees, detailed materials, dense branching foliage and inhabited forest depth.'}
