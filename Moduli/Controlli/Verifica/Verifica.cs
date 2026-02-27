using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal sealed class Verifica : BaseProcedure<ArgsVerifica>
    {
        public DataTable OutputVerifica { get; private set; } = new DataTable("Verifica");

        public Verifica(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        public override void RunProcedure(ArgsVerifica args)
        {
            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            string aa = "20252026";

            string folderPath = (args._folderPath ?? "").Trim();

            // 1) Economici
            var procEco = new ProcedureNet7.ProceduraControlloDatiEconomici(_masterForm, CONNECTION)
            {
                ExportToExcel = false // evita file extra, export finale lo fa Verifica
            };

            procEco.RunProcedure(new ArgsProceduraControlloDatiEconomici
            {
                _selectedAA = aa,
                _codiciFiscali = null
            });

            var dtEco = procEco.OutputEconomici;

            // 2) Status sede (solo calcolo)
            var procSede = new ProcedureNet7.ControlloStatusSede(_masterForm, CONNECTION);
            var dtSede = procSede.Compute(aa, includeEsclusi: true, includeNonTrasmesse: true);

            // 3) Merge
            OutputVerifica = MergeEconomiciStatus(dtEco, dtSede);

            // 4) Export unico
            Utilities.ExportDataTableToExcel(OutputVerifica, "D://");

            Logger.LogInfo(100, $"Verifica completata. Record output: {OutputVerifica.Rows.Count}");
        }

        private static DataTable MergeEconomiciStatus(DataTable economici, DataTable status)
        {
            var result = new DataTable("Verifica");

            // 1) Schema: colonne economici
            foreach (DataColumn c in economici.Columns)
                result.Columns.Add(c.ColumnName, c.DataType);

            // 2) Schema: colonne status (evita duplicati; rinomina Motivo)
            var statusColMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn c in status.Columns)
            {
                if (c.ColumnName.Equals("CodFiscale", StringComparison.OrdinalIgnoreCase)) continue;
                if (c.ColumnName.Equals("NumDomanda", StringComparison.OrdinalIgnoreCase)) continue;

                var dst = c.ColumnName.Equals("Motivo", StringComparison.OrdinalIgnoreCase)
                    ? "MotivoStatusSede"
                    : c.ColumnName;

                statusColMap[c.ColumnName] = dst;

                if (!result.Columns.Contains(dst))
                    result.Columns.Add(dst, c.DataType);
            }

            // helpers
            static string S(object? v) => (v == null || v == DBNull.Value) ? "" : v.ToString()!.Trim();
            static string MakeKey(string cf, string numDomanda) => $"{cf}|{numDomanda}";

            // 3) Gruppi per chiave (gestisce anche duplicati senza perdere righe)
            var econByKey = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow er in economici.Rows)
            {
                var cf = economici.Columns.Contains("CodFiscale") ? S(er["CodFiscale"]) : "";
                var nd = economici.Columns.Contains("NumDomanda") ? S(er["NumDomanda"]) : "";
                var key = MakeKey(cf, nd);

                if (!econByKey.TryGetValue(key, out var list))
                    econByKey[key] = list = new List<DataRow>();

                list.Add(er);
            }

            var statusByKey = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow sr in status.Rows)
            {
                var cf = status.Columns.Contains("CodFiscale") ? S(sr["CodFiscale"]) : "";
                var nd = status.Columns.Contains("NumDomanda") ? S(sr["NumDomanda"]) : "";
                var key = MakeKey(cf, nd);

                if (!statusByKey.TryGetValue(key, out var list))
                    statusByKey[key] = list = new List<DataRow>();

                list.Add(sr);
            }

            // 4) Full outer join: unione chiavi
            var allKeys = new HashSet<string>(econByKey.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(statusByKey.Keys);

            foreach (var key in allKeys)
            {
                econByKey.TryGetValue(key, out var eList);
                statusByKey.TryGetValue(key, out var sList);

                eList ??= new List<DataRow>();
                sList ??= new List<DataRow>();

                var n = Math.Max(eList.Count, sList.Count);
                if (n == 0) continue;

                for (int i = 0; i < n; i++)
                {
                    var er = i < eList.Count ? eList[i] : null;
                    var sr = i < sList.Count ? sList[i] : null;

                    var nr = result.NewRow();

                    // Copia economici (se presenti)
                    if (er != null)
                    {
                        foreach (DataColumn c in economici.Columns)
                            nr[c.ColumnName] = er[c] ?? DBNull.Value;
                    }

                    // Copia status (se presenti) + riempi anche CodFiscale/NumDomanda se economici mancanti
                    if (sr != null)
                    {
                        if (result.Columns.Contains("CodFiscale") && (er == null || S(nr["CodFiscale"]) == ""))
                            nr["CodFiscale"] = status.Columns.Contains("CodFiscale") ? (object?)S(sr["CodFiscale"]) ?? "" : "";

                        if (result.Columns.Contains("NumDomanda") && (er == null || S(nr["NumDomanda"]) == ""))
                            nr["NumDomanda"] = status.Columns.Contains("NumDomanda") ? (object?)S(sr["NumDomanda"]) ?? "" : "";

                        foreach (DataColumn sc in status.Columns)
                        {
                            if (sc.ColumnName.Equals("CodFiscale", StringComparison.OrdinalIgnoreCase)) continue;
                            if (sc.ColumnName.Equals("NumDomanda", StringComparison.OrdinalIgnoreCase)) continue;

                            var dst = statusColMap[sc.ColumnName];
                            nr[dst] = sr[sc] ?? DBNull.Value;
                        }
                    }

                    result.Rows.Add(nr);
                }
            }

            return result;
        }

        private static string MakeKey(string cf, string numDomanda) => $"{cf}|{numDomanda}";
    }

}