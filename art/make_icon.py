# Generates the BluntyBuds "back-to-back B" icon (mirrored B | B, spines touching). No fonts used.
from PIL import Image, ImageDraw
BG=(26,28,33,255); ACC=(61,214,181,255); S=1024

def d_shape(dr, x0, y0, x1, y1, fill):
    """'D' bowl: flat left side at x0, rounded right corners ending at x1."""
    r=int(min((y1-y0)/2, (x1-x0)*0.62))
    dr.rounded_rectangle([x0,y0,x1,y1], radius=max(r,1), fill=fill, corners=(False,True,True,False))

def half_b(size, t, top, bot, mid, wtop, wbot):
    """Right-facing B glyph on transparent canvas, spine at x=0..t."""
    im=Image.new("RGBA",(size,size),(0,0,0,0)); dr=ImageDraw.Draw(im)
    st=max(1,int(t*0.62))   # half of the shared centre stem
    dr.rectangle([0,top,st,bot], fill=ACC)
    d_shape(dr,0,top,wtop,mid+t//2,ACC);       d_shape(dr,st,top+t,wtop-t,mid-t//2,(0,0,0,0))
    d_shape(dr,0,mid-t//2,wbot,bot,ACC);       d_shape(dr,st,mid+t//2,wbot-t,bot-t,(0,0,0,0))
    return im

def icon(size=S, bg=True, scale=1.0):
    im=Image.new("RGBA",(size,size),(0,0,0,0))
    if bg: ImageDraw.Draw(im).rounded_rectangle([0,0,size-1,size-1], radius=int(size*0.22), fill=BG)
    k=size/1024*scale; t=max(2,int(92*k)); top=int(size/2-340*k); bot=int(size/2+340*k); mid=int(size/2-25*k)
    b=half_b(size,t,top,bot,mid,int(285*k),int(325*k))
    cx=size//2
    im.alpha_composite(b,(cx,0))                         # normal B, spine starts at centre
    im.alpha_composite(b.transpose(Image.FLIP_LEFT_RIGHT),(cx-size,0))  # mirrored B, spine ends at centre
    return im

big=icon(); big.save("icon-1024.png")
big.save("bluntybuds.ico", sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
# Android adaptive: foreground 432px (safe zone 66%) on transparent; background colour separately
fg=icon(432,bg=False,scale=0.8); fg.save("ic_launcher_foreground.png")
for d,px in {"mdpi":48,"hdpi":72,"xhdpi":96,"xxhdpi":144,"xxxhdpi":192}.items():
    icon(px).save(f"ic_launcher_{d}.png") if px>=64 else icon(256).resize((px,px),Image.LANCZOS).save(f"ic_launcher_{d}.png")
icon(512).save("icon-512.png")
print("ok")
