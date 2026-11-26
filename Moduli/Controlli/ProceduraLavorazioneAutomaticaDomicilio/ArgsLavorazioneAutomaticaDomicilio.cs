using System.ComponentModel.DataAnnotations;

namespace ProcedureNet7
{
    internal sealed class ArgsLavorazioneAutomaticaDomicilio
    {
        [Required]
        public string AnnoAccademico = string.Empty;
        [Required]
        public string CartellaCsv = string.Empty;
        public bool Simulazione = true;
        public bool UsaOcr = true;
        public string? UnlimitedOcrUrl;
    }
}
