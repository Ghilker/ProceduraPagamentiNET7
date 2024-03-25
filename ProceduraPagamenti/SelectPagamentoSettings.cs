using DocumentFormat.OpenXml.Drawing;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class SelectPagamentoSettings : Form
    {
        string selectedAA;
        string selectedBeneficio;
        string catPagamento;

        List<string> codEnteList = new List<string>();
        List<string> tipoStudentiList = new List<string>();
        List<string> impegniList = new List<string>();
        public SelectPagamentoSettings(SqlConnection conn, string selectedAA, string selectedBeneficio, string catPagamento)
        {
            this.selectedAA = selectedAA;
            this.selectedBeneficio = selectedBeneficio;
            this.catPagamento = catPagamento;
            InitializeComponent();
            Dictionary<string, string> tipoStudenteData = new Dictionary<string, string>()
            {
                { "2", "Tutti gli studenti" },
                { "0", "Matricole" },
                { "1", "Anni successivi" }
            };
            codEnteList = GenerateCodEnteComboBox(ref promptCodEnteCombo, conn);
            tipoStudentiList = CreatePagamentiComboBox(ref promptTipoStudenteCombo, tipoStudenteData);
            impegniList = GenerateImpegnoComboBox(ref promptImpegnoCombo, conn);
        }

        List<string> GenerateCodEnteComboBox(ref ComboBox comboBox, SqlConnection conn)
        {
            SqlCommand readData = new($"SELECT * FROM Enti_di_gestione WHERE cod_ente <> '00'", conn);

            Dictionary<string, string> codEntiDict = new Dictionary<string, string>
            {
                { "00", "Tutte le sedi" }
            };

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    codEntiDict.Add(reader["cod_ente"].ToString(), reader["descrizione"].ToString());
                }
            }
            return CreatePagamentiComboBox(ref comboBox, codEntiDict);
        }

        List<string> GenerateImpegnoComboBox(ref ComboBox comboBox, SqlConnection conn)
        {
            SqlCommand readData = new($"SELECT * FROM impegni WHERE Cod_beneficio = '{selectedBeneficio}' and anno_accademico = '{selectedAA}' and categoria_pagamento = '{catPagamento}'", conn);

            Dictionary<string, string> impegni = new Dictionary<string, string>
            {
                { "0000", "Tutti gli impegni" }
            };

            using (SqlDataReader reader = readData.ExecuteReader())
            {
                while (reader.Read())
                {
                    impegni.Add(reader["num_impegno"].ToString(), reader["descr"].ToString());
                }
            }
            return CreatePagamentiComboBox(ref comboBox, impegni);
        }

        public static List<string> CreatePagamentiComboBox(ref ComboBox comboBox, Dictionary<string, string> toInsert)
        {
            List<string> returnList = new List<string>();
            comboBox.Items.Clear();
            foreach (KeyValuePair<string, string> item in toInsert)
            {
                comboBox.Items.Add(new { Text = $"{item.Key} - {item.Value}", Value = item.Key });
                returnList.Add(item.Key);
            }
            comboBox.DisplayMember = "Text";
            comboBox.ValueMember = "Value";
            returnList.RemoveAt(0);
            return returnList;
        }

        public InputReturn InputTipoStud
        {
            get
            {
                dynamic pagamentiSelectedTipoPagamentoObj = promptTipoStudenteCombo.SelectedItem;
                string pagamentiSelectedTipoPagamento = pagamentiSelectedTipoPagamentoObj?.Value ?? "";

                dynamic pagamentiSelectedTipoPagamentoObjRiepilogo = promptTipoStudenteCombo.SelectedItem;
                string pagamentiSelectedTipoPagamentRiepilogo = pagamentiSelectedTipoPagamentoObjRiepilogo?.Text ?? "";
                RiepilogoArguments.Instance.tipoStudente = pagamentiSelectedTipoPagamentRiepilogo;

                return new InputReturn(pagamentiSelectedTipoPagamento, tipoStudentiList);
            }
        }

        public InputReturn InputImpegno
        {
            get
            {
                dynamic pagamentiSelectedImpegnoObj = promptImpegnoCombo.SelectedItem;
                string pagamentiSelectedImpegno = pagamentiSelectedImpegnoObj?.Value ?? "";

                dynamic pagamentiSelectedImpegnoObjRiepilogo = promptImpegnoCombo.SelectedItem;
                string pagamentiSelectedImpegnoRiepilogo = pagamentiSelectedImpegnoObjRiepilogo?.Text ?? "";
                RiepilogoArguments.Instance.numImpegno = pagamentiSelectedImpegnoRiepilogo;

                return new InputReturn(pagamentiSelectedImpegno, impegniList);
            }
        }

        public InputReturn InputCodEnte
        {
            get
            {
                dynamic pagamentiSelectedCodEnteObj = promptCodEnteCombo.SelectedItem;
                string pagamentiSelectedCodEnte = pagamentiSelectedCodEnteObj?.Value ?? "";

                dynamic pagamentiSelectedCodEnteObjRiepilogo = promptCodEnteCombo.SelectedItem;
                string pagamentiSelectedCodEnteRiepilogo = pagamentiSelectedCodEnteObjRiepilogo?.Text ?? "";
                RiepilogoArguments.Instance.enteDiGestione = pagamentiSelectedCodEnteRiepilogo;

                return new InputReturn(pagamentiSelectedCodEnte, codEnteList);
            }
        }

        private void promptTipoStudenteOkBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
        }

        private void promptTipoStudenteNokBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }
    }

    public class InputReturn
    {
        public string inputVar;
        public List<string> inputList;

        public InputReturn(string inputVar, List<string> inputList)
        {
            this.inputVar = inputVar;
            this.inputList = inputList;
        }
    }
}
