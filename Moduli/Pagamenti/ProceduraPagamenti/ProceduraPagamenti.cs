using DocumentFormat.OpenXml;
using ProcedureNet7.PagamentiProcessor;
using ProcedureNet7.Storni;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ProcedureNet7
{
    public partial class ProceduraPagamenti : BaseProcedure<ArgsPagamenti>
    {
        string debugStudente = "";
        string selectedSaveFolder = string.Empty;
        string selectedAA = string.Empty;

        string selectedCodEnte = string.Empty;
        string selectedDataRiferimento = string.Empty;
        string selectedNumeroMandato = string.Empty;
        string selectedVecchioMandato = string.Empty;
        string selectedTipoProcedura = string.Empty;
        string selectedTipoPagamento = string.Empty;
        string selectedRichiestoPA = string.Empty;
        string dbTableName = string.Empty;
        bool dbTableExists;
        string tipoStudente = string.Empty;
        string tipoBeneficio = string.Empty;
        string codTipoPagamento = string.Empty;
        string selectedImpegno = string.Empty;
        string categoriaPagam = string.Empty;
        bool isIntegrazione = false;
        bool isRiemissione = false;

        bool usingFiltroManuale = false;
        bool cicloTuttiIPagamenti = false;

        double importoTotale = 0;

        bool massivoDefault = false;
        string massivoString = string.Empty;

        bool studenteForzato = false;
        string studenteForzatoCF = string.Empty;

        Dictionary<string, StudentePagamenti> studentiDaPagare = new();
        readonly Dictionary<StudentePagamenti, List<string>> studentiConErroriPA = new();

        List<string> impegniList = new();

        Dictionary<string, string> dictQueryWhere = new();
        string stringQueryWhere = string.Empty;
        bool usingStringWhere = false;

        bool isTR = false;
        bool insertInDatabase = false;

        SqlTransaction? sqlTransaction = null;
        bool exitProcedureEarly = false;

        public int studentiProcessatiAmount = 0;
        public Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, int>>>> impegnoAmount = new();

        private readonly Dictionary<string, PaymentCount> _conteggiPerPagamento = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flussoWrittenCF = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> _flussoFilesByCF = new(StringComparer.OrdinalIgnoreCase);
        private readonly DeterminaAccumulator _detAccDisco = new();
        private readonly DeterminaAccumulator _detAccPnrr = new();

        IAcademicYearProcessor? selectedAcademicProcessor;

        public ProceduraPagamenti(MasterForm masterForm, SqlConnection mainConnection) : base(masterForm, mainConnection) { }
    }
}
