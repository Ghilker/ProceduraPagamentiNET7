using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsEstrazioneStudenti : IValidatableObject
    {
        [Required(ErrorMessage = "Selezionare la cartella di salvataggio")]
        public string _folderPath { get; set; }
        public List<bool> _queryArguments { get; set; }
        public string _selectedCodEnti { get; set; }
        public string _selectedTipoCorso { get; set; }
        public string _selectedEsitiBorsa { get; set; }
        public string _selectedAnnoCorso { get; set; }

        [Required(ErrorMessage = "Indicare l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy")]
        public string _annoAccademico { get; set; }
        public string _fiscalCodesFilePath { get; set; }
        public string _selectedStatusSede { get; set; }
        public string _selectedCittadinanza { get; set; }
        public string _selectedBlocco { get; set; }
        public string _selectedCodComune { get; set; }

        public ArgsEstrazioneStudenti()
        {
            _queryArguments = new List<bool>();
            _folderPath = string.Empty;
            _selectedCodEnti = string.Empty;
            _selectedTipoCorso = string.Empty;
            _selectedEsitiBorsa = string.Empty;
            _selectedAnnoCorso = string.Empty;
            _annoAccademico = string.Empty;
            _fiscalCodesFilePath = string.Empty;
            _selectedStatusSede = string.Empty;
            _selectedCittadinanza = string.Empty;
            _selectedBlocco = string.Empty;
            _selectedCodComune = string.Empty;
        }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            List<string> errorMessages = new List<string>();

            if (_queryArguments == null || _queryArguments.Count == 0)
            {
                errorMessages.Add("Errore nella costruzione della lista.");
            }

            if (_queryArguments.Count > 1 && _queryArguments[1] == true && (_selectedEsitiBorsa == "''" || _selectedEsitiBorsa == null))
            {
                errorMessages.Add("Selezionare l'esito borsa desiderato dal menù a tendina.");
            }

            if (_queryArguments.Count > 2 && _queryArguments[2] == true && (_selectedCodEnti == "''" || _selectedCodEnti == null))
            {
                errorMessages.Add("Selezionare il codice ente desiderato dal menù a tendina.");
            }

            if (_queryArguments.Count > 3 && _queryArguments[3] == true && (_selectedTipoCorso == "''" || _selectedTipoCorso == null))
            {
                errorMessages.Add("Selezionare il tipo corso desiderato dal menù a tendina.");
            }

            if (_queryArguments.Count > 4 && _queryArguments[4] == true && (_selectedAnnoCorso == "''" || _selectedAnnoCorso == null))
            {
                errorMessages.Add("Selezionare l'anno di corso desiderato dal menù a tendina.");
            }

            if (_queryArguments.Count > 7 && _queryArguments[7] == true && (_fiscalCodesFilePath == string.Empty))
            {
                errorMessages.Add("Selezionare il file dei codici fiscali.");
            }

            if (_queryArguments.Count > 8 && _queryArguments[8] == true && (_selectedStatusSede == "''" || _selectedStatusSede == null))
            {
                errorMessages.Add("Selezionare lo status sede desiderato dal menù a tendina.");
            }

            if (errorMessages.Any())
            {
                string combinedErrors = string.Join(Environment.NewLine, errorMessages);
                yield return new ValidationResult(combinedErrors);
            }
        }
    }
}
