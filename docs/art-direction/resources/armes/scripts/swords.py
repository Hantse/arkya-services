"""Six sculpted swords from the approved Arkya weapon boards.

Coordinates are metres, primary grip at the origin, blade along +Z, face -Y.
The reverse receives the same inset construction as the display face.
"""
import math

from weapon_common import PALETTE, band, curve, gem, grip, mesh, profile, sphere, tube


def _blade(name, stations, tip, kind='steel', edge_kind='edge', y=0):
    """Closed blade with a central arris and deliberately broad cutting bevels."""
    vertices = []
    for cx, z, width, thickness in stations:
        ring = [(-1, 0), (-.71, -.58), (0, -1), (.71, -.58),
                (1, 0), (.71, .58), (0, 1), (-.71, .58)]
        vertices.extend((cx + x * width, y + d * thickness, z) for x, d in ring)
    faces = [tuple(reversed(range(8)))]
    edge_faces = []
    for row in range(len(stations) - 1):
        for j in range(8):
            faces.append((row * 8 + j, row * 8 + (j + 1) % 8,
                          (row + 1) * 8 + (j + 1) % 8, (row + 1) * 8 + j))
            if j in (0, 3, 4, 7):
                edge_faces.append(len(faces) - 1)
    tip_index = len(vertices)
    vertices.append((tip[0], y, tip[1]))
    last = (len(stations) - 1) * 8
    for j in range(8):
        faces.append((last + j, last + (j + 1) % 8, tip_index))
        if j in (0, 3, 4, 7):
            edge_faces.append(len(faces) - 1)
    obj = mesh(name, vertices, faces, kind, bevel=0, smooth=False)
    if edge_kind and edge_kind != kind:
        painted = obj.data.color_attributes['Painted']
        base, edge = PALETTE[kind][0], PALETTE[edge_kind][0]
        for i in edge_faces:
            for loop in obj.data.polygons[i].loop_indices:
                old = painted.data[loop].color
                painted.data[loop].color = (*[min(.98, old[c] * edge[c] / base[c])
                                              for c in range(3)], 1)
    return obj


def _smooth_stations(stations, steps=5):
    """Refine the curved sword silhouette without flattening its cutting section."""
    result = []
    for i in range(len(stations)-1):
        a, b, c, d = (stations[max(0,i-1)], stations[i], stations[i+1],
                      stations[min(len(stations)-1,i+2)])
        for j in range(steps):
            t = j/steps
            values = [.5*(2*b[k]+(-a[k]+c[k])*t+(2*a[k]-5*b[k]+4*c[k]-d[k])*t*t+
                           (-a[k]+3*b[k]-3*c[k]+d[k])*t*t*t) for k in range(4)]
            values[1] = b[1]+(c[1]-b[1])*t
            result.append(tuple(values))
    return result+[stations[-1]]


def _bezier(a, b, c, d, steps=10):
    return [tuple((1-t)**3 * a[k] + 3*(1-t)**2*t*b[k] +
                  3*(1-t)*t*t*c[k] + t**3*d[k] for k in range(2))
            for t in [j / steps for j in range(steps)]]


def _outline(segments):
    return [point for segment in segments for point in _bezier(*segment)]


def _ribbon(points, width):
    centers = curve([(x, 0, z) for x, z in points], steps=6)
    left, right = [], []
    for i, point in enumerate(centers):
        before, after = centers[max(0, i-1)], centers[min(len(centers)-1, i+1)]
        dx, dz = after[0]-before[0], after[2]-before[2]
        length = math.hypot(dx, dz)
        radius = width * (.12 + .88 * math.sin(math.pi*i/(len(centers)-1))**.45)
        ox, oz = -dz/length*radius, dx/length*radius
        left.append((point[0]+ox, point[2]+oz))
        right.append((point[0]-ox, point[2]-oz))
    return left + list(reversed(right))


def _double_gem(name, x, z, width, height, depth=.012, surface=.026, kind='jade'):
    for sign in (-1, 1):
        gem(name + ('_Front' if sign == -1 else '_Back'),
            (x, sign * surface, z), width, height, depth, kind)


def _diamond_mount(name, x, z, width, height, kind='jade', thickness=.04):
    points = [(x, z-height/2), (x+width/2, z), (x, z+height/2), (x-width/2, z)]
    profile(name + '_Bezel', points, thickness, 'gold', bevel=.004)
    _double_gem(name, x, z, width*.62, height*.66, .012, thickness/2+.003, kind)


def _pommel(name, z, kind='jade', size=1, diamond=False):
    width, height = .066*size, .105*size
    if diamond:
        _diamond_mount(name, 0, z, width, height, kind, .035*size)
    else:
        silhouette = _outline([
            ((0,z-height/2),(-width*.45,z-height*.30),(-width*.62,z),(-width*.38,z+height*.25)),
            ((-width*.38,z+height*.25),(-width*.23,z+height*.43),(-width*.08,z+height*.50),(0,z+height/2)),
            ((0,z+height/2),(width*.08,z+height*.50),(width*.23,z+height*.43),(width*.38,z+height*.25)),
            ((width*.38,z+height*.25),(width*.62,z),(width*.45,z-height*.30),(0,z-height/2)),
        ])
        profile(name+'_Frame', silhouette, .035*size, 'gold', bevel=.005*size)
        for sign in (-1, 1):
            sphere(name+'_Cabochon', (0,sign*.022*size,z+.002*size),
                   (.022*size,.012*size,.038*size),kind,segments=16,rings=12)
            tube(name+'_RaisedRim',
                 [(math.sin(j*math.tau/40)*.024*size,sign*.022*size,
                   z+.002*size+math.cos(j*math.tau/40)*.041*size) for j in range(41)],
                 [.0024*size]*41,'gold',sides=8)


def _hilt(two_handed=False, variant='A'):
    bottom = -.31 if two_handed else -.068
    top = .080
    grip('Leather_Grip',bottom,top,.023 if two_handed else .022)
    band('Lower_Ferrule',bottom,.027,.015,'gold')
    band('Upper_Ferrule',top,.028,.018,'gold')
    if two_handed:
        band('Grip_Separator',-.115,.0265,.012,'gold')
        if variant in ('A','C'):
            _diamond_mount('Grip_Accent',0,-.115,.035,.044,'crystal',.052)
        _pommel('Counterweight_Pommel',-.389,'crystal' if variant=='C' else 'jade',1.2,variant=='C')
        band('Pommel_Collar',-.331,.030,.020,'gold')
    else:
        _pommel('Compact_Pommel',-.119,'crystal' if variant=='C' else 'jade',.85,variant=='C')


def _plain_guard(z, scale=1, jade_tips=False):
    upper = [(x*scale,z+h*scale) for x,h in _bezier((-.16,-.020),(-.10,.037),(.10,.037),(.16,-.020),24)]
    upper.append((.16*scale,z-.020*scale))
    lower = [(x*scale,z+h*scale) for x,h in _bezier((.16,-.047),(.09,.002),(-.09,.002),(-.16,-.047),24)]
    lower.append((-.16*scale,z-.047*scale))
    profile('Arched_Guard',upper+lower,.037*scale,'gold',bevel=.0045)
    for sign in (-1,1):
        points = curve([(-.145*scale,sign*.023*scale,z-.026*scale),
                        (-.075*scale,sign*.025*scale,z+.007*scale),
                        (0,sign*.025*scale,z+.013*scale),
                        (.075*scale,sign*.025*scale,z+.007*scale),
                        (.145*scale,sign*.023*scale,z-.026*scale)],steps=5)
        tube('Guard_Raised_Edge',points,[.0025*scale]*len(points),'edge',sides=8)
    for side in (-1,1):
        end = [(side*.148*scale,z-.018*scale),(side*.163*scale,z-.006*scale),
               (side*.178*scale,z-.036*scale),(side*.158*scale,z-.061*scale)]
        profile('Guard_Endcap',end,.047*scale,'gold',bevel=.004)
        if jade_tips:
            _double_gem('Guard_Endcap_Jade',side*.162*scale,z-.034*scale,
                        .018*scale,.031*scale,.008,.026*scale)


def _crescent_guard(z, scale=1, kind='jade', ivory=False):
    """Pair of substantial curled leaves, with inset material and rolled rims."""
    for side in (-1,1):
        shape = _outline([
            ((.013,.001),(.061,.068),(.138,.056),(.160,.119)),
            ((.160,.119),(.204,.065),(.194,-.014),(.155,-.056)),
            ((.155,-.056),(.180,.019),(.079,.052),(.013,.001)),
        ])
        points = [(side*x*scale,z+h*scale) for x,h in shape]
        profile('Leaf_Guard_Frame',points,.045*scale,'gold',bevel=.005)
        inset = _outline([
            ((.046,.016),(.100,.066),(.140,.058),(.160,.095)),
            ((.160,.095),(.184,.059),(.183,.013),(.163,-.018)),
            ((.163,-.018),(.162,.033),(.090,.051),(.046,.016)),
        ])
        for sign in (-1,1):
            profile('Leaf_Guard_Inset',[(side*x*scale,z+h*scale) for x,h in inset],
                    .010*scale,'ivory' if ivory else kind,y=sign*.025*scale,bevel=.0025)
            vein = curve([(side*.042*scale,sign*.034*scale,z+.013*scale),
                          (side*.102*scale,sign*.034*scale,z+.043*scale),
                          (side*.156*scale,sign*.033*scale,z+.045*scale),
                          (side*.175*scale,sign*.029*scale,z+.005*scale)],steps=6)
            tube('Leaf_Guard_Vein',vein,[.003*scale]*len(vein),'gold',sides=8)


def _crystal_guard(z, scale=1):
    """Forked crescents expose a deep curved notch beside the crystal blade."""
    for side in (-1,1):
        shape = _outline([
            ((.010,0),(.050,.008),(.066,.038),(.080,.060)),
            ((.080,.060),(.116,.014),(.153,.034),(.154,.108)),
            ((.154,.108),(.217,.044),(.194,-.032),(.150,-.063)),
            ((.150,-.063),(.154,-.009),(.065,-.015),(.010,0)),
        ])
        profile('Forked_Crescent',[(side*x*scale,z+h*scale) for x,h in shape],
                .047*scale,'gold',bevel=.005)
        for sign in (-1,1):
            rim = curve([(side*.170*scale,sign*.025*scale,z+.078*scale),
                         (side*.180*scale,sign*.030*scale,z+.026*scale),
                         (side*.162*scale,sign*.027*scale,z-.024*scale)],steps=8)
            tube('Crescent_Inlay',rim,[.0045*scale]*len(rim),'crystal',sides=10)


def _sword_one_a():
    _hilt()
    _plain_guard(.119,.86)
    stations=[(0,.12,.046,.013),(0,.18,.048,.014),(0,.39,.044,.012),
              (0,.68,.037,.010),(0,.765,.034,.008)]
    _blade('Forged_Steel_Blade',stations,(0,.868))
    _diamond_mount('Guard_Heart',0,.130,.068,.115,'jade',.047)
    # Raised steel shoulders catch light while the central steel plane stays quiet.
    for sign in (-1,1):
        inset=[(0,.187),(.010,.236),(.008,.70),(0,.80),(-.008,.70),(-.010,.236)]
        profile('Blade_Central_Ridge',inset,.003,'steel',y=sign*.013,bevel=.001)
    return {'name':'Arming Sword A','category':'sword-one-hand','length_m':1.032,
            'markers':{'Grip_Primary':[0,0,0],'Tip':[0,0,.868]},
            'notes':'E1-A: straight forged blade, restrained arched bronze guard and oval jade pommel.'}


def _sword_one_b():
    # The grip sweeps back only above and below the unobstructed hand centre.
    handle=curve([(-.010,0,-.075),(0,0,-.026),(0,0,0),(.004,0,.050),(.018,0,.092)],steps=7)
    tube('Carved_Curved_Grip',handle,[.022]*len(handle),'leather',sides=16)
    for j in range(8):
        z=-.065+j*.020
        x=0 if z<.04 else (z-.04)*.25
        band('Grip_Binding',z,.0235,.006,'leather',x=x)
    band('Lower_Ferrule',-.079,.025,.014,'gold',x=-.011)
    band('Upper_Ferrule',.087,.027,.018,'gold',x=.017)
    curl=curve([(-.011,0,-.078),(-.034,0,-.105),(-.050,0,-.118),(-.066,0,-.110)],steps=7)
    tube('Hooked_Pommel',curl,[.022-i*.00035 for i in range(len(curl))],'gold',sides=14)
    for sign in (-1,1):
        sphere('Pommel_Jade',(-.037,sign*.018,-.104),(.028,.010,.020),'jade')

    guard_shape=_outline([
        ((-.154,.035),(-.190,.098),(-.148,.180),(-.084,.172)),
        ((-.084,.172),(-.034,.167),(.011,.093),(.066,.108)),
        ((.066,.108),(.121,.118),(.140,.164),(.137,.233)),
        ((.137,.233),(.202,.166),(.167,.087),(.093,.071)),
        ((.093,.071),(.037,.057),(-.010,.145),(-.070,.144)),
        ((-.070,.144),(-.125,.139),(-.149,.098),(-.154,.035)),
    ])
    profile('Swept_S_Guard',guard_shape,.043,'gold',bevel=.005)
    guard_inset=_ribbon([(-.154,.065),(-.149,.113),(-.113,.150),
                         (-.071,.153),(-.004,.112),(.057,.087),
                         (.110,.107),(.143,.151),(.145,.204)],.009)
    for sign in (-1,1):
        # Keep the bevel below the tapered ribbon's 1.08 mm end half-width.
        profile('Swept_Guard_Jade',guard_inset,.010,'jade',y=sign*.024,bevel=.0004)
        sphere('Guard_Gold_Bezel',(.010,sign*.033,.123),(.030,.012,.031),'gold')
        sphere('Guard_Jade_Cabochon',(.010,sign*.044,.123),(.021,.009,.022),'jade')
        leaf=[(-.010,.141),(-.032,.188),(-.024,.212),(-.010,.193),(.003,.222),(.013,.185),(.025,.166)]
        profile('Ivory_Leaf',leaf,.012,'ivory',y=sign*.022,bevel=.003)
    stations=[(.012,.146,.036,.014),(.010,.250,.042,.016),
              (.019,.400,.044,.017),(.047,.550,.052,.017),
              (.092,.705,.052,.015),(.145,.825,.032,.011),(.179,.884,.016,.006)]
    _blade('Swept_Leaf_Steel',_smooth_stations(stations),(.208,.934))
    for sign in (-1,1):
        inlay=curve([(.017,sign*.015,.180),(.017,sign*.017,.360),
                     (.035,sign*.018,.490),(.082,sign*.016,.670),
                     (.145,sign*.010,.825),(.192,sign*.003,.916)],steps=7)
        tube('Blade_Bronze_Filament',inlay,[.0030]*len(inlay),'gold',sides=8)
    return {'name':'Leaf Sword B','category':'sword-one-hand','length_m':1.068,
            'markers':{'Grip_Primary':[0,0,0],'Tip':[.208,0,.934]},
            'notes':'E1-B: swept single-edge leaf silhouette, curved leather grip, asymmetric jade crescent and ivory insert.'}


def _sword_one_c():
    _hilt(variant='C')
    _crystal_guard(.122,.78)
    stations=[(0,.130,.049,.017),(0,.235,.050,.020),(0,.450,.047,.018),
              (0,.700,.043,.016),(0,.777,.041,.013)]
    _blade('Solid_Crystal_Blade',stations,(0,.885),'crystal','crystal')
    _diamond_mount('Crystal_Heart',0,.139,.078,.133,'crystal',.050)
    for sign in (-1,1):
        # Separate tapered gold rails avoid a flat shield across the crystal blade.
        for side in (-1,1):
            rail=curve([(side*.037,sign*.016,.142),(side*.022,sign*.018,.202),(0,sign*.020,.273)],steps=8)
            tube('Crystal_Blade_Cradle',rail,[.006*(1-i/(len(rail)+2))+.001 for i in range(len(rail))],'gold',sides=10)
    return {'name':'Crystal Sword C','category':'sword-one-hand','length_m':1.049,
            'markers':{'Grip_Primary':[0,0,0],'Tip':[0,0,.885]},
            'notes':'E1-C: solid faceted turquoise crystal, forked bronze guard and diamond pommel.'}


def _sword_two_a():
    _hilt(True,'A')
    _plain_guard(.133,1.12,True)
    stations=[(0,.153,.050,.015),(0,.190,.039,.015),(0,.242,.038,.015),
              (0,.273,.056,.018),(0,.840,.047,.015),(0,1.025,.043,.012)]
    _blade('Long_Forged_Blade',stations,(0,1.175))
    _diamond_mount('Guard_Jade_Heart',0,.140,.071,.107,'jade',.052)
    for sign in (-1,1):
        spur=[(0,.169),(.023,.260),(0,.334),(-.023,.260)]
        profile('Gold_Ricasso_Spur',spur,.006,'gold',y=sign*.017,bevel=.002)
        fuller=[(0,.293),(.008,.354),(.006,1.027),(0,1.083),(-.006,1.027),(-.008,.354)]
        profile('Long_Raised_Ridge',fuller,.003,'steel',y=sign*.017,bevel=.0008)
    return {'name':'Greatsword A','category':'sword-two-hands','length_m':1.627,
            'markers':{'Grip_Primary':[0,0,0],'Grip_Secondary':[0,0,-.205],'Tip':[0,0,1.175]},
            'notes':'E2-A: disciplined straight greatsword, narrowed ricasso, elongated leather grip and oval counterweight.'}


def _sword_two_b():
    _hilt(True,'B')
    _crescent_guard(.112,1.02,ivory=True)
    stations=[(0,.155,.045,.017),(0,.210,.041,.017),(0,.280,.078,.021),
              (0,.370,.060,.021),(0,.570,.047,.020),(0,.800,.054,.019),
              (0,.925,.078,.020),(0,1.045,.035,.013)]
    _blade('Leafpoint_Silver_Blade',stations,(0,1.160),'steel','edge')
    for sign in (-1,1):
        inlay=[(0,.171),(.034,.280),(.025,.380),(.020,.620),(.029,.882),(.040,.925),
               (0,1.070),(-.040,.925),(-.029,.882),(-.020,.620),(-.025,.380),(-.034,.280)]
        profile('Jade_Blade_Channel',inlay,.006,'jade',y=sign*.020,bevel=.002)
        for side in (-1,1):
            rail=curve([(0,sign*.024,.178),(side*.039,sign*.025,.280),
                        (side*.028,sign*.024,.400),(side*.025,sign*.023,.650),
                        (side*.033,sign*.023,.865),(side*.046,sign*.024,.925),
                        (0,sign*.018,1.084)],steps=5)
            tube('Blade_Channel_Gold_Border',rail,[.0032]*len(rail),'gold',sides=8)
    _diamond_mount('Guard_Heart',0,.158,.076,.151,'jade',.055)
    return {'name':'Leaf Greatsword B','category':'sword-two-hands','length_m':1.612,
            'markers':{'Grip_Primary':[0,0,0],'Grip_Secondary':[0,0,-.205],'Tip':[0,0,1.160]},
            'notes':'E2-B: broad leaf-point steel outline, jade central channel and paired ivory leaf guards.'}


def _sword_two_c():
    _hilt(True,'C')
    _crystal_guard(.115,1.02)
    stations=[(0,.155,.053,.021),(0,.215,.042,.020),(0,.290,.085,.026),
              (0,.400,.060,.023),(0,.625,.046,.021),(0,.815,.061,.023),
              (0,.952,.081,.025),(0,1.075,.032,.012)]
    _blade('Crystal_Cutting_Planes',stations,(0,1.174),'crystal','crystal')
    spine=[(0,.170,.023,.023),(0,.265,.028,.027),(0,.290,.034,.029),
           (0,.338,.021,.027),(0,.620,.015,.025),(0,.900,.018,.026),
           (0,.952,.028,.028),(0,1.066,.010,.015)]
    for sign in (-1,1):
        # Face rails sit proud of the crystal across their full width.
        rail_sections=[(x,z,w,t*.30) for x,z,w,t in spine]
        _blade('Structural_Steel_Spine',rail_sections,(0,1.137),'steel','edge',y=sign*.023)
        for side in (-1,1):
            rail=curve([(side*.023,sign*.028,.179),(side*.017,sign*.031,.235),
                        (side*.027,sign*.032,.290),(side*.014,sign*.030,.355),
                        (side*.010,sign*.029,.650),(side*.013,sign*.030,.891),
                        (side*.021,sign*.031,.952),(0,sign*.016,1.100)],steps=5)
            tube('Spine_Gold_Edging',rail,[.0038]*len(rail),'gold',sides=8)
    _diamond_mount('Guard_Crystal_Heart',0,.145,.082,.134,'crystal',.058)
    _double_gem('Blade_Crest_Crystal',0,.946,.024,.071,.007,.035,'crystal')
    return {'name':'Crystal Greatsword C','category':'sword-two-hands','length_m':1.626,
            'markers':{'Grip_Primary':[0,0,0],'Grip_Secondary':[0,0,-.205],'Tip':[0,0,1.174]},
            'notes':'E2-C: waisted turquoise crystal planes surrounding a solid steel spine, crescent guard and long two-hand grip.'}


def build(code):
    builders={'E1-A':_sword_one_a,'E1-B':_sword_one_b,'E1-C':_sword_one_c,
              'E2-A':_sword_two_a,'E2-B':_sword_two_b,'E2-C':_sword_two_c}
    if code not in builders:
        raise ValueError('Unsupported sword concept: '+str(code))
    return builders[code]()
