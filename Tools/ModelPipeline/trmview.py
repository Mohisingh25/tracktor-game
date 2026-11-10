import gzip, struct, sys, numpy as np, math
from PIL import Image, ImageDraw
d=gzip.open(sys.argv[1]).read(); o=0
assert d[:4]==b'TRM1'; o=4
n,=struct.unpack_from('<i',d,o); o+=4; info={}
for _ in range(n):
    l=d[o]; o+=1; k=d[o:o+l].decode(); o+=l; info[k],=struct.unpack_from('<f',d,o); o+=4
np_,=struct.unpack_from('<i',d,o); o+=4; parts=[]
for _ in range(np_):
    l=d[o]; o+=1; nm=d[o:o+l].decode(); o+=l; grp,tile=d[o],d[o+1]; o+=2
    piv=np.array(struct.unpack_from('<3f',d,o)); o+=12
    nv,ni=struct.unpack_from('<ii',d,o); o+=8
    pos=np.frombuffer(d,np.float32,nv*3,o).reshape(-1,3); o+=nv*12
    nrm=np.frombuffer(d,np.int8,nv*3,o).reshape(-1,3)/127.; o+=nv*3
    uv=np.frombuffer(d,np.float16,nv*2,o).reshape(-1,2).astype(np.float32); o+=nv*4
    idx=np.frombuffer(d,np.uint32,ni,o).reshape(-1,3); o+=ni*4
    parts.append((nm,grp,tile,piv,pos,nrm,uv,idx))
assert o==len(d), (o,len(d))
mode=sys.argv[3] if len(sys.argv)>3 else 'tex'
tex={}
if mode=='tex':
    for t in range(5): tex[t]=np.asarray(Image.open('/home/claude/blendwork/textures_2k/vu_Base_color_%d.jpg'%(1001+t)).convert('RGB').resize((512,512)))/255.
eyeZ=info['hitchZ']
cols=[(0.75,0.75,0.78),(0.9,0.2,0.2),(0.2,0.9,0.2),(0.9,0.6,0.1),(0.2,0.5,0.95),(0.6,0.3,0.8),(0.8,0.7,0.3),(0.2,0.9,0.9)]
az=math.radians(float(sys.argv[4]) if len(sys.argv)>4 else 35); el=math.radians(18)
W,H=1400,900
allT=[];allC=[];allD=[]
L=np.array([0.4,0.8,-0.45]); L/=np.linalg.norm(L)
for nm,grp,tile,piv,pos,nrm,uv,idx in parts:
    off = piv if grp<=4 else piv+np.array([0,0,eyeZ])   # trolley parts are relative to the hitch eye
    P=pos+off
    tri=P[idx]
    cen=tri.mean(1)
    nf=np.cross(tri[:,1]-tri[:,0],tri[:,2]-tri[:,0])  # Unity: clockwise front face (left-handed)
    if mode=='tex':
        u=uv[idx].mean(1); px=np.clip((u[:,0]%1)*511,0,511).astype(int); py=np.clip((1-u[:,1]%1)*511,0,511).astype(int)
        c=tex[tile][py,px]
    else: c=np.tile(cols[grp],(len(idx),1))
    nv_=nrm[idx].mean(1); nv_/=np.maximum(np.linalg.norm(nv_,axis=1,keepdims=True),1e-6)
    sh=0.35+0.65*np.clip(nv_@L,0,1)
    allT.append(tri); allC.append(c*sh[:,None])
T=np.concatenate(allT); C=np.concatenate(allC)
# camera: orbit around (0,1.1,-1.5)
tgt=np.array([0,1.0,-1.4]); dist=9.5
cam=tgt+dist*np.array([math.sin(az)*math.cos(el),math.sin(el),math.cos(az)*math.cos(el)])
f=tgt-cam; f/=np.linalg.norm(f); r=np.cross([0,1,0],f); r/=np.linalg.norm(r); up=np.cross(f,r)
rel=T-cam; x=rel@r; y=rel@up; z=rel@f
fov=1/math.tan(math.radians(28)); sx=W/2+x/z*fov*W/2; sy=H/2-y/z*fov*W/2
depth=z.mean(1); order=np.argsort(-depth)
im=Image.new('RGB',(W,H),(185,205,225)); dr=ImageDraw.Draw(im)
# ground
for i in order:
    c=tuple((np.clip(C[i],0,1)*255).astype(int)); dr.polygon([(sx[i,0],sy[i,0]),(sx[i,1],sy[i,1]),(sx[i,2],sy[i,2])],fill=c)
im.save(sys.argv[2]); print(info['wheelBase'], len(T))
