using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ProceduraRendiconto : BaseProcedure<ArgsProceduraRendiconto>
    {

        string selectedSaveFolder = string.Empty;
        string aaInizio = string.Empty;
        string aaFine = string.Empty;
        List<string> benefici = new List<string>()
        {
            "BS", "PL", "CI", "TR"
        };

        public ProceduraRendiconto(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsProceduraRendiconto args)
        {
            try
            {
                _masterForm.inProcedure = true;
                Logger.LogInfo(0, $"Inizio lavorazione");

                selectedSaveFolder = args._selectedSaveFolder;
                aaInizio = args._annoAccademicoInizio;
                aaFine = args._annoAccademicoFine;

                List<string> anniAccademici = GenerateAcademicYears(aaInizio, aaFine);

                string rendicontoFolderPath = Path.Combine(selectedSaveFolder, "Rendiconto");
                if (!Directory.Exists(rendicontoFolderPath))
                {
                    Directory.CreateDirectory(rendicontoFolderPath);
                }

                // Create a folder for each academic year inside the "Rendiconto" folder
                foreach (string annoAccademico in anniAccademici)
                {
                    string yearFolderPath = Path.Combine(rendicontoFolderPath, annoAccademico);
                    if (!Directory.Exists(yearFolderPath))
                    {
                        Directory.CreateDirectory(yearFolderPath);
                    }

                    // Execute query for each benefit
                    foreach (string beneficio in benefici)
                    {
                        ExecuteQuery(annoAccademico, beneficio, yearFolderPath);
                    }
                }

                Logger.LogInfo(100, $"Fine lavorazione");
                _masterForm.inProcedure = false;
            }
            catch
            {
                _masterForm.inProcedure = false;
                throw;
            }
        }

        static List<string> GenerateAcademicYears(string startYearInput, string endYearInput)
        {
            List<string> academicYears = new List<string>();

            int startYear = int.Parse(startYearInput.Substring(0, 4));
            int endYear = int.Parse(endYearInput.Substring(4, 4));

            for (int year = startYear; year <= endYear - 1; year++)
            {
                string academicYear = $"{year}{year + 1}";
                academicYears.Add(academicYear);
            }

            return academicYears;
        }
        void ExecuteQuery(string annoAccademico, string beneficio, string folderPath)
        {
            Logger.LogInfo(null, $"Lavorazione A.A. {annoAccademico} e beneficio {beneficio}");
            string query = string.Empty;
            if (beneficio == "BS")
            {
                query = @$"
                SELECT        
                    Domanda.Anno_accademico, 
                    Domanda.Cod_fiscale, 
                    Domanda.Num_domanda, 
                    Studente.Codice_Studente, 
                    vIscrizioni.Anno_corso, 
                    gen.superamento_esami,
                    gen.superamento_esami_tassa_reg,
                    Sede_studi.descrizione as sede_studi, 
                    vIscrizioni.Cod_tipologia_studi, 
                    vEsiti_concorsiBS.Imp_beneficio, 
                    ISNULL(tb_pagam.pagato, 0) as pagato, 
	                (vEsiti_concorsiBS.Imp_beneficio - ISNULL(tb_pagam.pagato, 0)) AS da_pagare,
                    vMODALITA_PAGAMENTO.modalita_pagamento, 
                    vMODALITA_PAGAMENTO.IBAN,
                    vFINANZIATI_FSE.Tipo_fondo, 
                    gen.Blocco_pagamento, 
                    dbo.SlashDescrBlocchi(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS blocchi
                FROM            
                    Domanda 
                    INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
                    INNER JOIN vEsiti_concorsiBS ON Domanda.Anno_accademico = vEsiti_concorsiBS.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiBS.Num_domanda 
                    INNER JOIN vDATIGENERALI_dom AS gen ON Domanda.Anno_accademico = gen.Anno_accademico AND Domanda.Num_domanda = gen.Num_domanda 
                    INNER JOIN vValori_calcolati AS vv ON Domanda.Anno_accademico = vv.Anno_accademico AND Domanda.Num_domanda = vv.Num_domanda
                    INNER JOIN vIscrizioni ON Domanda.Cod_fiscale = vIscrizioni.Cod_fiscale AND Domanda.Anno_accademico = vIscrizioni.Anno_accademico 
                    INNER JOIN Sede_studi ON vIscrizioni.cod_sede_studi = Sede_studi.cod_sede_studi
                    LEFT OUTER JOIN vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                    LEFT OUTER JOIN vFINANZIATI_FSE ON Domanda.Anno_accademico = vFINANZIATI_FSE.Anno_accademico AND Domanda.Cod_fiscale = vFINANZIATI_FSE.Cod_fiscale AND vFINANZIATI_FSE.Cod_beneficio = 'BS'
                    LEFT OUTER JOIN (
                        SELECT        
                            SUM(Imp_pagato) AS pagato, Num_domanda
                        FROM Pagamenti 
                        WHERE 
                            (Anno_accademico IN ('{annoAccademico}')) 
                            AND (Ritirato_azienda = 0)
                            AND Cod_tipo_pagam IN (
                                SELECT Cod_tipo_pagam_old 
                                FROM Decod_pagam_new 
                                WHERE Cod_tipo_pagam_new LIKE 'BS%' 
                                AND Cod_tipo_pagam_new NOT LIKE 'BST%' 
                            )
                            OR (Cod_tipo_pagam LIKE 'BS%' AND Cod_tipo_pagam NOT LIKE 'BST%')
                        GROUP BY Num_domanda
                    ) AS tb_pagam ON Domanda.Num_domanda = tb_pagam.Num_domanda
                WHERE        
                    (Domanda.Anno_accademico = '{annoAccademico}') 
                    AND (Domanda.Tipo_bando in ('lz', 'l2'))
                    AND (vEsiti_concorsiBS.Cod_tipo_esito = '2') 
                    AND vEsiti_concorsiBS.Imp_beneficio <> ISNULL(tb_pagam.pagato, 0)
                ";
            }

            if (beneficio == "PL")
            {
                query = @$"
                    SELECT        
                        Domanda.Anno_accademico, 
                        Domanda.Cod_fiscale, 
                        Domanda.Num_domanda, 
                        Studente.Codice_Studente, 
                        vIscrizioni.Anno_corso, 
                        Sede_studi.descrizione as sede_studi, 
                        vIscrizioni.Cod_tipologia_studi, 
                        vEsiti_concorsiPL.Imp_beneficio, 
                        ISNULL(tb_pagam.pagato, 0) as pagato, 
                        (vEsiti_concorsiPL.Imp_beneficio - ISNULL(tb_pagam.pagato, 0)) AS da_pagare,
                        vMODALITA_PAGAMENTO.modalita_pagamento, 
                        vMODALITA_PAGAMENTO.IBAN,
                        gen.Blocco_pagamento, 
                        dbo.SlashDescrBlocchi(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS blocchi
                    FROM            
                        Domanda 
                        INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
                        INNER JOIN vEsiti_concorsiPL ON Domanda.Anno_accademico = vEsiti_concorsiPL.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiPL.Num_domandaBS 
                        INNER JOIN vDATIGENERALI_dom AS gen ON Domanda.Anno_accademico = gen.Anno_accademico AND Domanda.Num_domanda = gen.Num_domanda 
                        INNER JOIN vValori_calcolati AS vv ON Domanda.Anno_accademico = vv.Anno_accademico AND Domanda.Num_domanda = vv.Num_domanda
                        INNER JOIN vIscrizioni ON Domanda.Cod_fiscale = vIscrizioni.Cod_fiscale AND Domanda.Anno_accademico = vIscrizioni.Anno_accademico 
                        INNER JOIN Sede_studi ON vIscrizioni.cod_sede_studi = Sede_studi.cod_sede_studi
                        LEFT OUTER JOIN vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                     LEFT OUTER JOIN (
                            SELECT        
                                SUM(Imp_pagato) AS pagato, Num_domanda
                            FROM Pagamenti 
                            WHERE 
                                (Anno_accademico IN ('{annoAccademico}')) 
                                AND (Ritirato_azienda = 0)
                                AND Cod_tipo_pagam IN (
                                    SELECT Cod_tipo_pagam_old 
                                    FROM Decod_pagam_new 
                                    WHERE Cod_tipo_pagam_new LIKE 'PL%' 
                                )
                                OR (Cod_tipo_pagam LIKE 'PL%')
                            GROUP BY Num_domanda
                        ) AS tb_pagam ON Domanda.Num_domanda = tb_pagam.Num_domanda
                    WHERE        
                        (Domanda.Anno_accademico = '{annoAccademico}') 
                        AND (Domanda.Tipo_bando in ('lz', 'l2'))
                        AND (vEsiti_concorsiPL.Cod_tipo_esito = '2') 
                        AND vEsiti_concorsiPL.Imp_beneficio <> ISNULL(tb_pagam.pagato, 0)
                ";
            }

            if (beneficio == "CI")
            {
                query = $@"
                    SELECT        
                        Domanda.Anno_accademico, 
                        Domanda.Cod_fiscale, 
                        Domanda.Num_domanda, 
                        Studente.Codice_Studente, 
                        vIscrizioni.Anno_corso, 
                        Sede_studi.descrizione as sede_studi, 
                        vIscrizioni.Cod_tipologia_studi, 
                        vEsiti_concorsiCI.Imp_beneficio, 
                        ISNULL(tb_pagam.pagato, 0) as pagato, 
                        (vEsiti_concorsiCI.Imp_beneficio - ISNULL(tb_pagam.pagato, 0)) AS da_pagare,
                        vMODALITA_PAGAMENTO.modalita_pagamento, 
                        vMODALITA_PAGAMENTO.IBAN,
                        gen.Blocco_pagamento, 
                        dbo.SlashDescrBlocchi(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS blocchi
                    FROM            
                        Domanda 
                        INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
                        INNER JOIN vEsiti_concorsiCI ON Domanda.Anno_accademico = vEsiti_concorsiCI.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiCI.Num_domanda
                        INNER JOIN vDATIGENERALI_dom AS gen ON Domanda.Anno_accademico = gen.Anno_accademico AND Domanda.Num_domanda = gen.Num_domanda 
                        INNER JOIN vValori_calcolati AS vv ON Domanda.Anno_accademico = vv.Anno_accademico AND Domanda.Num_domanda = vv.Num_domanda
                        INNER JOIN vIscrizioni ON Domanda.Cod_fiscale = vIscrizioni.Cod_fiscale AND Domanda.Anno_accademico = vIscrizioni.Anno_accademico 
                        INNER JOIN Sede_studi ON vIscrizioni.cod_sede_studi = Sede_studi.cod_sede_studi
                        LEFT OUTER JOIN vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                     LEFT OUTER JOIN (
                            SELECT        
                                SUM(Imp_pagato) AS pagato, Num_domanda
                            FROM Pagamenti 
                            WHERE 
                                (Anno_accademico IN ('{annoAccademico}')) 
                                AND (Ritirato_azienda = 0)
                                AND Cod_tipo_pagam IN (
                                    SELECT Cod_tipo_pagam_old 
                                    FROM Decod_pagam_new 
                                    WHERE Cod_tipo_pagam_new LIKE 'CI%' 
                                )
                                OR (Cod_tipo_pagam LIKE 'CI%')
                            GROUP BY Num_domanda
                        ) AS tb_pagam ON Domanda.Num_domanda = tb_pagam.Num_domanda
                    WHERE        
                        (Domanda.Anno_accademico = '{annoAccademico}') 
                        AND (Domanda.Tipo_bando in ('lz', 'l2'))
                        AND (vEsiti_concorsiCI.Cod_tipo_esito = '2') 
                        AND vEsiti_concorsiCI.Imp_beneficio <> ISNULL(tb_pagam.pagato, 0)
                    ";
            }


            if (beneficio == "00")
            {
                query = @$"
                    SELECT        
                        Domanda.Anno_accademico, 
                        Domanda.Cod_fiscale, 
                        Domanda.Num_domanda, 
                        Studente.Codice_Studente, 
                        gen.Invalido,
                        vIscrizioni.Anno_corso, 
                        Sede_studi.descrizione as sede_studi, 
                        vIscrizioni.Cod_tipologia_studi, 
                        ISNULL(tb_pagam.pagato, 0) as pagato, 
                        vMODALITA_PAGAMENTO.modalita_pagamento, 
                        vMODALITA_PAGAMENTO.IBAN,
                        gen.Blocco_pagamento, 
                        dbo.SlashDescrBlocchi(Domanda.Num_domanda, '{annoAccademico}', 'BS') AS blocchi
                    FROM            
                        Domanda 
                        INNER JOIN Studente ON Domanda.Cod_fiscale = Studente.Cod_fiscale 
                        INNER JOIN vEsiti_concorsiBS ON Domanda.Anno_accademico = vEsiti_concorsiBS.Anno_accademico AND Domanda.Num_domanda = vEsiti_concorsiBS.Num_domanda 
                        INNER JOIN vDATIGENERALI_dom AS gen ON Domanda.Anno_accademico = gen.Anno_accademico AND Domanda.Num_domanda = gen.Num_domanda 
                        INNER JOIN vValori_calcolati AS vv ON Domanda.Anno_accademico = vv.Anno_accademico AND Domanda.Num_domanda = vv.Num_domanda
                        INNER JOIN vIscrizioni ON Domanda.Cod_fiscale = vIscrizioni.Cod_fiscale AND Domanda.Anno_accademico = vIscrizioni.Anno_accademico 
                        INNER JOIN Sede_studi ON vIscrizioni.cod_sede_studi = Sede_studi.cod_sede_studi
                        LEFT OUTER JOIN vMODALITA_PAGAMENTO ON Domanda.Cod_fiscale = vMODALITA_PAGAMENTO.Cod_fiscale 
                        LEFT OUTER JOIN (
                            SELECT        
                                SUM(Imp_pagato) AS pagato, Num_domanda
                            FROM Pagamenti 
                            WHERE 
                                (Anno_accademico IN ('{annoAccademico}')) 
                                AND (Ritirato_azienda = 0)
                                AND Cod_tipo_pagam IN (
                                    SELECT Cod_tipo_pagam_old 
                                    FROM Decod_pagam_new 
                                    WHERE Cod_tipo_pagam_new LIKE 'BST%' 
                                )
                                OR (Cod_tipo_pagam LIKE 'BST%')
                            GROUP BY Num_domanda
                        ) AS tb_pagam ON Domanda.Num_domanda = tb_pagam.Num_domanda
                    WHERE        
                        (Domanda.Anno_accademico = '{annoAccademico}') 
                        AND (Domanda.Tipo_bando in ('lz', 'l2')) 
                        AND (vEsiti_concorsiBS.Cod_tipo_esito <> '0') 
                        AND '140' <> ISNULL(tb_pagam.pagato, 0)
                        AND (
                            Domanda.Anno_accademico < '20232024'
                            AND Cod_tipologia_studi NOT IN ('06','07')
                            AND (
                                Cod_ente NOT IN ('02','03','04','05','06','12') 
                                OR (Cod_ente = '03' AND vv.Anno_corso < 0)
                            )
                            OR Domanda.Anno_accademico >= '20232024'
                        )
                ";
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                Logger.LogInfo(null, $"A.A. {annoAccademico} e beneficio {beneficio} non implementati");
                return;
            }

            SqlCommand command = new SqlCommand(query, CONNECTION);
            command.CommandTimeout = 900000;
            try
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    DataTable dataTable = new DataTable();
                    dataTable.Load(reader);
                    if (dataTable.Rows.Count > 0)
                    {
                        // Save the DataTable to a file or process it as needed
                        Utilities.ExportDataTableToExcel(dataTable, folderPath, true, $"{beneficio}.xlsx");
                    }
                }
                Logger.LogInfo(null, $"Fine lavorazione A.A. {annoAccademico} e beneficio {beneficio}");
            }
            catch (Exception ex)
            {
                Logger.LogError(0, $"Errore durante l'esecuzione della query per {annoAccademico} e {beneficio}: {ex.Message}");
                throw;
            }
        }
    }
}
