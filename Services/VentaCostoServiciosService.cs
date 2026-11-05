using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>Costo de un servicio (entrega o armado de una línea) de una venta: precio cobrado, estándar, real y aplicado.</summary>
    public sealed record VentaCostoServicioLinea(
        int? DetalleId,
        string Descripcion,
        decimal PrecioCobrado,
        decimal CostoEstandar,
        decimal? CostoReal,
        string RowVersionBase64 = "")
    {
        /// <summary>El real, si existe, reemplaza al estándar: nunca se descuentan ambos.</summary>
        public decimal CostoAplicado => CostoReal ?? CostoEstandar;
        public bool UsaCostoReal => CostoReal.HasValue;
    }

    public sealed record VentaCostoServiciosInfo(
        int VentaId,
        string NumeroVenta,
        EstadoVenta Estado,
        bool Editable,
        VentaCostoServicioLinea? Envio,
        IReadOnlyList<VentaCostoServicioLinea> Armados)
    {
        public bool TieneServicios => Envio != null || Armados.Count > 0;
        public decimal TotalCobrado => (Envio?.PrecioCobrado ?? 0m) + Armados.Sum(a => a.PrecioCobrado);
        public decimal TotalCosto => (Envio?.CostoAplicado ?? 0m) + Armados.Sum(a => a.CostoAplicado);
    }

    /// <summary>Costos reales cargados por el usuario. null = volver al estándar.</summary>
    public sealed record VentaCostoServiciosComando(
        decimal? CostoRealEnvio,
        IReadOnlyDictionary<int, decimal?> CostoRealArmadoPorDetalle,
        string? EnvioRowVersionBase64 = null,
        IReadOnlyDictionary<int, string>? DetalleRowVersionsBase64 = null);

    public interface IVentaCostoServiciosService
    {
        Task<VentaCostoServiciosInfo?> ObtenerAsync(int ventaId);
        Task GuardarAsync(int ventaId, VentaCostoServiciosComando comando, string? usuario);
    }

    public sealed class VentaCostoServiciosService : IVentaCostoServiciosService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<VentaCostoServiciosService> _logger;

        public VentaCostoServiciosService(AppDbContext context, ILogger<VentaCostoServiciosService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<VentaCostoServiciosInfo?> ObtenerAsync(int ventaId)
        {
            var venta = await _context.Ventas
                .AsNoTracking()
                .Include(v => v.Envio)
                .Include(v => v.Detalles.Where(d => !d.IsDeleted)).ThenInclude(d => d.Producto)
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted);

            if (venta == null)
                return null;

            VentaCostoServicioLinea? envio = null;
            if (venta.Envio is { IsDeleted: false } e)
            {
                envio = new VentaCostoServicioLinea(
                    null,
                    string.IsNullOrWhiteSpace(e.TipoEnvio?.NombreVisible()) ? "Envío" : e.TipoEnvio!.Value.NombreVisible(),
                    e.IncluidoEnTotal ? VentaMontos.NormalizarImporteEnvio(e.CostoEnvio) : 0m,
                    e.CostoEstandarServicio ?? 0m,
                    e.CostoRealServicio,
                    Convert.ToBase64String(e.RowVersion ?? Array.Empty<byte>()));
            }

            var armados = venta.Detalles
                .Where(d => d.TipoArmado.HasValue && !d.EntregaCajaCerrada)
                .Select(d => new VentaCostoServicioLinea(
                    d.Id,
                    $"{d.TipoArmado!.Value.NombreVisible()} · {d.Producto?.Nombre ?? "Producto"} × {d.Cantidad}",
                    d.ArmadoSubtotal,
                    Math.Round(d.ArmadoCostoUnitarioEstandar * d.Cantidad, 2, MidpointRounding.AwayFromZero),
                    d.ArmadoCostoReal,
                    Convert.ToBase64String(d.RowVersion ?? Array.Empty<byte>())))
                .ToList();

            return new VentaCostoServiciosInfo(
                venta.Id, venta.Numero, venta.Estado, PermiteEditar(venta.Estado), envio, armados);
        }

        public async Task GuardarAsync(int ventaId, VentaCostoServiciosComando comando, string? usuario)
        {
            ArgumentNullException.ThrowIfNull(comando);

            var venta = await _context.Ventas
                .Include(v => v.Envio)
                .Include(v => v.Detalles.Where(d => !d.IsDeleted))
                .FirstOrDefaultAsync(v => v.Id == ventaId && !v.IsDeleted)
                ?? throw new InvalidOperationException("La venta no existe.");

            if (!PermiteEditar(venta.Estado))
                throw new InvalidOperationException("No se puede modificar el costo de servicios de una venta cancelada.");

            // Validación completa ANTES de mutar: un valor inválido no deja cambios parciales.
            if (comando.CostoRealEnvio.HasValue)
            {
                if (venta.Envio is not { IsDeleted: false })
                    throw new InvalidOperationException("La venta no tiene envío: no hay costo de entrega para cargar.");
                ValidarImporte(comando.CostoRealEnvio.Value, "El costo real de entrega");
            }

            var lineasConArmado = venta.Detalles
                .Where(d => d.TipoArmado.HasValue && !d.EntregaCajaCerrada)
                .ToDictionary(d => d.Id);

            foreach (var (detalleId, costo) in comando.CostoRealArmadoPorDetalle)
            {
                if (!lineasConArmado.ContainsKey(detalleId))
                    throw new InvalidOperationException("Una de las líneas no lleva armado o no pertenece a la venta.");
                if (costo.HasValue)
                    ValidarImporte(costo.Value, "El costo real del armado");
            }

            // Control de formulario obsoleto: cada fila enviada debe traer la versión que el usuario vio. Si otra persona
            // cargó un costo o cambió el envío desde entonces, se rechaza todo sin guardar nada.
            if (venta.Envio is { IsDeleted: false } envioVigente)
                VerificarVersion(envioVigente.RowVersion, comando.EnvioRowVersionBase64);
            foreach (var (detalleId, detalleVigente) in lineasConArmado)
            {
                string? esperada = null;
                comando.DetalleRowVersionsBase64?.TryGetValue(detalleId, out esperada);
                VerificarVersion(detalleVigente.RowVersion, esperada);
            }

            var cambios = new List<string>();

            if (venta.Envio is { IsDeleted: false } envio)
            {
                var nuevo = comando.CostoRealEnvio.HasValue ? Redondear(comando.CostoRealEnvio.Value) : (decimal?)null;
                if (envio.CostoRealServicio != nuevo)
                {
                    cambios.Add($"entrega {Fmt(envio.CostoRealServicio)} -> {Fmt(nuevo)}");
                    envio.CostoRealServicio = nuevo;
                }
            }

            foreach (var (detalleId, detalle) in lineasConArmado)
            {
                // Línea ausente del comando = sin costo real (vuelve al estándar), igual que un valor vacío.
                comando.CostoRealArmadoPorDetalle.TryGetValue(detalleId, out var valor);
                var nuevo = valor.HasValue ? Redondear(valor.Value) : (decimal?)null;
                if (detalle.ArmadoCostoReal != nuevo)
                {
                    cambios.Add($"armado línea {detalleId} {Fmt(detalle.ArmadoCostoReal)} -> {Fmt(nuevo)}");
                    detalle.ArmadoCostoReal = nuevo;
                }
            }

            if (cambios.Count == 0)
                return;

            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Costo real de servicios de la venta {VentaId} ({Numero}) actualizado por {Usuario}: {Cambios}",
                venta.Id, venta.Numero, usuario ?? "desconocido", string.Join("; ", cambios));
        }

        private static bool PermiteEditar(EstadoVenta estado) => estado != EstadoVenta.Cancelada;

        private static void VerificarVersion(byte[]? actual, string? esperadaBase64)
        {
            byte[]? esperada = null;
            if (!string.IsNullOrEmpty(esperadaBase64))
            {
                try { esperada = Convert.FromBase64String(esperadaBase64); }
                catch (FormatException) { /* se trata como versión ausente */ }
            }

            if (esperada is not { Length: > 0 } || actual is null || !actual.AsSpan().SequenceEqual(esperada))
                throw new InvalidOperationException(
                    "Los costos cambiaron desde que abriste la pantalla (o el formulario es inválido). Recargá la venta y volvé a intentar.");
        }

        private static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

        private static string Fmt(decimal? valor) => valor.HasValue ? valor.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "estándar";

        private static void ValidarImporte(decimal valor, string nombre)
        {
            if (valor < 0m)
                throw new InvalidOperationException($"{nombre} no puede ser negativo.");
            if (valor > Validation.MontoArgentinoAttribute.Maximo)
                throw new InvalidOperationException($"{nombre} no puede superar 20.000.000.");
        }
    }
}
