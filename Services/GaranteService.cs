using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class GaranteService : IGaranteService
    {
        private const int PuntajeMinimoGarante = 4;
        private const int MaxGarantiasActivas = 3;

        private readonly AppDbContext _context;
        private readonly ILogger<GaranteService> _logger;

        public GaranteService(AppDbContext context, ILogger<GaranteService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Validación pura (sin escritura en DB)
        // ─────────────────────────────────────────────────────────────────────

        public async Task<(bool Ok, List<string> Errores)> ValidarGaranteAsync(int clienteId, int garanteClienteId)
        {
            var errores = new List<string>();

            if (garanteClienteId == clienteId)
            {
                errores.Add("El garante no puede ser el mismo cliente.");
                return (false, errores);
            }

            var garante = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == garanteClienteId && !c.IsDeleted);

            if (garante == null)
            {
                errores.Add("El cliente garante no existe en el sistema.");
                return (false, errores);
            }

            if (!garante.Activo)
                errores.Add("El garante no está activo.");

            if (garante.CantidadComprasCliente == 0)
                errores.Add("El garante nunca realizó compras en el sistema.");

            if (garante.PuntajeCliente < PuntajeMinimoGarante)
                errores.Add($"El garante tiene puntaje {garante.PuntajeCliente} (mínimo requerido: {PuntajeMinimoGarante}).");

            var garantiasActivas = await _context.Garantes
                .CountAsync(g => g.GaranteClienteId == garanteClienteId && !g.IsDeleted);

            if (garantiasActivas >= MaxGarantiasActivas)
                errores.Add($"El garante ya garantiza {garantiasActivas} clientes (máximo permitido: {MaxGarantiasActivas}).");

            return (errores.Count == 0, errores);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Asignación
        // ─────────────────────────────────────────────────────────────────────

        public async Task<(bool Ok, string? Error)> AsignarGaranteAsync(
            int clienteId,
            int garanteClienteId,
            string? observacion,
            string usuario)
        {
            var (ok, errores) = await ValidarGaranteAsync(clienteId, garanteClienteId);
            if (!ok)
                return (false, string.Join(" ", errores));

            var cliente = await _context.Clientes
                .Include(c => c.Garante)
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
                return (false, "El cliente no existe.");

            var ahora = DateTime.UtcNow;

            // Dar de baja el garante anterior si existe
            if (cliente.Garante != null && !cliente.Garante.IsDeleted)
            {
                cliente.Garante.IsDeleted = true;
                cliente.Garante.FechaBaja = ahora;
                cliente.Garante.MotivoBaja = "Reemplazado por nuevo garante.";
                cliente.Garante.UpdatedAt = ahora;
                cliente.Garante.UpdatedBy = usuario;
            }

            var nuevoGarante = new Garante
            {
                ClienteId = clienteId,
                GaranteClienteId = garanteClienteId,
                Observaciones = observacion,
                CreatedAt = ahora,
                UpdatedAt = ahora,
                CreatedBy = usuario,
                UpdatedBy = usuario,
                IsDeleted = false
            };

            _context.Garantes.Add(nuevoGarante);
            await _context.SaveChangesAsync();

            cliente.GaranteId = nuevoGarante.Id;
            cliente.UpdatedAt = ahora;
            cliente.UpdatedBy = usuario;
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Garante asignado: ClienteId={ClienteId} ← GaranteClienteId={GaranteClienteId} por {Usuario}",
                clienteId, garanteClienteId, usuario);

            return (true, null);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Remoción
        // ─────────────────────────────────────────────────────────────────────

        public async Task<(bool Ok, string? Error)> RemoverGaranteAsync(int clienteId, string motivo, string usuario)
        {
            var cliente = await _context.Clientes
                .Include(c => c.Garante)
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
                return (false, "El cliente no existe.");

            if (cliente.Garante == null || cliente.Garante.IsDeleted)
                return (false, "El cliente no tiene garante activo asignado.");

            var ahora = DateTime.UtcNow;

            cliente.Garante.IsDeleted = true;
            cliente.Garante.FechaBaja = ahora;
            cliente.Garante.MotivoBaja = motivo;
            cliente.Garante.UpdatedAt = ahora;
            cliente.Garante.UpdatedBy = usuario;

            cliente.GaranteId = null;
            cliente.UpdatedAt = ahora;
            cliente.UpdatedBy = usuario;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Garante removido: ClienteId={ClienteId} por {Usuario}. Motivo: {Motivo}",
                clienteId, usuario, motivo);

            return (true, null);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Info del garante actual
        // ─────────────────────────────────────────────────────────────────────

        public async Task<GaranteInfoViewModel?> ObtenerInfoGaranteAsync(int clienteId)
        {
            var cliente = await _context.Clientes
                .AsNoTracking()
                .Include(c => c.Garante)
                    .ThenInclude(g => g!.GaranteCliente)
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente?.Garante == null || cliente.Garante.IsDeleted || cliente.Garante.GaranteCliente == null)
                return null;

            var g = cliente.Garante;
            var gc = g.GaranteCliente!;

            var garantiasActivas = await _context.Garantes
                .AsNoTracking()
                .CountAsync(r => r.GaranteClienteId == gc.Id && !r.IsDeleted);

            var motivosInvalidez = new List<string>();

            if (!gc.Activo)
                motivosInvalidez.Add("Inactivo.");

            if (gc.CantidadComprasCliente == 0)
                motivosInvalidez.Add("Sin compras previas.");

            if (gc.PuntajeCliente < PuntajeMinimoGarante)
                motivosInvalidez.Add($"Puntaje insuficiente ({gc.PuntajeCliente}/{PuntajeMinimoGarante}).");

            if (garantiasActivas > MaxGarantiasActivas)
                motivosInvalidez.Add($"Garantiza {garantiasActivas} clientes (máx. {MaxGarantiasActivas}).");

            var nombre = !string.IsNullOrWhiteSpace(gc.NombreCompleto)
                ? gc.NombreCompleto
                : gc.ToDisplayName();

            return new GaranteInfoViewModel
            {
                GaranteRegistroId = g.Id,
                GaranteClienteId = gc.Id,
                NombreCompleto = nombre,
                NumeroDocumento = gc.NumeroDocumento,
                PuntajeCliente = gc.PuntajeCliente,
                ClienteActivo = gc.Activo,
                TieneCompras = gc.CantidadComprasCliente > 0,
                CantidadGarantiasActivas = garantiasActivas,
                FechaAlta = g.CreatedAt,
                Observacion = g.Observaciones,
                EsValido = motivosInvalidez.Count == 0,
                MotivosInvalidez = motivosInvalidez
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Búsqueda de candidatos
        // ─────────────────────────────────────────────────────────────────────

        public async Task<List<GaranteCandidatoDto>> BuscarCandidatosAsync(
            string query,
            int clienteIdExcluir,
            int maxResultados = 10)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<GaranteCandidatoDto>();

            var q = query.Trim();

            var clientes = await _context.Clientes
                .AsNoTracking()
                .Where(c =>
                    !c.IsDeleted &&
                    c.Id != clienteIdExcluir &&
                    (c.Apellido.Contains(q) ||
                     c.Nombre.Contains(q) ||
                     (c.NombreCompleto != null && c.NombreCompleto.Contains(q)) ||
                     c.NumeroDocumento.Contains(q)))
                .OrderBy(c => c.Apellido)
                .Take(maxResultados)
                .Select(c => new
                {
                    c.Id,
                    c.Apellido,
                    c.Nombre,
                    c.NombreCompleto,
                    c.NumeroDocumento,
                    c.PuntajeCliente,
                    c.Activo,
                    c.CantidadComprasCliente
                })
                .ToListAsync();

            if (clientes.Count == 0)
                return new List<GaranteCandidatoDto>();

            var ids = clientes.Select(c => c.Id).ToList();

            // Cuenta garantías activas en batch
            var garantiasPorCliente = await _context.Garantes
                .AsNoTracking()
                .Where(g => g.GaranteClienteId.HasValue && ids.Contains(g.GaranteClienteId!.Value) && !g.IsDeleted)
                .GroupBy(g => g.GaranteClienteId!.Value)
                .Select(grp => new { ClienteId = grp.Key, Count = grp.Count() })
                .ToDictionaryAsync(x => x.ClienteId, x => x.Count);

            return clientes.Select(c =>
            {
                var nombre = !string.IsNullOrWhiteSpace(c.NombreCompleto)
                    ? c.NombreCompleto
                    : $"{c.Apellido}, {c.Nombre}";
                garantiasPorCliente.TryGetValue(c.Id, out var garantias);
                return new GaranteCandidatoDto(
                    c.Id,
                    nombre,
                    c.NumeroDocumento,
                    c.PuntajeCliente,
                    c.Activo,
                    c.CantidadComprasCliente,
                    garantias);
            }).ToList();
        }
    }
}
