using System.ComponentModel.DataAnnotations;

namespace ProcedureNet7
{
    internal sealed class ArgsControlloDomicilio
    {
        [Required(ErrorMessage = "Inserire l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _selectedAA = string.Empty;

        [Required(ErrorMessage = "Selezionare la cartella di destinazione del CSV")]
        public string _folderPath = string.Empty;

        // Filtro facoltativo utile per collaudi puntuali. Se nullo vengono elaborati tutti gli studenti.
        public IReadOnlyCollection<string>? _codiciFiscali = null;
    }
}
