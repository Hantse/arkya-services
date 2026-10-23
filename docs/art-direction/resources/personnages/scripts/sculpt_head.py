"""Adult facial planes and individually shaped hair locks for the reference pass."""
import math
import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def bell(x, z, cx, cz, rx, rz):
    return math.exp(-((x-cx)/rx)**2-((z-cz)/rz)**2)


def refine_face(api, silhouette, objects):
    sx,sy,sz=api.SCALE
    ez=1.578 if silhouette=="b" else 1.549
    ex=.0456 if silhouette=="b" else .037
    for obj in list(objects):
        if "eyebrow" in obj.name.lower():
            objects.remove(obj);bpy.data.objects.remove(obj,do_unlink=True)
    for obj in objects:
        name=obj.name.lower()
        if obj.name not in ("Head","Eyes") and "lash" not in name:continue
        original=[v.co.copy() for v in obj.data.vertices]
        iris=set();pupil=set()
        if obj.name=="Eyes":
            for poly in obj.data.polygons:
                mat=obj.data.materials[poly.material_index].name
                if mat=="Iris":iris.update(poly.vertices)
                if mat=="Pupil":pupil.update(poly.vertices)
        for v,old in zip(obj.data.vertices,original):
            x,y,z=old.x/sx,old.y/sy,old.z/sz
            side=1 if x>=0 else -1;ax=abs(x);dx=ax-ex;dz=z-ez
            front=api.riglib.smooth(api.riglib.clamp((-y-.018)/.065))
            eye=math.exp(-(dx/.046)**4-(dz/.037)**4)*front
            if obj.name=="Eyes":
                x=side*(ex-.001+dx*.85)
                z=ez+dz*.72+dx*.16
                y=-.087+(y+.087)*.94+.002
                if v.index in iris or v.index in pupil:
                    factor=.60 if silhouette=="b" else .67
                    x=side*(ex-.001+dx*factor)
                    z=ez+dz*factor+.0015
                v.co=(x*sx,y*sy,z*sz)
                continue
            # The lids, rather than a flattened iris, create the almond opening.
            x=side*(ex+dx*(1-.15*eye)-.001*eye)
            z=ez+dz*(1-.52*eye)+dx*.20*eye
            y+=.0035*eye
            if obj.name=="Head":
                lower=api.riglib.smooth(api.riglib.clamp((ez-z)/.045))
                neckfade=api.riglib.smooth(api.riglib.clamp((z-(ez-.190))/.045))
                z-=.012*lower*neckfade*front
                # High malar plane, recessed lower cheek and a deliberate jaw angle.
                cheek=bell(ax,z,.066,ez-.073,.043,.047)*front
                x-=side*.012*cheek;y+=.014*cheek
                malar=bell(ax,z,.074,ez-.030,.031,.020)*front
                x+=side*.003*malar;y-=.0045*malar
                jaw=bell(ax,z,.064,ez-.140,.032,.027)*front
                x+=side*.0035*jaw;y+=.004*jaw
                chin=bell(x,z,0,ez-.150,.033,.023)*front
                z-=.006*chin;y-=.004*chin
                underlip=bell(x,z,0,ez-.120,.028,.010)*front
                y+=.005*underlip
                # Lengthen the bridge, narrow the alae, and remove the round upturn.
                nose=bell(x,z,0,ez-.054,.026,.030)*front
                x*=1-.26*nose
                y+=.008*nose;z-=.003*nose
                tip=bell(x,z,0,ez-.061,.018,.014)*front
                y+=.002*tip;z-=.003*tip
                bridge=bell(x,z,0,ez-.027,.013,.026)*front
                y-=.0035*bridge
                mouth=bell(x,z,0,ez-.095,.043,.019)*front
                y+=.012*mouth
                # Retain the source lip seam, reduce the inflated lip roll.
                z=(ez-.095)+(z-(ez-.095))*(1-.26*mouth)
                corner=bell(ax,z,.030,ez-.096,.014,.011)*front
                z+=.0025*corner
                orbit=bell(ax,z,ex,ez+.013,.037,.013)*front
                y+=.003*orbit
                browridge=bell(ax,z,.052,ez+.033,.040,.015)*front
                y-=.006*browridge
                if sx==1 and ax>.095:
                    ear=api.riglib.clamp((ax-.095)/.025)
                    x-=side*.016*ear;z=(ez-.02)+(z-(ez-.02))*(1-.14*ear)
            v.co=(x*sx,y*sy,z*sz)
        obj.data.update()
        if obj.name=="Head":paint_skin(obj,original,api,ez)
    rebuild_eyes(api,objects,ez,ex,silhouette)
    return restore_cranium(api,objects,silhouette)


def restore_cranium(api,objects,silhouette):
    """Close the historical Snow scalp opening without moving the approved face."""
    if silhouette!="a":return {"required":False}
    head=next(o for o in objects if o.name=="Head")
    original=[v.co.copy() for v in head.data.vertices]
    bm=bmesh.new();bm.from_mesh(head.data)
    edges={e for e in bm.edges if e.is_boundary};components=[]
    while edges:
        seed=edges.pop();component={seed};stack=[seed]
        while stack:
            edge=stack.pop()
            for vertex in edge.verts:
                for other in vertex.link_edges:
                    if other in edges:edges.remove(other);component.add(other);stack.append(other)
        vertices={v for e in component for v in e.verts}
        if min(v.co.z/api.SCALE.z for v in vertices)>1.45 and max(v.co.z/api.SCALE.z for v in vertices)>1.62:
            components.append(component)
    assert len(components)==1,("Expected one historical scalp opening",len(components))
    component=components[0];edge=next(iter(component));source_loop=edge.link_loops[0]
    first=source_loop.vert;current=source_loop.link_loop_next.vert
    ordered=[first,current];previous=edge
    while current!=first:
        next_edge=next(e for e in current.link_edges if e in component and e!=previous)
        vertex=next_edge.other_vert(current)
        if vertex==first:break
        ordered.append(vertex);previous=next_edge;current=vertex
    assert len(ordered)==len(component)
    paint=bm.loops.layers.float_color.get("Painted")
    assert paint is not None
    colors=[]
    for vertex in ordered:
        loops=list(vertex.link_loops)
        colors.append(sum((loop[paint] for loop in loops),Vector((0,0,0,0)))/len(loops))
    average=sum(colors,Vector((0,0,0,0)))/len(colors)
    group=head.vertex_groups.get("DEF-ArkyaCraniumHead") or head.vertex_groups.new(name="DEF-ArkyaCraniumHead")
    weights=bm.verts.layers.deform.verify()
    center=Vector((0,.094,1.602))*api.SCALE
    rings=[ordered];new_faces=[];vertex_colors={}
    for row in range(1,9):
        t=row/9;ring=[]
        for vertex,color in zip(ordered,colors):
            p=vertex.co.lerp(center,t)
            p.y+=.018*api.SCALE.y*math.sin(math.pi*t)
            v=bm.verts.new(p);v[weights][group.index]=1.;ring.append(v)
            vertex_colors[v]=color.lerp(average,t)
        rings.append(ring)
    for outer,inner in zip(rings,rings[1:]):
        for j in range(len(ordered)):
            k=(j+1)%len(ordered)
            new_faces.append(bm.faces.new((outer[k],outer[j],inner[j],inner[k])))
    tip=bm.verts.new(center);tip[weights][group.index]=1.;vertex_colors[tip]=average
    for j in range(len(ordered)):
        k=(j+1)%len(ordered)
        new_faces.append(bm.faces.new((rings[-1][k],rings[-1][j],tip)))
    for vertex,color in zip(ordered,colors):vertex_colors[vertex]=color
    for face in new_faces:
        face.smooth=True;face.material_index=0
        for loop in face.loops:loop[paint]=vertex_colors[loop.vert]
    assert not any(e.is_boundary for e in component)
    bm.normal_update();bm.to_mesh(head.data);bm.free();head.data.update()
    assert all(head.data.vertices[i].co==co for i,co in enumerate(original))
    return {"required":True,"closedScalpBoundaryEdges":len(ordered),
            "addedVertices":len(head.data.vertices)-len(original),"existingVertexCoordinatesUnchanged":True}


def rebuild_eyes(api,objects,ez,ex,silhouette):
    """Continuous sclera with iris and pupil on its visible surface, without holes."""
    old=next(o for o in objects if o.name=="Eyes")
    objects.remove(old);bpy.data.objects.remove(old,do_unlink=True)
    verts=[];faces=[];materials=[];colors=[]
    n=48;rows=24
    rx=.0315 if silhouette=="b" else .030
    ry=.0325 if silhouette=="b" else .0245
    rz=.0265 if silhouette=="b" else .0225
    for side in (-1,1):
        cx=side*(ex-.001);cy=-.085 if silhouette=="b" else -.083
        start=len(verts);verts.append((cx,cy-ry,ez));colors.append((.94,.95,.94,1))
        for row in range(1,rows):
            phi=math.pi*row/rows
            for j in range(n):
                a=math.tau*j/n
                verts.append((cx+rx*math.sin(phi)*math.cos(a),cy-ry*math.cos(phi),ez+rz*math.sin(phi)*math.sin(a)))
                colors.append((.94,.95,.94,1))
        end=len(verts);verts.append((cx,cy+ry,ez));colors.append((.94,.95,.94,1))
        for j in range(n):faces.append((start,start+1+j,start+1+(j+1)%n));materials.append(0)
        for row in range(rows-2):
            k=start+1+row*n
            for j in range(n):faces.append((k+j,k+n+j,k+n+(j+1)%n,k+(j+1)%n));materials.append(0)
        k=start+1+(rows-2)*n
        for j in range(n):faces.append((k+j,end,k+(j+1)%n));materials.append(0)
        for mat,radius,offset in ((1,.0108 if silhouette=="b" else .0102,.0007),(2,.0048,.0014)):
            start=len(verts)
            def point(u,v):
                dz=v+.0015
                yy=cy-ry*math.sqrt(max(.05,1-(u/rx)**2-(dz/rz)**2))-offset
                return (cx+u,yy,ez+dz)
            verts.append(point(0,0));colors.append((.72,.72,.72,1) if mat==1 else (1,1,1,1))
            ring_count=7
            for row in range(1,ring_count+1):
                r=radius*row/ring_count
                for j in range(n):
                    a=math.tau*j/n;verts.append(point(r*math.cos(a),r*math.sin(a)))
                    value=(.64+.20*math.sin(a*17+.4)*math.sin(a*7)+.15*math.sin(math.pi*row/ring_count)) if mat==1 else 1
                    if mat==1 and row==ring_count:value*=.55
                    colors.append((value,value,value,1))
            for j in range(n):faces.append((start,start+1+j,start+1+(j+1)%n));materials.append(mat)
            for row in range(ring_count-1):
                k=start+1+row*n
                for j in range(n):faces.append((k+j,k+n+j,k+n+(j+1)%n,k+(j+1)%n));materials.append(mat)
    obj=api.mesh("Eyes",verts,faces,"EyesWhite","Head")
    obj.data.materials.append(api.M["Iris"]);obj.data.materials.append(api.M["Pupil"])
    for poly,index in zip(obj.data.polygons,materials):poly.material_index=index
    attr=obj.data.color_attributes.new(name="Painted",type="FLOAT_COLOR",domain="POINT")
    for item,color in zip(attr.data,colors):item.color=color
    obj["paintedSculpt"]=True
    api.PARTS.remove(obj);objects.append(obj)


def finish_lids(api,objects,silhouette):
    """Cover Snow's coarse eye-hole edge with a continuous anatomical lid rim."""
    if silhouette!="a":return
    head=next(o for o in objects if o.name=="Head")
    tree=BVHTree.FromPolygons([v.co for v in head.data.vertices],[tuple(p.vertices) for p in head.data.polygons])
    source=head.data.color_attributes.get("Painted")
    n=64;rows=6
    for side in (-1,1):
        verts=[];faces=[];colors=[]
        cx=side*.036;cz=1.5475
        for row in range(rows):
            t=row/(rows-1)
            for j in range(n):
                a=math.tau*j/n;dx=(.0245+.010*t)*math.cos(a)
                dz=(.008+.010*t)*math.sin(a)*(.72+.28*abs(math.sin(a)))+side*dx*.15
                x=cx+dx;z=cz+dz
                eye_y=-.083-.0245*math.sqrt(max(.04,1-(dx/.030)**2-((z-1.549)/.0225)**2))-.0018
                hit,_,face,_=tree.ray_cast(Vector((x*api.SCALE.x,-1,z*api.SCALE.z)),Vector((0,1,0)),2)
                skin_y=hit.y/api.SCALE.y-.0008 if hit is not None else eye_y
                y=eye_y*(1-t)+skin_y*t-.0015*math.sin(math.pi*t)
                verts.append((x,y,z))
                nearest,_,index,_=tree.find_nearest(Vector((x*api.SCALE.x,y*api.SCALE.y,z*api.SCALE.z)))
                if source and index is not None:
                    poly=head.data.polygons[index]
                    indices=poly.vertices if source.domain=="POINT" else poly.loop_indices
                    c=[sum(source.data[k].color[channel] for k in indices)/len(indices) for channel in range(3)]
                else:c=(.75,.68,.62)
                colors.append((*c,1))
        for row in range(rows-1):
            for j in range(n):
                k=row*n+j;other=row*n+(j+1)%n
                faces.append((k,k+n,other+n,other))
        obj=api.mesh("Hero continuous eyelid rim "+str(side),verts,faces,"Skin","Head")
        assert all(poly.normal.y<0 for poly in obj.data.polygons)
        obj["reforgePart"]=False
        attr=obj.data.color_attributes.new(name="Painted",type="FLOAT_COLOR",domain="POINT")
        for item,c in zip(attr.data,colors):item.color=c
        obj["paintedSculpt"]=True


def paint_skin(obj, original, api, ez):
    attr=obj.data.color_attributes.get("Painted")
    if not attr:return
    shades=[]
    for old in original:
        x,y,z=(old[k]/api.SCALE[k] for k in range(3));ax=abs(x)
        front=api.riglib.smooth(api.riglib.clamp((-y-.025)/.060))
        lips=bell(x,z,0,ez-.079,.031,.012)*front
        cheek=bell(ax,z,.065,ez-.044,.031,.021)*front
        nose=bell(x,z,0,ez-.045,.020,.021)*front
        socket=bell(ax,z,.047,ez+.017,.033,.012)*front
        hollow=bell(ax,z,.078,ez-.083,.029,.027)*front
        warm=.85*lips+.22*cheek+.18*nose
        shade=1-.16*lips-.13*socket-.08*hollow
        shades.append((shade*(1+.06*warm),shade*(1-.17*warm),shade*(1-.16*warm)+.025*hollow))
    for index,item in enumerate(attr.data):
        vertex=index if attr.domain=="POINT" else obj.data.loops[index].vertex_index
        tint=shades[vertex];old=item.color
        item.color=(*(max(.03,min(1,old[k]*tint[k])) for k in range(3)),1)


def add_brows(api,objects,ez):
    head=next(o for o in objects if o.name=="Head")
    tree=BVHTree.FromPolygons([v.co for v in head.data.vertices],[tuple(p.vertices) for p in head.data.polygons])
    for side in (-1,1):
        path=[]
        for i in range(25):
            t=i/24;x=side*(.017+.075*t)
            angle=t/.62 if t<.62 else (1-t)/.38
            z=ez+.018+.014*angle-.002*t
            hit,_,_,_=tree.ray_cast(Vector((x*api.SCALE.x,-1,z*api.SCALE.z)),Vector((0,1,0)),2)
            y=hit.y/api.SCALE.y-.003 if hit is not None else -.12
            path.append((x,y,z))
        obj=api.tube("Hero angular eyebrow "+str(side),path,
            lambda t:.0047*(1-.95*t**2.2),"Hair","Head",depth=.30,sides=12)
        attr=obj.data.color_attributes.new(name="Painted",type="FLOAT_COLOR",domain="POINT")
        for item in attr.data:item.color=(.095,.083,.080,1)
        obj["paintedSculpt"]=True


def lock(api,name,path,radius,depth=.8,phase=0,twist=.7,bone=None,sections=48):
    """Asymmetric broad tresses, with irregular channels carried by the mesh."""
    pts=[Vector(p) for p in path];verts=[];faces=[];colors=[]
    channels=(.20,1.27,2.55,3.61,5.08)
    for i,p in enumerate(pts):
        t=i/(len(pts)-1)
        tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
        right=tangent.cross(Vector((0,1,0)))
        if right.length<.01:right=tangent.cross(Vector((1,0,0)))
        right.normalize();other=tangent.cross(right).normalized()
        r=radius(t) if callable(radius) else radius
        for j in range(sections):
            a=math.tau*j/sections;theta=a+phase+twist*(t-.5)
            shape=1+.10*math.cos(theta*3+.7)+.065*math.sin(theta*2-1.3)
            trough=0
            for k,channel in enumerate(channels):
                delta=math.atan2(math.sin(theta-channel),math.cos(theta-channel))
                trough+=math.exp(-(delta/(.12+.035*(k%2)))**2)*(.10+.025*(k%3))
            shape-=trough
            shape*=1+.045*math.sin(math.pi*t)*math.cos(theta-t*2.3)
            verts.append(p+right*(r*math.cos(a)*shape)+other*(r*math.sin(a)*depth*shape))
            sheen=max(0,math.cos(theta-.3))**5
            value=.34+.19*(1-math.cos(theta)*.5)+.12*sheen-.62*trough
            value*=.72+.28*math.sin(math.pi*t)**.45
            colors.append((value*1.04,value*.96,value*.97,1))
    for i in range(len(pts)-1):
        for j in range(sections):
            k=i*sections+j;n=i*sections+(j+1)%sections
            faces.append((k,n,n+sections,k+sections))
    faces.extend((tuple(range(sections-1,-1,-1)),tuple((len(pts)-1)*sections+j for j in range(sections))))
    obj=api.mesh(name,verts,faces,"Hair",bone)
    attr=obj.data.color_attributes.new(name="Painted",type="FLOAT_COLOR",domain="POINT")
    for item,color in zip(attr.data,colors):item.color=color
    obj["paintedSculpt"]=True
    return obj


def hair(api,silhouette,ring_band):
    z=.046 if silhouette=="b" else 0
    for obj in list(api.PARTS):
        if obj.name.startswith(("HairMain swept","HairMain side fringe","HairMain temple wave",
                                "HairMain broad","HairMain underhelmet","HairMain nape","HairBraid")):
            api.PARTS.remove(obj);bpy.data.objects.remove(obj,do_unlink=True)
    paths=[
        ([(-.078,-.114,1.670+z),(-.025,-.142,1.642+z),(.047,-.145,1.604+z),
          (.103,-.109,1.568+z),(.125,-.065,1.529+z),(.108,-.051,1.511+z)],.030,.34),
        ([(-.047,-.130,1.663+z),(-.001,-.150,1.631+z),(.054,-.147,1.603+z),
          (.094,-.126,1.581+z),(.112,-.101,1.553+z)],.020,.35),
        ([(-.074,-.113,1.661+z),(-.099,-.103,1.623+z),(-.103,-.076,1.579+z),
          (-.090,-.052,1.548+z),(-.082,-.048,1.555+z)],.022,.55)]
    for i,(points,width,depth) in enumerate(paths):
        lock(api,"HairMain parted sculpted lock "+str(i),api.catmull(points,10),
             lambda t,w=width:w*(.58+.42*math.sin(math.pi*t)**.5)*(1-.985*t**4),
             depth,phase=i*.8,twist=.45,bone="Head",sections=40)
    for j in range(5):
        x=(j-2)*.032
        lock(api,"HairMain nape adult lock "+str(j),api.catmull([
            (x,.104,1.592+z),(x*.95,.109,1.557+z),(x*.9+.008,.087,1.531+z)],7),
             lambda t:.022*(1-.98*t**2),.45,phase=j*.4,bone="Head",sections=24)
    control=[(0,.065,1.755+z),(-.071,.163,1.901+z),(-.199,.226,1.850+z),
        (-.271,.286,1.667+z),(-.287,.279,1.452+z),(-.261,.283,1.240+z),
        (-.279,.243,1.060+z),(-.340,.213,.983+z)]
    radii=(.075,.085,.079,.068,.060,.052,.043)
    for i in range(len(control)-1):
        p=Vector(control[i]);q=Vector(control[i+1]);r=radii[i]
        axis=(q-p).normalized()
        if i:axis=((p-Vector(control[i-1])).normalized()+axis).normalized()
        end_axis=(q-p).normalized()
        if i+2<len(control):end_axis=(end_axis+(Vector(control[i+2])-q).normalized()).normalized()
        distance=(q-p).length
        h0=p+axis*distance*.36;h1=q-end_axis*distance*.36
        path=[]
        for row in range(41):
            t=row/40
            path.append(p*(1-t)**3+h0*3*t*(1-t)**2+h1*3*t*t*(1-t)+q*t**3)
        def profile(t,r=r,i=i):
            u=api.riglib.clamp((t-.075)/.85)
            return r*(.29+.71*math.sin(math.pi*u)**(.58+.10*(i%3)))*(1+.05*math.sin(math.tau*t+i))
        lock(api,"HairBraid organic sculpted lobe "+str(i),path,profile,
             depth=.78+.04*(i%3),phase=i*.71,twist=(-1 if i%2 else 1)*.80)
        ring_band("HairBraid flat forged cuff "+str(i),p,axis,r*.325,r*.275,.030,"Metal",None)
    lock(api,"HairBraid sculpted curling end",api.catmull([control[-1],(-.390,.180,1.007+z),
        (-.402,.156,1.082+z),(-.363,.156,1.127+z),(-.346,.162,1.091+z)],10),
        lambda t:.031*(1-.985*t**1.25),.67,phase=1.7,twist=.9,sections=40)
