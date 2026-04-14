using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class AggiuntaProvvedimenti : BaseProcedure<ArgsAggiuntaProvvedimenti>
    {
        private readonly Dictionary<string, string> variazCodVariazioneItems = new()
        {
            { "01", "Variazione generica" },
            { "02", "Rinuncia" },
            { "03", "Revoca" },
            { "04", "Decadenza" },
            { "05", "Riammissione come idoneo" },
            { "06", "Riammissione come vincitore" },
            { "07", "Da idoneo a vincitore" },
            { "08", "Da vincitore a idoneo" },
            { "09", "Rinuncia idoneità" },
            { "010", "Rinuncia vincitore" },
            { "011", "Revoca per incompatibilità col bando" },
            { "012", "Da PENDOLARE a IN SEDE" },
            { "013", "Da PENDOLARE a FUORI SEDE" },
            { "014", "Da FUORI SEDE a PENDOLARE" },
            { "015", "Da FUORI SEDE a IN SEDE" },
            { "016", "Da IN SEDE a FUORI SEDE" },
            { "017", "Da IN SEDE a PENDOLARE" },
            { "018", "Revoca per sede distaccata" },
            { "019", "Revoca per mancata iscrizione" },
            { "020", "Revoca per studente iscritto ripetente" },
            { "021", "Revoca per ISEE non presente in banca dati" },
            { "022", "Revoca per studente già laureato" },
            { "023", "Revoca per patrimonio oltre il limite" },
            { "024", "Revoca per reddito oltre il limite" },
            { "025", "Revoca per mancanza esami o crediti" },
            { "026", "Rinuncia a tutti i benefici" },
            { "027", "Revoca per iscrizione fuori temine" },
            { "028", "Revoca per ISEE fuori termine" },
            { "029", "Revoca per ISEE non prodotta" },
            { "030", "Decadenza per mancata comunicazione modalità di pagamento" },
            { "031", "Revoca per mancanza contratto di affitto" },
            { "032", "Variazione I.S.E.E." },
            { "033", "Revoca premio di laurea" },
            { "034", "Rinuncia premio di laurea" },
            { "035", "Riammissione come idoneo premio di laurea" },
            { "036", "Riammisione come vincitore premio di laurea" },
        };

        private readonly Dictionary<string, string> variazCodBeneficioItems = new()
        {
            { "00", "Tutti i benefici" },
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
        };

        private static readonly HashSet<string> SupportedExcelExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".xls",
            ".xlsx",
            ".xlsm",
            ".xlsb"
        };

        private List<string> _studentInformation = new();
        private readonly ManualResetEvent _waitHandle = new(false);

        private SqlTransaction? sqlTransaction;

        private string selectedFolderPath = string.Empty;
        private string numProvvedimento = string.Empty;
        private string aaProvvedimento = string.Empty;
        private string dataProvvedimento = string.Empty;
        private DateTime dataProvvedimentoDate;
        private string provvedimentoSelezionato = string.Empty;
        private string notaProvvedimento = string.Empty;
        private string beneficioProvvedimento = string.Empty;
        private bool requireNuovaSpecifica;

        private string tipoFondo = string.Empty;
        private string capitolo = string.Empty;
        private string esePR = string.Empty;
        private string eseSA = string.Empty;
        private string impegnoPR = string.Empty;
        private string impegnoSA = string.Empty;

        // Stato interno per la lavorazione specifiche impegni
        private List<string> _specificheNumDomandas = new();
        private int _specificheNumDomandaColumn = -1;
        private int _specificheFirstSelectedRowIndex = -1;
        private readonly Dictionary<string, decimal> _specificheImporti = new(StringComparer.OrdinalIgnoreCase);

        public AggiuntaProvvedimenti(MasterForm masterForm, SqlConnection mainConnection)
            : base(masterForm, mainConnection)
        {
        }

        public override void RunProcedure(ArgsAggiuntaProvvedimenti args)
        {
            SqlTransaction? transaction = null;

            try
            {
                if (CONNECTION == null)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure: connessione null");
                    return;
                }

                ValidateArgs(args);

                selectedFolderPath = args._selectedFolderPath;
                numProvvedimento = (args._numProvvedimento ?? string.Empty).Trim();
                aaProvvedimento = (args._aaProvvedimento ?? string.Empty).Trim();
                dataProvvedimento = (args._dataProvvedimento ?? string.Empty).Trim();
                dataProvvedimentoDate = ParseProvvedimentoDate(dataProvvedimento);
                provvedimentoSelezionato = (args._provvedimentoSelezionato ?? string.Empty).Trim();
                notaProvvedimento = (args._notaProvvedimento ?? string.Empty).Trim();
                beneficioProvvedimento = (args._beneficioProvvedimento ?? string.Empty).Trim();
                requireNuovaSpecifica = args._requireNuovaSpecifica;

                capitolo = (args._capitolo ?? string.Empty).Trim();
                esePR = (args._esePR ?? string.Empty).Trim();
                eseSA = (args._eseSA ?? string.Empty).Trim();
                impegnoPR = (args._impegnoPR ?? string.Empty).Trim();
                impegnoSA = (args._impegnoSA ?? string.Empty).Trim();
                tipoFondo = (args._tipoFondo ?? string.Empty).Trim();

                string? foundFolder = FindProvvedimentoFolder(selectedFolderPath, numProvvedimento);
                if (string.IsNullOrWhiteSpace(foundFolder))
                {
                    Logger.Log(0, $"Cartella determina det{numProvvedimento} non trovata in '{selectedFolderPath}'", LogLevel.INFO);
                    return;
                }

                List<string> excelFilePaths = GetEligibleExcelFiles(foundFolder).ToList();
                if (excelFilePaths.Count == 0)
                {
                    Logger.Log(0, $"Nessun file Excel valido trovato in '{foundFolder}'", LogLevel.INFO);
                    return;
                }

                Panel? provvedimentiPanel = null;
                _masterForm.Invoke((MethodInvoker)delegate
                {
                    provvedimentiPanel = _masterForm.GetProcedurePanel();
                });

                if (provvedimentiPanel == null)
                    throw new InvalidOperationException("Impossibile ottenere il pannello procedura.");

                transaction = CONNECTION.BeginTransaction();
                sqlTransaction = transaction;

                foreach (string excelFilePath in excelFilePaths)
                {
                    Logger.Log(5, $"Elaborazione file: {Path.GetFileName(excelFilePath)}", LogLevel.INFO);

                    DataTable allStudentsData = ReadExcelSnapshot(excelFilePath);
                    if (allStudentsData.Rows.Count == 0)
                    {
                        Logger.Log(10, $"File vuoto o senza righe utili: {Path.GetFileName(excelFilePath)}", LogLevel.INFO);
                        continue;
                    }

                    DataGridView? studentsGridView = null;

                    try
                    {
                        _studentInformation = new List<string>();

                        if (beneficioProvvedimento == "BS" || beneficioProvvedimento == "CI")
                        {
                            ShowInfoMessage("Selezionare il primo numero domanda");
                            studentsGridView = Utilities.CreateDataGridView(
                                allStudentsData,
                                _masterForm,
                                provvedimentiPanel,
                                OnProvvedimentiNumDomandaClicked);
                        }
                        else
                        {
                            ShowInfoMessage("Selezionare il primo codice fiscale");
                            studentsGridView = Utilities.CreateDataGridView(
                                allStudentsData,
                                _masterForm,
                                provvedimentiPanel,
                                OnProvvedimentiCodFiscaleClicked);
                        }

                        studentsGridView.HandleDestroyed += (_, __) => _waitHandle.Set();

                        _waitHandle.Reset();
                        _waitHandle.WaitOne();

                        _studentInformation = _studentInformation
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (_studentInformation.Count == 0)
                        {
                            Logger.Log(15, $"Nessun identificativo valido selezionato nel file {Path.GetFileName(excelFilePath)}", LogLevel.INFO);
                            continue;
                        }

                        HandleProvvedimenti(
                            numProvvedimento,
                            aaProvvedimento,
                            provvedimentoSelezionato,
                            notaProvvedimento);

                        if (requireNuovaSpecifica)
                        {
                            HandleSpecificheImpegniInternal(allStudentsData, provvedimentiPanel);
                        }
                    }
                    finally
                    {
                        DisposeGridSafe(studentsGridView);
                    }
                }

                transaction.Commit();
                Logger.Log(100, "Fine lavorazione", LogLevel.INFO);
            }
            catch
            {
                SafeRollback(transaction);
                throw;
            }
            finally
            {
                sqlTransaction = null;
                _masterForm.inProcedure = false;
            }
        }

        private static void ValidateArgs(ArgsAggiuntaProvvedimenti args)
        {
            if (args == null)
                throw new ArgumentNullException(nameof(args));

            if (string.IsNullOrWhiteSpace(args._selectedFolderPath))
                throw new ArgumentException("Percorso cartella non valorizzato.");

            if (!Directory.Exists(args._selectedFolderPath))
                throw new DirectoryNotFoundException($"Cartella non trovata: {args._selectedFolderPath}");

            if (string.IsNullOrWhiteSpace(args._numProvvedimento))
                throw new ArgumentException("Numero provvedimento non valorizzato.");

            if (string.IsNullOrWhiteSpace(args._aaProvvedimento))
                throw new ArgumentException("Anno accademico non valorizzato.");

            if (string.IsNullOrWhiteSpace(args._dataProvvedimento))
                throw new ArgumentException("Data provvedimento non valorizzata.");

            if (string.IsNullOrWhiteSpace(args._provvedimentoSelezionato))
                throw new ArgumentException("Tipo provvedimento non valorizzato.");

            if (string.IsNullOrWhiteSpace(args._beneficioProvvedimento))
                throw new ArgumentException("Beneficio provvedimento non valorizzato.");
        }

        private static DateTime ParseProvvedimentoDate(string input)
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

            throw new FormatException($"Data provvedimento non valida: '{input}'");
        }

        private static string? FindProvvedimentoFolder(string rootFolder, string numeroProvvedimento)
        {
            string pattern = $@"\bdet{Regex.Escape(numeroProvvedimento)}\b";

            foreach (string subDirectory in Directory.EnumerateDirectories(rootFolder))
            {
                string directoryName = Path.GetFileName(subDirectory);
                if (Regex.IsMatch(directoryName, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    Logger.Log(0, $"Cartella trovata: {directoryName}", LogLevel.INFO);
                    return subDirectory;
                }
            }

            return null;
        }

        private IEnumerable<string> GetEligibleExcelFiles(string folderPath)
        {
            foreach (string filePath in Directory.EnumerateFiles(folderPath))
            {
                if (IsEligibleExcelFile(filePath, out string reason))
                {
                    yield return filePath;
                }
                else
                {
                    Logger.Log(2, $"File ignorato: {Path.GetFileName(filePath)} | Motivo: {reason}", LogLevel.INFO);
                }
            }
        }

        private bool IsEligibleExcelFile(string filePath, out string reason)
        {
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                reason = "file inesistente";
                return false;
            }

            string fileName = Path.GetFileName(filePath);
            string extension = Path.GetExtension(filePath);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                reason = "nome file non valido";
                return false;
            }

            if (!SupportedExcelExtensions.Contains(extension))
            {
                reason = "estensione non supportata";
                return false;
            }

            if (fileName.StartsWith("~$", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("~", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(".~lock", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                reason = "file temporaneo/fantasma di Excel";
                return false;
            }

            if (fileName.Contains("Riepilogo", StringComparison.OrdinalIgnoreCase))
            {
                reason = "file riepilogo escluso";
                return false;
            }

            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(filePath);
            }
            catch (Exception ex)
            {
                reason = $"attributi non leggibili: {ex.Message}";
                return false;
            }

            if ((attributes & FileAttributes.Hidden) != 0 ||
                (attributes & FileAttributes.System) != 0 ||
                (attributes & FileAttributes.Temporary) != 0)
            {
                reason = "file hidden/system/temporary";
                return false;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(filePath);
            }
            catch (Exception ex)
            {
                reason = $"file info non leggibile: {ex.Message}";
                return false;
            }

            if (info.Length <= 0)
            {
                reason = "file vuoto";
                return false;
            }

            if (IsFileLocked(filePath))
            {
                reason = "file in uso o bloccato";
                return false;
            }

            return true;
        }

        private static bool IsFileLocked(string filePath)
        {
            try
            {
                using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        private DataTable ReadExcelSnapshot(string originalFilePath)
        {
            string tempFile = CreateStableWorkingCopy(originalFilePath);

            try
            {
                return Utilities.ReadExcelToDataTable(tempFile, true);
            }
            finally
            {
                TryDeleteFile(tempFile);
            }
        }

        private static string CreateStableWorkingCopy(string originalFilePath)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "ProcedureNet7", "AggiuntaProvvedimenti");
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

        private void OnProvvedimentiNumDomandaClicked(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (sender is not DataGridView dataGridView || e.RowIndex < 0 || e.ColumnIndex < 0)
                    return;

                HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

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

                _studentInformation = values.ToList();
            }
            finally
            {
                _waitHandle.Set();
            }
        }

        private void OnProvvedimentiCodFiscaleClicked(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (sender is not DataGridView dataGridView || e.RowIndex < 0 || e.ColumnIndex < 0)
                    return;

                HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

                for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                {
                    object? cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                    string value = (Convert.ToString(cellValue, CultureInfo.InvariantCulture) ?? string.Empty)
                        .Trim()
                        .ToUpperInvariant();

                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    values.Add(value);
                }

                _studentInformation = values.ToList();
            }
            finally
            {
                _waitHandle.Set();
            }
        }

        private void HandleProvvedimenti(
            string numProvvedimento,
            string aaProvvedimento,
            string provvedimentoSelezionato,
            string notaProvvedimento)
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("Connessione non disponibile.");

            if (sqlTransaction == null)
                throw new InvalidOperationException("Transazione non disponibile.");

            List<string> inputValues = _studentInformation
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (inputValues.Count == 0)
            {
                Logger.Log(20, "Nessuno studente da elaborare", LogLevel.INFO);
                return;
            }

            string createTempFiscalTable = @"
IF OBJECT_ID('tempdb..#TempFiscalCodes') IS NOT NULL
    DROP TABLE #TempFiscalCodes;

CREATE TABLE #TempFiscalCodes
(
    FiscalCode VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL
);";

            string createTempNumDomandaTable = @"
IF OBJECT_ID('tempdb..#TempNumDom') IS NOT NULL
    DROP TABLE #TempNumDom;

CREATE TABLE #TempNumDom
(
    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL
);";

            string createTempCommonTable = @"
IF OBJECT_ID('tempdb..#TempCommonCodes') IS NOT NULL
    DROP TABLE #TempCommonCodes;

CREATE TABLE #TempCommonCodes
(
    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL
);";

            using (var cmd = new SqlCommand(createTempFiscalTable, CONNECTION, sqlTransaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand(createTempNumDomandaTable, CONNECTION, sqlTransaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqlCommand(createTempCommonTable, CONNECTION, sqlTransaction))
            {
                cmd.ExecuteNonQuery();
            }

            using (var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
            {
                bulkCopy.DestinationTableName = "#TempFiscalCodes";

                DataTable dtFiscalCodes = new();
                dtFiscalCodes.Columns.Add("FiscalCode", typeof(string));

                foreach (string code in inputValues)
                {
                    dtFiscalCodes.Rows.Add(code);
                }

                bulkCopy.WriteToServer(dtFiscalCodes);
            }

            if (beneficioProvvedimento != "BS" && beneficioProvvedimento != "CI")
            {
                string fillTempNumDomanda = @"
INSERT INTO #TempNumDom (NumDomanda)
SELECT DISTINCT d.num_domanda
FROM Domanda d
INNER JOIN #TempFiscalCodes t
    ON d.Cod_fiscale = t.FiscalCode
WHERE d.Anno_accademico = @aaProvvedimento
  AND d.Tipo_bando = 'LZ';";

                using var cmd = new SqlCommand(fillTempNumDomanda, CONNECTION, sqlTransaction);
                cmd.Parameters.Add("@aaProvvedimento", SqlDbType.VarChar, 8).Value = aaProvvedimento;
                cmd.ExecuteNonQuery();
            }
            else
            {
                using var bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction);
                bulkCopy.DestinationTableName = "#TempNumDom";

                DataTable dtNumDom = new();
                dtNumDom.Columns.Add("NumDomanda", typeof(string));

                foreach (string code in inputValues)
                {
                    dtNumDom.Rows.Add(code);
                }

                bulkCopy.WriteToServer(dtNumDom);
            }

            string fillTempCommonCodes = @"
INSERT INTO #TempCommonCodes (NumDomanda)
SELECT DISTINCT d.Num_domanda
FROM Domanda d
INNER JOIN PROVVEDIMENTI p
    ON d.Num_domanda = p.Num_domanda
   AND d.Anno_accademico = p.Anno_accademico
INNER JOIN #TempNumDom t
    ON d.Num_domanda = t.NumDomanda
WHERE d.Anno_accademico = @aaProvvedimento
  AND p.Anno_accademico = @aaProvvedimento
  AND p.num_provvedimento = @numProvvedimento
  AND d.Tipo_bando = 'LZ';";

            using (var cmd = new SqlCommand(fillTempCommonCodes, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.Add("@aaProvvedimento", SqlDbType.VarChar, 8).Value = aaProvvedimento;
                cmd.Parameters.Add("@numProvvedimento", SqlDbType.VarChar, 50).Value = numProvvedimento;
                cmd.ExecuteNonQuery();
            }

            List<string> commonCodes = new();
            using (var cmd = new SqlCommand("SELECT DISTINCT NumDomanda FROM #TempCommonCodes;", CONNECTION, sqlTransaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string code = Convert.ToString(reader["NumDomanda"]) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(code))
                        commonCodes.Add(code);
                }
            }

            foreach (string code in commonCodes)
            {
                Logger.Log(30, $"{code} - Provvedimento #{numProvvedimento} già aggiunto", LogLevel.INFO);
            }

            if (commonCodes.Count > 0 && (provvedimentoSelezionato == "01" || provvedimentoSelezionato == "02"))
            {
                BlocksUtil.RemoveBlockNumDomanda(CONNECTION, sqlTransaction, commonCodes, "BRM", aaProvvedimento, "Area4");
            }

            string insertNewProvvedimenti = @"
INSERT INTO [dbo].[PROVVEDIMENTI]
(
    [Num_domanda],
    [tipo_provvedimento],
    [data_provvedimento],
    [Anno_accademico],
    [note],
    [num_provvedimento],
    [riga_valida],
    [data_validita]
)
SELECT DISTINCT
       d.num_domanda,
       @provvedimentoSelezionato,
       @dataProvvedimento,
       @aaProvvedimento,
       @notaProvvedimento,
       @numProvvedimento,
       1,
       CURRENT_TIMESTAMP
FROM Domanda d
INNER JOIN #TempNumDom t
    ON d.Num_domanda = t.NumDomanda
WHERE d.Anno_accademico = @aaProvvedimento
  AND d.Tipo_bando = 'LZ'
  AND NOT EXISTS
  (
      SELECT 1
      FROM #TempCommonCodes c
      WHERE c.NumDomanda = d.Num_domanda
  );";

            int affectedRows;
            using (var cmd = new SqlCommand(insertNewProvvedimenti, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.Add("@provvedimentoSelezionato", SqlDbType.VarChar, 10).Value = provvedimentoSelezionato;
                cmd.Parameters.Add("@dataProvvedimento", SqlDbType.DateTime).Value = dataProvvedimentoDate;
                cmd.Parameters.Add("@aaProvvedimento", SqlDbType.VarChar, 8).Value = aaProvvedimento;
                cmd.Parameters.Add("@notaProvvedimento", SqlDbType.VarChar, -1).Value = notaProvvedimento;
                cmd.Parameters.Add("@numProvvedimento", SqlDbType.VarChar, 50).Value = numProvvedimento;

                affectedRows = cmd.ExecuteNonQuery();
            }

            if (affectedRows <= 0)
            {
                Logger.Log(100, "Nessun provvedimento da aggiungere", LogLevel.INFO);
                Logger.Log(80, $"Studenti nel file: {inputValues.Count}", LogLevel.INFO);
                return;
            }

            Logger.Log(60, $"Modificati: {affectedRows} studenti", LogLevel.INFO);

            string updateDecadenzeSql = @"
UPDATE dt
SET data_fine_validita = CURRENT_TIMESTAMP
FROM Decadenze_tracciabilita_bs dt
INNER JOIN Domanda d
    ON dt.Anno_accademico = d.Anno_accademico
   AND dt.Cod_fiscale = d.Cod_fiscale
INNER JOIN #TempNumDom t
    ON d.Num_domanda = t.NumDomanda
LEFT JOIN #TempCommonCodes c
    ON c.NumDomanda = d.Num_domanda
WHERE d.Anno_accademico = @aaProvvedimento
  AND dt.Cod_beneficio = @beneficioProvvedimento
  AND c.NumDomanda IS NULL
  AND dt.data_fine_validita IS NULL;";

            using (var cmd = new SqlCommand(updateDecadenzeSql, CONNECTION, sqlTransaction))
            {
                cmd.Parameters.Add("@aaProvvedimento", SqlDbType.VarChar, 8).Value = aaProvvedimento;
                cmd.Parameters.Add("@beneficioProvvedimento", SqlDbType.VarChar, 2).Value = beneficioProvvedimento;

                int updated = cmd.ExecuteNonQuery();
                Logger.Log(70, $"Aggiornate data_fine_validita in Decadenze_tracciabilita_bs: {updated} righe.", LogLevel.INFO);
            }

            List<string> newCodes = new();
            string selectNewlyInserted = @"
SELECT DISTINCT t.NumDomanda
FROM #TempNumDom t
WHERE NOT EXISTS
(
    SELECT 1
    FROM #TempCommonCodes c
    WHERE c.NumDomanda = t.NumDomanda
);";

            using (var cmd = new SqlCommand(selectNewlyInserted, CONNECTION, sqlTransaction))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    string code = Convert.ToString(reader["NumDomanda"]) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(code))
                        newCodes.Add(code);
                }
            }

            foreach (string code in newCodes)
            {
                Logger.Log(40, $"{code}: aggiunto provvedimento #{numProvvedimento}", LogLevel.INFO);
            }

            if (provvedimentoSelezionato == "01" || provvedimentoSelezionato == "02")
            {
                BlocksUtil.RemoveBlockNumDomanda(CONNECTION, sqlTransaction, newCodes, "BRM", aaProvvedimento, "Area4");
            }

            Logger.Log(80, $"Studenti nel file: {inputValues.Count}", LogLevel.INFO);
        }

        private void HandleSpecificheImpegniInternal(DataTable allStudentsData, Panel procedurePanel)
        {
            if (CONNECTION == null)
                throw new InvalidOperationException("Connessione non disponibile.");

            if (sqlTransaction == null)
                throw new InvalidOperationException("Transazione non disponibile.");

            bool needNewSpecific = RequiresNewSpecificaForProvvedimento(provvedimentoSelezionato);

            _specificheNumDomandas = new List<string>();
            _specificheNumDomandaColumn = -1;
            _specificheFirstSelectedRowIndex = -1;
            _specificheImporti.Clear();

            Logger.Log(110, "Specifiche impegni: selezione numeri domanda", LogLevel.INFO);

            DataGridView? numDomandaGridView = null;
            DataGridView? importiGridView = null;

            try
            {
                numDomandaGridView = Utilities.CreateDataGridView(
                    allStudentsData,
                    _masterForm,
                    procedurePanel,
                    OnSpecificheNumDomandaClicked);

                numDomandaGridView.HandleDestroyed += (_, __) => _waitHandle.Set();

                ShowInfoMessage("Cliccare sul primo numero domanda per le specifiche impegni");
                _waitHandle.Reset();
                _waitHandle.WaitOne();

                _specificheNumDomandas = _specificheNumDomandas
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (_specificheNumDomandas.Count == 0)
                {
                    Logger.Log(111, "Specifiche impegni: nessun numero domanda valido selezionato", LogLevel.INFO);
                    return;
                }

                CloseSpecificheImpegniRows(_specificheNumDomandas);

                if (!needNewSpecific)
                {
                    Logger.Log(112, "Specifiche impegni: chiusura eseguita, nessuna nuova specifica da aprire", LogLevel.INFO);
                    return;
                }

                Logger.Log(113, "Specifiche impegni: selezione importi", LogLevel.INFO);

                importiGridView = Utilities.CreateDataGridView(
                    allStudentsData,
                    _masterForm,
                    procedurePanel,
                    OnSpecificheImportiClicked);

                importiGridView.HandleDestroyed += (_, __) => _waitHandle.Set();

                ShowInfoMessage("Cliccare sul primo importo attuale per le specifiche impegni");
                _waitHandle.Reset();
                _waitHandle.WaitOne();

                if (_specificheImporti.Count == 0)
                {
                    Logger.Log(114, "Specifiche impegni: nessun importo valido selezionato", LogLevel.INFO);
                    return;
                }

                int inserted = InsertSpecificheImpegniRows(_specificheImporti);
                Logger.Log(115, $"Specifiche impegni: inserite {inserted} nuove righe", LogLevel.INFO);
            }
            finally
            {
                DisposeGridSafe(numDomandaGridView);
                DisposeGridSafe(importiGridView);
            }
        }

        private static bool RequiresNewSpecificaForProvvedimento(string provvedimento)
        {
            return provvedimento switch
            {
                "01" => true,
                "02" => true,
                "05" => true,
                "09" => true,
                "13" => true,
                _ => false
            };
        }

        private void OnSpecificheNumDomandaClicked(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (sender is not DataGridView dataGridView || e.RowIndex < 0 || e.ColumnIndex < 0)
                    return;

                HashSet<string> values = new(StringComparer.OrdinalIgnoreCase);

                _specificheNumDomandaColumn = e.ColumnIndex;
                _specificheFirstSelectedRowIndex = e.RowIndex;

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

                _specificheNumDomandas = values.ToList();
            }
            finally
            {
                _waitHandle.Set();
            }
        }

        private void OnSpecificheImportiClicked(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (sender is not DataGridView dataGridView || e.ColumnIndex < 0)
                    return;

                if (_specificheNumDomandaColumn < 0 || _specificheFirstSelectedRowIndex < 0)
                    throw new InvalidOperationException("Specifiche impegni: selezione numeri domanda non valida.");

                _specificheImporti.Clear();

                for (int i = _specificheFirstSelectedRowIndex; i < dataGridView.Rows.Count; i++)
                {
                    object? numDomandaVar = dataGridView.Rows[i].Cells[_specificheNumDomandaColumn].Value;
                    object? importoVar = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;

                    string numDomanda = Convert.ToString(numDomandaVar, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                    string importoString = Convert.ToString(importoVar, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(numDomanda))
                        continue;

                    if (!long.TryParse(numDomanda, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        continue;

                    if (!TryParseMoney(importoString, out decimal importo))
                        continue;

                    _specificheImporti[numDomanda] = importo;
                }
            }
            finally
            {
                _waitHandle.Set();
            }
        }

        private void CloseSpecificheImpegniRows(List<string> numDomande)
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            CreateTempSpecificheNumDomTable();
            BulkInsertSpecificheNumDomande(numDomande);

            string sqlUpdate = @"
UPDATE si
SET si.data_fine_validita = @dataProvvedimento,
    si.Num_determina = @numDetermina,
    si.data_determina = @dataProvvedimento,
    si.descrizione_determina = @descrDetermina
FROM Specifiche_impegni si
INNER JOIN #TempSpecNumDomande t
    ON si.num_domanda = t.NumDomanda
WHERE si.anno_accademico = @selectedAA
  AND si.cod_beneficio = @selectedCodBeneficio
  AND si.data_fine_validita IS NULL;";

            using SqlCommand updateCommand = new(sqlUpdate, CONNECTION, sqlTransaction);
            updateCommand.Parameters.Add("@dataProvvedimento", SqlDbType.DateTime).Value = dataProvvedimentoDate;
            updateCommand.Parameters.Add("@numDetermina", SqlDbType.VarChar, 100).Value = numProvvedimento;
            updateCommand.Parameters.Add("@descrDetermina", SqlDbType.VarChar, -1).Value = notaProvvedimento;
            updateCommand.Parameters.Add("@selectedAA", SqlDbType.VarChar, 8).Value = aaProvvedimento;
            updateCommand.Parameters.Add("@selectedCodBeneficio", SqlDbType.VarChar, 2).Value = beneficioProvvedimento;

            int updated = updateCommand.ExecuteNonQuery();
            Logger.Log(116, $"Specifiche impegni: chiuse {updated} righe aperte", LogLevel.INFO);
        }

        private int InsertSpecificheImpegniRows(Dictionary<string, decimal> importi)
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            CreateTempSpecificheImportiTable();
            BulkInsertSpecificheImporti(importi);

            string determinaConferimento = string.IsNullOrWhiteSpace(numProvvedimento)
                ? string.Empty
                : $"{numProvvedimento} del {dataProvvedimentoDate:dd/MM/yyyy}";

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
    0,
    NULL,
    NULL
FROM #TempSpecImporti t
INNER JOIN Domanda d
    ON d.Anno_accademico = @selectedAA
   AND d.Num_domanda = t.NumDomanda
INNER JOIN Studente s
    ON s.Cod_fiscale = d.Cod_fiscale;";

            using SqlCommand insertCommand = new(sqlInsert, CONNECTION, sqlTransaction);
            insertCommand.Parameters.Add("@selectedAA", SqlDbType.VarChar, 8).Value = aaProvvedimento;
            insertCommand.Parameters.Add("@selectedCodBeneficio", SqlDbType.VarChar, 2).Value = beneficioProvvedimento;
            insertCommand.Parameters.Add("@tipoFondo", SqlDbType.VarChar, 50).Value = tipoFondo;
            insertCommand.Parameters.Add("@capitolo", SqlDbType.VarChar, 50).Value = capitolo;
            insertCommand.Parameters.Add("@determinaConferimento", SqlDbType.VarChar, 200).Value = determinaConferimento;
            insertCommand.Parameters.Add("@impegnoPR", SqlDbType.VarChar, 50).Value = impegnoPR;
            insertCommand.Parameters.Add("@impegnoSA", SqlDbType.VarChar, 50).Value = impegnoSA;
            insertCommand.Parameters.Add("@eseSA", SqlDbType.VarChar, 10).Value = eseSA;
            insertCommand.Parameters.Add("@esePR", SqlDbType.VarChar, 10).Value = esePR;

            return insertCommand.ExecuteNonQuery();
        }

        private void CreateTempSpecificheNumDomTable()
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            string sql = @"
IF OBJECT_ID('tempdb..#TempSpecNumDomande') IS NOT NULL
    DROP TABLE #TempSpecNumDomande;

CREATE TABLE #TempSpecNumDomande
(
    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY
);";

            using SqlCommand cmd = new(sql, CONNECTION, sqlTransaction);
            cmd.ExecuteNonQuery();
        }

        private void BulkInsertSpecificheNumDomande(IEnumerable<string> numDomande)
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
            bulk.DestinationTableName = "#TempSpecNumDomande";
            bulk.WriteToServer(dt);
        }

        private void CreateTempSpecificheImportiTable()
        {
            if (CONNECTION == null || sqlTransaction == null)
                throw new InvalidOperationException("Connessione o transazione non disponibile.");

            string sql = @"
IF OBJECT_ID('tempdb..#TempSpecImporti') IS NOT NULL
    DROP TABLE #TempSpecImporti;

CREATE TABLE #TempSpecImporti
(
    NumDomanda VARCHAR(50) COLLATE Latin1_General_CI_AS NOT NULL PRIMARY KEY,
    Importo DECIMAL(18,2) NOT NULL
);";

            using SqlCommand cmd = new(sql, CONNECTION, sqlTransaction);
            cmd.ExecuteNonQuery();
        }

        private void BulkInsertSpecificheImporti(Dictionary<string, decimal> importi)
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
            bulk.DestinationTableName = "#TempSpecImporti";
            bulk.WriteToServer(dt);
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