"""Six sculpted axe designs translated from Arkya's approved concept sheets.

The grip origin is shared by every variant. Blade profiles have closed back
faces and a thin cutting plane beneath a thicker, bevelled structural core.
"""

import math

from weapon_common import band, curve, gem, grip, profile, sphere, tube


def _path(start, commands, steps=9):
    """Sample cubic arcs while preserving the authored sharp end points."""
    points = [start]
    for command in commands:
        if len(command) == 2 and isinstance(command[0], (int, float)):
            points.append(command)
            continue
        a = points[-1]
        b, c, d = command
        for i in range(1, steps + 1):
            t = i / steps
            u = 1 - t
            points.append((u*u*u*a[0] + 3*u*u*t*b[0] + 3*u*t*t*c[0] + t*t*t*d[0],
                           u*u*u*a[1] + 3*u*u*t*b[1] + 3*u*t*t*c[1] + t*t*t*d[1]))
    if points[-1] == points[0]:
        points.pop()
    return points


def _map(points, x, z, scale=1, mirror=1):
    return [(x + px*scale*mirror, z + pz*scale) for px, pz in points]


def _paired_profile(name, points, depth, kind, y=.031, bevel=.002):
    for side in (-1, 1):
        profile(name + ('_Front' if side < 0 else '_Back'), points, depth, kind,
                y=side*y, bevel=bevel)


def _trim(name, points, kind='gold', radius=.005, y=.034):
    for side in (-1, 1):
        pts = [(x, side*y, z) for x, z in points]
        tube(name + ('_Front' if side < 0 else '_Back'), pts, radius, kind, sides=8)


def _diamond(name, x, z, width=.045, height=.065, kind='jade', y=.034):
    for side in (-1, 1):
        gem(name + '_Setting_' + str(side), (x, side*y, z), width*1.5,
            height*1.35, .015, 'gold')
        gem(name + '_Stone_' + str(side), (x, side*(y+.010), z), width,
            height, .014, kind)


def _shaft(name, points, radii):
    sampled = curve(points, steps=6)
    # The supported interpolation keeps tapered curved hafts smoothly connected.
    values = []
    for i in range(len(sampled)):
        t = i/(len(sampled)-1)*(len(radii)-1)
        j = min(int(t), len(radii)-2)
        a = t-j
        values.append(radii[j]*(1-a) + radii[j+1]*a)
    tube(name, sampled, values, 'wood', sides=16)


def _pommel(name, z, x=0, ivory=False, scale=1):
    kind = 'ivory' if ivory else 'gold'
    sphere(name + '_Body', (x, 0, z), (.033*scale, .031*scale, .047*scale), kind)
    band(name + '_Collar', z+.029*scale, .029*scale, .018*scale, 'gold', x=x)
    _diamond(name + '_Inlay', x, z+.004*scale, .020*scale, .034*scale,
             'jade', y=.028*scale)


def _eye(name, x, z, scale=1, crystal=False):
    tube(name + '_Socket', [(x,0,z-.075*scale), (x,0,z+.082*scale)],
         [.038*scale,.037*scale], 'steel', sides=16)
    band(name + '_LowerRing', z-.064*scale, .041*scale, .021*scale, 'gold',x=x)
    band(name + '_UpperRing', z+.074*scale, .040*scale, .017*scale, 'gold',x=x)
    sphere(name + '_EndGrain', (x,0,z+.086*scale),
           (.031*scale,.031*scale,.013*scale), 'wood')
    _diamond(name + '_SocketSeal', x, z+.005*scale, .036*scale, .064*scale,
             'crystal' if crystal else 'jade', y=.038*scale)


def _poll(name, x, z, scale=1, crystal=False):
    profile(name + '_Bridge', _map([(0,-.035),(.071,-.036),(.071,.040),(0,.040)],
                                  x,z,scale), .054*scale, 'steel', bevel=.008*scale)
    profile(name + '_Cap', _map([(.062,-.056),(.098,-.053),(.099,.054),(.064,.056)],
                              x,z,scale), .067*scale, 'edge', bevel=.009*scale)
    if crystal:
        _diamond(name + '_Inlay', x+.080*scale,z,.019*scale,.038*scale,
                 'jade',y=.034*scale)


def _a_head(name, x, z, scale):
    outer = _path((-.255,.205), [
        ((-.251,.110),(-.123,.085),(.006,.084)),
        (.027,.080), (.027,-.026),
        ((-.112,-.018),(-.193,-.101),(-.139,-.205)),
        ((-.287,-.137),(-.327,.051),(-.255,.205))])
    core = _path((-.235,.153), [
        ((-.219,.089),(-.106,.065),(.020,.065)),
        (.025,-.007),
        ((-.115,-.001),(-.180,-.079),(-.164,-.152)),
        ((-.251,-.091),(-.284,.043),(-.235,.153))])
    profile(name + '_SilverCuttingPlane', _map(outer,x,z,scale), .010*scale,
            'edge',bevel=.002*scale)
    profile(name + '_ForgedBlade', _map(core,x,z,scale), .043*scale,
            'steel',bevel=.006*scale)
    inset = _path((-.217,.115), [
        ((-.164,.062),(-.080,.052),(-.031,.048)),
        ((-.108,.024),(-.173,-.029),(-.190,-.089)),
        ((-.218,-.044),(-.236,.051),(-.217,.115))])
    _trim(name + '_FaceBorder', _map(inset + [inset[0]],x,z,scale),
          radius=.003*scale,y=.0235*scale)
    _diamond(name + '_BladeSeal',x-.125*scale,z+.022*scale,.032*scale,.044*scale,
             y=.024*scale)
    _eye(name,x,z,scale)
    _poll(name,x,z,scale)
    diag = _path((-.050,.085), [((-.032,.038),(.013,-.032),(.035,-.061))])
    _trim(name + '_CrossBrace',_map(diag,x,z,scale),radius=.012*scale,y=.041*scale)


def _b_head(name, x, z, scale=1, long=False):
    if long:
        # This elongated descending blade is the defining H2-B silhouette.
        outer = _path((-.245,.260), [
            ((-.316,.100),(-.290,-.110),(-.182,-.246)),
            ((-.105,-.349),(-.053,-.482),(-.109,-.587)),
            ((-.001,-.459),(-.044,-.303),(-.119,-.194)),
            ((-.206,-.065),(-.107,.068),(.026,.048)),
            (.055,.098),
            ((-.067,.138),(-.200,.139),(-.245,.260))])
        core = _path((-.224,.199), [
            ((-.260,.071),(-.242,-.081),(-.145,-.226)),
            ((-.079,-.336),(-.049,-.431),(-.091,-.533)),
            ((-.032,-.425),(-.075,-.290),(-.136,-.188)),
            ((-.214,-.050),(-.104,.086),(.026,.066)),
            (.033,.090),
            ((-.083,.122),(-.183,.115),(-.224,.199))])
        inset = _path((-.205,.167), [
            ((-.181,.130),(-.120,.114),(-.069,.112)),
            ((-.143,.076),(-.193,-.019),(-.177,-.086)),
            ((-.216,-.023),(-.228,.100),(-.205,.167))])
    else:
        outer = _path((-.229,.220), [
            ((-.305,.144),(-.335,-.067),(-.151,-.211)),
            ((-.227,-.116),(-.226,-.022),(-.143,.009)),
            ((-.085,.035),(-.010,.004),(.031,-.044)),
            (.039,.076),
            ((-.088,.026),(-.234,.055),(-.229,.220))])
        core = _path((-.221,.161), [
            ((-.261,.082),(-.277,-.067),(-.183,-.153)),
            ((-.233,-.055),(-.198,.015),(-.133,.030)),
            ((-.074,.046),(-.013,.019),(.025,-.014)),
            (.030,.067),
            ((-.086,.045),(-.213,.066),(-.221,.161))])
        inset = _path((-.207,.134), [
            ((-.169,.070),(-.075,.060),(-.007,.062)),
            (-.010,.039),
            ((-.079,.065),(-.157,.044),(-.197,.017)),
            ((-.210,.051),(-.214,.093),(-.207,.134))])
    profile(name + '_SweptSilverEdge',_map(outer,x,z,scale),.010*scale,
            'edge',bevel=.002*scale)
    profile(name + '_SweptBlade',_map(core,x,z,scale),.039*scale,
            'steel',bevel=.006*scale)
    _paired_profile(name + '_JadeInlay',_map(inset,x,z,scale),.008*scale,
                    'jade',y=.022*scale,bevel=.002*scale)
    _trim(name + '_InlayFrame',_map(inset+[inset[0]],x,z,scale),radius=.0045*scale,
          y=.030*scale)
    _eye(name,x,z+.025*scale,.91*scale)
    frame = _path((-.019,.108), [((-.079,.063),(-.044,-.013),(.043,-.071))])
    _trim(name + '_RootMount',_map(frame,x,z,scale),radius=.011*scale,y=.040*scale)
    branch = _path((-.028,.045), [((-.072,-.030),(-.040,-.063),(.021,-.105))])
    _trim(name + '_RootBrace',_map(branch,x,z,scale),radius=.0075*scale,y=.037*scale)
    if long:
        hook = _path((-.018,.113), [((.079,.123),(.123,.068),(.115,.027)),
                     ((.095,.067),(.068,.072),(.025,.042)),(-.018,.113)])
        profile(name + '_RearHook',_map(hook,x,z,scale),.049*scale,
                'gold',bevel=.002*scale)
        tube(name + '_CrownNeck',[(x,0,z+.091*scale),(x,0,z+.144*scale)],
             [.016*scale,.010*scale],'gold',sides=12)
        gem(name + '_Crown',(x,0,z+.193*scale),.034*scale,.140*scale,
            .037*scale,'gold')
        _diamond(name + '_CrownInlay',x,z+.194*scale,.014*scale,.038*scale,
                 y=.019*scale)
    else:
        _poll(name,x,z+.025*scale,.75*scale)


def _crystal_blade(name, x, z, scale, mirror=1):
    crescent = _path((-.150,.284), [
        ((-.331,.198),(-.367,-.107),(-.138,-.222)),
        ((-.201,-.102),(-.231,.057),(-.150,.284))])
    core = _path((-.170,.242), [
        ((-.303,.164),(-.326,-.083),(-.163,-.183)),
        ((-.218,-.070),(-.242,.072),(-.170,.242))])
    profile(name + '_CrystalCuttingEdge',_map(crescent,x,z,scale,mirror),
            .013*scale,'crystal',bevel=.002*scale)
    profile(name + '_CrystalBody',_map(core,x,z,scale,mirror),
            .048*scale,'crystal',bevel=.009*scale)
    inset = _path((-.194,.181), [
        ((-.278,.118),(-.289,-.040),(-.211,-.124)),
        ((-.245,-.006),(-.242,.067),(-.194,.181))])
    _paired_profile(name + '_DeepCrystalPlane',_map(inset,x,z,scale,mirror),
                    .005*scale,'jade',y=.026*scale,bevel=.002*scale)
    connection = _path((-.010,.059), [
        ((-.058,.059),(-.103,.080),(-.190,.126)),
        (-.221,.029),
        ((-.159,.018),(-.112,-.009),(-.040,-.030)),
        (-.009,-.040),(-.010,.059)])
    profile(name + '_SteelBridge',_map(connection,x,z,scale,mirror),
            .064*scale,'steel',bevel=.008*scale)
    brace = _path((-.182,.145), [((-.220,.071),(-.246,-.041),(-.202,-.113))])
    _trim(name + '_CurvedGoldCradle',_map(brace,x,z,scale,mirror),
          radius=.014*scale,y=.037*scale)
    for i, (px,pz) in enumerate([(-.184,.142),(-.209,-.104)]):
        _diamond(name + '_CradleClasp' + str(i),x+px*scale*mirror,z+pz*scale,
                 .038*scale,.042*scale,'gold',y=.039*scale)
    ridge = _path((-.022,.034), [((-.087,.033),(-.132,.058),(-.181,.081))])
    _trim(name + '_BridgeRidge',_map(ridge,x,z,scale,mirror),
          'edge',radius=.004*scale,y=.037*scale)


def _single_handle(code, variant, head_x, head_z):
    points = [(0,0,-.118),(0,0,.105),(.008,0,.180),
              (head_x+.018,0,.27),(head_x,0,head_z+.08)]
    _shaft(code + '_CarvedHaft',points,[.020,.020,.023,.026,.030])
    if variant == 'B':
        tube(code + '_IvoryHandBinding',[(0,0,-.105),(0,0,.104)],.0255,'ivory',sides=16)
        # The narrow diagonal straps keep the ivory binding visible.
        for j in range(3):
            start_z=-.10+j*.071
            pts=[(.027*math.sin(a),.027*math.cos(a),start_z+a/(2*math.pi)*.048)
                 for a in [i*2*math.pi/24 for i in range(25)]]
            tube(code+'_LeatherBinding'+str(j),pts,.004,'leather',sides=6)
    else:
        grip(code + '_HandGrip',-.105,.105,radius=.025)
    band(code + '_GripCrown',.108,.029,.018,'gold')
    _pommel(code + '_Pommel',-.127,scale=.82)
    if variant == 'B':
        leaf = _path((head_x,head_z-.04), [
            ((head_x+.030,head_z-.14),(-.040,.200),(.005,.145))])
        _trim(code+'_CarvedRoot',leaf,'wood',radius=.009,y=.022)
        _diamond(code+'_HaftJade',head_x*.35,.177,.018,.035,y=.025)
    elif variant == 'C':
        profile(code+'_UpperSleeve',[(-.026,.151),(.032,.151),(.025,.224),(-.032,.224)],
                .055,'steel',bevel=.005)
        band(code+'_SleeveLower',.150,.032,.014,'gold')
        band(code+'_SleeveUpper',.227,.031,.015,'gold')
        _diamond(code+'_SleeveJade',0,.187,.019,.041,y=.031)
    else:
        band(code+'_HaftBand',.245,.029,.023,'gold',x=.012)


def _long_handle(code, variant, head_x, head_z):
    points=[(0,0,-.209),(0,0,.12),(0,0,.550),
            (head_x+.035,0,.780),(head_x,0,head_z+.093)]
    radii=[.021,.021,.022,.030,.035]
    if variant=='C':
        points=[(0,0,-.209),(0,0,.12),(0,0,.550),(0,0,.840),(0,0,head_z+.093)]
        radii=[.021,.021,.022,.024,.030]
    _shaft(code+'_LongHaft',points,radii)
    grip(code+'_LowerHandGrip',-.181,.173,radius=.027)
    grip(code+'_UpperHandGrip',.185,.535,radius=.027)
    band(code+'_LowerGripRing',-.183,.030,.019,'gold')
    band(code+'_CenterGripRing',.178,.030,.016,'gold')
    band(code+'_UpperGripRing',.542,.032,.021,'gold')
    _pommel(code+'_Pommel',-.221,ivory=variant=='B')
    if variant=='B':
        root=_path((head_x-.012,head_z-.062), [
            ((head_x-.028,.842),(head_x+.057,.744),(head_x+.042,.680)),
            ((head_x+.030,.626),(.032,.598),(.004,.552))])
        _trim(code+'_SpiralRoots',root,'gold',radius=.007,y=.030)
        _diamond(code+'_HaftStone',head_x+.040,.818,.029,.066,y=.035)
        _diamond(code+'_GripStone',0,.568,.024,.046,y=.032)
    elif variant=='A':
        band(code+'_WoodMidRing',.765,.035,.025,'gold',x=head_x+.039)
        _diamond(code+'_NeckSeal',head_x+.008,.919,.030,.063,y=.033)
    else:
        # Two hand zones are uninterrupted; the upper shaft continues in leather.
        grip(code+'_UpperShaftWrapping',.565,.805,radius=.027)
        band(code+'_NeckRing',.813,.031,.019,'gold')


def build(code):
    """Build one named concept at the primary hand origin, with its head +Z."""
    if code not in {'H1-A','H1-B','H1-C','H2-A','H2-B','H2-C'}:
        raise ValueError('Unknown axe concept: '+str(code))
    two_handed=code.startswith('H2')
    variant=code[-1]
    if not two_handed:
        x=-.043 if variant=='B' else 0
        z={'A':.410,'B':.398,'C':.382}[variant]
        _single_handle(code,variant,x,z)
        if variant=='A':
            _a_head(code,x,z,.90)
            tip=[x-.2295,0,z+.1845]
        elif variant=='B':
            _b_head(code,x,z,.95)
            tip=[x-.21755,0,z+.209]
        else:
            _crystal_blade(code,x,z,.815)
            _eye(code,x,z,.95,crystal=True)
            _poll(code,x,z,.94,crystal=True)
            tip=[x-.12225,0,z+.23146]
        markers={'Grip_Primary':[0,0,0],'Tip':tip}
        return {'name':code,'category':'AxeOneHand','length_m':round(tip[2]+.166,3),
                'markers':markers,'notes':'Closed crescent/bearded blade; solid socket; wrapped single-hand grip; sculpted reverse face.'}
    x=-.047 if variant=='B' else (-.035 if variant=='A' else 0)
    z={'A':1.005,'B':1.020,'C':1.000}[variant]
    _long_handle(code,variant,x,z)
    if variant=='A':
        _a_head(code,x,z,1.35)
        tip=[x-.34425,0,z+.27675]
    elif variant=='B':
        _b_head(code,x,z,1,long=True)
        tip=[x-.245,0,z+.260]
    else:
        _crystal_blade(code,x,z,1.25)
        _crystal_blade(code+'_CounterBlade',x,z,.78,mirror=-1)
        _eye(code,x,z,1.38,crystal=True)
        profile(code+'_CentralCrystal', [(-.025,z-.090),(.025,z-.090),
                (.027,z+.092),(-.027,z+.092)],.109,'crystal',bevel=.006)
        for side in (-1,1):
            tube(code+'_CentralSpine'+str(side),[(side*.034,0,z-.115),(side*.034,0,z+.115)],
                 .009,'steel',sides=10)
        gem(code+'_Crown',(0,0,z+.192),.045,.170,.045,'gold')
        _diamond(code+'_CrownInlay',0,z+.196,.020,.044,'jade',y=.024)
        tip=[-.1875,0,z+.355]
    markers={'Grip_Primary':[0,0,0],'Grip_Secondary':[0,0,.355],'Tip':tip}
    return {'name':code,'category':'AxeTwoHands','length_m':round(tip[2]+.268,3),
            'markers':markers,'notes':'Two unobstructed hand zones; solid metal socket; closed blade surfaces; asymmetric counterblade on H2-C.'}
