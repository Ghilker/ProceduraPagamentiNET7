import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Workbook, SpreadsheetFile } from '@oai/artifact-tool';

const workspace = path.dirname(fileURLToPath(import.meta.url));
const outputDir = path.resolve(workspace, '../../outputs/01a19b29-4e8a-73e1-b852-eb1941fe1c1e');
const entries = JSON.parse(await fs.readFile(path.join(workspace, 'catalogo.json'), 'utf8'));
const labels = { Verificata: 'Verificata', DaConfermare: 'Da confermare', NuovoCodiceProposto: 'Nuovo proposto' };
const data = entries.map(entry => [
  entry['Codice regola'], entry['Descrizione regola'], entry.Benefici,
  entry['Codice segnalazione verificato'] ?? '', entry['Codici proposti da verificare'] ?? '',
  labels[entry.Stato] ?? entry.Stato, entry.Fonte, entry.Nota,
]);
const start = 11;
const last = start + data.length - 1;
const workbook = Workbook.create();
const sheet = workbook.worksheets.add('Catalogo');
sheet.showGridLines = false;
sheet.tabColor = '#243746';
sheet.getRange(`A1:H${last}`).format.font = { name: 'Arial', size: 10, color: '#243746' };
sheet.getRange(`A1:H${last}`).format.verticalAlignment = 'center';
const widths = [22, 61, 20, 25, 24, 22, 57, 94];
for (let column = 0; column < widths.length; column++)
  sheet.getRangeByIndexes(0, column, last, 1).format.columnWidth = widths[column];

sheet.getRange('A2').values = [['Motivi Verifica']];
sheet.getRange('A2').format.font = { name: 'Arial', size: 14, bold: true, color: '#243746' };
sheet.getRange('B2').values = [['2026/2027 e successivi']];
sheet.getRange('B2').format.font = { name: 'Arial', size: 11, color: '#526475' };
sheet.getRange('A2:H2').format.rowHeight = 24;
sheet.getRange('A3:H3').format.borders = { bottom: { style: 'thin', color: '#B6C4CC' } };
sheet.getRange('A4:D4').values = [['Motivi', 'Mappature verificate', 'Da confermare', 'Nuovi proposti']];
sheet.getRange('A4:D4').format.horizontalAlignment = 'center';
sheet.getRange('A5:D5').formulas = [[
  `=COUNTA(A${start}:A${last})`,
  `=COUNTIFS(F${start}:F${last},"Verificata")`,
  `=COUNTIFS(F${start}:F${last},"Da confermare")`,
  `=COUNTIFS(F${start}:F${last},"Nuovo proposto")`,
]];
sheet.getRange('A5:D5').format.font = { name: 'Arial', size: 12, bold: true, color: '#243746' };
sheet.getRange('A5:D5').format.horizontalAlignment = 'center';
sheet.getRange('A5:D5').setNumberFormat('0');
sheet.getRange('A7').values = [['Fonti e stato']];
sheet.getRange('B7').values = [['Codice VB e piano del repository. Codici.xlsx non disponibile; catalogo DB corrente non letto.']];
sheet.getRange('B7').format.font = { name: 'Arial', size: 10, italic: true, color: '#526475' };
sheet.getRange('A8').values = [['Candidati']];
sheet.getRange('B8').values = [['I candidati non sono codici attivi. GENF201-999 conserva il codice numerico della forzatura.']];
sheet.getRange('A7:H8').format.rowHeight = 20;

sheet.getRange('A10:H10').values = [[
  'Codice C#', 'Motivo di esclusione', 'Benefici', 'Segnalazione verificata',
  'Codici candidati', 'Stato mappatura', 'Fonte della corrispondenza', 'Nota di verifica',
]];
sheet.getRange(`A${start}:H${last}`).values = data;
sheet.tables.add(`A10:H${last}`, true, 'CatalogoMotivi');
sheet.getRange('A10:H10').format = {
  fill: '#243746', font: { name: 'Arial', size: 10, bold: true, color: '#FFFFFF' },
  horizontalAlignment: 'center', verticalAlignment: 'center', wrapText: true, rowHeight: 34,
};
sheet.getRange(`A${start}:H${last}`).format.verticalAlignment = 'top';
sheet.getRange(`A${start}:H${last}`).format.horizontalAlignment = 'left';
sheet.getRange(`A${start}:A${last}`).setNumberFormat('@');
sheet.getRange(`D${start}:E${last}`).setNumberFormat('@');
for (const column of ['B', 'G', 'H'])
  sheet.getRange(`${column}${start}:${column}${last}`).format.wrapText = true;
sheet.getRange(`G${start}:H${last}`).format.font = { name: 'Arial', size: 10, color: '#526475' };
for (let index = 0; index < data.length; index++) {
  const lines = Math.max(...[1, 6, 7].map(column => Math.ceil((data[index][column] || '').length / (widths[column] * 0.80))));
  sheet.getRange(`A${start + index}:H${start + index}`).format.rowHeight = Math.max(32, lines * 15 + 8);
}
sheet.getRange(`F${start}:F${last}`).conditionalFormats.add('containsText', {
  text: 'Da confermare', format: { fill: '#FFF1D6', font: { color: '#8A4B00', bold: true } },
});
sheet.getRange(`F${start}:F${last}`).conditionalFormats.add('containsText', {
  text: 'Nuovo proposto', format: { fill: '#FFF1D6', font: { color: '#8A4B00', bold: true } },
});
sheet.freezePanes.freezeRows(10);
sheet.freezePanes.freezeColumns(1);
workbook.recalculate();

const actual = sheet.getRange('A5:D5').values[0];
const expected = [data.length, ...['Verificata', 'Da confermare', 'Nuovo proposto'].map(state => data.filter(row => row[5] === state).length)];
if (actual.some((value, index) => value !== expected[index]))
  throw new Error(`Riepilogo non riconciliato: ${JSON.stringify({ actual, expected })}`);
if (new Set(data.map(row => row[0])).size !== data.length)
  throw new Error('Codici C# duplicati nel catalogo.');
if (data.some(row => row[3] && (row[3].length !== 3 || !/^\d{3}$/.test(row[3]))))
  throw new Error('Il codice di segnalazione deve preservare tre cifre.');

await fs.mkdir(outputDir, { recursive: true });
const top = await workbook.inspect({ kind: 'table', range: 'Catalogo!A4:F14', include: 'values,formulas', tableMaxRows: 11, tableMaxCols: 6, maxChars: 1800 });
console.log(top.ndjson);
const errors = await workbook.inspect({ kind: 'match', searchTerm: '#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A|#NUM!|#NULL!|#SPILL!|#CALC!', options: { useRegex: true, maxResults: 30 }, summary: 'Errori formule', maxChars: 800 });
console.log(errors.ndjson);
const preview = await workbook.render({ sheetName: 'Catalogo', range: 'A1:F18', scale: 1.4, format: 'png' });
await fs.writeFile(path.join(workspace, 'catalogo-preview.png'), new Uint8Array(await preview.arrayBuffer()));
const detail = await workbook.render({ sheetName: 'Catalogo', range: 'G20:H30', scale: 1.2, format: 'png' });
await fs.writeFile(path.join(workspace, 'catalogo-fonti-preview.png'), new Uint8Array(await detail.arrayBuffer()));
const output = await SpreadsheetFile.exportXlsx(workbook);
const destination = path.join(outputDir, 'Catalogo_motivi_Verifica_20262027.xlsx');
await output.save(destination);
console.log(JSON.stringify({ output: destination, rows: data.length, summary: actual }));
