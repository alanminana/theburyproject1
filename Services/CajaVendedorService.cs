using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Gestiona el padrón de usuarios habilitados por caja (vendedores que venden en ella
    /// y cajeros que la operan). Es la base del enforcement "cada usuario sobre su propia caja".
    /// Nota: por estabilidad de migración la tabla/columna conservan el nombre histórico
    /// (CajaVendedores / VendedorUserId) pero almacenan cualquier usuario habilitado.
    /// </summary>
    public class CajaVendedorService : ICajaVendedorService
    {
        private readonly AppDbContext _context;
        private readonly IUsuarioService _usuarioService;
        private readonly ILogger<CajaVendedorService> _logger;

        public CajaVendedorService(
            AppDbContext context,
            IUsuarioService usuarioService,
            ILogger<CajaVendedorService> logger)
        {
            _context = context;
            _usuarioService = usuarioService;
            _logger = logger;
        }

        public Task<List<UsuarioSelectItem>> ObtenerVendedoresDisponiblesAsync()
            => _usuarioService.GetUsuariosPorRolAsync(Roles.Vendedor);

        public Task<List<UsuarioSelectItem>> ObtenerCajerosDisponiblesAsync()
            => _usuarioService.GetUsuariosPorRolAsync(Roles.Cajero);

        public async Task<bool> EsMiembroAsync(int cajaId, string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            return await _context.CajaVendedores
                .AsNoTracking()
                .AnyAsync(cv => cv.CajaId == cajaId && cv.VendedorUserId == userId && !cv.IsDeleted);
        }

        public async Task<List<int>> ObtenerCajaIdsDeUsuarioAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return new List<int>();
            }

            return await _context.CajaVendedores
                .AsNoTracking()
                .Where(cv => cv.VendedorUserId == userId && !cv.IsDeleted)
                .Select(cv => cv.CajaId)
                .Distinct()
                .ToListAsync();
        }

        public async Task<List<string>> ObtenerVendedorIdsAsignadosAsync(int cajaId)
        {
            return await _context.CajaVendedores
                .AsNoTracking()
                .Where(cv => cv.CajaId == cajaId && !cv.IsDeleted)
                .Select(cv => cv.VendedorUserId)
                .ToListAsync();
        }

        public async Task<List<UsuarioSelectItem>> ObtenerVendedoresAsignadosAsync(int cajaId)
        {
            return await _context.CajaVendedores
                .AsNoTracking()
                .Where(cv => cv.CajaId == cajaId && !cv.IsDeleted)
                .Join(_context.Users,
                      cv => cv.VendedorUserId,
                      u => u.Id,
                      (cv, u) => new UsuarioSelectItem(u.Id, u.UserName ?? u.Email ?? ""))
                .OrderBy(x => x.UserName)
                .ToListAsync();
        }

        public async Task AsignarVendedoresAsync(int cajaId, IEnumerable<string> vendedorUserIds, string usuario)
        {
            var cajaExiste = await _context.Cajas.AnyAsync(c => c.Id == cajaId && !c.IsDeleted);
            if (!cajaExiste)
            {
                throw new InvalidOperationException("Caja no encontrada");
            }

            var seleccion = (vendedorUserIds ?? Enumerable.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            // Validar que cada usuario seleccionado tenga rol Vendedor o Cajero (usuarios habilitables).
            if (seleccion.Count > 0)
            {
                var vendedores = await _usuarioService.GetUsuariosPorRolAsync(Roles.Vendedor);
                var cajeros = await _usuarioService.GetUsuariosPorRolAsync(Roles.Cajero);
                var habilitables = vendedores.Concat(cajeros).Select(u => u.Id).ToHashSet();

                if (seleccion.Any(id => !habilitables.Contains(id)))
                {
                    throw new InvalidOperationException("Uno o más usuarios seleccionados no tienen rol de vendedor ni cajero.");
                }
            }

            var existentes = await _context.CajaVendedores
                .Where(cv => cv.CajaId == cajaId)
                .ToListAsync();

            var aEliminar = existentes
                .Where(cv => !seleccion.Contains(cv.VendedorUserId))
                .ToList();
            if (aEliminar.Count > 0)
            {
                _context.CajaVendedores.RemoveRange(aEliminar);
            }

            var actuales = existentes.Select(cv => cv.VendedorUserId).ToHashSet();
            foreach (var vendedorId in seleccion.Where(id => !actuales.Contains(id)))
            {
                _context.CajaVendedores.Add(new CajaVendedor
                {
                    CajaId = cajaId,
                    VendedorUserId = vendedorId,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = usuario
                });
            }

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Caja {CajaId}: padrón de vendedores actualizado por {Usuario}. Total asignados: {Count}",
                cajaId, usuario, seleccion.Count);
        }
    }
}
