using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Data.SqlClient;

namespace ProcedureNet7.ProceduraAllegatiSpace
{
    internal class ProceduraAllegati : BaseProcedure<ArgsProceduraAllegati>
    {
        private readonly GeneratoriAllegatiFactory generatoriFactory;

        public ProceduraAllegati(MasterForm masterForm, SqlConnection connection_string)
            : base(masterForm, connection_string)
        {
            generatoriFactory = new GeneratoriAllegatiFactory(connection_string);
        }

        public override void RunProcedure(ArgsProceduraAllegati args)
        {
            if (args is null)
                throw new ArgumentNullException(nameof(args));

            _masterForm.inProcedure = true;

            try
            {
                ValidateArgs(args);

                IGeneratoreAllegato generatore =
                    generatoriFactory.GetRequired(args._selectedTipoAllegato);

                AllegatoContext context = new(
                    args._selectedAA.Trim(),
                    args._selectedFileExcel.Trim(),
                    args._selectedSaveFolder.Trim(),
                    args._selectedTipoAllegato.Trim(),
                    args._selectedTipoAllegatoName.Trim(),
                    args._selectedTipoBeneficio.Trim());

                Logger.LogInfo(0, $"Inizio generazione allegato: {generatore.Descrizione}");

                generatore.Generate(context);

                Logger.LogInfo(100, "Fine lavorazione allegato.");
            }
            finally
            {
                _masterForm.inProcedure = false;
            }
        }

        private static void ValidateArgs(ArgsProceduraAllegati args)
        {
            Validator.ValidateObject(args, new ValidationContext(args), validateAllProperties: true);

            if (string.IsNullOrWhiteSpace(args._selectedAA) || args._selectedAA.Trim().Length != 8)
                throw new ValidationException("Anno accademico non valido. Formato atteso: xxxxyyyy.");

            if (string.IsNullOrWhiteSpace(args._selectedFileExcel) || !File.Exists(args._selectedFileExcel))
                throw new ValidationException("File Excel non trovato.");

            if (string.IsNullOrWhiteSpace(args._selectedSaveFolder) || !Directory.Exists(args._selectedSaveFolder))
                throw new ValidationException("Cartella di salvataggio non trovata.");

            if (string.IsNullOrWhiteSpace(args._selectedTipoAllegato))
                throw new ValidationException("Tipo allegato non selezionato.");
        }
    }
}
