using ClosedXML.Excel;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ProcedureNet7
{
    internal class RendicontoMiur : BaseProcedure<ArgsRendicontoMiur>
    {
        private string mainFolderPath = string.Empty;

        private string interventiPath;
        private string spesaPath;
        private Dictionary<string, string> entiStudi;

        public RendicontoMiur(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override async void RunProcedure(ArgsRendicontoMiur args)
        {
            mainFolderPath = args._folderPath;
            await Task.Run(() => StartPopulation(mainFolderPath));

        }

        public async void StartPopulation(string basePath)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                _ = MessageBox.Show("ERROR - Base path is null");
                return;
            }
            _masterForm.inProcedure = true;
            string excelFilePath = Path.Combine(basePath, "COD_SEDE.xlsx");
            interventiPath = Path.Combine(basePath, "MODELLO_INTERVENTI_UN.xlsx");
            spesaPath = Path.Combine(basePath, "MODELLO_SPESA_UN.xlsx");
            entiStudi = ReadExcelFile(excelFilePath);

            if (string.IsNullOrEmpty(excelFilePath) || string.IsNullOrEmpty(interventiPath) || string.IsNullOrEmpty(spesaPath) || entiStudi.Count == 0)
            {
                _ = MessageBox.Show("Errore, controllare se i file modelli sono presenti e se il file COD_SEDE.xlsx è compilato");
                _masterForm.inProcedure = false;
                return;
            }

            Stopwatch stopwatch = new();
            stopwatch.Start();

            await Task.Run(async () =>
            {
                CreateFolders(basePath, entiStudi);
                await CreateAndPopulateExcelFiles(basePath, entiStudi).ConfigureAwait(false);
            });

            stopwatch.Stop();

            _masterForm.Invoke(new Action(() =>
            {
                // Format the elapsed time to minutes and seconds
                string elapsedTime = string.Format("{0:%m}:{0:%s}", stopwatch.Elapsed);
                Logger.Log(100, $"Fine lavorazione. Tempo trascorso: {elapsedTime}", LogLevel.INFO);
                _masterForm.inProcedure = false;
            }));
        }

        private async Task CreateAndPopulateExcelFiles(string basePath, Dictionary<string, string> data)
        {
            Task task1 = Task.Run(() => CreateInterventiFiles(basePath, data));
            Task task2 = Task.Run(() => CreateSpesaFiles(basePath, data));

            await Task.WhenAll(task1, task2);
        }
        private async Task CreateInterventiFiles(string basePath, Dictionary<string, string> sedeStudiDictionary)
        {
            List<Task> tasks = new();

            foreach (KeyValuePair<string, string> sedeStudi in sedeStudiDictionary)
            {
                // Start a new task for each sedeStudi. These tasks run concurrently.
                Task task = Task.Run(async () =>
                {
                    string folderPath = Path.Combine(basePath, sedeStudi.Value);
                    string folderName = Path.GetFileName(folderPath);
                    string newExcelFilePath = Path.Combine(folderPath, "Interventi_" + sedeStudi.Key + ".xlsx");

                    // Copy the template Excel file into the folder
                    File.Copy(interventiPath, newExcelFilePath, overwrite: true);

                    // Populate the Excel file
                    await PopulateInterventiFile(newExcelFilePath, sedeStudi.Key, folderName);
                });
                tasks.Add(task);
            }
            string sediStudio = string.Join(", ", sedeStudiDictionary.Keys.Select(k => $"'{k}'"));
            // Check if the string is not empty and has the required format
            if (!string.IsNullOrEmpty(sediStudio) && sediStudio.Length > 2)
            {
                // Remove the first and the last characters (single quotes) from the string
                sediStudio = sediStudio[1..^1];
            }
            Task taskTotal = Task.Run(async () =>
            {
                string totalExcelFilePath = Path.Combine(basePath, "Interventi_All.xlsx");
                File.Copy(interventiPath, totalExcelFilePath, overwrite: true);

                await PopulateInterventiFile(totalExcelFilePath, sediStudio, Path.GetFileName(basePath));
            });
            tasks.Add(taskTotal);

            // Wait for all tasks to complete
            await Task.WhenAll(tasks);
        }

        private async Task CreateSpesaFiles(string basePath, Dictionary<string, string> sedeStudiDictionary)
        {
            List<Task> tasks = new();

            foreach (KeyValuePair<string, string> sedeStudi in sedeStudiDictionary)
            {
                Task task = Task.Run(async () =>
                {
                    string folderPath = Path.Combine(basePath, sedeStudi.Value);
                    string folderName = Path.GetFileName(folderPath);
                    string newExcelFilePath = Path.Combine(folderPath, "Spesa_" + sedeStudi.Key + ".xlsx");

                    // Copy the template Excel file into the folder
                    File.Copy(spesaPath, newExcelFilePath, overwrite: true);

                    // Now open the copied Excel file and populate it
                    await PopulateSpesaFile(newExcelFilePath, sedeStudi.Key, folderName);
                });

                tasks.Add(task);
            }

            string sediStudio = string.Join(", ", sedeStudiDictionary.Keys.Select(k => $"'{k}'"));
            if (!string.IsNullOrEmpty(sediStudio) && sediStudio.Length > 2)
            {
                // Remove the first and the last characters (single quotes) from the string
                sediStudio = sediStudio[1..^1];
            }
            Task taskTotal = Task.Run(async () =>
            {
                string totalExcelFilePath = Path.Combine(basePath, "Spesa_All.xlsx");
                File.Copy(spesaPath, totalExcelFilePath, overwrite: true);

                await PopulateSpesaFile(totalExcelFilePath, sediStudio, Path.GetFileName(basePath));
            });
            tasks.Add(taskTotal);

            await Task.WhenAll(tasks);
        }

        private async Task PopulateInterventiFile(string excelFilePath, string code, string folderName)
        {
            using (XLWorkbook excelWorkbook = new(excelFilePath))
            {
                var excelWorksheet = excelWorkbook.Worksheets.First();

                // Create tasks for each method
                Task totalBSTask = Task.Run(() => TotalBS(excelWorksheet, code, folderName));
                Task idoneiBSTask = Task.Run(() => IdoneiBorsaS(excelWorksheet, code, folderName));
                Task domandeCITotTask = Task.Run(() => DomandeCITotali(excelWorksheet, code, folderName));
                Task mediaMesiMobilTask = Task.Run(() => MediaMesiMobilità(excelWorksheet, code, folderName));
                Task domandeCIVincitoTask = Task.Run(() => DomandeCIVincitori(excelWorksheet, code, folderName));
                Task totalePostoAlloggioTask = Task.Run(() => TotalePostoAlloggio(excelWorksheet, code, folderName));
                Task totalePremiDiLaureaPLTask = Task.Run(() => TotalePremiDiLaurea(excelWorksheet, code, folderName));
                Task vincitoriPostoAlloggioTask = Task.Run(() => VincitoriPostoAlloggio(excelWorksheet, code, folderName));
                Task totaleContributoAlloggioTask = Task.Run(() => TotaleContributoAlloggio(excelWorksheet, code, folderName));
                Task vincitoriContributoAlloggioTask = Task.Run(() => VincitoriContributoAlloggio(excelWorksheet, code, folderName));
                Task vincitoriPostoAlloggioConBorsaTask = Task.Run(() => VincitoriPostoAlloggioConBorsa(excelWorksheet, code, folderName));
                Task totaleContributoAlloggioConBorsaTask = Task.Run(() => TotaleContributoAlloggioConBorsa(excelWorksheet, code, folderName));

                // Wait for all tasks to complete
                await Task.WhenAll(
                    totalBSTask, idoneiBSTask, domandeCITotTask, domandeCIVincitoTask, mediaMesiMobilTask,
                    totalePostoAlloggioTask, vincitoriPostoAlloggioTask, vincitoriPostoAlloggioConBorsaTask,
                    totaleContributoAlloggioTask, vincitoriContributoAlloggioTask, totaleContributoAlloggioConBorsaTask,
                    totalePremiDiLaureaPLTask);

                excelWorkbook.Save();
            }
        }

        private async Task PopulateSpesaFile(string excelFilePath, string code, string folderName)
        {
            using (XLWorkbook excelWorkbook = new(excelFilePath))
            {
                var excelWorksheet = excelWorkbook.Worksheets.First();

                // Create tasks for each method
                Task totalSpendingBS = Task.Run(() => TotalSpendingBS(excelWorksheet, code, folderName));
                Task totalSpendingCI = Task.Run(() => TotalSpendingCI(excelWorksheet, code, folderName));
                Task totalSpendingCA = Task.Run(() => TotalSpendingCA(excelWorksheet, code, folderName));
                Task totalSpendingCaConBorsa = Task.Run(() => TotalSpendingCaConBorsa(excelWorksheet, code, folderName));
                Task totalSpendingPL = Task.Run(() => TotalSpendingPL(excelWorksheet, code, folderName));

                // Wait for all tasks to complete
                await Task.WhenAll(
                    totalSpendingBS, totalSpendingCI, totalSpendingCA, totalSpendingCaConBorsa, totalSpendingPL);

                excelWorkbook.Save();
            }
        }

        private void TotalBS(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            // SQL Query
            string query = $@"
                    SELECT        
                        Graduatorie.Cod_ente, 
                        Sede_studi.Descrizione, 
                        Graduatorie.Cod_tipologia_studi, 
                        Tipologie_studi.Descrizione AS Expr2, 
                        Graduatorie.Cod_fiscale, 
                        Cittadinanze_Ue.DESCRIZIONE AS Expr1, 
                        CASE WHEN Cittadinanza.cod_cittadinanza IN 
                        (
                            'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 
                            'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 
                            'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 
                            'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134',
                            'Z149', 'Z105', 'Z146'
                        ) THEN 'UE' 
                        ELSE 'NOTUE' 
                        END AS codeu, 
                        Decod_cittadinanza.Descrizione AS Expr3, 
                        Cittadinanza.Cod_cittadinanza, 
                        Graduatorie.Data_validita
                    FROM           
                        Graduatorie INNER JOIN
                        Cittadinanza ON Graduatorie.Cod_fiscale = Cittadinanza.Cod_fiscale INNER JOIN
                        Sede_studi ON Graduatorie.Cod_sede_studi = Sede_studi.Cod_sede_studi INNER JOIN
                        Tipologie_studi ON Graduatorie.Cod_tipologia_studi = Tipologie_studi.Cod_tipologia_studi INNER JOIN
                        Decod_cittadinanza ON Cittadinanza.Cod_cittadinanza = Decod_cittadinanza.Cod_cittadinanza LEFT OUTER JOIN
                        Cittadinanze_Ue ON Cittadinanza.Cod_cittadinanza = Cittadinanze_Ue.CODICE
                    WHERE        
                        (Graduatorie.Cod_tipo_graduat = 1) AND 
                        (Graduatorie.Anno_accademico = '20232024') AND 
                        (Graduatorie.Cod_beneficio = 'BS') AND 
                        (Graduatorie.Cod_sede_studi in ('{code}')) AND
                        (Cittadinanza.Data_validita =
                            (SELECT        
                                MAX(Data_validita) AS Expr1
                            FROM            
                                Cittadinanza AS ct
                            WHERE        
                                (cittadinanza.cod_fiscale = Cod_fiscale) AND 
                                (Data_validita < '01/11/2024'))
                        ) 
                    and Graduatorie.Cod_fiscale <> '0000000000000001'
                    ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int codTipoStudi3to5Count = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(4, 3).Value = codTipoStudi3to5Count;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECount = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                              new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(4, 4).Value = notUECount;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6Count = queryResults.AsEnumerable().Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(4, 5).Value = codTipoStudi6Count;

            // Count cod_tipologia_studi = 7
            int codTipoStudi7Count = queryResults.AsEnumerable().Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(4, 6).Value = codTipoStudi7Count;

            Logger.Log(9, folderName + ": Totale borse", LogLevel.INFO);
        }
        private void IdoneiBorsaS(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT 
	                    dbo.Sede_studi.Cod_ente, 
	                    dbo.Sede_studi.Descrizione, 
	                    i.Cod_tipologia_studi AS Cod_tipologia_studi, 
	                    d.Cod_fiscale, 
	                    EC.Cod_tipo_esito, 
	                    CASE WHEN c.cod_cittadinanza IN 
		                    (
			                    'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 
			                    'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 
			                    'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 
			                    'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 
			                    'z149', 'Z105', 'Z146'
		                    ) THEN 'UE' ELSE 'NOTUE' END AS codeu, 
		                dbo.Tipologie_studi.Descrizione AS Expr1, 
		                CONVERT(money, EC.Imp_beneficio, 0) AS Expr2, 
		                dbo.Decod_cittadinanza.Descrizione AS Expr3, 
		                dbo.vFINANZIATI_FSE.Tipo_fondo, 
		                dbo.Valori_calcolati.Status_sede 
                    FROM   
	                    dbo.Domanda AS d INNER JOIN
	                    dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
	                    dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
	                    dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
	                    dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
	                    dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
	                    dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
	                    dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
	                    dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
	                    dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
                    WHERE 
	                    (d.Anno_accademico = '20232024') AND 
	                    i.Cod_sede_studi in ('{code}') AND 
	                    (EC.Cod_beneficio = 'BS') AND 
	                    (d.Tipo_bando = 'lz') and 
	                    d.Cod_fiscale <> '0000000000000001' AND 
	                    ec.Cod_tipo_esito<>0 and
	                    (EC.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Esiti_concorsi
			                    WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita < '01/11/2024'))) AND 
	                    (i.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Iscrizioni
			                    WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita < '01/11/2024'))) AND (c.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Cittadinanza
			                    WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita < '01/11/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Benefici_richiesti AS br
			                    WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
							                    (Cod_beneficio = 'BS') AND (Data_validita < '01/11/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Valori_calcolati AS vc
			                    WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita < '01/11/2024')))
                    ";
            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int codTipoStudi3to5Count = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(5, 3).Value = codTipoStudi3to5Count;
            excelWorksheet.Cell(6, 3).Value = codTipoStudi3to5Count;

            int codTipoStudi3to5CountInSede = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString())
                           && row["Status_sede"].ToString() == "A");
            excelWorksheet.Cell(30, 3).Value = codTipoStudi3to5CountInSede;

            int codTipoStudi3to5CountInSedeC = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString())
                            && row["Status_sede"].ToString() == "C");
            excelWorksheet.Cell(31, 3).Value = codTipoStudi3to5CountInSedeC;

            int codTipoStudi3to5CountInSedeBorD = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString())
                           && (row["Status_sede"].ToString() == "B" || row["Status_sede"].ToString() == "D"));
            excelWorksheet.Cell(32, 3).Value = codTipoStudi3to5CountInSedeBorD;

            int codTipoStudi3to5CountFSE = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString())
                            && row["tipo_fondo"] != DBNull.Value);
            excelWorksheet.Cell(33, 3).Value = codTipoStudi3to5CountFSE;

            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECount = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                              new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(5, 4).Value = notUECount;
            excelWorksheet.Cell(6, 4).Value = notUECount;

            int notUECountInSede = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                              new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()) && row["Status_sede"].ToString() == "A");
            excelWorksheet.Cell(30, 4).Value = notUECountInSede;

            int notUECountInSedeC = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                              new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()) &&
                              row["Status_sede"].ToString() == "C");
            excelWorksheet.Cell(31, 4).Value = notUECountInSedeC;

            int notUECountInSedeBorD = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                              new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()) &&
                              (row["Status_sede"].ToString() == "B" || row["Status_sede"].ToString() == "D"));
            excelWorksheet.Cell(32, 4).Value = notUECountInSedeBorD;

            int notUECountFSE = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                              new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()) &&
                              row["tipo_fondo"] != DBNull.Value);
            excelWorksheet.Cell(33, 4).Value = notUECountFSE;

            // Count cod_tipologia_studi = 6
            int codTipoStudi6Count = queryResults.AsEnumerable().Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(5, 5).Value = codTipoStudi6Count;
            excelWorksheet.Cell(6, 5).Value = codTipoStudi6Count;

            int codTipoStudi6CountInSede = queryResults.AsEnumerable().Count(row => row["Cod_tipologia_studi"].ToString() == "6" && row["Status_sede"].ToString() == "A");
            excelWorksheet.Cell(30, 5).Value = codTipoStudi6CountInSede;

            int codTipoStudi6CountInSedeC = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6" &&
                              row["Status_sede"].ToString() == "C");
            excelWorksheet.Cell(31, 5).Value = codTipoStudi6CountInSedeC;

            int codTipoStudi6CountInSedeBorD = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6" &&
                              (row["Status_sede"].ToString() == "B" || row["Status_sede"].ToString() == "D"));
            excelWorksheet.Cell(32, 5).Value = codTipoStudi6CountInSedeBorD;

            int codTipoStudi6CountFSE = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6" &&
                              row["tipo_fondo"] != DBNull.Value);
            excelWorksheet.Cell(33, 5).Value = codTipoStudi6CountFSE;

            // Count cod_tipologia_studi = 7
            int codTipoStudi7Count = queryResults.AsEnumerable().Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(5, 6).Value = codTipoStudi7Count;
            excelWorksheet.Cell(6, 6).Value = codTipoStudi7Count;

            int codTipoStudi7CountInSede = queryResults.AsEnumerable().Count(row => row["Cod_tipologia_studi"].ToString() == "7" && row["Status_sede"].ToString() == "A");
            excelWorksheet.Cell(30, 6).Value = codTipoStudi7CountInSede;

            int codTipoStudi7CountInSedeC = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7" &&
                              row["Status_sede"].ToString() == "C");
            excelWorksheet.Cell(31, 6).Value = codTipoStudi7CountInSedeC;

            int codTipoStudi7CountInSedeBorD = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7" &&
                              (row["Status_sede"].ToString() == "B" || row["Status_sede"].ToString() == "D"));
            excelWorksheet.Cell(32, 6).Value = codTipoStudi7CountInSedeBorD;

            int codTipoStudi7CountFSE = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7" &&
                              row["tipo_fondo"] != DBNull.Value);
            excelWorksheet.Cell(33, 6).Value = codTipoStudi7CountFSE;

            Logger.Log(18, folderName + ": Idonei e vincitori borse", LogLevel.INFO);

        }
        private void DomandeCITotali(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT     dbo.Graduatorie.Cod_ente, dbo.Sede_studi.Descrizione, dbo.Graduatorie.Cod_tipologia_studi, dbo.Tipologie_studi.Descrizione AS Expr2, 
                          dbo.Graduatorie.Cod_fiscale, dbo.Cittadinanze_Ue.DESCRIZIONE AS Expr1, CASE WHEN Cittadinanza.cod_cittadinanza IN (	   
					     'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 
                          'Z134', 'z149','Z105','Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, dbo.Decod_cittadinanza.Descrizione AS Expr3, Cittadinanza.cod_cittadinanza, Graduatorie.Data_validita
                    FROM         dbo.Graduatorie INNER JOIN
                          dbo.Cittadinanza ON dbo.Graduatorie.Cod_fiscale = dbo.Cittadinanza.Cod_fiscale INNER JOIN
                          dbo.Sede_studi ON dbo.Graduatorie.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                          dbo.Tipologie_studi ON dbo.Graduatorie.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                          dbo.Decod_cittadinanza ON dbo.Cittadinanza.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza LEFT OUTER JOIN
                          dbo.Cittadinanze_Ue ON dbo.Cittadinanza.Cod_cittadinanza = dbo.Cittadinanze_Ue.CODICE
                    WHERE     (dbo.Graduatorie.Cod_tipo_graduat = 1) and Graduatorie.Cod_sede_studi in ('{code}') AND (dbo.Graduatorie.Anno_accademico = '20232024') AND (dbo.Graduatorie.Cod_beneficio = 'CI') AND 
                            (dbo.Cittadinanza.Data_validita =
                            (SELECT     MAX(Data_validita) AS Expr1
                            FROM          dbo.Cittadinanza AS ct
                            WHERE      (cittadinanza.cod_fiscale = Cod_fiscale) AND (Data_validita < '01/11/2024')))
                ";
            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(9, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(9, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(9, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(9, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(27, folderName + ": Totale contributo integrativo", LogLevel.INFO);

        }
        private void DomandeCIVincitori(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT dbo.Sede_studi.Cod_ente, dbo.Sede_studi.Descrizione, i.Cod_tipologia_studi, d.Cod_fiscale, EC.Cod_tipo_esito, CASE WHEN c.cod_cittadinanza IN ('Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 
                    'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'z149', 'Z105', 'Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, 
                    dbo.Tipologie_studi.Descrizione AS Expr1, CONVERT(money, EC.Imp_beneficio, 0) AS Expr2, dbo.Decod_cittadinanza.Descrizione AS Expr3, dbo.vFINANZIATI_FSE.Tipo_fondo, 
                    dbo.Valori_calcolati.Status_sede 
                        FROM   dbo.Domanda AS d INNER JOIN
                    dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
                    dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                    dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                    dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                    dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND 
                    EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
                    dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                    dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
                    dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
                    dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
                    WHERE (d.Anno_accademico = '20232024') AND (EC.Cod_beneficio = 'ci') AND ( i.Cod_sede_studi in ('{code}')) AND (EC.Cod_tipo_esito = 2) AND (d.Tipo_bando = 'lz') AND (EC.Data_validita =
                    (SELECT MAX(Data_validita) AS Expr1
                     FROM    dbo.Esiti_concorsi
                     WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita < '30/11/2024'))) AND 
                    (i.Data_validita =
                    (SELECT MAX(Data_validita) AS Expr1
                     FROM    dbo.Iscrizioni
                     WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita < '30/11/2024'))) AND (c.Data_validita =
                    (SELECT MAX(Data_validita) AS Expr1
                     FROM    dbo.Cittadinanza
                     WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita < '30/11/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
                    (SELECT MAX(Data_validita) AS Expr1
                     FROM    dbo.Benefici_richiesti AS br
                     WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
                                     (Cod_beneficio = 'CI') AND (Data_validita < '30/11/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
                    (SELECT MAX(Data_validita) AS Expr1
                     FROM    dbo.Valori_calcolati AS vc
                     WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita < '30/11/2024')))
                ";
            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(10, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(10, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(10, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(10, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(36, folderName + ": Vincitori contributo integrativo", LogLevel.INFO);
        }
        private void MediaMesiMobilità(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT      
                        COALESCE(specifiche_ci.durata_ci, 5) AS durata_ci,
	                    Iscrizioni.Cod_tipologia_studi,
	                    CASE WHEN Cittadinanza.cod_cittadinanza IN 
	                    (
		                    'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 
		                    'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 
		                    'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 
		                    'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134',
		                    'Z149', 'Z105', 'Z146'
	                    ) THEN 'UE' 
	                    ELSE 'NOTUE' 
	                    END AS codeu
                    FROM            
                        dbo.specifiche_ci INNER JOIN
                        dbo.Domanda ON dbo.specifiche_ci.anno_accademico = dbo.Domanda.Anno_accademico AND dbo.specifiche_ci.num_domanda = dbo.Domanda.Num_domanda INNER JOIN
                        dbo.Iscrizioni ON Domanda.Anno_accademico = Iscrizioni.Anno_accademico AND Domanda.Cod_fiscale = Iscrizioni.Cod_fiscale INNER JOIN
	                    Cittadinanza ON Domanda.Cod_fiscale = Cittadinanza.Cod_fiscale

                        where domanda.anno_accademico='20232024' and domanda.cod_fiscale in (
                        SELECT 
                            d.Cod_fiscale
                        FROM   
                            dbo.Domanda AS d INNER JOIN
                            dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
                            dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                            dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                            dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                            dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
                            dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                            dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
                            dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
                            dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
                        WHERE 
                            (d.Anno_accademico = '20232024') AND 
                            i.Cod_sede_studi in ('{code}') AND 
                            (EC.Cod_beneficio = 'CI') AND 
                            (d.Tipo_bando = 'lz') and 
                            d.Cod_fiscale <> '0000000000000001' AND 
                            ec.Cod_tipo_esito = 2 and
                            (EC.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Esiti_concorsi
                                    WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita < '30/11/2024'))) AND 
                            (i.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Iscrizioni
                                    WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita < '30/11/2024'))) AND (c.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Cittadinanza
                                    WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita < '30/11/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Benefici_richiesti AS br
                                    WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
					                                            (Cod_beneficio = 'CI') AND (Data_validita < '30/11/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Valori_calcolati AS vc
                                    WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita < '30/11/2024')))
                    ) and data_fine_validita is null";
            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            static double calculateAverage(List<int> list)
            {
                if (list.Count == 0) { return 0; }

                double sum = list.Sum();
                double average = sum / list.Count;

                return Math.Round(average, 2, MidpointRounding.AwayFromZero);
            }


            // Count cod_tipologia_studi = 3, 4, and 5
            List<int> durataCiList = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the combined list
            double median = calculateAverage(durataCiList);
            // Store the median in Excel at cell (11, 3)
            excelWorksheet.Cell(11, 3).Value = median;

            // Gather durata_ci values for Cod_tipologia_studi 3, 4, and 5 where codeu is NOTUE
            List<int> durataCiListNotUE = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()) && row["codeu"].ToString() == "NOTUE")
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the filtered list
            double medianNotUE = calculateAverage(durataCiListNotUE);
            // Store the median in Excel at cell (11, 3)
            excelWorksheet.Cell(11, 4).Value = medianNotUE;

            // Count cod_tipologia_studi = 6
            List<int> durataCiList6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the combined list
            double median6 = calculateAverage(durataCiList6);
            // Store the median in Excel at cell (11, 3)
            excelWorksheet.Cell(11, 5).Value = median6;

            // Count cod_tipologia_studi = 7
            List<int> durataCiList7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the combined list
            double median7 = calculateAverage(durataCiList7);
            // Store the median in Excel at cell (11, 3)
            excelWorksheet.Cell(11, 6).Value = median7;

            Logger.Log(45, folderName + ": Media mesi mobilità", LogLevel.INFO);

        }
        private void TotalePostoAlloggio(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT     dbo.Graduatorie.Cod_ente, dbo.Sede_studi.Descrizione, dbo.Graduatorie.Cod_tipologia_studi, dbo.Tipologie_studi.Descrizione AS Expr2, 
                                          dbo.Graduatorie.Cod_fiscale, dbo.Cittadinanze_Ue.DESCRIZIONE AS Expr1, CASE WHEN Cittadinanza.cod_cittadinanza IN (	   
					                     'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 
                                          'Z134', 'z149','Z105','Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, dbo.Decod_cittadinanza.Descrizione AS Expr3, Cittadinanza.cod_cittadinanza, Graduatorie.Data_validita
                    FROM         dbo.Graduatorie INNER JOIN
                                          dbo.Cittadinanza ON dbo.Graduatorie.Cod_fiscale = dbo.Cittadinanza.Cod_fiscale INNER JOIN
                                          dbo.Sede_studi ON dbo.Graduatorie.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                                          dbo.Tipologie_studi ON dbo.Graduatorie.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                          dbo.Decod_cittadinanza ON dbo.Cittadinanza.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza LEFT OUTER JOIN
                                          dbo.Cittadinanze_Ue ON dbo.Cittadinanza.Cod_cittadinanza = dbo.Cittadinanze_Ue.CODICE
                    WHERE     (dbo.Graduatorie.Cod_tipo_graduat = 1) and Graduatorie.Cod_sede_studi in ('{code}') AND (dbo.Graduatorie.Anno_accademico = '20232024') AND (dbo.Graduatorie.Cod_beneficio = 'PA') AND 
                                          (dbo.Cittadinanza.Data_validita =
                                              (SELECT     MAX(Data_validita) AS Expr1
                                                FROM          dbo.Cittadinanza AS ct
                                                WHERE      (cittadinanza.cod_fiscale = Cod_fiscale) AND (Data_validita < '01/11/2024'))) 
                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(16, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(16, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(16, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(16, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(54, folderName + ": Totale posto alloggio", LogLevel.INFO);
        }
        private void VincitoriPostoAlloggio(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"

                SELECT dbo.Sede_studi.Cod_ente, dbo.Sede_studi.Descrizione, i.Cod_tipologia_studi, d.Cod_fiscale, EC.Cod_tipo_esito, CASE WHEN c.cod_cittadinanza IN ('Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 
                                'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'z149', 'Z105', 'Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, 
                                dbo.Tipologie_studi.Descrizione AS Expr1,
				                dbo.Decod_cittadinanza.Descrizione AS Expr3
                FROM   dbo.Domanda AS d INNER JOIN
                                dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
                                dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                                dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                                dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND 
                                EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
                                dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                                dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
                                dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
                                dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
                WHERE (d.Anno_accademico = '20232024') AND (EC.Cod_beneficio = 'pa')AND (i.Cod_sede_studi in ('{code}'))  AND (EC.Cod_tipo_esito = 2) AND (d.Tipo_bando in ('lz','l2')) AND (EC.Data_validita =
                                    (SELECT MAX(Data_validita) AS Expr1
                                     FROM    dbo.Esiti_concorsi
                                     WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita <= '30/05/2024'))) AND 
                                (i.Data_validita =
                                    (SELECT MAX(Data_validita) AS Expr1
                                     FROM    dbo.Iscrizioni
                                     WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '30/05/2024'))) AND (c.Data_validita =
                                    (SELECT MAX(Data_validita) AS Expr1
                                     FROM    dbo.Cittadinanza
                                     WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '30/05/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
                                    (SELECT MAX(Data_validita) AS Expr1
                                     FROM    dbo.Benefici_richiesti AS br
                                     WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
                                                     (Cod_beneficio = 'pa') AND (Data_validita <= '30/05/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
                                    (SELECT MAX(Data_validita) AS Expr1
                                     FROM    dbo.Valori_calcolati AS vc
                                     WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita <= '30/05/2024')))
                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(17, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(17, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(17, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(17, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(63, folderName + ": Vincitori posto alloggio", LogLevel.INFO);
        }
        private void VincitoriPostoAlloggioConBorsa(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                        select 
	                        d.Cod_fiscale,
	                        EC.Cod_tipo_esito,
	                        Iscrizioni.Cod_tipologia_studi,
	                        CASE WHEN Cittadinanza.cod_cittadinanza IN 
	                        (
		                        'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 
		                        'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 
		                        'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 
		                        'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134',
		                        'Z149', 'Z105', 'Z146'
	                        ) THEN 'UE' 
	                        ELSE 'NOTUE' 
	                        END AS codeu
                        from 
	                        dbo.Domanda AS d INNER JOIN
	                        dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
	                        dbo.Iscrizioni ON d.Anno_accademico = Iscrizioni.Anno_accademico AND d.Cod_fiscale = Iscrizioni.Cod_fiscale INNER JOIN
	                        Cittadinanza ON d.Cod_fiscale = Cittadinanza.Cod_fiscale
                        where 
	                        d.anno_accademico='20232024' AND 
	                        (EC.Cod_beneficio = 'bs') AND 
	                        (EC.Cod_tipo_esito = 2) and
	                        (EC.Data_validita =
		                        (SELECT        
			                        MAX(Data_validita) AS Expr1
		                        FROM            
			                        dbo.Esiti_concorsi
		                        WHERE        
			                        (Anno_accademico = EC.Anno_accademico) AND 
			                        (Num_domanda = EC.Num_domanda) AND 
			                        (Cod_beneficio = EC.Cod_beneficio) AND 
			                        (Data_validita < '01/06/2024'))) and 
	                        Iscrizioni.Data_validita = 
		                        (Select max(data_validita) as expr1
		                        from
			                        Iscrizioni i
		                        where
			                        Iscrizioni.Anno_accademico = i.Anno_accademico and 
			                        Iscrizioni.Cod_fiscale = i.Cod_fiscale and
			                        Data_validita < '01/06/2024') and 
	                        Cittadinanza.Data_validita = 
		                        (Select max(data_validita) as expr1
		                        from
			                        Cittadinanza c
		                        where
			                        Cittadinanza.Cod_fiscale = c.Cod_fiscale and
			                        Data_validita < '01/06/2024')
			
                        and d.cod_fiscale in (
			
			                        SELECT d.Cod_fiscale
			                        FROM   dbo.Domanda AS d INNER JOIN
                                        dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
                                        dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                                        dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                        dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                                        dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND 
                                        EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
                                        dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                                        dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
                                        dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
                                        dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
			                        WHERE (d.Anno_accademico = '20232024') AND (EC.Cod_beneficio = 'pa')AND (i.Cod_sede_studi in ('{code}'))  AND (EC.Cod_tipo_esito = 2) AND (d.Tipo_bando in ('lz','l2')) AND (EC.Data_validita =
                                            (SELECT MAX(Data_validita) AS Expr1
                                                FROM    dbo.Esiti_concorsi
                                                WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita <= '30/05/2024'))) AND 
                                        (i.Data_validita =
                                            (SELECT MAX(Data_validita) AS Expr1
                                                FROM    dbo.Iscrizioni
                                                WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '30/05/2024'))) AND (c.Data_validita =
                                            (SELECT MAX(Data_validita) AS Expr1
                                                FROM    dbo.Cittadinanza
                                                WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '30/05/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
                                            (SELECT MAX(Data_validita) AS Expr1
                                                FROM    dbo.Benefici_richiesti AS br
                                                WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
                                                                (Cod_beneficio = 'pa') AND (Data_validita <= '30/05/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
                                            (SELECT MAX(Data_validita) AS Expr1
                                                FROM    dbo.Valori_calcolati AS vc
                                                WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita <= '30/05/2024'))))

                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(18, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(18, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(18, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(18, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(72, folderName + ": Vincitori posto alloggio con borsa", LogLevel.INFO);
        }
        private void TotaleContributoAlloggio(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT        a.Cod_ente, dbo.Sede_studi.Descrizione, i.Cod_tipologia_studi, d.Cod_fiscale, CASE WHEN c.cod_cittadinanza IN ('Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 
                                             'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'z149', 'Z105', 'Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, 
                                             dbo.Tipologie_studi.Descrizione AS Expr1, dbo.Decod_cittadinanza.Descrizione AS Expr3, vEsiti_concorsi.Cod_tipo_esito, vEsiti_concorsi.Cod_beneficio, vEsiti_concorsi.Imp_beneficio, pagato.pagato
                    FROM            dbo.Domanda AS d INNER JOIN
                                             dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
                                             dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                                             dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                                             dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                             dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                                             dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                                             dbo.vEsiti_concorsi ON d.Num_domanda = vEsiti_concorsi.num_domanda AND d.Anno_accademico = vEsiti_concorsi.Anno_accademico

						                      left outer join (SELECT SUM(Imp_pagato) AS pagato, Num_domanda, Anno_accademico
                                         FROM    Pagamenti
                                         WHERE (Anno_accademico IN ('20232024')) AND (
                                                    cod_tipo_pagam in (
                                                            SELECT Cod_tipo_pagam_new FROM Decod_pagam_new WHERE Cod_tipo_pagam_old IN ('c1','ca','43','44')
                                                        )
                                                    OR cod_tipo_pagam IN ('c1','ca','43','44')
                                                    )
					                     GROUP BY Num_domanda,Anno_accademico) as pagato on pagato.Num_domanda=D.Num_domanda and pagato.Anno_accademico=D.Anno_accademico
                    WHERE        d.Anno_accademico in ('20232024') and i.Cod_sede_studi in ('{code}') and d.Cod_fiscale <> 'XXXXXXXXXXXXXXXX' AND (i.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Iscrizioni
                                                   WHERE        (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) AND (a.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Appartenenza
                                                   WHERE        (Anno_accademico = a.Anno_accademico) AND (Cod_fiscale = a.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) AND (c.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Cittadinanza
                                                   WHERE        (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) and cod_beneficio ='ca' 

                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "9", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(21, 3).Value = countWithoutEsitoCondition;

            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5", "8", "9", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(21, 4).Value = notUECountWithoutEsito;

            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(21, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(21, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(81, folderName + ": Totale contributo alloggio", LogLevel.INFO);

        }
        private void VincitoriContributoAlloggio(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT        a.Cod_ente, dbo.Sede_studi.Descrizione, i.Cod_tipologia_studi, d.Cod_fiscale, CASE WHEN c.cod_cittadinanza IN ('Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 
                                             'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'z149', 'Z105', 'Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, 
                                             dbo.Tipologie_studi.Descrizione AS Expr1, dbo.Decod_cittadinanza.Descrizione AS Expr3, vEsiti_concorsi.Cod_tipo_esito, vEsiti_concorsi.Cod_beneficio, vEsiti_concorsi.Imp_beneficio, pagato.pagato
                    FROM            dbo.Domanda AS d INNER JOIN
                                             dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
                                             dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                                             dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                                             dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                             dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                                             dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                                             dbo.vEsiti_concorsi ON d.Num_domanda = vEsiti_concorsi.num_domanda AND d.Anno_accademico = vEsiti_concorsi.Anno_accademico

						                      left outer join (SELECT SUM(Imp_pagato) AS pagato, Num_domanda, Anno_accademico
                                        FROM    Pagamenti
                                         WHERE (Anno_accademico IN ('20232024')) AND (
                                                    cod_tipo_pagam in (
                                                            SELECT Cod_tipo_pagam_new FROM Decod_pagam_new WHERE Cod_tipo_pagam_old IN ('c1','ca','43','44')
                                                        )
                                                    OR cod_tipo_pagam IN ('c1','ca','43','44')
                                                    )
					                     GROUP BY Num_domanda,Anno_accademico) as pagato on pagato.Num_domanda=D.Num_domanda and pagato.Anno_accademico=D.Anno_accademico
                    WHERE        d.Anno_accademico in ('20232024') and i.Cod_sede_studi in ('{code}') and d.Cod_fiscale <> 'XXXXXXXXXXXXXXXX' AND (i.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Iscrizioni
                                                   WHERE        (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) AND (a.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Appartenenza
                                                   WHERE        (Anno_accademico = a.Anno_accademico) AND (Cod_fiscale = a.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) AND (c.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Cittadinanza
                                                   WHERE        (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) and cod_beneficio ='ca' and cod_tipo_esito = 2

                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "9", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(22, 3).Value = countWithoutEsitoCondition;

            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5", "8", "9", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(22, 4).Value = notUECountWithoutEsito;

            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(22, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(22, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(84, folderName + ": Vincitori contributo alloggio", LogLevel.INFO);

        }
        private void TotaleContributoAlloggioConBorsa(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    select
	                    d.Cod_fiscale,
	                    EC.Cod_tipo_esito,
	                    Iscrizioni.Cod_tipologia_studi,
	                    CASE WHEN Cittadinanza.cod_cittadinanza IN 
	                    (
		                    'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 
		                    'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 
		                    'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 
		                    'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134',
		                    'Z149', 'Z105', 'Z146'
	                    ) THEN 'UE' 
	                    ELSE 'NOTUE' 
	                    END AS codeu
                    from 
	                    dbo.Domanda AS d INNER JOIN
	                    dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
	                    dbo.Iscrizioni ON d.Anno_accademico = Iscrizioni.Anno_accademico AND d.Cod_fiscale = Iscrizioni.Cod_fiscale INNER JOIN
	                    Cittadinanza ON d.Cod_fiscale = Cittadinanza.Cod_fiscale
                    where 
	                    d.anno_accademico='20232024' AND 
	                    (EC.Cod_beneficio = 'bs') AND 
	                    (EC.Cod_tipo_esito = 2) and
	                    (EC.Data_validita =
		                    (SELECT        
			                    MAX(Data_validita) AS Expr1
		                    FROM            
			                    dbo.Esiti_concorsi
		                    WHERE        
			                    (Anno_accademico = EC.Anno_accademico) AND 
			                    (Num_domanda = EC.Num_domanda) AND 
			                    (Cod_beneficio = EC.Cod_beneficio) AND 
			                    (Data_validita < '01/11/2024'))) and 
	                    Iscrizioni.Data_validita = 
		                    (Select max(data_validita) as expr1
		                    from
			                    Iscrizioni i
		                    where
			                    Iscrizioni.Anno_accademico = i.Anno_accademico and 
			                    Iscrizioni.Cod_fiscale = i.Cod_fiscale and
			                    Data_validita < '01/11/2024') and 
	                    Cittadinanza.Data_validita = 
		                    (Select max(data_validita) as expr1
		                    from
			                    Cittadinanza c
		                    where
			                    Cittadinanza.Cod_fiscale = c.Cod_fiscale and
			                    Data_validita < '01/11/2024') and 
	                    d.cod_fiscale in (
			
			                    SELECT        d.Cod_fiscale
			                    FROM            dbo.Domanda AS d INNER JOIN
                                             dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
                                             dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                                             dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                                             dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                             dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                                             dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                                             dbo.vEsiti_concorsi ON d.Num_domanda = vEsiti_concorsi.num_domanda AND d.Anno_accademico = vEsiti_concorsi.Anno_accademico

						                      left outer join (SELECT SUM(Imp_pagato) AS pagato, Num_domanda, Anno_accademico
                                         FROM    Pagamenti
                                         WHERE (Anno_accademico IN ('20232024')) AND (
                                                    cod_tipo_pagam in (
                                                            SELECT Cod_tipo_pagam_new FROM Decod_pagam_new WHERE Cod_tipo_pagam_old IN ('c1','ca','43','44')
                                                        )
                                                    OR cod_tipo_pagam IN ('c1','ca','43','44')
                                                    )
					                     GROUP BY Num_domanda,Anno_accademico) as pagato on pagato.Num_domanda=D.Num_domanda and pagato.Anno_accademico=D.Anno_accademico
			                    WHERE        d.Anno_accademico in ('20232024') and i.Cod_sede_studi in ('{code}') and d.Cod_fiscale <> 'XXXXXXXXXXXXXXXX' and Cod_tipo_esito = 2 AND (i.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Iscrizioni
                                                   WHERE        (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) AND (a.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Appartenenza
                                                   WHERE        (Anno_accademico = a.Anno_accademico) AND (Cod_fiscale = a.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) AND (c.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Cittadinanza
                                                   WHERE        (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '01/11/2024'))) and cod_beneficio ='ca' and cod_tipo_esito = 2
                    )

                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5", "8", "9", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(23, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5", "8", "9", "10" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(23, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(23, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(23, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(90, folderName + ": Vincitori contributo alloggio con borsa", LogLevel.INFO);
        }
        private void TotalePremiDiLaurea(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT        a.Cod_ente, dbo.Sede_studi.Descrizione, i.Cod_tipologia_studi, d.Cod_fiscale, CASE WHEN c.cod_cittadinanza IN ('Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 
                                             'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'z149', 'Z105', 'Z146') THEN 'UE' ELSE 'NOTUE' END AS codeu, 
                                             dbo.Tipologie_studi.Descrizione AS Expr1, dbo.Decod_cittadinanza.Descrizione AS Expr3, vEsiti_concorsiPL_1.Cod_tipo_esito, vEsiti_concorsiPL_1.Cod_beneficio, vEsiti_concorsiPL_1.Imp_beneficio
						                     , pagato.Imp_pagato
                    FROM            dbo.Domanda AS d INNER JOIN
                                             dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
                                             dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                                             dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                                             dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                                             dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                                             dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                                             dbo.vEsiti_concorsiPL AS vEsiti_concorsiPL_1 ON d.Num_domanda = vEsiti_concorsiPL_1.num_domandaBS AND d.Anno_accademico = vEsiti_concorsiPL_1.Anno_accademico
						                     left outer join (SELECT Num_domanda, Imp_pagato , Anno_accademico     
                      FROM    Pagamenti
                                         WHERE (Anno_accademico IN ('20212022')) AND (
                                                    cod_tipo_pagam in (
                                                            SELECT Cod_tipo_pagam_new FROM Decod_pagam_new WHERE Cod_tipo_pagam_old IN ('34')
                                                        )
                                                    OR cod_tipo_pagam IN ('34')
                                                    ) and Anno_accademico='20212022') 


as pagato on pagato.Num_domanda=D.Num_domanda and pagato.Anno_accademico=D.Anno_accademico

                    WHERE        (d.Anno_accademico = '20202021') and i.Cod_sede_studi in ('{code}')
                     AND (i.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Iscrizioni
                                                   WHERE        (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '31/10/2024'))) AND (a.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Appartenenza
                                                   WHERE        (Anno_accademico = a.Anno_accademico) AND (Cod_fiscale = a.Cod_fiscale) AND (Data_validita <= '31/10/2024'))) AND (c.Data_validita =
                                                 (SELECT        MAX(Data_validita) AS Expr1
                                                   FROM            dbo.Cittadinanza
                                                   WHERE        (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '31/10/2024'))) and Cod_tipo_esito=2
                
                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Count cod_tipologia_studi = 3, 4, and 5
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(26, 3).Value = countWithoutEsitoCondition;


            // Count NOTUE and also check for cod_tipologia_studi = 3, 4, and 5
            int notUECountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["codeu"].ToString() == "NOTUE" &&
                                new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));
            excelWorksheet.Cell(26, 4).Value = notUECountWithoutEsito;


            // Count cod_tipologia_studi = 6
            int codTipoStudi6CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "6");
            excelWorksheet.Cell(26, 5).Value = codTipoStudi6CountWithoutEsito;


            // Count cod_tipologia_studi = 7
            int codTipoStudi7CountWithoutEsito = queryResults.AsEnumerable()
                .Count(row => row["Cod_tipologia_studi"].ToString() == "7");
            excelWorksheet.Cell(26, 6).Value = codTipoStudi7CountWithoutEsito;

            Logger.Log(99, folderName + ": Premi di laurea", LogLevel.INFO);
        }

        private void TotalSpendingBS(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT 
	                    dbo.Sede_studi.Cod_ente, 
	                    dbo.Sede_studi.Descrizione, 
	                    i.Cod_tipologia_studi AS Cod_tipologia_studi, 
	                    d.Cod_fiscale, 
	                    EC.Cod_tipo_esito, 
		                dbo.Tipologie_studi.Descrizione AS Expr1, 
		                CONVERT(money, EC.Imp_beneficio, 0) AS Importo, 
		                dbo.Decod_cittadinanza.Descrizione AS Expr3, 
		                dbo.vFINANZIATI_FSE.Tipo_fondo, 
		                dbo.Valori_calcolati.Status_sede 
                    FROM   
	                    dbo.Domanda AS d INNER JOIN
	                    dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
	                    dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
	                    dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
	                    dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
	                    dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
	                    dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
	                    dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
	                    dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
	                    dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
                    WHERE 
	                    (d.Anno_accademico = '20232024') AND 
	                    i.Cod_sede_studi in ('{code}') AND 
	                    (EC.Cod_beneficio = 'BS') AND 
	                    (d.Tipo_bando = 'lz') and 
	                    d.Cod_fiscale <> '0000000000000001' AND 
	                    ec.Cod_tipo_esito<>0 and
	                    (EC.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Esiti_concorsi
			                    WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita < '01/11/2024'))) AND 
	                    (i.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Iscrizioni
			                    WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita < '01/11/2024'))) AND (c.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Cittadinanza
			                    WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita < '01/11/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Benefici_richiesti AS br
			                    WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
							                    (Cod_beneficio = 'BS') AND (Data_validita < '01/11/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
		                    (SELECT MAX(Data_validita) AS Expr1
			                    FROM    dbo.Valori_calcolati AS vc
			                    WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita < '01/11/2024')))
                    ";
            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Importo"));
            excelWorksheet.Cell(4, 3).Value = sumImpBeneficioForCodTipoStudi;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Importo"));
            excelWorksheet.Cell(4, 4).Value = sumImpBeneficioForCodTipoStudi6;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Importo"));
            excelWorksheet.Cell(4, 5).Value = sumImpBeneficioForCodTipoStudi7;


            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudiFSE = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()) && row["tipo_fondo"] != DBNull.Value)
                .Sum(row => row.Field<decimal>("Importo"));
            excelWorksheet.Cell(5, 3).Value = sumImpBeneficioForCodTipoStudiFSE;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudiFSE6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()) && row["tipo_fondo"] != DBNull.Value)
                .Sum(row => row.Field<decimal>("Importo"));
            excelWorksheet.Cell(5, 4).Value = sumImpBeneficioForCodTipoStudiFSE6;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudiFSE7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()) && row["tipo_fondo"] != DBNull.Value)
                .Sum(row => row.Field<decimal>("Importo"));
            excelWorksheet.Cell(5, 5).Value = sumImpBeneficioForCodTipoStudiFSE7;

            Logger.Log(1, folderName + ": Spese borse di studio", LogLevel.INFO);
        }
        private void TotalSpendingCI(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT      
                        COALESCE(specifiche_ci.durata_ci, 5) AS durata_ci,
	                    Iscrizioni.Cod_tipologia_studi,
	                    CASE WHEN Cittadinanza.cod_cittadinanza IN 
	                    (
		                    'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 
		                    'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 
		                    'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 
		                    'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134',
		                    'Z149', 'Z105', 'Z146'
	                    ) THEN 'UE' 
	                    ELSE 'NOTUE' 
	                    END AS codeu
                    FROM            
                        dbo.specifiche_ci INNER JOIN
                        dbo.Domanda ON dbo.specifiche_ci.anno_accademico = dbo.Domanda.Anno_accademico AND dbo.specifiche_ci.num_domanda = dbo.Domanda.Num_domanda INNER JOIN
                        dbo.Iscrizioni ON Domanda.Anno_accademico = Iscrizioni.Anno_accademico AND Domanda.Cod_fiscale = Iscrizioni.Cod_fiscale INNER JOIN
	                    Cittadinanza ON Domanda.Cod_fiscale = Cittadinanza.Cod_fiscale

                        where domanda.anno_accademico='20232024' and domanda.cod_fiscale in (
                        SELECT 
                            d.Cod_fiscale
                        FROM   
                            dbo.Domanda AS d INNER JOIN
                            dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
                            dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                            dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                            dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                            dbo.Benefici_richiesti ON d.Anno_accademico = dbo.Benefici_richiesti.Anno_accademico AND d.Num_domanda = dbo.Benefici_richiesti.Num_domanda AND EC.Cod_beneficio = dbo.Benefici_richiesti.Cod_beneficio INNER JOIN
                            dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                            dbo.Valori_calcolati ON d.Anno_accademico = dbo.Valori_calcolati.Anno_accademico AND d.Num_domanda = dbo.Valori_calcolati.Num_domanda INNER JOIN
                            dbo.Sede_studi ON i.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi LEFT OUTER JOIN
                            dbo.vFINANZIATI_FSE ON d.Anno_accademico = dbo.vFINANZIATI_FSE.Anno_accademico AND d.Num_domanda = dbo.vFINANZIATI_FSE.Num_domanda
                        WHERE 
                            (d.Anno_accademico = '20232024') AND 
                            i.Cod_sede_studi in ('{code}') AND 
                            (EC.Cod_beneficio = 'CI') AND 
                            (d.Tipo_bando = 'lz') and 
                            d.Cod_fiscale <> '0000000000000001' AND 
                            ec.Cod_tipo_esito = 2 and
                            (EC.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Esiti_concorsi
                                    WHERE (Anno_accademico = EC.Anno_accademico) AND (Num_domanda = EC.Num_domanda) AND (Cod_beneficio = EC.Cod_beneficio) AND (Data_validita < '30/11/2024'))) AND 
                            (i.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Iscrizioni
                                    WHERE (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita < '30/11/2024'))) AND (c.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Cittadinanza
                                    WHERE (Cod_fiscale = c.Cod_fiscale) AND (Data_validita < '30/11/2024'))) AND (dbo.Benefici_richiesti.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Benefici_richiesti AS br
                                    WHERE (Benefici_richiesti.num_domanda = Num_domanda) AND (Benefici_richiesti.anno_accademico = Anno_accademico) AND (Benefici_richiesti.cod_beneficio = Cod_beneficio) AND 
					                                            (Cod_beneficio = 'CI') AND (Data_validita < '30/11/2024') AND (Data_fine_validita IS NULL))) AND (dbo.Valori_calcolati.Data_validita =
                                (SELECT MAX(Data_validita) AS Expr1
                                    FROM    dbo.Valori_calcolati AS vc
                                    WHERE (Anno_accademico = Valori_calcolati.Anno_accademico) AND (Num_domanda = Valori_calcolati.Num_domanda) AND (Data_validita < '30/11/2024')))
                    ) and data_fine_validita is null";

            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            static double calculateAverage(List<int> list)
            {
                if (list.Count == 0) { return 0; }

                double sum = list.Sum();
                double average = sum / list.Count;

                return Math.Round(average, 2, MidpointRounding.AwayFromZero);
            }


            // Count cod_tipologia_studi = 3, 4, and 5
            List<int> durataCiList = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the combined list
            double median = calculateAverage(durataCiList);
            int countWithoutEsitoCondition = queryResults.AsEnumerable()
                .Count(row => new[] { "3", "4", "5" }.Contains(row["Cod_tipologia_studi"].ToString()));

            double result = Math.Ceiling(median) * 510 * countWithoutEsitoCondition;
            excelWorksheet.Cell(7, 3).Value = result;

            // Count cod_tipologia_studi = 6
            List<int> durataCiList6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the combined list
            double median6 = calculateAverage(durataCiList6);
            int countWithoutEsitoCondition6 = queryResults.AsEnumerable()
                .Count(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()));

            double result6 = Math.Ceiling(median) * 510 * countWithoutEsitoCondition6;
            excelWorksheet.Cell(7, 4).Value = result6;

            // Count cod_tipologia_studi = 7
            List<int> durataCiList7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Select(row => Convert.ToInt32(row["durata_ci"]))
                .ToList();
            // Calculate the median for the combined list
            double median7 = calculateAverage(durataCiList7);
            int countWithoutEsitoCondition7 = queryResults.AsEnumerable()
                .Count(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()));

            double result7 = Math.Ceiling(median) * 510 * countWithoutEsitoCondition7;
            excelWorksheet.Cell(7, 5).Value = result7;

            Logger.Log(35, folderName + ": Spese mobilità internazionale", LogLevel.INFO);
        }
        private void TotalSpendingCA(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"

                    SELECT        
	                    a.Cod_ente, 
	                    dbo.Sede_studi.Descrizione, 
	                    i.Cod_tipologia_studi, 
	                    d.Cod_fiscale, 
	                    dbo.Tipologie_studi.Descrizione AS Expr1,
	                    dbo.Decod_cittadinanza.Descrizione AS Expr3, 
	                    vEsiti_concorsi.Cod_tipo_esito, 
	                    vEsiti_concorsi.Cod_beneficio, 
	                    vEsiti_concorsi.Imp_beneficio
                    FROM            
	                    dbo.Domanda AS d INNER JOIN
	                    dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
	                    dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
	                    dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
	                    dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
	                    dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
	                    dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
	                    dbo.vEsiti_concorsi ON d.Num_domanda = vEsiti_concorsi.num_domanda AND d.Anno_accademico = vEsiti_concorsi.Anno_accademico
                    WHERE        
	                    d.Anno_accademico in ('20232024') AND 
                        i.Cod_sede_studi in ('{code}') and
                        vEsiti_concorsi.cod_tipo_esito = 2 and
	                    (i.Data_validita =
		                    (SELECT
			                    MAX(Data_validita) AS Expr1
		                    FROM            
			                    dbo.Iscrizioni
		                    WHERE        
			                    (Anno_accademico = i.Anno_accademico) AND 
			                    (Cod_fiscale = i.Cod_fiscale) AND 
			                    (Data_validita <= '01/11/2024')
		                    )
	                    ) AND 
	                    (a.Data_validita =
                            (SELECT        
			                    MAX(Data_validita) AS Expr1
                            FROM            
			                    dbo.Appartenenza
                            WHERE        
			                    (Anno_accademico = a.Anno_accademico) AND 
			                    (Cod_fiscale = a.Cod_fiscale) AND 
			                    (Data_validita <= '01/11/2024')
		                    )
	                    ) AND 
	                    (c.Data_validita =
                            (SELECT        
			                    MAX(Data_validita) AS Expr1
                            FROM            
			                    dbo.Cittadinanza
                            WHERE        
			                    (Cod_fiscale = c.Cod_fiscale) AND 
			                    (Data_validita <= '01/11/2024')
		                    )
	                    ) and 
	                    cod_beneficio ='ca'
                
                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(10, 3).Value = sumImpBeneficioForCodTipoStudi;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(10, 4).Value = sumImpBeneficioForCodTipoStudi6;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(10, 5).Value = sumImpBeneficioForCodTipoStudi7;




            Logger.Log(55, folderName + ": Spese contributo alloggio", LogLevel.INFO);
        }
        private void TotalSpendingCaConBorsa(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                     SELECT        
                         a.Cod_ente, 
                         dbo.Sede_studi.Descrizione, 
                         i.Cod_tipologia_studi, 
                         d.Cod_fiscale, 
                         dbo.Tipologie_studi.Descrizione AS Expr1,
                         dbo.Decod_cittadinanza.Descrizione AS Expr3, 
                         vEsiti_concorsi.Cod_tipo_esito, 
                         vEsiti_concorsi.Cod_beneficio, 
                         vEsiti_concorsi.Imp_beneficio
                     FROM            
                         dbo.Domanda AS d INNER JOIN
                         dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
                         dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
                         dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
                         dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
                         dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
                         dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
                         dbo.vEsiti_concorsi ON d.Num_domanda = vEsiti_concorsi.num_domanda AND d.Anno_accademico = vEsiti_concorsi.Anno_accademico
                     WHERE        
                         d.Anno_accademico in ('20232024') AND 
     
                         vEsiti_concorsi.cod_tipo_esito = 2 and
                         (i.Data_validita =
                             (SELECT
                                 MAX(Data_validita) AS Expr1
                             FROM            
                                 dbo.Iscrizioni
                             WHERE        
                                 (Anno_accademico = i.Anno_accademico) AND 
                                 (Cod_fiscale = i.Cod_fiscale) AND 
                                 (Data_validita <= '01/11/2024')
                             )
                         ) AND 
                         (a.Data_validita =
                             (SELECT        
                                 MAX(Data_validita) AS Expr1
                             FROM            
                                 dbo.Appartenenza
                             WHERE        
                                 (Anno_accademico = a.Anno_accademico) AND 
                                 (Cod_fiscale = a.Cod_fiscale) AND 
                                 (Data_validita <= '01/11/2024')
                             )
                         ) AND 
                         (c.Data_validita =
                             (SELECT        
                                 MAX(Data_validita) AS Expr1
                             FROM            
                                 dbo.Cittadinanza
                             WHERE        
                                 (Cod_fiscale = c.Cod_fiscale) AND 
                                 (Data_validita <= '01/11/2024')
                             )
                         ) and 
                         cod_beneficio ='ca' and 
	                     d.Cod_fiscale in (
				                    select
					                    d.Cod_fiscale
				                    from 
					                    dbo.Domanda AS d INNER JOIN
					                    dbo.Esiti_concorsi AS EC ON d.Anno_accademico = EC.Anno_accademico AND d.Num_domanda = EC.Num_domanda INNER JOIN
					                    dbo.Iscrizioni ON d.Anno_accademico = Iscrizioni.Anno_accademico AND d.Cod_fiscale = Iscrizioni.Cod_fiscale INNER JOIN
					                    Cittadinanza ON d.Cod_fiscale = Cittadinanza.Cod_fiscale
				                    where 
					                    d.anno_accademico='20232024' AND 
					                    (EC.Cod_beneficio = 'bs') AND 
					                    (EC.Cod_tipo_esito = 2) and
					                    iscrizioni.Cod_sede_studi in ('{code}') and
					                    (EC.Data_validita =
						                    (SELECT        
							                    MAX(Data_validita) AS Expr1
						                    FROM            
							                    dbo.Esiti_concorsi
						                    WHERE        
							                    (Anno_accademico = EC.Anno_accademico) AND 
							                    (Num_domanda = EC.Num_domanda) AND 
							                    (Cod_beneficio = EC.Cod_beneficio) AND 
							                    (Data_validita < '01/11/2024'))) and 
					                    Iscrizioni.Data_validita = 
						                    (Select max(data_validita) as expr1
						                    from
							                    Iscrizioni i
						                    where
							                    Iscrizioni.Anno_accademico = i.Anno_accademico and 
							                    Iscrizioni.Cod_fiscale = i.Cod_fiscale and
							                    Data_validita < '01/11/2024') and 
					                    Cittadinanza.Data_validita = 
						                    (Select max(data_validita) as expr1
						                    from
							                    Cittadinanza c
						                    where
							                    Cittadinanza.Cod_fiscale = c.Cod_fiscale and
							                    Data_validita < '01/11/2024')
		                    )

                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(11, 3).Value = sumImpBeneficioForCodTipoStudi;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(11, 4).Value = sumImpBeneficioForCodTipoStudi6;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(11, 5).Value = sumImpBeneficioForCodTipoStudi7;

            Logger.Log(75, folderName + ": Spese contributo alloggio borsisti", LogLevel.INFO);
        }
        private void TotalSpendingPL(IXLWorksheet excelWorksheet, string code, string folderName)
        {
            string query = $@"
                    SELECT        
	                    a.Cod_ente, 
	                    dbo.Sede_studi.Descrizione, 
	                    i.Cod_tipologia_studi, 
	                    d.Cod_fiscale, 
	                    dbo.Tipologie_studi.Descrizione AS Expr1, 
	                    dbo.Decod_cittadinanza.Descrizione AS Expr3, 
	                    vEsiti_concorsiPL_1.Cod_tipo_esito, 
	                    vEsiti_concorsiPL_1.Cod_beneficio, 
	                    vEsiti_concorsiPL_1.Imp_beneficio
                    FROM            
	                    dbo.Domanda AS d INNER JOIN
	                    dbo.Appartenenza AS a ON d.Anno_accademico = a.Anno_accademico AND d.Cod_fiscale = a.Cod_fiscale INNER JOIN
	                    dbo.Sede_studi ON a.Cod_sede_studi = dbo.Sede_studi.Cod_sede_studi INNER JOIN
	                    dbo.Iscrizioni AS i ON d.Anno_accademico = i.Anno_accademico AND d.Cod_fiscale = i.Cod_fiscale INNER JOIN
	                    dbo.Tipologie_studi ON i.Cod_tipologia_studi = dbo.Tipologie_studi.Cod_tipologia_studi INNER JOIN
	                    dbo.Cittadinanza AS c ON d.Cod_fiscale = c.Cod_fiscale INNER JOIN
	                    dbo.Decod_cittadinanza ON c.Cod_cittadinanza = dbo.Decod_cittadinanza.Cod_cittadinanza INNER JOIN
	                    dbo.vEsiti_concorsiPL AS vEsiti_concorsiPL_1 ON d.Num_domanda = vEsiti_concorsiPL_1.num_domandaBS AND d.Anno_accademico = vEsiti_concorsiPL_1.Anno_accademico
                    WHERE        
	                    (d.Anno_accademico = '20202021') and 
	                    i.Cod_sede_studi in ('{code}') AND 
	                    (i.Data_validita =
		                    (SELECT        MAX(Data_validita) AS Expr1
		                    FROM            dbo.Iscrizioni
		                    WHERE        (Anno_accademico = i.Anno_accademico) AND (Cod_fiscale = i.Cod_fiscale) AND (Data_validita <= '31/10/2024'))) AND 
	                    (a.Data_validita =
		                    (SELECT        MAX(Data_validita) AS Expr1
		                    FROM            dbo.Appartenenza
		                    WHERE        (Anno_accademico = a.Anno_accademico) AND (Cod_fiscale = a.Cod_fiscale) AND (Data_validita <= '31/10/2024'))) AND 
	                    (c.Data_validita =
		                    (SELECT        MAX(Data_validita) AS Expr1
		                    FROM            dbo.Cittadinanza
		                    WHERE        (Cod_fiscale = c.Cod_fiscale) AND (Data_validita <= '31/10/2024'))
	                    ) and Cod_tipo_esito=2
                
                ";

            // Execute SQL Query and get results
            DataTable queryResults = ExecuteSqlQuery(query, CONNECTION);

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi = queryResults.AsEnumerable()
                .Where(row => new[] { "3", "4", "5", "8", "10" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(14, 3).Value = sumImpBeneficioForCodTipoStudi;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi6 = queryResults.AsEnumerable()
                .Where(row => new[] { "6" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(14, 4).Value = sumImpBeneficioForCodTipoStudi6;

            // Summing the Imp_beneficio values for rows where Cod_tipologia_studi is one of the specified values
            decimal sumImpBeneficioForCodTipoStudi7 = queryResults.AsEnumerable()
                .Where(row => new[] { "7" }.Contains(row["Cod_tipologia_studi"].ToString()))
                .Sum(row => row.Field<decimal>("Imp_beneficio"));
            excelWorksheet.Cell(14, 5).Value = sumImpBeneficioForCodTipoStudi7;

            Logger.Log(99, folderName + ": Spese premio laurea", LogLevel.INFO);
        }

        private static DataTable ExecuteSqlQuery(string query, SqlConnection conn)
        {
            if (conn.State == ConnectionState.Closed)
            {
                conn.Open();
            }

            DataTable dataTable = new();
            using SqlCommand cmd = new(query, conn)
            {
                CommandTimeout = 900000
            };
            SqlDataAdapter da = new(cmd);
            da.Fill(dataTable);
            return dataTable;
        }
        private Dictionary<string, string> ReadExcelFile(string filePath)
        {
            Dictionary<string, string> data = new();

            using (XLWorkbook wb = new(filePath))
            {
                var ws = wb.Worksheets.First();
                var range = ws.RangeUsed();
                for (int row = 2; row <= range.RowCount(); row++)
                {
                    string code = ws.Cell(row, 1).GetValue<string>();
                    string name = ws.Cell(row, 2).GetValue<string>();
                    if (!data.ContainsKey(code))
                    {
                        data.Add(code, name);
                    }
                }
            }

            return data;
        }
        private static void CreateFolders(string basePath, Dictionary<string, string> data)
        {
            foreach (KeyValuePair<string, string> item in data)
            {
                string folderPath = Path.Combine(basePath, item.Value);
                if (Directory.Exists(folderPath))
                {
                    DirectoryInfo di = new(folderPath);
                    foreach (FileInfo file in di.GetFiles())
                    {
                        file.Delete();
                    }
                    foreach (DirectoryInfo dir in di.GetDirectories())
                    {
                        dir.Delete(true);
                    }
                }
                else
                {
                    Directory.CreateDirectory(folderPath);
                }
            }
        }
    }
}