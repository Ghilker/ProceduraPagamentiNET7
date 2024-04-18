using ProcedureNet7;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ProceduraPagamentiNET7.ProceduraPagamenti
{
    public partial class FiltroManuale : Form
    {
        private TipoFiltro selectedFiltro;
        private string queryWhereText;
        private SqlConnection conn;
        SqlTransaction sqlTransaction = null;
        private string tableName;

        private ContextMenuStrip filtroSessoStrip;
        private ContextMenuStrip filtroDisabileStrip;
        private ContextMenuStrip filtroStatusSedeStrip;
        private ContextMenuStrip filtroCittadinanzaStrip;
        private ContextMenuStrip filtroEnteStrip;
        private ContextMenuStrip filtroEsitoPAStrip;
        private ContextMenuStrip filtroAnnoCorsoStrip;
        private ContextMenuStrip filtroTipoCorsoStrip;
        private ContextMenuStrip filtroSedeStudiStrip;
        private ContextMenuStrip filtroLoretoStrip;

        private Dictionary<TipoFiltro, string> _TipoFiltro = new Dictionary<TipoFiltro, string>()
        {
            { TipoFiltro.filtroGuidato, "Filtro Guidato" },
            { TipoFiltro.filtroQuery, "Filtro con query" }
        };

        private Dictionary<string, string> filtroSessoCombo = new Dictionary<string, string>()
        {
            { "M" , "Maschio" },
            { "F", "Femmina" },
        };

        private Dictionary<string, string> filtroDisabileCombo = new Dictionary<string, string>()
        {
            { "0" , "No" },
            { "1", "Si" },
        };

        private Dictionary<string, string> filtroEsitoPACombo = new Dictionary<string, string>()
        {
            { "NULL", "Non richiesto" },
            { "0", "Escluso" },
            { "1", "Idoneo" },
            { "2", "Vincitore" }
        };

        private Dictionary<string, string> filtroStatusSedeCombo = new Dictionary<string, string>()
        {
            { "A", "In sede" },
            { "B", "Fuori sede" },
            { "C", "Pendolare" },
            { "D", "Pendolare calcolato" }
        };

        public Dictionary<string, string> DictWhereItems
        {
            get
            {
                Dictionary<string, string> returnDict = new Dictionary<string, string>()
                {
                    { "Sesso", Utilities.GetCheckBoxSelectedCodes(filtroSessoStrip.Items) },
                    { "StatusSede", Utilities.GetCheckBoxSelectedCodes(filtroStatusSedeStrip.Items) },
                    { "Cittadinanza", Utilities.GetCheckBoxSelectedCodes(filtroCittadinanzaStrip.Items) },
                    { "CodEnte", Utilities.GetCheckBoxSelectedCodes(filtroEnteStrip.Items) },
                    { "EsitoPA", Utilities.GetCheckBoxSelectedCodes(filtroEsitoPAStrip.Items) },
                    { "AnnoCorso", Utilities.GetCheckBoxSelectedCodes(filtroAnnoCorsoStrip.Items) },
                    { "Disabile", Utilities.GetCheckBoxSelectedCodes(filtroDisabileStrip.Items) },
                    { "TipoCorso", Utilities.GetCheckBoxSelectedCodes(filtroTipoCorsoStrip.Items) },
                    { "SedeStudi", Utilities.GetCheckBoxSelectedCodes(filtroSedeStudiStrip.Items) },
                    { "TogliereLoreto", Utilities.GetCheckBoxSelectedCodes(filtroLoretoStrip.Items) }
                };

                return returnDict;
            }
        }

        public string StringQueryWhere
        {
            get
            {
                return queryWhereText;
            }
        }

        public TipoFiltro TipoFiltro
        {
            get
            {
                return selectedFiltro;
            }
        }

        public FiltroManuale(SqlConnection conn, SqlTransaction sqlTransaction, string tableName)
        {
            this.conn = conn;
            this.tableName = tableName;
            this.sqlTransaction = sqlTransaction;
            InitializeComponent();
            CreateComboFiltro(ref selectTipoFiltroCombo, _TipoFiltro);
            filtroQueryPanel.Visible = false;
            filtroGuidatoPanel.Visible = false;

            Utilities.CreateDropDownMenu(ref filtroPABtn, ref filtroEsitoPAStrip, filtroEsitoPACombo);
            CreateCittadinanzaStrip();
            Utilities.CreateDropDownMenu(ref filtroStatusSedeBTN, ref filtroStatusSedeStrip, filtroStatusSedeCombo);
            Utilities.CreateDropDownMenu(ref filtroSessoBtn, ref filtroSessoStrip, filtroSessoCombo);
            Utilities.CreateDropDownMenu(ref filtroDisabileBtn, ref filtroDisabileStrip, filtroDisabileCombo);
            CreateCodEnteStrip();
            CreateAnnoCorsoStrip();
            CreateCodCorsoStrip();
            CreateSedeStudiStrip();
            CreateTogliereLoretoStrip();
        }

        public static void CreateComboFiltro(ref ComboBox comboBox, Dictionary<TipoFiltro, string> toInsert)
        {
            comboBox.Items.Clear();
            foreach (KeyValuePair<TipoFiltro, string> item in toInsert)
            {
                comboBox.Items.Add(new { Text = $"{item.Value}", Value = item.Key });
            }
            comboBox.DisplayMember = "Text";
            comboBox.ValueMember = "Value";
        }

        private void CreateStrip(string query, ref Button button, ref ContextMenuStrip strip, bool cleanStrip = false)
        {
            var comboData = new Dictionary<string, string>();
            using (SqlCommand command = new SqlCommand(query, conn, sqlTransaction))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string key = reader[0].ToString();
                        string value = reader[1].ToString();
                        comboData[key] = value;
                    }
                }
            }
            Utilities.CreateDropDownMenu(ref button, ref strip, comboData, clean: cleanStrip);
        }

        private void CreateCittadinanzaStrip()
        {
            string query = $@"
                SELECT pagam.cod_cittadinanza AS codEnte, decod.Descrizione AS descEnte
                FROM {tableName} AS pagam
                INNER JOIN Decod_cittadinanza AS decod ON pagam.cod_cittadinanza = decod.Cod_cittadinanza
                GROUP BY pagam.cod_cittadinanza, decod.Descrizione
                ORDER BY decod.Descrizione";
            CreateStrip(query, ref filtroCittadinanzaBtn, ref filtroCittadinanzaStrip);
        }

        private void CreateCodEnteStrip()
        {
            string query = $@"
                SELECT pagam.cod_ente AS codEnte, Enti_di_gestione.Descrizione AS descEnte
                FROM {tableName} AS pagam
                INNER JOIN Enti_di_gestione ON pagam.cod_ente = Enti_di_gestione.Cod_ente
                GROUP BY pagam.cod_ente, Enti_di_gestione.Descrizione
                ORDER BY Enti_di_gestione.Descrizione";
            CreateStrip(query, ref filtroEnteBtn, ref filtroEnteStrip);
        }

        private void CreateAnnoCorsoStrip()
        {
            string query = $@"
                SELECT pagam.anno_corso, pagam.anno_corso
                FROM {tableName} AS pagam
                GROUP BY pagam.anno_corso
                ORDER BY pagam.anno_corso";
            CreateStrip(query, ref filtroAnnoCorsoBtn, ref filtroAnnoCorsoStrip, true);
        }

        private void CreateCodCorsoStrip()
        {
            string query = $@"
                SELECT pagam.cod_corso, Tipologie_studi.Descrizione
                FROM {tableName} AS pagam INNER JOIN
                    Tipologie_studi ON pagam.cod_corso = Tipologie_studi.Cod_tipologia_studi
                GROUP BY pagam.cod_corso, Tipologie_studi.Descrizione
                ORDER BY pagam.cod_corso";
            CreateStrip(query, ref filtroTipoCorsoBtn, ref filtroTipoCorsoStrip);
        }

        private void CreateSedeStudiStrip()
        {
            string query = $@"
                SELECT pagam.sede_studi, Sede_studi.Descrizione
                FROM {tableName} AS pagam INNER JOIN
	                Sede_studi ON pagam.sede_studi = Sede_studi.Cod_sede_studi
                GROUP BY pagam.sede_studi, Sede_studi.Descrizione
                ORDER BY Sede_studi.Descrizione";
            CreateStrip(query, ref filtroSedeStudiBtn, ref filtroSedeStudiStrip);
        }

        private void CreateTogliereLoretoStrip()
        {
            string query = $@"
                SELECT pagam.togliere_loreto, pagam.togliere_loreto
                FROM {tableName} AS pagam 
                GROUP BY pagam.togliere_loreto
                ORDER BY pagam.togliere_loreto";
            CreateStrip(query, ref filtroLoretoBtn, ref filtroLoretoStrip, true);
        }


        private void selectTipoFiltroCombo_SelectedIndexChanged(object sender, EventArgs e)
        {
            dynamic selectedItem = selectTipoFiltroCombo.SelectedItem;
            selectedFiltro = selectedItem?.Value ?? "";

            switch (selectedFiltro)
            {
                case TipoFiltro.filtroQuery:
                    filtroQueryPanel.Visible = true;
                    filtroGuidatoPanel.Visible = false;
                    break;
                case TipoFiltro.filtroGuidato:
                    filtroGuidatoPanel.Visible = true;
                    filtroQueryPanel.Visible = false;
                    break;
            }
        }

        private void queryTextBox_TextChanged(object sender, EventArgs e)
        {
            queryWhereText = queryTextBox.Text;
        }

        private void confermaForm_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
        }

        private void annullaForm_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }
    }

    public enum TipoFiltro
    {
        filtroGuidato,
        filtroQuery
    }
}
