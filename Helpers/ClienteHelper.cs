using System;
using System.Collections.Generic;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Contiene m�todos auxiliares para operaciones comunes con clientes
    /// </summary>
    public static class ClienteHelper
    {
        /// <summary>
        /// Calcula la edad basada en la fecha de nacimiento
        /// </summary>
        public static int? CalcularEdad(DateTime? fechaNacimiento)
        {
            if (!fechaNacimiento.HasValue)
                return null;

            var hoy = DateTime.Today;
            var edad = hoy.Year - fechaNacimiento.Value.Year;

            // Restar 1 si el cumplea�os a�n no ha ocurrido este a�o
            if (fechaNacimiento.Value.Date > hoy.AddYears(-edad))
                edad--;

            return edad;
        }

        /// <summary>
        /// Clasifica el nivel de riesgo crediticio (escala 1-5, <see cref="NivelRiesgoCredito"/>)
        /// en 3 franjas de lectura rápida (bajo/medio/alto). Única fuente de verdad para esa
        /// clasificación: la usan tanto el filtro de riesgo del listado como el chip visual de
        /// cada fila, para que nunca puedan mostrar información contradictoria entre sí.
        /// </summary>
        public static string ClasificarRiesgo(NivelRiesgoCredito nivel) => nivel switch
        {
            NivelRiesgoCredito.Rechazado or NivelRiesgoCredito.RechazadoRevisar => "alto",
            NivelRiesgoCredito.AprobadoCondicional or NivelRiesgoCredito.AprobadoLimitado => "medio",
            NivelRiesgoCredito.AprobadoTotal => "bajo",
            _ => "bajo"
        };

        private static readonly Dictionary<string, NivelRiesgoCredito[]> BucketsRiesgo = new(StringComparer.OrdinalIgnoreCase)
        {
            ["bajo"] = new[] { NivelRiesgoCredito.AprobadoTotal },
            ["medio"] = new[] { NivelRiesgoCredito.AprobadoCondicional, NivelRiesgoCredito.AprobadoLimitado },
            ["alto"] = new[] { NivelRiesgoCredito.Rechazado, NivelRiesgoCredito.RechazadoRevisar }
        };

        /// <summary>
        /// Devuelve los niveles de <see cref="NivelRiesgoCredito"/> que integran una franja de
        /// riesgo ("bajo"/"medio"/"alto"). Vacío si la franja no es reconocida (no filtra nada).
        /// </summary>
        public static IReadOnlyCollection<NivelRiesgoCredito> NivelesRiesgoDeBucket(string? bucket)
            => !string.IsNullOrWhiteSpace(bucket) && BucketsRiesgo.TryGetValue(bucket.Trim(), out var niveles)
                ? niveles
                : Array.Empty<NivelRiesgoCredito>();
    }
}