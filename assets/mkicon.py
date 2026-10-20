from PIL import Image, ImageDraw, ImageFilter
S=1024
img=Image.new('RGBA',(S,S),(0,0,0,0))
# rounded square background with vertical gradient
bg=Image.new('RGBA',(S,S))
d=ImageDraw.Draw(bg)
for y in range(S):
    t=y/S
    c=(int(30+(14-30)*t),int(35+(16-35)*t),int(46+(22-46)*t),255)
    d.line([(0,y),(S,y)],fill=c)
mask=Image.new('L',(S,S),0)
ImageDraw.Draw(mask).rounded_rectangle([40,40,S-40,S-40],radius=210,fill=255)
img.paste(bg,(0,0),mask)
d=ImageDraw.Draw(img)
d.rounded_rectangle([40,40,S-40,S-40],radius=210,outline=(52,58,72,255),width=10)
# shield
cx=S//2
shield=[(cx,170),(cx+300,265),(cx+290,520),(cx+220,700),(cx,860),(cx-220,700),(cx-290,520),(cx-300,265)]
glow=Image.new('RGBA',(S,S),(0,0,0,0))
ImageDraw.Draw(glow).polygon(shield,fill=(242,163,60,120))
glow=glow.filter(ImageFilter.GaussianBlur(40))
img=Image.alpha_composite(img,glow)
d=ImageDraw.Draw(img)
d.polygon(shield,fill=(242,163,60,255))
inner=[(cx,225),(cx+245,305),(cx+237,515),(cx+178,670),(cx,805),(cx-178,670),(cx-237,515),(cx-245,305)]
d.polygon(inner,fill=(23,26,33,255))
# power symbol
r=150; cy=530; w=46
d.arc([cx-r,cy-r,cx+r,cy+r],start=-55,end=235,fill=(242,163,60,255),width=w)
d.rounded_rectangle([cx-w//2,cy-r-40,cx+w//2,cy+10],radius=w//2,fill=(242,163,60,255))
img.save('app_1024.png')
sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)]
img.save('app.ico',sizes=sizes)
img.resize((256,256),Image.LANCZOS).save('app_256.png')
print('ok')
