using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Windows.Forms;

namespace ProcedureNet7
{
    internal class ControlloDomicilio : BaseProcedure<ArgsControlloDomicilio>
    {
        public string selectedAA = string.Empty;

        // Mappa codici → descrizione
        private static readonly Dictionary<string, string> ErrorDescriptions = new()
        {
            ["001"] = "Codice fiscale mancante per il domicilio.",
            ["002"] = "Data registrazione contratto non valida.",
            ["003"] = "Data decorrenza contratto non valida.",
            ["004"] = "Data scadenza contratto non valida.",
            ["005"] = "Contratto copre meno di 10 mesi nel periodo indicato dal bando.",
            ["006"] = "Contratto non copre alcun periodo utile nell'anno accademico.",
            ["007"] = "Contratto ente indicato ma denominazione ente mancante.",
            ["008"] = "Durata contratto ente inferiore a 10 mesi.",
            ["009"] = "Importo rata ente nullo o negativo.",
            ["010"] = "La serie della proroga è uguale alla serie del contratto.",
            ["011"] = "Numero di serie del contratto non valido.",
            ["012"] = "Numero di serie della proroga non valida."
        };

        private sealed class IstanzaDomicilioOpenDTO
        {
            public string AnnoAccademico { get; set; } = "";
            public string CodFiscale { get; set; } = "";
            public int NumDomanda { get; set; }
            public int NumIstanza { get; set; }
            public DateTime? DataValiditaIstanza { get; set; }

            public string ComuneDomicilio { get; set; } = "";
            public bool TitoloOneroso { get; set; }
            public bool ContrattoEnte { get; set; }

            public string SerieContratto { get; set; } = "";
            public string DataRegistrazioneString { get; set; } = "";
            public string DataDecorrenzaString { get; set; } = "";
            public string DataScadenzaString { get; set; } = "";
            public int DurataContratto { get; set; }
            public bool Prorogato { get; set; }
            public int DurataProroga { get; set; }
            public string SerieProroga { get; set; } = "";

            public string DenominazioneEnte { get; set; } = "";
            public double ImportoRataEnte { get; set; }
        }

        public ControlloDomicilio(MasterForm? _masterForm, SqlConnection? connection_string)
            : base(_masterForm, connection_string) { }

        private bool AskOperatorSendMessagesAndPersist()
        {
            // Se non c'è UI interattiva, manteniamo il comportamento storico (procedi).
            if (!Environment.UserInteractive)
                return true;

            // Se non ho form o handle non creato: fallback (procedi)
            if (_masterForm == null || _masterForm.IsDisposed || !_masterForm.IsHandleCreated)
                return true;

            bool result = true;

            _ = _masterForm.Invoke((MethodInvoker)delegate
            {
                var dlg = MessageBox.Show(
                    _masterForm,
                    "Inviare i messaggi agli studenti e salvare su database?\n\n" +
                    "Sì: procede (inserimenti DB + messaggi).\n" +
                    "No: genera solo il file di report (nessuna scrittura su DB).",
                    "Controllo Domicilio",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button1
                );

                result = (dlg == DialogResult.Yes);
            });

            return result;
        }

        private static int ReadInt(SqlDataReader rdr, string col)
        {
            object o = rdr[col];
            if (o == DBNull.Value) return 0;

            if (o is int i) return i;
            if (o is short s) return s;
            if (o is long l) return (l > int.MaxValue) ? int.MaxValue : (int)l;

            var str = (Convert.ToString(o) ?? "").Trim();
            if (str.Length == 0) return 0;
            return int.TryParse(str, out var v) ? v : 0;
        }

        private static double ReadDouble(SqlDataReader rdr, string col)
        {
            object o = rdr[col];
            if (o == DBNull.Value) return 0.0;

            if (o is double d) return d;
            if (o is float f) return f;
            if (o is decimal m) return (double)m;

            var str = (Convert.ToString(o) ?? "").Trim();
            if (str.Length == 0) return 0.0;

            return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v
                : (double.TryParse(str, NumberStyles.Any, CultureInfo.CurrentCulture, out v) ? v : 0.0);
        }

        private static string ReadString(SqlDataReader rdr, string col)
        {
            object o = rdr[col];
            return o == DBNull.Value ? string.Empty : (Convert.ToString(o) ?? string.Empty);
        }

        private static DateTime? ReadDateTimeNullable(SqlDataReader rdr, string col)
        {
            object o = rdr[col];
            if (o == DBNull.Value) return null;

            if (o is DateTime dt) return dt;

            var str = (Convert.ToString(o) ?? "").Trim();
            if (str.Length == 0) return null;

            return DateTime.TryParse(str, out var parsed) ? parsed : (DateTime?)null;
        }

        public override void RunProcedure(ArgsControlloDomicilio args)
        {
            selectedAA = args._selectedAA;
            string folderPath = args._folderPath;

            int startYear = int.Parse(selectedAA.Substring(0, 4));
            int endYear = int.Parse(selectedAA.Substring(4, 4));
            DateTime dateRangeStart = new(startYear, 10, 1);
            DateTime dateRangeEnd = new(endYear, 9, 30);

            bool doDbWritesAndSendMessages = AskOperatorSendMessagesAndPersist();

            // 1) LUOGO_REPERIBILITA_STUDENTE (come prima)
            string dataQuery = @"
    SELECT
        d.Anno_accademico                 AS ANNO_ACCADEMICO,
        d.Num_domanda                     AS Num_domanda,
        d.Cod_fiscale                     AS COD_FISCALE,

        -- Domicilio (ultimo record DOM se presente)
        lrs.COD_COMUNE                    AS COD_COMUNE,
        lrs.TITOLO_ONEROSO                AS TITOLO_ONEROSO,
        lrs.N_SERIE_CONTRATTO             AS N_SERIE_CONTRATTO,
        lrs.DATA_REG_CONTRATTO            AS DATA_REG_CONTRATTO,
        lrs.DATA_DECORRENZA               AS DATA_DECORRENZA,
        lrs.DATA_SCADENZA                 AS DATA_SCADENZA,
        lrs.DURATA_CONTRATTO              AS DURATA_CONTRATTO,
        lrs.PROROGA                       AS PROROGA,
        lrs.DURATA_PROROGA                AS DURATA_PROROGA,
        lrs.ESTREMI_PROROGA               AS ESTREMI_PROROGA,
        lrs.TIPO_CONTRATTO_TITOLO_ONEROSO AS TIPO_CONTRATTO_TITOLO_ONEROSO,
        lrs.DENOM_ENTE                    AS DENOM_ENTE,
        lrs.IMPORTO_RATA                  AS IMPORTO_RATA,
        lrs.DATA_VALIDITA                 AS DATA_VALIDITA

    FROM Domanda d
    INNER JOIN vValori_calcolati vv
        ON d.Anno_accademico = vv.Anno_accademico
       AND d.Num_domanda     = vv.Num_domanda
    INNER JOIN vEsiti_concorsi ve
        ON d.Anno_accademico = ve.Anno_accademico
       AND d.Num_domanda     = ve.Num_domanda
       AND ve.Cod_beneficio  = 'BS'
       AND ve.Cod_tipo_esito <> 0

    OUTER APPLY (
        SELECT TOP (1)
            LRS.COD_COMUNE,
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
            LRS.DATA_VALIDITA
        FROM LUOGO_REPERIBILITA_STUDENTE LRS
        WHERE LRS.COD_FISCALE     = d.Cod_fiscale
          AND LRS.ANNO_ACCADEMICO = d.Anno_accademico
          AND LRS.TIPO_LUOGO      = 'DOM'
        ORDER BY LRS.DATA_VALIDITA DESC
    ) lrs

    WHERE d.Anno_accademico = @aa
      AND d.Tipo_bando      = 'lz'
      AND vv.Status_sede    = 'B'
      AND NOT EXISTS (
            SELECT 1
            FROM Validazione_contratto_locazione vcl
            WHERE vcl.anno_accademico  = d.Anno_accademico
              AND vcl.cod_fiscale      = d.Cod_fiscale
              AND vcl.data_fine_validita IS NULL
      );
";
            // 2) ISTANZE APERTE NON LAVORATE (idg.esito_istanza IS NULL e idg.data_fine_validita IS NULL)
            //    + contratto locazione istanza (icl) corrente
            //    Una riga per CF (ultima istanza aperta).
            string istanzaOpenQuery = @"
                ;WITH q AS (
                    SELECT
                        idg.Anno_accademico,
                        idg.Cod_fiscale,
                        idg.Num_domanda,
                        idg.Num_istanza,
                        idg.Data_validita AS DataValiditaIstanza,
                        icl.COD_COMUNE,
                        icl.TITOLO_ONEROSO,
                        icl.TIPO_CONTRATTO_TITOLO_ONEROSO,
                        icl.N_SERIE_CONTRATTO,
                        icl.DATA_REG_CONTRATTO,
                        icl.DATA_DECORRENZA,
                        icl.DATA_SCADENZA,
                        icl.DURATA_CONTRATTO,
                        icl.PROROGA,
                        icl.DURATA_PROROGA,
                        icl.ESTREMI_PROROGA,
                        icl.DENOM_ENTE,
                        icl.IMPORTO_RATA,
                        ROW_NUMBER() OVER (
                            PARTITION BY idg.Cod_fiscale
                            ORDER BY idg.Data_validita DESC, idg.Num_istanza DESC
                        ) AS rn
                    FROM Istanza_dati_generali idg
                    INNER JOIN Istanza_status iis
                        ON idg.Num_istanza = iis.Num_istanza
                       AND iis.data_fine_validita IS NULL
                    INNER JOIN Istanza_Contratto_locazione icl
                        ON idg.Num_istanza = icl.Num_istanza
                       AND icl.data_fine_validita IS NULL
                    WHERE idg.Anno_accademico = @aa
                      AND idg.Data_fine_validita IS NULL
                      AND idg.Esito_istanza IS NULL
                )
                SELECT *
                FROM q
                WHERE rn = 1;
            ";

            var domicilioRows = new List<StudentiDomicilioDTO>();

            Logger.LogInfo(35, "Lavorazione studenti - inserimento in domicilio" +
                              (doDbWritesAndSendMessages ? " (DB+Messaggi: SI)" : " (Solo file: SI)"));

            using (SqlCommand readData = new SqlCommand(dataQuery, CONNECTION))
            {
                readData.CommandTimeout = 900000;
                readData.Parameters.AddWithValue("@aa", selectedAA);

                using (SqlDataReader reader = readData.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var codFiscale = Utilities.RemoveAllSpaces(
                            Utilities.SafeGetString(reader, "COD_FISCALE").ToUpper()
                        );

                        domicilioRows.Add(new StudentiDomicilioDTO
                        {
                            AnnoAccademico = Utilities.SafeGetString(reader, "ANNO_ACCADEMICO"),
                            CodFiscale = codFiscale,
                            NumDomanda = Utilities.SafeGetInt(reader, "Num_domanda"),
                            ComuneDomicilio = Utilities.SafeGetString(reader, "COD_COMUNE"),
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
                            ImportoRataEnte = Utilities.SafeGetDouble(reader, "IMPORTO_RATA")
                        });
                    }
                }
            }

            // Carica istanze aperte non lavorate in mappa CF -> DTO (robusta contro '' in colonne numeriche)
            var istanzeOpenByCf = new Dictionary<string, IstanzaDomicilioOpenDTO>(StringComparer.OrdinalIgnoreCase);

            using (SqlCommand cmdIst = new SqlCommand(istanzaOpenQuery, CONNECTION))
            {
                cmdIst.CommandTimeout = 900000;
                cmdIst.Parameters.AddWithValue("@aa", selectedAA);

                using (SqlDataReader r = cmdIst.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var cf = Utilities.RemoveAllSpaces(ReadString(r, "Cod_fiscale")).ToUpper();
                        if (string.IsNullOrWhiteSpace(cf))
                            continue;

                        var dto = new IstanzaDomicilioOpenDTO
                        {
                            AnnoAccademico = ReadString(r, "Anno_accademico"),
                            CodFiscale = cf,
                            NumDomanda = ReadInt(r, "Num_domanda"),
                            NumIstanza = ReadInt(r, "Num_istanza"),
                            DataValiditaIstanza = ReadDateTimeNullable(r, "DataValiditaIstanza"),

                            ComuneDomicilio = ReadString(r, "COD_COMUNE"),
                            TitoloOneroso = ReadInt(r, "TITOLO_ONEROSO") == 1,
                            ContrattoEnte = ReadInt(r, "TIPO_CONTRATTO_TITOLO_ONEROSO") == 1,

                            SerieContratto = ReadString(r, "N_SERIE_CONTRATTO"),
                            DataRegistrazioneString = ReadString(r, "DATA_REG_CONTRATTO"),
                            DataDecorrenzaString = ReadString(r, "DATA_DECORRENZA"),
                            DataScadenzaString = ReadString(r, "DATA_SCADENZA"),

                            DurataContratto = ReadInt(r, "DURATA_CONTRATTO"),
                            Prorogato = ReadInt(r, "PROROGA") == 1,
                            DurataProroga = ReadInt(r, "DURATA_PROROGA"),
                            SerieProroga = ReadString(r, "ESTREMI_PROROGA"),

                            DenominazioneEnte = ReadString(r, "DENOM_ENTE"),
                            ImportoRataEnte = ReadDouble(r, "IMPORTO_RATA")
                        };

                        if (!istanzeOpenByCf.ContainsKey(cf))
                            istanzeOpenByCf[cf] = dto;
                    }
                }
            }

            var reportTable = new DataTable("ReportDomicilioContratti");
            reportTable.Columns.Add("CodFiscale", typeof(string));
            reportTable.Columns.Add("ComuneDomicilio", typeof(string));
            reportTable.Columns.Add("TitoloOneroso", typeof(bool));

            reportTable.Columns.Add("ValidatoDaContratto", typeof(bool));
            reportTable.Columns.Add("ValidatoDaEnte", typeof(bool));
            reportTable.Columns.Add("TipoValidazione", typeof(string));

            reportTable.Columns.Add("ContrattoEnte", typeof(bool));
            reportTable.Columns.Add("SerieContratto", typeof(string));
            reportTable.Columns.Add("DataRegistrazione", typeof(DateTime));
            reportTable.Columns.Add("DataDecorrenza", typeof(DateTime));
            reportTable.Columns.Add("DataScadenza", typeof(DateTime));
            reportTable.Columns.Add("DurataContratto", typeof(int));
            reportTable.Columns.Add("Prorogato", typeof(bool));
            reportTable.Columns.Add("DurataProroga", typeof(int));
            reportTable.Columns.Add("SerieProroga", typeof(string));
            reportTable.Columns.Add("ContrattoValido", typeof(bool));
            reportTable.Columns.Add("ProrogaValida", typeof(bool));

            reportTable.Columns.Add("ContrattoEnteValido", typeof(bool));
            reportTable.Columns.Add("DenominazioneEnte", typeof(string));
            reportTable.Columns.Add("ImportoRataEnte", typeof(double));

            reportTable.Columns.Add("MesiCoperti", typeof(int));
            reportTable.Columns.Add("ErroriCodici", typeof(string));
            reportTable.Columns.Add("ErroriContratto", typeof(string));

            // Output aggiuntivo: istanza non lavorata + validazione su ICL
            reportTable.Columns.Add("IstanzaNonLavorata", typeof(bool));
            reportTable.Columns.Add("NumIstanzaNonLavorata", typeof(int));
            reportTable.Columns.Add("DomicilioValidoInIstanza", typeof(bool));
            reportTable.Columns.Add("IstanzaTipoValidazione", typeof(string));
            reportTable.Columns.Add("IstanzaMesiCoperti", typeof(int));
            reportTable.Columns.Add("IstanzaErroriCodici", typeof(string));
            reportTable.Columns.Add("IstanzaErroriContratto", typeof(string));

            SqlTransaction? trans = null;

            try
            {
                if (doDbWritesAndSendMessages)
                    trans = CONNECTION.BeginTransaction();

                foreach (var row in domicilioRows)
                {
                    var errorCodes = new List<string>();
                    void AddError(string code)
                    {
                        if (!errorCodes.Contains(code))
                            errorCodes.Add(code);
                    }

                    var studente = new StudenteInfo();
                    studente.InformazioniPersonali.CodFiscale = row.CodFiscale;

                    if (string.IsNullOrWhiteSpace(studente.InformazioniPersonali.CodFiscale))
                        AddError("001");

                    bool titoloOneroso = row.TitoloOneroso;

                    bool contrattoEnte = row.ContrattoEnte;
                    string denominazioneEnte = row.DenominazioneEnte;
                    int durataContratto = row.DurataContratto;
                    double importoRataEnte = row.ImportoRataEnte;
                    bool contrattoEnteValido = false;

                    // PARSING DATE: errori SOLO se NON ente
                    DateTime dataRegistrazione = default;
                    if (!DateTime.TryParse(row.DataRegistrazioneString, out dataRegistrazione) && !contrattoEnte)
                        AddError("002");

                    DateTime dataDecorrenza = default;
                    if (!DateTime.TryParse(row.DataDecorrenzaString, out dataDecorrenza) && !contrattoEnte)
                        AddError("003");

                    DateTime dataScadenza = default;
                    if (!DateTime.TryParse(row.DataScadenzaString, out dataScadenza) && !contrattoEnte)
                        AddError("004");

                    int monthsCovered = 0;

                    bool validatoDaContratto = false;
                    bool validatoDaEnte = false;

                    // VALIDAZIONE ENTE
                    if (contrattoEnte)
                    {
                        if (string.IsNullOrWhiteSpace(denominazioneEnte))
                        {
                            AddError("007");
                        }
                        else
                        {
                            if (durataContratto < 10)
                                AddError("008");

                            if (importoRataEnte <= 0)
                                AddError("009");

                            if (durataContratto >= 10 && importoRataEnte > 0)
                            {
                                contrattoEnteValido = true;
                                validatoDaEnte = true;
                            }
                        }
                    }

                    // VALIDAZIONE CONTRATTO/PROROGA (DATE)
                    bool hasContrattoDateRange = titoloOneroso &&
                                                 dataDecorrenza != default &&
                                                 dataScadenza != default;

                    bool contrattoValido = false;
                    bool prorogaValida = false;

                    if (hasContrattoDateRange)
                    {
                        DateTime effectiveStart = (dataDecorrenza > dateRangeStart) ? dataDecorrenza : dateRangeStart;
                        DateTime effectiveEnd = (dataScadenza < dateRangeEnd) ? dataScadenza : dateRangeEnd;

                        if (effectiveStart <= effectiveEnd)
                        {
                            monthsCovered = ((effectiveEnd.Year - effectiveStart.Year) * 12)
                                          + (effectiveEnd.Month - effectiveStart.Month + 1);

                            if (monthsCovered >= 10)
                                validatoDaContratto = true;
                            else
                                AddError("005");
                        }
                        else
                        {
                            AddError("006");
                        }
                    }

                    // VALIDAZIONE NUMERI DI SERIE – SOLO se NON ente
                    string serieContratto = row.SerieContratto;
                    string serieProroga = row.SerieProroga;

                    bool hasSerieContratto = !string.IsNullOrWhiteSpace(serieContratto);
                    bool hasSerieProroga = !string.IsNullOrWhiteSpace(serieProroga);

                    if (!contrattoEnte)
                    {
                        if (hasSerieContratto)
                        {
                            contrattoValido = DomicilioUtils.IsValidSerie(serieContratto);
                            if (!contrattoValido)
                                AddError("011");
                        }

                        if (hasSerieProroga)
                        {
                            prorogaValida = DomicilioUtils.IsValidSerie(serieProroga);

                            if (hasSerieContratto &&
                                serieProroga.IndexOf(serieContratto, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                prorogaValida = false;
                                AddError("010");
                            }

                            if (!prorogaValida)
                                AddError("012");
                        }
                    }
                    else
                    {
                        contrattoValido = false;
                        prorogaValida = false;
                    }

                    bool domicilioCheck = validatoDaContratto || validatoDaEnte;
                    studente.SetDomicilioCheck(domicilioCheck);

                    string tipoValidazione =
                        (validatoDaContratto && validatoDaEnte) ? "CONTRATTO+ENTE" :
                        (validatoDaContratto) ? "CONTRATTO" :
                        (validatoDaEnte) ? "ENTE" : "NESSUNA";

                    studente.SetDomicilio(
                        row.ComuneDomicilio,
                        titoloOneroso,
                        serieContratto,
                        dataRegistrazione,
                        dataDecorrenza,
                        dataScadenza,
                        row.DurataContratto,
                        row.Prorogato ?? false,
                        row.DurataProroga,
                        serieProroga,
                        contrattoValido,
                        prorogaValida,
                        contrattoEnte,
                        denominazioneEnte,
                        importoRataEnte
                    );

                    string errorCodesStr = errorCodes.Count > 0 ? string.Join(";", errorCodes) : string.Empty;
                    string errorDescStr = errorCodes.Count > 0
                        ? string.Join(" | ", errorCodes.ConvertAll(c => ErrorDescriptions.TryGetValue(c, out var d) ? d : c))
                        : string.Empty;

                    // ====== VALIDAZIONE SU ISTANZA (ICL) SE PRESENTE ======
                    bool istanzaNonLavorata = istanzeOpenByCf.TryGetValue(row.CodFiscale, out var istanza);
                    int numIstanzaNonLavorata = istanzaNonLavorata ? istanza!.NumIstanza : 0;

                    bool istanzaDomicilioValido = false;
                    string istanzaTipoValidazione = "NESSUNA";
                    int istanzaMonths = 0;
                    string istanzaErrorCodesStr = "";
                    string istanzaErrorDescStr = "";

                    if (istanzaNonLavorata && istanza != null)
                    {
                        var ecI = new List<string>();
                        void AddErrI(string code) { if (!ecI.Contains(code)) ecI.Add(code); }

                        bool iTitoloOneroso = istanza.TitoloOneroso;
                        bool iContrattoEnte = istanza.ContrattoEnte;
                        string iDenomEnte = istanza.DenominazioneEnte;
                        int iDurataContratto = istanza.DurataContratto;
                        double iImporto = istanza.ImportoRataEnte;

                        DateTime iReg = default;
                        if (!DateTime.TryParse(istanza.DataRegistrazioneString, out iReg) && !iContrattoEnte) AddErrI("002");

                        DateTime iDec = default;
                        if (!DateTime.TryParse(istanza.DataDecorrenzaString, out iDec) && !iContrattoEnte) AddErrI("003");

                        DateTime iScad = default;
                        if (!DateTime.TryParse(istanza.DataScadenzaString, out iScad) && !iContrattoEnte) AddErrI("004");

                        bool iValidContratto = false;
                        bool iValidEnte = false;

                        if (iContrattoEnte)
                        {
                            if (string.IsNullOrWhiteSpace(iDenomEnte)) AddErrI("007");
                            else
                            {
                                if (iDurataContratto < 10) AddErrI("008");
                                if (iImporto <= 0) AddErrI("009");

                                if (iDurataContratto >= 10 && iImporto > 0)
                                    iValidEnte = true;
                            }
                        }

                        bool iHasDateRange = iTitoloOneroso && iDec != default && iScad != default;
                        if (iHasDateRange)
                        {
                            DateTime effStart = (iDec > dateRangeStart) ? iDec : dateRangeStart;
                            DateTime effEnd = (iScad < dateRangeEnd) ? iScad : dateRangeEnd;

                            if (effStart <= effEnd)
                            {
                                istanzaMonths = ((effEnd.Year - effStart.Year) * 12) + (effEnd.Month - effStart.Month + 1);
                                if (istanzaMonths >= 10) iValidContratto = true;
                                else AddErrI("005");
                            }
                            else
                            {
                                AddErrI("006");
                            }
                        }

                        string iSerieContr = istanza.SerieContratto;
                        string iSeriePror = istanza.SerieProroga;

                        bool iHasSerieContr = !string.IsNullOrWhiteSpace(iSerieContr);
                        bool iHasSeriePror = !string.IsNullOrWhiteSpace(iSeriePror);

                        bool iContrOk = false;
                        bool iProrOk = false;

                        if (!iContrattoEnte)
                        {
                            if (iHasSerieContr)
                            {
                                iContrOk = DomicilioUtils.IsValidSerie(iSerieContr);
                                if (!iContrOk) AddErrI("011");
                            }

                            if (iHasSeriePror)
                            {
                                iProrOk = DomicilioUtils.IsValidSerie(iSeriePror);

                                if (iHasSerieContr &&
                                    iSeriePror.IndexOf(iSerieContr, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    iProrOk = false;
                                    AddErrI("010");
                                }

                                if (!iProrOk) AddErrI("012");
                            }
                        }

                        istanzaDomicilioValido = iValidContratto || iValidEnte;
                        istanzaTipoValidazione =
                            (iValidContratto && iValidEnte) ? "CONTRATTO+ENTE" :
                            (iValidContratto) ? "CONTRATTO" :
                            (iValidEnte) ? "ENTE" : "NESSUNA";

                        istanzaErrorCodesStr = ecI.Count > 0 ? string.Join(";", ecI) : "";
                        istanzaErrorDescStr = ecI.Count > 0
                            ? string.Join(" | ", ecI.ConvertAll(c => ErrorDescriptions.TryGetValue(c, out var d) ? d : c))
                            : "";
                    }

                    // CALCOLO Data_scadenza_inserimento
                    DateTime? dataScadenzaInserimento = null;
                    if (contrattoEnte)
                    {
                        if (durataContratto > 0)
                            dataScadenzaInserimento = dateRangeStart.AddMonths(durataContratto + 1);
                    }
                    else
                    {
                        if (dataScadenza != default)
                            dataScadenzaInserimento = dataScadenza.AddMonths(1);
                    }

                    // Persist + messaggi solo se richiesto
                    if (errorCodes.Count > 0 && doDbWritesAndSendMessages)
                    {
                        const string key = "DOMICILIO_CONTRATTO";

                        if (studente.MessaggiErrore == null)
                            studente.MessaggiErrore = new Dictionary<string, string>();

                        if (studente.MessaggiErrore.TryGetValue(key, out var existing))
                            studente.MessaggiErrore[key] = existing + " | " + errorDescStr;
                        else
                            studente.MessaggiErrore[key] = errorDescStr;

                        // MASTER: Validazione_contratto_locazione
                        int idErroriContratto;
                        using (var cmdIns = new SqlCommand(@"
                            INSERT INTO Validazione_contratto_locazione
                                (Anno_accademico,
                                 Cod_fiscale,
                                 Num_domanda,
                                 Data_validita,
                                 Data_fine_validita,
                                 Data_scadenza_inserimento,
                                 Notificato,
                                 id_messaggi_studente)
                            OUTPUT INSERTED.id_errori_contratto
                            VALUES
                                (@AnnoAccademico,
                                 @CodFiscale,
                                 @NumDomanda,
                                 CURRENT_TIMESTAMP,
                                 NULL,
                                 @DataScadenzaIns,
                                 0,
                                 NULL);
                        ", CONNECTION, trans))
                        {
                            cmdIns.Parameters.AddWithValue("@AnnoAccademico", row.AnnoAccademico ?? selectedAA);
                            cmdIns.Parameters.AddWithValue("@CodFiscale", row.CodFiscale);
                            cmdIns.Parameters.AddWithValue("@NumDomanda", row.NumDomanda);
                            cmdIns.Parameters.AddWithValue("@DataScadenzaIns",
                                dataScadenzaInserimento.HasValue ? (object)dataScadenzaInserimento.Value : DBNull.Value);

                            idErroriContratto = Convert.ToInt32(cmdIns.ExecuteScalar());
                        }

                        // DETTAGLIO: Validazione_contratto_locazione_errori
                        foreach (var code in errorCodes)
                        {
                            using var cmdDett = new SqlCommand(@"
                                INSERT INTO Validazione_contratto_locazione_errori
                                    (id_errori_contratto, cod_errore)
                                VALUES
                                    (@IdErroriContratto, @CodErrore);
                            ", CONNECTION, trans);

                            cmdDett.Parameters.AddWithValue("@IdErroriContratto", idErroriContratto);
                            cmdDett.Parameters.AddWithValue("@CodErrore", code);
                            cmdDett.ExecuteNonQuery();
                        }

                        var aaForMsg = row.AnnoAccademico ?? selectedAA;
                        string aaFormatted = aaForMsg != null && aaForMsg.Length == 8
                            ? $"{aaForMsg.Substring(0, 4)}/{aaForMsg.Substring(4, 4)}"
                            : aaForMsg ?? string.Empty;

                        string errorLines = string.Join("<br>", errorCodes.ConvertAll(code =>
                        {
                            if (ErrorDescriptions.TryGetValue(code, out var desc))
                                return $"- {desc}";
                            return $"- {code}";
                        }));

                        string messaggioStudente =
                            $"Sono state rilevate anomalie sul contratto di locazione per l'anno accademico {aaFormatted} - bando borse di studio:<br>{errorLines}";

                        if (monthsCovered >= 10)
                        {
                            messaggioStudente += "<br>Se non l'hai già fatto, apri un'istanza di modifica domicilio dalla tua area riservata per correggere gli errori individuati <b>entro e non oltre il 10 febbraio</b>, pena il declassamento dello status sede a PENDOLARE CALCOLATO.<br>" +
                                "Nel caso in cui l'istanza sia già stata creata, attendi l'esito della lavorazione da parte degli uffici.";
                        }
                        else
                        {
                            messaggioStudente += "<br>Se non l'hai già fatto, apri un'istanza di modifica domicilio dalla tua area riservata per correggere gli errori individuati <b>entro e non oltre i 30 giorni dalla data di scadenza</b> del tuo contratto (comprese eventuali proroghe), pena il declassamento dello status sede a PENDOLARE CALCOLATO.<br>" +
                                "Nel caso in cui l'istanza sia già stata creata, attendi l'esito della lavorazione da parte degli uffici.";
                        }

                        int idMessaggiStudente;
                        using (var cmdMsg = new SqlCommand(@"
                            INSERT INTO MESSAGGI_STUDENTE
                                (COD_FISCALE,
                                 DATA_INSERIMENTO_MESSAGGIO,
                                 MESSAGGIO,
                                 LETTO,
                                 DATA_LETTURA,
                                 UTENTE)
                            OUTPUT INSERTED.Id_MESSAGGI_STUDENTE
                            VALUES
                                (@CodFiscale,
                                 CURRENT_TIMESTAMP,
                                 @Messaggio,
                                 0,
                                 NULL,
                                 'TEST');
                        ", CONNECTION, trans))
                        {
                            cmdMsg.Parameters.AddWithValue("@CodFiscale", row.CodFiscale);
                            cmdMsg.Parameters.AddWithValue("@Messaggio", messaggioStudente);
                            idMessaggiStudente = Convert.ToInt32(cmdMsg.ExecuteScalar());
                        }

                        // UPDATE Validazione_contratto_locazione: segna notificato e collega il messaggio
                        using (var cmdUpd = new SqlCommand(@"
                            UPDATE Validazione_contratto_locazione
                            SET Notificato = 1,
                                id_messaggi_studente = @IdMessaggiStudente
                            WHERE id_errori_contratto = @IdErroriContratto;
                        ", CONNECTION, trans))
                        {
                            cmdUpd.Parameters.AddWithValue("@IdMessaggiStudente", idMessaggiStudente);
                            cmdUpd.Parameters.AddWithValue("@IdErroriContratto", idErroriContratto);
                            cmdUpd.ExecuteNonQuery();
                        }
                    }

                    reportTable.Rows.Add(
                        row.CodFiscale,
                        row.ComuneDomicilio,
                        titoloOneroso,
                        validatoDaContratto,
                        validatoDaEnte,
                        tipoValidazione,
                        contrattoEnte,
                        serieContratto ?? string.Empty,
                        dataRegistrazione == default ? (object)DBNull.Value : dataRegistrazione,
                        dataDecorrenza == default ? (object)DBNull.Value : dataDecorrenza,
                        dataScadenza == default ? (object)DBNull.Value : dataScadenza,
                        durataContratto,
                        row.Prorogato ?? false,
                        row.DurataProroga,
                        serieProroga ?? string.Empty,
                        contrattoValido,
                        prorogaValida,
                        contrattoEnteValido,
                        denominazioneEnte ?? string.Empty,
                        importoRataEnte,
                        monthsCovered,
                        errorCodesStr,
                        errorDescStr,
                        istanzaNonLavorata,
                        numIstanzaNonLavorata,
                        istanzaDomicilioValido,
                        istanzaTipoValidazione,
                        istanzaMonths,
                        istanzaErrorCodesStr,
                        istanzaErrorDescStr
                    );
                }

                if (doDbWritesAndSendMessages)
                    trans?.Commit();

                Utilities.ExportDataTableToExcel(reportTable, folderPath);

                Logger.LogInfo(35, "UPDATE:Lavorazione studenti - inserimento in domicilio - completato" +
                                  (doDbWritesAndSendMessages ? " (DB+Messaggi: SI)" : " (Solo file: SI)"));
            }
            catch (Exception ex)
            {
                if (doDbWritesAndSendMessages)
                    trans?.Rollback();

                Logger.LogError(35, "Errore in ControlloDomicilio" + ex);
                throw;
            }
        }
    }
}
