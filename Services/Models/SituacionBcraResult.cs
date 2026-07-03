namespace TheBuryProject.Services.Models
{
    public class SituacionBcraResult
    {
        public int? SituacionCrediticiaBcra { get; init; }
        public string? SituacionCrediticiaDescripcion { get; init; }
        public string? SituacionCrediticiaPeriodo { get; init; }
        public DateTime? SituacionCrediticiaUltimaConsultaUtc { get; init; }
        public bool? SituacionCrediticiaConsultaOk { get; init; }

        // Última consulta BCRA exitosa (FASE 11C), separada del último intento.
        public int? SituacionCrediticiaBcraUltimoExito { get; init; }
        public string? SituacionCrediticiaDescripcionUltimoExito { get; init; }
        public string? SituacionCrediticiaPeriodoUltimoExito { get; init; }
        public DateTime? SituacionCrediticiaUltimoExitoUtc { get; init; }
    }
}
