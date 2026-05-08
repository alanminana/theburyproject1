using TheBuryProject.Models.Enums;
using TheBuryProject.Services;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Tests.Unit;

public class ProductoCondicionPagoRulesTests
{
    [Fact]
    public void ResolverCondicionesCarrito_NullUsaGlobalYNoRestringe()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 10,
                TipoPago = TipoPago.Efectivo,
                Permitido = null,
                MaxCuotasCredito = null
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.Efectivo,
            totalReferencia: 1000m);

        Assert.True(resultado.Permitido);
        Assert.False(resultado.TieneRestriccionesPorProducto);
        Assert.Empty(resultado.ProductoIdsBloqueantes);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
        Assert.Equal(1000m, resultado.TotalSinAplicarAjustes);
    }

    [Fact]
    public void ResolverCondicionesCarrito_PermitidoFalseBloqueaMedioParaCarrito()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.Transferencia, Permitido = true },
            new ProductoCondicionPagoDto { ProductoId = 2, TipoPago = TipoPago.Transferencia, Permitido = false }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(condiciones, TipoPago.Transferencia);

        Assert.False(resultado.Permitido);
        Assert.Equal(AlcanceBloqueoPago.Medio, resultado.AlcanceBloqueo);
        Assert.Equal(new[] { 2 }, resultado.ProductoIdsBloqueantes);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_BloqueoEspecificoDeTarjetaNoBloqueaOtrasTarjetas()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 3,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = 101, Permitido = false }
                }
            }
        };

        var bloqueada = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            configuracionTarjetaId: 101);
        var otraTarjeta = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            configuracionTarjetaId: 202);

        Assert.False(bloqueada.Permitido);
        Assert.Equal(AlcanceBloqueoPago.TarjetaEspecifica, bloqueada.AlcanceBloqueo);
        Assert.True(otraTarjeta.Permitido);
        Assert.Empty(otraTarjeta.ProductoIdsBloqueantes);
    }

    [Fact]
    public void ResolverCondicionesCarrito_BloqueoGeneralDeTarjetaCreditoBloqueaTodasLasTarjetasCredito()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 4,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = null, Permitido = false }
                }
            }
        };

        var visa = ProductoCondicionPagoRules.ResolverCondicionesCarrito(condiciones, TipoPago.TarjetaCredito, 1);
        var master = ProductoCondicionPagoRules.ResolverCondicionesCarrito(condiciones, TipoPago.TarjetaCredito, 2);

        Assert.False(visa.Permitido);
        Assert.False(master.Permitido);
        Assert.Equal(AlcanceBloqueoPago.Medio, visa.AlcanceBloqueo);
        Assert.Equal(AlcanceBloqueoPago.Medio, master.AlcanceBloqueo);
    }

    [Fact]
    public void ResolverCondicionesCarrito_ReglaEspecificaGanaSobreGeneralSoloCuandoNoReabreBloqueoSuperior()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 5,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = null,
                        Permitido = false,
                        MaxCuotasSinInteres = 6
                    },
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = 10,
                        Permitido = true,
                        MaxCuotasSinInteres = 12
                    }
                }
            },
            new ProductoCondicionPagoDto
            {
                ProductoId = 6,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = null,
                        Permitido = true,
                        MaxCuotasConInteres = 12
                    },
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = 10,
                        Permitido = false,
                        MaxCuotasConInteres = 3
                    }
                }
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            configuracionTarjetaId: 10);

        Assert.False(resultado.Permitido);
        Assert.Equal(new[] { 5, 6 }, resultado.ProductoIdsBloqueantes);
        Assert.Null(resultado.MaxCuotasSinInteres);
        Assert.Null(resultado.MaxCuotasConInteres);
        Assert.Empty(resultado.Restricciones);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasSinInteresUsaMinimoRestrictivo()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.TarjetaCredito, MaxCuotasSinInteres = 12 },
            new ProductoCondicionPagoDto { ProductoId = 2, TipoPago = TipoPago.TarjetaCredito, MaxCuotasSinInteres = 6 },
            new ProductoCondicionPagoDto { ProductoId = 3, TipoPago = TipoPago.TarjetaCredito, MaxCuotasSinInteres = null }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            maxCuotasSinInteresGlobal: 18);

        Assert.Equal(6, resultado.MaxCuotasSinInteres);
        Assert.Equal(new[] { 2 }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasSinInteresNullHeredarConservaGlobalActual()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                Permitido = null,
                MaxCuotasSinInteres = null
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            maxCuotasSinInteresGlobal: 12);

        Assert.True(resultado.Permitido);
        Assert.Equal(12, resultado.MaxCuotasSinInteres);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuentePermitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuenteRestriccion);
        Assert.Empty(resultado.Restricciones);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasConInteresUsaMinimoRestrictivo()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.TarjetaCredito, MaxCuotasConInteres = 24 },
            new ProductoCondicionPagoDto { ProductoId = 2, TipoPago = TipoPago.TarjetaCredito, MaxCuotasConInteres = 9 }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            maxCuotasConInteresGlobal: 12);

        Assert.Equal(9, resultado.MaxCuotasConInteres);
        Assert.Equal(new[] { 2 }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasConInteresMayorAlGlobalNoAmpliaRango()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.TarjetaCredito, MaxCuotasConInteres = 24 }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            maxCuotasConInteresGlobal: 12);

        Assert.Equal(12, resultado.MaxCuotasConInteres);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasCreditoUsaMinimoRestrictivo()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.CreditoPersonal, MaxCuotasCredito = 18 },
            new ProductoCondicionPagoDto { ProductoId = 2, TipoPago = TipoPago.CreditoPersonal, MaxCuotasCredito = 12 }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.CreditoPersonal,
            maxCuotasCreditoGlobal: 24);

        Assert.Equal(12, resultado.MaxCuotasCredito);
        Assert.Equal(new[] { 2 }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_CondicionInactivaNoParticipaYConservaGlobal()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.CreditoPersonal,
                Permitido = false,
                MaxCuotasCredito = 3,
                Activo = false
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.CreditoPersonal,
            maxCuotasCreditoGlobal: 24);

        Assert.True(resultado.Permitido);
        Assert.Equal(24, resultado.MaxCuotasCredito);
        Assert.False(resultado.TieneRestriccionesPorProducto);
        Assert.Empty(resultado.Bloqueos);
        Assert.Empty(resultado.Restricciones);
    }

    [Fact]
    public void ResolverCondicionesCarrito_ReglaTarjetaInactivaNoParticipaYUsaReglaGeneral()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = null,
                        MaxCuotasSinInteres = 9
                    },
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = 10,
                        Permitido = false,
                        MaxCuotasSinInteres = 3,
                        Activo = false
                    }
                }
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            configuracionTarjetaId: 10,
            maxCuotasSinInteresGlobal: 12);

        Assert.True(resultado.Permitido);
        Assert.Equal(9, resultado.MaxCuotasSinInteres);
        Assert.Empty(resultado.Bloqueos);
        var restriccion = Assert.Single(resultado.Restricciones);
        Assert.Equal(FuenteCondicionPagoEfectiva.TarjetaGeneral, restriccion.Fuente);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasCreditoMayorAlGlobalNoAmpliaRango()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.CreditoPersonal, MaxCuotasCredito = 36 }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.CreditoPersonal,
            maxCuotasCreditoGlobal: 24);

        Assert.Equal(24, resultado.MaxCuotasCredito);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_MaxCuotasCreditoManualQuedaRestringidoPorProducto()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.CreditoPersonal, MaxCuotasCredito = 18 }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.CreditoPersonal,
            maxCuotasCreditoGlobal: 120);

        Assert.Equal(18, resultado.MaxCuotasCredito);
        Assert.Equal(new[] { 1 }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_CreditoPersonalSinCondiciones_ConservaGlobal()
    {
        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            Array.Empty<ProductoCondicionPagoDto>(),
            TipoPago.CreditoPersonal,
            totalReferencia: 2500m,
            maxCuotasCreditoGlobal: 24);

        Assert.True(resultado.Permitido);
        Assert.False(resultado.TieneRestriccionesPorProducto);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuentePermitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuenteRestriccion);
        Assert.Equal(24, resultado.MaxCuotasCredito);
        Assert.Equal(2500m, resultado.TotalReferencia);
        Assert.Equal(2500m, resultado.TotalSinAplicarAjustes);
        Assert.Empty(resultado.Bloqueos);
        Assert.Empty(resultado.Restricciones);
    }

    [Fact]
    public void ResolverCondicionesCarrito_ProductoIdsBloqueantesYRestrictivosQuedanSeparados()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.CreditoPersonal,
                Permitido = false,
                MaxCuotasCredito = 6
            },
            new ProductoCondicionPagoDto
            {
                ProductoId = 2,
                TipoPago = TipoPago.CreditoPersonal,
                MaxCuotasCredito = 3
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(condiciones, TipoPago.CreditoPersonal);

        Assert.Equal(new[] { 1 }, resultado.ProductoIdsBloqueantes);
        Assert.Equal(new[] { 2 }, resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_RecargoYDescuentoSonInformativosYNoModificanTotales()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 8,
                TipoPago = TipoPago.TarjetaDebito,
                PorcentajeRecargo = 10m,
                PorcentajeDescuentoMaximo = 5m
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaDebito,
            totalReferencia: 2000m);

        var ajuste = Assert.Single(resultado.AjustesInformativos);
        Assert.Equal(10m, ajuste.PorcentajeRecargo);
        Assert.Equal(5m, ajuste.PorcentajeDescuentoMaximo);
        Assert.Equal(2000m, resultado.TotalReferencia);
        Assert.Equal(2000m, resultado.TotalSinAplicarAjustes);
    }

    [Fact]
    public void ResolverCondicionesCarrito_CreditoPersonalAjustesInformativosNoModificanTotal()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 18,
                TipoPago = TipoPago.CreditoPersonal,
                PorcentajeRecargo = 12m,
                PorcentajeDescuentoMaximo = 4m
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.CreditoPersonal,
            totalReferencia: 3000m,
            maxCuotasCreditoGlobal: 24);

        Assert.True(resultado.Permitido);
        var ajuste = Assert.Single(resultado.AjustesInformativos);
        Assert.Equal(12m, ajuste.PorcentajeRecargo);
        Assert.Equal(4m, ajuste.PorcentajeDescuentoMaximo);
        Assert.Equal(24, resultado.MaxCuotasCredito);
        Assert.Equal(3000m, resultado.TotalReferencia);
        Assert.Equal(3000m, resultado.TotalSinAplicarAjustes);
    }

    [Fact]
    public void NormalizarTipoPago_CreditoPersonallLegacyQuedaNormalizadoExplicitamente()
    {
#pragma warning disable CS0618
        var resultado = ProductoCondicionPagoRules.NormalizarTipoPago(TipoPago.CreditoPersonall);
#pragma warning restore CS0618

        Assert.Equal(TipoPago.CreditoPersonal, resultado);
    }

    [Fact]
    public void ValidarCondiciones_ReglaGeneralDeTarjetaDuplicadaDevuelveError()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = null },
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = null }
                }
            }
        };

        var validacion = Assert.Single(ProductoCondicionPagoRules.ValidarCondiciones(condiciones));

        Assert.Equal(CodigoValidacionCondicionPago.ReglaTarjetaGeneralDuplicada, validacion.Codigo);
        Assert.Equal(SeveridadValidacionCondicionPago.Error, validacion.Severidad);
    }

    [Fact]
    public void ValidarCondiciones_ReglaEspecificaDuplicadaPorMismaTarjetaDevuelveError()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = 7 },
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = 7 }
                }
            }
        };

        var validacion = Assert.Single(ProductoCondicionPagoRules.ValidarCondiciones(condiciones));

        Assert.Equal(CodigoValidacionCondicionPago.ReglaTarjetaEspecificaDuplicada, validacion.Codigo);
        Assert.Equal(7, validacion.ConfiguracionTarjetaId);
    }

    [Fact]
    public void ValidarCondiciones_CuotasCeroEnReglasNuevasSonInvalidas()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.CreditoPersonal,
                MaxCuotasCredito = 0
            }
        };

        var validacion = Assert.Single(ProductoCondicionPagoRules.ValidarCondiciones(condiciones));

        Assert.Equal(CodigoValidacionCondicionPago.CuotasMenoresAUno, validacion.Codigo);
        Assert.Equal(SeveridadValidacionCondicionPago.Error, validacion.Severidad);
    }

    [Fact]
    public void NormalizarMaxCuotasSinInteresLegacy_CuotasCeroMantieneClampSoloEnFlujoLegacy()
    {
        var legacy = ProductoCondicionPagoRules.NormalizarMaxCuotasSinInteresLegacy(0);
        var nuevaRegla = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                MaxCuotasSinInteres = 0
            }
        };

        var validacion = Assert.Single(ProductoCondicionPagoRules.ValidarCondiciones(nuevaRegla));

        Assert.Equal(1, legacy);
        Assert.Equal(CodigoValidacionCondicionPago.CuotasMenoresAUno, validacion.Codigo);
    }

    [Fact]
    public void ResolverCondicionesCarrito_ProductoBloqueadoNoDefineMaximoEfectivoVisible()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.CreditoPersonal,
                Permitido = false,
                MaxCuotasCredito = 2
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(condiciones, TipoPago.CreditoPersonal);

        Assert.False(resultado.Permitido);
        Assert.Null(resultado.MaxCuotasCredito);
        Assert.Empty(resultado.Restricciones);
        Assert.Empty(resultado.ProductoIdsRestrictivos);
    }

    [Fact]
    public void ResolverCondicionesCarrito_BloqueosMultiplesConservanDetallePorProductoYFuente()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                Permitido = false
            },
            new ProductoCondicionPagoDto
            {
                ProductoId = 2,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto { ConfiguracionTarjetaId = 10, Permitido = false }
                }
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            configuracionTarjetaId: 10);

        Assert.Equal(2, resultado.Bloqueos.Count);
        Assert.Contains(resultado.Bloqueos, b => b.ProductoId == 1 && b.Fuente == FuenteCondicionPagoEfectiva.Producto);
        Assert.Contains(resultado.Bloqueos, b => b.ProductoId == 2 && b.Fuente == FuenteCondicionPagoEfectiva.TarjetaEspecifica);
    }

    [Fact]
    public void ResolverCondicionesCarrito_RestriccionesMultiplesConservanDetallePorProductoYFuente()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                MaxCuotasSinInteres = 6
            },
            new ProductoCondicionPagoDto
            {
                ProductoId = 2,
                TipoPago = TipoPago.TarjetaCredito,
                Tarjetas = new[]
                {
                    new ProductoCondicionPagoTarjetaDto
                    {
                        ConfiguracionTarjetaId = 10,
                        MaxCuotasSinInteres = 6
                    }
                }
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(
            condiciones,
            TipoPago.TarjetaCredito,
            configuracionTarjetaId: 10);

        Assert.Equal(6, resultado.MaxCuotasSinInteres);
        Assert.Equal(2, resultado.Restricciones.Count);
        Assert.Contains(resultado.Restricciones, r => r.ProductoId == 1 && r.Fuente == FuenteCondicionPagoEfectiva.Producto);
        Assert.Contains(resultado.Restricciones, r => r.ProductoId == 2 && r.Fuente == FuenteCondicionPagoEfectiva.TarjetaEspecifica);
    }

    [Fact]
    public void ResolverCondicionesCarrito_FuentePermitidoYFuenteRestriccionQuedanDiferenciadas()
    {
        var condiciones = new[]
        {
            new ProductoCondicionPagoDto
            {
                ProductoId = 1,
                TipoPago = TipoPago.TarjetaCredito,
                MaxCuotasSinInteres = 6
            }
        };

        var resultado = ProductoCondicionPagoRules.ResolverCondicionesCarrito(condiciones, TipoPago.TarjetaCredito);

        Assert.True(resultado.Permitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Global, resultado.FuentePermitido);
        Assert.Equal(FuenteCondicionPagoEfectiva.Producto, resultado.FuenteRestriccion);
    }

    [Fact]
    public void NormalizarTipoPago_TarjetaLegacyNoQuedaPersistidoComoCreditoSilenciosamente()
    {
        var sinTipoTarjeta = ProductoCondicionPagoRules.NormalizarTipoPago(TipoPago.Tarjeta);
        var conCreditoExplicito = ProductoCondicionPagoRules.NormalizarTipoPago(TipoPago.Tarjeta, TipoTarjeta.Credito);
        var validacion = Assert.Single(ProductoCondicionPagoRules.ValidarCondiciones(new[]
        {
            new ProductoCondicionPagoDto { ProductoId = 1, TipoPago = TipoPago.Tarjeta }
        }));

        Assert.Equal(TipoPago.Tarjeta, sinTipoTarjeta);
        Assert.Equal(TipoPago.TarjetaCredito, conCreditoExplicito);
        Assert.Equal(CodigoValidacionCondicionPago.TipoPagoTarjetaLegacyAmbiguo, validacion.Codigo);
        Assert.Equal(SeveridadValidacionCondicionPago.Advertencia, validacion.Severidad);
    }
}
