"""Editable, self-contained Blender study for Arkya: Cites d'Ether.

All visible content is modeled in this file. No downloaded assets or image plates.
The caller owns scene reset, render settings, file saving and image rendering.
"""

import bpy
import math
import random
from mathutils import Vector


def build_scene():
    random.seed(24)
    mats = {}

    def material(name, color, roughness=0.5, metallic=0.0, variation=0.0, emission=0.0):
        mat = bpy.data.materials.new(name)
        mat.diffuse_color = (*color, 1)
        mat.use_nodes = True
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links
        bs = nodes.get('Principled BSDF')
        bs.inputs['Base Color'].default_value = (*color, 1)
        bs.inputs['Roughness'].default_value = roughness
        bs.inputs['Metallic'].default_value = metallic
        if emission:
            bs.inputs['Emission Color'].default_value = (*color, 1)
            bs.inputs['Emission Strength'].default_value = emission
        if variation:
            noise = nodes.new('ShaderNodeTexNoise')
            noise.inputs['Scale'].default_value = 3.5
            noise.inputs['Detail'].default_value = 2
            ramp = nodes.new('ShaderNodeValToRGB')
            ramp.color_ramp.elements[0].position = 0.22
            ramp.color_ramp.elements[0].color = tuple(c * (1 - variation) for c in color) + (1,)
            ramp.color_ramp.elements[1].position = 0.80
            ramp.color_ramp.elements[1].color = tuple(min(1, c * (1 + variation)) for c in color) + (1,)
            links.new(noise.outputs['Fac'], ramp.inputs['Fac'])
            links.new(ramp.outputs['Color'], bs.inputs['Base Color'])
        mats[name] = mat
        return mat

    ivory = material('Warm carved limestone', (0.76, 0.69, 0.54), .67, variation=.10)
    white = material('Pale ivory enamel', (.92, .86, .70), .38, variation=.035)
    trim = material('Honey limestone edges', (.57, .46, .29), .60, variation=.08)
    bronze = material('Brushed old gold', (.52, .29, .075), .32, .68)
    darkgold = material('Recessed bronze', (.22, .105, .028), .48, .58)
    teal = material('Peacock enamel roofs', (.023, .24, .28), .30, .25, variation=.08)
    teal_dark = material('Petroleum blue recess', (.015, .055, .078), .57)
    glass = material('Quiet turquoise glass', (.035, .32, .39), .21, .40)
    coral = material('Coral woven cloth', (.75, .17, .105), .80, variation=.12)
    coral_light = material('Salmon cloth border', (.95, .43, .24), .75)
    crystal = material('Ether crystal cyan', (.025, .48, .61), .16, .25, emission=.13)
    crystal_light = material('Ether crystal light facet', (.24, .83, .86), .18, .15, emission=.13)
    crystal_deep = material('Ether crystal deep facet', (.015, .18, .30), .24, .35)
    leaf = material('Cypress deep olive', (.055, .17, .071), .83, variation=.30)
    leaf_light = material('Garden leaf light', (.18, .32, .075), .88, variation=.20)
    leaf_dark = material('Garden leaf shadow', (.025, .10, .052), .87, variation=.15)
    earth = material('Planter soil', (.07, .047, .025), .92)
    bark = material('Cypress trunks', (.19, .11, .052), .90)
    lilac = material('Hanging violet flowers', (.40, .16, .47), .73)
    amber = material('Lantern warm glass', (1.0, .58, .17), .30, emission=2.0)
    water = material('Still reflecting pool', (.015, .18, .20), .14, .50)
    base_mat = material('Terrace foundation blue stone', (.15, .22, .24), .80, variation=.13)
    backdrop = material('Blue grey studio ground', (.19, .28, .32), .90)
    tile_mats = [material('Plaza limestone %02d' % i,
                          (.65 + i * .028, .59 + i * .025, .46 + i * .022), .72, variation=.07)
                 for i in range(6)]

    def apply_mat(obj, mat):
        obj.data.materials.append(mat)
        return obj

    def smooth(obj):
        for poly in obj.data.polygons:
            poly.use_smooth = True
        return obj

    def bevel(obj, width=.07, segments=3):
        mod = obj.modifiers.new('Soft crafted edges', 'BEVEL')
        mod.width = width
        mod.segments = segments
        mod.affect = 'EDGES'
        mod.harden_normals = True
        normal = obj.modifiers.new('Weighted surface normals', 'WEIGHTED_NORMAL')
        normal.keep_sharp = True
        normal.weight = 50
        return obj

    def mesh(name, vertices, faces, mat, do_smooth=False, edge=0):
        data = bpy.data.meshes.new(name + ' geometry')
        data.from_pydata(vertices, [], faces)
        data.update()
        obj = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(obj)
        apply_mat(obj, mat)
        if do_smooth:
            smooth(obj)
        if edge:
            bevel(obj, edge)
        return obj

    def box(name, loc, scale, mat, edge=.06):
        x, y, z = [v * .5 for v in scale]
        obj = mesh(name, [(-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
                          (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z)],
                   [(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)], mat, edge=edge)
        obj.location = loc
        return obj

    def sphere(name, loc, scale, mat, segments=32):
        rings = 16
        verts = [(0,0,1),(0,0,-1)]
        for row in range(1,rings):
            t=math.pi*row/rings
            for j in range(segments):
                a=2*math.pi*j/segments
                verts.append((math.sin(t)*math.cos(a),math.sin(t)*math.sin(a),math.cos(t)))
        faces=[]
        for j in range(segments):
            n=(j+1)%segments
            faces.append((0,2+j,2+n))
            for row in range(rings-2):
                a=2+row*segments
                b=a+segments
                faces.append((a+j,b+j,b+n,a+n))
            last=2+(rings-2)*segments
            faces.append((1,last+n,last+j))
        obj = mesh(name,verts,faces,mat,True)
        obj.location = loc
        obj.scale = scale
        return obj

    def cylinder(name, loc, radius, depth, mat, vertices=64, radius_top=None, edge=.04):
        if radius_top is None:
            radius_top=radius
        verts=[]
        for z,r in [(-depth*.5,radius),(depth*.5,radius_top)]:
            for j in range(vertices):
                a=2*math.pi*j/vertices
                verts.append((r*math.cos(a),r*math.sin(a),z))
        faces=[tuple(reversed(range(vertices))),tuple(vertices+j for j in range(vertices))]
        for j in range(vertices):
            n=(j+1)%vertices
            faces.append((j,n,vertices+n,vertices+j))
        obj=mesh(name,verts,faces,mat,True)
        obj.data.polygons[0].use_smooth=False
        obj.data.polygons[1].use_smooth=False
        obj.location=loc
        if edge:
            bevel(obj, edge, 3)
        return obj

    def line(name, points, radius, mat, cyclic=False, resolution=12):
        data = bpy.data.curves.new(name + ' curve', 'CURVE')
        data.dimensions = '3D'
        data.resolution_u = resolution
        data.bevel_depth = radius
        data.bevel_resolution = 3
        spline = data.splines.new('BEZIER')
        spline.bezier_points.add(len(points)-1)
        for p, coord in zip(spline.bezier_points, points):
            p.co = coord
            p.handle_left_type = 'AUTO'
            p.handle_right_type = 'AUTO'
        spline.use_cyclic_u = cyclic
        obj = bpy.data.objects.new(name, data)
        bpy.context.collection.objects.link(obj)
        apply_mat(obj, mat)
        return obj

    def rod(name, a, b, radius, mat):
        direction = Vector(b) - Vector(a)
        obj = cylinder(name, (Vector(a)+Vector(b)) * .5, radius, direction.length, mat, 24, edge=.015)
        obj.rotation_euler = direction.to_track_quat('Z', 'Y').to_euler()
        return obj

    def arc(name, center, outer, width, depth, start, end, mat, edge=.035, horizontal=False):
        segments = max(16, int(abs(end-start) / 3))
        coords=[]
        for i in range(segments+1):
            angle = math.radians(start+(end-start)*i/segments)
            for r,d in [(outer,-depth/2),(outer-width,-depth/2),(outer-width,depth/2),(outer,depth/2)]:
                if horizontal:
                    coords.append((center[0]+r*math.cos(angle),center[1]+r*math.sin(angle),center[2]+d))
                else:
                    coords.append((center[0]+r*math.cos(angle),center[1]+d,center[2]+r*math.sin(angle)))
        faces=[(0,3,2,1)]
        for i in range(segments):
            k=4*i
            for j in range(4):
                n=(j+1)%4
                faces.append((k+j,k+n,k+4+n,k+4+j))
        k=4*segments
        faces.append((k,k+1,k+2,k+3))
        return mesh(name,coords,faces,mat,edge=edge)

    def lathe(name, x, y, profile, mat, segments=72):
        verts=[]
        for r,z in profile:
            for j in range(segments):
                a=2*math.pi*j/segments
                verts.append((x+r*math.cos(a), y+r*math.sin(a), z))
        faces=[]
        for row in range(len(profile)-1):
            for j in range(segments):
                n=(j+1)%segments
                a=row*segments+j
                b=row*segments+n
                faces.append((a,b,b+segments,a+segments))
        faces.append(tuple(reversed(range(segments))))
        faces.append(tuple((len(profile)-1)*segments+j for j in range(segments)))
        return mesh(name,verts,faces,mat,do_smooth=True)

    def arch_panel(name, x, y, bottom, width, spring, mat, edge=.015):
        r=width/2
        points=[(x-r,y,bottom),(x+r,y,bottom),(x+r,y,spring)]
        points += [(x+r*math.cos(math.pi*i/24),y,spring+r*math.sin(math.pi*i/24)) for i in range(1,25)]
        return mesh(name,points,[tuple(range(len(points)))],mat,edge=edge)

    def column(x,y,z,height, slender=.26):
        box('Square column foot',(x,y,z+.13),(.86,.86,.26),ivory,.055)
        cylinder('Molded column foot',(x,y,z+.32),.40,.20,white)
        cylinder('Column lower gold collar',(x,y,z+.46),.30,.10,bronze)
        cylinder('Tapered limestone shaft',(x,y,z+height*.49),slender,height-.91,white,
                 radius_top=slender*.82,edge=.03)
        cylinder('Column neck collar',(x,y,z+height-.39),.29,.13,bronze)
        box('Column capital',(x,y,z+height-.18),(.78,.78,.26),white,.06)

    # A complete raised urban slice, with a clear navigable central route.
    box('Diorama lower shadow plinth',(0,1.1,-.52),(23.7,24.1,.48),teal_dark,.46)
    box('Diorama carved stone foundation',(0,1.1,.12),(23.3,23.7,1.02),base_mat,.35)
    box('Foundation limestone cornice',(0,1.1,.76),(23.6,24,.32),ivory,.16)
    box('Bronze foundation ribbon',(0,1.1,.59),(23.65,24.05,.085),bronze,.05)
    box('Terrace paving underlay',(0,1.1,.99),(22.8,23.2,.26),trim,.14)

    for ix in range(17):
        for iy in range(17):
            x=-10.55+ix*1.32
            y=-9.52+iy*1.32
            if iy % 2:
                x += .20
            box('Individually cut plaza paver',(x,y,1.15),(1.27,1.27,.16),random.choice(tile_mats),.035)

    # Front stair apron and broad intentional borders.
    for i in range(6):
        box('Approach stair %02d'%i,(0,-11.9+i*.43,.12+i*.18),(7.8,.87,.22),white,.045)
        box('Bronze stair nosing %02d'%i,(0,-12.33+i*.43,.18+i*.18),(7.3,.035,.025),bronze,.008)
    for x in [-4.0,4.0]:
        for i in range(6):
            box('Stair side carved block',(x,-11.85+i*.45,.33+i*.18),(.42,.71,.60),ivory,.07)
        line('Stair rail carved cap',[(x,-12.1,.66),(x,-10.6,1.30),(x,-9.48,1.76)],.16,white)
    for x in [-3.4,3.4]:
        box('Long bronze walkway inlay',(x,-3.5,1.241),(.055,12.3,.012),bronze,.01)
        box('Dark enamel walkway inlay',(x+.12,-3.5,1.24),(.10,12.3,.012),teal,.01)
    for r,w in [(2.5,.045),(2.37,.10),(1.9,.035)]:
        arc('Floor compass concentric inlay',(0,.8,1.246),r,w,.015,0,360,bronze,.005,True)
    for a in range(0,360,45):
        theta=math.radians(a)
        points=[(r*math.cos(theta+d),.8+r*math.sin(theta+d),1.257)
                for r,d in [(2.15,0),(.76,.12),(1.02,0),(.76,-.12)]]
        mesh('Compass bronze ray',points,[(0,1,2,3)],bronze)

    # Back terrace sits one step above the square.
    box('Ceremonial upper terrace',(0,7.5,1.42),(18.4,7.8,.38),white,.14)
    box('Ceremonial terrace bronze course',(0,7.5,1.60),(18.5,7.9,.065),bronze,.035)
    for i in range(3):
        box('Sanctum entry step',(0,3.13+i*.38,1.26+i*.15),(7.1,.7,.20),ivory,.045)

    # Semi-circular reflecting basin below the suspended focus.
    cylinder('Focus octagonal foot',(0,6.6,1.83),2.50,.34,white,8,edge=.10)
    cylinder('Focus octagonal bronze course',(0,6.6,2.04),2.35,.10,bronze,8,edge=.04)
    cylinder('Reflecting basin dark recess',(0,6.6,2.13),2.19,.13,teal_dark,64)
    cylinder('Still water beneath crystal',(0,6.6,2.22),2.08,.025,water,80,edge=0)
    arc('Pool ivory outer rim',(0,6.6,2.22),2.36,.20,.20,0,360,white,.045,True)
    for x in [-2.08,2.08]:
        cylinder('Ring support bronze base',(x,6.65,2.30),.50,.28,bronze,8)
        cylinder('Ring support tapered pier',(x,6.65,2.92),.36,1.10,white,8,radius_top=.22)
        sphere('Support enamel jewel',(x,6.65,3.54),(.27,.27,.27),teal)

    center=(0,6.65,6.75)
    for start,end in [(-68,68),(112,248)]:
        arc('Monument outer bronze structural arc',center,3.45,.62,.70,start,end,bronze,.065)
        arc('Monument inset pale carved arc',(0,6.26,6.75),3.34,.42,.12,start+1,end-1,white,.04)
        arc('Monument turquoise inner channel',(0,6.18,6.75),2.94,.082,.07,start+2,end-2,teal,.018)
        arc('Monument outer gold fillet',(0,6.17,6.75),3.43,.055,.07,start+1,end-1,bronze,.015)
        for a in range(int(start)+12,int(end),24):
            t=math.radians(a)
            x=3.12*math.cos(t)
            z=6.75+3.12*math.sin(t)
            obj=box('Ring ornamental clasp',(x,6.105,z),(.15,.13,.47),bronze,.03)
            obj.rotation_euler[1]=-t
            sphere('Ring clasp turquoise pin',(x,6.01,z),(.066,.041,.066),glass,16)
    # Crystal deliberately uses large readable facets rather than noisy glow.
    verts=[(0,6.65,8.64)]
    for z,r,offset in [(7.18,.82,0),(6.63,.56,.08)]:
        for i in range(6):
            a=2*math.pi*i/6+offset
            verts.append((r*math.cos(a),6.65+r*math.sin(a),z))
    verts.append((0,6.65,4.91))
    faces=[]
    for i in range(6):
        j=(i+1)%6
        faces.extend([(0,i+1,j+1),(i+1,7+i,7+j,j+1),(13,7+j,7+i)])
    focus=mesh('Suspended six sided ether heart',verts,faces,crystal)
    focus.data.materials.append(crystal_light)
    focus.data.materials.append(crystal_deep)
    for i,p in enumerate(focus.data.polygons):
        p.material_index=[0,1,0,2,0,1][i%6]
    for z in [10.40,3.10]:
        bpy.ops.mesh.primitive_cone_add(vertices=4,radius1=.24,radius2=0,depth=.55,location=(0,6.65,z))
        apply_mat(bpy.context.object,bronze).name='Floating bronze finial'

    # Two complete garden pavilions, not facades.
    def pavilion(x,y):
        cylinder('Pavilion broad octagonal footing',(x,y,1.80),2.35,.35,ivory,8,edge=.09)
        cylinder('Pavilion stone round wall',(x,y,3.31),1.96,2.75,ivory,64,edge=.08)
        cylinder('Pavilion base molded cornice',(x,y,2.07),2.13,.27,white)
        cylinder('Pavilion upper frieze',(x,y,4.77),2.16,.30,white)
        cylinder('Pavilion roof bronze eave',(x,y,5.02),2.32,.17,bronze)
        cylinder('Pavilion roof dark shadow',(x,y,5.14),2.24,.12,teal_dark)
        profile=[(2.28,5.16)]
        profile += [(2.25*math.cos(t),5.18+1.86*math.sin(t)) for t in [i*math.pi/2/24 for i in range(25)]]
        lathe('Smooth turquoise pavilion dome',x,y,profile,teal)
        for k in range(12):
            a=k*2*math.pi/12
            pts=[]
            for i in range(15):
                t=i*math.pi/2/14
                r=2.28*math.cos(t)
                pts.append((x+r*math.cos(a),y+r*math.sin(a),5.20+1.86*math.sin(t)))
            line('Dome raised bronze rib',pts,.025,bronze)
        cylinder('Dome crown collar',(x,y,7.07),.27,.17,bronze)
        cylinder('Dome crown lantern',(x,y,7.34),.14,.42,ivory,8)
        cylinder('Dome crown spire',(x,y,7.72),.25,.45,bronze,8,radius_top=0,edge=.015)
        sphere('Crown jewel',(x,y,7.97),(.08,.08,.11),crystal,16)
        for k in range(8):
            a=k*2*math.pi/8
            # Local forward-facing arch window is rotated around the pavilion.
            group=[]
            win=arch_panel('Arched enamel pavilion window',0,-2.015,2.43,.68,3.61,glass)
            group.append(win)
            group.append(arc('Window raised bronze arch',(0,-2.05,3.61),.41,.07,.08,0,180,bronze,.015))
            for sx in [-.375,.375]:
                group.append(box('Window carved jamb',(sx,-2.04,3.01),(.075,.11,1.17),white,.015))
            group.append(box('Window deep sill',(0,-2.07,2.43),(.92,.26,.11),white,.025))
            group.append(box('Window gold central mullion',(0,-2.065,3.15),(.025,.04,1.41),bronze,.008))
            group.append(box('Window gold crossbar',(0,-2.07,3.37),(.68,.04,.028),bronze,.008))
            for obj in group:
                # Geometry is authored in world coordinates about the origin.
                obj.rotation_euler[2]=a
                old=Vector(obj.location)
                obj.location=(x+old.x*math.cos(a)-old.y*math.sin(a),
                              y+old.x*math.sin(a)+old.y*math.cos(a),old.z)
            pa=a+math.pi/8
            px=x+1.91*math.cos(pa)
            py=y+1.91*math.sin(pa)
            cylinder('Pavilion fluted edge pilaster',(px,py,3.38),.115,2.30,white,16)
            cylinder('Pilaster bronze capital',(px,py,4.53),.15,.14,bronze,16)

    pavilion(-7.0,6.65)
    pavilion(7.0,6.65)

    # Rear slender tower silhouettes frame the open ring.
    for x in [-4.1,4.1]:
        box('Rear sanctum pier base',(x,9.15,2.01),(1.45,1.55,.74),white,.12)
        box('Rear tapered tower core',(x,9.15,5.50),(.94,1.10,6.5),ivory,.14)
        box('Tower upper pale cap',(x,9.15,8.85),(1.2,1.33,.25),white,.065)
        for dx in [-.55,.55]:
            box('Tower flying edge fin',(x+dx,9.15,6.2),(.15,1.30,6.0),white,.05)
        box('Tower recessed enamel face',(x,8.58,6.15),(.41,.055,4.5),teal,.04)
        box('Tower slim golden vertical',(x,8.53,6.15),(.04,.04,4.3),bronze,.01)
        cylinder('Tower bronze spire',(x,9.15,9.28),.63,.78,bronze,4,radius_top=0,edge=.03)
        sphere('Tower pearl',(x,9.15,9.8),(.14,.14,.18),crystal,24)

    # Two side colonnades have walk-through arches and thin shadowed roofs.
    for side in [-1,1]:
        x=side*10.0
        for y in [-4.5,-1.5,1.5,4.5]:
            column(x,y,1.24,3.45,.25)
        for y in [-3,0,3]:
            obj=arc('Side gallery rounded arch',(0,0,3.80),1.49,.22,.55,0,180,ivory,.055)
            obj.rotation_euler[2]=math.pi/2
            obj.location=(x,y,0)
        box('Gallery long carved roof cornice',(x,0,5.35),(1.35,10.5,.30),white,.075)
        box('Gallery bronze roof line',(x,0,5.55),(1.52,10.72,.10),bronze,.04)
        box('Gallery enamel roof',(x,0,5.68),(1.40,10.6,.18),teal,.06)
        for y in [-4.6,-1.6,1.4,4.4]:
            box('Gallery top decorative dentil',(x-side*.64,y,5.17),(.25,.27,.22),trim,.03)

    # Coral textiles carry the identity color at a few selected locations.
    def banner(x,y,z,width=1.15,length=2.65):
        rows=18
        cols=8
        verts=[]
        for i in range(rows+1):
            v=i/rows
            for j in range(cols+1):
                u=j/cols
                drop=length*v + (1-abs(u*2-1))*.38*(v**8)
                verts.append((x+(u-.5)*width,
                              y+.10*math.sin(u*math.pi*4+v*2)*(.25+.75*v)+.12*math.sin(v*4),z-drop))
        faces=[]
        for i in range(rows):
            for j in range(cols):
                k=i*(cols+1)+j
                faces.append((k,k+1,k+cols+2,k+cols+1))
        obj=mesh('Sculpted hanging coral cloth',verts,faces,coral,True)
        solid=obj.modifiers.new('Woven cloth thickness','SOLIDIFY')
        solid.thickness=.018
        rod('Banner bronze crossbar',(x-width*.64,y,z+.07),(x+width*.64,y,z+.07),.045,bronze)
        for sx in [-1,1]:
            sphere('Banner pole terminal',(x+sx*width*.65,y,z+.07),(.09,.09,.09),bronze,16)
            line('Banner cream woven border',[(x+sx*width*.44,y+.02,z-.06),
                    (x+sx*width*.44,y-.01,z-length*.65),
                    (x+sx*width*.44,y+.02,z-length),
                    (x,y+.01,z-length-.33)],.018,coral_light)
        for s,e in [(25,155),(205,335)]:
            arc('Banner open ring embroidered emblem',(x,y-.14,z-length*.47),.265,.052,.018,s,e,white,.004)
        obj=box('Banner emblem diamond',(x,y-.16,z-length*.47),(.12,.025,.21),bronze,.01)
        obj.rotation_euler[1]=math.radians(45)

    banner(-4.10,8.46,7.85,.75,2.65)
    banner(4.10,8.46,7.85,.75,2.65)
    banner(-10.0,-4.72,4.79,.95,2.18)
    banner(10.0,-4.72,4.79,.95,2.18)

    # Planters: stone vessels with smooth botanical masses and small flower clusters.
    def planter(x,y,r=.85,cypress=False):
        lathe('Carved garden urn',x,y,[(r*.60,1.23),(r*.75,1.30),(r*.72,1.45),(r*.88,1.77),
                                     (r,1.87),(r,1.99),(r*.86,2.03)],white,48)
        cylinder('Garden urn bronze lip',(x,y,1.97),r*1.015,.055,bronze,48,edge=.01)
        cylinder('Garden urn dark soil',(x,y,2.02),r*.86,.04,earth,48,edge=0)
        if cypress:
            cylinder('Cypress narrow trunk',(x,y,2.85),.12,1.60,bark,20,edge=0)
            for i in range(6):
                z=2.62+i*.51
                rr=.64*(1-i*.115)
                sphere('Cypress sculpted foliage',(x+math.sin(i*2)*.07,y+math.cos(i*2)*.06,z),
                       (rr,rr*.83,.86-i*.065),leaf if i%2 else leaf_dark,32)
            sphere('Cypress tapered crown',(x,y,5.65),(.20,.20,.66),leaf,24)
        else:
            for k in range(10):
                a=k*2.4
                rr=r*random.uniform(.15,.78)
                px=x+math.cos(a)*rr
                py=y+math.sin(a)*rr
                sphere('Garden shaped foliage',(px,py,2.18+random.uniform(-.1,.15)),
                       (r*.45,r*.40,r*.33),leaf_light if k%3 else leaf,24)
                if k%2==0:
                    for q in range(3):
                        sphere('Violet garden blossom',(px+random.uniform(-.13,.13),py+random.uniform(-.13,.13),2.51),
                               (.075,.075,.09),lilac,12)

    for x,y in [(-8.0,-7.9),(8.0,-7.9),(-7.3,-2.3),(7.3,-2.3),(-4.4,3.0),(4.4,3.0)]:
        planter(x,y,.79,False)
    for x,y in [(-9.0,9.4),(9.0,9.4),(-4.6,6.0),(4.6,6.0),(-8.15,1.1),(8.15,1.1)]:
        planter(x,y,.68,True)

    def balustrade(x1,x2,y):
        for x in [x1,x2]:
            box('Balustrade square post',(x,y,1.95),(.58,.63,1.43),ivory,.06)
            box('Balustrade bronze capital',(x,y,2.65),(.72,.77,.12),bronze,.04)
            box('Balustrade pale cap',(x,y,2.76),(.77,.82,.12),white,.04)
        for i in range(1,7):
            x=x1+(x2-x1)*i/7
            lathe('Turned terrace baluster',x,y,[(.13,1.35),(.18,1.43),(.12,1.56),(.09,1.86),(.15,2.13),(.12,2.31)],white,24)
        box('Balustrade hand rail',((x1+x2)/2,y,2.39),(abs(x2-x1)+.20,.39,.20),white,.065)
        box('Balustrade foot rail',((x1+x2)/2,y,1.37),(abs(x2-x1)+.20,.38,.18),ivory,.045)
    balustrade(-10.9,-5.3,-9.45)
    balustrade(5.3,10.9,-9.45)

    # Lanterns are warm and restrained; the crystal remains the single cool focus.
    def lantern(x,y):
        cylinder('Lantern post octagonal base',(x,y,1.40),.30,.32,white,8)
        cylinder('Lantern post bronze foot',(x,y,1.65),.21,.22,bronze,24)
        cylinder('Lantern post',(x,y,2.66),.075,1.95,bronze,24,edge=.015)
        cylinder('Lantern lower cup',(x,y,3.66),.27,.16,bronze,8)
        cylinder('Lantern glowing glass',(x,y,4.00),.18,.54,amber,8,radius_top=.29,edge=.02)
        cylinder('Lantern gilded roof',(x,y,4.35),.37,.23,bronze,8,radius_top=.09,edge=.025)
        sphere('Lantern top finial',(x,y,4.53),(.075,.075,.12),bronze,16)
        for a in [0,math.pi/2,math.pi,3*math.pi/2]:
            rod('Lantern bronze frame',(x+.18*math.cos(a),y+.18*math.sin(a),3.73),
                (x+.28*math.cos(a),y+.28*math.sin(a),4.27),.025,bronze)
        data=bpy.data.lights.new('Lantern warm local light','POINT')
        data.energy=24
        data.color=(1.0,.60,.26)
        data.shadow_soft_size=.25
        obj=bpy.data.objects.new('Lantern warm local light',data)
        bpy.context.collection.objects.link(obj)
        obj.location=(x,y,4.01)
    for x,y in [(-4.6,-7.0),(4.6,-7.0),(-4.45,.4),(4.45,.4)]:
        lantern(x,y)

    # Ivy on two corners brings life without obscuring the architecture.
    for side in [-1,1]:
        for strand in range(3):
            x=side*(10.9-strand*.27)
            y=7.3+strand*.70
            pts=[(x,y,1.80),(x+side*.25,y+.10,1.25),(x+side*.36,y+.20,.60)]
            line('Trailing garden ivy stem',pts,.025,leaf_dark)
            for j in range(6):
                z=1.85-j*.23
                for lr in [-1,1]:
                    obj=sphere('Ivy hanging leaf',(x+lr*.13+side*j*.055,y+.03*j,z),(.13,.075,.20),leaf_light,16)
                    obj.rotation_euler[1]=lr*.6

    # A small cloaked traveler gives a real scale cue in the large square.
    def traveler(x,y):
        clothing=teal_dark
        skin=material('Traveler warm skin',(.43,.23,.13),.71)
        leather=material('Traveler dark leather',(.095,.048,.025),.74)
        z=1.25
        for dx in [-.14,.14]:
            sphere('Traveler leather boot',(x+dx,y+.02,z+.11),(.12,.22,.14),leather,24)
            rod('Traveler trouser leg',(x+dx,y,z+.18),(x+dx,y,z+.70),.105,clothing)
        sphere('Traveler tunic',(x,y,z+.94),(.31,.19,.39),clothing,32)
        cylinder('Traveler neck',(x,y,z+1.31),.085,.15,skin,24)
        sphere('Traveler head',(x,y,z+1.49),(.16,.15,.20),skin,32)
        sphere('Traveler dark hair',(x,y-.015,z+1.59),(.17,.16,.13),leather,24)
        for dx in [-.35,.35]:
            sphere('Traveler ivory shoulder',(x+dx*.83,y,z+1.15),(.17,.19,.14),white,24)
            rod('Traveler sleeve',(x+dx,y,z+1.13),(x+dx*1.07,y+.02,z+.80),.09,clothing)
            sphere('Traveler gloved hand',(x+dx*1.07,y+.02,z+.71),(.073,.08,.10),leather,24)
        cape_verts=[]
        for i in range(12):
            t=i/11
            for j in range(9):
                u=j/8
                cape_verts.append((x+(u-.5)*(.48+t*.30),y-.17-.12*math.sin(t*2)+.045*math.cos(u*math.pi*5),
                                   z+1.26-t*.97+.04*math.cos(u*math.pi*2)))
        cape_faces=[]
        for i in range(11):
            for j in range(8):
                k=i*9+j
                cape_faces.append((k,k+1,k+10,k+9))
        cape=mesh('Traveler flowing coral cloak',cape_verts,cape_faces,coral,True)
        mod=cape.modifiers.new('Cape fabric thickness','SOLIDIFY')
        mod.thickness=.025
        rod('Traveler bronze walking staff',(x+.48,y+.18,z+.02),(x+.48,y+.18,z+1.83),.027,bronze)
        sphere('Traveler staff focus',(x+.48,y+.18,z+1.88),(.065,.065,.11),crystal,24)
    traveler(-.70,-3.8)

    # Soft, broad studio daylight makes the sculpted forms and satin materials legible.
    box('Infinite presentation ground',(0,0,-1.01),(2000,2000,.20),backdrop,.05)
    world=bpy.data.worlds.new('Ether blue daylight world')
    world.use_nodes=True
    world.node_tree.nodes['Background'].inputs['Color'].default_value=(.48,.66,.78,1)
    world.node_tree.nodes['Background'].inputs['Strength'].default_value=.42
    bpy.context.scene.world=world

    def light(name,kind,loc,energy,color,size,target):
        data=bpy.data.lights.new(name,kind)
        data.energy=energy
        data.color=color
        if kind=='AREA':
            data.shape='DISK'
            data.size=size
        obj=bpy.data.objects.new(name,data)
        bpy.context.collection.objects.link(obj)
        obj.location=loc
        obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
        return obj

    light('Large warm afternoon key','AREA',(-10,-7,20),3000,(1.0,.84,.63),10,(0,3,2))
    light('Sky blue courtyard fill','AREA',(12,-4,13),1700,(.66,.85,1.0),12,(0,4,3))
    light('Pale upper rim','AREA',(0,15,17),2700,(.75,.91,1.0),9,(0,5,5))
    sun=light('Soft sun','SUN',(-8,-9,18),2.3,(1.0,.87,.71),0,(0,0,0))
    sun.data.angle=math.radians(11)

    def camera(name,location,target,lens):
        data=bpy.data.cameras.new(name)
        data.lens=lens
        data.clip_end=500
        obj=bpy.data.objects.new(name,data)
        bpy.context.collection.objects.link(obj)
        obj.location=location
        obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
        return obj

    main=camera('Camera_Main',(22,-31,21),(0,1.4,2.0),40)
    camera('Camera_Overview',(24,-28,30),(0,1.1,1.0),42)
    camera('Camera_Path',(0,-10.2,2.65),(0,6.65,5.5),28)
    bpy.context.scene.camera=main
    bpy.context.scene.render.resolution_x=1600
    bpy.context.scene.render.resolution_y=1000
    bpy.context.scene.render.resolution_percentage=100
    bpy.context.scene.view_settings.view_transform='AgX'
    bpy.context.scene.view_settings.look='AgX - Medium High Contrast'
    return {
        'title': "Arkya / 02 - Cites d'Ether",
        'notes': 'Cour monumentale: anneau ouvert, cristal suspendu, domes nervures, galeries, jardins et voyageur a echelle humaine.',
        'assets': 'Entirely procedural original mesh and curve geometry; no external textures.',
        'palette': 'Warm ivory, petroleum blue, turquoise enamel, coral textiles, satin bronze.',
        'objects': len(bpy.context.scene.objects),
    }
