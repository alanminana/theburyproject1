using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Gestiona el padrón de vendedores por caja. Es un registro de control/visibilidad:
    /// no restringe ventas (sin enforcement).
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

            // Validar que cada usuario seleccionado tenga rol Vendedor.
            if (seleccion.Count > 0)
            {
                var vendedoresValidos = (await _usuarioService.GetUsuariosPorRolAsync(Roles.Vendedor))
                    .Select(v => v.Id)
                    .ToHashSet();

                if (seleccion.Any(id => !vendedoresValidos.Contains(id)))
                {
                    throw new InvalidOperationException("Uno o más usuarios seleccionados no tienen el rol de vendedor.");
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
