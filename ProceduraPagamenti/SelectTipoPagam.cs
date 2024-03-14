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
        string jsonPath = "ProcedureNet7.ProceduraPagamenti.JSON.PagamentiBenefici.json";
        List<Beneficio> beneficioList;
        List<Beneficio> test;
        List<TipologiaPagamento> tipologiaPagamentoList;
        List<CategoriaPagamento> categoriaList;
        List<DaPagareItem> daPagareList;

        public SelectTipoPagam(SqlConnection conn)
        {
            InitializeComponent();

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
            Beneficio selected = null;
            foreach (Beneficio beneficio in beneficioList)
            {
                if (beneficio.codBeneficio == selectedBeneficio)
                {
                    selected = beneficio;
                    break;
                }
            }
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
            TipologiaPagamento selected = null;
            foreach (TipologiaPagamento tipoPagam in tipologiaPagamentoList)
            {
                if (tipoPagam.nomeTipologiaPagamento == selectedTipoPagam)
                {
                    selected = tipoPagam; break;
                }
            }
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
            CategoriaPagamento selected = null;
            foreach (CategoriaPagamento cat in categoriaList)
            {
                if (cat.nomeCategoria == selectedCategoria)
                {
                    selected = cat; break;
                }
            }
            Dictionary<string, string> effettuarePagamenti = new Dictionary<string, string>();
            foreach (DaPagareItem daPagare in selected.daPagare)
            {
                effettuarePagamenti.Add(daPagare.Key, daPagare.Value);
            }
            daPagareList = selected.daPagare;
            CreatePagamentiComboBox(ref promptEffettuarePagamCombo, effettuarePagamenti);
        }

        private static List<Beneficio> DeserializeJsonResource(string resourcePath)
        {
            // Get the current assembly
            Assembly assembly = Assembly.GetExecutingAssembly();

            // Read the resource stream for the embedded JSON file
            using (Stream stream = assembly.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Could not find embedded resource");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    string jsonString = reader.ReadToEnd();
                    return JsonSerializer.Deserialize<List<Beneficio>>(jsonString);
                }
            }
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

            string sql = "SELECT * FROM Tipologie_pagam_test";
            SqlCommand cmd = new SqlCommand(sql, conn);

            using (SqlDataReader reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["categoria_pagamento"].ToString() == "00")
                    {
                        continue;
                    }
                    string codBeneficio = reader["cod_beneficio"].ToString();
                    Beneficio beneficio = returnList.Find(b => b.codBeneficio == codBeneficio);
                    if (beneficio == null)
                    {
                        beneficio = new Beneficio
                        {
                            nomeBeneficio = reader["descr_interno_tipo_beneficio"].ToString(),
                            codBeneficio = codBeneficio,
                            tipologiePagamenti = new List<TipologiaPagamento>()
                        };
                        returnList.Add(beneficio);
                    }

                    string nomeTipologiaPagam = reader["descr_interno_tipo_pagamento"].ToString();
                    TipologiaPagamento tipologiaPagamento = beneficio.tipologiePagamenti.Find(t => t.nomeTipologiaPagamento == nomeTipologiaPagam);
                    if (tipologiaPagamento == null)
                    {
                        tipologiaPagamento = new TipologiaPagamento
                        {
                            nomeTipologiaPagamento = nomeTipologiaPagam,
                            categoria = new List<CategoriaPagamento>()
                        };
                        beneficio.tipologiePagamenti.Add(tipologiaPagamento);
                    }

                    string nomCategoria = reader["descr_interno_cat_pagamento"].ToString();
                    CategoriaPagamento categoriaPagamento = tipologiaPagamento.categoria.Find(c => c.nomeCategoria == nomCategoria);

                    if (categoriaPagamento == null)
                    {
                        categoriaPagamento = new CategoriaPagamento
                        {
                            nomeCategoria = nomCategoria,
                            cod_categoria = reader["categoria_pagamento"].ToString(),
                            daPagare = new List<DaPagareItem>()
                        };
                        tipologiaPagamento.categoria.Add(categoriaPagamento);
                    }

                    DaPagareItem daPagareItem = new DaPagareItem
                    {
                        Key = reader["cod_interno_pagam"].ToString(),
                        Value = reader["descr_interno_tipo_emissione"].ToString()
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
    }

    public class TipologiaPagamento
    {
        public string nomeTipologiaPagamento { get; set; }
        public List<CategoriaPagamento> categoria { get; set; }
    }

    public class CategoriaPagamento
    {
        public string nomeCategoria { get; set; }
        public List<DaPagareItem> daPagare { get; set; }
        public string cod_categoria { get; set; }
    }

    public class DaPagareItem
    {
        public string Key { get; set; }
        public string Value { get; set; }
    }

}
