using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7
{
    internal class ProceduraControlloISEEUP : BaseProcedure<ArgsControlloISEEUP>
    {
        string selectedAA = string.Empty;
        double sogliaISEE;
        double sogliaISPE;
        bool usaIncongruenze;

        public List<StudenteControlloISEEUP> studentiDaControllare = new();

        public ProceduraControlloISEEUP(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsControlloISEEUP args)
        {
            selectedAA = args._annoAccademico;
            usaIncongruenze = args._usaIncongruenze;

            SqlTransaction sqlTransaction = CONNECTION.BeginTransaction();
            try
            {
                // Step 1: Retrieve threshold values
                string queryData = $"SELECT Soglia_Isee, Soglia_Ispe FROM DatiGenerali_con WHERE anno_accademico = @AnnoAccademico";
                SqlCommand readData = new(queryData, CONNECTION, sqlTransaction);
                readData.Parameters.AddWithValue("@AnnoAccademico", selectedAA);
                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        sogliaISEE = Utilities.SafeGetDouble(reader, "Soglia_Isee");
                        sogliaISPE = Utilities.SafeGetDouble(reader, "Soglia_Ispe");
                    }
                }

                Logger.LogInfo(null, "Elaborazione database ISEEUP");

                // Step 2: Retrieve student data
                string queryEstrazione = $@"
                    SELECT 
                        Domanda.cod_fiscale,
                        Domanda.Num_domanda,
                        Domanda.Id_domanda,
                        CONVERT(MONEY, SommaRedditiProdotti, 0) AS sommaredd,
                        CONVERT(MONEY, SEQU, 0) AS sequ,                        
                        CONVERT(MONEY, SEQUP, 0) AS sequp,
                        CONVERT(MONEY, ISEEU, 0) AS ISEEU,
                        CONVERT(MONEY, ISPEU, 0) AS ISPEU,
                        CONVERT(MONEY, ISEEUP, 0) AS ISEEUP,
                        CONVERT(MONEY, ISPEUP, 0) AS ISPEUP,
                        dbo.SlashIncongruenzeCod(Domanda.Num_domanda, Domanda.Anno_accademico) AS IncongruenzePresenti,
                        dbo.SlashBlocchi(Domanda.Num_domanda, domanda.Anno_accademico,'bs') as blocchiPresenti
                    FROM vISEEUP
                    INNER JOIN Domanda ON vISEEUP.anno_accademico = Domanda.Anno_accademico AND vISEEUP.cod_fiscale = Domanda.Cod_fiscale
                    WHERE Domanda.anno_accademico = @AnnoAccademico AND Domanda.Tipo_bando = 'lz' 
                    ORDER BY Domanda.cod_fiscale";
                SqlCommand readEstrazione = new(queryEstrazione, CONNECTION, sqlTransaction);
                readEstrazione.Parameters.AddWithValue("@AnnoAccademico", selectedAA);
                using (SqlDataReader reader = readEstrazione.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        StudenteControlloISEEUP studente = new();
                        studente.codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale");
                        studente.numDomanda = Utilities.SafeGetString(reader, "Num_domanda");
                        studente.id_domanda = Utilities.SafeGetString(reader, "id_domanda");
                        studente.sommaRedditi = Utilities.SafeGetDouble(reader, "sommaredd");
                        studente.SEQU = Utilities.SafeGetDouble(reader, "SEQU");
                        studente.SEQUP = Utilities.SafeGetDouble(reader, "SEQUP");
                        studente.ISEEU = Utilities.SafeGetDouble(reader, "ISEEU");
                        studente.ISPEU = Utilities.SafeGetDouble(reader, "ISPEU");
                        studente.ISEEUP = Utilities.SafeGetDouble(reader, "ISEEUP");
                        studente.ISPEUP = Utilities.SafeGetDouble(reader, "ISPEUP");

                        string incongruenzeCodesString = Utilities.SafeGetString(reader, "IncongruenzePresenti");
                        List<string> incongruenzeCodes = incongruenzeCodesString.Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        studente.incongruenzePresenti = new List<string>(incongruenzeCodes);

                        studente.incongruenzeDaMettere = new List<string>();
                        studente.incongruenzeDaTogliere = new List<string>();


                        string blocchiCodesString = Utilities.SafeGetString(reader, "blocchiPresenti");
                        List<string> blocchiCodes = blocchiCodesString.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        studente.blocchiPresenti = new List<string>(blocchiCodes);

                        studente.blocchiDaMettere = new List<string>();
                        studente.blocchiDaTogliere = new List<string>();

                        studentiDaControllare.Add(studente);
                    }
                }

                Logger.LogInfo(null, $"Numero di studenti estratti: {studentiDaControllare.Count}");

                Logger.LogInfo(null, "Elaborazione Studenti");

                // Step 3: Process each student to determine inconsistencies to add or remove
                foreach (StudenteControlloISEEUP studente in studentiDaControllare)
                {
                    if (studente.SEQU != 0 || studente.SEQUP != 0)
                    {
                        studente.incongruenzeDaTogliere.Add("28");
                        studente.blocchiDaTogliere.Add("BDR");
                    }

                    // Precedenza ai valori *UP* se esistono (SEQUP > 1)
                    bool usaUP = studente.SEQUP > 1;

                    bool superaISEE = usaUP
                        ? studente.ISEEUP > sogliaISEE
                        : studente.ISEEU > sogliaISEE;

                    bool superaISPE = usaUP
                        ? studente.ISPEUP > sogliaISPE
                        : studente.ISPEU > sogliaISPE;

                    if (superaISEE || superaISPE)
                    {
                        studente.incongruenzeDaMettere.Add("77");
                        studente.blocchiDaMettere.Add("BVE");
                    }
                    else
                    {
                        studente.incongruenzeDaTogliere.Add("77");
                        studente.blocchiDaTogliere.Add("BVE");
                    }


                    if (studente.SEQU == 1 && !(studente.SEQUP > 1))
                    {
                        if (studente.sommaRedditi < 9000)
                        {
                            studente.incongruenzeDaMettere.Add("24");
                            studente.blocchiDaMettere.Add("BSI");
                        }
                        else
                        {
                            studente.incongruenzeDaTogliere.Add("24");
                            studente.blocchiDaTogliere.Add("BSI");
                        }
                    }
                    else
                    {
                        studente.incongruenzeDaTogliere.Add("24");
                        studente.blocchiDaTogliere.Add("BSI");
                    }

                    // Remove inconsistencies that the student does not have
                    studente.incongruenzeDaTogliere = studente.incongruenzeDaTogliere
                        .Where(studente.incongruenzePresenti.Contains)
                        .ToList();

                    // Add inconsistencies that the student does not already have
                    studente.incongruenzeDaMettere = studente.incongruenzeDaMettere
                        .Where(incongruenza => !studente.incongruenzePresenti.Contains(incongruenza))
                        .ToList();

                    // Remove inconsistencies that the student does not have
                    studente.blocchiDaTogliere = studente.blocchiDaTogliere
                        .Where(studente.blocchiPresenti.Contains)
                        .ToList();

                    // Add inconsistencies that the student does not already have
                    studente.blocchiDaMettere = studente.blocchiDaMettere
                        .Where(incongruenza => !studente.blocchiPresenti.Contains(incongruenza))
                        .ToList();
                }

                Logger.LogInfo(null, $"Numero di studenti da elaborare: {studentiDaControllare.Count}");


                if (usaIncongruenze)
                {
                    // Step 4: Prepare bulk operations
                    // Lists to hold Num_domanda and Cod_incongruenza pairs for bulk operations
                    List<BulkIncongruenza> incongruenzeDaTogliereList = new();
                    List<BulkIncongruenza> incongruenzeDaMettereList = new();

                    foreach (StudenteControlloISEEUP studente in studentiDaControllare)
                    {
                        foreach (string codIncongruenza in studente.incongruenzeDaTogliere)
                        {
                            incongruenzeDaTogliereList.Add(new BulkIncongruenza
                            {
                                NumDomanda = studente.numDomanda,
                                CodIncongruenza = codIncongruenza
                            });
                        }

                        foreach (string codIncongruenza in studente.incongruenzeDaMettere)
                        {
                            incongruenzeDaMettereList.Add(new BulkIncongruenza
                            {
                                NumDomanda = studente.numDomanda,
                                id_domanda = studente.id_domanda,
                                CodIncongruenza = codIncongruenza
                            });
                        }
                    }

                    Logger.LogInfo(null, $"Numero totale di incongruenze da togliere: {incongruenzeDaTogliereList.Count}");
                    Logger.LogInfo(null, $"Numero totale di incongruenze da mettere: {incongruenzeDaMettereList.Count}");

                    // Calculate counts for each incongruenza code to be removed
                    var incongruenzeDaTogliereCounts = incongruenzeDaTogliereList
                        .GroupBy(i => i.CodIncongruenza)
                        .Select(g => new { CodIncongruenza = g.Key, Count = g.Count() })
                        .ToList();

                    // Log counts for incongruenze to be removed
                    Logger.LogInfo(null, "Dettaglio incongruenze da togliere:");
                    foreach (var item in incongruenzeDaTogliereCounts)
                    {
                        Logger.LogInfo(null, $"Incongruenza {item.CodIncongruenza}: {item.Count} occorrenze da togliere");
                    }

                    // Calculate counts for each incongruenza code to be added
                    var incongruenzeDaMettereCounts = incongruenzeDaMettereList
                        .GroupBy(i => i.CodIncongruenza)
                        .Select(g => new { CodIncongruenza = g.Key, Count = g.Count() })
                        .ToList();

                    // Log counts for incongruenze to be added
                    Logger.LogInfo(null, "Dettaglio incongruenze da mettere:");
                    foreach (var item in incongruenzeDaMettereCounts)
                    {
                        Logger.LogInfo(null, $"Incongruenza {item.CodIncongruenza}: {item.Count} occorrenze da mettere");
                    }

                    // Step 5: Perform bulk updates to remove inconsistencies
                    if (incongruenzeDaTogliereList.Any())
                    {
                        // Create a temporary table to hold the data
                        string tempTableTogliere = "#TempIncongruenzeTogliere";
                        string createTempTableTogliere = $@"
                        CREATE TABLE {tempTableTogliere} (
                            NumDomanda VARCHAR(50),
                            CodIncongruenza VARCHAR(10)
                        )";
                        SqlCommand createTempCmdTogliere = new(createTempTableTogliere, CONNECTION, sqlTransaction);
                        createTempCmdTogliere.ExecuteNonQuery();

                        // Bulk insert into temporary table
                        using (SqlBulkCopy bulkCopy = new(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                        {
                            bulkCopy.DestinationTableName = tempTableTogliere;
                            DataTable dtTogliere = new();
                            dtTogliere.Columns.Add("NumDomanda", typeof(string));
                            dtTogliere.Columns.Add("CodIncongruenza", typeof(string));

                            foreach (var item in incongruenzeDaTogliereList)
                            {
                                dtTogliere.Rows.Add(item.NumDomanda, item.CodIncongruenza);
                            }

                            bulkCopy.WriteToServer(dtTogliere);
                        }

                        Logger.LogInfo(null, $"Incongruenze da togliere inserite nella tabella temporanea: {incongruenzeDaTogliereList.Count}");

                        // Update the Incongruenze table using the temporary table
                        string updateIncongruenzeTogliere = $@"
                        UPDATE i
                        SET i.Data_fine_validita = CURRENT_TIMESTAMP
                        FROM [dbo].[Incongruenze] i
                        INNER JOIN {tempTableTogliere} t ON i.Num_domanda = t.NumDomanda AND i.Cod_incongruenza = t.CodIncongruenza
                        WHERE i.Anno_accademico = @AnnoAccademico AND i.Data_fine_validita IS NULL";
                        SqlCommand updateCmdTogliere = new(updateIncongruenzeTogliere, CONNECTION, sqlTransaction);
                        updateCmdTogliere.Parameters.AddWithValue("@AnnoAccademico", selectedAA);
                        int rowsAffectedTogliere = updateCmdTogliere.ExecuteNonQuery();

                        Logger.LogInfo(null, $"Numero di incongruenze aggiornate (togliere): {rowsAffectedTogliere}");

                        // Drop the temporary table
                        string dropTempTableTogliere = $"DROP TABLE {tempTableTogliere}";
                        SqlCommand dropTempCmdTogliere = new(dropTempTableTogliere, CONNECTION, sqlTransaction);
                        dropTempCmdTogliere.ExecuteNonQuery();
                    }
                    else
                    {
                        Logger.LogInfo(null, "Nessuna incongruenza da togliere.");
                    }

                    // Step 6: Perform bulk inserts to add inconsistencies
                    if (incongruenzeDaMettereList.Any())
                    {
                        // Create a temporary table to hold the data
                        string tempTableMettere = "#TempIncongruenzeMettere";
                        string createTempTableMettere = $@"
                        CREATE TABLE {tempTableMettere} (
                            AnnoAccademico VARCHAR(10),
                            NumDomanda VARCHAR(50),
                            CodIncongruenza VARCHAR(10),
                            DataValidita DATETIME,
                            DataFineValidita DATETIME,
                            CodForzatura VARCHAR(10),
                            Utente VARCHAR(50),
                            idDomanda VARCHAR(10)
                        )";
                        SqlCommand createTempCmdMettere = new(createTempTableMettere, CONNECTION, sqlTransaction);
                        createTempCmdMettere.ExecuteNonQuery();

                        // Bulk insert into temporary table
                        using (SqlBulkCopy bulkCopy = new(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction))
                        {
                            bulkCopy.DestinationTableName = tempTableMettere;
                            DataTable dtMettere = new();
                            dtMettere.Columns.Add("AnnoAccademico", typeof(string));
                            dtMettere.Columns.Add("NumDomanda", typeof(string));
                            dtMettere.Columns.Add("CodIncongruenza", typeof(string));
                            dtMettere.Columns.Add("DataValidita", typeof(DateTime));
                            dtMettere.Columns.Add("DataFineValidita", typeof(DateTime));
                            dtMettere.Columns.Add("CodForzatura", typeof(string));
                            dtMettere.Columns.Add("Utente", typeof(string));
                            dtMettere.Columns.Add("IdDomanda", typeof(string));

                            foreach (var item in incongruenzeDaMettereList)
                            {
                                dtMettere.Rows.Add(
                                    selectedAA,
                                    item.NumDomanda,
                                    item.CodIncongruenza,
                                    DateTime.Now,
                                    DBNull.Value,
                                    DBNull.Value,
                                    "Verif_ISEEUP",
                                    item.id_domanda
                                );
                            }

                            bulkCopy.WriteToServer(dtMettere);
                        }

                        Logger.LogInfo(null, $"Incongruenze da mettere inserite nella tabella temporanea: {incongruenzeDaMettereList.Count}");

                        // Insert into Incongruenze table from the temporary table
                        string insertIncongruenzeMettere = $@"
                        INSERT INTO [dbo].[Incongruenze]
                        ([Anno_accademico], [Num_domanda], [Cod_incongruenza], [Data_validita], [Data_fine_validita], [Cod_forzatura], [Utente], [id_domanda])
                        SELECT 
                            AnnoAccademico, NumDomanda, CodIncongruenza, DataValidita, DataFineValidita, CodForzatura, Utente, IdDomanda
                        FROM {tempTableMettere}";
                        SqlCommand insertCmdMettere = new(insertIncongruenzeMettere, CONNECTION, sqlTransaction);
                        int rowsInsertedMettere = insertCmdMettere.ExecuteNonQuery();

                        Logger.LogInfo(null, $"Numero di incongruenze inserite (mettere): {rowsInsertedMettere}");

                        // Drop the temporary table
                        string dropTempTableMettere = $"DROP TABLE {tempTableMettere}";
                        SqlCommand dropTempCmdMettere = new(dropTempTableMettere, CONNECTION, sqlTransaction);
                        dropTempCmdMettere.ExecuteNonQuery();
                    }
                    else
                    {
                        Logger.LogInfo(null, "Nessuna incongruenza da mettere.");
                    }
                }
                else
                {
                    // Step 4: Process blocks when usaIncongruenze is false

                    // Collect blocks to add and remove
                    Dictionary<string, List<string>> blocksToAdd = new Dictionary<string, List<string>>();
                    Dictionary<string, List<string>> blocksToRemove = new Dictionary<string, List<string>>();

                    foreach (StudenteControlloISEEUP studente in studentiDaControllare)
                    {
                        // Blocks to add
                        foreach (string blockCode in studente.blocchiDaMettere)
                        {
                            if (!blocksToAdd.ContainsKey(blockCode))
                            {
                                blocksToAdd[blockCode] = new List<string>();
                            }
                            blocksToAdd[blockCode].Add(studente.codFiscale);
                        }

                        // Blocks to remove
                        foreach (string blockCode in studente.blocchiDaTogliere)
                        {
                            if (!blocksToRemove.ContainsKey(blockCode))
                            {
                                blocksToRemove[blockCode] = new List<string>();
                            }
                            blocksToRemove[blockCode].Add(studente.codFiscale);
                        }
                    }

                    // Log the number of blocks to add and remove
                    int totalBlocksToAdd = blocksToAdd.Sum(kvp => kvp.Value.Count);
                    int totalBlocksToRemove = blocksToRemove.Sum(kvp => kvp.Value.Count);
                    Logger.LogInfo(null, $"Numero totale di blocchi da mettere: {totalBlocksToAdd}");
                    Logger.LogInfo(null, $"Numero totale di blocchi da togliere: {totalBlocksToRemove}");

                    // For each block code, remove blocks using BlocksUtils.RemoveBlock
                    foreach (var kvp in blocksToRemove)
                    {
                        string blockCode = kvp.Key;
                        List<string> codFiscaliList = kvp.Value;

                        Logger.LogInfo(null, $"Removing block {blockCode} from {codFiscaliList.Count} students");

                        // Use BlocksUtils.RemoveBlock
                        BlocksUtil.RemoveBlock(CONNECTION, sqlTransaction, codFiscaliList, blockCode, selectedAA, "Verif_ISEEUP");
                    }

                    // For each block code, add blocks using BlocksUtils.AddBlock
                    foreach (var kvp in blocksToAdd)
                    {
                        string blockCode = kvp.Key;
                        List<string> codFiscaliList = kvp.Value;

                        Logger.LogInfo(null, $"Adding block {blockCode} to {codFiscaliList.Count} students");

                        // Use BlocksUtils.AddBlock
                        BlocksUtil.AddBlock(CONNECTION, sqlTransaction, codFiscaliList, blockCode, selectedAA, "Verif_ISEEUP", false);
                    }
                }

                _ = _masterForm.Invoke((MethodInvoker)delegate
                {
                    DialogResult result = MessageBox.Show(
                        _masterForm,
                        "Completare procedura?",
                        "Attenzione",
                        MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Question
                    );

                    if (result == DialogResult.OK)
                    {
                        sqlTransaction?.Commit();
                    }
                    else
                    {
                        Logger.LogInfo(null, "Procedura interrotta dall'utente.");
                        sqlTransaction?.Rollback();
                    }
                });

                Logger.LogInfo(100, "Fine lavorazione");
            }
            catch (Exception ex)
            {
                // Rollback the transaction if any error occurs
                sqlTransaction.Rollback();
                Logger.LogError(null, $"An error occurred: {ex.Message}");
                throw; // Re-throw the exception after logging
            }
        }
    }


    public class BulkIncongruenza
    {
        public string NumDomanda { get; set; }
        public string CodIncongruenza { get; set; }
        public string id_domanda { get; set; }
    }

}
