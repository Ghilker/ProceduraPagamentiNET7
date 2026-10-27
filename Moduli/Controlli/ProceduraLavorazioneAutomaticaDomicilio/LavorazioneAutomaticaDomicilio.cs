using System.Data.SqlClient;

namespace ProcedureNet7
{
    internal sealed class LavorazioneAutomaticaDomicilio : BaseProcedure<ArgsLavorazioneAutomaticaDomicilio>
    {
        public string? OutputPath { get; private set; }

        public LavorazioneAutomaticaDomicilio(MasterForm? masterForm, SqlConnection? connection) : base(masterForm, connection) { }

        public override void RunProcedure(ArgsLavorazioneAutomaticaDomicilio args)
        {
            ArgumentNullException.ThrowIfNull(args);
            using var analyzer = new ControlloDomicilio(_masterForm, CONNECTION);
            var requests = analyzer.Analyze(new ArgsControlloDomicilio
            {
                _selectedAA = args.AnnoAccademico, _folderPath = args.CartellaCsv
            }, forAutomation: true);
            Logger.LogInfo(80, args.Simulazione ? "Simulazione: nessuna istanza verrà modificata." : "Avvio accoglimento delle sole istanze idonee.");
            var results = DomicilioAutomaticEngine.Run(requests, args.Simulazione,
                args.Simulazione ? null : new DomicilioAutomaticProcessExecutor(CONNECTION!,
                    ReferenceEquals(_masterForm?.MainConnection, CONNECTION)
                        ? _masterForm?.MainConnectionString : CONNECTION?.ConnectionString));
            OutputPath = DomicilioAutomaticReport.Export(requests, results, args.Simulazione, args.CartellaCsv, args.AnnoAccademico.Trim());
            Logger.LogInfo(100, $"{(args.Simulazione ? "Simulazione" : "Lavorazione")} completata. CSV: {OutputPath}. " +
                string.Join("; ", results.GroupBy(x => x.Outcome).Select(x => $"{x.Key}: {x.Count()}")));
        }
    }
}
