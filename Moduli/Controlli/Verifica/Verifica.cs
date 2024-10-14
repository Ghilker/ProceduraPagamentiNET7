using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class Verifica : BaseProcedure<ArgsVerifica>
    {
        DatiVerifica DatiVerifica;
        bool postGraduatorieDefinitive;

        Dictionary<string, StudenteVerifica> verificaDict = new Dictionary<string, StudenteVerifica>();

        public Verifica(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsVerifica args)
        {
            AddDatiVerifica();

            CreateVerificaList();

            if (verificaDict.Count > 0)
            {
                string test = "";
            }

            CreateCFTable();

            AddCarrieraPregressa();

            if (verificaDict.Count > 0)
            {
                string test = "";
            }

            AddBeneficiRichiesti();

            if (verificaDict.Count > 0)
            {
                string test = "";
            }

            AddNucleoFamiliare();

            if (verificaDict.Count > 0)
            {
                string test = "";
            }

            AddDatiEconomici();


            List<StudenteVerifica> studenti = verificaDict.Values.ToList();

            DataTable studentsTable = new DataTable();
            studentsTable.Columns.Add("CodFiscale");
            studentsTable.Columns.Add("ISEDSU");
            studentsTable.Columns.Add("ISEEDSU");
            studentsTable.Columns.Add("ISPDSU");
            studentsTable.Columns.Add("ISPEDSU");

            foreach (StudenteVerifica studenteVerifica in studenti)
            {
                studentsTable.Rows.Add(studenteVerifica.codFiscale, studenteVerifica.verificaDatiEconomici.ISEDSU.ToString(), studenteVerifica.verificaDatiEconomici.ISEEDSU.ToString(), studenteVerifica.verificaDatiEconomici.ISPDSU.ToString(), studenteVerifica.verificaDatiEconomici.ISPEDSU.ToString());
            }

            Utilities.ExportDataTableToExcel(studentsTable, "C:\\Users\\giacomo_pavone\\Desktop\\Pagamenti");

            if (verificaDict.Count > 0)
            {
                string test = "";
            }

        }

        void AddDatiVerifica()
        {
            string dataQuery = "SELECT top(1) Cod_tipo_graduat from graduatorie where anno_accademico = 20232024 and Cod_beneficio = 'bs' order by Cod_tipo_graduat desc";
            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - estrazione dati generali");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    postGraduatorieDefinitive = Utilities.SafeGetInt(reader, "Cod_tipo_graduat") == 1;
                }
            }

            dataQuery = $@"
                select 
                Soglia_Isee,
                Soglia_Ispe, 
                Importo_borsa_A, 
                Importo_borsa_B, 
                Importo_borsa_C,
                quota_mensa  
                from DatiGenerali_con 
                where Anno_accademico = 20232024
            ";

            readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - estrazione dati generali");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    DatiVerifica = new DatiVerifica()
                    {
                        sogliaISEE = Utilities.SafeGetDouble(reader, "Soglia_Isee"),
                        sogliaISPE = Utilities.SafeGetDouble(reader, "Soglia_Ispe"),
                        importoBorsaA = Utilities.SafeGetDouble(reader, "Importo_borsa_A"),
                        importoBorsaB = Utilities.SafeGetDouble(reader, "Importo_borsa_B"),
                        importoBorsaC = Utilities.SafeGetDouble(reader, "Importo_borsa_C"),
                        quotaMensa = Utilities.SafeGetDouble(reader, "quota_mensa"),
                    };
                }
            }
        }

        void CreateVerificaList()
        {
            string dataQuery = @"
                    SELECT   
                    domanda.Cod_fiscale, 
                    domanda.num_domanda, 
                    Studente.Cognome,
                    Studente.Nome,
                    studente.Data_nascita,
                    studente.Sesso,
                    Studente.Cod_comune_nasc,
                    vcit.Cod_cittadinanza
                    ,vdom.Invalido
                    ,vres.provincia_residenza
                    ,vres.COD_COMUNE
                    ,vd.Cod_comune_domicilio
                    ,vd.TITOLO_ONEROSO
                    ,vd.TIPO_CONTRATTO_TITOLO_ONEROSO
                    ,vd.DATA_REG_CONTRATTO
                    ,vd.DATA_DECORRENZA
                    ,vd.DATA_SCADENZA
                    ,vd.N_SERIE_CONTRATTO
                    ,vd.DURATA_CONTRATTO
                    ,vd.PROROGA
                    ,vd.DURATA_PROROGA
                    ,vd.ESTREMI_PROROGA
                    ,vd.TIPO_ENTE
                    ,vd.DENOM_ENTE
                    ,vd.IMPORTO_RATA
                    ,visc.Cod_sede_studi
                    ,Corsi_laurea.Comune_Sede_studi
                    ,visc.Cod_tipologia_studi
                    ,visc.Cod_corso_laurea
                    ,vMerito.Anno_immatricolaz
                    ,visc.Anno_corso
                    ,vMerito.Numero_esami
                    ,vMerito.Numero_crediti
                    ,visc.Crediti_tirocinio
                    ,vMerito.Crediti_riconosciuti_da_rinuncia
                    ,vMerito.Crediti_extra_curriculari
                    ,vMerito.Somma_voti
                    ,vMerito.Utilizzo_bonus
                    ,vMerito.Crediti_rimanenti
                    FROM            Domanda
                    Inner join Studente on Domanda.Cod_fiscale = Studente.Cod_fiscale
                    left outer join vCittadinanza vcit on Domanda.Cod_fiscale= vcit.Cod_fiscale
                    inner join vDATIGENERALI_dom vdom on Domanda.Anno_accademico = vdom.Anno_accademico and Domanda.Num_domanda = vdom.Num_domanda
                    left outer join vResidenza vres on Domanda.Anno_accademico = vres.ANNO_ACCADEMICO and Domanda.Cod_fiscale = vres.COD_FISCALE and Domanda.Tipo_bando = vres.tipo_bando
                    left outer join vDomicilio vd on Domanda.Anno_accademico = vd.Anno_accademico and Domanda.Cod_fiscale = vd.COD_FISCALE
                    inner join vIscrizioni visc on Domanda.Anno_accademico = visc.Anno_accademico and Domanda.Cod_fiscale = visc.Cod_fiscale and Domanda.Tipo_bando = visc.tipo_bando
                    left outer join Corsi_laurea on visc.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea and visc.Cod_facolta = Corsi_laurea.Cod_facolta and visc.Cod_sede_studi = Corsi_laurea.Cod_sede_studi
                    left outer join vMerito on Domanda.Anno_accademico = vMerito.Anno_accademico and Domanda.Num_domanda = vMerito.Num_domanda

                    WHERE        (domanda.Num_domanda IN
                                (SELECT DISTINCT d.Num_domanda
                                FROM            Domanda AS d INNER JOIN
                                                            vStatus_compilazione AS vv ON d.Anno_accademico = vv.anno_accademico AND d.Num_domanda = vv.num_domanda
                                WHERE        (d.Anno_accademico = '20232024') AND (vv.status_compilazione >= 90) AND (d.Tipo_bando = 'lz')))
                    and Corsi_laurea.Anno_accad_fine is null  
			                    order by Domanda.Cod_fiscale
                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - creazione lista");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    // Check if the codFiscale already exists in the dictionary
                    if (!verificaDict.ContainsKey(codFiscale))
                    {
                        StudenteVerifica studenteVerifica = new StudenteVerifica
                        {
                            codFiscale = codFiscale,
                            numDomanda = Utilities.SafeGetString(reader, "Num_domanda")
                        };

                        studenteVerifica.verificaAnagrafica = new VerificaAnagrafica
                        {
                            cognome = Utilities.SafeGetString(reader, "cognome"),
                            nome = Utilities.SafeGetString(reader, "nome"),
                            dataNascita = DateTime.Parse(Utilities.SafeGetString(reader, "data_nascita")),
                            sesso = Utilities.SafeGetString(reader, "sesso"),
                            comuneNascita = Utilities.SafeGetString(reader, "cod_comune_nasc"),
                            cittadinanza = Utilities.SafeGetString(reader, "cod_cittadinanza"),
                            invalidità = Utilities.SafeGetString(reader, "invalido") == "1" ? true : false
                        };

                        studenteVerifica.verificaResidenza = new VerificaResidenza
                        {
                            residenzaItaliana = Utilities.SafeGetString(reader, "provincia_residenza") == "EE" ? false : true,
                            provinciaResidenzaItaliana = Utilities.SafeGetString(reader, "provincia_residenza"),
                            comuneResidenzaItaliana = Utilities.SafeGetString(reader, "cod_comune")
                        };

                        studenteVerifica.verificaDomicilio = new VerificaDomicilio
                        {
                            diversoDaResidenza = Utilities.SafeGetString(reader, "cod_comune_domicilio") == string.Empty ? false : true
                        };

                        if (studenteVerifica.verificaDomicilio.diversoDaResidenza)
                        {
                            VerificaDomicilio verificaDomicilio = studenteVerifica.verificaDomicilio;
                            verificaDomicilio.comuneDomicilio = Utilities.SafeGetString(reader, "cod_comune_domicilio");
                            verificaDomicilio.contrOneroso = Utilities.SafeGetString(reader, "TITOLO_ONEROSO") == "1";
                            verificaDomicilio.contrLocazione = Utilities.SafeGetString(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO") != "1";
                            verificaDomicilio.contrEnte = Utilities.SafeGetString(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO") == "1";
                            verificaDomicilio.conoscenzaDatiContratto = Utilities.SafeGetString(reader, "N_SERIE_CONTRATTO") != string.Empty;

                            if (verificaDomicilio.contrLocazione)
                            {
                                if (DateTime.TryParse(Utilities.SafeGetString(reader, "DATA_REG_CONTRATTO"), out DateTime dataReg))
                                {
                                    verificaDomicilio.dataRegistrazioneLocazione = dataReg;
                                }
                                if (DateTime.TryParse(Utilities.SafeGetString(reader, "DATA_DECORRENZA"), out DateTime dataDecorrenza))
                                {
                                    verificaDomicilio.dataRegistrazioneLocazione = dataDecorrenza;
                                }
                                if (DateTime.TryParse(Utilities.SafeGetString(reader, "DATA_SCADENZA"), out DateTime dataScadenza))
                                {
                                    verificaDomicilio.dataRegistrazioneLocazione = dataScadenza;
                                }

                                verificaDomicilio.codiceSerieLocazione = Utilities.SafeGetString(reader, "N_SERIE_CONTRATTO");
                                verificaDomicilio.durataMesiLocazione = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO");
                                verificaDomicilio.prorogatoLocazione = Utilities.SafeGetString(reader, "PROROGA") == "1";
                                if (verificaDomicilio.prorogatoLocazione)
                                {
                                    verificaDomicilio.durataMesiProrogaLocazione = Utilities.SafeGetInt(reader, "DURATA_PROROGA");
                                    verificaDomicilio.codiceSerieProrogaLocazione = Utilities.SafeGetString(reader, "ESTREMI_PROROGA");
                                }
                            }

                            if (verificaDomicilio.contrEnte)
                            {
                                string tipoEnte = Utilities.SafeGetString(reader, "TIPO_ENTE");
                                switch (tipoEnte)
                                {
                                    case "ep":
                                        verificaDomicilio.tipologiaEnteIstituto = VerificaDomicilio.TipologiaEnteIstituto.EntePubblicoPrivato;
                                        break;
                                    case "ir":
                                        verificaDomicilio.tipologiaEnteIstituto = VerificaDomicilio.TipologiaEnteIstituto.IstitutoReligioso;
                                        break;
                                    case "fa":
                                        verificaDomicilio.tipologiaEnteIstituto = VerificaDomicilio.TipologiaEnteIstituto.FondazioneAssociazione;
                                        break;
                                    case "se":
                                        verificaDomicilio.tipologiaEnteIstituto = VerificaDomicilio.TipologiaEnteIstituto.ErasmusSocrates;
                                        break;
                                }

                                verificaDomicilio.denominazioneIstituto = Utilities.SafeGetString(reader, "DENOM_ENTE");
                                verificaDomicilio.durataMesiContrattoIstituto = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO");
                                verificaDomicilio.importoMensileRataIstituto = Utilities.SafeGetDouble(reader, "IMPORTO_RATA");
                            }
                        }

                        studenteVerifica.verificaIscrizione = new VerificaIscrizione
                        {
                            codSedeStudi = Utilities.SafeGetString(reader, "cod_sede_studi"),
                            comuneSedeStudi = Utilities.SafeGetString(reader, "Comune_Sede_studi"),
                            codTipoCorso = Utilities.SafeGetString(reader, "Cod_tipologia_studi"),
                            codCorsoLaurea = Utilities.SafeGetString(reader, "Cod_corso_laurea"),
                            aaPrimaImmatricolazione = Utilities.SafeGetString(reader, "Anno_immatricolaz"),
                            annoCorso = Utilities.SafeGetString(reader, "Anno_corso"),
                            matricola = Utilities.SafeGetString(reader, "Anno_corso") == "1",
                            numeroEsamiSostenuti = Utilities.SafeGetInt(reader, "Numero_esami"),
                            sommaTotaleCrediti = Utilities.SafeGetInt(reader, "Numero_crediti"),
                            creditiTirocinio = Utilities.SafeGetInt(reader, "Crediti_tirocinio"),
                            creditiRinuncia = Utilities.SafeGetInt(reader, "Crediti_riconosciuti_da_rinuncia"),
                            creditiExtracurr = Utilities.SafeGetInt(reader, "Crediti_extra_curriculari"),
                            sommaVoti = Utilities.SafeGetInt(reader, "Somma_voti"),
                            utilizzoBonus = Utilities.SafeGetString(reader, "Utilizzo_bonus") == "1",
                            creditiBonusRimanenti = Utilities.SafeGetInt(reader, "Crediti_rimanenti")
                        };


                        // Add to the dictionary
                        verificaDict.Add(codFiscale, studenteVerifica);
                    }
                }
            }

            Logger.LogInfo(45, "UPDATE:Verifica - creazione lista - completato");
        }
        void CreateCFTable()
        {
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");
            List<string> codFiscali = verificaDict.Keys.ToList();

            Logger.LogDebug(null, "Creazione tabella CF");
            string createTempTable = "CREATE TABLE #CFEstrazione (Cod_fiscale VARCHAR(16));";
            using (SqlCommand createCmd = new SqlCommand(createTempTable, CONNECTION))
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
                    cfTable.Rows.Add(cf);
                }

                // Use SqlBulkCopy to efficiently insert the data into the temporary table
                using SqlBulkCopy bulkCopy = new SqlBulkCopy(CONNECTION);
                bulkCopy.DestinationTableName = "#CFEstrazione";
                bulkCopy.WriteToServer(cfTable);
            }

            Logger.LogDebug(null, "Creazione index della tabella CF");
            string indexingCFTable = "CREATE INDEX idx_Cod_fiscale ON #CFEstrazione (Cod_fiscale)";
            using (SqlCommand indexingCFTableCmd = new SqlCommand(indexingCFTable, CONNECTION))
            {
                indexingCFTableCmd.ExecuteNonQuery();
            }

            Logger.LogDebug(null, "Aggiornamento statistiche della tabella CF");
            string updateStatistics = "UPDATE STATISTICS #CFEstrazione";
            using (SqlCommand updateStatisticsCmd = new SqlCommand(updateStatistics, CONNECTION))
            {
                updateStatisticsCmd.ExecuteNonQuery();
            }
            #endregion
        }
        void AddCarrieraPregressa()
        {
            string dataQuery = @"
                    SELECT vCARRIERA_PREGRESSA.*, 
                           ben.anni_beneficiario,
                           res.anni_restituiti
                    FROM vCARRIERA_PREGRESSA
                    inner join #CFEstrazione cfe ON vCARRIERA_PREGRESSA.Cod_fiscale = cfe.Cod_fiscale 
                    CROSS APPLY dbo.SlashAnniBeneficiarioBS(vCARRIERA_PREGRESSA.Cod_fiscale, '20232024') AS ben
                    CROSS APPLY dbo.SlashAnniRestituitiBS(vCARRIERA_PREGRESSA.Cod_fiscale, '20232024') AS res
                    WHERE vCARRIERA_PREGRESSA.Anno_accademico = '20232024'
                    ORDER BY vCARRIERA_PREGRESSA.Cod_fiscale;
                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta carriera pregressa");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {
                        VerificaIscrizione verificaIscrizione = studenteVerifica.verificaIscrizione;

                        string codAvvenimento = Utilities.SafeGetString(reader, "Cod_avvenimento");
                        switch (codAvvenimento)
                        {
                            case "CD":
                                verificaIscrizione.titoloPregresso = true;
                                verificaIscrizione.sedeIstitutoFrequentatoTitoloPregresso = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria");
                                verificaIscrizione.codTipoCorsoTitoloPregresso = Utilities.SafeGetString(reader, "Tipologia_corso");
                                verificaIscrizione.durataTitoloPregresso = Utilities.SafeGetInt(reader, "Durata_leg_titolo_conseguito");
                                break;
                            case "RI":
                                verificaIscrizione.rinunciaStudi = true;
                                verificaIscrizione.sedeIstitutoFrequentatoRinunciaStudi = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria");
                                verificaIscrizione.codTipoCorsoRinunciaStudi = Utilities.SafeGetString(reader, "Tipologia_corso");

                                string anniBeneficiario = Utilities.SafeGetString(reader, "anni_beneficiario");
                                if (anniBeneficiario != string.Empty)
                                {
                                    verificaIscrizione.beneficiarioBSRinunciaStudi = true;
                                    verificaIscrizione.anniBeneficiarioBSRinunciaStudi = anniBeneficiario.Split(',').Select(int.Parse).ToList();
                                }

                                string anniRestituiti = Utilities.SafeGetString(reader, "anni_restituiti");
                                if (anniRestituiti != string.Empty)
                                {
                                    verificaIscrizione.restituitiImportiBSRinunciaStudi = Utilities.SafeGetString(reader, "importi_restituiti") == "1";
                                    verificaIscrizione.anniRestituitiBSRinunciaStudi = anniRestituiti.Split(',').Select(int.Parse).ToList();
                                }
                                break;
                            case "TS":
                                verificaIscrizione.trasferimento = true;
                                verificaIscrizione.sedeIstitutoFrequentatoTrasferimento = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria");
                                verificaIscrizione.codTipoCorsoTrasferimento = Utilities.SafeGetString(reader, "Tipologia_corso");
                                verificaIscrizione.aaPrimaImmatricolazioneTrasferimento = Utilities.SafeGetString(reader, "Prima_immatricolaz");
                                verificaIscrizione.aaConseguimentoTrasferimento = Utilities.SafeGetString(reader, "Anno_avvenimento");
                                break;
                            case "AT":
                                verificaIscrizione.attesaTitolo = true;
                                verificaIscrizione.sedeIstitutoFrequentatoAttesaTitolo = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria");
                                verificaIscrizione.codTipoCorsoAttesaTitolo = Utilities.SafeGetString(reader, "Tipologia_corso");
                                verificaIscrizione.aaConseguimentoAttesaTitolo = Utilities.SafeGetString(reader, "Anno_avvenimento");
                                break;
                            case "DI":
                                verificaIscrizione.doppiaIscrizione = true;
                                verificaIscrizione.sedeIstitutoFrequentatoDoppiaIscrizione = Utilities.SafeGetInt(reader, "Sede_istituzione_universitaria");
                                verificaIscrizione.codTipoCorsoDoppiaIscrizione = Utilities.SafeGetString(reader, "Tipologia_corso");
                                verificaIscrizione.aaPrimaImmatricolazioneDoppiaIscrizione = Utilities.SafeGetString(reader, "Prima_immatricolaz");
                                verificaIscrizione.annoCorsoDoppiaIscrizione = Utilities.SafeGetInt(reader, "anno_corso");
                                verificaIscrizione.sommaCreditiDoppiaIscrizione = Utilities.SafeGetInt(reader, "numero_crediti");
                                break;
                        }

                    }
                }
            }
        }
        void AddBeneficiRichiesti()
        {
            string dataQuery = @"
                WITH FilteredDomanda AS (
                    SELECT d.Num_domanda, d.Anno_accademico, d.Cod_fiscale, d.Data_validita, d.Utente, d.Tipo_bando, d.Id_Domanda, d.DataCreazioneRecord
                    FROM Domanda AS d
                    INNER JOIN #CFEstrazione cfe on d.cod_fiscale = cfe.cod_fiscale
                    INNER JOIN vStatus_compilazione AS vv ON d.Anno_accademico = vv.anno_accademico AND d.Num_domanda = vv.num_domanda
                    WHERE d.Anno_accademico = '20232024' 
                      AND vv.status_compilazione >= 90 
                      AND d.Tipo_bando = 'lz'
                ),
                DomandaWithJoins AS (
                    SELECT 
                        fd.Cod_fiscale,
                        vben.Cod_beneficio,
                        vdom.Posto_alloggio_confort,
                        vci.attesa_ci,
                        vci.durata_ci,
                        vci.paese_ci,
                        vci.data_partenza,
                        vdom.Possesso_altra_borsa,
                        vae.Beneficio_residenziale,
                        vimp.importo_borsa
                    FROM FilteredDomanda AS fd
                    LEFT outer JOIN vBenefici_richiesti AS vben ON fd.Anno_accademico = vben.Anno_accademico AND fd.Num_domanda = vben.Num_domanda
                    LEFT outer JOIN vDATIGENERALI_dom AS vdom ON fd.Anno_accademico = vdom.Anno_accademico AND fd.Num_domanda = vdom.Num_domanda
                    LEFT outer JOIN vSpecifiche_ci AS vci ON fd.Anno_accademico = vci.anno_accademico AND fd.Num_domanda = vci.num_domanda
                    LEFT outer JOIN vBenefici_altri_enti AS vae ON fd.Anno_accademico = vae.Anno_accademico AND fd.Num_domanda = vae.Num_domanda
                    LEFT outer JOIN vImporti_borsa_percepiti AS vimp ON fd.Anno_accademico = vimp.anno_accademico AND fd.Num_domanda = vimp.num_domanda
                )
                SELECT *
                FROM DomandaWithJoins

                ORDER BY Cod_fiscale;

                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta carriera pregressa");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {
                        if (studenteVerifica.verificaBeneficiRichiesti == null)
                        {
                            studenteVerifica.verificaBeneficiRichiesti = new VerificaBeneficiRichiesti();
                        }

                        VerificaBeneficiRichiesti verificaBeneficiRichiesti = studenteVerifica.verificaBeneficiRichiesti;

                        string codBeneficio = Utilities.SafeGetString(reader, "Cod_beneficio");
                        switch (codBeneficio)
                        {
                            case "BS":
                                verificaBeneficiRichiesti.richiestoBorsaDiStudio = true;

                                verificaBeneficiRichiesti.beneficiAltriEnti = Utilities.SafeGetString(reader, "Possesso_altra_borsa") == "1";

                                verificaBeneficiRichiesti.beneficiPercepitiPrecedenti = Utilities.SafeGetDouble(reader, "importo_borsa") > 0;
                                verificaBeneficiRichiesti.sommaImportiBeneficiPercepitiPrecedenti = Utilities.SafeGetDouble(reader, "importo_borsa");
                                break;
                            case "PA":
                                verificaBeneficiRichiesti.richiestoPostoAlloggio = true;
                                verificaBeneficiRichiesti.richiestoPostoAlloggioComfort = Utilities.SafeGetString(reader, "Posto_alloggio_confort") == "1";
                                verificaBeneficiRichiesti.beneficiOspitalitaResidenziale = Utilities.SafeGetString(reader, "Beneficio_residenziale") == "1";
                                break;
                            case "CI":
                                verificaBeneficiRichiesti.richiestoContributoInternazionale = true;
                                verificaBeneficiRichiesti.inAttesaCI = Utilities.SafeGetString(reader, "attesa_ci") == "1";
                                if (!verificaBeneficiRichiesti.inAttesaCI)
                                {
                                    verificaBeneficiRichiesti.codNazioneCI = Utilities.SafeGetString(reader, "paese_ci");
                                    verificaBeneficiRichiesti.dataPartenzaCI = DateTime.Parse(Utilities.SafeGetString(reader, "data_partenza"));
                                    verificaBeneficiRichiesti.durataMesiCI = Utilities.SafeGetInt(reader, "durata_ci");
                                }
                                break;
                        }

                    }
                }
            }
        }
        void AddNucleoFamiliare()
        {
            string dataQuery = @"
                    select 
                        domanda.Cod_fiscale,
                        Num_componenti,
                        Numero_conviventi_estero,
                        Cod_status_genit,
                        Cod_tipologia_nucleo,
                        motivo_assenza_genit,
                        Residenza_est_da,
                        Reddito_2_anni
                    from vNucleo_familiare 
                    inner join Domanda on vNucleo_familiare.Anno_accademico = Domanda.Anno_accademico and vNucleo_familiare.Num_domanda = Domanda.Num_domanda
                    INNER JOIN #CFEstrazione cfe on domanda.cod_fiscale = cfe.cod_fiscale
                    where domanda.Anno_accademico = '20232024'
                    order by domanda.Cod_fiscale

                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta nucleo familiare");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {
                        DateTime.TryParse(Utilities.SafeGetString(reader, "Residenza_est_da"), out DateTime result);


                        studenteVerifica.verificaNucleoFamiliare = new VerificaNucleoFamiliare()
                        {
                            numeroComponentiNF = Utilities.SafeGetInt(reader, "Num_componenti"),
                            numeroComponentiEsteroNF = Utilities.SafeGetInt(reader, "Numero_conviventi_estero"),
                            almenoUnGenitore = Utilities.SafeGetString(reader, "Cod_status_genit") != "N",
                            orfano = Utilities.SafeGetString(reader, "Cod_tipologia_nucleo") == "A" &&
                                        Utilities.SafeGetString(reader, "Motivo_assenza_genit") == "1" &&
                                        Utilities.SafeGetString(reader, "Cod_status_genit") == "N",
                            indipendente = Utilities.SafeGetString(reader, "Cod_tipologia_nucleo") == "B" &&
                                            Utilities.SafeGetInt(reader, "Num_componenti") == 1 &&
                                            Utilities.SafeGetString(reader, "Motivo_assenza_genit") == "2" &&
                                            Utilities.SafeGetString(reader, "Reddito_2_anni") == "1" &&
                                            DateTime.Now.Year - result.Year >= 2,
                            dataInizioResidenzaIndipendente = result,
                            redditoSuperiore = Utilities.SafeGetString(reader, "Reddito_2_anni") == "1"
                        };
                    }
                }
            }
        }
        void AddDatiEconomici()
        {
            List<string> codiciFiscaliIT = new List<string>();
            List<string> codiciFiscaliEE = new List<string>();
            List<string> codiciFiscaliIntIT = new List<string>();
            List<string> codiciFiscaliIntEE = new List<string>();

            string dataQuery = @"
                    select domanda.Cod_fiscale, Tipo_redd_nucleo_fam_origine, Tipo_redd_nucleo_fam_integr 
                    from Domanda 
                    INNER JOIN #CFEstrazione cfe on domanda.cod_fiscale = cfe.cod_fiscale
                    inner join vTipologie_redditi on Domanda.Anno_accademico = vTipologie_redditi.Anno_accademico and Domanda.Num_domanda = vTipologie_redditi.Num_domanda 
                    where domanda.Anno_accademico = 20232024 and Tipo_bando = 'lz'
                    order by Cod_fiscale

                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - divisione dati economici");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());
                    string tipoRedditiOrigine = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Tipo_redd_nucleo_fam_origine"));
                    string tipoRedditiIntegrazione = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Tipo_redd_nucleo_fam_integr"));
                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {
                        studenteVerifica.verificaDatiEconomici = new();
                        studenteVerifica.verificaDatiEconomici.tipologiaReddito = tipoRedditiOrigine;
                        studenteVerifica.verificaDatiEconomici.tipologiaRedditoIntegrazione = tipoRedditiIntegrazione;

                        if (tipoRedditiOrigine == "it")
                        {
                            codiciFiscaliIT.Add(codFiscale);
                        }
                        else if (tipoRedditiOrigine == "ee")
                        {
                            codiciFiscaliEE.Add(codFiscale);
                        }

                        if (tipoRedditiIntegrazione == "it")
                        {
                            codiciFiscaliIntIT.Add(codFiscale);
                        }
                        else if (tipoRedditiIntegrazione == "ee")
                        {
                            codiciFiscaliIntEE.Add(codFiscale);
                        }
                    }

                }
            }

            AddDatiEconomiciItaliani(codiciFiscaliIT);
            AddDatiEconomiciStranieri(codiciFiscaliEE);
            AddDatiEconomiciItalianiInt(codiciFiscaliIntIT);
            AddDatiEconomiciStranieriInt(codiciFiscaliIntEE);
            CalcoloDatiEconomici();
        }
        void AddDatiEconomiciItaliani(List<string> codiciFiscaliItaliani)
        {
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");


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

                foreach (var cf in codiciFiscaliItaliani)
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

            string dataQuery = @"
                WITH codici_pagam AS (
                    SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
                    FROM Decod_pagam_new dpn
                    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1

                    UNION

                    SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
                    FROM Decod_pagam_new dpn
                    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
                ),
                sumPagamenti AS (
                    SELECT
                        SUM(p.imp_pagato) AS somma,
                        d.Cod_fiscale
                    FROM Pagamenti p
                    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
                    WHERE
                        p.Ritirato_azienda = 0
                        AND p.Ese_finanziario = 2021
                        AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
                    GROUP BY d.Cod_fiscale
                ),
                impAltreBorse AS (
                    SELECT
                        vb.num_domanda,
                        vb.anno_accademico,
                        SUM(vb.importo_borsa) AS importo_borsa
                    FROM vimporti_borsa_percepiti vb
                    INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
                    INNER JOIN vstatus_allegati vs ON a.id_allegato = vs.id_allegato
                    WHERE
                        vb.data_fine_validita IS NULL
                        AND a.data_fine_validita IS NULL
                        AND a.cod_tipo_allegato = '07'
                        AND vs.cod_status = '05'
		                AND vb.anno_accademico = 20232024
                    GROUP BY vb.num_domanda, vb.anno_accademico
                )
                SELECT
                    d.Anno_accademico,
                    d.Num_domanda,
                    d.Cod_fiscale,
                    ISNULL(sp.somma, 0) AS detrazioniADISU,
                    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse,
                    cteISP.ISP,
                    cteISP.ISR,
                    cteISP.Scala_equivalenza as SEQ
                FROM Domanda d
                INNER JOIN #CFEstrazione cfe on d.cod_fiscale = cfe.cod_fiscale
                INNER JOIN vCertificaz_ISEE_CO cteISP ON d.Anno_accademico = cteISP.Anno_accademico AND d.Num_domanda = cteISP.Num_domanda
                LEFT JOIN sumPagamenti sp ON d.Cod_fiscale = sp.Cod_fiscale
                LEFT JOIN impAltreBorse iab ON d.Num_domanda = iab.num_domanda AND d.Anno_accademico = iab.anno_accademico
                WHERE
                    d.Anno_accademico = 20232024
                    AND d.tipo_bando = 'LZ'
                ORDER BY d.Num_domanda;
                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta dati economici redditi italiani");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {
                        double ISPdb = Utilities.SafeGetDouble(reader, "ISP");
                        double ISRdb = Utilities.SafeGetDouble(reader, "ISR");
                        studenteVerifica.verificaDatiEconomici = new()
                        {
                            ISR = ISRdb,
                            ISP = ISPdb,
                            ISPDSU = ISPdb,
                            SEQ = Utilities.SafeGetDouble(reader, "SEQ"),
                            detrazioni = Utilities.SafeGetDouble(reader, "detrazioniADISU") + Utilities.SafeGetDouble(reader, "detrazioniAltreBorse")
                        };
                    }
                }
            }
        }
        void AddDatiEconomiciStranieri(List<string> codiciFiscaliStranieri)
        {
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");


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

                foreach (var cf in codiciFiscaliStranieri)
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

            string dataQuery = @"
                    WITH codici_pagam AS (
                        SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
                        FROM Decod_pagam_new dpn
                        INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                        WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1

                        UNION

                        SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
                        FROM Decod_pagam_new dpn
                        INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                        WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
                    ),
                    sumPagamenti AS (
                        SELECT
                            SUM(p.imp_pagato) AS somma,
                            d.Cod_fiscale
                        FROM Pagamenti p
                        INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
                        WHERE
                            p.Ritirato_azienda = 0
                            AND p.Ese_finanziario = 2021
                            AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
                        GROUP BY d.Cod_fiscale
                    ),
                    impAltreBorse AS (
                        SELECT
                            vb.num_domanda,
                            vb.anno_accademico,
                            SUM(vb.importo_borsa) AS importo_borsa
                        FROM vimporti_borsa_percepiti vb
                        INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
                        INNER JOIN vstatus_allegati vs ON a.id_allegato = vs.id_allegato
                        WHERE
                            vb.data_fine_validita IS NULL
                            AND a.data_fine_validita IS NULL
                            AND a.cod_tipo_allegato = '07'
                            AND vs.cod_status = '05'
                            AND vb.anno_accademico = 20232024
                        GROUP BY vb.num_domanda, vb.anno_accademico
                    )
                    select 
                        Domanda.Cod_fiscale,
                        Numero_componenti,
                        Redd_complessivo,
                        Patr_mobiliare,
                        Possesso_abitaz,
                        Superf_abitaz_MQ,
                        Poss_altre_abit,
                        Sup_compl_altre_MQ,
                        Franchigia,
                        franchigia_pat_mobiliare,
                        tasso_rendimento_pat_mobiliare,
                        ISNULL(sp.somma, 0) AS detrazioniADISU,
                        ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse
                    from vNucleo_fam_stranieri_DO
                        inner join Domanda on Domanda.Anno_accademico = vNucleo_fam_stranieri_DO.Anno_accademico and Domanda.Num_domanda = vNucleo_fam_stranieri_DO.Num_domanda
	                    inner join DatiGenerali_con dc on Domanda.Anno_accademico = dc.Anno_accademico
                        inner join #CFEstrazione cfe on Domanda.cod_fiscale = cfe.cod_fiscale
                        LEFT JOIN sumPagamenti sp ON Domanda.Cod_fiscale = sp.Cod_fiscale
                        LEFT JOIN impAltreBorse iab ON Domanda.Num_domanda = iab.num_domanda AND Domanda.Anno_accademico = iab.anno_accademico
                    where Domanda.Anno_accademico = '20232024'
                    order by Domanda.Cod_fiscale

                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta nucleo familiare stranieri");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {

                        double calculatedISP = Math.Max((Utilities.SafeGetDouble(reader, "Superf_abitaz_MQ") + Utilities.SafeGetDouble(reader, "Sup_compl_altre_MQ")) * 500 - Utilities.SafeGetDouble(reader, "Franchigia"), 0);
                        double calculatedPatrimonioMob = Math.Max(Utilities.SafeGetDouble(reader, "Patr_mobiliare") - Utilities.SafeGetDouble(reader, "franchigia_pat_mobiliare"), 0);
                        double calculatedISPDSU = calculatedISP + calculatedPatrimonioMob;
                        double calculatedISR = Math.Max(Utilities.SafeGetDouble(reader, "Redd_complessivo") + calculatedPatrimonioMob * Utilities.SafeGetDouble(reader, "tasso_rendimento_pat_mobiliare") -
                                                        (Utilities.SafeGetDouble(reader, "detrazioniADISU") + Utilities.SafeGetDouble(reader, "detrazioniAltreBorse")), 0);

                        studenteVerifica.verificaDatiEconomici = new VerificaDatiEconomici()
                        {
                            ISPDSU = calculatedISPDSU,
                            ISR = calculatedISR,
                            SEQ = CalculateSEQ(studenteVerifica.verificaNucleoFamiliare.numeroComponentiNF),
                        };

                    }
                }
            }

        }
        void AddDatiEconomiciItalianiInt(List<string> codiciFiscaliStranieri)
        {
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");


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

                foreach (var cf in codiciFiscaliStranieri)
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

            string dataQuery = @"
                WITH codici_pagam AS (
                    SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
                    FROM Decod_pagam_new dpn
                    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1

                    UNION

                    SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
                    FROM Decod_pagam_new dpn
                    INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                    WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
                ),
                sumPagamenti AS (
                    SELECT
                        SUM(p.imp_pagato) AS somma,
                        d.Cod_fiscale
                    FROM Pagamenti p
                    INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
                    WHERE
                        p.Ritirato_azienda = 0
                        AND p.Ese_finanziario = 2021
                        AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
                    GROUP BY d.Cod_fiscale
                ),
                impAltreBorse AS (
                    SELECT
                        vb.num_domanda,
                        vb.anno_accademico,
                        SUM(vb.importo_borsa) AS importo_borsa
                    FROM vimporti_borsa_percepiti vb
                    INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
                    INNER JOIN vstatus_allegati vs ON a.id_allegato = vs.id_allegato
                    WHERE
                        vb.data_fine_validita IS NULL
                        AND a.data_fine_validita IS NULL
                        AND a.cod_tipo_allegato = '07'
                        AND vs.cod_status = '05'
		                AND vb.anno_accademico = 20232024
                    GROUP BY vb.num_domanda, vb.anno_accademico
                )
                SELECT
                    d.Anno_accademico,
                    d.Num_domanda,
                    d.Cod_fiscale,
                    ISNULL(sp.somma, 0) AS detrazioniADISU,
                    ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse,
                    cteISP.ISP,
                    cteISP.ISR,
                    cteISP.Scala_equivalenza as SEQ
                FROM Domanda d
                INNER JOIN #CFEstrazione cfe on d.cod_fiscale = cfe.cod_fiscale
                INNER JOIN vCertificaz_ISEE_CI cteISP ON d.Anno_accademico = cteISP.Anno_accademico AND d.Num_domanda = cteISP.Num_domanda
                LEFT JOIN sumPagamenti sp ON d.Cod_fiscale = sp.Cod_fiscale
                LEFT JOIN impAltreBorse iab ON d.Num_domanda = iab.num_domanda AND d.Anno_accademico = iab.anno_accademico
                WHERE
                    d.Anno_accademico = 20232024
                    AND d.tipo_bando = 'LZ'
                ORDER BY d.Num_domanda;
                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta dati economici redditi italiani");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {
                        double ISPdb = Utilities.SafeGetDouble(reader, "ISP");
                        double ISRdb = Utilities.SafeGetDouble(reader, "ISR");
                        if (studenteVerifica.verificaDatiEconomici != null)
                        {
                            studenteVerifica.verificaDatiEconomici.ISR += ISRdb;
                            studenteVerifica.verificaDatiEconomici.ISP += ISPdb;
                            studenteVerifica.verificaDatiEconomici.ISPDSU = studenteVerifica.verificaDatiEconomici.ISP;
                        }
                        else
                        {
                            studenteVerifica.verificaDatiEconomici = new()
                            {
                                ISR = ISRdb,
                                ISP = ISPdb,
                                ISPDSU = ISPdb,
                                SEQ = Utilities.SafeGetDouble(reader, "SEQ"),
                                detrazioni = Utilities.SafeGetDouble(reader, "detrazioniADISU") + Utilities.SafeGetDouble(reader, "detrazioniAltreBorse")
                            };
                        }
                    }
                }
            }
        }
        void AddDatiEconomiciStranieriInt(List<string> codiciFiscaliStranieri)
        {
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");


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

                foreach (var cf in codiciFiscaliStranieri)
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

            string dataQuery = @"
                    WITH codici_pagam AS (
                        SELECT dpn.Cod_tipo_pagam_new AS cod_tipo_pagam
                        FROM Decod_pagam_new dpn
                        INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                        WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND tp.visibile = 1

                        UNION

                        SELECT dpn.Cod_tipo_pagam_old AS cod_tipo_pagam
                        FROM Decod_pagam_new dpn
                        INNER JOIN Tipologie_pagam tp ON dpn.Cod_tipo_pagam_new = tp.Cod_tipo_pagam
                        WHERE LEFT(tp.Cod_tipo_pagam, 2) = 'BS' AND LEFT(tp.Cod_tipo_pagam, 3) <> 'BSA' AND tp.visibile = 1
                    ),
                    sumPagamenti AS (
                        SELECT
                            SUM(p.imp_pagato) AS somma,
                            d.Cod_fiscale
                        FROM Pagamenti p
                        INNER JOIN Domanda d ON p.Anno_accademico = d.Anno_accademico AND p.Num_domanda = d.Num_domanda
                        WHERE
                            p.Ritirato_azienda = 0
                            AND p.Ese_finanziario = 2021
                            AND p.cod_tipo_pagam IN (SELECT cod_tipo_pagam FROM codici_pagam)
                        GROUP BY d.Cod_fiscale
                    ),
                    impAltreBorse AS (
                        SELECT
                            vb.num_domanda,
                            vb.anno_accademico,
                            SUM(vb.importo_borsa) AS importo_borsa
                        FROM vimporti_borsa_percepiti vb
                        INNER JOIN Allegati a ON vb.anno_accademico = a.anno_accademico AND vb.num_domanda = a.num_domanda
                        INNER JOIN vstatus_allegati vs ON a.id_allegato = vs.id_allegato
                        WHERE
                            vb.data_fine_validita IS NULL
                            AND a.data_fine_validita IS NULL
                            AND a.cod_tipo_allegato = '07'
                            AND vs.cod_status = '05'
                            AND vb.anno_accademico = 20232024
                        GROUP BY vb.num_domanda, vb.anno_accademico
                    )
                    select 
                        Domanda.Cod_fiscale,
                        Numero_componenti,
                        Redd_complessivo,
                        Patr_mobiliare,
                        Possesso_abitaz,
                        Superf_abitaz_MQ,
                        Poss_altre_abit,
                        Sup_compl_altre_MQ,
                        Franchigia,
                        franchigia_pat_mobiliare,
                        tasso_rendimento_pat_mobiliare,
                        ISNULL(sp.somma, 0) AS detrazioniADISU,
                        ISNULL(iab.importo_borsa, 0) AS detrazioniAltreBorse
                    from vNucleo_fam_stranieri_DI
                        inner join Domanda on Domanda.Anno_accademico = vNucleo_fam_stranieri_DI.Anno_accademico and Domanda.Num_domanda = vNucleo_fam_stranieri_DI.Num_domanda
	                    inner join DatiGenerali_con dc on Domanda.Anno_accademico = dc.Anno_accademico
                        inner join #CFEstrazione cfe on Domanda.cod_fiscale = cfe.cod_fiscale
                        LEFT JOIN sumPagamenti sp ON Domanda.Cod_fiscale = sp.Cod_fiscale
                        LEFT JOIN impAltreBorse iab ON Domanda.Num_domanda = iab.num_domanda AND Domanda.Anno_accademico = iab.anno_accademico
                    where Domanda.Anno_accademico = '20232024'
                    order by Domanda.Cod_fiscale

                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta nucleo familiare stranieri");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    verificaDict.TryGetValue(codFiscale, out StudenteVerifica? studenteVerifica);
                    if (studenteVerifica != null)
                    {

                        double calculatedISP = Math.Max((Utilities.SafeGetDouble(reader, "Superf_abitaz_MQ") + Utilities.SafeGetDouble(reader, "Sup_compl_altre_MQ")) * 500, 0);
                        double calculatedPatrimonioMob = Math.Max(Utilities.SafeGetDouble(reader, "Patr_mobiliare"), 0);
                        double calculatedISPDSU = calculatedISP + calculatedPatrimonioMob;
                        double calculatedISR = Math.Max(Utilities.SafeGetDouble(reader, "Redd_complessivo") + calculatedPatrimonioMob * Utilities.SafeGetDouble(reader, "tasso_rendimento_pat_mobiliare"), 0);

                        if (studenteVerifica.verificaDatiEconomici != null)
                        {
                            studenteVerifica.verificaDatiEconomici.ISPDSU += calculatedISPDSU;
                            studenteVerifica.verificaDatiEconomici.ISR += calculatedISR;
                        }
                        else
                        {
                            studenteVerifica.verificaDatiEconomici = new VerificaDatiEconomici()
                            {
                                ISPDSU = calculatedISPDSU,
                                ISR = calculatedISR,
                                SEQ = CalculateSEQ(studenteVerifica.verificaNucleoFamiliare.numeroComponentiNF),
                            };
                        }
                    }
                }
            }
        }
        double CalculateSEQ(int numComponenti)
        {
            double calculatedSEQ = 1;

            if (numComponenti < 1)
            {
                return 1;
            }

            switch (numComponenti)
            {
                case 1:
                    calculatedSEQ = 1;
                    break;
                case 2:
                    calculatedSEQ = 1.57;
                    break;
                case 3:
                    calculatedSEQ = 2.04;
                    break;
                case 4:
                    calculatedSEQ = 2.46;
                    break;
                case 5:
                    calculatedSEQ = 2.85;
                    break;
                default:
                    calculatedSEQ = 2.85 + (numComponenti - 5) * 0.35;
                    break;
            }

            return Math.Round(calculatedSEQ, 2);
        }

        void CalcoloDatiEconomici()
        {
            foreach (StudenteVerifica studenteVerifica in verificaDict.Values.ToList())
            {
                VerificaDatiEconomici verificaDatiEconomici = studenteVerifica.verificaDatiEconomici;

                if (verificaDatiEconomici == null)
                {
                    continue;
                }

                double calculatedISEDSU = verificaDatiEconomici.ISR + 0.2 * verificaDatiEconomici.ISPDSU;
                double calculatedISEEDSU = calculatedISEDSU / verificaDatiEconomici.SEQ;
                double calculatedISPEDSU = verificaDatiEconomici.ISPDSU / verificaDatiEconomici.SEQ;

                verificaDatiEconomici.ISEDSU = Math.Round(calculatedISEDSU, 2);
                verificaDatiEconomici.ISEEDSU = Math.Round(calculatedISEEDSU, 2);
                verificaDatiEconomici.ISPEDSU = Math.Round(calculatedISPEDSU, 2);
            }
        }

        void CalcoloStatusSede()
        {
            foreach (StudenteVerifica studenteVerifica in verificaDict.Values.ToList())
            {
                bool possibileFuoriSede = false;

                VerificaResidenza studenteResidenza = studenteVerifica.verificaResidenza;
                bool italiano = studenteResidenza.residenzaItaliana;
                if (italiano)
                {
                    string provinciaResidenza = studenteResidenza.provinciaResidenzaItaliana;
                    if (provinciaResidenza != "RM" && provinciaResidenza != "VT" && provinciaResidenza != "FR" && provinciaResidenza != "LT")
                    {
                        possibileFuoriSede = true;
                    }
                }
                else
                {
                    possibileFuoriSede = true;
                }
            }
        }
    }
}