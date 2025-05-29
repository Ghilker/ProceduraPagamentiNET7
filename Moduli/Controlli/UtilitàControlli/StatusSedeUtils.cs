using IbanNet;
using OpenCvSharp.Features2D;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Security.Policy;

namespace ProcedureNet7
{
    public static class StatusSedeUtils
    {
        private class StudentBenefitFlags
        {
            public bool RichiestaPA;
            public bool RinunciaPA;
            public bool IdoneoPA_attesa_2assegn;
            public bool VincitorePA_NoAssegnazione;
        }
        public static Dictionary<string, bool> CalcolaResidenzaEstera(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<StudenteInfo> students,
            SqlTransaction transaction = null)
        {
            var inputPairs = students.ToDictionary(
                s => s.InformazioniPersonali.CodFiscale,
                s => int.Parse(s.InformazioniPersonali.NumDomanda, CultureInfo.InvariantCulture)
            );
            return CalcolaResidenzaEstera(conn, annoAccademico, inputPairs, transaction);
        }

        public static Dictionary<int, bool> CalcolaNumConvEstero(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<StudenteInfo> students,
            SqlTransaction transaction = null)
        {
            var domande = students
                .Select(s => int.Parse(s.InformazioniPersonali.NumDomanda, CultureInfo.InvariantCulture));
            return CalcolaNumConvEstero(conn, annoAccademico, domande, transaction);
        }

        public static Dictionary<string, string> ForzatureStatusSede(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<StudenteInfo> students,
            SqlTransaction transaction = null)
        {
            var fiscals = students.Select(s => s.InformazioniPersonali.CodFiscale);
            return ForzatureStatusSede(conn, annoAccademico, fiscals, transaction);
        }

        /// <summary>
        /// Bulk‐load any “forzature” overrides (status_sede) for the given fiscal codes.
        /// </summary>
        public static Dictionary<string, string> ForzatureStatusSede(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<string> fiscals,
            SqlTransaction tx)
        {
            if (conn is null) throw new ArgumentNullException(nameof(conn));
            if (tx is null) throw new ArgumentNullException(nameof(tx));
            if (string.IsNullOrWhiteSpace(annoAccademico))
                throw new ArgumentException("annoAccademico is required", nameof(annoAccademico));
            if (fiscals is null) throw new ArgumentNullException(nameof(fiscals));

            // initialize every CF to "0"
            var result = fiscals.Distinct()
                               .ToDictionary(cf => cf, cf => "0",
                                             StringComparer.OrdinalIgnoreCase);

            // build TVP
            var tvp = new DataTable();
            tvp.Columns.Add("Cod_fiscale", typeof(string));
            foreach (var cf in result.Keys)
                tvp.Rows.Add(cf);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT f.Cod_fiscale, f.status_sede
  FROM Forzature_StatusSede AS f
  JOIN @Input AS i
    ON f.Cod_fiscale = i.Cod_fiscale
 WHERE f.Anno_Accademico       = @annoAccademico
   AND f.Data_fine_validita IS NULL;
";
            var p = cmd.Parameters.Add("@Input", SqlDbType.Structured);
            p.TypeName = "dbo.CFEstrazione";
            p.Value = tvp;

            cmd.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var cf = rdr.GetString(0).Trim();
                var st = rdr.GetString(1).Trim();
                result[cf] = st;
            }

            return result;
        }

        /// <summary>
        /// Bulk‐calculate “residenza estera” (true = family in Italy) for multiple students.
        /// Input is a map: CF → NumDomanda.
        /// </summary>
        public static Dictionary<string, bool> CalcolaResidenzaEstera(
            SqlConnection conn,
            string annoAccademico,
            Dictionary<string, int> inputPairs,
            SqlTransaction tx)
        {
            if (conn is null) throw new ArgumentNullException(nameof(conn));
            if (tx is null) throw new ArgumentNullException(nameof(tx));
            if (string.IsNullOrWhiteSpace(annoAccademico))
                throw new ArgumentException("Required", nameof(annoAccademico));
            if (inputPairs is null) throw new ArgumentNullException(nameof(inputPairs));

            // init all CF→false
            var result = inputPairs.Keys
                .ToDictionary(k => k, k => false, StringComparer.OrdinalIgnoreCase);

            // build TVP
            var tvp = new DataTable();
            tvp.Columns.Add("CodFiscale", typeof(string));
            tvp.Columns.Add("NumDomanda", typeof(int));
            foreach (var kv in inputPairs)
                tvp.Rows.Add(kv.Key, kv.Value);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT 
    i.CodFiscale,
    CASE 
      WHEN v.cod_TIPOLOGIA_NUCLEO = 'I' THEN integr.Val
      ELSE orig.Val
    END AS TipoReddito
FROM @InputPairs AS i
LEFT JOIN VNUCLEO_FAMILIARE AS v
  ON v.ANNO_ACCADEMICO = @annoAccademico
 AND v.NUM_DOMANDA     = i.NumDomanda
OUTER APPLY (
    SELECT TOP(1) TIPO_REDD_NUCLEO_FAM_INTEGR AS Val
      FROM TIPOLOGIE_REDDITI
     WHERE ANNO_ACCADEMICO = @annoAccademico
       AND NUM_DOMANDA     = i.NumDomanda
       AND TIPO_REDD_NUCLEO_FAM_INTEGR IS NOT NULL
     ORDER BY DATA_VALIDITA DESC
) integr
OUTER APPLY (
    SELECT TOP(1) TIPO_REDD_NUCLEO_FAM_ORIGINE AS Val
      FROM TIPOLOGIE_REDDITI
     WHERE ANNO_ACCADEMICO = @annoAccademico
       AND NUM_DOMANDA     = i.NumDomanda
       AND TIPO_REDD_NUCLEO_FAM_ORIGINE IS NOT NULL
     ORDER BY DATA_VALIDITA DESC
) orig;
";
            var p = cmd.Parameters.Add("@InputPairs", SqlDbType.Structured);
            p.TypeName = "dbo.InputPairType";
            p.Value = tvp;

            cmd.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var cf = rdr.GetString(0).Trim();
                var tipo = rdr.GetString(1).Trim();
                // true if NOT "EE"
                result[cf] = !string.Equals(tipo, "EE", StringComparison.OrdinalIgnoreCase);
            }

            return result;
        }

        /// <summary>
        /// Bulk‐calculate “conviventi estero ≥50%” for multiple domanda numbers.
        /// </summary>
        public static Dictionary<int, bool> CalcolaNumConvEstero(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<int> numeroDomande,
            SqlTransaction tx)
        {
            if (conn is null) throw new ArgumentNullException(nameof(conn));
            if (tx is null) throw new ArgumentNullException(nameof(tx));
            if (string.IsNullOrWhiteSpace(annoAccademico))
                throw new ArgumentException("Required", nameof(annoAccademico));
            if (numeroDomande is null) throw new ArgumentNullException(nameof(numeroDomande));

            var domList = numeroDomande.ToList();
            var result = domList.ToDictionary(n => n, n => false);

            // build TVP
            var tvp = new DataTable();
            tvp.Columns.Add("NumDomanda", typeof(int));
            foreach (var n in domList)
                tvp.Rows.Add(n);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT
    d.NumDomanda,
    CAST(
      CASE 
        WHEN v.NUMERO_CONVIVENTI_ESTERO >= 0.5 * v.NUM_COMPONENTI THEN 1
        ELSE 0
      END
    AS BIT) AS IsConvEstero
FROM @InputDomande AS d
LEFT JOIN VNUCLEO_FAMILIARE AS v
  ON v.ANNO_ACCADEMICO = @annoAccademico
 AND v.NUM_DOMANDA     = d.NumDomanda;
";
            var p = cmd.Parameters.Add("@InputDomande", SqlDbType.Structured);
            p.TypeName = "dbo.NumeroDomandaType";
            p.Value = tvp;

            cmd.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });

            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                var num = rdr.GetInt32(0);
                var isConv = !rdr.IsDBNull(1) && rdr.GetBoolean(1);
                result[num] = isConv;
            }

            return result;
        }


        public static Dictionary<string, string> CalcolaSedeStudiBulk(
            SqlConnection conn,
            string annoAccademico,
            List<StudenteInfo> students,
            SqlTransaction tx)
        {
            if (conn is null) throw new ArgumentNullException(nameof(conn));
            if (tx is null) throw new ArgumentNullException(nameof(tx));
            if (string.IsNullOrWhiteSpace(annoAccademico))
                throw new ArgumentException("annoAccademico is required", nameof(annoAccademico));
            if (students is null) throw new ArgumentNullException(nameof(students));

            // 1) Parse domanda numbers once and init result = "0"
            var domandaMap = students.ToDictionary(
                s => s.InformazioniPersonali.CodFiscale,
                s => int.Parse(s.InformazioniPersonali.NumDomanda.Trim(), CultureInfo.InvariantCulture)
            );
            var result = domandaMap.Keys.ToDictionary(cf => cf, cf => "0");

            var domande = domandaMap.Values;
            var benefitFlags = LoadStudentBenefits(conn, annoAccademico, domande, tx);


            // 2) Collect all distinct comuni + sedi (incl. "A","B")
            var allComuni = students
                .SelectMany(s => new[]
                {
            s.InformazioniSede.Residenza.codComune,
            s.InformazioniIscrizione.ComuneSedeStudi,
            s.InformazioniSede.Domicilio.codComuneDomicilio
                })
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allSedi = students
                .Select(s => s.InformazioniIscrizione.ComuneSedeStudi)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim().ToUpperInvariant())
                .Distinct()
                .Union(new[] { "A", "B" })
                .ToList();

            // 3) TVP builder helper
            DataTable BuildTvp(IEnumerable<string> keys)
            {
                var tvp = new DataTable();
                tvp.Columns.Add("Cod_fiscale", typeof(string));    // ← must match your CFEstrazione definition
                foreach (var k in keys)
                    tvp.Rows.Add(k);
                return tvp;
            }

            // 4) Bulk‐load comuni→province
            var comuniProvince = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var cmdProv = conn.CreateCommand())
            {
                cmdProv.Transaction = tx;
                cmdProv.CommandText = @"
                SELECT 
                    c.COD_COMUNE, 
                    c.COD_PROVINCIA
                FROM COMUNI c
                JOIN @Input AS l 
                  ON l.Cod_fiscale = c.COD_COMUNE;";
                var p = cmdProv.Parameters.Add("@Input", SqlDbType.Structured);
                p.TypeName = "dbo.CFEstrazione";
                p.Value = BuildTvp(allComuni);

                using var rdr = cmdProv.ExecuteReader();
                while (rdr.Read())
                    comuniProvince[rdr.GetString(0).Trim()] = rdr.GetString(1).Trim();
            }

            // 5) Bulk‐load FUORISEDE, PENDOLARI, INSede
            var fuoriSedeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var pendolariMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var inSedeMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var tableMaps = new[]
            {
        (Name: "COMUNI_FUORISEDE", Map: fuoriSedeMap),
        (Name: "COMUNI_PENDOLARI", Map: pendolariMap),
        (Name: "COMUNI_INSede",    Map: inSedeMap)
    };

            var sediTvp = BuildTvp(allSedi);
            foreach (var (Name, Map) in tableMaps)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $@"
                SELECT 
                    cod_sede_studi, 
                    cod_comune
                FROM {Name}
                JOIN @Input AS l
                  ON l.Cod_fiscale = cod_sede_studi;";
                var p = cmd.Parameters.Add("@Input", SqlDbType.Structured);
                p.TypeName = "dbo.CFEstrazione";
                p.Value = sediTvp;

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var sede = rdr.GetString(0).Trim().ToUpperInvariant();
                    var com = rdr.GetString(1).Trim();
                    if (!Map.TryGetValue(sede, out var set))
                    {
                        set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        Map[sede] = set;
                    }
                    set.Add(com);
                }
            }

            // 6) Load overrides, in‐Italy and abroad-half flags
            var overrides = ForzatureStatusSede(conn, annoAccademico, domandaMap.Keys, tx);
            var inItaly = CalcolaResidenzaEstera(conn, annoAccademico, domandaMap, tx);
            var abroadHalf = CalcolaNumConvEstero(conn, annoAccademico, domandaMap.Values, tx);
            var paSet = LoadProlungamentiPA(conn, annoAccademico, domandaMap.Keys, tx);
            var paDeadlines = LoadTerminiPA(conn, annoAccademico, tx);
            // 7) “Magic” sets
            var distaccateSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "TM","TGM","TUN","UTU","UTM","TSR","TUM","TU","TNC" };
            var facISet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IID", "IIF", "IAJ" };
            var facPSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ZAM" };
            var facXSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "04301" };
            var bSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "29386", "29400" };
            var eSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "129618", "129618_1", "12961_1" };
            var jSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DPTEA_57", "ECOLUISS_52" };
            const string cOverrideCode = "04UUTK42";

            // 8) In-memory pass
            foreach (var s in students)
            {
                var cf = s.InformazioniPersonali.CodFiscale;

                if(cf == "BCCNLS83E63B114T")
                {
                    string test = "";
                }

                var domanda = domandaMap[cf];
                var flags = benefitFlags[domanda];
                var codSede = s.InformazioniIscrizione.CodSedeStudi.Trim();
                var facolta = s.InformazioniIscrizione.CodFacolta.Trim();
                var codCorso = s.InformazioniIscrizione.CodCorsoLaurea.Trim().ToUpperInvariant();
                var comuneSede = s.InformazioniIscrizione.ComuneSedeStudi.Trim();
                var comuneRes = s.InformazioniSede.Residenza.codComune?.Trim();
                var dom = s.InformazioniSede.Domicilio;

                // 1) Override
                if (overrides.TryGetValue(cf, out var ov) && ov != "0")
                {
                    result[cf] = ov;
                    continue;
                }
                if (s.InformazioniBeneficio.EsitoPA == 2)
                {
                    result[cf] = "B";
                    continue;
                }
                // 2) Distaccate & faculty-based in sede
                if (distaccateSet.Contains(codSede)
                    || (facolta.Equals("I", StringComparison.OrdinalIgnoreCase) && facISet.Contains(codCorso))
                    || (facolta.Equals("P", StringComparison.OrdinalIgnoreCase) && facPSet.Contains(codCorso))
                    || (facolta.Equals("X", StringComparison.OrdinalIgnoreCase) && facXSet.Contains(codSede))
                    || (codSede == "B" && bSet.Contains(comuneSede))
                    || (codSede == "E" && eSet.Contains(comuneSede))
                    || (codSede == "J" && jSet.Contains(comuneSede)))
                {
                    result[cf] = "A";
                    continue;
                }

                // 3) Political refugee
                if (s.InformazioniPersonali.Rifugiato)
                {
                    result[cf] = "B";
                    continue;
                }

                // 4) ≥50% family abroad
                if (abroadHalf.TryGetValue(domanda, out var isAbroad) && isAbroad)
                {
                    result[cf] = "B";
                    continue;
                }

                // 5) One-off C override
                if (codSede == "C" && codCorso == cOverrideCode)
                {
                    result[cf] = "A";
                    continue;
                }

                // 6) Ente 02 (Cassino)
                if (s.InformazioniIscrizione.CodEnte == "02")
                {
                    if (string.Equals(comuneRes, comuneSede, StringComparison.OrdinalIgnoreCase))
                    {
                        result[cf] = "A";
                        continue;
                    }

                    bool isPend = CheckCassinoPendolare(comuneRes, comuneSede, codSede,comuniProvince, pendolariMap, fuoriSedeMap);
                    if (isPend)
                    {
                        // PA logic inside Cassino
                        if (flags.RichiestaPA && !flags.RinunciaPA && paSet.Contains(cf))
                        {
                            var termine = paDeadlines[s.InformazioniIscrizione.CodEnte];
                            result[cf] = termine >= DateTime.Now ? "B" : "C";
                        }
                        else if (flags.IdoneoPA_attesa_2assegn || flags.VincitorePA_NoAssegnazione)
                        {
                            result[cf] = "B";
                        }
                        else
                        {
                            result[cf] = "C";
                        }
                        continue;
                    }
                }

                // 7) Ente 05 (Viterbo)
                if (s.InformazioniIscrizione.CodEnte == "05")
                {
                    if (string.Equals(comuneRes, comuneSede, StringComparison.OrdinalIgnoreCase))
                    {
                        result[cf] = "A";
                    }
                    else
                    {
                        result[cf] = IsViterboPendolare(comuneRes, comuniProvince, fuoriSedeMap) ? "C" : "B";
                    }
                    continue;
                }

                // 8) Special distaccata overrides
                if ((comuneSede == "F061" && codCorso == "ISP")
                 || (comuneSede == "A462" && codCorso == "TW8"))
                {
                    result[cf] = "A";
                    continue;
                }

                // 9) Same-comune catch-all
                if (string.Equals(comuneRes, comuneSede, StringComparison.OrdinalIgnoreCase))
                {
                    result[cf] = "A";
                    continue;
                }

                // 10) inSede, pendolari, province, fuorisede
                var key = new[] { "A", "O", "P", "D" }.Contains(codSede) ? codSede : "B";
                if (inSedeMap.GetValueOrDefault(key, new()).Contains(comuneRes))
                {
                    result[cf] = "A";
                }
                else if (pendolariMap.GetValueOrDefault(key, new()).Contains(comuneRes)
                         && comuneSede == "H501")
                {
                    result[cf] = "C";
                }
                else if (SameProvince(comuneRes, comuneSede, comuniProvince))
                {
                    result[cf] = "C";
                }
                else if (fuoriSedeMap.GetValueOrDefault("LT", new()).Contains(comuneRes))
                {
                    result[cf] = "B";
                }
                else
                {
                    // domicilio fallback
                    if (dom.titoloOneroso && s.InformazioniSede.DomicilioCheck
                        && (dom.codComuneDomicilio == comuneSede
                            || dom.tipologiaEnteIstituto == Domicilio.TipologiaEnteIstituto.ErasmusSocrates))
                    {
                        result[cf] = "B";
                    }
                    else
                    {
                        result[cf] = "D";
                    }
                }
                if (((flags.RichiestaPA && !flags.RinunciaPA) || flags.VincitorePA_NoAssegnazione) && s.InformazioniBeneficio.EsitoPA == 2)
                {
                    if (paSet.Contains(cf) && paDeadlines.TryGetValue(s.InformazioniIscrizione.CodEnte, out var termine2)
                        && termine2 >= DateTime.Now)
                    {
                        result[cf] = "B";
                    }
                    else if (((flags.RichiestaPA && !flags.RinunciaPA) || flags.VincitorePA_NoAssegnazione) && s.InformazioniBeneficio.EsitoPA == 2)
                    {
                        result[cf] = "B";
                    }
                    else
                    {
                        result[cf] = "D";
                    }
                }

            }

            return result;
        }

    private static Dictionary<int, StudentBenefitFlags> LoadStudentBenefits(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<int> numeroDomande,
            SqlTransaction tx)
        {
            var domande = numeroDomande.ToList();
            var tvp = new DataTable();
            tvp.Columns.Add("NumDomanda", typeof(int));
            foreach (var n in domande) tvp.Rows.Add(n);

            var result = domande.ToDictionary(
                n => n,
                n => new StudentBenefitFlags(),
                EqualityComparer<int>.Default);

            // BENEFICI_RICHIESTI -> RichiestaPA
            using var cmd1 = conn.CreateCommand();
            cmd1.Transaction = tx;
            cmd1.CommandText = @"
SELECT B.NUM_DOMANDA, B.COD_BENEFICIO
  FROM BENEFICI_RICHIESTI AS B
  JOIN @Input AS I ON I.NumDomanda = B.NUM_DOMANDA
 WHERE B.ANNO_ACCADEMICO = @annoAccademico
   AND B.RIGA_VALIDA    = '0'
   AND B.DATA_FINE_VALIDITA IS NULL
   AND B.DATA_VALIDITA = (
     SELECT MAX(DATA_VALIDITA)
       FROM BENEFICI_RICHIESTI
      WHERE NUM_DOMANDA     = B.NUM_DOMANDA
        AND ANNO_ACCADEMICO = B.ANNO_ACCADEMICO
        AND COD_BENEFICIO   = B.COD_BENEFICIO
   )";
            var p1 = cmd1.Parameters.Add("@Input", SqlDbType.Structured);
            p1.TypeName = "dbo.NumeroDomandaType";
            p1.Value = tvp;
            cmd1.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });
            using var r1 = cmd1.ExecuteReader();
            while (r1.Read())
            {
                var num = (int)r1.GetDecimal(0);
                var ben = r1.GetString(1).Trim().ToUpperInvariant();
                if (ben == "PA") result[num].RichiestaPA = true;
            }

            // VARIAZIONI -> RinunciaPA
            using var cmd2 = conn.CreateCommand();
            cmd2.Transaction = tx;
            cmd2.CommandText = @"
SELECT V.NUM_DOMANDA, V.COD_TIPO_VARIAZ, V.COD_BENEFICIO
  FROM VARIAZIONI AS V
  JOIN @Input AS I ON I.NumDomanda = V.NUM_DOMANDA
 WHERE V.ANNO_ACCADEMICO = @annoAccademico
   AND V.DATA_VALIDITA  = (
     SELECT MAX(DATA_VALIDITA)
       FROM VARIAZIONI
      WHERE NUM_DOMANDA      = V.NUM_DOMANDA
        AND ANNO_ACCADEMICO  = V.ANNO_ACCADEMICO
        AND COD_TIPO_VARIAZ  = V.COD_TIPO_VARIAZ
        AND COD_BENEFICIO    = V.COD_BENEFICIO
   )";
            var p2 = cmd2.Parameters.Add("@Input", SqlDbType.Structured);
            p2.TypeName = "dbo.NumeroDomandaType";
            p2.Value = tvp;
            cmd2.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });
            using var r2 = cmd2.ExecuteReader();
            while (r2.Read())
            {
                var num = (int)r2.GetDecimal(0);
                var tipoStr = r2.GetString(1);
                var tipo = int.Parse(tipoStr, CultureInfo.InvariantCulture);
                var ben = r2.GetString(2).Trim().ToUpperInvariant();
                if (tipo == 2 && ben == "PA") result[num].RinunciaPA = true;
            }

            // ESITI_CONCORSI -> IdoneoPA_attesa_2assegn & VincitorePA_NoAssegnazione
            using var cmd3 = conn.CreateCommand();
            cmd3.Transaction = tx;
            cmd3.CommandText = @"
SELECT E.NUM_DOMANDA, E.COD_BENEFICIO, E.COD_TIPO_ESITO
  FROM ESITI_CONCORSI AS E
  JOIN @Input AS I ON I.NumDomanda = E.NUM_DOMANDA
 WHERE E.ANNO_ACCADEMICO = @annoAccademico
   AND E.DATA_VALIDITA  = (
     SELECT MAX(DATA_VALIDITA)
       FROM ESITI_CONCORSI
      WHERE NUM_DOMANDA      = E.NUM_DOMANDA
        AND ANNO_ACCADEMICO  = E.ANNO_ACCADEMICO
        AND COD_BENEFICIO    = E.COD_BENEFICIO
   )";
            var p3 = cmd3.Parameters.Add("@Input", SqlDbType.Structured);
            p3.TypeName = "dbo.NumeroDomandaType";
            p3.Value = tvp;
            cmd3.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });
            using var r3 = cmd3.ExecuteReader();
            while (r3.Read())
            {
                var num = (int)r3.GetDecimal(0);
                var ben = r3.GetString(1).Trim().ToUpperInvariant();
                var esito = r3.GetString(2).Trim();
                if (ben == "PA")
                {
                    if (esito == "1") result[num].IdoneoPA_attesa_2assegn = true;
                    if (esito == "4") result[num].VincitorePA_NoAssegnazione = true;
                }
            }

            return result;
        }

        private static HashSet<string> LoadProlungamentiPA(
            SqlConnection conn,
            string annoAccademico,
            IEnumerable<string> fiscals,
            SqlTransaction tx)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tvp = new DataTable();
            tvp.Columns.Add("Cod_fiscale", typeof(string));
            foreach (var cf in fiscals) tvp.Rows.Add(cf);

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT cod_fiscale
  FROM Prolungamenti_posto_alloggio
 WHERE anno_accademico    = @annoAccademico
   AND data_fine_validita IS NULL
   AND cod_fiscale IN (SELECT Cod_fiscale FROM @Input);";
            var p = cmd.Parameters.Add("@Input", SqlDbType.Structured);
            p.TypeName = "dbo.CFEstrazione";
            p.Value = tvp;
            cmd.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });

            using var r = cmd.ExecuteReader();
            while (r.Read())
                set.Add(r.GetString(0).Trim());
            return set;
        }

        private static Dictionary<string, DateTime> LoadTerminiPA(
            SqlConnection conn,
            string annoAccademico,
            SqlTransaction tx)
        {
            var dict = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
SELECT cod_ente, data_termine_ass_pa
  FROM termine_assegnazione_pa
 WHERE anno_accademico = @annoAccademico;";
            cmd.Parameters.Add(new SqlParameter("@annoAccademico", SqlDbType.NVarChar, 9)
            {
                Value = annoAccademico
            });

            using var r = cmd.ExecuteReader();
            while (r.Read())
                dict[r.GetString(0).Trim()] = r.GetDateTime(1);
            return dict;
        }
        private static bool CheckCassinoPendolare(
    string comuneRes,
    string comuneSede,
    string codSede,
    Dictionary<string, string> comuniProvince,
    Dictionary<string, HashSet<string>> pendolariMap,
    Dictionary<string, HashSet<string>> fuoriSedeMap)
        {
            // 1) Same-province pendolari (excluding "A" fuorisede)
            if (comuniProvince.TryGetValue(comuneRes, out var provRes)
                && comuniProvince.TryGetValue(comuneSede, out var provSede)
                && string.Equals(provRes, provSede, StringComparison.OrdinalIgnoreCase)
                && !fuoriSedeMap.GetValueOrDefault("A", new HashSet<string>()).Contains(comuneRes))
            {
                return true;
            }

            // 2) Specific pendolari table entries for sede 'C034' or 'D810'
            if ((comuneSede == "C034" || comuneSede == "D810")
                && pendolariMap.GetValueOrDefault(codSede, new HashSet<string>()).Contains(comuneRes))
            {
                return true;
            }

            return false;
        }

        private static bool IsViterboPendolare(
            string comuneRes,
            Dictionary<string, string> comuniProvince,
            Dictionary<string, HashSet<string>> fuoriSedeMap)
        {
            // Students in province "VT" are pendolari, unless explicitly fuorisede for 'D'
            if (comuniProvince.TryGetValue(comuneRes, out var prov)
                && string.Equals(prov, "VT", StringComparison.OrdinalIgnoreCase))
            {
                return !fuoriSedeMap.GetValueOrDefault("D", new HashSet<string>()).Contains(comuneRes);
            }
            return false;
        }

        private static bool SameProvince(
            string comuneRes,
            string comuneSede,
            Dictionary<string, string> comuniProvince)
        {
            if (comuniProvince.TryGetValue(comuneRes, out var provRes)
                && comuniProvince.TryGetValue(comuneSede, out var provSede))
            {
                return string.Equals(provRes, provSede, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

    }

}
