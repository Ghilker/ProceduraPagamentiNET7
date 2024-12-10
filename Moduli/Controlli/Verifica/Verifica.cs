using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProceduraVerifica = ProcedureNet7.Verifica;

namespace ProcedureNet7.Verifica
{
    internal class Verifica : BaseProcedure<ArgsVerifica>
    {
        DatiVerifica _datiVerifica;
        string selectedAA = "20232024";

        Dictionary<string, StudenteVerifica> studenti = new();

        public Verifica(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }

        public override void RunProcedure(ArgsVerifica args)
        {

            AddDatiVerifica();

            CreateVerificaList();
            CreateCFTable();
            AddCarrieraPregressa();
            AddBeneficiRichiesti();
        }

        void AddDatiVerifica()
        {
            string dataQuery = $"SELECT top(1) Cod_tipo_graduat from graduatorie where anno_accademico = '{selectedAA}' and Cod_beneficio = 'bs' order by Cod_tipo_graduat desc";
            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - estrazione dati generali");
            bool graduatoriaDefinitiva = false;

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    graduatoriaDefinitiva = Utilities.SafeGetInt(reader, "Cod_tipo_graduat") == 1;
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
                where Anno_accademico = '{selectedAA}'
            ";

            readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - estrazione dati generali");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    _datiVerifica = new DatiVerifica()
                    {
                        sogliaISEE = Utilities.SafeGetDouble(reader, "Soglia_Isee"),
                        sogliaISPE = Utilities.SafeGetDouble(reader, "Soglia_Ispe"),
                        importoBorsaA = Utilities.SafeGetDouble(reader, "Importo_borsa_A"),
                        importoBorsaB = Utilities.SafeGetDouble(reader, "Importo_borsa_B"),
                        importoBorsaC = Utilities.SafeGetDouble(reader, "Importo_borsa_C"),
                        quotaMensa = Utilities.SafeGetDouble(reader, "quota_mensa"),
                        graduatoriaDefinitiva = graduatoriaDefinitiva
                    };
                }
            }
        }

        void CreateVerificaList()
        {
            string dataQuery = $@"
                    SELECT   distinct
						domanda.Cod_fiscale, 
						domanda.num_domanda, 
						vs.status_compilazione,
						Studente.Cognome,
						Studente.Nome,
						studente.Data_nascita,
						studente.Sesso,
						Studente.Cod_comune_nasc,
						vcit.Cod_cittadinanza
						,vdom.Invalido
						,vdom.Rifug_politico
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
                    FROM            
						Domanda
						left outer join Studente on Domanda.Cod_fiscale = Studente.Cod_fiscale
						left outer join vCittadinanza vcit on Domanda.Cod_fiscale= vcit.Cod_fiscale
						left outer join vDATIGENERALI_dom vdom on Domanda.Anno_accademico = vdom.Anno_accademico and Domanda.Num_domanda = vdom.Num_domanda
						left outer join vResidenza vres on Domanda.Anno_accademico = vres.ANNO_ACCADEMICO and Domanda.Cod_fiscale = vres.COD_FISCALE and Domanda.Tipo_bando = vres.tipo_bando
						left outer join vDomicilio vd on Domanda.Anno_accademico = vd.Anno_accademico and Domanda.Cod_fiscale = vd.COD_FISCALE
						left outer join vIscrizioni visc on Domanda.Anno_accademico = visc.Anno_accademico and Domanda.Cod_fiscale = visc.Cod_fiscale and Domanda.Tipo_bando = visc.tipo_bando
						left outer join Corsi_laurea on visc.Cod_corso_laurea = Corsi_laurea.Cod_corso_laurea and visc.Cod_facolta = Corsi_laurea.Cod_facolta and visc.Cod_sede_studi = Corsi_laurea.Cod_sede_studi
						left outer join vMerito on Domanda.Anno_accademico = vMerito.Anno_accademico and Domanda.Num_domanda = vMerito.Num_domanda
						left outer join vStatus_compilazione vs on Domanda.Anno_accademico = vs.anno_accademico and Domanda.Num_domanda = vs.num_domanda

                    WHERE        
						Domanda.Anno_accademico = '{selectedAA}'
						and Domanda.Tipo_bando = 'lz' 
						--and domanda.cod_fiscale like 'xxxxxxxxxxxx%'
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
                    if (!studenti.ContainsKey(codFiscale))
                    {
                        StudenteVerifica StudenteVerificaOld = new ProceduraVerifica.StudenteVerifica
                        {
                            codFiscale = codFiscale,
                            numDomanda = Utilities.SafeGetString(reader, "Num_domanda")
                        };

                        StudenteVerificaOld.statusDomanda = new ProceduraVerifica.StatusDomanda(Utilities.SafeGetInt(reader, "status_compilazione"));

                        StudenteVerificaOld.anagrafica = new ProceduraVerifica.Anagrafica
                        {
                            cognome = Utilities.SafeGetString(reader, "cognome"),
                            nome = Utilities.SafeGetString(reader, "nome"),
                            dataNascita = DateTime.Parse(Utilities.SafeGetString(reader, "data_nascita")),
                            sesso = Utilities.SafeGetString(reader, "sesso"),
                            codComuneNascita = Utilities.SafeGetString(reader, "cod_comune_nasc"),
                            codCittadinanza = Utilities.SafeGetString(reader, "cod_cittadinanza"),
                            invalido = Utilities.SafeGetString(reader, "invalido") == "1",
                            rifugiatoPolitico = Utilities.SafeGetString(reader, "Rifug_politico") == "1"
                        };

                        StudenteVerificaOld.residenza = new ProceduraVerifica.Residenza
                        {
                            residenzaItaliana = Utilities.SafeGetString(reader, "provincia_residenza") == "EE" ? false : true,
                            codProvinciaResidenzaItaliana = Utilities.SafeGetString(reader, "provincia_residenza"),
                            codComuneResidenza = Utilities.SafeGetString(reader, "cod_comune")
                        };

                        StudenteVerificaOld.domicilio = new ProceduraVerifica.Domicilio
                        {
                            possiedeDomicilio = Utilities.SafeGetString(reader, "cod_comune_domicilio") == string.Empty ? false : true
                        };

                        if (StudenteVerificaOld.domicilio.possiedeDomicilio)
                        {
                            ProceduraVerifica.Domicilio verificaDomicilio = StudenteVerificaOld.domicilio;
                            verificaDomicilio.codComuneDomicilio = Utilities.SafeGetString(reader, "cod_comune_domicilio");
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
                                        verificaDomicilio.tipologiaEnteIstituto = ProceduraVerifica.Domicilio.TipologiaEnteIstituto.EntePubblicoPrivato;
                                        break;
                                    case "ir":
                                        verificaDomicilio.tipologiaEnteIstituto = ProceduraVerifica.Domicilio.TipologiaEnteIstituto.IstitutoReligioso;
                                        break;
                                    case "fa":
                                        verificaDomicilio.tipologiaEnteIstituto = ProceduraVerifica.Domicilio.TipologiaEnteIstituto.FondazioneAssociazione;
                                        break;
                                    case "se":
                                        verificaDomicilio.tipologiaEnteIstituto = ProceduraVerifica.Domicilio.TipologiaEnteIstituto.ErasmusSocrates;
                                        break;
                                }

                                verificaDomicilio.denominazioneIstituto = Utilities.SafeGetString(reader, "DENOM_ENTE");
                                verificaDomicilio.durataMesiContrattoIstituto = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO");
                                verificaDomicilio.importoMensileRataIstituto = Utilities.SafeGetDouble(reader, "IMPORTO_RATA");
                            }
                        }

                        StudenteVerificaOld.merito = new ProceduraVerifica.Merito
                        {
                            codSedeStudi = Utilities.SafeGetString(reader, "cod_sede_studi"),
                            comuneSedeStudi = Utilities.SafeGetString(reader, "Comune_Sede_studi"),
                            codTipoCorso = Utilities.SafeGetString(reader, "Cod_tipologia_studi"),
                            codCorsoLaurea = Utilities.SafeGetString(reader, "Cod_corso_laurea"),
                            aaPrimaImmatricolazione = Utilities.SafeGetString(reader, "Anno_immatricolaz"),
                            annoCorsoDichiarato = Utilities.SafeGetString(reader, "Anno_corso"),
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
                        studenti.Add(codFiscale, StudenteVerificaOld);
                    }
                }
            }

            Logger.LogInfo(45, "UPDATE:Verifica - creazione lista - completato");
        }

        void CreateCFTable()
        {
            #region CREAZIONE CF TABLE
            Logger.LogInfo(30, "Lavorazione studenti");
            List<string> codFiscali = studenti.Keys.ToList();

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
            string dataQuery = $@"
                    SELECT vCARRIERA_PREGRESSA.*, 
                           ben.anni_beneficiario,
                           res.anni_restituiti
                    FROM vCARRIERA_PREGRESSA
                    inner join #CFEstrazione cfe ON vCARRIERA_PREGRESSA.Cod_fiscale = cfe.Cod_fiscale 
                    CROSS APPLY dbo.SlashAnniBeneficiarioBS(vCARRIERA_PREGRESSA.Cod_fiscale, '{selectedAA}') AS ben
                    CROSS APPLY dbo.SlashAnniRestituitiBS(vCARRIERA_PREGRESSA.Cod_fiscale, '{selectedAA}') AS res
                    WHERE vCARRIERA_PREGRESSA.Anno_accademico = '{selectedAA}'
                    ORDER BY vCARRIERA_PREGRESSA.Cod_fiscale;
                ";

            SqlCommand readData = new(dataQuery, CONNECTION);
            Logger.LogInfo(45, "Verifica - aggiunta carriera pregressa");

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    string codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                    studenti.TryGetValue(codFiscale, out StudenteVerifica? StudenteVerificaOld);
                    if (StudenteVerificaOld != null)
                    {
                        ProceduraVerifica.Merito verificaIscrizione = StudenteVerificaOld.merito;

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
            string dataQuery = $@"
                WITH FilteredDomanda AS (
                    SELECT d.Num_domanda, d.Anno_accademico, d.Cod_fiscale, d.Data_validita, d.Utente, d.Tipo_bando, d.Id_Domanda, d.DataCreazioneRecord
                    FROM Domanda AS d
                    INNER JOIN #CFEstrazione cfe on d.cod_fiscale = cfe.cod_fiscale
                    INNER JOIN vStatus_compilazione AS vv ON d.Anno_accademico = vv.anno_accademico AND d.Num_domanda = vv.num_domanda
                    WHERE d.Anno_accademico = '{selectedAA}' 
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

                    studenti.TryGetValue(codFiscale, out StudenteVerifica? StudenteVerificaOld);
                    if (StudenteVerificaOld != null)
                    {
                        if (StudenteVerificaOld.beneficiRichiesti == null)
                        {
                            StudenteVerificaOld.beneficiRichiesti = new ProceduraVerifica.BeneficiRichiesti();
                        }

                        ProceduraVerifica.BeneficiRichiesti verificaBeneficiRichiesti = StudenteVerificaOld.beneficiRichiesti;

                        string codBeneficio = Utilities.SafeGetString(reader, "Cod_beneficio");
                        switch (codBeneficio)
                        {
                            case "BS":
                                verificaBeneficiRichiesti.richiestaBS = true;
                                verificaBeneficiRichiesti.beneficiarioAltriEnti = Utilities.SafeGetString(reader, "Possesso_altra_borsa") == "1";
                                verificaBeneficiRichiesti.importoBeneficiPrecedenti = Utilities.SafeGetDouble(reader, "importo_borsa");
                                break;
                            case "PA":
                                verificaBeneficiRichiesti.richiestaPA = true;
                                verificaBeneficiRichiesti.beneficiOspitalitaResidenziale = Utilities.SafeGetString(reader, "Beneficio_residenziale") == "1";
                                break;
                            case "CI":
                                verificaBeneficiRichiesti.richiestaCI = true;
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


    }
}