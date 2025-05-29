using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class SelectTipoPagam : Form
    {
        List<Beneficio> beneficioList = new List<Beneficio>();
        List<TipologiaPagamento> tipologiaPagamentoList = new List<TipologiaPagamento>();
        List<CategoriaPagamento> categoriaList = new List<CategoriaPagamento>();
        List<DaPagareItem> daPagareList = new List<DaPagareItem>();
        SqlTransaction? sqlTransaction = null;

        public SelectTipoPagam(SqlConnection conn, SqlTransaction sqlTransaction)
        {
            InitializeComponent();

            this.sqlTransaction = sqlTransaction;

            beneficioList = GenerateBeneficiList(conn);

            if (beneficioList.Count > 0)
            {
                PopulateBeneficioComboBox();
            }
        }

        private void PopulateBeneficioComboBox()
        {
            Dictionary<string, string> benefici = new Dictionary<string, string>();
            foreach (Beneficio beneficio in beneficioList)
            {
                benefici.Add(beneficio.codBeneficio, beneficio.nomeBeneficio);
            }
            CreatePagamentiComboBox(ref promptBeneficioCombo, benefici);
        }
        private void PopulateTipoPagamComboBox(string selectedBeneficio)
        {
            Beneficio? nullableBeneficio = null;

            foreach (Beneficio beneficio in beneficioList)
            {
                if (beneficio.codBeneficio == selectedBeneficio)
                {
                    nullableBeneficio = beneficio;
                    break;
                }
            }

            if (nullableBeneficio == null)
            {
                Logger.LogError(null, "Beneficio selezionato è nullo");
                return;
            }

            Beneficio selected = nullableBeneficio;
            Dictionary<string, string> tipoPagamenti = new Dictionary<string, string>();
            foreach (TipologiaPagamento tipoPagamento in selected.tipologiePagamenti)
            {
                tipoPagamenti.Add(tipoPagamento.nomeTipologiaPagamento, tipoPagamento.nomeTipologiaPagamento);
            }
            tipologiaPagamentoList = selected.tipologiePagamenti;
            CreatePagamentiComboBox(ref promptTipoPagamCombo, tipoPagamenti);
        }

        private void PopulateCatPagamentoComboBox(string selectedTipoPagam)
        {
            TipologiaPagamento? nullableTipologiaPagam = null;

            foreach (TipologiaPagamento tipoPagam in tipologiaPagamentoList)
            {
                if (tipoPagam.nomeTipologiaPagamento == selectedTipoPagam)
                {
                    nullableTipologiaPagam = tipoPagam;
                    break;
                }
            }

            if (nullableTipologiaPagam == null)
            {
                Logger.LogError(null, "La tipologia di pagamento selezionata è nulla");
                return;
            }

            TipologiaPagamento selected = nullableTipologiaPagam;

            Dictionary<string, string> categoriaPagamenti = new Dictionary<string, string>();
            foreach (CategoriaPagamento categoria in selected.categoria)
            {
                categoriaPagamenti.Add(categoria.nomeCategoria, categoria.cod_categoria);
            }
            categoriaList = selected.categoria;
            CreatePagamentiComboBox(ref promptCategoriaPagamCombo, categoriaPagamenti, true);
        }

        private void PopulateEffettuarePagamentoComboBox(string selectedCategoria)
        {
            CategoriaPagamento? nullableCategoraPagam = null;

            foreach (CategoriaPagamento cat in categoriaList)
            {
                if (cat.nomeCategoria == selectedCategoria)
                {
                    nullableCategoraPagam = cat;
                    break;
                }
            }

            if (nullableCategoraPagam == null)
            {
                Logger.LogError(null, "La categoria di pagamento selezionata è nulla");
                return;
            }

            CategoriaPagamento selected = nullableCategoraPagam;

            Dictionary<string, string> effettuarePagamenti = new Dictionary<string, string>();
            foreach (DaPagareItem daPagare in selected.daPagare)
            {
                effettuarePagamenti.Add(daPagare.Key, daPagare.Value);
            }
            daPagareList = selected.daPagare;
            CreatePagamentiComboBox(ref promptEffettuarePagamCombo, effettuarePagamenti);
        }

        public static void CreatePagamentiComboBox(ref ComboBox comboBox, Dictionary<string, string> toInsert, bool inverted = false)
        {
            comboBox.Items.Clear();
            foreach (KeyValuePair<string, string> item in toInsert)
            {
                if (!inverted)
                {
                    comboBox.Items.Add(new { Text = $"{item.Value}", Value = item.Key });
                }
                else
                {
                    comboBox.Items.Add(new { Text = $"{item.Key}", Value = item.Value });
                }
            }
            comboBox.DisplayMember = "Text";
            comboBox.ValueMember = "Value";
        }
        public string SelectedCodPagamento
        {
            get
            {
                dynamic selectedCodPagamentoObj = promptEffettuarePagamCombo.SelectedItem;
                string selectedCodPagamento = selectedCodPagamentoObj?.Value ?? "";

                return selectedCodPagamento;
            }
        }

        public string SelectedTipoBeneficio
        {
            get
            {
                dynamic selectedCodBeneficioObj = promptBeneficioCombo.SelectedItem;
                string selectedCodBeneficio = selectedCodBeneficioObj?.Value ?? "";

                dynamic selectedCodBeneficioObjRiepilogo = promptBeneficioCombo.SelectedItem;
                string selectedCodBeneficioRiepilogo = selectedCodBeneficioObjRiepilogo?.Text ?? "";
                RiepilogoArguments.Instance.tipoBeneficio = selectedCodBeneficioRiepilogo;

                return selectedCodBeneficio;
            }
        }

        public string SelectedCategoriaBeneficio
        {
            get
            {
                dynamic selectedCatPagamentoObj = promptCategoriaPagamCombo.SelectedItem;
                string selectedCatPagamento = selectedCatPagamentoObj?.Value ?? "";

                return selectedCatPagamento;
            }
        }

        private void promptTipoPagamOkBtn_Click(object sender, EventArgs e)
        {
            dynamic selectedCatPagamentoObjRiepilogo = promptCategoriaPagamCombo.SelectedItem;
            string selectedCatPagamentoRiepilogo = selectedCatPagamentoObjRiepilogo?.Text ?? "";
            dynamic selectedCodPagamentoObjRiepilogo = promptEffettuarePagamCombo.SelectedItem;
            string selectedCodPagamentoRiepilogo = selectedCodPagamentoObjRiepilogo?.Text ?? "";
            RiepilogoArguments.Instance.pagamentoSelezionato = selectedCatPagamentoRiepilogo + " " + selectedCodPagamentoRiepilogo;

            this.DialogResult = DialogResult.OK;
        }

        private void promptTipoPagamNokBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }

        private void promptBeneficioCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            dynamic pagamentiSelectedBeneficioObj = promptBeneficioCombo.SelectedItem;
            string pagamentiSelectedBeneficio = pagamentiSelectedBeneficioObj?.Value ?? "";
            PopulateTipoPagamComboBox(pagamentiSelectedBeneficio);
            promptCategoriaPagamCombo.Items.Clear();
            promptEffettuarePagamCombo.Items.Clear();
        }

        private void promptTipoPagamCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            dynamic pagamentiSelectedTipoPagamentoObj = promptTipoPagamCombo.SelectedItem;
            string pagamentiSelectedTipoPagamento = pagamentiSelectedTipoPagamentoObj?.Value ?? "";
            PopulateCatPagamentoComboBox(pagamentiSelectedTipoPagamento);
            promptEffettuarePagamCombo.Items.Clear();
        }

        private void promptCategoriaPagamCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            dynamic pagamentiSelectedCategoriaObj = promptCategoriaPagamCombo.SelectedItem;
            string pagamentiSelectedCategoria = pagamentiSelectedCategoriaObj?.Text ?? "";
            PopulateEffettuarePagamentoComboBox(pagamentiSelectedCategoria);
        }

        List<Beneficio> GenerateBeneficiList(SqlConnection conn)
        {
            List<Beneficio> returnList = new List<Beneficio>();

            string sql = @"
                    SELECT     
	                    Tipologie_benefici.Cod_beneficio, Tipologie_benefici.Descrizione, Tipologie_pagam.*
                    FROM 
	                    Tipologie_pagam 
	                    INNER JOIN Tipologie_benefici ON Tipologie_benefici.Cod_beneficio = Left(Tipologie_pagam.cod_tipo_pagam, 2)
                    WHERE        
	                    LEN(cod_tipo_pagam) = 4";

            SqlCommand cmd = new SqlCommand(sql, conn, sqlTransaction);

            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (Utilities.SafeGetString(reader, "categoria_pagamento") == "00")
                    {
                        continue;
                    }
                    string codBeneficio = Utilities.SafeGetString(reader, "Cod_beneficio").Substring(0, 2);
                    Beneficio? beneficio = returnList.Find(b => b.codBeneficio == codBeneficio);
                    if (beneficio == null)
                    {
                        beneficio = new Beneficio
                        {
                            nomeBeneficio = Utilities.SafeGetString(reader, "Descrizione"),
                            codBeneficio = codBeneficio,
                            tipologiePagamenti = new List<TipologiaPagamento>()
                        };
                        returnList.Add(beneficio);
                    }

                    string nomeTipologiaPagam = Utilities.SafeGetString(reader, "descr_interno_tipo_pagamento");
                    TipologiaPagamento? tipologiaPagamento = beneficio.tipologiePagamenti.Find(t => t.nomeTipologiaPagamento == nomeTipologiaPagam);
                    if (tipologiaPagamento == null)
                    {
                        tipologiaPagamento = new TipologiaPagamento
                        {
                            nomeTipologiaPagamento = nomeTipologiaPagam,
                            categoria = new List<CategoriaPagamento>()
                        };
                        beneficio.tipologiePagamenti.Add(tipologiaPagamento);
                    }

                    string nomCategoria = Utilities.SafeGetString(reader, "descr_interno_cat_pagamento");
                    CategoriaPagamento? categoriaPagamento = tipologiaPagamento.categoria.Find(c => c.nomeCategoria == nomCategoria);

                    if (categoriaPagamento == null)
                    {
                        categoriaPagamento = new CategoriaPagamento
                        {
                            nomeCategoria = nomCategoria,
                            cod_categoria = Utilities.SafeGetString(reader, "categoria_pagamento"),
                            daPagare = new List<DaPagareItem>()
                        };
                        tipologiaPagamento.categoria.Add(categoriaPagamento);
                    }

                    DaPagareItem daPagareItem = new DaPagareItem
                    {
                        Key = Utilities.SafeGetString(reader, "cod_tipo_pagam").Substring(2, 2),
                        Value = Utilities.SafeGetString(reader, "descr_interno_tipo_emissione")
                    };

                    categoriaPagamento.daPagare.Add(daPagareItem);
                }
            }

            return returnList;
        }

    }

    public class Beneficio
    {
        public string nomeBeneficio { get; set; }
        public string codBeneficio { get; set; }
        public List<TipologiaPagamento> tipologiePagamenti { get; set; }

        public Beneficio()
        {
            nomeBeneficio = string.Empty;
            codBeneficio = string.Empty;
            tipologiePagamenti = new List<TipologiaPagamento>();
        }
    }

    public class TipologiaPagamento
    {
        public string nomeTipologiaPagamento { get; set; }
        public List<CategoriaPagamento> categoria { get; set; }
        public TipologiaPagamento()
        {
            nomeTipologiaPagamento = string.Empty;
            categoria = new List<CategoriaPagamento>();
        }
    }

    public class CategoriaPagamento
    {
        public string nomeCategoria { get; set; }
        public List<DaPagareItem> daPagare { get; set; }
        public string cod_categoria { get; set; }
        public CategoriaPagamento()
        {
            nomeCategoria = string.Empty;
            daPagare = new List<DaPagareItem>();
            cod_categoria = string.Empty;
        }
    }

    public class DaPagareItem
    {
        public string Key { get; set; }
        public string Value { get; set; }

        public DaPagareItem()
        {
            Key = string.Empty;
            Value = string.Empty;
        }
    }

}
