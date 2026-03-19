using System;
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

    }
}