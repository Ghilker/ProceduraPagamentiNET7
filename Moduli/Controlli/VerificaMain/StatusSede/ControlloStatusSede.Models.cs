using ProcedureNet7.Storni;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;

namespace ProcedureNet7
{
    internal sealed partial class ControlloStatusSede
    {
        private sealed class StatusSedeStudent
        {
            public StudenteInfo Info { get; init; } = new StudenteInfo();
            public StudentKey Key { get; init; }
            public bool AlwaysA { get; init; }
            public bool InSedeList { get; init; }
            public bool PendolareList { get; init; }
            public bool FuoriSedeList { get; init; }
            public bool HasAlloggio12 { get; init; }
            public int MinMesiDomicilioFuoriSede { get; init; }
            public bool HasIstanzaDomicilio { get; init; }
            public string CodTipoIstanzaDomicilio { get; init; } = "";
            public int NumIstanzaDomicilio { get; init; }
            public DomicilioSnapshot? IstanzaDomicilio { get; init; }
            public bool HasUltimaIstanzaChiusaDomicilio { get; init; }
            public string CodTipoUltimaIstanzaChiusaDomicilio { get; init; } = "";
            public int NumUltimaIstanzaChiusaDomicilio { get; init; }
            public string EsitoUltimaIstanzaChiusaDomicilio { get; init; } = "";
            public string UtentePresaCaricoUltimaIstanzaChiusaDomicilio { get; init; } = "";

            public static StatusSedeStudent FromRecord(
                IDataRecord record,
                IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey)
            {
                int numDomanda = record.SafeGetInt("NumDomanda");
                string codFiscale = record.SafeGetString("CodFiscale").Trim().ToUpperInvariant();
                string numDomandaTxt = numDomanda <= 0 ? string.Empty : numDomanda.ToString(CultureInfo.InvariantCulture);
                var key = new StudentKey(codFiscale, numDomandaTxt);

                StudenteInfo studenteInfo;
                if (infoByKey != null && infoByKey.TryGetValue(key, out var existingInfo) && existingInfo != null)
                    studenteInfo = existingInfo;
                else
                    studenteInfo = new StudenteInfo();

                studenteInfo.InformazioniPersonali.NumDomanda = numDomandaTxt;
                studenteInfo.InformazioniPersonali.CodFiscale = codFiscale;
                studenteInfo.InformazioniSede.StatusSede = record.SafeGetString("StatusSedeAttuale").Trim().ToUpperInvariant();
                studenteInfo.InformazioniSede.ForzaturaStatusSede = record.SafeGetString("ForcedStatus").Trim().ToUpperInvariant();
                bool alwaysA = record.SafeGetBool("AlwaysA");
                bool rifugiatoPolitico = record.SafeGetBool("RifugiatoPolitico");
                studenteInfo.InformazioniPersonali.Rifugiato = rifugiatoPolitico;
                int numeroComponentiNucleo = record.SafeGetInt("NumComponenti");
                int numeroComponentiNucleoEstero = record.SafeGetInt("NumConvEstero");
                studenteInfo.SetNucleoFamiliare(numeroComponentiNucleo, numeroComponentiNucleoEstero);

                string comuneResidenza = record.SafeGetString("ComuneResidenza").Trim();
                string provinciaResidenza = record.SafeGetString("ProvinciaResidenza").Trim().ToUpperInvariant();
                studenteInfo.SetResidenza(string.Empty, comuneResidenza, provinciaResidenza, string.Empty, comuneResidenza);

                studenteInfo.InformazioniIscrizione.CodSedeStudi = record.SafeGetString("CodSedeStudi").Trim().ToUpperInvariant();
                studenteInfo.InformazioniIscrizione.CodCorsoLaurea = record.SafeGetString("CodCorso").Trim();
                studenteInfo.InformazioniIscrizione.TipoCorso = record.SafeGetInt("CodTipoStudi");
                studenteInfo.InformazioniIscrizione.CodFacolta = record.SafeGetString("CodFacolta").Trim();
                studenteInfo.InformazioniIscrizione.ComuneSedeStudi = record.SafeGetString("ComuneSedeStudi").Trim();
                studenteInfo.InformazioniIscrizione.ProvinciaSedeStudi = record.SafeGetString("ProvinciaSede").Trim().ToUpperInvariant();

                bool inSedeList = record.SafeGetBool("InSedeList");
                bool pendolareList = record.SafeGetBool("PendolareList");
                bool fuoriSedeList = record.SafeGetBool("FuoriSedeList");
                bool hasAlloggio12 = record.SafeGetBool("HasAlloggio12");

                string comuneDomicilio = record.SafeGetString("ComuneDomicilio").Trim();
                bool titoloOneroso = record.SafeGetBool("TitoloOneroso");
                bool contrattoEnte = record.SafeGetBool("ContrattoEnte");
                string tipoEnte = record.SafeGetString("TipoEnte").Trim().ToUpperInvariant();
                string serieContratto = record.SafeGetString("SerieContratto").Trim();
                DateTime dataRegistrazione = record.SafeGetDateTime("DataRegistrazione");
                DateTime dataDecorrenza = record.SafeGetDateTime("DataDecorrenza");
                DateTime dataScadenza = record.SafeGetDateTime("DataScadenza");
                int durataContratto = record.SafeGetInt("DurataContratto");
                bool prorogato = record.SafeGetBool("Prorogato");
                int durataProroga = record.SafeGetInt("DurataProroga");
                string serieProroga = record.SafeGetString("SerieProroga").Trim();
                string denomEnte = record.SafeGetString("DenomEnte").Trim();
                double importoRataEnte = record.SafeGetDouble("ImportoRataEnte");
                int minMesiDomicilioFuoriSede = record.SafeGetInt("MinMesiDomicilioFuoriSede");

                studenteInfo.InformazioniSede.Domicilio.codComuneDomicilio = comuneDomicilio;
                studenteInfo.InformazioniSede.Domicilio.titoloOneroso = titoloOneroso;
                studenteInfo.InformazioniSede.Domicilio.contrEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.TipoEnte = tipoEnte;
                studenteInfo.InformazioniSede.Domicilio.codiceSerieLocazione = serieContratto;
                studenteInfo.InformazioniSede.Domicilio.dataRegistrazioneLocazione = dataRegistrazione;
                studenteInfo.InformazioniSede.Domicilio.dataDecorrenzaLocazione = dataDecorrenza;
                studenteInfo.InformazioniSede.Domicilio.dataScadenzaLocazione = dataScadenza;
                studenteInfo.InformazioniSede.Domicilio.durataMesiLocazione = durataContratto;
                studenteInfo.InformazioniSede.Domicilio.prorogatoLocazione = prorogato;
                studenteInfo.InformazioniSede.Domicilio.durataMesiProrogaLocazione = durataProroga;
                studenteInfo.InformazioniSede.Domicilio.codiceSerieProrogaLocazione = serieProroga;
                studenteInfo.InformazioniSede.ContrattoEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.contrEnte = contrattoEnte;
                studenteInfo.InformazioniSede.Domicilio.denominazioneIstituto = denomEnte;
                studenteInfo.InformazioniSede.Domicilio.importoMensileRataIstituto = importoRataEnte;

                bool hasIstanzaDomicilio = record.SafeGetBool("HasIstanzaDomicilio");
                int numIstanzaDomicilio = record.SafeGetInt("NumIstanzaAperta");
                string codTipoIstanzaDomicilio = record.SafeGetString("CodTipoIstanzaAperta").Trim();

                DomicilioSnapshot? istanzaDomicilio = null;
                if (hasIstanzaDomicilio)
                {
                    istanzaDomicilio = new DomicilioSnapshot
                    {
                        ComuneDomicilio = record.SafeGetString("IstanzaComuneDomicilio").Trim(),
                        TitoloOneroso = record.SafeGetBool("IstanzaTitoloOneroso"),
                        ContrattoEnte = record.SafeGetBool("IstanzaContrattoEnte"),
                        TipoEnte = record.SafeGetString("IstanzaTipoEnte").Trim().ToUpperInvariant(),
                        SerieContratto = record.SafeGetString("IstanzaSerieContratto").Trim(),
                        DataRegistrazione = record.SafeGetDateTime("IstanzaDataRegistrazione"),
                        DataDecorrenza = record.SafeGetDateTime("IstanzaDataDecorrenza"),
                        DataScadenza = record.SafeGetDateTime("IstanzaDataScadenza"),
                        DurataContratto = record.SafeGetInt("IstanzaDurataContratto"),
                        Prorogato = record.SafeGetBool("IstanzaProrogato"),
                        DurataProroga = record.SafeGetInt("IstanzaDurataProroga"),
                        SerieProroga = record.SafeGetString("IstanzaSerieProroga").Trim(),
                        DenomEnte = record.SafeGetString("IstanzaDenomEnte").Trim(),
                        ImportoRataEnte = record.SafeGetDouble("IstanzaImportoRataEnte")
                    };
                }

                bool hasUltimaIstanzaChiusaDomicilio = record.SafeGetBool("HasUltimaIstanzaChiusaDomicilio");
                int numUltimaIstanzaChiusaDomicilio = record.SafeGetInt("NumUltimaIstanzaChiusaDomicilio");
                string codTipoUltimaIstanzaChiusaDomicilio = record.SafeGetString("CodTipoUltimaIstanzaChiusaDomicilio").Trim();
                string esitoUltimaIstanzaChiusaDomicilio = record.SafeGetString("EsitoUltimaIstanzaChiusaDomicilio").Trim();
                string utentePresaCaricoUltimaIstanzaChiusaDomicilio = record.SafeGetString("UtentePresaCaricoUltimaIstanzaChiusaDomicilio").Trim();

                return new StatusSedeStudent
                {
                    Info = studenteInfo,
                    Key = key,
                    AlwaysA = alwaysA,
                    InSedeList = inSedeList,
                    PendolareList = pendolareList,
                    FuoriSedeList = fuoriSedeList,
                    HasAlloggio12 = hasAlloggio12,
                    MinMesiDomicilioFuoriSede = minMesiDomicilioFuoriSede,
                    HasIstanzaDomicilio = hasIstanzaDomicilio,
                    CodTipoIstanzaDomicilio = codTipoIstanzaDomicilio,
                    NumIstanzaDomicilio = numIstanzaDomicilio,
                    IstanzaDomicilio = istanzaDomicilio,
                    HasUltimaIstanzaChiusaDomicilio = hasUltimaIstanzaChiusaDomicilio,
                    CodTipoUltimaIstanzaChiusaDomicilio = codTipoUltimaIstanzaChiusaDomicilio,
                    NumUltimaIstanzaChiusaDomicilio = numUltimaIstanzaChiusaDomicilio,
                    EsitoUltimaIstanzaChiusaDomicilio = esitoUltimaIstanzaChiusaDomicilio,
                    UtentePresaCaricoUltimaIstanzaChiusaDomicilio = utentePresaCaricoUltimaIstanzaChiusaDomicilio
                };
            }
        }

        private sealed class DomicilioSnapshot
        {
            public string ComuneDomicilio { get; init; } = "";
            public bool TitoloOneroso { get; init; }
            public bool ContrattoEnte { get; init; }
            public string TipoEnte { get; init; } = "";
            public string SerieContratto { get; init; } = "";
            public DateTime DataRegistrazione { get; init; }
            public DateTime DataDecorrenza { get; init; }
            public DateTime DataScadenza { get; init; }
            public int DurataContratto { get; init; }
            public bool Prorogato { get; init; }
            public int DurataProroga { get; init; }
            public string SerieProroga { get; init; } = "";
            public string DenomEnte { get; init; } = "";
            public double ImportoRataEnte { get; init; }
        }
    }
}
