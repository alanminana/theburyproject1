using System;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Snapshot de los factores de comportamiento de un cliente, calculado a partir
    /// de sus ventas y creditos. Se persiste en las columnas homonimas de Cliente.
    /// </summary>
    public class ClienteScoringSnapshot
    {
        public int AntiguedadDias { get; set; }

        public DateTime? UltimaVentaFecha { get; set; }

        public int CantidadComprasCliente { get; set; }

        public int CreditosEnTermino { get; set; }

        public int CreditosConAtraso { get; set; }

        /// <summary>True si existe historial de repago evaluable.</summary>
        public bool TieneHistorialCredito => CreditosEnTermino > 0 || CreditosConAtraso > 0;

        /// <summary>True si tiene historial y no registra creditos con atraso.</summary>
        public bool PagaEnTermino => TieneHistorialCredito && CreditosConAtraso == 0;
    }

    /// <summary>
    /// Resultado de recalcular el scoring de un cliente: snapshot + puntaje final.
    /// </summary>
    public class ClienteScoringResultado
    {
        public int ClienteId { get; set; }

        public int Puntaje { get; set; }

        public ClienteScoringSnapshot Snapshot { get; set; } = new();
    }
}
