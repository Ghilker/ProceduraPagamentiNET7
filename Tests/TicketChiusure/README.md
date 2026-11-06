# Chiusura assistita ticket

Esecuzione dalla cartella del repository:

```powershell
dotnet run --project Tests/TicketChiusure/TicketChiusure.Tests.csproj -p:NuGetAudit=false
```

I test compilano i sorgenti di produzione e usano dati amministrativi in memoria.
Verificano esclusioni, anno obbligatorio, importi lordi dell'esito, rate e riemissioni,
rimborso tassa, permesso e blocchi pertinenti, istanze, richieste multiple,
duplicati con mantenimento del più vecchio e struttura dell'Excel esportato.
Non eseguono query SQL, invii email o modifiche ai ticket.

La procedura produce `Ticket_Chiusure_*.xlsx` con due fogli:
`Ticket chiudibili` e `Da verificare`. Non contiene risposte precompilate.
Se è attivo l'invio email, l'allegato rimane il report precedente `Export_*.xlsx`,
con un solo foglio `Sheet1`, esiti, blocchi, sedi e classificazione. Il nuovo
report delle chiusure resta un file locale separato e non viene allegato.
Le evidenze indicano ogni condizione e il testo che l'ha attivata.
La lettura dei blocchi usa i codici attivi originali e il catalogo ADISU, senza
dedurre l'assenza di un blocco dalle sole descrizioni. Codici sconosciuti e dati
non disponibili richiedono verifica. Le famiglie comprendono anche ISEE, DOV/CIMEA,
titolo, tirocinio, status sede, carriera, altri benefici, disabilità e PEC.
La versione del vocabolario è esposta nell'Excel insieme al residuo lordo,
ai codici attivi e al dettaglio dei movimenti. I numeri mancanti restano vuoti.
UID valorizzato, risposta operatore e categorie escluse impediscono qualunque
inserimento nel risultato. L'anno dichiarato è preservato e non è sostituito
dall'ultima domanda quando manca. Le comunicazioni di caricamento del permesso
sono distinte dalle richieste di verifica/sblocco.

La deduplicazione confronta CF e AA, conserva il più vecchio per data e usa
l'ID come ordinamento secondario. Riconosce messaggi identici e richieste
operative equivalenti solo per ambiti senza particolari personali non confrontabili;
parametri numerici diversi, contestazioni e richieste non comprese impediscono
il confronto semantico. La semplice uguaglianza dell'argomento non basta.
I blocchi generici non vengono deduplicati per somiglianza. Un generico errore
di invio non è risolto dalla presenza di una vecchia istanza: occorrono numero
e data di presentazione pertinenti. Il racconto della prima rata già ricevuta
non sostituisce la verifica del saldo o dell'integrazione richiesti.

Le regressioni ricostruiscono gli errori dell'export del 2 novembre 2026 con
testi privi di dati identificativi. Comprendono contestazioni, documenti richiesti,
rettifiche di residenza/merito/ISEE, richieste multiple, apostrofi corrotti e
informazioni generali distinte dalle comunicazioni di documenti da lavorare.

Prova facoltativa su CSV locale, senza collegamento al database:

```powershell
dotnet run --project Tests/TicketChiusure/TicketChiusure.Tests.csproj -- --csv 'C:\percorso\ticket.csv'
```

Il rapporto diagnostico rimane in `work/ticket-chiusure-csv-probe.json` e non
costituisce un elenco definitivo per le chiusure che richiedono dati amministrativi.

Per una simulazione con dati reali, impostare esternamente la variabile di ambiente
`TICKET_REVIEW_CONNECTION` e usare `--database-cases` (casi dell'audit) oppure
`--database` (intera estrazione):

```powershell
dotnet run --project Tests/TicketChiusure/TicketChiusure.Tests.csproj -- --database 'C:\percorso\ticket.csv' 'C:\percorso\risultati'
```

Queste modalità interrogano il loader SQL di produzione, producono un nuovo Excel
e un rapporto locale, senza chiamare aggiornamenti ticket o invii email. Richiedono
lo stesso tipo SQL `dbo.FiscalCodeList` già previsto dalla procedura.
