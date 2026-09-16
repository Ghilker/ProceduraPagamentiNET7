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
        private const decimal IseeMinimoResidentiEsteroPerCoefficiente = 7002.97m;
        private static readonly IReadOnlyList<string> OutputBenefitCodes = EsitoBorsaSupport.SupportedBenefitCodes;
        private static readonly IReadOnlyList<string> OutputExclusionReasonBenefitCodes = new[] { "BS", "PA", "CI" };

        private static DataTable BuildOutputTable()
        {
            var dt = new DataTable("Verifica");

            dt.Columns.Add("CodFiscale", typeof(string));
            dt.Columns.Add("NumDomanda", typeof(string));
            dt.Columns.Add("StatusCompilazione", typeof(int));
            dt.Columns.Add("Verifica provvedimenti esclusione", typeof(string));

            foreach (var codBeneficio in OutputBenefitCodes)
            {
                dt.Columns.Add($"EsitoAttuale_{codBeneficio}", typeof(int));
                dt.Columns.Add($"EsitoCalcolato_{codBeneficio}", typeof(int));

                if (OutputExclusionReasonBenefitCodes.Contains(codBeneficio, StringComparer.OrdinalIgnoreCase))
                {
                    dt.Columns.Add($"CodiciEsclusioneCalcolata_{codBeneficio}", typeof(string));
                    dt.Columns.Add($"DescrizioneEsclusioneCalcolata_{codBeneficio}", typeof(string));
                }
            }

            dt.Columns.Add("TipoRedditoOrigine", typeof(string));
            dt.Columns.Add("TipoRedditoIntegrazione", typeof(string));
            dt.Columns.Add("HasIseeBaseEntroScadenza", typeof(bool));
            dt.Columns.Add("HasCOUniversitarioEntroScadenza", typeof(bool));
            dt.Columns.Add("HasCOOrdinarioConIntegrazioneEsteriEntroScadenza", typeof(bool));
            dt.Columns.Add("HasCOOrdinarioSemestreFiltroEntroScadenza", typeof(bool));
            dt.Columns.Add("UltimaCOImportataOrdinariaSenzaUniversitaria", typeof(bool));
            dt.Columns.Add("IseeOrdinarioInAttesaRegolarizzazione", typeof(bool));
            dt.Columns.Add("CodiciBlocchiIncongruenze", typeof(string));
            dt.Columns.Add("DescrizioneBlocchiIncongruenze", typeof(string));
            dt.Columns.Add("CodiciIncongruenzeNonEscludenti", typeof(string));
            dt.Columns.Add("DescrizioneIncongruenzeNonEscludenti", typeof(string));
            dt.Columns.Add("HasCIUniversitarioEntroScadenza", typeof(bool));
            dt.Columns.Add("OrigineEconomicaAdeguata", typeof(bool));
            dt.Columns.Add("MotivoAdeguatezzaOrigine", typeof(string));
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
            dt.Columns.Add("GestioneDomicili", typeof(string));
            dt.Columns.Add("EsitoAnalisiDomicili", typeof(string));
            dt.Columns.Add("DataInizioTotaleDomicili", typeof(string));
            dt.Columns.Add("DataFineTotaleDomicili", typeof(string));
            dt.Columns.Add("MesiCopertiDomicili", typeof(int));
            dt.Columns.Add("ValidoCertoPerSaldoDomicili", typeof(bool));
            dt.Columns.Add("MotivoAnalisiDomicili", typeof(string));
            dt.Columns.Add("AnomalieAnalisiDomicili", typeof(string));
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
            dt.Columns.Add("RegolaMeritoApplicata", typeof(string));
            dt.Columns.Add("FaseElaborativaVerifica", typeof(string));
            dt.Columns.Add("TipoStudenteNormalizzato", typeof(int));
            dt.Columns.Add("DiagnosticaIscrizioneVB", typeof(string));
            dt.Columns.Add("EsamiMinimiRichiestiMerito", typeof(decimal));
            dt.Columns.Add("CreditiMinimiRichiestiMerito", typeof(decimal));
            dt.Columns.Add("IdCreditiRichiestiSelezionato", typeof(int));
            dt.Columns.Add("AnnoCreditiRichiestiSelezionato", typeof(int));
            dt.Columns.Add("CodCorsoCreditiRichiestiSelezionato", typeof(string));
            dt.Columns.Add("SogliaCreditiSelezionata", typeof(decimal));
            dt.Columns.Add("EsamiMinimiRichiestiPassaggioMerito", typeof(decimal));
            dt.Columns.Add("CreditiMinimiRichiestiPassaggioMerito", typeof(decimal));
            dt.Columns.Add("SlashMotiviEsclusioneBS", typeof(string));
            dt.Columns.Add("VariazioniEscludentiBS", typeof(string));
            dt.Columns.Add("RinunciaBSDaVariazioni", typeof(bool));
            dt.Columns.Add("DecadutoBSDaVariazioni", typeof(bool));
            dt.Columns.Add("RevocatoDaVariazioni", typeof(bool));
            dt.Columns.Add("RevocatoBandoBSDaVariazioni", typeof(bool));

            dt.Columns.Add("NumeroEventiCarrieraPregressa", typeof(int));
            dt.Columns.Add("UltimoAnnoAvvenimentoCarrieraPregressa", typeof(int));
            dt.Columns.Add("TotaleCreditiCarrieraPregressa", typeof(decimal));
            dt.Columns.Add("HaPassaggioCorsoEsteroCarrieraPregressa", typeof(bool));
            dt.Columns.Add("HaRipetenzaCarrieraPregressa", typeof(bool));
            dt.Columns.Add("CodiciAvvenimentoCarrieraPregressa", typeof(string));
            dt.Columns.Add("BorsaPregressaNonRestituitaConfliggente", typeof(bool));
            dt.Columns.Add("AnnoBorsaRichiestoNormalizzato", typeof(int));
            dt.Columns.Add("AnniBorsaPregressaUsufruitiNormalizzati", typeof(string));
            dt.Columns.Add("AnniBorsaPregressaRestituitiNormalizzati", typeof(string));
            dt.Columns.Add("AnniBorsaPregressaNonRestituitaConfliggenti", typeof(string));
            dt.Columns.Add("BorsaPregressaEsteraNonRichiedeRestituzione", typeof(bool));
            dt.Columns.Add("DiagnosticaBorsaPregressaRestituzioni", typeof(string));
            dt.Columns.Add("BorsaStoricaStessoAnnoConfliggente", typeof(bool));
            dt.Columns.Add("BorsaStoricaRichiedeRevisione", typeof(bool));
            dt.Columns.Add("DiagnosticaBorsaStoricaStessoAnno", typeof(string));

            dt.Columns.Add("StatusSedeRiferimentoImportoBorsa", typeof(string));
            dt.Columns.Add("ImportoBaseBorsa", typeof(decimal));
            dt.Columns.Add("ImportoFinaleBorsa", typeof(decimal));
            dt.Columns.Add("ImportoAssegnatoBS", typeof(decimal));
            dt.Columns.Add("ImportoSpecificheImpegniBS", typeof(decimal));
            dt.Columns.Add("SpecificheImpegniUgualeImportoAssegnatoBS", typeof(string));
            dt.Columns.Add("SpecificheImpegniUgualeImportoCalcolatoBS", typeof(string));

            dt.Columns.Add("CFN_Attuale", typeof(decimal));
            dt.Columns.Add("MeritoConseguito_Attuale", typeof(decimal));
            dt.Columns.Add("MeritoMinimoPrevisto_Attuale", typeof(decimal));
            dt.Columns.Add("MeritoMassimoConseguibile_Attuale", typeof(decimal));
            dt.Columns.Add("CoefficienteCongiunto_Attuale", typeof(decimal));
            dt.Columns.Add("MeritoConseguito_Calcolato", typeof(decimal));
            dt.Columns.Add("MeritoMinimoPrevisto_Calcolato", typeof(decimal));
            dt.Columns.Add("MeritoMassimoConseguibile_Calcolato", typeof(decimal));
            dt.Columns.Add("CFN_Calcolato", typeof(decimal));
            dt.Columns.Add("MediaVoti_Calcolata", typeof(decimal));
            dt.Columns.Add("MediaN_Calcolata", typeof(decimal));
            dt.Columns.Add("ISEEDSU_Coefficiente", typeof(decimal));
            dt.Columns.Add("ISEEMax_Coefficiente", typeof(decimal));
            dt.Columns.Add("ISEEN_Calcolato", typeof(decimal));
            dt.Columns.Add("CoefficienteCongiunto_Calcolato", typeof(decimal));
            dt.Columns.Add("DifferenzaCFN", typeof(decimal));
            dt.Columns.Add("DifferenzaCoefficienteCongiunto", typeof(decimal));
            dt.Columns.Add("EsitoConfrontoCoefficienteCongiunto", typeof(string));

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
                    AddOutputRow(dt, context, pair.Key, info);
                }
            }
            finally
            {
                dt.EndLoadData();
            }

            return (items, dt);
        }

        private static void AddOutputRow(DataTable dt, VerificaPipelineContext context, StudentKey key, StudenteInfo info)
        {
            var eco = info.InformazioniEconomiche;
            var sede = info.InformazioniSede;
            var dom = info.InformazioniSede.Domicilio;
            var outcomeDomicili = sede.OutcomeDomicili;
            var iscr = info.InformazioniIscrizione;
            var impBorsa = info.InformazioniImportoBorsa;
            decimal? importoAssegnatoBsAttuale = GetImportoAssegnato(context, key, "BS");
            decimal? importoAssegnatoBsExport = importoAssegnatoBsAttuale
                                                ?? ToNullableDecimal(eco.Raw.ImportoAssegnato);

            var row = dt.NewRow();
            row["CodFiscale"] = info.InformazioniPersonali.CodFiscale ?? "";
            row["NumDomanda"] = info.InformazioniPersonali.NumDomanda ?? "";
            row["StatusCompilazione"] = info.StatusCompilazione;
            row["Verifica provvedimenti esclusione"] = info.VerificaProvvedimentiEsclusioneBs ?? "";

            FillBenefitOutcomeColumns(row, context, key);

            row["TipoRedditoOrigine"] = eco.Raw.TipoRedditoOrigine ?? "";
            row["TipoRedditoIntegrazione"] = eco.Raw.TipoRedditoIntegrazione ?? "";

            context.TryGetEsitoBorsaFacts(key, out var facts);
            row["HasIseeBaseEntroScadenza"] = facts?.HasIseeBaseEntroScadenza == true;
            row["HasCOUniversitarioEntroScadenza"] = facts?.HasCoUniversitarioEntroScadenza == true;
            row["HasCOOrdinarioConIntegrazioneEsteriEntroScadenza"] = facts?.HasCoOrdinarioConIntegrazioneEsteriEntroScadenza == true;
            row["HasCOOrdinarioSemestreFiltroEntroScadenza"] = facts?.HasCoOrdinarioSemestreFiltroEntroScadenza == true;
            row["UltimaCOImportataOrdinariaSenzaUniversitaria"] = facts?.UltimaCoImportataOrdinariaSenzaUniversitaria == true;
            row["IseeOrdinarioInAttesaRegolarizzazione"] = facts?.IseeOrdinarioInAttesaRegolarizzazione == true;
            var codiciBlocchiIncongruenze = facts?.CodiciBlocchiIncongruenze
                .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            row["CodiciBlocchiIncongruenze"] = string.Join(";", codiciBlocchiIncongruenze);
            row["DescrizioneBlocchiIncongruenze"] = string.Join(
                " | ",
                codiciBlocchiIncongruenze.Select(VerificaBlocchiIncongruenzeCatalog.GetDescrizione));
            var codiciIncongruenze = facts?.CodiciIncongruenzeNonEscludenti
                .OrderBy(static code => code, StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();
            row["CodiciIncongruenzeNonEscludenti"] = string.Join(";", codiciIncongruenze);
            row["DescrizioneIncongruenzeNonEscludenti"] = string.Join(
                " | ",
                codiciIncongruenze.Select(VerificaBlocchiIncongruenzeCatalog.GetDescrizioneIncongruenza));
            row["HasCIUniversitarioEntroScadenza"] = facts?.HasCiUniversitarioEntroScadenza == true;
            row["OrigineEconomicaAdeguata"] = facts?.OrigineEconomicaAdeguata == true;
            row["MotivoAdeguatezzaOrigine"] = facts?.MotivoAdeguatezzaOrigine ?? "";

            SetNullableDecimal(
                row,
                "ImportoAssegnatoBS",
                importoAssegnatoBsExport);
            SetNullableDecimal(row, "ISR", eco.Calcolate.ISRDSU);
            SetNullableDecimal(row, "ISP", eco.Calcolate.ISPDSU);
            SetNullableDecimal(row, "Detrazioni", eco.Calcolate.Detrazioni);
            SetNullableDecimal(row, "ISEDSU", eco.Calcolate.ISEDSU);
            SetNullableDecimal(row, "ISEEDSU", eco.Calcolate.ISEEDSU);
            SetNullableDecimal(row, "ISPEDSU", eco.Calcolate.ISPEDSU);
            SetNullableDecimal(row, "ISPDSU", eco.Calcolate.ISPDSU);
            SetNullableDecimal(row, "SEQ", eco.Calcolate.SEQ);
            SetNullableDecimal(row, "ISEDSU_Attuale", ToNullableDecimal(eco.Attuali.ISEDSU));
            SetNullableDecimal(row, "ISEEDSU_Attuale", ToNullableDecimal(eco.Attuali.ISEEDSU));
            SetNullableDecimal(row, "ISPEDSU_Attuale", ToNullableDecimal(eco.Attuali.ISPEDSU));
            SetNullableDecimal(row, "ISPDSU_Attuale", ToNullableDecimal(eco.Attuali.ISPDSU));
            SetNullableDecimal(row, "SEQ_Attuale", ToNullableDecimal(eco.Attuali.SEQ));

            row["StatusSedeAttuale"] = sede.StatusSede ?? "";
            row["StatusSedeSuggerito"] = sede.StatusSedeSuggerito ?? "";
            row["MotivoStatusSede"] = sede.MotivoStatusSede ?? "";
            row["ComuneResidenza"] = sede.Residenza.codComune ?? "";
            row["ProvinciaResidenza"] = sede.Residenza.provincia ?? "";
            row["ComuneSedeStudi"] = info.InformazioniIscrizione.ComuneSedeStudi ?? "";
            row["ProvinciaSede"] = info.InformazioniIscrizione.ProvinciaSedeStudi ?? "";
            row["ComuneDomicilio"] = sede.UsaNuovaGestioneDomicili
                ? string.Join(", ", outcomeDomicili?.ComuniCoinvolti ?? new List<string>())
                : dom?.codComuneDomicilio ?? "";
            row["SerieContrattoDomicilio"] = dom?.codiceSerieLocazione ?? "";
            row["DataRegistrazioneDomicilio"] = FormatDateForExport(dom?.dataRegistrazioneLocazione);
            row["DataDecorrenzaDomicilio"] = sede.UsaNuovaGestioneDomicili
                ? FormatDateForExport(outcomeDomicili?.DataInizioTotale)
                : FormatDateForExport(dom?.dataDecorrenzaLocazione);
            row["DataScadenzaDomicilio"] = sede.UsaNuovaGestioneDomicili
                ? FormatDateForExport(outcomeDomicili?.DataFineTotale)
                : FormatDateForExport(dom?.dataScadenzaLocazione);
            row["ProrogatoDomicilio"] = dom?.prorogatoLocazione ?? false;
            row["SerieProrogaDomicilio"] = dom?.codiceSerieProrogaLocazione ?? "";
            row["DomicilioPresente"] = sede.DomicilioPresente;
            row["DomicilioValido"] = sede.DomicilioValido;
            row["GestioneDomicili"] = sede.UsaNuovaGestioneDomicili
                ? "DOMICILI_CONTRATTI_PROROGHE"
                : "LUOGO_REPERIBILITA_STUDENTE";
            row["EsitoAnalisiDomicili"] = outcomeDomicili?.Esito.ToString() ?? "";
            row["DataInizioTotaleDomicili"] = FormatDateForExport(outcomeDomicili?.DataInizioTotale);
            row["DataFineTotaleDomicili"] = FormatDateForExport(outcomeDomicili?.DataFineTotale);
            SetIfHasValue(row, "MesiCopertiDomicili", outcomeDomicili?.MesiCoperti);
            row["ValidoCertoPerSaldoDomicili"] = outcomeDomicili?.ValidoCertoPerSaldo == true;
            row["MotivoAnalisiDomicili"] = outcomeDomicili?.Motivo ?? "";
            row["AnomalieAnalisiDomicili"] = outcomeDomicili == null
                ? ""
                : string.Join(" | ", outcomeDomicili.Anomalie);
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
            int annoCorsoCalcolato = EsitoBorsaSupport.GetAnnoCorsoCalcolato(iscr, facts, aaInizio, aaNumero);
            bool ripetenteDaPassaggio = aaNumero >= 20252026
                                        && ((facts?.RipetenteDaPassaggio).HasValue == true
                                            ? facts!.RipetenteDaPassaggio!.Value
                                            : iscr.HaRipetenzaCarrieraPregressa != 0);
            int annoCorsoRiferimentoBeneficio = iscr.AnnoCorso;

            row["AnnoCorsoCalcolatoMerito"] = annoCorsoCalcolato != 0 ? annoCorsoCalcolato : DBNull.Value;
            row["AnnoCorsoRiferimentoBeneficio"] = annoCorsoRiferimentoBeneficio != 0 ? annoCorsoRiferimentoBeneficio : DBNull.Value;
            row["CodTipoOrdinamentoMerito"] = facts?.CodTipoOrdinamento ?? "";
            row["PassaggioTrasferimentoMerito"] = facts?.PassaggioTrasferimento == true;
            row["RipetenteDaPassaggioMerito"] = ripetenteDaPassaggio;
            row["PrimaImmatricolazTsMerito"] = facts?.PrimaImmatricolazTs ?? (object)DBNull.Value;
            row["RegolaMeritoApplicata"] = iscr.RegolaMeritoApplicata ?? "";
            row["FaseElaborativaVerifica"] = context.FaseElaborativa.ToString();
            SetIfHasValue(row, "TipoStudenteNormalizzato", facts?.TipoStudenteNormalizzato);
            row["DiagnosticaIscrizioneVB"] = facts?.DiagnosticaIscrizione ?? string.Empty;
            SetIfHasValue(row, "EsamiMinimiRichiestiMerito", iscr.EsamiMinimiRichiestiMerito);
            SetIfHasValue(row, "CreditiMinimiRichiestiMerito", iscr.CreditiMinimiRichiestiMerito);
            SetIfHasValue(row, "IdCreditiRichiestiSelezionato", iscr.IdCreditiRichiestiSelezionato);
            SetIfHasValue(row, "AnnoCreditiRichiestiSelezionato", iscr.AnnoCreditiRichiestiSelezionato);
            row["CodCorsoCreditiRichiestiSelezionato"] = iscr.CodCorsoCreditiRichiestiSelezionato ?? string.Empty;
            SetIfHasValue(row, "SogliaCreditiSelezionata", iscr.SogliaCreditiSelezionata);
            SetIfHasValue(row, "EsamiMinimiRichiestiPassaggioMerito", iscr.EsamiMinimiRichiestiPassaggio);
            SetIfHasValue(row, "CreditiMinimiRichiestiPassaggioMerito", iscr.CreditiMinimiRichiestiPassaggio);
            row["SlashMotiviEsclusioneBS"] = facts?.SlashMotiviEsclusioneBS ?? "";
            row["VariazioniEscludentiBS"] = EsitoBorsaSupport.GetVariazioniEscludentiBsSummary(facts);
            row["RinunciaBSDaVariazioni"] = facts?.RinunciaBS == true;
            row["DecadutoBSDaVariazioni"] = facts?.DecadutoBS == true;
            row["RevocatoDaVariazioni"] = facts != null && (facts.Revocato || facts.RevocatoMancataIscrizione || facts.RevocatoIscrittoRipetente || facts.RevocatoISEE || facts.RevocatoLaureato || facts.RevocatoPatrimonio || facts.RevocatoReddito || facts.RevocatoEsami || facts.RevocatoFuoriTermine || facts.RevocatoIseeFuoriTermine || facts.RevocatoIseeNonProdotta || facts.RevocatoTrasmissioneIseeFuoriTermine || facts.RevocatoNoContrattoLocazione);
            row["RevocatoBandoBSDaVariazioni"] = facts?.RevocatoBandoBS == true;

            SetIfPositiveInt(row, "NumeroEventiCarrieraPregressa", iscr.NumeroEventiCarrieraPregressa);
            SetIfHasValue(row, "UltimoAnnoAvvenimentoCarrieraPregressa", iscr.UltimoAnnoAvvenimentoCarrieraPregressa);
            row["TotaleCreditiCarrieraPregressa"] = iscr.TotaleCreditiCarrieraPregressa;
            row["HaPassaggioCorsoEsteroCarrieraPregressa"] = iscr.HaPassaggioCorsoEsteroCarrieraPregressa != 0;
            row["HaRipetenzaCarrieraPregressa"] = iscr.HaRipetenzaCarrieraPregressa != 0;
            row["CodiciAvvenimentoCarrieraPregressa"] = iscr.CodiciAvvenimentoCarrieraPregressa ?? "";
            row["BorsaPregressaNonRestituitaConfliggente"] = facts?.BorsaPregressaNonRestituitaConfliggente == true;
            SetIfHasValue(row, "AnnoBorsaRichiestoNormalizzato", facts?.AnnoBorsaRichiestoNormalizzato);
            row["AnniBorsaPregressaUsufruitiNormalizzati"] = facts?.AnniBorsaPregressaUsufruitiNormalizzati ?? "";
            row["AnniBorsaPregressaRestituitiNormalizzati"] = facts?.AnniBorsaPregressaRestituitiNormalizzati ?? "";
            row["AnniBorsaPregressaNonRestituitaConfliggenti"] = facts?.AnniBorsaPregressaNonRestituitaConfliggenti ?? "";
            row["BorsaPregressaEsteraNonRichiedeRestituzione"] = facts?.BorsaPregressaEsteraNonRichiedeRestituzione == true;
            row["DiagnosticaBorsaPregressaRestituzioni"] = facts?.DiagnosticaBorsaPregressaRestituzioni ?? "";
            row["BorsaStoricaStessoAnnoConfliggente"] = context.BorsaStoricaStessoAnnoConflitti.Contains(key);
            row["BorsaStoricaRichiedeRevisione"] = context.BorsaStoricaRichiedeRevisione.Contains(key);
            row["DiagnosticaBorsaStoricaStessoAnno"] = context.GetDiagnosticaBorsaStoricaStessoAnno(key);

            row["StatusSedeRiferimentoImportoBorsa"] = impBorsa.StatusSedeRiferimento ?? "";
            SetNullableDecimal(row, "ImportoBaseBorsa", impBorsa.ImportoBase);
            SetNullableDecimal(row, "ImportoFinaleBorsa", impBorsa.ImportoFinale);
            SetNullableDecimal(row, "ImportoSpecificheImpegniBS", impBorsa.ImportoSpecificheImpegniBs);
            row["SpecificheImpegniUgualeImportoAssegnatoBS"] =
                MoneyEquals(impBorsa.ImportoSpecificheImpegniBs, importoAssegnatoBsAttuale) ? "SI" : "NO";
            row["SpecificheImpegniUgualeImportoCalcolatoBS"] =
                MoneyEquals(impBorsa.ImportoSpecificheImpegniBs, impBorsa.ImportoFinale) ? "SI" : "NO";

            FillCoefficienteCongiuntoColumns(row, context, key, info);

            dt.Rows.Add(row);
        }

        private static void FillCoefficienteCongiuntoColumns(
            DataRow row,
            VerificaPipelineContext context,
            StudentKey key,
            StudenteInfo info)
        {
            var iscr = info.InformazioniIscrizione;
            var attuale = iscr.CoefficienteCongiunto;

            SetNullableDecimal(row, "CFN_Attuale", attuale.CFN);
            SetNullableDecimal(row, "MeritoConseguito_Attuale", attuale.MeritoConseguito);
            SetNullableDecimal(row, "MeritoMinimoPrevisto_Attuale", attuale.MeritoMinimoPrevisto);
            SetNullableDecimal(row, "MeritoMassimoConseguibile_Attuale", attuale.MeritoMassimoConseguibile);
            SetNullableDecimal(row, "CoefficienteCongiunto_Attuale", attuale.CoefficienteCongiunto);

            int? esitoBsAttuale = GetEsitoAttuale(context, key, "BS");
            if (esitoBsAttuale != 1 && esitoBsAttuale != 2)
            {
                row["EsitoConfrontoCoefficienteCongiunto"] = esitoBsAttuale == 0
                    ? "NON APPLICABILE - studente escluso"
                    : "NON APPLICABILE - studente non idoneo/vincitore o esito BS assente";
                return;
            }

            decimal? meritoConseguito = iscr.NumeroCrediti.HasValue
                ? iscr.NumeroCrediti.Value + Math.Max(iscr.CreditiUtilizzati ?? 0m, 0m)
                : null;
            decimal? meritoMinimo = iscr.CreditiMinimiRichiestiMerito
                                    ?? attuale.MeritoMinimoPrevisto;
            decimal? meritoMassimo = attuale.MeritoMassimoConseguibile;
            decimal? cfn = CalculateNormalizedValue(meritoConseguito, meritoMinimo, meritoMassimo);

            decimal? media = iscr.NumeroEsami > 0 && iscr.SommaVoti.HasValue
                ? iscr.SommaVoti.Value / iscr.NumeroEsami.Value
                : null;
            decimal? mediaN = media.HasValue
                ? Clamp01((media.Value - 18m) / 12m)
                : null;

            decimal? iseeDsu = info.InformazioniEconomiche.Calcolate.ISEEDSU;
            bool residenteEstero = string.Equals(
                info.InformazioniSede.Residenza.provincia?.Trim(),
                "EE",
                StringComparison.OrdinalIgnoreCase);
            decimal? iseePerCoefficiente = iseeDsu.HasValue && residenteEstero
                ? Math.Max(iseeDsu.Value, IseeMinimoResidentiEsteroPerCoefficiente)
                : iseeDsu;
            decimal? iseeMax = context.CalcParams?.SogliaIsee > 0m
                ? context.CalcParams.SogliaIsee
                : null;
            decimal? iseeN = iseePerCoefficiente.HasValue && iseeMax.HasValue
                ? Clamp01(1m - (iseePerCoefficiente.Value / iseeMax.Value))
                : null;

            decimal? coefficiente = cfn.HasValue && mediaN.HasValue && iseeN.HasValue
                ? RoundCoefficient((0.70m * cfn.Value) + (0.04m * mediaN.Value) + (0.26m * iseeN.Value))
                : null;

            SetNullableDecimal(row, "MeritoConseguito_Calcolato", meritoConseguito);
            SetNullableDecimal(row, "MeritoMinimoPrevisto_Calcolato", meritoMinimo);
            SetNullableDecimal(row, "MeritoMassimoConseguibile_Calcolato", meritoMassimo);
            SetNullableDecimal(row, "CFN_Calcolato", cfn);
            SetNullableDecimal(row, "MediaVoti_Calcolata", media.HasValue ? RoundCoefficient(media.Value) : null);
            SetNullableDecimal(row, "MediaN_Calcolata", mediaN.HasValue ? RoundCoefficient(mediaN.Value) : null);
            SetNullableDecimal(row, "ISEEDSU_Coefficiente", iseePerCoefficiente);
            SetNullableDecimal(row, "ISEEMax_Coefficiente", iseeMax);
            SetNullableDecimal(row, "ISEEN_Calcolato", iseeN.HasValue ? RoundCoefficient(iseeN.Value) : null);
            SetNullableDecimal(row, "CoefficienteCongiunto_Calcolato", coefficiente);

            decimal? differenzaCfn = Difference(cfn, attuale.CFN);
            decimal? differenzaCoefficiente = Difference(coefficiente, attuale.CoefficienteCongiunto);
            SetNullableDecimal(row, "DifferenzaCFN", differenzaCfn);
            SetNullableDecimal(row, "DifferenzaCoefficienteCongiunto", differenzaCoefficiente);
            row["EsitoConfrontoCoefficienteCongiunto"] =
                BuildCoefficientComparisonMessage(attuale, cfn, mediaN, iseeN, coefficiente, differenzaCoefficiente);
        }

        private static decimal? CalculateNormalizedValue(decimal? value, decimal? minimum, decimal? maximum)
        {
            if (!value.HasValue || !minimum.HasValue || !maximum.HasValue)
                return null;

            decimal range = maximum.Value - minimum.Value;
            if (range <= 0m)
                return null;

            return RoundCoefficient(Clamp01((value.Value - minimum.Value) / range));
        }

        private static decimal Clamp01(decimal value)
            => Math.Min(Math.Max(value, 0m), 1m);

        private static decimal RoundCoefficient(decimal value)
            => decimal.Round(value, 6, MidpointRounding.AwayFromZero);

        private static decimal? Difference(decimal? calculated, decimal? current)
            => calculated.HasValue && current.HasValue
                ? RoundCoefficient(calculated.Value - current.Value)
                : null;

        private static string BuildCoefficientComparisonMessage(
            CoefficienteCongiuntoAttuale current,
            decimal? cfn,
            decimal? mediaN,
            decimal? iseeN,
            decimal? calculated,
            decimal? difference)
        {
            if (!current.MeritoMassimoConseguibile.HasValue)
                return "NON CALCOLABILE - merito massimo conseguibile assente nella vista";
            if (!cfn.HasValue)
                return "NON CALCOLABILE - dati merito insufficienti o intervallo merito non valido";
            if (!mediaN.HasValue)
                return "NON CALCOLABILE - numero esami o somma voti assenti";
            if (!iseeN.HasValue)
                return "NON CALCOLABILE - ISEEDSU o limite ISEE di bando assente";
            if (!current.CoefficienteCongiunto.HasValue)
                return "CALCOLATO - coefficiente attuale assente nella vista";
            if (!difference.HasValue)
                return "NON CONFRONTABILE";

            return Math.Abs(difference.Value) <= 0.000001m
                ? "COERENTE - coefficiente calcolato uguale al valore attuale"
                : $"DIFFERENTE - scostamento {difference.Value.ToString("0.000000", CultureInfo.InvariantCulture)}";
        }

        private static void FillBenefitOutcomeColumns(DataRow row, VerificaPipelineContext context, StudentKey key)
        {
            context.TryGetEsitoBorsaFacts(key, out var facts);
            context.TryGetEsitiConcorsoByBenefit(key, out var rawByBenefit);
            context.TryGetEsitiCalcolatiByBenefit(key, out var calcolatiByBenefit);

            foreach (var codBeneficio in OutputExclusionReasonBenefitCodes)
            {
                row[$"CodiciEsclusioneCalcolata_{codBeneficio}"] = string.Empty;
                row[$"DescrizioneEsclusioneCalcolata_{codBeneficio}"] = string.Empty;
            }

            foreach (var codBeneficio in OutputBenefitCodes)
            {
                bool richiesto = facts != null && EsitoBorsaSupport.IsBenefitRequested(facts, codBeneficio);
                if (!richiesto)
                {
                    SetNullableInt(row, $"EsitoAttuale_{codBeneficio}", null);
                    SetNullableInt(row, $"EsitoCalcolato_{codBeneficio}", null);
                    continue;
                }

                int? esitoAttuale = null;
                int? esitoCalcolato = null;

                if (rawByBenefit != null &&
                    rawByBenefit.TryGetValue(codBeneficio, out var raw) &&
                    raw != null)
                {
                    esitoAttuale = raw.CodTipoEsito;
                }

                if (calcolatiByBenefit != null &&
                    calcolatiByBenefit.TryGetValue(codBeneficio, out var calcolato) &&
                    calcolato != null)
                {
                    esitoCalcolato = calcolato.EsitoCalcolato;

                    if (OutputExclusionReasonBenefitCodes.Contains(codBeneficio, StringComparer.OrdinalIgnoreCase))
                    {
                        row[$"CodiciEsclusioneCalcolata_{codBeneficio}"] = calcolato.CodiciMotivo ?? string.Empty;
                        row[$"DescrizioneEsclusioneCalcolata_{codBeneficio}"] = calcolato.Motivi ?? string.Empty;
                    }
                }

                SetNullableInt(row, $"EsitoAttuale_{codBeneficio}", esitoAttuale);
                SetNullableInt(row, $"EsitoCalcolato_{codBeneficio}", esitoCalcolato);
            }
        }

        private static decimal? GetImportoAssegnato(VerificaPipelineContext context, StudentKey key, string codBeneficio)
        {
            if (context.TryGetEsitiConcorsoByBenefit(key, out var rawByBenefit) &&
                rawByBenefit != null &&
                rawByBenefit.TryGetValue(codBeneficio, out var raw) &&
                raw != null)
            {
                return raw.ImportoAssegnato;
            }

            return null;
        }

        private static int? GetEsitoAttuale(VerificaPipelineContext context, StudentKey key, string codBeneficio)
        {
            if (context.TryGetEsitiConcorsoByBenefit(key, out var rawByBenefit) &&
                rawByBenefit != null &&
                rawByBenefit.TryGetValue(codBeneficio, out var raw) &&
                raw != null)
            {
                return raw.CodTipoEsito;
            }

            return null;
        }

        private static decimal? ToNullableDecimal(object? value)
        {
            if (value == null || value == DBNull.Value)
                return null;

            try
            {
                return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }

        private static void SetNullableDecimal(DataRow row, string columnName, decimal? value)
        {
            row[columnName] = value.HasValue ? value.Value : DBNull.Value;
        }

        private static void SetIfHasValue(DataRow row, string columnName, object? value)
        {
            row[columnName] = value ?? DBNull.Value;
        }

        private static void SetNullableInt(DataRow row, string columnName, int? value)
        {
            row[columnName] = value.HasValue ? value.Value : DBNull.Value;
        }

        private static void SetIfPositiveInt(DataRow row, string columnName, int value)
        {
            row[columnName] = value > 0 ? value : DBNull.Value;
        }

        private static bool MoneyEquals(decimal? left, decimal? right)
        {
            if (!left.HasValue || !right.HasValue)
                return false;

            return decimal.Round(left.Value, 2, MidpointRounding.AwayFromZero) ==
                   decimal.Round(right.Value, 2, MidpointRounding.AwayFromZero);
        }

        private static string FormatDateForExport(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue || value.Value.Year < 1900)
                return "";

            return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
