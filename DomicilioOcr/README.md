# OCR dei documenti di domicilio

## Unlimited-OCR 2

Quando si avvia la lavorazione automatica con **Usa Unlimited-OCR 2** selezionato, la procedura verifica il servizio `Unlimited-OCR-2` su `http://127.0.0.1:10000`. Se il servizio è spento, prepara il motore quando necessario e lo avvia in Ubuntu WSL. Su Windows serve Ubuntu WSL 2 con una GPU NVIDIA accessibile da WSL e i pacchetti Ubuntu `build-essential` e `ninja-build` per la compilazione dei kernel. La prima preparazione scarica le dipendenze e il modello. Per eseguirla o diagnosticarla a terminale, usare lo script dalla cartella del programma.

```powershell
& .\DomicilioOcr\Setup-UnlimitedOcr.ps1
```

Lo script installa [Unlimited-OCR 2 con SGLang](https://github.com/baidu/Unlimited-OCR#sglang) nel profilo utente di Ubuntu. I log automatici `setup.log` e `server.log` sono in `~/.local/share/procedurenet7/unlimited-ocr2`. Durante l'attesa il logger dell'applicazione mostra la fase e il tempo trascorso. Se il processo termina con errore in preparazione o avvio, riporta il codice di uscita e le ultime righe diagnostiche. Per un server interno già pronto si può impostare `DOMICILIO_UNLIMITED_OCR_URL` nell'ambiente dell'applicazione con l'URL HTTPS completo di `/v1/chat/completions`.

L'avvio in Ubuntu esclude gli indirizzi di loopback dal proxy per il solo processo del motore. La procedura aspetta che SGLang completi il test interno di avvio (`/ready`) e verifichi il nome del modello prima di analizzare i PDF con il servizio locale.

Aprire ProcedureNet7 in configurazione **Debug** e scegliere **Verifiche → Lavorazione automatica istanze domicilio**. Lasciare attiva **Usa OCR per i PDF scannerizzati** e spuntare **Usa Unlimited-OCR 2** per selezionare il nuovo motore. Dopo **Avvia** e la conferma della cartella CSV, la schermata mostra l'esito del controllo. Con la casella Unlimited-OCR 2 deselezionata, l'OCR usa Tesseract in locale. Per il primo collaudo usare **Simulazione** e confrontare i dati estratti con i PDF originali.

iText estrae il testo per primo. Alla prima pagina senza testo, Unlimited-OCR riceve le immagini del singolo PDF in ordine per poter usare il contesto delle altre pagine. Le immagini che supererebbero 20 milioni di pixel vengono renderizzate a risoluzione ridotta. Il lettore divide le richieste oltre il limite delle immagini e ritenta su due gruppi più piccoli se il modello raggiunge il limite di testo. Usa il risultato solo per le pagine prive di testo iText, conservando i numeri originali. Le pagine che restano senza testo o con una risposta incompleta richiedono verifica manuale.

## Modello Tesseract italiano

`tessdata/ita.traineddata` proviene dal progetto ufficiale Tesseract `tessdata_fast`, versione `4.1.0`:

https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/ita.traineddata

- Dimensione: 2.701.314 byte.
- SHA-256: `B8F89E1E785118DAC4D51AE042C029A64EDB5C3EE42EF73027A6D412748D8827`.
- Licenza: Apache 2.0, inclusa in `LICENSE`.
- Motore: Tesseract LSTM, lingua `ita`.

Il progetto copia il modello Tesseract e la licenza nella cartella `tessdata` dell'output e della pubblicazione. Tesseract esegue il riconoscimento in locale.
