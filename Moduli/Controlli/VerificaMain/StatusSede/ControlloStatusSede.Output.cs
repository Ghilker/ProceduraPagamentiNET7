using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class ControlloStatusSede
    {
        private static ValutazioneStatusSede CreateResult(StatusSedeStudent row, StatusSedeDecision decision)
        {
            return new ValutazioneStatusSede
            {
                Info = row.Info,
                StatoSuggerito = decision.SuggestedStatus,
                Motivo = decision.Reason,
                DomicilioPresente = decision.DomicilioPresente,
                DomicilioValido = decision.DomicilioValido,
                HasAlloggio12 = row.HasAlloggio12,

                HasIstanzaDomicilio = row.HasIstanzaDomicilio,
                CodTipoIstanzaDomicilio = row.CodTipoIstanzaDomicilio,
                NumIstanzaDomicilio = row.NumIstanzaDomicilio,

                HasUltimaIstanzaChiusaDomicilio = row.HasUltimaIstanzaChiusaDomicilio,
                CodTipoUltimaIstanzaChiusaDomicilio = row.CodTipoUltimaIstanzaChiusaDomicilio,
                NumUltimaIstanzaChiusaDomicilio = row.NumUltimaIstanzaChiusaDomicilio,
                EsitoUltimaIstanzaChiusaDomicilio = row.EsitoUltimaIstanzaChiusaDomicilio,
                UtentePresaCaricoUltimaIstanzaChiusaDomicilio = row.UtentePresaCaricoUltimaIstanzaChiusaDomicilio
            };
        }

        private static DataTable ToDataTable(IReadOnlyList<ValutazioneStatusSede> items)
        {
            var dt = BuildOutputTable();

            foreach (var v in items)
            {
                var info = v.Info;
                var dom = info.InformazioniSede.Domicilio;

                var r = dt.NewRow();
                r["CodFiscale"] = info.InformazioniPersonali.CodFiscale;
                r["NumDomanda"] = info.InformazioniPersonali.NumDomanda;

                r["StatusSedeAttuale"] = info.InformazioniSede.StatusSede;
                r["StatusSedeSuggerito"] = v.StatoSuggerito;
                r["Motivo"] = v.Motivo;

                r["ComuneResidenza"] = info.InformazioniSede.Residenza.codComune;
                r["ProvinciaResidenza"] = info.InformazioniSede.Residenza.provincia;

                r["ComuneSedeStudi"] = info.InformazioniIscrizione.ComuneSedeStudi;
                r["ProvinciaSede"] = info.InformazioniIscrizione.ProvinciaSedeStudi;

                r["ComuneDomicilio"] = dom?.codComuneDomicilio ?? "";

                r["SerieContrattoDomicilio"] = dom?.codiceSerieLocazione ?? "";
                r["DataRegistrazioneDomicilio"] = FormatDateForExport(dom?.dataRegistrazioneLocazione);
                r["DataDecorrenzaDomicilio"] = FormatDateForExport(dom?.dataDecorrenzaLocazione);
                r["DataScadenzaDomicilio"] = FormatDateForExport(dom?.dataScadenzaLocazione);
                r["ProrogatoDomicilio"] = dom?.prorogatoLocazione ?? false;
                r["SerieProrogaDomicilio"] = dom?.codiceSerieProrogaLocazione ?? "";

                r["DomicilioPresente"] = v.DomicilioPresente;
                r["DomicilioValido"] = v.DomicilioValido;
                r["HasAlloggio12"] = v.HasAlloggio12;

                r["HasIstanzaDomicilio"] = v.HasIstanzaDomicilio;
                r["CodTipoIstanzaDomicilio"] = v.CodTipoIstanzaDomicilio ?? "";
                r["NumIstanzaDomicilio"] = v.NumIstanzaDomicilio > 0
                    ? v.NumIstanzaDomicilio.ToString(CultureInfo.InvariantCulture)
                    : "";

                r["HasUltimaIstanzaChiusaDomicilio"] = v.HasUltimaIstanzaChiusaDomicilio;
                r["CodTipoUltimaIstanzaChiusaDomicilio"] = v.CodTipoUltimaIstanzaChiusaDomicilio ?? "";
                r["NumUltimaIstanzaChiusaDomicilio"] = v.NumUltimaIstanzaChiusaDomicilio > 0
                    ? v.NumUltimaIstanzaChiusaDomicilio.ToString(CultureInfo.InvariantCulture)
                    : "";
                r["EsitoUltimaIstanzaChiusaDomicilio"] = v.EsitoUltimaIstanzaChiusaDomicilio ?? "";
                r["UtentePresaCaricoUltimaIstanzaChiusaDomicilio"] = v.UtentePresaCaricoUltimaIstanzaChiusaDomicilio ?? "";

                dt.Rows.Add(r);
            }

            return dt;
        }
        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable();
            dt.Columns.Add("CodFiscale");
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("StatusSedeAttuale");
            dt.Columns.Add("StatusSedeSuggerito");
            dt.Columns.Add("Motivo");
            dt.Columns.Add("ComuneResidenza");
            dt.Columns.Add("ProvinciaResidenza");
            dt.Columns.Add("ComuneSedeStudi");
            dt.Columns.Add("ProvinciaSede");
            dt.Columns.Add("ComuneDomicilio");

            dt.Columns.Add("SerieContrattoDomicilio", typeof(string));
            dt.Columns.Add("DataRegistrazioneDomicilio", typeof(string));
            dt.Columns.Add("DataDecorrenzaDomicilio", typeof(string));
            dt.Columns.Add("DataScadenzaDomicilio", typeof(string));
            dt.Columns.Add("ProrogatoDomicilio", typeof(bool));
            dt.Columns.Add("SerieProrogaDomicilio", typeof(string));

            dt.Columns.Add("DomicilioPresente", typeof(bool));
            dt.Columns.Add("DomicilioValido", typeof(bool));
            dt.Columns.Add("HasAlloggio12", typeof(bool));

            dt.Columns.Add("HasIstanzaDomicilio", typeof(bool));
            dt.Columns.Add("CodTipoIstanzaDomicilio", typeof(string));
            dt.Columns.Add("NumIstanzaDomicilio", typeof(string));

            dt.Columns.Add("HasUltimaIstanzaChiusaDomicilio", typeof(bool));
            dt.Columns.Add("CodTipoUltimaIstanzaChiusaDomicilio", typeof(string));
            dt.Columns.Add("NumUltimaIstanzaChiusaDomicilio", typeof(string));
            dt.Columns.Add("EsitoUltimaIstanzaChiusaDomicilio", typeof(string));
            dt.Columns.Add("UtentePresaCaricoUltimaIstanzaChiusaDomicilio", typeof(string));

            return dt;
        }

        private static void AppendOutput(DataTable dt, StatusSedeStudent row, StatusSedeDecision d)
        {
            var info = row.Info;
            var dom = info.InformazioniSede.Domicilio;

            string cf = (info.InformazioniPersonali.CodFiscale ?? "").Trim().ToUpperInvariant();
            string numDomanda = (info.InformazioniPersonali.NumDomanda ?? "").Trim();

            string comuneRes = GetComuneResidenza(info);
            string provRes = (info.InformazioniSede.Residenza.provincia ?? "").Trim().ToUpperInvariant();

            string comuneSede = (info.InformazioniIscrizione.ComuneSedeStudi ?? "").Trim();
            string provSede = (info.InformazioniIscrizione.ProvinciaSedeStudi ?? "").Trim().ToUpperInvariant();

            string comuneDom = (dom?.codComuneDomicilio ?? "").Trim();

            dt.Rows.Add(
                cf,
                numDomanda,
                (info.InformazioniSede.StatusSede ?? "").Trim().ToUpperInvariant(),
                d.SuggestedStatus,
                d.Reason,
                comuneRes,
                provRes,
                comuneSede,
                provSede,
                comuneDom,
                dom?.codiceSerieLocazione ?? "",
                FormatDateForExport(dom?.dataRegistrazioneLocazione),
                FormatDateForExport(dom?.dataDecorrenzaLocazione),
                FormatDateForExport(dom?.dataScadenzaLocazione),
                dom?.prorogatoLocazione ?? false,
                dom?.codiceSerieProrogaLocazione ?? "",
                d.DomicilioPresente,
                d.DomicilioValido,
                row.HasAlloggio12,
                row.HasIstanzaDomicilio,
                row.CodTipoIstanzaDomicilio ?? "",
                row.NumIstanzaDomicilio > 0 ? row.NumIstanzaDomicilio.ToString(CultureInfo.InvariantCulture) : "",
                row.HasUltimaIstanzaChiusaDomicilio,
                row.CodTipoUltimaIstanzaChiusaDomicilio ?? "",
                row.NumUltimaIstanzaChiusaDomicilio > 0 ? row.NumUltimaIstanzaChiusaDomicilio.ToString(CultureInfo.InvariantCulture) : "",
                row.EsitoUltimaIstanzaChiusaDomicilio ?? "",
                row.UtentePresaCaricoUltimaIstanzaChiusaDomicilio ?? ""
            );
        }
        private static string GetComuneResidenza(StudenteInfo info)
        {
            // I dati possono essere codice o nome comune: mantieni in uscita quello valorizzato.
            var c1 = (info.InformazioniSede.Residenza.codComune ?? "").Trim();
            if (c1.Length > 0) return c1;

            var c2 = (info.InformazioniSede.Residenza.nomeComune ?? "").Trim();
            if (c2.Length > 0) return c2;

            return "";
        }
    }
}
