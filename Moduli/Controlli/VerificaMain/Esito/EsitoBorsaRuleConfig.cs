namespace ProcedureNet7
{
    internal sealed class EsitoBorsaRuleConfig
    {
        public decimal SogliaIsee { get; set; }
        public decimal SogliaIsp { get; set; }

        public void Validate(string annoAccademico)
        {
            if (SogliaIsee <= 0m)
                throw new InvalidOperationException($"Configurazione economica da sistemare per AA {annoAccademico}: DatiGenerali_con.Soglia_Isee deve essere valorizzata e maggiore di zero.");
            if (SogliaIsp <= 0m)
                throw new InvalidOperationException($"Configurazione economica da sistemare per AA {annoAccademico}: DatiGenerali_con.Soglia_ISPE deve essere valorizzata e maggiore di zero.");
        }
    }
}
