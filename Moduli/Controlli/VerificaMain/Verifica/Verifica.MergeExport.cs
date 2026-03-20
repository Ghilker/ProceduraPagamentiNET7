using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica
    {
        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable("Verifica");

            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));
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
            dt.Columns.Add("StatusSedeAttuale", typeof(string));
            dt.Columns.Add("StatusSedeSuggerito", typeof(string));
            dt.Columns.Add("MotivoStatusSede", typeof(string));
            dt.Columns.Add("ComuneResidenza", typeof(string));
            dt.Columns.Add("ProvinciaResidenza", typeof(string));
            dt.Columns.Add("ComuneSedeStudi", typeof(string));
            dt.Columns.Add("ProvinciaSede", typeof(string));
            dt.Columns.Add("ComuneDomicilio", typeof(string));
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

        private static DataTable ToDataTable(IReadOnlyList<StudenteInfo> items)
        {
            var dt = BuildOutputTable();

            foreach (var info in items)
            {
                var eco = info.InformazioniEconomiche;
                var sede = info.InformazioniSede;
                var dom = info.InformazioniSede.Domicilio;

                var row = dt.NewRow();
                row["CodFiscale"] = info.InformazioniPersonali.CodFiscale ?? "";
                row["NumDomanda"] = info.InformazioniPersonali.NumDomanda ?? "";

                row["TipoRedditoOrigine"] = eco.TipoRedditoOrigine ?? "";
                row["TipoRedditoIntegrazione"] = eco.TipoRedditoIntegrazione ?? "";
                row["CodTipoEsitoBS"] = eco.CodTipoEsitoBS ?? 0;
                row["ISR"] = eco.ISRDSU;
                row["ISP"] = eco.ISPDSU;
                row["Detrazioni"] = eco.Detrazioni;
                row["ISEDSU"] = eco.ISEDSU;
                row["ISEEDSU"] = eco.ISEEDSU;
                row["ISPEDSU"] = eco.ISPEDSU;
                row["ISPDSU"] = eco.ISPDSU;
                row["SEQ"] = eco.SEQ;
                row["ISEDSU_Attuale"] = Convert.ToDecimal(eco.ISEDSU_Attuale);
                row["ISEEDSU_Attuale"] = Convert.ToDecimal(eco.ISEEDSU_Attuale);
                row["ISPEDSU_Attuale"] = Convert.ToDecimal(eco.ISPEDSU_Attuale);
                row["ISPDSU_Attuale"] = Convert.ToDecimal(eco.ISPDSU_Attuale);
                row["SEQ_Attuale"] = Convert.ToDecimal(eco.SEQ_Attuale);

                row["StatusSedeAttuale"] = sede.StatusSede ?? "";
                row["StatusSedeSuggerito"] = sede.StatusSedeSuggerito ?? "";
                row["MotivoStatusSede"] = sede.MotivoStatusSede ?? "";
                row["ComuneResidenza"] = sede.Residenza.codComune ?? "";
                row["ProvinciaResidenza"] = sede.Residenza.provincia ?? "";
                row["ComuneSedeStudi"] = info.InformazioniIscrizione.ComuneSedeStudi ?? "";
                row["ProvinciaSede"] = info.InformazioniIscrizione.ProvinciaSedeStudi ?? "";
                row["ComuneDomicilio"] = dom?.codComuneDomicilio ?? "";
                row["SerieContrattoDomicilio"] = dom?.codiceSerieLocazione ?? "";
                row["DataRegistrazioneDomicilio"] = FormatDateForExport(dom?.dataRegistrazioneLocazione);
                row["DataDecorrenzaDomicilio"] = FormatDateForExport(dom?.dataDecorrenzaLocazione);
                row["DataScadenzaDomicilio"] = FormatDateForExport(dom?.dataScadenzaLocazione);
                row["ProrogatoDomicilio"] = dom?.prorogatoLocazione ?? false;
                row["SerieProrogaDomicilio"] = dom?.codiceSerieProrogaLocazione ?? "";
                row["DomicilioPresente"] = sede.DomicilioPresente;
                row["DomicilioValido"] = sede.DomicilioValido;
                row["HasAlloggio12"] = sede.HasAlloggio12;
                row["HasIstanzaDomicilio"] = sede.HasIstanzaDomicilio;
                row["CodTipoIstanzaDomicilio"] = sede.CodTipoIstanzaDomicilio ?? "";
                row["NumIstanzaDomicilio"] = sede.NumIstanzaDomicilio > 0 ? sede.NumIstanzaDomicilio.ToString(CultureInfo.InvariantCulture) : "";
                row["HasUltimaIstanzaChiusaDomicilio"] = sede.HasUltimaIstanzaChiusaDomicilio;
                row["CodTipoUltimaIstanzaChiusaDomicilio"] = sede.CodTipoUltimaIstanzaChiusaDomicilio ?? "";
                row["NumUltimaIstanzaChiusaDomicilio"] = sede.NumUltimaIstanzaChiusaDomicilio > 0 ? sede.NumUltimaIstanzaChiusaDomicilio.ToString(CultureInfo.InvariantCulture) : "";
                row["EsitoUltimaIstanzaChiusaDomicilio"] = sede.EsitoUltimaIstanzaChiusaDomicilio ?? "";
                row["UtentePresaCaricoUltimaIstanzaChiusaDomicilio"] = sede.UtentePresaCaricoUltimaIstanzaChiusaDomicilio ?? "";

                dt.Rows.Add(row);
            }

            return dt;
        }

        private static string FormatDateForExport(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue || value.Value.Year < 1900)
                return "";

            return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
