using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Tesseract;

namespace ProcedureNet7
{
    internal class ControlloPEC : BaseProcedure<ArgsControlloPEC>
    {
        private string? selectedAA;
        private SqlTransaction? sqlTransaction;

        // Cambia questi valori se vuoi un altro codice blocco / sorgente
        private const string BlockCode = "PEC";              // es.: Blocco Identità/Domicilio
        private const string BlockSource = "A4_PEC_Check";

        public ControlloPEC(MasterForm? masterForm, SqlConnection? connectionString)
            : base(masterForm, connectionString) { }

        public override void RunProcedure(ArgsControlloPEC args)
        {
            selectedAA = args._annoAccademico;

            Logger.LogInfo(null, $"Inizio procedura ControlloPEC. Anno accademico di partenza: {selectedAA}.");

            sqlTransaction = CONNECTION.BeginTransaction();

            // DataTable riepilogo (aggiunte/rimozioni blocchi)
            var recapTable = new DataTable("ControlloPEC_Riepilogo");
            recapTable.Columns.Add("CodiceFiscale", typeof(string));
            recapTable.Columns.Add("AnnoAccademico", typeof(string));
            recapTable.Columns.Add("Azione", typeof(string)); // RIMOZIONE_BLOCCO / AGGIUNTA_BLOCCO

            try
            {
                // STEP 0: rimozione blocchi PEC per chi ha domicilio con contratto valido
                Logger.LogInfo(null, "Step 0: rimozione blocchi PEC per studenti con domicilio con contratto (Titolo_oneroso = 1, N_serie_contratto non vuoto).");

                string sqlRemoveBlocks = @"
                    SELECT DISTINCT
                        vd.ANNO_ACCADEMICO,
                        vd.COD_FISCALE
                    FROM vDomicilio vd
                    WHERE 
                        vd.ANNO_ACCADEMICO >= @AnnoDa
                        AND vd.Titolo_oneroso = 1
                        AND ISNULL(vd.N_serie_contratto, '') <> ''
                ";

                var cfsPerAnnoDaSbloccare = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                using (var cmdRemove = new SqlCommand(sqlRemoveBlocks, CONNECTION, sqlTransaction))
                {
                    cmdRemove.Parameters.AddWithValue("@AnnoDa", selectedAA ?? string.Empty);

                    using (var reader = cmdRemove.ExecuteReader())
                    {
                        int rowCounter = 0;

                        while (reader.Read())
                        {
                            rowCounter++;

                            string cf = Utilities.SafeGetString(reader, "COD_FISCALE").Trim().ToUpper();
                            string aa = Utilities.SafeGetString(reader, "ANNO_ACCADEMICO").Trim();

                            if (string.IsNullOrWhiteSpace(cf) || string.IsNullOrWhiteSpace(aa))
                                continue;

                            if (!cfsPerAnnoDaSbloccare.TryGetValue(aa, out var cfList))
                            {
                                cfList = new List<string>();
                                cfsPerAnnoDaSbloccare[aa] = cfList;
                            }

                            if (!cfList.Contains(cf))
                                cfList.Add(cf);
                        }

                        Logger.LogInfo(null, $"Step 0: trovati {rowCounter} record (CF, AA) con contratto valido in vDomicilio.");
                        Logger.LogInfo(null, $"Step 0: anni accademici interessati: {cfsPerAnnoDaSbloccare.Count}.");
                    }
                }

                int totalBlocksRemoved = 0;

                foreach (var kvp in cfsPerAnnoDaSbloccare)
                {
                    string annoAccademico = kvp.Key;
                    List<string> cfList = kvp.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (!cfList.Any())
                        continue;

                    var hasBlockDict = BlocksUtil.HasBlock(
                        CONNECTION,
                        sqlTransaction,
                        cfList,
                        BlockCode,
                        annoAccademico
                    );

                    var cfsToRemove = cfList
                        .Where(cf => hasBlockDict.TryGetValue(cf, out bool has) && has)
                        .ToList();

                    if (!cfsToRemove.Any())
                    {
                        Logger.LogInfo(null,
                            $"Step 0 - AnnoAccademico={annoAccademico}: nessuno degli studenti con contratto aveva il blocco {BlockCode} da rimuovere.");
                        continue;
                    }

                    Logger.LogInfo(null,
                        $"Step 0 - AnnoAccademico={annoAccademico}: rimuovo blocco [{BlockCode}] a {cfsToRemove.Count} studenti (su {cfList.Count} con contratto).");

                    BlocksUtil.RemoveBlock(
                        CONNECTION,
                        sqlTransaction,
                        cfsToRemove,
                        BlockCode,
                        annoAccademico,
                        BlockSource    // usato come 'utente' per tracciatura
                    );

                    totalBlocksRemoved += cfsToRemove.Count;

                    // Riepilogo: rimozione blocco
                    foreach (var cf in cfsToRemove)
                    {
                        var row = recapTable.NewRow();
                        row["CodiceFiscale"] = cf;
                        row["AnnoAccademico"] = annoAccademico;
                        row["Azione"] = "RIMOZIONE_BLOCCO";
                        recapTable.Rows.Add(row);
                    }
                }

                Logger.LogInfo(null, $"Step 0: totale blocchi {BlockCode} rimossi per domicilio con contratto: {totalBlocksRemoved}.");

                // STEP 1: estrazione CF + AA che soddisfano le condizioni per l'INSERIMENTO blocco
                Logger.LogInfo(null, "Step 1: seleziono gli studenti da controllare (CF + AA) per inserimento blocco PEC.");

                string sqlQuery = @"
                    SELECT DISTINCT
                        LRS.ANNO_ACCADEMICO,
                        LRS.COD_FISCALE,
                        vProfilo.Indirizzo_PEC,
                        vb.Cod_tipo_esito
                    FROM LUOGO_REPERIBILITA_STUDENTE LRS
                    LEFT OUTER JOIN vProfilo 
                        ON LRS.COD_FISCALE = vProfilo.Cod_Fiscale 
                    LEFT OUTER JOIN Domini_PEC dp
                        ON UPPER(dp.Dominio) = UPPER(
                            CASE 
                                WHEN vProfilo.Indirizzo_PEC LIKE '%@%'
                                    THEN SUBSTRING(
                                        vProfilo.Indirizzo_PEC,
                                        CHARINDEX('@', vProfilo.Indirizzo_PEC) + 1,
                                        LEN(vProfilo.Indirizzo_PEC) - CHARINDEX('@', vProfilo.Indirizzo_PEC)
                                    )
                            END
                        )
                    INNER JOIN Domanda d 
                        ON LRS.ANNO_ACCADEMICO = d.Anno_accademico 
                       AND LRS.COD_FISCALE   = d.Cod_fiscale 
                       AND d.Tipo_bando      = 'lz'
                    INNER JOIN vEsiti_concorsiBS vb 
                        ON d.Anno_accademico = vb.Anno_accademico 
                       AND d.Num_domanda     = vb.Num_domanda 
                    WHERE 
                        LRS.ANNO_ACCADEMICO >= @AnnoDa
                        AND LRS.tipo_bando   = 'lz' 
                        AND LRS.TIPO_LUOGO   = 'DOL'
                        AND LRS.DATA_FINE_VALIDITA IS NULL
                        AND (LRS.INDIRIZZO = '' OR LRS.INDIRIZZO IN ('ROMA', 'CASSINO', 'FROSINONE'))
                        AND LRS.COD_FISCALE IN (
                            SELECT COD_FISCALE 
                            FROM vResidenza vr 
                            WHERE vr.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO 
                              AND vr.provincia_residenza = 'ee'
                        )
                        AND LRS.COD_FISCALE IN (
                            SELECT COD_FISCALE 
                            FROM vDomicilio vd
                            WHERE vd.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO
                              AND (vd.Indirizzo_domicilio = '' 
                                   OR vd.Indirizzo_domicilio IN ('ROMA','CASSINO','FROSINONE')
                                   OR vd.prov = 'EE')
                        )
                        -- Escludi chi ha un domicilio con contratto valido (Titolo_oneroso = 1 e N_serie_contratto non vuoto)
                        AND NOT EXISTS (
                            SELECT 1
                            FROM vDomicilio vd2
                            WHERE vd2.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO
                              AND vd2.COD_FISCALE     = LRS.COD_FISCALE
                              AND vd2.Titolo_oneroso  = 1
                              AND ISNULL(vd2.N_serie_contratto, '') <> ''
                        )
                        -- PEC mancante O PEC con dominio non valido (non presente in Domini_PEC)
                        AND (vProfilo.Indirizzo_PEC IS NULL OR dp.Dominio IS NULL)
                        -- Considera solo esiti diversi da 0
                        AND vb.Cod_tipo_esito <> 0
                    ORDER BY LRS.COD_FISCALE, LRS.ANNO_ACCADEMICO;
                ";

                Logger.LogInfo(null, $"Eseguo la seguente query per il controllo PEC:\n{sqlQuery}");

                // CF -> lista anni accademici in cui rientra nelle condizioni
                var studAnnoAccademico = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                using (var cmd = new SqlCommand(sqlQuery, CONNECTION, sqlTransaction))
                {
                    cmd.Parameters.AddWithValue("@AnnoDa", selectedAA ?? string.Empty);

                    using (var reader = cmd.ExecuteReader())
                    {
                        int rowCounter = 0;

                        while (reader.Read())
                        {
                            rowCounter++;

                            string cf = Utilities.SafeGetString(reader, "COD_FISCALE").Trim().ToUpper();
                            string aa = Utilities.SafeGetString(reader, "ANNO_ACCADEMICO").Trim();

                            if (string.IsNullOrWhiteSpace(cf) || string.IsNullOrWhiteSpace(aa))
                                continue;

                            if (!studAnnoAccademico.TryGetValue(cf, out var anniList))
                            {
                                anniList = new List<string>();
                                studAnnoAccademico[cf] = anniList;
                            }

                            if (!anniList.Contains(aa))
                                anniList.Add(aa);
                        }

                        Logger.LogInfo(null, $"Trovati {rowCounter} record (CF, AA) che soddisfano le condizioni di ControlloPEC per inserimento blocco.");
                        Logger.LogInfo(null, $"Studenti distinti trovati: {studAnnoAccademico.Count}.");
                    }
                }

                if (!studAnnoAccademico.Any())
                {
                    Logger.LogInfo(null, "Nessuno studente soddisfa le condizioni per l'inserimento di nuovi blocchi PEC. Procedura ControlloPEC terminata.");
                    sqlTransaction?.Rollback();
                    return;
                }

                // STEP 2: inverti (CF -> [AA]) in (AA -> [CF])
                Logger.LogInfo(null, "Step 2: costruisco la mappatura (AnnoAccademico -> [CF]).");

                var cfsPerAnno = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in studAnnoAccademico)
                {
                    string cf = kvp.Key;
                    List<string> anni = kvp.Value;

                    foreach (var aa in anni)
                    {
                        if (!cfsPerAnno.TryGetValue(aa, out var cfList))
                        {
                            cfList = new List<string>();
                            cfsPerAnno[aa] = cfList;
                        }

                        if (!cfList.Contains(cf))
                            cfList.Add(cf);
                    }
                }

                // STEP 3: per ogni anno accademico aggiungi il blocco, saltando chi ce l'ha già
                Logger.LogInfo(null, "Step 3: aggiungo blocchi per anno accademico, evitando duplicati.");

                var cfsThatWillReceiveBlock = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in cfsPerAnno)
                {
                    string annoAccademico = kvp.Key;
                    List<string> allCfsForYear = kvp.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

                    if (!allCfsForYear.Any())
                        continue;

                    var hasBlockDict = BlocksUtil.HasBlock(
                        CONNECTION,
                        sqlTransaction,
                        allCfsForYear,
                        BlockCode,
                        annoAccademico
                    );

                    var cfsToBlock = allCfsForYear
                        .Where(cf => hasBlockDict.TryGetValue(cf, out bool has) ? !has : true)
                        .ToList();

                    if (!cfsToBlock.Any())
                    {
                        Logger.LogInfo(null,
                            $"AnnoAccademico={annoAccademico}: tutti gli studenti avevano già il blocco {BlockCode} o nessuno da bloccare.");
                        continue;
                    }

                    Logger.LogInfo(null,
                        $"AnnoAccademico={annoAccademico}: aggiungo blocco [{BlockCode}] a {cfsToBlock.Count} studenti (su {allCfsForYear.Count}).");

                    BlocksUtil.AddBlock(
                        CONNECTION,
                        sqlTransaction,
                        cfsToBlock,
                        BlockCode,
                        annoAccademico,
                        BlockSource,
                        true
                    );

                    foreach (var cf in cfsToBlock)
                    {
                        cfsThatWillReceiveBlock.Add(cf);

                        // Riepilogo: aggiunta blocco
                        var row = recapTable.NewRow();
                        row["CodiceFiscale"] = cf;
                        row["AnnoAccademico"] = annoAccademico;
                        row["Azione"] = "AGGIUNTA_BLOCCO";
                        recapTable.Rows.Add(row);
                    }
                }

                Logger.LogInfo(null,
                    $"Totale studenti che riceveranno il blocco {BlockCode}: {cfsThatWillReceiveBlock.Count}.");

                // STEP 4: eventuale messaggio
                if (cfsThatWillReceiveBlock.Any())
                {
                    Logger.LogInfo(null, "Step 4: preparo messaggi di notifica per gli studenti con blocco inserito.");

                    var personalizedMessages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    string messaggioBase =
                        "Gentile studente/ssa, <br>" +
                        "è stato apposto un blocco sulla sua domanda di borsa di studio relativo alla mancata indicazione di un indirizzo " +
                        "PEC valido. <br>" +
                        "Per poter ricevere futuri ed eventuali pagamenti inserisca al più presto il suo indirizzo PEC per rimuovere autonomamente il blocco.<br>" +
                        "Tale operazione è effettuabile dalla sezione Profilo nella sua area riservata.";

                    foreach (var cf in cfsThatWillReceiveBlock)
                    {
                        personalizedMessages[cf] = messaggioBase;
                    }

                    MessageUtils.InsertMessages(
                        CONNECTION,
                        sqlTransaction,
                        personalizedMessages,
                        BlockSource
                    );

                    Logger.LogInfo(null,
                        $"Inseriti {personalizedMessages.Count} messaggi di avviso per gli studenti con blocco {BlockCode}.");
                }
                else
                {
                    Logger.LogInfo(null, "Nessun nuovo blocco inserito => nessun messaggio creato.");
                }

                // STEP 5: conferma / rollback utente + creazione file riepilogo
                Logger.LogInfo(null, "Step 5: richiesta conferma per il commit della procedura ControlloPEC.");

                _ = _masterForm?.Invoke((MethodInvoker)delegate
                {
                    var result = MessageBox.Show(
                        _masterForm,
                        "Confermare l'inserimento/rimozione dei blocchi per il ControlloPEC?",
                        "Conferma procedura ControlloPEC",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.OK)
                    {
                        Logger.LogInfo(100, "Procedura ControlloPEC confermata. Eseguo commit della transazione.");
                        sqlTransaction?.Commit();

                        try
                        {
                            if (recapTable.Rows.Count > 0)
                            {
                                string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                string dateFolder = Path.Combine(documentsPath, "ControlloPEC", DateTime.Now.ToString("yyyyMMdd"));
                                Directory.CreateDirectory(dateFolder);

                                string fileName = $"ControlloPEC_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

                                string fullPath = Utilities.ExportDataTableToExcel(
                                    recapTable,
                                    dateFolder,
                                    includeHeaders: true,
                                    fileName: fileName
                                );

                                Logger.LogInfo(100, $"File di riepilogo ControlloPEC creato: {fullPath}");
                            }
                            else
                            {
                                Logger.LogInfo(100, "Nessun record lavorato: nessun file di riepilogo creato.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(100, $"Errore durante creazione file di riepilogo ControlloPEC: {ex.Message}");
                        }
                    }
                    else
                    {
                        Logger.LogInfo(null, "Procedura ControlloPEC annullata dall'utente. Eseguo rollback.");
                        sqlTransaction?.Rollback();
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(100, $"Errore durante l'esecuzione di ControlloPEC: {ex.Message}");
                sqlTransaction?.Rollback();
            }
        }
    }
}
