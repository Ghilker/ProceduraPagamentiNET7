using System.Data;
using System.Data.SqlClient;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace ProcedureNet7
{
    internal class EstrazioneStudenti : BaseProcedure<ArgsEstrazioneStudenti>
    {
        public EstrazioneStudenti(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }

        public override void RunProcedure(ArgsEstrazioneStudenti args)
        {
            string folderPath = args._folderPath;
            List<bool> queryArguments = args._queryArguments;
            string selectedCodEnti = args._selectedCodEnti;
            string selectedTipoCorso = args._selectedTipoCorso;
            string selectedEsitiBorsa = args._selectedEsitiBorsa;
            string selectedAnnoCorso = args._selectedAnnoCorso;
            string annoAccademico = args._annoAccademico;
            string fiscalCodesFilePath = args._fiscalCodesFilePath;
            string selectedStatusSede = args._selectedStatusSede;
            string selectedCittadinanza = args._selectedCittadinanza;
            string selectedBlocco = args._selectedBlocco;
            string selectedCodComune = args._selectedCodComune;

            // _masterForm.inProcedure = true;
            // Start building the SQL query
            StringBuilder queryBuilder = new();

            if (queryArguments.Count > 0 && queryArguments[7])
            {
                _ = queryBuilder.Append("SET NOCOUNT ON; DECLARE @CFEstrazione dbo.CFEstrazione; ");

                // Using Utilities.ExcelToDataTable to convert Excel file to DataTable
                DataTable dataTable = Utilities.ReadExcelToDataTable(fiscalCodesFilePath);

                const int batchSize = 1000;
                int batchCount = 0;

                // Iterate through the DataTable rows, starting from the second row (index 1)
                for (int i = 0; i < dataTable.Rows.Count; i++)
                {
                    DataRow row = dataTable.Rows[i];

                    if (batchCount >= batchSize)
                    {
                        queryBuilder.Length--; // Remove last comma
                        _ = queryBuilder.Append("; ");
                        batchCount = 0;
                    }

                    if (batchCount == 0)
                    {
                        _ = queryBuilder.Append("INSERT INTO @CFEstrazione (Cod_fiscale) VALUES ");
                    }

                    string value = row[0]?.ToString().Replace("'", "''") ?? string.Empty;
                    _ = queryBuilder.Append("('").Append(value).Append("'),");
                    batchCount++;
                }

                queryBuilder.Length--; // Remove last comma
                _ = queryBuilder.Append("; ");
            }

            _ = queryBuilder.AppendLine(@"
                                        SELECT        
                                            Domanda.Anno_accademico,");
            if (queryArguments.Count > 0 && queryArguments[0])
            {
                _ = queryBuilder.AppendLine(@"   
                                            Sede_studi.Cod_ente as Cod_ente,");
                _ = queryBuilder.AppendLine(@"   
                                            Sede_studi.Cod_sede_studi As Cod_sede_studi,");
            }


            _ = queryBuilder.AppendLine(@"      
                                            Sede_studi.Descrizione AS Ente_gestione,
                                            Domanda.Num_domanda, 
                                            Studente.Codice_Studente, 
                                            vDATIGENERALI_dom.Matricola_studente, 
                                            Domanda.Cod_fiscale, 
                                            Studente.Cognome, 
                                            Studente.Nome, 
                                            FORMAT(CONVERT(DATETIME, Studente.Data_nascita), 'dd/MM/yyyy') AS Data_nascita,
                                            vResidenza.comune_residenza,");

            if (queryArguments.Count > 0 && queryArguments[0])
            {
                _ = queryBuilder.AppendLine(@"
                                            vResidenza.COD_COMUNE,");
            }

            _ = queryBuilder.AppendLine(@" 
                                            Tipologie_status_sede.Descrizione AS Status_sede,");

            // Add Studente.Sesso column based on the first boolean argument
            if (queryArguments.Count > 0 && queryArguments[0])
            {
                _ = queryBuilder.AppendLine(@"   
                                            Studente.Sesso,
                                            vDATIGENERALI_dom.Invalido,");
            }

            // Continue building the query
            _ = queryBuilder.AppendLine(@"      
                                            Decod_cittadinanza.Descrizione AS Nazionalità,");

            if (queryArguments.Count > 0 && queryArguments[0])
            {
                _ = queryBuilder.AppendLine(@"
                                            CASE WHEN vCittadinanza.cod_cittadinanza IN 
	                                        (
		                                        'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'Z149', 'Z105', 'Z146'
	                                        ) THEN 'UE' 
	                                        ELSE 'NOTUE' 
	                                        END AS codeu,");
            }

            if (queryArguments.Count > 0 && queryArguments[0])
            {
                _ = queryBuilder.AppendLine(@"   
                                            Corsi_laurea.Corso_Stem,");
            }

            _ = queryBuilder.AppendLine(@"      
                                            Facolta.Descrizione AS Facoltà, 
                                            Corsi_laurea.Descrizione AS Corso_di_laurea, 
                                            Tipologie_studi.Descrizione AS Tipo_corso,");
            if (queryArguments.Count > 0 && queryArguments[0])
            {
                _ = queryBuilder.AppendLine(@"
                                            vIscrizioni.Cod_tipologia_studi As Cod_tipo_corso,");
            }
            if (queryArguments.Count > 0 && queryArguments[6])
            {
                _ = queryBuilder.AppendLine(@"
                                            vDATIGENERALI_dom.Superamento_esami,
                                            vDATIGENERALI_dom.Superamento_esami_tassa_reg,");
            }
            _ = queryBuilder.AppendLine($@"      
                                            vValori_calcolati.Anno_corso,
                                            vMerito.Anno_immatricolaz,
                                            vMerito.Numero_crediti AS Totale_crediti, 
                                            vMerito.Crediti_extra_curriculari AS Crediti_extra_curriculari, 
                                            vMerito.Crediti_riconosciuti_da_rinuncia AS Crediti_riconosciuti_da_rinuncia,
                                            vIscrizioni.Crediti_tirocinio,
                                            vEsiti_concorsiBS.esito_BS AS Esito_borsa_studio,
                                            CASE
                                                WHEN vEsiti_concorsiPA.esito_PA IS NULL THEN 'Non Richiesto'
                                                ELSE vEsiti_concorsiPA.esito_PA
                                            END AS Esito_posto_alloggio,
                                            dbo.SlashBlocchi(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS Cod_blocchi,
                                            dbo.SlashDescrBlocchi(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS Descr_blocchi,
                                            dbo.SlashMotiviEsclusioneTest(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS Motivi_esclusione
                                        FROM            
                                            Domanda 
		                                    INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
		                                    INNER JOIN vValori_calcolati ON Domanda.Anno_accademico = vValori_calcolati.Anno_accademico AND Domanda.Num_domanda = vValori_calcolati.Num_domanda
		                                    INNER JOIN vIscrizioni ON Domanda.Anno_accademico = vIscrizioni.Anno_accademico AND Domanda.Cod_fiscale = vIscrizioni.Cod_fiscale 
		                                    INNER JOIN Facolta ON vIscrizioni.Cod_facolta = Facolta.Cod_facolta AND vIscrizioni.Cod_sede_studi = Facolta.Cod_sede_studi 
		                                    INNER JOIN Tipologie_studi ON vIscrizioni.Cod_tipologia_studi = Tipologie_studi.Cod_tipologia_studi 
		                                    INNER JOIN Corsi_laurea ON vIscrizioni.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea 
			                                    AND vIscrizioni.Anno_accad_inizio = Corsi_laurea.Anno_accad_inizio 
			                                    AND vIscrizioni.Cod_tipo_ordinamento = Corsi_laurea.Cod_tipo_ordinamento 
			                                    AND vIscrizioni.Cod_facolta = Corsi_laurea.Cod_facolta 
			                                    AND vIscrizioni.Cod_sede_studi = Corsi_laurea.Cod_sede_studi 
			                                    AND vIscrizioni.Cod_tipologia_studi = Corsi_laurea.Cod_tipologia_studi 
		                                    INNER JOIN vMerito ON Domanda.Anno_accademico = vMerito.Anno_accademico AND Domanda.Num_domanda = vMerito.Num_domanda 
		                                    INNER JOIN vDATIGENERALI_dom ON Domanda.Anno_accademico = vDATIGENERALI_dom.Anno_accademico AND Domanda.Num_domanda = vDATIGENERALI_dom.Num_domanda 
		                                    INNER JOIN vCittadinanza ON Domanda.Cod_fiscale = vCittadinanza.Cod_fiscale 
		                                    INNER JOIN Decod_cittadinanza ON vCittadinanza.Cod_cittadinanza = Decod_cittadinanza.Cod_cittadinanza 
		                                    LEFT OUTER JOIN vEsiti_concorsiBS ON Domanda.Anno_accademico = vEsiti_concorsiBS.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiBS.Num_domanda 
		                                    INNER JOIN vAppartenenza ON Domanda.Cod_fiscale = vAppartenenza.Cod_fiscale AND Domanda.Anno_accademico = vAppartenenza.Anno_accademico 
		                                    INNER JOIN Sede_studi ON vIscrizioni.Cod_sede_studi = Sede_studi.Cod_sede_studi
                                            INNER JOIN Tipologie_status_sede ON vValori_calcolati.Status_sede = Tipologie_status_sede.Status_sede
                                            INNER JOIN vResidenza ON Domanda.Anno_accademico = vResidenza.ANNO_ACCADEMICO AND Domanda.Cod_fiscale = vResidenza.COD_FISCALE
                                            ");

            if (queryArguments.Count > 0 && queryArguments[7])
            {
                _ = queryBuilder.AppendLine(@"
                                            INNER JOIN @CFEstrazione f ON Domanda.Cod_fiscale = f.Cod_fiscale");
            }

            if (!string.IsNullOrEmpty(selectedBlocco))
            {
                _ = queryBuilder.AppendLine(@"
                                            INNER JOIN vMotivazioni_blocco_pagamenti ON Domanda.Num_domanda = vMotivazioni_blocco_pagamenti.Num_domanda AND Domanda.Anno_accademico = vMotivazioni_blocco_pagamenti.Anno_accademico
                                            ");
            }

            _ = queryBuilder.AppendLine($@"
                                            LEFT OUTER JOIN vEsiti_concorsiPA ON Domanda.Anno_accademico = vEsiti_concorsiPA.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiPA.Num_domanda
                                        WHERE        
                                            (Domanda.Anno_accademico = '{annoAccademico}') AND (dbo.Domanda.Tipo_bando IN ('lz','l2'))");

            if (queryArguments.Count > 1 && queryArguments[1])
            {
                _ = queryBuilder.AppendLine($@"   
                                            AND vEsiti_concorsiBS.esito_BS in ({selectedEsitiBorsa})");
            }
            if (queryArguments.Count > 1 && queryArguments[2])
            {
                _ = queryBuilder.AppendLine($@"   
                                            AND Sede_studi.Cod_ente in ({selectedCodEnti})");
            }

            if (queryArguments.Count > 1 && queryArguments[3])
            {
                _ = queryBuilder.AppendLine($@"   
                                            AND vIscrizioni.Cod_tipologia_studi in ({selectedTipoCorso})");
            }

            if (queryArguments.Count > 1 && queryArguments[4])
            {
                _ = queryArguments[5] ? queryBuilder.AppendLine($@"
                                            AND  ( ") : queryBuilder.AppendLine($"AND ");
                _ = queryBuilder.AppendLine($@"    
                                                vValori_calcolati.Anno_corso in ({selectedAnnoCorso})");
                if (queryArguments[5])
                {
                    _ = queryBuilder.AppendLine($@" 
                                            OR vValori_calcolati.Anno_corso < 0) ");
                }
            }

            if (queryArguments.Count > 1 && queryArguments[8])
            {
                _ = queryBuilder.AppendLine($@"   
                                            AND vValori_calcolati.Status_sede in ({selectedStatusSede})");
            }

            if (queryArguments.Count > 0 && queryArguments[9])
            {
                _ = queryBuilder.AppendLine(@"
                                            AND vCittadinanza.cod_cittadinanza NOT IN 
	                                        (
		                                        'Z102', 'Z103', 'Z107', 'Z131', 'Z109', 'Z110', 'Z112', 'Z111', 'Z115', 'Z116', 'Z120', 'Z126', 'Z128', 'Z114', 'Z132', 'Z000', 'Z144', 'Z145', 'Z146', 'Z156', 'Z155', 'Z150', 'Z104', 'Z211', 'Z121', 'Z127', 'Z129', 'Z134', 'Z149', 'Z105', 'Z146'
	                                        )");
            }

            if (!string.IsNullOrEmpty(selectedCittadinanza))
            {
                _ = queryBuilder.AppendLine($@"
                                            AND vCittadinanza.cod_cittadinanza = '{selectedCittadinanza}'

                                            ");
            }

            if (!string.IsNullOrEmpty(selectedBlocco))
            {
                _ = queryBuilder.AppendLine($@"
                                            AND vMotivazioni_blocco_pagamenti.Cod_tipologia_blocco = '{selectedBlocco}' AND vMotivazioni_blocco_pagamenti.Data_fine_validita IS NULL
                                            ");
            }

            if (!string.IsNullOrEmpty(selectedCodComune))
            {
                _ = queryBuilder.AppendLine($@"
                                            AND vResidenza.Cod_comune = '{selectedCodComune}'");
            }

            _ = queryBuilder.AppendLine("   ORDER BY Domanda.Cod_fiscale");

            string query = queryBuilder.ToString();
            Logger.Log(1, query, LogLevel.INFO);
            Logger.Log(1, "Connessione al database", LogLevel.INFO);
            SqlCommand cmd = new(query, CONNECTION)
            {
                CommandTimeout = 900000
            };


            Logger.Log(10, "Esecuzione Query", LogLevel.INFO);
            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                // Create a DataTable to hold the data
                DataTable dataTable = new DataTable();
                Logger.Log(20, "Esecuzione Query - Salvataggio in DataTable", LogLevel.INFO);
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    dataTable.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                }

                // Minimize the operations inside the loop
                var values = new object[reader.FieldCount];
                while (reader.Read())
                {
                    reader.GetValues(values);
                    dataTable.Rows.Add(values);
                }

                Logger.Log(50, $"Esecuzione Query - Totale domande n#{dataTable.Rows.Count}", LogLevel.INFO);

                // Use ExportDataTableToExcel to export the DataTable to an Excel file
                Logger.Log(55, "Esecuzione Query - Creazione file excel", LogLevel.INFO);

                // Generate a dynamic file name with a timestamp
                string fileName = $"EstrazioneStudenti_{DateTime.Now:ddMMyy_HHmm}.xlsx";
                string excelFilePath = Path.Combine(folderPath, fileName);

                // Export the DataTable to Excel
                Logger.Log(65, "Esecuzione Query - Scrittura su file dei dati", LogLevel.INFO);
                Utilities.ExportDataTableToExcel(dataTable, excelFilePath);

                Logger.Log(80, "Esecuzione Query - Salvataggio file excel", LogLevel.INFO);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            //_masterForm.inProcedure = false;
            Logger.Log(100, "Fine lavorazione.", LogLevel.INFO);
        }
    }
}
