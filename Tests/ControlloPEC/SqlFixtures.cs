using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Text.RegularExpressions;
using ProcedureNet7;

internal static class SqlFixtures
{
    private const string StartingYear = "20252026";
    private static readonly IReadOnlyList<Scenario> Cases = BuildCases();
    private static readonly HashSet<string> ExtractedFiscalCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "NO_NEW_DOMICILE", "NEW_REGISTERED_CONTRACT", "ADEQUATE_NEW_DOMICILE", "VALID_APPROVED_PEC",
        "MALFORMED_APPROVED_DOMAIN", "UNAPPROVED_DOMAIN", "LEGACY_CONTRACT", "SERIES_NULL",
        "CITY_ONLY_DOMICILE", "CONTRACT_CLOSED"
    };
    private static readonly string[] Sources =
    {
        "vDomicilio", "Domicili", "Contratti", "Domanda", "LUOGO_REPERIBILITA_STUDENTE",
        "vProfilo", "vResidenza", "vEsiti_concorsiBS", "Comuni", "Domini_PEC"
    };

    public static void ValidateQueryComposition()
    {
        foreach (var query in new[] { ControlloPecQueries.RemovalCandidates, ControlloPecQueries.BlockCandidates, ControlloPecQueries.PaymentCandidates, ControlloPecQueries.ApprovedDomains })
        {
            var sql = FixtureQuery(query);
            Assert(!Regex.IsMatch(sql, @"\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|EXEC|EXECUTE|INTO)\b", RegexOptions.IgnoreCase),
                "Le fixture devono eseguire esclusivamente SELECT, senza DML, oggetti temporanei o istruzioni amministrative.");
            Assert(!Regex.IsMatch(sql, @"\bdbo\.", RegexOptions.IgnoreCase),
                "Ogni fonte del database deve essere sostituita da una CTE sintetica.");
            Assert(!sql.Contains('#'), "L'estrazione pagamenti deve usare la CTE di fixture, senza tabelle temporanee.");
            Assert(Regex.Matches(sql, @"\bWITH\b", RegexOptions.IgnoreCase).Count == 1,
                "Le fixture e i contratti esenti devono appartenere alla medesima istruzione WITH.");
        }
        Assert(Cases.Where(s => s.Code == "NO_NEW_DOMICILE").Select(s => s.Year).ToHashSet().SetEquals(new[] { "20252026", "20262027", "20272028" }),
            "Le fixture devono includere lo stesso studente nei tre anni per rilevare sconfinamenti dell'estrazione pagamenti.");
        Assert(Cases.Any(s => s.SqlCandidate && s.Year == "20262027" && !ExtractedFiscalCodes.Contains(s.Code)),
            "Le fixture devono includere almeno un candidato dell'anno che non appartiene all'estrazione pagamenti.");
    }

    public static void Run(SqlConnection connection)
    {
        ValidateQueryComposition();
        var domains = Query(connection, ControlloPecQueries.ApprovedDomains)
            .Select(row => row["Dominio"]?.ToString() ?? "")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert(domains.SetEquals(new[] { "pec.it", "LEGALMAIL.IT" }),
            "Approvato=0 e domini vuoti devono essere esclusi; quelli approvati devono essere ripuliti dagli spazi esterni.");
        Console.WriteLine("PASS: elenco PEC include soltanto domini approvati non vuoti.");

        var removals = Query(connection, ControlloPecQueries.RemovalCandidates).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedRemovals = Cases.Where(s => s.Removal).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssertSet(removals, expectedRemovals, "candidati alla rimozione del blocco");
        Console.WriteLine("PASS: rimozioni per contratto e domicilio adeguato distinte tra gestione storica e nuova.");

        var candidates = Query(connection, ControlloPecQueries.BlockCandidates);
        var candidateKeys = candidates.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssertSet(candidateKeys, Cases.Where(s => s.SqlCandidate).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            "candidati SQL prima della verifica dell'indirizzo PEC");
        var blocks = candidates.Where(row => !ControlloPecAddress.IsValid(row["Indirizzo_PEC"] as string, domains))
            .Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        AssertSet(blocks, Cases.Where(s => s.Block).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
            "candidati al blocco dopo la verifica PEC del portale");
        foreach (var year in new[] { "20252026", "20262027", "20272028" })
        {
            var selectedCandidates = candidates.Where(row => string.Equals(row["ANNO_ACCADEMICO"]?.ToString(), year, StringComparison.Ordinal) &&
                ExtractedFiscalCodes.Contains(row["COD_FISCALE"]?.ToString() ?? "")).ToList();
            var paymentCandidates = Query(connection, ControlloPecQueries.PaymentCandidates, year);
            Assert(paymentCandidates.All(row => string.Equals(row["ANNO_ACCADEMICO"]?.ToString(), year, StringComparison.Ordinal) &&
                ExtractedFiscalCodes.Contains(row["COD_FISCALE"]?.ToString() ?? "")),
                "La query pagamenti deve limitarsi all'anno esatto e ai soli codici fiscali estratti.");
            AssertSet(paymentCandidates.Select(FullKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
                selectedCandidates.Select(FullKey).ToHashSet(StringComparer.OrdinalIgnoreCase),
                "coerenza dei candidati PEC tra controlli e pagamenti nell'anno " + year);
            var paymentBlocks = paymentCandidates.Where(row => !ControlloPecAddress.IsValid(row["Indirizzo_PEC"] as string, domains))
                .Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            AssertSet(paymentBlocks, Cases.Where(s => s.Block && s.Year == year && ExtractedFiscalCodes.Contains(s.Code))
                .Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase),
                "esito PEC identico dopo sintassi, domini approvati ed esenzioni per i pagamenti nell'anno " + year);
        }
        Console.WriteLine("PASS: pagamenti limitati all'anno esatto e all'estrazione CF, con la stessa politica PEC dei controlli.");
        Console.WriteLine($"PASS: {Cases.Count} scenari SQL sintetici; nuova gestione, collegamenti, indirizzi e PEC verificati in sola lettura.");
    }

    private static List<Dictionary<string, object?>> Query(SqlConnection connection, string query, string academicYear = StartingYear)
    {
        using var command = new SqlCommand(FixtureQuery(query), connection) { CommandTimeout = 60 };
        command.Parameters.Add("@AnnoDa", SqlDbType.VarChar, 8).Value = academicYear;
        using var reader = command.ExecuteReader();
        var result = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }

    private static string FixtureQuery(string query)
    {
        var rewritten = query;
        foreach (var source in Sources)
            rewritten = Regex.Replace(rewritten, @"\bdbo\." + Regex.Escape(source) + @"\b", "Fixture_" + source, RegexOptions.IgnoreCase);
        rewritten = rewritten.Replace("#CFEstrazione", "Fixture_CFEstrazione", StringComparison.OrdinalIgnoreCase);
        const string marker = ";WITH ContrattiEsentiPec AS";
        return rewritten.Contains(marker, StringComparison.Ordinal)
            ? rewritten.Replace(marker, ";WITH " + FixtureDefinitions() + ",\nContrattiEsentiPec AS", StringComparison.Ordinal)
            : ";WITH " + FixtureDefinitions() + "\n" + rewritten;
    }

    private static string FixtureDefinitions()
    {
        var tables = new List<string>
        {
            Cte("Domanda", new[] { "Anno_accademico", "Num_domanda", "Cod_fiscale", "Tipo_bando" },
                Cases.Select(s => Row(s.Year, s.Number, s.Code, s.ApplicationBando))),
            Cte("LUOGO_REPERIBILITA_STUDENTE", new[] { "ANNO_ACCADEMICO", "COD_FISCALE", "tipo_bando", "TIPO_LUOGO", "DATA_FINE_VALIDITA", "INDIRIZZO" },
                Cases.Select(s => Row(s.Year, s.Code, s.LocationBando, "DOL", s.LocationClosed ? new DateTime(2026, 1, 1) : null, s.LocationAddress))),
            Cte("vProfilo", new[] { "Cod_Fiscale", "Indirizzo_PEC" }, Cases.Select(s => Row(s.Code, s.Pec))),
            Cte("vResidenza", new[] { "ANNO_ACCADEMICO", "COD_FISCALE", "provincia_residenza" }, Cases.Select(s => Row(s.Year, s.Code, s.ResidenceProvince))),
            Cte("vEsiti_concorsiBS", new[] { "Anno_accademico", "Num_domanda", "Cod_tipo_esito" }, Cases.Select(s => Row(s.Year, s.Number, s.Result))),
            Cte("vDomicilio", new[] { "ANNO_ACCADEMICO", "COD_FISCALE", "Titolo_oneroso", "N_serie_contratto", "Indirizzo_domicilio", "prov" },
                Cases.Where(s => s.LegacyDomicile).Select(s => Row(s.Year, s.Code, s.LegacyOnerous ? 1 : 0, s.LegacySeries, s.LegacyAddress, "EE"))),
            Cte("Domicili", new[] { "ID", "ANNO_ACCADEMICO", "NUM_DOMANDA", "COD_FISCALE", "TIPO_LUOGO", "TIPO_DOMICILIO", "DATA_FINE_VALIDITA", "IS_DELETED", "COD_COMUNE", "INDIRIZZO" },
                Cases.Where(s => s.NewDomicile).Select(s => Row(s.Number, s.DomicileYear ?? s.Year, s.OtherApplication ? 999999 : s.Number,
                    s.OtherStudent ? "ANOTHER_SYNTHETIC_STUDENT" : s.Code, s.DomicilePlace, s.Onerous ? "1" : "0",
                    s.DomicileClosed ? new DateTime(2026, 1, 1) : null, s.DomicileDeleted ? 1 : 0, s.Municipality, s.DomicileAddress))),
            Cte("Contratti", new[] { "ID_DOMICILIO", "DATA_FINE_VALIDITA", "IS_DELETED", "TIPO_CONTRATTO_TITOLO_ONEROSO", "N_SERIE_CONTRATTO" },
                Cases.Where(s => s.Contract).Select(s => Row(s.Number, s.ContractClosed ? new DateTime(2026, 1, 1) : null,
                    s.ContractDeleted ? 1 : 0, s.ContractType, s.ContractSeries))),
            Cte("Comuni", new[] { "COD_COMUNE", "COD_PROVINCIA" }, new[] { Row("H501", "RM"), Row("FOREIGN", "EE") }),
            Cte("Domini_PEC", new[] { "Dominio", "Approvato" }, new[]
            {
                Row("pec.it", 1), Row(" LEGALMAIL.IT ", 1), Row("unapproved.it", 0), Row(" ", 1)
            }),
            Cte("CFEstrazione", new[] { "COD_FISCALE" }, ExtractedFiscalCodes.OrderBy(x => x).Select(code => Row(code)))
        };
        return string.Join(",\n", tables);
    }

    private static string Cte(string name, string[] columns, IEnumerable<object?[]> rows)
        => $"Fixture_{name} ({string.Join(",", columns)}) AS (SELECT * FROM (VALUES\n" +
           string.Join(",\n", rows.Select(row => "(" + string.Join(",", row.Select(Literal)) + ")")) +
           ") v(" + string.Join(",", columns) + "))";

    private static object?[] Row(params object?[] values) => values;
    private static string Literal(object? value) => value switch
    {
        null => "NULL",
        DateTime date => "CAST('" + date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "' AS datetime)",
        int number => number.ToString(CultureInfo.InvariantCulture),
        _ => "N'" + value.ToString()!.Replace("'", "''") + "'"
    };

    private static IReadOnlyList<Scenario> BuildCases()
    {
        var cases = new List<Scenario>();
        Scenario Add(string name)
        {
            var value = new Scenario(name, cases.Count + 1);
            cases.Add(value);
            return value;
        }
        void Exempt(Scenario value) { value.Removal = true; value.SqlCandidate = false; value.Block = false; }
        void NotEligible(Scenario value) { value.SqlCandidate = false; value.Block = false; }
        Scenario Contract(string name)
        {
            var value = Add(name);
            value.NewDomicile = true; value.Contract = true; value.Onerous = true;
            return value;
        }

        var legacy = Add("LEGACY_CONTRACT"); legacy.Year = "20252026";
        legacy.LegacyDomicile = true; legacy.LegacyOnerous = true; legacy.LegacySeries = "LEG123"; Exempt(legacy);
        var ignored = Add("NEW_IGNORES_LEGACY"); ignored.LegacyDomicile = true; ignored.LegacyOnerous = true; ignored.LegacySeries = "LEG123";
        var registered = Contract("NEW_REGISTERED_CONTRACT"); Exempt(registered);
        // No duration/date columns exist in Fixture_Contratti. This intentionally
        // verifies that mere current registration exempts regardless of term or geography.
        registered.DomicileAddress = ""; registered.Municipality = "FOREIGN";
        Contract("DOMICILE_CLOSED").DomicileClosed = true;
        Contract("DOMICILE_DELETED").DomicileDeleted = true;
        Contract("CONTRACT_CLOSED").ContractClosed = true;
        Contract("CONTRACT_DELETED").ContractDeleted = true;
        Contract("NON_REGISTERED_ENTITY_RELATION").ContractType = 1;
        Contract("SERIES_WHITESPACE").ContractSeries = "   ";
        Contract("SERIES_NULL").ContractSeries = null;
        Contract("OTHER_APPLICATION").OtherApplication = true;
        Contract("OTHER_STUDENT").OtherStudent = true;
        Contract("OTHER_ACADEMIC_YEAR").DomicileYear = "20252026";
        var missing = Contract("NO_CONTRACT"); missing.Contract = false;
        Contract("FREE_DOMICILE").Onerous = false;
        Contract("WRONG_PLACE_TYPE").DomicilePlace = "DOL";

        var adequate = Add("ADEQUATE_NEW_DOMICILE"); adequate.NewDomicile = true;
        adequate.DomicileAddress = "Via Sintetica 12"; adequate.Municipality = "H501"; Exempt(adequate);
        var townOnly = Add("CITY_ONLY_DOMICILE"); townOnly.NewDomicile = true; townOnly.DomicileAddress = "  ROMA  "; townOnly.Municipality = "H501";
        var foreign = Add("FOREIGN_DOMICILE"); foreign.NewDomicile = true; foreign.DomicileAddress = "Via Sintetica"; foreign.Municipality = "FOREIGN";
        var unknownTown = Add("UNKNOWN_MUNICIPALITY"); unknownTown.NewDomicile = true; unknownTown.DomicileAddress = "Via Sintetica"; unknownTown.Municipality = "UNKNOWN";
        Add("NO_NEW_DOMICILE");
        var valid = Add("VALID_APPROVED_PEC"); valid.Pec = "studente@pec.it"; valid.Block = false;
        var display = Add("DISPLAY_NAME_APPROVED_PEC"); display.Pec = "Mario Rossi <studente@PEC.IT>"; display.Block = false;
        Add("MALFORMED_APPROVED_DOMAIN").Pec = "@pec.it";
        Add("UNAPPROVED_DOMAIN").Pec = "studente@unapproved.it";
        var dol = Add("ADEQUATE_DOL"); dol.LocationAddress = "Via Sintetica 12"; NotEligible(dol);
        var residence = Add("ITALIAN_RESIDENCE"); residence.ResidenceProvince = "RM"; NotEligible(residence);
        var noResult = Add("NO_ELIGIBLE_RESULT"); noResult.Result = 0; NotEligible(noResult);
        var absentLegacy = Add("LEGACY_NO_DOMICILE"); absentLegacy.Year = "20252026"; NotEligible(absentLegacy);
        var badLegacy = Add("LEGACY_INADEQUATE_DOMICILE"); badLegacy.Year = "20252026"; badLegacy.LegacyDomicile = true;
        var old = Add("BELOW_STARTING_YEAR"); old.Year = "20242025"; old.LegacyDomicile = true; old.LegacyOnerous = true; old.LegacySeries = "LEG123"; NotEligible(old);
        var closedDol = Add("CLOSED_DOL"); closedDol.LocationClosed = true; NotEligible(closedDol);
        var otherBando = Add("OTHER_BANDO"); otherBando.ApplicationBando = "XX"; NotEligible(otherBando);
        var unrelatedAddress = Add("ADEQUATE_OTHER_APPLICATION"); unrelatedAddress.NewDomicile = true;
        unrelatedAddress.DomicileAddress = "Via Sintetica 12"; unrelatedAddress.Municipality = "H501"; unrelatedAddress.OtherApplication = true;
        var closedAddress = Add("ADEQUATE_BUT_CLOSED"); closedAddress.NewDomicile = true;
        closedAddress.DomicileAddress = "Via Sintetica 12"; closedAddress.Municipality = "H501"; closedAddress.DomicileClosed = true;
        var deletedAddress = Add("ADEQUATE_BUT_DELETED"); deletedAddress.NewDomicile = true;
        deletedAddress.DomicileAddress = "Via Sintetica 12"; deletedAddress.Municipality = "H501"; deletedAddress.DomicileDeleted = true;
        var otherStudentAddress = Add("ADEQUATE_OTHER_STUDENT"); otherStudentAddress.NewDomicile = true;
        otherStudentAddress.DomicileAddress = "Via Sintetica 12"; otherStudentAddress.Municipality = "H501"; otherStudentAddress.OtherStudent = true;
        var otherYearAddress = Add("ADEQUATE_OTHER_YEAR"); otherYearAddress.NewDomicile = true;
        otherYearAddress.DomicileAddress = "Via Sintetica 12"; otherYearAddress.Municipality = "H501"; otherYearAddress.DomicileYear = "20252026";
        var legacyAddress = Add("LEGACY_ADEQUATE_NO_CLEANUP"); legacyAddress.Year = "20252026"; legacyAddress.NewDomicile = true;
        legacyAddress.DomicileAddress = "Via Sintetica 12"; legacyAddress.Municipality = "H501"; NotEligible(legacyAddress);
        var sameStudentLegacy = Add("NO_NEW_DOMICILE"); sameStudentLegacy.Year = "20252026"; sameStudentLegacy.LegacyDomicile = true;
        var sameStudentFuture = Add("NO_NEW_DOMICILE"); sameStudentFuture.Year = "20272028";
        var otherStudentFuture = Add("FUTURE_NOT_EXTRACTED"); otherStudentFuture.Year = "20272028";
        return cases;
    }

    private static string Key(Scenario scenario) => scenario.Code + ":" + scenario.Year;
    private static string Key(Dictionary<string, object?> row) => row["COD_FISCALE"] + ":" + row["ANNO_ACCADEMICO"];
    private static string FullKey(Dictionary<string, object?> row)
        => Key(row) + "|" + row["Indirizzo_PEC"] + "|" + row["Cod_tipo_esito"];
    private static void AssertSet(HashSet<string> actual, HashSet<string> expected, string description)
    {
        Assert(actual.SetEquals(expected), "Risultato errato per " + description + ". Mancanti: " + string.Join(", ", expected.Except(actual)) +
            "; inattesi: " + string.Join(", ", actual.Except(expected)));
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class Scenario
    {
        public Scenario(string code, int number) { Code = code; Number = number; }
        public string Code { get; }
        public int Number { get; }
        public string Year { get; set; } = "20262027";
        public bool Removal { get; set; }
        public bool SqlCandidate { get; set; } = true;
        public bool Block { get; set; } = true;
        public string ApplicationBando { get; set; } = "lz";
        public string LocationBando { get; set; } = "lz";
        public string LocationAddress { get; set; } = "ROMA";
        public bool LocationClosed { get; set; }
        public string ResidenceProvince { get; set; } = "EE";
        public int Result { get; set; } = 1;
        public string? Pec { get; set; }
        public bool LegacyDomicile { get; set; }
        public bool LegacyOnerous { get; set; }
        public string? LegacySeries { get; set; }
        public string LegacyAddress { get; set; } = "ROMA";
        public bool NewDomicile { get; set; }
        public bool Onerous { get; set; }
        public bool DomicileClosed { get; set; }
        public bool DomicileDeleted { get; set; }
        public string? DomicileYear { get; set; }
        public bool OtherApplication { get; set; }
        public bool OtherStudent { get; set; }
        public string DomicilePlace { get; set; } = "DOM";
        public string DomicileAddress { get; set; } = "";
        public string Municipality { get; set; } = "FOREIGN";
        public bool Contract { get; set; }
        public bool ContractClosed { get; set; }
        public bool ContractDeleted { get; set; }
        public int ContractType { get; set; }
        public string? ContractSeries { get; set; } = "REG123";
    }
}
