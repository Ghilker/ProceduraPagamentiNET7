using System.Data.SqlClient;
using System.Globalization;

namespace ProcedureNet7;

public partial class ProceduraPagamenti
{
    private readonly List<StudenteEsclusoPagamento> _esclusioniPagamento = new();
    private readonly Dictionary<string, string> _descrizioniPagamenti = new(StringComparer.OrdinalIgnoreCase);

    private void ProcessStudentList()
    {
        _esclusioniPagamento.Clear();
        _descrizioniPagamenti.Clear();
        studentiConErroriPA.Clear();
        _flussoWrittenCF.Clear();
        _flussoFilesByCF.Clear();
        _importiAudit.Clear();
        _motiviNonFlusso.Clear();
        // Keep every selected student, including those removed by later checks.
        // These objects retain the information acquired up to their exclusion.
        var studentiAudit = studentiDaPagare.Values.ToList();

        // Resolve output context while the transaction is usable, also for a partial report on failure.
        string folder = GetCurrentPagamentoFolder();
        using (var cmd = new SqlCommand("SELECT Cod_tipo_pagam, Descrizione FROM Tipologie_pagam", CONNECTION, sqlTransaction))
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
                _descrizioniPagamenti[Utilities.SafeGetString(reader, "Cod_tipo_pagam").Trim()] =
                    Utilities.SafeGetString(reader, "Descrizione").Trim();

        var contesto = new ContestoEsclusioniPagamento(
            selectedAA.Length == 8 ? $"{selectedAA[..4]}/{selectedAA[4..]}" : selectedAA,
            DescrizioneBeneficio(isTR ? "TR" : tipoBeneficio), DescrizionePagamento(codTipoPagamento),
            codTipoPagamento, categoriaPagam, selectedDataRiferimento, DateTime.Now, false);
        bool completata = false;
        try
        {
            ProcessStudentListCore();
            completata = true;
        }
        finally
        {
            try
            {
                using var workbook = PagamentoEsclusioniWorkbook.Create(_esclusioniPagamento, contesto with { Completata = completata });
                workbook.SaveAs(Path.Combine(folder, "Studenti esclusi dall'elaborazione.xlsx"));
            }
            catch (Exception ex) when (!completata)
            {
                // Preserve the original processing exception.
                Logger.LogError(null, $"Impossibile esportare le esclusioni parziali: {ex.Message}");
            }
            finally
            {
                // Attempt the audit even if the exclusions workbook could not be saved.
                ExportAuditPagamento(studentiAudit, contesto with { Completata = completata }, folder);
            }
        }
    }

    private void ExportStudentiRimossi(IEnumerable<string> codFiscali, string fase, MotivoEsclusionePagamento causa)
        => ExportStudentiRimossi(codFiscali.Select(cf => (CodFiscale: cf, Motivazione: causa)), fase);

    private void ExportStudentiRimossi(IEnumerable<(string CodFiscale, MotivoEsclusionePagamento Motivazione)> studenti, string fase)
    {
        foreach (var item in studenti)
        {
            if (!studentiDaPagare.TryGetValue(item.CodFiscale, out var studente)) continue;
            _esclusioniPagamento.Add(new StudenteEsclusoPagamento(
                item.CodFiscale, studente.InformazioniPersonali.NumDomanda,
                studente.InformazioniPersonali.Cognome, studente.InformazioniPersonali.Nome,
                studente.InformazioniIscrizione.CodEnte, studente.InformazioniPagamento.NumeroImpegno,
                fase, item.Motivazione));
        }
    }

    private static MotivoEsclusionePagamento Esclusione(string motivo, string dettaglio, double? importo = null)
        => new(motivo, dettaglio, importo);

    private static string Euro(double amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("it-IT"));

    private string DescrizionePagamento(string codice)
        => _descrizioniPagamenti.TryGetValue(codice, out var descrizione) && !string.IsNullOrWhiteSpace(descrizione)
            ? descrizione : $"Pagamento {codice} (descrizione non disponibile)";

    private static string DescrizioneBeneficio(string codice) => codice.ToUpperInvariant() switch
    {
        "BS" => "Borsa di studio", "TR" => "Rimborso tassa regionale", "PL" => "Premio di laurea",
        "BL" => "Buono libro", "CS" => "Contributo straordinario", _ => $"Beneficio {codice}"
    };
}
