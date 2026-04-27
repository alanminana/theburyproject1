using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TheBuryProject.Models.Constants;
using TheBuryProject.Data.Seeds;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Data
{
    /// <summary>
    /// Inicializador de base de datos para crear roles, permisos y usuarios por defecto
    /// </summary>
    public static class DbInitializer
    {
        /// <summary>
        /// Inicializa roles, módulos, permisos y usuario administrador
        /// </summary>
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                var context = services.GetRequiredService<AppDbContext>();
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
                var logger = services.GetRequiredService<ILogger<Program>>();

                logger.LogInformation("Iniciando inicialización de base de datos...");

                // Aplicar migraciones pendientes
                // Evitar intentar aplicar migraciones si las tablas ya existen pero falta la tabla __EFMigrationsHistory
                var connection = context.Database.GetDbConnection();
                await connection.OpenAsync();
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT OBJECT_ID(N'dbo.AspNetRoles', N'U')";
                    var objId = await cmd.ExecuteScalarAsync();

                    var hasAspNetRoles = objId != null && objId != DBNull.Value;

                    var shouldMigrate = true;

                    if (hasAspNetRoles)
                    {
                        // Si existen las tablas de Identity, verificar si existe el historial de migraciones
                        try
                        {
                            cmd.CommandText = "SELECT COUNT(*) FROM [__EFMigrationsHistory]";
                            var countObj = await cmd.ExecuteScalarAsync();
                            var count = 0;
                            if (countObj != null && countObj != DBNull.Value)
                                count = Convert.ToInt32(countObj);

                            if (count == 0)
                            {
                                shouldMigrate = false;
                                logger.LogWarning("La base de datos contiene tablas de Identity pero no tiene entradas en __EFMigrationsHistory. Se omitirá ApplyMigrations para evitar conflictos.");
                            }
                        }
                        catch
                        {
                            // Si la consulta falla (por ejemplo, la tabla __EFMigrationsHistory no existe), evitar migrar
                            shouldMigrate = false;
                            logger.LogWarning("No se pudo leer __EFMigrationsHistory. Se omitirá ApplyMigrations para evitar conflictos con tablas existentes.");
                        }
                    }

                    if (shouldMigrate && hasAspNetRoles)
                    {
                        try
                        {
                            var knownMigrations = context.Database.GetMigrations().ToHashSet();
                            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
                            var hasAnyKnown = appliedMigrations.Any(m => knownMigrations.Contains(m));
                            if (!hasAnyKnown)
                            {
                                shouldMigrate = false;
                                logger.LogWarning("El historial de migraciones no corresponde al proyecto actual. Se omitira ApplyMigrations para evitar conflictos.");
                            }
                        }
                        catch
                        {
                            shouldMigrate = false;
                            logger.LogWarning("No se pudo validar el historial de migraciones. Se omitira ApplyMigrations para evitar conflictos.");
                        }
                    }

                    if (shouldMigrate)
                    {
                        try
                        {
                            await context.Database.MigrateAsync();
                            logger.LogInformation("Migraciones aplicadas exitosamente");
                        }
                        catch (SqlException ex) when (ex.Number == 2714)
                        {
                            logger.LogWarning(ex, "Se omitieron migraciones porque ya existen tablas en la base de datos.");
                        }
                    }
                    else
                    {
                        logger.LogInformation("Se omitió la aplicación de migraciones porque ya existen tablas en la base de datos sin historial de migraciones.");
                    }
                }
                finally
                {
                    await connection.CloseAsync();
                }

                // Ejecutar seeder de roles, módulos y permisos
                await RolesPermisosSeeder.SeedAsync(context, roleManager);
                await SucursalesSeeder.SeedAsync(context, logger);
                await EnsureRoleMetadataAsync(context, roleManager, logger);
                logger.LogInformation("Roles, módulos, permisos y sucursales inicializados exitosamente");

                // Crear usuario administrador si no existe (lee credenciales desde configuración/secret)
                await CreateAdminUserAsync(services, logger);

                logger.LogInformation("Inicialización de base de datos completada");
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "Error durante la inicialización de la base de datos");
                throw;
            }
        }

        private static async Task EnsureRoleMetadataAsync(
            AppDbContext context,
            RoleManager<IdentityRole> roleManager,
            ILogger logger)
        {
            var roles = await roleManager.Roles
                .AsNoTracking()
                .ToListAsync();

            var metadatas = await context.RolMetadatas
                .IgnoreQueryFilters()
                .ToDictionaryAsync(m => m.RoleId);

            var changes = false;

            foreach (var role in roles)
            {
                if (!metadatas.TryGetValue(role.Id, out var metadata))
                {
                    context.RolMetadatas.Add(new RolMetadata
                    {
                        RoleId = role.Id,
                        Descripcion = RolMetadataDefaults.GetDescripcion(role.Name),
                        Activo = true,
                        IsDeleted = false
                    });
                    changes = true;
                    continue;
                }

                if (metadata.IsDeleted)
                {
                    metadata.IsDeleted = false;
                    changes = true;
                }

                if (string.IsNullOrWhiteSpace(metadata.Descripcion))
                {
                    metadata.Descripcion = RolMetadataDefaults.GetDescripcion(role.Name);
                    changes = true;
                }
            }

            if (changes)
            {
                await context.SaveChangesAsync();
                logger.LogInformation("Metadata de roles sincronizada correctamente");
            }
        }

        /// <summary>
            /// Crea el usuario administrador por defecto con rol SuperAdmin.
        /// Lee credenciales desde IConfiguration: "Admin:Email" y "Admin:Password".
        /// En desarrollo, si no están definidas, se puede usar la contraseña por defecto (sólo dev).
        /// Nunca registra la contraseña en logs.
        /// </summary>
        private static async Task CreateAdminUserAsync(IServiceProvider services, ILogger logger)
        {
            var configuration = services.GetRequiredService<IConfiguration>();
            var env = services.GetService<IWebHostEnvironment>();
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var context = services.GetRequiredService<AppDbContext>();

            var adminEmail = configuration["Admin:Email"] ?? "admin@thebury.com";
            var adminUserName = configuration["Admin:UserName"] ?? "admin";
            var adminPassword = configuration["Admin:Password"]; // debe venir de user-secrets / ENV
            var sucursalDefault = await context.Sucursales
                .AsNoTracking()
                .Where(s => s.Activa)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync(s => s.Nombre == SucursalesSeeder.GetDefaultSucursalName())
                ?? await context.Sucursales
                    .AsNoTracking()
                    .Where(s => s.Activa)
                    .OrderBy(s => s.Id)
                    .FirstOrDefaultAsync();

            // Fallback seguro: permitir el valor por defecto sólo en entorno Development
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                if (env != null && env.IsDevelopment())
                {
                    adminPassword = "Admin123!"; // único fallback para desarrollo
                    logger.LogWarning("No se encontró Admin:Password en configuración. Usando contraseña por defecto SOLO en Development.");
                }
                else
                {
                    logger.LogWarning("No se creó el usuario administrador automático: 'Admin:Password' no está configurado en la configuración/variables de entorno.");
                    logger.LogWarning("Configure 'Admin:Password' mediante user-secrets o variable de entorno antes de ejecutar en producción.");
                    return;
                }
            }

            var adminUser = await userManager.FindByNameAsync(adminUserName)
                ?? await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = adminUserName,
                    Email = adminEmail,
                    EmailConfirmed = true,
                    Activo = true,
                    FechaCreacion = DateTime.UtcNow,
                    SucursalId = sucursalDefault?.Id,
                    Sucursal = sucursalDefault?.Nombre
                };

                var result = await userManager.CreateAsync(adminUser, adminPassword);

                if (result.Succeeded)
                {
                    logger.LogInformation("Usuario administrador creado: {UserName} ({Email})", adminUserName, adminEmail);

                    // Asignar rol SuperAdmin
                    await userManager.AddToRoleAsync(adminUser, Roles.SuperAdmin);
                    logger.LogInformation("Rol 'SuperAdmin' asignado al usuario {UserName}", adminUserName);

                    logger.LogWarning("⚠️ Credenciales provisionales creadas. Cambiar la contraseña inmediatamente si es necesario.");
                }
                else
                {
                    logger.LogError("Error al crear usuario administrador: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                logger.LogInformation("Usuario administrador ya existe: {UserName} ({Email})", adminUser.UserName, adminEmail);

                var adminUpdated = false;
                if (!string.Equals(adminUser.UserName, adminUserName, StringComparison.OrdinalIgnoreCase))
                {
                    var userNameOwner = await userManager.FindByNameAsync(adminUserName);
                    if (userNameOwner == null || userNameOwner.Id == adminUser.Id)
                    {
                        adminUser.UserName = adminUserName;
                        adminUpdated = true;
                    }
                    else
                    {
                        logger.LogWarning("No se pudo sincronizar el usuario administrador a '{UserName}' porque ya existe otro usuario con ese nombre.", adminUserName);
                    }
                }
                if (sucursalDefault != null &&
                    (adminUser.SucursalId != sucursalDefault.Id ||
                     !string.Equals(adminUser.Sucursal, sucursalDefault.Nombre, StringComparison.Ordinal)))
                {
                    adminUser.SucursalId = sucursalDefault.Id;
                    adminUser.Sucursal = sucursalDefault.Nombre;
                    adminUpdated = true;
                }

                // Verificar que tenga el rol SuperAdmin
                if (!await userManager.IsInRoleAsync(adminUser, Roles.SuperAdmin))
                {
                    await userManager.AddToRoleAsync(adminUser, Roles.SuperAdmin);
                    logger.LogInformation("Rol 'SuperAdmin' asignado al usuario existente {Email}", adminEmail);
                }

                if (adminUpdated)
                {
                    await userManager.UpdateAsync(adminUser);
                    logger.LogInformation("Sucursal base sincronizada para usuario administrador {Email}", adminEmail);
                }
            }
        }

        /// <summary>
        /// Crea usuarios de prueba para cada rol (solo en desarrollo)
        /// </summary>
        public static async Task CreateTestUsersAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var services = scope.ServiceProvider;
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var context = services.GetRequiredService<AppDbContext>();
            var logger = services.GetRequiredService<ILogger<Program>>();
            var sucursalDefault = await context.Sucursales
                .AsNoTracking()
                .Where(s => s.Activa)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync(s => s.Nombre == SucursalesSeeder.GetDefaultSucursalName())
                ?? await context.Sucursales
                    .AsNoTracking()
                    .Where(s => s.Activa)
                    .OrderBy(s => s.Id)
                    .FirstOrDefaultAsync();

            var testUsers = new[]
            {
                new { UserName = "administrador", Email = "administrador@thebury.com", Password = "Admin123!", Role = Roles.Administrador },
                new { UserName = "gerente", Email = "gerente@thebury.com", Password = "Gerente123!", Role = Roles.Gerente },
                new { UserName = "vendedor", Email = "vendedor@thebury.com", Password = "Vendedor123!", Role = Roles.Vendedor },
                new { UserName = "cajero", Email = "cajero@thebury.com", Password = "Cajero123!", Role = Roles.Cajero },
                new { UserName = "repositor", Email = "repositor@thebury.com", Password = "Repositor123!", Role = Roles.Repositor },
                new { UserName = "tecnico", Email = "tecnico@thebury.com", Password = "Tecnico123!", Role = Roles.Tecnico },
                new { UserName = "contador", Email = "contador@thebury.com", Password = "Contador123!", Role = Roles.Contador }
            };

            foreach (var testUser in testUsers)
            {
                var user = await userManager.FindByNameAsync(testUser.UserName)
                    ?? await userManager.FindByEmailAsync(testUser.Email);
                if (user == null)
                {
                    user = new ApplicationUser
                    {
                        UserName = testUser.UserName,
                        Email = testUser.Email,
                        EmailConfirmed = true,
                        Activo = true,
                        FechaCreacion = DateTime.UtcNow,
                        SucursalId = sucursalDefault?.Id,
                        Sucursal = sucursalDefault?.Nombre
                    };

                    var result = await userManager.CreateAsync(user, testUser.Password);
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(user, testUser.Role);
                        logger.LogInformation("Usuario de prueba creado: {UserName} ({Email}) con rol {Role}",
                            testUser.UserName, testUser.Email, testUser.Role);
                    }
                }
                else
                {
                    var userUpdated = false;
                    if (!string.Equals(user.UserName, testUser.UserName, StringComparison.OrdinalIgnoreCase))
                    {
                        var userNameOwner = await userManager.FindByNameAsync(testUser.UserName);
                        if (userNameOwner == null || userNameOwner.Id == user.Id)
                        {
                            user.UserName = testUser.UserName;
                            userUpdated = true;
                        }
                    }

                    if (sucursalDefault != null &&
                        (user.SucursalId != sucursalDefault.Id ||
                         !string.Equals(user.Sucursal, sucursalDefault.Nombre, StringComparison.Ordinal)))
                    {
                        user.SucursalId = sucursalDefault.Id;
                        user.Sucursal = sucursalDefault.Nombre;
                        userUpdated = true;
                    }

                    if (userUpdated)
                    {
                        await userManager.UpdateAsync(user);
                    }
                }
            }

            logger.LogWarning("Usuarios de prueba creados - ⚠️ SOLO PARA DESARROLLO");
        }
    }
}
