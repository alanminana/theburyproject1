using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Helpers
{
    public static class AppDbContextExtensions
    {
        public static Task<List<SucursalOptionViewModel>> GetSucursalOptionsAsync(this AppDbContext context)
        {
            return context.Sucursales
                .AsNoTracking()
                .Where(s => s.Activa)
                .OrderBy(s => s.Nombre)
                .Select(s => new SucursalOptionViewModel
                {
                    Id = s.Id,
                    Nombre = s.Nombre
                })
                .ToListAsync();
        }

        public static async Task<Sucursal?> GetSucursalAsync(this AppDbContext context, int? sucursalId)
        {
            if (!sucursalId.HasValue)
            {
                return null;
            }

            return await context.Sucursales
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sucursalId.Value && s.Activa);
        }
    }
}
