using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Micro-lote 7 — Paridad Crear/Editar Venta (validación de render real).
/// Rendersiza /Venta/Create y /Venta/Edit a través del pipeline HTTP + Razor real
/// (parcial compartido _VentaWizardForm) y verifica que ambas pantallas exponen el
/// mismo wizard de 4 pasos y los mismos componentes, con las únicas diferencias
/// inherentes al modo (seed de edición, copy). También verifica el guard de estado
/// no editable. Repetible; no requiere navegador ni credenciales reales.
/// </summary>
[Collection("HttpIntegration")]
public class VentaCrearEditarParidadHttpTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string TestUser = "testuser";
    private static int _counter = 41000;
    private readonly CustomWebApplicationFactory _factory;

    public VentaCrearEditarParidadHttpTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // Ids/estructura que ambas pantallas deben renderizar (contrato de paridad real).
    private static readonly string[] PasosCanonicos = { "cliente", "productos", "pago", "credito", "revision" };

    private static readonly string[] ComponentesCompartidos =
    {
        "id=\"venta-form\"", "id=\"select-tipo-pago\"", "id=\"tbody-detalles\"",
        "id=\"btn-confirmar\"", "id=\"panel-tarjeta\"", "id=\"panel-cheque\"",
        "id=\"panel-credito-personal\"", "id=\"panel-planes-pago\"",
        "id=\"panel-verificacion-crediticia\"", "id=\"panel-documentacion-faltante\"",
        "id=\"detalles-hidden-inputs\"", "id=\"modal-documentacion\"",
        "id=\"total-final\"", "id=\"hdn-total\"", "data-mobile-total"
    };

    [Fact]
    public async Task Create_RenderizaWizardCompartidoConCreditoCondicional()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync("/Venta/Create");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"venta-create-page\"", html);
        AssertWizardConCreditoCondicional(html);
        foreach (var id in ComponentesCompartidos)
        {
            Assert.Contains(id, html);
        }

        // Copy y ausencia de seed propios del modo creación.
        Assert.Contains("Guardar Operación", html);
        Assert.DoesNotContain("id=\"venta-inicial-json\"", html);
    }

    [Fact]
    public async Task Edit_RenderizaMismoWizardConCreditoCondicional_YPrecargaSeed()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, clienteNombre, productoNombre) = await SeedVentaEditableAsync(EstadoVenta.Presupuesto);
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/Edit/{ventaId}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"venta-edit-page\"", html);
        AssertWizardConCreditoCondicional(html);
        foreach (var id in ComponentesCompartidos)
        {
            Assert.Contains(id, html);
        }

        // Diferencias propias del modo edición.
        Assert.Contains("Guardar cambios", html);
        Assert.Contains("id=\"venta-inicial-json\"", html);
        Assert.Contains("window.ventaInicial", html);
        // Precarga real del cliente y del producto en el seed.
        Assert.Contains(clienteNombre, html);
        Assert.Contains(productoNombre, html);
    }

    [Fact]
    public async Task CrearYEditar_RenderizanElMismoConjuntoDePasosYComponentes()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, _, _) = await SeedVentaEditableAsync(EstadoVenta.Presupuesto);
        var client = _factory.CreateAuthenticatedClient();

        var createHtml = await (await client.GetAsync("/Venta/Create")).Content.ReadAsStringAsync();
        var editHtml = await (await client.GetAsync($"/Venta/Edit/{ventaId}")).Content.ReadAsStringAsync();

        // Ambas: mismos 4 pasos, ninguna un 5.º paso "crédito".
        AssertWizardConCreditoCondicional(createHtml);
        AssertWizardConCreditoCondicional(editHtml);

        // Ambas: mismos componentes clave.
        foreach (var id in ComponentesCompartidos)
        {
            Assert.Contains(id, createHtml);
            Assert.Contains(id, editHtml);
        }
    }

    [Fact]
    public async Task Edit_VentaNoEditable_RedirigeAlDetalleSinRenderizarForm()
    {
        await _factory.SeedTestUserAsync();
        await SeedCajaAbiertaAsync();
        var (ventaId, _, _) = await SeedVentaEditableAsync(EstadoVenta.Confirmada);
        var client = _factory.CreateAuthenticatedClient();

        var response = await client.GetAsync($"/Venta/Edit/{ventaId}");

        // El guard de estado redirige (no editable) — no renderiza el form de edición.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Venta/Details", response.Headers.Location?.OriginalString ?? string.Empty);
    }

    private static void AssertWizardConCreditoCondicional(string html)
    {
        foreach (var paso in PasosCanonicos)
        {
            Assert.Contains($"id=\"step-btn-{paso}\"", html);
            Assert.Contains($"id=\"step-panel-{paso}\"", html);
        }

        // No existe un 5.º paso "crédito": converge a la estructura canónica de Create.
        Assert.Contains("id=\"step-panel-credito\"", html);
        Assert.Contains("id=\"step-btn-credito\"", html);
    }

    private async Task SeedCajaAbiertaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        if (await context.AperturasCaja.AnyAsync(a => !a.Cerrada && a.UsuarioApertura == TestUser))
        {
            return;
        }

        var caja = new Caja
        {
            Codigo = "CAJA-PARIDAD",
            Nombre = "Caja Paridad Crear/Editar",
            IsDeleted = false,
            RowVersion = new byte[8]
        };
        context.Cajas.Add(caja);
        await context.SaveChangesAsync();

        context.AperturasCaja.Add(new AperturaCaja
        {
            CajaId = caja.Id,
            UsuarioApertura = TestUser,
            MontoInicial = 0m,
            Cerrada = false,
            IsDeleted = false,
            RowVersion = new byte[8]
        });
        await context.SaveChangesAsync();
    }

    private async Task<(int VentaId, string ClienteNombre, string ProductoNombre)> SeedVentaEditableAsync(EstadoVenta estado)
    {
        var suffix = Interlocked.Increment(ref _counter);

        using var scope = _factory.Services.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();

        var marca = new Marca { Codigo = $"MP{suffix}", Nombre = $"Marca {suffix}", Activo = true };
        var categoria = new Categoria { Codigo = $"CP{suffix}", Nombre = $"Categoria {suffix}", Activo = true };
        var clienteNombre = $"ClienteParidad{suffix}";
        var cliente = new Cliente
        {
            Nombre = clienteNombre,
            Apellido = "FIX-RV-PARIDAD",
            NumeroDocumento = $"40{suffix}",
            Telefono = "1111-2222",
            Domicilio = "Calle Paridad 123"
        };

        context.Marcas.Add(marca);
        context.Categorias.Add(categoria);
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync();

        var productoNombre = $"ProductoParidad{suffix}";
        var producto = new Producto
        {
            Codigo = $"PP{suffix}",
            Nombre = productoNombre,
            CategoriaId = categoria.Id,
            MarcaId = marca.Id,
            PrecioCompra = 100m,
            PrecioVenta = 121m,
            PorcentajeIVA = 21m,
            Activo = true
        };
        context.Productos.Add(producto);
        await context.SaveChangesAsync();

        var venta = new Venta
        {
            ClienteId = cliente.Id,
            Numero = $"VP-{suffix}",
            FechaVenta = new DateTime(2026, 7, 24, 10, 0, 0, DateTimeKind.Utc),
            Estado = estado,
            TipoPago = TipoPago.Efectivo,
            Subtotal = 100m,
            IVA = 21m,
            Total = 121m,
            EstadoAutorizacion = EstadoAutorizacionVenta.NoRequiere
        };
        venta.Detalles.Add(new VentaDetalle
        {
            ProductoId = producto.Id,
            Cantidad = 1,
            PrecioUnitario = 121m,
            Subtotal = 121m,
            PorcentajeIVA = 21m,
            AlicuotaIVANombre = "IVA 21%",
            SubtotalFinalNeto = 100m,
            SubtotalFinalIVA = 21m,
            SubtotalFinal = 121m
        });

        context.Ventas.Add(venta);
        await context.SaveChangesAsync();

        return (venta.Id, clienteNombre, productoNombre);
    }
}
