using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    internal class ArgsControlloDomicilio 
    {
        [Required(ErrorMessage = "Inserire l'anno accademico")]
        [ValidAAFormat(ErrorMessage = "L'anno accademico deve essere nel formato xxxxyyyy.")]
        public string _selectedAA = string.Empty;
        public string _folderPath = string.Empty;

        // nuovo: filtro per lavorazione singola/multipla (CF)
        public IReadOnlyCollection<string>? _codiciFiscali = null;

        // nuovo: bypass prompt UI (se chiamata da altra procedura)
        public bool? _doDbWritesAndSendMessages = null;

        // nuovo: export opzionale
        public bool _exportExcel = true;

        // nuovo: transazione esterna opzionale (no commit/rollback in ControlloDomicilio)
        public SqlTransaction? _externalTransaction = null;

        // nuovo: utente inserimento messaggi
        public string _utenteMessaggi = "TEST";
    }
}
