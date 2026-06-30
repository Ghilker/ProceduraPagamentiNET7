using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace ProcedureNet7
{
    internal sealed class CarriereTicketTopicSheet : TicketTopicSheetBase
    {
        public override int Order => 20;
        public override string SheetName => "Carriere";

        // Il foglio è determinato esclusivamente dall'argomento primario.
        protected override bool Matches(DataRow row) =>
            PrimaryIs(row, PrimaryTopic.ISCRIZIONE_E_CARRIERA);

        public override DataTable BuildSheet(TicketTopicSheetContext context)
        {
            Logger.LogInfo(null, "Avvio costruzione foglio Carriere");
            DataTable result = base.BuildSheet(context);
            Logger.LogInfo(null, $"Ticket pertinenti al foglio Carriere: {result.Rows.Count}");

            AddOfficeColumn(result, "AA_RECORD_OPERATIVO");
            AddOfficeColumn(result, "CRITERIO_SELEZIONE_RECORD");
            AddOfficeColumn(result, "NUM_DOMANDA");
            AddOfficeColumn(result, "STATUS_COMPILAZIONE", typeof(int));
            result.Columns.Add("TIPO_CARRIERA", typeof(string));
            result.Columns.Add("CATEGORIA_DOMANDE_STUDENTE", typeof(string));
            result.Columns.Add("NUMERO_ANNI_ACCADEMICI", typeof(int));
            result.Columns.Add("ANNI_ACCADEMICI_PARTECIPATI", typeof(string));
            AddOfficeColumn(result, "ANNO_IMMATRICOLAZIONE", typeof(int));
            AddOfficeColumn(result, "ANNO_CORSO", typeof(int));
            AddOfficeColumn(result, "TIPO_STUDI");
            AddOfficeColumn(result, "CORSO_LAUREA");
            AddOfficeColumn(result, "SEDE_STUDI");
            AddOfficeColumn(result, "NUMERO_ESAMI", typeof(int));
            AddOfficeColumn(result, "NUMERO_CREDITI", typeof(decimal));
            AddOfficeColumn(result, "CREDITI_RICONOSCIUTI", typeof(decimal));
            AddOfficeColumn(result, "EVENTI_CARRIERA_PREGRESSA", typeof(int));
            AddOfficeColumn(result, "CODICI_CARRIERA_PREGRESSA");
            AddOfficeColumn(result, "CREDITI_CARRIERA_PREGRESSA", typeof(decimal));
            AddOfficeColumn(result, "BENEFICI_PREGRESSI");
            AddOfficeColumn(result, "IMPORTI_RESTITUITI");
            AddOfficeColumn(result, "BLOCCHI");
            AddOfficeColumn(result, "INDICAZIONE_OPERATORE");

            int onlyCurrentStudents = 0;
            int onlyPreviousStudents = 0;
            int currentAndPreviousStudents = 0;
            int missingApplications = 0;

            foreach (DataRow row in result.Rows)
            {
                string fiscalCode = SafeString(row, "CODFISC");
                IReadOnlyList<int> orderedYears = context.GetValidAcademicYears(fiscalCode);
                StudentApplicationCategory category = context.GetStudentApplicationCategory(fiscalCode);

                row["CATEGORIA_DOMANDE_STUDENTE"] = context.GetStudentApplicationCategoryLabel(category);
                row["TIPO_CARRIERA"] = GetCareerType(category, context.NewStudentAcademicYear);

                switch (category)
                {
                    case StudentApplicationCategory.SOLO_DOMANDA_ANNO_CORRENTE:
                        onlyCurrentStudents++;
                        break;
                    case StudentApplicationCategory.SOLO_DOMANDE_PRECEDENTI:
                        onlyPreviousStudents++;
                        break;
                    case StudentApplicationCategory.DOMANDA_ANNO_CORRENTE_E_PRECEDENTI:
                        currentAndPreviousStudents++;
                        break;
                    default:
                        missingApplications++;
                        break;
                }

                row["NUMERO_ANNI_ACCADEMICI"] = orderedYears.Count;
                row["ANNI_ACCADEMICI_PARTECIPATI"] = context.GetParticipatedAcademicYears(fiscalCode);

                TicketRecordSelection selection = ResolveOperationalSelection(context, row);
                TicketOfficeRecord? record = selection.Record;
                row["CRITERIO_SELEZIONE_RECORD"] = selection.Criterion;
                if (record == null)
                {
                    row["INDICAZIONE_OPERATORE"] =
                        "Domanda non trovata: verificare codice fiscale e identità dello studente.";
                    continue;
                }

                row["AA_RECORD_OPERATIVO"] = FormatAcademicYear(record.AcademicYear);
                row["NUM_DOMANDA"] = record.ApplicationNumber;
                row["STATUS_COMPILAZIONE"] = record.CompilationStatus;
                row["ANNO_IMMATRICOLAZIONE"] = record.EnrollmentYear;
                row["ANNO_CORSO"] = record.CourseYear;
                row["TIPO_STUDI"] = record.StudyType;
                row["CORSO_LAUREA"] = record.DegreeCourse;
                row["SEDE_STUDI"] = record.StudyLocation;
                row["NUMERO_ESAMI"] = record.ExamCount;
                row["NUMERO_CREDITI"] = record.CreditCount;
                row["CREDITI_RICONOSCIUTI"] = record.RecognizedCredits;
                row["EVENTI_CARRIERA_PREGRESSA"] = record.PreviousCareerEvents;
                row["CODICI_CARRIERA_PREGRESSA"] = record.PreviousCareerCodes;
                row["CREDITI_CARRIERA_PREGRESSA"] = record.PreviousCareerCredits;
                row["BENEFICI_PREGRESSI"] = record.PreviousBenefitsUsed ? "SI" : "";
                row["IMPORTI_RESTITUITI"] = record.PreviousAmountsReturned ? "SI" : "";
                row["BLOCCHI"] = record.Blocks;
                row["INDICAZIONE_OPERATORE"] = Evaluate(record, category, context.NewStudentAcademicYear);
            }

            if (result.Rows.Count == 0)
            {
                Logger.LogInfo(null, "Foglio Carriere completato: nessun ticket pertinente");
                return result;
            }

            DataView view = result.DefaultView;
            view.Sort = "TIPO_CARRIERA ASC, CODFISC ASC";
            DataTable sortedResult = view.ToTable();
            Logger.LogInfo(
                null,
                $"Foglio Carriere completato. Solo {FormatAcademicYear(context.NewStudentAcademicYear)}: {onlyCurrentStudents}; solo precedenti: {onlyPreviousStudents}; entrambe: {currentAndPreviousStudents}; non classificabili: {missingApplications}");
            return sortedResult;
        }

        private static string GetCareerType(
            StudentApplicationCategory category,
            int currentAcademicYear) => category switch
        {
            StudentApplicationCategory.SOLO_DOMANDA_ANNO_CORRENTE =>
                $"1 - SOLO DOMANDA {FormatAcademicYear(currentAcademicYear)}",
            StudentApplicationCategory.SOLO_DOMANDE_PRECEDENTI =>
                "2 - SOLO DOMANDE PRECEDENTI",
            StudentApplicationCategory.DOMANDA_ANNO_CORRENTE_E_PRECEDENTI =>
                $"3 - DOMANDA {FormatAcademicYear(currentAcademicYear)} E PRECEDENTI",
            _ => "4 - NESSUNA DOMANDA O ANNI NON CLASSIFICABILI"
        };

        private static string Evaluate(
            TicketOfficeRecord record,
            StudentApplicationCategory category,
            int currentAcademicYear)
        {
            var indications = new List<string>();

            switch (category)
            {
                case StudentApplicationCategory.SOLO_DOMANDA_ANNO_CORRENTE:
                    indications.Add($"studente con sola domanda {FormatAcademicYear(currentAcademicYear)}");
                    break;
                case StudentApplicationCategory.SOLO_DOMANDE_PRECEDENTI:
                    indications.Add("studente con sole domande precedenti");
                    break;
                case StudentApplicationCategory.DOMANDA_ANNO_CORRENTE_E_PRECEDENTI:
                    indications.Add($"studente con domanda {FormatAcademicYear(currentAcademicYear)} e domande precedenti");
                    break;
                default:
                    indications.Add("nessuna domanda valida o anni accademici da verificare");
                    break;
            }

            if (record.PreviousCareerEvents > 0)
                indications.Add($"{record.PreviousCareerEvents} eventi di carriera pregressa");
            if (record.RecognizedCredits > 0m)
                indications.Add($"{record.RecognizedCredits:0.##} crediti riconosciuti");
            if (record.PreviousBenefitsUsed)
                indications.Add("benefici pregressi dichiarati come usufruiti");
            if (record.PreviousAmountsReturned)
                indications.Add("importi pregressi dichiarati come restituiti");
            if (record.CompilationStatus > 0 && record.CompilationStatus < 90)
                indications.Add("domanda non trasmessa/completa");
            if (!string.IsNullOrWhiteSpace(record.Blocks))
                indications.Add("blocchi presenti");

            return string.Join("; ", indications);
        }
    }
}
