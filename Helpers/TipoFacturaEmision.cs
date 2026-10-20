using Microsoft.AspNetCore.Mvc.Rendering;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Helpers
{
    /// <summary>
    /// Fuente única de los tipos de comprobante que puede elegir el operador al EMITIR una factura nueva
    /// de venta: A, B o C. <see cref="TipoFactura.NotaCredito"/> y <see cref="TipoFactura.NotaDebito"/> siguen
    /// existiendo en el enum (comprobantes históricos, numeración, lectura) pero no son una factura nueva.
    /// Ojo: <c>Enum.IsDefined</c> no alcanza, acepta NC/ND; por eso la lista es explícita.
    /// No es un motor de reglas tributarias: no decide qué tipo corresponde a cada cliente.
    /// </summary>
    public static class TipoFacturaEmision
    {
        public static readonly IReadOnlyList<TipoFactura> Permitidos =
            new[] { TipoFactura.A, TipoFactura.B, TipoFactura.C };

        public const string MensajeInvalido =
            "El tipo de comprobante no es válido para emitir una factura nueva. Elegí A, B o C.";

        public static bool EsPermitido(TipoFactura tipo) => Permitidos.Contains(tipo);

        /// <summary>Lanza <see cref="InvalidOperationException"/> (el tipo que ya capturan los llamadores) si el tipo no es A/B/C.</summary>
        public static void Validar(TipoFactura tipo)
        {
            if (!EsPermitido(tipo))
                throw new InvalidOperationException(MensajeInvalido);
        }

        public static bool EsPermitidoTexto(string? valor) =>
            valor is "A" or "B" or "C";

        /// <summary>Opciones del selector de emisión (solo A/B/C) con <paramref name="seleccionado"/> marcado.</summary>
        public static SelectList CrearSelectList(TipoFactura seleccionado = TipoFactura.B)
        {
            var items = Permitidos.Select(t => new { Value = t.ToString(), Text = t.ToString() });
            var marcado = EsPermitido(seleccionado) ? seleccionado : TipoFactura.B;
            return new SelectList(items, "Value", "Text", marcado.ToString());
        }
    }
}
