namespace TheBuryProject.Helpers
{
    /// <summary>
    /// H5-I: discriminación del RECARGO del medio de pago (débito con recargo, tarjeta en cuotas con interés, recargo de
    /// Efectivo/Transferencia) en neto + IVA, y su reparto exacto.
    ///
    /// Criterio fiscal adoptado (el más conservador y consistente con lo que ya hace el cálculo de IVA de la venta): el
    /// recargo es un importe FINAL (IVA incluido) que se suma al total que paga el cliente y sigue la alícuota de los
    /// ítems sobre los que se calcula. Si la venta tiene ítems con distintas alícuotas, el recargo se prorratea entre
    /// alícuotas proporcionalmente al bruto (con IVA) de cada una; cada parte se separa en neto e IVA con la misma
    /// fórmula que el resto de la venta (neto = bruto / (1 + alícuota), redondeo comercial a 2 decimales; IVA = bruto - neto).
    /// DECISIÓN PENDIENTE (normativa): el tratamiento legal del interés de financiación (exento / no gravado / gravado) no
    /// se infiere del código; aquí se grava a la alícuota de los ítems.
    ///
    /// Reparto: método del mayor resto en centavos; la suma de las partes es SIEMPRE exactamente el importe a repartir.
    /// </summary>
    public static class RecargoMedioPagoIva
    {
        public readonly record struct ParteAlicuota(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva);

        /// <summary>
        /// Recargo (en pesos enteros) que contiene el total cobrado: total - truncado a pesos de lo que suman los ítems.
        /// El total ya viene truncado a pesos (VentaService.TruncarAPesos), así que la diferencia es un entero.
        /// 0 si no hay recargo (o si hay un descuento: eso no se discrimina aquí).
        /// </summary>
        public static decimal CalcularRecargo(decimal totalCobrado, decimal brutoItems) =>
            Math.Max(0m, totalCobrado - Math.Floor(brutoItems));

        /// <summary>Reparte <paramref name="importe"/> en centavos proporcionalmente a <paramref name="pesos"/> (mayor resto).</summary>
        public static decimal[] Prorratear(IReadOnlyList<decimal> pesos, decimal importe)
        {
            var resultado = new decimal[pesos.Count];
            var total = pesos.Sum(p => Math.Max(0m, p));
            if (importe <= 0m || pesos.Count == 0 || total <= 0m)
                return resultado;

            var centavos = (long)Math.Round(importe * 100m, MidpointRounding.AwayFromZero);
            var baseCent = new long[pesos.Count];
            var resto = new decimal[pesos.Count];
            long asignado = 0;
            for (var i = 0; i < pesos.Count; i++)
            {
                var exacto = centavos * Math.Max(0m, pesos[i]) / total;
                baseCent[i] = (long)Math.Floor(exacto);
                resto[i] = exacto - baseCent[i];
                asignado += baseCent[i];
            }

            var faltan = centavos - asignado;
            foreach (var i in Enumerable.Range(0, pesos.Count)
                         .OrderByDescending(i => resto[i])
                         .ThenByDescending(i => pesos[i])
                         .ThenBy(i => i)
                         .Take((int)faltan))
            {
                baseCent[i]++;
            }

            for (var i = 0; i < pesos.Count; i++)
                resultado[i] = baseCent[i] / 100m;
            return resultado;
        }

        /// <summary>
        /// Reparte el recargo (importe final con IVA) entre alícuotas según el bruto de cada una y lo separa en neto + IVA.
        /// Garantía: Σ Bruto == recargo y, para cada parte, Neto + Iva == Bruto.
        /// </summary>
        public static List<ParteAlicuota> Repartir(IEnumerable<(decimal Porcentaje, decimal Bruto)> bases, decimal recargo)
        {
            var grupos = bases
                .Where(b => b.Bruto > 0m)
                .GroupBy(b => b.Porcentaje)
                .Select(g => (Porcentaje: g.Key, Bruto: g.Sum(x => x.Bruto)))
                .OrderByDescending(g => g.Porcentaje)
                .ToList();

            var brutos = Prorratear(grupos.Select(g => g.Bruto).ToList(), recargo);
            var partes = new List<ParteAlicuota>();
            for (var i = 0; i < grupos.Count; i++)
            {
                if (brutos[i] <= 0m) continue;
                var porcentaje = grupos[i].Porcentaje;
                var neto = porcentaje > 0m
                    ? Math.Round(brutos[i] / (1m + porcentaje / 100m), 2, MidpointRounding.AwayFromZero)
                    : brutos[i];
                partes.Add(new ParteAlicuota(porcentaje, brutos[i], neto, brutos[i] - neto));
            }

            return partes;
        }

        /// <summary>
        /// Descuenta el truncado a pesos (fracción de centavos que el total cobrado no incluye) primero del IVA y luego
        /// del neto, como hace VentaService.CalcularTotales, para que Neto + IVA == Total exacto.
        /// </summary>
        public static (decimal Neto, decimal Iva) AbsorberTruncado(decimal neto, decimal iva, decimal truncado)
        {
            if (truncado <= 0m) return (neto, iva);
            var deIva = Math.Min(truncado, iva);
            return (neto - (truncado - deIva), iva - deIva);
        }
    }
}
