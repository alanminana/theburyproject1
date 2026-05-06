using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Controllers;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;
using TheBuryProject.ViewModels.Requests;
using TheBuryProject.ViewModels.Responses;

namespace TheBuryProject.Tests.Unit;

public class VentaApiControllerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task CalcularTotalesVenta_RequestInvalido_DevuelveBadRequest()
    {
        var controller = CreateController();

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("Debe especificar al menos un detalle para calcular los totales", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CalcularTotalesVenta_RequestNull_DevuelveBadRequest()
    {
        var controller = CreateController();

        var result = await controller.CalcularTotalesVenta(null!);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("Debe especificar al menos un detalle para calcular los totales", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CalcularTotalesVenta_RequestValido_DevuelveOkConCamposEsperados()
    {
        var ventaService = new StubVentaService
        {
            Totales = new CalculoTotalesVentaResponse
            {
                Subtotal = 100m,
                DescuentoGeneralAplicado = 5m,
                IVA = 21m,
                Total = 116m,
                Detalles =
                {
                    new DetalleCalculoTotalesVentaResponse
                    {
                        ProductoId = 7,
                        PorcentajeIVA = 21m,
                        SubtotalNeto = 100m,
                        SubtotalIVA = 21m,
                        Subtotal = 121m,
                        DescuentoGeneralProrrateado = 5m,
                        SubtotalFinalNeto = 95m,
                        SubtotalFinalIVA = 21m,
                        SubtotalFinal = 116m
                    }
                }
            }
        };
        var controller = CreateController(ventaService: ventaService);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 7, Cantidad = 1, PrecioUnitario = 121m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(100m, json.RootElement.GetProperty("subtotal").GetDecimal());
        Assert.Equal(5m, json.RootElement.GetProperty("descuentoGeneralAplicado").GetDecimal());
        Assert.Equal(21m, json.RootElement.GetProperty("iva").GetDecimal());
        Assert.Equal(116m, json.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("detalles").ValueKind);
    }

    [Fact]
    public async Task CalcularTotalesVenta_SinTarjetaId_MaxCuotasEsNull()
    {
        var controller = CreateController();

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 1, Cantidad = 1, PrecioUnitario = 100m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("maxCuotasSinInteresEfectivo").ValueKind);
        Assert.False(json.RootElement.GetProperty("cuotasSinInteresLimitadasPorProducto").GetBoolean());
    }

    [Fact]
    public async Task CalcularTotalesVenta_ConTarjetaSinInteresYRestriccionProducto_DevuelveMaxEfectivo()
    {
        var configuracionPago = new StubConfiguracionPagoService
        {
            MaxCuotasResult = new MaxCuotasSinInteresResultado
            {
                TarjetaId = 5,
                MaxCuotas = 3,
                LimitadoPorProducto = true
            }
        };
        var controller = CreateController(configuracionPagoService: configuracionPago);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            TarjetaId = 5,
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 10, Cantidad = 1, PrecioUnitario = 200m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(3, json.RootElement.GetProperty("maxCuotasSinInteresEfectivo").GetInt32());
        Assert.True(json.RootElement.GetProperty("cuotasSinInteresLimitadasPorProducto").GetBoolean());
    }

    [Fact]
    public async Task CalcularTotalesVenta_TarjetaSinRestriccion_DevuelveMaxTarjetaSinLimitacionProducto()
    {
        var configuracionPago = new StubConfiguracionPagoService
        {
            MaxCuotasResult = new MaxCuotasSinInteresResultado
            {
                TarjetaId = 5,
                MaxCuotas = 12,
                LimitadoPorProducto = false
            }
        };
        var controller = CreateController(configuracionPagoService: configuracionPago);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            TarjetaId = 5,
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 10, Cantidad = 1, PrecioUnitario = 200m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(12, json.RootElement.GetProperty("maxCuotasSinInteresEfectivo").GetInt32());
        Assert.False(json.RootElement.GetProperty("cuotasSinInteresLimitadasPorProducto").GetBoolean());
    }

    [Fact]
    public async Task CalcularTotalesVenta_ConTarjetaConInteres_MaxCuotasEsNull()
    {
        // Stub returns null → tarjeta ConInteres or not found → no limit
        var configuracionPago = new StubConfiguracionPagoService { MaxCuotasResult = null };
        var controller = CreateController(configuracionPagoService: configuracionPago);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            TarjetaId = 7,
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 1, Cantidad = 1, PrecioUnitario = 100m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("maxCuotasSinInteresEfectivo").ValueKind);
        Assert.False(json.RootElement.GetProperty("cuotasSinInteresLimitadasPorProducto").GetBoolean());
    }

    [Fact]
    public async Task CalcularTotalesVenta_TarjetaDebitoConRecargo_DevuelveRecargoYTotalFinal()
    {
        var ventaService = new StubVentaService
        {
            Totales = new CalculoTotalesVentaResponse { Subtotal = 826.45m, IVA = 173.55m, Total = 1_000m }
        };
        var configuracionPago = new StubConfiguracionPagoService
        {
            TarjetaById = new ConfiguracionTarjetaViewModel
            {
                Id = 9,
                Activa = true,
                TipoTarjeta = TipoTarjeta.Debito,
                TieneRecargoDebito = true,
                PorcentajeRecargoDebito = 5m
            }
        };
        var controller = CreateController(ventaService: ventaService, configuracionPagoService: configuracionPago);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            TarjetaId = 9,
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 1, Cantidad = 1, PrecioUnitario = 1_000m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(1_000m, json.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(50m, json.RootElement.GetProperty("recargoDebitoAplicado").GetDecimal());
        Assert.Equal(5m, json.RootElement.GetProperty("porcentajeRecargoDebitoAplicado").GetDecimal());
        Assert.Equal(1_050m, json.RootElement.GetProperty("totalConRecargoDebito").GetDecimal());
    }

    [Fact]
    public async Task CalcularTotalesVenta_TarjetaDebitoSinRecargo_NoDevuelveRecargo()
    {
        var ventaService = new StubVentaService
        {
            Totales = new CalculoTotalesVentaResponse { Total = 1_000m }
        };
        var configuracionPago = new StubConfiguracionPagoService
        {
            TarjetaById = new ConfiguracionTarjetaViewModel
            {
                Id = 10,
                Activa = true,
                TipoTarjeta = TipoTarjeta.Debito,
                TieneRecargoDebito = false
            }
        };
        var controller = CreateController(ventaService: ventaService, configuracionPagoService: configuracionPago);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            TarjetaId = 10,
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 1, Cantidad = 1, PrecioUnitario = 1_000m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("recargoDebitoAplicado").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("porcentajeRecargoDebitoAplicado").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("totalConRecargoDebito").ValueKind);
    }

    [Fact]
    public async Task CalcularTotalesVenta_TarjetaCreditoConRecargoDebitoConfigurado_NoAplicaRecargo()
    {
        var ventaService = new StubVentaService
        {
            Totales = new CalculoTotalesVentaResponse { Total = 1_000m }
        };
        var configuracionPago = new StubConfiguracionPagoService
        {
            TarjetaById = new ConfiguracionTarjetaViewModel
            {
                Id = 11,
                Activa = true,
                TipoTarjeta = TipoTarjeta.Credito,
                TieneRecargoDebito = true,
                PorcentajeRecargoDebito = 5m
            }
        };
        var controller = CreateController(ventaService: ventaService, configuracionPagoService: configuracionPago);

        var result = await controller.CalcularTotalesVenta(new CalcularTotalesVentaRequest
        {
            TarjetaId = 11,
            Detalles = { new DetalleCalculoVentaRequest { ProductoId = 1, Cantidad = 1, PrecioUnitario = 1_000m } }
        });

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("recargoDebitoAplicado").ValueKind);
        Assert.Equal(1_000m, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 0)]
    public async Task PrevalidarCredito_ParametrosInvalidos_DevuelveBadRequest(int clienteId, decimal monto)
    {
        var controller = CreateController();

        var result = await controller.PrevalidarCredito(clienteId, monto);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task PrevalidarCredito_RequestValido_DevuelveOkConCamposEsperados()
    {
        var validacion = new StubValidacionVentaService
        {
            Resultado = new PrevalidacionResultViewModel
            {
                Resultado = ResultadoPrevalidacion.RequiereAutorizacion,
                ClienteId = 3,
                MontoSolicitado = 500m,
                LimiteCredito = 1_000m,
                CupoDisponible = 400m,
                CreditoUtilizado = 600m,
                TieneMora = true,
                DiasMora = 8,
                MontoMora = 123m,
                DocumentacionCompleta = false,
                DocumentosFaltantes = { "DNI" },
                DocumentosVencidos = { "Recibo" },
                Motivos =
                {
                    new MotivoPrevalidacion
                    {
                        Categoria = CategoriaMotivo.Cupo,
                        Titulo = "Cupo insuficiente",
                        Descripcion = "Requiere autorización",
                        EsBloqueante = false
                    }
                }
            }
        };
        var controller = CreateController(validacionVentaService: validacion);

        var result = await controller.PrevalidarCredito(3, 500m);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(1, json.RootElement.GetProperty("resultado").GetInt32());
        Assert.Equal("warning", json.RootElement.GetProperty("colorBadge").GetString());
        Assert.Equal("Requiere autorización", json.RootElement.GetProperty("textoEstado").GetString());
        Assert.Equal(1_000m, json.RootElement.GetProperty("limiteCredito").GetDecimal());
        Assert.Equal(400m, json.RootElement.GetProperty("cupoDisponible").GetDecimal());
        Assert.Equal(600m, json.RootElement.GetProperty("creditoUtilizado").GetDecimal());
        Assert.True(json.RootElement.GetProperty("tieneMora").GetBoolean());
        Assert.Equal(8, json.RootElement.GetProperty("diasMora").GetInt32());
        Assert.Equal(123m, json.RootElement.GetProperty("montoMora").GetDecimal());
        Assert.False(json.RootElement.GetProperty("documentacionCompleta").GetBoolean());
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("documentosFaltantes").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("documentosVencidos").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("motivos").ValueKind);
        Assert.True(json.RootElement.TryGetProperty("mensajeResumen", out _));
    }

    [Fact]
    public async Task BuscarProductos_TerminoValido_DevuelveCamposConsumidosPorJS()
    {
        var productoService = new StubProductoService
        {
            ProductosVenta =
            {
                new ProductoVentaDto
                {
                    Id = 9,
                    Codigo = "SKU-9",
                    Nombre = "Notebook",
                    Marca = "Marca",
                    Categoria = "Categoria",
                    StockActual = 4,
                    PrecioVenta = 1500m,
                    CaracteristicasResumen = "16GB · SSD",
                    CodigoExacto = true
                }
            }
        };
        var controller = CreateController(productoService: productoService);

        var result = await controller.BuscarProductos("note", take: 20);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        var item = json.RootElement[0];
        Assert.Equal(9, item.GetProperty("id").GetInt32());
        Assert.Equal("SKU-9", item.GetProperty("codigo").GetString());
        Assert.Equal("Notebook", item.GetProperty("nombre").GetString());
        Assert.Equal("Marca", item.GetProperty("marca").GetString());
        Assert.Equal("Categoria", item.GetProperty("categoria").GetString());
        Assert.Equal(4m, item.GetProperty("stockActual").GetDecimal());
        Assert.Equal(1500m, item.GetProperty("precioVenta").GetDecimal());
        Assert.Equal("16GB · SSD", item.GetProperty("caracteristicasResumen").GetString());
    }

    [Fact]
    public async Task BuscarProductos_TerminoVacio_DevuelveListaVacia()
    {
        var controller = CreateController();

        var result = await controller.BuscarProductos("   ");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value);
    }

    [Fact]
    public async Task GetTarjetasActivas_DevuelveCamposConsumidosPorJS()
    {
        var configuracionPago = new StubConfiguracionPagoService
        {
            Tarjetas =
            {
                new TarjetaActivaVentaResultado
                {
                    Id = 4,
                    Nombre = "Visa",
                    Tipo = TipoTarjeta.Credito,
                    PermiteCuotas = true,
                    CantidadMaximaCuotas = 12,
                    TipoCuota = TipoCuotaTarjeta.ConInteres,
                    TasaInteres = 5m,
                    TieneRecargo = true,
                    PorcentajeRecargo = 2m
                }
            }
        };
        var controller = CreateController(configuracionPagoService: configuracionPago);

        var result = await controller.GetTarjetasActivas();

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        var item = json.RootElement[0];
        Assert.Equal(4, item.GetProperty("id").GetInt32());
        Assert.Equal("Visa", item.GetProperty("nombre").GetString());
        Assert.Equal(1, item.GetProperty("tipo").GetInt32());
        Assert.True(item.GetProperty("permiteCuotas").GetBoolean());
        Assert.Equal(12, item.GetProperty("cantidadMaximaCuotas").GetInt32());
        Assert.Equal(1, item.GetProperty("tipoCuota").GetInt32());
        Assert.Equal(5m, item.GetProperty("tasaInteres").GetDecimal());
        Assert.True(item.GetProperty("tieneRecargo").GetBoolean());
        Assert.Equal(2m, item.GetProperty("porcentajeRecargo").GetDecimal());
    }

    [Theory]
    [InlineData(0, 100, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(1, 100, 0)]
    public async Task CalcularCuotasTarjeta_ParametrosInvalidos_DevuelveBadRequest(int tarjetaId, decimal monto, int cuotas)
    {
        var controller = CreateController();

        var result = await controller.CalcularCuotasTarjeta(tarjetaId, monto, cuotas);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("Los parámetros para calcular cuotas deben ser válidos", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CalcularCuotasTarjeta_RequestValido_DevuelveMontoCuotaMontoTotalInteres()
    {
        var ventaService = new StubVentaService
        {
            Tarjeta = new DatosTarjetaViewModel
            {
                MontoCuota = 110m,
                MontoTotalConInteres = 330m
            }
        };
        var controller = CreateController(ventaService: ventaService);

        var result = await controller.CalcularCuotasTarjeta(2, 300m, 3);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(110m, json.RootElement.GetProperty("montoCuota").GetDecimal());
        Assert.Equal(330m, json.RootElement.GetProperty("montoTotal").GetDecimal());
        Assert.Equal(30m, json.RootElement.GetProperty("interes").GetDecimal());
    }

    [Fact]
    public async Task CalcularCuotasTarjeta_TarjetaNoDisponible_DevuelveBadRequest()
    {
        var ventaService = new StubVentaService
        {
            InvalidCardCalculationMessage = "La tarjeta seleccionada no está disponible"
        };
        var controller = CreateController(ventaService: ventaService);

        var result = await controller.CalcularCuotasTarjeta(2, 300m, 3);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("La tarjeta seleccionada no está disponible", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CalcularCuotasTarjeta_ConInteresSinTasa_DevuelveBadRequest()
    {
        var ventaService = new StubVentaService
        {
            InvalidCardCalculationMessage = "La tarjeta con interés no tiene tasa configurada"
        };
        var controller = CreateController(ventaService: ventaService);

        var result = await controller.CalcularCuotasTarjeta(2, 300m, 3);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("La tarjeta con interés no tiene tasa configurada", json.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetPrecioProducto_IdInvalido_DevuelveBadRequest(int id)
    {
        var controller = CreateController();

        var result = await controller.GetPrecioProducto(id);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("El identificador de producto debe ser válido", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetPrecioProducto_ProductoInexistente_DevuelveNotFound()
    {
        var controller = CreateController(productoService: new StubProductoService());

        var result = await controller.GetPrecioProducto(99);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var json = ToJson(notFound.Value);
        Assert.Equal("Producto no encontrado", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetPrecioProducto_ProductoValido_DevuelvePrecioVigente()
    {
        var productoService = new StubProductoService
        {
            Producto = new Producto
            {
                Id = 8,
                Codigo = "TV-8",
                Nombre = "Televisor",
                PrecioVenta = 100m,
                StockActual = 2,
                Activo = true
            },
            PrecioVenta = new ProductoPrecioVentaResultado
            {
                ProductoId = 8,
                PrecioVenta = 150m,
                FuentePrecio = FuentePrecioVigente.ProductoPrecioLista,
                Codigo = "TV-8",
                Nombre = "Televisor",
                StockActual = 2
            }
        };
        var controller = CreateController(productoService: productoService);

        var result = await controller.GetPrecioProducto(8);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(150m, json.RootElement.GetProperty("precioVenta").GetDecimal());
        Assert.Equal(2m, json.RootElement.GetProperty("stockActual").GetDecimal());
        Assert.Equal("TV-8", json.RootElement.GetProperty("codigo").GetString());
        Assert.Equal("Televisor", json.RootElement.GetProperty("nombre").GetString());
    }

    [Fact]
    public async Task BuscarClientes_TerminoValido_DevuelveCamposConsumidosPorJS()
    {
        var clienteService = new StubClienteService
        {
            Clientes =
            {
                new Cliente
                {
                    Id = 5,
                    Nombre = "Ana",
                    Apellido = "Gomez",
                    TipoDocumento = "DNI",
                    NumeroDocumento = "123",
                    Telefono = "555",
                    Email = "ana@example.test",
                    Activo = true
                }
            }
        };
        var controller = CreateController(clienteService: clienteService);

        var result = await controller.BuscarClientes("ana", take: 10);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        var item = json.RootElement[0];
        Assert.Equal(5, item.GetProperty("id").GetInt32());
        Assert.Equal("Ana", item.GetProperty("nombre").GetString());
        Assert.Equal("Gomez", item.GetProperty("apellido").GetString());
        Assert.Equal("DNI", item.GetProperty("tipoDocumento").GetString());
        Assert.Equal("123", item.GetProperty("numeroDocumento").GetString());
        Assert.Equal("555", item.GetProperty("telefono").GetString());
        Assert.Equal("ana@example.test", item.GetProperty("email").GetString());
        Assert.True(item.TryGetProperty("display", out _));
    }

    [Fact]
    public async Task BuscarClientes_TerminoVacio_DevuelveListaVacia()
    {
        var controller = CreateController();

        var result = await controller.BuscarClientes("   ");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsAssignableFrom<System.Collections.IEnumerable>(ok.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetCreditosCliente_ClienteIdInvalido_DevuelveBadRequest(int clienteId)
    {
        var controller = CreateController();

        var result = await controller.GetCreditosCliente(clienteId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("El identificador de cliente debe ser válido", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetCreditosCliente_DevuelveCamposLegacyDelSelector()
    {
        var creditoService = new StubCreditoService
        {
            CreditosVenta =
            {
                new CreditoVentaResultado
                {
                    Id = 11,
                    Numero = "CR-11",
                    MontoAprobado = 10_000m,
                    SaldoPendiente = 4_000m,
                    TasaInteres = 3m,
                    Estado = EstadoCredito.Activo,
                    Disponible = true
                }
            }
        };
        var controller = CreateController(creditoService: creditoService);

        var result = await controller.GetCreditosCliente(5);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        var item = json.RootElement[0];
        Assert.Equal(11, item.GetProperty("id").GetInt32());
        Assert.Equal("CR-11", item.GetProperty("numero").GetString());
        Assert.Equal(10_000m, item.GetProperty("montoAprobado").GetDecimal());
        Assert.Equal(4_000m, item.GetProperty("saldoPendiente").GetDecimal());
        Assert.Equal(3m, item.GetProperty("tasaInteres").GetDecimal());
        Assert.Equal($"CR-11 - Saldo disponible: ${4_000m:N2}", item.GetProperty("detalle").GetString());
    }

    [Fact]
    public async Task GetCreditosCliente_ClienteSinCreditos_DevuelveListaVacia()
    {
        var controller = CreateController(creditoService: new StubCreditoService());

        var result = await controller.GetCreditosCliente(7);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(0, json.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetInfoCredito_CreditoIdInvalido_DevuelveBadRequest(int creditoId)
    {
        var controller = CreateController();

        var result = await controller.GetInfoCredito(creditoId);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var json = ToJson(badRequest.Value);
        Assert.Equal("El identificador de crédito debe ser válido", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetInfoCredito_Inexistente_DevuelveNotFound()
    {
        var controller = CreateController(creditoService: new StubCreditoService());

        var result = await controller.GetInfoCredito(99);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var json = ToJson(notFound.Value);
        Assert.Equal("Crédito no encontrado", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetInfoCredito_NoDisponible_DevuelveNotFoundConEstado()
    {
        var creditoService = new StubCreditoService
        {
            CreditoVenta = new CreditoVentaResultado
            {
                Id = 12,
                Numero = "CR-12",
                Estado = EstadoCredito.Cancelado,
                Disponible = false
            }
        };
        var controller = CreateController(creditoService: creditoService);

        var result = await controller.GetInfoCredito(12);

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        var json = ToJson(notFound.Value);
        Assert.Equal("El crédito está en estado Cancelado y no se puede usar", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetInfoCredito_Disponible_DevuelveCamposLegacy()
    {
        var creditoService = new StubCreditoService
        {
            CreditoVenta = new CreditoVentaResultado
            {
                Id = 13,
                Numero = "CR-13",
                MontoAprobado = 12_000m,
                SaldoPendiente = 8_000m,
                TasaInteres = 2.5m,
                Estado = EstadoCredito.Aprobado,
                Disponible = true
            }
        };
        var controller = CreateController(creditoService: creditoService);

        var result = await controller.GetInfoCredito(13);

        var ok = Assert.IsType<OkObjectResult>(result);
        var json = ToJson(ok.Value);
        Assert.Equal(13, json.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("CR-13", json.RootElement.GetProperty("numero").GetString());
        Assert.Equal(12_000m, json.RootElement.GetProperty("montoAprobado").GetDecimal());
        Assert.Equal(8_000m, json.RootElement.GetProperty("saldoPendiente").GetDecimal());
        Assert.Equal(2.5m, json.RootElement.GetProperty("tasaInteres").GetDecimal());
        Assert.Equal(1, json.RootElement.GetProperty("estado").GetInt32());
    }

    private static VentaApiController CreateController(
        IProductoService? productoService = null,
        ICreditoService? creditoService = null,
        IVentaService? ventaService = null,
        IClienteService? clienteService = null,
        IConfiguracionPagoService? configuracionPagoService = null,
        IValidacionVentaService? validacionVentaService = null)
    {
        return new VentaApiController(
            productoService ?? new StubProductoService(),
            creditoService ?? new StubCreditoService(),
            ventaService ?? new StubVentaService(),
            clienteService ?? new StubClienteService(),
            configuracionPagoService ?? new StubConfiguracionPagoService(),
            validacionVentaService ?? new StubValidacionVentaService(),
            NullLogger<VentaApiController>.Instance);
    }

    private static JsonDocument ToJson(object? value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return JsonDocument.Parse(json);
    }

    private sealed class StubProductoService : IProductoService
    {
        public Producto? Producto { get; set; }
        public ProductoPrecioVentaResultado? PrecioVenta { get; set; }
        public List<ProductoVentaDto> ProductosVenta { get; } = new();

        public Task<IEnumerable<Producto>> GetAllAsync() => throw new NotImplementedException();
        public Task<Producto?> GetByIdAsync(int id) => Task.FromResult(Producto?.Id == id ? Producto : null);
        public Task<IEnumerable<Producto>> GetByCategoriaAsync(int categoriaId) => throw new NotImplementedException();
        public Task<IEnumerable<Producto>> GetByMarcaAsync(int marcaId) => throw new NotImplementedException();
        public Task<IEnumerable<Producto>> GetProductosConStockBajoAsync() => throw new NotImplementedException();
        public Task<Producto> CreateAsync(Producto producto) => throw new NotImplementedException();
        public Task<Producto> UpdateAsync(Producto producto) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<IEnumerable<Producto>> SearchAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false, string? orderBy = null, string? orderDirection = "asc") => throw new NotImplementedException();
        public Task<List<int>> SearchIdsAsync(string? searchTerm = null, int? categoriaId = null, int? marcaId = null, bool stockBajo = false, bool soloActivos = false) => throw new NotImplementedException();
        public Task<IEnumerable<ProductoVentaDto>> BuscarParaVentaAsync(string term, int take = 20, int? categoriaId = null, int? marcaId = null, bool soloConStock = true, decimal? precioMin = null, decimal? precioMax = null) => Task.FromResult<IEnumerable<ProductoVentaDto>>(ProductosVenta);
        public Task<ProductoPrecioVentaResultado?> ObtenerPrecioVigenteParaVentaAsync(int productoId) => Task.FromResult(PrecioVenta);
        public Task<Producto> ActualizarStockAsync(int id, decimal cantidad) => throw new NotImplementedException();
        public Task<Producto> ActualizarComisionAsync(int id, decimal porcentaje) => throw new NotImplementedException();
        public Task<bool> ExistsCodigoAsync(string codigo, int? excludeId = null) => throw new NotImplementedException();
    }

    private sealed class StubClienteService : IClienteService
    {
        public List<Cliente> Clientes { get; } = new();

        public Task<IEnumerable<Cliente>> GetAllAsync() => throw new NotImplementedException();
        public Task<Cliente?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<Cliente> CreateAsync(Cliente cliente) => throw new NotImplementedException();
        public Task<Cliente> UpdateAsync(Cliente cliente) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<IEnumerable<Cliente>> SearchAsync(string? searchTerm = null, string? tipoDocumento = null, bool? soloActivos = null, bool? conCreditosActivos = null, decimal? puntajeMinimo = null, string? orderBy = null, string? orderDirection = null) => Task.FromResult<IEnumerable<Cliente>>(Clientes);
        public Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null) => throw new NotImplementedException();
        public Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento) => throw new NotImplementedException();
        public Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string motivo) => throw new NotImplementedException();
    }

    private sealed class StubVentaService : IVentaService
    {
        public CalculoTotalesVentaResponse Totales { get; set; } = new();
        public DatosTarjetaViewModel Tarjeta { get; set; } = new() { MontoCuota = 0m, MontoTotalConInteres = 0m };
        public bool ThrowOnInvalidCardCalculation { get; set; }
        public string? InvalidCardCalculationMessage { get; set; }

        public Task<List<VentaViewModel>> GetAllAsync(VentaFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<VentaViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<VentaViewModel> CreateAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<VentaViewModel?> UpdateAsync(int id, VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaAsync(int id) => throw new NotImplementedException();
        public Task<bool> ConfirmarVentaCreditoAsync(int id) => throw new NotImplementedException();
        public Task<bool> CancelarVentaAsync(int id, string motivo) => throw new NotImplementedException();
        public Task AsociarCreditoAVentaAsync(int ventaId, int creditoId) => throw new NotImplementedException();
        public Task<bool> FacturarVentaAsync(int id, FacturaViewModel facturaViewModel) => throw new NotImplementedException();
        public Task<int?> AnularFacturaAsync(int facturaId, string motivo) => throw new NotImplementedException();
        public Task<bool> ValidarStockAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> SolicitarAutorizacionAsync(int id, string usuarioSolicita, string motivo) => throw new NotImplementedException();
        public Task<bool> AutorizarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RechazarVentaAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RegistrarExcepcionDocumentalAsync(int id, string usuarioAutoriza, string motivo) => throw new NotImplementedException();
        public Task<bool> RequiereAutorizacionAsync(VentaViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> GuardarDatosTarjetaAsync(int ventaId, DatosTarjetaViewModel datosTarjeta) => throw new NotImplementedException();
        public Task<bool> GuardarDatosChequeAsync(int ventaId, DatosChequeViewModel datosCheque) => throw new NotImplementedException();
        public Task<DatosTarjetaViewModel> CalcularCuotasTarjetaAsync(int tarjetaId, decimal monto, int cuotas)
        {
            if (ThrowOnInvalidCardCalculation && (tarjetaId <= 0 || monto <= 0 || cuotas <= 0))
                throw new InvalidOperationException("Parámetros inválidos");

            if (!string.IsNullOrWhiteSpace(InvalidCardCalculationMessage))
                throw new InvalidOperationException(InvalidCardCalculationMessage);

            return Task.FromResult(Tarjeta);
        }
        public Task<DatosCreditoPersonallViewModel> CalcularCreditoPersonallAsync(int creditoId, decimal montoAFinanciar, int cuotas, DateTime fechaPrimeraCuota) => throw new NotImplementedException();
        public Task<DatosCreditoPersonallViewModel?> ObtenerDatosCreditoVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ValidarDisponibilidadCreditoAsync(int creditoId, decimal monto) => throw new NotImplementedException();
        public CalculoTotalesVentaResponse CalcularTotalesPreview(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => Totales;
        public Task<CalculoTotalesVentaResponse> CalcularTotalesPreviewAsync(List<DetalleCalculoVentaRequest> detalles, decimal descuentoGeneral, bool descuentoEsPorcentaje) => Task.FromResult(Totales);
        public Task<decimal?> GetTotalVentaAsync(int ventaId) => throw new NotImplementedException();
    }

    private sealed class StubConfiguracionPagoService : IConfiguracionPagoService
    {
        public List<TarjetaActivaVentaResultado> Tarjetas { get; } = new();
        public ConfiguracionTarjetaViewModel? TarjetaById { get; set; }

        public Task<List<ConfiguracionPagoViewModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> GetByTipoPagoAsync(TipoPago tipoPago) => throw new NotImplementedException();
        public Task<decimal?> ObtenerTasaInteresMensualCreditoPersonalAsync() => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel> CreateAsync(ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<ConfiguracionPagoViewModel?> UpdateAsync(int id, ConfiguracionPagoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task GuardarConfiguracionesModalAsync(IReadOnlyList<ConfiguracionPagoViewModel> configuraciones) => throw new NotImplementedException();
        public Task<List<ConfiguracionTarjetaViewModel>> GetTarjetasActivasAsync() => throw new NotImplementedException();
        public Task<List<TarjetaActivaVentaResultado>> GetTarjetasActivasParaVentaAsync() => Task.FromResult(Tarjetas);
        public Task<ConfiguracionTarjetaViewModel?> GetTarjetaByIdAsync(int id) =>
            Task.FromResult(TarjetaById?.Id == id ? TarjetaById : null);
        public Task<bool> ValidarDescuento(TipoPago tipoPago, decimal descuento) => throw new NotImplementedException();
        public Task<decimal> CalcularRecargo(TipoPago tipoPago, decimal monto) => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoAsync() => throw new NotImplementedException();
        public Task<List<PerfilCreditoViewModel>> GetPerfilesCreditoActivosAsync() => throw new NotImplementedException();
        public Task GuardarCreditoPersonalAsync(CreditoPersonalConfigViewModel config) => throw new NotImplementedException();
        public Task<ParametrosCreditoCliente> ObtenerParametrosCreditoClienteAsync(int clienteId, decimal tasaGlobal) => throw new NotImplementedException();
        public Task<(int Min, int Max, string Descripcion, string? PerfilNombre)> ResolverRangoCuotasAsync(MetodoCalculoCredito metodo, int? perfilId, int? clienteId) => throw new NotImplementedException();
        public MaxCuotasSinInteresResultado? MaxCuotasResult { get; set; }
        public Task<MaxCuotasSinInteresResultado?> ObtenerMaxCuotasSinInteresEfectivoAsync(int tarjetaId, IEnumerable<int> productoIds) => Task.FromResult(MaxCuotasResult);
    }

    private sealed class StubValidacionVentaService : IValidacionVentaService
    {
        public PrevalidacionResultViewModel Resultado { get; set; } = new();

        public Task<PrevalidacionResultViewModel> PrevalidarAsync(int clienteId, decimal monto) => Task.FromResult(Resultado);
        public Task<ValidacionVentaResult> ValidarVentaCreditoPersonalAsync(int clienteId, decimal montoVenta, int? creditoId = null) => throw new NotImplementedException();
        public Task<ValidacionVentaResult> ValidarConfirmacionVentaAsync(int ventaId) => throw new NotImplementedException();
        public Task<bool> ClientePuedeRecibirCreditoAsync(int clienteId, decimal montoSolicitado) => throw new NotImplementedException();
        public Task<ResumenCrediticioClienteViewModel> ObtenerResumenCrediticioAsync(int clienteId) => throw new NotImplementedException();
    }

    private sealed class StubPrecioVigenteResolver : IPrecioVigenteResolver
    {
        public PrecioVigenteResultado? Resultado { get; set; }

        public Task<PrecioVigenteResultado?> ResolverAsync(int productoId, int? listaId = null, DateTime? fecha = null) => Task.FromResult(Resultado);
        public Task<IReadOnlyDictionary<int, PrecioVigenteResultado>> ResolverBatchAsync(IEnumerable<int> productoIds, int? listaId = null, DateTime? fecha = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, PrecioVigenteResultado>>(new Dictionary<int, PrecioVigenteResultado>());
    }

    private sealed class StubCreditoService : ICreditoService
    {
        public List<CreditoVentaResultado> CreditosVenta { get; } = new();
        public CreditoVentaResultado? CreditoVenta { get; set; }

        public Task<List<CreditoViewModel>> GetAllAsync(CreditoFilterViewModel? filter = null) => throw new NotImplementedException();
        public Task<CreditoViewModel?> GetByIdAsync(int id) => throw new NotImplementedException();
        public Task<List<CreditoViewModel>> GetByClienteIdAsync(int clienteId) => throw new NotImplementedException();
        public Task<List<CreditoVentaResultado>> GetCreditosDisponiblesParaVentaAsync(int clienteId) => Task.FromResult(CreditosVenta);
        public Task<CreditoVentaResultado?> GetCreditoParaVentaAsync(int creditoId) => Task.FromResult(CreditoVenta);
        public Task<CreditoViewModel> CreateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<CreditoViewModel> CreatePendienteConfiguracionAsync(int clienteId, decimal montoTotal) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(CreditoViewModel viewModel) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<SimularCreditoViewModel> SimularCreditoAsync(SimularCreditoViewModel modelo) => throw new NotImplementedException();
        public Task<bool> AprobarCreditoAsync(int creditoId, string aprobadoPor) => throw new NotImplementedException();
        public Task<bool> RechazarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<bool> CancelarCreditoAsync(int creditoId, string motivo) => throw new NotImplementedException();
        public Task<(bool Success, string? NumeroCredito, string? ErrorMessage)> SolicitarCreditoAsync(SolicitudCreditoViewModel solicitud, string usuarioSolicitante, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasByCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetCuotaByIdAsync(int cuotaId) => throw new NotImplementedException();
        public Task<bool> PagarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<PagoMultipleCuotasResult> PagarCuotasAsync(PagoMultipleCuotasRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> AdelantarCuotaAsync(PagarCuotaViewModel pago) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetPrimeraCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<CuotaViewModel?> GetUltimaCuotaPendienteAsync(int creditoId) => throw new NotImplementedException();
        public Task<List<CuotaViewModel>> GetCuotasVencidasAsync() => throw new NotImplementedException();
        public Task ActualizarEstadoCuotasAsync() => throw new NotImplementedException();
        public Task<bool> RecalcularSaldoCreditoAsync(int creditoId) => throw new NotImplementedException();
        public Task ConfigurarCreditoAsync(ConfiguracionCreditoComando comando) => throw new NotImplementedException();
    }
}
