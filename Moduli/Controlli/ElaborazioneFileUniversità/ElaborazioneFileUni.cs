using System.Data;
using System.Data.SqlClient;

namespace ProcedureNet7
{
    public class ElaborazioneFileUni : BaseProcedure<ArgsElaborazioneFileUni>
    {
        string folderPath = string.Empty;

        bool checkCondizione;
        bool checkStem;
        bool checkTassaRegionale;
        bool checkTitoloAcquisito;

        List<StudenteElaborazione> studentiRimossi = new();

        public ElaborazioneFileUni(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsElaborazioneFileUni args)
        {
            folderPath = args._selectedUniFolder;
            var excelFiles = Directory.GetFiles(folderPath, "*.xlsx")
                                .Where(filePath => !Path.GetFileName(filePath).Contains('~'));
            foreach (var filePath in excelFiles)
            {
                string nameAndDate = Path.GetFileNameWithoutExtension(filePath);
                string[] parts = nameAndDate.Split('-');
                string uniName = parts[0];
                DataTable initialStudentData = Utilities.ReadExcelToDataTable(filePath);
                List<StudenteElaborazione> studenteElaborazioneList = new List<StudenteElaborazione>();
                if (initialStudentData.Rows.Count > 0)
                {

                    foreach (DataRow row in initialStudentData.AsEnumerable())
                    {
                        StudenteElaborazione studente = new();

                        if (initialStudentData.Columns.Contains("COD_FISCALE"))
                        {
                            string codFiscale = row["COD_FISCALE"].ToString().ToUpper();
                            if (codFiscale.Length != 16)
                            {
                                if (studente.colErroriElaborazione == null)
                                {
                                    studente.colErroriElaborazione = new();
                                }
                                studente.colErroriElaborazione.Add("COD_FISCALE");
                            }
                            studente.codFiscale = codFiscale;
                        }

                        if (initialStudentData.Columns.Contains("TIPO_CORSO_UNI"))
                        {
                            ProcessTipoCorso(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("TIPO_ISCRIZIONE_UNI"))
                        {
                            ProcessTipoIscrizione(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("CONDIZIONE"))
                        {
                            checkCondizione = true;
                            ProcessIscrizioneCondizione(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("DESCR_CORSO_UNI"))
                        {
                            checkStem = true;
                            ProcessDescrizioneCorso(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("ANNO_CORSO_UNI"))
                        {
                            ProcessAnnoCorso(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("ANNO_IMMATRICOLAZIONE_UNI"))
                        {
                            ProcessAnnoImmatricolazione(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("CREDITI_UNI"))
                        {
                            ProcessCrediti(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("CREDITI_CONVALIDATI"))
                        {
                            ProcessCreditiConvalidati(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("TASSA_REGIONALE"))
                        {
                            checkTassaRegionale = true;
                            ProcessTassaRegionale(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("ACQUISIZIONE_TITOLO"))
                        {
                            checkTitoloAcquisito = true;
                            ProcessAcquisizioneTitolo(row, studente, uniName);
                        }
                        if (initialStudentData.Columns.Contains("MATRICOLA"))
                        {
                            studente.matricola = row["MATRICOLA"].ToString().ToUpper();
                        }

                        // Add the fully processed student to the list
                        studenteElaborazioneList.Add(studente);
                    }
                }

                if (studenteElaborazioneList.Count > 0)
                {
                    string test = "";
                }

                // After processing all students in the foreach loop
                var studentiConErrori = studenteElaborazioneList
                    .Where(studente => studente.colErroriElaborazione != null && studente.colErroriElaborazione.Count > 0)
                    .ToList();

                // Clone the structure (columns) of the original DataTable
                DataTable studentiConErroriTable = initialStudentData.Clone();

                // Add rows for students with errors
                foreach (var studente in studentiConErrori)
                {
                    DataRow originalRow = initialStudentData.AsEnumerable().FirstOrDefault(row => row["COD_FISCALE"].ToString().ToUpper() == studente.codFiscale);

                    if (originalRow != null)
                    {
                        // Copy only students with errors
                        DataRow newRow = studentiConErroriTable.NewRow();
                        newRow.ItemArray = originalRow.ItemArray.Clone() as object[]; // Copy the row data
                        studentiConErroriTable.Rows.Add(newRow);
                    }
                }

                if (studentiConErrori.Count > 0)
                {
                    // Pass the filtered data (students with errors) to the UniElaborazioneDati form
                    UniElaborazioneDati form = new UniElaborazioneDati(studentiConErroriTable, _masterForm, studentiConErrori);
                    DialogResult result = form.ShowDialog(); // Open the form modally

                    if (result == DialogResult.OK)
                    {
                        studentiRimossi.AddRange(studenteElaborazioneList.Where(studenteCheck => studenteCheck.daRimuovere));
                        studenteElaborazioneList.RemoveAll(studentiRimossi.Contains);
                    }
                    else
                    {
                        return;
                    }
                }

                #region CREAZIONE CF TABLE
                Logger.LogInfo(30, "Lavorazione studenti");
                List<string> codFiscali = new();
                foreach (StudenteElaborazione studentecf in studenteElaborazioneList)
                {
                    codFiscali.Add(studentecf.codFiscale);
                }

                // Check if the table exists, create if not, otherwise truncate
                Logger.LogDebug(null, "Verifica e creazione/troncamento della tabella CF");

                string checkTableExistsQuery = @"
                    IF OBJECT_ID('tempdb..#CFEstrazione') IS NOT NULL 
                    BEGIN
                        TRUNCATE TABLE #CFEstrazione;
                    END
                    ELSE
                    BEGIN
                        CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));
                    END";

                using (SqlCommand checkTableCmd = new SqlCommand(checkTableExistsQuery, CONNECTION))
                {
                    checkTableCmd.ExecuteNonQuery();
                }

                Logger.LogDebug(null, "Inserimento in tabella CF dei codici fiscali");
                Logger.LogInfo(30, "Lavorazione studenti - creazione tabella codici fiscali");

                // Create a DataTable to hold the fiscal codes
                using (DataTable cfTable = new DataTable())
                {
                    cfTable.Columns.Add("Cod_fiscale", typeof(string));

                    foreach (var cf in codFiscali)
                    {
                        cfTable.Rows.Add(cf);
                    }

                    // Use SqlBulkCopy to efficiently insert the data into the temporary table
                    using SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION);
                    bulkCopy.DestinationTableName = "#CFEstrazione";
                    bulkCopy.WriteToServer(cfTable);
                }

                // Check if the index already exists before creating it
                Logger.LogDebug(null, "Verifica esistenza indice della tabella CF");

                string checkIndexExistsQuery = @"
                    IF NOT EXISTS (
                        SELECT 1 
                        FROM tempdb.sys.indexes 
                        WHERE name = 'idx_Cod_fiscale' 
                        AND object_id = OBJECT_ID('tempdb..#CFEstrazione')
                    )
                    BEGIN
                        CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale);
                    END";

                using (SqlCommand checkIndexCmd = new SqlCommand(checkIndexExistsQuery, CONNECTION))
                {
                    checkIndexCmd.ExecuteNonQuery();
                }

                // Update statistics after ensuring the data and index are there
                Logger.LogDebug(null, "Aggiornamento statistiche della tabella CF");
                string updateStatistics = "UPDATE STATISTICS #CFEstrazione";
                using (SqlCommand updateStatisticsCmd = new SqlCommand(updateStatistics, CONNECTION))
                {
                    updateStatisticsCmd.ExecuteNonQuery();
                }
                #endregion



                string queryData = $@"
                        SELECT        
                            Domanda.Cod_fiscale, 
                            Domanda.Num_domanda,
                            vdom.Invalido, 
                            Studente.sesso,
                            vi.Cod_tipologia_studi, 
                            vi.Anno_corso, 
                            Corsi_laurea.durata_legale,
                            CAST(vm.Numero_crediti AS INT) AS Numero_crediti, 
                            COALESCE(vm.Crediti_extra_curriculari, 0) AS Crediti_extra_curriculari, 
                            COALESCE(vm.Crediti_riconosciuti_da_rinuncia, 0) AS Crediti_riconosciuti_da_rinuncia, 
                            CAST(vi.Crediti_tirocinio AS INT) AS Crediti_tirocinio,
                            Corsi_laurea.Corso_Stem,
                            Corsi_laurea.Descrizione AS Descrizione_corso,
                            dbo.SlashBlocchi(Domanda.Num_domanda, Domanda.Anno_accademico, 'bs') AS cod_blocchi,
                            dbo.SlashDescrBlocchi(Domanda.Num_domanda, Domanda.Anno_accademico, 'bs') AS descr_blocchi,
                            COALESCE(esiti_bs.Cod_tipo_esito, -1) AS Cod_tipo_esito_bs,
                            COALESCE(esiti_pa.Cod_tipo_esito, -1) AS Cod_tipo_esito_pa,
							dbo.SlashIncongruenze(Domanda.Num_domanda, Domanda.Anno_accademico) AS Descr_incongruenze,
							dbo.SlashIncongruenzeCod(Domanda.Num_domanda, Domanda.Anno_accademico) AS Cod_incongruenze
                        FROM            
                            Domanda
                        INNER JOIN #CFEstrazione cfe on domanda.cod_fiscale = cfe.cod_fiscale
                        INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale
                        INNER JOIN vDATIGENERALI_dom AS vdom ON Domanda.Anno_accademico = vdom.Anno_accademico AND Domanda.Num_domanda = vdom.Num_domanda
                        INNER JOIN vIscrizioni AS vi ON Domanda.Anno_accademico = vi.Anno_accademico AND Domanda.Cod_fiscale = vi.Cod_fiscale
                        INNER JOIN vMerito AS vm ON Domanda.Anno_accademico = vm.Anno_accademico AND Domanda.Num_domanda = vm.Num_domanda
                        INNER JOIN
                            Corsi_laurea ON vi.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea 
                            AND vi.Cod_facolta = Corsi_laurea.Cod_facolta 
                            AND vi.Cod_sede_studi = Corsi_laurea.Cod_sede_studi 
                            AND vi.Cod_tipo_ordinamento = Corsi_laurea.Cod_tipo_ordinamento
                        LEFT JOIN
                            vEsiti_concorsi AS esiti_bs ON Domanda.Anno_accademico = esiti_bs.Anno_accademico 
                            AND Domanda.Num_domanda = esiti_bs.Num_domanda 
                            AND esiti_bs.Cod_beneficio = 'BS'
                        LEFT JOIN
                            vEsiti_concorsi AS esiti_pa ON Domanda.Anno_accademico = esiti_pa.Anno_accademico 
                            AND Domanda.Num_domanda = esiti_pa.Num_domanda 
                            AND esiti_pa.Cod_beneficio = 'PA'
                        WHERE        
                            Domanda.Anno_accademico = '20242025' 
                            AND Domanda.Tipo_bando = 'lz' 
                            AND (Corsi_laurea.Anno_accad_fine IS NULL OR Corsi_laurea.Anno_accad_fine = '20242025')

                            ";

                SqlCommand readData = new(queryData, CONNECTION);

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                        // Use LINQ to find the student in studenteElaborazioneList by fiscal code
                        var studente = studenteElaborazioneList.FirstOrDefault(s => s.codFiscale.ToUpper() == codFiscale);

                        if (studente != null)
                        {
                            studente.numDomanda = Utilities.SafeGetString(reader, "Num_domanda");
                            studente.esitoBS = Utilities.SafeGetInt(reader, "Cod_tipo_esito_bs");
                            studente.esitoPA = Utilities.SafeGetInt(reader, "Cod_tipo_esito_pa");

                            // Example: update student properties based on query results
                            studente.tipoCorsoDic = Utilities.SafeGetString(reader, "Cod_tipologia_studi");
                            studente.annoCorsoDic = Utilities.SafeGetInt(reader, "Anno_corso");
                            studente.durataLegaleCorso = Utilities.SafeGetInt(reader, "Durata_legale");
                            studente.creditiConseguitiDic = Utilities.SafeGetInt(reader, "Numero_crediti");
                            studente.creditiExtraCurrDic = Utilities.SafeGetInt(reader, "Crediti_extra_curriculari");
                            studente.creditiDaRinunciaDic = Utilities.SafeGetInt(reader, "Crediti_riconosciuti_da_rinuncia");
                            studente.creditiTirocinioDic = Utilities.SafeGetInt(reader, "Crediti_tirocinio");
                            studente.stemDic = Utilities.SafeGetInt(reader, "Corso_Stem") == 1;
                            studente.sessoDic = Utilities.SafeGetString(reader, "Sesso");
                            studente.disabile = Utilities.SafeGetInt(reader, "Invalido") == 1;
                            studente.descrCorsoDic = Utilities.SafeGetString(reader, "Descrizione_corso");

                            string blockCodesString = Utilities.SafeGetString(reader, "cod_blocchi");
                            string blockDescriptionsString = Utilities.SafeGetString(reader, "descr_blocchi");

                            // Split block codes by "/" and remove empty entries
                            List<string> blockCodes = blockCodesString.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                            // Split block descriptions by "#"
                            List<string> blockDescriptions = blockDescriptionsString.Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                            // Clear existing blocks (if necessary)
                            studente.blocchiPresenti = new Dictionary<string, string>();

                            // Ensure the number of block codes matches the number of descriptions
                            for (int i = 0; i < blockCodes.Count && i < blockDescriptions.Count; i++)
                            {
                                // Add the code-description pair to the dictionary
                                studente.blocchiPresenti[blockCodes[i]] = blockDescriptions[i];
                            }

                            string incongruenzeCodesString = Utilities.SafeGetString(reader, "cod_incongruenze");
                            string incongruenzeDescrString = Utilities.SafeGetString(reader, "Descr_incongruenze");

                            List<string> incongruenzeCodes = incongruenzeCodesString.Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                            List<string> incongruenzeDescr = incongruenzeDescrString.Split(new char[] { '#' }, StringSplitOptions.RemoveEmptyEntries).ToList();

                            studente.incongruenzePresenti = new Dictionary<string, string>();

                            for (int i = 0; i < incongruenzeCodes.Count && i < incongruenzeDescr.Count; i++)
                            {
                                studente.incongruenzePresenti[incongruenzeCodes[i]] = incongruenzeDescr[i];
                            }
                        }
                    }
                }
                if (studenteElaborazioneList.Count > 0)
                {
                    string test = "";
                }

                PopulateSeTipo(studenteElaborazioneList);
                PopulateSeAnno(studenteElaborazioneList);
                PopulateSeCFU(studenteElaborazioneList);
                PopulateCongruenzaAnno(studenteElaborazioneList);

                ProcessStudents(studenteElaborazioneList, uniName);

                PopulateDataInFile(studenteElaborazioneList, filePath, nameAndDate, initialStudentData);
            }

            Logger.LogInfo(100, "Fine elaborazione");

            void ProcessTipoCorso(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = row["TIPO_CORSO_UNI"].ToString().ToUpper();
                if (cellToProcess == "")
                {
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("TIPO_CORSO_UNI");
                    return;
                }

                switch (uniType)
                {
                    case "LUMSA":
                        if (cellToProcess == "L2" || cellToProcess == "TR")
                        {
                            studente.tipoCorsoUni = "3";
                        }
                        else if (cellToProcess == "LM5" || cellToProcess == "CU")
                        {
                            studente.tipoCorsoUni = "4";
                        }
                        else if (cellToProcess == "LM")
                        {
                            studente.tipoCorsoUni = "5";
                        }
                        else if (cellToProcess == "S1")
                        {
                            studente.tipoCorsoUni = "7";
                        }
                        else
                        {
                            //ERRORE
                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_CORSO_UNI");
                        }
                        break;
                    case "SAPIENZA":
                        if (cellToProcess.Contains("TRIENNALE"))
                        {
                            studente.tipoCorsoUni = "3";
                        }
                        else if (cellToProcess.Contains("CICLO UNICO") || cellToProcess.Contains("PERCORSO UNITARIO"))
                        {
                            studente.tipoCorsoUni = "4";
                        }
                        else if (cellToProcess.Contains("BIENNALE"))
                        {
                            studente.tipoCorsoUni = "5";
                        }
                        else if (cellToProcess.Contains("DOTTORATO"))
                        {
                            studente.tipoCorsoUni = "6";
                        }
                        else if (cellToProcess.Contains("SPECIALIZZAZIONE"))
                        {
                            studente.tipoCorsoUni = "7";
                        }
                        else
                        {
                            //ERRORE
                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_CORSO_UNI");
                        }
                        break;
                    case "SANRAF":
                    case "UNIEU":
                    case "CONSCEC":
                    case "LUISS":
                    case "RUFA":
                    case "ABAROMA":
                    case "ABAFROS":
                    case "MERCATORUM":
                    case "UNICAMILLUS":
                    case "ACCDANZA":
                        if (cellToProcess.Contains("TRIENNALE"))
                        {
                            studente.tipoCorsoUni = "3";
                        }
                        else if (cellToProcess.Contains("CICLO UNICO"))
                        {
                            studente.tipoCorsoUni = "4";
                        }
                        else if (cellToProcess.Contains("MAGISTRALE"))
                        {
                            studente.tipoCorsoUni = "5";
                        }
                        else
                        {
                            //ERRORE
                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_CORSO_UNI");
                        }
                        break;
                    case "ROMA3":
                    case "UNICAS":
                    case "UNIVIT":
                        if (cellToProcess == "MT")
                        {
                            studente.tipoCorsoUni = "3";
                        }
                        else if (cellToProcess == "LM")
                        {
                            studente.tipoCorsoUni = "4";
                        }
                        else if (cellToProcess == "MS")
                        {
                            studente.tipoCorsoUni = "5";
                        }
                        else
                        {
                            //ERRORE
                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_CORSO_UNI");
                        }
                        break;
                    case "TORVERGATA":
                        if (cellToProcess.Contains("CICLO UNICO"))
                        {
                            studente.tipoCorsoUni = "4";
                        }
                        else if (cellToProcess.Contains("CORSO DI LAUREA MAGISTRALE"))
                        {
                            studente.tipoCorsoUni = "5";
                        }
                        else if (cellToProcess.Contains("LAUREA MAGISTRALE"))
                        {
                            studente.tipoCorsoUni = "4";
                        }
                        else if (cellToProcess.Contains("CORSO DI LAUREA"))
                        {
                            studente.tipoCorsoUni = "3";
                        }
                        else
                        {
                            //ERRORE
                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_CORSO_UNI");
                        }
                        break;

                }
            }
            void ProcessTipoIscrizione(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = row["TIPO_ISCRIZIONE_UNI"].ToString().ToUpper();

                if (cellToProcess == "")
                {
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("TIPO_ISCRIZIONE_UNI");
                    return;
                }
                switch (uniType)
                {
                    case "LUMSA":
                    case "UNIEU":
                    case "SAPIENZA":
                    case "SANRAF":
                    case "CONSCEC":
                    case "LUISS":
                    case "RUFA":
                    case "UNICAMILLUS":
                    case "ACCDANZA":
                    case "ROMA3":
                    case "UNICAS":
                    case "UNIVIT":
                        if (cellToProcess == "IC" || cellToProcess == "IN CORSO" || cellToProcess == "C" || cellToProcess == "IMMATRICOLAZIONE")
                        {
                            studente.tipoIscrizioneUni = "IC";
                        }
                        else if (cellToProcess == "FC" || cellToProcess == "FUORI CORSO")
                        {
                            studente.tipoIscrizioneUni = "FC";
                        }
                        else if (cellToProcess == "RI" || cellToProcess == "RIPETENTE")
                        {
                            studente.tipoIscrizioneUni = "RI";
                        }
                        else if (cellToProcess == "IN ATTESA")
                        {
                            studente.tipoIscrizioneUni = "NN";
                        }
                        else
                        {
                            //ERRORE

                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_ISCRIZIONE_UNI");
                        }
                        break;

                    case "ABAROMA":
                    case "ABAFROS":
                    case "MERCATORUM":
                        if (cellToProcess == "IC" || cellToProcess == "IN CORSO")
                        {
                            studente.tipoIscrizioneUni = "IC";
                        }
                        else if (cellToProcess.Contains(Utilities.RemoveNonAlphanumeric("FC")) || cellToProcess.Contains(Utilities.RemoveNonAlphanumericAndKeepSpaces("FUORI CORSO")))
                        {
                            studente.tipoIscrizioneUni = "FC";
                        }
                        else
                        {
                            //ERRORE
                            if (studente.colErroriElaborazione == null)
                            {
                                studente.colErroriElaborazione = new();
                            }
                            studente.colErroriElaborazione.Add("TIPO_ISCRIZIONE_UNI");
                        }
                        break;
                }
            }
            void ProcessIscrizioneCondizione(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = row["CONDIZIONE"].ToString().ToUpper();

                if (cellToProcess == "")
                {
                    return;
                }
                switch (uniType)
                {
                    case "LUMSA":
                    case "UNIEU":
                    case "SANRAF":
                    case "CONSCEC":
                    case "LUISS":
                    case "RUFA":
                    case "UNICAMILLUS":
                    case "ACCDANZA":
                    case "ROMA3":
                    case "UNICAS":
                    case "UNIVIT":
                    case "TORVERGATA":
                    case "ABAROMA":
                    case "ABAFROS":
                    case "MERCATORUM":
                        if (cellToProcess == "SI" || cellToProcess == "SÌ" || cellToProcess == "SÍ" || cellToProcess == "VERO" || cellToProcess == "TRUE" || cellToProcess == "OK")
                        {
                            studente.iscrCondizione = true;
                        }
                        break;
                    case "SAPIENZA":
                        if (cellToProcess == "IMMATRICOLATO CON RISERVA")
                        {
                            studente.iscrCondizione = true;
                        }
                        if (cellToProcess.Contains("USCITA") || cellToProcess.Contains("ABBANDONO"))
                        {
                            studente.controlloImmatricolazione = true;
                        }
                        break;
                }
            }
            void ProcessDescrizioneCorso(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = Utilities.RemoveNonAlphanumericAndKeepSpaces(row["DESCR_CORSO_UNI"].ToString().ToUpper());

                if (cellToProcess == "")
                {
                    return;
                }
                switch (uniType)
                {
                    case "LUMSA":
                    case "UNIEU":
                    case "SAPIENZA":
                    case "SANRAF":
                    case "CONSCEC":
                    case "LUISS":
                    case "RUFA":
                    case "UNICAMILLUS":
                    case "ACCDANZA":
                    case "ROMA3":
                    case "UNICAS":
                    case "UNIVIT":
                    case "TORVERGATA":
                    case "ABAROMA":
                    case "ABAFROS":
                    case "MERCATORUM":
                        studente.descrCorsoUni = cellToProcess;
                        break;
                }
            }
            void ProcessAnnoCorso(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string studenteTipoCorso = studente.tipoCorsoUni;
                string studenteTipoIscrizione = studente.tipoIscrizioneUni;
                string cellToProcess = row["ANNO_CORSO_UNI"].ToString().ToUpper();

                try
                {
                    if (uniType == "TORVERGATA")
                    {
                        string cellDurataLegale = row["DURATA_CDS"].ToString();

                        int durataLegaleCorso = int.Parse(cellDurataLegale);
                        int numeroIscrizioniCorso = int.Parse(cellToProcess);

                        if (numeroIscrizioniCorso <= durataLegaleCorso)
                        {
                            studente.annoCorsoUni = numeroIscrizioniCorso;
                            studente.tipoIscrizioneUni = "IC";
                        }
                        else
                        {
                            studente.annoCorsoUni = durataLegaleCorso - numeroIscrizioniCorso;
                            studente.tipoIscrizioneUni = "FC";
                        }
                    }
                    else if (studenteTipoIscrizione == "IC" || studenteTipoIscrizione == "RI" || studenteTipoIscrizione == "NN")
                    {
                        studente.annoCorsoUni = int.Parse(cellToProcess);
                    }
                    else if (studenteTipoIscrizione == "FC")
                    {
                        switch (uniType)
                        {
                            case "LUMSA":
                            case "UNIEU":
                                studente.annoCorsoUni = int.Parse(cellToProcess) * -1;
                                break;
                            case "SAPIENZA":
                            case "ROMA3":
                            case "UNICAS":
                            case "UNIVIT":
                            case "LUISS":
                                string cellAnnoFC = row["ANNO_FC"].ToString();
                                studente.annoCorsoUni = int.Parse(cellAnnoFC) * -1;
                                break;
                            case "SANRAF":
                            case "CONSCEC":
                            case "ACCDANZA":
                            case "UNICAMILLUS":
                                studente.annoCorsoUni = int.Parse(cellToProcess); //COLONNA CON NUMERI IN NEGATIVO MANUALMENTE INSERITI
                                break;
                            case "RUFA":
                            case "ABAROMA":
                            case "ABAFROS":
                                int durataLegaleCorso = 0;
                                switch (studenteTipoCorso)
                                {
                                    case "3":
                                        durataLegaleCorso = 3; break;
                                    case "4":
                                        durataLegaleCorso = 5; break;
                                    case "5":
                                        durataLegaleCorso = 2; break;
                                }
                                studente.annoCorsoUni = durataLegaleCorso - int.Parse(cellToProcess);
                                break;
                            case "MERCATORUM":
                                studente.annoCorsoUni = int.Parse(Utilities.RemoveNonNumeric(cellToProcess)) * -1;
                                break;
                        }
                    }
                    else
                    {
                        throw new();
                    }
                }
                catch
                {
                    studente.annoCorsoUni = 0;
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("ANNO_CORSO_UNI");
                }
            }
            void ProcessAnnoImmatricolazione(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = Utilities.RemoveNonNumeric(row["ANNO_IMMATRICOLAZIONE_UNI"].ToString().ToUpper());
                //if (!AcademicYearProcessor.ProcessAcademicYear(cellToProcess, out string annoAccademicoProcessed))
                //{
                //    if (studente.colErroriElaborazione == null)
                //    {
                //        studente.colErroriElaborazione = new();
                //    }
                //    studente.colErroriElaborazione.Add("ANNO_IMMATRICOLAZIONE_UNI");
                //}
                //studente.aaImmatricolazioneUni = annoAccademicoProcessed;
                try
                {
                    string annoImmatricolazione = string.Empty;
                    switch (uniType)
                    {
                        case "LUMSA":
                        case "ROMA3":
                        case "UNICAS":
                        case "UNIVIT":
                        case "SANRAF":
                        case "TORVERGATA":
                        case "UNIEU":
                        case "CONSCEC":
                        case "LUISS":
                        case "ABAFROS":
                        case "RUFA":
                        case "UNICAMILLUS":
                        case "ACCDANZA":
                            annoImmatricolazione = cellToProcess;
                            break;
                        case "ABAROMA":
                            annoImmatricolazione = "20" + cellToProcess.Substring(0, 2) + "20" + cellToProcess.Substring(2, 2);
                            break;
                        case "SAPIENZA":
                            string annoPrecedente = (int.Parse(cellToProcess) - 1).ToString();
                            annoImmatricolazione = annoPrecedente + cellToProcess;
                            break;
                        case "MERCATORUM":
                            int lenght = cellToProcess.Length;
                            if (lenght == 8)
                            {
                                annoImmatricolazione = cellToProcess;
                            }
                            else if (lenght == 6)
                            {
                                annoImmatricolazione = cellToProcess.Substring(0, 4) + "20" + cellToProcess.Substring(4, 2);
                            }
                            break;
                    }

                    if (annoImmatricolazione.Length != 8)
                    {
                        throw new();
                    }

                    studente.aaImmatricolazioneUni = annoImmatricolazione;
                }
                catch
                {
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("ANNO_IMMATRICOLAZIONE_UNI");
                }
            }
            void ProcessCrediti(DataRow row, StudenteElaborazione studente, string uniType)
            {
                try
                {
                    string cellToProcess = row["CREDITI_UNI"].ToString().ToUpper();

                    if (uniType == "TORVERGATA")
                    {
                        if (string.IsNullOrWhiteSpace(cellToProcess) && studente.annoCorsoUni == 1)
                        {
                            cellToProcess = "0";
                        }
                        cellToProcess = (int.Parse(cellToProcess.Replace(".", "")) * 0.1).ToString();
                    }
                    switch (uniType)
                    {
                        case "LUMSA":
                        case "UNIEU":
                        case "SAPIENZA":
                        case "SANRAF":
                        case "CONSCEC":
                        case "LUISS":
                        case "RUFA":
                        case "UNICAMILLUS":
                        case "ACCDANZA":
                        case "ROMA3":
                        case "UNICAS":
                        case "UNIVIT":
                        case "TORVERGATA":
                        case "ABAROMA":
                        case "ABAFROS":
                        case "MERCATORUM":
                            studente.creditiConseguitiUni = int.Parse(cellToProcess);
                            break;
                    }
                }
                catch
                {
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("CREDITI_UNI");
                }
            }
            void ProcessCreditiConvalidati(DataRow row, StudenteElaborazione studente, string uniType)
            {
                try
                {
                    string cellToProcess = Utilities.RemoveNonNumeric(row["CREDITI_CONVALIDATI"].ToString().ToUpper());
                    if (cellToProcess == "")
                    {
                        return;
                    }

                    if (uniType == "TORVERGATA")
                    {
                        if (string.IsNullOrWhiteSpace(cellToProcess) && studente.annoCorsoUni == 1)
                        {
                            cellToProcess = "0";
                        }
                        cellToProcess = (int.Parse(cellToProcess) * 0.1).ToString();
                    }
                    switch (uniType)
                    {
                        case "LUMSA":
                        case "UNIEU":
                        case "SAPIENZA":
                        case "SANRAF":
                        case "CONSCEC":
                        case "LUISS":
                        case "RUFA":
                        case "UNICAMILLUS":
                        case "ACCDANZA":
                        case "ROMA3":
                        case "UNICAS":
                        case "UNIVIT":
                        case "TORVERGATA":
                        case "ABAROMA":
                        case "ABAFROS":
                        case "MERCATORUM":
                            studente.creditiConvalidatiUni = int.Parse(cellToProcess);
                            break;
                    }
                }
                catch
                {

                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("CREDITI_CONVALIDATI");
                }
            }
            void ProcessTassaRegionale(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = Utilities.RemoveNonAlphanumericAndKeepSpaces(row["TASSA_REGIONALE"].ToString().ToUpper());

                if (cellToProcess == "")
                {
                    return;
                }
                try
                {
                    switch (uniType)
                    {
                        case "LUMSA":
                        case "UNIEU":
                        case "SAPIENZA":
                        case "SANRAF":
                        case "CONSCEC":
                        case "LUISS":
                        case "RUFA":
                        case "UNICAMILLUS":
                        case "ACCDANZA":
                        case "ROMA3":
                        case "UNICAS":
                        case "UNIVIT":
                        case "TORVERGATA":
                        case "ABAROMA":
                        case "ABAFROS":
                        case "MERCATORUM":
                            if (cellToProcess == "S" || cellToProcess == "SI" || cellToProcess == "VERO" || cellToProcess == "TRUE" || cellToProcess == "OK" || IsDate(cellToProcess))
                            {
                                studente.tassaRegionalePagata = true;
                            }
                            break;
                    }
                    bool IsDate(string input)
                    {
                        // Try to parse the input string as a DateTime
                        DateTime date;
                        return DateTime.TryParse(input, out date);
                    }
                }
                catch
                {
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("TASSA_REGIONALE");
                }

            }
            void ProcessAcquisizioneTitolo(DataRow row, StudenteElaborazione studente, string uniType)
            {
                string cellToProcess = Utilities.RemoveNonAlphanumericAndKeepSpaces(row["ACQUISIZIONE_TITOLO"].ToString().ToUpper());

                if (cellToProcess == "")
                {
                    return;
                }
                try
                {
                    switch (uniType)
                    {
                        case "LUMSA":
                        case "UNIEU":
                        case "SAPIENZA":
                        case "SANRAF":
                        case "CONSCEC":
                        case "LUISS":
                        case "RUFA":
                        case "UNICAMILLUS":
                        case "ACCDANZA":
                        case "TORVERGATA":
                        case "ABAROMA":
                        case "ABAFROS":
                        case "MERCATORUM":
                            if (cellToProcess == "S" || cellToProcess == "SI" || cellToProcess == "VERO" || cellToProcess == "TRUE" || cellToProcess == "OK")
                            {
                                studente.titoloAcquisito = true;

                                if (row.Table.Columns.Contains("DESCR_TITOLO") && row["DESCR_TITOLO"] != null && row["DESCR_TITOLO"] != DBNull.Value)
                                {
                                    studente.descrTitoloAcquisito = Utilities.RemoveNonAlphanumericAndKeepSpaces(row["DESCR_TITOLO"].ToString().ToUpper());
                                }
                                else
                                {
                                    studente.descrTitoloAcquisito = string.Empty; // Set to empty if column doesn't exist or value is null
                                }
                            }


                            break;
                        case "ROMA3":
                        case "UNICAS":
                        case "UNIVIT":
                            if (cellToProcess != "")
                            {
                                studente.titoloAcquisito = true;
                                studente.descrTitoloAcquisito = cellToProcess;
                            }
                            break;
                    }
                }
                catch
                {
                    if (studente.colErroriElaborazione == null)
                    {
                        studente.colErroriElaborazione = new();
                    }
                    studente.colErroriElaborazione.Add("ACQUISIZIONE_TITOLO");
                }
            }

            void PopulateSeTipo(List<StudenteElaborazione> studenteElaborazioneList)
            {
                foreach (StudenteElaborazione studente in studenteElaborazioneList)
                {
                    if (
                            studente.tipoCorsoDic == studente.tipoCorsoUni ||
                            (
                                (studente.annoCorsoDic == 1 || studente.annoCorsoDic == 2 || studente.annoCorsoDic == 3) &&
                                (
                                    (studente.tipoCorsoDic == "3" && studente.tipoCorsoUni == "4") ||
                                    (studente.tipoCorsoDic == "4" && studente.tipoCorsoUni == "3")
                                )
                            )
                        )
                    {
                        studente.seTitpo = true;
                    }

                }
            }
            void PopulateSeAnno(List<StudenteElaborazione> studenteElaborazioneList)
            {
                foreach (StudenteElaborazione studente in studenteElaborazioneList)
                {
                    if (studente.annoCorsoDic == studente.annoCorsoUni)
                    {
                        studente.seAnno = true;
                    }
                }
            }
            void PopulateSeCFU(List<StudenteElaborazione> studenteElaborazioneList)
            {
                foreach (StudenteElaborazione studente in studenteElaborazioneList)
                {
                    int creditiUni = studente.creditiConseguitiUni + studente.creditiConvalidatiUni;
                    int creditiDic = studente.creditiConseguitiDic - studente.creditiDaRinunciaDic - studente.creditiTirocinioDic - studente.creditiExtraCurrDic;
                    if (creditiDic <= creditiUni)
                    {
                        studente.seCFU = true;
                    }
                }
            }
            void PopulateCongruenzaAnno(List<StudenteElaborazione> studenteElaborazioneList)
            {
                string currentAcademicYear = "20242025"; // Example: This should be passed dynamically depending on the year you're checking

                foreach (StudenteElaborazione studente in studenteElaborazioneList)
                {
                    // Check if immatriculation year is valid and course year is available
                    if (!string.IsNullOrEmpty(studente.aaImmatricolazioneUni) && studente.annoCorsoUni != 0)
                    {
                        try
                        {
                            // Extract start year and current year
                            int startYearImmatricolazione = int.Parse(studente.aaImmatricolazioneUni.Substring(0, 4)); // e.g., 2020
                            int startYearCurrent = int.Parse(currentAcademicYear.Substring(0, 4)); // e.g., 2024

                            // Calculate the expected course year based on the difference between the current year and immatriculation year
                            int yearsInCourse = startYearCurrent - startYearImmatricolazione + 1; // +1 accounts for the fact they start in the 1st year

                            // Determine the legal duration of the course based on course type
                            int legalDuration = studente.durataLegaleCorso;

                            // Determine expected course year based on whether the student is out of time (fuori corso)
                            int expectedCourseYear;
                            if (yearsInCourse <= legalDuration)
                            {
                                // If within legal duration, the expected course year is positive
                                expectedCourseYear = yearsInCourse;
                            }
                            else
                            {
                                // If beyond legal duration, calculate how many years they are "out of time"
                                expectedCourseYear = (yearsInCourse - legalDuration) * -1; // Negative number for out-of-time years
                            }

                            // Compare with the student's actual university course year
                            if (expectedCourseYear == studente.annoCorsoUni)
                            {
                                studente.congruenzaAnno = true; // The coherence check passes
                            }
                            else
                            {
                                studente.congruenzaAnno = false; // The coherence check fails
                            }
                        }
                        catch
                        {
                            // Handle any errors in parsing or calculations
                            studente.congruenzaAnno = false;
                        }
                    }
                    else
                    {
                        // If data is missing or invalid, set the check to false
                        studente.congruenzaAnno = false;
                    }
                }
            }

            void ProcessStudents(List<StudenteElaborazione> studenteElaborazioneList, string uniType)
            {
                string queryData = $@"
                    SELECT DISTINCT 
	                    Crediti_richiesti.Tipologia_corso, 
	                    Crediti_richiesti.Anno_corso, 
	                    Crediti_richiesti.Cod_corso_laurea, 
	                    Crediti_richiesti.Crediti_richiesti, 
	                    Crediti_richiesti.Disabile
                    FROM            
	                    Crediti_richiesti 
	                    LEFT OUTER JOIN Corsi_laurea ON Crediti_richiesti.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea
                    WHERE        
                    (Crediti_richiesti.Anno_accademico = 20242025) AND 
                    (Corsi_laurea.Anno_accad_fine IS NULL) OR (Crediti_richiesti.Anno_accademico = 20242025) AND (Crediti_richiesti.Cod_corso_laurea IN ('LUISS', 'LUMSA', 'UNINT'))
                    ORDER BY Crediti_richiesti.Cod_corso_laurea, Crediti_richiesti.Tipologia_corso, Crediti_richiesti.Anno_corso
                    ";

                SqlCommand readData = new(queryData, CONNECTION);

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    // Store the reader results in a list or dictionary
                    var creditRecords = new List<CreditRecord>();
                    while (reader.Read())
                    {
                        string tipoCorso = Utilities.SafeGetString(reader, "Tipologia_corso");

                        int annoCorsoCalcolato;
                        if (tipoCorso == "4")
                        {
                            int annoDB = Utilities.SafeGetInt(reader, "Anno_corso");
                            annoCorsoCalcolato = annoDB > 0 ? annoDB : Math.Abs(annoDB) + 6;
                        }
                        else
                        {
                            annoCorsoCalcolato = Utilities.SafeGetInt(reader, "Anno_corso");
                        }

                        var record = new CreditRecord
                        {
                            Disabile = Utilities.SafeGetInt(reader, "Disabile"),
                            TipologiaCorso = tipoCorso,
                            CodCorsoLaurea = Utilities.SafeGetString(reader, "Cod_corso_laurea"),
                            AnnoCorso = annoCorsoCalcolato,
                            CreditiRichiesti = Utilities.SafeGetInt(reader, "Crediti_richiesti")
                        };
                        creditRecords.Add(record);
                    }

                    foreach (StudenteElaborazione studente in studenteElaborazioneList)
                    {
                        foreach (var record in creditRecords)
                        {
                            bool creditiDisabile = record.Disabile == 1;

                            if (creditiDisabile && !studente.disabile)
                            {
                                continue;
                            }

                            if (record.TipologiaCorso != studente.tipoCorsoDic)
                            {
                                continue;
                            }

                            if (
                                (record.CodCorsoLaurea == "0" && (uniType == "LUISS" || uniType == "LUMSA" || uniType == "UNINT")) ||
                                (record.CodCorsoLaurea != "0" && !(uniType == "LUISS" || uniType == "LUMSA" || uniType == "UNINT"))
                            )
                            {
                                continue;
                            }

                            if (studente.tipoCorsoDic == "4" && record.CodCorsoLaurea == "0")
                            {
                                int annoCorsoStudenteCalcolato = studente.annoCorsoDic > 0 ? studente.annoCorsoDic : Math.Abs(studente.annoCorsoDic) + studente.durataLegaleCorso;
                                if (record.AnnoCorso != annoCorsoStudenteCalcolato)
                                {
                                    continue;
                                }
                            }
                            else if (record.AnnoCorso != studente.annoCorsoDic)
                            {
                                continue;
                            }

                            studente.creditiRichiestiDB = record.CreditiRichiesti;
                            break;
                        }
                    }

                }

                foreach (StudenteElaborazione studente in studenteElaborazioneList)
                {
                    studente.blocchiDaTogliere = new();
                    studente.blocchiDaMettere = new();

                    studente.blocchiDaTogliere.Add("BVI");
                    studente.incongruenzeDaTogliere.Add("12");

                    if (studente.seAnno && studente.seTitpo && studente.seCFU)
                    {
                        if (checkTitoloAcquisito)
                        {
                            if (studente.titoloAcquisito)
                            {
                                studente.blocchiDaTogliere.Add("BIS");
                            }
                        }
                        if (studente.creditiConseguitiDic <= studente.creditiConseguitiUni)
                        {
                            studente.blocchiDaTogliere.Add("CMA");
                            studente.blocchiDaTogliere.Add("BET");
                            studente.blocchiDaTogliere.Add("BIT");
                        }
                    }

                    if (studente.seTitpo)
                    {
                        studente.blocchiDaTogliere.Add("ITD");
                        studente.incongruenzeDaTogliere.Add("64");
                    }
                    else
                    {
                        studente.blocchiDaMettere.Add("ITD");
                        studente.incongruenzeDaMettere.Add("64");
                    }

                    if (studente.seAnno)
                    {
                        studente.blocchiDaTogliere.Add("IAD");
                        studente.incongruenzeDaTogliere.Add("63");
                    }
                    else
                    {
                        if (studente.blocchiPresenti.ContainsKey("VAI"))
                        {
                            studente.blocchiDaTogliere.Add("VAI");
                        }
                        studente.blocchiDaMettere.Add("IAD");
                        studente.incongruenzeDaMettere.Add("63");
                    }

                    if (studente.seCFU)
                    {
                        studente.blocchiDaTogliere.Add("BMI");
                        studente.blocchiDaTogliere.Add("IMD");
                        studente.incongruenzeDaTogliere.Add("01");
                        studente.incongruenzeDaTogliere.Add("75");
                        studente.incongruenzeDaTogliere.Add("25");
                    }
                    else
                    {
                        if (studente.creditiConseguitiUni < studente.creditiRichiestiDB)
                        {
                            studente.blocchiDaMettere.Add("BMI");
                            studente.incongruenzeDaMettere.Add("25");
                        }
                        else
                        {
                            studente.blocchiDaMettere.Add("IMD");
                            studente.incongruenzeDaMettere.Add("75");
                        }
                    }

                    if (studente.congruenzaAnno)
                    {
                        studente.blocchiDaTogliere.Add("VAI");
                        studente.incongruenzeDaTogliere.Add("85");
                    }
                    else
                    {
                        if (!studente.blocchiPresenti.ContainsKey("IAD") && !studente.blocchiDaMettere.Contains("IAD"))
                        {
                            studente.blocchiDaMettere.Add("VAI");
                        }
                        studente.incongruenzeDaMettere.Add("85");
                    }

                    if (checkTassaRegionale && !studente.disabile)
                    {
                        if (studente.tassaRegionalePagata)
                        {
                            studente.blocchiDaTogliere.Add("BTR");
                            studente.incongruenzeDaTogliere.Add("81");
                        }
                        else
                        {
                            studente.blocchiDaMettere.Add("BTR");
                            studente.incongruenzeDaMettere.Add("81");
                        }
                    }

                    if (checkCondizione && studente.iscrCondizione)
                    {
                        studente.blocchiDaMettere.Add("IMR");
                        studente.incongruenzeDaMettere.Add("65");
                    }
                    else
                    {
                        studente.blocchiDaTogliere.Add("IMR");
                        studente.incongruenzeDaTogliere.Add("65");
                    }

                    if (studente.creditiDaRinunciaDic == 0 && studente.creditiConvalidatiUni > 0)
                    {
                        studente.blocchiDaMettere.Add("CRC");
                    }

                    if (checkStem && studente.sessoDic == "F" && studente.stemDic)
                    {
                        bool isStem = StringSimilarityChecker.AreStringsSimilar(studente.descrCorsoUni, studente.descrCorsoDic);
                        if (!isStem)
                        {
                            studente.blocchiDaMettere.Add("VST");
                        }
                    }


                    studente.blocchiDaTogliere = studente.blocchiDaTogliere
                        .Where(studente.blocchiPresenti.ContainsKey)
                        .ToList();

                    studente.blocchiDaMettere = studente.blocchiDaMettere
                        .Where(blocco => !studente.blocchiPresenti.ContainsKey(blocco))
                        .ToList();

                    studente.incongruenzeDaTogliere = studente.incongruenzeDaTogliere
                        .Where(studente.incongruenzePresenti.ContainsKey)
                        .ToList();

                    studente.incongruenzeDaMettere = studente.incongruenzeDaMettere
                        .Where(incongruenza => !studente.incongruenzePresenti.ContainsKey(incongruenza))
                        .ToList();
                }
            }

            void PopulateDataInFile(List<StudenteElaborazione> studenteElaborazioneList, string filepath, string fileName, DataTable initialStudentData)
            {
                DataTable producedTable = new DataTable();

                producedTable.Columns.Add("Codice Fiscale");
                producedTable.Columns.Add("Matricola");
                producedTable.Columns.Add("Tipo Iscrizione UNI");
                producedTable.Columns.Add("Iscrizione con Condizione UNI");
                producedTable.Columns.Add("Tipo Corso UNI");
                producedTable.Columns.Add("Descrizione Corso UNI");
                producedTable.Columns.Add("Anno Corso UNI");
                producedTable.Columns.Add("Anno Immatricolazione UNI");
                producedTable.Columns.Add("Crediti Conseguiti UNI");
                producedTable.Columns.Add("Crediti Convalidati UNI");
                producedTable.Columns.Add("Tassa Regionale Pagata UNI");
                producedTable.Columns.Add("Titolo Acquisito UNI");
                producedTable.Columns.Add("Descrizione Titolo Acquisito UNI");
                producedTable.Columns.Add("     ");
                producedTable.Columns.Add("Se Tipo");
                producedTable.Columns.Add("Se Anno");
                producedTable.Columns.Add("Se CFU");
                producedTable.Columns.Add("Congruenza AA");
                producedTable.Columns.Add("      ");
                producedTable.Columns.Add("Codice Fiscale ");
                producedTable.Columns.Add("Numero Domanda");
                producedTable.Columns.Add("Esito BS");
                producedTable.Columns.Add("Esito PA");
                producedTable.Columns.Add("Disabile DIC");
                producedTable.Columns.Add("Sesso DIC");
                producedTable.Columns.Add("Stem DIC");
                producedTable.Columns.Add("Descrizione Corso Dic");
                producedTable.Columns.Add("Tipo Corso DIC");
                producedTable.Columns.Add("Anno Corso DIC");
                producedTable.Columns.Add("Crediti Conseguiti DIC");
                producedTable.Columns.Add("Crediti Extra Curr DIC");
                producedTable.Columns.Add("Crediti Rinuncia DIC");
                producedTable.Columns.Add("Crediti Tirocinio DIC");
                producedTable.Columns.Add("Descrizione blocchi presenti");
                producedTable.Columns.Add("Cod blocchi presenti");
                producedTable.Columns.Add("Blocchi da TOGLIERE");
                producedTable.Columns.Add("Blocchi da METTERE");
                producedTable.Columns.Add("Descrizione incongruenze");
                producedTable.Columns.Add("Cod incongruenze presenti");
                producedTable.Columns.Add("Incongruenze da TOGLIERE");
                producedTable.Columns.Add("Incongruenze da METTERE");

                foreach (StudenteElaborazione studente in studenteElaborazioneList)
                {
                    string descrBlocchi = studente.blocchiPresenti.Values.Count > 0 ? "#" + string.Join("#", studente.blocchiPresenti.Values) : " ";
                    string codBlocchi = studente.blocchiPresenti.Keys.Count > 0 ? "#" + string.Join("#", studente.blocchiPresenti.Keys) : " ";
                    string blocchiDaTogliere = studente.blocchiDaTogliere.Count > 0 ? "/" + string.Join("/", studente.blocchiDaTogliere) : " ";
                    string blocchiDaMettere = studente.blocchiDaMettere.Count > 0 ? "/" + string.Join("/", studente.blocchiDaMettere) : " ";

                    string descrIncongruenze = studente.incongruenzePresenti.Values.Count > 0 ? "#" + string.Join("#", studente.incongruenzePresenti.Values) : " ";
                    string codIncongruenze = studente.incongruenzePresenti.Keys.Count > 0 ? "#" + string.Join("#", studente.incongruenzePresenti.Keys) : " ";
                    string incongruenzeDaTogliere = studente.incongruenzeDaTogliere.Count > 0 ? "/" + string.Join("/", studente.incongruenzeDaTogliere) : " ";
                    string incongruenzeDaMettere = studente.incongruenzeDaMettere.Count > 0 ? "/" + string.Join("/", studente.incongruenzeDaMettere) : " ";

                    producedTable.Rows.Add(
                        studente.codFiscale.ToString(),
                        studente.matricola.ToString(),
                        studente.tipoIscrizioneUni.ToString(),
                        studente.iscrCondizione ? "1" : "0",
                        studente.tipoCorsoUni.ToString(),
                        studente.descrCorsoUni.ToString(),
                        studente.annoCorsoUni.ToString(),
                        studente.aaImmatricolazioneUni.ToString(),
                        studente.creditiConseguitiUni.ToString(),
                        studente.creditiConvalidatiUni.ToString(),
                        studente.tassaRegionalePagata ? "1" : "0",
                        studente.titoloAcquisito ? "1" : "0",
                        studente.descrTitoloAcquisito,
                        " ",
                        studente.seTitpo ? "OK" : "NOK",
                        studente.seAnno ? "OK" : "NOK",
                        studente.seCFU ? "OK" : "NOK",
                        studente.congruenzaAnno ? "OK" : "NOK",
                        " ",
                        studente.codFiscale,
                        studente.numDomanda,
                        studente.esitoBS >= 0 ? studente.esitoBS : "Non richiesto",
                        studente.esitoPA >= 0 ? studente.esitoPA : "Non richiesto",
                        studente.disabile ? "1" : "0",
                        studente.sessoDic.ToString(),
                        studente.stemDic ? "1" : "0",
                        studente.descrCorsoDic.ToString(),
                        studente.tipoCorsoDic.ToString(),
                        studente.annoCorsoDic.ToString(),
                        studente.creditiConseguitiDic.ToString(),
                        studente.creditiExtraCurrDic.ToString(),
                        studente.creditiDaRinunciaDic.ToString(),
                        studente.creditiTirocinioDic.ToString(),
                        descrBlocchi,
                        codBlocchi,
                        blocchiDaTogliere,
                        blocchiDaMettere,
                        descrIncongruenze,
                        codIncongruenze,
                        incongruenzeDaTogliere,
                        incongruenzeDaMettere
                        );
                }

                string filePath = Utilities.ExportDataTableToExcel(producedTable, Path.Combine(folderPath, "Output"), true, fileName);

                // Open Excel application
                var excelApp = new Microsoft.Office.Interop.Excel.Application();
                var workbook = excelApp.Workbooks.Open(filePath);
                var worksheet = (Microsoft.Office.Interop.Excel.Worksheet)workbook.Sheets[1];

                // Define soft colors
                var softRedColor = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.FromArgb(245, 100, 100));
                var darkBlueColor = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.FromArgb(50, 120, 185));

                // Get row and column counts
                var rowCount = producedTable.Rows.Count + 1; // Including header
                var columnCount = producedTable.Columns.Count;

                // Apply formatting to header row
                var headerRange = worksheet.Range[worksheet.Cells[1, 1], worksheet.Cells[1, columnCount]];
                headerRange.Interior.Color = darkBlueColor;
                headerRange.Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.White);
                headerRange.Font.Bold = true;

                // Get the width of the first column
                var firstColumnWidth = worksheet.Columns[1].ColumnWidth;

                // Apply formatting to columns in bulk
                for (int i = 1; i <= columnCount; i++)
                {
                    string columnName = producedTable.Columns[i - 1].ColumnName;
                    var columnRange = worksheet.Columns[i];

                    // Set each column to have the same width as the first column
                    columnRange.ColumnWidth = firstColumnWidth;

                    // Apply red background if the column name contains spaces
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        columnRange.ColumnWidth = 3;
                        columnRange.Interior.Color = softRedColor;
                    }
                }

                // Apply borders to used range
                var usedRange = worksheet.UsedRange;
                usedRange.Borders.LineStyle = Microsoft.Office.Interop.Excel.XlLineStyle.xlContinuous;
                usedRange.Borders.Weight = Microsoft.Office.Interop.Excel.XlBorderWeight.xlThin;

                // Add new worksheet after the existing ones
                var newWorksheet = (Microsoft.Office.Interop.Excel.Worksheet)workbook.Sheets.Add(After: workbook.Sheets[workbook.Sheets.Count]);
                newWorksheet.Name = "Raw";

                // Write the initialStudentData into newWorksheet in bulk
                int initialRowCount = initialStudentData.Rows.Count;
                int initialColCount = initialStudentData.Columns.Count;

                // Create an array to hold the data including headers
                object[,] dataArray = new object[initialRowCount + 1, initialColCount];

                // Write column headers to the first row of dataArray
                for (int col = 0; col < initialColCount; col++)
                {
                    dataArray[0, col] = initialStudentData.Columns[col].ColumnName;
                }

                // Write data to dataArray
                for (int row = 0; row < initialRowCount; row++)
                {
                    for (int col = 0; col < initialColCount; col++)
                    {
                        dataArray[row + 1, col] = initialStudentData.Rows[row][col];
                    }
                }

                // Define the range where data will be written
                var startCell = (Microsoft.Office.Interop.Excel.Range)newWorksheet.Cells[1, 1];
                var endCell = (Microsoft.Office.Interop.Excel.Range)newWorksheet.Cells[initialRowCount + 1, initialColCount];
                var writeRange = newWorksheet.Range[startCell, endCell];

                // Assign the data array to the Excel Range in bulk
                writeRange.Value2 = dataArray;

                // Apply formatting to header row in newWorksheet
                var newHeaderRange = newWorksheet.Range[newWorksheet.Cells[1, 1], newWorksheet.Cells[1, initialColCount]];
                newHeaderRange.Interior.Color = darkBlueColor;
                newHeaderRange.Font.Color = System.Drawing.ColorTranslator.ToOle(System.Drawing.Color.White);
                newHeaderRange.Font.Bold = true;

                // Save and close the workbook
                workbook.Save();
                workbook.Close(false);
                excelApp.Quit();

                // Release COM objects to free memory
                System.Runtime.InteropServices.Marshal.ReleaseComObject(newWorksheet);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(worksheet);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(workbook);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(excelApp);

                // Force garbage collection
                GC.Collect();
                GC.WaitForPendingFinalizers();

            }

        }
    }
    public class CreditRecord
    {
        public int Disabile { get; set; }
        public string TipologiaCorso { get; set; }
        public string CodCorsoLaurea { get; set; }
        public int AnnoCorso { get; set; }
        public int CreditiRichiesti { get; set; }
    }

}
