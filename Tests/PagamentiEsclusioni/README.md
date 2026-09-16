Verifiche senza database sui componenti usati dal report delle esclusioni e dall'audit completo del pagamento.

Esecuzione dalla radice del repository:

```powershell
dotnet run --project Tests/PagamentiEsclusioni/PagamentiEsclusioni.Tests.csproj
```

Un percorso passato dopo `--` salva anche gli Excel di esempio con dati fittizi. Il test verifica riemissioni e integrazioni, vincitori PA senza assegnazione, importi negativi/zero/mancanti, deduplicazione, riconciliazione del riepilogo, formattazione e salvataggio/riapertura del file. Per l'audit verifica inoltre la presenza di tutti gli studenti di più impegni, anche esclusi senza impegno o senza flusso, la conservazione dei campi di dettaglio, il totale dei soli flussi generati e i casi di elaborazione interrotta o vuota. Non esegue query o pagamenti.

Le verifiche sui mancati flussi coprono impegno diverso da quello selezionato, impegno assente dall'elenco da elaborare, elenco vuoto, impegno mancante e filtro matricole/anni successivi. Nell'audit controllano motivo, dettaglio e fase, la precedenza di esclusioni e flussi già generati, le interruzioni e l'indicazione esplicita dei casi senza una causa registrata.
