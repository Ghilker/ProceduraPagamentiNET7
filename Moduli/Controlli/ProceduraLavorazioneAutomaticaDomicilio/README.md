# Lavorazione automatica istanze domicilio

Nel menu Verifiche scegliere `LavorazioneAutomaticaDomicilio`, indicare l'anno accademico (dal 20262027), lasciare selezionata **Simulazione** e premere **Avvia**. La cartella richiesta contiene il CSV con riepilogo per istanza e dettaglio per operazione. La simulazione esegue esclusivamente letture.

Deselezionando Simulazione si accolgono soltanto istanze inviate, aperte e libere che superano tutti i controlli. Tutte le operazioni devono essere applicabili e tutti i documenti necessari leggibili integralmente, riconosciuti e congruenti. Scansioni non leggibili neppure con OCR, dati ambigui, documenti incompleti e Learning Agreement privi di una regola di confronto restano alla lavorazione manuale. Le operazioni che non richiedono nuovi PDF possono usare i documenti correnti previsti dalla loro tipologia. I requisiti dello status fuori sede sono valutati separatamente.

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

Per un contratto di locazione serve una ricevuta o attestazione che confermi espressamente l’avvenuta registrazione del contratto esaminato. Una richiesta di registrazione, un modello RLI compilato, un protocollo di invio o una ricevuta di sola presentazione lasciano l’istanza in verifica manuale anche quando riportano ufficio, serie, numero e date. Se il fascicolo contiene sia la richiesta sia la ricevuta di esito, la prova della registrazione deve provenire dalla ricevuta pertinente. Una ricevuta di proroga o subentro documenta il proprio adempimento.

Il lettore riconosce riferimenti equivalenti della stessa ricevuta e scadenze riportate da proroghe formalmente collegate allo stesso contratto e ufficio. Queste equivalenze non modificano i dati. Non basta prendere la data massima contenuta nel PDF: ricevute di contratti diversi restano da verificare.

Per le discordanze effettive si possono proporre soltanto estremi, data di registrazione, decorrenza e scadenza documentate. La proposta usa esclusivamente valori espliciti, univoci e associati a una pagina del PDF indicato nell'operazione. Un identificativo completo diverso resta da verificare: non viene sostituito per far coincidere un possibile altro contratto. Non usa allegati correnti di ripiego e non aggiunge campi mancanti o nulli. I campi interessati sono `numeroSerieContratto`, `dataRegistrazioneContratto`, `dataInizioContratto`, `dataFineContratto`, `numeroSerieSubentro`, `dataInizioSubentro`, `numeroSerieProroga`, `dataDecorrenza` e `dataScadenza`, secondo l'entità e il tipo di documento.

Nei documenti di locazione basta che compaia il codice fiscale completo dello studente. Il CSV riporta il ruolo quando è leggibile. Per un contratto con subentro si cerca lo studente nel subentro. Una correzione richiede che il codice fiscale compaia nella ricevuta pertinente.

Il comune dell’immobile può essere indicato con il nome o con il codice comune nei dati catastali. Quando il contratto omette il comune, la lavorazione può ricavarlo da un altro allegato della stessa istanza che riporta lo stesso ufficio, anno, serie e numero di registrazione. Il CSV indica l’allegato e la pagina usati. Comuni discordanti richiedono una verifica. Il comune dell’ufficio di registrazione e la residenza dei soggetti non documentano l’ubicazione dell’immobile.

Prima si costruisce una copia dell'intera istanza con i valori proposti, poi si ripetono validazione del grafo, associazione degli allegati e lettura integrale dei PDF. Per proporre una correzione servono identità, tipo di documento, comune dell’immobile e collegamento verificabili sulla ricevuta allegata. Scansioni con lettura incerta, conflitti e campi non verificati richiedono una verifica manuale. Le sole clausole di rinnovo non provano una proroga.

## Lettura OCR delle scansioni

La casella **Usa OCR per i PDF scannerizzati** è selezionata per impostazione predefinita. Deselezionarla per eseguire la simulazione o la lavorazione usando solo il testo estraibile direttamente. Le pagine prive di testo rimangono alla verifica manuale. La scelta vale anche per la rivalidazione delle correzioni.

Il download termina prima dell'analisi dei PDF. Il log mostra prima `Scaricati X/Y` con i conteggi dei file mancanti, vuoti o oltre il limite, poi `Analizzati X/Y`. I PDF sono salvati nella cartella temporanea dell'esecuzione e riutilizzati per la rivalidazione delle correzioni. La cartella viene rimossa alla chiusura ordinaria della procedura, anche in caso di errore gestito.

Il lettore condiviso con il controllo domicilio tenta prima l'estrazione iText. Con **Usa Unlimited-OCR 2** selezionato, la procedura verifica che il [servizio OCR](../../../DomicilioOcr/README.md) sia pronto prima di avviare l'analisi. Alla prima pagina da recuperare, il servizio riceve le immagini del PDF in ordine così da poter usare il contesto dell'intero documento. Se il corpo della pagina è una scansione e iText legge solo un breve testo ai margini, la procedura conserva il testo nativo e lo integra con la lettura OCR. Il CSV indica queste pagine con `iText+OCR pagine=...`. Le letture discordanti richiedono una verifica manuale. Con la casella Unlimited-OCR 2 deselezionata si usa l'OCR locale in italiano con Docnet e Tesseract.

Il testo OCR segue gli stessi controlli del testo digitale e conserva il numero della pagina per le prove documentali. La colonna CSV `DATI_ESTRATTI_DAL_PDF` indica `OCR pagine=...`. Una risposta incompleta di Unlimited-OCR, una confidenza media di Tesseract inferiore all'80%, l'assenza di testo o un errore OCR producono `PDF_OCR_REVIEW` e richiedono verifica manuale. La soglia dell'80% si applica a Tesseract. Restano necessari tutti i controlli documentali esistenti.

L'OCR lavora su un massimo di quattro documenti contemporaneamente e accetta PDF fino a 20 MB. Unlimited-OCR riduce la risoluzione delle pagine che a 300 DPI supererebbero 20 milioni di pixel. Invia fino a tre pagine consecutive e 20 MB di immagini per richiesta, prima della codifica Base64. Se il modello raggiunge il limite di testo, divide il gruppo interessato fino alla singola pagina. Ogni pagina conserva il proprio numero originale.

Con il servizio Unlimited-OCR locale standard, la procedura riusa per sette giorni le trascrizioni complete quando coincidono il contenuto del PDF, il modello e la configurazione di lettura. Le salva in `%LOCALAPPDATA%\ProcedureNet7\Domicilio\UnlimitedOcrCache`, cifrate per l'account Windows che esegue la procedura. Le voci scadute vengono rimosse durante le lavorazioni successive. A ogni analisi la procedura confronta il testo con i dati correnti dell'istanza e indica nel log finale quante verifiche hanno riutilizzato la trascrizione. Risposte incomplete, errori del servizio ed endpoint personalizzati seguono la lettura ordinaria.

Il modello incluso in `DomicilioOcr/tessdata/ita.traineddata` e la licenza vengono copiati nella cartella `tessdata` accanto all'eseguibile, anche in pubblicazione: distribuire questa cartella con l'applicazione. Sono necessarie le librerie native PDFium, Tesseract e Leptonica dei pacchetti già referenziati e i runtime Microsoft Visual C++ richiesti da Tesseract. Non è richiesta un'installazione separata dell'eseguibile Tesseract né un download al primo utilizzo.

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

`dotnet run --project Tests/DomicilioPdfOcr/DomicilioPdfOcr.csproj --configuration Release` esegue le regressioni OCR locali su PDF sintetici, senza connessioni al database. Controlla scansioni, PDF misti, campi estratti, provenienza per pagina e casi da lasciare alla verifica manuale. I progetti di test storici citati sotto non sono presenti in tutte le copie del repository.

I progetti `Tests/LavorazioneAutomaticaDomicilio` e `Tests/ControlloIstanzeDomicilio` verificano selezione, simulazione, PDF e CSV. Il secondo offre `--automation-simulation configPath outputFolder [codiceFiscale]`, limitato al database di test autorizzato e senza scritture. Le credenziali rimangono nel file di configurazione esterno.

`Tests/DomicilioPdfEvidence` verifica prove documentali e limiti del lettore; `Tests/DomicilioAutomaticCorrections` verifica pianificazione e copia dei dati. I test locali del backend controllano la whitelist dei campi, i valori precedenti e la conservazione dei campi estranei. Il collaudo attuale delle nuove correzioni usa soltanto test locali e simulazione SQL in lettura; le modalità di rollback descritte sotto eseguono scritture temporanee e richiedono un'autorizzazione separata.

`Tests/DomicilioAutomationBackend` offre il collaudo opzionale `--rollback configPath requestId`: un applicatore fittizio genera un errore dopo la presa in carico, mentre un interceptor impedisce ogni commit. Il test confronta istanza, dati, storico, messaggi e blocchi prima e dopo. Senza argomenti non apre connessioni.

La modalità `--accept-rollback configPath requestId` usa invece il componente e i servizi reali, verifica gli effetti nella transazione immediatamente prima del commit e impedisce sempre il commit, controllando poi il ripristino dei dati. Entrambe le modalità sono limitate al database di test autorizzato.
