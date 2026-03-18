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
        private void InsertIntoMovimentazioni(List<StudentePagamenti> studentiDaProcessare, string impegno, string? overrideMandatoCodPagam = null)
        {
            List<StudentePagamenti> studentiSenzaImpegno = new();
            foreach (StudentePagamenti studente in studentiDaProcessare)
            {
                if (string.IsNullOrWhiteSpace(studente.InformazioniPagamento.NumeroImpegno) || impegno != studente.InformazioniPagamento.NumeroImpegno)
                {
                    studentiSenzaImpegno.Add(studente);
                }
            }

            _ = studentiDaProcessare.RemoveAll(studentiSenzaImpegno.Contains);

            if (studentiDaProcessare.Count == 0)
            {
                throw new Exception("Nessuno studente con impegno trovato");
            }

            try
            {
                int lastCodiceMovimento = 0;
                int nextCodiceMovimento = 0;
                Logger.LogInfo(80, $"Lavorazione studenti - Inserimento in movimenti contabili");
                Dictionary<int, StudentePagamenti> codMovimentiPerStudente = new();
                string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_GENERALI (ID_CAUSALE_MOVIMENTO_GENERALE, IMPORTO_MOVIMENTO, UTENTE_VALIDAZIONE, DATA_VALIDITA_MOVIMENTO_GENERALE, NOTE_VALIDAZIONE_MOVIMENTO, COD_MANDATO) VALUES ";
                string note = "Inserimento tramite elaborazione file pagamenti";
                string notaNow = note;

                if (studentiDaProcessare.Any())
                {
                    StudentePagamenti firstStudent = studentiDaProcessare.First();
                    if (firstStudent.InformazioniPagamento.PagatoPendolare)
                    {
                        notaNow = "Pagamento effettuato come pendolare";
                    }
                    else
                    {
                        notaNow = note;
                    }

                    string mandatoToUse = firstStudent.InformazioniPagamento.MandatoProvvisorio;
                    if (!string.IsNullOrWhiteSpace(overrideMandatoCodPagam) && !string.IsNullOrWhiteSpace(mandatoToUse))
                    {
                        // sostituisci il prefisso prima del primo '_' con il codice override
                        mandatoToUse = System.Text.RegularExpressions.Regex.Replace(
                                           mandatoToUse,
                                           @"^[^_]+",
                                           overrideMandatoCodPagam);
                    }

                    string importoDaInserire = firstStudent.InformazioniPagamento.ImportoDaPagare.ToString(CultureInfo.InvariantCulture);
                    if(!string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                    {
                        importoDaInserire = "100";
                    }
                    string firstStudentValues = string.Format("('{0}', {1}, '{2}', '{3}', '{4}', '{5}')",
                            string.IsNullOrWhiteSpace(overrideMandatoCodPagam) ? codTipoPagamento : overrideMandatoCodPagam,
                            importoDaInserire,
                            "sa",
                            DateTime.Now.ToString("dd/MM/yyyy"),
                            notaNow,
                            mandatoToUse);
                    string initialInsertQuery = $"{baseSqlInsert} {firstStudentValues};";
                    using (SqlCommand cmd = new(initialInsertQuery, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    })
                    {
                        _ = cmd.ExecuteNonQuery();
                    }
                    string sqlCodMovimento = "SELECT TOP(1) CODICE_MOVIMENTO FROM MOVIMENTI_CONTABILI_GENERALI ORDER BY CODICE_MOVIMENTO DESC";
                    object? result;
                    using (SqlCommand cmdCM = new(sqlCodMovimento, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    })
                    {
                        result = cmdCM.ExecuteScalar() ?? null;
                    }
                    if (result != null)
                    {
                        lastCodiceMovimento = Convert.ToInt32(result);
                        codMovimentiPerStudente.Add(lastCodiceMovimento, firstStudent);
                    }
                    else
                    {
                        throw new Exception("Ultimo codice movimento non trovato");
                    }
                }
                else
                {
                    throw new Exception("Lista studenti da pagare è vuota a questo punto");
                }

                const int batchSize = 1000;
                int numberOfBatches = (int)Math.Ceiling((double)(studentiDaProcessare.Count - 1) / batchSize);
                int currentMovimento = lastCodiceMovimento;
                StringBuilder finalQueryBuilder = new();
                Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili generali");
                for (int batchNumber = 0; batchNumber < numberOfBatches; batchNumber++)
                {
                    nextCodiceMovimento = currentMovimento + 1;
                    StringBuilder queryBuilder = new();
                    _ = queryBuilder.Append(baseSqlInsert);

                    var batch = studentiDaProcessare.Skip(1 + batchNumber * batchSize).Take(batchSize);
                    List<string> valuesList = new();

                    foreach (StudentePagamenti studente in batch)
                    {
                        if (studente.InformazioniPagamento.PagatoPendolare)
                        {
                            notaNow = "Pagamento effettuato come pendolare";
                        }
                        else
                        {
                            notaNow = note;
                        }

                        string mandatoToUse = studente.InformazioniPagamento.MandatoProvvisorio;
                        if (!string.IsNullOrWhiteSpace(overrideMandatoCodPagam) && !string.IsNullOrWhiteSpace(mandatoToUse))
                        {
                            mandatoToUse = System.Text.RegularExpressions.Regex.Replace(
                                               mandatoToUse,
                                               @"^[^_]+",
                                               overrideMandatoCodPagam);
                        }
                        string importoDaInserire = studente.InformazioniPagamento.ImportoDaPagare.ToString(CultureInfo.InvariantCulture);
                        if (!string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                        {
                            importoDaInserire = "100";
                        }
                        string studenteValues = string.Format("('{0}', {1}, '{2}', '{3}', '{4}', '{5}')",
                            string.IsNullOrWhiteSpace(overrideMandatoCodPagam) ? codTipoPagamento : overrideMandatoCodPagam,
                            importoDaInserire,
                            "sa",
                            DateTime.Now.ToString("dd/MM/yyyy"),
                            notaNow,
                            mandatoToUse);

                        valuesList.Add(studenteValues);
                        if (codMovimentiPerStudente.ContainsKey(nextCodiceMovimento))
                        {
                            Logger.LogError(null, $"Codice movimento {nextCodiceMovimento} già presente, studente duplicato? CF: {studente.InformazioniPersonali.CodFiscale}");
                            continue;
                        }
                        codMovimentiPerStudente.Add(nextCodiceMovimento, studente);
                        nextCodiceMovimento++;
                    }

                    currentMovimento = nextCodiceMovimento - 1;

                    _ = queryBuilder.Append(string.Join(",", valuesList));
                    _ = queryBuilder.Append("; ");

                    _ = finalQueryBuilder.Append(queryBuilder);
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili generali - batch n°{batchNumber}");
                }
                string finalQuery = finalQueryBuilder.ToString();
                try
                {
                    if (!string.IsNullOrWhiteSpace(finalQuery))
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                }
                catch
                {
                    throw;
                }

                InsertIntoStatiDelMovimentoContabile(codMovimentiPerStudente);
                InsertIntoMovimentiContabiliElementariPagamenti(codMovimentiPerStudente, overrideMandatoCodPagam);
                InsertIntoMovimentiContabiliElementariDetrazioni(codMovimentiPerStudente, overrideMandatoCodPagam);
                InsertIntoMovimentiContabiliElementariAssegnazioni(codMovimentiPerStudente);
            }
            catch (Exception ex)
            {
                Logger.LogError(100, ex.Message);
                throw;
            }

            void InsertIntoStatiDelMovimentoContabile(Dictionary<int, StudentePagamenti> codMovimentiPerStudente)
            {
                try
                {
                    const int batchSize = 1000;
                    string baseSqlInsert = "INSERT INTO STATI_DEL_MOVIMENTO_CONTABILE (ID_STATO, CODICE_MOVIMENTO, DATA_ASSUNZIONE_DELLO_STATO, UTENTE_STATO) VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;
                    Logger.LogInfo(80, $"Lavorazione studenti - Stati del movimento contabile");
                    foreach (var entry in codMovimentiPerStudente)
                    {
                        int codMovimento = entry.Key;
                        string insertStatement = $"(2, '{codMovimento}', '{DateTime.Now:dd/MM/yyyy}', 'sa')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                        {
                            _ = finalQueryBuilder.Append(baseSqlInsert);
                            _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                            _ = finalQueryBuilder.Append("; ");

                            batchStatements.Clear();
                            currentBatchSize = 0;
                        }
                    }

                    string finalQuery = finalQueryBuilder.ToString();

                    if (string.IsNullOrWhiteSpace(finalQuery))
                    {
                        throw new Exception("STATI_DEL_MOVIMENTO_CONTABILE senza contenuti.");
                    }
                    try
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                    catch
                    {
                        throw;
                    }

                    Logger.LogInfo(80, $"Lavorazione studenti - Stati del movimento contabile - completo");
                }
                catch
                {
                    throw;
                }
            }
            void InsertIntoMovimentiContabiliElementariPagamenti(Dictionary<int, StudentePagamenti> codMovimentiPerStudente, string? overrideMandatoCodPagam = null)
            {
                try
                {
                    string codMovimentoElementare = "00";
                    string sqlCodMovimento = $"SELECT DISTINCT Cod_mov_contabile_elem FROM Decod_pagam_new where Cod_tipo_pagam_new = '{codTipoPagamento}'";
                    SqlCommand cmdCM = new(sqlCodMovimento, CONNECTION, sqlTransaction)
                    {
                        CommandTimeout = 9000000
                    };
                    object result = cmdCM.ExecuteScalar();
                    if (result != null)
                    {
                        string? nullableCode = result.ToString();
                        string code;
                        if (nullableCode != null)
                        {
                            code = nullableCode;
                        }
                        else
                        {
                            throw new Exception($"Codice movimento nullo nel database");
                        }
                        codMovimentoElementare = code;
                    }

                    if (!string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                    {
                        codMovimentoElementare = overrideMandatoCodPagam;
                    }

                    const int batchSize = 1000; // Maximum number of rows per INSERT statement
                    string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari");
                    foreach (KeyValuePair<int, StudentePagamenti> entry in codMovimentiPerStudente)
                    {
                        int codMovimento = entry.Key;
                        double importoLordo = entry.Value.InformazioniPagamento.ImportoDaPagareLordo;
                        int segno = 1;

                        foreach (Detrazione detrazione in entry.Value.InformazioniPagamento.Detrazioni)
                        {
                            if (detrazione.codReversale != "01" && string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                            {
                                importoLordo += detrazione.importo;
                            }
                        }
                        if (!string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                        {
                            importoLordo = 100;
                        }

                        string importoFinale = importoLordo.ToString(CultureInfo.InvariantCulture);

                        // Assuming you might need some data from the StudentePagamenti object, you can access it like this: entry.Value
                        string insertStatement = $"('{entry.Value.InformazioniPersonali.CodFiscale}', '{selectedAA}', '{codMovimentoElementare}', '{codMovimento}', '{importoFinale}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || codMovimento == codMovimentiPerStudente.Keys.Last())
                        {
                            _ = finalQueryBuilder.Append(baseSqlInsert);
                            _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                            _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                            batchStatements.Clear(); // Clear the batch for the next round
                            currentBatchSize = 0; // Reset the batch size counter
                        }
                    }

                    string finalQuery = finalQueryBuilder.ToString();
                    if (string.IsNullOrWhiteSpace(finalQuery))
                    {
                        throw new Exception("MOVIMENTI_CONTABILI_ELEMENTARI senza contenuti.");
                    }
                    try
                    {
                        // Execute all accumulated SQL statements at once
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery(); // Execute the query
                    }
                    catch
                    {
                        throw;
                    }

                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - completo");
                }
                catch
                {
                    throw;
                }
            }
            void InsertIntoMovimentiContabiliElementariDetrazioni(Dictionary<int, StudentePagamenti> codMovimentiPerStudente, string? overrideMandatoCodPagam = null)
            {
                try
                {
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Detrazioni");
                    const int batchSize = 1000; // Maximum number of rows per INSERT statement
                    string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;

                    foreach (KeyValuePair<int, StudentePagamenti> entry in codMovimentiPerStudente)
                    {

                        if ((entry.Value.InformazioniPagamento.Detrazioni == null || entry.Value.InformazioniPagamento.Detrazioni.Count <= 0) && string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                        {
                            continue;
                        }
                        int codMovimento = entry.Key;
                        int segno = 0;

                        if (!string.IsNullOrWhiteSpace(overrideMandatoCodPagam))
                        {
                            string codMovimentoOverride = categoriaPagam == "PR" ? "BSN0" : "BSNS";
                            // Assuming you might need some data from the StudentePagamenti object, you can access it like this: entry.Value
                            string insertStatement = $"('{entry.Value.InformazioniPersonali.CodFiscale}', '{selectedAA}', '{codMovimentoOverride}', '{codMovimento}', '100', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                            batchStatements.Add(insertStatement);
                            currentBatchSize++;

                            // Execute batch when reaching batchSize or end of dictionary
                            if (currentBatchSize == batchSize || (codMovimento == codMovimentiPerStudente.Keys.Last()))
                            {
                                _ = finalQueryBuilder.Append(baseSqlInsert);
                                _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                                _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                                batchStatements.Clear(); // Clear the batch for the next round
                                currentBatchSize = 0; // Reset the batch size counter
                            }
                            continue;
                        }
                        foreach (Detrazione detrazione in entry.Value.InformazioniPagamento.Detrazioni)
                        {
                            string importoDaDetrarre = detrazione.importo.ToString(CultureInfo.InvariantCulture);

                            if (detrazione.needUpdate)
                            {
                                string updateStr = $@"UPDATE MOVIMENTI_CONTABILI_ELEMENTARI
                                    SET CODICE_MOVIMENTO = '{codMovimento}'
                                    ,STATO = 2
                                    WHERE ID_CAUSALE = '{detrazione.codReversale}'
                                    AND ANNO_ACCADEMICO = '{selectedAA}'
                                    AND CODICE_FISCALE = '{entry.Value.InformazioniPersonali.CodFiscale}'";
                                using SqlCommand cmd = new(updateStr, CONNECTION, sqlTransaction)
                                {
                                    CommandTimeout = 9000000
                                };
                                _ = cmd.ExecuteNonQuery();
                                continue;
                            }

                            // Assuming you might need some data from the StudentePagamenti object, you can access it like this: entry.Value
                            string insertStatement = $"('{entry.Value.InformazioniPersonali.CodFiscale}', '{selectedAA}', '01', '{codMovimento}', '{importoDaDetrarre}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                            batchStatements.Add(insertStatement);
                            currentBatchSize++;

                            // Execute batch when reaching batchSize or end of dictionary
                            if (currentBatchSize == batchSize || (codMovimento == codMovimentiPerStudente.Keys.Last() && detrazione == entry.Value.InformazioniPagamento.Detrazioni.Last()))
                            {
                                _ = finalQueryBuilder.Append(baseSqlInsert);
                                _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                                _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                                batchStatements.Clear(); // Clear the batch for the next round
                                currentBatchSize = 0; // Reset the batch size counter
                            }
                        }
                    }

                    // Execute any remaining statements that didn't fill a complete batch
                    if (batchStatements.Count > 0)
                    {
                        _ = finalQueryBuilder.Append(baseSqlInsert);
                        _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                        _ = finalQueryBuilder.Append("; ");
                    }

                    string finalQuery = finalQueryBuilder.ToString();
                    if (!string.IsNullOrWhiteSpace(finalQuery))
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Detrazioni - Completo");
                }
                catch
                {
                    throw;
                }
            }
            void InsertIntoMovimentiContabiliElementariAssegnazioni(Dictionary<int, StudentePagamenti> codMovimentiPerStudente)
            {
                try
                {
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Assegnazioni");
                    const int batchSize = 1000; // Maximum number of rows per INSERT statement
                    string baseSqlInsert = "INSERT INTO MOVIMENTI_CONTABILI_ELEMENTARI (CODICE_FISCALE, ANNO_ACCADEMICO, ID_CAUSALE, CODICE_MOVIMENTO, IMPORTO, SEGNO, ANNO_ESERCIZIO_FINANZIARIO, STATO,DATA_INSERIMENTO, UTENTE_MOVIMENTO, NOTE_MOVIMENTO_ELEMENTARE, NUMERO_REVERSALE)  VALUES ";
                    StringBuilder finalQueryBuilder = new();

                    List<string> batchStatements = new();
                    int currentBatchSize = 0;

                    foreach (KeyValuePair<int, StudentePagamenti> entry in codMovimentiPerStudente)
                    {

                        if (entry.Value.InformazioniPagamento.Assegnazioni == null || entry.Value.InformazioniPagamento.Assegnazioni.Count <= 0)
                        {
                            continue;
                        }

                        double costoPA = 0;
                        foreach (Assegnazione assegnazione in entry.Value.InformazioniPagamento.Assegnazioni)
                        {
                            if (assegnazione.costoTotale <= 0)
                            {
                                continue;
                            }
                            costoPA += assegnazione.costoTotale;
                        }
                        costoPA = Math.Round(costoPA - entry.Value.InformazioniPagamento.ImportoAccontoPA, 2);
                        int codMovimento = entry.Key;

                        string costoPostoAlloggio = costoPA.ToString(CultureInfo.InvariantCulture);
                        int segno = 0;

                        // Assuming you might need some data from the StudentePagamenti object, you can access it like this: entry.Value
                        string insertStatement = $"('{entry.Value.InformazioniPersonali.CodFiscale}', '{selectedAA}', '02', '{codMovimento}', '{costoPostoAlloggio}', '{segno}', '{DateTime.Now:yyyy}','2', '{DateTime.Now:dd/MM/yyyy}', 'sa', '', '')";

                        batchStatements.Add(insertStatement);
                        currentBatchSize++;

                        // Execute batch when reaching batchSize or end of dictionary
                        if (currentBatchSize == batchSize || (codMovimento == codMovimentiPerStudente.Keys.Last()))
                        {
                            _ = finalQueryBuilder.Append(baseSqlInsert);
                            _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                            _ = finalQueryBuilder.Append("; "); // End the SQL statement with a semicolon and a space for separation

                            batchStatements.Clear(); // Clear the batch for the next round
                            currentBatchSize = 0; // Reset the batch size counter
                        }
                    }


                    // Execute any remaining statements that didn't fill a complete batch
                    if (batchStatements.Count > 0)
                    {
                        _ = finalQueryBuilder.Append(baseSqlInsert);
                        _ = finalQueryBuilder.Append(string.Join(",", batchStatements));
                        _ = finalQueryBuilder.Append("; ");
                    }

                    string finalQuery = finalQueryBuilder.ToString();
                    if (!string.IsNullOrWhiteSpace(finalQuery))
                    {
                        using SqlCommand cmd = new(finalQuery, CONNECTION, sqlTransaction)
                        {
                            CommandTimeout = 9000000
                        };
                        _ = cmd.ExecuteNonQuery();
                    }
                    Logger.LogInfo(80, $"Lavorazione studenti - Movimenti contabili elementari - Assegnazioni - Completo");
                }
                catch
                {
                    throw;
                }
            }
        }

        #region Determina
        private DeterminaDati BuildDeterminaDatiFromAccumulator(
                    DeterminaAccumulator acc,
                    string pagamentoDescrizione,
                    IEnumerable<string> impegnoList,
                    string aa,
                    string tipoBeneficio,
                    string categoriaPagam,
                    string tipoStudente,
                    string selectedDataRiferimento,
                    string? overrideTipoFondo // "DISCO" or "PNRR"
                )
        {
            var tabella = acc.ToDataTable();

            var (numeroStudenti, importoTotaleLordo, importoTotalePA, importoTotaleNetto) = acc.TotalsAll();
            importoTotale = importoTotaleLordo;
            string tipoIscrizioneTxt = tipoStudente switch
            {
                "0" => "Solo Matricole",
                "1" => "Solo Anni Successivi",
                "2" => "Matricole e Anni Successivi",
                _ => "Tutti"
            };

            string tipoFondo = overrideTipoFondo ?? categoriaPagam;

            var impegniHash = new HashSet<string>(impegnoList ?? Enumerable.Empty<string>());
            string esercizio = TryGetUniqueEsercizioPerImpegni(impegniHash) ?? DateTime.Now.ToString("yyyy");
            string listaImpegniConEsercizio = string.Join(Environment.NewLine,
                impegniHash.OrderBy(x => x).Select(i => $"Esercizio {esercizio} - Impegno {i}"));

            string tipoBando = MapTipoBando(tipoBeneficio);
            List<string> determinazioni = LoadRichiami(aa, tipoBando, tipoFondo);
            List<string> visti = LoadVisti(aa, tipoBando);
            string vistiTesto = visti.Count > 0 ? string.Join(Environment.NewLine, visti)
                                                : $"Vista la data del {selectedDataRiferimento}";

            (string? cap, string? desc) = TryGetCapitolo(tipoFondo);
            string tipoFondoCompleto = !string.IsNullOrWhiteSpace(cap)
                ? $"{tipoFondo} — Cap. {cap}: {desc}"
                : $"{tipoFondo} - {pagamentoDescrizione}";

            return new DeterminaDati
            {
                TipoPagamento = pagamentoDescrizione,
                TipoBeneficio = tipoBeneficio,
                NumeroStudenti = numeroStudenti.ToString(),
                AnnoAccademico = aa.Insert(4, "/"),
                ImportoDaPagare = importoTotaleLordo.ToString("N2", new System.Globalization.CultureInfo("it-IT")),
                TipoIscrizione = tipoIscrizioneTxt,
                TipoFondo = tipoFondo,
                TipoFondoCompleto = tipoFondoCompleto,
                VistoEstratto = vistiTesto,
                DeterminazioniEstratte = determinazioni,
                ListaImpegniConEsercizio = listaImpegniConEsercizio,
                EsercizioFinanziario = esercizio,
                TabellaRiepilogo = tabella
            };
        }

        private string? TryGetUniqueEsercizioPerImpegni(HashSet<string> impegni)
        {
            if (impegni == null || impegni.Count == 0) return null;

            try
            {
                // Se la colonna 'esercizio' non esiste, questa query fallirà: fallback gestito dal catch.
                string sql = $@"
            SELECT DISTINCT esercizio
            FROM Impegni
            WHERE anno_accademico = @aa
              AND categoria_pagamento = @cat
              AND num_impegno IN ({string.Join(",", impegni.Select((_, i) => "@p" + i))})
        ";

                using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction) { CommandTimeout = 120000 };
                cmd.Parameters.AddWithValue("@aa", selectedAA);
                cmd.Parameters.AddWithValue("@cat", categoriaPagam);
                int idx = 0;
                foreach (var imp in impegni)
                    cmd.Parameters.AddWithValue("@p" + (idx++), imp);

                using var rdr = cmd.ExecuteReader();
                var vals = new List<string>();
                while (rdr.Read())
                {
                    if (rdr[0] != DBNull.Value) vals.Add(rdr[0].ToString()!);
                }
                rdr.Close();

                if (vals.Count == 1) return vals[0];
                if (vals.Count > 1) return string.Join("/", vals.Distinct());
                return null;
            }
            catch
            {
                // Tabella o colonna non disponibile: lascia null, verrà fatto fallback al corrente
                return null;
            }
        }
        //private readonly DeterminaAccumulator _detAccDisco = new();
        //private readonly DeterminaAccumulator _detAccPnrr = new();


        private static string NormalizeFondo(string? f)
        {
            return string.IsNullOrWhiteSpace(f) ? "DISCO" : f.Trim().ToUpperInvariant();
        }

        private string ResolveTipoFondoForImpegno(string impegno)
        {
            try
            {
                using var cmd = new SqlCommand(@"
            SELECT TOP 1 tipo_fondo
            FROM Impegni
            WHERE anno_accademico = @aa
              AND categoria_pagamento = @cat
              AND num_impegno = @imp
        ", CONNECTION, sqlTransaction) { CommandTimeout = 120000 };

                cmd.Parameters.AddWithValue("@aa", selectedAA);
                cmd.Parameters.AddWithValue("@cat", categoriaPagam);
                cmd.Parameters.AddWithValue("@imp", impegno);

                var v = cmd.ExecuteScalar() as string;
                return NormalizeFondo(v);
            }
            catch
            {
                return "DISCO";
            }
        }

        private List<string> LoadRichiami(string aa, string tipoBando, string? tipoFondo)
        {
            var result = new List<string>();
            string sql = @"
        SELECT testo_richiamo_determina
        FROM Autocompilazione_Richiami_determine_liquidazione
        WHERE Anno_accademico = @aa
          AND tipo_bando = @tb
          AND (@tf IS NULL OR tipo_fondo IS NULL OR tipo_fondo = @tf)
        ORDER BY indice_ordinamento
    ";

            using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction) { CommandTimeout = 120000 };
            cmd.Parameters.AddWithValue("@aa", aa);
            cmd.Parameters.AddWithValue("@tb", tipoBando);
            cmd.Parameters.AddWithValue("@tf", (object?)tipoFondo ?? DBNull.Value);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                if (rdr[0] != DBNull.Value)
                {
                    var text = rdr[0].ToString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text!);
                }
            }
            return result;
        }

        private List<string> LoadVisti(string aa, string tipoBando)
        {
            var result = new List<string>();
            string sql = @"
        SELECT testo_visto_determina
        FROM Autocompilazione_Visti_determine_liquidazione
        WHERE Anno_accademico = @aa AND tipo_bando = @tb
        ORDER BY indice_ordinamento
    ";

            using var cmd = new SqlCommand(sql, CONNECTION, sqlTransaction) { CommandTimeout = 120000 };
            cmd.Parameters.AddWithValue("@aa", aa);
            cmd.Parameters.AddWithValue("@tb", tipoBando);

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                if (rdr[0] != DBNull.Value)
                {
                    var text = rdr[0].ToString();
                    if (!string.IsNullOrWhiteSpace(text)) result.Add(text!);
                }
            }
            return result;
        }

        private (string? capitolo, string? descrizione) TryGetCapitolo(string? tipoFondo)
        {
            if (string.IsNullOrWhiteSpace(tipoFondo)) return (null, null);

            try
            {
                using var cmd = new SqlCommand(@"
            SELECT TOP 1 Capitolo, Descrizione_capitolo
            FROM Capitoli
            WHERE Tipo_fondo = @tf
        ", CONNECTION, sqlTransaction) { CommandTimeout = 120000 };
                cmd.Parameters.AddWithValue("@tf", tipoFondo);

                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    string? cap = rdr["Capitolo"] as string;
                    string? desc = rdr["Descrizione_capitolo"] as string;
                    return (cap, desc);
                }
            }
            catch
            {
                // ignore
            }

            return (null, null);
        }

        private string MapTipoBando(string tipoBeneficioCode)
        {
            if (string.IsNullOrWhiteSpace(tipoBeneficioCode)) return "LZ";
            tipoBeneficioCode = tipoBeneficioCode.ToUpperInvariant();
            if (tipoBeneficioCode.StartsWith("BS")) return "LZ";
            if (tipoBeneficioCode.StartsWith("CS")) return "CS";
            if (tipoBeneficioCode.StartsWith("PL")) return "PL";
            if (tipoBeneficioCode.StartsWith("BL")) return "BL";
            return "LZ";
        }
        #endregion
    }
}
