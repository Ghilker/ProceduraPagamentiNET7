# Lavorazione automatica istanze domicilio

Nel menu Verifiche scegliere `LavorazioneAutomaticaDomicilio`, indicare l'anno accademico (dal 20262027), lasciare selezionata **Simulazione** e premere **Avvia**. La cartella richiesta contiene il CSV con riepilogo per istanza e dettaglio per operazione. La simulazione esegue esclusivamente letture.

Deselezionando Simulazione si accolgono soltanto istanze inviate, aperte e libere che superano tutti i controlli. Tutte le operazioni devono essere applicabili e tutti i documenti necessari leggibili integralmente, riconosciuti e congruenti. Scansioni senza testo, dati ambigui, documenti incompleti e Learning Agreement privi di una regola di confronto restano alla lavorazione manuale. Le operazioni che non richiedono nuovi PDF possono usare i documenti correnti previsti dalla loro tipologia. I requisiti dello status fuori sede sono valutati separatamente.

`AUTO_DOMICILIO` identifica le colonne utente e lo storico: non richiede un nuovo account né credenziali dedicate. La connessione SQL è quella corrente di Procedure.

Il componente riceve la stringa originale conservata in memoria al collegamento, con le opzioni di cifratura della connessione corrente. Non ricostruisce le credenziali dalla proprietà `ConnectionString` di una connessione già aperta e non richiede `PersistSecurityInfo=True`. Dopo un aggiornamento dell'applicazione riconnettersi prima di avviare la lavorazione.

## Esiti CSV

- `IDONEA`: controlli superati in simulazione, nessuna modifica.
- `IDONEA_CON_CORREZIONI`: proposta documentata di correzione che supera tutti i controlli dopo la rivalidazione completa; nessuna modifica in simulazione.
- `ACCOLTA`: accoglimento effettivo confermato.
- `IGNORATA`: istanza lasciata invariata, con la motivazione del controllo.
- `ERRORE`: esito non confermato; consultare lo stato dell'istanza prima di riprovare.

Il CSV conserva il controllo sui dati originali e aggiunge campi proposti, valori originali e documentati, allegato e pagina di provenienza, esito e motivazione dopo le correzioni. Una proposta può restare `IGNORATA` quando emerge un altro problema nella rivalidazione. Non vengono eseguiti rifiuti o accoglimenti parziali. Una nuova esecuzione ignora le istanze già concluse o in carico.

## Correzioni documentate

Il lettore riconosce riferimenti equivalenti della stessa ricevuta e scadenze riportate da proroghe formalmente collegate allo stesso contratto e ufficio. Queste equivalenze non modificano i dati. Non basta prendere la data massima contenuta nel PDF: ricevute di contratti diversi restano da verificare.

Per le discordanze effettive si possono proporre soltanto estremi, data di registrazione, decorrenza e scadenza documentate. La proposta usa esclusivamente valori espliciti, univoci e associati a una pagina del PDF indicato nell'operazione. Un identificativo completo diverso resta da verificare: non viene sostituito per far coincidere un possibile altro contratto. Non usa allegati correnti di ripiego e non aggiunge campi mancanti o nulli. I campi interessati sono `numeroSerieContratto`, `dataRegistrazioneContratto`, `dataInizioContratto`, `dataFineContratto`, `numeroSerieSubentro`, `dataInizioSubentro`, `numeroSerieProroga`, `dataDecorrenza` e `dataScadenza`, secondo l'entità e il tipo di documento.

Prima si costruisce una copia dell'intera istanza con i valori proposti, poi si ripetono validazione del grafo, associazione degli allegati e lettura integrale dei PDF. Identità, ruolo, tipo di documento, comune e collegamento devono essere verificabili; scansioni prive di testo, conflitti, campi non verificati e documenti di enti o Learning Agreement non supportati impediscono la correzione automatica. Una ricevuta leggibile non rende verificabili le altre pagine scansionate dello stesso PDF. Le sole clausole di rinnovo non provano una proroga.

## Componente di accoglimento

Il componente locale .NET 8 riusa i servizi e i manager del backoffice. Per compilare e predisporre il pacchetto, dalla radice del repository:

```powershell
.\Tools\DomicilioAutomationWorker\Build.ps1 `
  -DirStudioRoot 'C:\percorso\Code\DirStudio' `
  -OutputPath '.\Runtime\DomicilioAutomation'
```

Il backend deve includere `DomicilioLavorazioneService.AcceptAutomaticallyAsync`. Compilare quindi Procedure con Visual Studio/MSBuild, configurazione `VERIFICHE`: il contenuto di Runtime viene copiato nella cartella `DomicilioAutomation` accanto all'applicazione. Distribuire insieme questa cartella e installare il runtime .NET 8 ASP.NET Core. Il componente non carica file appsettings; riceve la connessione dalla procedura attraverso lo standard input, senza credenziali nella riga di comando.

Prima di modificare ogni istanza, la transazione verifica che sia ancora libera e che dati, allegati e contenuto dei PDF coincidano con quelli precedenti all'analisi. Le correzioni devono corrispondere esattamente al payload precedente, cambiare solo i campi consentiti e citare l'allegato proposto della relativa operazione. Rivalida inoltre le regole backend, inclusi gli avvisi. La singola transazione comprende correzioni con storico dei valori prima/dopo, presa in carico, applicazione delle operazioni, associazione degli allegati, chiusura, storico, messaggio studente e rimozione del blocco VVC con aggiornamento del flag di pagamento. Un errore annulla l'intera transazione. In caso di interruzione della comunicazione la procedura rilegge lo stato, senza ritentare l'accoglimento automaticamente.

## Verifiche

I progetti `Tests/LavorazioneAutomaticaDomicilio` e `Tests/ControlloIstanzeDomicilio` verificano selezione, simulazione, PDF e CSV. Il secondo offre `--automation-simulation configPath outputFolder [codiceFiscale]`, limitato al database di test autorizzato e senza scritture. Le credenziali rimangono nel file di configurazione esterno.

`Tests/DomicilioPdfEvidence` verifica prove documentali e limiti del lettore; `Tests/DomicilioAutomaticCorrections` verifica pianificazione e copia dei dati. I test locali del backend controllano la whitelist dei campi, i valori precedenti e la conservazione dei campi estranei. Il collaudo attuale delle nuove correzioni usa soltanto test locali e simulazione SQL in lettura; le modalità di rollback descritte sotto eseguono scritture temporanee e richiedono un'autorizzazione separata.

`Tests/DomicilioAutomationBackend` offre il collaudo opzionale `--rollback configPath requestId`: un applicatore fittizio genera un errore dopo la presa in carico, mentre un interceptor impedisce ogni commit. Il test confronta istanza, dati, storico, messaggi e blocchi prima e dopo. Senza argomenti non apre connessioni.

La modalità `--accept-rollback configPath requestId` usa invece il componente e i servizi reali, verifica gli effetti nella transazione immediatamente prima del commit e impedisce sempre il commit, controllando poi il ripristino dei dati. Entrambe le modalità sono limitate al database di test autorizzato.
