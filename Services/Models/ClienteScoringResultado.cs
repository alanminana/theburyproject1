using System;

namespace TheBuryProject.Services.Models
{
    /// <summary>
    /// Snapshot de los factores de comportamiento de un cliente, calculado a partir
    /// de sus ventas y créditos. Se persiste en las columnas homónimas de Cliente.
    /// </summary>
    public class ClienteScoringSnapshot
    {
        public int AntiguedadDias { get; set; }

        public DateTime? UltimaVentaFecha { get; set; }

        public int CreditosEnTermino { get; set; }

        public int CreditosConAtraso { get; set; }

        /// <summary>True si el cliente no registra créditos con atraso.</summary>
        public bool PagaEnTermino => CreditosConAtraso == 0;
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
