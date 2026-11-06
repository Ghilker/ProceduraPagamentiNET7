import json, re, datetime
from pathlib import Path
from collections import Counter

base = Path(__file__).parent
docs = json.loads((base/'date-documents.json').read_text(encoding='utf-8'))
analysis = {x['id']:x for x in json.loads((base/'analysis.json').read_text(encoding='utf-8'))['requests']}
date_pattern = r'\d{1,2}/\d{1,2}/\d{4}'
def key(year,series,number):
    return f'{int(year)}|{series.upper()}|{int(number)}'

results = []
for r in docs:
    contract_ops = [op for op in analysis[r['id']]['operations'] if op['ENTITA'] == 'CONTRATTO']
    primary = next(op for op in contract_ops if any(c in op['CODICI_ANOMALIA'].split(' | ') for c in r['codes']))
    proposed = primary['DATI_PROPOSTI']
    expected_end = re.search(r'\bFine=(' + date_pattern + ')',proposed).group(1)
    registrations, extensions = [], []
    for d in r['documents']:
        for page in d['pages']:
            t = re.sub(r'\s+',' ',page['text'])
            for m in re.finditer(r"Il contratto e['’]? stato registrato il ("+date_pattern+r') al n\.?\s*([0-9]+)\s*[-–]?\s*serie\s*([A-Z0-9]+)',t,re.I):
                registrations.append({'key':key(m[1][-4:],m[3],m[2]),'date':m[1], 'attachment':d['attachment'], 'path':d['path'], 'page':page['page']})
            endings = re.findall(r'proroga di un contratto\s*di locazione fino alla data ('+date_pattern+')',t,re.I)
            links = re.findall(r'anno\s*(\d{4})\s*serie\s*([A-Z0-9]+)\s*n\.?\s*(\d+)',t,re.I)
            for end in endings:
                extensions.append({'end':end,'links':[key(*v) for v in links], 'attachment':d['attachment'], 'path':d['path'], 'page':page['page']})
    registration_keys = set(x['key'] for x in registrations)
    matching = [x for x in extensions if x['end'] == expected_end and len(x['links']) == 1 and x['links'][0] in registration_keys]
    only_end = r['codes'] == ['PDF_012']
    if only_end and matching:
        category = 'SCADENZA_CONFERMATA_DA_PROROGA_NEL_PDF'
        action = 'Migliorare lettura e collegamento delle ricevute; lasciare invariata la data dichiarata.'
    elif matching:
        category = 'SCADENZA_CONFERMATA_MA_ALTRI_CAMPI_DA_VALUTARE'
        action = 'Conservare la scadenza documentata; valutare separatamente gli altri dati discordanti.'
    elif r['id'] == 22634:
        category = 'RINNOVO_CONTRATTUALE_DA_VALIDARE'
        action = 'Il testo contiene clausole di rinnovo, ma non la ricevuta della proroga con la scadenza dichiarata; verifica operatore.'
    else:
        category = 'DISCORDANZA_DATI_DA_VALUTARE'
        action = 'Proporre eventuale correzione solo dopo verifica del contesto e nuova validazione completa.'
    results.append({'id':r['id'],'codes':r['codes'],'category':category,'proposed_data':proposed,
        'declared_end':expected_end,'current_finding':r['reason'],'registrations':registrations,
        'extensions':extensions,'matching_extension_pages':matching,'proposed_action':action,
        'data_changes_applied':False})

out={'scope':'29 istanze con discordanza di date nel gruppo puro; lettura diretta dei PDF',
     'counts':dict(Counter(x['category'] for x in results)), 'items':results}
(base/'date-pdf-review.json').write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(out['counts'],ensure_ascii=False))
for x in results:
    print(x['id'],x['category'], 'declared_end='+x['declared_end'], 'proof='+','.join(f"{p['attachment']}:p{p['page']}" for p in x['matching_extension_pages']))
