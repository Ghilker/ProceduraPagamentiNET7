using System.Data.SqlClient;

namespace ProcedureNet7
{
    public partial class ProceduraPagamenti
    {
        private bool PopulateDomiciliNuovaGestione()
        {
            if (!AnalizzatoreDomicili.UsaNuovaGestione(selectedAA)) return false;

            var students = studentiDaPagare.Values.ToDictionary(StudentKey.From, s => (StudenteInfo)s);
            const string studentiSql = @"(SELECT d.Num_domanda AS NumDomanda, d.Cod_fiscale AS CodFiscale
                FROM Domanda d INNER JOIN #CFEstrazione cf ON cf.Cod_fiscale = d.Cod_fiscale
                WHERE d.Anno_accademico = @AA)";
            const string accessoriSql = @"
SELECT d.NumDomanda, d.CodFiscale, i.COD_SEDE_STUDI AS CodSedeStudi,
    ISNULL(cl.COD_SEDE_DISTACCATA, '00000') AS CodSedeDistaccata,
    sede.ComuneSedeStudi, c.COD_PROVINCIA AS ProvinciaSede,
    CAST(CASE WHEN ISNULL(ss.TELEMATICA, 0) = 1 OR ISNULL(cl.CORSO_IN_PRESENZA, 1) = 0 THEN 1 ELSE 0 END AS BIT) AS AlwaysA,
    pa.Cod_tipo_esito AS EsitoPA, pk.Cod_tipo_esito AS EsitoPK,
    CAST(CASE WHEN EXISTS (SELECT 1 FROM vBenefici_richiesti b
        WHERE b.Anno_accademico = @AA AND b.Num_domanda = d.NumDomanda
        AND ((b.Cod_beneficio = 'PA' AND pa.Cod_tipo_esito IN (1, 2))
          OR (b.Cod_beneficio = 'PK' AND pk.Cod_tipo_esito IN (1, 2)))) THEN 1 ELSE 0 END AS BIT) AS HasAlloggio12
FROM {TEMP_TABLE} d
OUTER APPLY (SELECT TOP (1) * FROM ISCRIZIONI i
    WHERE i.COD_FISCALE = d.CodFiscale AND i.ANNO_ACCADEMICO = @AA
      AND (i.TIPO_BANDO IS NULL OR i.TIPO_BANDO LIKE 'L%') ORDER BY i.DATA_VALIDITA DESC) i
OUTER APPLY (SELECT TOP (1) TRY_CONVERT(INT, m.ANNO_IMMATRICOLAZ) AS AnnoImmatricolazione
    FROM MERITO m WHERE m.ANNO_ACCADEMICO = @AA AND m.NUM_DOMANDA = d.NumDomanda
    ORDER BY m.DATA_VALIDITA DESC) m
LEFT JOIN CORSI_LAUREA cl
    ON cl.COD_CORSO_LAUREA = i.COD_CORSO_LAUREA AND cl.COD_TIPO_ORDINAMENTO = i.COD_TIPO_ORDINAMENTO
    AND cl.COD_FACOLTA = i.COD_FACOLTA AND cl.ANNO_ACCAD_INIZIO = i.ANNO_ACCAD_INIZIO
    AND cl.COD_SEDE_STUDI = i.COD_SEDE_STUDI AND cl.COD_TIPOLOGIA_STUDI = i.COD_TIPOLOGIA_STUDI
    AND (cl.ANNO_ACCAD_FINE >= CONVERT(NVARCHAR(20), ISNULL(m.AnnoImmatricolazione, 0)) OR cl.ANNO_ACCAD_FINE IS NULL)
LEFT JOIN SEDE_STUDI ss ON ss.COD_SEDE_STUDI = i.COD_SEDE_STUDI
LEFT JOIN SEDI_DISTACCATE sd ON sd.COD_SEDE_DISTACCATA = cl.COD_SEDE_DISTACCATA
CROSS APPLY (SELECT CASE WHEN NULLIF(ISNULL(cl.COD_SEDE_DISTACCATA, ''), '') IS NULL OR cl.COD_SEDE_DISTACCATA = '00000'
    THEN CASE WHEN ISNULL(cl.COMUNE_SEDE_STUDI, '') IN ('00000', '0000') THEN 'H501' ELSE ISNULL(cl.COMUNE_SEDE_STUDI, '') END
    ELSE ISNULL(sd.COD_COMUNE, '') END AS ComuneSedeStudi) sede
LEFT JOIN Comuni c ON c.COD_COMUNE = sede.ComuneSedeStudi
OUTER APPLY (SELECT TOP (1) e.Cod_tipo_esito FROM ESITI_CONCORSI e
    WHERE e.Anno_accademico = @AA AND e.Num_domanda = d.NumDomanda AND e.Cod_beneficio = 'PA'
      AND e.Data_validita <= @DataRiferimento
    ORDER BY e.Data_validita DESC) pa
OUTER APPLY (SELECT TOP (1) e.Cod_tipo_esito FROM ESITI_CONCORSI e
    WHERE e.Anno_accademico = @AA AND e.Num_domanda = d.NumDomanda AND e.Cod_beneficio = 'PK'
      AND e.Data_validita <= @DataRiferimento
    ORDER BY e.Data_validita DESC) pk;";

            using (var command = new SqlCommand(accessoriSql.Replace("{TEMP_TABLE}", studentiSql), CONNECTION, sqlTransaction))
            {
                command.CommandTimeout = 9000000;
                command.Parameters.AddWithValue("@AA", selectedAA);
                command.Parameters.Add("@DataRiferimento", System.Data.SqlDbType.DateTime).Value = DataRiferimentoStatusSede;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (!students.TryGetValue(new StudentKey(reader.SafeGetString("CodFiscale"),
                        reader.SafeGetString("NumDomanda")), out var info)) continue;
                    info.InformazioniIscrizione.CodSedeStudi = reader.SafeGetString("CodSedeStudi");
                    info.InformazioniIscrizione.CodSedeDistaccata = reader.SafeGetString("CodSedeDistaccata");
                    info.InformazioniIscrizione.ComuneSedeStudi = reader.SafeGetString("ComuneSedeStudi");
                    info.InformazioniIscrizione.ProvinciaSedeStudi = reader.SafeGetString("ProvinciaSede");
                    info.InformazioniSede.AlwaysA = reader.SafeGetBool("AlwaysA");
                    info.InformazioniSede.HasAlloggio12 = reader.SafeGetBool("HasAlloggio12");
                    foreach (string benefit in new[] { "PA", "PK" })
                        info.EsitiConcorsoByBenefit[benefit] = new EsitoConcorsoBenefitRaw
                        {
                            CodBeneficio = benefit,
                            CodTipoEsito = reader.IsDBNull(reader.GetOrdinal("Esito" + benefit))
                                ? null : reader.SafeGetInt("Esito" + benefit)
                        };
                }
            }

            VerificaRaccoltaDati.LoadStatusSedeClassificationFlags(CONNECTION, sqlTransaction, students);
            using var domiciliCommand = new SqlCommand(VerificaRaccoltaDati.DomiciliNuovaGestionePopulationSql
                .Replace("{TEMP_TABLE}", studentiSql), CONNECTION, sqlTransaction) { CommandTimeout = 9000000 };
            domiciliCommand.Parameters.AddWithValue("@AA", selectedAA);
            VerificaRaccoltaDati.LoadDomiciliNuovaGestione(domiciliCommand, students);
            return true;
        }
    }
}
