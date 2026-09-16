using System;
using System.Data;
using System.Globalization;
using System.Linq;

namespace ProcedureNet7.Verifica
{
    internal sealed partial class Verifica
    {
        private static DataTable BuildDomiciliDiagnosticTable()
        {
            var table = new DataTable("DiagnosticaDomicili");

            table.Columns.Add("CodFiscale", typeof(string));
            table.Columns.Add("NumDomanda", typeof(string));
            table.Columns.Add("AnnoAccademico", typeof(string));
            table.Columns.Add("FaseElaborativa", typeof(string));
            table.Columns.Add("DataRiferimento", typeof(string));
            table.Columns.Add("GestioneDomicili", typeof(string));
            table.Columns.Add("TipoRiga", typeof(string));

            table.Columns.Add("StatusSedeAttuale", typeof(string));
            table.Columns.Add("StatusSedeSuggerito", typeof(string));
            table.Columns.Add("MotivoStatusSede", typeof(string));
            table.Columns.Add("ComuneResidenza", typeof(string));
            table.Columns.Add("ProvinciaResidenza", typeof(string));
            table.Columns.Add("ComuneSedeStudi", typeof(string));
            table.Columns.Add("ProvinciaSedeStudi", typeof(string));
            table.Columns.Add("MinMesiRichiesti", typeof(int));
            table.Columns.Add("GiorniMassimiInterruzione", typeof(int));
            table.Columns.Add("FinestraProrogaTrentaGiorni", typeof(bool));

            table.Columns.Add("DomicilioId", typeof(long));
            table.Columns.Add("DomicilioUtilizzato", typeof(bool));
            table.Columns.Add("CodComuneDomicilio", typeof(string));
            table.Columns.Add("IndirizzoDomicilio", typeof(string));
            table.Columns.Add("NumeroCivicoDomicilio", typeof(string));
            table.Columns.Add("CapDomicilio", typeof(string));
            table.Columns.Add("TipoDomicilioCodice", typeof(int));
            table.Columns.Add("TipoDomicilioDescrizione", typeof(string));
            table.Columns.Add("DataInizioDomicilio", typeof(string));
            table.Columns.Add("DataFineDomicilio", typeof(string));
            table.Columns.Add("InserimentoDatiContratto", typeof(bool));

            table.Columns.Add("ContrattoId", typeof(long));
            table.Columns.Add("ContrattoUtilizzato", typeof(bool));
            table.Columns.Add("TipoContrattoCodice", typeof(int));
            table.Columns.Add("TipoContrattoDescrizione", typeof(string));
            table.Columns.Add("NumeroSerieContratto", typeof(string));
            table.Columns.Add("DataRegistrazioneContratto", typeof(string));
            table.Columns.Add("DenominazioneEnte", typeof(string));
            table.Columns.Add("ImportoRata", typeof(decimal));
            table.Columns.Add("TipoEnte", typeof(string));
            table.Columns.Add("NumeroSerieSubentro", typeof(string));
            table.Columns.Add("DataInizioSubentro", typeof(string));
            table.Columns.Add("DataCessazione", typeof(string));
            table.Columns.Add("DataInizioContratto", typeof(string));
            table.Columns.Add("DataFineContratto", typeof(string));

            table.Columns.Add("ProrogaId", typeof(long));
            table.Columns.Add("ProrogaUtilizzata", typeof(bool));
            table.Columns.Add("NumeroSerieProroga", typeof(string));
            table.Columns.Add("DataDecorrenzaProroga", typeof(string));
            table.Columns.Add("DataScadenzaProroga", typeof(string));

            table.Columns.Add("EsitoAnalisi", typeof(string));
            table.Columns.Add("DomicilioPresente", typeof(bool));
            table.Columns.Add("DomicilioValido", typeof(bool));
            table.Columns.Add("EsitoProvvisorio", typeof(bool));
            table.Columns.Add("ValidoCertoPerSaldo", typeof(bool));
            table.Columns.Add("DataInizioTotale", typeof(string));
            table.Columns.Add("DataFineTotale", typeof(string));
            table.Columns.Add("DataInizioCoperturaAA", typeof(string));
            table.Columns.Add("DataFineCoperturaAA", typeof(string));
            table.Columns.Add("MesiCoperti", typeof(int));
            table.Columns.Add("GiorniCoperti", typeof(int));
            table.Columns.Add("MassimoBucoGiorni", typeof(int));
            table.Columns.Add("ContieneContrattoEnte", typeof(bool));
            table.Columns.Add("ContieneContrattoErasmus", typeof(bool));
            table.Columns.Add("ComuniCoinvolti", typeof(string));
            table.Columns.Add("DomiciliUtilizzati", typeof(string));
            table.Columns.Add("ContrattiUtilizzati", typeof(string));
            table.Columns.Add("ProrogheUtilizzate", typeof(string));
            table.Columns.Add("MotivoAnalisi", typeof(string));
            table.Columns.Add("AnomalieAnalisi", typeof(string));

            return table;
        }

        private static DataTable BuildDomiciliDiagnosticTable(VerificaPipelineContext context)
        {
            DataTable table = BuildDomiciliDiagnosticTable();

            foreach (var pair in VerificaExecutionSupport.OrderStudents(context.Students))
            {
                StudenteInfo info = pair.Value;
                var sede = info.InformazioniSede;

                if (!sede.UsaNuovaGestioneDomicili)
                {
                    AddLegacyDomicilioDiagnosticRow(table, context, info);
                    continue;
                }

                if (sede.DomiciliNuovaGestione.Count == 0)
                {
                    AddDomicilioDiagnosticRow(
                        table,
                        context,
                        info,
                        "NESSUN_DOMICILIO_ATTIVO",
                        null,
                        null,
                        null);
                    continue;
                }

                foreach (DomicilioAnalisiInput domicilio in sede.DomiciliNuovaGestione
                    .OrderBy(item => item.Id))
                {
                    if (domicilio.Contratti.Count == 0)
                    {
                        AddDomicilioDiagnosticRow(
                            table,
                            context,
                            info,
                            "DOMICILIO_SENZA_CONTRATTO",
                            domicilio,
                            null,
                            null);
                        continue;
                    }

                    foreach (ContrattoDomicilioAnalisiInput contratto in domicilio.Contratti
                        .OrderBy(item => item.Id))
                    {
                        if (contratto.Proroghe.Count == 0)
                        {
                            AddDomicilioDiagnosticRow(
                                table,
                                context,
                                info,
                                "CONTRATTO_SENZA_PROROGA",
                                domicilio,
                                contratto,
                                null);
                            continue;
                        }

                        foreach (ProrogaDomicilioAnalisiInput proroga in contratto.Proroghe
                            .OrderBy(item => item.DataDecorrenza)
                            .ThenBy(item => item.Id))
                        {
                            AddDomicilioDiagnosticRow(
                                table,
                                context,
                                info,
                                "CONTRATTO_CON_PROROGA",
                                domicilio,
                                contratto,
                                proroga);
                        }
                    }
                }
            }

            return table;
        }

        private static void AddDomicilioDiagnosticRow(
            DataTable table,
            VerificaPipelineContext context,
            StudenteInfo info,
            string tipoRiga,
            DomicilioAnalisiInput? domicilio,
            ContrattoDomicilioAnalisiInput? contratto,
            ProrogaDomicilioAnalisiInput? proroga)
        {
            DataRow row = CreateDiagnosticBaseRow(
                table,
                context,
                info,
                "DOMICILI/CONTRATTI/PROROGHE",
                tipoRiga);
            OutcomeAnalisiDomicili? outcome = info.InformazioniSede.OutcomeDomicili;

            if (domicilio != null)
            {
                row["DomicilioId"] = domicilio.Id;
                row["DomicilioUtilizzato"] = outcome?.IdDomiciliUtilizzati.Contains(domicilio.Id) == true;
                row["CodComuneDomicilio"] = domicilio.CodComune;
                row["IndirizzoDomicilio"] = domicilio.Indirizzo;
                row["NumeroCivicoDomicilio"] = domicilio.NumeroCivico;
                row["CapDomicilio"] = domicilio.Cap;
                row["TipoDomicilioCodice"] = (int)domicilio.TipoDomicilio;
                row["TipoDomicilioDescrizione"] = domicilio.TipoDomicilio.ToString();
                row["DataInizioDomicilio"] = FormatDiagnosticDate(domicilio.DataInizio);
                row["DataFineDomicilio"] = FormatDiagnosticDate(domicilio.DataFine);
                row["InserimentoDatiContratto"] = domicilio.InserimentoDatiContratto;
            }

            if (contratto != null)
            {
                row["ContrattoId"] = contratto.Id;
                row["ContrattoUtilizzato"] = outcome?.IdContrattiUtilizzati.Contains(contratto.Id) == true;
                row["TipoContrattoCodice"] = (int)contratto.TipoContratto;
                row["TipoContrattoDescrizione"] = contratto.TipoContratto.ToString();
                row["NumeroSerieContratto"] = contratto.NumeroSerieContratto;
                row["DataRegistrazioneContratto"] = FormatDiagnosticDate(contratto.DataRegistrazioneContratto);
                row["DenominazioneEnte"] = contratto.DenominazioneEnte;
                row["ImportoRata"] = contratto.ImportoRata ?? (object)DBNull.Value;
                row["TipoEnte"] = contratto.TipoEnte;
                row["NumeroSerieSubentro"] = contratto.NumeroSerieSubentro;
                row["DataInizioSubentro"] = FormatDiagnosticDate(contratto.DataInizioSubentro);
                row["DataCessazione"] = FormatDiagnosticDate(contratto.DataCessazione);
                row["DataInizioContratto"] = FormatDiagnosticDate(contratto.DataInizio);
                row["DataFineContratto"] = FormatDiagnosticDate(contratto.DataFine);
            }

            if (proroga != null)
            {
                row["ProrogaId"] = proroga.Id;
                row["ProrogaUtilizzata"] = outcome?.IdProrogheUtilizzate.Contains(proroga.Id) == true;
                row["NumeroSerieProroga"] = proroga.NumeroSerieProroga;
                row["DataDecorrenzaProroga"] = FormatDiagnosticDate(proroga.DataDecorrenza);
                row["DataScadenzaProroga"] = FormatDiagnosticDate(proroga.DataScadenza);
            }

            FillDiagnosticOutcome(row, outcome);
            table.Rows.Add(row);
        }

        private static void AddLegacyDomicilioDiagnosticRow(
            DataTable table,
            VerificaPipelineContext context,
            StudenteInfo info)
        {
            var domicilio = info.InformazioniSede.Domicilio;
            DataRow row = CreateDiagnosticBaseRow(
                table,
                context,
                info,
                "GESTIONE_STORICA",
                domicilio.possiedeDomicilio ? "DOMICILIO_STORICO" : "NESSUN_DOMICILIO_STORICO");

            row["CodComuneDomicilio"] = domicilio.codComuneDomicilio ?? string.Empty;
            row["TipoDomicilioCodice"] = domicilio.titoloOneroso ? 1 : 0;
            row["TipoDomicilioDescrizione"] = domicilio.titoloOneroso ? "Oneroso" : "Gratuito";
            row["InserimentoDatiContratto"] = domicilio.conoscenzaDatiContratto;
            row["TipoContrattoCodice"] = domicilio.contrEnte ? 1 : 0;
            row["TipoContrattoDescrizione"] = domicilio.contrEnte ? "Ente" : "Registrato";
            row["NumeroSerieContratto"] = domicilio.codiceSerieLocazione ?? string.Empty;
            row["DataRegistrazioneContratto"] = FormatDiagnosticDate(domicilio.dataRegistrazioneLocazione);
            row["DenominazioneEnte"] = domicilio.denominazioneIstituto ?? string.Empty;
            row["ImportoRata"] = Convert.ToDecimal(domicilio.importoMensileRataIstituto, CultureInfo.InvariantCulture);
            row["TipoEnte"] = domicilio.TipoEnte ?? string.Empty;
            row["DataInizioContratto"] = FormatDiagnosticDate(domicilio.dataDecorrenzaLocazione);
            row["DataFineContratto"] = FormatDiagnosticDate(domicilio.dataScadenzaLocazione);
            row["NumeroSerieProroga"] = domicilio.codiceSerieProrogaLocazione ?? string.Empty;
            row["DomicilioPresente"] = info.InformazioniSede.DomicilioPresente;
            row["DomicilioValido"] = info.InformazioniSede.DomicilioValido;
            row["MotivoAnalisi"] = info.InformazioniSede.MotivoStatusSede ?? string.Empty;

            table.Rows.Add(row);
        }

        private static DataRow CreateDiagnosticBaseRow(
            DataTable table,
            VerificaPipelineContext context,
            StudenteInfo info,
            string gestione,
            string tipoRiga)
        {
            DataRow row = table.NewRow();
            var sede = info.InformazioniSede;

            row["CodFiscale"] = info.InformazioniPersonali.CodFiscale ?? string.Empty;
            row["NumDomanda"] = info.InformazioniPersonali.NumDomanda ?? string.Empty;
            row["AnnoAccademico"] = context.AnnoAccademico;
            row["FaseElaborativa"] = context.FaseElaborativa.ToString();
            row["DataRiferimento"] = FormatDiagnosticDate(context.ReferenceDate);
            row["GestioneDomicili"] = gestione;
            row["TipoRiga"] = tipoRiga;
            row["StatusSedeAttuale"] = sede.StatusSede ?? string.Empty;
            row["StatusSedeSuggerito"] = sede.StatusSedeSuggerito ?? string.Empty;
            row["MotivoStatusSede"] = sede.MotivoStatusSede ?? string.Empty;
            row["ComuneResidenza"] = sede.Residenza.codComune ?? string.Empty;
            row["ProvinciaResidenza"] = sede.Residenza.provincia ?? string.Empty;
            row["ComuneSedeStudi"] = sede.CodComuneSedeStudi ?? string.Empty;
            row["ProvinciaSedeStudi"] = sede.CodProvinciaSedeStudi ?? string.Empty;
            row["MinMesiRichiesti"] = sede.MinMesiDomicilioFuoriSede > 0
                ? sede.MinMesiDomicilioFuoriSede
                : 10;
            row["GiorniMassimiInterruzione"] = 15;
            row["FinestraProrogaTrentaGiorni"] = true;

            return row;
        }

        private static void FillDiagnosticOutcome(DataRow row, OutcomeAnalisiDomicili? outcome)
        {
            if (outcome == null)
                return;

            row["EsitoAnalisi"] = outcome.Esito.ToString();
            row["DomicilioPresente"] = outcome.Presente;
            row["DomicilioValido"] = outcome.Valido;
            row["EsitoProvvisorio"] = outcome.Provvisorio;
            row["ValidoCertoPerSaldo"] = outcome.ValidoCertoPerSaldo;
            row["DataInizioTotale"] = FormatDiagnosticDate(outcome.DataInizioTotale);
            row["DataFineTotale"] = FormatDiagnosticDate(outcome.DataFineTotale);
            row["DataInizioCoperturaAA"] = FormatDiagnosticDate(outcome.DataInizioCoperturaAnnoAccademico);
            row["DataFineCoperturaAA"] = FormatDiagnosticDate(outcome.DataFineCoperturaAnnoAccademico);
            row["MesiCoperti"] = outcome.MesiCoperti;
            row["GiorniCoperti"] = outcome.GiorniCoperti;
            row["MassimoBucoGiorni"] = outcome.MassimoBucoGiorni;
            row["ContieneContrattoEnte"] = outcome.ContieneContrattoEnte;
            row["ContieneContrattoErasmus"] = outcome.ContieneContrattoErasmus;
            row["ComuniCoinvolti"] = string.Join(" | ", outcome.ComuniCoinvolti);
            row["DomiciliUtilizzati"] = string.Join(" | ", outcome.IdDomiciliUtilizzati);
            row["ContrattiUtilizzati"] = string.Join(" | ", outcome.IdContrattiUtilizzati);
            row["ProrogheUtilizzate"] = string.Join(" | ", outcome.IdProrogheUtilizzate);
            row["MotivoAnalisi"] = outcome.Motivo;
            row["AnomalieAnalisi"] = string.Join(" | ", outcome.Anomalie);
        }

        private static string FormatDiagnosticDate(DateTime? value)
        {
            if (!value.HasValue || value.Value == DateTime.MinValue || value.Value.Year < 1900)
                return string.Empty;

            return value.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
    }
}
