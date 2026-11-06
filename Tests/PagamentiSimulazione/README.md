Verifiche offline sull'assembly reale della procedura pagamenti. Compilare prima `ProcedureNet7.csproj` in configurazione `PAGAMENTI` con MSBuild di Visual Studio, poi eseguire:

```powershell
dotnet run --project Tests/PagamentiSimulazione/PagamentiSimulazione.Tests.csproj
dotnet run --project Tests/PagamentiEsclusioni/PagamentiEsclusioni.Tests.csproj
```

Il primo programma verifica le modalità esposte, l'identità dell'estrazione ordinaria e simulata, la destinazione temporanea anche quando esiste una tabella permanente, le protezioni contro troncamento e inserimenti contabili, le cartelle di output e lo stato conservato tra cicli automatici. Produce e riapre anche un flusso con dati fittizi di uno studente senza impegno, controllandone presenza e importi. Usa una connessione intenzionalmente chiusa e non avvia form né esegue SQL. Il secondo programma copre anche la generazione dei gruppi senza impegno, i filtri sugli impegni esistenti, le esclusioni e gli audit.

La modalità `Simulazione PA tollerante` include la simulazione senza impegni e adatta le date delle assegnazioni per il saldo alla data di riferimento della procedura. L'inizio viene limitato al periodo PA, usando il limite iniziale se manca; la fine è limitata dalla fine registrata, dalla fine del periodo PA e dalla data di estrazione. Un intervallo invertito o senza giorni maturati produce una trattenuta zero, senza escludere lo studente. Le date originali e quelle applicate sono riportate nel file delle anomalie PA. Restano invariati gli altri requisiti PA, compresa la disponibilità di stanza e dati economici.

`AssegnazioniPaRegressionTests` verifica il metodo realmente chiamato dall'elaborazione: fine futura o assente, inizio mancante o fuori periodo, date invertite o coincidenti, estrazione prima o dopo il periodo PA, tariffa giornaliera, giorno finale incluso e limite complessivo di 165 giorni su più assegnazioni dei fuori corso. Controlla inoltre che le modalità precedenti mantengano le esclusioni e i calcoli originari. Il test del limite alla data di estrazione è stato eseguito prima della correzione e ha fallito come previsto.

Non sostituiscono una verifica integrata con SQL Server e dati di prova.
