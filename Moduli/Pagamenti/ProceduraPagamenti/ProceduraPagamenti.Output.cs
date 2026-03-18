using DocumentFormat.OpenXml;
using ProcedureNet7.PagamentiProcessor;
using ProcedureNet7.Storni;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class ProceduraPagamenti
    {
        private void IncrementImpegno(string impegno, string categoriaCU, bool withSS, bool withDetrazioni, int count)
        {
            if (!impegnoAmount.TryGetValue(impegno, out var perImpegno))
                impegnoAmount[impegno] = perImpegno = new Dictionary<string, Dictionary<string, Dictionary<string, int>>>();

            if (!perImpegno.TryGetValue(categoriaCU, out var perCU))
                perImpegno[categoriaCU] = perCU = new Dictionary<string, Dictionary<string, int>>();

            var ssKey = withSS ? "ConSS" : "SenzaSS";
            if (!perCU.TryGetValue(ssKey, out var perSS))
                perCU[ssKey] = perSS = new Dictionary<string, int>();

            var detKey = withDetrazioni ? "ConDetrazione" : "SenzaDetrazione";
            if (!perSS.ContainsKey(detKey))
                perSS[detKey] = 0;

            perSS[detKey] += count;
        }
        private string GetCurrentPagamentoFolder()
        {
            string currentMonthName = DateTime.Now.ToString("MMMM").ToUpper();
            string currentYear = DateTime.Now.ToString("yyyy");

            string firstHalfAA = selectedAA.Substring(2, 2);
            string secondHalfAA = selectedAA.Substring(6, 2);

            // Cartella base del periodo (come già fai ora)
            string baseFolderPath = Utilities.EnsureDirectory(
                Path.Combine(selectedSaveFolder, currentMonthName + currentYear + "_" + firstHalfAA + secondHalfAA));

            // Codice tipo pagamento attuale (es. BSP0, BST0, ecc.)
            string currentCodTipoPagamento = tipoBeneficio + selectedTipoPagamento;

            string sqlTipoPagam =
                $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{currentCodTipoPagamento}'";

            using SqlCommand cmd = new(sqlTipoPagam, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };

            string pagamentoDescrizione = Convert.ToString(cmd.ExecuteScalar());

            if (string.IsNullOrWhiteSpace(pagamentoDescrizione))
                pagamentoDescrizione = currentCodTipoPagamento;

            // Cartella del pagamento corrente
            string beneficioFolderPath = Utilities.EnsureDirectory(
                Path.Combine(baseFolderPath, pagamentoDescrizione));

            return beneficioFolderPath;
        }


        private void GenerateOutputFiles()
        {
           // _detAccDisco.Reset();
           // _detAccPnrr.Reset();
            _flussoWrittenCF.Clear(); // reset tracker for this run
            _flussoFilesByCF.Clear();

            Logger.LogInfo(60, $"Lavorazione studenti - Generazione files");

            string beneficioFolderPath = GetCurrentPagamentoFolder();

            bool doAllImpegni = selectedImpegno == "0000";
            IEnumerable<string> impegnoList = doAllImpegni ? impegniList : new List<string> { selectedImpegno };
            try
            {
                foreach (string impegno in impegnoList)
                {
                    Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno}");
                    ProcessImpegno(impegno, beneficioFolderPath);
                }
            }
            catch
            {
                throw;
            }
            Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Completato");

            if (!insertInDatabase) return;

            Logger.LogInfo(10, "Starting index optimization");

            string rebuildIndexesSQL = @"
        DECLARE @TableName NVARCHAR(255);
        DECLARE IndexCursor CURSOR FOR
        SELECT DISTINCT t.name 
        FROM sys.indexes i 
        INNER JOIN sys.tables t ON i.object_id = t.object_id 
        WHERE t.name IN ('MOVIMENTI_CONTABILI_ELEMENTARI', 'MOVIMENTI_CONTABILI_GENERALI', 'STATI_DEL_MOVIMENTO_CONTABILE');

        OPEN IndexCursor;
        FETCH NEXT FROM IndexCursor INTO @TableName;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            EXEC ('ALTER INDEX ALL ON ' + @TableName + ' REORGANIZE;');
            FETCH NEXT FROM IndexCursor INTO @TableName;
        END;

        CLOSE IndexCursor;
        DEALLOCATE IndexCursor;";
            SqlCommand rebuildIndexesCmd = new(rebuildIndexesSQL, CONNECTION, sqlTransaction) { CommandTimeout = 9000000 };
            _ = rebuildIndexesCmd.ExecuteNonQuery();

            Logger.LogInfo(10, "Updating statistics");
            string updateStatisticsSQL = @"
        UPDATE STATISTICS [MOVIMENTI_CONTABILI_ELEMENTARI];
        UPDATE STATISTICS [MOVIMENTI_CONTABILI_GENERALI];
        UPDATE STATISTICS [STATI_DEL_MOVIMENTO_CONTABILE];";
            SqlCommand updateStatisticsCmd = new(updateStatisticsSQL, CONNECTION, sqlTransaction) { CommandTimeout = 9000000 };
            _ = updateStatisticsCmd.ExecuteNonQuery();

            Logger.LogInfo(10, "Index and statistics optimization complete.");
        }

        private (bool useCategorieCU, bool useServizioSanitario, bool splitByEnte) GetLayoutConfig()
        {
            var aa = (selectedAA ?? string.Empty).Trim();
            bool isTassaRegionale = isTR; // già valorizzato quando scegli il pagamento

            bool useCategorieCU;
            bool useServizioSanitario;

            switch (aa)
            {
                case "20232024":
                    // niente CU, niente SS
                    useCategorieCU = false;
                    useServizioSanitario = false;
                    break;

                case "20242025":
                    // solo CU, niente SS
                    useCategorieCU = true;
                    useServizioSanitario = false;
                    break;

                case "20252026":
                    // CU + SS
                    useCategorieCU = true;
                    useServizioSanitario = true;
                    break;

                default:
                    // fallback per anni futuri: comportati come 2025/2026
                    useCategorieCU = true;
                    useServizioSanitario = true;
                    break;
            }

            // Tassa regionale: non separare per ente
            bool splitByEnte = !isTassaRegionale;
            if (isTassaRegionale)
            {
                useCategorieCU = false;
                useServizioSanitario = false;
            }

            return (useCategorieCU, useServizioSanitario, splitByEnte);
        }

        private void ProcessImpegno(string impegno, string beneficioFolderPath)
        {
            //var fondo = ResolveTipoFondoForImpegno(impegno);
            //var acc = string.Equals(fondo, "PNRR", StringComparison.OrdinalIgnoreCase) ? _detAccPnrr : _detAccDisco;

            var groupedStudents = studentiDaPagare.Values
                .Where(s => s.InformazioniPagamento.NumeroImpegno == impegno)
                .ToList();

            if (!groupedStudents.Any())
                return;

            string sqlImpegno =
                $"SELECT Descr FROM Impegni WHERE Num_impegno = '{impegno}' AND categoria_pagamento = '{categoriaPagam}'";
            using SqlCommand cmdImpegno = new(sqlImpegno, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            string impegnoDescrizione = Convert.ToString(cmdImpegno.ExecuteScalar());
            string impegnoFolder = Utilities.EnsureDirectory(
                Path.Combine(beneficioFolderPath, $"imp-{impegno}-{impegnoDescrizione}"));

            // Configurazione layout in base ad AA + tipo pagamento
            var (useCategorieCU, useServizioSanitario, splitByEnte) = GetLayoutConfig();

            if (useCategorieCU)
            {
                // 2024/2025 e 2025/2026: distingui per categoria CU
                foreach (string cat in new[] { "111", "211", "311" })
                {
                    var studentsInCategory = groupedStudents
                        .Where(s => s.InformazioniPagamento.CategoriaCU == cat)
                        .ToList();

                    if (!studentsInCategory.Any())
                        continue;

                    string sqlCat =
                        $"SELECT descrizione FROM tipologie_categoria_cu WHERE categoria_CU = '{cat}'";
                    using SqlCommand cmdCat = new(sqlCat, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    string catDescrizione = Convert.ToString(cmdCat.ExecuteScalar());

                    string catFolder = Utilities.EnsureDirectory(
                        Path.Combine(impegnoFolder, catDescrizione));

                    ProcessCategory(cat, catDescrizione, studentsInCategory, catFolder, impegno,
                                    useServizioSanitario, splitByEnte);
                }
            }
            else
            {
                // 2023/2024: niente split per categoria CU, lavoro tutto insieme
                string catDescrizione = "Tutti";
                string catFolder = impegnoFolder;

                ProcessCategory(
                    categoriaCU: "ALL",
                    catDescrizione: catDescrizione,
                    students: groupedStudents,
                    catFolder: catFolder,
                    impegno: impegno,
                    useServizioSanitario: false,   // nessun SS per 23/24
                    splitByEnte: splitByEnte       // per TR sarà comunque false
                );
            }

            try
            {
                var auditDt = BuildFullAuditDataTable(groupedStudents, impegno);
                if (auditDt.Rows.Count > 0)
                {
                    Utilities.ExportDataTableToExcel(auditDt, impegnoFolder, true, "AUDIT_Studenti_Completo");
                    Logger.LogInfo(20, $"Impegno {impegno}: esportato audit completo studenti ({auditDt.Rows.Count} righe).");
                }
                else
                {
                    Logger.LogInfo(20, $"Impegno {impegno}: nessuno studente da esportare nel dump audit.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(10, $"Impegno {impegno}: errore export audit completo: {ex.Message}");
            }

            // === AFTER processing all categories for this impegno:
            // Export Excel with CF not written in any flusso for THIS impegno
            var allCfInImpegno = groupedStudents
                .Select(s => (s.InformazioniPersonali.CodFiscale, s))
                .GroupBy(x => x.CodFiscale, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First().s)
                .ToList();

            var missing = allCfInImpegno
                .Where(s => !_flussoWrittenCF.Contains(s.InformazioniPersonali.CodFiscale))
                .ToList();

            if (missing.Count > 0)
            {
                Logger.LogInfo(20, $"Impegno {impegno}: {missing.Count} CF non presenti in alcun flusso. Esporto Excel di log dettagliato.");

                var dt = new DataTable();
                dt.Columns.Add("Impegno");
                dt.Columns.Add("CategoriaCU");
                dt.Columns.Add("NumDomanda");
                dt.Columns.Add("CodFiscale");
                dt.Columns.Add("Cognome");
                dt.Columns.Add("Nome");
                dt.Columns.Add("AnnoCorso");
                dt.Columns.Add("HasPA");
                dt.Columns.Add("TipoStudente");
                dt.Columns.Add("SelectedRichiestoPA");
                dt.Columns.Add("CodEnte");
                dt.Columns.Add("CategoriaPagam");
                dt.Columns.Add("Motivo");

                foreach (var s in missing)
                {
                    string catCU = s.InformazioniPagamento?.CategoriaCU ?? "";
                    bool hasPA = HasPAForStudent(s, categoriaPagam);

                    string motivo = BuildNonFlussoReason(
                        s,
                        catCU,
                        selectedRichiestoPA,
                        tipoStudente,
                        categoriaPagam);

                    dt.Rows.Add(
                        impegno,
                        catCU,
                        s.InformazioniPersonali?.NumDomanda,
                        s.InformazioniPersonali?.CodFiscale,
                        s.InformazioniPersonali?.Cognome,
                        s.InformazioniPersonali?.Nome,
                        s.InformazioniIscrizione?.AnnoCorso.ToString(),
                        hasPA ? "true" : "false",
                        tipoStudente,
                        selectedRichiestoPA,
                        s.InformazioniIscrizione?.CodEnte,
                        categoriaPagam,
                        motivo
                    );
                }

                Utilities.ExportDataTableToExcel(
                    dt,
                    Path.Combine(beneficioFolderPath, $"imp-{impegno}-{impegnoDescrizione}"),
                    true,
                    "CF_non_in_flusso");
            }
            else
            {
                Logger.LogInfo(20, $"Impegno {impegno}: tutti i CF risultano emessi in almeno un flusso.");
            }

            void ProcessCategory(
                string categoriaCU,
                string catDescrizione,
                List<StudentePagamenti> students,
                string catFolder,
                string impegno,
                bool useServizioSanitario,
                bool splitByEnte)
            {
                Logger.LogInfo(60, $"Lavorazione studenti - Impegno {impegno} - Categoria {catDescrizione}");

                try
                {
                    // =========================================================
                    // NEW: Check difformità CU vs posto alloggio (211/311)
                    // - 211: SOLO SENZA alloggio
                    // - 311: SOLO CON alloggio
                    // - 111: ammessi entrambi
                    // Se difforme: segnalato (log) e rimosso dai flussi.
                    // La motivazione dettagliata viene restituita da BuildNonFlussoReason.
                    // =========================================================
                    if (categoriaCU == "211" || categoriaCU == "311")
                    {
                        var invalid = students
                            .Where(s => IsCuAlloggioMismatch(s, categoriaCU, categoriaPagam, out _))
                            .ToList();

                        if (invalid.Count > 0)
                        {
                            Logger.LogError(20,
                                $"Impegno {impegno} - CategoriaCU {categoriaCU}: trovati {invalid.Count} studenti con difformità CU/alloggio. Rimossi dai flussi.");
                            var invalidSet = new HashSet<StudentePagamenti>(invalid);
                            students = students.Where(s => !invalidSet.Contains(s)).ToList();
                        }

                        if (students.Count == 0)
                            return;
                    }

                    // Gestione cartelle base per Servizio Sanitario
                    string noSSBasePath;
                    string withSSBasePath;

                    List<StudentePagamenti> noSS;
                    List<StudentePagamenti> withSS;

                    if (useServizioSanitario)
                    {
                        // 2025/2026 (e default): split Con/Senza SS
                        var noSSBase = Utilities.EnsureDirectory(Path.Combine(catFolder, "SenzaServizioSanitario"));
                        var withSSBase = Utilities.EnsureDirectory(Path.Combine(catFolder, "ConServizioSanitario"));

                        noSSBasePath = noSSBase;
                        withSSBasePath = withSSBase;

                        noSS = students.Where(s => s.InformazioniBeneficio?.HaServizioSanitario == false).ToList();
                        withSS = students.Where(s => s.InformazioniBeneficio?.HaServizioSanitario == true).ToList();
                    }
                    else
                    {
                        // 2023/2024 e 2024/2025: niente split SS, tutto in un’unica cartella
                        noSSBasePath = catFolder;
                        withSSBasePath = catFolder;

                        noSS = students;                          // tutto in noSS
                        withSS = new List<StudentePagamenti>();   // vuota
                    }

                    bool HasAlloggioEff(StudentePagamenti s) =>
                        IsAlloggioEffective(s, categoriaCU, categoriaPagam);

                    bool NoAlloggioEff(StudentePagamenti s) => !HasAlloggioEff(s);

                    // Split finali (usano noSS / withSS in base alla config sopra)
                    var studentsWithPANoSS = noSS.Where(HasAlloggioEff).ToList();
                    var studentsWithoutPANoSS = noSS.Where(NoAlloggioEff).ToList();
                    var studentsWithPAWithSS = withSS.Where(HasAlloggioEff).ToList();
                    var studentsWithoutPAWithSS = withSS.Where(NoAlloggioEff).ToList();

                    // Dispatch sui casi in base alla configurazione
                    ProcessBlock(studentsWithPANoSS, noSSBasePath, flagPA: true, withSS: false);
                    ProcessBlock(studentsWithPAWithSS, withSSBasePath, flagPA: true, withSS: true);
                    ProcessBlock(studentsWithoutPANoSS, noSSBasePath, flagPA: false, withSS: false);
                    ProcessBlock(studentsWithoutPAWithSS, withSSBasePath, flagPA: false, withSS: true);

                    void ProcessBlock(List<StudentePagamenti> target, string baseFolder, bool flagPA, bool withSS)
                    {
                        if (target == null || target.Count == 0) return;

                        if (flagPA)
                        {
                            // Con detrazione / con posto alloggio
                            // acc.AddWithPA(impegno, categoriaCU, target, categoriaPagam);
                            string paFolder = Utilities.EnsureDirectory(Path.Combine(baseFolder, "CON DETRAZIONE"));

                            // Per tassa regionale NON separo per ente
                            if (splitByEnte)
                            {
                                ProcessStudentsByCodEnte(selectedCodEnte, target, paFolder, impegno, categoriaCU);
                            }
                            else
                            {
                                // Unico flusso per tutti gli enti
                                ProcessStudentsByAnnoCorso(
                                    target,
                                    paFolder,
                                    processMatricole: tipoStudente == "2" || tipoStudente == "1",
                                    processAnniSuccessivi: tipoStudente == "2" || tipoStudente == "3",
                                    nomeFileInizio: "ConDetrazioni",
                                    codEnteFlusso: "00",
                                    impegnoFlusso: impegno);
                            }

                            GenerateGiuliaFile(paFolder, target, impegno);
                            IncrementImpegno(impegno, categoriaCU, withSS, true, target.Count);
                        }
                        else
                        {
                            // Senza detrazione / senza posto alloggio
                            //acc.AddWithoutPA(impegno, categoriaCU, target);

                            // Split per anno corso ed export Excel
                            if (tipoStudente == "2") // matricole + anni successivi
                            {
                                ProcessStudentsByAnnoCorso(
                                    target,
                                    baseFolder,
                                    processMatricole: true,
                                    processAnniSuccessivi: true,
                                    nomeFileInizio: "SenzaDetrazioni",
                                    codEnteFlusso: "00",
                                    impegnoFlusso: impegno);
                            }
                            else if (tipoStudente == "1") // solo matricole
                            {
                                ProcessStudentsByAnnoCorso(
                                    target,
                                    baseFolder,
                                    processMatricole: true,
                                    processAnniSuccessivi: false,
                                    nomeFileInizio: "SenzaDetrazioni",
                                    codEnteFlusso: "00",
                                    impegnoFlusso: impegno);
                            }
                            else if (tipoStudente == "3") // solo anni successivi
                            {
                                ProcessStudentsByAnnoCorso(
                                    target,
                                    baseFolder,
                                    processMatricole: false,
                                    processAnniSuccessivi: true,
                                    nomeFileInizio: "SenzaDetrazioni",
                                    codEnteFlusso: "00",
                                    impegnoFlusso: impegno);
                            }

                            var mat = GenerareExcelDataTableNoDetrazioni(
                                target.Where(s => s.InformazioniIscrizione?.AnnoCorso == 1).ToList(), impegno);
                            if (mat != null && mat.Rows.Count > 0)
                                Utilities.ExportDataTableToExcel(mat, baseFolder, false, "Matricole");

                            var succ = GenerareExcelDataTableNoDetrazioni(
                                target.Where(s => s.InformazioniIscrizione?.AnnoCorso != 1).ToList(), impegno);
                            if (succ != null && succ.Rows.Count > 0)
                                Utilities.ExportDataTableToExcel(succ, baseFolder, false, "AnniSuccessivi");

                            IncrementImpegno(impegno, categoriaCU, withSS, false, target.Count);
                        }
                    }
                }
                catch
                {
                    throw;
                }
            }

            void ProcessStudentsByAnnoCorso(
                List<StudentePagamenti> students,
                string folderPath,
                bool processMatricole,
                bool processAnniSuccessivi,
                string nomeFileInizio,
                string codEnteFlusso,
                string impegnoFlusso)
            {
                if (processMatricole && processAnniSuccessivi)
                {
                    ProcessAndWriteStudents(students, folderPath, $"{nomeFileInizio}", codEnteFlusso, impegnoFlusso);
                }
                else
                {
                    if (processMatricole)
                        ProcessAndWriteStudents(
                            students.Where(s => s.InformazioniIscrizione.AnnoCorso == 1).ToList(),
                            folderPath,
                            $"{nomeFileInizio}_Matricole",
                            codEnteFlusso,
                            impegnoFlusso);

                    if (processAnniSuccessivi)
                        ProcessAndWriteStudents(
                            students.Where(s => s.InformazioniIscrizione.AnnoCorso != 1).ToList(),
                            folderPath,
                            $"{nomeFileInizio}_AnniSuccessivi",
                            codEnteFlusso,
                            impegnoFlusso);
                }
            }

            void ProcessStudentsByCodEnte(
                string selectedCodEnte,
                List<StudentePagamenti> studentsWithPA,
                string newFolderPath,
                string impegno,
                string categoriaCU)
            {
                if (studentsWithPA.Count <= 0) return;

                bool allCodEnte = selectedCodEnte == "00";
                Dictionary<string, List<string>> codEnteGroups = new();

                if (allCodEnte)
                {
                    codEnteGroups.Add("02", new List<string> { "02" });
                    codEnteGroups.Add("05", new List<string> { "05" });
                    var otherCodEntes = studentsWithPA.Select(s => s.InformazioniIscrizione.CodEnte)
                                                     .Distinct()
                                                     .Where(c => c != "02" && c != "05")
                                                     .ToList();
                    codEnteGroups.Add("Roma", otherCodEntes);
                }
                else
                {
                    codEnteGroups.Add(selectedCodEnte, new List<string> { selectedCodEnte });
                }

                foreach (var group in codEnteGroups)
                {
                    string groupName = group.Key;
                    List<string> groupCodEntes = group.Value;

                    var studentsInGroup = studentsWithPA.Where(s => groupCodEntes.Contains(s.InformazioniIscrizione.CodEnte)).ToList();
                    if (studentsInGroup.Count == 0) continue;

                    string nomeCodEnte = "";

                    if (groupName == "Roma") nomeCodEnte = "Roma";
                    else if (groupName == "02") nomeCodEnte = "Cassino";
                    else if (groupName == "05") nomeCodEnte = "Viterbo";

                    Logger.LogInfo(60, $"Lavorazione studenti - Generazione files - Impegno n°{impegno} - Flusso con detrazioni ente: {nomeCodEnte}");
                    string specificFolderPath = Utilities.EnsureDirectory(Path.Combine(newFolderPath, $"{nomeCodEnte}"));

                    if (tipoStudente == "2")
                    {
                        ProcessStudentsByAnnoCorso(studentsInGroup, specificFolderPath, true, true, "Con Detrazioni_" + nomeCodEnte, groupName, impegno);
                    }
                    else
                    {
                        bool processMatricole = tipoStudente == "0";
                        ProcessStudentsByAnnoCorso(studentsInGroup, specificFolderPath, processMatricole, !processMatricole, "Con Detrazioni_" + nomeCodEnte, groupName, impegno);
                    }
                }

                List<string> sediStudi = studentsWithPA.Select(s => s.InformazioniIscrizione.CodEnte).Distinct().ToList();
                DataTable dataTableMatricole = GenerareExcelDataTableConDetrazioni(studentsWithPA.Where(s => s.InformazioniIscrizione.AnnoCorso == 1).ToList(), sediStudi, impegno);
                DataTable dataTableASuccessivi = GenerareExcelDataTableConDetrazioni(studentsWithPA.Where(s => s.InformazioniIscrizione.AnnoCorso != 1).ToList(), sediStudi, impegno);

                if (dataTableMatricole != null && dataTableMatricole.Rows.Count > 0)
                    Utilities.ExportDataTableToExcel(dataTableMatricole, newFolderPath, false, "Matricole");
                if (dataTableASuccessivi != null && dataTableASuccessivi.Rows.Count > 0)
                    Utilities.ExportDataTableToExcel(dataTableASuccessivi, newFolderPath, false, "AnniSuccessivi");
            }

            void ProcessAndWriteStudents(List<StudentePagamenti> students, string folderPath, string fileName, string codEnteFlusso, string impegnoFlusso)
            {
                if (students.Any())
                {
                    DataTable dataTableFlusso = GenerareFlussoDataTable(students, codEnteFlusso);

                    // mark CFs as written + remember which flusso file
                    string baseName = $"flusso_{fileName}_{impegnoFlusso}";
                    foreach (var st in students)
                    {
                        var cf = st.InformazioniPersonali.CodFiscale;
                        if (string.IsNullOrWhiteSpace(cf)) continue;

                        _flussoWrittenCF.Add(cf);
                        if (!_flussoFilesByCF.TryGetValue(cf, out var list))
                        {
                            list = new List<string>();
                            _flussoFilesByCF[cf] = list;
                        }
                        list.Add(baseName);
                    }

                    if (dataTableFlusso != null && dataTableFlusso.Rows.Count > 0)
                    {
                        Utilities.WriteDataTableToTextFile(dataTableFlusso, folderPath, baseName);
                    }
                    if (insertInDatabase)
                    {
                        InsertIntoMovimentazioni(students, impegnoFlusso);
                    }
                    studentiProcessatiAmount += students.Count;
                }
            }
        }

        private DataTable GenerareFlussoDataTable(List<StudentePagamenti> studentiDaGenerare, string codEnteFlusso)
        {
            if (!studentiDaGenerare.Any())
                return new DataTable();

            DataTable studentsData = new();
            _ = studentsData.Columns.Add("Incrementale", typeof(int));
            _ = studentsData.Columns.Add("Cod_fiscale", typeof(string));
            _ = studentsData.Columns.Add("Cognome", typeof(string));
            _ = studentsData.Columns.Add("Nome", typeof(string));
            _ = studentsData.Columns.Add("totale_lordo", typeof(double));
            _ = studentsData.Columns.Add("reversali", typeof(double));
            _ = studentsData.Columns.Add("importo_netto", typeof(double));
            _ = studentsData.Columns.Add("conferma_pagamento", typeof(int));
            _ = studentsData.Columns.Add("IBAN", typeof(string));
            _ = studentsData.Columns.Add("Istituto_bancario", typeof(string));
            _ = studentsData.Columns.Add("italiano", typeof(string));
            _ = studentsData.Columns.Add("indirizzo_residenza", typeof(string));
            _ = studentsData.Columns.Add("cod_catastale_residenza", typeof(string));
            _ = studentsData.Columns.Add("provincia_residenza", typeof(string));
            _ = studentsData.Columns.Add("cap_residenza", typeof(string));
            _ = studentsData.Columns.Add("nazione_citta_residenza", typeof(string));
            _ = studentsData.Columns.Add("sesso", typeof(string));
            _ = studentsData.Columns.Add("data_nascita", typeof(string));
            _ = studentsData.Columns.Add("luogo_nascita", typeof(string));
            _ = studentsData.Columns.Add("cod_catastale_luogo_nascita", typeof(string));
            _ = studentsData.Columns.Add("provincia_nascita", typeof(string));
            _ = studentsData.Columns.Add("vuoto1", typeof(string));
            _ = studentsData.Columns.Add("vuoto2", typeof(string));
            _ = studentsData.Columns.Add("vuoto3", typeof(string));
            _ = studentsData.Columns.Add("vuoto4", typeof(string));
            _ = studentsData.Columns.Add("vuoto5", typeof(string));
            _ = studentsData.Columns.Add("mail", typeof(string));
            _ = studentsData.Columns.Add("vuoto6", typeof(string));
            _ = studentsData.Columns.Add("telefono", typeof(long));

            int incremental = 1;

            foreach (StudentePagamenti studente in studentiDaGenerare)
            {
                _ = DateTime.TryParse(selectedDataRiferimento, out DateTime dataTabella);
                string dataCreazioneTabella = dataTabella.ToString("ddMMyy");

                string annoAccademicoBreve = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));

                string mandatoProvvisorio = selectedNumeroMandato;
                if (string.IsNullOrWhiteSpace(selectedNumeroMandato))
                {
                    mandatoProvvisorio = $"{codTipoPagamento}_{dataCreazioneTabella}_{annoAccademicoBreve}_{studente.InformazioniPagamento.NumeroImpegno}_{codEnteFlusso}";
                }
                studente.SetMandatoProvvisorio(mandatoProvvisorio);

                int straniero = studente.InformazioniSede.Residenza.provincia == "EE" ? 0 : 1;
                string indirizzoResidenza = straniero == 0 ? studente.InformazioniSede.Residenza.indirizzo.Replace("//", "-") : studente.InformazioniSede.Residenza.indirizzo;
                string capResidenza = straniero == 0 ? "00000" : studente.InformazioniSede.Residenza.CAP;
                string dataSenzaSlash = studente.InformazioniPersonali.DataNascita.Replace("/", "");

                bool hasAssegnazione = (categoriaPagam == "PR"
                                        && studente.InformazioniPagamento.Detrazioni != null
                                        && studente.InformazioniPagamento.Detrazioni.Count > 0
                                        && studente.InformazioniPagamento.Detrazioni.FirstOrDefault(d => d.codReversale == "01") != null)
                                       || (studente.InformazioniPagamento.Assegnazioni != null
                                           && studente.InformazioniPagamento.Assegnazioni.Count > 0);

                double accontoPA = studente.InformazioniPagamento.ImportoAccontoPA;
                if (categoriaPagam == "SA")
                {
                    accontoPA = 0;
                }

                _ = studentsData.Rows.Add(
                    incremental,
                    studente.InformazioniPersonali.CodFiscale,
                    studente.InformazioniPersonali.Cognome,
                    studente.InformazioniPersonali.Nome,
                    studente.InformazioniPagamento.ImportoDaPagareLordo,
                    hasAssegnazione ? (studente.InformazioniPagamento.ImportoSaldoPA == 0 ? accontoPA : studente.InformazioniPagamento.ImportoSaldoPA) : 0,
                    studente.InformazioniPagamento.ImportoDaPagare,
                    1,
                    studente.InformazioniConto.IBAN,
                    studente.InformazioniConto.Swift,
                    straniero,
                    indirizzoResidenza,
                    studente.InformazioniSede.Residenza.codComune,
                    studente.InformazioniSede.Residenza.provincia,
                    capResidenza,
                    studente.InformazioniSede.Residenza.nomeComune,
                    studente.InformazioniPersonali.Sesso,
                    dataSenzaSlash,
                    studente.InformazioniPersonali.LuogoNascita.nomeComune,
                    studente.InformazioniPersonali.LuogoNascita.codComune,
                    studente.InformazioniPersonali.LuogoNascita.provincia,
                    "",
                    "",
                    "",
                    "",
                    "",
                    studente.InformazioniPersonali.IndirizzoEmail,
                    "",
                    studente.InformazioniPersonali.Telefono
                );
                incremental++;
            }
            return studentsData;
        }

        private void ProcessAndWriteStudentsWithSSUnifiedPerCategoria(List<StudentePagamenti> students, string folderPath, string impegnoFlusso, string categoriaCUFlusso)
        {
            if (students == null || students.Count == 0) return;

            DataTable dt = GenerareFlussoDataTableWithOverridePerCategoria(students);

            string baseName = $"flusso_WithSS_{categoriaCUFlusso}_{impegnoFlusso}";
            foreach (var st in students)
            {
                var cf = st.InformazioniPersonali?.CodFiscale;
                if (string.IsNullOrWhiteSpace(cf)) continue;

                _flussoWrittenCF.Add(cf);
                if (!_flussoFilesByCF.TryGetValue(cf, out var list))
                {
                    list = new List<string>();
                    _flussoFilesByCF[cf] = list;
                }
                list.Add(baseName);
            }

            if (dt != null && dt.Rows.Count > 0)
                Utilities.WriteDataTableToTextFile(dt, folderPath, baseName);

            if (insertInDatabase)
                InsertIntoMovimentazioni(students, impegnoFlusso, categoriaPagam == "PR" ? "BSN0" : "BSNS");
        }

        private DataTable GenerareFlussoDataTableWithOverridePerCategoria(List<StudentePagamenti> studenti)
        {
            if (studenti == null || studenti.Count == 0) return new DataTable();

            var t = new DataTable();
            t.Columns.Add("Incrementale", typeof(int));
            t.Columns.Add("Cod_fiscale", typeof(string));
            t.Columns.Add("Cognome", typeof(string));
            t.Columns.Add("Nome", typeof(string));
            t.Columns.Add("totale_lordo", typeof(double));
            t.Columns.Add("reversali", typeof(double));
            t.Columns.Add("importo_netto", typeof(double));
            t.Columns.Add("conferma_pagamento", typeof(int));
            t.Columns.Add("IBAN", typeof(string));
            t.Columns.Add("Istituto_bancario", typeof(string));
            t.Columns.Add("italiano", typeof(string));
            t.Columns.Add("indirizzo_residenza", typeof(string));
            t.Columns.Add("cod_catastale_residenza", typeof(string));
            t.Columns.Add("provincia_residenza", typeof(string));
            t.Columns.Add("cap_residenza", typeof(string));
            t.Columns.Add("nazione_citta_residenza", typeof(string));
            t.Columns.Add("sesso", typeof(string));
            t.Columns.Add("data_nascita", typeof(string));
            t.Columns.Add("luogo_nascita", typeof(string));
            t.Columns.Add("cod_catastale_luogo_nascita", typeof(string));
            t.Columns.Add("provincia_nascita", typeof(string));
            t.Columns.Add("vuoto1", typeof(string));
            t.Columns.Add("vuoto2", typeof(string));
            t.Columns.Add("vuoto3", typeof(string));
            t.Columns.Add("vuoto4", typeof(string));
            t.Columns.Add("vuoto5", typeof(string));
            t.Columns.Add("mail", typeof(string));
            t.Columns.Add("vuoto6", typeof(string));
            t.Columns.Add("telefono", typeof(long));

            int i = 1;
            foreach (var s in studenti)
            {
                _ = DateTime.TryParse(selectedDataRiferimento, out DateTime dataTabella);
                string dataCreazioneTabella = dataTabella.ToString("ddMMyy");

                string annoAccademicoBreve = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));

                string mandatoProvvisorio = selectedNumeroMandato;
                string tipo = categoriaPagam == "PR" ? "BSN0" : "BSNS";
                if (string.IsNullOrWhiteSpace(selectedNumeroMandato))
                {
                    mandatoProvvisorio = $"{tipo}_{dataCreazioneTabella}_{annoAccademicoBreve}_{s.InformazioniPagamento.NumeroImpegno}_SSN";
                }
                s.SetMandatoProvvisorio(mandatoProvvisorio);

                int straniero = s?.InformazioniSede?.Residenza?.provincia == "EE" ? 0 : 1;
                string indirizzo = s?.InformazioniSede?.Residenza?.indirizzo ?? "";
                if (straniero == 0) indirizzo = indirizzo.Replace("//", "-");
                string cap = straniero == 0 ? "00000" : (s?.InformazioniSede?.Residenza?.CAP ?? "");
                string dn = (s?.InformazioniPersonali?.DataNascita ?? "").Replace("/", "");

                // override importi
                const double LORDO = 100.0;
                const double DETRAZIONI = 100.0;
                const double NETTO = 0.0;

                t.Rows.Add(
                    i++,
                    s?.InformazioniPersonali?.CodFiscale,
                    s?.InformazioniPersonali?.Cognome,
                    s?.InformazioniPersonali?.Nome,
                    LORDO,
                    DETRAZIONI,
                    NETTO,
                    1,
                    s?.InformazioniConto?.IBAN,
                    s?.InformazioniConto?.Swift,
                    straniero,
                    indirizzo,
                    s?.InformazioniSede?.Residenza?.codComune,
                    s?.InformazioniSede?.Residenza?.provincia,
                    cap,
                    s?.InformazioniSede?.Residenza?.nomeComune,
                    s?.InformazioniPersonali?.Sesso,
                    dn,
                    s?.InformazioniPersonali?.LuogoNascita?.nomeComune,
                    s?.InformazioniPersonali?.LuogoNascita?.codComune,
                    s?.InformazioniPersonali?.LuogoNascita?.provincia,
                    "",
                    "",
                    "",
                    "",
                    "",
                    s?.InformazioniPersonali?.IndirizzoEmail,
                    "",
                    s?.InformazioniPersonali?.Telefono
                );
            }
            return t;
        }

        private static void GenerateGiuliaFile(string newFolderPath, List<StudentePagamenti> studentsWithPA, string impegno)
        {
            DataTable dataTable = GenerareGiuliaDataTable(studentsWithPA, impegno);
            if (dataTable.Rows.Count > 0)
            {
                Utilities.ExportDataTableToExcel(dataTable, newFolderPath, true, "Dettaglio PA");
            }
        }

        private static DataTable GenerareGiuliaDataTable(List<StudentePagamenti> studentsWithPA, string impegno)
        {
            Logger.LogInfo(60, $"Lavorazione studenti - impegno n°{impegno} - Generazione Dettaglio PA");
            int progressivo = 1;
            DataTable returnDataTable = new();

            _ = returnDataTable.Columns.Add("Progressivo");
            _ = returnDataTable.Columns.Add("ID Assegnazione");
            _ = returnDataTable.Columns.Add("Cognome");
            _ = returnDataTable.Columns.Add("Nome");
            _ = returnDataTable.Columns.Add("CodFiscale");
            _ = returnDataTable.Columns.Add("Residenza");
            _ = returnDataTable.Columns.Add("Data decorrenza");
            _ = returnDataTable.Columns.Add("Data fine assegnazione");
            _ = returnDataTable.Columns.Add("Data inizio PA");
            _ = returnDataTable.Columns.Add("Data fine PA");
            _ = returnDataTable.Columns.Add("Num giorni");
            _ = returnDataTable.Columns.Add("Importo borsa totale");
            _ = returnDataTable.Columns.Add("Importo lordo borsa");
            _ = returnDataTable.Columns.Add("Acconto PA");
            _ = returnDataTable.Columns.Add("Saldo PA");
            _ = returnDataTable.Columns.Add("Importo netto borsa");
            _ = returnDataTable.Columns.Add("Stato correttezza");
            _ = returnDataTable.Columns.Add("Controllo status sede");

            foreach (StudentePagamenti studente in studentsWithPA)
            {
                if (studente.InformazioniPagamento.Assegnazioni == null || studente.InformazioniPagamento.Assegnazioni.Count <= 0)
                    continue;

                double accontoPA = Math.Round(studente.InformazioniPagamento.ImportoAccontoPA, 2);
                double saldoPA = Math.Round(studente.InformazioniPagamento.ImportoSaldoPA, 2);
                double saldo = Math.Round(studente.InformazioniPagamento.ImportoDaPagare, 2);

                DateTime dataIniziale = DateTime.MinValue;
                DateTime dataFinale = DateTime.MinValue;

                foreach (Assegnazione assegnazioneCheck in studente.InformazioniPagamento.Assegnazioni)
                {
                    if (dataIniziale == DateTime.MinValue)
                        dataIniziale = assegnazioneCheck.dataDecorrenza;

                    if (dataFinale < assegnazioneCheck.dataFineAssegnazione)
                        dataFinale = assegnazioneCheck.dataFineAssegnazione;
                }

                bool controlloApprofondito = false;

                // Check if the student has been "in" for more than 7 months
                bool isMoreThanSevenMonths = (dataFinale - dataIniziale).TotalDays > 7 * 30;

                // If the student has been "in" for less than or equal to 7 months, do further checks
                if (dataFinale < new DateTime(2024, 7, 15) && !isMoreThanSevenMonths)
                {
                    bool hasDomicilio = studente.InformazioniSede.DomicilioCheck;
                    bool isMoreThanHalfAbroad = studente.InformazioniPersonali.NumeroComponentiNucleoFamiliareEstero >= (studente.InformazioniPersonali.NumeroComponentiNucleoFamiliare / 2.0);

                    if (!hasDomicilio && !isMoreThanHalfAbroad)
                        controlloApprofondito = true;
                }

                foreach (Assegnazione assegnazione in studente.InformazioniPagamento.Assegnazioni)
                {
                    _ = returnDataTable.Rows.Add(
                        progressivo.ToString(),
                        assegnazione.idAssegnazione,
                        studente.InformazioniPersonali.Cognome,
                        studente.InformazioniPersonali.Nome,
                        studente.InformazioniPersonali.CodFiscale,
                        assegnazione.codPensionato,
                        assegnazione.dataDecorrenza.ToString("dd/MM/yyyy"),
                        assegnazione.dataFineAssegnazione.ToString("dd/MM/yyyy"),
                        dataIniziale.ToString("dd/MM/yyyy"),
                        dataFinale.ToString("dd/MM/yyyy"),
                        (assegnazione.dataFineAssegnazione - assegnazione.dataDecorrenza).Days.ToString(),
                        studente.InformazioniBeneficio.ImportoBeneficio.ToString("F2"),
                        studente.InformazioniPagamento.ImportoDaPagareLordo.ToString("F2"),
                        accontoPA.ToString("F2"),
                        saldoPA.ToString("F2"),
                        saldo.ToString("F2"),
                        assegnazione.statoCorrettezzaAssegnazione.ToString(),
                        controlloApprofondito ? "CONTROLLARE" : "OK"
                    );
                }

                progressivo++;
            }

            _ = returnDataTable.Rows.Add(" ");
            return returnDataTable;
        }

        private DataTable GenerareExcelDataTableConDetrazioni(List<StudentePagamenti> studentiDaGenerare, List<string> sediStudi, string impegno)
        {
            if (!studentiDaGenerare.Any())
                return new DataTable();

            DataTable studentsData = new();

            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
            using SqlCommand cmd = new(sqlTipoPagam, CONNECTION, sqlTransaction) { CommandTimeout = 9000000 };
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();

            string annoAccedemicoFileName = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));
            string impegnoNome = "impegno " + impegno;
            string titolo = pagamentoDescrizione + " " + annoAccedemicoFileName + " " + impegnoNome;

            _ = studentsData.Columns.Add("1");
            _ = studentsData.Columns.Add("2");
            _ = studentsData.Columns.Add("3");
            _ = studentsData.Columns.Add("4");
            _ = studentsData.Columns.Add("5");
            _ = studentsData.Columns.Add("6");
            _ = studentsData.Columns.Add("7");
            _ = studentsData.Columns.Add("8");

            _ = studentsData.Rows.Add(titolo);
            _ = studentsData.Rows.Add("ALLEGATO DETERMINA");

            var gruppoCassino = new List<string> { "02" };
            var gruppoViterbo = new List<string> { "05" };
            var gruppoRoma = sediStudi
                .Where(c => c != "02" && c != "05")
                .Distinct()
                .ToList();

            var codEnteGroups = new List<(string NomeGruppo, List<string> Codes)>
    {
        ("Cassino", gruppoCassino),
        ("Viterbo", gruppoViterbo),
        ("Roma",    gruppoRoma)
    };

            string nomePA = categoriaPagam == "PR" ? "ACCONTO PA" : "SALDO COSTO DEL SERVIZIO";

            foreach (var (NomeGruppo, Codes) in codEnteGroups)
            {
                var studentiGruppo = studentiDaGenerare
                    .Where(s => Codes.Contains(s.InformazioniIscrizione.CodEnte))
                    .ToList();

                if (studentiGruppo.Count == 0)
                    continue;

                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(NomeGruppo);
                _ = studentsData.Rows.Add("N.PROG.", "NUMERO DOMANDA", "CODICE FISCALE", "COGNOME", "NOME", "TOTALE LORDO", nomePA, "IMPORTO NETTO");

                int progressivo = 1;
                double totaleLordo = 0;
                double totalePA = 0;
                double totaleNetto = 0;

                foreach (StudentePagamenti s in studentiGruppo)
                {
                    double costoPA = categoriaPagam == "PR"
                        ? s.InformazioniPagamento.ImportoAccontoPA
                        : s.InformazioniPagamento.ImportoSaldoPA;

                    _ = studentsData.Rows.Add(
                        progressivo,
                        s.InformazioniPersonali.NumDomanda,
                        s.InformazioniPersonali.CodFiscale,
                        s.InformazioniPersonali.Cognome,
                        s.InformazioniPersonali.Nome,
                        s.InformazioniPagamento.ImportoDaPagareLordo.ToString().Replace(",", "."),
                        costoPA.ToString().Replace(",", "."),
                        s.InformazioniPagamento.ImportoDaPagare.ToString().Replace(",", ".")
                    );

                    totaleLordo += s.InformazioniPagamento.ImportoDaPagareLordo;
                    totalePA += costoPA;
                    totaleNetto += s.InformazioniPagamento.ImportoDaPagare;
                    progressivo++;
                }

                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ", " ", " ", " ", "TOTALE",
                    Math.Round(totaleLordo, 2).ToString().Replace(",", "."),
                    Math.Round(totalePA, 2).ToString().Replace(",", "."),
                    Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
                _ = studentsData.Rows.Add(" ");
                _ = studentsData.Rows.Add(" ");
            }

            return studentsData;
        }

        private DataTable GenerareExcelDataTableNoDetrazioni(List<StudentePagamenti> studentiDaGenerare, string impegno)
        {
            if (!studentiDaGenerare.Any())
                return new DataTable();

            DataTable studentsData = new();
            string sqlTipoPagam = $"SELECT Descrizione FROM Tipologie_pagam WHERE Cod_tipo_pagam = '{codTipoPagamento}'";
            SqlCommand cmd = new(sqlTipoPagam, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            string pagamentoDescrizione = (string)cmd.ExecuteScalar();

            string annoAccedemicoFileName = string.Concat(selectedAA.AsSpan(2, 2), selectedAA.AsSpan(6, 2));
            string impegnoNome = "impegno " + impegno;
            string titolo = pagamentoDescrizione + " " + annoAccedemicoFileName + " " + impegnoNome;

            _ = studentsData.Columns.Add("1");
            _ = studentsData.Columns.Add("2");
            _ = studentsData.Columns.Add("3");
            _ = studentsData.Columns.Add("4");
            _ = studentsData.Columns.Add("5");
            _ = studentsData.Columns.Add("6");
            _ = studentsData.Columns.Add("7");
            _ = studentsData.Columns.Add("8");

            _ = studentsData.Rows.Add(titolo);
            _ = studentsData.Rows.Add("ALLEGATO DETERMINA");
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add("N.PROG.", "NUMERO DOMANDA", "CODICE FISCALE", "COGNOME", "NOME", "TOTALE LORDO", "ACCONTO PA", "IMPORTO NETTO");

            int progressivo = 1;
            double totaleLordo = 0;
            double totaleAcconto = 0;
            double totaleNetto = 0;

            foreach (StudentePagamenti s in studentiDaGenerare)
            {
                double importoAcconto = 0;

                _ = studentsData.Rows.Add(
                    progressivo,
                    s.InformazioniPersonali.NumDomanda,
                    s.InformazioniPersonali.CodFiscale,
                    s.InformazioniPersonali.Cognome,
                    s.InformazioniPersonali.Nome,
                    s.InformazioniPagamento.ImportoDaPagareLordo.ToString().Replace(",", "."),
                    importoAcconto.ToString().Replace(",", "."),
                    s.InformazioniPagamento.ImportoDaPagare.ToString().Replace(",", ".")
                );
                totaleLordo += s.InformazioniPagamento.ImportoDaPagareLordo;
                totaleAcconto += importoAcconto;
                totaleNetto += s.InformazioniPagamento.ImportoDaPagare;
                progressivo++;
            }

            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ", " ", " ", " ", "TOTALE",
                Math.Round(totaleLordo, 2).ToString().Replace(",", "."),
                Math.Round(totaleAcconto, 2).ToString().Replace(",", "."),
                Math.Round(totaleNetto, 2).ToString().Replace(",", "."));
            _ = studentsData.Rows.Add(" ");
            _ = studentsData.Rows.Add(" ");
            return studentsData;
        }

        // =========================================================
        // NEW/UPDATED: logica coerente "posto alloggio" (assegnazioni + detrazione 01 per PR)
        // =========================================================
        private bool HasPAForStudent(StudentePagamenti s, string categoriaPagam)
        {
            if (s?.InformazioniPagamento == null) return false;

            bool hasAssegnazioni = (s.InformazioniPagamento.Assegnazioni?.Count ?? 0) > 0;

            bool hasDetrazione01 = (s.InformazioniPagamento.Detrazioni?.Any(d =>
                string.Equals(d?.codReversale, "01", StringComparison.OrdinalIgnoreCase)) ?? false);

            // Coerenza col flusso: detrazione 01 rilevante solo per PR
            if (!string.Equals(categoriaPagam, "PR", StringComparison.OrdinalIgnoreCase))
                hasDetrazione01 = false;

            return hasAssegnazioni || hasDetrazione01;
        }

        // NEW: "alloggio effettivo" = core + eccezione 311|PR (VincitorePA)
        private bool IsAlloggioEffective(StudentePagamenti s, string categoriaCU, string categoriaPagam)
        {
            bool core = HasPAForStudent(s, categoriaPagam);
            if (core) return true;

            return string.Equals(categoriaPagam, "PR", StringComparison.OrdinalIgnoreCase)
                && string.Equals(categoriaCU, "311", StringComparison.OrdinalIgnoreCase)
                && (s?.InformazioniBeneficio?.VincitorePA == true);
        }

        // NEW: mismatch CU/alloggio (solo 211/311)
        private bool IsCuAlloggioMismatch(StudentePagamenti s, string categoriaCU, string categoriaPagam, out string motivo)
        {
            motivo = "";

            if (s == null)
            {
                motivo = "Oggetto Studente nullo o non inizializzato.";
                return true;
            }

            bool hasAlloggio = IsAlloggioEffective(s, categoriaCU, categoriaPagam);

            if (string.Equals(categoriaCU, "211", StringComparison.OrdinalIgnoreCase))
            {
                if (hasAlloggio)
                {
                    motivo = "Difformità: CategoriaCU=211 (solo SENZA posto alloggio) ma lo studente risulta CON posto alloggio. Rimosso dal flusso.";
                    return true;
                }
            }

            if (string.Equals(categoriaCU, "311", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasAlloggio)
                {
                    motivo = "Difformità: CategoriaCU=311 (solo CON posto alloggio) ma lo studente risulta SENZA posto alloggio. Rimosso dal flusso.";
                    return true;
                }
            }

            return false;
        }

        private string BuildNonFlussoReason(StudentePagamenti s, string categoriaCU, string selectedRichiestoPA, string tipoStudente, string categoriaPagam)
        {
            if (s == null)
                return "Oggetto Studente nullo o non inizializzato.";

            if (s.InformazioniPersonali == null)
                return "Dati anagrafici mancanti.";

            if (s.InformazioniPagamento == null)
                return "Dati pagamento mancanti.";

            if (s.InformazioniIscrizione == null)
                return "Dati iscrizione mancanti.";

            // NEW: check difformità CU/alloggio (stessa logica usata per rimozione dal flusso)
            if ((categoriaCU == "211" || categoriaCU == "311") &&
                IsCuAlloggioMismatch(s, categoriaCU, categoriaPagam, out var mismatchMotivo))
            {
                return mismatchMotivo;
            }

            bool hasAlloggioEffective = IsAlloggioEffective(s, categoriaCU, categoriaPagam);
            bool hasIBAN = !string.IsNullOrWhiteSpace(s.InformazioniConto?.IBAN);
            bool hasImporto = s.InformazioniPagamento.ImportoDaPagare > 0;
            int annoCorso = s.InformazioniIscrizione.AnnoCorso;

            // TipoStudente meaning (come nel tuo commento originale)
            // 0 = solo matricole (AnnoCorso=1)
            // 1 = solo anni successivi (AnnoCorso>1)
            // 2 = entrambi
            if (tipoStudente == "0" && annoCorso != 1)
                return $"Studente di anno {annoCorso}, ma tipoStudente=0 (solo matricole).";
            if (tipoStudente == "1" && annoCorso == 1)
                return $"Studente matricola (AnnoCorso=1), ma tipoStudente=1 (solo anni successivi).";

            if (!hasIBAN)
                return "IBAN mancante o non valido: lo studente non può essere incluso nel flusso.";
            if (!hasImporto)
                return $"Importo da pagare pari a zero ({s.InformazioniPagamento.ImportoDaPagare}).";

            switch (categoriaCU)
            {
                case "111":
                    // 111: può essere con o senza alloggio
                    if (hasAlloggioEffective && !(selectedRichiestoPA == "2" || selectedRichiestoPA == "1"))
                        return $"Categoria 111 con alloggio, ma selectedRichiestoPA={selectedRichiestoPA} non consente flussi CON alloggio.";
                    if (!hasAlloggioEffective && !(selectedRichiestoPA == "2" || selectedRichiestoPA == "0" || selectedRichiestoPA == "3"))
                        return $"Categoria 111 senza alloggio, ma selectedRichiestoPA={selectedRichiestoPA} non consente flussi SENZA alloggio.";
                    return "Categoria 111: regole generali rispettate, ma non scritto (esclusione tecnica o errore successivo).";

                case "211":
                    // 211: solo SENZA alloggio (le difformità sono già intercettate sopra)
                    if (!(selectedRichiestoPA == "2" || selectedRichiestoPA == "0" || selectedRichiestoPA == "3"))
                        return $"Categoria 211 (senza alloggio) ma selectedRichiestoPA={selectedRichiestoPA} non consente flussi SENZA alloggio.";
                    return "Categoria 211 corretta ma non emesso (filtro, esclusione tecnica o errore).";

                case "311":
                    // 311: solo CON alloggio (le difformità sono già intercettate sopra)
                    if (!(selectedRichiestoPA == "2" || selectedRichiestoPA == "1"))
                        return $"Categoria 311 (con alloggio) ma selectedRichiestoPA={selectedRichiestoPA} non consente flussi CON alloggio.";
                    return "Categoria 311 corretta ma non emesso (filtro, esclusione tecnica o errore).";

                default:
                    return $"CategoriaCU {categoriaCU} non riconosciuta o non gestita dai flussi.";
            }
        }

        private DataTable BuildFullAuditDataTable(IEnumerable<StudentePagamenti> students, string impegno)
        {
            var dt = new DataTable();

            dt.Columns.Add("Impegno");
            dt.Columns.Add("CategoriaPagam");
            dt.Columns.Add("CategoriaCU");
            dt.Columns.Add("TipoStudente");
            dt.Columns.Add("SelectedRichiestoPA");

            dt.Columns.Add("NumDomanda");
            dt.Columns.Add("CodFiscale");
            dt.Columns.Add("Cognome");
            dt.Columns.Add("Nome");
            dt.Columns.Add("Sesso");
            dt.Columns.Add("DataNascita");
            dt.Columns.Add("Disabile");
            dt.Columns.Add("CodCittadinanza");
            dt.Columns.Add("Telefono");
            dt.Columns.Add("Email");
            dt.Columns.Add("Rifugiato");

            dt.Columns.Add("AnnoCorso");
            dt.Columns.Add("TipoCorso");
            dt.Columns.Add("CodCorsoLaurea");
            dt.Columns.Add("CodSedeStudi");
            dt.Columns.Add("CodFacolta");
            dt.Columns.Add("CodEnte");
            dt.Columns.Add("ComuneSedeStudi");

            dt.Columns.Add("IBAN");
            dt.Columns.Add("Swift");
            dt.Columns.Add("BonificoEstero");

            dt.Columns.Add("ContrattoValido");
            dt.Columns.Add("ProrogaValido");
            dt.Columns.Add("ContrattoEnte");
            dt.Columns.Add("DomicilioDefinito");
            dt.Columns.Add("DomicilioCheck");
            dt.Columns.Add("StatusSede");
            dt.Columns.Add("ForzaturaStatusSede");
            dt.Columns.Add("PrevScadenza");
            dt.Columns.Add("GiorniDallaScad");
            dt.Columns.Add("CodBlocchi");

            dt.Columns.Add("Res_Indirizzo");
            dt.Columns.Add("Res_CAP");
            dt.Columns.Add("Res_CodComune");
            dt.Columns.Add("Res_Provincia");
            dt.Columns.Add("Res_NomeComune");

            dt.Columns.Add("Dom_Possiede");
            dt.Columns.Add("Dom_CodComune");
            dt.Columns.Add("Dom_TitoloOneroso");
            dt.Columns.Add("Dom_ContrOneroso");
            dt.Columns.Add("Dom_ContrLocazione");
            dt.Columns.Add("Dom_ContrEnte");
            dt.Columns.Add("Dom_ConoscenzaDatiContratto");
            dt.Columns.Add("Dom_DataReg");
            dt.Columns.Add("Dom_DataDecorrenza");
            dt.Columns.Add("Dom_DataScadenza");
            dt.Columns.Add("Dom_SerieLocazione");
            dt.Columns.Add("Dom_DurataMesi");
            dt.Columns.Add("Dom_Prorogato");
            dt.Columns.Add("Dom_DurataMesiProroga");
            dt.Columns.Add("Dom_SerieProroga");
            dt.Columns.Add("Dom_TipologiaIstituto");
            dt.Columns.Add("Dom_DenomIstituto");
            dt.Columns.Add("Dom_ImportoMensileIstituto");

            dt.Columns.Add("NumeroImpegno");
            dt.Columns.Add("ImportoLordo");
            dt.Columns.Add("ImportoNetto");
            dt.Columns.Add("ImportoAccontoPA");
            dt.Columns.Add("ImportoSaldoPA");
            dt.Columns.Add("MandatoProvvisorio");
            dt.Columns.Add("PagatoPendolare");
            dt.Columns.Add("HasPA");

            dt.Columns.Add("EsitoPA");
            dt.Columns.Add("EraVincitorePA");
            dt.Columns.Add("VincitorePA");
            dt.Columns.Add("RichiestaPA");
            dt.Columns.Add("RinunciaPA");
            dt.Columns.Add("SuperamentoEsami");
            dt.Columns.Add("SuperamentoEsamiTassaRegionale");
            dt.Columns.Add("ImportoBeneficio");

            dt.Columns.Add("NumAssegnazioni");
            dt.Columns.Add("NumDetrazioni");
            dt.Columns.Add("NumReversali");
            dt.Columns.Add("NumPagamentiEffettuati");

            dt.Columns.Add("Assegnazioni_Sintesi");
            dt.Columns.Add("Detrazioni_Sintesi");
            dt.Columns.Add("Reversali_Sintesi");
            dt.Columns.Add("Pagamenti_Sintesi");

            foreach (var s in students)
            {
                var p = s.InformazioniPersonali;
                var i = s.InformazioniIscrizione;
                var c = s.InformazioniConto;
                var se = s.InformazioniSede;
                var pay = s.InformazioniPagamento;
                var ben = s.InformazioniBeneficio;

                string resIndirizzo = se?.Residenza?.indirizzo ?? "";
                string resCAP = se?.Residenza?.CAP ?? "";
                string resCodComune = se?.Residenza?.codComune ?? "";
                string resProvincia = se?.Residenza?.provincia ?? "";
                string resNomeComune = se?.Residenza?.nomeComune ?? "";

                var dom = se?.Domicilio;
                string domTipologia = dom?.tipologiaEnteIstituto.ToString() ?? "";

                var assegnazioni = pay?.Assegnazioni ?? new List<Assegnazione>();
                var detrazioni = pay?.Detrazioni ?? new List<Detrazione>();
                var reversali = pay?.Reversali ?? new List<Reversale>();
                var pagEff = pay?.PagamentiEffettuati ?? new List<Pagamento>();

                string assSint = (assegnazioni.Count == 0)
                    ? ""
                    : string.Join(" | ", assegnazioni.Select(a =>
                        $"{a.idAssegnazione}:{a.codPensionato} {a.dataDecorrenza:yyyy-MM-dd}→{a.dataFineAssegnazione:yyyy-MM-dd} (€/m {a.costoMensile})"));

                string detSint = (detrazioni.Count == 0)
                    ? ""
                    : string.Join(" | ", detrazioni.Select(d => $"{d.codReversale}:{d.importo} ({d.causale})"));

                string revSint = (reversali.Count == 0) ? "" : $"Tot={reversali.Count}";
                string paysSint = (pagEff.Count == 0) ? "" : $"Tot={pagEff.Count}";

                bool hasPA = HasPAForStudent(s, categoriaPagam);

                dt.Rows.Add(
                    impegno,
                    categoriaPagam,
                    pay?.CategoriaCU ?? "",
                    tipoStudente,
                    selectedRichiestoPA,

                    p?.NumDomanda ?? "",
                    p?.CodFiscale ?? "",
                    p?.Cognome ?? "",
                    p?.Nome ?? "",
                    p?.Sesso ?? "",
                    p?.DataNascita ?? "",
                    p?.Disabile == true ? "1" : "0",
                    p?.CodCittadinanza ?? "",
                    p?.Telefono.ToString(),
                    p?.IndirizzoEmail ?? "",
                    p?.Rifugiato == true ? "1" : "0",

                    i?.AnnoCorso.ToString(),
                    i?.TipoCorso.ToString(),
                    i?.CodCorsoLaurea ?? "",
                    i?.CodSedeStudi ?? "",
                    i?.CodFacolta ?? "",
                    i?.CodEnte ?? "",
                    i?.ComuneSedeStudi ?? "",

                    c?.IBAN ?? "",
                    c?.Swift ?? "",
                    c?.BonificoEstero == true ? "1" : "0",

                    se?.ContrattoValido == true ? "1" : "0",
                    se?.ProrogaValido == true ? "1" : "0",
                    se?.ContrattoEnte == true ? "1" : "0",
                    se?.DomicilioDefinito == true ? "1" : "0",
                    se?.DomicilioCheck == true ? "1" : "0",
                    se?.StatusSede ?? "",
                    se?.ForzaturaStatusSede ?? "",
                    se?.PrevScadenza?.ToString("yyyy-MM-dd") ?? "",
                    se?.GiorniDallaScad.ToString(),
                    se?.CodBlocchi ?? "",

                    resIndirizzo,
                    resCAP,
                    resCodComune,
                    resProvincia,
                    resNomeComune,

                    dom?.possiedeDomicilio == true ? "1" : "0",
                    dom?.codComuneDomicilio ?? "",
                    dom?.titoloOneroso == true ? "1" : "0",
                    dom?.contrOneroso == true ? "1" : "0",
                    dom?.contrLocazione == true ? "1" : "0",
                    dom?.contrEnte == true ? "1" : "0",
                    dom?.conoscenzaDatiContratto == true ? "1" : "0",
                    dom?.dataRegistrazioneLocazione == default ? "" : dom.dataRegistrazioneLocazione.ToString("yyyy-MM-dd"),
                    dom?.dataDecorrenzaLocazione == default ? "" : dom.dataDecorrenzaLocazione.ToString("yyyy-MM-dd"),
                    dom?.dataScadenzaLocazione == default ? "" : dom.dataScadenzaLocazione.ToString("yyyy-MM-dd"),
                    dom?.codiceSerieLocazione ?? "",
                    dom?.durataMesiLocazione.ToString(),
                    dom?.prorogatoLocazione == true ? "1" : "0",
                    dom?.durataMesiProrogaLocazione.ToString(),
                    dom?.codiceSerieProrogaLocazione ?? "",
                    domTipologia,
                    dom?.denominazioneIstituto ?? "",
                    dom?.importoMensileRataIstituto.ToString(),

                    pay?.NumeroImpegno ?? "",
                    pay?.ImportoDaPagareLordo.ToString(),
                    pay?.ImportoDaPagare.ToString(),
                    pay?.ImportoAccontoPA.ToString(),
                    pay?.ImportoSaldoPA.ToString(),
                    pay?.MandatoProvvisorio ?? "",
                    pay?.PagatoPendolare == true ? "1" : "0",
                    hasPA ? "1" : "0",

                    ben?.EsitoPA.ToString(),
                    ben?.EraVincitorePA == true ? "1" : "0",
                    ben?.VincitorePA == true ? "1" : "0",
                    ben?.RichiestaPA == true ? "1" : "0",
                    ben?.RinunciaPA == true ? "1" : "0",
                    ben?.SuperamentoEsami == true ? "1" : "0",
                    ben?.SuperamentoEsamiTassaRegionale == true ? "1" : "0",
                    ben?.ImportoBeneficio.ToString(),

                    assegnazioni.Count.ToString(),
                    detrazioni.Count.ToString(),
                    reversali.Count.ToString(),
                    pagEff.Count.ToString(),

                    assSint,
                    detSint,
                    revSint,
                    paysSint
                );
            }

            return dt;
        }

    }
}
