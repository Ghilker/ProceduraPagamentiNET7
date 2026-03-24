using ProcedureNet7.Storni;
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

            dt.Columns.Add("TipoBando", typeof(string));
            dt.Columns.Add("AnnoCorsoIscrizione", typeof(int));
            dt.Columns.Add("CodSedeStudiIscrizione", typeof(string));
            dt.Columns.Add("CodCorsoLaureaIscrizione", typeof(string));
            dt.Columns.Add("CodFacoltaIscrizione", typeof(string));
            dt.Columns.Add("CodTipologiaStudiIscrizione", typeof(string));
            dt.Columns.Add("CreditiTirocinioIscrizione", typeof(decimal));
            dt.Columns.Add("CreditiRiconosciutiIscrizione", typeof(decimal));
            dt.Columns.Add("IscrittoSemestreFiltroIscrizione", typeof(bool));
            dt.Columns.Add("CodSedeDistaccataAppartenenza", typeof(string));
            dt.Columns.Add("CodEnteAppartenenza", typeof(string));
            dt.Columns.Add("AnnoImmatricolazioneMerito", typeof(int));
            dt.Columns.Add("NumeroEsamiMerito", typeof(int));
            dt.Columns.Add("NumeroCreditiMerito", typeof(decimal));
            dt.Columns.Add("SommaVotiMerito", typeof(decimal));
            dt.Columns.Add("UtilizzoBonusMerito", typeof(bool));
            dt.Columns.Add("CreditiUtilizzatiMerito", typeof(decimal));
            dt.Columns.Add("CreditiRimanentiMerito", typeof(decimal));
            dt.Columns.Add("CreditiRiconosciutiDaRinunciaMerito", typeof(decimal));
            dt.Columns.Add("AACreditiRiconosciutiMerito", typeof(string));

            dt.Columns.Add("NumeroEventiCarrieraPregressa", typeof(int));
            dt.Columns.Add("UltimoAnnoAvvenimentoCarrieraPregressa", typeof(int));
            dt.Columns.Add("TotaleCreditiCarrieraPregressa", typeof(decimal));
            dt.Columns.Add("HaPassaggioCorsoEsteroCarrieraPregressa", typeof(bool));
            dt.Columns.Add("HaRipetenzaCarrieraPregressa", typeof(bool));
            dt.Columns.Add("CodiciAvvenimentoCarrieraPregressa", typeof(string));

            dt.Columns.Add("StatusSedeRiferimentoImportoBorsa", typeof(string));
            dt.Columns.Add("ImportoBaseBorsa", typeof(decimal));
            dt.Columns.Add("ImportoFinaleBorsa", typeof(decimal));
            dt.Columns.Add("CalcoloImportoBorsaEseguito", typeof(bool));

            return dt;
        }

        private static DataTable ToDataTable(
            IReadOnlyList<StudenteInfo> items)
        {
            var dt = BuildOutputTable();

            foreach (var info in items)
            {
                var eco = info.InformazioniEconomiche;
                var sede = info.InformazioniSede;
                var dom = info.InformazioniSede.Domicilio;
                var iscr = info.InformazioniIscrizione;
                var impBorsa = info.InformazioniImportoBorsa;

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

                row["TipoBando"] = iscr.TipoBando ?? "";
                SetIfPositiveInt(row, "AnnoCorsoIscrizione", iscr.AnnoCorso);
                row["CodSedeStudiIscrizione"] = iscr.CodSedeStudi ?? "";
                row["CodCorsoLaureaIscrizione"] = iscr.CodCorsoLaurea ?? "";
                row["CodFacoltaIscrizione"] = iscr.CodFacolta ?? "";
                row["CodTipologiaStudiIscrizione"] = iscr.TipoCorso > 0 ? iscr.TipoCorso.ToString(CultureInfo.InvariantCulture) : "";
                SetIfHasValue(row, "CreditiTirocinioIscrizione", iscr.CreditiTirocinio);
                SetIfHasValue(row, "CreditiRiconosciutiIscrizione", iscr.CreditiRiconosciuti);
                row["IscrittoSemestreFiltroIscrizione"] = iscr.ConfermaSemestreFiltro != 0;
                row["CodSedeDistaccataAppartenenza"] = iscr.CodSedeDistaccata ?? "";
                row["CodEnteAppartenenza"] = iscr.CodEnte ?? "";
                SetIfHasValue(row, "AnnoImmatricolazioneMerito", iscr.AnnoImmatricolazione);
                SetIfHasValue(row, "NumeroEsamiMerito", iscr.NumeroEsami);
                SetIfHasValue(row, "NumeroCreditiMerito", iscr.NumeroCrediti);
                SetIfHasValue(row, "SommaVotiMerito", iscr.SommaVoti);
                row["UtilizzoBonusMerito"] = iscr.UtilizzoBonus != 0;
                SetIfHasValue(row, "CreditiUtilizzatiMerito", iscr.CreditiUtilizzati);
                SetIfHasValue(row, "CreditiRimanentiMerito", iscr.CreditiRimanenti);
                SetIfHasValue(row, "CreditiRiconosciutiDaRinunciaMerito", iscr.CreditiRiconosciutiDaRinuncia);
                row["AACreditiRiconosciutiMerito"] = iscr.AACreditiRiconosciuti ?? "";

                SetIfPositiveInt(row, "NumeroEventiCarrieraPregressa", iscr.NumeroEventiCarrieraPregressa);
                SetIfHasValue(row, "UltimoAnnoAvvenimentoCarrieraPregressa", iscr.UltimoAnnoAvvenimentoCarrieraPregressa);
                row["TotaleCreditiCarrieraPregressa"] = iscr.TotaleCreditiCarrieraPregressa;
                row["HaPassaggioCorsoEsteroCarrieraPregressa"] = iscr.HaPassaggioCorsoEsteroCarrieraPregressa != 0;
                row["HaRipetenzaCarrieraPregressa"] = iscr.HaRipetenzaCarrieraPregressa != 0;
                row["CodiciAvvenimentoCarrieraPregressa"] = iscr.CodiciAvvenimentoCarrieraPregressa ?? "";

                row["StatusSedeRiferimentoImportoBorsa"] = impBorsa.StatusSedeRiferimento ?? "";
                row["ImportoBaseBorsa"] = impBorsa.ImportoBase;
                row["ImportoFinaleBorsa"] = impBorsa.ImportoFinale;
                row["CalcoloImportoBorsaEseguito"] = impBorsa.CalcoloEseguito;

                dt.Rows.Add(row);
            }

            return dt;
        }

        private static DataTable BuildCarrieraPregressaOutputTable()
        {
            var dt = new DataTable("CarrieraPregressa");
            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("TipoBando", typeof(string));
            dt.Columns.Add("CodAvvenimento", typeof(string));
            dt.Columns.Add("AnnoAvvenimento", typeof(int));
            dt.Columns.Add("UnivDiConseguim", typeof(string));
            dt.Columns.Add("UnivProvenienza", typeof(string));
            dt.Columns.Add("PrimaImmatricolaz", typeof(string));
            dt.Columns.Add("TipologiaCorso", typeof(string));
            dt.Columns.Add("DurataLegTitoloConseguito", typeof(int));
            dt.Columns.Add("PassaggioCorsoEstero", typeof(bool));
            dt.Columns.Add("SedeIstituzioneUniversitaria", typeof(string));
            dt.Columns.Add("BeneficiUsufruiti", typeof(string));
            dt.Columns.Add("ImportiRestituiti", typeof(string));
            dt.Columns.Add("NumeroCrediti", typeof(decimal));
            dt.Columns.Add("AnnoCorso", typeof(int));
            dt.Columns.Add("Ripetente", typeof(bool));
            dt.Columns.Add("Ateneo", typeof(string));
            dt.Columns.Add("CodComuneAteneo", typeof(string));
            dt.Columns.Add("CodAteneo", typeof(string));
            dt.Columns.Add("IscrittoSemestreFiltroDi", typeof(bool));
            return dt;
        }

        private static DataTable ToCarrieraPregressaDataTable(IReadOnlyList<StudenteInfo> items)
        {
            var dt = BuildCarrieraPregressaOutputTable();

            foreach (var info in items)
            {
                foreach (var item in info.InformazioniIscrizione.CarrierePregresse
                    .OrderBy(x => x.AnnoAvvenimento ?? 0)
                    .ThenBy(x => x.CodAvvenimento ?? "", StringComparer.OrdinalIgnoreCase))
                {
                    var row = dt.NewRow();
                    row["CodFiscale"] = info.InformazioniPersonali.CodFiscale ?? "";
                    row["NumDomanda"] = info.InformazioniPersonali.NumDomanda ?? "";
                    row["TipoBando"] = info.InformazioniIscrizione.TipoBando ?? "";
                    row["CodAvvenimento"] = item.CodAvvenimento ?? "";
                    SetIfHasValue(row, "AnnoAvvenimento", item.AnnoAvvenimento);
                    row["UnivDiConseguim"] = item.UnivDiConseguim ?? "";
                    row["UnivProvenienza"] = item.UnivProvenienza ?? "";
                    row["PrimaImmatricolaz"] = FormatDateForExport(item.PrimaImmatricolaz);
                    row["TipologiaCorso"] = item.TipologiaCorso ?? "";
                    SetIfHasValue(row, "DurataLegTitoloConseguito", item.DurataLegTitoloConseguito);
                    row["PassaggioCorsoEstero"] = item.PassaggioCorsoEstero != 0;
                    row["SedeIstituzioneUniversitaria"] = item.SedeIstituzioneUniversitaria ?? "";
                    row["BeneficiUsufruiti"] = item.BeneficiUsufruiti ?? "";
                    row["ImportiRestituiti"] = item.ImportiRestituiti ?? "";
                    SetIfHasValue(row, "NumeroCrediti", item.NumeroCrediti);
                    SetIfHasValue(row, "AnnoCorso", item.AnnoCorso);
                    row["Ripetente"] = item.Ripetente != 0;
                    row["Ateneo"] = item.Ateneo ?? "";
                    row["CodComuneAteneo"] = item.CodComuneAteneo ?? "";
                    row["CodAteneo"] = item.CodAteneo ?? "";
                    row["IscrittoSemestreFiltroDi"] = item.ConfermaSemestreFiltroDi != 0;
                    dt.Rows.Add(row);
                }
            }

            return dt;
        }

        private static void SetIfHasValue(DataRow row, string columnName, object? value)
        {
            if (value == null)
                row[columnName] = DBNull.Value;
            else
                row[columnName] = value;
        }

        private static void SetIfPositiveInt(DataRow row, string columnName, int value)
        {
            if (value > 0)
                row[columnName] = value;
            else
                row[columnName] = DBNull.Value;
        }

        private static string FormatDateForExport(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue || value.Value.Year < 1900)
                return "";

            return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
