# 새 그림: 밝은 단색 배경을 투명하게 (외곽 반투명 + 회색 번짐 제거)
from PIL import Image
import numpy as np
from scipy import ndimage as ndi
a=np.asarray(Image.open('jukebox_source.webp').convert('RGB')).astype(np.float32)
H,W=a.shape[:2]
border=np.concatenate([a[0],a[-1],a[:,0],a[:,-1]])
bg=np.median(border,0)
d=np.sqrt(((a-bg)**2).sum(2))
sat=a.max(2)-a.min(2); lum=a.mean(2)
cand=(d<26)|((sat<14)&(lum>135))
seed=np.zeros((H,W),bool); seed[0,:]=seed[-1,:]=seed[:,0]=seed[:,-1]=True
seed&=cand
bgm=ndi.binary_propagation(seed,mask=cand)
fg=~bgm
fg=ndi.binary_fill_holes(fg)
fg=ndi.binary_opening(fg,iterations=1)
# 외곽 1~2px 띠에서만 반투명 알파 계산
edge=fg & ~ndi.binary_erosion(fg,iterations=2)
alpha=fg.astype(np.float32)
bgl=ndi.grey_dilation(np.where(bgm,lum,0),size=7)
alpha[edge]=np.clip((bgl[edge]-lum[edge])/70,0,1)
alpha=np.maximum(alpha, ndi.binary_erosion(fg,iterations=2).astype(np.float32))
# 회색 배경이 섞인 외곽 색을 되돌림
m=(alpha>0.02)&(alpha<1)
c=a.copy()
bgc=np.dstack([bgl]*3)
c[m]=np.clip((a[m]-(1-alpha[m,None])*bgc[m])/alpha[m,None],0,255)
out=np.dstack([c,alpha*255]).astype(np.uint8)
ys,xs=np.where(alpha>0.03)
box=(xs.min()-2,ys.min()-2,xs.max()+3,ys.max()+3)
img=Image.fromarray(out,'RGBA').crop(box)
img.save('cut2.png')
print('bg',bg,'box',box,img.size)
for col,n in [((40,90,160),'b'),((20,20,20),'k'),((255,255,255),'w')]:
    b=Image.new('RGBA',img.size,col+(255,)); b.alpha_composite(img); b.convert('RGB').save('cut2_'+n+'.png')
