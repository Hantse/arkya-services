"""Third Ether art pass. Apply to the saved v2 scene; characters are untouched."""
import bpy
import math
import random
from mathutils import Vector

def refine_scene():
    rng=random.Random(3624)
    collection=bpy.data.collections.new('Ether V3 | crafted city detail')
    bpy.context.scene.collection.children.link(collection)
    def obj(name,data):
        o=bpy.data.objects.new('V3 | '+name,data);collection.objects.link(o);return o
    def material(name,color,rough=.7,metal=0):
        m=bpy.data.materials.new(name);m.use_nodes=True;m.diffuse_color=(*color,1)
        p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(*color,1)
        p.inputs['Roughness'].default_value=rough;p.inputs['Metallic'].default_value=metal
        return m
    def mesh(name,verts,faces,mat):
        d=bpy.data.meshes.new(name);d.from_pydata(verts,[],faces);d.update();d.materials.append(mat)
        o=obj(name,d)
        for p in d.polygons:p.use_smooth=True
        return o
    def line(name,points,width,mat):
        d=bpy.data.curves.new(name,'CURVE');d.dimensions='3D';d.bevel_depth=width;d.bevel_resolution=3
        s=d.splines.new('BEZIER');s.bezier_points.add(len(points)-1)
        for p,co in zip(s.bezier_points,points):p.co=co;p.handle_left_type=p.handle_right_type='AUTO'
        o=obj(name,d);d.materials.append(mat);return o
    def box(name,loc,scale,mat,edge=.03):
        x,y,z=[s/2 for s in scale]
        o=mesh(name,[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),(-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)],[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],mat)
        o.location=loc;m=o.modifiers.new('Rounded crafted corners','BEVEL');m.width=edge;m.segments=3
        o.modifiers.new('Weighted normals','WEIGHTED_NORMAL');return o
    # Separate the distant pointed gate from the foreground open-ring landmark.
    for o in list(bpy.context.scene.objects):
        if o.name.startswith('V2 ') and o.type in {'MESH','CURVE'} and not o.name.startswith(('V2 Continuous','V2 Extended')):
            center=sum((o.matrix_world@Vector(p) for p in o.bound_box),Vector())/8
            if center.y>14:o.location+=Vector((3.3,7.0,0))
        if o.name.startswith('Lantern') and o.location.y<0:
            # Bring the right foreground post back to the paving margin.
            o.location.x+=1.35 if o.location.x>0 else -.35
    # Broad restrained variation makes adjacent stone blocks distinguishable.
    for m in list(bpy.data.materials):
        if not m.use_nodes or not m.name.startswith(('Warm carved limestone','Pale ivory enamel','Plaza limestone','Honey limestone','Distant limestone')):continue
        nodes,links=m.node_tree.nodes,m.node_tree.links
        p=nodes.get('Principled BSDF')
        if not p:continue
        original=p.inputs['Base Color'].links[0].from_socket if p.inputs['Base Color'].is_linked else None
        color=tuple(p.inputs['Base Color'].default_value)
        noise=nodes.new('ShaderNodeTexNoise');noise.inputs['Scale'].default_value=3.7;noise.inputs['Detail'].default_value=2
        ramp=nodes.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.18;ramp.color_ramp.elements[0].color=(.58,.55,.48,1)
        ramp.color_ramp.elements[1].position=.82;ramp.color_ramp.elements[1].color=(1,.98,.91,1)
        links.new(noise.outputs['Fac'],ramp.inputs[0])
        mix=nodes.new('ShaderNodeMixRGB');mix.blend_type='MULTIPLY';mix.inputs[0].default_value=.4
        if original:links.new(original,mix.inputs[1])
        else:mix.inputs[1].default_value=color
        links.new(ramp.outputs['Color'],mix.inputs[2]);links.new(mix.outputs[0],p.inputs['Base Color'])
        info=nodes.new('ShaderNodeObjectInfo');variation=nodes.new('ShaderNodeMapRange')
        variation.inputs['To Min'].default_value=.83;variation.inputs['To Max'].default_value=1.03
        links.new(info.outputs['Random'],variation.inputs['Value'])
        multiply=nodes.new('ShaderNodeMixRGB');multiply.blend_type='MULTIPLY';multiply.inputs[0].default_value=.65
        links.new(mix.outputs[0],multiply.inputs[1]);links.new(variation.outputs[0],multiply.inputs[2]);links.new(multiply.outputs[0],p.inputs['Base Color'])
    bronze=bpy.data.materials['Brushed old gold'];stone=bpy.data.materials['Warm carved limestone']
    # More delicate cool lanterns echo the crystal and avoid luminous yellow boxes.
    for m in bpy.data.materials:
        if m.name=='Lantern warm glass':
            p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=(.03,.26,.28,1)
            p.inputs['Emission Color'].default_value=(.05,.65,.76,1);p.inputs['Emission Strength'].default_value=.8
    for name in ('Ether crystal cyan','Ether crystal light facet','Ether crystal deep facet'):
        p=bpy.data.materials[name].node_tree.nodes.get('Principled BSDF')
        p.inputs['Emission Color'].default_value=(.025,.45,.70,1);p.inputs['Emission Strength'].default_value=.32
        p.inputs['Transmission Weight'].default_value=.36
    # Asymmetric artisan stall with real cloth folds and hand-built counter.
    wood=material('V3 warm walnut',(.12,.05,.024),.6)
    cloth=material('V3 terracotta awning',(.39,.075,.043),.83)
    cream=material('V3 awning ivory edge',(.66,.49,.26),.8)
    for x in [5.9,9.4]:
        for y in [1.3,4.4]:box('Market upright',(x,y,2.65),(.1,.1,2.9),wood)
    verts=[];faces=[]
    for j in range(17):
        v=j/16
        for i in range(31):
            u=i/30;x=5.8+3.7*u;y=1.1+3.45*v
            z=4.2+.65*v-.17*math.sin(math.pi*u)+.045*math.sin(u*math.pi*14)*(1-v*.4)
            verts.append((x,y,z))
    for j in range(16):
        for i in range(30):
            q=j*31+i;faces.append((q,q+1,q+32,q+31))
    awning=mesh('Asymmetric market canvas',verts,faces,cloth);sol=awning.modifiers.new('Canvas thickness','SOLIDIFY');sol.thickness=.016
    for side in [0,30]:line('Awning woven edge',[verts[j*31+side] for j in range(17)],.018,cream)
    line('Awning lower tasseled edge',verts[:31],.023,cream)
    for x in [6.2,7.3,8.4,9.0]:
        box('Artisan counter board',(x,1.7,2.05),(.95,.85,.13),wood)
    for x in [6.2,8.8]:box('Counter legs',(x,1.7,1.62),(.13,.6,.85),wood)
    for k in range(9):
        x=6.15+.33*k
        box('Bound artisan books',(x,1.7,2.20+rng.uniform(0,.08)),(.24,.38,.16+rng.uniform(0,.14)),cream if k%3==0 else wood,.015)
    # Ivy and bell flowers use broad overlapping leaves with irregular clustering.
    leaves=[material('V3 ivy '+str(i),c,.72) for i,c in enumerate([(.038,.10,.016),(.08,.21,.025),(.18,.28,.045)])]
    lavender=material('V3 hanging lavender blossoms',(.34,.12,.46),.65)
    verts=[];faces=[];indices=[]
    def leaf(center,length,width,angle,mat_index):
        center=Vector(center);d=Vector((math.sin(angle),-.18,-math.cos(angle)));cross=Vector((math.cos(angle),0,math.sin(angle)))
        start=len(verts)
        for t in [0,.25,.5,.75,1]:
            w=width*math.sin(math.pi*t)
            mid=center+d*length*t+Vector((0,-math.sin(math.pi*t)*.035,0))
            verts.extend([mid-cross*w,mid+Vector((0,-.025,0)),mid+cross*w])
        for n in range(4):
            q=start+n*3;faces.extend([(q,q+3,q+4,q+1),(q+1,q+4,q+5,q+2)]);indices.extend([mat_index]*2)
    for side in [-1,1]:
        for column in range(8):
            x=side*(5.0+column*.43);top=6.25+rng.uniform(-.4,.35);length=rng.uniform(.7,2.9)
            pts=[]
            for j in range(13):
                t=j/12;co=(x+.15*math.sin(t*5+column),8.65-.07*math.sin(t*3),top-length*t);pts.append(co)
                for k in [-1,1]:leaf(co,rng.uniform(.18,.35),.10,k*rng.uniform(.45,1.25),rng.randrange(3))
                if j>6 and column%3==0:
                    for k in range(3):leaf(Vector(co)+Vector((rng.uniform(-.05,.05),-.03,-k*.09)),.11,.06,k*1.9,3)
            line('Irregular ivy stem',pts,.012,wood)
    ivy=mesh('Interwoven living ivy and flowers',verts,faces,leaves[0])
    for m in leaves[1:]+[lavender]:ivy.data.materials.append(m)
    for p,i in zip(ivy.data.polygons,indices):p.material_index=i
    # A foreground tree casts leaf shadows across the approach and frames the sky.
    line('Foreground garden branch',[(-17,-7,6.5),(-11,-6,8.7),(-6,-5.5,9.3),(-3.5,-5,9.0)],.14,wood)
    verts=[];faces=[];indices=[]
    for i in range(540):
        x=rng.uniform(-13,-2.5);y=rng.uniform(-8,-2.5);z=9.1+rng.uniform(-.7,.8)
        if rng.random()>.65+.25*math.sin(x*.6+y):continue
        angle=rng.uniform(0,math.tau);length=rng.uniform(.35,.7);width=length*.35
        f=Vector((math.cos(angle),math.sin(angle),rng.uniform(-.3,.2)));c=Vector((-math.sin(angle),math.cos(angle),.1));start=len(verts)
        for t in [0,.25,.5,.75,1]:
            mid=Vector((x,y,z))+f*length*t;w=width*math.sin(math.pi*t)
            verts.extend([mid-c*w,mid+Vector((0,0,.045)),mid+c*w])
        for j in range(4):
            q=start+j*3;faces.extend([(q,q+3,q+4,q+1),(q+1,q+4,q+5,q+2)]);indices.extend([i%3]*2)
    crown=mesh('Foreground living canopy',verts,faces,leaves[0])
    soft=crown.modifiers.new('Supple leaf outlines','SUBSURF');soft.levels=soft.render_levels=1
    for m in leaves[1:]:crown.data.materials.append(m)
    for p,i in zip(crown.data.polygons,indices):p.material_index=i
    # Small inset joints make the ring feel engraved at a useful reading distance.
    for side in [-1,1]:
        for angle in [35,60,85,110,135]:
            a=math.radians(angle if side==1 else 180+angle)
            x=3.2*math.cos(a);z=6.75+3.2*math.sin(a)
            line('Ring incised chevron',[(x-.09,6.09,z+.11),(x,6.08,z),(x+.09,6.09,z+.11)],.014,bronze)
    # Directional sun, cool bounce, and a brighter horizon create distinct planes.
    world=bpy.context.scene.world;nodes=world.node_tree.nodes;bg=nodes.get('Background')
    bg.inputs['Strength'].default_value=.11
    sky=next((n for n in nodes if n.type=='TEX_SKY'),None)
    if sky:
        sky.sun_elevation=math.radians(27);sky.sun_rotation=math.radians(135);sky.dust_density=.7;sky.air_density=.9
    for o in bpy.context.scene.objects:
        if o.type=='LIGHT':
            if o.data.type=='SUN':
                o.data.energy=3.7;o.data.angle=.035;o.data.color=(1,.83,.64)
                o.rotation_euler=(Vector((0,4,0))-Vector((-14,3,20))).to_track_quat('-Z','Y').to_euler()
            elif o.data.type=='AREA':o.data.energy*=.65
    # Pale blue haze is restricted to the distant architecture, not the hero.
    haze=material('V3 atmospheric city depth',(.5,.6,.7));n=haze.node_tree.nodes;n.clear()
    out=n.new('ShaderNodeOutputMaterial');volume=n.new('ShaderNodeVolumePrincipled')
    volume.inputs['Density'].default_value=.0038;volume.inputs['Color'].default_value=(.64,.76,.87,1)
    haze.node_tree.links.new(volume.outputs['Volume'],out.inputs['Volume'])
    box('Distant city atmosphere',(0,63,25),(110,68,55),haze,.001)
    camdata=bpy.data.cameras.new('Camera_V3');camdata.lens=28;camdata.clip_end=400
    cam=obj('Camera_V3',camdata);cam.name='Camera_V3';cam.location=(2.7,-17.5,3.55)
    cam.rotation_euler=(Vector((0,9,5.65))-cam.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.scene.camera=cam;bpy.context.scene.view_settings.exposure=-.15
    return {'hero_position':[-.6,-7.6,1.25],'camera':'Camera_V3','improvements':['Separated foreground ring and distant gate','Asymmetric market and botanical detail','Dressed stone variation and cool lanterns','Directed daylight and atmospheric depth']}
