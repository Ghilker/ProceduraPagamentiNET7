# Prelavorazione documenti con Droso

Menu **Varie - PrelavorazioneDocumentiDroso**, nelle configurazioni VARIE e Debug, profilo Funzionario o superiore.

## Uso

1. Collegare ProcedureNet7 al database come per le altre procedure.
2. Impostare facoltativamente **Data presentazione: dal / al**, spuntando gli estremi desiderati. Il filtro usa la data dello stato pendente 01 dell'allegato e comprende tutta la giornata finale. Con entrambi gli estremi disattivati non c'è un filtro di date. Il limite iniziale è 100 allegati: **0 indica tutti gli allegati selezionabili**, senza un limite sostitutivo; un numero positivo limita il totale estratto.
3. Premere **Avvia**: la procedura estrae gli allegati pendenti, scarica fino a quattro PDF contemporaneamente su connessioni dedicate e avvia automaticamente l'analisi con il modello integrato. I trasferimenti sono in streaming, con controlli di dimensione, intestazione PDF e hash; gli errori di un allegato non fermano gli altri. **Annulla** interrompe l'elaborazione e conserva i risultati già registrati.
4. Aprire **Documenti e dettagli…** per filtrare i risultati, aprire i PDF, registrare correzioni con **Salva revisione locale** e consultare modello, sessione e avanzamento. La finestra può restare aperta durante la lavorazione. Le azioni locali, l'esportazione e la pulizia sono raccolte nella stessa finestra.

Non occorre selezionare un eseguibile o una cartella modello. Nei dettagli, **Altre azioni → Impostazioni…** contiene soltanto cartella delle sessioni e database allegati (`ADISU_ALLEGATI`, sullo stesso server della connessione principale). Le vecchie impostazioni dell'eseguibile e del modello vengono ignorate; cartella sessioni e database vengono conservati.

Per prove senza DB: nei dettagli, **Altre azioni → Carica PDF locali…**, poi **Analizza sessione**. La stessa azione rianalizza una sessione riaperta. Con **Apri sessione…** si riprende `session.json`; **Esporta CSV…** salva il riepilogo. Le correzioni locali sono conservate anche dopo una nuova analisi dello stesso lotto.

La preparazione esegue il rendering di fino a quattro PDF contemporaneamente, mantenendo tutte le pagine e la risoluzione precedente. Poppler produce immagini RGB temporanee in PPM; Pillow le salva in PNG senza perdita con compressione 3, evitando il costo del salvataggio PNG di Poppler. I PPM vengono eliminati anche in caso di errore o annullamento. Le immagini decodificate restano identiche e i PNG già preparati rimangono riutilizzabili. I duplicati vengono preparati una sola volta. Le immagini sono conservate nella cartella `prepared` della sessione e riutilizzate nelle analisi successive soltanto se PDF, manifest e hash delle pagine corrispondono; una preparazione incompleta o alterata viene rigenerata. Il Logger distingue i tempi di selezione SQL, download e preparazione e indica i PDF riutilizzati.

Durante la classificazione, un worker CPU prepara fino a due pagine in anticipo mentre il modello elabora quelle disponibili. Rimangono invariati immagini, ordine delle pagine, aggregazione dell'intero PDF, pesi e soglie; la memoria aggiuntiva è limitata alla coda di pagine. Annullamento ed errori chiudono anche il worker. Il Logger riporta il tempo della classificazione. I modelli con risposte congelate mantengono il percorso della cache senza letture anticipate delle immagini.

**Elimina download** cancella, dopo conferma, le copie dei PDF e le immagini preparate della sola sessione aperta, inclusi i file temporanei e le immagini delle analisi precedenti. Conserva `session.json`, risultati, CSV e log; non segue i percorsi originali presenti nelle righe né collegamenti a cartelle esterne. Il comando è disabilitato durante una lavorazione. Dopo la pulizia, per aprire o rianalizzare i PDF è necessario riscaricarli o importarli. Per pulire un'altra sessione, aprirla con **Apri sessione**.

Le sessioni sono salvate per impostazione predefinita in `%LOCALAPPDATA%\ProcedureNet7\DrosoSessioni`. Includono PDF, identificativi, hash del modello e revisioni. I PDF hanno percorsi assoluti: non spostare soltanto `session.json`. Le impostazioni non contengono credenziali SQL.

## Modello nella solution

`DrosoAssets` contiene:

- `model/best.pt` e `model/model.json`: Droso V4 `V1-20261013-085314-3355`, checkpoint migliore dell'epoca 9, con encoder ResNet18 dell'epoca 7 congelato e incorporato nei pesi;
- `model/decision-visual-only.json`: soglie scelte sulla validazione e associate allo stesso checkpoint;
- `brain/visual-graph.pt`: grafo utilizzato dal modello;
- `scripts`: motore Python e ingresso di inferenza `infer_procedure.py`;
- `manifest.json`: identità e SHA256 dei pesi, dei metadati, del grafo e della politica delle soglie.

Il progetto copia automaticamente queste risorse negli output di build e publish, preservandone la struttura. Prima dell'analisi ne verifica l'integrità. Nessuna scrittura avviene accanto al modello: PDF preparati, verifica e diagnostica sono nella sessione.

Il checkpoint è `F8DD5D50E95024899256960A543DA93109093BC7F8A1C1C684F6BCFD822FB4A1`. Non occorre la cartella della ResNet18 sorgente né scaricare i suoi pesi: l'encoder è già nel modello. Per il caricamento serve torchvision 0.23.0, compatibile con il PyTorch 2.8.0 installato. I moduli Python del pacchetto includono il nuovo ingresso RGB e il suo adattatore. Questo percorso ricalcola le risposte e non usa la cache del vecchio modello.

ProcedureNet7 avvia direttamente Python/PyTorch, senza `DrosoDocLab.exe` e senza interfaccia WPF. Usa automaticamente Python e Poppler già installati per l'utente del PC GPU, tramite le posizioni gestite in `%LOCALAPPDATA%\DrosoDocLab`. Sono riconosciuti anche runtime distribuiti in `DrosoAssets/runtime/python/Scripts/python.exe` e `DrosoAssets/runtime/poppler/Library/bin`. Python/PyTorch e Poppler non sono duplicati nel repository: su un altro PC devono essere installati. La cache di risposte esistente `D:\DrosoDocLab\responses-v2` rimane disponibile soltanto per i modelli precedenti.

## Log ed errori

Avvio, avanzamento, completamento, annullamento ed eccezioni vengono registrati nel **Logger integrato di ProcedureNet7**. Gli errori includono stack trace e cause interne, copiabili dal log principale; non sono più confinati a una finestra di avviso. Il Logger mantiene il proprio salvataggio dei log nella cartella Documenti dell'utente, `procedure/logs`.

Ogni analisi conserva anche `request.json`, `events.jsonl`, `result.json` e, se presenti, `error.txt` e `engine-stderr.log`. Le richieste contengono percorsi dei PDF e delle risorse, non credenziali SQL.

Il precedente accesso negato era emerso durante la sostituzione di `progress.json` nel collegamento a Droso. L'avanzamento ora viaggia sullo standard output del processo: non viene letto o sostituito quel file. La cartella di lavorazione viene verificata in scrittura prima di avviare il motore; un eventuale diniego riporta nel Logger il percorso e l'eccezione completa.

## Perimetro e decisioni

Un allegato vuoto, non riconoscibile come PDF o non leggibile dal lettore PDF viene rifiutato direttamente con esito **04 — PDF non leggibile**. Non passa al modello, all'OCR o a tentativi di recupero. Il rifiuto è conservato nella sessione: riaprire o rianalizzare la stessa sessione non ritenta il file. Per esaminare un allegato sostituito occorre una nuova estrazione/importazione. Nei lotti misti i PDF validi proseguono normalmente. Guasti di trasferimento, file mancanti, hash modificati e problemi del runtime restano errori operativi.

Questi rifiuti di formato non richiedono un modello caricato; la scrittura dello stato nel DB e l'eventuale messaggio continuano a dipendere dalle opzioni della procedura e dai controlli di proprietario e versione dello stato. In modalità locale si registra soltanto l'esito nella sessione.

Le coorti sono quelle dell'estrazione mail: OLD 20232024–20252026, NEW 20262027. Sono selezionati gli ultimi allegati attivi per studente/tipo documento/tipo permesso con ultimo stato non ambiguo 01. Il limite è per allegato, non per studente. Ogni estrazione crea una sessione distinta; i collegamenti ai file ambigui e gli errori di download restano nel riepilogo.

Il punteggio non è una probabilità certificata di correttezza amministrativa. Il motore applica la politica dello stesso checkpoint: soglie iniziali 0,603 per permesso, 0,513 per ricevuta, 0,462 per passaporto e 0,890 per ERRATO, con margine minimo 0,15. Il flag di revisione usa questi criteri e il supporto delle categorie. La regola di ProcedureNet7 per proporre gli stati 02/04, basata sul punteggio oltre il 90%, resta invariata. Ogni PDF viene classificato come un unico documento combinando tutte le pagine. Anche per ricevute multipagina e documenti fronte/retro, il numero di pagine non impone il controllo degli uffici. Limiti: 30 MB e 20 pagine per PDF.

Inizialmente gli esiti sono locali; soltanto **Scrivi gli stati nel DB** abilita il salvataggio degli stati 02/04 degli allegati estratti dal database. I PDF importati restano locali anche con il flag attivo. La proposta dello stato segue la regola oltre il 90% descritta sopra, indipendentemente dal flag di revisione del motore.

**Invia messaggio ai respinti** è opzionale, inizialmente disattivato e disponibile solo con la scrittura degli stati nel DB. A fine analisi, dopo il salvataggio degli stati, inserisce un messaggio nell'area riservata dello studente tramite lo stesso `MessageUtils.InsertMessages` usato dai flussi di ritorno. Il testo è la costante `DrosoMessageRepository.MessageText`, attualmente **`#DA SCRIVERE#`**: se l'opzione è attivata viene inviato quel testo letterale.

L'invio riguarda soltanto i documenti della sessione con rifiuto 04 confermato e identificato nel DB. Ricontrolla proprietario, allegato attivo e versione dello stato prima di inviare; stati cambiati, sessioni locali e vecchie sessioni senza l'identità del rifiuto vengono esclusi. Un solo messaggio per codice fiscale raggruppa i documenti respinti notificabili di quello studente. Messaggio e flag `Messaggio_inviato` vengono salvati nella stessa transazione; il flag impedisce il reinvio degli stessi rifiuti anche riaprendo la sessione. I dettagli e il CSV conservano l'esito dell'invio. L'annullamento o un errore conserva gli invii già confermati e non dichiara riusciti quelli non confermati.

## Verifiche

Da ProcedureNet7:

```powershell
dotnet run --project Tests\Droso\ProcedureNet7.Droso.Tests.csproj -- '<cartella artefatti test>' 'D:\source\repos\ProcedureNet7\DrosoAssets'
```

Omettere l'ultimo argomento per i soli controlli senza inferenza. Il test genera PDF sintetici, verifica il motore reale, i duplicati, i limiti, l'annullamento, la mancata scrittura nelle risorse e la regressione di `progress.json` bloccato in lettura. Verifica anche eccezioni complete nel Logger e salva un'immagine della schermata. Usa i sorgenti reali con una finestra principale fittizia, senza autenticazione o connessioni SQL.

`python Tests\Droso\test_classification.py` verifica la preparazione anticipata durante l'inferenza, l'uguaglianza dei risultati su PDF multipagina con un piccolo modello CPU, l'annullamento e gli errori senza risultati parziali né worker rimasti attivi.

L'intera applicazione si compila con MSBuild di Visual Studio, necessario per le COM reference esistenti. L'estrazione SQL rimane da collaudare sulla connessione operativa; i test locali non interrogano il database.
