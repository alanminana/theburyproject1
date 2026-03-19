using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Data.Seeds;

public static class SucursalesSeeder
{
    private const string SucursalDefault = "Casa Central";

    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        var sucursal = await context.Sucursales
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Nombre == SucursalDefault);

        if (sucursal == null)
        {
            context.Sucursales.Add(new Sucursal
            {
                Nombre = SucursalDefault,
                Activa = true,
                CreatedBy = "DbInitializer",
                IsDeleted = false
            });

            await context.SaveChangesAsync();
            logger.LogInformation("Sucursal base creada: {Sucursal}", SucursalDefault);
            return;
        }

        var changed = false;
        if (!sucursal.Activa)
        {
            sucursal.Activa = true;
            changed = true;
        }

        if (sucursal.IsDeleted)
        {
            sucursal.IsDeleted = false;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Sucursal base reactivada: {Sucursal}", SucursalDefault);
        }
    }

    public static string GetDefaultSucursalName() => SucursalDefault;
}
