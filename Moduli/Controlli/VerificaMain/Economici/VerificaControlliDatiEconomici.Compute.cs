using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed partial class VerificaControlliDatiEconomici
    {
        public void Compute(
            string aa,
            IReadOnlyCollection<string>? codiciFiscali = null,
            IReadOnlyDictionary<StudentKey, StudenteInfo>? infoByKey = null)
        {
            Collect(aa, codiciFiscali, infoByKey);
            Calculate();
            Validate();
        }

        public void Collect(string aa, IReadOnlyDictionary<StudentKey, StudenteInfo> students)
        {
            void Log(int pct, string msg) => Logger.LogInfo(Math.Max(0, Math.Min(100, pct)), msg);

            Log(0, "Avvio raccolta dati ProceduraControlloDatiEconomici");

            aa = (aa ?? "").Trim();
            if (string.IsNullOrWhiteSpace(aa) || aa.Length != 8)
                throw new ArgumentException("Anno accademico non valido (atteso char(8), es: 20232024).");

            ResetState(aa);
            Log(5, $"Parametri validati. AA={aa}");

            InitializeStudentsFromContext(students);
            Log(15, $"Targets inizializzati dal contesto: {_targets.Count}");

            ExecuteCollectionPipeline(aa, Log);
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

            ResetState(aa);
            Log(5, $"Parametri validati. AA={aa}");

            var targets = (codiciFiscali != null && codiciFiscali.Count > 0)
                ? LoadTargetsFromCfList(aa, codiciFiscali.ToList())
                : LoadTargetsAll(aa);

            _targets.AddRange(targets);
            InitializeStudentsFromTargets(_targets, infoByKey);
            Log(15, $"Targets caricati: {_targets.Count}");

            ExecuteCollectionPipeline(aa, Log);
        }

        private void ExecuteCollectionPipeline(string aa, Action<int, string> log)
        {
            log(18, "Caricamento valori attuali da vValori_calcolati.");
            LoadValoriCalcolatiAttuali(aa, _targets);

            LoadCalcParams(aa);
            LoadNucleoFamiliare(aa);

            log(19, "Caricamento esito concorso BS (cod_tipo_esito) da vEsiti_concorsi.");
            LoadEsitoBorsaStudio(aa, _targets);

            log(20, "Preparazione tabella temporanea CF e bulk insert.");
            EnsureTempCfTableAndFill(_targets.Select(target => target.CodFiscale));

            log(22, "Caricamento INPS + attestazione CO (stored-like, >=20242025).");
            LoadInpsAndAttestazioni_StoredLike(aa, _targets);

            log(30, "Esecuzione della query per tipologie reddito e split stored-like.");
            var split = LoadTipologieRedditiAndSplit(aa);

            log(40, "Avvio estrazione dati economici (origine).");
            if (split.OrigIT_CO.Count > 0) AddDatiEconomiciItaliani_CO(aa, split.OrigIT_CO);
            if (split.OrigIT_DO.Count > 0) AddDatiEconomiciItaliani_DOFromCert(aa, split.OrigIT_DO);
            if (split.OrigEE.Count > 0) AddDatiEconomiciStranieri_DO(aa, split.OrigEE);

            log(60, "Avvio estrazione dati economici (integrazione) - solo nucleo 'I'.");
            if (split.IntIT_CI.Count > 0) AddDatiEconomiciItaliani_CI(aa, split.IntIT_CI);
            if (split.IntDI.Count > 0) AddDatiEconomiciStranieri_DI(aa, split.IntDI);

            _collectionCompleted = true;
            log(70, $"Raccolta dati economici completata. Righe in memoria: {_rows.Count}, studenti nel contesto: {_studentsByKey.Count}");
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

            var orderedInfos = (_studentsByKey.Count > 0
                    ? _studentsByKey.Values
                    : _rows.Values.Select(item => item.Info))
                .OrderBy(item => item.InformazioniPersonali.CodFiscale)
                .ThenBy(item => item.InformazioniPersonali.NumDomanda)
                .ToList();

            foreach (var info in orderedInfos)
            {
                SyncInpsFlagsToStudentInfo(info);
                var eco = info.InformazioniEconomiche;

                var outputRow = OutputEconomici.NewRow();
                outputRow["CodFiscale"] = info.InformazioniPersonali.CodFiscale;
                outputRow["NumDomanda"] = info.InformazioniPersonali.NumDomanda;
                outputRow["TipoRedditoOrigine"] = eco.TipoRedditoOrigine ?? "";
                outputRow["TipoRedditoIntegrazione"] = eco.TipoRedditoIntegrazione ?? "";
                outputRow["CodTipoEsitoBS"] = (object?)eco.CodTipoEsitoBS ?? DBNull.Value;
                outputRow["ImportoAssegnato"] = eco.ImportoAssegnato;
                outputRow["ISR"] = (double)RoundSql(eco.ISRDSU, 2);
                outputRow["ISP"] = (double)RoundSql(eco.ISPDSU, 2);
                outputRow["Detrazioni"] = (double)RoundSql(eco.Detrazioni, 2);
                outputRow["ISEDSU"] = (double)RoundSql(eco.ISEDSU, 2);
                outputRow["ISEEDSU"] = (double)RoundSql(eco.ISEEDSU, 2);
                outputRow["ISPEDSU"] = (double)RoundSql(eco.ISPEDSU, 2);
                outputRow["ISPDSU"] = (double)RoundSql(eco.ISPDSU, 2);
                outputRow["SEQ"] = (double)RoundSql(eco.SEQ, 2);
                outputRow["ISEDSU_Attuale"] = eco.ISEDSU_Attuale;
                outputRow["ISEEDSU_Attuale"] = eco.ISEEDSU_Attuale;
                outputRow["ISPEDSU_Attuale"] = eco.ISPEDSU_Attuale;
                outputRow["ISPDSU_Attuale"] = eco.ISPDSU_Attuale;
                outputRow["SEQ_Attuale"] = eco.SEQ_Attuale;
                OutputEconomici.Rows.Add(outputRow);
            }

            OutputEconomiciList = orderedInfos
                .Select(item => new ValutazioneEconomici
                {
                    Info = item,
                    TipoRedditoOrigine = item.InformazioniEconomiche.TipoRedditoOrigine ?? string.Empty,
                    TipoRedditoIntegrazione = item.InformazioniEconomiche.TipoRedditoIntegrazione ?? string.Empty,
                    CodTipoEsitoBS = item.InformazioniEconomiche.CodTipoEsitoBS,
                    ImportoAssegnato = item.InformazioniEconomiche.ImportoAssegnato,
                    ISR = RoundSql(item.InformazioniEconomiche.ISRDSU, 2),
                    ISP = RoundSql(item.InformazioniEconomiche.ISPDSU, 2),
                    Detrazioni = RoundSql(item.InformazioniEconomiche.Detrazioni, 2),
                    ISEDSU = RoundSql(item.InformazioniEconomiche.ISEDSU, 2),
                    ISEEDSU = RoundSql(item.InformazioniEconomiche.ISEEDSU, 2),
                    ISPEDSU = RoundSql(item.InformazioniEconomiche.ISPEDSU, 2),
                    ISPDSU = RoundSql(item.InformazioniEconomiche.ISPDSU, 2),
                    SEQ = RoundSql(item.InformazioniEconomiche.SEQ, 2),
                    ISEDSU_Attuale = (decimal)item.InformazioniEconomiche.ISEDSU_Attuale,
                    ISEEDSU_Attuale = (decimal)item.InformazioniEconomiche.ISEEDSU_Attuale,
                    ISPEDSU_Attuale = (decimal)item.InformazioniEconomiche.ISPEDSU_Attuale,
                    ISPDSU_Attuale = (decimal)item.InformazioniEconomiche.ISPDSU_Attuale,
                    SEQ_Attuale = (decimal)item.InformazioniEconomiche.SEQ_Attuale
                })
                .ToList();

            Logger.LogInfo(100, $"Completato. Record output: {OutputEconomici.Rows.Count}");
        }

        private void SyncInpsFlagsToStudentInfo(StudenteInfo info)
        {
            string cf = NormalizeCf(info.InformazioniPersonali.CodFiscale);
            var eco = info.InformazioniEconomiche;

            var key = BuildStudentKey(info.InformazioniPersonali.CodFiscale, info.InformazioniPersonali.NumDomanda);

            if (_statusInpsOrigineByKey.TryGetValue(key, out var inpsOrigine))
                eco.StatusInpsOrigine = inpsOrigine;

            if (_statusInpsIntegrazioneByCf.TryGetValue(cf, out var inpsIntegrazione))
                eco.StatusInpsIntegrazione = inpsIntegrazione;

            if (_coAttestazioneOkByKey.TryGetValue(key, out var coOk))
                eco.CoAttestazioneOk = coOk;
        }
    }
}
