using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services;

/// <summary>
/// Reglas puras para resolver condiciones de pago por producto sin DB ni servicios productivos.
/// </summary>
public static class ProductoCondicionPagoRules
{
    public static TipoPago NormalizarTipoPago(TipoPago tipoPago, TipoTarjeta? tipoTarjetaLegacy = null)
    {
#pragma warning disable CS0618
        return tipoPago switch
        {
            TipoPago.CreditoPersonall => TipoPago.CreditoPersonal,
            TipoPago.Tarjeta when tipoTarjetaLegacy == TipoTarjeta.Credito => TipoPago.TarjetaCredito,
            TipoPago.Tarjeta when tipoTarjetaLegacy == TipoTarjeta.Debito => TipoPago.TarjetaDebito,
            _ => tipoPago
        };
#pragma warning restore CS0618
    }

    public static int? NormalizarMaxCuotasSinInteresLegacy(int? maxCuotasSinInteresPermitidas)
    {
        return maxCuotasSinInteresPermitidas.HasValue
            ? Math.Max(1, maxCuotasSinInteresPermitidas.Value)
            : null;
    }

    public static IReadOnlyList<ProductoCondicionPagoValidacionDto> ValidarCondiciones(
        IEnumerable<ProductoCondicionPagoDto> condiciones)
    {
        ArgumentNullException.ThrowIfNull(condiciones);

        var validaciones = new List<ProductoCondicionPagoValidacionDto>();

        foreach (var condicion in condiciones)
        {
            if (!condicion.Activo)
            {
                continue;
            }

            if (condicion.TipoPago == TipoPago.Tarjeta)
            {
                validaciones.Add(CrearValidacion(
                    condicion,
                    null,
                    CodigoValidacionCondicionPago.TipoPagoTarjetaLegacyAmbiguo,
                    SeveridadValidacionCondicionPago.Advertencia,
                    "TipoPago.Tarjeta es legacy y ambiguo; requiere TipoTarjeta para resolverse sin asumir credito."));
            }

            AgregarValidacionCuotaSiCorresponde(validaciones, condicion, null, condicion.MaxCuotasSinInteres);
            AgregarValidacionCuotaSiCorresponde(validaciones, condicion, null, condicion.MaxCuotasConInteres);
            AgregarValidacionCuotaSiCorresponde(validaciones, condicion, null, condicion.MaxCuotasCredito);

            var tarjetasActivas = condicion.Tarjetas.Where(t => t.Activo).ToArray();
            var generales = tarjetasActivas.Where(t => t.ConfiguracionTarjetaId is null).ToArray();
            if (generales.Length > 1)
            {
                validaciones.Add(CrearValidacion(
                    condicion,
                    null,
                    CodigoValidacionCondicionPago.ReglaTarjetaGeneralDuplicada,
                    SeveridadValidacionCondicionPago.Error,
                    "Hay mas de una regla general de tarjeta para el mismo producto y medio."));
            }

            foreach (var grupo in tarjetasActivas
                         .Where(t => t.ConfiguracionTarjetaId.HasValue)
                         .GroupBy(t => t.ConfiguracionTarjetaId!.Value)
                         .Where(g => g.Count() > 1))
            {
                validaciones.Add(CrearValidacion(
                    condicion,
                    grupo.Key,
                    CodigoValidacionCondicionPago.ReglaTarjetaEspecificaDuplicada,
                    SeveridadValidacionCondicionPago.Error,
                    "Hay mas de una regla especifica para la misma tarjeta."));
            }

            foreach (var tarjeta in tarjetasActivas)
            {
                AgregarValidacionCuotaSiCorresponde(validaciones, condicion, tarjeta.ConfiguracionTarjetaId, tarjeta.MaxCuotasSinInteres);
                AgregarValidacionCuotaSiCorresponde(validaciones, condicion, tarjeta.ConfiguracionTarjetaId, tarjeta.MaxCuotasConInteres);
            }
        }

        return validaciones;
    }

    public static CondicionesPagoCarritoResultado ResolverCondicionesCarrito(
        IEnumerable<ProductoCondicionPagoDto> condiciones,
        TipoPago tipoPago,
        int? configuracionTarjetaId = null,
        decimal? totalReferencia = null,
        int? maxCuotasSinInteresGlobal = null,
        int? maxCuotasConInteresGlobal = null,
        int? maxCuotasCreditoGlobal = null,
        TipoTarjeta? tipoTarjetaLegacy = null)
    {
        ArgumentNullException.ThrowIfNull(condiciones);

        var tipoNormalizado = NormalizarTipoPago(tipoPago, tipoTarjetaLegacy);
        var condicionesDelMedio = condiciones
            .Where(c => c.Activo && NormalizarTipoPago(c.TipoPago, tipoTarjetaLegacy) == tipoNormalizado)
            .ToArray();

        var bloqueos = new List<CondicionPagoBloqueoDetalleDto>();
        var restriccionesCandidatas = new List<CondicionPagoRestriccionCuotasDto>();
        var ajustes = new List<CondicionPagoAjusteInformativoDto>();

        foreach (var condicion in condicionesDelMedio)
        {
            var tarjetaGeneral = ObtenerReglaTarjetaGeneral(condicion);
            var tarjetaEspecifica = ObtenerReglaTarjetaEspecifica(condicion, configuracionTarjetaId);
            var bloqueo = ResolverBloqueo(condicion, tipoNormalizado, configuracionTarjetaId, tarjetaGeneral, tarjetaEspecifica);

            if (bloqueo is not null)
            {
                bloqueos.Add(bloqueo);
                continue;
            }

            AgregarRestriccionSiExiste(
                restriccionesCandidatas,
                condicion.ProductoId,
                tipoNormalizado,
                configuracionTarjetaId,
                TipoRestriccionCuotas.MaxCuotasSinInteres,
                ResolverEntero(
                    condicion.MaxCuotasSinInteres,
                    FuenteCondicionPagoEfectiva.Producto,
                    tarjetaGeneral?.MaxCuotasSinInteres,
                    FuenteCondicionPagoEfectiva.TarjetaGeneral,
                    tarjetaEspecifica?.MaxCuotasSinInteres,
                    FuenteCondicionPagoEfectiva.TarjetaEspecifica));

            AgregarRestriccionSiExiste(
                restriccionesCandidatas,
                condicion.ProductoId,
                tipoNormalizado,
                configuracionTarjetaId,
                TipoRestriccionCuotas.MaxCuotasConInteres,
                ResolverEntero(
                    condicion.MaxCuotasConInteres,
                    FuenteCondicionPagoEfectiva.Producto,
                    tarjetaGeneral?.MaxCuotasConInteres,
                    FuenteCondicionPagoEfectiva.TarjetaGeneral,
                    tarjetaEspecifica?.MaxCuotasConInteres,
                    FuenteCondicionPagoEfectiva.TarjetaEspecifica));

            AgregarRestriccionSiExiste(
                restriccionesCandidatas,
                condicion.ProductoId,
                tipoNormalizado,
                configuracionTarjetaId,
                TipoRestriccionCuotas.MaxCuotasCredito,
                condicion.MaxCuotasCredito.HasValue
                    ? (condicion.MaxCuotasCredito.Value, FuenteCondicionPagoEfectiva.Producto)
                    : null);

            var ajuste = ResolverAjusteInformativo(condicion, tarjetaGeneral, tarjetaEspecifica);
            if (ajuste is not null)
            {
                ajustes.Add(ajuste);
            }
        }

        var maxSinInteres = ResolverMinimo(maxCuotasSinInteresGlobal, restriccionesCandidatas, TipoRestriccionCuotas.MaxCuotasSinInteres);
        var maxConInteres = ResolverMinimo(maxCuotasConInteresGlobal, restriccionesCandidatas, TipoRestriccionCuotas.MaxCuotasConInteres);
        var maxCredito = ResolverMinimo(maxCuotasCreditoGlobal, restriccionesCandidatas, TipoRestriccionCuotas.MaxCuotasCredito);
        var restriccionesEfectivas = restriccionesCandidatas
            .Where(r =>
                (r.TipoRestriccion == TipoRestriccionCuotas.MaxCuotasSinInteres && maxSinInteres.HasValue && r.Valor == maxSinInteres.Value) ||
                (r.TipoRestriccion == TipoRestriccionCuotas.MaxCuotasConInteres && maxConInteres.HasValue && r.Valor == maxConInteres.Value) ||
                (r.TipoRestriccion == TipoRestriccionCuotas.MaxCuotasCredito && maxCredito.HasValue && r.Valor == maxCredito.Value))
            .OrderBy(r => r.ProductoId)
            .ThenBy(r => r.TipoRestriccion)
            .ToArray();

        var productoIdsBloqueantes = bloqueos
            .Select(b => b.ProductoId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var productoIdsRestrictivos = restriccionesEfectivas
            .Select(r => r.ProductoId)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        return new CondicionesPagoCarritoResultado
        {
            TipoPago = tipoNormalizado,
            ConfiguracionTarjetaId = configuracionTarjetaId,
            Permitido = bloqueos.Count == 0,
            FuentePermitido = bloqueos.Count == 0
                ? FuenteCondicionPagoEfectiva.Global
                : bloqueos[0].Fuente,
            FuenteRestriccion = restriccionesEfectivas.Length == 0
                ? FuenteCondicionPagoEfectiva.Global
                : restriccionesEfectivas[0].Fuente,
            AlcanceBloqueo = bloqueos.Count == 0
                ? AlcanceBloqueoPago.Ninguno
                : bloqueos[0].Alcance,
            MaxCuotasSinInteres = maxSinInteres,
            MaxCuotasConInteres = maxConInteres,
            MaxCuotasCredito = maxCredito,
            TieneRestriccionesPorProducto = bloqueos.Count > 0 || restriccionesEfectivas.Length > 0,
            ProductoIdsBloqueantes = productoIdsBloqueantes,
            ProductoIdsRestrictivos = productoIdsRestrictivos,
            Bloqueos = bloqueos,
            Restricciones = restriccionesEfectivas,
            AjustesInformativos = ajustes,
            TotalReferencia = totalReferencia,
            TotalSinAplicarAjustes = totalReferencia
        };
    }

    private static ProductoCondicionPagoValidacionDto CrearValidacion(
        ProductoCondicionPagoDto condicion,
        int? configuracionTarjetaId,
        CodigoValidacionCondicionPago codigo,
        SeveridadValidacionCondicionPago severidad,
        string motivo)
    {
        return new ProductoCondicionPagoValidacionDto
        {
            ProductoId = condicion.ProductoId,
            TipoPago = condicion.TipoPago,
            ConfiguracionTarjetaId = configuracionTarjetaId,
            Codigo = codigo,
            Severidad = severidad,
            Motivo = motivo
        };
    }

    private static void AgregarValidacionCuotaSiCorresponde(
        List<ProductoCondicionPagoValidacionDto> validaciones,
        ProductoCondicionPagoDto condicion,
        int? configuracionTarjetaId,
        int? valor)
    {
        if (valor is < 1)
        {
            validaciones.Add(CrearValidacion(
                condicion,
                configuracionTarjetaId,
                CodigoValidacionCondicionPago.CuotasMenoresAUno,
                SeveridadValidacionCondicionPago.Error,
                "Las cuotas de reglas nuevas deben ser mayores o iguales a 1."));
        }
    }

    private static ProductoCondicionPagoTarjetaDto? ObtenerReglaTarjetaGeneral(ProductoCondicionPagoDto condicion)
    {
        return condicion.Tarjetas.FirstOrDefault(t => t.Activo && t.ConfiguracionTarjetaId is null);
    }

    private static ProductoCondicionPagoTarjetaDto? ObtenerReglaTarjetaEspecifica(
        ProductoCondicionPagoDto condicion,
        int? configuracionTarjetaId)
    {
        return configuracionTarjetaId.HasValue
            ? condicion.Tarjetas.FirstOrDefault(t => t.Activo && t.ConfiguracionTarjetaId == configuracionTarjetaId.Value)
            : null;
    }

    private static bool EsPagoConTarjeta(TipoPago tipoPago)
    {
        return tipoPago is TipoPago.TarjetaCredito or TipoPago.TarjetaDebito;
    }

    private static CondicionPagoBloqueoDetalleDto? ResolverBloqueo(
        ProductoCondicionPagoDto condicion,
        TipoPago tipoPago,
        int? configuracionTarjetaId,
        ProductoCondicionPagoTarjetaDto? tarjetaGeneral,
        ProductoCondicionPagoTarjetaDto? tarjetaEspecifica)
    {
        if (condicion.Permitido == false)
        {
            return CrearBloqueo(
                condicion.ProductoId,
                tipoPago,
                configuracionTarjetaId,
                AlcanceBloqueoPago.Medio,
                FuenteCondicionPagoEfectiva.Producto,
                "El producto bloquea el medio de pago para el carrito.");
        }

        if (EsPagoConTarjeta(tipoPago) && tarjetaGeneral?.Permitido == false)
        {
            return CrearBloqueo(
                condicion.ProductoId,
                tipoPago,
                configuracionTarjetaId,
                AlcanceBloqueoPago.Medio,
                FuenteCondicionPagoEfectiva.TarjetaGeneral,
                "La regla general de tarjeta del producto bloquea el medio para el carrito.");
        }

        if (EsPagoConTarjeta(tipoPago) && tarjetaEspecifica?.Permitido == false)
        {
            return CrearBloqueo(
                condicion.ProductoId,
                tipoPago,
                configuracionTarjetaId,
                AlcanceBloqueoPago.TarjetaEspecifica,
                FuenteCondicionPagoEfectiva.TarjetaEspecifica,
                "La regla especifica del producto bloquea la tarjeta seleccionada.");
        }

        return null;
    }

    private static CondicionPagoBloqueoDetalleDto CrearBloqueo(
        int productoId,
        TipoPago tipoPago,
        int? configuracionTarjetaId,
        AlcanceBloqueoPago alcance,
        FuenteCondicionPagoEfectiva fuente,
        string motivo)
    {
        return new CondicionPagoBloqueoDetalleDto
        {
            ProductoId = productoId,
            TipoPago = tipoPago,
            ConfiguracionTarjetaId = configuracionTarjetaId,
            Alcance = alcance,
            Fuente = fuente,
            Motivo = motivo
        };
    }

    private static (int Valor, FuenteCondicionPagoEfectiva Fuente)? ResolverEntero(
        int? condicionGeneral,
        FuenteCondicionPagoEfectiva fuenteCondicionGeneral,
        int? tarjetaGeneral,
        FuenteCondicionPagoEfectiva fuenteTarjetaGeneral,
        int? tarjetaEspecifica,
        FuenteCondicionPagoEfectiva fuenteTarjetaEspecifica)
    {
        if (tarjetaEspecifica.HasValue)
        {
            return (tarjetaEspecifica.Value, fuenteTarjetaEspecifica);
        }

        if (tarjetaGeneral.HasValue)
        {
            return (tarjetaGeneral.Value, fuenteTarjetaGeneral);
        }

        return condicionGeneral.HasValue
            ? (condicionGeneral.Value, fuenteCondicionGeneral)
            : null;
    }

    private static void AgregarRestriccionSiExiste(
        List<CondicionPagoRestriccionCuotasDto> destino,
        int productoId,
        TipoPago tipoPago,
        int? configuracionTarjetaId,
        TipoRestriccionCuotas tipoRestriccion,
        (int Valor, FuenteCondicionPagoEfectiva Fuente)? restriccion)
    {
        if (restriccion is { Valor: >= 1 })
        {
            destino.Add(new CondicionPagoRestriccionCuotasDto
            {
                ProductoId = productoId,
                TipoPago = tipoPago,
                ConfiguracionTarjetaId = configuracionTarjetaId,
                TipoRestriccion = tipoRestriccion,
                Valor = restriccion.Value.Valor,
                Fuente = restriccion.Value.Fuente
            });
        }
    }

    private static int? ResolverMinimo(
        int? global,
        IEnumerable<CondicionPagoRestriccionCuotasDto> restricciones,
        TipoRestriccionCuotas tipoRestriccion)
    {
        var valores = restricciones
            .Where(r => r.TipoRestriccion == tipoRestriccion)
            .Select(r => r.Valor)
            .ToList();
        if (global is >= 1)
        {
            valores.Add(global.Value);
        }

        return valores.Count == 0 ? null : valores.Min();
    }

    private static CondicionPagoAjusteInformativoDto? ResolverAjusteInformativo(
        ProductoCondicionPagoDto condicion,
        ProductoCondicionPagoTarjetaDto? tarjetaGeneral,
        ProductoCondicionPagoTarjetaDto? tarjetaEspecifica)
    {
        var porcentajeRecargo = tarjetaEspecifica?.PorcentajeRecargo
            ?? tarjetaGeneral?.PorcentajeRecargo
            ?? condicion.PorcentajeRecargo;
        var porcentajeDescuento = tarjetaEspecifica?.PorcentajeDescuentoMaximo
            ?? tarjetaGeneral?.PorcentajeDescuentoMaximo
            ?? condicion.PorcentajeDescuentoMaximo;

        if (!porcentajeRecargo.HasValue && !porcentajeDescuento.HasValue)
        {
            return null;
        }

        var fuente = tarjetaEspecifica is not null
            ? FuenteCondicionPagoEfectiva.TarjetaEspecifica
            : tarjetaGeneral is not null
                ? FuenteCondicionPagoEfectiva.TarjetaGeneral
                : FuenteCondicionPagoEfectiva.Producto;

        return new CondicionPagoAjusteInformativoDto
        {
            ProductoId = condicion.ProductoId,
            Fuente = fuente,
            PorcentajeRecargo = porcentajeRecargo,
            PorcentajeDescuentoMaximo = porcentajeDescuento
        };
    }
}
