from PIL import Image, ImageDraw, ImageFilter, ImageChops
S=1024
def bez(p0,p1,p2,p3,n=80):
    out=[]
    for i in range(n+1):
        t=i/n;u=1-t
        out.append((u**3*p0[0]+3*u*u*t*p1[0]+3*u*t*t*p2[0]+t**3*p3[0], u**3*p0[1]+3*u*u*t*p1[1]+3*u*t*t*p2[1]+t**3*p3[1]))
    return out
pad=0.06; k=S*(1-2*pad)/64; off=S*pad
def sc(p): return [(off+x*k,off+y*k) for x,y in p]
up=sc(bez((31,30),(18,4),(1,6),(5,26))+bez((5,26),(7,37),(20,37),(31,32)))
lo=sc(bez((31,34),(20,36),(8,44),(13,55))+bez((13,55),(20,62),(29,50),(31,38)))
mir=lambda p:[(S-x,y) for x,y in p]
wings=[up,lo,mir(up),mir(lo)]
grad=Image.new('RGBA',(S,S)); gd=ImageDraw.Draw(grad)
for y in range(S):
    t=y/S
    gd.line([(0,y),(S,y)],fill=(int(140+(70-140)*t),int(232+(135-232)*t),int(215+(235-215)*t),255))
mask=Image.new('L',(S,S),0); md=ImageDraw.Draw(mask)
for p in wings: md.polygon(p,fill=255)
clear=Image.new('RGBA',(S,S),(0,0,0,0))
img=clear.copy()
shadow=Image.composite(Image.new('RGBA',(S,S),(5,10,25,150)),clear,mask.filter(ImageFilter.GaussianBlur(22)))
img.alpha_composite(shadow,(0,16))
img.alpha_composite(Image.composite(grad,clear,mask.point(lambda a:a*0.93)))
# soft glass highlight: radial-ish white fading downward, only on wings
hl=Image.new('L',(S,S),0); hd=ImageDraw.Draw(hl)
for y in range(S):
    hd.line([(0,y),(S,y)],fill=max(0,int(110*(1-y/(S*0.55)))))
hl=ImageChops.multiply(hl,mask)
img.alpha_composite(Image.composite(Image.new('RGBA',(S,S),(255,255,255,255)),clear,hl))
d=ImageDraw.Draw(img)
for p in wings: d.line(p+[p[0]],fill=(255,255,255,200),width=int(0.9*k),joint='curve')
cx=S/2
d.ellipse([cx-2.7*k,off+21*k,cx+2.7*k,off+53*k],fill=(18,28,42,255))
d.line([(cx-1*k,off+22*k),(cx-6*k,off+12*k)],fill=(200,235,240,255),width=int(1.3*k))
d.line([(cx+1*k,off+22*k),(cx+6*k,off+12*k)],fill=(200,235,240,255),width=int(1.3*k))
img.save('res/logo_1024.png')
img.save('res/app.ico',sizes=[(s,s) for s in [16,20,24,32,40,48,64,128,256]])
bg=Image.new('RGBA',(S,S),(18,22,32,255)); bg.alpha_composite(img)
row=Image.new('RGB',(256+16+32+16+16,256),(18,22,32))
row.paste(bg.convert('RGB').resize((256,256),Image.LANCZOS),(0,0))
for i,s in enumerate([32,16]):
    row.paste(bg.convert('RGB').resize((s,s),Image.LANCZOS),(272+i*48,100))
row.save('res/preview.png')
