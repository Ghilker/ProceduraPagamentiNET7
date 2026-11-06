import json,pathlib
from pypdf import PdfReader
root=pathlib.Path(r'D:\source\repos\ProcedureNet7\work\domicilio-review-20261103')
out=root/'sample-audit'; out.mkdir(exist_ok=True)
ids={17465,17522,17552,17437,17763,17770,17616,17624,17713,17580,18027,18115}
manifest=json.loads((root/'pdf-manifest.json').read_text(encoding='utf-8-sig'))
summary=[]
for f in manifest['Files']:
    matches=ids.intersection(f['RequestIds'])
    if not matches or f['State']!='DOWNLOADED': continue
    pdf=PdfReader(f['Path']); entries=[]
    for n,page in enumerate(pdf.pages,1):
        text=page.extract_text() or ''
        (out/f"{min(matches)}_p{n}.txt").write_text(text,encoding='utf-8')
        entries.append({'page':n,'chars':len(text.strip()),'images':len(page.images)})
    summary.append({'request':min(matches),'attachment':f['AttachmentId'],'path':f['Path'],'pages':entries})
(out/'extraction.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(summary,ensure_ascii=False))