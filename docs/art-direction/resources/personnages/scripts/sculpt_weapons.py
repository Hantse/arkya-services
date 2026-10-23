"""Reference crescent blades, rigidly carried by the existing hand bones.

Call ``build(api, silhouette, joints)`` from reforge_characters before binding.
All coordinates passed to the host API are in the human reference scale.  The
two weapons remain presentation geometry inside CharacterMesh; no equipment,
combat, extra bones or network state is introduced.
"""
import math
import re

import bpy
import bmesh
from mathutils import Vector, Quaternion, Matrix


BLADE_COLOR = (.045, .75, .62)
BLADE_ROUGHNESS = .20
BLADE_METALLIC = .25
BLADE_EMISSION = (.0042, .070, .0504)
GRIP_HALF_LENGTH = .054
GRIP_RADIUS = .0105
GRIP_SWEEP_RADIANS = .94


def _source_hand_masks(obj):
    """Select anatomical ownership from deform groups, never from proximity."""
    pattern=re.compile(r"DEF-(?:(?:Finger_)?(?:Index|Middle|Ring|Pinky|Thumb)(?:[1-4]|_Carpal)|"
                       r"Hand|Wrist|Forearm_?[12]|COR-(?:Thumb|Forearm)[\w_]*)(?:\.)([LR])$")
    ownership={}
    for group in obj.vertex_groups:
        match=pattern.fullmatch(group.name)
        if match:ownership[group.index]=match[1]
    masks={"L":set(),"R":set()}
    for vertex in obj.data.vertices:
        for group in vertex.groups:
            side=ownership.get(group.group)
            if side and group.weight>1e-5:masks[side].add(vertex.index)
    assert masks["L"] and masks["R"], "Source hand/forearm masks must be present"
    return masks


def grip_frame(api, side, joints):
    """Return the reference-space palm center and oriented weapon frame.

    The across axis points outwards, the down axis follows the open palm, and
    the normal faces the character's front.  Grip values are shared with the
    local finger-closing pass so the handle cannot drift away from the hand.
    """
    sign = 1 if side == "Left" else -1
    wrist, tip = [Vector(tuple(p[k] / api.SCALE[k] for k in range(3)))
                  for p in joints[side + "Hand"][:2]]
    down = (tip - wrist).normalized()
    across = Vector((sign, 0, 0))
    across = (across - down * across.dot(down)).normalized()
    normal = across.cross(down).normalized()
    if normal.y > 0:
        normal = -normal
    # The reference holds each crescent diagonally around the leg. Turning the
    # plane towards the palm keeps the short inner point in front of the skirt.
    across = (across*math.cos(GRIP_SWEEP_RADIANS)-normal*math.sin(GRIP_SWEEP_RADIANS)).normalized()
    normal = across.cross(down).normalized()
    if normal.y > 0:
        normal = -normal
    # Curling the distal fingers encloses this cylinder against the palm.
    center = wrist + down * ((tip-wrist).length*1.45) - normal * .014
    return {"center": center, "across": across, "down": down,
            "normal": normal, "wrist": wrist, "palmTip": tip,
            "halfLength": GRIP_HALF_LENGTH, "radius": GRIP_RADIUS,
            "bone": side + "Hand"}


def close_hands(api, objects, joints):
    """Sculpt a static grip using original finger masks, before game binding.

    Rain and Snow retain detailed source finger weights, although the game rig
    intentionally has no finger bones. Bending their existing geometry once
    keeps the silhouette and skin continuous, without adding runtime joints.
    """
    bodies=[obj for obj in objects if obj.name=="Body"]
    result=[]
    for obj in bodies:
        assert not obj.get("referenceGripClosed"), "Grip sculpture must run once"
        original_coordinates=[v.co.copy() for v in obj.data.vertices]
        source=[Vector(tuple(v.co[k]/api.SCALE[k] for k in range(3))) for v in obj.data.vertices]
        source_masks=_source_hand_masks(obj)
        eligible=source_masks["L"]|source_masks["R"]
        groups={}
        for group in obj.vertex_groups:
            match=re.fullmatch(r"DEF-(?:Finger_)?(Index|Middle|Ring|Pinky|Thumb)([1-4])\.([LR])",group.name)
            if match:groups[group.index]=(match[3],match[1],int(match[2]))
        assert groups, "Original finger masks are required before hand binding"
        weights=[{groups[g.group]:g.weight for g in v.groups if g.group in groups and g.weight>1e-5}
                 for v in obj.data.vertices]
        final=[p.copy() for p in source]
        for side,suffix in (("Left","L"),("Right","R")):
            frame=grip_frame(api,side,joints)
            wrist,center,U,V,N=(frame[k] for k in ("wrist","center","across","down","normal"))
            def centroid(finger,number):
                key=(suffix,finger,number)
                total=sum(row.get(key,0) for row in weights)
                assert total>0,(side,finger,number)
                return sum((p*row.get(key,0) for p,row in zip(source,weights)),Vector())/total
            source_width=(centroid("Pinky",1)-centroid("Index",1))
            source_width=(source_width-V*source_width.dot(V)).normalized()
            angle=math.atan2(source_width.cross(U).dot(V),source_width.dot(U))
            twisted=[]
            for i,p in enumerate(source):
                delta=p-wrist
                t=max(0,min(1,delta.dot(V)/.055))
                local=i in source_masks[suffix] and abs(delta.dot(source_width))<.12 and -.01<delta.dot(V)<.24 and delta.length<.26
                twisted.append(wrist+Quaternion(V,angle*t*t*(3-2*t))@delta if local else p.copy())
            affected=set()
            accumulated=[Vector() for _ in source]
            sums=[0.0 for _ in source]
            for finger in ("Index","Middle","Ring","Pinky","Thumb"):
                numbers=sorted({number for tag,name,number in groups.values() if tag==suffix and name==finger})
                first=2 if numbers[-1]==4 else 1
                # Compute each rest centerline after the same palm rotation.
                def transformed_centroid(number):
                    key=(suffix,finger,number)
                    total=sum(row.get(key,0) for row in weights)
                    return sum((p*row.get(key,0) for p,row in zip(twisted,weights)),Vector())/total
                first_c=transformed_centroid(first)
                last_c=transformed_centroid(numbers[-1])
                direction=(last_c-first_c).normalized()
                base=first_c-direction*.016
                centers=[transformed_centroid(n) for n in numbers if n>=first]
                pivots=[base]+[(a+b)*.5 for a,b in zip(centers,centers[1:])]
                bend_sign=-1 if U.cross(direction).dot(N)>0 else 1
                angles=(.84,1.25,.95) if finger!="Thumb" else (.15,.60,.68)
                transforms=[]
                matrix=Matrix.Identity(4)
                for k,pivot in enumerate(pivots):
                    q=Quaternion(U,angles[min(k,2)]*bend_sign).to_matrix().to_4x4()
                    local=Matrix.Translation(pivot)@q@Matrix.Translation(-pivot)
                    matrix=matrix@local
                    transforms.append(matrix.copy())
                if finger=="Thumb":
                    # Oppose the thumb across the closed index finger. The base
                    # remains at the palm; the last two phalanges reach the grip.
                    goal=center-U*.005+N*.040+V*.004
                    q=(last_c-base).rotation_difference(goal-base).to_matrix().to_4x4()
                    opposition=Matrix.Translation(base)@q@Matrix.Translation(-base)
                    transforms=[opposition.copy() for _ in transforms]
                for i,(p,row) in enumerate(zip(twisted,weights)):
                    if i not in source_masks[suffix]:continue
                    for k,n in enumerate(number for number in numbers if number>=first):
                        influence=row.get((suffix,finger,n),0)
                        if influence<1e-5:continue
                        accumulated[i]+=(transforms[k]@p)*influence
                        sums[i]+=influence
                        affected.add(i)
            for i,total in enumerate(sums):
                if total>0:final[i]=twisted[i]*(1-min(1,total))+accumulated[i]/max(1,total)
            # The palm rotation blends through the wrist, with no hard seam.
            for i,(old,new) in enumerate(zip(source,twisted)):
                if (old-new).length>1e-8 and final[i]==source[i]:final[i]=new
            result.append({"side":side,"sculptedFingerVertices":len(affected),"palmTwistRadians":angle})
        # Relax only the wrist/palm transition; the source thumb mask includes
        # part of the palm and can otherwise leave a sharp seam after opposition.
        neighbors=[[] for _ in source]
        for edge in obj.data.edges:
            a,b=edge.vertices;neighbors[a].append(b);neighbors[b].append(a)
        relax={}
        for side,suffix in (("Left","L"),("Right","R")):
            frame=grip_frame(api,side,joints)
            for i,p in enumerate(source):
                delta=p-frame["wrist"]
                d=delta.dot(frame["down"])
                if i in source_masks[suffix] and .01<d<.105 and delta.length<.115:
                    relax[i]=min(1,(d-.01)/.020,(.105-d)/.025)
        for _ in range(4):
            for factor in (.45,-.25):
                changes={i:(sum((final[j] for j in neighbors[i]),Vector())/len(neighbors[i])-final[i])*factor*w
                         for i,w in relax.items() if neighbors[i]}
                for i,change in changes.items():final[i]+=change
        assert all(final[i]==source[i] for i in range(len(source)) if i not in eligible), "Grip moved a vertex outside the source hands"
        for vertex,p in zip(obj.data.vertices,final):
            if vertex.index in eligible:vertex.co=p*api.SCALE
        assert all(vertex.co==original_coordinates[vertex.index] for vertex in obj.data.vertices if vertex.index not in eligible), "Body outside the source hand masks must remain bit-identical"
        for entry in result[-2:]:
            entry["nonHandVerticesUnchanged"]=True
            entry["protectedNonHandVertices"]=len(source)-len(eligible)
            entry["sourceMaskVertexCount"]=len(source_masks["L" if entry["side"]=="Left" else "R"])
        obj.data.update()
        if obj.data.has_custom_normals:
            api.select(obj)
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
        obj["referenceGripClosed"]=True
    print("REFERENCE_HANDS_CLOSED",result,flush=True)
    return result


def _catmull(points, steps=8):
    result = []
    points = [Vector(p) for p in points]
    for i in range(len(points) - 1):
        a, b, c, d = (points[max(0, i - 1)], points[i], points[i + 1],
                      points[min(len(points) - 1, i + 2)])
        for j in range(steps):
            t = j / steps
            result.append(.5 * ((2*b) + (-a+c)*t + (2*a-5*b+4*c-d)*t*t
                                + (-a+3*b-3*c+d)*t*t*t))
    result.append(points[-1])
    return result


def _mesh(api, frame, name, vertices, faces, material, smooth=True):
    origin, across, down, normal = (frame[k] for k in ("center", "across", "down", "normal"))
    coords = [origin + across*p[0] + down*p[1] + normal*p[2] for p in vertices]
    obj = api.mesh(name, coords, faces, material, frame["bone"], smooth)
    # Every disconnected piece is a closed solid. Recalculate before joining
    # mirrored hands, because Cycles alone does not reveal backface winding.
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    assert all(edge.is_manifold for edge in bm.edges), name
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    paint = obj.data.color_attributes.new(name="Painted", type="FLOAT_COLOR", domain="POINT")
    for vertex, color in zip(obj.data.vertices, paint.data):
        reference = Vector(tuple(vertex.co[k] / api.SCALE[k] for k in range(3))) - origin
        u, v, w = reference.dot(across), reference.dot(down), reference.dot(normal)
        broad = math.sin(u*28 + v*19) * math.sin(v*31 - u*9)
        edge = min(1, abs(w) / .014)
        if material == "Blade":
            # Broad pools of jade pigment and pale bevels, visible at game scale.
            value = .73 + .14*broad + .13*(1-edge)
            color.color = (value*.76, value, min(1, value*1.045), 1)
        elif material == "Metal":
            value = .78 + .09*broad + .12*edge
            color.color = (min(1, value*1.05), value, value*.84, 1)
        else:
            value = .75 + .12*broad
            color.color = (value*1.05, value, value*.95, 1)
    obj.data.color_attributes.active_color = paint
    obj.data.color_attributes.render_color_index = 0
    # The host's paint() preserves existing source paint on non-generic parts.
    obj["reforgePart"] = False
    obj["referenceWeapon"] = True
    return obj


def _sweep(api, frame, name, path, width, depth, material, sides=8):
    """Closed sculpted metal ribbon or leather winding with a bevel profile."""
    points = [Vector(p) for p in path]
    vertices, faces = [], []
    for i, point in enumerate(points):
        tangent = (points[min(i+1, len(points)-1)] - points[max(0, i-1)]).normalized()
        direction = Vector((-tangent.y, tangent.x, 0)).normalized()
        t = i / (len(points)-1)
        w = width(t) if callable(width) else width
        d = depth(t) if callable(depth) else depth
        for j in range(sides):
            a = math.tau*j/sides
            vertices.append(point + direction*(w*math.cos(a)) + Vector((0, 0, d*math.sin(a))))
    for i in range(len(points)-1):
        for j in range(sides):
            a, b = i*sides+j, i*sides+(j+1)%sides
            faces.append((a, b, b+sides, a+sides))
    faces += [tuple(range(sides-1, -1, -1)),
              tuple((len(points)-1)*sides+j for j in range(sides))]
    return _mesh(api, frame, name, vertices, faces, material)


def _perforated_frame(api, frame, name, outline, aperture):
    """A single forged gold plate with a genuine opening and planar bevels."""
    vertices, faces = [], []
    count = len(outline)
    for z in (-.006, .006):
        vertices.extend((u, v, z) for u, v in outline)
    faces += [tuple(range(count-1, -1, -1)), tuple(count+i for i in range(count))]
    for i in range(count):
        j = (i+1)%count
        faces.append((i, j, j+count, i+count))
    obj = _mesh(api, frame, name, vertices, faces, "Metal", False)
    # Subtract a closed cutter before beveling; the opening is part of the
    # continuous structural plate, rather than a ring attached by wire struts.
    points = []
    hole_count = 36
    cx, cy, rx, ry, organic = aperture
    for i in range(hole_count):
        a = math.tau*i/hole_count
        r = 1 + organic*math.cos(a)
        points.append((cx+rx*math.cos(a)*r, cy+ry*math.sin(a)*r))
    cut_vertices = [(u,v,z) for z in (-.04,.04) for u,v in points]
    cut_faces = [tuple(range(hole_count-1,-1,-1)),
                 tuple(hole_count+i for i in range(hole_count))]
    for i in range(hole_count):
        j = (i+1)%hole_count
        cut_faces.append((i,j,j+hole_count,i+hole_count))
    origin, across, down, normal = (frame[k] for k in ("center", "across", "down", "normal"))
    cut_vertices = [tuple((origin+across*u+down*v+normal*w)*api.SCALE) for u,v,w in cut_vertices]
    data = bpy.data.meshes.new(name+" aperture cutter")
    data.from_pydata(cut_vertices, [], cut_faces)
    cutter = bpy.data.objects.new(data.name,data)
    bpy.context.scene.collection.objects.link(cutter)
    bm=bmesh.new();bm.from_mesh(data)
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(data);bm.free()
    api.select(obj)
    boolean=obj.modifiers.new("Pierced forged aperture","BOOLEAN")
    boolean.operation="DIFFERENCE";boolean.solver="EXACT";boolean.object=cutter
    bpy.ops.object.modifier_apply(modifier=boolean.name)
    bpy.data.objects.remove(cutter,do_unlink=True)
    bpy.data.meshes.remove(data)
    # A small bevel leaves the face planar and gives the gold a crisp highlight.
    api.select(obj)
    bevel = obj.modifiers.new("Forged guard bevel", "BEVEL")
    bevel.width = .0018
    bevel.segments = 2
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    return obj


def _crescent(api, frame, name):
    # Long swept point, wide heel, and a shorter turned point match the reference
    # silhouette. Both edge paths meet at the tips; explicit apex fans avoid
    # collapsed duplicate vertices and zero-area export triangles.
    outer = _catmull([(-.190, -.070, 0), (-.151, .055, 0), (-.045, .112, 0),
                      (.105, .105, 0), (.290, .028, 0), (.470, -.090, 0)], 10)
    inner = _catmull([(-.190, -.070, 0), (-.121, .018, 0), (-.025, .068, 0),
                      (.112, .065, 0), (.275, .007, 0), (.470, -.090, 0)], 10)
    vertices = [outer[0], outer[-1]]
    faces = []
    cross = (0, .24, .72, 1)
    heights = (.0008, .0075, .0075, .005)
    for side in (1, -1):
        for i in range(1, len(outer)-1):
            t = i / (len(outer)-1)
            taper = min(1, math.sin(math.pi*t)*3.5)
            for s, height in zip(cross, heights):
                point = outer[i].lerp(inner[i], s)
                # A pronounced ridge catches light; the cutting edge is thin.
                point.z = side * height * taper
                vertices.append(point)
    rows, cols = len(outer)-2, len(cross)
    for side in range(2):
        start = 2 + side*rows*cols
        for i in range(rows-1):
            for j in range(cols-1):
                a = start+i*cols+j
                faces.append((a, a+1, a+1+cols, a+cols))
        for j in range(cols-1):
            faces.append((0, start+j, start+j+1))
            last = start+(rows-1)*cols+j
            faces.append((1, last+1, last))
    back = 2+rows*cols
    for j in (0, cols-1):
        faces.append((0, 2+j, back+j))
        for i in range(rows-1):
            a, b = 2+i*cols+j, back+i*cols+j
            faces.append((a, b, b+cols, a+cols))
        faces.append((1, back+(rows-1)*cols+j, 2+(rows-1)*cols+j))
    blade = _mesh(api, frame, name+" | turquoise crescent cutting edge", vertices, faces, "Blade")
    # Split the bevel crease but retain smooth normals along the swept length.
    for edge in blade.data.edges:
        a, b = edge.vertices
        if a >= 2 and b >= 2 and abs(a-b) == cols:
            column = (a-2)%cols
            if column in (1,2):
                edge.use_edge_sharp=True
    # The blade's inner spine is broad gold with a flat bevel, not wire piping.
    taper = lambda t: max(.035, min(1, t*7, (1-t)*7))
    _sweep(api, frame, name+" | broad swept gold spine", inner,
           lambda t: .012*taper(t), lambda t: .0055*taper(t), "Metal", 8)


def _grip(api, frame, name):
    vertices, faces = [], []
    sides = 16
    for u in (-GRIP_HALF_LENGTH, GRIP_HALF_LENGTH):
        for j in range(sides):
            a = math.tau*j/sides
            vertices.append((u, GRIP_RADIUS*math.cos(a), GRIP_RADIUS*math.sin(a)))
    faces += [tuple(range(sides-1, -1, -1)), tuple(sides+j for j in range(sides))]
    for j in range(sides):
        k = (j+1)%sides
        faces.append((j, k, k+sides, j+sides))
    _mesh(api, frame, name+" | leather grip", vertices, faces, "DarkLeather")
    # One continuous broad leather winding, with real overlap at each turn.
    winding = []
    for i in range(113):
        t = i/112
        a = math.tau*7*t
        winding.append((-.050+.100*t, .011*math.cos(a), .011*math.sin(a)))
    # Sweep along a cylinder using its radial frame, avoiding a degenerate XY
    # tangent at the top of the helix.
    vertices, faces = [], []
    for i, point in enumerate(winding):
        p = Vector(point)
        radial = Vector((0, p.y, p.z)).normalized()
        for j in range(4):
            a = math.tau*j/4
            vertices.append(p+Vector((.0048*math.cos(a), 0, 0))+radial*(.0013*math.sin(a)))
    for i in range(len(winding)-1):
        for j in range(4):
            a, b = i*4+j, i*4+(j+1)%4
            faces.append((a, b, b+4, a+4))
    faces += [(3, 2, 1, 0), tuple((len(winding)-1)*4+j for j in range(4))]
    _mesh(api, frame, name+" | wrapped leather grip", vertices, faces, "Leather")
    for sign in (-1, 1):
        # Gold end collars are short cylinders concentric with the handle.
        vertices, faces = [], []
        for u, radius in ((sign*.049, .012), (sign*.055, .014), (sign*.061, .012)):
            for j in range(16):
                a = math.tau*j/16
                vertices.append((u, radius*math.cos(a), radius*math.sin(a)))
        for r in range(2):
            for j in range(16):
                a, b = r*16+j, r*16+(j+1)%16
                faces.append((a, b, b+16, a+16))
        faces += [tuple(range(15, -1, -1)), tuple(32+j for j in range(16))]
        _mesh(api, frame, name+" | gold grip collar "+str(sign), vertices, faces, "Metal")


def build(api, silhouette, joints):
    """Append two hand-bound blades to api.PARTS and return grip landmarks."""
    assert "Blade" in api.M, "Create the Blade material before sculpt_weapons.build"
    start = len(api.PARTS)
    landmarks = {}
    for side in ("Left", "Right"):
        frame = grip_frame(api, side, joints)
        name = "Reference crescent blade "+side
        _crescent(api, frame, name)
        _grip(api, frame, name)
        _perforated_frame(api, frame, name+" | continuous long gold openwork",
              [(.050,-.008),(.061,-.034),(.099,-.047),(.139,-.045),
               (.110,-.023),(.100,-.004),(.151,.027),(.205,.036),
               (.179,.059),(.128,.082),(.077,.073),(.072,.031),(.050,.014)],
              (.115,.035,.019,.017,0))
        _perforated_frame(api, frame, name+" | continuous short gold openwork",
              [(-.050,-.009),(-.063,-.030),(-.100,-.044),(-.135,-.043),
               (-.110,-.030),(-.100,-.015),(-.130,-.002),(-.143,.007),
               (-.124,.023),(-.085,.049),(-.064,.035),(-.054,.016)],
              (-.102,.012,.011,.008,.20))
        landmarks[side] = {key: list(value) if isinstance(value, Vector) else value
                           for key, value in frame.items()}
    parts = api.PARTS[start:]
    triangles = 0
    for part in parts:
        part.data.calc_loop_triangles()
        triangles += len(part.data.loop_triangles)
    assert triangles <= 12000, triangles
    print("REFERENCE_BLADES_BUILT", triangles, "triangles", flush=True)
    return landmarks
