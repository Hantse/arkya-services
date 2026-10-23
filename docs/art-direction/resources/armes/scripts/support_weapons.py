"""Sculpted shields, staves and bows matching the approved Arkya weapon boards.

Blender coordinates are metres, +Z up, front -Y. Each build places the centre
of the primary grip at the origin and creates geometry only.
"""
import math

import weapon_common as w


def _closed_curve(points, steps=4):
    result = []
    n = len(points)
    for i in range(n):
        a, b, c, d = [points[j % n] for j in (i - 1, i, i + 1, i + 2)]
        for j in range(steps):
            t = j / steps
            result.append(tuple(.5 * (2*b[k] + (-a[k]+c[k])*t
                + (2*a[k]-5*b[k]+4*c[k]-d[k])*t*t
                + (-a[k]+3*b[k]-3*c[k]+d[k])*t*t*t) for k in range(2)))
    return result


def _line(name, points, radius, kind, steps=6, sides=8):
    path = w.curve(points, steps=steps)
    return w.tube(name, path, radius, kind, sides=sides)


def _ellipse_tube(name, points, radii, kind, sides=10):
    return w.tube(name, points, [radii]*len(points), kind, sides=sides)


def _tapered_jade_inlay(name, points, width, depth):
    radii=[]
    for i in range(len(points)):
        fullness=math.sin(math.pi*i/(len(points)-1))**.65
        radii.append((.001+width*fullness,.0015+depth*fullness))
    backing=[(x,y+.004,z) for x,y,z in points]
    w.tube(name+'_Gold_Border',backing,[(rx+.002,ry*.82) for rx,ry in radii],'gold',sides=10)
    return w.tube(name,points,radii,'jade',sides=10)


def _shield_face(name, outline, kind):
    """Closed convex radial shell with a subtly curved back and thicker edge."""
    vertices, faces = [], []
    n = len(outline)
    radii = (.18, .42, .67, .86, 1.0)
    for front in (True, False):
        for r in radii:
            y = -.087 - .072*(1-r*r) if front else -.045 - .022*(1-r*r)
            vertices.extend((x*r, y, z*r) for x, z in outline)
    layers = len(radii)
    for offset in (0, layers*n):
        for row in range(layers-1):
            for j in range(n):
                a = offset+row*n+j
                b = offset+row*n+(j+1)%n
                faces.append((a, b, b+n, a+n))
    front_center = len(vertices)
    vertices.append((0, -.159, 0))
    back_center = len(vertices)
    vertices.append((0, -.067, 0))
    for j in range(n):
        nxt = (j+1)%n
        faces.append((front_center, j, nxt))
        faces.append((back_center, layers*n+nxt, layers*n+j))
        a, b = (layers-1)*n+j, (layers-1)*n+nxt
        faces.append((a, a+layers*n, b+layers*n, b))
    return w.mesh(name, vertices, faces, kind, smooth=True)


def _shield_rim(name, outline, width=.026):
    # A closed four-sided band follows the outline; its bevel shapes both edges.
    maximum = max(math.hypot(x, z) for x, z in outline)
    inner = 1 - width/maximum
    vertices = []
    for scale, y in ((1, -.098), (inner, -.108), (inner, -.044), (1, -.041)):
        vertices.extend((x*scale, y, z*scale) for x, z in outline)
    n = len(outline)
    faces = []
    for r in range(4):
        for j in range(n):
            faces.append((r*n+j, r*n+(j+1)%n, ((r+1)%4)*n+(j+1)%n, ((r+1)%4)*n+j))
    return w.mesh(name, vertices, faces, 'gold', bevel=.003, smooth=True)


def _shield_back(code, width):
    # The physical handle is centred on the root, with clearance to the back.
    w.grip(code+'_Rear_Handgrip', -.062, .062, radius=.016)
    for z in (-.062, .062):
        _line(code+'_Grip_Bracket', [(0, 0, z), (0, -.020, z*1.35), (0, -.056, z*1.55)], .010, 'gold', sides=10)
        w.sphere(code+'_Back_Rivet', (0, -.048, z*1.55), (.016, .009, .016), 'gold', 12, 8)
    # A broad leather forearm strap remains open and raised away from the shell.
    for x in (-width*.19, width*.19):
        path = [(x, -.051, -.105), (x, .018, -.065), (x, .035, 0), (x, .018, .065), (x, -.051, .105)]
        _ellipse_tube(code+'_Rear_Arm_Strap', w.curve(path, steps=5), (.016, .006), 'leather', sides=8)
    w.tube(code+'_Back_Upper_Brace', [(-width*.29, -.055, .15), (width*.29, -.055, .15)], .012, 'wood', sides=8)
    w.tube(code+'_Back_Lower_Brace', [(-width*.25, -.055, -.17), (width*.25, -.055, -.17)], .012, 'wood', sides=8)


def _bezel(name, x, z, width, height, y=-.170, kind='crystal'):
    w.gem(name+'_Gold_Setting', (x, y+.003, z), width*1.37, height*1.20, .028, 'gold')
    w.gem(name+'_Inset', (x, y-.019, z), width, height, .035, kind)


def _shield(code):
    variant = code[-1]
    if variant == 'A':
        outline = _closed_curve([(0,-.405),(-.145,-.29),(-.235,-.09),(-.255,.19),
            (-.247,.348),(-.105,.323),(0,.405),(.105,.323),(.247,.348),(.255,.19),(.235,-.09),(.145,-.29)], 4)
        _shield_face(code+'_Domed_Steel_Body', outline, 'steel')
        _shield_rim(code+'_Forged_Perimeter', outline, .032)
        _line(code+'_Central_Gold_Spine', [(0,-.108,-.385),(0,-.155,-.23),(0,-.174,0),(0,-.145,.25),(0,-.108,.39)], .014, 'gold', sides=10)
        for sign in (-1,1):
            _line(code+'_Steel_Rib', [(sign*.028,-.133,-.34),(sign*.092,-.151,-.18),(sign*.118,-.167,.075),(sign*.20,-.125,.22),(sign*.19,-.112,.30)], .0045, 'edge')
            w.profile(code+'_Ivory_Shoulder', [(sign*.214,.32),(sign*.15,.302),(sign*.17,.266),(sign*.221,.275)], .010, 'ivory', y=-.114, bevel=.003)
            w.profile(code+'_Ivory_Lower_Inlay', [(sign*.09,-.31),(sign*.16,-.247),(sign*.146,-.23),(sign*.078,-.28)], .010, 'ivory', y=-.111, bevel=.003)
            for x,z in ((.218,.31),(.222,-.07),(.133,-.287)):
                w.sphere(code+'_Perimeter_Rivet', (sign*x,-.110,z), (.010,.007,.010), 'gold', 12, 6)
        _bezel(code+'_Central_Crystal',0,.035,.072,.146)
        _bezel(code+'_Foot_Crystal',0,-.337,.027,.056,-.113)
        w.gem(code+'_Crown', (0,-.112,.363), .061,.092,.023,'gold')
        height,width=.81,.52
        name='Heater shield · blue steel and warm gold'
    elif variant == 'B':
        outline = [(.343*math.sin(i*math.tau/64), .365*math.cos(i*math.tau/64)) for i in range(64)]
        _shield_face(code+'_Domed_Wood_Body',outline,'wood')
        _shield_rim(code+'_Bronze_Round_Rim',outline,.035)
        for x in (-.265,-.19,-.115,-.038,.039,.116,.192,.265):
            limit=.344*math.sqrt(max(0,1-(x/.332)**2))
            points=[]
            for j in range(11):
                z=-limit+2*limit*j/10
                r2=(x/.343)**2+(z/.365)**2
                points.append((x+.003*math.sin(j*1.3+x*8),-.090-.071*(1-r2),z))
            w.tube(code+'_Carved_Plank_Joint',points,.0017,'leather',sides=6)
        for angle in (0, 2*math.pi/3, 4*math.pi/3):
            points=[]
            for j in range(9):
                r=.07+.265*j/8
                x,z=math.sin(angle)*r,math.cos(angle)*r*1.06
                points.append((x,-.104-.066*(1-(r/.343)**2),z))
            _ellipse_tube(code+'_Three_Spoke_Bronze_Frame',points,(.020,.012),'gold',sides=10)
        w.sphere(code+'_Central_Boss_Rim',(0,-.163,0),(.102,.027,.126),'gold',24,14)
        w.sphere(code+'_Jade_Cabochon',(0,-.190,0),(.080,.031,.104),'jade',24,16)
        for i in range(6):
            angle=i*math.tau/6
            x,z=.312*math.sin(angle),.333*math.cos(angle)
            _bezel(code+'_Perimeter_Leaf_'+str(i),x,z,.034,.069,-.105,'jade')
            a=angle+.26
            w.sphere(code+'_Rim_Rivet',(.320*math.sin(a),-.107,.342*math.cos(a)),(.009,.006,.009),'gold',12,6)
        for x,z in ((0,.123),(-.102,-.065),(.102,-.065)):
            w.sphere(code+'_Spoke_Rivet',(x,-.184,z),(.013,.009,.013),'gold',12,8)
        height,width=.77,.70
        name='Round shield · carved wood and jade'
    else:
        outline=_closed_curve([(0,.42),(-.112,.29),(-.228,.08),(-.237,-.02),(-.145,-.25),(0,-.42),(.145,-.25),(.237,-.02),(.228,.08),(.112,.29)],5)
        _shield_face(code+'_Domed_Ivory_Body',outline,'ivory')
        _shield_rim(code+'_Diamond_Gold_Rim',outline,.029)
        for sign in (-1,1):
            # Raised jade inlays follow the convex face rather than filling the silhouette flat.
            pts=[(sign*.055,-.148,.315),(sign*.113,-.153,.23),(sign*.138,-.165,.10),(sign*.184,-.137,.018)]
            _tapered_jade_inlay(code+'_Upper_Jade_Inlay',w.curve(pts,steps=7),.018,.007)
            pts=[(sign*.055,-.145,-.31),(sign*.106,-.160,-.20),(sign*.156,-.155,-.06),(sign*.205,-.122,-.025)]
            _tapered_jade_inlay(code+'_Lower_Jade_Inlay',w.curve(pts,steps=7),.031,.008)
            _line(code+'_Gold_Swept_Rib',[(0,-.178,.0),(sign*.082,-.180,.025),(sign*.157,-.143,.045),(sign*.216,-.110,.016)],.010,'gold')
            _bezel(code+'_Side_Gem',sign*.217,.022,.033,.067,-.109)
        _line(code+'_Gold_Axial_Frame',[(0,-.11,-.39),(0,-.158,-.21),(0,-.183,0),(0,-.154,.25),(0,-.11,.39)],.012,'gold',sides=10)
        _bezel(code+'_Large_Crystal',0,0,.130,.322,-.188)
        _bezel(code+'_Crown_Crystal',0,.354,.033,.071,-.113)
        _bezel(code+'_Foot_Crystal',0,-.354,.032,.066,-.113)
        height,width=.85,.49
        name='Diamond shield · ivory, jade and turquoise crystal'
    _shield_back(code,width)
    return {'name':name,'category':'shield','length_m':height,
        'markers':{'Grip_Primary':[0,0,0],'Forearm':[width*.19,.015,0],'Tip':[0,-.085,height/2]},
        'notes':'Domed closed shield with finished back, raised leather straps and real central handgrip.'}


def _staff_base(code, kind='wood', bottom=-.82, top=.53):
    w.tube(code+'_Shaft',[(0,0,bottom),(0,0,-.55),(0,0,-.17),(0,0,.17),(0,0,top)],
        [.018,.022,.023,.023,.021],kind,sides=16)
    w.grip(code+'_Primary_Leather_Grip',-.15,.15,radius=.026)
    for z in (-.16,.16):
        w.band(code+'_Grip_Collar',z,.031,.022,'gold')
    for z in (-.64,.39):
        w.band(code+'_Shaft_Collar',z,.028,.019,'gold')
        _bezel(code+'_Shaft_Jewel',0,z,.017,.034,y=-.028)
    w.gem(code+'_Foot_Ferrule',(0,0,bottom+.04),.069,.13,.061,'gold')
    w.gem(code+'_Foot_Inlay',(0,-.031,bottom+.04),.024,.067,.012,'jade')


def _staff(code):
    variant=code[-1]
    if variant=='A':
        _staff_base(code)
        for z,r in ((.49,.034),(.54,.052),(.90,.038),(.94,.029)):
            w.band(code+'_Lantern_Collar',z,r,.024,'gold')
        for i in range(5):
            a=math.tau*i/5
            path=[(math.cos(a)*r,math.sin(a)*r,z) for r,z in ((.040,.535),(.077,.60),(.110,.735),(.075,.86),(.035,.928))]
            _line(code+'_Lantern_Cage_Rib',path,.009,'gold',steps=7,sides=10)
            x,y=.108*math.cos(a),.108*math.sin(a)
            w.sphere(code+'_Lantern_Rib_Knot',(x,y,.735),(.015,.015,.017),'gold',12,8)
        w.gem(code+'_Amber_Lantern_Core',(0,0,.737),.092,.232,.088,'amber')
        w.band(code+'_Core_Seat',.617,.037,.020,'steel')
        w.band(code+'_Core_Cap',.855,.023,.016,'steel')
        w.gem(code+'_Lantern_Finial',(0,0,.973),.047,.075,.047,'gold')
        name='Lantern staff · amber core and forged cage'
        upper=1.010
    elif variant=='B':
        bottom=-.82
        path=w.curve([(0,0,bottom),(-.018,.004,-.64),(.020,.001,-.42),(0,0,-.16),(0,0,.15),(.022,.007,.32),(-.014,0,.50)],steps=7)
        radii=[.023+.007*math.sin(i/len(path)*math.pi) for i in range(len(path))]
        w.tube(code+'_Sculpted_Wood_Shaft',path,radii,'wood',sides=16)
        w.grip(code+'_Leather_Grip',-.15,.15,radius=.030)
        for z in (-.17,.17):w.band(code+'_Grip_Collar',z,.033,.023,'gold')
        for sign in (-1,1):
            path=[(-.014,0,.48),(sign*.064,-.002,.60),(sign*.118,.009,.76),(sign*.087,0,.91),(.018,-.002,.99),(-.047,0,1.00)]
            sampled=w.curve(path,steps=7)
            w.tube(code+'_Crown_Wood_Arch',sampled,[.027*(1-i/(len(sampled)-1))+.005 for i in range(len(sampled))],'wood',sides=12)
        curl=w.curve([(-.046,0,1.0),(-.104,-.004,.981),(-.121,-.006,.945),(-.098,-.008,.916),(-.080,-.009,.938)],steps=6)
        w.tube(code+'_Crook_Curl',curl,[.002+.014*(1-i/(len(curl)-1)) for i in range(len(curl))],'wood',sides=12)
        shoot=w.curve([(.060,.009,.64),(.133,.003,.76),(.143,.008,.90),(.127,.007,.935)],steps=7)
        w.tube(code+'_Natural_Offshoot',shoot,[.0015+.010*(1-i/(len(shoot)-1)) for i in range(len(shoot))],'wood',sides=10)
        w.gem(code+'_Jade_Seed_Gold_Cup',(0,.004,.768),.111,.188,.080,'gold')
        w.sphere(code+'_Jade_Seed',(0,-.018,.786),(.047,.038,.077),'jade',20,14)
        for base,span in ((-.73,.23),(.21,.33)):
            pts=[]
            for i in range(36):
                t=i/35
                a=t*math.tau*1.5
                pts.append((.033*math.cos(a),.033*math.sin(a),base+t*span))
            _ellipse_tube(code+'_Carved_Ivory_Spiral',pts,(.008,.004),'ivory',sides=8)
        _line(code+'_Gold_Crown_Tendril',[(-.025,-.025,.35),(.026,-.041,.48),(.054,-.031,.60),(.030,-.035,.70)],.008,'gold',steps=6,sides=8)
        w.gem(code+'_Foot_Jade',(0,-.025,-.73),.030,.075,.024,'jade')
        w.gem(code+'_Wood_Foot',(0,0,-.81),.048,.12,.043,'wood')
        name='Living wood staff · carved crook and jade seed'
        upper=1.023
    else:
        _staff_base(code,'steel',bottom=-.82,top=.57)
        for sign in (-1,1):
            _line(code+'_Gold_Shaft_Fillet',[(sign*.015,-.016,-.70),(sign*.014,-.019,-.39),(sign*.012,-.020,-.19)],.0035,'gold',steps=8,sides=6)
            _line(code+'_Gold_Upper_Fillet',[(sign*.013,-.019,.18),(sign*.016,-.019,.37),(sign*.025,-.015,.53)],.0035,'gold',steps=8,sides=6)
            pts=[(sign*.022,0,.48),(sign*.042,0,.62),(sign*.108,.009,.76),(sign*.101,.007,.90),(sign*.073,.005,.972)]
            sampled=w.curve(pts,steps=7)
            w.tube(code+'_Swept_Gold_Crystal_Prong',sampled,[(.015*(1-i/(len(sampled)-1))+.0025,.008) for i in range(len(sampled))],'gold',sides=8)
            pts=[(sign*.019,.011,.50),(sign*.054,.029,.66),(sign*.081,.038,.71)]
            _line(code+'_Blue_Steel_Prong_Support',pts,.015,'steel',steps=8,sides=10)
        w.band(code+'_Crystal_Seat',.641,.033,.027,'gold')
        w.gem(code+'_Great_Turquoise_Crystal',(0,0,.833),.151,.379,.108,'crystal')
        _bezel(code+'_Neck_Jewel',0,.42,.028,.069,-.029)
        name='Crystal staff · turquoise prism and gold talons'
        upper=1.023
    return {'name':name,'category':'staff','length_m':{'A':1.856,'B':1.893,'C':1.868}[variant],
        'markers':{'Grip_Primary':[0,0,0],'Grip_Secondary':[0,0,-.39],'Tip':[0,0,upper]},
        'notes':'Solid shaft, leather handgrip, modelled ferrule and three-dimensional magical crown.'}


def _ribbon(name, points, width_values, depth, kind):
    """Closed elliptical sweep in XZ: broad front, rounded sidewalls and taper."""
    verts,faces=[],[]
    sides=10
    for i,(x,y,z) in enumerate(points):
        prev,nxt=points[max(i-1,0)],points[min(i+1,len(points)-1)]
        dx,dz=nxt[0]-prev[0],nxt[2]-prev[2]
        length=max(1e-8,math.hypot(dx,dz))
        nx,nz=-dz/length,dx/length
        width=width_values[i]
        for j in range(sides):
            a=j*math.tau/sides
            verts.append((x+nx*width*math.cos(a),y+depth*math.sin(a),z+nz*width*math.cos(a)))
    for i in range(len(points)-1):
        for j in range(sides):faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    faces.append(tuple(range(sides-1,-1,-1)))
    faces.append(tuple((len(points)-1)*sides+j for j in range(sides)))
    return w.mesh(name,verts,faces,kind,smooth=True)


def _leaf(name,x,z,sign=1,y=-.036):
    w.profile(name,[(x,z-.039),(x-sign*.021,z-.014),(x-sign*.016,z+.030),(x+sign*.016,z+.059),(x+sign*.021,z+.009)],.014,'jade',y=y,bevel=.004)
    _line(name+'_Gold_Midrib',[(x,y,z-.032),(x-sign*.004,y-.010,z+.009),(x+sign*.014,y-.002,z+.049)],.003,'gold',steps=5,sides=6)


def _bow(code):
    variant=code[-1]
    half={'A':.73,'B':.67,'C':.73}[variant]
    string_x={'A':.171,'B':.170,'C':.190}[variant]
    w.grip(code+'_Leather_Grip',-.092,.092,radius=.025)
    w.tube(code+'_Grip_Core',[(0,0,-.12),(0,0,.12)],.022,'wood' if variant!='C' else 'steel',sides=14)
    for sign in (-1,1):
        scale=half/.73
        controls=[(0,0,sign*.10),(-.034,0,sign*.19),(-.078,0,sign*.305),
            (-.054,0,sign*.42),(.025,0,sign*.54),(.123,0,sign*.64),(string_x,0,sign*.73)]
        controls=[(x,y,z*scale) for x,y,z in controls]
        path=w.curve(controls,steps=7)
        widths=[]
        for i in range(len(path)):
            t=i/(len(path)-1)
            widths.append((.025+.019*math.sin(t*math.pi))* (1-.69*t))
        if variant=='C':
            widths=[value*(1.45 if .14<i/(len(path)-1)<.58 else 1.0) for i,value in enumerate(widths)]
        _ribbon(code+'_Recurved_Limb',path,widths,.018 if variant!='C' else .021,
            'crystal' if variant=='C' else 'wood')
        for side in (-1,1):
            edge=[]
            for i,(x,y,z) in enumerate(path):
                prev,nxt=path[max(0,i-1)],path[min(len(path)-1,i+1)]
                dx,dz=nxt[0]-prev[0],nxt[2]-prev[2]
                ln=max(1e-8,math.hypot(dx,dz))
                edge.append((x-side*dz/ln*widths[i]*.84,-.011,z+side*dx/ln*widths[i]*.84))
            if variant!='B':w.tube(code+'_Gold_Limb_Edge',edge,.0045,'gold',sides=6)
        if variant=='A':
            outer=[(x,-.004,z) for x,y,z in path]
            _ribbon(code+'_Blue_Steel_Limb_Inlay',outer,[q*.26 for q in widths],.021,'steel')
        if variant=='B':
            _leaf(code+'_Jade_Leaf',-.065,sign*.275,sign,y=-.027)
            _leaf(code+'_Jade_Leaf_Side',-.039,sign*.325,-sign,y=-.028)
            _line(code+'_Natural_Wood_Ridge',[(x+.009,-.018,z) for x,y,z in controls[:-1]],.0035,'gold',steps=7,sides=6)
        collar_z=sign*.108*scale
        w.band(code+'_Grip_Collar',collar_z,.031,.022,'gold')
        _bezel(code+'_Grip_Jewel',0,sign*.139*scale,.025,.049,-.029,'jade' if variant!='C' else 'crystal')
        # Small turned horn continues beyond the nock while preserving the string gap.
        tip=[(string_x,0,sign*half),(string_x+.011,0,sign*(half+.025)),(string_x-.006,0,sign*(half+.049))]
        _line(code+'_Tip_Horn',tip,.010,'gold',steps=5,sides=10)
        _bezel(code+'_Tip_Jewel',string_x,sign*(half+.008),.021,.041,-.017,'jade' if variant=='B' else 'crystal')
        w.tube(code+'_String_Nock',[ (string_x,0,sign*(half-.035)),(string_x,0,sign*half)],.005,'leather',sides=8)
    string=w.tube(code+'_Taut_Bowstring',[(string_x,0,-half),(string_x,0,half)],.00125,'cord',sides=8)
    # Decimating this small closed tube can collapse it to a double-sided triangle.
    string['preserve_lod']=True
    # An outboard arrow rest leaves open space above the grip and between limbs.
    w.tube(code+'_Arrow_Rest',[(.014,-.005,.060),(.025,-.027,.060),(.025,-.051,.060)],.004,'gold',sides=8)
    name={'A':'Recurve bow · wood, blue steel and gold','B':'Woodland bow · sculpted timber and jade leaves','C':'Crystal bow · turquoise limbs and warm gold'}[variant]
    return {'name':name,'category':'bow','length_m':round(2*(half+.059),3),
        'markers':{'Grip_Primary':[0,0,0],'ArrowRest':[.025,-.051,.060],
                   'String_Center':[string_x,0,.060],'Tip':[string_x,0,half],
                   'String_Upper':[string_x,0,half],'String_Lower':[string_x,0,-half]},
        'notes':'Solid tapered recurve limbs and separate thin three-dimensional string. Static display pose; bending and drawing require future animation.'}


def build(code):
    if code in ('B-A','B-B','B-C'):
        return _shield(code)
    if code in ('BT-A','BT-B','BT-C'):
        return _staff(code)
    if code in ('AR-A','AR-B','AR-C'):
        return _bow(code)
    raise ValueError('Unsupported support weapon concept: '+code)
