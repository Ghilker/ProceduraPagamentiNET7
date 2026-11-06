import json,pathlib
import pypdfium2 as pdfium
from PIL import Image,ImageDraw
root=pathlib.Path(r'D:\source\repos\ProcedureNet7\work\domicilio-review-20261103\sample-audit')
entries=json.loads((root/'extraction.json').read_text(encoding='utf-8'))
for e in entries:
    doc=pdfium.PdfDocument(e['path']); thumbs=[]
    selected={0,1,2,len(doc)-1}
    if e['request']==17713: selected.add(3)
    if e['request']==18115: selected.update({7,8,9})
    for i in range(len(doc)):
        page=doc[i]; img=page.render(scale=1.25 if i in selected else .35).to_pil().convert('RGB')
        if i in selected: img.save(root/f"{e['request']}_p{i+1}.png")
        img.thumbnail((250,340)); tile=Image.new('RGB',(270,370),'white'); tile.paste(img,((270-img.width)//2,24)); ImageDraw.Draw(tile).text((8,5),f"{e['request']} p{i+1}",fill='black'); thumbs.append(tile)
    cols=4 if len(thumbs)>4 else len(thumbs); rows=(len(thumbs)+cols-1)//cols
    sheet=Image.new('RGB',(cols*270,rows*370),(220,220,220))
    for i,tile in enumerate(thumbs):sheet.paste(tile,((i%cols)*270,(i//cols)*370))
    sheet.save(root/f"{e['request']}_contact.png")
print('Rendered selected pages and contact sheets for',len(entries),'sample PDFs')