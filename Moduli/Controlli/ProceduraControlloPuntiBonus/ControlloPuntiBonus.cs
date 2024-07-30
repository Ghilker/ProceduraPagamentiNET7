using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ControlloPuntiBonus : BaseProcedure<ArgsControlloPuntiBonus>
    {

        string saveFolder = "";
        SqlTransaction sqlTransaction;
        List<StudenteControlliBonus> studenti = new();

        string currentAA = "";

        public ControlloPuntiBonus(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsControlloPuntiBonus args)
        {
            _masterForm.inProcedure = true;
            Logger.LogInfo(0, $"Inizio lavorazione");
            sqlTransaction = CONNECTION.BeginTransaction();
            saveFolder = args._selectedSaveFolder;

            try
            {
                currentAA = GetCurrentAA();
                PopulateStudentsList();
                sqlTransaction.Rollback();
                _masterForm.inProcedure = false;
                Logger.LogInfo(100, "Fine lavorazione");
            }
            catch (Exception ex)
            {
                sqlTransaction.Rollback();
                _masterForm.inProcedure = false;
                Logger.LogError(100, $"Errore: {ex.Message}");
            }
        }

        private string GetCurrentAA()
        {

            string currentAA = "";
            string dataQuery = $@"
                SELECT        TOP (1) Anno_accademico
                FROM            DatiGenerali_con
                ORDER BY Anno_accademico DESC
                ";

            SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction);
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    currentAA = Utilities.SafeGetString(reader, "Anno_accademico").ToUpper();
                }
            }

            return currentAA;
        }

        private void PopulateStudentsList()
        {
            int startYear = int.Parse(currentAA.Substring(0, 4));
            int endYear = int.Parse(currentAA.Substring(4, 4));

            // Decrement the years
            int previousStartYear = startYear - 1;
            int previousEndYear = endYear - 1;

            // Format the new academic year string
            string previousAcademicYear = previousStartYear.ToString("D4") + previousEndYear.ToString("D4");


            string dataQuery = $@"

                WITH CTE_Merito AS (
                    SELECT 
                        Domanda.Anno_accademico,
                        Domanda.Num_domanda,
                        Domanda.Cod_fiscale,
                        vMerito.Numero_crediti,
                        vMerito.Crediti_rimanenti,
                        vMerito.Crediti_utilizzati,
                        vMerito.Utente,
                        vMerito.Data_validita,
                        vMerito.Utilizzo_bonus,
		                vMerito.Crediti_riconosciuti_da_rinuncia,
		                vMerito.AACreditiRiconosciuti
                    FROM 
                        Domanda
                    INNER JOIN vMerito ON Domanda.Anno_accademico = vMerito.Anno_accademico 
                        AND Domanda.Num_domanda = vMerito.Num_domanda
                    WHERE 
                        Domanda.Anno_accademico = '{currentAA}' 
                        AND vMerito.Utilizzo_bonus = 1
                ),  CTE_Iscrizioni AS (
                    SELECT 
                        vIscrizioni.Cod_fiscale,
                        vIscrizioni.Anno_accademico,
                        vIscrizioni.Cod_tipologia_studi,
                        vIscrizioni.Anno_corso,
                        vIscrizioni.Cod_corso_laurea,
                        vIscrizioni.Anno_accad_inizio,
                        vIscrizioni.Cod_tipo_ordinamento,
                        vIscrizioni.Cod_facolta,
                        vIscrizioni.Cod_sede_studi,
		                Sede_studi.Descrizione AS SedeDescrizione
                    FROM 
                        vIscrizioni INNER JOIN Sede_studi ON vIscrizioni.Cod_sede_studi = Sede_studi.Cod_sede_studi 
                    WHERE 
                        vIscrizioni.Anno_accademico = '{currentAA}'
                ), CTE_ValoriCalcolati AS (
                    SELECT 
                        vValori_calcolati.Anno_accademico,
                        vValori_calcolati.Num_domanda,
                        vValori_calcolati.Anno_corso
                    FROM 
                        vValori_calcolati
                    WHERE 
                        vValori_calcolati.Anno_accademico = '{currentAA}' 
                )
                SELECT 
                    CTE_Merito.Num_domanda,
                    Studente.Cod_fiscale,
                    CTE_Iscrizioni.Cod_tipologia_studi,
                    CTE_ValoriCalcolati.Anno_corso,
                    CAST(CTE_Merito.Numero_crediti AS INT) as Numero_crediti,
                    CAST(CTE_Merito.Crediti_rimanenti AS INT) as Crediti_rimanenti,
                    CAST(CTE_Merito.Crediti_utilizzati AS INT) as Crediti_utilizzati,
                    vEsiti_concorsiBS.Cod_tipo_esito AS esitoBS,
                    vCARRIERA_PREGRESSA.Cod_avvenimento,
                    vCARRIERA_PREGRESSA.Anno_avvenimento,
                    vCARRIERA_PREGRESSA.Prima_immatricolaz,
                    vCARRIERA_PREGRESSA.Sede_istituzione_universitaria,
                    CAST(vCARRIERA_PREGRESSA.numero_crediti AS INT) AS cred_DI,
                    vCARRIERA_PREGRESSA.anno_corso AS annocorso_DI,
                    vCARRIERA_PREGRESSA.ripetente AS ripetenteDI,
                    vCARRIERA_PREGRESSA.Ateneo,
                    CAST(CTE_Merito.Crediti_riconosciuti_da_rinuncia AS INT) as Crediti_riconosciuti_da_rinuncia,
                    CTE_Merito.AACreditiRiconosciuti
                FROM 
                    CTE_Merito
                INNER JOIN Studente ON CTE_Merito.Cod_fiscale = Studente.Cod_fiscale
                INNER JOIN CTE_Iscrizioni ON CTE_Merito.Cod_fiscale = CTE_Iscrizioni.Cod_fiscale 
                    AND CTE_Merito.Anno_accademico = CTE_Iscrizioni.Anno_accademico
                INNER JOIN CTE_ValoriCalcolati ON CTE_Merito.Anno_accademico = CTE_ValoriCalcolati.Anno_accademico 
                    AND CTE_Merito.Num_domanda = CTE_ValoriCalcolati.Num_domanda
                INNER JOIN Corsi_laurea ON CTE_Iscrizioni.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea 
                    AND CTE_Iscrizioni.Anno_accad_inizio = Corsi_laurea.Anno_accad_inizio 
                    AND CTE_Iscrizioni.Cod_tipo_ordinamento = Corsi_laurea.Cod_tipo_ordinamento 
                    AND CTE_Iscrizioni.Cod_facolta = Corsi_laurea.Cod_facolta 
                    AND CTE_Iscrizioni.Cod_sede_studi = Corsi_laurea.Cod_sede_studi 
                    AND CTE_Iscrizioni.Cod_tipologia_studi = Corsi_laurea.Cod_tipologia_studi
                LEFT OUTER JOIN vCARRIERA_PREGRESSA ON CTE_Merito.Anno_accademico = vCARRIERA_PREGRESSA.Anno_accademico 
                    AND CTE_Merito.Cod_fiscale = vCARRIERA_PREGRESSA.Cod_fiscale
                LEFT OUTER JOIN vEsiti_concorsiBS ON CTE_Merito.Num_domanda = vEsiti_concorsiBS.Num_domanda 
                    AND CTE_Merito.Anno_accademico = vEsiti_concorsiBS.Anno_accademico
                WHERE 
                    CTE_Merito.Anno_accademico = '{currentAA}' 
	                AND esito_BS is not null
                ORDER BY 
                    CTE_Merito.Cod_fiscale;
            ";

            SqlCommand readData = new(dataQuery, CONNECTION, sqlTransaction);
            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper();
                    StudenteControlliBonus? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                    if (studente != null)
                    {
                        string codAvvenimento = Utilities.SafeGetString(reader, "Cod_avvenimento");

                        if (string.IsNullOrWhiteSpace(codAvvenimento)) { continue; }

                        studente.storicoAvvenimenti.Add(
                            new Avvenimento
                            {
                                codAvvenimento = Utilities.SafeGetString(reader, "Cod_avvenimento"),
                                annoAccademicoAvvenimento = Utilities.SafeGetString(reader, "Anno_avvenimento"),
                                AAPrimaImmatricolazione = Utilities.SafeGetString(reader, "Prima_immatricolaz"),
                                sedeIstituzioneUniversitariaAvvenimento = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria"),
                                creditiDI = Utilities.SafeGetInt(reader, "cred_DI"),
                                annoCorsoDI = Utilities.SafeGetInt(reader, "annocorso_DI"),
                                ripetenteDI = Utilities.SafeGetInt(reader, "ripetenteDI") == 1,
                                ateneoAvvenimento = Utilities.SafeGetString(reader, "Ateneo"),
                                creditiRiconosciutiAvvenimento = Utilities.SafeGetInt(reader, "Crediti_riconosciuti_da_rinuncia"),
                                AACreditiRiconosciuti = Utilities.SafeGetString(reader, "AACreditiRiconosciuti"),
                            });
                    }
                    else
                    {
                        StudenteControlliBonus newStudente = new StudenteControlliBonus
                        {
                            codFiscale = codFiscale,
                            numDomanda = Utilities.SafeGetString(reader, "Num_domanda"),
                            codTipologiaStudi = Utilities.SafeGetString(reader, "Cod_tipologia_studi"),
                            annoCorso = Utilities.SafeGetString(reader, "Anno_corso"),
                            numCrediti = Utilities.SafeGetInt(reader, "Numero_crediti"),
                            creditiBonusRimanentiDaTabella = Utilities.SafeGetInt(reader, "Crediti_rimanenti"),
                            creditiBonusUsatiDaTabella = Utilities.SafeGetInt(reader, "Crediti_utilizzati"),
                            currentEsitoBS = Utilities.SafeGetInt(reader, "esitoBS")
                        };

                        string codAvvenimento = Utilities.SafeGetString(reader, "Cod_avvenimento");
                        if (!string.IsNullOrWhiteSpace(codAvvenimento))
                        {
                            newStudente.storicoAvvenimenti.Add(
                                new Avvenimento
                                {
                                    codAvvenimento = Utilities.SafeGetString(reader, "Cod_avvenimento"),
                                    annoAccademicoAvvenimento = Utilities.SafeGetString(reader, "Anno_avvenimento"),
                                    AAPrimaImmatricolazione = Utilities.SafeGetString(reader, "Prima_immatricolaz"),
                                    sedeIstituzioneUniversitariaAvvenimento = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria"),
                                    creditiDI = Utilities.SafeGetInt(reader, "cred_DI"),
                                    annoCorsoDI = Utilities.SafeGetInt(reader, "annocorso_DI"),
                                    ripetenteDI = Utilities.SafeGetInt(reader, "ripetenteDI") == 1,
                                    ateneoAvvenimento = Utilities.SafeGetString(reader, "Ateneo"),
                                    creditiRiconosciutiAvvenimento = Utilities.SafeGetInt(reader, "Crediti_riconosciuti_da_rinuncia"),
                                    AACreditiRiconosciuti = Utilities.SafeGetString(reader, "AACreditiRiconosciuti"),
                                });
                        }
                        studenti.Add(newStudente);
                    }
                }
            }

            string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));";
            using (SqlCommand createCmd = new SqlCommand(createTempTable, CONNECTION, sqlTransaction))
            {
                createCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Inserimento in tabella CF dei codici fiscali");
            Logger.LogInfo(30, "Lavorazione studenti - creazione tabella codici fiscali");

            // Create a DataTable to hold the fiscal codes
            using (DataTable cfTable = new DataTable())
            {
                cfTable.Columns.Add("Cod_fiscale", typeof(string));

                foreach (var s in studenti)
                {
                    cfTable.Rows.Add(s.codFiscale);
                }

                // Use SqlBulkCopy to efficiently insert the data into the temporary table
                using SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION, SqlBulkCopyOptions.Default, sqlTransaction);
                bulkCopy.DestinationTableName = "#CFEstrazione";
                bulkCopy.WriteToServer(cfTable);
            }

            Logger.LogDebug(null, "Creazione index della tabella CF");
            string indexingCFTable = "CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale)";
            using (SqlCommand indexingCFTableCmd = new SqlCommand(indexingCFTable, CONNECTION, sqlTransaction))
            {
                indexingCFTableCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Aggiornamento statistiche della tabella CF");
            string updateStatistics = "UPDATE STATISTICS #CFEstrazione";
            using (SqlCommand updateStatisticsCmd = new SqlCommand(updateStatistics, CONNECTION, sqlTransaction))
            {
                updateStatisticsCmd.ExecuteNonQuery();
            }


            string storicoBonusQuery = $@"

                SELECT 
	                Domanda.Anno_accademico,
	                vMerito.Utilizzo_bonus,
                    Domanda.Cod_fiscale, 
                    vMerito.Crediti_utilizzati,
	                vEsiti_concorsiBS.Cod_tipo_esito,
                    vValori_calcolati.Anno_corso,
                    vIscrizioni.Cod_tipologia_studi
                FROM 
                    Domanda AS Domanda
                INNER JOIN vMerito  ON Domanda.Anno_accademico = vMerito.Anno_accademico 
                    AND Domanda.Num_domanda = vMerito.Num_domanda
                LEFT OUTER JOIN vEsiti_concorsiBS ON Domanda.Anno_accademico = vEsiti_concorsiBS.Anno_accademico 
                    AND Domanda.Num_domanda = vEsiti_concorsiBS.Num_domanda
                INNER JOIN  vValori_calcolati ON Domanda.Anno_accademico = vValori_calcolati.Anno_accademico AND Domanda.Num_domanda = vValori_calcolati.Num_domanda
                INNER JOIN vIscrizioni ON Domanda.Cod_fiscale = vIscrizioni.Cod_fiscale AND Domanda.Anno_accademico = vIscrizioni.Anno_accademico
                INNER JOIN #CFEstrazione ON Domanda.Cod_fiscale = #CFEstrazione.Cod_fiscale
                WHERE 
                    Domanda.Anno_accademico BETWEEN '20092010' AND '{previousAcademicYear}' 
	                and Domanda.Tipo_bando like 'l%'
	                order by Cod_fiscale, anno_accademico

                ";
            SqlCommand readDataStorico = new(storicoBonusQuery, CONNECTION, sqlTransaction);
            try
            {
                using (SqlDataReader reader = readDataStorico.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string codFiscale = Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper();
                        StudenteControlliBonus? studente = studenti.FirstOrDefault(s => s.codFiscale == codFiscale);
                        if (studente != null)
                        {
                            string annoAccademico = Utilities.SafeGetString(reader, "Anno_accademico");
                            bool richiestoBonus = Utilities.SafeGetInt(reader, "Utilizzo_bonus") == 1;
                            bool esclusoBorsa = Utilities.SafeGetInt(reader, "Cod_tipo_esito") == 0;
                            int creditiUtilizzati = Utilities.SafeGetInt(reader, "Crediti_utilizzati");
                            studente.storicoBonus.Add(new StoricoBonus
                            {
                                annoAccademico = Utilities.SafeGetString(reader, "Anno_accademico"),
                                richiestoBonus = Utilities.SafeGetInt(reader, "Utilizzo_bonus") == 1,
                                esclusoBorsa = Utilities.SafeGetInt(reader, "Cod_tipo_esito") == 0,
                                creditiUtilizzati = Utilities.SafeGetInt(reader, "Crediti_utilizzati"),
                                annoCorsoRichiestaBonus = Utilities.SafeGetInt(reader, "Anno_corso"),
                                codTipologiaStudiBonus = Utilities.SafeGetString(reader, "Cod_tipologia_studi"),
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(null, ex.Message);
            }

            foreach (StudenteControlliBonus studente in studenti)
            {
                if (studente.storicoAvvenimenti.Count == 0)
                {
                    CheckStudenteNoCarrieraPregressa(studente);
                }
                else
                {
                    CheckStudenteCarrieraPregressa(studente);
                }
            }


            DataTable studentiManuali = new DataTable();

            studentiManuali.Columns.Add("Num_domanda");
            studentiManuali.Columns.Add("Cod_fiscale");
            studentiManuali.Columns.Add("Cod_tipologia_studi");
            studentiManuali.Columns.Add("Anno_corso");
            studentiManuali.Columns.Add("Numero_crediti");
            studentiManuali.Columns.Add("Crediti_rimanenti");
            studentiManuali.Columns.Add("Crediti_utilizzati");
            studentiManuali.Columns.Add("esitoBS");
            studentiManuali.Columns.Add("Cod_avvenimento");
            studentiManuali.Columns.Add("Anno_avvenimento");
            studentiManuali.Columns.Add("Prima_immatricolaz");
            studentiManuali.Columns.Add("Sede_istituzione_universitaria");
            studentiManuali.Columns.Add("cred_DI");
            studentiManuali.Columns.Add("annocorso_DI");
            studentiManuali.Columns.Add("ripetenteDI");
            studentiManuali.Columns.Add("Ateneo");
            studentiManuali.Columns.Add("Crediti_riconosciuti_da_rinuncia");
            studentiManuali.Columns.Add("AACreditiRiconosciuti");

            foreach (StudenteControlliBonus dataStudente in studenti)
            {
                if (!dataStudente.controlloManualeStudente)
                {
                    continue;
                }
                foreach (Avvenimento avvenimento in dataStudente.storicoAvvenimenti)
                {
                    studentiManuali.Rows.Add(
                        dataStudente.numDomanda,
                        dataStudente.codFiscale,
                        dataStudente.codTipologiaStudi,
                        dataStudente.annoCorso,
                        dataStudente.numCrediti,
                        dataStudente.creditiBonusRimanentiDaTabella,
                        dataStudente.creditiBonusUsatiDaTabella,
                        dataStudente.currentEsitoBS,
                        avvenimento.codAvvenimento,
                        avvenimento.annoAccademicoAvvenimento,
                        avvenimento.AAPrimaImmatricolazione,
                        avvenimento.sedeIstituzioneUniversitariaAvvenimento,
                        avvenimento.creditiDI,
                        avvenimento.annoCorsoDI,
                        avvenimento.ripetenteDI,
                        avvenimento.ateneoAvvenimento,
                        avvenimento.creditiRiconosciutiAvvenimento,
                        avvenimento.AACreditiRiconosciuti
                        );
                }
            }

            Utilities.ExportDataTableToExcel(studentiManuali, saveFolder);

            if (studenti.Count > 0)
            {
                string test = "";
            }
        }



        void CheckStudenteNoCarrieraPregressa(StudenteControlliBonus studente)
        {
            if ((studente.codTipologiaStudi == "3" || studente.codTipologiaStudi == "4") && (studente.annoCorso == "1" || studente.annoCorso == "2"))
            {
                studente.creditiRimanentiCalcolati = -1;
                return;
            }

            int sommaCreditiUsati = 0;
            int annoCorsoPrimaRichiestaBonus = 0;
            string codTipologiaStudiPrimaRichiestaBonus = string.Empty;
            foreach (StoricoBonus storicoBonus in studente.storicoBonus)
            {
                if (storicoBonus.richiestoBonus && storicoBonus.esclusoBorsa)
                {
                    continue;
                }
                sommaCreditiUsati += storicoBonus.creditiUtilizzati;
                if (annoCorsoPrimaRichiestaBonus == 0)
                {
                    annoCorsoPrimaRichiestaBonus = storicoBonus.annoCorsoRichiestaBonus;
                    codTipologiaStudiPrimaRichiestaBonus = storicoBonus.codTipologiaStudiBonus;
                }
            }

            if (sommaCreditiUsati == 0)
            {
                studente.creditiRimanentiCalcolati = -1;
                return;
            }
            int maxCreditiRimanenti = 0;
            switch (studente.codTipologiaStudi)
            {
                case "3":
                    switch (annoCorsoPrimaRichiestaBonus)
                    {
                        case 2:
                            maxCreditiRimanenti = 5;
                            break;
                        case 3:
                            maxCreditiRimanenti = 12;
                            break;
                        case -1:
                            maxCreditiRimanenti = 15;
                            break;
                    }
                    break;
                case "4":
                    switch (annoCorsoPrimaRichiestaBonus)
                    {
                        case 2:
                            maxCreditiRimanenti = 5;
                            break;
                        case 3:
                            maxCreditiRimanenti = 12;
                            break;
                        case 4:
                            maxCreditiRimanenti = 15;
                            break;
                        case 5:
                            maxCreditiRimanenti = 15;
                            break;
                        case -1:
                            maxCreditiRimanenti = 15;
                            break;
                    }
                    break;
                case "5":
                    if (codTipologiaStudiPrimaRichiestaBonus == "5")
                    {
                        switch (annoCorsoPrimaRichiestaBonus)
                        {
                            case 2:
                                maxCreditiRimanenti = 12;
                                break;
                            case -1:
                                maxCreditiRimanenti = 12;
                                break;
                        }
                    }
                    else if (codTipologiaStudiPrimaRichiestaBonus == "3")
                    {
                        switch (annoCorsoPrimaRichiestaBonus)
                        {
                            case 2:
                                maxCreditiRimanenti = 5;
                                break;
                            case 3:
                                maxCreditiRimanenti = 12;
                                break;
                            case -1:
                                maxCreditiRimanenti = 15;
                                break;
                        }
                        break;
                    }
                    break;
            }

            int creditiCalcolati = maxCreditiRimanenti - sommaCreditiUsati;

            studente.creditiRimanentiCalcolati = studente.codTipologiaStudi == "5" ? Math.Max(creditiCalcolati, 12) : creditiCalcolati;
        }

        void CheckStudenteCarrieraPregressa(StudenteControlliBonus studente)
        {
            if (studente.storicoAvvenimenti.Count > 1)
            {
                studente.controlloManualeStudente = true;
                return;
            }
            foreach (Avvenimento avvenimento in studente.storicoAvvenimenti)
            {
                switch (avvenimento.codAvvenimento)
                {
                    case "RI":
                        if (avvenimento.creditiRiconosciutiAvvenimento > 0)
                        {
                            studente.creditiRimanentiCalcolati = 0;
                            return;
                        }
                        else
                        {
                            CheckStudenteNoCarrieraPregressa(studente);
                        }
                        break;
                    case "CD":
                    case "AT":
                        if (studente.codTipologiaStudi != "5")
                        {
                            CheckStudenteNoCarrieraPregressa(studente);
                        }
                        else
                        {
                            if (avvenimento.sedeIstituzioneUniversitariaAvvenimento == 0)
                            {
                                CheckStudenteNoCarrieraPregressa(studente);
                            }
                            else if (avvenimento.sedeIstituzioneUniversitariaAvvenimento == 1)
                            {
                                studente.creditiRimanentiCalcolati = -1;
                                studente.incongruenzeDaAggiungere.Add("13");
                            }
                            else if (avvenimento.sedeIstituzioneUniversitariaAvvenimento == 2)
                            {
                                studente.creditiRimanentiCalcolati = 0;
                            }
                        }
                        break;
                    case "TS":
                        if (studente.annoCorso == "1" || studente.annoCorso == "2")
                        {
                            studente.creditiRimanentiCalcolati = -1;
                        }
                        else
                        {
                            studente.creditiRimanentiCalcolati = -1;
                            studente.incongruenzeDaAggiungere.Add("13");
                        }
                        break;
                    case "DI":
                        CheckStudenteNoCarrieraPregressa(studente);
                        break;
                }
            }
        }


    }
}
