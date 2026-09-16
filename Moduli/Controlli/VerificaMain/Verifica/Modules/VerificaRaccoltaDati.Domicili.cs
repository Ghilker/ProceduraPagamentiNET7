using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class VerificaRaccoltaDati
    {
        private const string DomiciliNuovaGestionePopulationSql = @"
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

;WITH D AS
(
    SELECT
        CAST(t.NumDomanda AS INT) AS NumDomanda,
        UPPER(REPLACE(LTRIM(RTRIM(t.CodFiscale)), ' ', '')) AS CodFiscale
    FROM {TEMP_TABLE} t
)
SELECT
    D.NumDomanda,
    D.CodFiscale,

    dom.ID AS DomicilioId,
    ISNULL(dom.COD_COMUNE, '') AS DomicilioCodComune,
    ISNULL(dom.INDIRIZZO, '') AS DomicilioIndirizzo,
    ISNULL(CONVERT(NVARCHAR(64), dom.NUMERO_CIVICO), '') AS DomicilioNumeroCivico,
    ISNULL(CONVERT(NVARCHAR(16), dom.CAP), '') AS DomicilioCap,
    ISNULL(CAST(dom.TIPO_DOMICILIO AS INT), -1) AS DomicilioTipo,
    dom.DATA_INIZIO AS DomicilioDataInizio,
    dom.DATA_FINE AS DomicilioDataFine,
    CAST(ISNULL(dom.INSERIMENTO_DATI_CONTRATTO, 0) AS BIT) AS DomicilioInserimentoDatiContratto,

    con.ID AS ContrattoId,
    ISNULL(CAST(con.TIPO_CONTRATTO_TITOLO_ONEROSO AS INT), -1) AS ContrattoTipo,
    ISNULL(CONVERT(NVARCHAR(128), con.N_SERIE_CONTRATTO), '') AS ContrattoNumeroSerie,
    con.DATA_REG_CONTRATTO AS ContrattoDataRegistrazione,
    ISNULL(con.DENOM_ENTE, '') AS ContrattoDenominazioneEnte,
    con.IMPORTO_RATA AS ContrattoImportoRata,
    ISNULL(con.TIPO_ENTE, '') AS ContrattoTipoEnte,
    ISNULL(CONVERT(NVARCHAR(128), con.N_SERIE_SUBENTRO), '') AS ContrattoNumeroSerieSubentro,
    con.DATA_INIZIO_SUBENTRO AS ContrattoDataInizioSubentro,
    con.DATA_CESSAZIONE AS ContrattoDataCessazione,
    con.DATA_INIZIO AS ContrattoDataInizio,
    con.DATA_FINE AS ContrattoDataFine,

    pro.ID AS ProrogaId,
    ISNULL(CONVERT(NVARCHAR(128), pro.N_SERIE_PROROGA), '') AS ProrogaNumeroSerie,
    pro.DATA_DECORRENZA AS ProrogaDataDecorrenza,
    pro.DATA_SCADENZA AS ProrogaDataScadenza
FROM D
JOIN Domicili dom
  ON dom.NUM_DOMANDA = D.NumDomanda
 AND dom.ANNO_ACCADEMICO = @AA
 AND UPPER(REPLACE(LTRIM(RTRIM(dom.COD_FISCALE)), ' ', '')) = D.CodFiscale
 AND dom.DATA_FINE_VALIDITA IS NULL
 AND dom.IS_EQUAL_RESIDENZA = 0
LEFT JOIN Contratti con
  ON con.ID_DOMICILIO = dom.ID
 AND con.DATA_FINE_VALIDITA IS NULL
LEFT JOIN Proroghe pro
  ON pro.ID_CONTRATTO = con.ID
 AND pro.DATA_FINE_VALIDITA IS NULL
ORDER BY
    D.NumDomanda,
    dom.ID,
    con.ID,
    pro.DATA_DECORRENZA,
    pro.ID;";

        private void LoadDomiciliNuovaGestione(Verifica.VerificaPipelineContext context)
        {
            using var scope = MeasureCollectionStep(
                "VerificaRaccoltaDati.LoadDomiciliNuovaGestione",
                $"AA={context.AnnoAccademico}");

            foreach (var info in context.Students.Values)
            {
                info.InformazioniSede.UsaNuovaGestioneDomicili = true;
                info.InformazioniSede.DomiciliNuovaGestione.Clear();
                info.InformazioniSede.OutcomeDomicili = null;
                info.InformazioniSede.HasIstanzaDomicilio = false;
                info.InformazioniSede.IstanzaDomicilio = null;
                info.InformazioniSede.HasUltimaIstanzaChiusaDomicilio = false;
            }

            var domiciliByKey =
                new Dictionary<(StudentKey Studente, long DomicilioId), DomicilioAnalisiInput>();
            var contrattiByKey =
                new Dictionary<(StudentKey Studente, long ContrattoId), ContrattoDomicilioAnalisiInput>();
            var prorogheCaricate =
                new HashSet<(StudentKey Studente, long ContrattoId, long ProrogaId)>();

            using var cmd = CreatePopulationCommand(DomiciliNuovaGestionePopulationSql, context);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                var studentKey = CreateStudentKey(
                    reader.SafeGetString("CodFiscale"),
                    reader.SafeGetString("NumDomanda"));

                if (!TryGetStudentInfo(studentKey, out var info))
                    continue;

                long domicilioId = GetRequiredInt64(reader, "DomicilioId");
                var domicilioKey = (studentKey, domicilioId);

                if (!domiciliByKey.TryGetValue(domicilioKey, out var domicilio))
                {
                    domicilio = new DomicilioAnalisiInput
                    {
                        Id = domicilioId,
                        CodComune = reader.SafeGetString("DomicilioCodComune").Trim(),
                        Indirizzo = reader.SafeGetString("DomicilioIndirizzo").Trim(),
                        NumeroCivico = reader.SafeGetString("DomicilioNumeroCivico").Trim(),
                        Cap = reader.SafeGetString("DomicilioCap").Trim(),
                        TipoDomicilio =
                            (TipoDomicilioAnalisi)reader.SafeGetInt("DomicilioTipo"),
                        DataInizio = GetNullableDateTime(reader, "DomicilioDataInizio"),
                        DataFine = GetNullableDateTime(reader, "DomicilioDataFine"),
                        InserimentoDatiContratto =
                            reader.SafeGetBool("DomicilioInserimentoDatiContratto")
                    };

                    domiciliByKey.Add(domicilioKey, domicilio);
                    info.InformazioniSede.DomiciliNuovaGestione.Add(domicilio);
                }

                long? contrattoId = GetNullableInt64(reader, "ContrattoId");
                if (!contrattoId.HasValue)
                    continue;

                var contrattoKey = (studentKey, contrattoId.Value);
                if (!contrattiByKey.TryGetValue(contrattoKey, out var contratto))
                {
                    contratto = new ContrattoDomicilioAnalisiInput
                    {
                        Id = contrattoId.Value,
                        TipoContratto =
                            (TipoContrattoDomicilioAnalisi)reader.SafeGetInt("ContrattoTipo"),
                        NumeroSerieContratto =
                            reader.SafeGetString("ContrattoNumeroSerie").Trim(),
                        DataRegistrazioneContratto =
                            GetNullableDateTime(reader, "ContrattoDataRegistrazione"),
                        DenominazioneEnte =
                            reader.SafeGetString("ContrattoDenominazioneEnte").Trim(),
                        ImportoRata =
                            GetNullableDecimalValue(reader, "ContrattoImportoRata"),
                        TipoEnte =
                            reader.SafeGetString("ContrattoTipoEnte").Trim().ToUpperInvariant(),
                        NumeroSerieSubentro =
                            reader.SafeGetString("ContrattoNumeroSerieSubentro").Trim(),
                        DataInizioSubentro =
                            GetNullableDateTime(reader, "ContrattoDataInizioSubentro"),
                        DataCessazione =
                            GetNullableDateTime(reader, "ContrattoDataCessazione"),
                        DataInizio =
                            GetNullableDateTime(reader, "ContrattoDataInizio"),
                        DataFine =
                            GetNullableDateTime(reader, "ContrattoDataFine")
                    };

                    contrattiByKey.Add(contrattoKey, contratto);
                    domicilio.Contratti.Add(contratto);
                }

                long? prorogaId = GetNullableInt64(reader, "ProrogaId");
                if (!prorogaId.HasValue)
                    continue;

                var prorogaKey = (studentKey, contrattoId.Value, prorogaId.Value);
                if (!prorogheCaricate.Add(prorogaKey))
                    continue;

                contratto.Proroghe.Add(
                    new ProrogaDomicilioAnalisiInput
                    {
                        Id = prorogaId.Value,
                        NumeroSerieProroga =
                            reader.SafeGetString("ProrogaNumeroSerie").Trim(),
                        DataDecorrenza =
                            GetNullableDateTime(reader, "ProrogaDataDecorrenza"),
                        DataScadenza =
                            GetNullableDateTime(reader, "ProrogaDataScadenza")
                    });
            }
        }

        private static long GetRequiredInt64(SqlDataReader reader, string columnName)
        {
            long? value = GetNullableInt64(reader, columnName);
            if (!value.HasValue)
                throw new DataException($"Colonna obbligatoria {columnName} non valorizzata.");

            return value.Value;
        }

        private static long? GetNullableInt64(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToInt64(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static DateTime? GetNullableDateTime(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDateTime(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }

        private static decimal? GetNullableDecimalValue(SqlDataReader reader, string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal)
                ? null
                : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
        }
    }
}
