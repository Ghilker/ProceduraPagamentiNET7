using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica
    {
        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable("Verifica");

            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("StatusCompilazione", typeof(int));
            dt.Columns.Add("CodBeneficio", typeof(string));
            dt.Columns.Add("BeneficioRichiesto", typeof(bool));
            dt.Columns.Add("EsitoBorsaCalcolato", typeof(int));
            dt.Columns.Add("CodiceMotivoEsitoBorsaCalcolato", typeof(string));
            dt.Columns.Add("MotivoEsitoBorsaCalcolato", typeof(string));
            dt.Columns.Add("EsitoBeneficioCalcolato", typeof(int));
            dt.Columns.Add("CodiceMotivoEsitoBeneficioCalcolato", typeof(string));
            dt.Columns.Add("MotivoEsitoBeneficioCalcolato", typeof(string));
            dt.Columns.Add("TipoRedditoOrigine", typeof(string));
            dt.Columns.Add("TipoRedditoIntegrazione", typeof(string));
            dt.Columns.Add("CodTipoEsitoBS", typeof(int));
            dt.Columns.Add("CodTipoEsitoBeneficio", typeof(int));
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

            dt.Columns.Add("AnnoCorsoCalcolatoMerito", typeof(int));
            dt.Columns.Add("AnnoCorsoRiferimentoBeneficio", typeof(int));
            dt.Columns.Add("CodTipoOrdinamentoMerito", typeof(string));
            dt.Columns.Add("PassaggioTrasferimentoMerito", typeof(bool));
            dt.Columns.Add("RipetenteDaPassaggioMerito", typeof(bool));
            dt.Columns.Add("PrimaImmatricolazTsMerito", typeof(int));
            dt.Columns.Add("RichiestaCSMerito", typeof(bool));
            dt.Columns.Add("NubileProleMerito", typeof(bool));
            dt.Columns.Add("RegolaMeritoApplicata", typeof(string));
            dt.Columns.Add("FaseElaborativaVerifica", typeof(string));
            dt.Columns.Add("TipoStudenteNormalizzato", typeof(int));
            dt.Columns.Add("DiagnosticaIscrizioneVB", typeof(string));
            dt.Columns.Add("EsamiMinimiRichiestiMerito", typeof(decimal));
            dt.Columns.Add("CreditiMinimiRichiestiMerito", typeof(decimal));
            dt.Columns.Add("EsamiMinimiRichiestiPassaggioMerito", typeof(decimal));
            dt.Columns.Add("CreditiMinimiRichiestiPassaggioMerito", typeof(decimal));
            dt.Columns.Add("SlashMotiviEsclusioneBS", typeof(string));
            dt.Columns.Add("SlashMotiviEsclusioneBeneficio", typeof(string));
            dt.Columns.Add("VariazioniEscludentiBS", typeof(string));
            dt.Columns.Add("VariazioniEscludentiBeneficio", typeof(string));
            dt.Columns.Add("RinunciaBSDaVariazioni", typeof(bool));
            dt.Columns.Add("RinunciaBeneficioDaVariazioni", typeof(bool));
            dt.Columns.Add("DecadutoBSDaVariazioni", typeof(bool));
            dt.Columns.Add("DecadutoBeneficioDaVariazioni", typeof(bool));
            dt.Columns.Add("RevocatoDaVariazioni", typeof(bool));
            dt.Columns.Add("RevocatoBandoBSDaVariazioni", typeof(bool));
            dt.Columns.Add("RevocatoBandoBeneficioDaVariazioni", typeof(bool));

            dt.Columns.Add("NumeroEventiCarrieraPregressa", typeof(int));
            dt.Columns.Add("UltimoAnnoAvvenimentoCarrieraPregressa", typeof(int));
            dt.Columns.Add("TotaleCreditiCarrieraPregressa", typeof(decimal));
            dt.Columns.Add("HaPassaggioCorsoEsteroCarrieraPregressa", typeof(bool));
            dt.Columns.Add("HaRipetenzaCarrieraPregressa", typeof(bool));
            dt.Columns.Add("CodiciAvvenimentoCarrieraPregressa", typeof(string));

            dt.Columns.Add("StatusSedeRiferimentoImportoBorsa", typeof(string));
            dt.Columns.Add("ImportoBaseBorsa", typeof(decimal));
            dt.Columns.Add("ImportoFinaleBorsa", typeof(decimal));
            dt.Columns.Add("ImportoAssegnato", typeof(decimal));

            return dt;
        }

        private static (IReadOnlyList<StudenteInfo> Items, DataTable Table) BuildOrderedOutputs(VerificaPipelineContext context)
        {
            var students = context.Students;
            var orderedPairs = VerificaExecutionSupport.OrderStudents(students);
            var dt = BuildOutputTable();
            var items = new List<StudenteInfo>(orderedPairs.Count);

            dt.BeginLoadData();
            try
            {
                foreach (var pair in orderedPairs)
                {
                    var info = pair.Value;
                    items.Add(info);
                    context.EsitoBorsaFactsByStudent.TryGetValue(pair.Key, out var facts);
                    var benefitCodes = EsitoBorsaSupport.GetBenefitCodes(context, pair.Key, facts);
                    foreach (var codBeneficio in benefitCodes)
                        AddOutputRow(dt, context, pair.Key, info, codBeneficio);
                }
            }
            finally
            {
                dt.EndLoadData();
            }

            return (items, dt);
        }

        private static void AddOutputRow(DataTable dt, VerificaPipelineContext context, StudentKey key, StudenteInfo info, string codBeneficio)
        {
            var eco = info.InformazioniEconomiche;
            var sede = info.InformazioniSede;
            var dom = info.InformazioniSede.Domicilio;
            var iscr = info.InformazioniIscrizione;
            var impBorsa = info.InformazioniImportoBorsa;

            var row = dt.NewRow();
            row["CodFiscale"] = info.InformazioniPersonali.CodFiscale ?? "";
            row["NumDomanda"] = info.InformazioniPersonali.NumDomanda ?? "";
            context.EsitiCalcolatiByStudentBenefit.TryGetValue(key, out var calcolatiByBenefit);
            EsitoBeneficioCalcolato esitoBeneficio = new();
            EsitoConcorsoBenefitRaw rawBeneficio = new();
            calcolatiByBenefit?.TryGetValue(codBeneficio, out esitoBeneficio);
            context.EsitiConcorsoByStudentBenefit.TryGetValue(key, out var rawByBenefit);
            rawByBenefit?.TryGetValue(codBeneficio, out rawBeneficio);
            bool richiesto = context.EsitoBorsaFactsByStudent.TryGetValue(key, out var factsTmp) && EsitoBorsaSupport.IsBenefitRequested(factsTmp, codBeneficio);

            row["StatusCompilazione"] = info.StatusCompilazione;
            row["CodBeneficio"] = codBeneficio;
            row["BeneficioRichiesto"] = richiesto;
            row["EsitoBorsaCalcolato"] = esitoBeneficio?.EsitoCalcolato ?? info.EsitoBorsaCalcolato;
            row["CodiceMotivoEsitoBorsaCalcolato"] = esitoBeneficio?.CodiciMotivo ?? info.CodiciMotivoEsitoBorsaCalcolato ?? "";
            row["MotivoEsitoBorsaCalcolato"] = esitoBeneficio?.Motivi ?? info.MotiviEsitoBorsaCalcolato ?? "";
            row["EsitoBeneficioCalcolato"] = esitoBeneficio?.EsitoCalcolato ?? 0;
            row["CodiceMotivoEsitoBeneficioCalcolato"] = esitoBeneficio?.CodiciMotivo ?? "";
            row["MotivoEsitoBeneficioCalcolato"] = esitoBeneficio?.Motivi ?? "";

            row["TipoRedditoOrigine"] = eco.Raw.TipoRedditoOrigine ?? "";
            row["TipoRedditoIntegrazione"] = eco.Raw.TipoRedditoIntegrazione ?? "";
            row["CodTipoEsitoBS"] = eco.Raw.CodTipoEsitoBS ?? 0;
            row["CodTipoEsitoBeneficio"] = rawBeneficio?.CodTipoEsito ?? 0;
            row["ImportoAssegnato"] = rawBeneficio?.ImportoAssegnato ?? ToDecimalOrZero(eco.Raw.ImportoAssegnato);
            row["ISR"] = eco.Calcolate.ISRDSU;
            row["ISP"] = eco.Calcolate.ISPDSU;
            row["Detrazioni"] = eco.Calcolate.Detrazioni;
            row["ISEDSU"] = eco.Calcolate.ISEDSU;
            row["ISEEDSU"] = eco.Calcolate.ISEEDSU;
            row["ISPEDSU"] = eco.Calcolate.ISPEDSU;
            row["ISPDSU"] = eco.Calcolate.ISPDSU;
            row["SEQ"] = eco.Calcolate.SEQ;
            row["ISEDSU_Attuale"] = ToDecimalOrZero(eco.Attuali.ISEDSU);
            row["ISEEDSU_Attuale"] = ToDecimalOrZero(eco.Attuali.ISEEDSU);
            row["ISPEDSU_Attuale"] = ToDecimalOrZero(eco.Attuali.ISPEDSU);
            row["ISPDSU_Attuale"] = ToDecimalOrZero(eco.Attuali.ISPDSU);
            row["SEQ_Attuale"] = ToDecimalOrZero(eco.Attuali.SEQ);

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
            SetIfHasValue(row, "AnnoCorsoIscrizione", iscr.AnnoCorso);
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

            int aaInizio = EsitoBorsaSupport.ParseAnnoAccademicoInizio(context.AnnoAccademico);
            int aaNumero = EsitoBorsaSupport.ParseAnnoAccademicoAsNumber(context.AnnoAccademico);
            context.EsitoBorsaFactsByStudent.TryGetValue(key, out var facts);
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, facts, aaInizio, aaNumero);
            bool ripetenteDaPassaggio = aaNumero >= 20252026
                                        && ((facts?.RipetenteDaPassaggio).HasValue == true
                                            ? facts!.RipetenteDaPassaggio!.Value
                                            : iscr.HaRipetenzaCarrieraPregressa != 0);
            int annoCorsoRiferimentoBeneficio = ripetenteDaPassaggio ? iscr.AnnoCorso : annoCorsoCalcolato;

            row["AnnoCorsoCalcolatoMerito"] = annoCorsoCalcolato != 0 ? annoCorsoCalcolato : DBNull.Value;
            row["AnnoCorsoRiferimentoBeneficio"] = annoCorsoRiferimentoBeneficio != 0 ? annoCorsoRiferimentoBeneficio : DBNull.Value;
            row["CodTipoOrdinamentoMerito"] = facts?.CodTipoOrdinamento ?? "";
            row["PassaggioTrasferimentoMerito"] = facts?.PassaggioTrasferimento == true;
            row["RipetenteDaPassaggioMerito"] = ripetenteDaPassaggio;
            row["PrimaImmatricolazTsMerito"] = facts?.PrimaImmatricolazTs ?? (object)DBNull.Value;
            row["RichiestaCSMerito"] = facts?.RichiestaCS == true;
            row["NubileProleMerito"] = facts?.NubileProle == true;
            row["RegolaMeritoApplicata"] = iscr.RegolaMeritoApplicata ?? "";
            row["FaseElaborativaVerifica"] = context.FaseElaborativa.ToString();
            SetIfHasValue(row, "TipoStudenteNormalizzato", facts?.TipoStudenteNormalizzato);
            row["DiagnosticaIscrizioneVB"] = facts?.DiagnosticaIscrizione ?? string.Empty;
            SetIfHasValue(row, "EsamiMinimiRichiestiMerito", iscr.EsamiMinimiRichiestiMerito);
            SetIfHasValue(row, "CreditiMinimiRichiestiMerito", iscr.CreditiMinimiRichiestiMerito);
            SetIfHasValue(row, "EsamiMinimiRichiestiPassaggioMerito", iscr.EsamiMinimiRichiestiPassaggio);
            SetIfHasValue(row, "CreditiMinimiRichiestiPassaggioMerito", iscr.CreditiMinimiRichiestiPassaggio);
            row["SlashMotiviEsclusioneBS"] = facts?.SlashMotiviEsclusioneBS ?? "";
            row["SlashMotiviEsclusioneBeneficio"] = EsitoBorsaSupport.GetSlashMotiviEsclusione(facts, codBeneficio);
            row["VariazioniEscludentiBS"] = EsitoBorsaSupport.GetVariazioniEscludentiBsSummary(facts);
            row["VariazioniEscludentiBeneficio"] = EsitoBorsaSupport.GetVariazioniEscludentiSummary(facts, codBeneficio);
            row["RinunciaBSDaVariazioni"] = facts?.RinunciaBS == true;
            row["RinunciaBeneficioDaVariazioni"] = EsitoBorsaSupport.HasRinunciaVariazione(facts, codBeneficio);
            row["DecadutoBSDaVariazioni"] = facts?.DecadutoBS == true;
            row["DecadutoBeneficioDaVariazioni"] = EsitoBorsaSupport.HasDecadenzaVariazione(facts, codBeneficio);
            row["RevocatoDaVariazioni"] = facts != null && (facts.Revocato || facts.RevocatoMancataIscrizione || facts.RevocatoIscrittoRipetente || facts.RevocatoISEE || facts.RevocatoLaureato || facts.RevocatoPatrimonio || facts.RevocatoReddito || facts.RevocatoEsami || facts.RevocatoFuoriTermine || facts.RevocatoIseeFuoriTermine || facts.RevocatoIseeNonProdotta || facts.RevocatoTrasmissioneIseeFuoriTermine || facts.RevocatoNoContrattoLocazione);
            row["RevocatoBandoBSDaVariazioni"] = facts?.RevocatoBandoBS == true;
            row["RevocatoBandoBeneficioDaVariazioni"] = EsitoBorsaSupport.HasRevocaBandoVariazione(facts, codBeneficio);

            SetIfPositiveInt(row, "NumeroEventiCarrieraPregressa", iscr.NumeroEventiCarrieraPregressa);
            SetIfHasValue(row, "UltimoAnnoAvvenimentoCarrieraPregressa", iscr.UltimoAnnoAvvenimentoCarrieraPregressa);
            row["TotaleCreditiCarrieraPregressa"] = iscr.TotaleCreditiCarrieraPregressa;
            row["HaPassaggioCorsoEsteroCarrieraPregressa"] = iscr.HaPassaggioCorsoEsteroCarrieraPregressa != 0;
            row["HaRipetenzaCarrieraPregressa"] = iscr.HaRipetenzaCarrieraPregressa != 0;
            row["CodiciAvvenimentoCarrieraPregressa"] = iscr.CodiciAvvenimentoCarrieraPregressa ?? "";

            row["StatusSedeRiferimentoImportoBorsa"] = string.Equals(codBeneficio, "BS", StringComparison.OrdinalIgnoreCase) ? (impBorsa.StatusSedeRiferimento ?? "") : "";
            row["ImportoBaseBorsa"] = string.Equals(codBeneficio, "BS", StringComparison.OrdinalIgnoreCase) ? impBorsa.ImportoBase : DBNull.Value;
            row["ImportoFinaleBorsa"] = string.Equals(codBeneficio, "BS", StringComparison.OrdinalIgnoreCase) ? impBorsa.ImportoFinale : DBNull.Value;

            dt.Rows.Add(row);
        }

        private static decimal ToDecimalOrZero(object? value)
        {
            if (value == null || value == DBNull.Value)
                return 0m;

            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0m;
            }
        }

        private static void SetIfHasValue(DataRow row, string columnName, object? value)
        {
            row[columnName] = value ?? DBNull.Value;
        }

        private static void SetIfPositiveInt(DataRow row, string columnName, int value)
        {
            row[columnName] = value > 0 ? value : DBNull.Value;
        }

        private static string FormatDateForExport(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue || value.Value.Year < 1900)
                return "";

            return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
