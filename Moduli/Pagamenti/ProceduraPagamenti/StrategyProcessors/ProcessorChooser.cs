using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7.PagamentiProcessor
{
    public class ProcessorChooser
    {
        public IAcademicYearProcessor GetProcessor(string selectedAA)
        {
            switch (selectedAA)
            {
                case "20222023": return new AcademicProcessor2223();
                case "20232024": return new AcademicProcessor2324();
                case "20242025": return new AcademicProcessor2425();
                default: throw new Exception($"Processor dell'anno accademico {selectedAA} non è implementato!");
            }
        }
    }
}
