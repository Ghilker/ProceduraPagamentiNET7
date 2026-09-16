using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class ArgsVerifica
    {
        public string? _selectedAA = "20242025";
        public string? _folderPath = "D://";
        public int _faseElaborativa = 1; // 1 = provvisorie, 2 = definitive
        public bool _scriviSulDatabase;

        // stesso input di ProceduraControlloDatiEconomici (opzionale)
        public List<string>? _codiciFiscali;
    }
}
