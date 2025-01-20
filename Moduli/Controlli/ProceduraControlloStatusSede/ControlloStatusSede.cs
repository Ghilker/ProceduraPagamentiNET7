using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ControlloStatusSede : BaseProcedure<ArgsControlloStatusSede>
    {
        public ControlloStatusSede(MasterForm? _masterForm, SqlConnection? connection_string) : base(_masterForm, connection_string) { }


        public string selectedAA = string.Empty;
        public string folderPath = string.Empty;
        public DataTable studentiPendolari = new();

        public override void RunProcedure(ArgsControlloStatusSede args)
        {
            selectedAA = args._selectedAA;
            folderPath = args._folderPath;

            studentiPendolari.Columns.Add("CodFiscale");
            studentiPendolari.Columns.Add("Motivo");

            PopulateStudentDomicilio();

            Utilities.ExportDataTableToExcel(studentiPendolari, folderPath);
            Logger.LogInfo(100, "Fine lavorazione");
        }
        void PopulateStudentDomicilio()
        {
            // -----------------------
            // 0) Parse the academic year
            // -----------------------
            int startYear = int.Parse(selectedAA.Substring(0, 4));
            int endYear = int.Parse(selectedAA.Substring(4, 4));
            DateTime dateRangeStart = new DateTime(startYear, 10, 1);
            DateTime dateRangeEnd = new DateTime(endYear, 9, 30);

            // -----------------------
            // 1) Prepare the query
            // -----------------------
            string dataQuery = $@"
                    SELECT 
                        LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE, 
                        LUOGO_REPERIBILITA_STUDENTE.TITOLO_ONEROSO, 
                        LUOGO_REPERIBILITA_STUDENTE.N_SERIE_CONTRATTO, 
                        LUOGO_REPERIBILITA_STUDENTE.DATA_REG_CONTRATTO,  
                        LUOGO_REPERIBILITA_STUDENTE.DATA_DECORRENZA, 
                        LUOGO_REPERIBILITA_STUDENTE.DATA_SCADENZA, 
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_CONTRATTO, 
                        LUOGO_REPERIBILITA_STUDENTE.PROROGA, 
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_PROROGA, 
                        LUOGO_REPERIBILITA_STUDENTE.ESTREMI_PROROGA,
                        LUOGO_REPERIBILITA_STUDENTE.TIPO_CONTRATTO_TITOLO_ONEROSO,
                        LUOGO_REPERIBILITA_STUDENTE.DENOM_ENTE,
                        LUOGO_REPERIBILITA_STUDENTE.DURATA_CONTRATTO,
                        LUOGO_REPERIBILITA_STUDENTE.IMPORTO_RATA
                    FROM LUOGO_REPERIBILITA_STUDENTE
                    INNER JOIN Comuni ON LUOGO_REPERIBILITA_STUDENTE.COD_COMUNE = Comuni.Cod_comune
					INNER JOIN Domanda on LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO = Domanda.Anno_accademico and LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE = Domanda.Cod_fiscale and LUOGO_REPERIBILITA_STUDENTE.tipo_bando = Domanda.Tipo_bando
					INNER JOIN vValori_calcolati vv on Domanda.Anno_accademico = vv.Anno_accademico and Domanda.Num_domanda = vv.Num_domanda and vv.Status_sede = 'B'
					INNER JOIN vEsiti_concorsiBS vb on Domanda.Anno_accademico = vb.Anno_accademico and Domanda.Num_domanda = vb.Num_domanda and vb.Cod_tipo_esito <> 0
					INNER JOIN vDATIGENERALI_dom vd on Domanda.Anno_accademico = vd.Anno_accademico and Domanda.Num_domanda = vd.Num_domanda and vd.Rifug_politico <> 1
					inner join vNucleo_familiare vn on Domanda.Anno_accademico = vn.Anno_accademico and Domanda.Num_domanda = vn.Num_domanda and vn.Numero_conviventi_estero < (vn.Num_componenti / 2)
					inner  join vEsiti_concorsiPA vp on Domanda.Anno_accademico = vp.Anno_accademico and Domanda.Num_domanda = vp.Num_domanda and vp.Cod_tipo_esito = 0
					inner join vIscrizioni vi on Domanda.Anno_accademico = vi.Anno_accademico and Domanda.Cod_fiscale = vi.Cod_fiscale and Domanda.Tipo_bando = vi.tipo_bando
					and vi.Cod_tipologia_studi <> '06'
                    WHERE 
                        (LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO = '{selectedAA}') 
                        AND (LUOGO_REPERIBILITA_STUDENTE.TIPO_LUOGO   = 'DOM') 
                        AND (LUOGO_REPERIBILITA_STUDENTE.DATA_VALIDITA = (
                            SELECT MAX(DATA_VALIDITA)
                            FROM LUOGO_REPERIBILITA_STUDENTE AS rsd
                            WHERE 
                                (COD_FISCALE    = LUOGO_REPERIBILITA_STUDENTE.COD_FISCALE) 
                                AND (ANNO_ACCADEMICO = LUOGO_REPERIBILITA_STUDENTE.ANNO_ACCADEMICO) 
                                AND (TIPO_LUOGO     = 'DOM')
                        ))
						and Domanda.Cod_fiscale not in (select Cod_fiscale from Forzature_StatusSede where Data_fine_validita is null and Status_sede = 'B')
                ";

            // -----------------------
            // 2) Read data into DTOs
            // -----------------------
            var domicilioRows = new List<StudentiDomicilioDTO>();
            Logger.LogInfo(35, "Lavorazione studenti - inserimento in domicilio");

            using (SqlCommand readData = new SqlCommand(dataQuery, CONNECTION))
            {
                readData.CommandTimeout = 9000000;

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var codFiscale = Utilities.RemoveAllSpaces(Utilities.SafeGetString(reader, "Cod_fiscale").ToUpper());

                        domicilioRows.Add(new StudentiDomicilioDTO
                        {
                            CodFiscale = codFiscale,
                            TitoloOneroso = (Utilities.SafeGetInt(reader, "TITOLO_ONEROSO") == 1),
                            ContrattoEnte = (Utilities.SafeGetInt(reader, "TIPO_CONTRATTO_TITOLO_ONEROSO") == 1),
                            SerieContratto = Utilities.SafeGetString(reader, "N_SERIE_CONTRATTO"),
                            DataRegistrazioneString = Utilities.SafeGetString(reader, "DATA_REG_CONTRATTO"),
                            DataDecorrenzaString = Utilities.SafeGetString(reader, "DATA_DECORRENZA"),
                            DataScadenzaString = Utilities.SafeGetString(reader, "DATA_SCADENZA"),
                            DurataContratto = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO"),
                            Prorogato = (Utilities.SafeGetInt(reader, "PROROGA") == 1),
                            DurataProroga = Utilities.SafeGetInt(reader, "DURATA_PROROGA"),
                            SerieProroga = Utilities.SafeGetString(reader, "ESTREMI_PROROGA"),
                            DenominazioneEnte = Utilities.SafeGetString(reader, "DENOM_ENTE"),
                            DurataContrattoEnte = Utilities.SafeGetInt(reader, "DURATA_CONTRATTO"),
                            ImportoRataEnte = Utilities.SafeGetDouble(reader, "IMPORTO_RATA")
                        });
                    }
                }
            }

            // -----------------------
            // 3) Process the rows
            // -----------------------
            foreach (var row in domicilioRows)
            {
                bool studenteDomicilioOK = false;
                Domicilio domicilio = new Domicilio();
                // Debug check
                if (row.CodFiscale == "VLNDNS01T54E885A")
                {
                    string test = ""; // Just to set a breakpoint, presumably
                }

                // ----- TITOLO ONEROSO -----
                bool titoloOneroso = row.TitoloOneroso;
                DateTime.TryParse(row.DataRegistrazioneString, out DateTime dataRegistrazione);
                DateTime.TryParse(row.DataDecorrenzaString, out DateTime dataDecorrenza);
                DateTime.TryParse(row.DataScadenzaString, out DateTime dataScadenza);

                // Only if there's a real contract
                if (titoloOneroso)
                {
                    // Calculate the overlap between the contract and the academic year period
                    DateTime effectiveStart = (dataDecorrenza > dateRangeStart) ? dataDecorrenza : dateRangeStart;
                    DateTime effectiveEnd = (dataScadenza < dateRangeEnd) ? dataScadenza : dateRangeEnd;

                    if (effectiveStart <= effectiveEnd)
                    {
                        int monthsCovered = ((effectiveEnd.Year - effectiveStart.Year) * 12)
                                          + (effectiveEnd.Month - effectiveStart.Month + 1);
                        if (monthsCovered >= 10)
                        {
                            // Mark the student as having a valid domicile for >=10 months
                            studenteDomicilioOK = true;
                        }
                    }
                }

                // ----- CONTRATTO ENTE -----
                bool contrattoEnte = row.ContrattoEnte;
                string denominazioneEnte = row.DenominazioneEnte;
                int durataContrattoEnte = row.DurataContrattoEnte;
                double importoRataEnte = row.ImportoRataEnte;
                bool contrattoEnteValido = false;

                if (contrattoEnte)
                {
                    if (string.IsNullOrWhiteSpace(denominazioneEnte))
                    {
                        // If there's no "Ente" name, consider invalid
                        studenteDomicilioOK = false;
                    }
                    else
                    {
                        // Must have at least 10 months and a rate > 0
                        if (durataContrattoEnte < 10 || importoRataEnte <= 0)
                        {
                            studenteDomicilioOK = false;
                        }
                        else
                        {
                            studenteDomicilioOK = true;
                            contrattoEnteValido = true;
                        }
                    }
                }

                // ----- Serie Contratto & Serie Proroga -----
                string serieContratto = row.SerieContratto;
                string serieProroga = row.SerieProroga;

                bool contrattoValido = contrattoEnteValido || IsValidSerie(serieContratto);
                bool prorogaValido = IsValidSerie(serieProroga);

                // If the proroga contains the same base as the contract, we consider that invalid
                if (!string.IsNullOrEmpty(serieContratto)
                    && !string.IsNullOrEmpty(serieProroga)
                    && serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    prorogaValido = false;
                }

                bool isDomicilioValidOrNotNeeded = studenteDomicilioOK && contrattoValido && !(row.Prorogato == true && !prorogaValido);

                if (isDomicilioValidOrNotNeeded)
                    continue;

                // Build the message for reasons why they were paid as pendolari
                string messaggio = string.Empty;

                if (!studenteDomicilioOK)
                {
                    messaggio += "#Durata contratto minore dieci mesi";
                }
                if (!contrattoValido)
                {
                    messaggio += $"#Serie contratto non valida: {serieContratto}";
                }
                if (row.Prorogato == true && !prorogaValido)
                {
                    messaggio +=
                        $"#Serie proroga non valida: Contratto {serieContratto} " +
                        $"- Proroga {serieProroga}";
                }

                studentiPendolari.Rows.Add(row.CodFiscale, messaggio);

            }


            // -----------------------
            // Local helper method
            // -----------------------
            bool IsValidSerie(string serie)
            {
                if (string.IsNullOrWhiteSpace(serie))
                    return false;

                serie = serie.Trim();
                // Remove trailing dots
                serie = serie.TrimEnd('.');

                // Case-insensitive matching
                RegexOptions options = RegexOptions.IgnoreCase;

                // Exclude date-only entries or date ranges
                string dateOnlyPattern1 = @"^\d{1,2}/\d{1,2}/\d{2,4}$";
                string dateOnlyPattern2 = @"^\d{1,2}/\d{1,2}/\d{2,4}\s*[\-–]\s*\d{1,2}/\d{1,2}/\d{2,4}$";
                string dateOnlyPattern3 = @"^dal\s+\d{1,2}/\d{1,2}/\d{2,4}\s+al\s+\d{1,2}/\d{1,2}/\d{2,4}$";
                string dateWordsPattern = @"^dal\s+\d{1,2}\s+\w+\s+\d{4}\s+al\s+\d{1,2}\s+\w+\s+\d{4}$";

                if (Regex.IsMatch(serie, dateOnlyPattern1, options) ||
                    Regex.IsMatch(serie, dateOnlyPattern2, options) ||
                    Regex.IsMatch(serie, dateOnlyPattern3, options) ||
                    Regex.IsMatch(serie, dateWordsPattern, options))
                {
                    return false;
                }

                // Exclude '3T' or 'serie 3T' alone
                string serieWithoutSpaces = Regex.Replace(serie, @"\s+", "");
                if (string.Equals(serieWithoutSpaces, "3T", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serieWithoutSpaces, "serie3T", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Exclude 'Foglio/part/sub/Cat' patterns unless they match a valid code
                if (Regex.IsMatch(serie, @"\b(Foglio|part|sub|Cat)\b", options) &&
                    !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
                {
                    return false;
                }

                // Exclude 'PRENOTAZIONE' unless there's a valid code
                if (Regex.IsMatch(serie, @"\bPRENOTAZIONE\b", options) &&
                    !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
                {
                    return false;
                }

                // Exclude 'automatico' unless there's a valid code
                if (Regex.IsMatch(serie, @"automatico", options) &&
                    !Regex.IsMatch(serie, @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b", options))
                {
                    return false;
                }

                // Patterns for valid codes
                string pattern1 = @"^(T|TRF|TEL)\s?[A-Z0-9]{10,50}\.?$";
                string pattern1b = @"\b(T|TRF|TEL)[A-Z0-9]{10,50}\b";
                string pattern2 = @"^[\d/\s\-]{4,}$";
                string pattern2b = @"^\d{1,20}([/\s\-]\d{1,20})+$";
                string pattern3 = @"(?i)^(.*\b(serie\s*3\s*T|serie\s*3T|serie\s*T3|serie\s*T|serie\s*IT|3\s*T|3T|T3|3/T)\b.*)$";
                string pattern4 = @"^QC([\s/]*\w+)+$";
                string pattern5 = @"(?i)^(.*\b(Protocollo|PROT\.?|prot\.?n?\.?|Protocol-?)\b.*\d+.*)$";
                string pattern6 = @"^(RA/|RM|FC/)\s*\S+$";
                // At least one digit, one letter, can include slash/hyphen/spaces, 5-50 in length
                string pattern7 = @"^(?=.*[A-Za-z])(?=.*\d)[A-Za-z0-9/\s\-]{5,50}$";

                // Check them in sequence
                if (Regex.IsMatch(serie, pattern1, options)) return true;
                if (Regex.IsMatch(serie, pattern1b, options)) return true;
                if (Regex.IsMatch(serie, pattern2, options)) return true;
                if (Regex.IsMatch(serie, pattern2b, options)) return true;
                if (Regex.IsMatch(serie, pattern3, options)) return true;
                if (Regex.IsMatch(serie, pattern4, options)) return true;
                if (Regex.IsMatch(serie, pattern5, options)) return true;
                if (Regex.IsMatch(serie, pattern6, options)) return true;
                if (Regex.IsMatch(serie, pattern7)) return true;

                // If none match, it's invalid
                return false;
            }
        }
    }
}
