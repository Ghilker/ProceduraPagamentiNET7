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
        public override void RunProcedure(ArgsPagamenti args)
        {
            Logger.LogDebug(null, "Inizio dell'esecuzione di RunProcedure");
            try
            {
                if (CONNECTION == null)
                {
                    Logger.LogError(null, "CONNESSIONE ASSENTE O NULLA");
                    return;
                }

                if (_masterForm == null)
                {
                    Logger.LogError(null, "MASTER FORM NULLO!!!");
                    return;
                }

                InitializeProcedure(args);
                if (exitProcedureEarly)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo InitializeProcedure");
                    return;
                }

                var processorChooser = new ProcessorChooser();
                selectedAcademicProcessor = processorChooser.GetProcessor(selectedAA);
                if (selectedAcademicProcessor == null)
                {
                    Logger.LogDebug(null, "Processor non può essere nullo in questo punto!");
                    return;
                }

                if (cicloTuttiIPagamenti)
                {
                    // lettura configurazioni una volta sola
                    var configurazioni = LoadAllPaymentConfigs(CONNECTION, null);
                    if (configurazioni.Count == 0)
                    {
                        Logger.LogInfo(null, "Nessuna configurazione di pagamento trovata in Tipologie_pagam.");
                        return;
                    }

                    foreach (var cfg in configurazioni)
                    {
                        if (exitProcedureEarly)
                            break;

                        // reset stato per il nuovo ciclo
                        ResetStatoPerCiclo();
                        InitializeProcedure(args);

                        sqlTransaction = CONNECTION.BeginTransaction();
                        if (CONNECTION == null || sqlTransaction == null)
                        {
                            Logger.LogDebug(null, "Connessione o transazione null in ciclo automatico");
                            _masterForm.inProcedure = false;
                            sqlTransaction?.Rollback();
                            return;
                        }

                        try
                        {
                            // TRUNCATE tabella CFEstrazione all’inizio del ciclo
                            ResetCFEstrazione();

                            // dati specifici del pagamento
                            tipoBeneficio = cfg.CodBeneficio;
                            codTipoPagamento = cfg.CodTipoPagam; // es. BSP0, BSP1, BST0...
                            selectedTipoPagamento = codTipoPagamento.Length >= 4
                                ? codTipoPagamento.Substring(2, 2)
                                : string.Empty;
                            categoriaPagam = cfg.CategoriaPagam;
                            isTR = codTipoPagamento.StartsWith("BST", StringComparison.OrdinalIgnoreCase);
                            string codBeneficioCalc = isTR ? "TR" : tipoBeneficio;

                            if(codTipoPagamento == "BST0")
                            {
                                string test = "";
                            }
                            // massivo su tutti: tipo studente / ente / impegno / PA
                            tipoStudente = "2";    // tutti gli studenti
                            selectedCodEnte = "00";   // tutti gli enti
                            selectedImpegno = "0000"; // tutti gli impegni
                            selectedRichiestoPA = "2";    // tutti (richiedenti e non)

                            // nome tabella di appoggio per questo pagamento
                            dbTableName = "TEST_SA";//BuildAutomaticTableName(cfg);

                            // carica tutti gli impegni compatibili
                            impegniList.Clear();
                            using (SqlCommand readData = new SqlCommand(
                                       "SELECT num_impegno FROM impegni WHERE Cod_beneficio = @ben AND categoria_pagamento = @cat",
                                       CONNECTION,
                                       sqlTransaction))
                            {
                                readData.Parameters.AddWithValue("@ben", codBeneficioCalc);
                                readData.Parameters.AddWithValue("@cat", categoriaPagam);
                                using SqlDataReader reader = readData.ExecuteReader();
                                while (reader.Read())
                                {
                                    impegniList.Add(Utilities.SafeGetString(reader, "num_impegno"));
                                }
                            }

                            Logger.LogInfo(
                                null,
                                $"[AUTO] AA {selectedAA} - Beneficio {tipoBeneficio} - Tipo {codTipoPagamento} - Cat {categoriaPagam} - {cfg.DescrPagamento}");

                            CheckAndCreateDatabaseTable();
                            if (exitProcedureEarly)
                            {
                                Logger.LogDebug(null, "Uscita anticipata dopo CheckAndCreateDatabaseTable (auto)");
                                sqlTransaction.Rollback();
                                break;
                            }

                            HandleFiltroManuale();
                            if (exitProcedureEarly)
                            {
                                Logger.LogDebug(null, "Uscita anticipata dopo HandleFiltroManuale (auto)");
                                sqlTransaction.Rollback();
                                break;
                            }

                            ClearMovimentiIfNeeded();
                            if (exitProcedureEarly)
                            {
                                Logger.LogDebug(null, "Uscita anticipata dopo ClearMovimentiIfNeeded (auto)");
                                sqlTransaction.Rollback();
                                break;
                            }

                            int studentiPrima = studentiProcessatiAmount;

                            GenerateStudentListToPay();
                            ProcessStudentList();

                            int studentiDopo = studentiProcessatiAmount;
                            int elaborati = studentiDopo - studentiPrima;

                            RegisterPaymentCount(codTipoPagamento, categoriaPagam, elaborati, cfg.DescrPagamento);

                            // commit dopo OGNI ciclo pagamento
                            sqlTransaction.Commit();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(null,
                                $"Errore nel ciclo automatico per {cfg.CodTipoPagam}/{cfg.CategoriaPagam}: {ex.Message}");
                            sqlTransaction?.Rollback();
                            throw;
                        }
                    }

                    // riepilogo finale solo informativo (i commit sono già stati fatti per ogni ciclo)
                    // riepilogo finale solo informativo (i commit sono già stati fatti per ogni ciclo)
                    _masterForm.Invoke((MethodInvoker)delegate
                    {
                        Logger.LogInfo(
                            100,
                            $"Numero studenti lavorati (auto): {studentiProcessatiAmount}",
                            System.Drawing.Color.DarkGreen);

                        foreach (var kvImp in impegnoAmount)
                        {
                            string imp = kvImp.Key;
                            int totImp = 0;

                            foreach (var kvCat in kvImp.Value)
                                foreach (var kvSS in kvCat.Value)
                                    foreach (var kvDet in kvSS.Value)
                                        totImp += kvDet.Value;

                            Logger.LogInfo(
                                null,
                                $" - di cui {totImp} con impegno n°{imp}",
                                System.Drawing.ColorTranslator.FromHtml("#A4449A"));
                        }

                        // dettaglio per tipo pagamento
                        foreach (var stats in _conteggiPerPagamento
                                     .Values
                                     .OrderBy(s => s.CodTipoPagam)
                                     .ThenBy(s => s.CategoriaPagam))
                        {
                            Logger.LogInfo(
                                null,
                                $"Pagamento {stats.CodTipoPagam} (cat {stats.CategoriaPagam}) - {stats.DescrPagamento}: {stats.Studenti} studenti",
                                System.Drawing.Color.DarkSlateGray);
                        }

                        Logger.LogInfo(
                            null,
                            $"Totale pagamenti (auto): {Math.Round(importoTotale, 2)} €");

                        // file di log finale
                        WriteFinalCountsLog(isAutomatic: true);

                        MessageBox.Show(
                            _masterForm,
                            "Procedura automatica completata.\nVerifica i log per il dettaglio.",
                            "Completato",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    });


                    return;
                }

                else
                {
                    sqlTransaction = CONNECTION.BeginTransaction();
                    if (CONNECTION == null || sqlTransaction == null)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure: connessione o transazione null");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }
                    // INTERATTIVO: un tipo di pagamento alla volta
                    HandleTipoPagamentoDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleTipoPagamentoDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    HandlePagamentoSettingsDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandlePagamentoSettingsDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    HandleTableNameSelectionDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleTableNameSelectionDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    HandleRiepilogoPagamentiDialog();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleRiepilogoPagamentiDialog");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    CheckAndCreateDatabaseTable();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo CheckAndCreateDatabaseTable");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    HandleFiltroManuale();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo HandleFiltroManuale");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    ClearMovimentiIfNeeded();
                    if (exitProcedureEarly)
                    {
                        Logger.LogDebug(null, "Uscita anticipata da RunProcedure dopo ClearMovimentiIfNeeded");
                        _masterForm.inProcedure = false;
                        sqlTransaction?.Rollback();
                        return;
                    }

                    GenerateStudentListToPay();
                    ProcessStudentList();
                }

                _masterForm.Invoke((MethodInvoker)delegate
                {
                    DialogResult result = MessageBox.Show(
                        _masterForm,
                        "Completare procedura",
                        "Attenzione",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.OK)
                    {
                        Logger.LogInfo(
                            100,
                            $"Numero studenti lavorati: {studentiProcessatiAmount}",
                            System.Drawing.Color.DarkGreen);

                        foreach (var kvImp in impegnoAmount)
                        {
                            string imp = kvImp.Key;
                            int totImp = 0;

                            foreach (var kvCat in kvImp.Value)
                                foreach (var kvSS in kvCat.Value)
                                    foreach (var kvDet in kvSS.Value)
                                        totImp += kvDet.Value;

                            Logger.LogInfo(
                                null,
                                $" - di cui {totImp} con impegno n°{imp}",
                                System.Drawing.ColorTranslator.FromHtml("#A4449A"));

                            foreach (var kvCat in kvImp.Value)
                            {
                                string cat = kvCat.Key;
                                int totCat = kvCat.Value.Values.Sum(d => d.Values.Sum());

                                Logger.LogInfo(
                                    null,
                                    $" - - {totCat} con categoria n°{cat}",
                                    System.Drawing.Color.DeepPink);

                                foreach (var kvSS in kvCat.Value)
                                {
                                    string ssLabel = kvSS.Key; // "ConSS" / "SenzaSS"
                                    int totSS = kvSS.Value.Values.Sum();

                                    Logger.LogInfo(
                                        null,
                                        $" - - - {ssLabel}: {totSS}",
                                        System.Drawing.Color.DarkBlue);

                                    foreach (var kvDet in kvSS.Value)
                                    {
                                        string detLabel = kvDet.Key; // "ConDetrazione" / "SenzaDetrazione"
                                        Logger.LogInfo(
                                            null,
                                            $" - - - - {detLabel}: {kvDet.Value}",
                                            System.Drawing.Color.DarkSlateBlue);
                                    }
                                }
                            }
                        }

                        // conteggio per il pagamento corrente
                        RegisterPaymentCount(codTipoPagamento, categoriaPagam, studentiProcessatiAmount);

                        // dettaglio per tipo pagamento
                        foreach (var stats in _conteggiPerPagamento
                                     .Values
                                     .OrderBy(s => s.CodTipoPagam)
                                     .ThenBy(s => s.CategoriaPagam))
                        {
                            Logger.LogInfo(
                                null,
                                $"Pagamento {stats.CodTipoPagam} (cat {stats.CategoriaPagam}) - {stats.DescrPagamento}: {stats.Studenti} studenti",
                                System.Drawing.Color.DarkSlateGray);
                        }

                        Logger.LogInfo(
                            null,
                            $"Totale pagamenti: {Math.Round(importoTotale, 2)} €");

                        // file di log finale
                        WriteFinalCountsLog(isAutomatic: false);

                        sqlTransaction?.Commit();
                    }
                    else
                    {
                        Logger.LogInfo(null, "Procedura chiusa dall'utente");
                        sqlTransaction?.Rollback();
                    }
                });

            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Errore: {ex.Message}");
                sqlTransaction?.Rollback();
                throw;
            }
            finally
            {
                Logger.LogDebug(100, "Fine dell'esecuzione di RunProcedure");
                FinalizeProcedure();
            }
        }
        private void ResetStatoPerCiclo()
        {
            // Collezioni per-studente
            studentiDaPagare.Clear();
            studentiConErroriPA.Clear();
            impegniList.Clear();

            // Filtri e where dinamico
            dictQueryWhere.Clear();
            stringQueryWhere = string.Empty;
            usingStringWhere = false;

            // Variabili di configurazione pagamento
            selectedCodEnte = string.Empty;
            selectedDataRiferimento = string.Empty;
            selectedNumeroMandato = string.Empty;
            selectedVecchioMandato = string.Empty;
            selectedTipoProcedura = string.Empty;
            selectedTipoPagamento = string.Empty;
            selectedRichiestoPA = string.Empty;
            dbTableName = string.Empty;
            dbTableExists = false;

            tipoStudente = string.Empty;
            tipoBeneficio = string.Empty;
            codTipoPagamento = string.Empty;
            selectedImpegno = string.Empty;
            categoriaPagam = string.Empty;

            isIntegrazione = false;
            isRiemissione = false;
            isTR = false;
            insertInDatabase = false;

            // Forzatura studente
            studenteForzato = false;
            studenteForzatoCF = string.Empty;

            // Stato procedura per ciclo
            exitProcedureEarly = false;

            // La transazione viene gestita dal chiamante per ogni ciclo
            sqlTransaction = null;
        }

        private void ResetCFEstrazione()
        {
            if (CONNECTION == null)
                return;

            const string sql = @"
IF OBJECT_ID('CFEstrazione','U') IS NOT NULL
    TRUNCATE TABLE CFEstrazione;";

            using SqlCommand cmd = new(sql, CONNECTION, sqlTransaction);
            cmd.CommandTimeout = 900000;
            cmd.ExecuteNonQuery();
        }

        private void RegisterPaymentCount(string codTipoPagam, string categoria, int studentiCount, string? descrPagamento = null)
        {
            if (studentiCount <= 0)
                return;

            if (string.IsNullOrWhiteSpace(codTipoPagam))
                return;

            string key = $"{codTipoPagam}_{categoria ?? string.Empty}";

            if (!_conteggiPerPagamento.TryGetValue(key, out var stats))
            {
                string descr = descrPagamento ?? string.Empty;

                if (string.IsNullOrWhiteSpace(descr))
                {
                    try
                    {
                        if (CONNECTION != null)
                        {
                            const string sql = @"SELECT Descrizione 
                                         FROM Tipologie_pagam 
                                         WHERE Cod_tipo_pagam = @cod";

                            using SqlCommand cmd = new(sql, CONNECTION, sqlTransaction)
                            {
                                CommandTimeout = 900000
                            };
                            cmd.Parameters.AddWithValue("@cod", codTipoPagam);
                            object? res = cmd.ExecuteScalar();
                            descr = Convert.ToString(res) ?? string.Empty;
                        }
                    }
                    catch
                    {
                        // in caso di errore, uso il codice stesso
                    }
                }

                if (string.IsNullOrWhiteSpace(descr))
                    descr = codTipoPagam;

                stats = new PaymentCount
                {
                    CodTipoPagam = codTipoPagam,
                    CategoriaPagam = categoria ?? string.Empty,
                    DescrPagamento = descr,
                    Studenti = 0
                };

                _conteggiPerPagamento[key] = stats;
            }

            stats.Studenti += studentiCount;
        }

        private void WriteFinalCountsLog(bool isAutomatic)
        {
            try
            {
                if (_conteggiPerPagamento.Count == 0)
                {
                    Logger.LogInfo(null, "Nessun dato di conteggio per tipo pagamento da scrivere nel log finale.");
                    return;
                }

                string aaSanitized = (selectedAA ?? string.Empty).Replace("/", string.Empty);
                string modeTag = isAutomatic ? "AUTO" : "SINGOLO";

                string fileName = $"LOG_CONTEGGI_PAGAMENTI_{aaSanitized}_{modeTag}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

                string logFolder = Utilities.EnsureDirectory(
                    System.IO.Path.Combine(selectedSaveFolder, "LOG_PAGAMENTI"));

                string fullPath = System.IO.Path.Combine(logFolder, fileName);

                var sb = new StringBuilder();
                sb.AppendLine($"Anno accademico: {selectedAA}");
                sb.AppendLine($"Data/ora generazione: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                sb.AppendLine($"Modalità: {(isAutomatic ? "Elaborazione automatica (tutti i tipi di pagamento)" : "Elaborazione singolo pagamento")}");
                sb.AppendLine();
                sb.AppendLine("CodTipo;Categoria;Descrizione;Studenti");

                foreach (var stats in _conteggiPerPagamento
                             .Values
                             .OrderBy(v => v.CodTipoPagam)
                             .ThenBy(v => v.CategoriaPagam))
                {
                    sb.AppendLine($"{stats.CodTipoPagam};{stats.CategoriaPagam};{stats.DescrPagamento};{stats.Studenti}");
                }

                sb.AppendLine();
                sb.AppendLine($"Totale studenti processati (tutti i pagamenti): {studentiProcessatiAmount}");

                System.IO.File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

                Logger.LogInfo(null, $"File di log conteggi studenti creato: {fullPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError(null, $"Errore nella scrittura del log conteggi studenti: {ex.Message}");
            }
        }

        private List<PagamentoConfig> LoadAllPaymentConfigs(SqlConnection conn, SqlTransaction tx)
        {
            var result = new List<PagamentoConfig>();

            const string sql = @"
        SELECT
            b.Cod_beneficio,
            b.Descrizione                     AS DescrizioneBeneficio,
            p.cod_tipo_pagam,
            p.categoria_pagamento,
            p.descr_interno_tipo_pagamento,
            p.descr_interno_cat_pagamento,
            p.descrizione as descr_interno_pagamento
        FROM Tipologie_pagam p
        INNER JOIN Tipologie_benefici b
            ON b.Cod_beneficio = LEFT(p.cod_tipo_pagam, 2)
        WHERE LEN(p.cod_tipo_pagam) = 4
        AND p.visibile = 1 and b.cod_beneficio = 'bs' and p.cod_tipo_pagam in (
'BSP1',
'BSP2',
'BSI1',
'BSI2',
'BSS0',
'BSS1',
'BSS2',
'BSI9',
'BSIA',
'BSIB',
'BST0',
'BST1',
'BST2')
";

            using (var cmd = new SqlCommand(sql, conn, tx))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (Utilities.SafeGetString(reader, "categoria_pagamento") == "00")
                        continue;

                    result.Add(new PagamentoConfig
                    {
                        CodBeneficio = Utilities.SafeGetString(reader, "Cod_beneficio").Substring(0, 2),
                        DescrBeneficio = Utilities.SafeGetString(reader, "DescrizioneBeneficio"),
                        CodTipoPagam = Utilities.SafeGetString(reader, "cod_tipo_pagam"),
                        CategoriaPagam = Utilities.SafeGetString(reader, "categoria_pagamento"),
                        DescrTipo = Utilities.SafeGetString(reader, "descr_interno_tipo_pagamento"),
                        DescrCategoria = Utilities.SafeGetString(reader, "descr_interno_cat_pagamento"),
                        DescrPagamento = Utilities.SafeGetString(reader, "descr_interno_pagamento")
                    });
                }
            }

            return result;
        }

    }
}
