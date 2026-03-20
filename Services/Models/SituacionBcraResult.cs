namespace TheBuryProject.Services.Models
{
    public class SituacionBcraResult
    {
        public int? SituacionCrediticiaBcra { get; init; }
        public string? SituacionCrediticiaDescripcion { get; init; }
        public string? SituacionCrediticiaPeriodo { get; init; }
        public DateTime? SituacionCrediticiaUltimaConsultaUtc { get; init; }
        public bool? SituacionCrediticiaConsultaOk { get; init; }
    }
}
