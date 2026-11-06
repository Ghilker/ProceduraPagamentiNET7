using System.Data;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica
    {
        // Mappatura completa basata sul catalogo DB 20262027 letto il 2026-11-02.
        // I nuovi codici assegnati restano distinguibili finché non registrati.
        internal static DataTable BuildCatalogoEsclusioniTable()
        {
            var table = new DataTable("Catalogo segnalazioni Verifica");
            table.Columns.Add("Codice regola", typeof(string));
            table.Columns.Add("Descrizione regola", typeof(string));
            table.Columns.Add("Benefici", typeof(string));
            table.Columns.Add("Codice segnalazione", typeof(string));
            table.Columns.Add("Descrizione segnalazione", typeof(string));
            table.Columns.Add("Stato", typeof(string));
            table.Columns.Add("Fonte", typeof(string));
            table.Columns.Add("Nota", typeof(string));

            foreach (var voce in EsitoBorsaMotiviCatalog.Voci)
            {
                table.Rows.Add(
                    voce.CodiceRegola,
                    voce.DescrizioneRegola,
                    voce.Benefici,
                    voce.CodiceSegnalazione ?? string.Empty,
                    voce.DescrizioneSegnalazione ?? string.Empty,
                    voce.Stato.ToString(),
                    voce.Fonte,
                    voce.Nota);
            }

            return table;
        }
    }
}
