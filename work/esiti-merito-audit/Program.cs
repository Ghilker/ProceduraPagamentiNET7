using System.Data.SqlClient;
using ProcedureNet7;
using ProcedureNet7.Verifica;

void Check(string label, string benefit, Action<VerificaPipelineContext, StudenteInfo> configure, int expected) {
 using var connection = new SqlConnection();
 var pipeline = new VerificaPipelineContext(connection) { AnnoAccademico="20262027", ReferenceDate=new DateTime(2026,9,1), FaseElaborativa=VerificaFaseElaborativa.GraduatorieProvvisorie };
 var key = new StudentKey("TEST", "1");
 var info = pipeline.GetOrCreateStudent(key);
 info.StatusCompilazione=90;
 info.InformazioniIscrizione.TipoCorso=6;
 info.InformazioniIscrizione.AnnoCorso=2;
 info.InformazioniIscrizione.AnnoImmatricolazione=20252026;
 info.InformazioniIscrizione.ComuneSedeStudi="H501";
 info.InformazioniSede.Residenza.codComune="F205";
 info.InformazioniSede.StatusSedeSuggerito="B";
 info.InformazioniEconomiche.Calcolate.ISEEDSU=1000m;
 info.EsitoBorsaFacts.BeneficiRichiesti.Add(benefit);
 pipeline.ComuniPensionatiAttivi.Add("H501");
 configure(pipeline,info);
 var result = CalcoloEsitoBorsa.CalculateBenefitResult(new EsitoBorsaStudentContext(pipeline,key,info,2026,20262027,new EsitoBorsaRuleConfig(),benefit));
 Console.WriteLine($"{label}: esito={result.EsitoCalcolato}; motivi={result.CodiciMotivo}; sogliaMerito={info.InformazioniIscrizione.CreditiMinimiRichiestiMerito}");
 if(result.EsitoCalcolato != expected) throw new Exception("Risultato inatteso");
}
Check("PK status A", "PK", (_,i)=>i.InformazioniSede.StatusSedeSuggerito="A", 1);
Check("PA status A controllo", "PA", (_,i)=>i.InformazioniSede.StatusSedeSuggerito="A", 0);
Check("PK rinuncia corrente", "PK", (_,i)=>i.EsitoBorsaFacts.RinunciaPK=true, 1);
Check("PK decadenza", "PK", (_,i)=>i.EsitoBorsaFacts.DecadutoPK=true, 1);
Check("PK revoca bando", "PK", (_,i)=>i.EsitoBorsaFacts.RevocatoBandoPK=true, 1);
Check("PK beneficio non restituito", "PK", (_,i)=>i.EsitoBorsaFacts.BeneficiPregressiNonRestituiti.Add("PK"), 1);
Check("PA revoca sede distaccata", "PA", (_,i)=>i.EsitoBorsaFacts.RevocatoSedeDistaccata=true, 1);
Check("BS catalogo soglie vuoto", "BS", (_,i)=>{i.InformazioniIscrizione.TipoCorso=3;i.InformazioniIscrizione.NumeroCrediti=1m;i.InformazioniIscrizione.CodCorsoLaurea="TEST";}, 1);
Check("BS 1 credito con soglia 25 controllo", "BS", (p,i)=>{i.InformazioniIscrizione.TipoCorso=3;i.InformazioniIscrizione.NumeroCrediti=1m;i.InformazioniIscrizione.CodCorsoLaurea="TEST";p.CreditiRichiestiCatalog.Add(3,2,"0",25m,false,1);}, 0);
Check("BS anno dichiarato 2 calcolato 3", "BS", (p,i)=>{i.InformazioniIscrizione.TipoCorso=3;i.InformazioniIscrizione.NumeroCrediti=80m;i.InformazioniIscrizione.CodCorsoLaurea="TEST";i.InformazioniIscrizione.AnnoImmatricolazione=20242025;p.CreditiRichiestiCatalog.Add(3,3,"0",80m,false,1);}, 1);
