import json, re
from pathlib import Path
from collections import Counter
from pypdf import PdfReader

base = Path(__file__).parent
analysis = json.loads((base/'analysis.json').read_text(encoding='utf-8'))
manifest = json.loads((base/'pdf-manifest.json').read_text(encoding='utf-8-sig'))
date_codes = {'PDF_010','PDF_011','PDF_012','PDF_013','PDF_014','PDF_015'}
selected = [r for r in analysis['requests'] if r['category'] == 'Solo date o estremi discordanti' and set(r['codes']) & date_codes]
result = []
for request in selected:
    docs = []
    for f in manifest['Files']:
        if request['id'] not in f['RequestIds'] or f['State'] != 'DOWNLOADED':
            continue
        reader = PdfReader(f['Path'])
        pages = []
        for n, page in enumerate(reader.pages, 1):
            text = page.extract_text() or ''
            lines = text.splitlines()
            relevant = [s.strip() for s in lines if re.search(r'durata|prorog|scadenz|cessaz|periodo|annualit|registrat|protocollo|decorren|\bdal\b.*\bal\b',s,re.I)]
            pages.append({'page':n,'text':text,'date_lines':relevant})
        docs.append({'attachment':f['AttachmentId'],'path':f['Path'],'pages':pages})
    result.append({'id':request['id'],'codes':request['codes'],'reason':request['reason'],'documents':docs})
(base/'date-documents.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'date_requests':len(selected),'with_downloaded_pdf':sum(bool(x['documents']) for x in result),
    'pdfs':sum(len(x['documents']) for x in result),'pages':sum(len(d['pages']) for x in result for d in x['documents'])},ensure_ascii=False))
for x in result:
    if x['id'] in (17840,19267,19018,22297,23035):
        print('ISTANZA',x['id'],x['reason'])
        for d in x['documents']:
            for p in d['pages']:
                for line in p['date_lines'][:10]:
                    print('ALLEGATO',d['attachment'],'PAG',p['page'],line[:220])
