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

        /// <summary>Resultado de <see cref="Discriminar"/>: partes del ajuste (con signo), truncado a pesos y neto/IVA totales.</summary>
        public readonly record struct Discriminacion(IReadOnlyList<ParteAlicuota> Partes, decimal Truncado, decimal Neto, decimal Iva);

        /// <summary>
        /// Neto e IVA de una venta cuyo <paramref name="total"/> cobrado difiere de la suma de sus ítems por el ajuste del
        /// medio de pago: recargo o interés (el total es mayor) o descuento (el total es menor), con IVA a la alícuota de los
        /// ítems, prorrateado por alícuota. Misma fórmula para el recargo (criterio H5-I) y para el descuento (reduce la
        /// base imponible, igual que un descuento de línea). El truncado a pesos (total entero, ítems con centavos) se
        /// absorbe primero del IVA y luego del neto. Garantía: <c>Neto + Iva == total</c> cuando los ítems son coherentes.
        /// Las partes devueltas llevan el signo del ajuste (negativas si es un descuento).
        /// </summary>
        public static Discriminacion Discriminar(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas)
        {
            var bruto = lineas.Sum(l => l.Bruto);
            var netoLineas = lineas.Sum(l => l.Neto);
            var ivaLineas = lineas.Sum(l => l.Iva);

            // Sin total cobrado (comprobante o venta legacy sin importe persistido) no hay ajuste que medir.
            if (total <= 0m)
                return new Discriminacion(Array.Empty<ParteAlicuota>(), 0m, netoLineas, ivaLineas);

            // Un total con centavos no fue truncado a pesos (ventas nacidas de cotización): el ajuste es la diferencia exacta.
            var truncado = total == Math.Floor(total) ? bruto - Math.Floor(bruto) : 0m;
            var ajuste = total - (bruto - truncado);
            if (Math.Abs(ajuste) < 0.005m)
            {
                var (n0, i0) = AbsorberTruncado(netoLineas, ivaLineas, truncado);
                var residuo0 = total - (n0 + i0);
                if (residuo0 != 0m && Math.Abs(residuo0) <= 0.05m * Math.Max(1, lineas.Count))
                    i0 += residuo0;
                return new Discriminacion(Array.Empty<ParteAlicuota>(), truncado, n0, i0);
            }

            var signo = ajuste > 0m ? 1m : -1m;
            var partes = Repartir(lineas.Select(l => (l.Porcentaje, l.Bruto)), Math.Abs(ajuste))
                .Select(p => new ParteAlicuota(p.Porcentaje, signo * p.Bruto, signo * p.Neto, signo * p.Iva))
                .ToList();
            var (neto, iva) = AbsorberTruncado(netoLineas + partes.Sum(p => p.Neto), ivaLineas + partes.Sum(p => p.Iva), truncado);
            // Si el snapshot de alguna línea difiere del bruto en centavos (neto + IVA != total de la línea), el centavo se
            // absorbe en el IVA para que neto + IVA == total cobrado.
            var residuo = total - (neto + iva);
            if (residuo != 0m && Math.Abs(residuo) <= 0.05m * Math.Max(1, lineas.Count))
                iva += residuo;
            return new Discriminacion(partes, truncado, neto, iva);
        }

        /// <summary>
        /// Resumen por alícuota (base imponible, IVA y total) de lo cobrado: las líneas agrupadas por alícuota más el ajuste del
        /// medio de pago prorrateado (<see cref="Discriminar"/>), con el truncado a pesos absorbido en la alícuota de mayor
        /// IVA. Es el mismo desglose que imprime el comprobante de factura; la suma de los totales es siempre <paramref name="total"/>
        /// (cuando los ítems son coherentes) y la de neto + IVA también.
        /// </summary>
        public static List<ParteAlicuota> ResumenPorAlicuota(decimal total, IReadOnlyList<(decimal Porcentaje, decimal Bruto, decimal Neto, decimal Iva)> lineas)
        {
            var d = Discriminar(total, lineas);
            var buckets = lineas
                .GroupBy(l => l.Porcentaje)
                .ToDictionary(g => g.Key, g => new ParteAlicuota(g.Key, g.Sum(l => l.Bruto), g.Sum(l => l.Neto), g.Sum(l => l.Iva)));
            foreach (var parte in d.Partes)
            {
                buckets[parte.Porcentaje] = buckets.TryGetValue(parte.Porcentaje, out var actual)
                    ? new ParteAlicuota(parte.Porcentaje, actual.Bruto + parte.Bruto, actual.Neto + parte.Neto, actual.Iva + parte.Iva)
                    : parte;
            }

            if (d.Truncado > 0m && buckets.Count > 0)
            {
                var destino = buckets.Values.OrderByDescending(b => b.Iva).First();
                var (neto, iva) = AbsorberTruncado(destino.Neto, destino.Iva, d.Truncado);
                buckets[destino.Porcentaje] = new ParteAlicuota(destino.Porcentaje, destino.Bruto - ((destino.Neto - neto) + (destino.Iva - iva)), neto, iva);
            }

            return buckets.Values.OrderByDescending(b => b.Porcentaje).ToList();
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
