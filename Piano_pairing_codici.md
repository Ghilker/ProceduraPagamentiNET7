# Piano di pairing codici di segnalazione / motivi di esclusione

## Criteri

- Sorgente segnalazioni: `Codici.xlsx`, anno accademico 2026/2027.
- Sorgente esclusioni: dizionario `MotiviEsclusione` in `EsitoBorsaSupport.cs`.
- Cardinalità richiesta: un `Cod_segnalazione` può essere associato a un solo codice di esclusione e viceversa.
- I codici Excel non elencati restano non associati.
- `ESATTO` indica equivalenza sostanziale delle descrizioni.
- `COMPATIBILE` indica una corrispondenza utilizzabile, ma con descrizione da uniformare.
- `NUOVO` indica che forzare un codice Excel esistente produrrebbe un significato errato.

## Esito dell'analisi

- Segnalazioni nel file Excel: **139**
- Motivi di esclusione nel codice: **73**
- Pairing con codice Excel esistente: **67**
- Nuovi codici di segnalazione proposti: **6**
- Nessun `Cod_segnalazione` è riutilizzato nel piano.
- Nessun codice di esclusione è associato a più di una segnalazione.

## Pairing proposto

| Cod. segnalazione | Cod. esclusione | Qualità | Descrizione segnalazione | Descrizione esclusione / nota |
|---:|---|---|---|---|
| 118 | GEN000 | ESATTO | DOMANDA INCOMPLETA | Domanda non completa |
| 107 | GEN001 | ESATTO | DOMANDA COMPLETATA MA NON TRASMESSA | Domanda completa ma non trasmessa |
| 16 | GEN003 | COMPATIBILE | DOMANDA CARTACEA SPEDITA FUORI TERMINE | Domanda presentata oltre il termine previsto dal bando; uniformare eliminando il riferimento esclusivo al cartaceo |
| 17 | GEN004 | ESATTO | DOCUMENTAZIONE CONSOLARE NON PRESENTE | Documentazione consolare mancante |
| 18 | GEN005 | ESATTO | PERMESSO DI SOGGIORNO NON PRESENTE | Permesso di soggiorno mancante |
| 19 | GEN006 | ESATTO | GIA' IN POSSESSO DI UN TITOLO ACCADEMICO DELLO STESSO LIVELLO O SUPERIORE | Tipologia studi incompatibile con il titolo accademico già conseguito |
| 20 | GEN007 | ESATTO | RINUNCIA A TUTTI I BENEFICI | Rinuncia a tutti i benefici |
| 1 | ISC001 | COMPATIBILE | FORMATO DELL'ANNO DI PRIMA IMMATRICOLAZIONE NON CORRETTO | Anno di immatricolazione mancante o non valido; il codice Excel 2 copre separatamente il caso mancante |
| 4 | ISC004 | ESATTO | CARRIERA INTERROTTA MA NON DICHIARATO PER QUANTO TEMPO | Interruzione carriera dichiarata senza numero anni valido |
| 6 | ISC006 | ESATTO | CORSO DI LAUREA DI ISCRIZIONE NON DICHIARATO | Corso di laurea mancante per studente anni successivi |
| 103 | ISC008 | COMPATIBILE | ANNO CORSO CALCOLATO INCONGRUENTE CON L'ANNO DI CORSO DICHIARATO | Anno di corso non classificabile |
| 106 | GEN093 | ESATTO | ISCRIZIONE NON EFFETTUATA ENTRO I TERMINI PREVISTI DALL'UNIVERSITA' | Iscrizione fuori termine |
| 101 | GEN094 | ESATTO | DOMANDA NON TRASMESSA | Domanda non trasmessa |
| 15 | GENDOC | ESATTO | FOTOCOPIA DOCUMENTO NON PRESENTE | Documento di riconoscimento mancante |
| 110 | RED011 | COMPATIBILE | ATTESTAZIONE ISEE NON PRESENTE IN BANCA DATI INPS | Valore ISEE assente o non valido |
| 47 | RED012 | ESATTO | ISPDSU OLTRE IL LIMITE | Valore ISP oltre la soglia ammessa |
| 48 | RED013 | ESATTO | ISEEDSU OLTRE IL LIMITE | Valore ISEE oltre la soglia ammessa |
| 42 | RED086 | COMPATIBILE | INCONGRUENZA GRAVE TRA I DATI INPS E LA DOMANDA | Stato ISEE non ammesso |
| 122 | RED087 | ESATTO | PRESENZA DEL CODICE FISCALE DELLO STUDENTE INDIPENDENTE NELLA DICHIARAZIONE ISEE DELLA FAMIGLIA D'ORIGINE | Codice fiscale dello studente indipendente presente nell'attestazione ISEE della famiglia di origine |
| 121 | RED031 | ESATTO | ATTESTAZIONE ISEE DI TIPO UNIVERSITARIO NON PRESENTE IN BANCA DATI INPS | Attestazione ISEE origine non adeguata per mancanza della tipologia universitaria |
| 223 (nuovo) | RED033 | NUOVO | INTEGRAZIONE ISEE UNIVERSITARIA/CORRENTE NON IMPORTATA ENTRO IL 31 DICEMBRE | Nessun codice Excel distingue l'integrazione del nucleo di origine dalla CO universitaria |
| 24 | MER001 | COMPATIBILE | ERRORE GENERICO NELLA FUNZIONE PRE_ESAMICREDITI | Dati di merito assenti o non sufficienti per il calcolo |
| 119 | MER088 | ESATTO | STUDENTE GIA' IN POSSESSO DI UNA BORSA DI STUDIO | Studente già in possesso di altra borsa |
| 40 | MER005 | COMPATIBILE | NUMERO CREDITI DICHIARATO SUPERIORE AL LIMITE MASSIMO AMMESSO PER L'ANNO DI CORSO | Crediti dichiarati incongruenti con il corso di studi |
| 102 | MER071 | COMPATIBILE | ESAME COMPLEMENTARE NON SUPERATO | Esame complementare non valido per il merito AFAM |
| 39 | MER072 | COMPATIBILE | ANNO DI IMMATRICOLAZIONE INCONGRUENTE CON L'ORDINAMENTO DIDATTICO | Anno di corso incongruente con l'anno accademico di immatricolazione |
| 105 | MER074 | ESATTO | NUMERO CREDITI INSUFFICIENTI PER LA PARTECIPAZIONE ALLA LAUREA SPECIALISTICA | Crediti riconosciuti insufficienti per il primo anno di specialistica |
| 116 | MER085 | COMPATIBILE | UTILIZZO DEL BONUS IMPROPRIO | Utilizzo del bonus non ammesso per il titolo di accesso dichiarato |
| 207 | MER086 | COMPATIBILE | MANCATA PRESENTAZIONE DELLA DOCUMENTAZIONE DEI CREDITI DI BONUS NON FRUITI NELLA PRECEDENTE CARRIERA | Bonus non ammesso per crediti riconosciuti non derivanti da trasferimento TS o con ripetenza |
| 67 | MER087 | COMPATIBILE | MANCATA INDICAZIONE DELL'UTILIZZO DEL BONUS ORDINARIO | Bonus non ammesso per carriera pregressa CD/AT; descrizione da riscrivere |
| 33 | MER012 | COMPATIBILE | NUMERO CREDITI POSSEDUTI NON SUFFICIENTE | Merito insufficiente per la borsa |
| 123 | MER092 | ESATTO | NUMERO CREDITI DA TIROCINIO MAGGIORE AL NUMERO CREDITI TOTALI DICHIARATO | Crediti di tirocinio superiori ai crediti dichiarati |
| 120 | MER089 | COMPATIBILE | MANCANZA REQUISITI PER L'ISCRIZIONE ALLA SPECIALISTICA | Titolo di accesso non ammesso per immatricolazione alla specialistica |
| 224 (nuovo) | MER090 | NUOVO | TITOLO DI ACCESSO IN ATTESA NON CONSEGUITO ENTRO IL 10 FEBBRAIO | Nessun codice Excel contiene la condizione temporale del 10 febbraio |
| 225 (nuovo) | MER091 | NUOVO | TITOLO DI ACCESSO MANCANTE PER L'IMMATRICOLAZIONE ALLA MAGISTRALE/SPECIALISTICA | Il codice 120 è già riservato a MER089 e non distingue titolo mancante da titolo non ammesso |
| 226 (nuovo) | MER093 | NUOVO | ISCRIZIONE MAGISTRALE BIENNALE NON AMMISSIBILE PER ULTERIORE TITOLO IN ATTESA | Nessun codice Excel descrive la combinazione dei due titoli |
| 30 | MER170 | COMPATIBILE | DICHIARAZIONE DI PASSAGGIO DA VECCHIO A NUOVO ORDINAMENTO INCONGRUENTE | Iscrizione Sapienza non ammessa in vecchio ordinamento |
| 50 | BS001 | ESATTO | ANNI DI FUORI CORSO INAMMISSIBILI PER LA BORSA DI STUDIO | Anno di corso oltre il limite ammesso per la borsa |
| 51 | BS002 | COMPATIBILE | RINUNCIA SENZA RESTITUZIONE BENEFICI FRUITI | Beneficio borsa già fruito e non restituito |
| 52 | BS003 | ESATTO | RINUNCIA ALLA BORSA DI STUDIO | Rinuncia pregressa alla borsa di studio |
| 64 | BS004 | COMPATIBILE | STUDENTE VINCITORE DI BORSA DI STUDIO | Borsa già assegnata per lo stesso anno di corso equivalente |
| 54 | PA001 | ESATTO | STUDENTE NON FUORI SEDE (POSTO ALLOGGIO) | Status sede non ammesso per il posto alloggio |
| 227 (nuovo) | PA002 | NUOVO | COMUNE DELLA SEDE DI STUDI NON PRESENTE TRA I PENSIONATI ATTIVI | Nessun codice Excel parla del catalogo dei pensionati attivi |
| 55 | PA003 | ESATTO | ANNI DI FUORI CORSO INAMMISSIBILI PER IL POSTO ALLOGGIO | Anno di corso oltre il limite ammesso per il posto alloggio |
| 56 | PA004 | ESATTO SU DESCRIZIONE GRADUATORIA | POSTO ALLOGGIO GIA' FRUITO PER LO STESSO ANNO DI CORSO | Beneficio già fruito e non restituito per il posto alloggio |
| 57 | PA005 | ESATTO | RINUNCIA AL POSTO ALLOGGIO | Rinuncia pregressa o corrente al posto alloggio |
| 79 | PA006 | ESATTO | DECADENZA DAL POSTO ALLOGGIO | Decadenza dal posto alloggio |
| 84 | PA007 | ESATTO | REVOCA PER INCOMPATIBILITA' CON IL BANDO (POSTO ALLOGGIO) | Revoca del posto alloggio per incompatibilità con il bando |
| 59 | CI001 | COMPATIBILE | STUDENTE ISCRITTO AD UN PRIMO ANNO E/O AD UN CORSO DI DOTTORATO-SPECIALIZZAZIONE | Primo anno non ammesso al contributo integrativo salvo tipologia corso 5 |
| 221 | CI002 | COMPATIBILE | ISCRITTO AD UN CORSO DI SPECIALIZZAZIONE MEDICA | Corso specialistico di tipologia 7 non ammesso al contributo integrativo |
| 72 | CI003 | COMPATIBILE | ANNI DI FUORI CORSO INAMMISSIBILI | Anno di corso oltre il limite ammesso per il contributo integrativo |
| 73 | CI004 | COMPATIBILE | RINUNCIA SENZA RESTITUZIONE BENEFICI FRUITI | Beneficio già fruito e non restituito per il contributo integrativo |
| 74 | CI005 | ESATTO | RINUNCIA AL CONTRIBUTO INTEGRATIVO | Rinuncia pregressa o corrente al contributo integrativo |
| 97 | CI006 | COMPATIBILE | STUDENTE NON SELEZIONATO DALL'UNIVERSITA' PER PROGRAMMI UE | Studente non selezionato per il contributo integrativo |
| 82 | CI007 | ESATTO | DECADENZA DAL CONTRIBUTO INTEGRATIVO | Decadenza dal contributo integrativo |
| 87 | CI008 | ESATTO | REVOCA PER INCOMPATIBILITA' CON IL BANDO (CONTRIBUTO INTEGRATIVO) | Revoca del contributo integrativo per incompatibilità con il bando |
| 12 | ISC009 | ESATTO | SEDE STUDI NON DICHIARATA | Comune della sede di studi mancante o non classificabile |
| 10 | ISC010 | ESATTO | COMUNE DI RESIDENZA NON DICHIARATO | Comune di residenza mancante o non classificabile |
| 77 | VAR003 | COMPATIBILE | REVOCA | Revoca di tutti i benefici da variazione |
| 78 | VAR004 | ESATTO | DECADENZA DALLA BORSA DI STUDIO | Decadenza della borsa di studio da variazione |
| 83 | VAR011 | ESATTO | REVOCA PER INCOMPATIBILITA' CON IL BANDO (BORSA DI STUDIO) | Revoca della borsa per incompatibilità con il bando |
| 89 | VAR019 | ESATTO | REVOCA PER MANCATA ISCRIZIONE | Revoca per mancata iscrizione |
| 90 | VAR020 | ESATTO | STUDENTE ISCRITTO RIPETENTE | Revoca per iscrizione come ripetente |
| 228 (nuovo) | VAR021 | NUOVO | REVOCA PER ISEE O ANNI DI FUORI CORSO INAMMISSIBILI | Il motivo nel codice unisce due causali che nel file sono separate (91 e 112) |
| 92 | VAR022 | ESATTO | REVOCA PER STUDENTE GIA' LAUREATO | Revoca per studente già laureato |
| 93 | VAR023 | ESATTO | REVOCA PER PATRIMONIO OLTRE IL LIMITE | Revoca per patrimonio oltre il limite |
| 94 | VAR024 | ESATTO | REVOCA PER REDDITO OLTRE IL LIMITE | Revoca per reddito oltre il limite |
| 95 | VAR025 | ESATTO | REVOCA PER MANCANZA ESAMI/CREDITI | Revoca per mancanza esami o crediti |
| 111 | VAR027 | ESATTO | REVOCA PER ISCRIZIONE FUORI TERMINE | Revoca per iscrizione fuori termine |
| 112 | VAR028 | ESATTO | REVOCA PER ISEE FUORI TERMINE | Revoca per ISEE fuori termine |
| 113 | VAR029 | ESATTO | REVOCA PER ISEE NON PRODOTTA | Revoca per ISEE non prodotta |
| 109 | VAR030 | COMPATIBILE | ATTESTAZIONE ISEE EFFETTUATA OLTRE IL TERMINE PREVISTO DAL BANDO | Revoca per trasmissione ISEE CAF fuori termine |
| 115 | VAR031 | ESATTO | REVOCA PER MANCANZA CONTRATTO DI AFFITTO | Revoca per mancanza contratto di locazione |

## Decisioni richieste prima dell'implementazione

1. Confermare se i sei nuovi `Cod_segnalazione` possono essere creati usando i numeri 223-228.
2. Decidere se `ISC001` debba restare aggregato oppure essere diviso in:
   - anno di immatricolazione non valido → segnalazione 1;
   - anno di immatricolazione mancante → segnalazione 2.
3. Decidere se `VAR021` debba essere diviso in due motivi di esclusione distinti:
   - anni di fuori corso inammissibili → segnalazione 91;
   - ISEE fuori termine → segnalazione 112.
4. Uniformare le descrizioni dei pairing marcati `COMPATIBILE` prima di rendere il mapping vincolante.
5. Conservare il mapping in un unico catalogo applicativo e aggiungere una validazione automatica di unicità su entrambi i lati.
