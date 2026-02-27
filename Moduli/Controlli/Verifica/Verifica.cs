using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    /// <summary>
    /// Verifica aggregata: usa i risultati object-first dei singoli controlli,
    /// mantenendo i DataTable solo per export/debug.
    /// </summary>
    internal sealed class Verifica : BaseProcedure<ArgsVerifica>
    {
        public Verifica(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        private string _aa = "";
        private string _folderPath = "";

        public IReadOnlyList<ValutazioneVerifica> OutputVerificaList { get; private set; } = Array.Empty<ValutazioneVerifica>();

        public DataTable OutputVerifica { get; private set; } = BuildOutputTable();

        public override void RunProcedure(ArgsVerifica args)
        {
            _aa = "20252026";
            _folderPath = "D://";

            if (CONNECTION == null) throw new InvalidOperationException("CONNECTION null");

            // 1) Controllo ISEE / Economici
            var controlloEconomici = new ProceduraControlloDatiEconomici(_masterForm, CONNECTION)
            {
                ExportToExcel = false
            };

            controlloEconomici.RunProcedure(new ArgsProceduraControlloDatiEconomici
            {
                _selectedAA = _aa,
                _codiciFiscali = null
            });

            var economiciList = controlloEconomici.OutputEconomiciList;

            // Dizionario per chiave (CF + NumDomanda) da passare al controllo status sede.
            var economiciByKey = economiciList
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.First());

            // 2) Controllo Status Sede (usa alcuni dati economici nella valutazione)
            var controlloStatusSede = new ControlloStatusSede(_masterForm, CONNECTION);
            var statusSedeList = controlloStatusSede.ComputeList(
                _aa,
                includeEsclusi: true,
                includeNonTrasmesse: true,
                iseeByKey: economiciByKey);

            // 3) Merge object-first per uso in altre strutture
            OutputVerificaList = MergeLists(economiciList, statusSedeList);

            // 4) DataTable solo per export/debug
            OutputVerifica = ToDataTable(OutputVerificaList);

            Utilities.ExportDataTableToExcel(OutputVerifica, _folderPath);
        }

        private static List<ValutazioneVerifica> MergeLists(
            IReadOnlyList<ValutazioneEconomici> economici,
            IReadOnlyList<ValutazioneStatusSede> statusSede)
        {
            var econByKey = economici
                .GroupBy(e => e.Key)
                .ToDictionary(g => g.Key, g => g.First());

            var sedeByKey = statusSede
                .GroupBy(s => s.Key)
                .ToDictionary(g => g.Key, g => g.First());

            var allKeys = new HashSet<StudentKey>(econByKey.Keys);
            allKeys.UnionWith(sedeByKey.Keys);

            var merged = new List<ValutazioneVerifica>(allKeys.Count);

            foreach (var key in allKeys.OrderBy(k => k.CodFiscale).ThenBy(k => k.NumDomanda))
            {
                econByKey.TryGetValue(key, out var eco);
                sedeByKey.TryGetValue(key, out var sede);

                var info = sede?.Info ?? eco?.Info ?? new StudenteInfo();
                info.InformazioniPersonali.CodFiscale = key.CodFiscale;
                info.InformazioniPersonali.NumDomanda = key.NumDomanda;

                if (sede != null)
                    info.InformazioniSede.StatusSedeSuggerito = sede.StatoSuggerito;

                merged.Add(new ValutazioneVerifica
                {
                    Info = info,
                    Economici = eco,
                    StatusSede = sede
                });
            }

            return merged;
        }

        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable("Verifica");

            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));

            // Economici / ISEE
            dt.Columns.Add("TipoRedditoOrigine", typeof(string));
            dt.Columns.Add("TipoRedditoIntegrazione", typeof(string));
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));

            dt.Columns.Add("ISR", typeof(decimal));
            dt.Columns.Add("ISP", typeof(decimal));
            dt.Columns.Add("Detrazioni", typeof(decimal));

            dt.Columns.Add("ISEDSU", typeof(decimal));
            dt.Columns.Add("ISEEDSU", typeof(decimal));
            dt.Columns.Add("ISPEDSU", typeof(decimal));

            dt.Columns.Add("ISPDSU", typeof(decimal));
            dt.Columns.Add("SEQ", typeof(decimal));

            dt.Columns.Add("ISEDSU_Attuale", typeof(decimal));
            dt.Columns.Add("ISEEDSU_Attuale", typeof(decimal));
            dt.Columns.Add("ISPEDSU_Attuale", typeof(decimal));
            dt.Columns.Add("ISPDSU_Attuale", typeof(decimal));
            dt.Columns.Add("SEQ_Attuale", typeof(decimal));

            // Status sede
            dt.Columns.Add("StatusSedeAttuale", typeof(string));
            dt.Columns.Add("StatusSedeSuggerito", typeof(string));
            dt.Columns.Add("MotivoStatusSede", typeof(string));

            dt.Columns.Add("ComuneResidenza", typeof(string));
            dt.Columns.Add("ProvinciaResidenza", typeof(string));

            dt.Columns.Add("ComuneSedeStudi", typeof(string));
            dt.Columns.Add("ProvinciaSede", typeof(string));

            dt.Columns.Add("ComuneDomicilio", typeof(string));

            dt.Columns.Add("DomicilioPresente", typeof(bool));
            dt.Columns.Add("DomicilioValido", typeof(bool));
            dt.Columns.Add("HasAlloggio12", typeof(bool));

            return dt;
        }

        private static DataTable ToDataTable(IReadOnlyList<ValutazioneVerifica> list)
        {
            var dt = BuildOutputTable();

            foreach (var v in list)
            {
                var info = v.Info;
                var eco = v.Economici;
                var sede = v.StatusSede;

                var row = dt.NewRow();

                row["CodFiscale"] = info.InformazioniPersonali.CodFiscale;
                row["NumDomanda"] = info.InformazioniPersonali.NumDomanda;

                if (eco != null)
                {
                    row["TipoRedditoOrigine"] = eco.TipoRedditoOrigine;
                    row["TipoRedditoIntegrazione"] = eco.TipoRedditoIntegrazione;
                    row["CodTipoEsitoBS"] = eco.CodTipoEsitoBS ?? 0;

                    row["ISR"] = eco.ISR;
                    row["ISP"] = eco.ISP;
                    row["Detrazioni"] = eco.Detrazioni;

                    row["ISEDSU"] = eco.ISEDSU;
                    row["ISEEDSU"] = eco.ISEEDSU;
                    row["ISPEDSU"] = eco.ISPEDSU;

                    row["ISPDSU"] = eco.ISPDSU;
                    row["SEQ"] = eco.SEQ;

                    row["ISEDSU_Attuale"] = eco.ISEDSU_Attuale ?? 0m;
                    row["ISEEDSU_Attuale"] = eco.ISEEDSU_Attuale ?? 0m;
                    row["ISPEDSU_Attuale"] = eco.ISPEDSU_Attuale ?? 0m;
                    row["ISPDSU_Attuale"] = eco.ISPDSU_Attuale ?? 0m;
                    row["SEQ_Attuale"] = eco.SEQ_Attuale ?? 0m;
                }

                if (sede != null)
                {
                    row["StatusSedeAttuale"] = info.InformazioniSede.StatusSede;
                    row["StatusSedeSuggerito"] = sede.StatoSuggerito;
                    row["MotivoStatusSede"] = sede.Motivo;

                    row["ComuneResidenza"] = info.InformazioniSede.Residenza.codComune;
                    row["ProvinciaResidenza"] = info.InformazioniSede.Residenza.provincia;

                    row["ComuneSedeStudi"] = info.InformazioniIscrizione.ComuneSedeStudi;
                    row["ProvinciaSede"] = info.InformazioniIscrizione.ProvinciaSedeStudi;

                    row["ComuneDomicilio"] = info.InformazioniSede.Domicilio.codComuneDomicilio;

                    row["DomicilioPresente"] = sede.DomicilioPresente;
                    row["DomicilioValido"] = sede.DomicilioValido;
                    row["HasAlloggio12"] = sede.HasAlloggio12;
                }

                dt.Rows.Add(row);
            }

            return dt;
        }
    }
}
