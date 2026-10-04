using System.Globalization;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Formatter central de los documentos (importes, fechas y mayúsculas al estilo argentino). Es la única
    /// implementación: contexto, plantillas y PDF la comparten, así un importe nunca se escribe distinto en el
    /// pagaré, el contrato y el recibo.
    /// </summary>
    public static class FormatoArgentino
    {
        public static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-AR");

        private static readonly string[] Meses =
        {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        /// <summary>188358,00 — coma decimal, sin separador de miles (como los comprobantes de referencia). Sin símbolo: la plantilla pone el "$".</summary>
        public static string Importe(decimal valor)
            => Math.Round(valor, 2, MidpointRounding.AwayFromZero).ToString("0.00", Cultura);

        /// <summary>02/10/2026.</summary>
        public static string Fecha(DateTime fecha) => fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        public static string MesTexto(int mes) => Meses[Math.Clamp(mes, 1, 12) - 1];

        /// <summary>2 de Septiembre del 2026.</summary>
        public static string FechaTexto(DateTime fecha) => $"{fecha.Day} de {MesTexto(fecha.Month)} del {fecha.Year}";

        /// <summary>Mayúsculas para presentación; no modifica el dato guardado.</summary>
        public static string Mayusculas(string? texto) => (texto ?? string.Empty).ToUpper(Cultura);

        public static string Minusculas(string? texto) => (texto ?? string.Empty).ToLower(Cultura);

        /// <summary>00012 — código visible de cliente.</summary>
        public static string Codigo(int id) => id.ToString("D5", CultureInfo.InvariantCulture);

        /// <summary>01, 02… — número de cuota con dos dígitos.</summary>
        public static string NumeroDosDigitos(int numero) => numero.ToString("D2", CultureInfo.InvariantCulture);
    }
}
