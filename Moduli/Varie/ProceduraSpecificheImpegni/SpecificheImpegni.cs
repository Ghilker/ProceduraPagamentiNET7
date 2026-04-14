using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class SpecificheImpegni : BaseProcedure<ArgsSpecificheImpegni>
    {
        private string selectedFile = string.Empty;
        private string selectedDate = string.Empty;
        private DateTime selectedDateValue;
        private string tipoFondo = string.Empty;
        private bool aperturaNuovaSpecifica;
        private bool soloApertura;
        private string capitolo = string.Empty;
        private string descrDetermina = string.Empty;
        private string esePR = string.Empty;
        private string eseSA = string.Empty;
        private string impegnoPR = string.Empty;
        private string impegnoSA = string.Empty;
        private string numDetermina = string.Empty;
        private string selectedAA = string.Empty;
        private string selectedCodBeneficio = string.Empty;
        private string selectedImportoMensa = string.Empty;
        private string selectedImpegnoMensa = string.Empty;

        private SqlTransaction? sqlTransaction;
        private bool isProvvedimentoAdded;
        private readonly ManualResetEvent _waitHandle = new(false);

        private List<string> _numDomandas = new();
        private int _numDomandaColumn = -1;
        private int _firstSelectedRowIndex = -1;
        private readonly Dictionary<string, decimal> numDomandaImporti = new(StringComparer.OrdinalIgnoreCase);

        public SpecificheImpegni(MasterForm masterForm, SqlConnection mainConnection)
            : base(masterForm, mainConnection)
        {
        }

        public override void RunProcedure(ArgsSpecificheImpegni args)
        {
            if (CONNECTION == null)
            {
                Logger.LogInfo(null, "Connessione assente. Uscita anticipata.");
                return;
            }

            SqlTransaction? ownedTransaction = null;
            bool ownsTransaction = false;
            bool success = false;

            DataGridView? numDomandaGridView = null;
            DataGridView? importiGridView = null;

            try
            {
                AssignArguments(args);
                ValidateArguments();

                Panel? specificheImpegniPanel = null;
                _masterForm.Invoke((MethodInvoker)delegate
                {
                    specificheImpegniPanel = _masterForm.GetProcedurePanel();
                });

                if (specificheImpegniPanel == null)
                    throw new InvalidOperationException("Impossibile ottenere il pannello procedura.");

                if (isProvvedimentoAdded)
                {
                    if (args._sqlTransaction == null)
                        throw new InvalidOperationException("La procedura annidata richiede una transazione esterna valida.");

                    sqlTransaction = args._sqlTransaction;
                }
                else
                {
                    ownedTransaction = CONNECTION.BeginTransaction();
                    sqlTransaction = ownedTransaction;
                    ownsTransaction = true;
                }

                Logger.LogInfo(10, "Transazione inizializzata.");

                DataTable allStudentsData = ReadExcelSnapshot(selectedFile);
                if (allStudentsData.Rows.Count == 0)
                {
                    Logger.LogInfo(11, $"Il file '{Path.GetFileName(selectedFile)}' è vuoto o non contiene righe leggibili.");
                    success = true;
                    return;
                }

                Logger.LogInfo(20, "Selezione numeri domanda.");
                numDomandaGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, specificheImpegniPanel, OnNumDomandaClicked);
                ShowInfoMessage("Cliccare sul primo numero domanda");
                _waitHandle.Reset();
                _waitHandle.WaitOne();

                DisposeGridSafe(numDomandaGridView);
                numDomandaGridView = null;

                _numDomandas = _numDomandas
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (_numDomandas.Count == 0)
                {
                    Logger.LogInfo(21, "Nessun numero domanda valido selezionato.");
                    success = true;
                    return;
                }

                Logger.LogInfo(22, $"Numeri domanda selezionati: {_numDomandas.Count}");

                if (!soloApertura)
                {
                    Logger.LogInfo(30, "Chiusura righe aperte in Specifiche_impegni.");
                    int updated = CloseOpenSpecificheRows();
                    Logger.LogInfo(31, $"Righe chiuse in Specifiche_impegni: {updated}");

                    if (!aperturaNuovaSpecifica)
                    {
                        Logger.LogInfo(40, "Nessuna nuova specifica da aprire. Fine procedura.");
                        success = true;
                        return;
                    }
                }

                Logger.LogInfo(60, "Selezione importi attuali.");
                importiGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, specificheImpegniPanel, OnImportiClicked);
                ShowInfoMessage("Cliccare sul primo importo attuale");
                _waitHandle.Reset();
                _waitHandle.WaitOne();

                DisposeGridSafe(importiGridView);
                importiGridView = null;

                if (numDomandaImporti.Count == 0)
                {
                    Logger.LogInfo(61, "Nessun importo valido selezionato.");
                    success = true;
                    return;
                }

                Logger.LogInfo(62, $"Importi rilevati: {numDomandaImporti.Count}");

                if (numDomandaImporti.Count != _numDomandas.Count)
                {
                    Logger.LogInfo(63,
                        $"Attenzione: numeri domanda selezionati = {_numDomandas.Count}, importi validi rilevati = {numDomandaImporti.Count}.");
                }

                int inserted = InsertNewSpecificheRows();
                Logger.LogInfo(90, $"Inserite {inserted} nuove righe in Specifiche_impegni.");

                success = true;
            }
            catch (Exception ex)
            {
                Logger.LogInfo(95, $"Errore durante l'esecuzione: {ex.Message}");

                if (ownsTransaction)
                {
                    SafeRollback(sqlTransaction);
                }

                throw;
            }
            finally
            {
                DisposeGridSafe(numDomandaGridView);
                DisposeGridSafe(importiGridView);

                if (ownsTransaction)
                {
                    if (success)
                    {
                        try
                        {
                            if (sqlTransaction?.Connection != null)
                            {
                                Logger.LogInfo(99, "Eseguo Commit...");
                                sqlTransaction.Commit();
                                Logger.LogInfo(100, "Commit eseguito con successo.");
                            }
                        }
                        catch (Exception cEx)
                        {
                            Logger.LogInfo(null, $"Errore durante il commit: {cEx.Message}");
                            SafeRollback(sqlTransaction);
                            throw;
                        }
                    }

                    sqlTransaction = null;
                    _masterForm.inProcedure = false;
                }

                Logger.LogInfo(100, "Fine lavorazione.");
            }
        }

        private void AssignArguments(ArgsSpecificheImpegni args)
        {
            selectedFile = args._selectedFile ?? string.Empty;
            selectedDate = args._selectedDate ?? string.Empty;
            selectedDateValue = ParseItalianDate(selectedDate);

            tipoFondo = args._tipoFondo ?? string.Empty;
            aperturaNuovaSpecifica = args._aperturaNuovaSpecifica;
            soloApertura = args._soloApertura;

            capitolo = args._capitolo ?? string.Empty;
            descrDetermina = args._descrDetermina ?? string.Empty;
            esePR = args._esePR ?? string.Empty;
            eseSA = args._eseSA ?? string.Empty;
            impegnoPR = args._impegnoPR ?? string.Empty;
            impegnoSA = args._impegnoSA ?? string.Empty;
            numDetermina = args._numDetermina ?? string.Empty;
            selectedAA = args._selectedAA ?? string.Empty;
            selectedCodBeneficio = args._selectedCodBeneficio ?? string.Empty;
            selectedImpegnoMensa = args._impegnoMensa ?? string.Empty;
            selectedImportoMensa = args._importoMensa ?? string.Empty;

            sqlTransaction = args._sqlTransaction;
            isProvvedimentoAdded = args._isProvvedimentoAdded;

            _numDomandas = new List<string>();
            _numDomandaColumn = -1;
            _firstSelectedRowIndex = -1;
            numDomandaImporti.Clear();
        }

        private void ValidateArguments()
        {
            if (string.IsNullOrWhiteSpace(selectedFile))
                throw new ArgumentException("File Excel non valorizzato.");

            if (!File.Exists(selectedFile))
                throw new FileNotFoundException("File Excel non trovato.", selectedFile);

            if (string.IsNullOrWhiteSpace(selectedAA))
                throw new ArgumentException("Anno accademico non valorizzato.");

            if (string.IsNullOrWhiteSpace(selectedCodBeneficio))
                throw new ArgumentException("Codice beneficio non valorizzato.");
        }

        private int CloseOpenSpecificheRows()
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            CreateTempNumDomTable();
            BulkInsertNumDomande(_numDomandas);

            string sqlUpdate = @"
UPDATE si
SET si.data_fine_validita = @selectedDate,
    si.Num_determina = @numDetermina,
    si.data_determina = @selectedDate,
    si.descrizione_determina = @descrDetermina
FROM Specifiche_impegni si
INNER JOIN #TempNumDomande t
    ON si.num_domanda = t.NumDomanda
WHERE si.anno_accademico = @selectedAA
  AND si.cod_beneficio = @selectedCodBeneficio
  AND si.data_fine_validita IS NULL;";

            using SqlCommand updateCommand = new(sqlUpdate, CONNECTION, sqlTransaction);
            updateCommand.Parameters.Add("@selectedDate", SqlDbType.DateTime).Value = selectedDateValue;
            updateCommand.Parameters.Add("@numDetermina", SqlDbType.VarChar, 100).Value = numDetermina;
            updateCommand.Parameters.Add("@descrDetermina", SqlDbType.VarChar, -1).Value = descrDetermina;
            updateCommand.Parameters.Add("@selectedAA", SqlDbType.VarChar, 8).Value = selectedAA;
            updateCommand.Parameters.Add("@selectedCodBeneficio", SqlDbType.VarChar, 2).Value = selectedCodBeneficio;

            return updateCommand.ExecuteNonQuery();
        }

        private int InsertNewSpecificheRows()
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            CreateTempImportiTable();
            BulkInsertImporti(numDomandaImporti);

            decimal? importoMensa = ParseNullableMoney(selectedImportoMensa);
            bool hasMensa = importoMensa.HasValue;

            string determinaConferimento = string.IsNullOrWhiteSpace(numDetermina)
                ? string.Empty
                : $"{numDetermina} del {selectedDateValue:dd/MM/yyyy}";

            string sqlInsert = @"
INSERT INTO [specifiche_impegni]
(
    [Anno_accademico],
    [Num_domanda],
    [Cod_fiscale],
    [Cod_beneficio],
    [Data_validita],
    [Utente],
    [Codice_Studente],
    [Tipo_fondo],
    [Capitolo],
    [Importo_assegnato],
    [Determina_conferimento],
    [num_impegno_primaRata],
    [num_impegno_saldo],
    [esercizio_saldo],
    [Esercizio_prima_rata],
    [data_fine_validita],
    [Num_determina],
    [data_determina],
    [descrizione_determina],
    monetizzazione_concessa,
    importo_servizio_mensa,
    impegno_monetizzazione
)
SELECT
    @selectedAA,
    t.NumDomanda,
    d.Cod_fiscale,
    @selectedCodBeneficio,
    CURRENT_TIMESTAMP,
    'Area4',
    s.Codice_Studente,
    @tipoFondo,
    @capitolo,
    t.Importo,
    @determinaConferimento,
    @impegnoPR,
    @impegnoSA,
    @eseSA,
    @esePR,
    NULL,
    NULL,
    NULL,
    NULL,
    @boolMonetizzazione,
    @importoMonetizzazione,
    @impegnoMonetizzazione
FROM #TempImporti t
INNER JOIN Domanda d
    ON d.Anno_accademico = @selectedAA
   AND d.Num_domanda = t.NumDomanda
INNER JOIN Studente s
    ON s.Cod_fiscale = d.Cod_fiscale;";

            using SqlCommand insertCommand = new(sqlInsert, CONNECTION, sqlTransaction);

            insertCommand.Parameters.Add("@selectedAA", SqlDbType.VarChar, 8).Value = selectedAA;
            insertCommand.Parameters.Add("@selectedCodBeneficio", SqlDbType.VarChar, 2).Value = selectedCodBeneficio;
            insertCommand.Parameters.Add("@tipoFondo", SqlDbType.VarChar, 50).Value = tipoFondo;
            insertCommand.Parameters.Add("@capitolo", SqlDbType.VarChar, 50).Value = capitolo;
            insertCommand.Parameters.Add("@determinaConferimento", SqlDbType.VarChar, 200).Value = determinaConferimento;
            insertCommand.Parameters.Add("@impegnoPR", SqlDbType.VarChar, 50).Value = impegnoPR;
            insertCommand.Parameters.Add("@impegnoSA", SqlDbType.VarChar, 50).Value = impegnoSA;
            insertCommand.Parameters.Add("@eseSA", SqlDbType.VarChar, 10).Value = eseSA;
            insertCommand.Parameters.Add("@esePR", SqlDbType.VarChar, 10).Value = esePR;
            insertCommand.Parameters.Add("@boolMonetizzazione", SqlDbType.Int).Value = hasMensa ? 1 : 0;

            SqlParameter pImportoMonetizzazione = insertCommand.Parameters.Add("@importoMonetizzazione", SqlDbType.Decimal);
            pImportoMonetizzazione.Precision = 18;
            pImportoMonetizzazione.Scale = 2;
            pImportoMonetizzazione.Value = hasMensa ? importoMensa!.Value : DBNull.Value;

            insertCommand.Parameters.Add("@impegnoMonetizzazione", SqlDbType.VarChar, 50).Value =
                hasMensa && !string.IsNullOrWhiteSpace(selectedImpegnoMensa)
                    ? selectedImpegnoMensa
                    : DBNull.Value;

            return insertCommand.ExecuteNonQuery();
        }

        private void CreateTempNumDomTable()
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            string sql = @"
IF OBJECT_ID('tempdb..#TempNumDomande') IS NOT NULL
    DROP TABLE #TempNumDomande;

CREATE TABLE #TempNumDomande
(
    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY
);";

            using SqlCommand cmd = new(sql, CONNECTION, sqlTransaction);
            cmd.ExecuteNonQuery();
        }

        private void BulkInsertNumDomande(IEnumerable<string> numDomande)
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            DataTable dt = new();
            dt.Columns.Add("NumDomanda", typeof(string));

            foreach (string numDomanda in numDomande
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                dt.Rows.Add(numDomanda);
            }

            using SqlBulkCopy bulk = new(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction);
            bulk.DestinationTableName = "#TempNumDomande";
            bulk.WriteToServer(dt);
        }

        private void CreateTempImportiTable()
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            string sql = @"
IF OBJECT_ID('tempdb..#TempImporti') IS NOT NULL
    DROP TABLE #TempImporti;

CREATE TABLE #TempImporti
(
    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY,
    Importo DECIMAL(18,2) NOT NULL
);";

            using SqlCommand cmd = new(sql, CONNECTION, sqlTransaction);
            cmd.ExecuteNonQuery();
        }

        private void BulkInsertImporti(Dictionary<string, decimal> importi)
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            DataTable dt = new();
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("Importo", typeof(decimal));

            foreach (KeyValuePair<string, decimal> pair in importi)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                dt.Rows.Add(pair.Key, decimal.Round(pair.Value, 2, MidpointRounding.AwayFromZero));
            }

            using SqlBulkCopy bulk = new(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction);
            bulk.DestinationTableName = "#TempImporti";
            bulk.WriteToServer(dt);
        }

        private DataTable ReadExcelSnapshot(string originalFilePath)
        {
            string tempFile = CreateStableWorkingCopy(originalFilePath);

            try
            {
                return Utilities.ReadExcelToDataTable(tempFile);
            }
            finally
            {
                TryDeleteFile(tempFile);
            }
        }

        private static string CreateStableWorkingCopy(string originalFilePath)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ProcedureNet7", "SpecificheImpegni");
            Directory.CreateDirectory(tempRoot);

            string tempFileName =
                $"{Path.GetFileNameWithoutExtension(originalFilePath)}_" +
                $"{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}" +
                $"{Path.GetExtension(originalFilePath)}";

            string tempFilePath = Path.Combine(tempRoot, tempFileName);
            File.Copy(originalFilePath, tempFilePath, true);

            return tempFilePath;
        }

        private static void TryDeleteFile(string filePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
            }
        }

        private void OnNumDomandaClicked(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (sender is not DataGridView dataGridView || e.RowIndex < 0 || e.ColumnIndex < 0)
                    return;

                HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

                _numDomandaColumn = e.ColumnIndex;
                _firstSelectedRowIndex = e.RowIndex;

                for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                {
                    object? cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                    string value = Convert.ToString(cellValue, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        continue;

                    values.Add(value);
                }

                _numDomandas = values.ToList();
            }
            finally
            {
                _waitHandle.Set();
            }
        }

        private void OnImportiClicked(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (sender is not DataGridView dataGridView || e.ColumnIndex < 0)
                    return;

                if (_numDomandaColumn < 0 || _firstSelectedRowIndex < 0)
                    throw new InvalidOperationException("Selezione dei numeri domanda non valida.");

                numDomandaImporti.Clear();

                int startRow = _firstSelectedRowIndex;

                for (int i = startRow; i < dataGridView.Rows.Count; i++)
                {
                    object? numDomandaVar = dataGridView.Rows[i].Cells[_numDomandaColumn].Value;
                    object? importoVar = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;

                    string numDomanda = Convert.ToString(numDomandaVar, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                    string importoString = Convert.ToString(importoVar, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(numDomanda) || !long.TryParse(numDomanda, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        continue;

                    if (!TryParseMoney(importoString, out decimal importo))
                        continue;

                    numDomandaImporti[numDomanda] = importo;
                }
            }
            finally
            {
                _waitHandle.Set();
            }
        }

        private static bool TryParseMoney(string? input, out decimal value)
        {
            value = 0m;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string normalized = input.Trim();

            return decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("it-IT"), out value)
                   || decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static decimal? ParseNullableMoney(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            if (TryParseMoney(input, out decimal value))
                return value;

            throw new FormatException($"Importo monetizzazione non valido: '{input}'.");
        }

        private static DateTime ParseItalianDate(string input)
        {
            string[] formats =
            {
                "dd/MM/yyyy",
                "d/M/yyyy",
                "dd/MM/yyyy HH:mm:ss",
                "d/M/yyyy H:mm:ss",
                "yyyy-MM-dd",
                "yyyy-MM-dd HH:mm:ss"
            };

            if (DateTime.TryParseExact(
                    input,
                    formats,
                    CultureInfo.GetCultureInfo("it-IT"),
                    DateTimeStyles.None,
                    out DateTime exact))
            {
                return exact;
            }

            if (DateTime.TryParse(
                    input,
                    CultureInfo.GetCultureInfo("it-IT"),
                    DateTimeStyles.None,
                    out DateTime generic))
            {
                return generic;
            }

            throw new FormatException($"Data non valida: '{input}'.");
        }

        private void ShowInfoMessage(string message)
        {
            _masterForm.Invoke((MethodInvoker)delegate
            {
                MessageBox.Show(message, "Seleziona", MessageBoxButtons.OK, MessageBoxIcon.Information);
            });
        }

        private void DisposeGridSafe(DataGridView? grid)
        {
            if (grid == null)
                return;

            try
            {
                _masterForm.Invoke((MethodInvoker)delegate
                {
                    if (!grid.IsDisposed)
                        grid.Dispose();
                });
            }
            catch
            {
            }
        }

        private static void SafeRollback(SqlTransaction? transaction)
        {
            if (transaction == null)
                return;

            try
            {
                if (transaction.Connection != null)
                    transaction.Rollback();
            }
            catch
            {
            }
        }
    }
}