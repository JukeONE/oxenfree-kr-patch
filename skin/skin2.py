# 새 그림 → 설치기 스킨: 버튼 글자 지우기 + Layout.cs 생성
from PIL import Image, ImageFilter
import numpy as np
im=Image.open('cut2.png'); a=np.asarray(im).astype(np.float32).copy()
FACES=[(128,835,167,875),(191,835,231,875),(256,835,295,875),(319,835,359,875),(383,835,423,875)]
LEDY=809
for (x0,y0,x1,y1) in FACES:
    # 면 가장자리 3px 안쪽까지를 기준색으로, 가운데(글자)를 좌우 보간
    X0,X1=x0+4,x1-5
    for y in range(y0+2,y1-2):
        L=a[y,X0-3:X0,:3].mean(0); R=a[y,X1+1:X1+4,:3].mean(0)
        for x in range(X0,X1+1):
            t=(x-X0)/(X1-X0)
            a[y,x,:3]=L*(1-t)+R*t
    # 세로 방향도 부드럽게(위아래 가장자리 행과 이어지게)
    blk=Image.fromarray(a[y0:y1,x0:x1].astype(np.uint8),'RGBA').filter(ImageFilter.GaussianBlur(1.2))
    bb=np.asarray(blk).astype(np.float32)
    a[y0+4:y1-4,x0+4:x1-4]=bb[4:-4,4:-4]
# 켜진 LED(A,B,C)를 꺼진 LED(D) 모양으로
cx=(FACES[3][0]+FACES[3][2])//2; r=17
off=a[LEDY-r:LEDY+r+1, cx-r:cx+r+1].copy()
yy,xx=np.mgrid[-r:r+1,-r:r+1]; w=np.clip((r+0.5-np.sqrt(yy**2+xx**2))/6,0,1)[...,None]
for f in FACES[:3]:
    c=(f[0]+f[2])//2
    reg=a[LEDY-r:LEDY+r+1, c-r:c+r+1]
    reg[:]=off*w+reg*(1-w)
Image.fromarray(a.astype(np.uint8),'RGBA').save('skin2.png',optimize=True)
W,H=im.size
scr=(145,390,400,757)
with open('Layout.cs','w',encoding='utf-8') as f:
    f.write('// skin2.py가 생성한 좌표 (skin.png 기준 픽셀)\nusing System.Drawing;\nstatic class Skin {\n')
    f.write('  public const int Width = %d, Height = %d;\n'%(W,H))
    f.write('  public static readonly Rectangle Screen = new Rectangle(%d, %d, %d, %d);\n'%(scr[0],scr[1],scr[2]-scr[0],scr[3]-scr[1]))
    f.write('  public static readonly Rectangle[] Buttons = {\n')
    for x0,y0,x1,y1 in FACES: f.write('    new Rectangle(%d, %d, %d, %d),\n'%(x0-5,y0-5,x1-x0+10,y1-y0+10))
    f.write('  };\n  public static readonly Rectangle[] Faces = {\n')
    for x0,y0,x1,y1 in FACES: f.write('    new Rectangle(%d, %d, %d, %d),\n'%(x0,y0,x1-x0,y1-y0))
    f.write('  };\n  public static readonly Point[] Leds = {\n')
    for x0,y0,x1,y1 in FACES: f.write('    new Point(%d, %d),\n'%((x0+x1)//2,LEDY))
    f.write('  };\n}\n')
print(W,H)
