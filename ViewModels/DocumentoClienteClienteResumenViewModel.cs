using TheBuryProject.Models.Enums;

namespace TheBuryProject.ViewModels
{
    public class DocumentoClienteClienteResumenViewModel
    {
        public int ClienteId { get; set; }
        public string ClienteNombre { get; set; } = string.Empty;
        public string? DocumentoIdentidad { get; set; }
        public string? Telefono { get; set; }
        public int TotalDocumentos { get; set; }
        public int Pendientes { get; set; }
        public int Verificados { get; set; }
        public int Rechazados { get; set; }
        public int Vencidos { get; set; }
        public DateTime? UltimaActualizacion { get; set; }
        public List<DocumentoClienteViewModel> Documentos { get; set; } = new();

        public string EstadoResumen => (Vencidos, Rechazados, Pendientes, Verificados) switch
        {
            var (v, _, _, _) when v > 0 => "Con vencidos",
            var (_, r, _, _) when r > 0 => "Con rechazados",
            var (_, _, p, _) when p > 0 => "Pendiente",
            var (_, _, _, ve) when ve > 0 => "Completo",
            _ => "Sin estado"
        };

        public string EstadoResumenClasses => EstadoResumen switch
        {
            "Con vencidos"   => "border border-orange-500/30 bg-orange-500/20 text-orange-400",
            "Con rechazados" => "border border-red-500/30 bg-red-500/20 text-red-400",
            "Pendiente"      => "border border-amber-500/30 bg-amber-500/20 text-amber-400",
            "Completo"       => "border border-emerald-500/30 bg-emerald-500/20 text-emerald-400",
            _                => "border border-slate-600/30 bg-slate-600/20 text-slate-400"
        };

        public static List<DocumentoClienteClienteResumenViewModel> FromDocumentos(
            IEnumerable<DocumentoClienteViewModel> documentos)
        {
            return documentos
                .GroupBy(d => d.ClienteId)
                .Select(g =>
                {
                    var first = g.First();
                    return new DocumentoClienteClienteResumenViewModel
                    {
                        ClienteId        = g.Key,
                        ClienteNombre    = first.ClienteNombre ?? $"Cliente #{g.Key}",
                        DocumentoIdentidad = first.Cliente.NumeroDocumento,
                        Telefono         = first.Cliente.Telefono,
                        TotalDocumentos  = g.Count(),
                        Pendientes       = g.Count(d => d.Estado == EstadoDocumento.Pendiente),
                        Verificados      = g.Count(d => d.Estado == EstadoDocumento.Verificado),
                        Rechazados       = g.Count(d => d.Estado == EstadoDocumento.Rechazado),
                        Vencidos         = g.Count(d => d.Estado == EstadoDocumento.Vencido),
                        UltimaActualizacion = g.Max(d => (DateTime?)d.FechaSubida),
                        Documentos       = g.ToList()
                    };
                })
                .OrderByDescending(c => c.Vencidos > 0 || c.Pendientes > 0)
                .ThenBy(c => c.ClienteNombre)
                .ToList();
        }
    }
}
