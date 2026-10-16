"""Second environment study, applied to a fresh copy of the original Ether scene.

The caller loads the v1 blend, invokes refine_scene(), adds its characters, and
owns saving and rendering. All added geometry and shaders are procedural.
"""

import bpy
import math
import random
from mathutils import Vector


def refine_scene():
    rng = random.Random(217)
    collection = bpy.data.collections.new('Ether V2 - city and botanical detail')
    bpy.context.scene.collection.children.link(collection)

    def mat(name, color, rough=.6, metal=0):
        m=bpy.data.materials.new(name)
        m.use_nodes=True
        m.diffuse_color=(*color,1)
        p=m.node_tree.nodes.get('Principled BSDF')
        p.inputs['Base Color'].default_value=(*color,1)
        p.inputs['Roughness'].default_value=rough
        p.inputs['Metallic'].default_value=metal
        return m

    stone=bpy.data.materials['Warm carved limestone']
    pale=bpy.data.materials['Pale ivory enamel']
    bronze=bpy.data.materials['Brushed old gold']
    teal=bpy.data.materials['Peacock enamel roofs']
    dark=bpy.data.materials['Petroleum blue recess']
    cloth=bpy.data.materials['Coral woven cloth']
    bark=bpy.data.materials['Cypress trunks']
    leafmats=[mat('V2 leaf %d'%i,c,.78) for i,c in enumerate([
        (.035,.115,.025),(.075,.20,.038),(.12,.27,.055),(.23,.36,.08),(.09,.18,.035)])]
    distant=mat('Distant limestone atmospheric blue',(.53,.61,.58),.77)
    cliffmat=mat('Warm limestone cliffs',(.36,.39,.32),.89)
    soil=mat('Garden bed humus',(.075,.057,.022),.94)
    flowers=[mat('Violet trumpet blossom',(.30,.075,.39),.65),
             mat('Lavender trumpet blossom',(.52,.20,.63),.65)]

    def attach(obj):
        collection.objects.link(obj)
        return obj

    def mesh(name,vs,fs,material,smooth=False):
        data=bpy.data.meshes.new(name+' mesh')
        data.from_pydata(vs,[],fs)
        data.update()
        ob=attach(bpy.data.objects.new('V2 '+name,data))
        data.materials.append(material)
        for p in data.polygons:p.use_smooth=smooth
        return ob

    def bevel(ob,width=.035,segments=3):
        m=ob.modifiers.new('Crafted edge rounding','BEVEL')
        m.width=width
        m.segments=segments
        m.harden_normals=True
        w=ob.modifiers.new('Architectural weighted normals','WEIGHTED_NORMAL')
        w.keep_sharp=True
        return ob

    def box(name,loc,dim,material,edge=.035):
        x,y,z=[v*.5 for v in dim]
        ob=mesh(name,[(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
                     (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)],
                    [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)],material)
        ob.location=loc
        if edge:bevel(ob,edge)
        return ob

    def cylinder(name,loc,radius,depth,material,top=None,n=48):
        if top is None:top=radius
        vs=[]
        for r,z in [(radius,-depth*.5),(top,depth*.5)]:
            for i in range(n):
                a=2*math.pi*i/n
                vs.append((r*math.cos(a),r*math.sin(a),z))
        fs=[tuple(reversed(range(n))),tuple(n+i for i in range(n))]
        fs += [(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
        ob=mesh(name,vs,fs,material,True)
        ob.data.polygons[0].use_smooth=False
        ob.data.polygons[1].use_smooth=False
        ob.location=loc
        bevel(ob,.022)
        return ob

    def line(name,coords,radius,material,cyclic=False):
        data=bpy.data.curves.new(name+' spline','CURVE')
        data.dimensions='3D'
        data.resolution_u=12
        data.bevel_depth=radius
        data.bevel_resolution=3
        s=data.splines.new('BEZIER')
        s.bezier_points.add(len(coords)-1)
        for p,co in zip(s.bezier_points,coords):
            p.co=co
            p.handle_left_type='AUTO'
            p.handle_right_type='AUTO'
        s.use_cyclic_u=cyclic
        ob=attach(bpy.data.objects.new('V2 '+name,data))
        data.materials.append(material)
        return ob

    def lathe(name,x,y,profile,material,n=72):
        vs=[]
        for r,z in profile:
            for i in range(n):
                a=2*math.pi*i/n
                vs.append((x+r*math.cos(a),y+r*math.sin(a),z))
        fs=[]
        for row in range(len(profile)-1):
            for i in range(n):
                j=(i+1)%n
                fs.append((row*n+i,row*n+j,(row+1)*n+j,(row+1)*n+i))
        return mesh(name,vs,fs,material,True)

    def prism(name,points,y,depth,material,edge=.035):
        count=len(points)
        vs=[(x,y+d,z) for d in [-depth/2,depth/2] for x,z in points]
        fs=[tuple(reversed(range(count))),tuple(count+i for i in range(count))]
        fs += [(i,(i+1)%count,(i+1)%count+count,i+count) for i in range(count)]
        ob=mesh(name,vs,fs,material)
        if edge:bevel(ob,edge)
        return ob

    def pointed_path(x,spring,r,n=28):
        pts=[]
        for i in range(n+1):
            a=math.pi-i*math.pi/3/n
            pts.append((x+r+2*r*math.cos(a),spring+2*r*math.sin(a)))
        for i in range(1,n+1):
            a=math.pi/3-i*math.pi/3/n
            pts.append((x-r+2*r*math.cos(a),spring+2*r*math.sin(a)))
        return pts

    def pointed_arch(name,x,y,spring,r,width,depth,material):
        pts=pointed_path(x,spring,r)
        vs=[]
        for i,p in enumerate(pts):
            tangent=Vector(pts[min(i+1,len(pts)-1)])-Vector(pts[max(i-1,0)])
            tangent.normalize()
            normal=Vector((-tangent.y,tangent.x))
            outer=Vector(p)+normal*width
            for q,d in [(p,-depth*.5),(outer,-depth*.5),(outer,depth*.5),(p,depth*.5)]:
                vs.append((q[0],y+d,q[1]))
        fs=[(0,3,2,1)]
        for i in range(len(pts)-1):
            for j in range(4):
                k=4*i
                fs.append((k+j,k+(j+1)%4,k+4+(j+1)%4,k+4+j))
        k=4*(len(pts)-1)
        fs.append((k,k+1,k+2,k+3))
        return bevel(mesh(name,vs,fs,material),.045)

    def arch_window(x,y,z,width=1.15,height=2.5):
        r=width*.5
        spring=z+height-r*math.sqrt(3)
        pts=[(x-r,z)]+pointed_path(x,spring,r)+[(x+r,z)]
        prism('Pointed recessed dark window',pts,y,.09,dark,.015)
        pointed_arch('Carved window archivolt',x,y-.11,spring,r,.13,.18,pale)
        for dx in [-r,r]:box('Window stone jamb',(x+dx,y-.10,(z+spring)*.5),(.13,.20,spring-z),pale,.02)
        box('Window broad sill',(x,y-.16,z),(.40+width,.43,.15),pale,.04)
        box('Window bronze mullion',(x,y-.18,z+height*.45),(.045,.07,height*.89),bronze,.008)
        for dz in [.35,.67]:box('Window bronze horizontal tracery',(x,y-.18,z+height*dz),(width*.91,.07,.035),bronze,.007)

    # Preserve the source objects and hide its presentation plinth, placeholder foliage and horizon.
    hide_prefixes=('Diorama ','Foundation limestone cornice','Bronze foundation ribbon','Infinite presentation ground',
                   'Cypress sculpted foliage','Cypress tapered crown','Cypress narrow trunk',
                   'Garden shaped foliage','Violet garden blossom','Ivy hanging leaf','Trailing garden ivy stem')
    for ob in list(bpy.context.scene.objects):
        if ob.name.startswith(hide_prefixes):
            ob.hide_render=True
            ob.hide_viewport=True
        if ob.name.startswith('Traveler'):continue
        if ob.type=='MESH':
            for m in ob.modifiers:
                if m.type=='BEVEL':m.width*=.62

    # Coarse stone, fine fired enamel, woven cloth and satin metal carry separate tactile cues.
    def detail_shader(m,noise_scale,bump_distance,bump_strength,roughness,variation=.13):
        nodes=m.node_tree.nodes
        links=m.node_tree.links
        p=nodes.get('Principled BSDF')
        p.inputs['Roughness'].default_value=roughness
        tex=nodes.new('ShaderNodeTexNoise')
        tex.name='V2 physical microstructure'
        tex.inputs['Scale'].default_value=noise_scale
        tex.inputs['Detail'].default_value=3.0
        bump=nodes.new('ShaderNodeBump')
        bump.inputs['Distance'].default_value=bump_distance
        bump.inputs['Strength'].default_value=bump_strength
        links.new(tex.outputs['Fac'],bump.inputs['Height'])
        links.new(bump.outputs['Normal'],p.inputs['Normal'])
        ramp=nodes.new('ShaderNodeMapRange')
        ramp.inputs['From Min'].default_value=.15
        ramp.inputs['From Max'].default_value=.85
        ramp.inputs['To Min'].default_value=max(.05,roughness-variation)
        ramp.inputs['To Max'].default_value=min(.95,roughness+variation)
        links.new(tex.outputs['Fac'],ramp.inputs['Value'])
        links.new(ramp.outputs[0],p.inputs['Roughness'])
    for m in list(bpy.data.materials):
        if m.name.startswith(('Warm carved','Pale ivory','Honey limestone','Plaza limestone')):
            detail_shader(m,46,.035,.24,.72,.10)
        elif m.name.startswith('Brushed old gold'):
            detail_shader(m,95,.012,.18,.34,.10)
        elif m.name.startswith('Peacock enamel'):
            detail_shader(m,120,.008,.15,.40,.065)
        elif m.name.startswith(('Coral woven','Salmon cloth')):
            detail_shader(m,185,.020,.30,.83,.10)
    # Glass windows read as dark openings; their reflection is the highlight.
    p=bpy.data.materials['Quiet turquoise glass'].node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(.008,.045,.068,1)
    p.inputs['Roughness'].default_value=.19
    p.inputs['Metallic'].default_value=.35
    for name in ['Ether crystal cyan','Ether crystal light facet','Ether crystal deep facet']:
        p=bpy.data.materials[name].node_tree.nodes.get('Principled BSDF')
        p.inputs['Transmission Weight'].default_value=.65
        p.inputs['Metallic'].default_value=.015
        p.inputs['Roughness'].default_value=.085
        p.inputs['Emission Strength'].default_value=.025
        p.inputs['IOR'].default_value=1.45
        p.inputs['Coat Weight'].default_value=.40
        p.inputs['Coat Roughness'].default_value=.13

    # Continuous ground establishes an inhabited city instead of an isolated display base.
    box('Continuous city limestone foundation',(0,15,.89),(145,160,.48),stone,.05)
    for row in range(10):
        for col in range(13):
            x=-8.0+col*1.33+(row%2)*.23
            y=-10.85-row*1.33
            tile=bpy.data.materials['Plaza limestone %02d'%(rng.randrange(6))]
            ob=box('Extended approach paving',(x,y,1.15),(1.28,1.28,.16),tile,.026)
            ob.rotation_euler[2]=rng.uniform(-.003,.003)

    # Tall background gate and adjoining walls supply an actual layered architectural horizon.
    gate_y=17.6
    for side in [-1,1]:
        for row in range(16):
            left=4.70
            end=22.70
            boundaries=[left]
            first=left+(1.14 if row%2 else 2.28)
            boundaries += [first+i*2.28 for i in range(8) if first+i*2.28<end]
            boundaries.append(end)
            for a,b in zip(boundaries[:-1],boundaries[1:]):
                box('Gatehouse individually dressed stone course',
                    (side*(a+b)*.5,gate_y,1.2+(row+.5)*.85),
                    (b-a-.018,3.6,.831),stone,.022)
        box('Gatehouse lower chamfered footing',(side*13.7,gate_y-1.0,1.77),(18.4,2.5,1.0),pale,.08)
        for z,depth,height in [(3.6,.22,.13),(7.0,.29,.22),(11.8,.34,.20),(14.8,.85,.36)]:
            box('Gatehouse continuous molding',(side*13.7,gate_y-1.85,z),(18.3,depth,height),pale,.035)
        for xx in [6.5,10.9,15.3,19.7]:
            x=side*xx
            box('Facade carved pilaster',(x,gate_y-1.92,8.4),(.54,.50,12.4),pale,.06)
            box('Pilaster bronze shoe',(x,gate_y-2.0,2.15),(.72,.62,.24),bronze,.025)
            for z in [6.9,11.6,14.5]:
                box('Pilaster layered capital',(x,gate_y-2.0,z),(.86,.76,.26),pale,.04)
        for xx in [8.6,13.0,17.4,21.2]:
            for zz in [4.0,8.1]:arch_window(side*xx,gate_y-1.87,zz,1.22,2.6)
    gate_r=4.0
    spring=6.4
    path=pointed_path(0,spring,gate_r)
    crown=[(-4.8,spring)]+path+[(4.8,spring),(4.8,17.1),(-4.8,17.1)]
    prism('Masonry over vaulted gateway',crown,gate_y,3.6,stone,.07)
    pointed_arch('Monumental gate carved archivolt',0,gate_y-2.02,spring,gate_r,.49,.72,pale)
    pointed_arch('Gate thin recessed bronze order',0,gate_y-2.43,spring,gate_r+.15,.05,.07,bronze)
    for x in [-4.3,4.3]:
        box('Gateway broad stone pier',(x,gate_y-1.55,3.8),(.62,1.0,5.2),pale,.07)
        for dx in [-.16,.16]:cylinder('Gateway clustered shaft',(x+dx,gate_y-2.15,3.9),.12,5.0,pale)
        box('Gateway column leaf capital',(x,gate_y-2.02,spring),(.94,1.1,.40),pale,.06)
    for z,h,depth in [(14.6,.25,.4),(16.8,.36,.8),(17.18,.13,1.0)]:
        box('Gatehouse crown molding',(0,gate_y-1.8,z),(10.5,depth,h),pale,.035)
    for x in [-3,-1.5,0,1.5,3]:
        arch_window(x,gate_y-1.90,14.65,.69,1.65)
    # Recessed ribs are real structure along the depth of the vaulted passage.
    for y in [17.0,18.5,20.0]:
        pointed_arch('Passage interior vaulted rib',0,y,spring,gate_r-.03,.15,.18,pale)

    # Upper palace terraces, towers and bridges create overlapping depth behind the gateway.
    for side in [-1,1]:
        box('High city rear terrace side',(side*13.3,29,3.2),(19.4,18,3.7),distant,.18)
    box('High city rear terrace beyond stair',(0,33.0,3.2),(7.2,10,3.7),distant,.16)
    for i in range(14):
        box('Stairs beyond great gate',(0,20.3+i*.56,1.32+i*.30),(6.9,.78,.36),pale,.045)
    for side in [-1,1]:
        box('Upper terrace balustrade base',(side*13,25,5.0),(18,.7,.35),pale,.06)
        box('Upper terrace uninterrupted cap',(side*13,25,6.41),(18.4,.87,.21),pale,.055)
    for x in [n for n in range(-20,21,2) if abs(n)>=4]:
        box('Upper terrace parapet pier',(x,25,5.75),(.48,.65,1.35),pale,.04)

    def tower(x,y,bottom,r,height,bodymat):
        cylinder('Palace tower faceted foundation',(x,y,bottom+.4),r*1.25,.8,pale,n=12)
        cylinder('Palace round tower',(x,y,bottom+height*.5),r,height,bodymat,n=72)
        for z in [bottom+.8,bottom+height*.45,bottom+height-.55]:
            lathe('Tower layered carved cornice',x,y,[(r*.99,z-.17),(r*1.04,z-.12),
                      (r*1.08,z),(r*1.08,z+.12),(r*1.015,z+.20)],pale)
        roof_z=bottom+height
        profile=[(r*1.19,roof_z-.18),(r*1.19,roof_z),(r*1.14,roof_z+.15)]
        profile += [(r*1.12*math.cos(t),roof_z+.18+r*1.18*math.sin(t))
                     for t in [math.pi*.5*i/24 for i in range(25)]]
        lathe('Distant turquoise copper cupola',x,y,profile,teal)
        for k in range(12):
            a=2*math.pi*k/12
            coords=[]
            for i in range(15):
                t=math.pi*.5*i/14
                rr=r*1.13*math.cos(t)
                coords.append((x+rr*math.cos(a),y+rr*math.sin(a),roof_z+.20+r*1.18*math.sin(t)))
            line('Palace dome bronze seam',coords,.022,bronze)
        cylinder('Palace spire needle',(x,y,roof_z+r*1.18+.68),r*.18,1.3,bronze,top=.015,n=32)
        for z in [bottom+height*.20,bottom+height*.61]:
            arch_window(x,y-r-.025,z,r*.60,height*.24)
        for a in [math.radians(-58),math.radians(-122)]:
            px=x+r*math.cos(a)
            py=y+r*math.sin(a)
            box('Tower corner gold fillet',(px,py,bottom+height*.53),(.055,.08,height*.72),bronze,.008)

    tower(-13.4,19.2,14.4,2.3,7.9,stone)
    tower(14.8,22.5,14.4,2.9,8.6,stone)
    tower(-5.8,36,5.1,3.4,21.0,distant)
    tower(7.0,38.5,5.1,4.5,22.7,distant)
    tower(21.0,39,5.1,2.3,15.3,distant)
    box('Distant palace nave',(-1,37,14.2),(20,9,18.2),distant,.18)
    for x in [-8.8,-4.9,-1,2.9,6.8]:
        arch_window(x,32.4,9.6,1.75,5.8)
    box('Distant palace cornice',(-1,32.3,23.4),(21,.75,.53),pale,.09)

    # Side walls turn the foreground square into part of a street network.
    for side in [-1,1]:
        box('Side district building',(side*16.1,0.8,7.0),(7.4,27.0,11.5),stone,.12)
        box('Side district top cornice',(side*16.1,0.8,12.8),(7.8,27.4,.48),pale,.08)
        for y in [-9.8,-4.5,.8,6.1,11.4]:
            box('Side wall buttress',(side*12.30,y,7.4),(.76,.90,11.3),pale,.07)
            for z in [2.0,6.4,12.45]:
                box('Side buttress molding',(side*12.20,y,z),(.98,1.20,.28),pale,.045)
        # Visible inside faces get tall framed recesses, without pasted images.
        for y in [-7.2,-1.8,3.5,8.8]:
            for z in [4.6,9.1]:
                box('Side district recessed opening',(side*12.36,y,z),(.06,2.1,2.8),dark,.02)
                for yy in [-1.14,1.14]:
                    box('Side window carved stone jamb',(side*12.25,y+yy,z),(.23,.16,3.14),pale,.03)
                for zz in [-1.52,1.52]:
                    box('Side window projecting sill',(side*12.20,y,z+zz),(.37,2.5,.20),pale,.04)
                box('Side window bronze central bar',(side*12.15,y,z),(.05,.045,2.85),bronze,.008)
                box('Side window bronze transom',(side*12.15,y,z+.35),(.05,2.15,.045),bronze,.008)

    # Additional profile moldings on the original pavilions replace their simple toy edges.
    for x in [-7.0,7.0]:
        y=6.65
        for z in [2.16,4.53]:
            lathe('Pavilion finely carved stringcourse',x,y,[(1.97,z-.12),(2.025,z-.07),(2.04,z),
                    (2.04,z+.06),(1.99,z+.13)],pale)
        lathe('Pavilion deep shadow cornice',x,y,[(2.02,4.81),(2.08,4.85),(2.14,4.91),(2.22,4.96),
                (2.24,5.0),(2.14,5.045)],bronze)
        for i in range(32):
            a=2*math.pi*i/32
            ob=box('Pavilion carved frieze dentil',(x+2.07*math.cos(a),y+2.07*math.sin(a),4.70),(.13,.18,.21),pale,.017)
            ob.rotation_euler[2]=a
    # Paired fine incised lines give the focus ring a crafted, assembled surface.
    for start,end in [(-68,68),(112,248)]:
        for rad in [3.075,3.20]:
            line('Fine ring concentric engraving',[(rad*math.cos(math.radians(a)),6.178,
                  6.75+rad*math.sin(math.radians(a))) for a in range(start+2,end-1,3)],.011,bronze)

    # Botanical meshes have individual curved leaf blades and branched, irregular silhouettes.
    def add_leaf(vs,fs,indices,center,axis,length,width,shade):
        axis=Vector(axis).normalized()
        side=axis.cross(Vector((0,0,1)))
        if side.length<.05:side=axis.cross(Vector((0,1,0)))
        side.normalize()
        up=side.cross(axis).normalized()
        c=Vector(center)
        k=len(vs)
        # A raised midrib creates highlights without a translucent flat card.
        coords=[c-axis*length*.48,c-axis*length*.19-side*width*.42,
                c+axis*length*.19-side*width*.42,c+axis*length*.52+up*length*.10,
                c+axis*length*.19+side*width*.42,c-axis*length*.19+side*width*.42,
                c+up*width*.19]
        vs.extend(tuple(p) for p in coords)
        for i in range(6):
            fs.append((k+i,k+(i+1)%6,k+6))
            indices.append(shade)

    def foliage_mesh(name,vs,fs,indices):
        ob=mesh(name,vs,fs,leafmats[0],True)
        for m in leafmats[1:]:ob.data.materials.append(m)
        for p,idx in zip(ob.data.polygons,indices):p.material_index=idx
        return ob

    def cypress(x,y,bottom,height=4.2,seed=0):
        local=random.Random(400+seed)
        cylinder('Cypress visible tapered trunk',(x,y,bottom+height*.38),.105,height*.75,bark,top=.025,n=20)
        vs=[];fs=[];idx=[]
        for layer in range(42):
            t=layer/41
            z=bottom+.25+t*height
            envelope=.69*(math.sin(math.pi*(.12+.84*t))**.60)*(1-t*.72)
            center=(x+.12*math.sin(t*4),y+.10*math.cos(t*5))
            for branch in range(11):
                angle=branch*math.tau/11+layer*1.91
                reach=envelope*local.uniform(.74,1.06)
                end=(center[0]+reach*math.cos(angle),center[1]+reach*math.sin(angle),z+.22)
                if layer%4==0 and branch%3==0:
                    line('Cypress upward woody spray',[(center[0],center[1],z-.08),
                         (center[0]+reach*.5*math.cos(angle),center[1]+reach*.5*math.sin(angle),z),end],.009,bark)
                for leaf_i in range(10):
                    along=.20+.08*leaf_i
                    side=(leaf_i%2)*2-1
                    px=center[0]+reach*along*math.cos(angle)+local.uniform(-.07,.07)
                    py=center[1]+reach*along*math.sin(angle)+local.uniform(-.07,.07)
                    pz=z+.22*along+local.uniform(-.07,.07)
                    direction=(math.cos(angle+side*.75)*.45,math.sin(angle+side*.75)*.45,.80)
                    add_leaf(vs,fs,idx,(px,py,pz),direction,local.uniform(.16,.29),.065,local.randrange(5))
        foliage_mesh('Cypress individual foliage sprays',vs,fs,idx)

    for i,(x,y) in enumerate([(-9,9.4),(9,9.4),(-4.6,6),(4.6,6),(-8.15,1.1),(8.15,1.1)]):
        cypress(x,y,2.02,3.9+(i%3)*.30,i)

    def shrub(x,y,z,radius=.85,height=.60,seed=0,flowered=True):
        local=random.Random(800+seed)
        vs=[];fs=[];idx=[]
        for branch in range(45):
            a=branch*2.399
            r=radius*math.sqrt(local.random())
            tip=(x+r*math.cos(a),y+r*math.sin(a),z+height*local.uniform(.45,1.10))
            if branch%6==0:
                line('Shrub slender branching stem',[(x,y,z-.13),(x+r*.6*math.cos(a),y+r*.6*math.sin(a),z+height*.4),tip],.012,bark)
            for j in range(14):
                dx=local.uniform(-.17,.17)
                dy=local.uniform(-.17,.17)
                dz=local.uniform(-.13,.13)
                add_leaf(vs,fs,idx,(tip[0]+dx,tip[1]+dy,tip[2]+dz),
                         (local.uniform(-1,1),local.uniform(-1,1),local.uniform(.1,.8)),
                         local.uniform(.16,.26),local.uniform(.09,.15),local.randrange(5))
        foliage_mesh('Garden broadleaf shrub',vs,fs,idx)
        if flowered:
            for k in range(14):
                a=local.random()*math.tau
                rr=radius*local.random()*.80
                px=x+math.cos(a)*rr;py=y+math.sin(a)*rr;pz=z+height*local.uniform(.7,1.14)
                lathe('Five lobed violet trumpet',px,py,[(.015,pz-.10),(.035,pz-.02),(.065,pz+.015),(.105,pz+.055)],flowers[k%2],n=10)

    for i,(x,y) in enumerate([(-8,-7.9),(8,-7.9),(-7.3,-2.3),(7.3,-2.3),(-4.4,3),(4.4,3)]):
        shrub(x,y,2.01,.76,.62,i)
    # Gardens on the elevated walls add lived-in terraces without filling the combat route.
    for side in [-1,1]:
        for j,xx in enumerate([7.2,12.0,17.0,21.0]):
            x=side*xx
            box('Terrace garden stone trough',(x,15.7,15.1),(3.5,1.4,.57),pale,.09)
            box('Terrace garden soil',(x,15.7,15.41),(3.25,1.20,.045),soil,.03)
            for k in [-1,0,1]:shrub(x+k*.9,15.7,15.4,.65,.58,60+j+k,False)
            # Long climbing stems and leaf pairs fall naturally over the parapet.
            for strand in range(5):
                px=x+(strand-2)*.45
                length=1.4+.5*math.sin(strand*2+j)
                coords=[(px,15.0,15.6),(px+.12,14.92,15.0-length*.45),(px-.10,14.9,15.0-length)]
                line('Hanging terrace vine',coords,.018,bark)
                vs=[];fs=[];idx=[]
                for k in range(18):
                    zz=15.5-k*length/18
                    lr=(-1)**k
                    add_leaf(vs,fs,idx,(px+lr*.10,14.87,zz),(lr*.65,-.08,-.35),.26,.16,k%5)
                foliage_mesh('Trailing terrace leaves',vs,fs,idx)

    # Cloth banners high above the square are one deliberate accent, not prop clutter.
    for x in [-10.8,10.8]:
        vs=[];fs=[]
        for i in range(25):
            v=i/24
            for j in range(17):
                u=j/16
                vs.append((x+(u-.5)*1.75,15.38+.18*math.sin(u*math.tau*2+v*3)*v,
                           13.8-v*5.4-(1-abs(2*u-1))*.55*v**8))
        for i in range(24):
            for j in range(16):
                k=i*17+j
                fs.append((k,k+1,k+18,k+17))
        ob=mesh('Great gate wind shaped coral banner',vs,fs,cloth,True)
        sol=ob.modifiers.new('Cloth seam thickness','SOLIDIFY');sol.thickness=.018
        line('High banner bronze beam',[(x-1.1,15.35,13.87),(x+1.1,15.35,13.87)],.045,bronze)
        for sx in [-1,1]:
            line('Banner embroidered pale edge',[(x+sx*.77,15.27,13.65),(x+sx*.77,15.28,9.0),
                 (x,15.32,7.93)],.018,pale)

    # Physical daylight and a blue sky produce clear sunlight/shadow value groups.
    world=bpy.data.worlds.new('Ether V2 open blue sky')
    world.use_nodes=True
    bg=world.node_tree.nodes.get('Background')
    sky=world.node_tree.nodes.new('ShaderNodeTexSky')
    sky.sky_type='NISHITA'
    sky.sun_elevation=math.radians(34)
    sky.sun_rotation=math.radians(215)
    sky.sun_disc=False
    sky.altitude=.2
    sky.air_density=1.1
    sky.dust_density=1.8
    bg.inputs['Strength'].default_value=.085
    world.node_tree.links.new(sky.outputs['Color'],bg.inputs['Color'])
    bpy.context.scene.world=world
    for ob in bpy.context.scene.objects:
        if ob.type!='LIGHT':continue
        if ob.name=='Soft sun':
            ob.data.energy=4.0
            ob.data.angle=math.radians(3.8)
            ob.data.color=(1.0,.88,.72)
            ob.rotation_euler=(Vector((0,6,0))-Vector((-18,-7,25))).to_track_quat('-Z','Y').to_euler()
        elif ob.data.type=='AREA':
            ob.data.energy*=.22
        elif ob.name.startswith('Lantern'):
            ob.data.energy=7
    # Front bounce preserves the hero and foreground while recesses remain dark.
    ld=bpy.data.lights.new('V2 sky bounce','AREA')
    ld.energy=420
    ld.shape='DISK';ld.size=9
    ld.color=(.62,.79,1)
    lo=attach(bpy.data.objects.new('V2 sky bounce',ld));lo.location=(1,-12,12)
    lo.rotation_euler=(Vector((0,1,2))-lo.location).to_track_quat('-Z','Y').to_euler()
    camdata=bpy.data.cameras.new('Camera_Refined')
    camdata.lens=30
    camdata.clip_end=1000
    cam=attach(bpy.data.objects.new('Camera_Refined',camdata))
    cam.location=(4.2,-15,4.5)
    cam.rotation_euler=(Vector((0,5.0,5.3))-cam.location).to_track_quat('-Z','Y').to_euler()
    bpy.context.scene.view_settings.view_transform='AgX'
    bpy.context.scene.view_settings.look='AgX - Medium High Contrast'
    bpy.context.scene.view_settings.exposure=-.45
    return {'environment':'Cites d Ether v2','camera':'Camera_Refined',
            'added_objects':len(collection.objects),
            'notes':['Layered monumental city with actual vaulted gateway and elevated palace',
                     'Individual botanical leaf meshes and irregular branching cypresses',
                     'Stone, fired enamel, woven cloth and satin bronze microstructure',
                     'Continuous street geometry, carved moldings and directional daylight'],
            'character_edits':False}
