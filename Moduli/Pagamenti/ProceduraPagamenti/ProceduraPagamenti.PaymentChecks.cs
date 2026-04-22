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
        private void ProcessStudentList()
        {
            if (studentiDaPagare.Count == 0)
            {
                return;
            }
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");
            List<string> codFiscali = studentiDaPagare.Keys.ToList();

            Logger.LogDebug(null, "Creazione tabella CF");
            string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16) COLLATE Latin1_General_CI_AS);";
            using (SqlCommand createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                createCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Inserimento in tabella CF dei codici fiscali");
            Logger.LogInfo(30, "Lavorazione studenti - creazione tabella codici fiscali");

            // Create a DataTable to hold the fiscal codes
            using (DataTable cfTable = new DataTable())
            {
                cfTable.Columns.Add("Cod_fiscale", typeof(string));

                foreach (var cf in codFiscali)
                {
                    if (cf == debugStudente)
                    {
                        string test = ""; // Just to set a breakpoint, presumably
                    }
                    cfTable.Rows.Add(cf);
                }

                // Use SqlBulkCopy to efficiently insert the data into the temporary table
                using SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction);
                bulkCopy.DestinationTableName = "#CFEstrazione";
                bulkCopy.WriteToServer(cfTable);
            }

            Logger.LogDebug(null, "Creazione index della tabella CF");
            string indexingCFTable = "CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale)";
            using (SqlCommand indexingCFTableCmd = new SqlCommand(indexingCFTable, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                indexingCFTableCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Aggiornamento statistiche della tabella CF");
            string updateStatistics = "UPDATE STATISTICS #CFEstrazione";
            using (SqlCommand updateStatisticsCmd = new SqlCommand(updateStatistics, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            })
            {
                updateStatisticsCmd.ExecuteNonQuery();
            }
            #endregion

            ControlloPagamenti();
            Logger.LogDebug(30, $"Numero studenti prima della pulizia = {studentiDaPagare.Count}");
            if (studentiDaPagare.Count == 0)
            {
                string dropCFTable = "DROP TABLE #CFEstrazione;";
                SqlCommand drop = new(dropCFTable, CONNECTION, sqlTransaction);
                _ = drop.ExecuteNonQuery();
                return;
            }
            CheckLiquefazione();
            Logger.LogDebug(30, $"Numero studenti dopo la pulizia = {studentiDaPagare.Count}");


            if (studentiDaPagare.Count > 0)
            {
                PopulateStudentsInformations();

                List<StudentePagamenti> studenti = studentiDaPagare.Values.ToList();
                bool continueProcessing = true;

                //if (!massivoDefault)
                //{

                //    _ = _masterForm?.Invoke((MethodInvoker)delegate
                //    {
                //        if (MessageBox.Show(_masterForm, "Do you want to see an overview of the current student list?", "Overview", MessageBoxButtons.YesNo) == DialogResult.Yes)
                //        {
                //            continueProcessing = ShowStudentOverview(studenti);
                //        }

                //        bool ShowStudentOverview(List<StudentePagamenti> studenti)
                //        {
                //            bool result = false;
                //            _ = _masterForm.Invoke((MethodInvoker)delegate
                //            {
                //                using var overviewForm = new StudentOverview(studenti, ref studentiDaPagare, _masterForm);
                //                if (overviewForm.ShowDialog() == DialogResult.OK)
                //                {
                //                    result = true;
                //                }
                //            });
                //            return result;
                //        }
                //    });

                //}

                if (!continueProcessing)
                {
                    string dropTempTable1 = "DROP TABLE #CFEstrazione;";
                    SqlCommand dropCmd1 = new(dropTempTable1, CONNECTION, sqlTransaction);
                    _ = dropCmd1.ExecuteNonQuery();
                    return;
                }

                bool generateFiles = true;
                insertInDatabase = false;
                _ = _masterForm?.Invoke((MethodInvoker)delegate
                {
                    if (MessageBox.Show(_masterForm, "Generate students files?", "Overview", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    {
                        generateFiles = true;
                        if (MessageBox.Show(_masterForm, "Insert into DB?", "Overview", MessageBoxButtons.YesNo) == DialogResult.Yes)
                        {
                            insertInDatabase = true;
                        }
                    }
                });

                List<StudentePagamenti> studentiAfter = studentiDaPagare.Values.ToList();
                if (studentiDaPagare.Count > 0 && generateFiles)
                {
                    GenerateOutputFiles();
                }
            }

            string dropTempTable = "DROP TABLE #CFEstrazione;";
            SqlCommand dropCmd = new(dropTempTable, CONNECTION, sqlTransaction);
            _ = dropCmd.ExecuteNonQuery();
        }
        private void ControlloPagamenti()
        {
            if (!studenteForzato)
            {
                ControlloInMovimentazioni();
            }

            FilterPagamenti();

            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR && !studenteForzato && categoriaPagam != "PR")
            {
                ControlloProvvedimenti();
            }

            void ControlloInMovimentazioni()
            {
                Logger.LogDebug(null, "Inizio del filtraggio dei pagamenti degli studenti");

                // Costruisco l'elenco dei codici mandato da considerare:
                // - sempre il pagamento corrente (codTipoPagamento)
                // - se è una rata "P" (es. BSP0/1/2), aggiungo anche la rata successiva "S" (BSS0/1/2)
                List<string> mandatiDaControllare = new();
                if (!string.IsNullOrWhiteSpace(codTipoPagamento))
                    mandatiDaControllare.Add(codTipoPagamento);

                if (!string.IsNullOrWhiteSpace(codTipoPagamento) &&
                    codTipoPagamento.Length == 4 &&
                    codTipoPagamento[2] == 'P') // es. BSP0, BSP1, BSP2
                {
                    string successivo = $"{codTipoPagamento[0]}{codTipoPagamento[1]}S{codTipoPagamento[3]}"; // BSS0/1/2
                    mandatiDaControllare.Add(successivo);
                }

                if (mandatiDaControllare.Count == 0)
                {
                    Logger.LogInfo(null, "Nessun codice mandato da controllare in MOVIMENTI_CONTABILI.");
                    return;
                }

                // WHERE dinamico: COD_MANDATO LIKE @mand0 OR COD_MANDATO LIKE @mand1 ...
                var sbMandati = new StringBuilder();
                for (int i = 0; i < mandatiDaControllare.Count; i++)
                {
                    if (i > 0) sbMandati.Append(" OR ");
                    sbMandati.Append($"COD_MANDATO LIKE @mand{i}");
                }
                string whereMandati = sbMandati.ToString();

                string sqlPagam = $@"
                    SELECT        
                        CODICE_FISCALE
                    FROM            
                        MOVIMENTI_CONTABILI_ELEMENTARI
                    WHERE  
                        ANNO_ACCADEMICO = @aa
                        AND CODICE_MOVIMENTO IN
                        (
                            SELECT CODICE_MOVIMENTO
                            FROM   MOVIMENTI_CONTABILI_GENERALI
                            WHERE  {whereMandati}
                        )";

                HashSet<string> studentiDaRimuovereHash = new();

                using SqlCommand readData = new(sqlPagam, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                readData.Parameters.AddWithValue("@aa", selectedAA);

                for (int i = 0; i < mandatiDaControllare.Count; i++)
                {
                    // LIKE 'BSP0%' / 'BSS0%' ...
                    readData.Parameters.AddWithValue($"@mand{i}", mandatiDaControllare[i] + "%");
                }

                Logger.LogInfo(11, "Lavorazione studenti - controllo movimentazioni");

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(
                            Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpper());

                        if (codFiscale == debugStudente)
                        {
                            string test = "";
                        }

                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        if (studente != null)
                        {
                            // Se esiste un movimento per:
                            // - il pagamento corrente (es. BSP0)
                            // - oppure per il pagamento successivo (es. BSS0),
                            // lo studente viene escluso dalla lista corrente
                            studentiDaRimuovereHash.Add(studente.InformazioniPersonali.CodFiscale);
                            continue;
                        }
                    }
                }

                foreach (string codFiscale in studentiDaRimuovereHash)
                {
                    studentiDaPagare.Remove(codFiscale);
                }

                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovereHash.Count} studenti dalla lista di pagamento");
            }


            void FilterPagamenti()
            {
                Logger.LogDebug(null, "Inizio del filtraggio dei pagamenti degli studenti");
                string sqlPagam = $@"
                    SELECT distinct
                        Domanda.Cod_fiscale,
                        Decod_pagam_new.Cod_tipo_pagam_new AS Cod_tipo_pagam,
                        Pagamenti.Imp_pagato,
                        Ritirato_azienda
                    FROM
                        Pagamenti
                        INNER JOIN Domanda ON Pagamenti.anno_accademico = Domanda.anno_accademico AND Pagamenti.num_domanda = Domanda.Num_domanda
                        INNER JOIN #CFestrazione cf ON Domanda.cod_fiscale = cf.Cod_fiscale
                        INNER JOIN Decod_pagam_new ON Pagamenti.Cod_tipo_pagam = Decod_pagam_new.Cod_tipo_pagam_old OR Pagamenti.Cod_tipo_pagam = Decod_pagam_new.Cod_tipo_pagam_new
                            ";
                if (tipoBeneficio != TipoBeneficio.PremioDiLaurea.ToCode())
                {
                    sqlPagam += $@"
                    WHERE
                        Domanda.Anno_accademico = '{selectedAA}'";
                }
                SqlCommand readData = new(sqlPagam, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(11, $"Lavorazione studenti - inserimento pagamenti");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                        if (codFiscale == debugStudente)
                        {
                            string test = "";
                        }

                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        if (studente != null)
                        {
                            string cod_tipo_pagam = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_tipo_pagam")).ToUpper();

                            if (!studenteForzato)
                            {
                                if (cod_tipo_pagam.Substring(0, 2) != tipoBeneficio)
                                {
                                    continue;
                                }

                                if (cod_tipo_pagam.Substring(0, 3) == "BST" && !isTR)
                                {
                                    continue;
                                }

                                if (cod_tipo_pagam.Substring(0, 2) != "BS" && isTR)
                                {
                                    continue;
                                }
                            }
                            double.TryParse(Utilities.SafeGetString(reader, "Imp_pagato"), out double impPagato);
                            studente.AddPagamentoEffettuato(
                                cod_tipo_pagam,
                                impPagato,
                                Utilities.SafeGetString(reader, "Ritirato_azienda") == "1"
                                );
                        }
                    }
                }

                HashSet<string> studentiDaRimuovereHash = new();
                foreach (var pair in studentiDaPagare)
                {
                    if (studenteForzato) { continue; }

                    StudentePagamenti studenteDaControllare = pair.Value;
                    bool stessoPagamento = false;
                    bool okTassaRegionale = false;
                    if (studenteDaControllare.InformazioniPersonali.CodFiscale == debugStudente)
                    {
                        string test = "";
                    }
                    if (studenteDaControllare.InformazioniPagamento.PagamentiEffettuati == null || studenteDaControllare.InformazioniPagamento.PagamentiEffettuati.Count <= 0)
                    {
                        continue;
                    }
                    double importiPagati = 0;
                    foreach (Pagamento pagamento in studenteDaControllare.InformazioniPagamento.PagamentiEffettuati)
                    {
                        if (pagamento.ritiratoAzienda)
                        {
                            continue;
                        }
                        if (pagamento.codTipoPagam == codTipoPagamento)
                        {
                            stessoPagamento = true;
                            if (tipoBeneficio == TipoBeneficio.PremioDiLaurea.ToCode())
                            {
                                stessoPagamento = false;
                                Logger.LogWarning(null, $"Attenzione: Studente con cf {studenteDaControllare.InformazioniPersonali.CodFiscale} ha già preso il premio di laurea!");
                            }
                            break;
                        }

                        if (isTR &&
                            (
                                (
                                    (
                                    (studenteDaControllare.InformazioniBeneficio.SuperamentoEsami || studenteDaControllare.InformazioniBeneficio.SuperamentoEsamiTassaRegionale)
                                    || (studenteDaControllare.InformazioniIscrizione.AnnoCorso > 1 || studenteDaControllare.InformazioniIscrizione.AnnoCorso < 0)
                                    )
                                    && (pagamento.codTipoPagam == "BSS0" || pagamento.codTipoPagam == "BSS1" || pagamento.codTipoPagam == "BSS2")
                                    && !pagamento.ritiratoAzienda
                                )
                                ||
                                (
                                    studenteDaControllare.InformazioniBeneficio.SuperamentoEsamiTassaRegionale
                                    && (pagamento.codTipoPagam == "BSP0" || pagamento.codTipoPagam == "BSP1" || pagamento.codTipoPagam == "BSP2")
                                    && !pagamento.ritiratoAzienda
                                )
                            )
                        )
                        {
                            okTassaRegionale = true;
                        }


                        importiPagati += pagamento.importoPagamento;
                    }
                    if ((stessoPagamento || (isTR && !okTassaRegionale)) && !studentiDaRimuovereHash.Contains(studenteDaControllare.InformazioniPersonali.CodFiscale))
                    {
                        studentiDaRimuovereHash.Add(studenteDaControllare.InformazioniPersonali.CodFiscale);
                        continue;
                    }

                    Math.Round(importiPagati, 2);
                    studenteDaControllare.SetImportiPagati(importiPagati);

                    if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR && Math.Abs(studenteDaControllare.InformazioniBeneficio.ImportoBeneficio - studenteDaControllare.InformazioniPagamento.ImportoPagato) < 5)
                    {
                        studentiDaRimuovereHash.Add(studenteDaControllare.InformazioniPersonali.CodFiscale);
                    }
                }

                foreach (string codFiscale in studentiDaRimuovereHash)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovereHash.Count} studenti dalla lista di pagamento");

                string lastValue = codTipoPagamento[3..];
                string firstPart = codTipoPagamento[..3];
                string integrazioneValue = codTipoPagamento.Substring(2, 1);
                if (integrazioneValue == "I")
                {
                    isIntegrazione = true;
                    Logger.LogInfo(null, "Il tipo di pagamento indica una integrazione");
                }
                if (lastValue != "0" && lastValue != "9" && lastValue != "6" && lastValue != "I")
                {
                    ControlloRiemissioni(firstPart, lastValue);
                    isRiemissione = true;
                    Logger.LogInfo(null, "Il tipo di pagamento indica una riemissione");
                }
                else if (integrazioneValue == "I")
                {
                    ControlloIntegrazioni();
                    Logger.LogInfo(null, "Controllo integrazioni eseguito per i tipi di pagamento con integrazione");
                }
            }

            void ControlloRiemissioni(string firstPart, string lastValue)
            {
                Logger.LogDebug(null, "Inizio del controllo delle riemissioni per gli studenti");
                HashSet<string> studentiDaRimuovere = new();
                foreach (var pair in studentiDaPagare)
                {
                    StudentePagamenti studente = pair.Value;
                    if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                    {
                        string test = "";
                    }
                    if (studente.InformazioniPagamento.PagamentiEffettuati == null || studente.InformazioniPagamento.PagamentiEffettuati.Count <= 0)
                    {
                        studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                        continue;
                    }

                    bool pagamentoPossibile = false;
                    foreach (Pagamento pagamento in studente.InformazioniPagamento.PagamentiEffettuati)
                    {
                        string pagamFirstPart = pagamento.codTipoPagam[..3];
                        string pagamLastValue = pagamento.codTipoPagam[3..];
                        if (pagamFirstPart != firstPart)
                        {
                            continue;
                        }

                        if (!pagamento.ritiratoAzienda)
                        {
                            continue;
                        }

                        bool conditionMet = false;
                        switch (pagamLastValue)
                        {
                            case "0":
                                conditionMet = lastValue == "1";
                                break;
                            case "6":
                                conditionMet = lastValue == "7";
                                break;
                            case "9":
                                conditionMet = lastValue == "A";
                                break;
                            case "1":
                                conditionMet = lastValue == "2";
                                break;
                            case "A":
                                conditionMet = lastValue == "B";
                                break;
                            case "7":
                                conditionMet = lastValue == "8";
                                break;
                        }

                        if (conditionMet)
                        {
                            pagamentoPossibile = true;
                            break;
                        }
                    }
                    if (!pagamentoPossibile)
                    {
                        studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                    }
                }
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovere.Count} studenti dalla lista di pagamento dopo il controllo delle riemissioni");
            }

            void ControlloIntegrazioni()
            {
                Logger.LogDebug(null, "Inizio del controllo delle integrazioni per gli studenti");
                HashSet<string> studentiDaRimuovere = new();
                foreach (var pair in studentiDaPagare)
                {
                    StudentePagamenti studente = pair.Value;
                    if (studente.InformazioniPagamento.PagamentiEffettuati == null || studente.InformazioniPagamento.PagamentiEffettuati.Count <= 0)
                    {
                        studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                        continue;
                    }
                    bool pagamentoPossibile = false;
                    foreach (Pagamento pagamento in studente.InformazioniPagamento.PagamentiEffettuati)
                    {
                        string pagamentoBeneficio = pagamento.codTipoPagam.Substring(0, 2);
                        if (pagamentoBeneficio != tipoBeneficio)
                        {
                            continue;
                        }

                        if (pagamento.ritiratoAzienda)
                        {
                            continue;
                        }

                        if (pagamento.codTipoPagam == codTipoPagamento)
                        {
                            continue;
                        }

                        string codCatPagam = pagamento.codTipoPagam.Substring(2, 2);
                        switch (codCatPagam)
                        {
                            case "P0":
                                if (selectedTipoPagamento == "I0")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "P1":
                                if (selectedTipoPagamento == "I0")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "P2":
                                if (selectedTipoPagamento == "I0")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "S0":
                                if (selectedTipoPagamento == "I9")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "S1":
                                if (selectedTipoPagamento == "I9")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "S2":
                                if (selectedTipoPagamento == "I9")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                            case "I9":
                                if (selectedTipoPagamento == "II")
                                {
                                    pagamentoPossibile = true;
                                }
                                break;
                        }
                    }
                    if (!pagamentoPossibile)
                    {
                        studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                    }
                }
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovere.Count} studenti dalla lista di pagamento dopo il controllo delle integrazioni");
            }
            void ControlloProvvedimenti()
            {
                string? sqlProvv = selectedAcademicProcessor.GetProvvedimentiQuery(selectedAA, tipoBeneficio);

                SqlCommand readData = new(sqlProvv, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(null, "Lavorazione studenti - controllo provvedimenti");
                HashSet<string> listaStudentiDaMantenere = new();

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    listaStudentiDaMantenere = selectedAcademicProcessor.ProcessProvvedimentiQuery(reader);
                }

                HashSet<string> studentiDaRimuovere = new();
                // Find students to remove
                foreach (var pair in studentiDaPagare)
                {
                    if (!listaStudentiDaMantenere.Contains(pair.Key))
                    {
                        studentiDaRimuovere.Add(pair.Key);
                    }
                }

                DataTable studentiDaRimuovereTable = new();
                studentiDaRimuovereTable.Columns.Add("CodFiscale");
                Logger.LogInfo(null, $"Trovati {studentiDaRimuovere.Count} studenti senza provvedimento");
                // Remove students not present in the query
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaRimuovereTable.Rows.Add(codFiscale);
                    studentiDaPagare.Remove(codFiscale);
                }
                if (studentiDaRimuovereTable.Rows.Count > 0)
                {
                    Utilities.ExportDataTableToExcel(studentiDaRimuovereTable, GetCurrentPagamentoFolder(), fileName: "Studenti senza provvedimento modifica importo");
                }
            }
        }

        private void CheckLiquefazione()
        {
            // Check for payment blocks
            string sqlKiller = $@"
                    SELECT DISTINCT
                        Domanda.cod_fiscale 
                    FROM 
                        Domanda 
                        INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE
                        Domanda.anno_accademico = '{selectedAA}' and
                        Domanda.num_domanda in (
                            SELECT DISTINCT Num_domanda
                                FROM vMotivazioni_blocco_pagamenti
                                WHERE Anno_accademico = '{selectedAA}' 
                                    AND Data_fine_validita IS NULL 
                                    AND Blocco_pagamento_attivo = 1";
            if (tipoBeneficio == TipoBeneficio.BuonoLibro.ToCode())
            {
                sqlKiller += " AND cod_tipologia_blocco in ('BPD', 'BSS', 'BS1')";
            }
            sqlKiller += " )";

            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode())
            {
                sqlKiller += " AND Domanda.tipo_bando like 'L%'";
            }

            if (tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode())
            {
                sqlKiller += " AND Domanda.tipo_bando like 'CS'";
            }
            if (tipoBeneficio == TipoBeneficio.PremioDiLaurea.ToCode())
            {
                sqlKiller += " AND Domanda.tipo_bando like 'PL'";
            }

            SqlCommand readData = new(sqlKiller, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili");
            HashSet<string> listaStudentiDaEliminareBlocchi = new();
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    if (codFiscale == debugStudente)
                    {
                        string test = "";
                    }
                    studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                    if (studente != null && !listaStudentiDaEliminareBlocchi.Contains(studente.InformazioniPersonali.CodFiscale))
                    {
                        listaStudentiDaEliminareBlocchi.Add(studente.InformazioniPersonali.CodFiscale);
                    }
                }
            }
            string listaCFblocchi = string.Join(", ", listaStudentiDaEliminareBlocchi.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFblocchi))
            {
                string sqlUpdateBlocchi = $@"
                    UPDATE {dbTableName}
                        SET togliere_loreto = 'BLC',
                            note = 'Studente con blocchi al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFblocchi})
                    ";
                SqlCommand blocchiUpdate = new(sqlUpdateBlocchi, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                blocchiUpdate.ExecuteNonQuery();
            }

            // Check for missing IBAN
            string sqlStudentiSenzaIBAN = $@" 
                SELECT DISTINCT
                    vMODALITA_PAGAMENTO.Cod_fiscale, vMODALITA_PAGAMENTO.IBAN
                FROM
                    vMODALITA_PAGAMENTO 
                    INNER JOIN #CFEstrazione cfe ON vMODALITA_PAGAMENTO.Cod_fiscale = cfe.Cod_fiscale 
             ";
            SqlCommand IBANDATA = new(sqlStudentiSenzaIBAN, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            Logger.LogInfo(12, $"Lavorazione studenti - controllo IBAN");
            HashSet<string> listaStudentiDaEliminareIBAN = new();
            HashSet<string> listaStudentiDaBloccareIBAN = new();
            using (SqlDataReader reader = IBANDATA.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    if (codFiscale == debugStudente)
                    {
                        string test = "";
                    }
                    studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                    if (studente != null)
                    {
                        string IBAN = Utilities.SafeGetString(reader, "IBAN").ToUpper().Trim();
                        if (IBAN == string.Empty && !listaStudentiDaEliminareIBAN.Contains(studente.InformazioniPersonali.CodFiscale))
                        {
                            listaStudentiDaEliminareIBAN.Add(studente.InformazioniPersonali.CodFiscale);
                        }

                        bool ibanValido = IbanValidatorUtil.ValidateIban(IBAN);

                        if (!ibanValido && !listaStudentiDaBloccareIBAN.Contains(studente.InformazioniPersonali.CodFiscale))
                        {
                            listaStudentiDaBloccareIBAN.Add(studente.InformazioniPersonali.CodFiscale);
                        }
                    }
                }
            }
            string listaCFIBANMancante = string.Join(", ", listaStudentiDaEliminareIBAN.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFIBANMancante))
            {
                string sqlUpdateIban = $@"
                    UPDATE {dbTableName}
                        SET liquidabile = 0,
                            note = 'Studente senza IBAN valido al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFIBANMancante})
                    ";
                SqlCommand ibanUpdate = new(sqlUpdateIban, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                ibanUpdate.ExecuteNonQuery();
            }

            string listaCFIBANNonValido = string.Join(", ", listaStudentiDaEliminareIBAN.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFIBANNonValido))
            {
                string sqlUpdateIban = $@"
                    UPDATE {dbTableName}
                        SET liquidabile = 0,
                            note = 'Studente con IBAN errato al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFIBANNonValido})
                    ";
                SqlCommand ibanUpdate = new(sqlUpdateIban, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                ibanUpdate.ExecuteNonQuery();
                BlocksUtil.AddBlock(CONNECTION, sqlTransaction, listaStudentiDaEliminareIBAN.ToList<string>(), "BSS", selectedAA, "IBAN_Check", true);
            }

            // Check for non-winners
            string sqlStudentiNonVincitori = $@" 
                SELECT DISTINCT
                    Domanda.cod_fiscale
                FROM
                    Domanda 
                    INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                    INNER JOIN vEsiti_concorsi ON Domanda.Anno_accademico = vEsiti_concorsi.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsi.Num_domanda
                WHERE 
                    Domanda.anno_accademico = '{selectedAA}'
                    AND cod_beneficio = '{tipoBeneficio}'
                    AND cod_tipo_esito <> 2
                    
             ";
            SqlCommand nonVincitori = new(sqlStudentiNonVincitori, CONNECTION, sqlTransaction)
            {
                CommandTimeout = 9000000
            };
            Logger.LogInfo(12, $"Lavorazione studenti - controllo Vincitori");
            HashSet<string> listaStudentiDaEliminareNonVincitori = new();
            using (SqlDataReader reader = nonVincitori.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    if (codFiscale == debugStudente)
                    {
                        string test = "";
                    }
                    studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                    if (studente != null && !listaStudentiDaEliminareNonVincitori.Contains(studente.InformazioniPersonali.CodFiscale))
                    {
                        listaStudentiDaEliminareNonVincitori.Add(studente.InformazioniPersonali.CodFiscale);
                    }
                }
            }
            string listaCFEscluso = string.Join(", ", listaStudentiDaEliminareNonVincitori.Select(cf => $"'{cf}'"));
            if (!string.IsNullOrWhiteSpace(listaCFEscluso))
            {
                string sqlUpdateEscluso = $@"
                    UPDATE {dbTableName}
                        SET togliere_loreto = 'EXL',
                            note = 'Studente non vincitore al momento dell''estrazione'
                    WHERE
                        cod_fiscale IN ({listaCFEscluso})
                    ";
                SqlCommand esclusoUpdate = new(sqlUpdateEscluso, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                esclusoUpdate.ExecuteNonQuery();
            }
            HashSet<string> listaStudentiDaEliminarePEC = new();
            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode())
            {
                // Check for students with invalid PEC addresses
                string sqlStudentiPecKiller = $@"
                        SELECT LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, Indirizzo_PEC 
                        FROM LUOGO_REPERIBILITA_STUDENTE
                        INNER JOIN #CFEstrazione cfe ON LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE = cfe.Cod_fiscale 
                        LEFT OUTER JOIN vProfilo ON LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE = vProfilo.Cod_Fiscale 
                        WHERE ANNO_ACCADEMICO = '{selectedAA}' 
                            AND tipo_bando = 'lz' 
                            AND TIPO_LUOGO = 'DOL'
                            AND DATA_FINE_VALIDITA IS NULL
                            AND (INDIRIZZO = '' OR INDIRIZZO = 'ROMA' OR INDIRIZZO = 'CASSINO' OR INDIRIZZO = 'FROSINONE')
                            AND LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE in (select COD_FISCALE FROM vResidenza where ANNO_ACCADEMICO = '{selectedAA}' AND provincia_residenza = 'ee')
                            AND LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE in (select COD_FISCALE FROM vDomicilio where ANNO_ACCADEMICO = '{selectedAA}' AND (Indirizzo_domicilio = '' or Indirizzo_domicilio = 'ROMA' or Indirizzo_domicilio = 'CASSINO' or Indirizzo_domicilio = 'FROSINONE'  or prov = 'EE'))
                            AND Indirizzo_PEC IS NULL
                        ";
                SqlCommand nonPecCmd = new(sqlStudentiPecKiller, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(12, $"Lavorazione studenti - controllo PEC");

                using (SqlDataReader reader = nonPecCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        if (studente != null && !listaStudentiDaEliminarePEC.Contains(studente.InformazioniPersonali.CodFiscale))
                        {
                            listaStudentiDaEliminarePEC.Add(studente.InformazioniPersonali.CodFiscale);
                        }
                    }
                }
                string listaCFPEC = string.Join(", ", listaStudentiDaEliminarePEC.Select(cf => $"'{cf}'"));
                if (!string.IsNullOrWhiteSpace(listaCFPEC))
                {
                    string sqlUpdatePec = $@"
                        UPDATE {dbTableName}
                            SET togliere_PEC = 1,
                                note = 'Studente senza PEC al momento dell''estrazione'
                        WHERE
                            cod_fiscale IN ({listaCFPEC})
                        ";
                    SqlCommand pecUpdate = new(sqlUpdatePec, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    pecUpdate.ExecuteNonQuery();
                }
            }
            Logger.LogInfo(12, $"Numero studenti da eliminare per blocchi presenti in domanda = {listaStudentiDaEliminareBlocchi.Count}");
            Logger.LogInfo(12, $"Numero studenti da eliminare per IBAN mancante = {listaStudentiDaEliminareIBAN.Count}");
            Logger.LogInfo(12, $"Numero studenti bloccati per IBAN non valido = {listaStudentiDaBloccareIBAN.Count}");
            Logger.LogInfo(12, $"Numero studenti da eliminare perché non più vincitori = {listaStudentiDaEliminareNonVincitori.Count}");
            Logger.LogInfo(12, $"Numero studenti da eliminare per PEC mancante = {listaStudentiDaEliminarePEC.Count}");
            foreach (string codFiscale in listaStudentiDaEliminareBlocchi)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaEliminareIBAN)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaBloccareIBAN)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaEliminareNonVincitori)
            {
                studentiDaPagare.Remove(codFiscale);
            }
            foreach (string codFiscale in listaStudentiDaEliminarePEC)
            {
                studentiDaPagare.Remove(codFiscale);
            }

            Logger.LogInfo(12, $"Lavorazione studenti - controllo eliminabili - completato");
        }

        private void PopulateStudentsInformations()
        {
            PopulateStudentLuogoNascita();
            PopulateStudentResidenza();
            PopulateStudentDomicilio();
            PopulateStudentForzature();
            PopulateStudentPaymentMethod();
            PopulateStudentiVecchioEsitoPA();
            PopulateStudentiIscrizioni();
            if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR)
            {
                PopulateStudentReversali();
                PopulateStudentDetrazioni();
                PopulateNucleoFamiliare();
                if (!isIntegrazione)
                {
                    PopulateStudentiAssegnazioni();
                }
            }
            PopulateStudentiImpegni();
            PopulateStudentServizioSanitario();
            PopulateStudentEconomia();
            PopulateStudentMonetizzazioneMensa();
            PopulateImportoDaPagare();

            void PopulateStudentLuogoNascita()
            {
                string dataQuery = @"
                SELECT *
                FROM Studente 
                    LEFT OUTER JOIN Comuni ON Studente.Cod_comune_nasc = Comuni.Cod_comune 
                    INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(30, $"Lavorazione studenti - inserimento in luogo nascita");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        studente?.SetLuogoNascita(
                                Utilities.SafeGetString(reader, "COD_COMUNE_NASC"),
                                Utilities.SafeGetString(reader, "descrizione"),
                                Utilities.SafeGetString(reader, "COD_PROVINCIA")
                            );
                    }
                }
                Logger.LogInfo(30, $"UPDATE:Lavorazione studenti - inserimento in luogo nascita - completato");
            }
            void PopulateStudentResidenza()
            {
                string dataQuery = $@"


                        SELECT        LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO, LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, LUOGO_REPERIBILITA_STUDENTE.INDIRIZZO, Comuni.Cod_comune, Comuni.Descrizione AS comune_residenza, 
                                                 Comuni.Cod_provincia AS provincia_residenza, LUOGO_REPERIBILITA_STUDENTE.CAP
                        FROM            LUOGO_REPERIBILITA_STUDENTE INNER JOIN
                                                 Comuni ON LUOGO_REPERIBILITA_STUDENTE.COD_COMUNE = Comuni.Cod_comune
                                        INNER JOIN #CFEstrazione cfe ON LUOGO_REPERIBILITA_STUDENTE.Cod_fiscale = cfe.Cod_fiscale 
                        WHERE        (LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO = '{selectedAA}') AND (LUOGO_REPERIBILITA_STUDENTE.TIPO_LUOGO = 'RES') AND 
                                                 (LUOGO_REPERIBILITA_STUDENTE.DATA_VALIDITA =
                                                     (SELECT        MAX(DATA_VALIDITA) AS Expr1
                                                       FROM            LUOGO_REPERIBILITA_STUDENTE AS rsd
                                                       WHERE        (COD_FISCALE = luogo_reperibilita_studente.cod_fiscale) AND (ANNO_ACCADEMICO = luogo_reperibilita_studente.anno_accademico) AND (TIPO_LUOGO = 'RES')))
                        ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(35, $"Lavorazione studenti - inserimento in residenza");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        studente?.SetResidenza(
                                Utilities.SafeGetString(reader, "INDIRIZZO"),
                                Utilities.SafeGetString(reader, "Cod_comune"),
                                Utilities.SafeGetString(reader, "provincia_residenza"),
                                Utilities.SafeGetString(reader, "CAP"),
                                Utilities.SafeGetString(reader, "comune_residenza")
                            );
                    }
                }
                Logger.LogInfo(35, $"UPDATE:Lavorazione studenti - inserimento in residenza - completato");
            }
            void PopulateStudentForzature()
            {
                string dataQuery = $@"


                        SELECT forz.cod_fiscale, forz.status_sede
                        FROM Forzature_StatusSede forz
                        INNER JOIN #CFEstrazione cfe ON forz.Cod_fiscale = cfe.Cod_fiscale 
                        where Anno_Accademico = '{selectedAA}' and Data_fine_validita is null
                             ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(35, $"Lavorazione studenti - inserimento in forzature");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        string forzatura = Utilities.SafeGetString(reader, "status_sede").ToUpper();
                        studente?.SetForzatura(forzatura);
                    }
                }
                Logger.LogInfo(35, $"UPDATE:Lavorazione studenti - inserimento in forzatura - completato");
            }
            void PopulateStudentDomicilio()
            {
                // -----------------------
                // 0) Parse the academic year
                // -----------------------
                int startYear = int.Parse(selectedAA.Substring(0, 4));
                int endYear = int.Parse(selectedAA.Substring(4, 4));
                DateTime dateRangeStart = new DateTime(startYear, 10, 1);
                DateTime dateRangeEnd = new DateTime(endYear, 9, 30);

                // -----------------------
                // 1) Prepare the query
                // -----------------------
                string dataQuery = $@"
                    SELECT 
                        LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, 
	                    LUOGO_REPERIBILITA_STUDENTE.COD_COMUNE,
                        LUOGO_REPERIBILITA_STUDENTE.TITOLO_ONEROSO, 
                        LUOGO_REPERIBILITA_STUDENTE.N_SERIE_CONTRATTO, 
                        LUOGO_REPERIBILITA_STUDENTE.DATA_REG_CONTRATTO,  
                        LUOGO_REPERIBILITA_STUDENTE.DATA_DECORRENZA, 
                        LUOGO_REPERIBILITA_STUDENTE.DATA_SCADENZA, 
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_CONTRATTO, 
                        LUOGO_REPERIBILITA_STUDENTE.PROROGA, 
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_PROROGA, 
                        LUOGO_REPERIBILITA_STUDENTE.ESTREMI_PROROGA,
                        LUOGO_REPERIBILITA_STUDENTE.TIPO_CONTRATTO_TITOLO_ONEROSO,
                        LUOGO_REPERIBILITA_STUDENTE.DENOM_ENTE,
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_CONTRATTO,
                        LUOGO_REPERIBILITA_STUDENTE.IMPORTO_RATA
                    FROM LUOGO_REPERIBILITA_STUDENTE
                    INNER JOIN Comuni ON LUOGO_REPERIBILITA_STUDENTE.COD_COMUNE = Comuni.Cod_comune
                    INNER JOIN #CFEstrazione cfe ON LUOGO_REPERIBILITA_STUDENTE.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE 
                        (LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO = '{selectedAA}') 
                        AND (LUOGO_REPERIBILITA_STUDENTE.TIPO_LUOGO   = 'DOM') 
                        AND (LUOGO_REPERIBILITA_STUDENTE.DATA_VALIDITA = (
                            SELECT MAX(DATA_VALIDITA)
                            FROM LUOGO_REPERIBILITA_STUDENTE AS rsd
                            WHERE 
                                (COD_FISCALE    = LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE) 
                                AND (ANNO_ACCADEMICO = LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO) 
                                AND (TIPO_LUOGO     = 'DOM')
                        ))
                ";

                // -----------------------
                // 2) Read data into DTOs
                // -----------------------
                var domicilioRows = new List<StudentiDomicilioDTO>();
                Logger.LogInfo(35, "Lavorazione studenti - inserimento in domicilio");

                using (SqlCommand readData = new SqlCommand(dataQuery, CONNECTION, sqlTransaction))
                {
                    readData.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                            domicilioRows.Add(new StudentiDomicilioDTO
                            {
                                CodFiscale = codFiscale,
                                ComuneDomicilio = Utilities.SafeGetString(reader, "COD_COMUNE"),
                                TitoloOneroso = (Utilities.SafeGetInt(reader, "TITOLO_ONEROSO") == 1),
                                ContrattoEnte = (Utilities.SafeGetInt(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO") == 1),
                                SerieContratto = Utilities.SafeGetString(reader, "N_SERIE_CONTRATTO"),
                                DataRegistrazioneString = Utilities.SafeGetString(reader, "DATA_REG_CONTRATTO"),
                                DataDecorrenzaString = Utilities.SafeGetString(reader, "DATA_DECORRENZA"),
                                DataScadenzaString = Utilities.SafeGetString(reader, "DATA_SCADENZA"),
                                DurataContratto = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO"),
                                Prorogato = (Utilities.SafeGetInt(reader, "PROROGA") == 1),
                                DurataProroga = Utilities.SafeGetInt(reader, "DURATA_PROROGA"),
                                SerieProroga = Utilities.SafeGetString(reader, "ESTREMI_PROROGA"),
                                DenominazioneEnte = Utilities.SafeGetString(reader, "DENOM_ENTE"),
                                ImportoRataEnte = Utilities.SafeGetDouble(reader, "IMPORTO_RATA")
                            });
                        }
                    }
                }

                // -----------------------
                // 3) Process the rows
                // -----------------------
                foreach (var row in domicilioRows)
                {
                    if (!studentiDaPagare.TryGetValue(row.CodFiscale, out StudentePagamenti? studente))
                        continue;
                    if (studente == null)
                        continue;

                    // Debug check
                    if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                    {
                        string test = ""; // Just to set a breakpoint, presumably
                    }

                    // ----- TITOLO ONEROSO -----
                    bool titoloOneroso = row.TitoloOneroso;
                    DateTime.TryParse(row.DataRegistrazioneString, out DateTime dataRegistrazione);
                    DateTime.TryParse(row.DataDecorrenzaString, out DateTime dataDecorrenza);
                    DateTime.TryParse(row.DataScadenzaString, out DateTime dataScadenza);

                    // Only if there's a real contract
                    if (titoloOneroso)
                    {
                        // Calculate the overlap between the contract and the academic year period
                        DateTime effectiveStart = (dataDecorrenza > dateRangeStart) ? dataDecorrenza : dateRangeStart;
                        DateTime effectiveEnd = (dataScadenza < dateRangeEnd) ? dataScadenza : dateRangeEnd;

                        if (effectiveStart <= effectiveEnd)
                        {
                            int monthsCovered = ((effectiveEnd.Year - effectiveStart.Year) * 12)
                                              + (effectiveEnd.Month - effectiveStart.Month + 1);
                            if (monthsCovered >= 10)
                            {
                                // Mark the student as having a valid domicile for >=10 months
                                studente.SetDomicilioCheck(true);
                            }
                        }
                    }

                    // ----- CONTRATTO ENTE -----
                    bool contrattoEnte = row.ContrattoEnte;
                    string denominazioneEnte = row.DenominazioneEnte;
                    int durataContratto = row.DurataContratto;
                    double importoRataEnte = row.ImportoRataEnte;
                    bool contrattoEnteValido = false;

                    if (contrattoEnte)
                    {
                        if (string.IsNullOrWhiteSpace(denominazioneEnte))
                        {
                            // If there's no "Ente" name, consider invalid
                            studente.SetDomicilioCheck(false);
                        }
                        else
                        {
                            // Must have at least 10 months and a rate > 0
                            if (durataContratto < 10 || importoRataEnte <= 0)
                            {
                                studente.SetDomicilioCheck(false);
                            }
                            else
                            {
                                studente.SetDomicilioCheck(true);
                                contrattoEnteValido = true;
                            }
                        }
                    }

                    // ----- Serie Contratto & Serie Proroga -----
                    string serieContratto = row.SerieContratto;
                    string serieProroga = row.SerieProroga;

                    bool contrattoValido = contrattoEnteValido || DomicilioUtils.IsValidSerie(serieContratto);
                    bool prorogaValido = DomicilioUtils.IsValidSerie(serieProroga);

                    // If the proroga contains the same base as the contract, we consider that invalid
                    if (!string.IsNullOrEmpty(serieContratto)
                        && !string.IsNullOrEmpty(serieProroga)
                        && serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        prorogaValido = false;
                    }

                    // Debug check
                    if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                    {
                        string test = ""; // Another debugging breakpoint
                    }

                    // Finally, store all the domicile info into the Studente object
                    studente.SetDomicilio(
                        row.ComuneDomicilio,
                        titoloOneroso,
                        serieContratto,
                        dataRegistrazione,
                        dataDecorrenza,
                        dataScadenza,
                        row.DurataContratto,
                        row.Prorogato ?? false,
                        row.DurataProroga,
                        serieProroga,
                        contrattoValido,
                        prorogaValido,
                        contrattoEnte,
                        denominazioneEnte,
                        importoRataEnte
                    );
                }

                Logger.LogInfo(35, "UPDATE:Lavorazione studenti - inserimento in domicilio - completato");
            }
            void PopulateStudentPaymentMethod()
            {
                string dataQuery = @"
                    SELECT * 
                    FROM Studente 
                        LEFT OUTER JOIN vModalita_pagamento ON studente.cod_fiscale = vmodalita_pagamento.cod_fiscale
                        INNER JOIN #CFEstrazione cfe ON Studente.Cod_fiscale = cfe.Cod_fiscale 
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento in informazioni");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        if (studente?.InformazioniPersonali.CodFiscale == debugStudente)
                        {
                            string test = ""; // Just to set a breakpoint, presumably
                        }
                        studente?.SetInformations(
                                long.TryParse(Regex.Replace(Utilities.SafeGetString(reader, "telefono_cellulare"), @"[^\d]", ""), out long telefonoNumber) ? telefonoNumber : 0,
                                Utilities.SafeGetString(reader, "indirizzo_e_mail"),
                                Utilities.SafeGetString(reader, "IBAN"),
                                Utilities.SafeGetString(reader, "swift"),
                                Utilities.SafeGetString(reader, "Bonifico_estero") != "1"
                            );
                    }
                }
                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento in informazioni - completato");
            }
            void PopulateStudentReversali()
            {
                string dataQuery = $@"
                    SELECT Domanda.Cod_fiscale, Reversali.*, (SELECT DISTINCT cod_tipo_pagam_new FROM Decod_pagam_new where Cod_tipo_pagam_old = Reversali.Cod_tipo_pagam OR Cod_tipo_pagam_new = Reversali.Cod_tipo_pagam) AS cod_tipo_pagam_new
                    FROM Domanda 
                        INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale 
                        INNER JOIN Reversali ON Domanda.num_domanda = Reversali.num_domanda AND Domanda.Anno_accademico = Reversali.Anno_accademico
                    WHERE Reversali.Ritirato_azienda = 0 AND Domanda.Anno_accademico = '{selectedAA}'
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(45, $"Lavorazione studenti - inserimento in reversali");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        studente?.AddReversale(
                                    Utilities.SafeGetString(reader, "cod_reversale"),
                                    double.TryParse(Utilities.SafeGetString(reader, "importo"), out double importo) ? importo : 0,
                                    Utilities.SafeGetString(reader, "Note"),
                                    Utilities.SafeGetString(reader, "cod_tipo_pagam"),
                                    Utilities.SafeGetString(reader, "cod_tipo_pagam_new")
                                );
                    }
                }
                Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento in reversali - completato");
            }
            void PopulateStudentDetrazioni()
            {
                string dataQuery = $@"
                    SELECT CODICE_FISCALE, ID_CAUSALE, IMPORTO, NOTE_MOVIMENTO_ELEMENTARE
                    FROM MOVIMENTI_CONTABILI_ELEMENTARI
                        INNER JOIN #CFEstrazione cfe ON MOVIMENTI_CONTABILI_ELEMENTARI.CODICE_FISCALE = cfe.Cod_fiscale 
                    WHERE SEGNO = 0 AND Anno_accademico = '{selectedAA}' AND CODICE_MOVIMENTO IS NULL
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(45, $"Lavorazione studenti - inserimento in detrazioni");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "CODICE_FISCALE").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        studente?.AddDetrazione(
                                    Utilities.SafeGetString(reader, "ID_CAUSALE"),
                                    double.TryParse(Utilities.SafeGetString(reader, "IMPORTO"), out double importo) ? importo : 0,
                                    Utilities.SafeGetString(reader, "NOTE_MOVIMENTO_ELEMENTARE"),
                                    true
                                );
                    }
                }
                Logger.LogInfo(45, $"UPDATE:Lavorazione studenti - inserimento in detrazioni - completato");
            }
            void PopulateStudentiVecchioEsitoPA()
            {
                string dataQuery = $@"
                    select distinct Domanda.Cod_fiscale, Esiti_concorsi.Cod_tipo_esito From 
                        Domanda inner join 
                        #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale inner join
                        Esiti_concorsi ON Domanda.Anno_accademico = Esiti_concorsi.Anno_accademico and Domanda.Num_domanda = Esiti_concorsi.Num_domanda
                    where domanda.Anno_accademico = '{selectedAA}' and Esiti_concorsi.Cod_beneficio = 'PA' and Esiti_concorsi.Cod_tipo_esito = '2'
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento esiti PA");
                List<string> studentiDaRimuovere = new List<string>();
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        studente?.SetEraVincitorePA(true);

                        if (studente != null && selectedRichiestoPA == "3")
                        {
                            studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                            continue;
                        }
                    }
                }

                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento esiti PA - completato");
            }
            void PopulateStudentiIscrizioni()
            {
                string dataQuery = $@"
                    SELECT        vIscrizioni.Cod_fiscale, vIscrizioni.Cod_tipologia_studi, vIscrizioni.Cod_corso_laurea, vIscrizioni.Cod_sede_studi, vIscrizioni.Cod_facolta, Corsi_laurea.Comune_Sede_studi, vIscrizioni.Conferma_semestre_filtro
                    FROM    vIscrizioni INNER JOIN
                         Corsi_laurea ON vIscrizioni.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea AND vIscrizioni.Anno_accad_inizio = Corsi_laurea.Anno_accad_inizio AND 
                         vIscrizioni.Cod_tipo_ordinamento = Corsi_laurea.Cod_tipo_ordinamento AND vIscrizioni.Cod_facolta = Corsi_laurea.Cod_facolta AND vIscrizioni.Cod_sede_studi = Corsi_laurea.Cod_sede_studi AND 
                         vIscrizioni.Cod_tipologia_studi = Corsi_laurea.Cod_tipologia_studi
                        INNER JOIN                         
                            #CFEstrazione cfe ON vIscrizioni.Cod_fiscale = cfe.Cod_fiscale
                    WHERE vIscrizioni.anno_accademico = '{selectedAA}' and vIscrizioni.tipo_bando = 'lz' 

                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento iscrizioni");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);

                        if (studente != null)
                        {
                            studente.InformazioniIscrizione.CodCorsoLaurea = Utilities.SafeGetString(reader, "cod_corso_laurea");
                            studente.InformazioniIscrizione.CodFacolta = Utilities.SafeGetString(reader, "Cod_facolta");
                            studente.InformazioniIscrizione.CodSedeStudi = Utilities.SafeGetString(reader, "cod_sede_studi");
                            studente.InformazioniIscrizione.ComuneSedeStudi = Utilities.SafeGetString(reader, "Comune_Sede_studi");
                            studente.InformazioniIscrizione.ConfermaSemestreFiltro = Utilities.SafeGetInt(reader, "Conferma_semestre_filtro");
                        }
                    }
                }

                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento iscrizioni - completato");
            }
            void PopulateNucleoFamiliare()
            {
                string dataQuery = $@"
                    select domanda.Cod_fiscale, Num_componenti, Numero_conviventi_estero
                    from Domanda 
                    inner join #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale
                    inner join vNucleo_familiare vn on Domanda.Anno_accademico = vn.Anno_accademico and Domanda.Num_domanda = vn.Num_domanda 
                    where Domanda.Anno_accademico = '{selectedAA}' and Tipo_bando = 'lz'
                    ";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(40, $"Lavorazione studenti - inserimento nucleo familiare");
                List<string> studentiDaRimuovere = new List<string>();
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);


                        if (studente != null)
                        {
                            int numeroComponenti = Utilities.SafeGetInt(reader, "Num_componenti");
                            int numeroComponentiEstero = Utilities.SafeGetInt(reader, "Numero_conviventi_estero");
                            studente.SetNucleoFamiliare(numeroComponenti, numeroComponentiEstero);
                        }
                    }
                }

                Logger.LogInfo(40, $"UPDATE:Lavorazione studenti - inserimento nucleo familiare - completato");
            }
            void PopulateStudentiAssegnazioni()
            {
                // ---------------------------
                // 1) Get "date info" FIRST
                // ---------------------------
                string dateQuery = $@"
                    SELECT 
                        FORMAT(min_data_PA, 'dd/MM/yyyy') AS min_data_PA, 
                        FORMAT(max_data_PA, 'dd/MM/yyyy') AS max_data_PA, 
                        detrazione_PA, 
                        detrazione_PA_fuori_corso
                    FROM DatiGenerali_con 
                    WHERE Anno_accademico = '{selectedAA}'
                ";

                // Local variables to store data from the first query
                DateTime min_data_PA = new(1990, 01, 01);
                DateTime max_data_PA = new(2999, 01, 01);
                double detrazione_PA = 0;
                double detrazione_PA_fuori_corso = 0;

                // Read the single row from DatiGenerali_con (if any)
                using (SqlCommand readDate = new(dateQuery, CONNECTION, sqlTransaction))
                {
                    readDate.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readDate.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string minDataPAStr = Utilities.SafeGetString(reader, "min_data_PA");
                            string maxDataPAStr = Utilities.SafeGetString(reader, "max_data_PA");
                            string detrazionePAStr = Utilities.SafeGetString(reader, "detrazione_PA");
                            string detrazionePAFuoriCorsoStr = Utilities.SafeGetString(reader, "detrazione_PA_fuori_corso");

                            if (!DateTime.TryParse(minDataPAStr, out min_data_PA))
                            {
                                Logger.LogError(null, "Errore nella data minima per Assegnazione PA");
                            }
                            if (!DateTime.TryParse(maxDataPAStr, out max_data_PA))
                            {
                                Logger.LogError(null, "Errore nella data massima per Assegnazione PA");
                            }
                            if (!double.TryParse(detrazionePAStr, out detrazione_PA))
                            {
                                Logger.LogError(null, "Errore nella detrazione PA");
                            }
                            if (!double.TryParse(detrazionePAFuoriCorsoStr, out detrazione_PA_fuori_corso))
                            {
                                Logger.LogError(null, "Errore nella detrazione PA per i fuori corso");
                            }
                        }
                    }
                }

                // ---------------------------
                // 2) Read "assegnazioni"
                // ---------------------------
                // We'll store rows in a simple in-memory list, then close the reader
                var assegnazioniList = new List<AssegnazionePaDto>();

                string dataQuery = $@"
                    SELECT DISTINCT     
                        Assegnazione_PA.Cod_fiscale,
                        Assegnazione_PA.Cod_Pensionato, 
                        Assegnazione_PA.Cod_Stanza, 
                        FORMAT(Assegnazione_PA.Data_Decorrenza, 'dd/MM/yyyy') AS Data_Decorrenza, 
                        FORMAT(Assegnazione_PA.Data_Fine_Assegnazione, 'dd/MM/yyyy') AS Data_Fine_Assegnazione, 
                        Assegnazione_PA.Cod_Fine_Assegnazione,
                        Costo_Servizio.Tipo_stanza, 
                        Costo_Servizio.Importo as importo_mensile,
                        Assegnazione_PA.id_assegnazione_pa
                    FROM            
                        Assegnazione_PA 
                        INNER JOIN vStanza 
                            ON Assegnazione_PA.Cod_Stanza = vStanza.Cod_Stanza 
                            AND Assegnazione_PA.Cod_Pensionato = vStanza.Cod_Pensionato 
                        INNER JOIN Costo_Servizio 
                            ON vStanza.Tipo_costo_Stanza = Costo_Servizio.Tipo_stanza 
                            AND Assegnazione_PA.Anno_Accademico = Costo_Servizio.Anno_accademico 
                            AND Assegnazione_PA.Cod_Pensionato = Costo_Servizio.Cod_pensionato
                        INNER JOIN #CFEstrazione cfe 
                            ON Assegnazione_PA.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE        
                        (Assegnazione_PA.Anno_Accademico = '{selectedAA}') AND 
                        (Assegnazione_PA.Cod_movimento = '01') AND 
                        (Assegnazione_PA.Ind_Assegnazione = 1) AND 
                        (Assegnazione_PA.Status_Assegnazione = 0) AND
                        Costo_Servizio.Cod_periodo = 'M' AND 
                        Assegnazione_PA.Data_Accettazione IS NOT NULL
                    ORDER BY Assegnazione_PA.id_assegnazione_pa
                ";

                Logger.LogInfo(50, "Lavorazione studenti - inserimento in assegnazioni");
                HashSet<string> processedFiscalCodes = new();
                HashSet<string> studentiDaRimuovere = new();

                using (SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction))
                {
                    readData.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            assegnazioniList.Add(new AssegnazionePaDto
                            {
                                CodFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper()),
                                CodPensionato = Utilities.SafeGetString(reader, "Cod_Pensionato"),
                                CodStanza = Utilities.SafeGetString(reader, "Cod_Stanza"),
                                DataDecorrenza = Utilities.SafeGetString(reader, "Data_Decorrenza"),
                                DataFineAssegnazione = Utilities.SafeGetString(reader, "Data_Fine_Assegnazione"),
                                CodFineAssegnazione = Utilities.SafeGetString(reader, "Cod_Fine_Assegnazione"),
                                TipoStanza = Utilities.SafeGetString(reader, "Tipo_stanza"),
                                ImportoMensileStr = Utilities.SafeGetString(reader, "importo_mensile"),
                                IdAssegnazionePa = Utilities.SafeGetString(reader, "id_assegnazione_pa")
                            });
                        }
                    }
                }

                var assegnazioniInfoPerStudente = assegnazioniList
                    .GroupBy(a => a.CodFiscale)
                    .ToDictionary(
                        g => g.Key,
                        g => new
                        {
                            Count = g.Count(),
                            SingleStartEndEqual = g.Count() == 1 &&
                                                  DateTime.TryParse(g.First().DataDecorrenza, out var s) &&
                                                  DateTime.TryParse(g.First().DataFineAssegnazione, out var e) &&
                                                  s == e
                        });

                // Keep track of fiscal codes for which we already evaluated the "remove" decision
                HashSet<string> removalEvaluated = new();

                // ---------------------------
                // 3) Process data OFFLINE
                // ---------------------------
                // Now we do the "business logic" with the results in memory.

                foreach (var assegnazione in assegnazioniList)
                {
                    // For convenience
                    string codFiscale = assegnazione.CodFiscale;
                    if (!studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente))
                    {
                        // Studente not found in our dictionary
                        continue;
                    }
                    if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                    {
                        string test = "";
                    }
                    // If stanza is 'XXX' or studente is null, just skip it
                    if (studente == null || assegnazione.CodStanza == "XXX")
                    {
                        studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                        continue;
                    }

                    // If selectedRichiestoPA is 0 or 3 => remove the student
                    if (selectedRichiestoPA == "0" || selectedRichiestoPA == "3")
                    {
                        studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                        continue;
                    }

                    bool studenteFuoriCorso = (studente.InformazioniIscrizione.AnnoCorso == -1 && !studente.InformazioniPersonali.Disabile);
                    bool studenteDisabileFuoriCorso = (studente.InformazioniIscrizione.AnnoCorso == -2 && studente.InformazioniPersonali.Disabile);

                    // ---------------------
                    //    PAGAMENTO = PR
                    // ---------------------
                    if (categoriaPagam == "PR" && !processedFiscalCodes.Contains(codFiscale))
                    {
                        if (studente.InformazioniBeneficio.EsitoPA != 2 || assegnazione.CodFineAssegnazione == "03")
                        {
                            continue;
                        }

                        // Check detrazioni or reversali
                        bool detrazioneAcconto = false;
                        if (studente.InformazioniPagamento.Detrazioni != null && studente.InformazioniPagamento.Detrazioni.Count > 0)
                        {
                            foreach (Detrazione detrazione in studente.InformazioniPagamento.Detrazioni)
                            {
                                if (detrazione.codReversale == "01")
                                {
                                    detrazioneAcconto = true;
                                    break;
                                }
                            }
                            if (detrazioneAcconto) continue;
                        }
                        else if (studente.InformazioniPagamento.Reversali != null && studente.InformazioniPagamento.Reversali.Count > 0)
                        {
                            foreach (Reversale reversale in studente.InformazioniPagamento.Reversali)
                            {
                                if (reversale.codReversale == "01")
                                {
                                    detrazioneAcconto = true;
                                    break;
                                }
                            }
                            if (detrazioneAcconto) continue;
                        }
                        else if (isRiemissione)
                        {
                            // If is a re-issue, skip again
                            continue;
                        }

                        // Add Detrazione logic
                        if (!studenteFuoriCorso && !studenteDisabileFuoriCorso)
                        {
                            studente.AddDetrazione("01", detrazione_PA, "Detrazione acconto PA");
                        }
                        else
                        {
                            studente.AddDetrazione("01", detrazione_PA_fuori_corso, "Detrazione acconto PA");
                        }
                        processedFiscalCodes.Add(codFiscale);
                    }
                    // ---------------------
                    //   PAGAMENTO = SA
                    // ---------------------
                    else if (categoriaPagam == "SA")
                    {
                        if (!DateTime.TryParse(assegnazione.DataFineAssegnazione?.Trim(), out DateTime endDate))
                        {
                            endDate = DateTime.MaxValue;
                        }

                        // Parse the importo mensile
                        double importoMensile = 0;
                        _ = double.TryParse(assegnazione.ImportoMensileStr?.Trim(), out importoMensile);

                        // Actually add the assegnazione
                        AssegnazioneDataCheck result = studente.AddAssegnazione(
                            assegnazione.CodPensionato?.Trim() ?? "",
                            assegnazione.CodStanza?.Trim() ?? "",
                            DateTime.Parse(assegnazione.DataDecorrenza?.Trim() ?? "01/01/0001"),
                            endDate,
                            assegnazione.CodFineAssegnazione?.Trim() ?? "",
                            assegnazione.TipoStanza?.Trim() ?? "",
                            importoMensile,
                            min_data_PA,
                            max_data_PA,
                            studenteFuoriCorso || studenteDisabileFuoriCorso,
                            assegnazione.IdAssegnazionePa
                        );

                        // Check if we had any error from AddAssegnazione
                        if (result != AssegnazioneDataCheck.Corretto)
                        {

                            string message = result switch
                            {
                                AssegnazioneDataCheck.Eccessivo =>
                                    "Assegnazione posto alloggio superiore alle mensilità possibili (10 mesi)",
                                AssegnazioneDataCheck.Incorretto =>
                                    "Assegnazione posto alloggio con data fine minore della data di entrata",
                                AssegnazioneDataCheck.DataUguale =>
                                    "Assegnazione posto alloggio con data decorrenza e fine uguali",
                                AssegnazioneDataCheck.DataDecorrenzaMinoreDiMin =>
                                    "Assegnazione posto alloggio con data decorrenza minore del minimo previsto dal bando",
                                AssegnazioneDataCheck.DataFineAssMaggioreMax =>
                                    "Assegnazione posto alloggio con data fine maggiore del massimo previsto dal bando",
                                AssegnazioneDataCheck.MancanzaDataFineAssegnazione =>
                                    "Assegnazione posto alloggio senza data fine",
                                // Fallback
                                _ => "Errore sconosciuto"
                            };

                            if (!studentiConErroriPA.TryGetValue(studente, out List<string>? value))
                            {
                                value = new List<string>();
                                studentiConErroriPA.Add(studente, value);
                            }
                            studentiDaRimuovere.Add(studente.InformazioniPersonali.CodFiscale);
                            value.Add(message);
                        }
                    }
                }

                // -----------------------------
                // Clean-up or post-processing
                // -----------------------------
                // Remove the students we flagged
                foreach (string codFiscale in studentiDaRimuovere)
                {
                    studentiDaPagare.Remove(codFiscale);
                }

                if (studentiConErroriPA.Count > 0)
                {
                    DataTable dtErr = new();
                    dtErr.Columns.Add("CodFiscale");
                    dtErr.Columns.Add("NumDomanda");
                    dtErr.Columns.Add("Cognome");
                    dtErr.Columns.Add("Nome");
                    dtErr.Columns.Add("CodEnte");
                    dtErr.Columns.Add("AnnoCorso");
                    dtErr.Columns.Add("EsitoPA");
                    dtErr.Columns.Add("NumAssegnazioni");
                    dtErr.Columns.Add("Errori"); // pipe-joined list

                    foreach (var kvp in studentiConErroriPA)
                    {
                        var s = kvp.Key;
                        var errori = kvp.Value;

                        string cf = s?.InformazioniPersonali?.CodFiscale ?? "";
                        string numDomanda = s?.InformazioniPersonali?.NumDomanda ?? "";
                        string cognome = s?.InformazioniPersonali?.Cognome ?? "";
                        string nome = s?.InformazioniPersonali?.Nome ?? "";
                        string codEnte = s?.InformazioniIscrizione?.CodEnte ?? "";
                        string annoCorso = (s?.InformazioniIscrizione?.AnnoCorso ?? 0).ToString();
                        string esitoPA = (s?.InformazioniBeneficio?.EsitoPA ?? 0).ToString();
                        int nAss = s?.InformazioniPagamento?.Assegnazioni?.Count ?? 0;
                        string msg = string.Join(" | ", errori.Distinct());

                        dtErr.Rows.Add(cf, numDomanda, cognome, nome, codEnte, annoCorso, esitoPA, nAss, msg);
                    }

                    // Uses your existing Excel exporter (same utility used elsewhere)
                    Utilities.ExportDataTableToExcel(dtErr, GetCurrentPagamentoFolder(), fileName: "Errori Assegnazioni PA");
                    Logger.LogInfo(60, $"Esportati {dtErr.Rows.Count} studenti con errori PA (Errori Assegnazioni PA.xlsx).");
                }

                Logger.LogInfo(50, "UPDATE:Lavorazione studenti - inserimento in assegnazioni - completato");
            }
            void PopulateStudentiImpegni()
            {
                string currentBeneficio = isTR ? "TR" : tipoBeneficio;
                if(currentBeneficio == "TR")
                {
                    string test = "";
                }
                string dataQuery = $@"
                    SELECT 
                        Specifiche_impegni.Cod_fiscale, 
                        num_impegno_primaRata, 
                        num_impegno_saldo, 
                        importo_assegnato,
                        categoria_CU
                    FROM Specifiche_impegni
                    INNER JOIN #CFEstrazione cfe ON Specifiche_impegni.Cod_fiscale = cfe.Cod_fiscale 
                    WHERE 
                        Cod_beneficio = '{currentBeneficio}' 
                        AND Anno_accademico = '{selectedAA}' 
                        AND Data_fine_validita IS NULL
                ";

                var studentiSenzaImpegno = new HashSet<string>();
                var studentiDaRimuovereIntegrazione = new HashSet<string>();

                // Step 1: Read all data from the DB into an in-memory list
                List<PopulateStudentiImpegniDTO> rows = new();

                Logger.LogInfo(45, $"Lavorazione studenti - inserimento impegni");

                using (SqlCommand readData = new SqlCommand(dataQuery, CONNECTION, sqlTransaction))
                {
                    readData.CommandTimeout = 9000000;

                    using (SqlDataReader reader = readData.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var codFiscale = Utilities.RemoveAllSpaces(
                                                   Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                            var impegnoPrimaRata = Utilities.SafeGetString(reader, "num_impegno_primaRata");
                            var impegnoSaldo = Utilities.SafeGetString(reader, "num_impegno_saldo");
                            double.TryParse(Utilities.SafeGetString(reader, "importo_assegnato"), out double importoAssegnato);
                            var categoriaCU = Utilities.SafeGetString(reader, "categoria_CU");


                            rows.Add(new PopulateStudentiImpegniDTO
                            {
                                CodFiscale = codFiscale,
                                ImpegnoPrimaRata = impegnoPrimaRata,
                                ImpegnoSaldo = impegnoSaldo,
                                ImportoAssegnato = importoAssegnato,
                                CategoriaCU = categoriaCU,
                            });
                        }
                    }
                }

                // Step 2: Process in-memory
                foreach (var row in rows)
                {
                    if (studentiDaPagare.TryGetValue(row.CodFiscale, out StudentePagamenti? studente))
                    {
                        if (studente is null) continue;

                        if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                        {
                            string test = "";
                        }
                        // Integrations check
                        if (isIntegrazione)
                        {
                            if (studente.InformazioniBeneficio.ImportoBeneficio != row.ImportoAssegnato)
                            {
                                studentiDaRimuovereIntegrazione.Add(studente.InformazioniPersonali.CodFiscale);
                                continue;
                            }
                        }

                        // Decide which impegno to use
                        string impegnoToSet = (categoriaPagam == "PR")
                            ? row.ImpegnoPrimaRata
                            : row.ImpegnoSaldo;

                        if (string.IsNullOrWhiteSpace(impegnoToSet))
                        {
                            studentiSenzaImpegno.Add(studente.InformazioniPersonali.CodFiscale);
                            continue;
                        }

                        // Finally set the impegno
                        studente.SetImpegno(impegnoToSet);
                        studente.SetCategoriaCU(row.CategoriaCU);
                    }
                }

                // Clean up
                if (studentiSenzaImpegno.Any())
                {
                    Logger.LogWarning(null,
                        $"Trovati {studentiSenzaImpegno.Count} studenti senza impegno");
                }
                if (studentiDaRimuovereIntegrazione.Any())
                {
                    Logger.LogWarning(null,
                        $"Trovati {studentiDaRimuovereIntegrazione.Count} studenti " +
                        $"con importo borsa diverso da quello assegnato");
                }

                foreach (string codFiscale in studentiSenzaImpegno)
                {
                    studentiDaPagare.Remove(codFiscale);
                }
                foreach (string codFiscale in studentiDaRimuovereIntegrazione)
                {
                    Logger.LogDebug(null,
                        $"Rimosso cf: {codFiscale} per assenza provvedimento per integrazione importi");
                    studentiDaPagare.Remove(codFiscale);
                }

                Logger.LogInfo(45,
                    "UPDATE:Lavorazione studenti - inserimento impegni - completato");
            }
            void PopulateStudentServizioSanitario()
            {
                string dataQuery = $@"
                SELECT vServizio_sanitario.cod_fiscale
                FROM vServizio_sanitario 
                    INNER JOIN #CFEstrazione cfe ON vServizio_sanitario.Cod_fiscale = cfe.Cod_fiscale
                WHERE anno_accademico = '{selectedAA}' and Servizio_concesso = 1
";

                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(30, $"Lavorazione studenti - inserimento servizio sanitario");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        studente?.SetServizioSanitario(true);
                    }
                }
                Logger.LogInfo(30, $"UPDATE:Lavorazione studenti - inserimento servizio sanitario - completato");
            }
            void PopulateStudentEconomia()
            {
                string tipoBando = MapTipoBando(tipoBeneficio);

                string dataQuery = $@"
                SELECT Domanda.cod_fiscale, vv.ISEEDSU
                FROM Domanda 
                    INNER JOIN #CFEstrazione cfe ON Domanda.Cod_fiscale = cfe.Cod_fiscale
                    INNER JOIN vValori_calcolati vv ON Domanda.Num_domanda = vv.Num_domanda and Domanda.Anno_accademico = vv.Anno_accademico
                Where Domanda.anno_accademico = '{selectedAA}' and tipo_bando = '{tipoBando}'
";
                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(30, $"Lavorazione studenti - inserimento ISEE");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        if (studente != null)
                        {
                            studente.InformazioniPagamento.ValoreISEE = Utilities.SafeGetDouble(reader, "ISEEDSU");
                        }
                    }
                }
                Logger.LogInfo(30, $"UPDATE:Lavorazione studenti - inserimento ISEE - completato");
            }
            void PopulateStudentMonetizzazioneMensa()
            {
                string dataQuery = $@"
                SELECT vMonetizzazione_mensa.cod_fiscale, vMonetizzazione_mensa.Concessa_monetizzazione
                FROM vMonetizzazione_mensa 
                    INNER JOIN #CFEstrazione cfe ON vMonetizzazione_mensa.Cod_fiscale = cfe.Cod_fiscale
                Where anno_accademico = '{selectedAA}'
";
                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                Logger.LogInfo(30, $"Lavorazione studenti - inserimento monetizzazione mensa");
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                        studentiDaPagare.TryGetValue(codFiscale, out StudentePagamenti? studente);
                        if (studente != null)
                        {
                            studente.InformazioniPagamento.ConcessaMonetizzazioneMensa = Utilities.SafeGetInt(reader, "Concessa_monetizzazione") == 1 ? true : false ;
                        }
                    }
                }
                Logger.LogInfo(30, $"UPDATE:Lavorazione studenti - inserimento monetizzazione mensa - completato");
            }

            void PopulateImportoDaPagare()
            {
                Logger.LogInfo(55, $"Lavorazione studenti - Calcolo importi");

                double sogliaISEE = 0;
                double importoPendolare = 0;

                string dataQuery = $@"
                SELECT Soglia_Isee, Importo_borsa_C
                FROM DatiGenerali_con      
                WHERE anno_accademico = '{selectedAA}'
";
                SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction)
                {
                    CommandTimeout = 9000000
                };
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sogliaISEE = Utilities.SafeGetDouble(reader, "Soglia_Isee");
                        importoPendolare = Utilities.SafeGetDouble(reader, "Importo_borsa_C");
                    }
                }

                if(sogliaISEE == 0 || importoPendolare == 0)
                {
                    throw new Exception("PORCO GIUDA COME");
                }

                var listaStudInfo = studentiDaPagare
                    .Values                          // IEnumerable<StudentePagamenti>
                    .Cast<StudenteInfo>()            // up-cast each element
                    .ToList();                       // List<StudenteInfo>

                //var result = StatusSedeUtils.CalcolaSedeStudiBulk(CONNECTION, selectedAA, listaStudInfo, sqlTransaction);
                //// convert to DataTable
                //var table = new DataTable();
                //table.Columns.Add("CodFiscale", typeof(string));
                //table.Columns.Add("StatusSede", typeof(string));
                //table.Columns.Add("statusSedeDB", typeof(string)); 

                //foreach (var kvp in result)
                //{
                //    string codiceFiscale = kvp.Key;
                //    string statusSede = kvp.Value;

                //    // try to get the custom object from your other dictionary
                //    studentiDaPagare.TryGetValue(codiceFiscale, out var studInfo);

                //    // extract the DB‐status (or default to empty/whatever you like)
                //    string statusSedeDB = studInfo.InformazioniSede.StatusSede;

                //    // add to DataTable
                //    var row = table.NewRow();
                //    row["CodFiscale"] = codiceFiscale;
                //    row["StatusSede"] = statusSede;
                //    row["StatusSedeDB"] = statusSedeDB;
                //    table.Rows.Add(row);
                //}

                //Utilities.ExportDataTableToExcel(table, selectedSaveFolder);


                // Use thread-safe collections
                ConcurrentDictionary<string, bool> studentiDaRimuovereDallaTabella = new();
                ConcurrentDictionary<string, double> studentiPAnegativo = new();
                ConcurrentBag<(string CodFiscale, string Motivazione)> studentiRimossiBag = new();
                ConcurrentBag<(string CodFiscale, string Motivazione)> studentiPagatiComePendolari = new();

                Parallel.ForEach(studentiDaPagare, pair =>
                {
                    StudentePagamenti studente = pair.Value;

                    if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                    {
                        string test = "";
                    }

                    // Initialize variables
                    double importoDaPagare = studente.InformazioniBeneficio.ImportoBeneficio;
                    double importoMassimo = studente.InformazioniBeneficio.ImportoBeneficio;


                    if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR)
                    {
                        selectedAcademicProcessor.AdjustPendolarePayment(
                            studente,
                            ref importoDaPagare,
                            ref importoMassimo,
                            studentiPagatiComePendolari,
                            sogliaISEE,
                            importoPendolare
                        );
                    }

                    double importoPA = 0;
                    double accontoPA = 0;

                    double importoDetrazioni = 0;
                    double importoReversali = 0;

                    // Check selectedImpegno
                    if (selectedImpegno != "0000")
                    {
                        if (studente.InformazioniPagamento.NumeroImpegno != selectedImpegno)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            return;
                        }
                    }

                    // Initialize flags
                    bool hasPrimaRata = false;
                    bool primaRataStorno = false;
                    bool hasSaldo = false;
                    bool saldoStorno = false;
                    bool riemessaPrimaRata = false;
                    bool riemessaSecondaRata = false;
                    bool integrazionePrimaRata = false;
                    bool stornoIntegrazionePrimaRata = false;
                    bool riemessaIntegrazionePrimaRata = false;

                    if (tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() || tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode())
                    {
                        // Process pagamentiEffettuati
                        foreach (Pagamento pagamento in studente.InformazioniPagamento.PagamentiEffettuati)
                        {
                            string lastValue = pagamento.codTipoPagam[2..];
                            if (lastValue == "P0")
                            {
                                hasPrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    primaRataStorno = true;
                                }
                                continue;
                            }
                            if (lastValue == "S0")
                            {
                                hasSaldo = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    saldoStorno = true;
                                }
                                continue;
                            }
                            if (lastValue == "P1" || lastValue == "P2")
                            {
                                riemessaPrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    riemessaPrimaRata = false;
                                }
                                continue;
                            }
                            if (lastValue == "S1" || lastValue == "S2")
                            {
                                riemessaSecondaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    riemessaSecondaRata = false;
                                }
                                continue;
                            }
                            if (lastValue == "I0")
                            {
                                integrazionePrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    stornoIntegrazionePrimaRata = true;
                                }
                                continue;
                            }
                            if (lastValue == "I1" || lastValue == "I2")
                            {
                                riemessaIntegrazionePrimaRata = true;
                                if (pagamento.ritiratoAzienda)
                                {
                                    riemessaIntegrazionePrimaRata = false;
                                }
                            }
                        }

                        // Check conditions and possibly remove student
                        if (!studenteForzato && categoriaPagam == "SA" && hasPrimaRata && primaRataStorno && !riemessaPrimaRata)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Prima rata non riemessa"));
                            return;
                        }

                        if (!studenteForzato && categoriaPagam == "SA" && integrazionePrimaRata && stornoIntegrazionePrimaRata && !riemessaIntegrazionePrimaRata)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Integrazione prima rata non riemessa"));
                            return;
                        }

                        // Process assegnazioni
                        if (!isTR && studente.InformazioniPagamento.Assegnazioni != null && studente.InformazioniPagamento.Assegnazioni.Count > 0)
                        {
                            foreach (Assegnazione assegnazione in studente.InformazioniPagamento.Assegnazioni)
                            {
                                importoPA += Math.Max(assegnazione.costoTotale, 0);
                            }
                        }

                        // Process reversali
                        if (!isTR && studente.InformazioniPagamento.Reversali != null && studente.InformazioniPagamento.Reversali.Count > 0)
                        {
                            foreach (Reversale reversale in studente.InformazioniPagamento.Reversali)
                            {
                                if (reversale.codReversale == "01")
                                {
                                    importoPA -= reversale.importo;
                                    studente.SetImportoAccontoPA(Math.Round(reversale.importo, 2));
                                }

                                if (riemessaPrimaRata && reversale.codReversale == "01")
                                {
                                    importoReversali += reversale.importo;
                                }
                                else if (riemessaSecondaRata && reversale.codReversale == "02")
                                {
                                    importoReversali += reversale.importo;
                                }
                            }
                        }

                        // Process detrazioni
                        if (!isTR && studente.InformazioniPagamento.Detrazioni != null && studente.InformazioniPagamento.Detrazioni.Count > 0)
                        {
                            foreach (Detrazione detrazione in studente.InformazioniPagamento.Detrazioni)
                            {
                                if (detrazione.codReversale == "01" && accontoPA <= 0)
                                {
                                    accontoPA += detrazione.importo;
                                    studente.SetImportoAccontoPA(Math.Round(accontoPA, 2));
                                    importoPA = accontoPA;
                                }
                                else if (detrazione.codReversale != "01")
                                {
                                    importoDetrazioni += detrazione.importo;
                                }
                            }
                        }
                    }

                    // Check if importoPA is negative
                    if (importoPA < 0)
                    {
                        studentiPAnegativo[studente.InformazioniPersonali.CodFiscale] = importoPA;
                    }

                    importoPA = Math.Round(Math.Max(importoPA, 0), 2);
                    studente.SetImportoSaldoPA(importoPA);

                    double importiPagati = isTR ? 0 : studente.InformazioniPagamento.ImportoPagato;

                    // Check if importoMassimo and importoPagato are close
                    if (Math.Abs(importoMassimo - studente.InformazioniPagamento.ImportoPagato) < 5 && !isTR)
                    {
                        studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                        studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Importo da pagare minore di €5"));
                        return;
                    }

                    if(tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() && !isTR &&
                    studente.InformazioniIscrizione.ConfermaSemestreFiltro == 1 && studente.InformazioniIscrizione.AnnoCorso == 1)
                    {
                        if(categoriaPagam == "PR")
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Semestre filtro solo saldo"));
                            return;
                        }
                        if (!studente.InformazioniBeneficio.SuperamentoEsami)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Semestre filtro senza superamento esami"));
                            return;
                        }
                    }

                    // Calculate importoDaPagare based on various conditions
                    if (categoriaPagam == "PR" && (
                        tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() || tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode()
                        ) && !isTR)
                    {
                        bool isAcconto = codTipoPagamento[2..] == "A0" || codTipoPagamento[2..] == "A1" || codTipoPagamento[2..] == "A2";
                        string currentYear = selectedAA[..4];
                        DateTime percentDate = new(int.Parse(currentYear), 11, 10);
                        if (isAcconto && DateTime.Parse(selectedDataRiferimento) <= percentDate && studente.InformazioniIscrizione.AnnoCorso == 1 && (studente.InformazioniIscrizione.TipoCorso == 3 || studente.InformazioniIscrizione.TipoCorso == 4))
                        {
                            importoDaPagare = importoMassimo * 0.2;
                            importoMassimo *= 0.2;
                        }
                        else if (isAcconto && DateTime.Parse(selectedDataRiferimento) <= percentDate)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            return;
                        }
                        else
                        {
                            importoDaPagare = importoMassimo * 0.5;
                            importoMassimo *= 0.5;

                            if (studente.InformazioniBeneficio.HaServizioSanitario && DateTime.Today < new DateTime(int.Parse(currentYear), 12, 27))
                            {
                                importoDaPagare -= 100;
                                importoMassimo -= 100;
                            }
                        }
                    }
                    else if ((tipoBeneficio == TipoBeneficio.BorsaDiStudio.ToCode() || tipoBeneficio == TipoBeneficio.ContributoStraordinario.ToCode()) && !isTR)
                    {
                        importoDaPagare = importoMassimo;
                        if (studente.InformazioniIscrizione.AnnoCorso == 1)
                        {
                            if (!studente.InformazioniBeneficio.SuperamentoEsami && studente.InformazioniBeneficio.SuperamentoEsamiTassaRegionale && !(studente.InformazioniIscrizione.TipoCorso == 6 || studente.InformazioniIscrizione.TipoCorso == 7))
                            {
                                importoDaPagare = importoMassimo * 0.5;
                                importoMassimo *= 0.5;
                            }
                            else if (!studente.InformazioniBeneficio.SuperamentoEsami && !studente.InformazioniBeneficio.SuperamentoEsamiTassaRegionale)
                            {
                                studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                                studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Senza superamento esami"));

                                return;
                            }
                        }
                    }
                    else if (isTR)
                    {
                        importoDaPagare = 140;
                        if (studente.InformazioniPersonali.CodFiscale == debugStudente)
                        {
                            string test = "";
                        }
                        if ((!hasSaldo || (hasSaldo && saldoStorno && !riemessaSecondaRata)) && !studenteForzato)
                        {
                            if (!(studente.InformazioniIscrizione.AnnoCorso == 1 && (studente.InformazioniBeneficio.SuperamentoEsami || studente.InformazioniBeneficio.SuperamentoEsamiTassaRegionale)))
                            {
                                studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                                studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Non ha saldo/saldo non riemesso"));
                                return;
                            }
                        }

                        if (studente.InformazioniPersonali.Disabile || studente.InformazioniPersonali.EsoneroTassaRegionale)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            return;
                        }

                        if (studente.InformazioniIscrizione.TipoCorso == 6 || studente.InformazioniIscrizione.TipoCorso == 7)
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            return;
                        }

                        if (studente.InformazioniIscrizione.AnnoCorso == 1 && !(studente.InformazioniBeneficio.SuperamentoEsami || studente.InformazioniBeneficio.SuperamentoEsamiTassaRegionale))
                        {
                            studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                            return;
                        }
                    }

                    if (Math.Abs(importiPagati - (importoMassimo - importoReversali)) < 5)
                    {
                        studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                        studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Importo da pagare minore di € 5"));
                        return;
                    }

                    studente.SetImportoDaPagareLordo(Math.Round(importoDaPagare - importiPagati - importoReversali, 2));
                    importoDaPagare -= (importiPagati + importoPA + importoDetrazioni + importoReversali);
                    importoDaPagare = Math.Round(importoDaPagare, 2);


                    if ((importoDaPagare == 0 || Math.Abs(importoDaPagare) < 5) && !studentiDaRimuovereDallaTabella.ContainsKey(studente.InformazioniPersonali.CodFiscale))
                    {
                        studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                        studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Importo da pagare minore di € 5"));
                        return;
                    }

                    studente.SetImportoDaPagare(importoDaPagare);

                    if (isRiemissione)
                    {
                        studente.SetImportoDaPagareLordo(importoDaPagare);
                        studente.RemoveAllAssegnazioni();
                    }

                    if (importoDaPagare < 0 && !studentiDaRimuovereDallaTabella.ContainsKey(studente.InformazioniPersonali.CodFiscale))
                    {
                        studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                        studentiRimossiBag.Add((studente.InformazioniPersonali.CodFiscale, "Importo da pagare negativo"));
                        return;
                    }
                });

                Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Calcolo importi - Completato");

                // Remove students with zero or negative importoDaPagare
                foreach (var pair in studentiDaPagare)
                {
                    StudentePagamenti studente = pair.Value;

                    if (studente.InformazioniPagamento.ImportoDaPagare > 0)
                    {
                        continue;
                    }
                    studentiDaRimuovereDallaTabella[studente.InformazioniPersonali.CodFiscale] = true;
                }

                // Remove students from the database
                if (studentiDaRimuovereDallaTabella.Count > 0 && !string.IsNullOrWhiteSpace(selectedVecchioMandato))
                {
                    Logger.LogInfo(55, $"Lavorazione studenti - Rimozione dalla tabella d'appoggio");
                    string createTempTableSql = "CREATE TABLE #TempCodFiscale (cod_fiscale VARCHAR(255) COLLATE Latin1_General_CI_AS);";
                    SqlCommand createTableCommand = new(createTempTableSql, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    createTableCommand.ExecuteNonQuery();

                    DataTable codFiscalesTable = new DataTable();
                    codFiscalesTable.Columns.Add("cod_fiscale", typeof(string));

                    foreach (string codFiscale in studentiDaRimuovereDallaTabella.Keys)
                    {
                        DataRow row = codFiscalesTable.NewRow();
                        row["cod_fiscale"] = codFiscale;
                        codFiscalesTable.Rows.Add(row);

                        studentiPAnegativo.TryRemove(codFiscale, out _);
                    }

                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                    {
                        bulkCopy.DestinationTableName = "#TempCodFiscale";
                        bulkCopy.ColumnMappings.Add("cod_fiscale", "cod_fiscale");
                        bulkCopy.WriteToServer(codFiscalesTable);
                    }

                    string deleteSql = $@"
                        DELETE main
                        FROM {dbTableName} AS main
                        INNER JOIN #TempCodFiscale temp ON main.cod_fiscale = temp.cod_fiscale;
                    ";
                    SqlCommand deleteCommand = new(deleteSql, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    deleteCommand.ExecuteNonQuery();

                    SqlCommand dropTableCommand = new("DROP TABLE #TempCodFiscale;", CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    dropTableCommand.ExecuteNonQuery();

                    Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Rimozione dalla tabella d'appoggio - Completato");
                }

                // Export negative PA students
                if (studentiPAnegativo.Count > 0)
                {
                    DataTable estrazionePAneg = new DataTable();
                    estrazionePAneg.Columns.Add("cod_fiscale", typeof(string));
                    estrazionePAneg.Columns.Add("rimborso", typeof(double));
                    foreach (var studentePair in studentiPAnegativo)
                    {
                        estrazionePAneg.Rows.Add(studentePair.Key, Math.Round(studentePair.Value, 2).ToString("F2"));
                    }
                    Utilities.ExportDataTableToExcel(estrazionePAneg, GetCurrentPagamentoFolder(), fileName: "Studenti PA negativo");
                }

                // Export removed students with reasons
                if (studentiRimossiBag.Count > 0)
                {
                    DataTable studentiRimossi = new DataTable();
                    studentiRimossi.Columns.Add("CodFiscale");
                    studentiRimossi.Columns.Add("Motivazione");

                    foreach (var item in studentiRimossiBag)
                    {
                        studentiRimossi.Rows.Add(item.CodFiscale, item.Motivazione);
                    }

                    Utilities.ExportDataTableToExcel(studentiRimossi, GetCurrentPagamentoFolder(), fileName: "Studenti rimossi con motivi");
                }

                if (studentiPagatiComePendolari.Count > 0)
                {
                    DataTable studentiPagatiPendolari = new DataTable();
                    studentiPagatiPendolari.Columns.Add("CodFiscale");
                    studentiPagatiPendolari.Columns.Add("Motivazione");

                    foreach (var item in studentiPagatiComePendolari)
                    {
                        studentiPagatiPendolari.Rows.Add(item.CodFiscale, item.Motivazione);
                    }

                    Utilities.ExportDataTableToExcel(studentiPagatiPendolari, GetCurrentPagamentoFolder(), fileName: "Studenti fuori sede pagati come pendolari");
                }

                Logger.LogInfo(null, $"Rimossi {studentiDaRimuovereDallaTabella.Count} studenti dal pagamento");

                // Remove students from the main collection
                foreach (string codFiscale in studentiDaRimuovereDallaTabella.Keys)
                {
                    studentiDaPagare.Remove(codFiscale);
                }

                Logger.LogInfo(55, $"UPDATE:Lavorazione studenti - Calcolo importi - Completato");
            }

        }

    }
}
