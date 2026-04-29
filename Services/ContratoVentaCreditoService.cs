using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    public class ContratoVentaCreditoService : IContratoVentaCreditoService
    {
        private readonly AppDbContext _context;
        private readonly IFinancialCalculationService _financialService;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ContratoVentaCreditoService> _logger;

        private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
        {
            WriteIndented = false
        };

        public ContratoVentaCreditoService(
            AppDbContext context,
            IFinancialCalculationService financialService,
            IWebHostEnvironment environment,
            ILogger<ContratoVentaCreditoService> logger)
        {
            _context = context;
            _financialService = financialService;
            _environment = environment;
            _logger = logger;
        }

        public async Task<ContratoVentaCreditoValidacionResult> ValidarDatosParaGenerarAsync(int ventaId)
        {
            var result = new ContratoVentaCreditoValidacionResult();
            await CargarDatosValidadosAsync(ventaId, result);
            return result;
        }

        public async Task<ContratoVentaCredito> GenerarAsync(int ventaId, string usuario)
        {
            if (string.IsNullOrWhiteSpace(usuario))
                throw new InvalidOperationException("El usuario de generación es requerido.");

            var existente = await ObtenerContratoPorVentaAsync(ventaId);
            if (existente != null)
                return existente;

            var validacion = new ContratoVentaCreditoValidacionResult();
            var datos = await CargarDatosValidadosAsync(ventaId, validacion);
            if (datos == null || !validacion.EsValido)
                throw new InvalidOperationException(string.Join(" ", validacion.Errores));

            var numeroContrato = await GenerarNumeroContratoAsync();
            var numeroPagare = await GenerarNumeroPagareAsync();
            var fechaEmision = DateTime.UtcNow;
            var snapshot = ConstruirSnapshot(datos, numeroContrato, numeroPagare, fechaEmision, usuario.Trim());

            var contrato = new ContratoVentaCredito
            {
                VentaId = datos.Venta.Id,
                CreditoId = datos.Credito.Id,
                ClienteId = datos.Cliente.Id,
                PlantillaContratoCreditoId = datos.Plantilla.Id,
                NumeroContrato = numeroContrato,
                NumeroPagare = numeroPagare,
                FechaGeneracionUtc = fechaEmision,
                UsuarioGeneracion = usuario.Trim(),
                EstadoDocumento = EstadoDocumento.Pendiente,
                TextoContratoSnapshot = datos.Plantilla.TextoContrato,
                TextoPagareSnapshot = datos.Plantilla.TextoPagare,
                DatosSnapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions)
            };

            _context.ContratosVentaCredito.Add(contrato);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Contrato de venta crédito {NumeroContrato} preparado para venta {VentaId} por {Usuario}",
                contrato.NumeroContrato,
                ventaId,
                usuario);

            return contrato;
        }

        public async Task<ContratoVentaCredito> GenerarPdfAsync(int ventaId, string usuario)
        {
            await GenerarAsync(ventaId, usuario);

            var contrato = await _context.ContratosVentaCredito
                .Include(c => c.Venta)
                .Include(c => c.Credito)
                .Include(c => c.Cliente)
                .Include(c => c.PlantillaContratoCredito)
                .FirstOrDefaultAsync(c => c.VentaId == ventaId)
                ?? throw new InvalidOperationException("No se pudo obtener el contrato generado.");

            if (!string.IsNullOrWhiteSpace(contrato.RutaArchivo))
            {
                var rutaExistente = ResolverRutaContrato(contrato.RutaArchivo);
                if (File.Exists(rutaExistente))
                    return contrato;
            }

            var pdfBytes = GenerarPdfBytes(contrato);
            var nombreArchivo = $"{SanitizeFileName(contrato.NumeroContrato)}.pdf";
            var rutaRelativa = Path.Combine(
                "App_Data",
                "contratos-venta-credito",
                contrato.VentaId.ToString(),
                nombreArchivo);
            var rutaCompleta = ResolverRutaContrato(rutaRelativa);
            var directorio = Path.GetDirectoryName(rutaCompleta);
            if (!string.IsNullOrWhiteSpace(directorio))
                Directory.CreateDirectory(directorio);

            await File.WriteAllBytesAsync(rutaCompleta, pdfBytes);

            contrato.RutaArchivo = rutaRelativa.Replace('\\', '/');
            contrato.NombreArchivo = nombreArchivo;
            contrato.ContentHash = CalcularSha256(pdfBytes);
            contrato.EstadoDocumento = EstadoDocumento.Verificado;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "PDF de contrato {NumeroContrato} generado para venta {VentaId} en {Ruta}",
                contrato.NumeroContrato,
                ventaId,
                contrato.RutaArchivo);

            return contrato;
        }

        public async Task<ContratoVentaCreditoPdfArchivo?> ObtenerPdfAsync(int ventaId)
        {
            var contrato = await ObtenerContratoPorVentaAsync(ventaId);
            if (contrato == null || string.IsNullOrWhiteSpace(contrato.RutaArchivo))
                return null;

            var rutaCompleta = ResolverRutaContrato(contrato.RutaArchivo);
            if (!File.Exists(rutaCompleta))
                return null;

            return new ContratoVentaCreditoPdfArchivo
            {
                NombreArchivo = contrato.NombreArchivo ?? $"{SanitizeFileName(contrato.NumeroContrato)}.pdf",
                Contenido = await File.ReadAllBytesAsync(rutaCompleta)
            };
        }

        public async Task<bool> ExisteContratoGeneradoAsync(int ventaId)
        {
            var contrato = await _context.ContratosVentaCredito
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.VentaId == ventaId &&
                    c.EstadoDocumento == EstadoDocumento.Verificado &&
                    c.RutaArchivo != null &&
                    c.RutaArchivo != string.Empty);

            if (contrato == null)
                return false;

            return File.Exists(ResolverRutaContrato(contrato.RutaArchivo!));
        }

        public async Task<bool> ExistePlantillaActivaAsync()
            => await ObtenerPlantillaActivaAsync() != null;

        public async Task<ContratoVentaCredito?> ObtenerContratoPorVentaAsync(int ventaId)
        {
            return await _context.ContratosVentaCredito
                .AsNoTracking()
                .Include(c => c.PlantillaContratoCredito)
                .Include(c => c.Venta)
                .Include(c => c.Credito)
                .Include(c => c.Cliente)
                .FirstOrDefaultAsync(c => c.VentaId == ventaId);
        }

        public async Task<ContratoVentaCredito?> ObtenerContratoPorCreditoAsync(int creditoId)
        {
            return await _context.ContratosVentaCredito
                .AsNoTracking()
                .Include(c => c.PlantillaContratoCredito)
                .Include(c => c.Venta)
                .Include(c => c.Credito)
                .Include(c => c.Cliente)
                .FirstOrDefaultAsync(c => c.CreditoId == creditoId);
        }

        private async Task<DatosContratoContexto?> CargarDatosValidadosAsync(
            int ventaId,
            ContratoVentaCreditoValidacionResult result)
        {
            var venta = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.Cliente)
                .Include(v => v.Credito).ThenInclude(c => c!.Garante).ThenInclude(g => g!.GaranteCliente)
                .Include(v => v.Credito).ThenInclude(c => c!.Cuotas.Where(cu => !cu.IsDeleted).OrderBy(cu => cu.NumeroCuota))
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto)
                .Include(v => v.AperturaCaja).ThenInclude(a => a!.Caja)
                .Include(v => v.VendedorUser)
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);

            if (venta == null)
            {
                result.Errores.Add("Venta no encontrada.");
                return null;
            }

            ValidarVenta(venta, result);
            ValidarCliente(venta.Cliente, result);

            if (venta.Credito == null)
            {
                result.Errores.Add("La venta no tiene crédito asociado.");
                return null;
            }

            var credito = venta.Credito;
            ValidarCredito(credito, result);
            ValidarGaranteSiCorresponde(credito, result);

            var plantilla = await ObtenerPlantillaActivaAsync();
            if (plantilla == null)
            {
                result.Errores.Add("No existe una plantilla activa y vigente para contrato de crédito.");
                return null;
            }

            ValidarPlantilla(plantilla, result);

            var planCuotas = ConstruirPlanCuotas(credito, result);
            if (!planCuotas.Any())
                result.Errores.Add("El crédito debe tener un plan de cuotas con fechas de vencimiento.");

            return new DatosContratoContexto
            {
                Venta = venta,
                Credito = credito,
                Cliente = venta.Cliente,
                Plantilla = plantilla,
                PlanCuotas = planCuotas
            };
        }

        private async Task<PlantillaContratoCredito?> ObtenerPlantillaActivaAsync()
        {
            var hoy = DateTime.UtcNow.Date;

            return await _context.PlantillasContratoCredito
                .AsNoTracking()
                .Where(p => p.Activa &&
                            p.VigenteDesde.Date <= hoy &&
                            (!p.VigenteHasta.HasValue || p.VigenteHasta.Value.Date >= hoy))
                .OrderByDescending(p => p.VigenteDesde)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync();
        }

        private static void ValidarVenta(Venta venta, ContratoVentaCreditoValidacionResult result)
        {
            if (venta.TipoPago != TipoPago.CreditoPersonal)
                result.Errores.Add("La venta debe ser de tipo Crédito Personal.");

            if (string.IsNullOrWhiteSpace(venta.Numero))
                result.Errores.Add("La venta no tiene número.");

            if (venta.ClienteId <= 0)
                result.Errores.Add("La venta no tiene cliente asociado.");

            if (venta.Total <= 0)
                result.Errores.Add("El total de la venta debe ser mayor a cero.");

            if (!venta.Detalles.Any(d => !d.IsDeleted))
                result.Errores.Add("La venta debe tener productos.");
        }

        private static void ValidarCliente(Cliente? cliente, ContratoVentaCreditoValidacionResult result)
        {
            if (cliente == null)
            {
                result.Errores.Add("La venta no tiene cliente asociado.");
                return;
            }

            AgregarSiVacio(cliente.Nombre, "El cliente debe tener nombre.", result);
            AgregarSiVacio(cliente.Apellido, "El cliente debe tener apellido.", result);
            AgregarSiVacio(cliente.TipoDocumento, "El cliente debe tener tipo de documento.", result);
            AgregarSiVacio(cliente.NumeroDocumento, "El cliente debe tener número de documento.", result);
            AgregarSiVacio(cliente.Domicilio, "El cliente debe tener domicilio.", result);
            AgregarSiVacio(cliente.Localidad, "El cliente debe tener localidad.", result);
            AgregarSiVacio(cliente.Telefono, "El cliente debe tener teléfono.", result);
        }

        private static void ValidarCredito(Credito credito, ContratoVentaCreditoValidacionResult result)
        {
            AgregarSiVacio(credito.Numero, "El crédito debe tener número.", result);

            if (credito.CantidadCuotas <= 0)
                result.Errores.Add("El crédito debe tener cantidad de cuotas mayor a cero.");

            if (credito.MontoAprobado <= 0)
                result.Errores.Add("El monto financiado del crédito debe ser mayor a cero.");

            if (!credito.FechaPrimeraCuota.HasValue)
                result.Errores.Add("El crédito debe tener fecha de primera cuota.");

            if (credito.TasaInteres < 0)
                result.Errores.Add("La tasa del crédito no puede ser negativa.");
        }

        private static void ValidarGaranteSiCorresponde(Credito credito, ContratoVentaCreditoValidacionResult result)
        {
            if (!credito.RequiereGarante)
                return;

            if (credito.Garante == null)
            {
                result.Errores.Add("El crédito requiere garante y no tiene garante asociado.");
                return;
            }

            var garante = credito.Garante;
            var garanteCliente = garante.GaranteCliente;
            var nombre = garanteCliente?.ToDisplayName() ?? garante.ToDisplayName();
            var documento = garanteCliente?.NumeroDocumento ?? garante.NumeroDocumento;
            var domicilio = garanteCliente?.Domicilio ?? garante.Domicilio;

            AgregarSiVacio(nombre, "El garante debe tener nombre completo.", result);
            AgregarSiVacio(documento, "El garante debe tener número de documento.", result);
            AgregarSiVacio(domicilio, "El garante debe tener domicilio.", result);
        }

        private static void ValidarPlantilla(
            PlantillaContratoCredito plantilla,
            ContratoVentaCreditoValidacionResult result)
        {
            AgregarSiVacio(plantilla.NombreVendedor, "La plantilla debe tener nombre de vendedor.", result);
            AgregarSiVacio(plantilla.DomicilioVendedor, "La plantilla debe tener domicilio de vendedor.", result);
            AgregarSiVacio(plantilla.CiudadFirma, "La plantilla debe tener ciudad de firma.", result);
            AgregarSiVacio(plantilla.Jurisdiccion, "La plantilla debe tener jurisdicción.", result);
            AgregarSiVacio(plantilla.TextoContrato, "La plantilla debe tener texto de contrato.", result);
            AgregarSiVacio(plantilla.TextoPagare, "La plantilla debe tener texto de pagaré.", result);

            if (plantilla.InteresMoraDiarioPorcentaje <= 0)
                result.Errores.Add("La plantilla debe tener interés por mora diario mayor a cero.");
        }

        private List<CuotaContratoSnapshot> ConstruirPlanCuotas(
            Credito credito,
            ContratoVentaCreditoValidacionResult result)
        {
            if (credito.Cuotas.Any())
            {
                var cuotasPersistidas = credito.Cuotas
                    .Where(c => !c.IsDeleted)
                    .OrderBy(c => c.NumeroCuota)
                    .Select(c => new CuotaContratoSnapshot
                    {
                        NumeroCuota = c.NumeroCuota,
                        MontoCapital = c.MontoCapital,
                        MontoInteres = c.MontoInteres,
                        MontoTotal = c.MontoTotal,
                        FechaVencimiento = c.FechaVencimiento
                    })
                    .ToList();

                if (cuotasPersistidas.Any(c => c.FechaVencimiento == default))
                    result.Errores.Add("Todas las cuotas del crédito deben tener fecha de vencimiento.");

                return cuotasPersistidas;
            }

            if (credito.CantidadCuotas <= 0 || credito.MontoAprobado <= 0 || !credito.FechaPrimeraCuota.HasValue)
                return new List<CuotaContratoSnapshot>();

            var montoFinanciado = credito.MontoAprobado;
            var tasaDecimal = credito.TasaInteres / 100m;
            var montoCuota = _financialService.ComputePmt(tasaDecimal, credito.CantidadCuotas, montoFinanciado);
            var totalAPagar = montoCuota * credito.CantidadCuotas;
            var interesTotal = totalAPagar - montoFinanciado;
            var capitalPorCuota = Math.Round(montoFinanciado / credito.CantidadCuotas, 2, MidpointRounding.AwayFromZero);
            var interesPorCuota = Math.Round(interesTotal / credito.CantidadCuotas, 2, MidpointRounding.AwayFromZero);

            var fecha = credito.FechaPrimeraCuota.Value.Date;
            var plan = new List<CuotaContratoSnapshot>();

            for (var i = 1; i <= credito.CantidadCuotas; i++)
            {
                plan.Add(new CuotaContratoSnapshot
                {
                    NumeroCuota = i,
                    MontoCapital = capitalPorCuota,
                    MontoInteres = interesPorCuota,
                    MontoTotal = montoCuota,
                    FechaVencimiento = fecha
                });
                fecha = fecha.AddMonths(1);
            }

            return plan;
        }

        private static ContratoVentaCreditoSnapshot ConstruirSnapshot(
            DatosContratoContexto datos,
            string numeroContrato,
            string numeroPagare,
            DateTime fechaEmision,
            string usuario)
        {
            var cliente = datos.Cliente;
            var credito = datos.Credito;
            var garante = credito.Garante;
            var garanteCliente = garante?.GaranteCliente;
            var montoCuota = datos.PlanCuotas.FirstOrDefault()?.MontoTotal ?? credito.MontoCuota;
            var totalAPagar = datos.PlanCuotas.Sum(c => c.MontoTotal);
            var primeraCuota = datos.PlanCuotas.OrderBy(c => c.NumeroCuota).First();

            return new ContratoVentaCreditoSnapshot
            {
                Vendedor = new VendedorSnapshot
                {
                    Nombre = datos.Plantilla.NombreVendedor,
                    Domicilio = datos.Plantilla.DomicilioVendedor,
                    DNI = datos.Plantilla.DniVendedor,
                    CUIT = datos.Plantilla.CuitVendedor,
                    CiudadFirma = datos.Plantilla.CiudadFirma,
                    Jurisdiccion = datos.Plantilla.Jurisdiccion,
                    InteresMoraDiarioPorcentaje = datos.Plantilla.InteresMoraDiarioPorcentaje
                },
                Comprador = new CompradorSnapshot
                {
                    NombreCompleto = cliente.ToDisplayName(),
                    DNI = cliente.NumeroDocumento,
                    CUITCUIL = cliente.CuilCuit,
                    Domicilio = cliente.Domicilio,
                    Localidad = cliente.Localidad ?? string.Empty,
                    Telefono = cliente.Telefono
                },
                Garante = garante == null
                    ? null
                    : new GaranteSnapshot
                    {
                        NombreCompleto = garanteCliente?.ToDisplayName() ?? garante.ToDisplayName(),
                        DNI = garanteCliente?.NumeroDocumento ?? garante.NumeroDocumento ?? string.Empty,
                        Domicilio = garanteCliente?.Domicilio ?? garante.Domicilio ?? string.Empty,
                        Relacion = garante.Relacion
                    },
                Venta = new VentaSnapshot
                {
                    Numero = datos.Venta.Numero,
                    Fecha = datos.Venta.FechaVenta,
                    Total = datos.Venta.Total,
                    Productos = datos.Venta.Detalles
                        .Where(d => !d.IsDeleted)
                        .OrderBy(d => d.Id)
                        .Select(d => new ProductoVentaSnapshot
                        {
                            Codigo = d.Producto?.Codigo ?? string.Empty,
                            Nombre = d.Producto?.Nombre ?? string.Empty,
                            Cantidad = d.Cantidad,
                            PrecioUnitario = d.PrecioUnitario,
                            Descuento = d.Descuento,
                            Subtotal = d.Subtotal
                        })
                        .ToList()
                },
                Credito = new CreditoSnapshot
                {
                    Numero = credito.Numero,
                    CantidadCuotas = credito.CantidadCuotas,
                    MontoCuota = montoCuota,
                    TotalAPagar = totalAPagar,
                    FechaPrimeraCuota = primeraCuota.FechaVencimiento,
                    PlanCuotas = datos.PlanCuotas
                },
                Contrato = new ContratoSnapshot
                {
                    Numero = numeroContrato,
                    NumeroPagare = numeroPagare,
                    FechaEmision = fechaEmision
                },
                UsuarioGeneracion = usuario,
                Sucursal = datos.Venta.VendedorUser?.Sucursal,
                Caja = datos.Venta.AperturaCaja?.Caja?.Nombre
            };
        }

        private async Task<string> GenerarNumeroContratoAsync()
        {
            var count = await _context.ContratosVentaCredito.IgnoreQueryFilters().CountAsync();
            return $"CVC-{DateTime.UtcNow:yyyyMM}-{count + 1:D6}";
        }

        private async Task<string> GenerarNumeroPagareAsync()
        {
            var count = await _context.ContratosVentaCredito.IgnoreQueryFilters().CountAsync();
            return $"PAG-{DateTime.UtcNow:yyyyMM}-{count + 1:D6}";
        }

        private byte[] GenerarPdfBytes(ContratoVentaCredito contrato)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var snapshot = JsonSerializer.Deserialize<ContratoVentaCreditoSnapshot>(
                contrato.DatosSnapshotJson,
                SnapshotJsonOptions) ?? throw new InvalidOperationException("El snapshot del contrato es inválido.");

            var variables = CrearVariables(snapshot);
            var textoContrato = ResolverVariables(contrato.TextoContratoSnapshot, variables);
            var textoPagare = ResolverVariables(contrato.TextoPagareSnapshot, variables);

            var documento = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                    page.Header().Element(c => HeaderDocumento(c, "Contrato de Venta", contrato.NumeroContrato));
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text(textoContrato).LineHeight(1.25f);
                        col.Item().PaddingTop(18).Element(FirmasContrato);
                    });
                    page.Footer().Element(FooterDocumento);
                });

                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.6f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken4));

                    page.Header().Element(c => HeaderDocumento(c, "Pagaré", contrato.NumeroPagare));
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Text(textoPagare).LineHeight(1.25f);
                        col.Item().PaddingTop(18).Element(FirmaPagare);
                    });
                    page.Footer().Element(FooterDocumento);
                });
            });

            return documento.GeneratePdf();
        }

        private static Dictionary<string, string> CrearVariables(ContratoVentaCreditoSnapshot snapshot)
        {
            var productos = string.Join(
                Environment.NewLine,
                snapshot.Venta.Productos.Select(p =>
                    $"{p.Cantidad} x {p.Nombre} ({p.Codigo}) - {p.Subtotal:C2}"));

            var planCuotas = string.Join(
                Environment.NewLine,
                snapshot.Credito.PlanCuotas.Select(c =>
                    $"Cuota {c.NumeroCuota}: vence {c.FechaVencimiento:dd/MM/yyyy} - {c.MontoTotal:C2}"));

            var saldoFinanciado = snapshot.Credito.TotalAPagar - (snapshot.Credito.TotalAPagar - snapshot.Credito.MontoCuota * snapshot.Credito.CantidadCuotas);
            var vendedorNombre   = snapshot.Vendedor.Nombre;
            var vendedorDom      = snapshot.Vendedor.Domicilio;
            var vendedorDni      = snapshot.Vendedor.DNI ?? string.Empty;
            var vendedorCuit     = snapshot.Vendedor.CUIT ?? string.Empty;
            var compradorNombre  = snapshot.Comprador.NombreCompleto;
            var compradorDni     = snapshot.Comprador.DNI;
            var compradorCuil    = snapshot.Comprador.CUITCUIL ?? string.Empty;
            var compradorDom     = snapshot.Comprador.Domicilio;
            var compradorLocal   = snapshot.Comprador.Localidad;
            var compradorTel     = snapshot.Comprador.Telefono;
            var garanteNombre    = snapshot.Garante?.NombreCompleto ?? string.Empty;
            var garanteDni       = snapshot.Garante?.DNI ?? string.Empty;
            var garanteDom       = snapshot.Garante?.Domicilio ?? string.Empty;
            var garanteRelacion  = snapshot.Garante?.Relacion ?? string.Empty;
            var ventaNumero      = snapshot.Venta.Numero;
            var ventaFecha       = snapshot.Venta.Fecha.ToString("dd/MM/yyyy");
            var ventaTotal       = snapshot.Venta.Total.ToString("C2");
            var creditoNumero    = snapshot.Credito.Numero;
            var cantCuotas       = snapshot.Credito.CantidadCuotas.ToString();
            var montoCuota       = snapshot.Credito.MontoCuota.ToString("C2");
            var totalPagar       = snapshot.Credito.TotalAPagar.ToString("C2");
            var fechaPrimera     = snapshot.Credito.FechaPrimeraCuota.ToString("dd/MM/yyyy");
            var contratoNum      = snapshot.Contrato.Numero;
            var pagareNum        = snapshot.Contrato.NumeroPagare;
            var fechaEmision     = snapshot.Contrato.FechaEmision.ToString("dd/MM/yyyy HH:mm");
            var usuario          = snapshot.UsuarioGeneracion;
            var sucursal         = snapshot.Sucursal ?? string.Empty;
            var caja             = snapshot.Caja ?? string.Empty;

            return new Dictionary<string, string>
            {
                // Formato legacy (compatibilidad con plantillas existentes)
                ["{{Vendedor.Nombre}}"]          = vendedorNombre,
                ["{{Vendedor.Domicilio}}"]        = vendedorDom,
                ["{{Vendedor.DNI}}"]              = vendedorDni,
                ["{{Vendedor.CUIT}}"]             = vendedorCuit,
                ["{{Comprador.NombreCompleto}}"]  = compradorNombre,
                ["{{Comprador.DNI}}"]             = compradorDni,
                ["{{Comprador.CUITCUIL}}"]        = compradorCuil,
                ["{{Comprador.Domicilio}}"]       = compradorDom,
                ["{{Comprador.Localidad}}"]       = compradorLocal,
                ["{{Comprador.Telefono}}"]        = compradorTel,
                ["{{Garante.NombreCompleto}}"]    = garanteNombre,
                ["{{Garante.DNI}}"]               = garanteDni,
                ["{{Garante.Domicilio}}"]         = garanteDom,
                ["{{Garante.Relacion}}"]          = garanteRelacion,
                ["{{Venta.Numero}}"]              = ventaNumero,
                ["{{Venta.Fecha}}"]               = ventaFecha,
                ["{{Venta.Total}}"]               = ventaTotal,
                ["{{Venta.Productos}}"]           = productos,
                ["{{Credito.Numero}}"]            = creditoNumero,
                ["{{Credito.CantidadCuotas}}"]    = cantCuotas,
                ["{{Credito.MontoCuota}}"]        = montoCuota,
                ["{{Credito.TotalAPagar}}"]       = totalPagar,
                ["{{Credito.FechaPrimeraCuota}}"] = fechaPrimera,
                ["{{Credito.PlanCuotas}}"]        = planCuotas,
                ["{{Contrato.Numero}}"]           = contratoNum,
                ["{{Pagare.Numero}}"]             = pagareNum,
                ["{{Contrato.FechaEmision}}"]     = fechaEmision,
                ["{{UsuarioGeneracion}}"]         = usuario,
                ["{{Sucursal}}"]                  = sucursal,
                ["{{Caja}}"]                      = caja,
                // Formato nuevo con nombres de variable MAYÚSCULAS
                ["{{VENDEDOR_NOMBRE}}"]           = vendedorNombre,
                ["{{VENDEDOR_DOMICILIO}}"]        = vendedorDom,
                ["{{VENDEDOR_DNI}}"]              = vendedorDni,
                ["{{VENDEDOR_CUIT}}"]             = vendedorCuit,
                ["{{COMPRADOR_NOMBRE}}"]          = compradorNombre,
                ["{{COMPRADOR_DNI}}"]             = compradorDni,
                ["{{COMPRADOR_CUIL}}"]            = compradorCuil,
                ["{{COMPRADOR_DOMICILIO}}"]       = compradorDom,
                ["{{COMPRADOR_LOCALIDAD}}"]       = compradorLocal,
                ["{{COMPRADOR_TELEFONO}}"]        = compradorTel,
                ["{{GARANTE_NOMBRE}}"]            = garanteNombre,
                ["{{GARANTE_DNI}}"]               = garanteDni,
                ["{{GARANTE_DOMICILIO}}"]         = garanteDom,
                ["{{GARANTE_RELACION}}"]          = garanteRelacion,
                ["{{NUMERO_CONTRATO}}"]           = contratoNum,
                ["{{NUMERO_PAGARE}}"]             = pagareNum,
                ["{{FECHA_OPERACION}}"]           = ventaFecha,
                ["{{FECHA_HORA_EMISION}}"]        = fechaEmision,
                ["{{PRODUCTOS_DETALLE}}"]         = productos,
                ["{{PRECIO_TOTAL}}"]              = ventaTotal,
                ["{{SALDO_FINANCIADO}}"]          = saldoFinanciado.ToString("C2"),
                ["{{CANTIDAD_CUOTAS}}"]           = cantCuotas,
                ["{{VALOR_CUOTA}}"]               = montoCuota,
                ["{{PLAN_CUOTAS}}"]               = planCuotas,
                ["{{INTERES_MORA}}"]              = snapshot.Vendedor.InteresMoraDiarioPorcentaje.ToString("F4"),
                ["{{USUARIO_GENERACION}}"]        = usuario,
                ["{{SUCURSAL}}"]                  = sucursal,
                ["{{CAJA}}"]                      = caja,
            };
        }

        private static string ResolverVariables(string template, Dictionary<string, string> variables)
        {
            var resultado = template;
            foreach (var variable in variables)
                resultado = resultado.Replace(variable.Key, variable.Value, StringComparison.Ordinal);

            return resultado;
        }

        private static void HeaderDocumento(IContainer container, string titulo, string numero)
        {
            container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingBottom(8).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(titulo).SemiBold().FontSize(18).FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Número: {numero}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(120).AlignRight().Text(DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm"))
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
            });
        }

        private static void FooterDocumento(IContainer container)
        {
            container.BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(6).AlignCenter().Text(text =>
            {
                text.Span("Generado por TheBuryProject - Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        }

        private static void FirmasContrato(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(c => FirmaLinea(c, "Vendedor"));
                row.RelativeItem().Element(c => FirmaLinea(c, "Comprador"));
                row.RelativeItem().Element(c => FirmaLinea(c, "Fiador / Garante"));
            });
        }

        private static void FirmaPagare(IContainer container)
        {
            container.AlignRight().Width(220).Element(c => FirmaLinea(c, "Firma y aclaración"));
        }

        private static void FirmaLinea(IContainer container, string label)
        {
            container.PaddingTop(35).Column(col =>
            {
                col.Item().BorderTop(1).BorderColor(Colors.Grey.Darken2).PaddingTop(4).AlignCenter().Text(label).FontSize(9);
            });
        }

        private string ResolverRutaContrato(string rutaRelativa)
        {
            var root = Path.GetFullPath(_environment.ContentRootPath);
            var fullPath = Path.GetFullPath(Path.Combine(root, rutaRelativa.Replace('/', Path.DirectorySeparatorChar)));

            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La ruta del contrato es inválida.");

            return fullPath;
        }

        private static string CalcularSha256(byte[] bytes)
        {
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars().ToHashSet();
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
                builder.Append(invalid.Contains(ch) ? '_' : ch);

            return builder.ToString();
        }

        private static void AgregarSiVacio(
            string? valor,
            string mensaje,
            ContratoVentaCreditoValidacionResult result)
        {
            if (string.IsNullOrWhiteSpace(valor))
                result.Errores.Add(mensaje);
        }
    }
}
