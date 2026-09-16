using System.Collections.Concurrent;

namespace ProcedureNet7;

public partial class ProceduraPagamenti
{
    private readonly ConcurrentDictionary<string, ImportiAuditPagamento> _importiAudit =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MotivoEsclusionePagamento> _motiviNonFlusso =
        new(StringComparer.OrdinalIgnoreCase);

    private void ExportAuditPagamento(IEnumerable<StudentePagamenti> studenti,
        ContestoEsclusioniPagamento contesto, string folder)
    {
        try
        {
            using var dati = BuildFullAuditDataTable(studenti, impegno: null);
            using var workbook = PagamentoAuditWorkbook.Create(
                dati, _esclusioniPagamento, _flussoFilesByCF, _importiAudit, contesto, _motiviNonFlusso);
            workbook.SaveAs(Path.Combine(folder, "AUDIT_Pagamento_Completo.xlsx"));
            Logger.LogInfo(20, $"Esportato audit dell'intero pagamento: {dati.Rows.Count} studenti, inclusi gli esclusi.");
        }
        catch (Exception ex)
        {
            // As with the per-impegno audit, report export errors without replacing
            // a processing/export exception already in flight or changing the payment outcome.
            Logger.LogError(null, $"Impossibile esportare l'audit completo del pagamento: {ex.Message}");
        }
    }
}
