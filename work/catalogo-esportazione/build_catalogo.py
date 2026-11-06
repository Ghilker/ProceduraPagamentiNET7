import json
import math
from collections import Counter
from pathlib import Path

from openpyxl import Workbook, load_workbook
from openpyxl.formatting.rule import FormulaRule
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.worksheet.table import Table
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent
OUTPUT = ROOT.parent.parent / 'outputs' / '01a19b29-4e8a-73e1-b852-eb1941fe1c1e' / 'Catalogo_motivi_Verifica_20262027_DB.xlsx'
audit = ROOT.parent / 'catalogo-sql-audit'
entries = json.loads((audit / 'mappatura-decisa.json').read_text(encoding='utf-8'))
db_rows = json.loads((audit / 'snapshot/catalogo20262027.json').read_text(encoding='utf-8-sig'))
labels = {'Verificata': 'Esistente nel DB', 'DaRegistrare': 'Da registrare'}
rows = [[r['CodiceRegola'], r['DescrizioneRegola'], r['Benefici'],
         r['CodiceSegnalazione'], labels[r['Stato']], r['DescrizioneSegnalazione'],
         r['PrecedenteAbbinamento'], r['Fonte'], r['Nota']] for r in entries]
counts = Counter(r[4] for r in rows)
assert len(rows) == len({r[0] for r in rows}) == 74
assert counts == {'Esistente nel DB': 52, 'Da registrare': 22}
assert len({r[3] for r in rows}) == 74

wb = Workbook()
ws = wb.active
ws.title = 'Catalogo'
ws.sheet_view.showGridLines = False
ws.sheet_properties.tabColor = '243746'
widths = [22, 65, 20, 22, 22, 70, 25, 64, 105]
last = 10 + len(rows)
for col, width in enumerate(widths, 1):
    ws.column_dimensions[chr(64 + col)].width = width
for row in ws.iter_rows(min_row=1, max_row=last, max_col=9):
    for cell in row:
        cell.font = Font(name='Arial', size=10, color='243746')
        cell.alignment = Alignment(vertical='center')
ws['A2'] = 'Motivi Verifica'
ws['A2'].font = Font(name='Arial', size=14, bold=True, color='243746')
ws['B2'] = '2026/2027 e successivi'
ws['B2'].font = Font(name='Arial', size=11, color='526475')
ws.row_dimensions[2].height = 24
for cell in ws[3]:
    cell.border = Border(bottom=Side(style='thin', color='B6C4CC'))
for col, (label, value) in enumerate(zip(
        ['Motivi mappati', 'Codici esistenti', 'Codici da registrare', 'Causali DB 2026/2027'],
        [len(rows), counts['Esistente nel DB'], counts['Da registrare'], len(db_rows)]), 1):
    ws.cell(4, col, label).alignment = Alignment(horizontal='center', vertical='center')
    cell = ws.cell(5, col, value)
    cell.font = Font(name='Arial', size=12, bold=True, color='243746')
    cell.alignment = Alignment(horizontal='center', vertical='center')
    cell.number_format = '0'
ws['A7'] = 'Fonti e stato'
ws['B7'] = 'ADISU.dbo.Decod_segnalazioni, 2026/2027. Lettura del 02/11/2026 in sola consultazione.'
ws['B7'].font = Font(name='Arial', size=10, italic=True, color='526475')
ws['A8'] = 'Nuovi codici'
ws['B8'] = '225-246 assegnati e da registrare. Nessuna modifica eseguita sul database.'
for row in (7, 8):
    ws.cell(row, 2).alignment = Alignment(vertical='center', wrap_text=True)
    ws.row_dimensions[row].height = 35
headers = ['Codice C#', 'Motivo di esclusione', 'Benefici', 'Codice assegnato',
           'Stato nel DB', 'Descrizione DB / nuova causale', 'Precedente ipotesi',
           'Fonte della corrispondenza', 'Motivazione della scelta']
for col, label in enumerate(headers, 1):
    cell = ws.cell(10, col, label)
    cell.font = Font(name='Arial', size=10, bold=True, color='FFFFFF')
    cell.fill = PatternFill('solid', fgColor='243746')
    cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
ws.row_dimensions[10].height = 34
for index, values in enumerate(rows, 11):
    for col, value in enumerate(values, 1):
        cell = ws.cell(index, col, value)
        cell.alignment = Alignment(horizontal='left', vertical='top', wrap_text=col in (2, 6, 8, 9))
        if col in (1, 4, 7):
            cell.number_format = '@'
        if col in (8, 9):
            cell.font = Font(name='Arial', size=10, color='526475')
    lines = max(math.ceil(len(values[col]) / (widths[col] * .80)) for col in (1, 5, 7, 8))
    ws.row_dimensions[index].height = max(32, lines * 15 + 8)
table = Table(displayName='CatalogoMotivi', ref=f'A10:I{last}')
ws.add_table(table)
amber = PatternFill('solid', fgColor='FFF1D6')
warning_font = Font(name='Arial', size=10, bold=True, color='8A4B00')
ws.conditional_formatting.add(f'E11:E{last}', FormulaRule(formula=['E11="Da registrare"'], fill=amber, font=warning_font))
ws.freeze_panes = 'B11'
ws.sheet_properties.pageSetUpPr.fitToPage = True
ws.page_setup.orientation = 'landscape'
ws.page_setup.paperSize = ws.PAPERSIZE_A3
ws.page_setup.fitToWidth = 1
ws.page_setup.fitToHeight = 0
ws.print_title_rows = '1:10'
ws.print_options.horizontalCentered = False
ws.print_area = f'A1:I{last}'

source = wb.create_sheet('Catalogo DB')
source.sheet_view.showGridLines = False
source['A2'] = 'Catalogo ADISU 2026/2027'
source['A2'].font = Font(name='Arial', size=14, bold=True, color='243746')
source.row_dimensions[2].height = 24
source['A3'] = '203 causali correnti; 237 codici distinti nello storico. Lettura 02/11/2026; nessuna modifica DB.'
source['A3'].font = Font(name='Arial', size=10, italic=True, color='526475')
db_headers = ['Codice DB', 'Descrizione interna DB', 'Descrizione graduatoria DB', 'Tipo', 'Iter', 'Categoria', 'Regola C# / forzatura', 'Uso nella Verifica']
db_widths = [15, 80, 80, 9, 9, 13, 28, 46]
for col, (label, width) in enumerate(zip(db_headers, db_widths), 1):
    source.column_dimensions[chr(64 + col)].width = width
    cell = source.cell(5, col, label)
    cell.font = Font(name='Arial', size=10, bold=True, color='FFFFFF')
    cell.fill = PatternFill('solid', fgColor='243746')
    cell.alignment = Alignment(horizontal='center', vertical='center', wrap_text=True)
source.row_dimensions[5].height = 32
rules_by_code = {r['CodiceSegnalazione']: r['CodiceRegola'] for r in entries if r['Stato'] == 'Verificata'}
for index, row in enumerate(db_rows, 6):
    code = row['Codice']
    direct = rules_by_code.get(code, '')
    force = f'GENF{code}' if code.isdigit() and 200 < int(code) <= 999 else ''
    usage = 'Causale riutilizzata' if direct else 'Forzatura generale disponibile' if force else 'Nessuna regola diretta del motore'
    if code == '000':
        usage = 'Esito idoneo'
    values = [code, row['Descrizione'], row['DescrizioneGrad'], row['Tipo'], row['Iter'], row['Categoria'] or '', ';'.join(filter(None, [direct, force])), usage]
    for col, value in enumerate(values, 1):
        cell = source.cell(index, col, value)
        cell.font = Font(name='Arial', size=10, color='243746')
        cell.alignment = Alignment(horizontal='left', vertical='top', wrap_text=col in (2, 3, 7, 8))
        if col in (1, 4, 5, 6):
            cell.number_format = '@'
    lines = max(math.ceil(len(str(values[col] or '')) / (db_widths[col] * .8)) for col in (1, 2, 6, 7))
    source.row_dimensions[index].height = max(30, lines * 15 + 8)
source.add_table(Table(displayName='CatalogoDatabase', ref=f'A5:H{5 + len(db_rows)}'))
source.freeze_panes = 'B6'
OUTPUT.parent.mkdir(parents=True, exist_ok=True)
wb.save(OUTPUT)

# Rilettura del file salvato: controlli sui dati effettivamente consegnati.
saved = load_workbook(OUTPUT, data_only=False)
sheet = saved['Catalogo']
for index, expected in enumerate(rows, 11):
    actual = [sheet.cell(index, col).value or '' for col in range(1, 10)]
    assert actual == expected, (index, actual, expected)
    for col in (1, 4, 7):
        assert sheet.cell(index, col).number_format == '@'
assert [sheet.cell(5, col).value for col in range(1, 5)] == [74, 52, 22, 203]
assert sheet.freeze_panes == 'B11'
assert sheet.tables['CatalogoMotivi'].ref == f'A10:I{last}'
assert saved['Catalogo DB'].max_row == 208
for index, expected in enumerate(db_rows, 6):
    assert saved['Catalogo DB'].cell(index, 1).value == expected['Codice']
    assert saved['Catalogo DB'].cell(index, 2).value == expected['Descrizione']
    assert saved['Catalogo DB'].cell(index, 3).value == expected['DescrizioneGrad']
for tab in saved:
    assert not any(cell.data_type in ('f', 'e') for row in tab for cell in row)

# Anteprima delle celle e degli stili salvati; il runtime Artifact Tool non è disponibile.
def preview(first_row, last_row, first_col, last_col, filename):
    col_sizes = [round(sheet.column_dimensions[chr(64 + c)].width * 7 + 5) for c in range(first_col, last_col + 1)]
    row_sizes = [round((sheet.row_dimensions[r].height or 15) * 4 / 3) for r in range(first_row, last_row + 1)]
    image = Image.new('RGB', (sum(col_sizes) + 20, sum(row_sizes) + 20), 'white')
    draw = ImageDraw.Draw(image)
    y = 10
    for ri, r in enumerate(range(first_row, last_row + 1)):
        x = 10
        for ci, c in enumerate(range(first_col, last_col + 1)):
            cell = sheet.cell(r, c)
            value = '' if cell.value is None else str(cell.value)
            width, height = col_sizes[ci], row_sizes[ri]
            fill = '#' + cell.fill.fgColor.rgb[-6:] if cell.fill.patternType == 'solid' else '#FFFFFF'
            color = '#' + cell.font.color.rgb[-6:] if cell.font.color and cell.font.color.type == 'rgb' else '#243746'
            bold = cell.font.bold
            if c == 5 and r >= 11 and value == 'Da registrare':
                fill, color, bold = '#FFF1D6', '#8A4B00', True
            draw.rectangle((x, y, x + width, y + height), fill=fill)
            font_path = 'C:/Windows/Fonts/arialbd.ttf' if bold else 'C:/Windows/Fonts/arial.ttf'
            font = ImageFont.truetype(font_path, round((cell.font.sz or 10) * 4 / 3))
            lines = [value]
            if cell.alignment.wrap_text and value:
                lines = []
                line = ''
                for word in value.split(' '):
                    candidate = f'{line} {word}'.strip()
                    if line and draw.textlength(candidate, font=font) > width - 12:
                        lines.append(line)
                        line = word
                    else:
                        line = candidate
                lines.append(line)
            line_height = round((cell.font.sz or 10) * 1.6)
            ty = y + 5 if cell.alignment.vertical == 'top' else y + max(3, (height - len(lines) * line_height) / 2)
            for line in lines:
                tx = x + (width - draw.textlength(line, font=font)) / 2 if cell.alignment.horizontal == 'center' else x + 6
                draw.text((tx, ty), line, fill=color, font=font)
                ty += line_height
            x += width
        y += row_sizes[ri]
    image.save(ROOT / filename)

preview(1, 18, 1, 6, 'catalogo-preview.png')
preview(20, 25, 8, 9, 'catalogo-fonti-preview.png')
print(json.dumps({'output': str(OUTPUT), 'rows': len(rows), 'existing': 52, 'to_register': 22, 'db_catalog': 203, 'saved_file_verified': True}))
