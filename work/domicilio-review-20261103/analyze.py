import csv, json, re
from collections import Counter, defaultdict
from pathlib import Path

SOURCE = Path(r'C:\Users\giacomo_pavone\Desktop\Lavorazione_Automatica_Domicilio_20262027_LAVORAZIONE_20261103_093012.csv')
BASE = Path(__file__).parent
rows = list(csv.DictReader(SOURCE.open(encoding='utf-8-sig', newline=''), delimiter=';'))
summaries = {r['NUM_ISTANZA']: r for r in rows if r['TIPO_RIGA'] == 'ISTANZA'}
ops = defaultdict(list)
for r in rows:
    if r['TIPO_RIGA'] == 'OPERAZIONE':
        ops[r['NUM_ISTANZA']].append(r)
assert len(rows) == len(summaries) + sum(map(len, ops.values()))
assert all(len(ops[k]) == int(v['NUM_OPERAZIONI']) for k, v in summaries.items())

def codes(row):
    return set(filter(None, re.split(r'\s*\|\s*', row['CODICI_ANOMALIA'])))

date_codes = {'PDF_010','PDF_011','PDF_012','PDF_013','PDF_014','PDF_015'}
correction_codes = date_codes | {'PDF_017'}
counts = Counter()
combos = Counter()
classified = []
samples = defaultdict(list)
for ident, r in summaries.items():
    if r['AZIONE_ISTANZA'] == 'ACCOLTA':
        continue
    c = codes(r)
    counts.update(c)
    combos[tuple(sorted(c))] += 1
    correction = sorted(c & correction_codes)
    other = sorted(c - correction_codes)
    if r['PRESA_CARICO'] == 'SI':
        category = 'Gia in carico'
    elif 'PDF_006' in c:
        category = 'Almeno un PDF senza testo'
    elif c <= correction_codes and c:
        category = 'Solo date o estremi discordanti'
    elif 'PDF_TEXT_INCOMPLETE' in c or 'PDF_019' in c:
        category = 'PDF parzialmente leggibili'
    elif 'IST_DOC_05' in c or any(x.startswith('DOC_') and x != 'DOC_DA_VALIDARE' for x in c):
        category = 'Documenti mancanti o non disponibili'
    elif any(x.startswith('IST_') for x in c):
        category = 'Regole istanza o dati strutturali'
    elif any(x.startswith('PDF_ENTE') for x in c) or 'PDF_LA_POLICY' in c:
        category = 'Ente o Learning Agreement'
    elif 'PDF_AMBIGUOUS_FIELDS' in c:
        category = 'Piu dati o ricevute nel PDF'
    else:
        category = 'Lettura campi identita e collegamenti'
    item = {'id': int(ident), 'category': category, 'codes': sorted(c), 'correction_codes': correction,
            'other_codes': other, 'reason': r['MOTIVO_AZIONE'], 'claimed': r['PRESA_CARICO'],
            'operations': [{k: v for k,v in op.items() if k not in ('COD_FISCALE','STUDENTE','NUM_DOMANDA')} for op in ops[ident]]}
    classified.append(item)
    if len(samples[category]) < 5:
        samples[category].append(item)

pure = [x for x in classified if x['category'] == 'Solo date o estremi discordanti']
stats = {
    'source': str(SOURCE), 'rows': len(rows), 'requests': len(summaries), 'operations': sum(map(len,ops.values())),
    'outcomes': dict(Counter(x['AZIONE_ISTANZA'] for x in summaries.values())),
    'categories': dict(Counter(x['category'] for x in classified)),
    'code_counts': dict(counts.most_common()),
    'pure_correction_ids': [x['id'] for x in pure],
    'any_date_mismatch': sum(bool(set(x['codes']) & date_codes) for x in classified),
    'any_reference_mismatch': sum('PDF_017' in x['codes'] for x in classified),
    'date_or_reference_mismatch': sum(bool(x['correction_codes']) for x in classified),
    'combinations': [{'count': n, 'codes': list(c)} for c,n in combos.most_common(30)]
}
(BASE / 'analysis.json').write_text(json.dumps({'stats':stats, 'requests':classified}, ensure_ascii=False, indent=2), encoding='utf-8')
(BASE / 'samples.json').write_text(json.dumps({'pure': pure, 'samples':samples},ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(stats, ensure_ascii=False, indent=2))
