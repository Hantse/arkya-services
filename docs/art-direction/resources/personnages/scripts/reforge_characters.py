"""Rebuild Arkya character presentation against the 20 October sculpt reference.

The immutable Rain/Snow studies supply anatomy and source deformation weights.
New meshes supply the costume, layered metalwork, crown and sculpted hairstyles.
Run in Blender 4.5; no external assets, operators with UI context or add-ons.
"""
import argparse
import bpy
import bmesh
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Matrix
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import rig_characters as riglib
import sculpt_reference as sculpt
import sculpt_head
import sculpt_weapons

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "game"
M = {}
PARTS = []
SCALE = Vector((1, 1, 1))


def select(obj):
    riglib.select(obj)


def triangulate_for_export(obj):
    """Resolve bent source quads here instead of leaving Unity to discard them."""
    positions = [v.co.copy() for v in obj.data.vertices]
    original_faces = len(obj.data.polygons)
    non_triangles = sum(len(p.vertices) != 3 for p in obj.data.polygons)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=list(bm.faces),
                         quad_method="BEAUTY", ngon_method="BEAUTY")
    collapsed = [f for f in bm.faces if f.calc_area() <= 1e-14]
    removed_zero_area = len(collapsed)
    if collapsed:
        bmesh.ops.delete(bm, geom=collapsed, context="FACES_ONLY")
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    assert len(obj.data.vertices) == len(positions)
    assert all(v.co == positions[v.index] for v in obj.data.vertices)
    assert all(len(p.vertices) == 3 for p in obj.data.polygons)
    smallest = min(p.area for p in obj.data.polygons)
    degenerate = sum(p.area <= 1e-14 for p in obj.data.polygons)
    assert degenerate == 0, (obj.name, degenerate, smallest)
    return {"mesh": obj.name, "sourcePolygons": original_faces,
            "resolvedNonTriangles": non_triangles,
            "removedZeroAreaTriangles": removed_zero_area,
            "exportTriangles": len(obj.data.polygons),
            "minimumTriangleAreaSquareMeters": smallest,
            "degenerateTriangles": degenerate,
            "vertexCoordinatesUnchanged": True}


def mesh(name, vertices, faces, material, bone=None, smooth=True):
    data = bpy.data.meshes.new(name)
    data.from_pydata([tuple(Vector(v) * SCALE) for v in vertices], [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(obj)
    data.materials.append(M[material])
    for p in data.polygons:
        p.use_smooth = smooth
    obj["reforgePart"] = True
    if bone:
        obj["rigBone"] = bone
    PARTS.append(obj)
    return obj


def catmull(points, steps=6, closed=False):
    p = [Vector(v) for v in points]
    result = []
    for i in range(len(p) if closed else len(p) - 1):
        a, b, c, d = (p[(i - 1) % len(p)] if closed else p[max(0, i - 1)],
                      p[i], p[(i + 1) % len(p)],
                      p[(i + 2) % len(p)] if closed else p[min(len(p) - 1, i + 2)])
        for j in range(steps):
            t = j / steps
            result.append(.5 * ((2*b) + (-a+c)*t + (2*a-5*b+4*c-d)*t*t
                                + (-a+3*b-3*c+d)*t*t*t))
    if not closed:
        result.append(p[-1])
    return result


def tube(name, path, radius, material, bone=None, depth=1, sides=12, grooves=0):
    pts = [Vector(p) for p in path]
    verts, faces = [], []
    for i, p in enumerate(pts):
        t = i / (len(pts)-1)
        tangent = (pts[min(i+1, len(pts)-1)] - pts[max(0, i-1)]).normalized()
        right = tangent.cross(Vector((0, 1, 0)))
        if right.length < .01:
            right = tangent.cross(Vector((1, 0, 0)))
        right.normalize()
        other = tangent.cross(right).normalized()
        r = radius(t) if callable(radius) else radius
        for j in range(sides):
            a = math.tau*j/sides
            shape = 1 + grooves*math.cos(a*6 + t*4)
            if material=="Hair":shape+=.025*math.cos(a*13+t*7)
            verts.append(p + right*r*math.cos(a)*shape + other*r*math.sin(a)*depth*shape)
    for i in range(len(pts)-1):
        for j in range(sides):
            a = i*sides+j
            b = i*sides+(j+1)%sides
            faces.append((a, b, b+sides, a+sides))
    faces.extend([tuple(range(sides-1, -1, -1)),
                  tuple((len(pts)-1)*sides+j for j in range(sides))])
    obj=mesh(name, verts, faces, material, bone)
    obj["tubeSides"]=sides
    return obj


def curve(name, points, width, material="Metal", bone=None, depth=1, steps=6):
    return tube(name, catmull(points, steps), width, material, bone, depth)


def plate(name, outline, center, depth, material="OutfitAccent", bone=None,
          trim=.009, rings=7):
    """Convex closed shell with a broad rolled border, not a flat decal."""
    corners=[Vector(p) for p in outline]
    border=[]
    for i,p in enumerate(corners):
        a=p.lerp(corners[i-1],.08)
        b=p.lerp(corners[(i+1)%len(corners)],.08)
        for j in range(4):
            t=j/4
            border.append(a*(1-t)**2+p*2*t*(1-t)+b*t*t)
        nxt=corners[(i+1)%len(corners)].lerp(p,.08)
        for j in range(5):
            border.append(b.lerp(nxt,j/5))
    # Mirrored outlines must retain outward (-Y) front faces in backface-culling
    # renderers. Cycles renders both sides and cannot reveal reversed winding.
    signed_area=sum(p.x*border[(i+1)%len(border)].z-
                    border[(i+1)%len(border)].x*p.z for i,p in enumerate(border))
    if signed_area<0:
        border.reverse()
    c = Vector(center)
    n = Vector((0, -1, 0))
    rx=max(abs(p.x-c.x) for p in border)
    rz=max(abs(p.z-c.z) for p in border)
    def face_y(p):
        return c.y-depth+depth*.58*((p.x-c.x)**2/max(rx*rx,1e-6)
                                   +(p.z-c.z)**2/max(rz*rz,1e-6))
    # A continuous convex height field avoids radial polygon facets on enamel.
    for p in border:
        p.y=face_y(p)
    verts = [c+n*depth]
    count = len(border)
    for i in range(1, rings+1):
        r = i/rings
        for p in border:
            q=c.lerp(p,r)
            q.y=face_y(q)
            verts.append(q)
    faces = [(0, 1+j, 1+(j+1)%count) for j in range(count)]
    for i in range(rings-1):
        start = 1+i*count
        for j in range(count):
            k = (j+1)%count
            faces.append((start+j, start+count+j, start+count+k, start+k))
    # The back is inset 6 mm; rim closes the shell.
    start = len(verts)
    verts += [p-n*.006 for p in border]
    end = 1+(rings-1)*count
    for j in range(count):
        k = (j+1)%count
        faces.append((end+j, start+j, start+k, end+k))
    faces.append(tuple(start+j for j in range(count-1,-1,-1)))
    obj = mesh(name, verts, faces, material, bone)
    obj["plateFrontFaceCount"]=count*rings
    if trim:
        # A broad raised gold bevel has a flat highlight, unlike round piping.
        verts=[];faces=[]
        for p in border:
            inward=(c-p)
            inward.y=0
            inward.normalize()
            for offset,lift in ((-trim*.35,0),(0,.0035),(trim*.95,.0045),(trim*1.20,.001)):
                verts.append(p+inward*offset+n*lift)
        for j in range(count):
            k=(j+1)%count
            for q in range(3):
                faces.append((j*4+q,k*4+q,k*4+q+1,j*4+q+1))
        gold=mesh(name+" | broad gold bevel",verts,faces,"Metal",bone)
        select(gold)
        mod=gold.modifiers.new("Gold border thickness","SOLIDIFY");mod.thickness=.002
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return obj


def ring(name, center, rx, ry, width, material="Metal", bone=None, tilt=0):
    p = []
    for j in range(49):
        a = math.tau*j/48
        p.append(Vector(center)+Vector((rx*math.cos(a), ry*math.sin(a), tilt*math.cos(a))))
    return tube(name, p, width, material, bone, depth=.30 if material=="Leather" else .75,sides=10)


def ribbon(name, path, widths, material, bone=None, fold=.008, normal=(0,-1,0)):
    pts = [Vector(p) for p in path]
    verts, faces = [], []
    across = 10
    for i,p in enumerate(pts):
        tangent = (pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        direction = tangent.cross(Vector(normal)).normalized()
        t = i/(len(pts)-1)
        w = widths(t) if callable(widths) else widths
        for j in range(across+1):
            u = j/across*2-1
            verts.append(p+direction*w*u+Vector(normal)*fold*math.cos(u*math.pi*2+t*3))
    for i in range(len(pts)-1):
        for j in range(across):
            a=i*(across+1)+j
            faces.append((a,a+1,a+across+2,a+across+1))
    o=mesh(name,verts,faces,material,bone)
    o["ribbonVertexCount"]=len(verts)
    o["ribbonFaceCount"]=len(faces)
    select(o)
    mod=o.modifiers.new("Fabric thickness","SOLIDIFY");mod.thickness=.003
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


def shell(name, levels, material, bone=None, scallop=0):
    verts, faces=[],[]
    count=64
    for i,(z,rx,ry,y) in enumerate(levels):
        t=i/(len(levels)-1)
        for j in range(count):
            a=math.tau*j/count
            w=scallop*math.sin(10*a+t*2)*t
            verts.append(((rx+w)*math.sin(a),y-(ry+w)*math.cos(a),z))
    for i in range(len(levels)-1):
        for j in range(count):
            a=i*count+j;b=i*count+(j+1)%count
            faces.append((a,b,b+count,a+count))
    o=mesh(name,verts,faces,material,bone)
    select(o)
    mod=o.modifiers.new("Tailored thickness","SOLIDIFY");mod.thickness=.004
    bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


def crystal(name, center, size, bone="Head"):
    c=Vector(center);w,d,h=size
    verts=[c+Vector((0,0,h*.6)),c+Vector((0,0,-h*.45))]
    for i in range(6):
        a=math.tau*i/6
        verts.append(c+Vector((w*math.cos(a),d*math.sin(a),-.06*h)))
    faces=[]
    for j in range(6):
        faces.extend([(0,2+j,2+(j+1)%6),(1,2+(j+1)%6,2+j)])
    return mesh(name,verts,faces,"Gem",bone,False)


def materials():
    settings = {
        "Skin": ((1,1,1),.69,0), "Hair": ((1,1,1),.56,0),
        "Iris": ((1,1,1),.24,0), "OutfitAccent": ((1,1,1),.40,.28),
        "Metal": ((.78,.43,.075),.28,.73),
        "Ivory": ((.78,.80,.74),.81,0),
        "Leather": ((.092,.036,.026),.68,0),
        "DarkLeather": ((.025,.014,.018),.71,0),
        "Gem": ((.045,.8,.59),.18,.25),
        "Blade": ((.045,.75,.62),.20,.25),
        "EyesWhite": ((.9,.88,.81),.25,0),
        "Pupil": ((.004,.008,.009),.23,0),
        "Mouth": ((.05,.012,.014),.67,0),
    }
    for name,(col,rough,metal) in settings.items():
        m=bpy.data.materials.get(name) or bpy.data.materials.new(name)
        m.use_nodes=True
        bs=m.node_tree.nodes.get("Principled BSDF")
        for link in list(bs.inputs["Base Color"].links):
            m.node_tree.links.remove(link)
        bs.inputs["Base Color"].default_value=(*col,1)
        bs.inputs["Roughness"].default_value=rough
        bs.inputs["Metallic"].default_value=metal
        if name in ("Hair","Skin"):
            bs.inputs["Specular IOR Level"].default_value=.24
        m.diffuse_color=(*col,1)
        if name=="Gem":
            bs.inputs["Emission Color"].default_value=(.02,.35,.2,1)
            bs.inputs["Emission Strength"].default_value=.35
        elif name=="Blade":
            bs.inputs["Emission Color"].default_value=(.015,.25,.18,1)
            bs.inputs["Emission Strength"].default_value=.28
        M[name]=m


def armor(silhouette,joints):
    female=silhouette=="b"
    zc=.045 if female else 0
    def p(x,y,z):return (x,y,z+zc)
    # Close-fitting flexible under-armour with fully modelled back.
    if female:
        shell("Fitted leather waist support",[
            (.96+zc,.128,.093,-.018),(1.00+zc,.122,.093,-.02),
            (1.08+zc,.112,.092,-.025)],"DarkLeather")
        shell("Cuirass wraparound leather foundation",[
            (1.17+zc,.151,.110,-.016),(1.205+zc,.155,.114,-.016)],
            "Leather")
    # The sculptural breastplate has a broad open neck, raised crest and fitted waist.
    outline=[p(-.142,-.066,1.305),p(-.096,-.133,1.293),p(0,-.145,1.254),
             p(.096,-.133,1.293),p(.142,-.066,1.305),p(.140,-.118,1.19),
             p(.105,-.13,1.10),p(0,-.151,1.045),p(-.105,-.13,1.10),p(-.140,-.118,1.19)]
    plate("Jade sculpted cuirass",outline,p(0,-.155,1.185),.034,trim=.014)
    for s in (-1,1):
        curve("Cuirass inlaid sun sweep",[
            p(s*.118,-.145,1.257),p(s*.095,-.18,1.223),
            p(s*.055,-.198,1.211),p(s*.045,-.197,1.167),
            p(s*.076,-.175,1.145)],.0045)
    crystal("Sternum jade focus",p(0,-.204,1.213),(.020,.015,.074),"Chest")
    # White diagonal drapery sits in front of armour and wraps over one shoulder.
    drape=catmull([p(.102,-.066,1.373),p(.097,-.170,1.305),
                  p(.035,-.219,1.211),p(-.044,-.20,1.108),p(-.118,-.13,1.003)],7)
    ribbon("Ivory diagonal drape",drape,lambda t:.035+.008*math.sin(math.pi*t),"Ivory")
    # Its dark and gold outer woven edge remains visible in a small game view.
    edge=[p+Vector((.025,-.007,.015)) for p in drape]
    tube("Diagonal drape leather border",edge,.009,"Leather",depth=.45)
    tube("Diagonal drape gold stitching",edge,.0028,"Metal")
    # A broad back drape and an open folded scarf have genuine cloth thickness.
    ribbon("Ivory back drape",catmull([
        p(.102,.048,1.373),p(.075,.104,1.285),
        p(-.005,.110,1.156),p(-.092,.098,1.018)],8),
        .056,"Ivory",fold=.009,normal=(0,1,0))
    verts=[];faces=[];n=64
    for i in range(6):
        t=i/5
        for j in range(n):
            a=math.tau*j/n
            front=(1+math.cos(a))*.5
            rx=.063+.020*t
            ry=.068+.030*t
            zz=1.395+zc-.069*t-.013*front*math.sin(t*math.pi)
            verts.append((rx*math.sin(a),-.020-ry*math.cos(a)-.003*math.sin(t*math.pi*3),zz))
    for i in range(5):
        for j in range(n):
            a=i*n+j;b=i*n+(j+1)%n;faces.append((a,b,b+n,a+n))
    collar=mesh("Ivory folded open scarf",verts,faces,"Ivory","Chest")
    select(collar)
    mod=collar.modifiers.new("Scarf thickness","SOLIDIFY");mod.thickness=.003
    bpy.ops.object.modifier_apply(modifier=mod.name)
    shell("Waist leather belt",[(.965+zc,.145,.110,-.018),(.99+zc,.142,.109,-.018)],"Leather","Hips")
    for z in (.963,.991):
        ring("Waist raised gold band",(0,-.018,z+zc),.146,.111,.004,"Metal","Hips")
    plate("Waist central gold clasp",
          [p(0,-.147,1.009),p(.034,-.146,.98),p(0,-.147,.945),p(-.034,-.146,.98)],
          p(0,-.15,.98),.008,"Metal","Hips",trim=0)
    crystal("Waist crystal",p(0,-.164,.98),(.013,.008,.043),"Hips")
    # Independent skirt panels follow the thighs while preserving a central slit.
    for s,side in ((1,"Left"),(-1,"Right")):
        bone=side+"UpperLeg"
        verts=[];faces=[];count=49
        for i in range(10):
            t=i/9
            for j in range(count):
                a=math.pi*j/(count-1)
                ripple=.005*t*math.sin(a*10+.2)
                verts.append((s*((.181+.030*t+ripple)*math.sin(a)+.002),
                              -.012-(.139+.019*t+ripple)*math.cos(a),
                              .966+zc-.233*t-.005*t*math.sin(a*4)**2))
        for i in range(9):
            for j in range(count-1):
                a=i*count+j;faces.append((a,a+1,a+1+count,a+count))
        skirt=mesh("Ivory wrap skirt "+side,verts,faces,"Ivory")
        select(skirt);mod=skirt.modifiers.new("Skirt hem thickness","SOLIDIFY");mod.thickness=.003
        bpy.ops.object.modifier_apply(modifier=mod.name)
        hem=[Vector(v)+Vector((0,0,-.001)) for v in verts[-count:]]
        tube("Skirt gold weighted hem "+side,hem,.0035,"Metal",bone)
        for j in range(3):
            x=s*(.035+j*.056)
            end=.792+zc+.025*abs(j-1)
            plate("Leather skirt lamella "+side+str(j),
                  [(x-.025,-.175,.952+zc),(x+.025,-.175,.952+zc),
                   (x+.026,-.185,end+.025),(x,-.192,end),
                   (x-.026,-.185,end+.025)],
                  (x,-.185,(.95+zc+end)/2),.004,"Leather",trim=.0035,rings=4)
        # Large hip shells project over the layered cloth rather than flattening it.
        plate("Jade flared hip "+side,
              [(s*.097,-.138,1.01+zc),(s*.158,-.078,1.003+zc),
               (s*.211,-.082,.895+zc),(s*.158,-.16,.903+zc),(s*.096,-.169,.927+zc)],
              (s*.150,-.154,.958+zc),.023,bone="Hips",trim=.008)
        # Arm plating is aligned to the actual source joints.
        upper=Vector(tuple(joints[side+"UpperArm"][0][k]/SCALE[k] for k in range(3)))
        elbow=Vector(tuple(joints[side+"LowerArm"][0][k]/SCALE[k] for k in range(3)))
        wrist=Vector(tuple(joints[side+"Hand"][0][k]/SCALE[k] for k in range(3)))
        shoulder=upper+Vector((s*.025,-.012,-.012))
        x,z=shoulder.x,shoulder.z
        large=1 if s==1 else .46
        outline=[(x-s*.062*large,-.065,z+.065*large),
                 (x+s*.055*large,-.056,z+.040*large),
                 (x+s*.120*large,-.052,z-.074*large),
                 (x+s*.017*large,-.106,z-.125*large),
                 (x-s*.055*large,-.103,z-.064*large)]
        if s==1:
            # A real wraparound cap supports the wing; it is visible from behind.
            verts=[];faces=[];columns=40
            for row in range(13):
                phi=.04+(1.90-.04)*row/12
                for j in range(columns):
                    a=math.tau*j/columns
                    verts.append((x+.021+.073*math.sin(phi)*math.cos(a),
                                  -.011+.093*math.sin(phi)*math.sin(a),
                                  z-.035+.086*math.cos(phi)))
            for row in range(12):
                for j in range(columns):
                    a=row*columns+j;b=row*columns+(j+1)%columns
                    faces.append((a,b,b+columns,a+columns))
            cap=mesh("Jade enveloping shoulder cap",verts,faces,"OutfitAccent",side+"UpperArm")
            select(cap);mod=cap.modifiers.new("Pauldron inner shell","SOLIDIFY");mod.thickness=.005
            bpy.ops.object.modifier_apply(modifier=mod.name)
            tube("Pauldron back gold rim",[Vector(v) for v in verts[-columns:]]+[Vector(verts[-columns])],
                 .006,"Metal",side+"UpperArm",depth=.65)
            plate("Jade winged pauldron "+side,outline,
                  (x+s*.022*large,-.103,z-.026*large),.032*large,
                  bone=side+"UpperArm",trim=.014)
            plate("Pauldron lower wing "+side,
                  [(x-s*.018,-.096,z-.095),(x+s*.079,-.075,z-.063),
                   (x+s*.110,-.057,z-.146),(x+s*.023,-.102,z-.157)],
                  (x+s*.046,-.109,z-.12),.017,bone=side+"UpperArm",trim=.009)
        else:
            ring("Unarmored upper arm leather band",upper.lerp(elbow,.20),
                 .045,.047,.010,"Leather",side+"UpperArm",s*.016)
        center=elbow.lerp(wrist,.56)
        x,z=center.x,center.z
        plate("Jade swept vambrace "+side,
              [(x-s*.052,-.036,z+.105),(x+s*.035,-.043,z+.080),
               (x+s*.049,-.046,z-.067),(x+s*.019,-.064,z-.106),
               (x-s*.034,-.064,z-.066)],
              (x,-.074,z),.024,bone=side+"LowerArm",trim=.010)
        curve("Vambrace gold rising scroll "+side,
              [(x-s*.020,-.098,z-.055),(x+s*.014,-.103,z-.026),
               (x+s*.015,-.101,z+.018),(x-s*.01,-.094,z+.04),
               (x-s*.022,-.089,z+.023)],.004,"Metal",side+"LowerArm")
        for t in (.20,.88):
            c=elbow.lerp(wrist,t)
            ring("Forearm leather fastening "+side,c,.038,.042,.009,"Leather",side+"LowerArm",s*.008)
        # Open sandal uppers and layered greaves expose the leg anatomy on B.
        ankle=Vector(tuple(joints[side+"Foot"][0][k]/SCALE[k] for k in range(3)))
        knee=Vector(tuple(joints[side+"LowerLeg"][0][k]/SCALE[k] for k in range(3)))
        x=ankle.x
        if female:
            # Sole follows the original Rain foot silhouette.
            outline=[(x-.047,-.191,.018),(x+.047,-.191,.018),
                     (x+.041,.047,.018),(x-.041,.047,.018)]
            # Elliptical padded sole, horizontally oriented.
            verts=[];faces=[]
            for z in (0,.018):
                for j in range(48):
                    a=j*math.tau/48
                    verts.append((x+.047*math.sin(a),-.069-.126*math.cos(a),z))
            faces.extend([tuple(range(47,-1,-1)),tuple(range(48,96))])
            for j in range(48):
                k=(j+1)%48;faces.append((j,k,k+48,j+48))
            mesh("Sandal gold sole "+side,verts,faces,"Metal",side+"Foot")
            for z in (.18,.32,.43):
                ring("Calf leather lacing "+side,(x,.003,z),.053,.053,.005,"Leather",side+"LowerLeg")
            for sign in (-1,1):
                path=[]
                for i in range(61):
                    t=i/60;a=sign*t*math.tau*1.7
                    path.append((x+.052*math.sin(a),.003-.055*math.cos(a),.13+t*.34))
                tube("Crossed calf ribbon "+side+str(sign),path,.005,"Leather",side+"LowerLeg",depth=.5,sides=8)
            for y in (-.150,-.075):
                curve("Sandal arch strap "+side,[(x-.043,y,.034),(x-.025,y-.006,.075),
                      (x,y-.006,.091),(x+.025,y-.006,.075),(x+.043,y,.034)],
                      .009,"Leather",side+"Foot")
        plate("Jade knee guard "+side,
              [(x-.047,-.052,knee.z+.038),(x,-.071,knee.z+.069),
               (x+.047,-.052,knee.z+.038),(x+.039,-.075,knee.z-.036),
               (x,-.087,knee.z-.059),(x-.039,-.075,knee.z-.036)],
              (x,-.088,knee.z+.005),.018,bone=side+"LowerLeg",trim=.007)
        # Narrow shin shell lets crossed leather straps remain legible.
        plate("Jade shin crest "+side,
              [(x-.024,-.063,.409),(x+.024,-.063,.409),
               (x+.029,-.069,.237),(x,-.081,.152),(x-.029,-.069,.237)],
              (x,-.080,.29),.013,bone=side+"LowerLeg",trim=.005)
        for j in range(3):
            xx=x+s*(.042+j*.013)
            plate("Ankle wing "+side+str(j),
                  [(xx,-.042,.105),(xx+s*.022,-.03,.149+j*.023),
                   (xx+s*.042,-.022,.201+j*.025),(xx+s*.005,-.052,.173)],
                  (xx+s*.015,-.048,.16+j*.013),.007,bone=side+"LowerLeg",trim=.003,rings=4)


def headwear(silhouette):
    female=silhouette=="b"
    z=.047 if female else 0
    # Three scalloped bands crown the forehead without covering expressive brows.
    for i in range(2):
        path=catmull([(-.116,-.045,1.599+z+i*.028),(-.089,-.105,1.644+z+i*.028),
                     (0,-.137,1.689+z+i*.028),(.089,-.105,1.644+z+i*.028),
                     (.116,-.045,1.599+z+i*.028)],8)
        tube("Crown gold swept band",path,.009,"Metal","Head",depth=.62,sides=12)
    plate("Crown jade front shell",
          [(-.105,-.095,1.646+z),(0,-.137,1.696+z),(.105,-.095,1.646+z),
           (.099,-.096,1.688+z),(0,-.110,1.742+z),(-.099,-.096,1.688+z)],
          (0,-.13,1.716+z),.014,bone="Head",trim=.006,rings=5)
    for obj in PARTS:
        if obj.name.startswith("Crown jade front shell"):
            for v in obj.data.vertices:
                v.co.y+=.064*SCALE.y*(v.co.x/(.105*SCALE.x))**2
            obj.data.update()
    for s in (-1,1):
        ring("Crown temple medallion",(s*.113,-.052,1.624+z),.014,.010,.005,"Metal","Head")
    for j in range(-2,3):
        crystal("Crown ether crystal "+str(j),
                (j*.038,-.014+abs(j)*.009,1.769+z-abs(j)*.026),
                (.013 if j else .018,.014,.121 if j==0 else .080),"Head")


def hairstyles(silhouette):
    female=silhouette=="b"
    z=.046 if female else 0
    # A fitted cap plus broad swept ribbons create a continuous scalp.
    verts=[];faces=[];n=48;m=16
    for i in range(m+1):
        for j in range(n):
            a=math.tau*j/n
            front=(1+math.cos(a))*.5
            phi=(i/m)*(1.93-.46*front)
            verts.append((.112*math.sin(phi)*math.sin(a),
                          -.005-.116*math.sin(phi)*math.cos(a),
                          1.597+z+.124*math.cos(phi)))
    for i in range(m):
        for j in range(n):
            a=i*n+j;b=i*n+(j+1)%n;faces.append((a,b,b+n,a+n))
    mesh("HairMain scalp",verts,faces,"Hair","Head")
    for j in range(7):
        u=(j-3)/3
        points=[(u*.088,-.091,1.66+z+.020*(1-abs(u))),
                (u*.086-.019,-.055,1.710+z),
                (u*.082-.025,.02,1.725+z),
                (u*.074-.018,.091,1.69+z),
                (u*.055,.109,1.63+z)]
        tube("HairMain swept lock "+str(j),catmull(points,6),
             lambda t:.027*(.55+.45*math.sin(math.pi*t))*(1-.75*t**5),
             "Hair","Head",depth=.50,sides=24,grooves=.09)
    # Side-swept fringe breaks the symmetry, with one deliberate curled tip.
    tube("HairMain side fringe",catmull([(-.069,-.111,1.713+z),
         (-.017,-.143,1.685+z),(.044,-.15,1.634+z),
         (.091,-.119,1.586+z),(.112,-.090,1.55+z),
         (.091,-.102,1.534+z)],7),
         lambda t:.029*(.6+.4*math.sin(math.pi*t))*(1-.96*t**5),
         "Hair","Head",depth=.49,sides=20,grooves=.07)
    for s in (-1,1):
        tube("HairMain temple wave "+str(s),catmull([
            (s*.096,-.066,1.666+z),(s*.117,-.035,1.616+z),
            (s*.106,.007,1.565+z),(s*.105,-.011,1.54+z)],6),
            lambda t:.022*(1-.93*t**4),"Hair","Head",depth=.58,sides=16,grooves=.06)
    # Long selection: nine layered substantial locks, all kept in one mesh batch.
    for j in range(9):
        u=(j-4)/4
        path=catmull([(u*.072,.075,1.7+z),
            (u*.126,.139,1.60+z),(u*.163,.154,1.43+z),
            (u*.164+.017*math.sin(j),.188,1.24+z),
            (u*.12,.20,1.14+z),(u*.089,.168,1.19+z)],7)
        tube("HairLong sculpted wave "+str(j),path,
             lambda t:.035*(.65+.35*math.sin(t*math.pi))*(1-.95*t**5),
             "Hair",depth=.57,sides=18,grooves=.10)
    # The reference's recognisable high, segmented silhouette; real fluted volumes.
    control=[(0,.062,1.715+z),(-.08,.120,1.82+z),(-.18,.194,1.78+z),
             (-.235,.236,1.63+z),(-.24,.258,1.44+z),
             (-.215,.243,1.26+z),(-.25,.224,1.11+z),
             (-.32,.198,1.10+z),(-.34,.170,1.17+z)]
    for i in range(len(control)-1):
        p=Vector(control[i]);q=Vector(control[i+1])
        radius=.065 if i<3 else .058 if i<5 else .045
        path=catmull([p,p.lerp(q,.30)+Vector((-.008,0,.008)),
                      p.lerp(q,.70)+Vector((-.008,0,.008)),q],6)
        tube("HairBraid fluted segment "+str(i),path,
             lambda t,r=radius:r*(.46+.54*math.sin(math.pi*t)**.7),
             "Hair",depth=.85,sides=36,grooves=.20)
        if i<7:
            # Bands perpendicular to the tail tangent, with a flat golden profile.
            tangent=(q-p).normalized();right=tangent.cross(Vector((0,1,0))).normalized()
            other=tangent.cross(right).normalized()
            rr=radius*.54
            band=[p+right*(rr*math.cos(j*math.tau/40))
                  +other*(rr*.85*math.sin(j*math.tau/40)) for j in range(41)]
            tube("HairBraid golden binding "+str(i),band,.009,"Metal",depth=.65,sides=10)


def refine_face(silhouette, objects):
    # Retain authored lips/eyelids, soften the large children's-eye proportions.
    center_z=(1.575 if silhouette=="b" else 1.549)*SCALE.z
    eye_x=(.047 if silhouette=="b" else .037)*SCALE.x
    for obj in objects:
        if obj.name not in ("Head","Eyes") and "eyelash" not in obj.name.lower() and "eyebrow" not in obj.name.lower():
            continue
        for v in obj.data.vertices:
            p=v.co
            center=Vector((math.copysign(eye_x,p.x),-.1*SCALE.y,center_z))
            dx=(abs(p.x)-eye_x)/(.047*SCALE.x)
            dz=(p.z-center_z)/(.046*SCALE.z)
            front=riglib.clamp((-p.y-.035*SCALE.y)/(.05*SCALE.y))
            influence=math.exp(-dx*dx-dz*dz)*front
            if obj.name=="Eyes":
                influence=1
            p.z=center_z+(p.z-center_z)*(1-.22*influence)
            p.x=center.x+(p.x-center.x)*(1-.08*influence)
            if obj.name=="Head":
                # More room between nose, lips and chin without moving the neck.
                lower=math.exp(-((p.z-(center_z-.094*SCALE.z))/(.060*SCALE.z))**2)
                front=riglib.clamp((-p.y-.035*SCALE.y)/(.065*SCALE.y))
                p.z-=.015*SCALE.z*lower*front
                p.x*=1-.045*lower*front
            if "eyebrow" in obj.name.lower():
                p.z+=(.006-abs(abs(p.x)-eye_x)*.18)*SCALE.z
                brow_z=center_z+.047*SCALE.z
                p.z=brow_z+(p.z-brow_z)*.66
        obj.data.update()
        if "eyebrow" in obj.name.lower():
            paint=obj.data.color_attributes.get("Painted")
            if paint:
                for c in paint.data:
                    c.color=(.11,.11,.11,1)


def mature_head_proportions(objects,silhouette):
    anchor=Vector((0,-.012,(1.410 if silhouette=="b" else 1.355)*SCALE.z))
    for obj in objects:
        facial=obj.name in ("Head","Eyes") or "eyebrow" in obj.name.lower() or "eyelash" in obj.name.lower()
        if facial or obj.name.startswith(("HairMain","HairLong","HairBraid")) or obj.get("rigBone")=="Head":
            for v in obj.data.vertices:
                v.co=anchor+(v.co-anchor)*.89
                if silhouette=="a" and obj.name=="Head":
                    # The source neck base flares beyond the collar; tuck only
                    # this covered attachment below the unchanged jaw and rig.
                    neck=riglib.smooth(riglib.clamp((1.423-v.co.z/SCALE.z)/.035))
                    v.co.x*=1-.30*neck
            obj.data.update()


def fit_straps_and_greaves(objects,silhouette):
    body=next(o for o in objects if o.name=="Body")
    targets=[body]
    if silhouette=="a":
        targets.extend(o for o in objects if "pants" in o.name.lower() or o.name.lower().startswith("boot shaft"))
    coords=[];polygons=[]
    for target in targets:
        start=len(coords);coords.extend(v.co.copy() for v in target.data.vertices)
        polygons.extend(tuple(start+i for i in p.vertices) for p in target.data.polygons)
    tree=BVHTree.FromPolygons(coords,polygons)
    leg_trees={}
    for obj in objects:
        if obj.name.startswith("Gold curved upper greave"):
            side=1 if obj["legCenterX"]>0 else -1
            if side not in leg_trees:
                local=[p for p in polygons if sum(coords[i].x for i in p)*side>0 and max(coords[i].z for i in p)<.62*SCALE.z]
                leg_trees[side]=BVHTree.FromPolygons(coords,local)
            count=obj["surfaceVertexCount"];face_count=obj["surfaceFaceCount"]
            vertices=[v.co.copy() for v in list(obj.data.vertices)[:count]]
            faces=[tuple(p.vertices) for p in list(obj.data.polygons)[:face_count]]
            for v in vertices:
                radial=Vector((v.x-obj["legCenterX"],v.y-obj["legCenterY"],0)).normalized()
                hit,_,_,_=leg_trees[side].ray_cast(v+radial*.15,-radial,.30)
                if hit is not None:v[:]=hit+radial*.006*SCALE.z
            obj.data.clear_geometry();obj.data.from_pydata(vertices,[],faces);obj.data.update()
            for poly in obj.data.polygons:poly.use_smooth=True
            select(obj);mod=obj.modifiers.new("Fitted greave thickness","SOLIDIFY");mod.thickness=.004*SCALE.z
            bpy.ops.object.modifier_apply(modifier=mod.name)
            continue
        if any(label in obj.name for label in ("Forearm leather fastening",
                "Unarmored upper arm leather band","Calf leather lacing",
                "Crossed calf ribbon","Sandal arch strap")):
            for v in obj.data.vertices:
                point,normal,_,distance=tree.find_nearest(v.co,.075*SCALE.z)
                if point is not None:
                    thickness=.0028+.0010*v.normal.dot(normal)
                    v.co=point+normal*thickness*SCALE.z
            obj.data.update()
        if silhouette=="b" and obj.name.startswith("Jade shin crest"):
            for v in obj.data.vertices:
                z=v.co.z/SCALE.z
                v.co.y+=.050*SCALE.y*riglib.clamp((.44-z)/.30)
            obj.data.update()


def repair_generated_surface_normals(objects):
    checked=0
    for obj in objects:
        if not obj.get("reforgePart"):
            continue
        bm=bmesh.new();bm.from_mesh(obj.data)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        if not any(e.is_boundary for e in bm.edges) and bm.calc_volume(signed=True)<0:
            bmesh.ops.reverse_faces(bm,faces=list(bm.faces))
        bm.to_mesh(obj.data);bm.free();obj.data.update()
        front_count=obj.get("plateFrontFaceCount")
        if front_count:
            wrong=[p.index for p in list(obj.data.polygons)[:front_count] if p.normal.y>=0]
            assert not wrong,(obj.name,"inward front faces",wrong[:10])
            checked+=front_count
    return checked

def paint(obj):
    data=obj.data
    old=data.color_attributes.get("Painted")
    if old and (not obj.get("reforgePart") or obj.get("paintedSculpt")):
        return
    for a in list(data.color_attributes):
        data.color_attributes.remove(a)
    attr=data.color_attributes.new(name="Painted",type="FLOAT_COLOR",domain="POINT")
    material=data.materials[0].name
    data.update()
    for v,c in zip(data.vertices,attr.data):
        p=v.co;n=v.normal
        broad=math.sin(p.x*21+p.z*9)*math.sin(p.y*18-p.z*8)
        brush=math.sin(p.z*57+p.x*28)*math.sin(p.y*37+p.x*15)
        light=riglib.clamp(.50+.5*n.dot(Vector((-.25,-.4,.82))))
        value=.78+.17*light+.04*broad+.015*brush
        warmth=.04*light-.015
        if material=="Hair":
            value=.33+.34*light+.07*broad+.025*brush
            warmth=.015
        elif material=="Ivory":
            value=.87+.11*light+.025*broad;warmth=.006
        elif material=="Gem":
            value=.82+.16*light;warmth=-.02
        elif material=="Metal":
            value=.46+.44*light+.07*broad;warmth=.055*light
        elif material=="OutfitAccent":
            value=.57+.31*light+.055*broad;warmth=-.015
        c.color=(min(1,value+warmth),min(1,value),min(1,value-warmth),1)
    data.color_attributes.active_color=attr
    data.color_attributes.render_color_index=0


def unify_paint_and_normals(obj):
    # Blender permits a point and a corner layer with the same name after join.
    # Export one unambiguous corner layer so brows, blush and gold paint survive.
    data=obj.data
    attr=data.color_attributes.get("Painted")
    if attr.domain=="POINT":
        colors=[tuple(c.color) for c in attr.data]
        data.color_attributes.remove(attr)
        attr=data.color_attributes.new(name="Painted",type="FLOAT_COLOR",domain="CORNER")
        for loop,c in zip(data.loops,attr.data):
            c.color=colors[loop.vertex_index]
    data.color_attributes.active_color=attr
    data.color_attributes.render_color_index=0
    if data.has_custom_normals:
        select(obj)
        bpy.ops.mesh.customdata_custom_splitnormals_clear()


def bind(obj,rig,joints,height):
    if obj.get("rigWeightMode")=="torso":
        count=obj.get("ribbonVertexCount",len(obj.data.vertices))
        heights=obj.data.attributes.get("TorsoBindZ")
        source_z=([item.value for item in list(heights.data)[:count]] if heights
                  else [v.co.z for v in list(obj.data.vertices)[:count]])
        obj.vertex_groups.clear()
        groups={name:obj.vertex_groups.new(name=name) for name in ("Chest","Spine","Hips")}
        for v in obj.data.vertices:
            p=v.co.copy()
            p.z=source_z[v.index%count]
            weights=riglib.axial_weights(p,[height*.77,height*.62,height*.51],["Chest","Spine","Hips"])
            for name,weight in weights.items():
                if weight>0:groups[name].add([v.index],weight,"REPLACE")
        obj.parent=rig
        mod=obj.modifiers.new("Game skinning","ARMATURE");mod.object=rig
        return
    bone=obj.get("rigBone")
    if bone:
        obj.vertex_groups.clear()
        g=obj.vertex_groups.new(name=bone)
        g.add(list(range(len(obj.data.vertices))),1,"REPLACE")
        obj.parent=rig
        mod=obj.modifiers.new("Game skinning","ARMATURE");mod.object=rig
        return
    riglib.bind(obj,rig,joints,height)


def restore_complete_body(old,race):
    source=json.loads((OUT/"source-joints.json").read_text())["b"]["bodyBounds"]
    sx=max(v.co.x for v in old.data.vertices)/source[1][0]
    sy=sx/.92*.95 if race=="elf" else sx
    sz=sx/.92 if race=="elf" else sx
    dz=min(v.co.z for v in old.data.vertices)-source[0][2]*sz
    with bpy.data.libraries.load(str(OUT/"complete-rain-anatomy.blend"),link=False) as (available,loaded):
        loaded.objects=["CompleteRainBody"]
    obj=loaded.objects[0]
    bpy.context.scene.collection.objects.link(obj)
    obj.animation_data_clear()
    for v in obj.data.vertices:
        v.co.x*=sx;v.co.y*=sy;v.co.z=v.co.z*sz+dz
        # Lift the sole skin onto the padded sandal, tapering to zero at ankle.
        v.co.z+=.017*SCALE.z*(1-riglib.smooth(v.co.z/(.095*SCALE.z)))
    obj.data.update()
    bpy.data.objects.remove(old,do_unlink=True)
    obj.name="Body"
    return obj


def build(race,silhouette):
    global SCALE
    slug=f"{race}-{silhouette}"
    PARTS.clear();M.clear()
    bpy.ops.wm.open_mainfile(filepath=str(ROOT/"assets"/f"{slug}-short.blend"),use_scripts=False)
    bpy.context.preferences.filepaths.save_version=0
    # Construct the original skeleton BEFORE touching anatomy or the silhouette.
    for o in list(bpy.context.scene.objects):
        if o.type!="MESH":
            bpy.data.objects.remove(o,do_unlink=True)
        else:
            o.parent=None
    rig,joints=riglib.skeleton(silhouette,race)
    originals=list(o for o in bpy.context.scene.objects if o.type=="MESH")
    SCALE=Vector((.92*1.85/1.75,.95*1.85/1.75,1.85/1.75)) if race=="elf" else Vector((1,1,1))
    keep=[]
    for o in originals:
        name=o.name.lower()
        retain=o.name in ("Body","Head","Eyes") or "eyebrow" in name or "eyelash" in name
        if silhouette=="a" and ("pants" in name or "shoes_" in name or name.startswith("boot shaft") or name=="ether guard-shirt"):
            retain=True
        if retain:
            keep.append(o)
        else:
            bpy.data.objects.remove(o,do_unlink=True)
    if silhouette=="b":
        old=next(o for o in keep if o.name=="Body")
        keep.remove(old)
        keep.append(restore_complete_body(old,race))
    materials()
    if silhouette=="a":
        shirt=next(o for o in keep if o.name=="Ether Guard-shirt")
        shirt.data.materials.clear();shirt.data.materials.append(M["DarkLeather"])
        for poly in shirt.data.polygons:poly.material_index=0
    cranium_check=sculpt_head.refine_face(sys.modules[__name__],silhouette,keep)
    grip_checks=sculpt_weapons.close_hands(sys.modules[__name__],keep,joints)
    armor(silhouette,joints)
    headwear(silhouette)
    hairstyles(silhouette)
    sculpt.rebuild(sys.modules[__name__],silhouette,joints)
    sculpt_head.hair(sys.modules[__name__],silhouette,sculpt.ring_band)
    sculpt_head.add_brows(sys.modules[__name__],keep,1.578 if silhouette=="b" else 1.549)
    sculpt_head.finish_lids(sys.modules[__name__],keep,silhouette)
    blade_landmarks=sculpt_weapons.build(sys.modules[__name__],silhouette,joints)
    if silhouette=="a":
        keep+=riglib.add_knee_liners(rig,joints)
    meshes=keep+PARTS
    mature_head_proportions(meshes,silhouette)
    fit_straps_and_greaves(meshes,silhouette)
    sculpt.fit_drapery(sys.modules[__name__],meshes)
    front_faces_checked=repair_generated_surface_normals(meshes)
    for o in meshes:
        for i,m in enumerate(o.data.materials):
            key=m.name.split(".")[0]
            if key in M:o.data.materials[i]=M[key]
        paint(o)
        unify_paint_and_normals(o)
        bind(o,rig,joints,1.85 if race=="elf" else 1.75)
    drapery_checks=[]
    for o in meshes:
        if o.get("rigWeightMode")!="torso":continue
        used={o.vertex_groups[g.group].name for v in o.data.vertices for g in v.groups if g.weight>1e-7}
        assert used.issubset({"Chest","Spine","Hips"}),(o.name,"non-torso weights",used)
        layer_error=0.0
        count=o.get("ribbonVertexCount")
        if count:
            for i in range(count):
                a={g.group:g.weight for g in o.data.vertices[i].groups}
                b={g.group:g.weight for g in o.data.vertices[i+count].groups}
                layer_error=max(layer_error,max(abs(a.get(g,0)-b.get(g,0)) for g in a.keys()|b.keys()))
            assert layer_error<1e-7,(o.name,"drape layer weights differ",layer_error)
        drapery_checks.append({"part":o.name,"bones":sorted(used),"armWeightsAbsent":True,
                               "pairedLayerWeightError":layer_error})
    blade_triangles=0;blade_checks=[]
    for o in meshes:
        if not o.get("referenceWeapon"):continue
        o.data.calc_loop_triangles();blade_triangles+=len(o.data.loop_triangles)
        bone=o["rigBone"]
        assert bone in ("LeftHand","RightHand"),(o.name,bone)
        for v in o.data.vertices:
            assert len(v.groups)==1 and o.vertex_groups[v.groups[0].group].name==bone
            assert abs(v.groups[0].weight-1)<1e-7
        blade_checks.append({"part":o.name,"bone":bone,"rigidHandWeight":1.0})
    batches={name:[] for name in ("CharacterMesh","HairMain","HairLong","HairBraid")}
    for o in meshes:
        key=next((x for x in ("HairMain","HairLong","HairBraid") if o.name.startswith(x)),"CharacterMesh")
        batches[key].append(o)
    merged=[riglib.merge(name,items) for name,items in batches.items()]
    for o in merged:
        select(o)
        if o.data.has_custom_normals:
            bpy.ops.mesh.customdata_custom_splitnormals_clear()
        bpy.ops.object.vertex_group_limit_total(limit=4)
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    triangulation_checks = [triangulate_for_export(o) for o in merged]
    clips=riglib.create_animations(rig)
    riglib.neutral_materials()
    report=riglib.validation(rig,merged,clips,slug)
    coords=[v.co for o in merged for v in o.data.vertices]
    report["artPass"]="Adult face and sculpted hair reference pass 2026-10-20"
    report["bodyReferenceHeightMeters"]=1.85 if race=="elf" else 1.75
    report["boundsBlenderZUp"]=[[min(p[k] for p in coords) for k in range(3)],
                              [max(p[k] for p in coords) for k in range(3)]]
    report["newCostumeAndHairParts"]=len(PARTS)
    report["sourceSkeletonPreserved"]=True
    report["staticGripChecks"]=grip_checks
    report["exportTriangulationChecks"]=triangulation_checks
    report["cranialSurfaceRepair"]=cranium_check
    report["bladeGripLandmarksReferenceSpace"]=blade_landmarks
    report["bladeTriangles"]=blade_triangles
    report["bladeWeightChecks"]=blade_checks
    report["torsoDraperyWeightChecks"]=drapery_checks
    report["outwardPlateFrontFacesVerified"]=front_faces_checked
    report["rigRestMatrices"]={b.name:[list(row) for row in b.matrix_local] for b in rig.data.bones}
    (OUT/f"{slug}.materials.json").write_text(json.dumps({m.name:list(m.diffuse_color)
        for o in merged for m in o.data.materials},indent=2),encoding="utf-8")
    bpy.context.scene["attribution"]=riglib.CREDIT
    bpy.context.scene["asset_description"]="Adult face and sculpted hair reference pass 2026-10-20; preserved Generic rig V1"
    for o in merged:o.hide_render=False
    for _ in range(2):
        bpy.ops.outliner.orphans_purge(do_local_ids=True,do_linked_ids=True,do_recursive=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/f"{slug}.blend"),compress=True)
    bpy.ops.object.select_all(action="DESELECT");rig.select_set(True)
    for o in merged:o.select_set(True)
    bpy.context.view_layer.objects.active=rig
    export_path=OUT/f"{slug}.pending.fbx"
    bpy.ops.export_scene.fbx(filepath=str(export_path),use_selection=True,
        object_types={"ARMATURE","MESH"},apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_UNITS",global_scale=1,axis_forward="-Z",axis_up="Y",
        use_mesh_modifiers=True,use_mesh_edges=False,mesh_smooth_type="FACE",
        colors_type="LINEAR",prioritize_active_color=True,add_leaf_bones=False,
        use_armature_deform_only=True,armature_nodetype="NULL",bake_anim=True,
        bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,bake_anim_step=1,bake_anim_simplify_factor=0,
        path_mode="AUTO",embed_textures=False)
    export_path.replace(OUT/f"{slug}.fbx")
    (OUT/f"{slug}.validation.json").write_text(json.dumps(report,indent=2),encoding="utf-8")
    print("REFORGED",slug,report["trianglesAllHairOptions"],report["boundsBlenderZUp"],flush=True)


if __name__=="__main__":
    parser=argparse.ArgumentParser();parser.add_argument("--only",default="")
    args=parser.parse_args(sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else [])
    for race,silhouette in (("human","a"),("human","b"),("elf","a"),("elf","b")):
        if not args.only or f"{race}-{silhouette}" in args.only.split(","):
            build(race,silhouette)

