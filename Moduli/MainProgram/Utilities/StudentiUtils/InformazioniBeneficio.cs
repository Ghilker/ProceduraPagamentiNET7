using ProcedureNet7.Verifica;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    public class InformazioniBeneficio
    {
        public int EsitoPA { get; set; }
        public bool EraVincitorePA { get; set; }
        public bool SuperamentoEsami { get; set; }
        public bool SuperamentoEsamiTassaRegionale { get; set; }
        public double ImportoBeneficio { get; set; }
        public bool VincitorePA { get; set; }
        public bool RichiestaPA { get; set; }
        public bool RinunciaPA { get; set; }
        public bool HaServizioSanitario { get; set; }
        public bool ConcessaMonetizzazioneMensa { get; set; }

        public EsitoBorsaFacts EsitoBorsaFacts { get; } = new();
        public Dictionary<string, EsitoConcorsoBenefitRaw> EsitiConcorsoByBenefit { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, EsitoBeneficioCalcolato> EsitiCalcolatiByBenefit { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
