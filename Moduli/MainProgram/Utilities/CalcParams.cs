namespace ProcedureNet7
{
    public sealed class CalcParams
    {
        public decimal Franchigia { get; set; }
        public decimal RendPatr { get; set; }
        public decimal FranchigiaPatMob { get; set; }
        public decimal ImportoBorsaA { get; set; }
        public decimal ImportoBorsaB { get; set; }
        public decimal ImportoBorsaC { get; set; }
        public decimal SogliaIsee { get; set; }

        public CalcParams Clone()
        {
            return new CalcParams
            {
                Franchigia = Franchigia,
                RendPatr = RendPatr,
                FranchigiaPatMob = FranchigiaPatMob,
                ImportoBorsaA = ImportoBorsaA,
                ImportoBorsaB = ImportoBorsaB,
                ImportoBorsaC = ImportoBorsaC,
                SogliaIsee = SogliaIsee
            };
        }
    }
}