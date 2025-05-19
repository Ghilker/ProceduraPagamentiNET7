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
        private static readonly Dictionary<string, int> _comuneCompatGroup = new()
        {
            // group 1
            ["G954"] = 1,
            ["L725"] = 1,

            // group 2
            ["G698"] = 2,
            ["E472"] = 2,

            // group 3
            ["D708"] = 3,
            ["D843"] = 3,

            // group 4
            ["A323"] = 4,
            ["F880"] = 4
        };

        public override void RunProcedure(ArgsControlloStatusSede args)
        {
            selectedAA = args._selectedAA;
            folderPath = args._folderPath;

            studentiPendolari.Columns.Add("CodFiscale");
            studentiPendolari.Columns.Add("TitoloOneroso");
            studentiPendolari.Columns.Add("SerieContratto");
            studentiPendolari.Columns.Add("DataRegistrazione");
            studentiPendolari.Columns.Add("DataDecorrenza");
            studentiPendolari.Columns.Add("DataScadenza");
            studentiPendolari.Columns.Add("DurataContratto");
            studentiPendolari.Columns.Add("Prorogato");
            studentiPendolari.Columns.Add("DurataProroga");
            studentiPendolari.Columns.Add("SerieProroga");
            studentiPendolari.Columns.Add("ContrattoEnte");
            studentiPendolari.Columns.Add("DenomEnte");
            studentiPendolari.Columns.Add("ImportoRataEnte");
            studentiPendolari.Columns.Add("Motivo");
            studentiPendolari.Columns.Add("StatoAttuale");
            studentiPendolari.Columns.Add("StatoSuggerito");
            studentiPendolari.Columns.Add("ComuneDom");
            studentiPendolari.Columns.Add("ComuneRes");
            studentiPendolari.Columns.Add("ComuneSede");

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
            LRS.COD_FISCALE, 
	        LRS.TITOLO_ONEROSO, 
	        LRS.N_SERIE_CONTRATTO, 
	        LRS.DATA_REG_CONTRATTO,  
	        LRS.DATA_DECORRENZA, 
	        LRS.DATA_SCADENZA, 
	        LRS.DURATA_CONTRATTO, 
	        LRS.PROROGA, 
	        LRS.DURATA_PROROGA, 
	        LRS.ESTREMI_PROROGA,
	        LRS.TIPO_CONTRATTO_TITOLO_ONEROSO,
	        LRS.DENOM_ENTE,
	        LRS.IMPORTO_RATA,
            LRS.COD_COMUNE  AS CodComuneDom,
            vr.Cod_comune   AS CodComuneRes,
            cl.Comune_Sede_studi AS CodComuneSede,
            vv.Status_sede,
            prev.DATA_SCADENZA AS PrevScadenza,
            DATEDIFF(day, prev.DATA_SCADENZA, LRS.DATA_REG_CONTRATTO) AS GiorniDallaScad,
	        dbo.SlashBlocchi(vv.Num_domanda, vv.Anno_accademico, '') as cod_blocchi
        FROM 
            LUOGO_REPERIBILITA_STUDENTE AS LRS
            OUTER APPLY (
                SELECT TOP 1
                       prev.DATA_SCADENZA
                FROM   LUOGO_REPERIBILITA_STUDENTE prev
                WHERE  prev.COD_FISCALE      = LRS.COD_FISCALE
                  AND  prev.ANNO_ACCADEMICO  = LRS.ANNO_ACCADEMICO
                  AND  prev.TIPO_LUOGO       = 'DOM'
                  AND  prev.PROROGA          = 0                -- original contract
                  AND  prev.DATA_VALIDITA    < LRS.DATA_VALIDITA
                ORDER BY prev.DATA_VALIDITA DESC
            ) AS prev
            INNER JOIN Comuni 
                ON LRS.COD_COMUNE = Comuni.Cod_comune
            INNER JOIN Domanda
                ON LRS.ANNO_ACCADEMICO = Domanda.Anno_accademico
               AND LRS.COD_FISCALE     = Domanda.Cod_fiscale
               AND LRS.tipo_bando      = Domanda.Tipo_bando

            -- Must be Status_sede = 'B'
            INNER JOIN vValori_calcolati AS vv
                ON Domanda.Anno_accademico = vv.Anno_accademico
               AND Domanda.Num_domanda     = vv.Num_domanda
               AND vv.Status_sede in ('B', 'D')

            -- Must have tipo esito BS != 0
            INNER JOIN vEsiti_concorsiBS AS vb
                ON Domanda.Anno_accademico = vb.Anno_accademico
               AND Domanda.Num_domanda     = vb.Num_domanda
               AND vb.Cod_tipo_esito <> 0

            -- Must not be rifug politico
            INNER JOIN vDATIGENERALI_dom AS vd
                ON Domanda.Anno_accademico = vd.Anno_accademico
               AND Domanda.Num_domanda     = vd.Num_domanda
               AND vd.Rifug_politico <> 1

            -- Additional constraints for vIscrizioni
            INNER JOIN vIscrizioni AS vi
                ON Domanda.Anno_accademico = vi.Anno_accademico
               AND Domanda.Cod_fiscale     = vi.Cod_fiscale
               AND Domanda.Tipo_bando      = vi.tipo_bando
               AND vi.Cod_tipologia_studi <> '06'

            INNER JOIN Corsi_laurea           AS cl
                  ON vi.Cod_corso_laurea     = cl.Cod_corso_laurea
                 AND vi.Anno_accad_inizio    = cl.Anno_accad_inizio
                 AND vi.Cod_tipo_ordinamento = cl.Cod_tipo_ordinamento
                 AND vi.Cod_facolta          = cl.Cod_facolta
                 AND vi.Cod_sede_studi       = cl.Cod_sede_studi
                 AND vi.Cod_tipologia_studi  = cl.Cod_tipologia_studi

            -- Join to vResidenza to handle the 'EE' logic
            INNER JOIN vResidenza AS vr
                ON vr.Cod_fiscale      = Domanda.Cod_fiscale
               AND vr.Anno_accademico = Domanda.Anno_accademico
               -- Adjust if necessary to match the rest of your keys
        WHERE
            -- Same check for LUOGO_REPERIBILITA_STUDENTE
            LRS.ANNO_ACCADEMICO = '{selectedAA}'
            AND LRS.TIPO_LUOGO = 'DOM'
            AND LRS.DATA_VALIDITA = (
                 SELECT MAX(DATA_VALIDITA) 
                 FROM LUOGO_REPERIBILITA_STUDENTE AS rsd
                 WHERE rsd.COD_FISCALE      = LRS.COD_FISCALE
                   AND rsd.ANNO_ACCADEMICO = LRS.ANNO_ACCADEMICO
                   AND rsd.TIPO_LUOGO      = 'DOM'
            )

            -- Must NOT be in forzature as B
            AND Domanda.Cod_fiscale NOT IN (
                 SELECT Cod_Fiscale
                 FROM Forzature_StatusSede
                 WHERE Data_fine_validita IS NULL
                   AND Status_sede = 'B'
                   AND Anno_Accademico = '{selectedAA}'
            )

            AND Domanda.Cod_fiscale NOT IN (
                 SELECT Cod_Fiscale
                 FROM Forzature_StatusSede
                 WHERE Data_fine_validita IS NULL
                   AND Status_sede = 'D'
                   AND Anno_Accademico = '{selectedAA}'
            )

            -- Must NOT have tipo esito PA <> 0
            AND Domanda.Num_domanda NOT IN (
                 SELECT Num_domanda
                 FROM vEsiti_concorsiPA
                 WHERE (Cod_tipo_esito <> 0)
                   AND (Anno_accademico = '{selectedAA}')
            )

            -- Now handle the ""EE"" logic via 'NOT' approach:
            AND NOT (
                vr.provincia_residenza = 'EE'
                AND Domanda.Num_domanda IN
                (
                     SELECT d1.Num_domanda
                     FROM Domanda d1
                     INNER JOIN vNucleo_familiare vn1
                          ON d1.Anno_accademico = vn1.Anno_accademico
                         AND d1.Num_domanda     = vn1.Num_domanda
                     WHERE vn1.Numero_conviventi_estero >= vn1.Num_componenti / 2
                )
		
            
            );


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
                            ImportoRataEnte = Utilities.SafeGetDouble(reader, "IMPORTO_RATA"),
                            StatusSede = Utilities.SafeGetString(reader, "Status_sede"),
                            CodBlocchi = Utilities.SafeGetString(reader, "cod_blocchi"),
                            ComuneDomicilio = Utilities.SafeGetString(reader, "CodComuneDom"),
                            ComuneResidenza = Utilities.SafeGetString(reader, "CodComuneRes"),
                            ComuneSedeStudi = Utilities.SafeGetString(reader, "CodComuneSede"),
                            GiorniDallaScad = Utilities.SafeGetInt(reader, "GiorniDallaScad"),
                            PrevScadenza = Utilities.SafeGetDateTime(reader, "PrevScadenza"),


                        });
                    }
                }
            }

            // -----------------------
            // 3) Process the rows
            // -----------------------
            foreach (var row in domicilioRows)
            {

                if (row.CodFiscale == "MLNNNL04M44F839V")
                {
                    string test = "";
                }
                bool studenteDomicilioOK = false;
                bool hasVccBlock = !string.IsNullOrWhiteSpace(row.CodBlocchi) &&
                   row.CodBlocchi.IndexOf("/VVC", StringComparison.OrdinalIgnoreCase) >= 0;

                bool domEqualsRes = AreComuniCompatible(row.ComuneDomicilio,
                                        row.ComuneResidenza);


                bool domEqualsSeat = string.Equals(row.ComuneDomicilio,
                                                    row.ComuneSedeStudi,
                                                    StringComparison.OrdinalIgnoreCase);

                bool geoOk = !domEqualsRes && domEqualsSeat;


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
                        int monthsCovered = 0;

                        // Start from the 1st day of the month of 'effectiveStart'
                        DateTime currentMonthStart = new DateTime(effectiveStart.Year, effectiveStart.Month, 1);

                        // Iterate while the start of the month is within the effective coverage
                        while (currentMonthStart <= effectiveEnd)
                        {
                            // End of the current month
                            DateTime currentMonthEnd = currentMonthStart.AddMonths(1).AddDays(-1);

                            // Determine the portion of this month actually covered
                            // Coverage starts on the later of (effectiveStart or currentMonthStart)
                            DateTime coverageStart = (currentMonthStart < effectiveStart) ? effectiveStart : currentMonthStart;
                            // Coverage ends on the earlier of (effectiveEnd or currentMonthEnd)
                            DateTime coverageEnd = (currentMonthEnd > effectiveEnd) ? effectiveEnd : currentMonthEnd;

                            // Calculate how many days are covered in this month
                            double daysCovered = (coverageEnd - coverageStart).TotalDays + 1;

                            // If coverage in this calendar month is at least 15 days, consider it a "full month"
                            if (daysCovered >= 15)
                            {
                                monthsCovered++;
                            }

                            // Move to the next month
                            currentMonthStart = currentMonthStart.AddMonths(1);
                        }

                        // Check if there are at least 10 months fully covered (>=25 days each)
                        if (monthsCovered >= 10)
                        {
                            studenteDomicilioOK = true;
                        }
                    }
                }


                // ----- CONTRATTO ENTE -----
                bool contrattoEnte = row.ContrattoEnte;
                string denominazioneEnte = row.DenominazioneEnte;
                int durataContratto = row.DurataContratto;
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
                        if (durataContratto < 10 || importoRataEnte <= 0)
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

                bool prorogaTempisticaOK =
    !(row.Prorogato ?? false)            // nothing to check if no proroga
    || row.GiorniDallaScad <= 30;        // filed within 30 days

                // final validity
                bool isDomicilioValidOrNotNeeded =
                        studenteDomicilioOK
                     && contrattoValido
                     && !(row.Prorogato == true && !prorogaValido)
                     && prorogaTempisticaOK              // ← NEW
                     && geoOk;

                // Build the message for reasons why they were paid as pendolari
                string messaggio = string.Empty;
                if (!geoOk)
                {
                    messaggio += "#Comune domicilio/residenza/sede non coerenti";
                }
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

                if (row.Prorogato == true && !prorogaTempisticaOK)
                {
                    messaggio += $"#Proroga inserita dopo {row.GiorniDallaScad} giorni (limite 30)";
                }

                if (row.StatusSede == "B" && !isDomicilioValidOrNotNeeded)
                {
                    studentiPendolari.Rows.Add(
                        row.CodFiscale,
                        row.TitoloOneroso,
                        row.SerieContratto,
                        row.DataRegistrazioneString,
                        row.DataDecorrenzaString,
                        row.DataScadenzaString,
                        row.DurataContratto,
                        row.Prorogato,
                        row.DurataProroga,
                        row.SerieProroga,
                        row.ContrattoEnte,
                        row.DenominazioneEnte,
                        row.ImportoRataEnte,
                        messaggio,
                        "B",                            // StatoAttuale
                        "D",                             // StatoSuggerito
                        row.ComuneDomicilio,
                        row.ComuneResidenza,
                        row.ComuneSedeStudi
                    );
                    continue;
                }

                if (row.StatusSede == "B" && isDomicilioValidOrNotNeeded && hasVccBlock)
                {
                    string motivoVcc = "#Rimuovere blocco VVC";
                    studentiPendolari.Rows.Add(
                        row.CodFiscale,
                        row.TitoloOneroso,
                        row.SerieContratto,
                        row.DataRegistrazioneString,
                        row.DataDecorrenzaString,
                        row.DataScadenzaString,
                        row.DurataContratto,
                        row.Prorogato,
                        row.DurataProroga,
                        row.SerieProroga,
                        row.ContrattoEnte,
                        row.DenominazioneEnte,
                        row.ImportoRataEnte,
                        motivoVcc,
                        "B",
                        "B",
                        row.ComuneDomicilio,
                        row.ComuneResidenza,
                        row.ComuneSedeStudi
                    );
                    continue;
                }

                // ─── D ➜ B ────────────────────────────────────────────────────────────────
                if (row.StatusSede == "D" && isDomicilioValidOrNotNeeded)
                {
                    string motivazione = "#Contratto valido almeno 10 mesi";
                    // (aggiungi eventuali altri motivi se utili)

                    studentiPendolari.Rows.Add(
                        row.CodFiscale,
                        row.TitoloOneroso,
                        row.SerieContratto,
                        row.DataRegistrazioneString,
                        row.DataDecorrenzaString,
                        row.DataScadenzaString,
                        row.DurataContratto,
                        row.Prorogato,
                        row.DurataProroga,
                        row.SerieProroga,
                        row.ContrattoEnte,
                        row.DenominazioneEnte,
                        row.ImportoRataEnte,
                        motivazione,
                        "D",                            // StatoAttuale
                        "B",                             // StatoSuggerito
                        row.ComuneDomicilio,
                        row.ComuneResidenza,
                        row.ComuneSedeStudi
                    );
                }

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

                string serieNoSpaces = Regex.Replace(serie, @"\s+", "");
                if (string.Equals(serieNoSpaces, "3T", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serieNoSpaces, "T3", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serieNoSpaces, "serie3T", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(serieNoSpaces, "serieT3", StringComparison.OrdinalIgnoreCase))
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
        private static bool AreComuniCompatible(string? c1, string? c2)
        {
            if (string.Equals(c1, c2, StringComparison.OrdinalIgnoreCase))
                return true;

            return _comuneCompatGroup.TryGetValue(c1 ?? string.Empty, out int g1) &&
                   _comuneCompatGroup.TryGetValue(c2 ?? string.Empty, out int g2) &&
                   g1 == g2;
        }
    }
}