using System.Text.Json;
using DomicilioAutomation;
using Serbenco.Helpers;

internal static class CorrectionPatchTests
{
    public static void Run()
    {
        var groups = new (string Name, Action Test)[]
        {
            ("normalizzazione estremi conserva tutti gli altri dati e tipi", ReferencePreservesOtherFields),
            ("più correzioni dichiarate restano un singolo payload verificabile", MultipleFields),
            ("la lettura case insensitive mantiene le chiavi originali", KeyCase),
            ("before payload e before field devono coincidere esattamente", BeforeMustMatch),
            ("una correzione non può modificare dati non dichiarati", HiddenChanges),
            ("campi strutturali, entità e operazioni non autorizzati sono rifiutati", Whitelist),
            ("campi assenti, nulli e vuoti non vengono compilati automaticamente", NoAutofill),
            ("JSON malformati o chiavi duplicate non sono applicabili", InvalidJson),
            ("fonti e campi duplicati non sono ammissibili", SourcesAndDuplicates),
            ("le date richiedono formato ISO e una variazione effettiva", Dates),
            ("i riferimenti rispettano i limiti dei payload backend", ReferenceLengths),
            ("il protocollo mantiene correzioni, fonti e valori senza perdite", WireRoundtrip),
            ("i guard del servizio falliscono prima di accedere a qualsiasi contesto", ServiceInputGuards)
        };
        foreach (var group in groups) { group.Test(); Console.WriteLine("PASS: " + group.Name); }
        Console.WriteLine($"PASS: {groups.Length} gruppi correzioni automatiche; nessuna connessione o scrittura SQL eseguita.");
    }

    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Rejected(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Una correzione fuori ambito è stata accettata.");
    }
    private static DomicilioAutomationFieldCorrection Field(string name = "numeroSerieContratto", string before = "Serie 3T 123", string after = "TJR25T000123000GG")
        => new(name, before, after, 2, 2001m, "Codice identificativo del contratto");
    private static DomicilioAutomationOperationCorrection Patch(string before, string after, params DomicilioAutomationFieldCorrection[] fields)
        => new(25, before, after, fields.ToList());
    private static string Apply(DomicilioAutomationOperationCorrection correction, int entity = 2, int operation = 2, string? current = null)
        => DomicilioAutomationCorrectionPatch.Apply(current ?? correction.BeforePayload, correction, entity, operation);

    private static void ReferencePreservesOtherFields()
    {
        const string before = "{\"numeroSerieContratto\":\"Serie 3T 123\",\"tipoContrattoTitoloOneroso\":0,\"addSubentro\":false,\"tipoEnte\":null,\"nested\":{\"keep\":[1,true,null,\"x\"]}}";
        var after = before.Replace("Serie 3T 123", "TJR25T000123000GG");
        Assert(Apply(Patch(before, after, Field())) == after, "Il risultato deve essere esattamente il payload proposto verificato.");
        using var parsed = JsonDocument.Parse(after);
        Assert(parsed.RootElement.GetProperty("tipoContrattoTitoloOneroso").ValueKind == JsonValueKind.Number &&
            parsed.RootElement.GetProperty("addSubentro").ValueKind == JsonValueKind.False &&
            parsed.RootElement.GetProperty("tipoEnte").ValueKind == JsonValueKind.Null, "Enum, flag e null originali devono rimanere invariati.");
    }
    private static void MultipleFields()
    {
        const string before = "{\"numeroSerieContratto\":\"Serie 3T 123\",\"dataFineContratto\":\"2027-08-30T00:00:00\",\"addSubentro\":false}";
        var after = before.Replace("Serie 3T 123", "TJR25T000123000GG").Replace("2027-08-30T00:00:00", "2027-08-31");
        Assert(Apply(Patch(before, after, Field(), Field("dataFineContratto", "2027-08-30T00:00:00", "2027-08-31"))) == after,
            "La correzione deve ammettere tutti e soli i campi dichiarati insieme.");
        Assert(Apply(Patch("{\"numeroSerieProroga\":\"12345\"}", "{\"numeroSerieProroga\":\"26070911540515489\"}",
            Field("numeroSerieProroga", "12345", "26070911540515489")), 3) == "{\"numeroSerieProroga\":\"26070911540515489\"}", "Gli estremi della proroga devono usare la propria whitelist.");
    }
    private static void KeyCase()
    {
        const string before = "{\"NumeroSerieContratto\":\"Serie 3T 123\"}";
        var after = before.Replace("Serie 3T 123", "TJR25T000123000GG");
        Apply(Patch(before, after, Field("NUMEROSERIECONTRATTO")));
        Rejected(() => Apply(Patch(before, after.Replace("NumeroSerieContratto", "numeroSerieContratto"), Field())));
    }
    private static void BeforeMustMatch()
    {
        const string before = "{\"numeroSerieContratto\":\"Serie 3T 123\"}";
        var patch = Patch(before, before.Replace("Serie 3T 123", "TJR25T000123000GG"), Field());
        Rejected(() => Apply(patch, current: before + " "));
        Rejected(() => Apply(patch with { Fields = new() { Field(before: "SERIE 3T 123") } }));
        Rejected(() => Apply(patch with { Fields = new() { Field(after: "TJR25T999999000GG") } }));
    }
    private static void HiddenChanges()
    {
        const string before = "{\"numeroSerieContratto\":\"Serie 3T 123\",\"tipoContrattoTitoloOneroso\":0,\"addSubentro\":false,\"nested\":{\"keep\":1}}";
        var corrected = before.Replace("Serie 3T 123", "TJR25T000123000GG");
        foreach (var after in new[] { corrected.Replace(":0", ":1"), corrected.Replace(":0", ":\"0\""), corrected.Replace(":0", ":0.0"),
            corrected.Replace(":false", ":true"), corrected.Replace("\"keep\":1", "\"keep\":2"), corrected.Replace("\"nested\"", "\"extra\"") })
            Rejected(() => Apply(Patch(before, after, Field())));
    }
    private static void Whitelist()
    {
        foreach (var name in new[] { "codFiscale", "tipoContrattoTitoloOneroso", "idAllegatoContratto", "dataCessazione", "addSubentro", "indirizzoDomicilio" })
        {
            var before = JsonSerializer.Serialize(new Dictionary<string,string> { [name] = "before" });
            var after = JsonSerializer.Serialize(new Dictionary<string,string> { [name] = "after" });
            Rejected(() => Apply(Patch(before, after, Field(name, "before", "after"))));
        }
        var patch = Patch("{\"numeroSerieContratto\":\"Serie 3T 123\"}", "{\"numeroSerieContratto\":\"TJR25T000123000GG\"}", Field());
        foreach (var entity in new[] { 0, 1, 3, 4 }) Rejected(() => Apply(patch, entity));
        foreach (var operation in new[] { 0, 3, 4 }) Rejected(() => Apply(patch, operation: operation));
    }
    private static void NoAutofill()
    {
        Rejected(() => Apply(Patch("{}", "{\"numeroSerieContratto\":\"TJR25T000123000GG\"}", Field(before: ""))));
        Rejected(() => Apply(Patch("{\"numeroSerieContratto\":null}", "{\"numeroSerieContratto\":\"TJR25T000123000GG\"}", Field(before: ""))));
        Rejected(() => Apply(Patch("{\"numeroSerieContratto\":\"\"}", "{\"numeroSerieContratto\":\"TJR25T000123000GG\"}", Field(before: ""))));
        Rejected(() => Apply(Patch("{\"numeroSerieContratto\":12345}", "{\"numeroSerieContratto\":\"TJR25T000123000GG\"}", Field(before: "12345"))));
    }
    private static void InvalidJson()
    {
        foreach (var json in new[] { "{", "null", "[]", "{\"numeroSerieContratto\":\"Serie 3T 123\",\"NumeroSerieContratto\":\"different\"}",
            "{\"numeroSerieContratto\":\"Serie 3T 123\",\"extra\":{\"a\":1,\"A\":2}}" })
            Rejected(() => Apply(Patch(json, json.Replace("Serie 3T 123", "TJR25T000123000GG"), Field())));
    }
    private static void SourcesAndDuplicates()
    {
        const string before = "{\"numeroSerieContratto\":\"Serie 3T 123\"}";
        var after = before.Replace("Serie 3T 123", "TJR25T000123000GG");
        foreach (var field in new[] { Field() with { Page = 0 }, Field() with { AttachmentId = 0 }, Field() with { Label = " " } })
            Rejected(() => Apply(Patch(before, after, field)));
        Rejected(() => Apply(Patch(before, after, Field(), Field("NUMEROSERIECONTRATTO"))));
        Rejected(() => Apply(Patch(before, after)));
        Rejected(() => Apply(Patch(before, before, Field(after: "Serie 3T 123"))));
    }
    private static void Dates()
    {
        const string before = "{\"dataFineContratto\":\"2027-08-30T00:00:00\"}";
        foreach (var afterDate in new[] { "31/08/2027", "2027-08-31T00:00:00", "2027-02-30", "2027-08-30", "1800-08-31" })
        {
            var after = JsonSerializer.Serialize(new { dataFineContratto = afterDate });
            Rejected(() => Apply(Patch(before, after, Field("dataFineContratto", "2027-08-30T00:00:00", afterDate))));
        }
        var timed = before.Replace("T00:00:00", "T12:00:00");
        Rejected(() => Apply(Patch(timed, "{\"dataFineContratto\":\"2027-08-31\"}", Field("dataFineContratto", "2027-08-30T12:00:00", "2027-08-31"))));
    }
    private static void ReferenceLengths()
    {
        foreach (var sample in new[] { (2,"numeroSerieContratto",101), (3,"numeroSerieProroga",51) })
        {
            var before = JsonSerializer.Serialize(new Dictionary<string,string> { [sample.Item2] = "12345" });
            var value = new string('A', sample.Item3);
            var after = JsonSerializer.Serialize(new Dictionary<string,string> { [sample.Item2] = value });
            Rejected(() => Apply(Patch(before, after, Field(sample.Item2,"12345",value)), sample.Item1));
        }
    }
    private static void WireRoundtrip()
    {
        var correction = Patch("{\"numeroSerieContratto\":\"Serie 3T 123\"}", "{\"numeroSerieContratto\":\"TJR25T000123000GG\"}", Field());
        var request = new DomicilioAutomationRequest(99,new string('A',64)) { Corrections = new() { correction } };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var copy = JsonSerializer.Deserialize<DomicilioAutomationRequest>(JsonSerializer.Serialize(request,options),options)!;
        Assert(copy.Corrections.Count==1 && copy.Corrections[0].Fields[0]==correction.Fields[0] && Apply(copy.Corrections[0])==correction.AfterPayload,
            "Il protocollo worker deve preservare la correzione e la prova prima/dopo.");
    }
    private static void ServiceInputGuards()
    {
        var service = new DomicilioLavorazioneService(null!,null!,null!);
        void Refused(Func<Task> call)
        {
            try { call().GetAwaiter().GetResult(); }
            catch (IstanzaLavorazioneException) { return; }
            throw new Exception("Il guard di input non ha fermato il servizio prima dei contesti null.");
        }
        string Capture(System.Data.Common.DbConnection c,System.Data.Common.DbTransaction? t,int id) => throw new Exception("Nessuna lettura SQL consentita nei test locali.");
        Refused(() => service.AcceptAutomaticallyAsync(0,new string('A',64),Capture));
        Refused(() => service.AcceptAutomaticallyAsync(1,new string('A',64)+"\n",Capture));
        var patch=Patch("{}","{}");
        Refused(() => service.AcceptAutomaticallyAsync(1,new string('A',64),Capture,new[] {patch,patch}));
        Refused(() => service.AcceptAutomaticallyAsync(1,new string('A',64),Capture,new[] {patch}));
        Refused(() => service.AcceptAutomaticallyAsync(1,new string('A',64),Capture,new[] {patch with { Fields = null! }}));
    }
}
