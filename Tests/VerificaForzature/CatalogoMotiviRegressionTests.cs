using ProcedureNet7;

// Fixtures della lettura ADISU del 02/11/2026. I test non leggono file di lavoro
// e non aprono connessioni: la collisione viene controllata anche sullo storico.
internal static class CatalogoMotiviRegressionTests
{
    private const string CurrentSnapshotCodes =
        "000;001;002;003;004;005;006;007;008;009;010;011;012;013;014;015;016;017;018;019;020;021;022;023;024;025;026;027;028;029;030;031;032;033;034;035;036;037;038;039;040;041;042;043;044;045;046;047;048;049;050;051;052;053;054;055;056;057;058;059;060;061;062;063;064;065;066;067;068;069;070;071;072;073;074;075;076;077;078;079;080;081;082;083;084;085;086;087;088;089;090;091;092;093;094;095;096;097;098;099;100;101;102;103;104;105;106;107;108;109;110;111;112;113;114;115;116;117;118;119;120;121;122;123;201;202;203;204;205;206;207;208;209;210;212;220;221;222;223;224;BL1;BL11;BL12;BL13;BL15;BL16;BL17;BL18;BL2;BL20;BL21;BL22;BL3;BL30;BL4;BL5;BL7;CS1;CS2;CS3;CS4;CS5;CS6;CS7;CS8;CS9;M1;M2;M3;M4;M5;M6;M7;M8;M9;MA;MA1;MA2;MA3;MB;MC;MD;ME;MF;MG;MH;MI;MJ;MK;ML;MM;MN;MO;MP;MQ;MR;MS;MT;MU;MV;MX;MY;MZ";
    private const string HistoricalAdditionalCodes =
        "211;213;214;215;216;218;219;BL10;BL14;BL19;BL24;BL25;BL26;BL6;BL8;BL9;M01;M02;M03;M04;M05;M06;M07;M08;M09;M10;M11;M12;M13;M14;M15;M16;M17;M18";

    // Contratto della mappatura decisa sui 74 motivi; i nuovi 225-246 non
    // risultano registrati in nessuno dei due snapshot.
    private const string ExpectedPairings =
        "GEN000=118;GEN001=107;GEN003=225;GEN004=017;GEN005=018;GEN006=019;GEN007=020;" +
        "ISC001=226;ISC004=004;ISC006=006;ISC008=227;GEN093=106;GEN094=101;GENDOC=015;" +
        "RED011=228;RED012=047;RED013=048;RED086=229;RED087=122;RED031=230;RED033=231;" +
        "MER001=232;MER088=119;MER005=233;MER071=102;MER072=103;MER074=105;MER085=116;" +
        "MER086=234;MER087=235;MER012=033;MER092=123;MER089=120;MER090=236;MER091=237;" +
        "MER093=238;MER170=239;BS001=050;BS002=245;BS003=052;BS004=222;PA001=054;" +
        "PA002=240;PA003=055;PA004=241;PA005=057;PA006=079;PA007=084;PA008=088;" +
        "CI001=071;CI002=242;CI003=072;CI004=246;CI005=074;CI006=097;CI007=082;CI008=087;" +
        "ISC009=243;ISC010=010;VAR003=077;VAR004=078;VAR011=083;VAR019=089;VAR020=090;" +
        "VAR021=244;VAR022=092;VAR023=093;VAR024=094;VAR025=095;VAR027=111;VAR028=112;" +
        "VAR029=113;VAR030=114;VAR031=115";

    public static void Run()
    {
        int checks = 0;
        var failures = new List<string>();
        var current = Codes(CurrentSnapshotCodes);
        var historical = Codes(CurrentSnapshotCodes + ";" + HistoricalAdditionalCodes);
        var expected = ExpectedPairings.Split(';')
            .Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
        var rows = EsitoBorsaMotiviCatalog.Voci.Where(v => !v.CodiceRegola.Contains('*')).ToArray();

        Check("tutti i 74 motivi hanno una segnalazione univoca e documentata", () =>
        {
            Equal(current.Count, 203, "fixture catalogo AA 20262027");
            Equal(historical.Count, 237, "fixture storico");
            Equal(rows.Length, 74, "motivi statici");
            Equal(rows.Select(v => v.CodiceRegola).Distinct(StringComparer.OrdinalIgnoreCase).Count(), 74, "regole univoche");
            Equal(rows.Select(v => v.CodiceSegnalazione).Distinct(StringComparer.Ordinal).Count(), 74, "segnalazioni univoche");
            Equal(rows.Count(v => v.Stato == StatoMappaturaMotivo.Verificata), 52, "abbinamenti al catalogo DB");
            Equal(rows.Count(v => v.Stato == StatoMappaturaMotivo.DaRegistrare), 22, "codici nuovi da registrare");
            foreach (var row in rows)
            {
                Required(row.DescrizioneRegola, $"descrizione regola {row.CodiceRegola}");
                Required(row.DescrizioneSegnalazione, $"descrizione segnalazione {row.CodiceRegola}");
                Required(row.Fonte, $"fonte {row.CodiceRegola}");
                Required(row.Benefici, $"benefici {row.CodiceRegola}");
                Equal(EsitoBorsaMotiviCatalog.GetDescrizione(row.CodiceRegola), row.DescrizioneRegola,
                    $"descrizione centralizzata {row.CodiceRegola}");
                string code = row.CodiceSegnalazione ?? "";
                if (code.Length != 3 || code.Any(c => c < '0' || c > '9') || code == "000")
                    throw new InvalidOperationException($"Segnalazione non valida: {row.CodiceRegola} -> {code}.");
                if (row.Stato == StatoMappaturaMotivo.Verificata)
                {
                    if (!current.Contains(code))
                        throw new InvalidOperationException($"Segnalazione verificata assente dal catalogo 20262027: {code}.");
                }
                else if (row.Stato == StatoMappaturaMotivo.DaRegistrare)
                {
                    if (int.Parse(code) < 225 || historical.Contains(code))
                        throw new InvalidOperationException($"Nuova segnalazione occupata o sotto 225: {row.CodiceRegola} -> {code}.");
                    Required(row.Nota, $"nota registrazione {row.CodiceRegola}");
                }
                else
                    throw new InvalidOperationException($"Motivo statico senza mappatura: {row.CodiceRegola}.");
            }
        });

        foreach (var pairing in expected)
        {
            Check($"pairing definitivo {pairing.Key}", () =>
            {
                var entry = rows.Single(v => v.CodiceRegola == pairing.Key);
                var expectedState = int.Parse(pairing.Value) >= 225
                    ? StatoMappaturaMotivo.DaRegistrare : StatoMappaturaMotivo.Verificata;
                foreach (string benefit in entry.Benefici.Split(';'))
                {
                    var resolved = EsitoBorsaMotiviCatalog.Risolvi(pairing.Key, benefit);
                    Equal(resolved.CodiceSegnalazione, pairing.Value, "segnalazione assegnata");
                    Equal(resolved.Stato, expectedState, "stato registrazione");
                    var result = EsitoBorsaMotiviCatalog.Mappa(pairing.Key, benefit);
                    Equal(result.Completa, true, "mappatura completa anche per codice nuovo");
                    Equal(result.CodiciSegnalazione, pairing.Value, "codice esportabile");
                    Equal(result.RichiedeRegistrazione, expectedState == StatoMappaturaMotivo.DaRegistrare,
                        "registrazione necessaria");
                    if (result.RichiedeRegistrazione)
                        Contains(result.Diagnostica, pairing.Key, "diagnostica del nuovo codice");
                    else
                        Equal(result.Diagnostica, "", "nessuna diagnostica per codice DB");
                }
            });
        }

        Check("MER170 e forzatura 201 hanno significati e codici distinti", () =>
        {
            var merit = EsitoBorsaMotiviCatalog.Risolvi("MER170", "BS");
            var forcing = EsitoBorsaMotiviCatalog.Risolvi("GENF201", "BS");
            Equal(merit.CodiceSegnalazione, "239", "nuovo codice vecchio ordinamento");
            Equal(forcing.CodiceSegnalazione, "201", "codice DB semestre filtro");
            Contains(merit.DescrizioneRegola, "Sapienza", "causale MER170");
            Contains(forcing.DescrizioneSegnalazione, "SEMESTRE FILTRO", "causale DB 201");
            var result = EsitoBorsaMotiviCatalog.Mappa("GENF201;MER170;GENF201", "BS");
            Equal(result.CodiciSegnalazione, "201;239", "nessuna deduplicazione tra causali diverse");
            Equal(result.Completa, true, "mappatura delle due causali");
            Equal(result.RichiedeRegistrazione, true, "239 da registrare");
        });
        Check("114 descrive la mancata comunicazione delle modalita di pagamento", () =>
        {
            var row = EsitoBorsaMotiviCatalog.Risolvi("VAR030", "BS");
            Equal(row.CodiceSegnalazione, "114", "segnalazione pagamento");
            foreach (string description in new[] { row.DescrizioneRegola, row.DescrizioneSegnalazione })
            {
                Contains(description, "pagamento", "descrizione 114");
                if (description.Contains("ISEE", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("CAF", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La causale 114 conserva la precedente descrizione ISEE/CAF errata.");
            }
        });
        Check("103 riguarda il confronto tra anno calcolato e dichiarato", () =>
        {
            var row = EsitoBorsaMotiviCatalog.Risolvi("MER072", "BS");
            Equal(row.CodiceSegnalazione, "103", "segnalazione incongruenza");
            Contains(row.DescrizioneRegola, "calcolato", "anno calcolato");
            Contains(row.DescrizioneRegola, "dichiarato", "anno dichiarato");
        });
        Check("223 e 224 occupati mantengono i significati DB", () =>
        {
            var foreignTitle = EsitoBorsaMotiviCatalog.Risolvi("GENF223", "BS");
            var postgraduate = EsitoBorsaMotiviCatalog.Risolvi("GENF224", "BS");
            Equal(foreignTitle.Stato, StatoMappaturaMotivo.Verificata, "223 esistente");
            Equal(postgraduate.Stato, StatoMappaturaMotivo.Verificata, "224 esistente");
            Contains(foreignTitle.DescrizioneSegnalazione, "ESTERO", "titolo estero 223");
            Contains(postgraduate.DescrizioneSegnalazione, "DOTTORATO", "dottorato 224");
            Contains(postgraduate.DescrizioneSegnalazione, "TRIENNALE", "titolo di accesso 224");
            Equal(EsitoBorsaMotiviCatalog.Risolvi("RED033", "BS").CodiceSegnalazione, "231", "integrazione ISEE separata");
            Equal(EsitoBorsaMotiviCatalog.Risolvi("MER090", "BS").CodiceSegnalazione, "236", "scadenza titolo separata");
        });

        Check("normalizzazione di codice e beneficio", () =>
        {
            var row = EsitoBorsaMotiviCatalog.Risolvi(" pa008 ", " pk ");
            Equal(row.CodiceRegola, "PA008", "codice normalizzato");
            Equal(row.CodiceSegnalazione, "088", "pairing normalizzato");
            var result = EsitoBorsaMotiviCatalog.Mappa(" red013 ; gen005 ; RED013 ; ", " bs ");
            Equal(result.Completa, true, "mapping normalizzato completo");
            Equal(result.CodiciSegnalazione, "048;018", "ordine e deduplicazione");
            Equal(result.RichiedeRegistrazione, false, "nessun codice nuovo");
        });
        Check("input vuoto non richiede abbinamenti", () =>
        {
            foreach (string? input in new string?[] { null, "", " ; ; " })
            {
                var result = EsitoBorsaMotiviCatalog.Mappa(input, "BS");
                Equal(result.Completa, true, "nessun motivo da mappare");
                Equal(result.CodiciSegnalazione, "", "nessun codice");
                Equal(result.Diagnostica, "", "nessuna diagnostica");
                Equal(result.RichiedeRegistrazione, false, "nessuna registrazione");
            }
        });
        Check("codici esistenti nuovi e sconosciuti restano distinti", () =>
        {
            var result = EsitoBorsaMotiviCatalog.Mappa("RED013;ISC001;RED012;RED033;VAR021;XYZ123", "BS");
            Equal(result.Completa, false, "il solo sconosciuto rende il mapping incompleto");
            Equal(result.CodiciSegnalazione, "048;226;047;231;244", "codici nuovi inclusi in ordine originale");
            Equal(result.RichiedeRegistrazione, true, "nuovi codici presenti");
            foreach (string code in new[] { "ISC001", "RED033", "VAR021", "XYZ123" })
                Contains(result.Diagnostica, code, "diagnostica preservata");
        });
        Check("codice sconosciuto non genera una segnalazione", () =>
        {
            var row = EsitoBorsaMotiviCatalog.Risolvi(" xyz123 ", "BS");
            Equal(row.Stato, StatoMappaturaMotivo.Sconosciuta, "stato sconosciuto");
            Equal(row.CodiceSegnalazione, null, "nessuna segnalazione");
            var result = EsitoBorsaMotiviCatalog.Mappa(" xyz123 ", "BS");
            Equal(result.Completa, false, "mapping sconosciuto incompleto");
            Equal(result.CodiciSegnalazione, "", "nessun codice esportato");
            Equal(result.RichiedeRegistrazione, false, "nessun numero riservato allo sconosciuto");
            Contains(result.Diagnostica, "XYZ123", "codice sconosciuto preservato");
        });
        Check("beneficio incompatibile impedisce il mapping", () =>
        {
            var result = EsitoBorsaMotiviCatalog.Mappa("BS001;RED012", "CI");
            Equal(result.Completa, false, "beneficio incompatibile");
            Equal(result.CodiciSegnalazione, "047", "resta il motivo economico comune");
            Equal(result.RichiedeRegistrazione, false, "nessun nuovo codice");
            Contains(result.Diagnostica, "BS001", "regola incompatibile");
            Contains(result.Diagnostica, "CI", "beneficio incompatibile");
        });
        Check("deduplica tra BS004 e identita DB GENF222", () =>
        {
            var result = EsitoBorsaMotiviCatalog.Mappa("BS004;genf222;RED012;BS004;GENF222", "BS");
            Equal(result.Completa, true, "alias risolti");
            Equal(result.CodiciSegnalazione, "222;047", "deduplica sulla stessa segnalazione DB");
            Equal(result.RichiedeRegistrazione, false, "222 gia presente in DB");
        });
        Check("deduplica tra nuovo codice e relativa forzatura", () =>
        {
            var result = EsitoBorsaMotiviCatalog.Mappa("RED033;GENF231;RED012;RED033", "BS");
            Equal(result.Completa, true, "alias nuovo risolto");
            Equal(result.CodiciSegnalazione, "231;047", "deduplica del numero riservato");
            Equal(result.RichiedeRegistrazione, true, "alias resta da registrare");
        });

        foreach (string code in current.Where(code => code.Length == 3
            && int.TryParse(code, out int number) && number > 200).OrderBy(code => code, StringComparer.Ordinal))
        {
            Check($"identita GENF del catalogo DB {code}", () =>
            {
                var row = EsitoBorsaMotiviCatalog.Risolvi("GENF" + code, "BS");
                Equal(row.Stato, StatoMappaturaMotivo.Verificata, "forzatura esistente");
                Equal(row.CodiceSegnalazione, code, "identita DB");
                Required(row.DescrizioneSegnalazione, "descrizione DB della forzatura");
                var result = EsitoBorsaMotiviCatalog.Mappa("GENF" + code + ";genf" + code, "BS");
                Equal(result.Completa, true, "forzatura ammessa");
                Equal(result.CodiciSegnalazione, code, "deduplica forzatura");
                Equal(result.RichiedeRegistrazione, false, "forzatura gia in DB");
            });
        }
        foreach (var row in rows.Where(row => int.TryParse(row.CodiceSegnalazione, out int number) && number >= 225))
        {
            Check($"alias GENF del codice da registrare {row.CodiceSegnalazione}", () =>
            {
                string benefit = row.Benefici.Split(';')[0];
                var result = EsitoBorsaMotiviCatalog.Mappa("GENF" + row.CodiceSegnalazione, benefit);
                Equal(result.Completa, true, "alias nuovo risolto");
                Equal(result.CodiciSegnalazione, row.CodiceSegnalazione, "numero riservato conservato");
                Equal(result.RichiedeRegistrazione, true, "alias non ancora presente in DB");
                var alias = EsitoBorsaMotiviCatalog.Risolvi("GENF" + row.CodiceSegnalazione, benefit);
                Equal(alias.Stato, StatoMappaturaMotivo.DaRegistrare, "stato alias nuovo");
                Equal(alias.DescrizioneSegnalazione, row.DescrizioneSegnalazione, "stessa causale del numero riservato");
            });
        }
        foreach (string code in new[] { "GENFABC", "GENF200", "GENF211", "GENF217", "GENF999", "GENF1000", "GENF201X", "XGENF201", "ABC201" })
        {
            Check($"forzatura non presente nel catalogo 20262027 {code}", () =>
            {
                var result = EsitoBorsaMotiviCatalog.Mappa(code, "BS");
                Equal(result.Completa, false, "forzatura sconosciuta");
                Equal(result.CodiciSegnalazione, "", "nessuna identita presunta");
                Equal(result.RichiedeRegistrazione, false, "nessuna riserva automatica");
                Contains(result.Diagnostica, code, "codice sconosciuto preservato");
            });
        }

        if (failures.Count > 0)
            throw new InvalidOperationException($"Catalogo motivi: {failures.Count} scenari falliti su {checks}:\n" + string.Join("\n", failures));
        Console.WriteLine($"Superati {checks} scenari: 74 mapping, snapshot DB, nuovi codici da registrare e alias GENF.");

        void Check(string label, Action action)
        {
            checks++;
            try { action(); }
            catch (Exception ex) { failures.Add($"{label}: {ex.GetBaseException().Message}"); }
        }
    }

    private static HashSet<string> Codes(string codes)
        => codes.Split(';', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static void Required(string? actual, string label)
    {
        if (string.IsNullOrWhiteSpace(actual))
            throw new InvalidOperationException($"Manca {label}.");
    }

    private static void Contains(string? actual, string expected, string label)
    {
        if (actual == null || !actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label}: manca {expected} in {actual}.");
    }

    private static void Equal<T>(T actual, T expected, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new InvalidOperationException($"{label}: atteso {expected}, ottenuto {actual}.");
    }
}

