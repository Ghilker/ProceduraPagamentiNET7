# Lavorazione automatica istanze domicilio

Nel menu Verifiche scegliere `LavorazioneAutomaticaDomicilio`, indicare l'anno accademico (dal 20262027), lasciare selezionata **Simulazione** e premere **Avvia**. La cartella richiesta contiene il CSV con riepilogo per istanza e dettaglio per operazione. La simulazione esegue esclusivamente letture.

Deselezionando Simulazione si accolgono soltanto istanze inviate, aperte e libere che superano tutti i controlli. Tutte le operazioni devono essere applicabili e tutti i documenti necessari leggibili integralmente, riconosciuti e congruenti. Scansioni senza testo, dati ambigui, documenti incompleti e Learning Agreement privi di una regola di confronto restano alla lavorazione manuale. Le operazioni che non richiedono nuovi PDF possono usare i documenti correnti previsti dalla loro tipologia. I requisiti dello status fuori sede sono valutati separatamente.

`AUTO_DOMICILIO` identifica le colonne utente e lo storico: non richiede un nuovo account né credenziali dedicate. La connessione SQL è quella corrente di Procedure.

Il componente riceve la stringa originale conservata in memoria al collegamento, con le opzioni di cifratura della connessione corrente. Non ricostruisce le credenziali dalla proprietà `ConnectionString` di una connessione già aperta e non richiede `PersistSecurityInfo=True`. Dopo un aggiornamento dell'applicazione riconnettersi prima di avviare la lavorazione.

## Esiti CSV

- `IDONEA`: controlli superati in simulazione, nessuna modifica.
- `ACCOLTA`: accoglimento effettivo confermato.
- `IGNORATA`: istanza lasciata invariata, con la motivazione del controllo.
- `ERRORE`: esito non confermato; consultare lo stato dell'istanza prima di riprovare.

Non vengono eseguite correzioni, rifiuti o accoglimenti parziali. Una nuova esecuzione ignora le istanze già concluse o in carico.

## Componente di accoglimento

Il componente locale .NET 8 riusa i servizi e i manager del backoffice. Per compilare e predisporre il pacchetto, dalla radice del repository:

```powershell
.\Tools\DomicilioAutomationWorker\Build.ps1 `
  -DirStudioRoot 'C:\percorso\Code\DirStudio' `
  -OutputPath '.\Runtime\DomicilioAutomation'
```

Il backend deve includere `DomicilioLavorazioneService.AcceptAutomaticallyAsync`. Compilare quindi Procedure con Visual Studio/MSBuild, configurazione `VERIFICHE`: il contenuto di Runtime viene copiato nella cartella `DomicilioAutomation` accanto all'applicazione. Distribuire insieme questa cartella e installare il runtime .NET 8 ASP.NET Core. Il componente non carica file appsettings; riceve la connessione dalla procedura attraverso lo standard input, senza credenziali nella riga di comando.

Prima di modificare ogni istanza, la transazione verifica che sia ancora libera e che dati, allegati e contenuto dei PDF coincidano con quelli precedenti all'analisi. Rivalida inoltre le regole backend, inclusi gli avvisi. La transazione comprende presa in carico, applicazione delle operazioni, associazione degli allegati, chiusura, storico, messaggio studente e rimozione del blocco VVC con aggiornamento del flag di pagamento. Un errore annulla l'intera transazione. In caso di interruzione della comunicazione la procedura rilegge lo stato, senza ritentare l'accoglimento automaticamente.

## Verifiche

I progetti `Tests/LavorazioneAutomaticaDomicilio` e `Tests/ControlloIstanzeDomicilio` verificano selezione, simulazione, PDF e CSV. Il secondo offre `--automation-simulation configPath outputFolder [codiceFiscale]`, limitato al database di test autorizzato e senza scritture. Le credenziali rimangono nel file di configurazione esterno.

`Tests/DomicilioAutomationBackend` offre il collaudo opzionale `--rollback configPath requestId`: un applicatore fittizio genera un errore dopo la presa in carico, mentre un interceptor impedisce ogni commit. Il test confronta istanza, dati, storico, messaggi e blocchi prima e dopo. Senza argomenti non apre connessioni.

La modalità `--accept-rollback configPath requestId` usa invece il componente e i servizi reali, verifica gli effetti nella transazione immediatamente prima del commit e impedisce sempre il commit, controllando poi il ripristino dei dati. Entrambe le modalità sono limitate al database di test autorizzato.
