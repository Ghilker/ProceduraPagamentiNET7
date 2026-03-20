using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaControlliDatiEconomici
    {
        private List<Target> _targets = new();
        private bool _collectionCompleted;
        private bool _calculationCompleted;

        public void Compute(
            string aa,
            IReadOnlyCollection<string>? codiciFiscali = null,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey = null)
        {
            Collect(aa, codiciFiscali, infoByKey);
            Calculate();
            Validate();
        }

        public void Collect(
            string aa,
            IReadOnlyCollection<string>? codiciFiscali = null,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey = null)
        {
            void Log(int pct, string msg) => Logger.LogInfo(Math.Max(0, Math.Min(100, pct)), msg);

            Log(0, "Avvio raccolta dati ProceduraControlloDatiEconomici");

            aa = (aa ?? "").Trim();
            if (string.IsNullOrWhiteSpace(aa) || aa.Length != 8)
                throw new ArgumentException("Anno accademico non valido (atteso char(8), es: 20232024).");

            _aa = aa;
            _rows.Clear();
            _targets.Clear();
            OutputEconomici = BuildOutputTable();
            OutputEconomiciList = Array.Empty<ValutazioneEconomici>();
            _collectionCompleted = false;
            _calculationCompleted = false;

            Log(5, $"Parametri validati. AA={aa}");

            _targets = (codiciFiscali != null && codiciFiscali.Count > 0)
                ? LoadTargetsFromCfList(aa, codiciFiscali.ToList())
                : LoadTargetsAll(aa);

            Log(15, $"Targets caricati: {_targets.Count}");

            foreach (var target in _targets)
            {
                if (_rows.ContainsKey(target.CodFiscale))
                    continue;

                StudenteInfo studenteInfo;
                var key = new StudentKey(target.CodFiscale, target.NumDomanda ?? "");
                if (infoByKey != null && infoByKey.TryGetValue(key, out var existingInfo) && existingInfo != null)
                    studenteInfo = existingInfo;
                else
                    studenteInfo = new StudenteInfo();

                studenteInfo.InformazioniPersonali.CodFiscale = target.CodFiscale;
                studenteInfo.InformazioniPersonali.NumDomanda = target.NumDomanda;

                _rows[target.CodFiscale] = new EconomicRow
                {
                    Info = studenteInfo,
                    CodFiscale = target.CodFiscale,
                    NumDomanda = target.NumDomanda
                };
            }

            Log(18, "Caricamento valori attuali da vValori_calcolati.");
            LoadValoriCalcolatiAttuali(aa, _targets);

            LoadCalcParams(aa);
            LoadNucleoFamiliare(aa);

            Log(19, "Caricamento esito concorso BS (cod_tipo_esito) da vEsiti_concorsi.");
            LoadEsitoBorsaStudio(aa, _targets);

            Log(20, "Preparazione tabella temporanea CF e bulk insert.");
            EnsureTempCfTableAndFill(_targets.Select(target => target.CodFiscale));

            Log(22, "Caricamento INPS + attestazione CO (stored-like, >=20242025).");
            LoadInpsAndAttestazioni_StoredLike(aa, _targets);

            Log(30, "Esecuzione della query per tipologie reddito e split stored-like.");
            var split = LoadTipologieRedditiAndSplit(aa);

            Log(40, "Avvio estrazione dati economici (origine).");
            if (split.OrigIT_CO.Count > 0) AddDatiEconomiciItaliani_CO(aa, split.OrigIT_CO);
            if (split.OrigIT_DO.Count > 0) AddDatiEconomiciItaliani_DOFromCert(aa, split.OrigIT_DO);
            if (split.OrigEE.Count > 0) AddDatiEconomiciStranieri_DO(aa, split.OrigEE);

            Log(60, "Avvio estrazione dati economici (integrazione) - solo nucleo 'I'.");
            if (split.IntIT_CI.Count > 0) AddDatiEconomiciItaliani_CI(aa, split.IntIT_CI);
            if (split.IntDI.Count > 0) AddDatiEconomiciStranieri_DI(aa, split.IntDI);

            _collectionCompleted = true;
            Log(70, $"Raccolta dati economici completata. Righe in memoria: {_rows.Count}");
        }

        public void Calculate()
        {
            if (!_collectionCompleted)
                throw new InvalidOperationException("La raccolta dati economici deve essere eseguita prima del calcolo.");

            Logger.LogInfo(85, "Calcolo ISEDSU/ISEEDSU/ISPEDSU.");
            CalcoloDatiEconomici();
            _calculationCompleted = true;
        }

        public void Validate()
        {
            if (!_collectionCompleted)
                throw new InvalidOperationException("La raccolta dati economici deve essere eseguita prima della validazione.");
            if (!_calculationCompleted)
                throw new InvalidOperationException("Il calcolo economici deve essere eseguito prima della validazione.");

            Logger.LogInfo(95, "Materializzazione output economici.");

            OutputEconomici.Rows.Clear();
            foreach (var economicRow in _rows.Values.OrderBy(item => item.CodFiscale))
            {
                SyncEconomicRowToStudentInfo(economicRow);

                var outputRow = OutputEconomici.NewRow();
                outputRow["CodFiscale"] = economicRow.CodFiscale;
                outputRow["NumDomanda"] = economicRow.NumDomanda ?? "";
                outputRow["TipoRedditoOrigine"] = economicRow.TipoRedditoOrigine ?? "";
                outputRow["TipoRedditoIntegrazione"] = economicRow.TipoRedditoIntegrazione ?? "";
                outputRow["CodTipoEsitoBS"] = (object?)economicRow.CodTipoEsitoBS ?? DBNull.Value;
                outputRow["ISR"] = (double)RoundSql(economicRow.ISRDSU, 2);
                outputRow["ISP"] = (double)RoundSql(economicRow.ISPDSU, 2);
                outputRow["Detrazioni"] = (double)RoundSql(economicRow.Detrazioni, 2);
                outputRow["ISEDSU"] = (double)economicRow.ISEDSU;
                outputRow["ISEEDSU"] = (double)economicRow.ISEEDSU;
                outputRow["ISPEDSU"] = (double)economicRow.ISPEDSU;
                outputRow["ISPDSU"] = (double)RoundSql(economicRow.ISPDSU, 2);
                outputRow["SEQ"] = (double)RoundSql(economicRow.SEQ, 2);
                outputRow["ISEDSU_Attuale"] = economicRow.ISEDSU_Attuale;
                outputRow["ISEEDSU_Attuale"] = economicRow.ISEEDSU_Attuale;
                outputRow["ISPEDSU_Attuale"] = economicRow.ISPEDSU_Attuale;
                outputRow["ISPDSU_Attuale"] = economicRow.ISPDSU_Attuale;
                outputRow["SEQ_Attuale"] = economicRow.SEQ_Attuale;
                OutputEconomici.Rows.Add(outputRow);
            }

            OutputEconomiciList = _rows.Values
                .OrderBy(item => item.CodFiscale)
                .Select(item => new ValutazioneEconomici
                {
                    Info = item.Info,
                    TipoRedditoOrigine = item.TipoRedditoOrigine ?? string.Empty,
                    TipoRedditoIntegrazione = item.TipoRedditoIntegrazione ?? string.Empty,
                    CodTipoEsitoBS = item.CodTipoEsitoBS,
                    ISR = RoundSql(item.ISRDSU, 2),
                    ISP = RoundSql(item.ISPDSU, 2),
                    Detrazioni = RoundSql(item.Detrazioni, 2),
                    ISEDSU = RoundSql(item.ISEDSU, 2),
                    ISEEDSU = RoundSql(item.ISEEDSU, 2),
                    ISPEDSU = RoundSql(item.ISPEDSU, 2),
                    ISPDSU = RoundSql(item.ISPDSU, 2),
                    SEQ = RoundSql(item.SEQ, 2),
                    ISEDSU_Attuale = (decimal)item.ISEDSU_Attuale,
                    ISEEDSU_Attuale = (decimal)item.ISEEDSU_Attuale,
                    ISPEDSU_Attuale = (decimal)item.ISPEDSU_Attuale,
                    ISPDSU_Attuale = (decimal)item.ISPDSU_Attuale,
                    SEQ_Attuale = (decimal)item.SEQ_Attuale
                })
                .ToList();

            Logger.LogInfo(100, $"Completato. Record output: {OutputEconomici.Rows.Count}");
        }

        private void SyncEconomicRowToStudentInfo(EconomicRow row)
        {
            var info = row.Info ?? new StudenteInfo();
            var eco = info.InformazioniEconomiche;

            info.InformazioniPersonali.CodFiscale = row.CodFiscale ?? "";
            info.InformazioniPersonali.NumDomanda = row.NumDomanda ?? "";

            eco.TipoRedditoOrigine = row.TipoRedditoOrigine ?? "";
            eco.TipoRedditoIntegrazione = row.TipoRedditoIntegrazione ?? "";
            eco.CodTipoEsitoBS = row.CodTipoEsitoBS;

            eco.NumeroComponenti = row.NumeroComponenti;
            eco.NumeroConviventiEstero = row.NumeroConviventiEstero;
            eco.NumeroComponentiIntegrazione = row.NumeroComponentiIntegrazione;
            eco.TipoNucleo = row.TipoNucleo ?? "";

            eco.AltriMezzi = RoundSql(row.AltriMezzi, 2);
            eco.SEQ_Origine = RoundSql(row.SEQ_Origine, 2);
            eco.SEQ_Integrazione = RoundSql(row.SEQ_Integrazione, 2);
            eco.ISRDSU = RoundSql(row.ISRDSU, 2);
            eco.ISPDSU = RoundSql(row.ISPDSU, 2);
            eco.Detrazioni = RoundSql(row.Detrazioni, 2);
            eco.SommaRedditiStud = RoundSql(row.SommaRedditiStud, 2);

            eco.ISEDSU = RoundSql(row.ISEDSU, 2);
            eco.ISEEDSU = RoundSql(row.ISEEDSU, 2);
            eco.ISPEDSU = RoundSql(row.ISPEDSU, 2);
            eco.SEQ = RoundSql(row.SEQ, 2);

            eco.ISEDSU_Attuale = row.ISEDSU_Attuale;
            eco.ISEEDSU_Attuale = row.ISEEDSU_Attuale;
            eco.ISPEDSU_Attuale = row.ISPEDSU_Attuale;
            eco.ISPDSU_Attuale = row.ISPDSU_Attuale;
            eco.SEQ_Attuale = row.SEQ_Attuale;

            if (_statusInpsOrigineByCf.TryGetValue(row.CodFiscale, out var inpsOrigine))
                eco.StatusInpsOrigine = inpsOrigine;

            if (_statusInpsIntegrazioneByCf.TryGetValue(row.CodFiscale, out var inpsIntegrazione))
                eco.StatusInpsIntegrazione = inpsIntegrazione;

            if (_coAttestazioneOkByCf.TryGetValue(row.CodFiscale, out var coOk))
                eco.CoAttestazioneOk = coOk;
        }
    }
}
