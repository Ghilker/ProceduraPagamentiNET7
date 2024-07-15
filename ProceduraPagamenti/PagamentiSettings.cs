using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ProcedureNet7
{
    static class PagamentiSettings
    {

        public static Dictionary<string, string> pagamentiTipoProcedura = new()
        {
            { "0", "Procedura Completa" },
            { "1", "Procedura Senza Creazione Tabella" },
            { "2", "Solo Creazione Tabella D'Appoggio" },
        };

        public static void CreatePagamentiComboBox(ref ComboBox comboBox, Dictionary<string, string> toInsert, bool needCode = false)
        {
            comboBox.Items.Clear();
            foreach (KeyValuePair<string, string> item in toInsert)
            {
                if (!needCode)
                {
                    _ = comboBox.Items.Add(new { Text = item.Value, Value = item.Key });
                }
                else
                {
                    _ = comboBox.Items.Add(new { Text = $"{item.Key} - {item.Value}", Value = item.Key });
                }
            }
            comboBox.DisplayMember = "Text";
            comboBox.ValueMember = "Value";
        }

        public static string SQLTabellaAppoggio = $@"
                            WITH MaxEsitiConcorsiTemp as (
                            SELECT
                                Num_domanda
                                ,Anno_accademico
                                ,MAX(data_validita) AS MaxDataValidita
                            FROM
                                Esiti_concorsi
                            WHERE
		                            data_validita <= @maxDataValidita
                                AND Esiti_concorsi.Cod_beneficio = @codBeneficio
                                AND Anno_accademico = @annoAccademico
                            GROUP BY
		                            Num_domanda,
		                            Anno_accademico),
                            MaxEsitiConcorsiPATemp as (
                            SELECT
                                Num_domanda
                                ,Anno_accademico
                                ,MAX(data_validita) AS MaxDataValidita
                            FROM
                                Esiti_concorsi
                            WHERE
		                            data_validita <= @maxDataValidita
                                AND Esiti_concorsi.Cod_beneficio = 'PA'
                                AND Anno_accademico = @annoAccademico
                            GROUP BY
		                            Num_domanda,
		                            Anno_accademico),
		                            MaxDateAppartenenza as (
                            SELECT
                                cod_fiscale
                                ,anno_accademico
                                ,MAX(data_validita) AS MaxDataValidita
                            FROM
                                dbo.Appartenenza
                            WHERE
		                            data_validita <= @maxDataValidita
                                AND Anno_accademico = @annoAccademico
                            GROUP BY
		                            cod_fiscale,
		                            anno_accademico),

                                AppartenenzaTotali
                                AS
                                (
                                    SELECT
                                        Appartenenza.Cod_ente
                                        ,Appartenenza.Cod_fiscale
                                        ,Appartenenza.Anno_accademico
                                    FROM
                                        [Appartenenza] INNER JOIN MaxDateAppartenenza ON Appartenenza.Cod_fiscale = MaxDateAppartenenza.Cod_fiscale AND Appartenenza.Anno_accademico = MaxDateAppartenenza.Anno_accademico
                                    WHERE Appartenenza.Data_validita = MaxDateAppartenenza.MaxDataValidita AND Appartenenza.Anno_accademico = @annoAccademico
                                )

                                ,MaxValoriCalcolati
                                AS
                                (
                                    SELECT
                                        Num_domanda
                                        ,anno_accademico
                                        ,MAX(data_validita) AS MaxDataValidita
                                    FROM
                                        dbo.Valori_calcolati
                                    WHERE
                                    data_validita <= @maxDataValidita
                                        AND Anno_accademico = @annoAccademico
                                    GROUP BY
                                    Num_domanda,
                                    anno_accademico
                                )

                                ,ValoriCalcolatiTotali
                                AS
                                (
                                    SELECT
                                        Valori_calcolati.Anno_accademico
                                        ,Valori_calcolati.Num_domanda
                                        ,Valori_calcolati.Anno_corso
                                        ,Valori_calcolati.Status_sede
                                    FROM
                                        Valori_calcolati INNER JOIN MaxValoriCalcolati ON Valori_calcolati.Num_domanda = MaxValoriCalcolati.Num_domanda AND Valori_calcolati.Anno_accademico = MaxValoriCalcolati.Anno_accademico
                                    WHERE Valori_calcolati.Data_validita = MaxValoriCalcolati.MaxDataValidita AND Valori_calcolati.Anno_accademico = @annoAccademico
                                )

                                ,MaxDatiGeneraliDom
                                AS
                                (
                                    SELECT
                                        Num_domanda
                                        ,anno_accademico
                                        ,MAX(data_validita) AS MaxDataValidita
                                    FROM
                                        dbo.DatiGenerali_dom
                                    WHERE
                                    data_validita <= @maxDataValidita
                                        AND Anno_accademico = @annoAccademico
                                    GROUP BY
                                    Num_domanda,
                                    anno_accademico
                                )

                                ,DatiGeneraliDomTotali
                                AS
                                (
                                    SELECT
                                        DatiGenerali_dom.Anno_accademico
                                        ,DatiGenerali_dom.Num_domanda
                                        ,Tipo_studente
                                        ,Invalido
                                        ,Iscrizione_FuoriTermine
                                        ,Pagamento_tassareg
                                        ,Blocco_pagamento
                                        ,esonero_pag_tassa_reg
                                        ,Conferma_PA
                                        ,Superamento_esami
                                        ,Superamento_esami_tassa_reg
                                    FROM
                                        DatiGenerali_dom INNER JOIN MaxDatiGeneraliDom ON DatiGenerali_dom.Num_domanda = MaxDatiGeneraliDom.Num_domanda AND DatiGenerali_dom.Anno_accademico = MaxDatiGeneraliDom.Anno_accademico
                                    WHERE DatiGenerali_dom.Data_validita = MaxDatiGeneraliDom.MaxDataValidita AND DatiGenerali_dom.Anno_accademico = @annoAccademico and DatiGenerali_dom.Blocco_pagamento<>1
                                )

                                ,MaxIscrizioni
                                AS
                                (
                                    SELECT
                                        anno_accademico
                                        ,cod_fiscale
                                        ,MAX(data_validita) AS MaxDataValidita
                                    FROM
                                        iscrizioni
                                    WHERE 
		                            data_validita <= @maxDataValidita
                                        AND Anno_accademico = @annoAccademico
                                    GROUP BY 
		                            anno_accademico, 
		                            cod_fiscale
                                )

                                ,IscrizioniTotali
                                AS
                                (
                                    SELECT
                                        i.cod_fiscale
                                        ,i.cod_tipologia_studi AS cod_corso
                                        ,i.anno_accademico
                                        ,i.cod_facolta AS facolta
                                        ,i.cod_sede_studi AS sede_studi
                                    FROM
                                        iscrizioni i
                                        INNER JOIN MaxIscrizioni mdv ON i.anno_accademico = mdv.anno_accademico AND i.cod_fiscale = mdv.cod_fiscale AND i.data_validita = mdv.MaxDataValidita
                                    WHERE i.anno_accademico = @annoAccademico
                                )

                                ,EsitiConcorsiTotali
                                AS
                                (
                                    SELECT
                                        Esiti_concorsi.Cod_tipo_esito
                                        ,Esiti_concorsi.Cod_beneficio
                                        ,Esiti_concorsi.Imp_beneficio
                                        ,Esiti_concorsi.Anno_accademico
                                        ,Esiti_concorsi.Num_domanda
                                    FROM
                                        Esiti_concorsi INNER JOIN MaxEsitiConcorsiTemp ON Esiti_concorsi.Num_domanda = MaxEsitiConcorsiTemp.Num_domanda AND Esiti_concorsi.Anno_accademico = MaxEsitiConcorsiTemp.Anno_accademico
                                    WHERE Esiti_concorsi.Data_validita = MaxEsitiConcorsiTemp.MaxDataValidita AND Esiti_concorsi.Anno_accademico = @annoAccademico AND Esiti_concorsi.Cod_beneficio = @codBeneficio
                                )

                                ,EsitiPA  AS
                                (
                                    SELECT
                                        Esiti_concorsi.Cod_tipo_esito
                                        ,Esiti_concorsi.Cod_beneficio
                                        ,Esiti_concorsi.Imp_beneficio
                                        ,Esiti_concorsi.Anno_accademico
                                        ,Esiti_concorsi.Num_domanda
                                    FROM
                                        Esiti_concorsi INNER JOIN MaxEsitiConcorsiPATemp ON Esiti_concorsi.Num_domanda = MaxEsitiConcorsiPATemp.Num_domanda AND Esiti_concorsi.Anno_accademico = MaxEsitiConcorsiPATemp.Anno_accademico
                                    WHERE Esiti_concorsi.Data_validita = MaxEsitiConcorsiPATemp.MaxDataValidita AND Esiti_concorsi.Anno_accademico = @annoAccademico AND Esiti_concorsi.Cod_beneficio = 'PA' and Cod_tipo_esito = 2
                                )

                                ,EsitiTotali
                                AS
                                (
                                    SELECT
                                        DISTINCT
                                        Domanda.*
	                                    ,EsitiConcorsiTotali.Cod_tipo_esito
                                        ,EsitiConcorsiTotali.Cod_beneficio
                                        ,EsitiConcorsiTotali.Imp_beneficio
	                                    ,ValoriCalcolatiTotali.Status_sede
                                        ,ValoriCalcolatiTotali.Anno_corso
	                                    ,AppartenenzaTotali.Cod_ente
	                                    ,DatiGeneraliDomTotali.Tipo_studente
                                        ,DatiGeneraliDomTotali.Invalido
                                        ,DatiGeneraliDomTotali.Iscrizione_FuoriTermine
                                        ,DatiGeneraliDomTotali.Pagamento_tassareg
	                                    ,DatiGeneraliDomTotali.Blocco_pagamento
                                        ,DatiGeneraliDomTotali.esonero_pag_tassa_reg
                                        ,DatiGeneraliDomTotali.Conferma_PA
                                        ,DatiGeneraliDomTotali.Superamento_esami
                                        ,DatiGeneraliDomTotali.Superamento_esami_tassa_reg
                                        ,EsitiPA.Cod_tipo_esito as EsitoPA
                                    FROM
                                        domanda INNER JOIN
                                        AppartenenzaTotali ON domanda.Cod_fiscale=AppartenenzaTotali.Cod_fiscale AND Domanda.Anno_accademico = AppartenenzaTotali.Anno_accademico LEFT OUTER JOIN
                                        ValoriCalcolatiTotali ON Domanda.Anno_accademico=ValoriCalcolatiTotali.Anno_accademico AND Domanda.Num_domanda=ValoriCalcolatiTotali.Num_domanda INNER JOIN
                                        DatiGeneraliDomTotali ON Domanda.Anno_accademico = DatiGeneraliDomTotali.Anno_accademico AND Domanda.Num_domanda = DatiGeneraliDomTotali.Num_domanda INNER JOIN
                                        EsitiConcorsiTotali ON Domanda.Anno_accademico = EsitiConcorsiTotali.Anno_accademico AND Domanda.Num_domanda = EsitiConcorsiTotali.Num_domanda LEFT OUTER JOIN
									    EsitiPA ON Domanda.Anno_accademico = EsitiPA.Anno_accademico AND Domanda.Num_domanda = EsitiPA.Num_domanda LEFT OUTER JOIN
                                        DOMANDA_PREMI_LAUREA ON domanda.Anno_accademico = DOMANDA_PREMI_LAUREA.ANNO_ACCADEMICO AND domanda.Cod_fiscale = DOMANDA_PREMI_LAUREA.COD_FISCALE
                                    )

                                ,BeneficiRichiestiTotali
                                AS
                                (
                                    SELECT
                                        *
                                    FROM
                                        Benefici_richiesti
                                    WHERE 
		                            Riga_valida = '0'AND
                                        data_fine_validita IS NULL AND
                                        Anno_accademico = @annoAccademico
                                )

                                ,StatisticheTotali
                                AS
                                (
                                    SELECT
                                        DISTINCT
                                        domanda.anno_accademico
		                            ,domanda.cod_fiscale
		                            ,Studente.Cognome
		                            ,Studente.Nome
		                            ,Studente.Data_nascita
                                    ,Studente.sesso
		                            ,domanda.num_domanda
		                            ,EsitiTotali.Cod_tipo_esito
		                            ,EsitiTotali.Status_sede
		                            ,vCittadinanza.Cod_cittadinanza
		                            ,EsitiTotali.Cod_ente
		                            ,EsitiTotali.Cod_beneficio
                                    ,EsitiTotali.EsitoPA
		                            ,EsitiTotali.Anno_corso
		                            ,invalido AS disabile
		                            ,imp_beneficio AS imp_beneficio
		                            ,iscrizione_fuoritermine AS iscrizione_fuoritermine
		                            ,pagamento_tassareg AS pagamento_tassareg
		                            ,blocco_pagamento AS blocco_pagamento
		                            ,esonero_pag_tassa_reg AS esonero_pag_tassa_reg
		                            ,IscrizioniTotali.cod_corso
		                            ,IscrizioniTotali.facolta
		                            ,IscrizioniTotali.sede_studi
		                            ,Superamento_esami
		                            ,Superamento_esami_tassa_reg

                                    FROM
                                        Domanda INNER JOIN
                                        IscrizioniTotali ON Domanda.Cod_fiscale=IscrizioniTotali.Cod_fiscale AND Domanda.Anno_accademico = IscrizioniTotali.Anno_accademico INNER JOIN
                                        EsitiTotali ON Domanda.Anno_accademico = EsitiTotali.Anno_accademico AND Domanda.Num_domanda = EsitiTotali.Num_domanda INNER JOIN
                                        dbo.Studente ON Domanda.Cod_fiscale = dbo.Studente.Cod_fiscale LEFT OUTER JOIN
                                        dbo.vCittadinanza ON Domanda.Cod_fiscale = vCittadinanza.Cod_fiscale LEFT OUTER JOIN
                                        BeneficiRichiestiTotali ON Domanda.Anno_accademico = BeneficiRichiestiTotali.Anno_accademico AND Domanda.Num_domanda = BeneficiRichiestiTotali.Num_domanda 
                                    WHERE
		                                Cod_tipo_esito='2' AND
                                        domanda.Anno_accademico = @annoAccademico AND
                                        Domanda.Tipo_bando in ('lz', 'pl', 'bl', 'ca')

                                )
                            ";

    }
}
