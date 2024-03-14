using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class RiepilogoPagamenti : Form
    {
        public RiepilogoPagamenti(RiepilogoArguments riepilogoArguments)
        {
            InitializeComponent();


            riepilogoTipoProcedura.Text = riepilogoArguments.tipoProcedura;
            riepilogoCartellaSalvataggio.Text = riepilogoArguments.cartellaSalvataggio;
            riepilogoAnnoAccademico.Text = riepilogoArguments.annoAccademico;
            riepilogoDataRiferimento.Text = riepilogoArguments.dataRiferimento;
            riepilogoNumeroMandato.Text = riepilogoArguments.numMandato;
            riepilogoMandatoDaSostituire.Text = riepilogoArguments.numMandatoOld;
            riepilogoTipoBeneficio.Text = riepilogoArguments.tipoBeneficio;
            riepilogoPagamentoSelezionato.Text = riepilogoArguments.pagamentoSelezionato;
            riepilogoEnteDiGestione.Text = riepilogoArguments.enteDiGestione;
            riepilogoTipoStudente.Text = riepilogoArguments.tipoStudente;
            riepilogoNomeDiTabella.Text = riepilogoArguments.nomeTabella;
            riepilogoNumImpegno.Text = riepilogoArguments.numImpegno;
        }

        private void promptRiepilogoOkBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
        }

        private void promptRiepilogoNokBtn_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }
    }

    public class RiepilogoArguments
    {
        public string tipoProcedura { get; set; }
        public string cartellaSalvataggio { get; set; }
        public string annoAccademico { get; set; }
        public string numMandato { get; set; }
        public string numMandatoOld { get; set; }
        public string tipoBeneficio { get; set; }
        public string pagamentoSelezionato { get; set; }
        public string enteDiGestione { get; set; }
        public string tipoStudente { get; set; }
        public string nomeTabella { get; set; }
        public string dataRiferimento { get; set; }
        public string numImpegno { get; set; }

        // Static variable that holds a single instance of the class
        private static RiepilogoArguments instance = null;

        // Private constructor to prevent instance creation outside of this class
        private RiepilogoArguments() { }

        // Public static method to get the instance of the class
        public static RiepilogoArguments Instance
        {
            get
            {
                // If the instance hasn't been created yet, create it
                if (instance == null)
                {
                    instance = new RiepilogoArguments();
                }
                // Return the single instance of the class
                return instance;
            }
        }
    }
}
