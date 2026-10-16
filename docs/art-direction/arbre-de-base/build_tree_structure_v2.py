"""Sculpted, continuous hero-tree wood and deterministic foliage attachment points.

Import inside Blender and call build_structure(). No scene reset, save, or render.
"""
import math
import bpy
import bmesh
from mathutils import Vector


def _spline(points, radii, steps=8):
    points = [Vector(p) for p in points]
    samples = []
    for i in range(len(points)-1):
        a, b = points[max(0,i-1)], points[i]
        c, d = points[i+1], points[min(len(points)-1,i+2)]
        for j in range(steps):
            t = j/steps
            pos = .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
            radius = radii[i]*(1-t)+radii[i+1]*t
            samples.append((pos, radius))
    samples.append((points[-1],radii[-1]))
    return samples


def _tube(vertices, faces, points, radii, sides=18, steps=7, flute=.065,
          seed=0, root=False):
    samples = _spline(points,radii,steps)
    offset=len(vertices)
    normal=Vector((1,0,0))
    phase=(seed*.618)%math.tau
    for i,(center,radius) in enumerate(samples):
        tangent=(samples[min(len(samples)-1,i+1)][0]-samples[max(0,i-1)][0]).normalized()
        normal=normal-tangent*normal.dot(tangent)
        if normal.length<.01: normal=tangent.cross(Vector((0,1,0)))
        normal.normalize()
        binormal=tangent.cross(normal).normalized()
        progress=i/(len(samples)-1)
        for j in range(sides):
            angle=math.tau*j/sides
            # Broad flutes are part of the silhouette, not surface squiggles.
            grain=1+flute*math.cos(5*angle+phase+.65*progress)
            grain+=flute*.32*math.cos(9*angle-phase-.3*progress)
            radial=(normal*math.cos(angle)+binormal*math.sin(angle))*radius*grain
            if root:
                radial.z*=.64
                radial.x*=1.12
                radial.y*=1.12
            p=center+radial
            if root and p.z<.02:p.z=.02
            vertices.append(tuple(p))
    for i in range(len(samples)-1):
        for j in range(sides):
            a=offset+i*sides+j
            b=offset+i*sides+(j+1)%sides
            faces.append((a,b,b+sides,a+sides))
    faces.append(tuple(offset+j for j in reversed(range(sides))))
    end=offset+(len(samples)-1)*sides
    faces.append(tuple(end+j for j in range(sides)))
    return samples


def _at(samples,t):
    f=t*(len(samples)-1)
    i=min(int(f),len(samples)-2)
    fraction=f-i
    return samples[i][0].lerp(samples[i+1][0],fraction)


def build_structure():
    vertices,faces,anchors=[],[],[]
    collection=bpy.data.collections.new('Hero Tree | continuous sculpted wood')
    bpy.context.scene.collection.children.link(collection)

    trunk_points=[(0,0,.10),(.05,-.035,.6),(.23,-.06,1.35),
                  (.31,.04,2.2),(.12,.20,3.05),(-.19,.26,3.95),
                  (-.22,.24,4.75),(.03,.29,5.5),(.48,.34,6.22),
                  (.74,.40,7.1),(.61,.38,7.86),(.82,.28,8.43)]
    trunk_radii=[1.06,.85,.64,.60,.56,.48,.38,.285,.205,.14,.075,.027]
    trunk=_tube(vertices,faces,trunk_points,trunk_radii,sides=36,steps=9,flute=.14,seed=3)

    def trunk_at_height(height):
        for i in range(len(trunk)-1):
            a,b=trunk[i][0],trunk[i+1][0]
            if a.z<=height<=b.z:return a.lerp(b,(height-a.z)/(b.z-a.z))
        return trunk[-1][0].copy()

    # Buttresses begin inside the trunk and sweep down as broad load-bearing fins.
    for i in range(8):
        angle=math.tau*i/8+.14*math.sin(i*2.1)
        radial=Vector((math.cos(angle),math.sin(angle),0))
        side=Vector((-radial.y,radial.x,0))
        length=(2.45,2.85,2.3,2.8,2.5,3.05,2.25,2.65)[i]
        turn=(.24 if i%2 else -.22)
        start_height=(1.88,2.31,2.05,2.43,1.94,2.23,2.38,2.12)[i]
        p0=trunk_at_height(start_height)+radial*.14
        p1=trunk_at_height(start_height*.69)+radial*.28
        p2=radial*.75+Vector((.07,0,.64+(i%3)*.075))
        p3=radial*1.25+side*turn+Vector((0,0,.36))
        p4=radial*(length*.75)+side*(turn*1.8)+Vector((0,0,.15))
        p5=radial*length+side*(turn*2.8)+Vector((0,0,.055))
        _tube(vertices,faces,[p0,p1,p2,p3,p4,p5],[.13,.285,.44,.30,.125,.023],
              sides=22,steps=8,flute=.065,seed=i*2,root=True)

    def anchor(point,direction,scale,seed):
        anchors.append({'center':list(point),'direction':list(direction.normalized()),
                        'scale':scale,'seed':seed})

    specs=[(-168,2.85,3.75,5.72,.38),(-53,3.45,3.85,6.16,.365),
           (93,3.12,3.65,6.35,.37),(19,4.18,3.48,7.12,.32),
           (155,4.35,3.07,7.62,.31),(-106,4.91,2.72,8.12,.27),
           (61,5.35,2.36,8.49,.235)]
    for index,(degrees,z,reach,height,radius) in enumerate(specs):
        angle=math.radians(degrees)
        out=Vector((math.cos(angle),math.sin(angle),0))
        side=Vector((-out.y,out.x,0))
        start=trunk_at_height(z)
        sweep=(-.22 if index%2 else .24)
        end=start+out*reach+side*sweep
        end.z=height
        points=[start-out*.17,
                start+out*.52+side*.08+Vector((0,0,.16)),
                start+out*(reach*.46)+side*(sweep*.3)+Vector((0,0,.58)),
                start+out*(reach*.78)+side*(sweep*.7)+Vector((0,0,(height-z)*.63)),
                end-out*.23+Vector((0,0,-.45)),end]
        main=_tube(vertices,faces,points,[radius,radius*.94,radius*.74,radius*.43,radius*.19,.026],
                   sides=24,steps=8,flute=.07,seed=11+index)
        anchor(end,end-points[-2],.84+(index%3)*.05,600+index*20)

        # Three forked secondary limbs make every foliage mass a real branch terminal.
        for branch,u in enumerate((.43,.60,.76)):
            origin=_at(main,u)
            sign=1 if (branch+index)%2 else -1
            side_reach=(1.12,.98,.81)[branch]
            direction=(out*.48+side*sign*.88).normalized()
            rise=(.72,.80,.75)[branch]+(.10 if index>3 else 0)
            tip=origin+direction*side_reach+out*.18+Vector((0,0,rise))
            mid=origin.lerp(tip,.56)+Vector((0,0,-.13))
            secondary=[origin-out*.10,origin+direction*.27+Vector((0,0,.12)),mid,tip]
            rad=radius*(.49-.075*branch)
            shoot=_tube(vertices,faces,secondary,[rad,rad*.88,rad*.52,.026],
                        sides=18,steps=6,flute=.045,seed=30+index*3+branch)
            anchor(tip,tip-mid,.76+(branch%2)*.07,601+index*20+branch*3)
            fork_origin=_at(shoot,.56)
            fork_direction=(out*.80-side*sign*.56).normalized()
            fork_tip=fork_origin+fork_direction*(.58+.12*(2-branch))+Vector((0,0,.58))
            fork_mid=fork_origin.lerp(fork_tip,.52)+Vector((0,0,-.09))
            _tube(vertices,faces,[fork_origin, fork_mid,fork_tip],
                  [rad*.58,rad*.34,.025],sides=16,steps=7,flute=.035,seed=51+branch)
            anchor(fork_tip,fork_tip-fork_mid,.72+.08*(branch==0),602+index*20+branch*3)

    anchor(Vector(trunk_points[-1]),Vector(trunk_points[-1])-Vector(trunk_points[-2]),.98,900)
    for i,(u,side) in enumerate(((.84,-1),(.91,1))):
        origin=_at(trunk,u)
        end=origin+Vector((side*.93,.46,1.12))
        points=[origin,origin+Vector((side*.48,.18,.38)),end]
        _tube(vertices,faces,points,[.10,.075,.025],sides=18,steps=9,flute=.045,seed=110+i)
        anchor(end,end-points[-2],.92,901+i)

    # Interior boughs support the central canopy instead of leaving a hollow crown.
    for layer in range(2):
        for i in range(5):
            angle=math.tau*i/5+layer*.57+.17
            out=Vector((math.cos(angle),math.sin(angle),0))
            side=Vector((-out.y,out.x,0))
            start_height=(4.94 if layer==0 else 6.13)+i*.13
            origin=trunk_at_height(start_height)
            reach=(1.34 if layer==0 else 1.09)+.16*math.sin(i*1.9)
            end=Vector((.16,.20,0))+out*reach+side*.12
            end.z=(6.72 if layer==0 else 8.06)+.21*math.sin(i*1.4+.5)
            mid=origin.lerp(end,.56)+Vector((0,0,-.18))
            collar=origin+out*.25+Vector((0,0,.17))
            points=[origin-out*.10,collar,mid,end-out*.09-Vector((0,0,.21)),end]
            radius=.155 if layer==0 else .105
            _tube(vertices,faces,points,[radius,radius*.86,radius*.56,.052,.027],
                  sides=18,steps=7,flute=.045,seed=140+layer*5+i)
            anchor(end,end-points[-2],.93 if layer==0 else 1.00,950+layer*5+i)

    mesh=bpy.data.meshes.new('Hero tree sculpt source')
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    wood=bpy.data.objects.new('Hero Tree | continuous trunk, roots and limbs',mesh)
    collection.objects.link(wood)
    for obj in bpy.context.selected_objects:obj.select_set(False)
    wood.select_set(True)
    bpy.context.view_layer.objects.active=wood
    # Voxel union removes all tube intersections and preserves a single watertight surface.
    remesh=wood.modifiers.new('Fuse wood into one grown surface','REMESH')
    remesh.mode='VOXEL';remesh.voxel_size=.031
    remesh.use_smooth_shade=True
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    smooth=wood.modifiers.new('Soften grown branch collars','SMOOTH')
    smooth.factor=.62;smooth.iterations=3
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    triangles=sum(len(p.vertices)-2 for p in wood.data.polygons)
    if triangles>115000:
        decimate=wood.modifiers.new('Retain silhouette within hero prop budget','DECIMATE')
        decimate.ratio=115000/triangles
        bpy.ops.object.modifier_apply(modifier=decimate.name)
    # Remove only microscopic voxel fragments from the finest twig tips.
    bm=bmesh.new();bm.from_mesh(wood.data)
    visited=set();components=[]
    for vertex in bm.verts:
        if vertex in visited:continue
        pending=[vertex];visited.add(vertex);component=[]
        while pending:
            current=pending.pop();component.append(current)
            for edge in current.link_edges:
                other=edge.other_vert(current)
                if other not in visited:visited.add(other);pending.append(other)
        components.append(component)
    if len(components)>1:
        largest=max(components,key=len)
        fragments=[v for component in components if component is not largest for v in component]
        bmesh.ops.delete(bm,geom=fragments,context='VERTS')
        bm.to_mesh(wood.data)
    bm.free()
    for polygon in wood.data.polygons:polygon.use_smooth=True
    material=bpy.data.materials.new('Hero Tree | bark placeholder')
    material.diffuse_color=(.28,.12,.075,1)
    material.use_nodes=True
    shader=material.node_tree.nodes.get('Principled BSDF')
    shader.inputs['Base Color'].default_value=material.diffuse_color
    shader.inputs['Roughness'].default_value=.82
    wood.data.materials.append(material)
    wood['design']='Seven scaffold limbs and ten inner boughs, 62 real terminals, eight grown buttresses'
    wood['foliage_anchor_count']=len(anchors)
    lower=min(v.co.z for v in wood.data.vertices)
    for vertex in wood.data.vertices:vertex.co.z-=lower
    for item in anchors:item['center'][2]-=lower
    points=[v.co for v in wood.data.vertices]
    bounds={'min':[min(p[i] for p in points) for i in range(3)],
            'max':[max(p[i] for p in points) for i in range(3)]}
    return {'wood_objects':[wood],'foliage_anchors':anchors,'bounds':bounds,
            'triangles':sum(len(p.vertices)-2 for p in wood.data.polygons)}
