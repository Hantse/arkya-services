"""Second sculpt pass: continuous fitted forms from the October character sheets."""
import bpy
import math
from mathutils import Vector
from mathutils.geometry import tessellate_polygon
from mathutils.bvhtree import BVHTree

A = None

TORSO_PARTS = ("Jade fitted crescent", "Cuirass forged spiral", "Leather underbust",
               "Ivory flowing diagonal", "Woven dark drape", "Drape embroidered",
               "Ivory continuous shoulder back")


def sampled_envelope(objects, scale, front=True):
    """A continuous sampled depth field avoids ray-hit discontinuities at seams."""
    vertices=[];polygons=[]
    for obj in objects:
        start=len(vertices);vertices.extend(v.co.copy() for v in obj.data.vertices)
        polygons.extend(tuple(start+i for i in p.vertices) for p in obj.data.polygons)
    tree=BVHTree.FromPolygons(vertices,polygons)
    nx,nz=65,91;x0,x1=-.22,.22;z0,z1=.96,1.46
    values={};direction=Vector((0,1 if front else -1,0))
    for j in range(nz):
        z=z0+(z1-z0)*j/(nz-1)
        for i in range(nx):
            x=x0+(x1-x0)*i/(nx-1)
            p,_,_,_=tree.ray_cast(Vector((x*scale.x,-1 if front else 1,z*scale.z)),direction,2)
            if p is not None:values[i,j]=p.y/scale.y
    # Only the side edges of the torso can miss; extend the nearest row sample.
    for j in range(nz):
        row=[i for i in range(nx) if (i,j) in values]
        if not row:continue
        for i in range(nx):
            if (i,j) not in values:values[i,j]=values[min(row,key=lambda k:abs(k-i)),j]
    rows=[j for j in range(nz) if (0,j) in values]
    for j in range(nz):
        if (0,j) not in values:
            source=min(rows,key=lambda k:abs(k-j))
            for i in range(nx):values[i,j]=values[i,source]
    for _ in range(4):
        smoothed=dict(values)
        for j in range(1,nz-1):
            for i in range(1,nx-1):
                mean=sum(values[i+dx,j+dz] for dx,dz in ((0,0),(-1,0),(1,0),(0,-1),(0,1)))/5
                smoothed[i,j]=min(values[i,j],mean) if front else max(values[i,j],mean)
        values=smoothed
    def sample(x,z):
        u=max(0,min(nx-1.00001,(x-x0)/(x1-x0)*(nx-1)))
        v=max(0,min(nz-1.00001,(z-z0)/(z1-z0)*(nz-1)))
        i,j=int(u),int(v);a,b=u-i,v-j
        return ((1-a)*(1-b)*values[i,j]+a*(1-b)*values[i+1,j]
                +(1-a)*b*values[i,j+1]+a*b*values[i+1,j+1])
    return sample


def surface(name, outline, center, front, material="OutfitAccent", bone=None,
            border=.012, rounds=8, smooth_outline=True):
    """Closed curved panel with a wide three-plane forged edge."""
    points=A.catmull(outline,5,closed=True) if smooth_outline else [Vector(p) for p in outline]
    if sum(p.x*points[(i+1)%len(points)].z-points[(i+1)%len(points)].x*p.z for i,p in enumerate(points))<0:
        points.reverse()
    c=Vector(center);n=len(points)
    verts=[(c.x,front(c.x,c.z),c.z)]
    for row in range(1,rounds+1):
        for p in points:
            v=c.lerp(p,row/rounds);v.y=front(v.x,v.z);verts.append(v)
    faces=[(0,1+j,1+(j+1)%n) for j in range(n)]
    for row in range(rounds-1):
        k=1+row*n
        for j in range(n):faces.append((k+j,k+n+j,k+n+(j+1)%n,k+(j+1)%n))
    front_count=n*rounds
    # Narrow swept flanges can be concave, with no visible radial kernel.
    invalid=any((points[j].x-c.x)*(points[(j+1)%n].z-c.z)-
                (points[(j+1)%n].x-c.x)*(points[j].z-c.z)<-1e-8 for j in range(n))
    if invalid:
        flat=[Vector((p.x,p.z,0)) for p in points]
        verts=[(p.x,front(p.x,p.z),p.z) for p in points]
        faces=[]
        for tri in tessellate_polygon([flat]):
            faces.append(tuple(v if isinstance(v,int) else min(range(n),key=lambda j:(flat[j]-v).length_squared) for v in tri))
        front_count=len(faces)
        boundary_start=0
    else:
        boundary_start=1+(rounds-1)*n
    k=len(verts)
    if name.startswith("Jade fitted crescent"):
        # A parallel back surface cannot cut through a concave fitted front.
        verts.extend((p[0],p[1]+.005,p[2]) for p in list(verts))
        faces.extend(tuple(k+i for i in reversed(face)) for face in list(faces))
        for j in range(n):
            a=boundary_start+j;b=boundary_start+(j+1)%n
            faces.append((a,a+k,b+k,b))
    else:
        verts.extend((p.x,front(p.x,p.z)+.005,p.z) for p in points)
        for j in range(n):faces.append((boundary_start+j,k+j,k+(j+1)%n,boundary_start+(j+1)%n))
        faces.append(tuple(k+j for j in range(n-1,-1,-1)))
    obj=A.mesh(name,verts,faces,material,bone)
    obj["plateFrontFaceCount"]=front_count
    if material=="Metal":obj.data.set_sharp_from_angle(angle=.22)
    if border:
        verts=[];faces=[]
        for p in points:
            inward=c-p;inward.y=0;inward.normalize()
            for distance,lift in ((-.12*border,0),(0,.003),(.75*border,.005),(border,.001)):
                q=p+inward*distance
                clearance=.0045 if name.startswith("Jade fitted crescent") else 0
                verts.append((q.x,front(q.x,q.z)-lift-clearance,q.z))
        for j in range(n):
            k=(j+1)%n
            for r in range(3):faces.append((j*4+r,k*4+r,k*4+r+1,j*4+r+1))
        o=A.mesh(name+" forged gold edge",verts,faces,"Metal",bone)
        A.select(o);m=o.modifiers.new("Closed forged border","SOLIDIFY");m.thickness=.002
        bpy.ops.object.modifier_apply(modifier=m.name)
        o.data.set_sharp_from_angle(angle=.20)
    return obj


def metal_sweep(name,points,width=.008,bone=None,normal=(0,-1,0)):
    """Broad forged inlay; shallow flat face with slim bevel shoulders."""
    pts=A.catmull(points,6);verts=[];faces=[];normal=Vector(normal)
    for i,p in enumerate(pts):
        tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        across=tangent.cross(normal).normalized()
        t=i/(len(pts)-1);w=width*(.45+.55*math.sin(math.pi*t)**.5)
        for u,h in ((-1,0),(-.72,.002),(.72,.002),(1,0)):
            verts.append(p+across*w*u+normal*h)
    for i in range(len(pts)-1):
        for j in range(3):
            k=i*4+j;faces.append((k,k+1,k+5,k+4))
    o=A.mesh(name,verts,faces,"Metal",bone)
    A.select(o);m=o.modifiers.new("Raised metal thickness","SOLIDIFY");m.thickness=.002
    bpy.ops.object.modifier_apply(modifier=m.name)
    o.data.set_sharp_from_angle(angle=.30)
    return o


def remove(parts):
    for obj in list(A.PARTS):
        if parts(obj.name):
            A.PARTS.remove(obj);bpy.data.objects.remove(obj,do_unlink=True)


def ring_band(name,center,axis,rx,ry,width,material="Metal",bone="Head"):
    axis=Vector(axis).normalized();right=axis.cross(Vector((0,1,0)))
    if right.length<.01:right=axis.cross(Vector((1,0,0)))
    right.normalize();other=axis.cross(right).normalized();c=Vector(center)
    verts=[];faces=[];n=48
    for along,r in ((-.5*width,.97),(-.38*width,1),(.38*width,1),(.5*width,.97)):
        for j in range(n):
            a=j*math.tau/n;verts.append(c+axis*along+right*rx*r*math.cos(a)+other*ry*r*math.sin(a))
    for i in range(3):
        for j in range(n):faces.append((i*n+j,i*n+(j+1)%n,(i+1)*n+(j+1)%n,(i+1)*n+j))
    o=A.mesh(name,verts,faces,material,bone)
    A.select(o);m=o.modifiers.new("Solid gold band","SOLIDIFY");m.thickness=.002
    bpy.ops.object.modifier_apply(modifier=m.name)
    return o


def cloth_cowl(z):
    verts=[];faces=[];n=80;rows=14
    for row in range(rows):
        t=row/(rows-1)
        for j in range(n):
            a=math.tau*j/n;front=max(0,math.cos(a));side=math.sin(a)
            rx=(.073 if z==0 else .060)+.035*t+(.012*(1-t) if z==0 else 0)
            ry=(.078 if z==0 else .060)+.037*t+(.004*(1-t) if z==0 else 0)
            zz=1.392+z-.036*t-.060*front*front*t+.006*side+(.010*(1-t)**2 if z==0 else 0)
            folds=.005*math.sin(t*math.pi*5+.5*side)*math.sin(math.pi*t)
            verts.append(((rx+folds)*side,-.012-(ry+folds)*math.cos(a),zz))
    for row in range(rows-1):
        for j in range(n):faces.append((row*n+j,row*n+(j+1)%n,(row+1)*n+(j+1)%n,(row+1)*n+j))
    o=A.mesh("Ivory soft falling cowl",verts,faces,"Ivory","Chest")
    A.select(o);m=o.modifiers.new("Hemmed fabric","SOLIDIFY");m.thickness=.003
    bpy.ops.object.modifier_apply(modifier=m.name)
    # Shoulder-to-waist cloth widens and twists over the side of the chest.
    path=A.catmull([(.102,-.020,1.357+z),(.094,-.150,1.322+z),
        (.038,-.215,1.218+z),(-.044,-.165,1.11+z),(-.111,-.123,1.012+z)],10)
    A.ribbon("Ivory flowing diagonal drape",path,lambda t:.040+.016*math.sin(math.pi*t)-.010*max(0,1-t/.18)**2,"Ivory",fold=.008)
    edge=[]
    for i,p in enumerate(path):
        tangent=(path[min(i+1,len(path)-1)]-path[max(0,i-1)]).normalized()
        across=tangent.cross(Vector((0,-1,0))).normalized()
        t=i/(len(path)-1)
        edge.append(p-across*(.037+.016*math.sin(math.pi*t)-.010*max(0,1-t/.18)**2)+Vector((0,-.010,0)))
    A.ribbon("Woven dark drape edge",edge,.009,"Leather",fold=0)
    # Small repeating curls are geometry, carried by the cloth weights.
    for i in range(4,len(edge)-3,5):
        p=edge[i]
        curve=[p+Vector((.005*math.cos(t*math.pi*1.6),-.003,.006*math.sin(t*math.pi*1.6))) for t in (0,.2,.4,.6,.8,1)]
        A.curve("Drape embroidered curl",curve,.0018,"Metal")
    A.ribbon("Ivory continuous shoulder back drape",A.catmull([
        (.102,.047,1.357+z),(.078,.089,1.29+z),(.012,.085,1.16+z),(-.085,.078,1.018+z)],9),
        lambda t:.053+.010*math.sin(math.pi*t)-.018*max(0,1-t/.18)**2,"Ivory",fold=.007,normal=(0,1,0))
    for s in (-1,1):
        ring_band("Scarf round gold clasp",(s*.059,-.063,1.381+z),(0,-1,0),.014,.014,.004,"Metal","Chest")


def costume(silhouette,joints):
    z=.045 if silhouette=="b" else 0
    female=silhouette=="b"
    keep=("Sandal ","Calf leather","Crossed calf","Forearm leather","Unarmored upper arm")
    remove(lambda n:not n.startswith(keep) and not n.startswith(("Crown","Hair")))
    anatomy=[o for o in bpy.context.scene.objects if o.name in ("Body","Ether Guard-shirt")]
    chest_surface=sampled_envelope(anatomy,A.SCALE)
    # Fitted chest pieces curve around the body and meet below an open V.
    for s in (-1,1):
        cx=s*.065;cz=1.225+z
        def chest_y(x,zz,cx=cx,cz=cz):
            dome=max(0,1-((x-cx)/.09)**2-((zz-cz)/.15)**2)
            return chest_surface(x,zz)-.010-.008*dome
        outline=[(s*.008,0,1.244+z),(s*.049,0,1.284+z),(s*.119,0,1.326+z),
            (s*.139,0,1.248+z),(s*.138,0,1.183+z),(s*.103,0,1.147+z),
            (s*.036,0,1.145+z),(s*.004,0,1.174+z)]
        surface("Jade fitted crescent breastplate "+str(s),outline,(cx,0,cz),chest_y,border=.015)
        coords=[(s*.100,1.261+z),(s*.077,1.275+z),(s*.048,1.260+z),
            (s*.046,1.230+z),(s*.070,1.221+z),(s*.086,1.237+z),(s*.074,1.244+z)]
        metal_sweep("Cuirass forged spiral "+str(s),[(x,chest_y(x,zz)-.004,zz) for x,zz in coords],.0055)
    # The support strap follows the ribs rather than a floating elliptical hoop.
    vertices=[];polygons=[]
    for obj in anatomy:
        start=len(vertices);vertices.extend(v.co.copy() for v in obj.data.vertices)
        polygons.extend(tuple(start+i for i in p.vertices) for p in obj.data.polygons
                        if all(abs(obj.data.vertices[i].co.x)<.17*A.SCALE.x for i in p.vertices))
    torso=BVHTree.FromPolygons(vertices,polygons)
    verts=[];faces=[]
    for zz in (1.142+z,1.165+z):
        for j in range(64):
            a=j*math.tau/64;radial=Vector((math.sin(a),-math.cos(a),0))
            center=Vector((0,0,zz*A.SCALE.z))
            hit,_,_,_=torso.ray_cast(center,radial,.3)
            point=hit+radial*.008 if hit is not None else center+radial*.13
            verts.append(tuple(point[k]/A.SCALE[k] for k in range(3)))
    for j in range(64):faces.append((j,(j+1)%64,(j+1)%64+64,j+64))
    strap=A.mesh("Leather underbust support",verts,faces,"Leather")
    A.select(strap);m=strap.modifiers.new("Fitted leather thickness","SOLIDIFY");m.thickness=.004
    bpy.ops.object.modifier_apply(modifier=m.name)
    cloth_cowl(z)
    A.shell("Waist fitted leather belt",[(.968+z,.147,.111,-.005),(.990+z,.142,.108,-.005)],"Leather","Hips")
    ring_band("Waist flat gold border",(0,-.005,.985+z),(0,0,1),.148,.113,.012,"Metal","Hips")
    surface("Waist gold hook",[(-.014,0,1.012+z),(.018,0,1.007+z),(.020,0,.968+z),(-.006,0,.961+z),(-.014,0,.977+z)],
        (.003,0,.984+z),lambda x,zz:-.132,"Metal","Hips",border=0)
    # Cloth overlaps in two curved wrap panels, with variable hem and diagonal folds.
    for s,side in ((1,"Left"),(-1,"Right")):
        verts=[];faces=[];n=49;rows=15
        for row in range(rows):
            t=row/(rows-1)
            for j in range(n):
                a=math.pi*j/(n-1)
                ripple=.004*t*math.sin(a*7+t*2)
                zz=.963+z-(.205+.026*math.sin(a)+.015*s*math.cos(a))*t
                verts.append((s*((.166+.034*t+ripple)*math.sin(a)+.001),
                    -.004-(.125+.032*t+ripple)*math.cos(a),zz))
        for row in range(rows-1):
            for j in range(n-1):
                k=row*n+j;faces.append((k,k+1,k+n+1,k+n))
        o=A.mesh("Ivory tailored wrap skirt "+side,verts,faces,"Ivory")
        A.select(o);m=o.modifiers.new("Wrap cloth thickness","SOLIDIFY");m.thickness=.004
        bpy.ops.object.modifier_apply(modifier=m.name)
        A.tube("Leather weighted skirt hem "+side,verts[-n:],.0045,"Leather",side+"UpperLeg",depth=.65)
    # Lamellar fan continues around the back, matching the main gameplay view.
    for j in range(18):
        a=j*math.tau/18+.02;length=.119+.023*math.cos(a*3+.2)
        w=.021 if abs(math.sin(a))<.5 else .026
        tangent=Vector((math.cos(a),math.sin(a),0));radial=Vector((math.sin(a),-math.cos(a),0))
        c=Vector((.176*math.sin(a),-.004-.142*math.cos(a),.946+z))
        # Build a pointed bent strip in a local frame, not a flat front rectangle.
        verts=[];faces=[];rows=10
        for row in range(rows):
            t=row/(rows-1);width=w*(1 if t<.75 else (1-t)/.25)
            for k in range(7):
                u=k/6*2-1
                verts.append(c+tangent*(u*width+.018*t*math.sin(a))+
                    radial*(.005+.018*t+.004*(1-u*u))+Vector((0,0,-length*t)))
        for row in range(rows-1):
            for k in range(6):
                i=row*7+k;faces.append((i,i+1,i+8,i+7))
        o=A.mesh("Leather skirt fan lamella "+str(j),verts,faces,"Leather")
        A.select(o);m=o.modifiers.new("Thick skirt leather","SOLIDIFY");m.thickness=.003
        bpy.ops.object.modifier_apply(modifier=m.name)
        # Wide gold tape follows edges in the same coordinates and weights.
        for edge in (0,6):
            path=[Vector(verts[row*7+edge])+radial*.003 for row in range(rows)]
            A.ribbon("Metal skirt lamella edge "+str(j),path,.0027,"Metal",fold=0,normal=radial)
        for k in range(2):
            p=c+radial*.012+Vector((0,0,-.035-k*.019))
            A.ribbon("Metal skirt lamella inset "+str(j),[p-tangent*.014,p+tangent*.014],.002,"Metal",fold=0,normal=radial)
    # Flared shells wrap both hips; backside curve remains visible under drapery.
    for s,side in ((1,"Left"),(-1,"Right")):
        hip=[(s*.074,0,1.019+z),(s*.131,0,1.025+z),(s*.184,0,.994+z),
             (s*.219,0,.902+z),(s*.175,0,.916+z),(s*.117,0,.922+z),(s*.075,0,.948+z)]
        surface("Jade curved hip shell "+side,hip,(s*.144,0,.972+z),
            lambda x,zz:-.158+.27*(abs(x)-.11),bone="Hips",border=.012)
        path=A.catmull([
            (s*.157,-.080,.982+z),(s*.182,.017,.980+z),(s*.146,.105,.984+z),(s*.072,.128,1.001+z)],8)
        verts=[];faces=[]
        for i,p in enumerate(path):
            for row in range(7):
                t=row/6*2-1
                radial=Vector((p.x,p.y,0)).normalized()
                verts.append(p+Vector((0,0,t*.037))+radial*(.004*(1-t*t)+.010*max(0,-t)))
        for i in range(len(path)-1):
            for row in range(6):
                k=i*7+row;faces.append((k,k+1,k+8,k+7))
        back=A.mesh("Jade back hip continuation "+side,verts,faces,"OutfitAccent","Hips")
        A.select(back);m=back.modifiers.new("Hip shell thickness","SOLIDIFY");m.thickness=.004
        bpy.ops.object.modifier_apply(modifier=m.name)
        for row in (0,6):
            A.tube("Hip back gold lip "+side,[verts[i*7+row] for i in range(len(path))],.004,"Metal","Hips",depth=.45)
        upper=Vector(tuple(joints[side+"UpperArm"][0][k]/A.SCALE[k] for k in range(3)))
        elbow=Vector(tuple(joints[side+"LowerArm"][0][k]/A.SCALE[k] for k in range(3)))
        wrist=Vector(tuple(joints[side+"Hand"][0][k]/A.SCALE[k] for k in range(3)))
        if s==1:
            x=upper.x+.021;zz=upper.z
            # One upward sweep, hollow fitted shoulder support and layered lower blade.
            surface("Jade raised wing pauldron",[(x-.049,0,zz+.052),(x-.006,0,zz+.045),
                (x+.052,0,zz+.042),(x+.094,0,zz-.046),(x+.031,0,zz-.072),
                (x-.046,0,zz-.046)],(x+.014,0,zz+.006),
                lambda xx,zz2:-.110+.90*(xx-x)**2+.9*(zz2-zz)**2,
                bone=side+"UpperArm",border=.016)
            # Wrap behind shoulder rather than leaving an open frontal badge.
            verts=[];faces=[];n=48
            for row in range(15):
                phi=.045+1.75*row/14
                for j in range(n):
                    a=j*math.tau/n
                    verts.append((x+.012+.072*math.sin(phi)*math.cos(a),
                        .002+.085*math.sin(phi)*math.sin(a),zz-.030+.077*math.cos(phi)))
            for row in range(14):
                for j in range(n):faces.append((row*n+j,row*n+(j+1)%n,(row+1)*n+(j+1)%n,(row+1)*n+j))
            cap=A.mesh("Jade enveloping shoulder shell",verts,faces,"OutfitAccent",side+"UpperArm")
            A.select(cap);m=cap.modifiers.new("Pauldron fitted shell","SOLIDIFY");m.thickness=.005
            bpy.ops.object.modifier_apply(modifier=m.name)
            A.tube("Pauldron back forged lip",verts[-n:]+[verts[-n]],.005,"Metal",side+"UpperArm",depth=.45)
            surface("Jade pauldron lower leaf",[(x-.035,0,zz-.060),(x+.046,0,zz-.040),
                (x+.093,0,zz-.111),(x+.043,0,zz-.106),(x-.020,0,zz-.099)],
                (x+.03,0,zz-.076),lambda xx,zz2:-.102+.6*(xx-x)**2,
                bone=side+"UpperArm",border=.008)
        c=elbow.lerp(wrist,.53);x,zz=c.x,c.z
        outline=[(x-s*.037,0,zz+.119),(x+s*.009,0,zz+.074),(x+s*.041,0,zz+.089),
            (x+s*.042,0,zz+.019),(x+s*.032,0,zz-.042),(x+s*.013,0,zz-.102),
            (x-s*.031,0,zz-.076),(x-s*.048,0,zz-.014),(x-s*.036,0,zz+.057)]
        fy=lambda xx,zz2:-.085-.025*max(0,1-((xx-x)/.065)**2-((zz2-zz)/.15)**2)
        surface("Jade scrolling vambrace "+side,outline,(x,0,zz),fy,bone=side+"LowerArm",border=.012)
        metal_sweep("Vambrace sculpted spiral "+side,
            [(xx,fy(xx,zz2)-.005,zz2) for xx,zz2 in [(x-s*.018,zz-.058),(x+s*.012,zz-.025),
              (x+s*.014,zz+.024),(x-s*.012,zz+.038),(x-s*.027,zz+.018),(x-s*.014,zz+.004)]],
            .005,side+"LowerArm")
        knee=Vector(tuple(joints[side+"LowerLeg"][0][k]/A.SCALE[k] for k in range(3)))
        foot=Vector(tuple(joints[side+"Foot"][0][k]/A.SCALE[k] for k in range(3)))
        x=knee.x;verts=[];faces=[];n=36
        for row in range(9):
            t=row/8
            for j in range(n):
                a=(-.62+.0)*math.pi+j/(n-1)*1.24*math.pi
                r=.056+.006*math.sin(math.pi*t)
                verts.append((x+r*math.sin(a),.004-r*math.cos(a),knee.z-.022-.090*t-.015*math.sin(a)*s))
        for row in range(8):
            for j in range(n-1):
                i=row*n+j;faces.append((i,i+1,i+n+1,i+n))
        o=A.mesh("Gold curved upper greave "+side,verts,faces,"Metal",side+"LowerLeg")
        o["surfaceVertexCount"]=len(verts);o["surfaceFaceCount"]=len(faces)
        o["legCenterX"]=x*A.SCALE.x
        o["legCenterY"]=.004*A.SCALE.y
        A.select(o);m=o.modifiers.new("Forged greave shell","SOLIDIFY");m.thickness=.004
        bpy.ops.object.modifier_apply(modifier=m.name)
        # Feathers grow from the ankle clasp, close to the foot instead of floating.
        ax=foot.x+s*.041
        for j in range(3):
            surface("Jade ankle feather "+side+str(j),
                [(ax,0,.075),(ax+s*(.018+j*.004),0,.099),
                 (ax+s*(.025+j*.007),0,.171+j*.017),(ax+s*.006,0,.132)],
                (ax+s*.012,0,.119),lambda xx,zz2:-.023,
                bone=side+"Foot",border=.0035,rounds=5)
        ring_band("Ankle round gold clasp "+side,(ax,-.030,.087),(0,-1,0),.013,.013,.006,"Metal",side+"Foot")


def headwear(silhouette):
    z=.047 if silhouette=="b" else 0
    remove(lambda n:n.startswith("Crown"))
    verts=[];faces=[];n=80;rows=20
    for row in range(rows):
        t=row/(rows-1)
        for j in range(n):
            a=j*math.tau/n;front=max(0,math.cos(a))
            bottom=1.589+z+.088*front**2
            top=1.76+z
            zz=bottom+(top-bottom)*t
            r=math.sqrt(max(.12,1-((zz-(1.626+z))/.152)**2))
            verts.append((.120*r*math.sin(a),.006-.124*r*math.cos(a),zz))
    for row in range(rows-1):
        for j in range(n):faces.append((row*n+j,row*n+(j+1)%n,(row+1)*n+(j+1)%n,(row+1)*n+j))
    o=A.mesh("Crown enveloping jade helmet",verts,faces,"OutfitAccent","Head")
    A.select(o);m=o.modifiers.new("Helmet forged thickness","SOLIDIFY");m.thickness=.005
    bpy.ops.object.modifier_apply(modifier=m.name)
    # Continuous lower visor, broad upper V flanges and round temple hinges.
    path=[]
    for j in range(n+1):
        a=j*math.tau/n;f=max(0,math.cos(a));zz=1.589+z+.088*f*f
        r=math.sqrt(max(.12,1-((zz-(1.626+z))/.152)**2))
        path.append((.121*r*math.sin(a),.006-.126*r*math.cos(a),zz))
    A.ribbon("Crown broad lower visor rim",path,.008,"Metal","Head",fold=.001,normal=(0,0,1))
    for s in (-1,1):
        metal_sweep("Crown continuous forged brow wing "+str(s),
            [(s*.115,-.045,1.615+z),(s*.101,-.074,1.665+z),
             (s*.055,-.100,1.704+z),(0,-.098,1.734+z)],.015,"Head")
        ring_band("Crown circular temple hinge",(s*.114,-.040,1.614+z),(s,0,0),.021,.021,.008)
        ring_band("Crown temple inset",(s*.119,-.040,1.614+z),(s,0,0),.012,.012,.010,"Metal")
    for j in range(-2,3):
        A.crystal("Crown ether crystal "+str(j),(j*.036,.005,1.793+z-abs(j)*.023),
            (.013 if j else .017,.013,.118 if j==0 else .074),"Head")


def hair(silhouette):
    z=.046 if silhouette=="b" else 0
    remove(lambda n:n.startswith(("HairMain swept","HairMain side fringe","HairMain temple wave","HairBraid")))
    # Fringe originates under the visor, with broad asymmetric fans and a curled tip.
    path=A.catmull([(-.075,-.118,1.659+z),(-.018,-.143,1.637+z),
        (.058,-.145,1.585+z),(.102,-.110,1.541+z),
        (.118,-.071,1.510+z),(.091,-.082,1.510+z)],9)
    A.tube("HairMain broad S fringe",path,
        lambda t:.048*(.65+.35*math.sin(math.pi*t))*(1-.985*t**4),
        "Hair","Head",depth=.24,sides=48,grooves=.07)
    for s in (-1,1):
        A.tube("HairMain underhelmet temple wave "+str(s),A.catmull([
            (s*.108,-.028,1.613+z),(s*.111,.002,1.571+z),
            (s*.107,.015,1.531+z),(s*.082,-.002,1.523+z)],8),
            lambda t:.021*(1-.96*t**4),"Hair","Head",depth=.55,sides=24,grooves=.12)
    for j in range(5):
        x=(j-2)*.032
        A.tube("HairMain nape sculpted wisp "+str(j),A.catmull([
            (x,.104,1.592+z),(x*.95,.109,1.557+z),(x*.9+.008,.087,1.531+z)],7),
            lambda t:.024*(1-.96*t**3),"Hair","Head",depth=.45,sides=20,grooves=.08)
    control=[(0,.065,1.755+z),(-.070,.156,1.901+z),(-.196,.228,1.858+z),
        (-.276,.277,1.667+z),(-.286,.285,1.452+z),(-.267,.275,1.240+z),
        (-.272,.250,1.060+z),(-.340,.213,.983+z),(-.385,.170,1.075+z)]
    for i in range(len(control)-2):
        p=Vector(control[i]);q=Vector(control[i+1]);r=(.079,.087,.079,.070,.063,.053,.043,.029)[i]
        path=A.catmull([p,p.lerp(q,.30)+Vector((-.011,-.006,.010)),p.lerp(q,.72)+Vector((.006,.004,-.006)),q],8)
        A.tube("HairBraid sculpted directional lobe "+str(i),path,
            lambda t,r=r:r*(.29+.71*math.sin(math.pi*A.riglib.clamp((t-.085)/.83))**.70),
            "Hair",depth=.82,sides=40,grooves=.15)
        if i<7:
            axis=(q-p).normalized()
            if i>0:axis=((p-Vector(control[i-1])).normalized()+axis).normalized()
            ring_band("HairBraid flat forged cuff "+str(i),p,axis,r*.36,r*.31,.026,"Metal",None)
    A.tube("HairBraid terminal curling lock",A.catmull([control[-2],(-.391,.183,1.001+z),
        (-.404,.160,1.079+z),(-.364,.159,1.125+z),(-.347,.163,1.090+z)],8),
        lambda t:.031*(1-.985*t**1.4),"Hair",depth=.68,sides=32,grooves=.10)


def refine_face(api,silhouette,objects):
    global A
    A=api
    sx,sy,sz=A.SCALE
    ez=1.578 if silhouette=="b" else 1.549
    ex=.046 if silhouette=="b" else .037
    for o in objects:
        name=o.name.lower()
        if o.name not in ("Head","Eyes") and not any(s in name for s in ("brow","lash")):continue
        original=[v.co.copy() for v in o.data.vertices]
        iris=set()
        if o.name=="Eyes":
            for poly in o.data.polygons:
                if o.data.materials[poly.material_index].name in ("Iris","Pupil"):iris.update(poly.vertices)
        for v,old in zip(o.data.vertices,original):
            x,y,z=old.x/sx,old.y/sy,old.z/sz;side=1 if x>=0 else -1
            dx=abs(x)-ex;dz=z-ez
            front=A.riglib.clamp((-y-.035)/.055)
            eye=math.exp(-(dx/.048)**4-(dz/.042)**4)*front
            if o.name=="Eyes":eye=1
            x=side*(ex+dx*(1-.09*eye)-.003*eye)
            z=ez+dz*(1-(.23 if o.name=="Eyes" else .36)*eye)+dx*.21*eye
            if v.index in iris:
                x=side*(ex-.003+(abs(x)-(ex-.003))*.82)
                z=ez+(z-ez)*.82
            if o.name=="Head":
                # Narrow nose bridge and nostril wings, bring the soft muzzle back.
                nose=math.exp(-(x/.029)**4-((z-(ez-.044))/.027)**4)*front
                x*=1-.30*nose;y+=.009*nose
                mouth=math.exp(-(x/.045)**4-((z-(ez-.079))/.022)**4)*front
                y+=.011*mouth
                z=(ez-.079)+(z-(ez-.079))*(1-.24*mouth)
                lower=math.exp(-((z-(ez-.117))/.060)**4)*front
                z-=.010*lower
                # A longer defined jaw and cheek plane replace the round cheek ball.
                jaw=math.exp(-((abs(x)-.060)/.035)**2-((z-(ez-.122))/.030)**2)*front
                x-=side*.003*jaw;y+=.004*jaw
                cheek=math.exp(-((abs(x)-.068)/.028)**2-((z-(ez-.047))/.032)**2)*front
                y+=.004*cheek
                if sx==1 and abs(x)>.095:
                    ear=A.riglib.clamp((abs(x)-.095)/.025)
                    x-=side*.017*ear;z=(ez-.02)+(z-(ez-.02))*(1-.14*ear)
            if "brow" in name:
                t=A.riglib.clamp((abs(x)-.013)/.079)
                nearby=[p.z/sz for p in original if abs(abs(p.x/sx)-abs(old.x/sx))<.003]
                base=(min(nearby)+max(nearby))*.5
                angular=t/.58 if t<.58 else (1-t)/.42
                taper=.87*(1-.90*t*t)
                z=ez+.016+.010*angular-.007*t+(old.z/sz-base)*taper
                nearby_y=[p.y/sy for p in original if abs(abs(p.x/sx)-abs(old.x/sx))<.003]
                y_mid=(min(nearby_y)+max(nearby_y))*.5
                y=y_mid+(y-y_mid)*max(.15,1-.8*t*t)
            v.co=(x*sx,y*sy,z*sz)
        o.data.update()
        if "brow" in name:
            painted=o.data.color_attributes.get("Painted")
            if painted:
                for c in painted.data:c.color=(.065,.052,.052,1)


def rebuild(api,silhouette,joints):
    global A
    A=api
    costume(silhouette,joints)
    headwear(silhouette)
    hair(silhouette)


def fit_drapery(api,objects):
    """Place fabric outside the actual layered costume, on both sides of the torso."""
    sx,sy,sz=api.SCALE
    support=[o for o in objects if o.name in ("Body","Ether Guard-shirt") or o.name.startswith((
        "Jade fitted crescent","Cuirass forged spiral","Leather underbust",
        "Jade raised wing pauldron"))]
    verts=[];polys=[]
    for obj in support:
        start=len(verts);verts.extend(v.co.copy() for v in obj.data.vertices)
        polys.extend(tuple(start+i for i in p.vertices) for p in obj.data.polygons)
    tree=BVHTree.FromPolygons(verts,polys)
    for obj in objects:
        if obj.name.startswith(TORSO_PARTS):obj["rigWeightMode"]="torso"
        front=obj.name.startswith(("Ivory flowing diagonal","Woven dark drape","Drape embroidered"))
        back=obj.name.startswith("Ivory continuous shoulder back")
        if not front and not back:continue
        vertex_count=obj.get("ribbonVertexCount")
        face_count=obj.get("ribbonFaceCount")
        if vertex_count:
            coordinates=[v.co.copy() for v in list(obj.data.vertices)[:vertex_count]]
            faces=[tuple(p.vertices) for p in list(obj.data.polygons)[:face_count]]
        else:
            coordinates=[v.co.copy() for v in obj.data.vertices]
            faces=None
        directions=[]
        for p in coordinates:
            direction=Vector((0,1 if front else -1,0))
            origin=Vector((p.x,-1 if front else 1,p.z))
            hit,_,_,_=tree.ray_cast(origin,direction,2)
            gap=(.012 if "Ivory" in obj.name else .020 if "Woven" in obj.name else .024)*sy
            delta=0 if hit is None else (hit.y-gap-p.y if front else hit.y+gap-p.y)
            # Keep shoulder folds above the chest; fit the lower drape to its support.
            fit=api.riglib.smooth((1.40-p.z/sz)/.09)
            if front:delta=min(0,delta)+max(0,delta)*fit
            else:delta=max(0,delta)+min(0,delta)*fit
            directions.append(delta)
        if vertex_count:
            neighbors=[set() for _ in coordinates]
            for face in faces:
                for i in face:neighbors[i].update(face)
            targets=[p.y+d for p,d in zip(coordinates,directions)]
            for _ in range(18):
                blend=[sum(targets[j] for j in neighbors[i])/len(neighbors[i]) if neighbors[i] else d for i,d in enumerate(targets)]
                targets=[min(a,b) if front else max(a,b) for a,b in zip(targets,blend)]
            directions=[target-p.y for target,p in zip(targets,coordinates)]
            for p,d in zip(coordinates,directions):p.y+=d
            obj.data.clear_geometry();obj.data.from_pydata(coordinates,[],faces);obj.data.update()
            bind_z=obj.data.attributes.new("TorsoBindZ","FLOAT","POINT")
            for item,p in zip(bind_z.data,coordinates):item.value=p.z
            for poly in obj.data.polygons:poly.use_smooth=True
            api.select(obj);m=obj.modifiers.new("Rebuilt drapery thickness","SOLIDIFY");m.thickness=.003*sy
            bpy.ops.object.modifier_apply(modifier=m.name)
            bind_z=obj.data.attributes.get("TorsoBindZ")
            for i,item in enumerate(bind_z.data):item.value=coordinates[i%vertex_count].z
            minimum=min((obj.data.vertices[i].co-obj.data.vertices[i+vertex_count].co).length for i in range(vertex_count))
            assert minimum>.001*sy,(obj.name,"Collapsed drapery thickness",minimum)
            obj["draperyMinimumThicknessMeters"]=minimum
        else:
            # Translate complete circular sections; never collapse a closed tube.
            sides=obj.get("tubeSides",1)
            for start in range(0,len(coordinates),sides):
                values=directions[start:start+sides]
                delta=min(values) if front else max(values)
                for i in range(start,min(start+sides,len(coordinates))):obj.data.vertices[i].co.y+=delta
        obj.data.update()
