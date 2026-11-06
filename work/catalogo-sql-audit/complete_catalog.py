import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
path = ROOT / 'Moduli/Controlli/VerificaMain/Esito/EsitoBorsaMotiviCatalog.cs'
text = path.read_text(encoding='utf-8-sig')
current = {r['Codice']: r for r in json.loads((HERE / 'snapshot/catalogo20262027.json').read_text(encoding='utf-8-sig'))}
used = {r['Codice'] for r in json.loads((HERE / 'snapshot/storicoCodici.json').read_text(encoding='utf-8-sig'))}
new_rules = ['GEN003', 'ISC001', 'ISC008', 'RED011', 'RED086', 'RED031', 'RED033', 'MER001',
             'MER005', 'MER086', 'MER087', 'MER090', 'MER091', 'MER093', 'MER170', 'PA002',
             'PA004', 'CI002', 'ISC009', 'VAR021', 'BS002', 'CI004']
new_codes = {rule: str(225 + i) for i, rule in enumerate(new_rules)}
assert not set(new_codes.values()) & used
overrides = {'PA001': '054', 'BS004': '222'}
descriptions = {
    'MER072': 'Anno di corso calcolato incongruente con l’anno di corso dichiarato',
    'VAR021': 'Revoca per ISEE non presente in banca dati INPS in base ai riferimenti dichiarati nella domanda',
    'VAR030': 'Decadenza per mancata comunicazione delle modalità di pagamento',
}
notes = {
    'GEN003': 'Il 016 riguarda esclusivamente la spedizione cartacea. Nuova causale generale per domanda oltre termine.',
    'ISC001': 'Accorpa anno mancante e anno non valido: 001 e 002 distinguono due sottocasi. Il loader non conserva il valore originale.',
    'ISC008': 'Il 008 nel DB indica anno calcolato minore del dichiarato; non equivale a un anno non classificabile. Il 103 resta associato a MER072.',
    'RED011': 'Il 110 riguarda attestazione assente in INPS, non il valore ISEE calcolato assente o non valido. Il 046 riguarda mezzi di sostentamento.',
    'RED086': 'Il 042 riguarda incongruenza grave e il 121 assenza di attestazione universitaria: coprono solo sottocasi degli stati/fonte non ammessi.',
    'RED031': 'Adeguatezza/importazione della fonte CO del nucleo di origine: il 121 non descrive tutti i casi coperti dalla regola.',
    'RED033': 'Integrazione del nucleo di origine non importata entro il 31 dicembre. Il precedente candidato 223 è già occupato da un titolo estero.',
    'MER001': 'Il 022 dichiara crediti pari a zero, il 024 un errore generico legacy. La regola corrente comprende dati mancanti/non calcolabili.',
    'MER005': 'La regola applica limiti assoluti per durata/tipologia del corso; il 040 riguarda il limite dell’anno indicato dall’ateneo e il 204 il limite standard DPCM.',
    'MER086': 'Il 207 riguarda documentazione dei bonus precedenti mancante, non il divieto per crediti riconosciuti/ripetenza.',
    'MER087': 'Il 067 riguarda mancata indicazione dell’utilizzo del bonus, non il divieto per carriera pregressa CD/AT.',
    'MER090': 'Causale specifica della scadenza del 10 febbraio. Il precedente candidato 224 è già occupato da dottorato/specializzazione con titolo triennale.',
    'MER091': 'Titolo di accesso mancante, distinto dal titolo non ammesso MER089/120.',
    'MER093': 'Combinazione di due titoli non rappresentata dal catalogo corrente.',
    'MER170': 'Il legacy scriveva 201, ma nel catalogo 2026/2027 il 201 riguarda il semestre filtro. GENF201 mantiene il significato DB e non è un alias di MER170.',
    'BS002': 'Il 051 presuppone una rinuncia; la regola corrente include benefici non restituiti anche in presenza di altre carriere pregresse.',
    'CI004': 'Il 073 presuppone una rinuncia; la regola include benefici non restituiti e la condizione comune di borsa non restituita.',
    'PA001': '054 scelto come causale canonica. Il 098 ha la stessa descrizione; la regola ammette gli status B e D. Vale anche per PK.',
    'PA002': 'Comune non incluso nel catalogo dei pensionati attivi. Il 100 riguarda la residenza per una sede distaccata.',
    'PA004': 'Il 056 ha descrizione interna di benefici non restituiti, ma in graduatoria indica alloggio già fruito per lo stesso anno. Nuova causale con testo coerente per entrambi gli usi e per PK.',
    'CI002': 'Tipologie_studi: 7 = Scuola di Specializzazione, non solo medica. Il 221 copre esclusivamente la specializzazione medica.',
    'ISC009': 'Il controllo riguarda il comune della sede assente/non classificabile; il 012 dichiara l’intera sede studi non dichiarata.',
    'BS004': 'Il 222 descrive già una borsa fruita per lo stesso anno di corso. Il 064 identifica genericamente un vincitore.',
    'VAR021': 'Tipologie_variaz 21: revoca per ISEE assente in INPS in base ai riferimenti dichiarati. Eliminata l’errata associazione agli anni fuori corso; 110 non distingue la revoca e 122 riguarda studente indipendente.',
    'VAR030': 'Tipologie_variaz 30 e segnalazione 114 coincidono sulla mancata comunicazione delle modalità di pagamento. Corretta la precedente etichetta ISEE CAF senza cambiare il trigger.',
}
previous = {r['CodiceRegola']: r for r in json.loads((HERE / 'mappatura-decisa.json').read_text(encoding='utf-8'))}
pattern = re.compile(r'^\s+(Verificata|Nuova)\("([A-Z0-9]+)", "((?:[^"\\]|\\.)*)", (Tutti|"[^"]+"), "([^"]+)", "((?:[^"\\]|\\.)*)", "((?:[^"\\]|\\.)*)", "((?:[^"\\]|\\.)*)"\),$', re.M)
entries = []
def quote(value):
    return json.dumps(value, ensure_ascii=False)
def replace(match):
    kind, rule, desc, benefits, prior, _, source, oldnote = match.groups()
    desc = descriptions.get(rule, desc)
    is_new = rule in new_codes
    code = new_codes.get(rule) or overrides.get(rule) or prior
    assert is_new or code in current, (rule, code)
    db_desc = desc if is_new else current[code]['Descrizione'].strip()
    assert len(db_desc) <= 255, (rule, len(db_desc))
    state = 'DaRegistrare' if is_new else 'Verificata'
    source = ('ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)'
              if is_new else f'ADISU.dbo.Decod_segnalazioni, AA 20262027, codice {code} (lettura 2026-11-02)')
    note = notes.get(rule, '')
    if is_new:
        note += ' Codice assegnato nella mappatura; da registrare in DB con lo script allegato.'
    entries.append(dict(CodiceRegola=rule, DescrizioneRegola=desc, Benefici='BS;PA;PK;CI' if benefits == 'Tutti' else benefits.strip('"'),
                        CodiceSegnalazione=code, DescrizioneSegnalazione=db_desc, Stato=state, Fonte=source, Nota=note,
                        PrecedenteAbbinamento=previous[rule]['PrecedenteAbbinamento'], Categoria=(('2' if rule.startswith('RED') or rule == 'VAR021' else '3' if rule.startswith(('ISC', 'MER')) else '4' if rule == 'GEN003' else None) if is_new else current[code]['Categoria'])))
    helper = 'Nuova' if is_new else 'Verificata'
    return f'                {helper}({quote(rule)}, {quote(desc)}, {benefits}, {quote(code)}, {quote(db_desc)}, {quote(source)}, {quote(note)}),'
text, count = pattern.subn(replace, text)
assert count == len(entries) == 74, count
assert len({r['CodiceSegnalazione'] for r in entries}) == 74
assert sum(r['Stato'] == 'DaRegistrare' for r in entries) == 22
path.write_text(text, encoding='utf-8', newline='\r\n')
(HERE / 'mappatura-decisa.json').write_text(json.dumps(entries, ensure_ascii=False, indent=2), encoding='utf-8')
print('Catalogo: 74 mappature univoche; 52 esistenti, 22 nuove (225-246).')
