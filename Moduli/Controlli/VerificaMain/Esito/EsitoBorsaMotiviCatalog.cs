using System;
using System.Collections.Generic;
using System.Globalization;

namespace ProcedureNet7
{
    internal enum StatoMappaturaMotivo
    {
        Verificata,
        DaRegistrare,
        Sconosciuta
    }

    internal sealed record MotivoEsclusioneCatalogo(
        string CodiceRegola,
        string DescrizioneRegola,
        string Benefici,
        string? CodiceSegnalazione,
        string? DescrizioneSegnalazione,
        StatoMappaturaMotivo Stato,
        string Fonte,
        string Nota);

    internal sealed record MappaturaMotiviEsclusione(
        string CodiciSegnalazione,
        bool Completa,
        string Diagnostica,
        bool RichiedeRegistrazione);

    internal static class EsitoBorsaMotiviCatalog
    {
        private const string Tutti = "BS;PA;PK;CI";

        public static IReadOnlyList<MotivoEsclusioneCatalogo> Voci { get; } =
            Array.AsReadOnly(new MotivoEsclusioneCatalogo[]
            {
                Verificata("GEN000", "Domanda non completa", Tutti, "118", "DOMANDA INCOMPLETA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 118 (lettura 2026-11-02)", ""),
                Verificata("GEN001", "Domanda completa ma non trasmessa", Tutti, "107", "DOMANDA COMPLETATA MA NON TRASMESSA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 107 (lettura 2026-11-02)", ""),
                Nuova("GEN003", "Domanda presentata oltre il termine previsto dal bando", Tutti, "225", "Domanda presentata oltre il termine previsto dal bando", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 016 riguarda esclusivamente la spedizione cartacea. Nuova causale generale per domanda oltre termine. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("GEN004", "Documentazione consolare mancante", Tutti, "017", "DOCUMENTAZIONE CONSOLARE NON PRESENTE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 017 (lettura 2026-11-02)", ""),
                Verificata("GEN005", "Permesso di soggiorno mancante", Tutti, "018", "PERMESSO DI SOGGIORNO NON PRESENTE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 018 (lettura 2026-11-02)", ""),
                Verificata("GEN006", "Tipologia studi incompatibile con il titolo accademico già conseguito", Tutti, "019", "GIA' IN POSSESSO DI UN TITOLO ACCADEMICO DELLO STESSO LIVELLO O SUPERIORE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 019 (lettura 2026-11-02)", ""),
                Verificata("GEN007", "Rinuncia a tutti i benefici", Tutti, "020", "RINUNCIA A TUTTI I BENEFICI", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 020 (lettura 2026-11-02)", ""),
                Nuova("ISC001", "Anno di immatricolazione mancante o non valido", Tutti, "226", "Anno di immatricolazione mancante o non valido", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Accorpa anno mancante e anno non valido: 001 e 002 distinguono due sottocasi. Il loader non conserva il valore originale. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("ISC004", "Interruzione carriera dichiarata senza numero anni valido", Tutti, "004", "CARRIERA INTERROTTA MA NON DICHIARATO PER QUANTO TEMPO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 004 (lettura 2026-11-02)", ""),
                Verificata("ISC006", "Corso di laurea mancante per studente anni successivi", Tutti, "006", "CORSO DI LAUREA DI ISCRIZIONE NON DICHIARATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 006 (lettura 2026-11-02)", ""),
                Nuova("ISC008", "Anno di corso non classificabile", Tutti, "227", "Anno di corso non classificabile", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 008 nel DB indica anno calcolato minore del dichiarato; non equivale a un anno non classificabile. Il 103 resta associato a MER072. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("GEN093", "Iscrizione fuori termine", Tutti, "106", "ISCRIZIONE NON EFFETTUATA ENTRO I TERMINE PREVISTI DALL'UNIVERSITA'", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 106 (lettura 2026-11-02)", ""),
                Verificata("GEN094", "Domanda non trasmessa", Tutti, "101", "DOMANDA NON TRASMESSA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 101 (lettura 2026-11-02)", ""),
                Verificata("GENDOC", "Documento di riconoscimento mancante", Tutti, "015", "FOTOCOPIA DOCUMENTO NON PRESENTE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 015 (lettura 2026-11-02)", ""),
                Nuova("RED011", "Valore ISEE assente o non valido", Tutti, "228", "Valore ISEE assente o non valido", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 110 riguarda attestazione assente in INPS, non il valore ISEE calcolato assente o non valido. Il 046 riguarda mezzi di sostentamento. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("RED012", "Valore ISP oltre la soglia ammessa", Tutti, "047", "ISPDSU OLTRE IL LIMITE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 047 (lettura 2026-11-02)", ""),
                Verificata("RED013", "Valore ISEE oltre la soglia ammessa", Tutti, "048", "ISEEDSU OLTRE IL LIMITE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 048 (lettura 2026-11-02)", ""),
                Nuova("RED086", "Stato ISEE non ammesso", Tutti, "229", "Stato ISEE non ammesso", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 042 riguarda incongruenza grave e il 121 assenza di attestazione universitaria: coprono solo sottocasi degli stati/fonte non ammessi. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("RED087", "Codice fiscale dello studente indipendente presente nell'attestazione ISEE della famiglia di origine", Tutti, "122", "PRESENZA DEL CODICE FISCALE DELLO STUDENTE INDIPENDENTE NELLA DICHIARAZIONE ISEE  DELLA FAMIGLIA D'ORIGINE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 122 (lettura 2026-11-02)", ""),
                Nuova("RED031", "Attestazione ISEE origine non adeguata: manca CO universitaria/ridotta/corrente; se l'ultima importata è ordinaria, il termine di regolarizzazione è il 10 dicembre", Tutti, "230", "Attestazione ISEE origine non adeguata: manca CO universitaria/ridotta/corrente; se l'ultima importata è ordinaria, il termine di regolarizzazione è il 10 dicembre", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Adeguatezza/importazione della fonte CO del nucleo di origine: il 121 non descrive tutti i casi coperti dalla regola. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Nuova("RED033", "Termine scaduto: integrazione ISEE universitaria/corrente del nucleo di origine non importata entro il 31 dicembre dell'anno di avvio", Tutti, "231", "Termine scaduto: integrazione ISEE universitaria/corrente del nucleo di origine non importata entro il 31 dicembre dell'anno di avvio", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Integrazione del nucleo di origine non importata entro il 31 dicembre. Il precedente candidato 223 è già occupato da un titolo estero. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Nuova("MER001", "Dati di merito assenti o non sufficienti per il calcolo", Tutti, "232", "Dati di merito assenti o non sufficienti per il calcolo", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 022 dichiara crediti pari a zero, il 024 un errore generico legacy. La regola corrente comprende dati mancanti/non calcolabili. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("MER088", "Studente già in possesso di altra borsa", Tutti, "119", "STUDENTE GIA' IN POSSESSO DI UNA BORSA DI STUDIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 119 (lettura 2026-11-02)", ""),
                Nuova("MER005", "Crediti dichiarati incongruenti con il corso di studi", Tutti, "233", "Crediti dichiarati incongruenti con il corso di studi", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "La regola applica limiti assoluti per durata/tipologia del corso; il 040 riguarda il limite dell’anno indicato dall’ateneo e il 204 il limite standard DPCM. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("MER071", "Esame complementare non valido per il merito AFAM", Tutti, "102", "ESAME COMPLEMENTARE NON SUPERATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 102 (lettura 2026-11-02)", ""),
                Verificata("MER072", "Anno di corso calcolato incongruente con l’anno di corso dichiarato", Tutti, "103", "ANNO CORSO CALCOLATO INCONGRUENTE CON L'ANNO DI CORSO DICHIARATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 103 (lettura 2026-11-02)", ""),
                Verificata("MER074", "Crediti riconosciuti insufficienti per il primo anno di specialistica", Tutti, "105", "NUMERO CREDITI INSUFFICIENTI PER LA PARTECIPAZIONE ALLA LAUREA SPECIALISTICA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 105 (lettura 2026-11-02)", ""),
                Verificata("MER085", "Utilizzo del bonus non ammesso per il titolo di accesso dichiarato", Tutti, "116", "UTILIZZO DEL BONUS IMPROPRIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 116 (lettura 2026-11-02)", ""),
                Nuova("MER086", "Utilizzo del bonus non ammesso in presenza di crediti riconosciuti non derivanti da trasferimento TS o con ripetenza", Tutti, "234", "Utilizzo del bonus non ammesso in presenza di crediti riconosciuti non derivanti da trasferimento TS o con ripetenza", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 207 riguarda documentazione dei bonus precedenti mancante, non il divieto per crediti riconosciuti/ripetenza. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Nuova("MER087", "Utilizzo del bonus non ammesso per carriera pregressa CD/AT con tipologia corso 9 o sede istituzione universitaria 2", Tutti, "235", "Utilizzo del bonus non ammesso per carriera pregressa CD/AT con tipologia corso 9 o sede istituzione universitaria 2", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 067 riguarda mancata indicazione dell’utilizzo del bonus, non il divieto per carriera pregressa CD/AT. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("MER012", "Merito insufficiente per la borsa", Tutti, "033", "NUMERO CREDITI POSSEDUTI NON SUFFICIENTE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 033 (lettura 2026-11-02)", ""),
                Verificata("MER092", "Crediti di tirocinio superiori ai crediti dichiarati", Tutti, "123", "NUMERO CREDITI DA TIROCINIO MAGGIORE AL NUMERO CREDITI TOTALI DICHIARATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 123 (lettura 2026-11-02)", ""),
                Verificata("MER089", "Titolo di accesso non ammesso per immatricolazione alla specialistica", Tutti, "120", "MANCANZA REQUISITI PER L'ISCRIZIONE ALLA SPECIALISTICA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 120 (lettura 2026-11-02)", ""),
                Nuova("MER090", "Titolo di accesso in attesa di conseguimento non valido dopo il 10/02", Tutti, "236", "Titolo di accesso in attesa di conseguimento non valido dopo il 10/02", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Causale specifica della scadenza del 10 febbraio. Il precedente candidato 224 è già occupato da dottorato/specializzazione con titolo triennale. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Nuova("MER091", "Titolo di accesso mancante per immatricolazione alla magistrale/specialistica", Tutti, "237", "Titolo di accesso mancante per immatricolazione alla magistrale/specialistica", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Titolo di accesso mancante, distinto dal titolo non ammesso MER089/120. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Nuova("MER093", "Iscrizione magistrale biennale non ammissibile: presente titolo triennale già conseguito e ulteriore titolo di ciclo unico/magistrale biennale in attesa", Tutti, "238", "Iscrizione magistrale biennale non ammissibile: presente titolo triennale già conseguito e ulteriore titolo di ciclo unico/magistrale biennale in attesa", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Combinazione di due titoli non rappresentata dal catalogo corrente. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Nuova("MER170", "Iscrizione non ammessa per Sapienza in vecchio ordinamento", Tutti, "239", "Iscrizione non ammessa per Sapienza in vecchio ordinamento", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il legacy scriveva 201, ma nel catalogo 2026/2027 il 201 riguarda il semestre filtro. GENF201 mantiene il significato DB e non è un alias di MER170. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("BS001", "Anno di corso oltre il limite ammesso per la borsa", "BS", "050", "ANNI DI FUORI CORSO INAMMISSIBILI PER LA BORSA DI STUDIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 050 (lettura 2026-11-02)", ""),
                Nuova("BS002", "Beneficio borsa già fruito e non restituito", "BS", "245", "Beneficio borsa già fruito e non restituito", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 051 presuppone una rinuncia; la regola corrente include benefici non restituiti anche in presenza di altre carriere pregresse. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("BS003", "Rinuncia pregressa alla borsa di studio", "BS", "052", "RINUNCIA ALLA BORSA DI STUDIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 052 (lettura 2026-11-02)", ""),
                Verificata("BS004", "Borsa già assegnata per lo stesso anno di corso equivalente", "BS", "222", "STUDENTE GIA' BENEFICIARIO DI BORSA DI STUDIO PER LO STESSO ANNO DI CORSO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 222 (lettura 2026-11-02)", "Il 222 descrive già una borsa fruita per lo stesso anno di corso. Il 064 identifica genericamente un vincitore."),
                Verificata("PA001", "Requisiti di status sede non ammessi per il posto alloggio: richiesto status B o D", "PA;PK", "054", "STUDENTE NON FUORI SEDE (POSTO ALLOGGIO)", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 054 (lettura 2026-11-02)", "054 scelto come causale canonica. Il 098 ha la stessa descrizione; la regola ammette gli status B e D. Vale anche per PK."),
                Nuova("PA002", "Comune della sede di studi non presente tra i pensionati attivi", "PA;PK", "240", "Comune della sede di studi non presente tra i pensionati attivi", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Comune non incluso nel catalogo dei pensionati attivi. Il 100 riguarda la residenza per una sede distaccata. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("PA003", "Anno di corso oltre il limite ammesso per il posto alloggio", "PA;PK", "055", "ANNI DI FUORI CORSO INAMMISSIBILI PER IL POSTO ALLOGGIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 055 (lettura 2026-11-02)", ""),
                Nuova("PA004", "Beneficio già fruito e non restituito per il posto alloggio", "PA;PK", "241", "Beneficio già fruito e non restituito per il posto alloggio", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 056 ha descrizione interna di benefici non restituiti, ma in graduatoria indica alloggio già fruito per lo stesso anno. Nuova causale con testo coerente per entrambi gli usi e per PK. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("PA005", "Rinuncia pregressa o corrente al posto alloggio", "PA;PK", "057", "RINUNCIA AL POSTO ALLOGGIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 057 (lettura 2026-11-02)", ""),
                Verificata("PA006", "Decadenza dal posto alloggio", "PA;PK", "079", "DECADENZA DAL POSTO ALLOGGIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 079 (lettura 2026-11-02)", ""),
                Verificata("PA007", "Revoca del posto alloggio per incompatibilità con il bando", "PA;PK", "084", "REVOCA PER INCOMPATIBILITA' CON IL BANDO (POSTO ALLOGGIO)", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 084 (lettura 2026-11-02)", ""),
                Verificata("PA008", "Revoca del posto alloggio per sede distaccata", "PA;PK", "088", "REVOCA PER SEDE DISTACCATA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 088 (lettura 2026-11-02)", ""),
                Verificata("CI001", "Primo anno non ammesso al contributo integrativo salvo tipologia corso 5", "CI", "071", "STUDENTE IMMATRICOLATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 071 (lettura 2026-11-02)", ""),
                Nuova("CI002", "Corso specialistico di tipologia 7 non ammesso al contributo integrativo", "CI", "242", "Corso specialistico di tipologia 7 non ammesso al contributo integrativo", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Tipologie_studi: 7 = Scuola di Specializzazione, non solo medica. Il 221 copre esclusivamente la specializzazione medica. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("CI003", "Anno di corso oltre il limite ammesso per il contributo integrativo", "CI", "072", "ANNI DI FUORI CORSO INAMMISSIBILI PER IL CONTRIBUTO INTEGRATIVO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 072 (lettura 2026-11-02)", ""),
                Nuova("CI004", "Beneficio già fruito e non restituito per il contributo integrativo", "CI", "246", "Beneficio già fruito e non restituito per il contributo integrativo", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il 073 presuppone una rinuncia; la regola include benefici non restituiti e la condizione comune di borsa non restituita. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("CI005", "Rinuncia pregressa o corrente al contributo integrativo", "CI", "074", "RINUNCIA AL CONTRIBUTO INTEGRATIVO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 074 (lettura 2026-11-02)", ""),
                Verificata("CI006", "Studente non selezionato per il contributo integrativo", "CI", "097", "STUDENTE NON SELEZIONATO DALL'UNIVERSITA' PER PROGRAMMI UE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 097 (lettura 2026-11-02)", ""),
                Verificata("CI007", "Decadenza dal contributo integrativo", "CI", "082", "DECADENZA DAL CONTRIBUTO INTEGRATIVO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 082 (lettura 2026-11-02)", ""),
                Verificata("CI008", "Revoca del contributo integrativo per incompatibilità con il bando", "CI", "087", "REVOCA PER INCOMPATIBILITA' CON IL BANDO (CONTRIBUTO INTEGRATIVO)", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 087 (lettura 2026-11-02)", ""),
                Nuova("ISC009", "Comune della sede di studi mancante o non classificabile", Tutti, "243", "Comune della sede di studi mancante o non classificabile", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Il controllo riguarda il comune della sede assente/non classificabile; il 012 dichiara l’intera sede studi non dichiarata. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("ISC010", "Comune di residenza mancante o non classificabile", Tutti, "010", "COMUNE DI RESIDENZA NON DICHIARATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 010 (lettura 2026-11-02)", ""),
                Verificata("VAR003", "Revoca di tutti i benefici da variazione", Tutti, "077", "REVOCA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 077 (lettura 2026-11-02)", ""),
                Verificata("VAR004", "Decadenza della borsa di studio da variazione", "BS", "078", "DECADENZA DALLA BORSA DI STUDIO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 078 (lettura 2026-11-02)", ""),
                Verificata("VAR011", "Revoca della borsa per incompatibilità con il bando", "BS", "083", "REVOCA PER INCOMPATIBILITA' CON IL BANDO (BORSA DI STUDIO)", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 083 (lettura 2026-11-02)", ""),
                Verificata("VAR019", "Revoca per mancata iscrizione", Tutti, "089", "REVOCA PER MANCATA ISCRIZIONE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 089 (lettura 2026-11-02)", ""),
                Verificata("VAR020", "Revoca per iscrizione come ripetente", Tutti, "090", "STUDENTE ISCRITTO RIPETENTE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 090 (lettura 2026-11-02)", ""),
                Nuova("VAR021", "Revoca per ISEE non presente in banca dati INPS in base ai riferimenti dichiarati nella domanda", Tutti, "244", "Revoca per ISEE non presente in banca dati INPS in base ai riferimenti dichiarati nella domanda", "ADISU.dbo.Decod_segnalazioni, AA 20262027; disponibilità verificata su tutto lo storico (lettura 2026-11-02)", "Tipologie_variaz 21: revoca per ISEE assente in INPS in base ai riferimenti dichiarati. Eliminata l’errata associazione agli anni fuori corso; 110 non distingue la revoca e 122 riguarda studente indipendente. Codice assegnato nella mappatura; da registrare in DB con lo script allegato."),
                Verificata("VAR022", "Revoca per studente già laureato", Tutti, "092", "REVOCA PER STUDENTE GIA' LAUREATO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 092 (lettura 2026-11-02)", ""),
                Verificata("VAR023", "Revoca per patrimonio oltre il limite", Tutti, "093", "REVOCA PER PATRIMONIO OLTRE IL LIMITE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 093 (lettura 2026-11-02)", ""),
                Verificata("VAR024", "Revoca per reddito oltre il limite", Tutti, "094", "REVOCA PER REDDITO OLTRE IL LIMITE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 094 (lettura 2026-11-02)", ""),
                Verificata("VAR025", "Revoca per mancanza esami o crediti", Tutti, "095", "REVOCA PER MANCANZA ESAMI/CREDITI", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 095 (lettura 2026-11-02)", ""),
                Verificata("VAR027", "Revoca per iscrizione fuori termine", Tutti, "111", "REVOCA PER ISCRIZIONE FUORI TERMINE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 111 (lettura 2026-11-02)", ""),
                Verificata("VAR028", "Revoca per ISEE fuori termine", Tutti, "112", "REVOCA PER ISEE FUORI TERMINE", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 112 (lettura 2026-11-02)", ""),
                Verificata("VAR029", "Revoca per ISEE non prodotta", Tutti, "113", "REVOCA PER ISEE NON PRODOTTA", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 113 (lettura 2026-11-02)", ""),
                Verificata("VAR030", "Decadenza per mancata comunicazione delle modalità di pagamento", Tutti, "114", "DECADENZA PER MANCATA COMUNICAZIONE MODALITA' DI PAGAMENTO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 114 (lettura 2026-11-02)", "Tipologie_variaz 30 e segnalazione 114 coincidono sulla mancata comunicazione delle modalità di pagamento. Corretta la precedente etichetta ISEE CAF senza cambiare il trigger."),
                Verificata("VAR031", "Revoca per mancanza contratto di locazione", Tutti, "115", "REVOCA PER MANCANZA CONTRATTO DI AFFITTO", "ADISU.dbo.Decod_segnalazioni, AA 20262027, codice 115 (lettura 2026-11-02)", ""),
            });

        private static readonly Dictionary<string, MotivoEsclusioneCatalogo> PerCodice = CreaIndice();

        // Decod_segnalazioni, AA 20262027, lettura 2026-11-02. I numeri non
        // presenti non diventano causali valide per il solo prefisso GENF.
        private static readonly IReadOnlyDictionary<string, string> ForzatureDb =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["201"] = "STUDENTE NON ISCRITTO AL SEMESTRE FILTRO, COME DA RISCONTRO DEL MIUR, CHE PRESENTA DOMANDA NELL’APERTURA STRAORDINARIA RISERVATA AGLI ISCRITTI AL SEMESTRE FILTRO",
                ["202"] = "GIA' IN POSSESSO DI UN TITOLO DI TERZO LIVELLO",
                ["203"] = "RISCONTRATA RIPETENZA INTERMEDIA",
                ["204"] = "NUMERO CREDITI DICHIARATO SUPERIORE AL LIMITE STANDARD PREVISTO DAL DPCM",
                ["205"] = "Documentazione relativa al titolo di soggiorno caricata oltre i termini",
                ["206"] = "ASSEGNATARI DI POSTO ALLOGGIO EXTRA BANDO",
                ["207"] = "MANCATA PRESENTAZIONE DELLA DOCUMENTAZIONE DEI CREDITI DI BONUS NON FRUITI NELLA PRECEDENTE CARRIERA",
                ["208"] = "CORSO NON ATTIVATO PER L'ANNO ACCADEMICO CORRENTE",
                ["209"] = "INCOMPATIBILITA' CON IL BANDO",
                ["210"] = "Iscrizione ad un corso di livello pari al titolo già conseguito (art. 3, c. 4,  DPCM 9 aprile 2001)",
                ["212"] = "ISCRIZIONE NON CONFERMATA DALL'ATENEO DICHIARATO",
                ["220"] = "NESSUNA RICEVUTA INVIATA O VALIDATA",
                ["221"] = "ISCRITTO AD UN CORSO DI SPECIALIZZAZIONE MEDICA",
                ["222"] = "STUDENTE GIA' BENEFICIARIO DI BORSA DI STUDIO PER LO STESSO ANNO DI CORSO",
                ["223"] = "STUDENTE ISCRITTO AD UN CORSO DI PRIMO LUVELLO CON UN TITOLO GIA' CONSEGUITO ALL'ESTERO",
                ["224"] = "ISCRIZIONE A DOTTORATO/SPECIALIZZAZIONE CON TITOLO DI ACCESSO TRIENNALE"
            };

        public static string GetDescrizione(string codice)
        {
            if (PerCodice.TryGetValue(Normalize(codice), out var voce))
                return voce.DescrizioneRegola;

            string normalized = Normalize(codice);
            if (TryGetForzatura(normalized, out string numero))
            {
                if (ForzatureDb.TryGetValue(numero, out string? descrizione))
                    return descrizione;
                foreach (var motivo in Voci)
                    if (motivo.CodiceSegnalazione == numero)
                        return motivo.DescrizioneRegola;
            }

            if (normalized.StartsWith("GENF", StringComparison.Ordinal))
                return $"Forzatura generale attiva {normalized.Substring(4)}";

            return codice;
        }

        public static MotivoEsclusioneCatalogo Risolvi(string codice, string beneficio)
        {
            string normalizedCode = Normalize(codice);
            string normalizedBenefit = Normalize(beneficio);
            if (!PerCodice.TryGetValue(normalizedCode, out var voce))
            {
                if (TryGetForzatura(normalizedCode, out string codiceSegnalazione))
                {
                    if (ForzatureDb.TryGetValue(codiceSegnalazione, out string? descrizione))
                    {
                        voce = Verificata(normalizedCode, descrizione, Tutti,
                            codiceSegnalazione, descrizione,
                            $"ADISU.dbo.Decod_segnalazioni, AA 20262027, codice {codiceSegnalazione} (lettura 2026-11-02)",
                            "Forzatura generale: conserva il significato corrente della causale DB.");
                    }
                    else
                    {
                        foreach (var motivo in Voci)
                        {
                            if (motivo.CodiceSegnalazione == codiceSegnalazione)
                            {
                                voce = motivo with { CodiceRegola = normalizedCode, Benefici = Tutti };
                                break;
                            }
                        }
                    }
                }

                if (voce == null)
                {
                    return new MotivoEsclusioneCatalogo(normalizedCode, GetDescrizione(normalizedCode),
                        Tutti, null, null, StatoMappaturaMotivo.Sconosciuta, string.Empty,
                        normalizedCode.StartsWith("GENF", StringComparison.OrdinalIgnoreCase)
                            ? "Forzatura non mappabile: codice assente dal catalogo AA 20262027 e dai nuovi codici assegnati, oppure formato GENF non valido."
                            : "Codice regola non presente nel catalogo dei motivi.");
                }
            }

            if (Array.IndexOf(voce.Benefici.Split(';'), normalizedBenefit) < 0)
            {
                return voce with
                {
                    CodiceSegnalazione = null,
                    DescrizioneSegnalazione = null,
                    Stato = StatoMappaturaMotivo.Sconosciuta,
                    Nota = $"Motivo non applicabile al beneficio {normalizedBenefit}; benefici previsti: {voce.Benefici}."
                };
            }

            return voce;
        }

        public static MappaturaMotiviEsclusione Mappa(string? codiciDelimitatiSemicolon, string beneficio)
        {
            var codici = new List<string>();
            var diagnostica = new List<string>();
            var regoleViste = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var destinazioniViste = new HashSet<string>(StringComparer.Ordinal);
            string normalizedBenefit = Normalize(beneficio);
            bool richiedeRegistrazione = false;
            bool completa = true;

            foreach (string rawCode in (codiciDelimitatiSemicolon ?? string.Empty).Split(';'))
            {
                string code = Normalize(rawCode);
                if (code.Length == 0 || !regoleViste.Add(code))
                    continue;

                var voce = Risolvi(code, normalizedBenefit);
                if ((voce.Stato == StatoMappaturaMotivo.Verificata || voce.Stato == StatoMappaturaMotivo.DaRegistrare)
                    && voce.CodiceSegnalazione != null)
                {
                    if (destinazioniViste.Add(voce.CodiceSegnalazione))
                        codici.Add(voce.CodiceSegnalazione);
                    if (voce.Stato == StatoMappaturaMotivo.DaRegistrare)
                    {
                        richiedeRegistrazione = true;
                        diagnostica.Add($"{code} [{normalizedBenefit}]: codice {voce.CodiceSegnalazione} assegnato, da registrare in Decod_segnalazioni. {voce.Nota}");
                    }
                    continue;
                }

                completa = false;
                diagnostica.Add($"{code} [{normalizedBenefit}]: {voce.Stato}. {voce.Nota}");
            }

            return new MappaturaMotiviEsclusione(string.Join(";", codici), completa,
                string.Join(" | ", diagnostica), richiedeRegistrazione);
        }

        private static Dictionary<string, MotivoEsclusioneCatalogo> CreaIndice()
        {
            var result = new Dictionary<string, MotivoEsclusioneCatalogo>(StringComparer.OrdinalIgnoreCase);
            var codiciAssegnati = new HashSet<string>(StringComparer.Ordinal);
            foreach (var voce in Voci)
            {
                result.Add(voce.CodiceRegola, voce);
                if (voce.Stato == StatoMappaturaMotivo.Verificata || voce.Stato == StatoMappaturaMotivo.DaRegistrare)
                {
                    if (string.IsNullOrWhiteSpace(voce.CodiceSegnalazione)
                        || string.IsNullOrWhiteSpace(voce.DescrizioneSegnalazione)
                        || !codiciAssegnati.Add(voce.CodiceSegnalazione))
                    {
                        throw new InvalidOperationException(
                            $"Catalogo motivi non valido: codice/descrizione assente o codice duplicato per {voce.CodiceRegola}.");
                    }
                }
                else if (voce.CodiceSegnalazione != null)
                {
                    throw new InvalidOperationException(
                        $"Catalogo motivi non valido: {voce.CodiceRegola} ha un codice ufficiale senza mappatura verificata.");
                }
            }
            return result;
        }

        private static bool TryGetForzatura(string codice, out string codiceSegnalazione)
        {
            codiceSegnalazione = string.Empty;
            if (codice.Length != 7 || !codice.StartsWith("GENF", StringComparison.Ordinal))
                return false;

            for (int i = 4; i < codice.Length; i++)
            {
                if (codice[i] < '0' || codice[i] > '9')
                    return false;
            }

            if (!int.TryParse(codice.Substring(4), NumberStyles.None, CultureInfo.InvariantCulture, out int value)
                || value <= 200 || value > 999)
            {
                return false;
            }

            codiceSegnalazione = value.ToString("D3", CultureInfo.InvariantCulture);
            return true;
        }

        private static MotivoEsclusioneCatalogo Verificata(string codice, string descrizione, string benefici,
            string codiceSegnalazione, string descrizioneSegnalazione, string fonte, string nota)
            => new(codice, descrizione, benefici, codiceSegnalazione, descrizioneSegnalazione,
                StatoMappaturaMotivo.Verificata, fonte, nota);

        private static MotivoEsclusioneCatalogo Nuova(string codice, string descrizione, string benefici,
            string codiceSegnalazione, string descrizioneSegnalazione, string fonte, string nota)
            => new(codice, descrizione, benefici, codiceSegnalazione, descrizioneSegnalazione,
                StatoMappaturaMotivo.DaRegistrare, fonte, nota);

        private static string Normalize(string? value)
            => (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
