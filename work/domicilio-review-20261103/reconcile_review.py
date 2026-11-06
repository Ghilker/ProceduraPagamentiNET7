import json
from pathlib import Path
from collections import Counter

root = Path(__file__).parent
refs = json.loads((root/'ref-pdf-review.json').read_text(encoding='utf-8'))
dates = json.loads((root/'date-pdf-review.json').read_text(encoding='utf-8'))
samples = json.loads((root/'sample-audit/findings.json').read_text(encoding='utf-8-sig'))
manifest = json.loads((root/'pdf-manifest.json').read_text(encoding='utf-8-sig'))
items = []
for r in refs['items']:
    recovered = r['result'] == 'EQUIVALENZA_DOCUMENTATA'
    items.append({'id':r['id'],'review':r['result'], 'parser_recovery_candidate':recovered,
        'data_change_applied':False,'evidence':r['provenance']})
for d in dates['items']:
    recovered = d['category'] == 'SCADENZA_CONFERMATA_DA_PROROGA_NEL_PDF'
    items.append({'id':d['id'],'review':d['category'], 'parser_recovery_candidate':recovered,
        'data_change_applied':False,'declared_end':d['declared_end'],
        'evidence':d['matching_extension_pages'], 'reason':d['current_finding']})
assert len(items)==127 and len(set(r['id'] for r in items))==127
assert sum(x['parser_recovery_candidate'] for x in items)==108
assert all(x['State']=='DOWNLOADED' and Path(x['Path']).exists() for x in manifest['Files'])
summary={'server':'10.9.20.70','database':'ADISU','access':'SELECT_ONLY',
    'source_csv':r'C:\Users\giacomo_pavone\Desktop\Lavorazione_Automatica_Domicilio_20262027_LAVORAZIONE_20261103_093012.csv',
    'requests_checked_in_database':len(manifest['Requests']), 'pdfs_downloaded':len(manifest['Files']),
    'primary_requests_reviewed':len(items),'parser_recovery_candidates':108,
    'reference_equivalence_candidates':90, 'extension_end_candidates':18,
    'remaining_primary_requests':19, 'additional_sample_requests':12,
    'interpretation':'Candidati alla successiva simulazione e rivalidazione completa; nessun accoglimento certificato dal solo report.',
    'sql_writes':0,'data_corrections_applied':0,'automatic_acceptances':0,
    'items':sorted(items,key=lambda x:x['id'])}
(root/'review-conclusions.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in summary.items() if k not in ('source_csv','items')},ensure_ascii=False,indent=2))
