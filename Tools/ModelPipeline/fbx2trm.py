"""Convert the user's tractor FBX into TractorModel.bytes for the game (runtime-loaded, no Unity importer).

Output coordinates are Unity's (left-handed, metres): x = -x_fbx, y up, z forward (front of tractor +Z).
Origin = ground under the midpoint between the axles.
"""
import sys, math, struct, gzip, json
import numpy as np
sys.path.insert(0, '/home/claude/tools')
from fbxparse import parse

src, out = sys.argv[1], sys.argv[2]
ver, nodes = parse(src)
top = {n['name']: n for n in nodes}
objs = {c['props'][0]: c for c in top['Objects']['children']}
parent, geo_of = {}, {}
for c in top['Connections']['children']:
    t, a, b = c['props'][0], c['props'][1], c['props'][2]
    if t != 'OO' or a not in objs or b not in objs: continue
    if objs[a]['name'] == 'Model' and objs[b]['name'] == 'Model': parent[a] = b
    if objs[a]['name'] == 'Geometry' and objs[b]['name'] == 'Model': geo_of.setdefault(b, []).append(a)

def p70(n):
    o = {}
    for c in n['children']:
        if c['name'] == 'Properties70':
            for p in c['children']: o[p['props'][0]] = p['props'][4:]
    return o

def name(k): return objs[k]['props'][1].split('\x00')[0]

def euler(r):
    rx, ry, rz = [math.radians(v) for v in r]
    X = np.array([[1, 0, 0], [0, math.cos(rx), -math.sin(rx)], [0, math.sin(rx), math.cos(rx)]])
    Y = np.array([[math.cos(ry), 0, math.sin(ry)], [0, 1, 0], [-math.sin(ry), 0, math.cos(ry)]])
    Z = np.array([[math.cos(rz), -math.sin(rz), 0], [math.sin(rz), math.cos(rz), 0], [0, 0, 1]])
    return Z @ Y @ X

def local(k):
    p = p70(objs[k])
    T = np.eye(4)
    R = euler(p.get('PreRotation', [0, 0, 0])) @ euler(p.get('Lcl Rotation', [0, 0, 0]))
    S = np.diag(p.get('Lcl Scaling', [1, 1, 1]))
    T[:3, :3] = R @ S
    T[:3, 3] = p.get('Lcl Translation', [0, 0, 0])
    G = np.eye(4)
    G[:3, 3] = p.get('GeometricTranslation', [0, 0, 0])
    return T, G

def world(k):
    T, _ = local(k)
    return world(parent[k]) @ T if k in parent else T

def child(g, nm):
    for c in g['children']:
        if c['name'] == nm: return c
    return None

def layer(g, lname, key, comps):
    L = child(g, lname)
    if L is None: return None, None, None
    mapping = child(L, 'MappingInformationType')['props'][0]
    ref = child(L, 'ReferenceInformationType')['props'][0]
    data = np.array(child(L, key)['props'][0], dtype=np.float64).reshape(-1, comps)
    idx = child(L, key + 'Index') or child(L, key + 'sIndex')
    idx = np.array(idx['props'][0]) if idx is not None else None
    return mapping, ref, (data, idx)

# ---------------------------------------------------------------- part classification
REAR_WHEEL = {'Truck.062', 'Truck.006', 'Truck.007'}
FRONT_WHEEL = {'Truck.008', 'Truck.011'}
TROLLEY_WHEEL = {'Truck.005', 'Truck.065'}
TROLLEY_CHASSIS = {'back.002', 'back.003', 'back.004'}
def group_of(nm, cx):
    if nm in REAR_WHEEL: return 3 if cx < 0 else 4          # RL / RR   (Unity x)
    if nm in FRONT_WHEEL: return 1 if cx < 0 else 2         # FL / FR
    if nm in TROLLEY_WHEEL: return 7
    if nm in TROLLEY_CHASSIS: return 5
    if nm.startswith('back'): return 6
    return 0

# ---------------------------------------------------------------- collect triangles
tris = {}   # (group, tile) -> list of (pos[3x3], nrm[3x3], uv[3x2])
cm = 0.01
for k, o in objs.items():
    if o['name'] != 'Model' or k not in geo_of: continue
    W = world(k); _, G = local(k); M = W @ G
    nm = name(k)
    for gk in geo_of[k]:
        g = objs[gk]
        V = np.array(child(g, 'Vertices')['props'][0]).reshape(-1, 3)
        PVI = np.array(child(g, 'PolygonVertexIndex')['props'][0])
        Vw = (np.c_[V, np.ones(len(V))] @ M.T)[:, :3] * cm
        Vw[:, 0] *= -1                                          # to Unity (left-handed)
        Nm = M[:3, :3]; Ninv = np.linalg.inv(Nm).T
        nmap, nref, (ndata, nidx) = layer(g, 'LayerElementNormal', 'Normals', 3)
        umap, uref, (udata, uidx) = layer(g, 'LayerElementUV', 'UV', 2)
        # polygon-vertex streams
        cp = np.where(PVI < 0, ~PVI, PVI)
        n_pv = len(PVI)
        def stream(mapping, ref, data, idx):
            if mapping == 'ByPolygonVertex':
                return data[idx] if ref == 'IndexToDirect' else data[:n_pv]
            if mapping in ('ByVertice', 'ByVertex'):
                return (data[idx] if ref == 'IndexToDirect' else data)[cp]
            raise SystemExit('unsupported mapping ' + mapping)
        N = stream(nmap, nref, ndata, nidx) @ Ninv.T
        N[:, 0] *= -1
        N /= np.maximum(np.linalg.norm(N, axis=1, keepdims=True), 1e-9)
        UV = stream(umap, uref, udata, uidx)
        P = Vw[cp]
        # polygons -> fan triangles
        ends = np.where(PVI < 0)[0]
        start = 0
        cx = P[:, 0].mean()
        for e in ends:
            poly = list(range(start, e + 1)); start = e + 1
            for i in range(1, len(poly) - 1):
                a, b, c = poly[0], poly[i], poly[i + 1]
                cen = (P[a] + P[b] + P[c]) / 3
                grp = group_of(nm, cen[0])
                tile = int(min(4, max(0, math.floor((UV[a, 0] + UV[b, 0] + UV[c, 0]) / 3))))
                # mirroring X flips handedness, so the original order is already Unity's clockwise front face
                tris.setdefault((grp, tile), []).append((a, b, c, id(P)))
            tris.setdefault('_streams', {})[id(P)] = (P, N, UV)

streams = tris.pop('_streams')

# ---------------------------------------------------------------- measurements (Unity metres)
def bbox(names):
    pts = []
    for k, o in objs.items():
        if o['name'] == 'Model' and name(k) in names and k in geo_of:
            W = world(k); _, G = local(k)
            for gk in geo_of[k]:
                V = np.array(child(objs[gk], 'Vertices')['props'][0]).reshape(-1, 3)
                Vw = (np.c_[V, np.ones(len(V))] @ (W @ G).T)[:, :3] * cm
                Vw[:, 0] *= -1
                pts.append(Vw)
    P = np.concatenate(pts)
    return P.min(0), P.max(0)

def axle(nm):
    mn, mx = bbox({nm})
    c = (mn + mx) / 2
    return np.array([0, c[1], c[2]]), (mx[1] - mn[1]) / 2, mn, mx

rearC, rearR, rmn, rmx = axle('Truck.062')
frontC, frontR, fmn, fmx = axle('Truck.008')
trolC, trolR, tmn, tmx = axle('Truck.005')
ground = min(rearC[1] - rearR, frontC[1] - frontR)
origin = np.array([0, ground, (rearC[2] + frontC[2]) / 2])
bodyMn, bodyMx = bbox({name(k) for k, o in objs.items() if o['name'] == 'Model' and k in geo_of and group_of(name(k), 0) == 0})
bedMn, bedMx = bbox({n for n in (name(k) for k, o in objs.items() if o['name'] == 'Model') if n.startswith('back') and n not in TROLLEY_CHASSIS})
dbMn, dbMx = bbox({'back.004'})
pipeMn, pipeMx = bbox({'Truck.014'})
hoodMn, hoodMx = bbox({'Cube'})
seatMn, seatMx = bbox({'Truck.056'})
wheelMn, wheelMx = bbox({'Truck.046'})

eye = np.array([0, ground + 0.5, dbMx[2]])                     # trolley hitch eye = front end of its drawbar
bedPivot = np.array([0, bedMn[1], bedMn[2]])                   # tipping hinge at the rear bottom edge
info = {
    'rearRadius': rearR, 'frontRadius': frontR,
    'wheelBase': frontC[2] - rearC[2],
    'rearTrack': (rmx[0] - rmn[0]) - 0.42, 'frontTrack': (fmx[0] - fmn[0]) - 0.20,
    'rearWidth': 0.42, 'frontWidth': 0.20,
    'length': bodyMx[2] - bodyMn[2], 'width': bodyMx[0] - bodyMn[0], 'height': bodyMx[1] - ground,
    'rearZ': bodyMn[2] - origin[2],
    'hitchZ': eye[2] - origin[2],
    'exhaustX': (pipeMn[0] + pipeMx[0]) / 2, 'exhaustY': pipeMx[1] - ground + 0.02, 'exhaustZ': (pipeMn[2] + pipeMx[2]) / 2 - origin[2],
    'hoodX': hoodMx[0] + 0.006, 'hoodY': (hoodMn[1] + hoodMx[1]) / 2 - ground + 0.1, 'hoodZ': (hoodMn[2] + hoodMx[2]) / 2 + 0.35 - origin[2],
    'seatY': seatMn[1] - ground + 0.06, 'seatZ': (seatMn[2] + seatMx[2]) / 2 + 0.12 - origin[2],
    'steerY': (wheelMn[1] + wheelMx[1]) / 2 - ground, 'steerZ': (wheelMn[2] + wheelMx[2]) / 2 - origin[2],
    'trolleyAxleZ': trolC[2] - eye[2], 'trolleyWheelRadius': trolR, 'trolleyTrack': (tmx[0] - tmn[0]) - 0.2,
    'trolleyBedFrontZ': bedMx[2] - eye[2], 'trolleyBedLength': bedMx[2] - bedMn[2], 'trolleyBedWidth': bedMx[0] - bedMn[0],
    'trolleyFloorY': bedMn[1] - ground, 'trolleySideHeight': bedMx[1] - bedMn[1],
}

def pivot_for(grp):
    if grp == 0: return origin
    if grp in (1, 2): return np.array([(fmn[0] + fmx[0]) / 2 * 0 + (fmn[0] if grp == 1 else fmx[0]) + (0.1 if grp == 1 else -0.1), frontC[1], frontC[2]])
    if grp in (3, 4): return np.array([(rmn[0] if grp == 3 else rmx[0]) + (0.21 if grp == 3 else -0.21), rearC[1], rearC[2]])
    if grp == 5: return eye
    if grp == 6: return bedPivot
    if grp == 7: return np.array([0, trolC[1], trolC[2]])

# ---------------------------------------------------------------- weld + write
GROUPS = ['Body', 'Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR', 'TrolleyChassis', 'TrolleyBed', 'TrolleyWheels']
parts = []
for (grp, tile), tl in sorted(tris.items()):
    piv = pivot_for(grp)
    keymap = {}; pos = []; nrm = []; uvs = []; idx = []
    for a, b, c, sid in tl:
        P, N, UV = streams[sid]
        for v in (a, b, c):
            p = P[v] - piv; n = N[v]; u = UV[v]
            key = (round(p[0], 4), round(p[1], 4), round(p[2], 4), round(n[0], 2), round(n[1], 2), round(n[2], 2), round(u[0], 4), round(u[1], 4))
            j = keymap.get(key)
            if j is None:
                j = len(pos); keymap[key] = j
                pos.append(p); nrm.append(n); uvs.append((u[0] - tile, u[1]))
            idx.append(j)
    parts.append((GROUPS[grp], grp, tile, piv - (origin if grp <= 4 else eye) if grp != 6 else piv - eye, np.array(pos, np.float32), np.array(nrm, np.float32), np.array(uvs, np.float32), np.array(idx, np.uint32)))

buf = bytearray()
buf += b'TRM1'
keys = list(info.keys())
buf += struct.pack('<i', len(keys))
for k in keys:
    kb = k.encode(); buf += struct.pack('<B', len(kb)) + kb + struct.pack('<f', float(info[k]))
buf += struct.pack('<i', len(parts))
tv = ti = 0
for nm, grp, tile, piv, pos, nrm, uvs, idx in parts:
    nb = nm.encode()
    buf += struct.pack('<B', len(nb)) + nb + struct.pack('<BB', grp, tile) + struct.pack('<3f', *piv)
    buf += struct.pack('<ii', len(pos), len(idx))
    buf += pos.tobytes()
    buf += np.clip(np.round(nrm * 127), -127, 127).astype(np.int8).tobytes()
    buf += uvs.astype(np.float16).tobytes()
    buf += idx.astype(np.uint32).tobytes()
    tv += len(pos); ti += len(idx)
with gzip.open(out, 'wb', 9) as f: f.write(bytes(buf))
json.dump({k: round(float(v), 4) for k, v in info.items()}, open(out + '.json', 'w'), indent=1)
print('parts', len(parts), 'verts', tv, 'tris', ti // 3, 'bytes', len(buf))
for k in keys: print('  %-20s %.3f' % (k, info[k]))
