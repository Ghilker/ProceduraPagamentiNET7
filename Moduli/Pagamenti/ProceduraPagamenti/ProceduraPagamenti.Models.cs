using DocumentFormat.OpenXml;
using ProcedureNet7.PagamentiProcessor;
using ProcedureNet7.Storni;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class ProceduraPagamenti
    {
        private sealed class PagamentoConfig
        {
            public string CodBeneficio { get; init; } = string.Empty;
            public string DescrBeneficio { get; init; } = string.Empty;
            public string CodTipoPagam { get; init; } = string.Empty;
            public string CategoriaPagam { get; init; } = string.Empty;
            public string DescrTipo { get; init; } = string.Empty;
            public string DescrCategoria { get; init; } = string.Empty;
            public string DescrPagamento { get; init; } = string.Empty;
        }
    }

    // Sums collected only from ProcessCategory calls actually executed
    sealed class DeterminaAccumulator
    {
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, Totals>>> _map
            = new(StringComparer.Ordinal);

        private struct Totals
        {
            public int Count;
            public double Lordo;
            public double PA;
            public double Netto;
        }

        public void Reset() => _map.Clear();

        // ===== API: now requires impegnoId =====
        public void AddWithoutPA(string impegnoId, string categoriaCU, IEnumerable<StudentePagamenti> students)
            => AddInternal(impegnoId, categoriaCU, "Senza detrazioni", students, withPA: false, categoriaPagam: null);

        public void AddWithPA(string impegnoId, string categoriaCU, IEnumerable<StudentePagamenti> students, string categoriaPagam)
        {
            var list = students?.ToList() ?? new List<StudentePagamenti>();
            if (list.Count == 0) return;

            // Group once by normalized ente bucket: Cassino / Viterbo / Roma
            var byBucket = list.GroupBy(s => EnteBucket(s?.InformazioniIscrizione?.CodEnte))
                               .ToDictionary(g => g.Key, g => (IEnumerable<StudentePagamenti>)g);

            if (byBucket.TryGetValue("Cassino", out var cassino) && cassino.Any())
                AddInternal(impegnoId, categoriaCU, "Cassino", cassino, withPA: true, categoriaPagam);

            if (byBucket.TryGetValue("Viterbo", out var viterbo) && viterbo.Any())
                AddInternal(impegnoId, categoriaCU, "Viterbo", viterbo, withPA: true, categoriaPagam);

            if (byBucket.TryGetValue("Roma", out var roma) && roma.Any())
                AddInternal(impegnoId, categoriaCU, "Roma", roma, withPA: true, categoriaPagam);
        }

        // Normalizes CodEnte into one of the three buckets we use everywhere else
        private static string EnteBucket(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "Roma";

            var s = raw.Trim();

            // If it's numeric like "2" or "05", normalize to 2 digits
            if (int.TryParse(s, out var n))
                s = n.ToString("00");

            var u = s.ToUpperInvariant();

            if (u == "02" || u.Contains("CASS")) return "Cassino";
            if (u == "05" || u.Contains("VIT")) return "Viterbo";

            return "Roma";
        }


        private void AddInternal(string impegnoId, string catCU, string group, IEnumerable<StudentePagamenti> students, bool withPA, string? categoriaPagam)
        {
            var chunk = students?.ToList() ?? new List<StudentePagamenti>();
            if (chunk.Count == 0) return;

            if (!_map.TryGetValue(impegnoId, out var perCU))
            {
                perCU = new(StringComparer.Ordinal);
                _map[impegnoId] = perCU;
            }
            if (!perCU.TryGetValue(catCU, out var perGroup))
            {
                perGroup = new(StringComparer.Ordinal);
                perCU[catCU] = perGroup;
            }
            if (!perGroup.TryGetValue(group, out var t))
                t = new Totals();

            // Deduplicate conservatively by CodFiscale
            var uniq = chunk
                .GroupBy(s => s.InformazioniPersonali.CodFiscale)
                .Select(g => g.First())
                .ToList();

            t.Count += uniq.Count;
            t.Lordo += uniq.Sum(s => s.InformazioniPagamento.ImportoDaPagareLordo);
            t.Netto += uniq.Sum(s => s.InformazioniPagamento.ImportoDaPagare);

            if (withPA)
            {
                t.PA += uniq.Sum(s => (categoriaPagam == "PR")
                    ? s.InformazioniPagamento.ImportoAccontoPA
                    : s.InformazioniPagamento.ImportoSaldoPA);
            }

            perGroup[group] = t;
        }

        public (int count, double lordo, double pa, double netto) TotalsAll()
        {
            int c = 0; double l = 0, p = 0, n = 0;
            foreach (var perCU in _map.Values)
                foreach (var perGroup in perCU.Values)
                    foreach (var t in perGroup.Values)
                    { c += t.Count; l += t.Lordo; p += t.PA; n += t.Netto; }
            return (c, l, p, n);
        }

        public DataTable ToDataTable()
        {
            var it = new CultureInfo("it-IT");

            var dt = new DataTable();
            dt.Columns.Add("IMPEGNO");
            dt.Columns.Add("DESCRIZIONE");
            dt.Columns.Add("N. STUDENTI", typeof(int));
            dt.Columns.Add("TOTALE LORDO");
            dt.Columns.Add("TOTALE PA");
            dt.Columns.Add("TOTALE NETTO");

            foreach (var impegno in _map.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var perCU = _map[impegno];

                foreach (var cat in perCU.Keys.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var groups = perCU[cat];

                    // helpers scoped to this loop
                    Totals SumOf(Dictionary<string, Totals> g, string? onlyGroup = null, string? excludeGroup = null)
                    {
                        var acc = new Totals();
                        foreach (var kv in g)
                        {
                            var name = kv.Key;
                            if (onlyGroup != null && !StringComparer.Ordinal.Equals(name, onlyGroup)) continue;
                            if (excludeGroup != null && StringComparer.Ordinal.Equals(name, excludeGroup)) continue;

                            var t = kv.Value;
                            acc.Count += t.Count;
                            acc.Lordo += t.Lordo;
                            acc.PA += t.PA;
                            acc.Netto += t.Netto;
                        }
                        return acc;
                    }

                    Totals SumGroup(string groupName) =>
                        groups.TryGetValue(groupName, out var t) ? t : default;

                    switch (cat)
                    {
                        case "111":
                            {
                                // --- no PA (single row) ---
                                var noPA = SumOf(groups, onlyGroup: "Senza detrazioni");
                                if (noPA.Count > 0)
                                    AddRow(dt, impegno, "TOTALE SENZA MENSA", noPA, it);

                                // --- with PA (split per ente: Cassino, Viterbo, Roma) ---
                                foreach (var grp in new[] { "Cassino", "Viterbo", "Roma" })
                                {
                                    var t = SumGroup(grp);
                                    if (t.Count <= 0) continue;

                                    var grpLabel = grp.ToLowerInvariant(); // roma, cassino, viterbo
                                    var descr = $"TOTALE SENZA MENSA CON PA ({grpLabel})";
                                    AddRow(dt, impegno, descr, t, it);
                                }
                                break;
                            }

                        case "211":
                            {
                                // They don't have PA
                                var noPA = SumOf(groups, onlyGroup: "Senza detrazioni");
                                if (noPA.Count > 0)
                                    AddRow(dt, impegno, "TOTALE CON MENSA", noPA, it);
                                break;
                            }

                        case "311":
                            {
                                // Spec unchanged: aggregate all with-PA groups into one line
                                var withPA = SumOf(groups, excludeGroup: "Senza detrazioni");
                                if (withPA.Count > 0)
                                    AddRow(dt, impegno, "TOTALE CON MENSA CON PA", withPA, it);
                                break;
                            }

                        default:
                            {
                                // Fallback behaviour
                                var noPA = SumOf(groups, onlyGroup: "Senza detrazioni");
                                var withPA = SumOf(groups, excludeGroup: "Senza detrazioni");
                                if (noPA.Count > 0)
                                    AddRow(dt, impegno, $"CATEGORIA {cat} (senza PA)", noPA, it);
                                // Also split with-PA per-group for visibility
                                foreach (var grp in new[] { "Cassino", "Viterbo", "Roma" })
                                {
                                    var t = SumGroup(grp);
                                    if (t.Count <= 0) continue;
                                    AddRow(dt, impegno, $"CATEGORIA {cat} CON PA ({grp.ToLowerInvariant()} – {Fmt(t.PA, it)})", t, it);
                                }
                                break;
                            }
                    }
                }

                // --- Subtotal per impegno ---
                var (ic, il, ip, inetto) = TotalsForImpegno(impegno);
                if (ic > 0)
                    dt.Rows.Add(impegno, "SUBTOTALE IMPEGNO", ic, Fmt(il, it), Fmt(ip, it), Fmt(inetto, it));
            }

            // --- Grand total ---
            var (c, l, p, n) = TotalsAll();
            if (c > 0)
                dt.Rows.Add("", "TOTALE GENERALE", c, Fmt(l, it), Fmt(p, it), Fmt(n, it));

            return dt;

            // ===== helpers (static) =====
            (int c, double l, double p, double n) TotalsForImpegno(string impegnoId)
            {
                int c = 0; double l = 0, p = 0, n = 0;
                var perCU = _map[impegnoId];
                foreach (var perGroup in perCU.Values)
                    foreach (var t in perGroup.Values)
                    { c += t.Count; l += t.Lordo; p += t.PA; n += t.Netto; }
                return (c, l, p, n);
            }

            static void AddRow(DataTable dt, string impegno, string descr, Totals t, CultureInfo it)
            {
                dt.Rows.Add(impegno, descr, t.Count, Fmt(t.Lordo, it), Fmt(t.PA, it), Fmt(t.Netto, it));
            }

            static string Fmt(double v, CultureInfo it) => "€ " + v.ToString("N2", it);
        }

    }

    sealed class PaymentCount
    {
        public string CodTipoPagam { get; init; } = string.Empty;
        public string CategoriaPagam { get; init; } = string.Empty;
        public string DescrPagamento { get; init; } = string.Empty;
        public int Studenti { get; set; }
    }
}
