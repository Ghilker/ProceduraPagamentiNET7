Verifiche con dati fittizi sul calcolo degli esiti con e senza forzature e sulle colonne della tabella esportata in Excel. Non vengono aperte connessioni al database.

Dalla radice del repository, in Developer PowerShell per Visual Studio:

```powershell
msbuild Tests/VerificaForzature/VerificaForzature.Tests.csproj /restore /p:Configuration=VERIFICHE
dotnet Tests/VerificaForzature/bin/VERIFICHE/net7.0-windows/ProcedureNet7.Verifica.Tests.dll
```

La compilazione usa MSBuild di Visual Studio per i riferimenti COM del progetto principale. I 25 scenari sugli esiti coprono i quattro benefici, forzature generali, rinunce, benefici pregressi, borsa storica, status sede PA, discrepanze in entrambe le direzioni, esiti uguali, benefici non richiesti, risultati assenti e ordine delle nuove colonne.

Altri 27 scenari verificano il CFN legacy e l'ISEE estero attraverso le colonne esportate: CFN negativi e superiori a 1, bonus escluso dal merito conseguito, precisione DB a due decimali, intervallo nullo, dati mancanti e sistema a esami. Per l'ISEE controllano il minimo di 7.000 dal 20262027 solo con provincia EE e Tipo_Comune valorizzato e diverso da 1 e 3, inclusi anno precedente, tipi esclusi, NULL e valori alla soglia.

Altri 40 scenari verificano la conferma dell'esito 2 per i vincitori ancora idonei dello stesso beneficio, anche nel confronto senza forzature; un'esclusione continua a produrre esito 0. Controllano inoltre l'importo BS azzerato solo per l'esclusione BS, il mantenimento dell'importo per gli idonei/vincitori, l'importo PA sempre zero, i dati mancanti e la coerenza delle colonne ImportoCalcolato_BS, ImportoCalcolato_PA e ImportoFinaleBorsa. Esiti e importi originali rimangono invariati. Il test richiama le fasi di calcolo e costruzione della riga esportata con dati fittizi, senza eseguire il caricamento della configurazione dal DB.
