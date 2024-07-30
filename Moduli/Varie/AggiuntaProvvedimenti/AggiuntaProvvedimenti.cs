using Microsoft.VisualBasic.ApplicationServices;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class AggiuntaProvvedimenti : BaseProcedure<ArgsAggiuntaProvvedimenti>
    {

        private readonly Dictionary<string, string> variazCodVariazioneItems = new()
        {
            {"01","Variazione generica"},
            {"02","Rinuncia"},
            {"03","Revoca"},
            {"04","Decadenza"},
            {"05","Riammissione come idoneo"},
            {"06","Riammissione come vincitore"},
            {"07","Da idoneo a vincitore"},
            {"08","Da vincitore a idoneo"},
            {"09","Rinuncia idoneità"},
            {"010","Rinuncia vincitore"},
            {"011","Revoca per incompatibilità col bando"},
            {"012","Da PENDOLARE a IN SEDE"},
            {"013","Da PENDOLARE a FUORI SEDE"},
            {"014","Da FUORI SEDE a PENDOLARE"},
            {"015","Da FUORI SEDE a IN SEDE"},
            {"016","Da IN SEDE a FUORI SEDE"},
            {"017","Da IN SEDE a PENDOLARE"},
            {"018","Revoca per sede distaccata"},
            {"019","Revoca per mancata iscrizione"},
            {"020","Revoca per studente iscritto ripetente"},
            {"021","Revoca per ISEE non presente in banca dati"},
            {"022","Revoca per studente già laureato"},
            {"023","Revoca per patrimonio oltre il limite"},
            {"024","Revoca per reddito oltre il limite"},
            {"025","Revoca per mancanza esami o crediti"},
            {"026","Rinuncia a tutti i benefici"},
            {"027","Revoca per iscrizione fuori temine"},
            {"028","Revoca per ISEE fuori termine"},
            {"029","Revoca per ISEE non prodotta"},
            {"030","Decadenza per mancata comunicazione modalità di pagamento"},
            {"031","Revoca per mancanza contratto di affitto"},
            {"032","Variazione I.S.E.E."},
            {"033","Revoca premio di laurea"},
            {"034","Rinuncia premio di laurea"},
            {"035","Riammissione come idoneo premio di laurea"},
            {"036","Riammisione come vincitore premio di laurea"},
        };
        private readonly Dictionary<string, string> variazCodBeneficioItems = new()
        {
            { "00", "Tutti i benefici" },
            { "BS", "Borsa di studio" },
            { "PA", "Posto alloggio" },
            { "CI", "Contributo integrativo" },
        };

        public AggiuntaProvvedimenti(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        private List<string> _studentInformation = new List<string>();
        Dictionary<string, double> _studenteImportoPairs = new Dictionary<string, double>();
        Dictionary<string, string> _studenteFondoPairs = new Dictionary<string, string>();
        Dictionary<string, List<bool>> _studenteChecks = new Dictionary<string, List<bool>>();
        private ManualResetEvent _waitHandle = new ManualResetEvent(false);

        SqlTransaction? sqlTransaction = null;

        string selectedFolderPath = string.Empty;
        string numProvvedimento = string.Empty;
        string aaProvvedimento = string.Empty;
        string dataProvvedimento = string.Empty;
        string provvedimentoSelezionato = string.Empty;
        string notaProvvedimento = string.Empty;
        string beneficioProvvedimento = string.Empty;
        bool requireNuovaSpecifica;

        string tipoFondo = string.Empty;
        string capitolo = string.Empty;
        string esePR = string.Empty;
        string eseSA = string.Empty;
        string impegnoPR = string.Empty;
        string impegnoSA = string.Empty;

        public override void RunProcedure(ArgsAggiuntaProvvedimenti args)
        {
            try
            {


                if (CONNECTION == null)
                {
                    Logger.LogDebug(null, "Uscita anticipata da RunProcedure: connessione o transazione null");
                    return;
                }

                selectedFolderPath = args._selectedFolderPath;
                numProvvedimento = args._numProvvedimento;
                aaProvvedimento = args._aaProvvedimento;
                dataProvvedimento = args._dataProvvedimento;
                provvedimentoSelezionato = args._provvedimentoSelezionato;
                notaProvvedimento = args._notaProvvedimento;
                beneficioProvvedimento = args._beneficioProvvedimento;
                requireNuovaSpecifica = args._requireNuovaSpecifica;
                capitolo = args._capitolo;
                esePR = args._esePR;
                eseSA = args._eseSA;
                impegnoPR = args._impegnoPR;
                impegnoSA = args._impegnoSA;
                tipoFondo = args._tipoFondo;

                string pattern = $@"\bdet{numProvvedimento}\b";
                // Get all the subdirectories in the selectedFolderPath
                string[] subDirectories = Directory.GetDirectories(selectedFolderPath);

                string foundFolder = string.Empty;

                // Iterate through each subdirectory
                foreach (string subDirectory in subDirectories)
                {
                    // Get the name of the subdirectory
                    string directoryName = Path.GetFileName(subDirectory);

                    // Check if the directory name contains the numProvvedimento
                    if (Regex.IsMatch(directoryName, pattern))
                    {
                        Logger.Log(0, directoryName, LogLevel.INFO);
                        foundFolder = subDirectory;
                        // Perform your logic here when a match is found
                        break;
                    }
                }

                if (foundFolder == string.Empty)
                {
                    _masterForm.inProcedure = false;
                    return;
                }

                // Get all Excel files in the foundFolder
                string[] excelFilePaths = Directory.GetFiles(foundFolder, "*.xls*");

                // Check if any Excel files were found
                if (excelFilePaths.Length == 0)
                {
                    _masterForm.inProcedure = false;
                    return;
                }

                Panel? provvedimentiPanel = null;
                _masterForm.Invoke((MethodInvoker)delegate
                {
                    provvedimentiPanel = _masterForm.GetProcedurePanel();
                });
                foreach (string excelFilePath in excelFilePaths)
                {
                    if (excelFilePath.StartsWith("~"))
                    {
                        continue;
                    }
                    DataTable allStudentsData = Utilities.ReadExcelToDataTable(excelFilePath, true);
                    DataGridView? studentsGridView = null;
                    if (beneficioProvvedimento == "BS")
                    {
                        MessageBox.Show("Selezionare il primo numero domanda", "Seleziona", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        studentsGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, provvedimentiPanel, OnNumDomandaClicked);
                    }
                    else
                    {
                        MessageBox.Show("Selezionare il primo codice fiscale", "Seleziona", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        studentsGridView = Utilities.CreateDataGridView(allStudentsData, _masterForm, provvedimentiPanel, OnCodFiscaleClicked);
                    }

                    _waitHandle.Reset();
                    _waitHandle.WaitOne();
                    _masterForm.Invoke((MethodInvoker)delegate
                    {
                        studentsGridView.Dispose();
                    });

                    HandleProvvedimenti(numProvvedimento, aaProvvedimento, dataProvvedimento, provvedimentoSelezionato, notaProvvedimento);
                    HandleSpecificheImpegni(excelFilePath);
                }

                Logger.Log(100, "Fine lavorazione", LogLevel.INFO);
                _masterForm.inProcedure = false;
            }
            catch
            {
                sqlTransaction.Rollback();
                throw;
            }
        }

        private void OnNumDomandaClicked(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    _studentInformation = new List<string>();

                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        var cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (cellValue != null)
                        {
                            string? numDomanda = cellValue.ToString();
                            if (string.IsNullOrEmpty(numDomanda) || !int.TryParse(numDomanda, out int _))
                            {
                                continue;
                            }
                            _studentInformation.Add(numDomanda);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }

        private void OnCodFiscaleClicked(object? sender, DataGridViewCellEventArgs e)
        {
            if (sender is DataGridView dataGridView)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                {
                    _studentInformation = new List<string>();

                    for (int i = e.RowIndex; i < dataGridView.Rows.Count; i++)
                    {
                        var cellValue = dataGridView.Rows[i].Cells[e.ColumnIndex].Value;
                        if (cellValue != null)
                        {
                            string? codFiscale = cellValue.ToString();
                            if (string.IsNullOrEmpty(codFiscale))
                            {
                                continue;
                            }
                            _studentInformation.Add(codFiscale);
                        }
                    }
                }
            }
            _waitHandle.Set();
        }

        private void HandleProvvedimenti(string numProvvedimento, string aaProvvedimento, string dataProvvedimento, string provvedimentoSelezionato, string notaProvvedimento)
        {
            try
            {
                sqlTransaction = CONNECTION.BeginTransaction();
                List<string> localStudentInfo = _studentInformation;

                if (beneficioProvvedimento != "BS")
                {
                    localStudentInfo = new List<string>();
                    string fiscalCodesString = string.Join(", ", _studentInformation.Select(fiscalCode => $"'{fiscalCode}'"));
                    string retrieveNumDomandaQuery = $@"
                        SELECT Domanda.num_domanda
                        FROM Domanda
                        WHERE Cod_fiscale in ({fiscalCodesString}) 
                        AND Anno_accademico = '{aaProvvedimento}'
                        AND Tipo_bando = 'LZ'
                    ";

                    using SqlCommand command = new(retrieveNumDomandaQuery, CONNECTION, sqlTransaction);
                    using SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        localStudentInfo.Add(Utilities.SafeGetString(reader, "Num_domanda"));
                    }

                }

                // Convert the list of fiscal codes to a single string
                string numDomandaString = string.Join(", ", localStudentInfo.Select(numDom => $"'{numDom}'"));
                string retrieveQuery = $@"
                    SELECT Domanda.Num_domanda
                    FROM Domanda INNER JOIN
                         PROVVEDIMENTI ON Domanda.Num_domanda = PROVVEDIMENTI.Num_domanda AND Domanda.Anno_accademico = PROVVEDIMENTI.Anno_accademico
                    WHERE (Domanda.Anno_accademico = '{aaProvvedimento}') 
                          and PROVVEDIMENTI.Anno_accademico = '{aaProvvedimento}'
                          and num_provvedimento = '{numProvvedimento}'
                          AND (Domanda.Tipo_bando = 'lz')
                          AND (Domanda.Num_domanda IN ({numDomandaString}))
                ";

                List<string> retrievedNumDomandas = new();

                using (SqlCommand command = new(retrieveQuery, CONNECTION, sqlTransaction))
                {
                    using SqlDataReader reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        retrievedNumDomandas.Add(Utilities.SafeGetString(reader, "Num_domanda"));
                    }
                }

                // Reporting the fiscal codes that are in both lists
                List<string> commonCodes = _studentInformation.Intersect(retrievedNumDomandas).ToList();
                foreach (string code in commonCodes)
                {
                    Logger.Log(30, code + " - Provvedimento #" + numProvvedimento + " già aggiunto", LogLevel.INFO);
                }

                if (commonCodes.Count > 0 && (provvedimentoSelezionato == "01" || provvedimentoSelezionato == "02"))
                {
                    string commonCodesString = string.Join(", ", commonCodes.Select(numDom => $"'{numDom}'"));
                    string removeBlockCommonQuery = $@"
                        UPDATE Motivazioni_blocco_pagamenti
                        SET Blocco_pagamento_attivo = 0, 
                            Data_fine_validita = CURRENT_TIMESTAMP, 
                            Utente_sblocco = 'Area4'
                        WHERE Anno_accademico = '{aaProvvedimento}' 
                            AND Cod_tipologia_blocco = 'BRM' 
		                    and Blocco_pagamento_attivo=1
                            AND Num_domanda IN 
                                (SELECT Num_domanda
                                 FROM Domanda
                                 WHERE Anno_accademico = '{aaProvvedimento}'
                                     AND tipo_bando IN ('lz') 
                                     AND Domanda.Num_domanda IN 
                                         ({commonCodesString}));

	                    INSERT INTO DatiGenerali_dom ([Anno_accademico], [Num_domanda], [Status_domanda], [Tipo_studente], [Rifug_politico], [Tutelato], [Num_anni_conferma], [Straniero_povero], [Reddito_2_anni], [Residenza_est_da], [Firmata], [Straniero_fam_res_ita], [Fotocopia], [Firmata_genitore], [Cert_storico_ana], [Doc_consolare], [Doc_consolare_provv], [Permesso_sogg], [Permesso_sogg_provv], [Numero_componenti_nucleo_familiare], [SEQ], [Nubile_prole], [Fuori_termine], [Invalido], [Status_sede_stud], [Superamento_esami], [Superamento_esami_tassa_reg], [Appartenente_UE], [Selezionato_CEE], [Conferma_PA], [Matricola_studente], [Incompatibilita_con_bando], [Note_ufficio], [Domanda_sanata], [Data_validita], [Utente], [Conferma_reddito], [Pagamento_tassareg], [Blocco_pagamento], [Domanda_senza_documentazione], [Esame_complementare], [Esami_fondamentali], [Percorrenza_120_minuti], [Distanza_50KM_sede], [Iscrizione_FuoriTermine], [Autorizzazione_invio], [Nubile_prole_calcolata], [Possesso_altra_borsa], [Studente_detenuto], [esonero_pag_tassa_reg], [presentato_contratto], [presentato_doc_cred_rim], [tipo_doc_cred_rim], [n_sentenza_divsep], [anno_sentenza_divsep], [Id_Domanda], [Inserimento_PEC], [Rinuncia_in_corso], [Doppia_borsa], [Posto_alloggio_confort], [RichiestaMensa])
	                    SELECT distinct Domanda.Anno_accademico, Domanda.Num_domanda, vDATIGENERALI_dom.Status_domanda, vDATIGENERALI_dom.Tipo_studente, vDATIGENERALI_dom.Rifug_politico, vDATIGENERALI_dom.Tutelato, vDATIGENERALI_dom.Num_anni_conferma, vDATIGENERALI_dom.Straniero_povero, vDATIGENERALI_dom.Reddito_2_anni, vDATIGENERALI_dom.Residenza_est_da, vDATIGENERALI_dom.Firmata, vDATIGENERALI_dom.Straniero_fam_res_ita, vDATIGENERALI_dom.Fotocopia, vDATIGENERALI_dom.Firmata_genitore, vDATIGENERALI_dom.Cert_storico_ana, vDATIGENERALI_dom.Doc_consolare, vDATIGENERALI_dom.Doc_consolare_provv, vDATIGENERALI_dom.Permesso_sogg, vDATIGENERALI_dom.Permesso_sogg_provv, vDATIGENERALI_dom.Numero_componenti_nucleo_familiare, vDATIGENERALI_dom.SEQ, vDATIGENERALI_dom.Nubile_prole, vDATIGENERALI_dom.Fuori_termine, vDATIGENERALI_dom.Invalido, vDATIGENERALI_dom.Status_sede_stud, vDATIGENERALI_dom.Superamento_esami, vDATIGENERALI_dom.Superamento_esami_tassa_reg, vDATIGENERALI_dom.Appartenente_UE, vDATIGENERALI_dom.Selezionato_CEE, vDATIGENERALI_dom.Conferma_PA, vDATIGENERALI_dom.Matricola_studente, vDATIGENERALI_dom.Incompatibilita_con_bando, vDATIGENERALI_dom.Note_ufficio, vDATIGENERALI_dom.Domanda_sanata, CURRENT_TIMESTAMP, 'Area4', vDATIGENERALI_dom.Conferma_reddito, vDATIGENERALI_dom.Pagamento_tassareg, 0, vDATIGENERALI_dom.Domanda_senza_documentazione, vDATIGENERALI_dom.Esame_complementare, vDATIGENERALI_dom.Esami_fondamentali, vDATIGENERALI_dom.Percorrenza_120_minuti, vDATIGENERALI_dom.Distanza_50KM_sede, vDATIGENERALI_dom.Iscrizione_FuoriTermine, vDATIGENERALI_dom.Autorizzazione_invio, vDATIGENERALI_dom.Nubile_prole_calcolata, vDATIGENERALI_dom.Possesso_altra_borsa, vDATIGENERALI_dom.Studente_detenuto, vDATIGENERALI_dom.esonero_pag_tassa_reg, vDATIGENERALI_dom.presentato_contratto, vDATIGENERALI_dom.presentato_doc_cred_rim, vDATIGENERALI_dom.tipo_doc_cred_rim, vDATIGENERALI_dom.n_sentenza_divsep, vDATIGENERALI_dom.anno_sentenza_divsep, vDATIGENERALI_dom.Id_Domanda, vDATIGENERALI_dom.Inserimento_PEC, vDATIGENERALI_dom.Rinuncia_in_corso, vDATIGENERALI_dom.Doppia_borsa, vDATIGENERALI_dom.Posto_alloggio_confort, vDATIGENERALI_dom.RichiestaMensa 
	                    FROM 
		                    Domanda INNER JOIN vDATIGENERALI_dom ON Domanda.Anno_accademico = vDATIGENERALI_dom.Anno_accademico AND 
		                    Domanda.Num_domanda = vDATIGENERALI_dom.Num_domanda 
	                    WHERE 
		                    (Domanda.Anno_accademico = '{aaProvvedimento}' and 
		                    tipo_bando in ('lz','l2') AND 
		                    Domanda.Num_domanda in ({commonCodesString}) and
		                    Domanda.Num_domanda not in (SELECT DISTINCT Num_domanda
                                 FROM Motivazioni_blocco_pagamenti
                                 WHERE Anno_accademico = '{aaProvvedimento}'
                                     AND Data_fine_validita IS NULL 
                                     AND Blocco_pagamento_attivo = 1))
                        ";
                    using SqlCommand command = new(removeBlockCommonQuery, CONNECTION, sqlTransaction);
                    _ = command.ExecuteNonQuery();

                }

                // Using the remaining codes in the second query
                List<string> remainingCodes = localStudentInfo.Except(commonCodes).ToList();

                if (remainingCodes.Any())
                {
                    string remainingCodesString = string.Join(", ", remainingCodes.Select(numDom => $"'{numDom}'"));

                    string insertQuery = $@"
                        INSERT INTO [dbo].[PROVVEDIMENTI]
                                   ([Num_domanda], [tipo_provvedimento], [data_provvedimento], [Anno_accademico], [note], [num_provvedimento], [riga_valida], [data_validita])
                        SELECT 
                            domanda.num_domanda, 
                            '{provvedimentoSelezionato}', 
                            '{dataProvvedimento}', 
                            '{aaProvvedimento}', 
                            '{notaProvvedimento}', 
                            {numProvvedimento}, 
                            1, 
                            CURRENT_TIMESTAMP 
                        FROM           
                            Domanda
                        WHERE        
                            (Domanda.Anno_accademico = '{aaProvvedimento}') 
                            AND (Domanda.Tipo_bando = 'lz') 
                            AND (Domanda.Num_domanda IN ({remainingCodesString}))";
                    foreach (string code in remainingCodes)
                    {
                        Logger.Log(40, code + ": aggiunto provvedimento #" + numProvvedimento, LogLevel.INFO);
                    }
                    using (SqlCommand command = new(insertQuery, CONNECTION, sqlTransaction))
                    {
                        int affectedRows = command.ExecuteNonQuery();

                        // Report the number of affected rows
                        Logger.Log(60, $"Modificati: {affectedRows} studenti", LogLevel.INFO);
                    }

                    if ((provvedimentoSelezionato == "01" || provvedimentoSelezionato == "02"))
                    {
                        string removeBlockCommonQuery = $@"
                        UPDATE Motivazioni_blocco_pagamenti
                        SET Blocco_pagamento_attivo = 0, 
                            Data_fine_validita = CURRENT_TIMESTAMP, 
                            Utente_sblocco = 'Area4'
                        WHERE Anno_accademico = '{aaProvvedimento}' 
                            AND Cod_tipologia_blocco = 'BRM' 
		                    and Blocco_pagamento_attivo=1
                            AND Num_domanda IN 
                                (SELECT Num_domanda
                                 FROM Domanda
                                 WHERE Anno_accademico = '{aaProvvedimento}'
                                     AND tipo_bando IN ('lz') 
                                     AND Domanda.Num_domanda IN 
                                         ({remainingCodesString}))

	                    INSERT INTO DatiGenerali_dom ([Anno_accademico], [Num_domanda], [Status_domanda], [Tipo_studente], [Rifug_politico], [Tutelato], [Num_anni_conferma], [Straniero_povero], [Reddito_2_anni], [Residenza_est_da], [Firmata], [Straniero_fam_res_ita], [Fotocopia], [Firmata_genitore], [Cert_storico_ana], [Doc_consolare], [Doc_consolare_provv], [Permesso_sogg], [Permesso_sogg_provv], [Numero_componenti_nucleo_familiare], [SEQ], [Nubile_prole], [Fuori_termine], [Invalido], [Status_sede_stud], [Superamento_esami], [Superamento_esami_tassa_reg], [Appartenente_UE], [Selezionato_CEE], [Conferma_PA], [Matricola_studente], [Incompatibilita_con_bando], [Note_ufficio], [Domanda_sanata], [Data_validita], [Utente], [Conferma_reddito], [Pagamento_tassareg], [Blocco_pagamento], [Domanda_senza_documentazione], [Esame_complementare], [Esami_fondamentali], [Percorrenza_120_minuti], [Distanza_50KM_sede], [Iscrizione_FuoriTermine], [Autorizzazione_invio], [Nubile_prole_calcolata], [Possesso_altra_borsa], [Studente_detenuto], [esonero_pag_tassa_reg], [presentato_contratto], [presentato_doc_cred_rim], [tipo_doc_cred_rim], [n_sentenza_divsep], [anno_sentenza_divsep], [Id_Domanda], [Inserimento_PEC], [Rinuncia_in_corso], [Doppia_borsa], [Posto_alloggio_confort], [RichiestaMensa])
	                    SELECT distinct Domanda.Anno_accademico, Domanda.Num_domanda, vDATIGENERALI_dom.Status_domanda, vDATIGENERALI_dom.Tipo_studente, vDATIGENERALI_dom.Rifug_politico, vDATIGENERALI_dom.Tutelato, vDATIGENERALI_dom.Num_anni_conferma, vDATIGENERALI_dom.Straniero_povero, vDATIGENERALI_dom.Reddito_2_anni, vDATIGENERALI_dom.Residenza_est_da, vDATIGENERALI_dom.Firmata, vDATIGENERALI_dom.Straniero_fam_res_ita, vDATIGENERALI_dom.Fotocopia, vDATIGENERALI_dom.Firmata_genitore, vDATIGENERALI_dom.Cert_storico_ana, vDATIGENERALI_dom.Doc_consolare, vDATIGENERALI_dom.Doc_consolare_provv, vDATIGENERALI_dom.Permesso_sogg, vDATIGENERALI_dom.Permesso_sogg_provv, vDATIGENERALI_dom.Numero_componenti_nucleo_familiare, vDATIGENERALI_dom.SEQ, vDATIGENERALI_dom.Nubile_prole, vDATIGENERALI_dom.Fuori_termine, vDATIGENERALI_dom.Invalido, vDATIGENERALI_dom.Status_sede_stud, vDATIGENERALI_dom.Superamento_esami, vDATIGENERALI_dom.Superamento_esami_tassa_reg, vDATIGENERALI_dom.Appartenente_UE, vDATIGENERALI_dom.Selezionato_CEE, vDATIGENERALI_dom.Conferma_PA, vDATIGENERALI_dom.Matricola_studente, vDATIGENERALI_dom.Incompatibilita_con_bando, vDATIGENERALI_dom.Note_ufficio, vDATIGENERALI_dom.Domanda_sanata, CURRENT_TIMESTAMP, 'Area4', vDATIGENERALI_dom.Conferma_reddito, vDATIGENERALI_dom.Pagamento_tassareg, 0, vDATIGENERALI_dom.Domanda_senza_documentazione, vDATIGENERALI_dom.Esame_complementare, vDATIGENERALI_dom.Esami_fondamentali, vDATIGENERALI_dom.Percorrenza_120_minuti, vDATIGENERALI_dom.Distanza_50KM_sede, vDATIGENERALI_dom.Iscrizione_FuoriTermine, vDATIGENERALI_dom.Autorizzazione_invio, vDATIGENERALI_dom.Nubile_prole_calcolata, vDATIGENERALI_dom.Possesso_altra_borsa, vDATIGENERALI_dom.Studente_detenuto, vDATIGENERALI_dom.esonero_pag_tassa_reg, vDATIGENERALI_dom.presentato_contratto, vDATIGENERALI_dom.presentato_doc_cred_rim, vDATIGENERALI_dom.tipo_doc_cred_rim, vDATIGENERALI_dom.n_sentenza_divsep, vDATIGENERALI_dom.anno_sentenza_divsep, vDATIGENERALI_dom.Id_Domanda, vDATIGENERALI_dom.Inserimento_PEC, vDATIGENERALI_dom.Rinuncia_in_corso, vDATIGENERALI_dom.Doppia_borsa, vDATIGENERALI_dom.Posto_alloggio_confort , vDATIGENERALI_dom.RichiestaMensa 
	                    FROM 
		                    Domanda INNER JOIN vDATIGENERALI_dom ON Domanda.Anno_accademico = vDATIGENERALI_dom.Anno_accademico AND 
		                    Domanda.Num_domanda = vDATIGENERALI_dom.Num_domanda 
	                    WHERE 
		                    (Domanda.Anno_accademico = '{aaProvvedimento}' and 
		                    tipo_bando in ('lz','l2') AND 
		                    Domanda.Num_domanda in ({remainingCodesString}) and
		                    Domanda.Num_domanda not in (SELECT DISTINCT Num_domanda
                                 FROM Motivazioni_blocco_pagamenti
                                 WHERE Anno_accademico = '{aaProvvedimento}'
                                     AND Data_fine_validita IS NULL 
                                     AND Blocco_pagamento_attivo = 1))
                        ";
                        using (SqlCommand command = new(removeBlockCommonQuery, CONNECTION, sqlTransaction))
                        {
                            _ = command.ExecuteNonQuery();
                        }
                    }
                }
                else
                {
                    Logger.Log(100, "Nessun provvedimento da aggiungere", LogLevel.INFO);
                }


                Logger.Log(80, "Studenti nel file: " + _studentInformation.Count().ToString(), LogLevel.INFO);
                sqlTransaction.Commit();
            }
            catch
            {
                throw;
            }
        }
        private void HandleSpecificheImpegni(string selectedFile)
        {

            Dictionary<string, string> provvedimentiItems = new()
            {
                { "00", "Varie" },
                { "01", "Riammissione come vincitore" },
                { "02", "Riammissione come idoneo" },
                { "03", "Revoca senza recupero somme" },
                { "04", "Decadenza" },
                { "05", "Modifica importo" },
                { "06", "Revoca con recupero somme" },
                { "07", "Pagamento" },
                { "08", "Rinuncia" },
                { "09", "Da idoneo a vincitore" },
                { "10", "Rinuncia con recupero somme" },
                { "11", "Rinuncia senza recupero somme" },
                { "12", "Rimborso tassa regionale indebitamente pagata" },
                { "13", "Cambio status sede" }
            };


            bool needNewSpecific = false;

            switch (provvedimentoSelezionato)
            {
                case "01": //Riammissione come vincitore
                case "02": //Riammissione come idoneo
                case "05": //Modifica importo 
                case "09": //Da idoneo a vincitore
                case "13": //Cambio status sede
                    needNewSpecific = true;
                    break;
                case "07": //Pagamento
                case "12": //Rimborso tassa regionale indebitamente pagata
                    break;
                case "04": //Decadenza
                case "03": //Revoca senza recupero somme
                case "06": //Revoca con recupero somme
                case "08": //Rinuncia
                case "10": //Rinuncia con recupero somme
                case "11": //Rinuncia senza recupero somme
                    break;
            }

            string selectedFilePath = "";
            ArgsSpecificheImpegni argsSpecificheImpegni = new ArgsSpecificheImpegni
            {
                _selectedFile = selectedFile,
                _selectedDate = dataProvvedimento,
                _tipoFondo = tipoFondo,
                _aperturaNuovaSpecifica = needNewSpecific,
                _capitolo = capitolo,
                _descrDetermina = notaProvvedimento,
                _esePR = esePR,
                _eseSA = eseSA,
                _impegnoPR = impegnoPR,
                _impegnoSA = impegnoSA,
                _numDetermina = numProvvedimento,
                _selectedAA = aaProvvedimento,
                _selectedCodBeneficio = beneficioProvvedimento
            };
            SpecificheImpegni specificheImpegni = new(_masterForm, CONNECTION);
            specificheImpegni.RunProcedure(argsSpecificheImpegni);
        }

    }
}
