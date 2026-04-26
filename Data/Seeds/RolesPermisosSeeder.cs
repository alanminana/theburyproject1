// Data/Seeds/RolesPermisosSeeder.cs
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Data.Seeds;

/// <summary>
/// Seeder para roles, módulos, acciones y permisos iniciales del sistema.
/// Soporta soft-delete (revive) usando IgnoreQueryFilters y canoniza claves/claimValue.
/// </summary>
public static class RolesPermisosSeeder
{
    public static async Task SeedAsync(AppDbContext context, RoleManager<IdentityRole> roleManager)
    {
        await SeedRolesAsync(roleManager);
        await SeedModulosYAccionesAsync(context);
        await SeedPermisosAsync(context);
    }

    private static string Canon(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string ClaimValue(string moduloClave, string accionClave)
        => $"{Canon(moduloClave)}.{Canon(accionClave)}";

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        var roles = Models.Constants.Roles.GetAllRoles();

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
                await roleManager.CreateAsync(new IdentityRole(roleName));
        }
    }

    private static async Task SeedModulosYAccionesAsync(AppDbContext context)
    {
        var modulosData = new List<(string Nombre, string Clave, string Categoria, string Icono, int Orden, List<(string Nombre, string Clave, int Orden)> Acciones)>
        {
            // CATÁLOGO
            ("Productos", "productos", "Catálogo", "bi-box-seam", 1, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4),
                ("Exportar", "export", 5)
            }),
            ("Categorías", "categorias", "Catálogo", "bi-tags", 2, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4)
            }),
            ("Marcas", "marcas", "Catálogo", "bi-award", 3, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4)
            }),
            ("Precios", "precios", "Catálogo", "bi-currency-dollar", 4, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Simular Cambio", "simulate", 2),
                ("Aprobar Cambio", "approve", 3),
                ("Aplicar Cambio", "apply", 4),
                ("Revertir Cambio", "revert", 5),
                ("Crear Lista", "create", 6),
                ("Editar Lista", "update", 7),
                ("Eliminar Lista", "delete", 8),
                ("Ver Historial", "history", 9)
            }),

            // CLIENTES
            ("Clientes", "clientes", "Clientes", "bi-people", 10, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4),
                ("Ver Documentos", "viewdocs", 5),
                ("Subir Documentos", "uploaddocs", 6),
                ("Exportar", "export", 7),
                ("Administrar límites de crédito por puntaje", "managecreditlimits", 8)
            }),
            ("Evaluación Crédito", "evaluacioncredito", "Clientes", "bi-clipboard-check", 11, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Evaluar", "evaluate", 2),
                ("Aprobar", "approve", 3),
                ("Rechazar", "reject", 4)
            }),

            // VENTAS
            ("Ventas", "ventas", "Ventas", "bi-cart", 20, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4),
                ("Autorizar", "authorize", 5),
                ("Rechazar", "reject", 6),
                ("Facturar", "invoice", 7),
                ("Cancelar", "cancel", 8),
                ("Exportar", "export", 9)
            }),
            ("Cotizaciones", "cotizaciones", "Ventas", "bi-file-text", 21, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Convertir a Venta", "convert", 4),
                ("Anular", "cancel", 5)
            }),
            ("Créditos", "creditos", "Ventas", "bi-credit-card-2-front", 22, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Simular", "simulate", 3),
                ("Aprobar", "approve", 4),
                ("Ver Cuotas", "viewinstallments", 5),
                ("Reprogramar", "reschedule", 6)
            }),
            ("Cobranzas", "cobranzas", "Ventas", "bi-cash-stack", 23, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Pagar Cuota", "payinstallment", 2),
                ("Ver Moras", "viewarrears", 3),
                ("Aplicar Punitorio", "applyfine", 4),
                ("Ver Alertas", "viewalerts", 5)
            }),

            // COMPRAS
            ("Proveedores", "proveedores", "Compras", "bi-building", 30, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4)
            }),
            ("Órdenes de Compra", "ordenescompra", "Compras", "bi-clipboard-data", 31, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Recepcionar", "receive", 4),
                ("Cancelar", "cancel", 5)
            }),
            ("Cheques", "cheques", "Compras", "bi-file-earmark-text", 32, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Depositar", "deposit", 4),
                ("Anular", "cancel", 5)
            }),

            // STOCK
            ("Stock", "stock", "Stock", "bi-boxes", 40, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Ver Kardex", "viewkardex", 2),
                ("Ajustar", "adjust", 3),
                ("Transferir", "transfer", 4),
                ("Ver Alertas", "viewalerts", 5)
            }),
            ("Movimientos", "movimientos", "Stock", "bi-arrow-left-right", 41, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Registrar", "create", 2)
            }),

            // DEVOLUCIONES
            ("Devoluciones", "devoluciones", "Devoluciones", "bi-arrow-return-left", 50, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Aprobar", "approve", 3),
                ("Rechazar", "reject", 4),
                ("Completar", "complete", 5)
            }),
            ("Garantías", "garantias", "Devoluciones", "bi-shield-check", 51, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Actualizar", "update", 3)
            }),
            ("RMAs", "rmas", "Devoluciones", "bi-truck", 52, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Actualizar Estado", "updatestatus", 3)
            }),
            ("Notas de Crédito", "notascredito", "Devoluciones", "bi-file-earmark-check", 53, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Aplicar", "apply", 2),
                ("Cancelar", "cancel", 3)
            }),

            // CAJA
            ("Caja", "caja", "Caja", "bi-cash-coin", 60, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Abrir", "open", 2),
                ("Cerrar", "close", 3),
                ("Movimientos", "movements", 4),
                ("Ver Historial", "history", 5)
            }),

            // AUTORIZACIONES
            ("Autorizaciones", AutorizacionesConstants.Modulo, "Autorizaciones", "bi-check2-circle", 70, new List<(string, string, int)>
            {
                ("Ver", AutorizacionesConstants.Acciones.Ver, 1),
                ("Aprobar", AutorizacionesConstants.Acciones.Aprobar, 2),
                ("Rechazar", AutorizacionesConstants.Acciones.Rechazar, 3),
                ("Gestionar Umbrales", AutorizacionesConstants.Acciones.GestionarUmbrales, 4)
            }),

            // REPORTES
            ("Reportes", "reportes", "Reportes", "bi-graph-up", 80, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Ventas", "sales", 2),
                ("Márgenes", "margins", 3),
                ("Morosidad", "arrears", 4),
                ("Stock", "stock", 5),
                ("Exportar", "export", 6)
            }),
            ("Dashboard", "dashboard", "Reportes", "bi-speedometer2", 81, new List<(string, string, int)>
            {
                ("Ver", "view", 1)
            }),
            ("Notificaciones", "notificaciones", "Sistema", "bi-bell", 82, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Marcar Leidas", "update", 2),
                ("Eliminar", "delete", 3)
            }),

            // CONFIGURACIÓN
            ("Acciones", "acciones", "Configuración", "bi-list-check", 90, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4)
            }),
            ("Módulos", "modulos", "Configuración", "bi-diagram-3", 91, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4)
            }),
            ("Configuración", "configuracion", "Configuración", "bi-gear", 92, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Editar", "update", 2)
            }),
            ("Usuarios", "usuarios", "Configuración", "bi-person", 93, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4),
                ("Asignar Roles", "assignroles", 5),
                ("Resetear Contraseña", "resetpassword", 6)
            }),
            ("Roles", "roles", "Configuración", "bi-shield", 94, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Eliminar", "delete", 4),
                ("Asignar Permisos", "assignpermissions", 5)
            }),

            // TICKETS INTERNOS
            ("Tickets", "tickets", "Sistema", "bi-ticket-perforated", 100, new List<(string, string, int)>
            {
                ("Ver", "view", 1),
                ("Crear", "create", 2),
                ("Editar", "update", 3),
                ("Cambiar Estado", "changestatus", 4),
                ("Resolver", "resolve", 5),
                ("Eliminar", "delete", 6)
            })
        };

        foreach (var (nombre, claveRaw, categoria, icono, orden, acciones) in modulosData)
        {
            var clave = Canon(claveRaw);

            var modulo = await context.ModulosSistema
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Clave == clave);

            var isNewModulo = false;

            if (modulo == null)
            {
                modulo = new ModuloSistema
                {
                    Nombre = nombre,
                    Clave = clave,
                    Categoria = categoria,
                    Icono = icono,
                    Orden = orden,
                    Activo = true,
                    IsDeleted = false
                };
                context.ModulosSistema.Add(modulo);
                isNewModulo = true;
            }
            else
            {
                // revive + upsert metadata
                modulo.Nombre = nombre;
                modulo.Clave = clave;
                modulo.Categoria = categoria;
                modulo.Icono = icono;
                modulo.Orden = orden;
                modulo.Activo = true;
                modulo.IsDeleted = false;
            }

            // Para módulos nuevos, no puede haber acciones existentes.
            Dictionary<string, AccionModulo>? accionesExistentes = null;

            if (!isNewModulo)
            {
                var list = await context.AccionesModulo
                    .IgnoreQueryFilters()
                    .Where(a => a.ModuloId == modulo.Id)
                    .ToListAsync();

                accionesExistentes = list.ToDictionary(a => Canon(a.Clave), a => a);
            }

            foreach (var (nombreAccion, claveAccionRaw, ordenAccion) in acciones)
            {
                var claveAccion = Canon(claveAccionRaw);

                if (isNewModulo)
                {
                    context.AccionesModulo.Add(new AccionModulo
                    {
                        Modulo = modulo,
                        Nombre = nombreAccion,
                        Clave = claveAccion,
                        Orden = ordenAccion,
                        Activa = true,
                        IsDeleted = false
                    });
                    continue;
                }

                if (accionesExistentes != null && accionesExistentes.TryGetValue(claveAccion, out var accionExistente))
                {
                    // revive + upsert metadata
                    accionExistente.Nombre = nombreAccion;
                    accionExistente.Clave = claveAccion;
                    accionExistente.Orden = ordenAccion;
                    accionExistente.Activa = true;
                    accionExistente.IsDeleted = false;
                }
                else
                {
                    context.AccionesModulo.Add(new AccionModulo
                    {
                        ModuloId = modulo.Id,
                        Nombre = nombreAccion,
                        Clave = claveAccion,
                        Orden = ordenAccion,
                        Activa = true,
                        IsDeleted = false
                    });
                }
            }
        }

        await context.SaveChangesAsync();
    }

    private static async Task SeedPermisosAsync(AppDbContext context)
    {
        var roles = await context.Roles.AsNoTracking().ToListAsync();
        var modulos = await context.ModulosSistema
            .Include(m => m.Acciones)
            .AsNoTracking()
            .ToListAsync();

        var superAdminRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.SuperAdmin);
        if (superAdminRole != null)
            await AsignarTodosLosPermisosAsync(context, superAdminRole.Id, modulos);

        var adminRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Administrador);
        if (adminRole != null)
            await AsignarTodosLosPermisosAsync(context, adminRole.Id, modulos, exceptoAcciones: new[] { "usuarios.delete", "roles.delete", "configuracion.update" });

        var gerenteRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Gerente);
        if (gerenteRole != null)
        {
            var modulosGerente = new[]
            {
                "ventas", "cotizaciones", "creditos", "cobranzas", "clientes", "evaluacioncredito",
                "proveedores", "ordenescompra", "stock", "movimientos",
                "devoluciones", "garantias", "rmas", "notascredito",
                AutorizacionesConstants.Modulo,
                "reportes", "dashboard", "notificaciones"
            };
            await AsignarPermisosModulosAsync(context, gerenteRole.Id, modulos, modulosGerente);
        }

        var vendedorRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Vendedor);
        if (vendedorRole != null)
        {
            await AsignarPermisosEspecificosAsync(context, vendedorRole.Id, modulos, new Dictionary<string, string[]>
            {
                { "ventas", new[] { "view", "create", "authorize" } },
                { "creditos", new[] { "view", "viewinstallments" } },
                { "cotizaciones", new[] { "view", "create", "update", "convert" } },
                { "clientes", new[] { "view", "create", "update" } },
                { "productos", new[] { "view" } },
                { "categorias", new[] { "view" } },
                { "marcas", new[] { "view" } },
                { "stock", new[] { "view" } },
                { "dashboard", new[] { "view" } },
                { "notificaciones", new[] { "view", "update", "delete" } }
            });
        }

        var cajeroRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Cajero);
        if (cajeroRole != null)
        {
            await AsignarPermisosEspecificosAsync(context, cajeroRole.Id, modulos, new Dictionary<string, string[]>
            {
                { "ventas", new[] { "view" } },
                { "cobranzas", new[] { "view", "payinstallment", "viewarrears", "viewalerts" } },
                { "caja", new[] { "view", "open", "close", "movements", "history" } },
                { "clientes", new[] { "view" } },
                { "dashboard", new[] { "view" } },
                { "notificaciones", new[] { "view", "update", "delete" } }
            });
        }

        var repositorRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Repositor);
        if (repositorRole != null)
        {
            await AsignarPermisosEspecificosAsync(context, repositorRole.Id, modulos, new Dictionary<string, string[]>
            {
                { "stock", new[] { "view", "viewkardex", "adjust", "transfer", "viewalerts" } },
                { "movimientos", new[] { "view", "create" } },
                { "productos", new[] { "view" } },
                { "devoluciones", new[] { "view" } },
                { "dashboard", new[] { "view" } },
                { "notificaciones", new[] { "view", "update", "delete" } }
            });
        }

        var tecnicoRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Tecnico);
        if (tecnicoRole != null)
        {
            await AsignarPermisosEspecificosAsync(context, tecnicoRole.Id, modulos, new Dictionary<string, string[]>
            {
                { "devoluciones", new[] { "view", "create", "approve", "reject", "complete" } },
                { "garantias", new[] { "view", "create", "update" } },
                { "rmas", new[] { "view", "create", "updatestatus" } },
                { "notascredito", new[] { "view" } },
                { "productos", new[] { "view" } },
                { "stock", new[] { "view", "viewkardex" } },
                { "dashboard", new[] { "view" } },
                { "notificaciones", new[] { "view", "update", "delete" } }
            });
        }

        var contadorRole = roles.FirstOrDefault(r => r.Name == Models.Constants.Roles.Contador);
        if (contadorRole != null)
        {
            await AsignarPermisosEspecificosAsync(context, contadorRole.Id, modulos, new Dictionary<string, string[]>
            {
                { "ventas", new[] { "view" } },
                { "creditos", new[] { "view", "viewinstallments" } },
                { "cobranzas", new[] { "view", "viewarrears", "viewalerts" } },
                { "clientes", new[] { "view" } },
                { "proveedores", new[] { "view" } },
                { "ordenescompra", new[] { "view" } },
                { "reportes", new[] { "view", "sales", "margins", "arrears", "stock", "export" } },
                { "dashboard", new[] { "view" } },
                { "notificaciones", new[] { "view", "update", "delete" } }
            });
        }

        // Tickets: todos los roles pueden crear y ver; solo Admin/SuperAdmin/Gerente pueden resolver y eliminar
        var todosLosRoles = new[] { gerenteRole, vendedorRole, cajeroRole, repositorRole, tecnicoRole, contadorRole };
        foreach (var rol in todosLosRoles.Where(r => r != null))
        {
            await AsignarPermisosEspecificosAsync(context, rol!.Id, modulos, new Dictionary<string, string[]>
            {
                { "tickets", new[] { "view", "create", "update", "changestatus" } }
            });
        }

        await context.SaveChangesAsync();
    }

    private static async Task<Dictionary<(int ModuloId, int AccionId), RolPermiso>> LoadPermisosRoleAsync(AppDbContext context, string roleId)
    {
        var list = await context.RolPermisos
            .IgnoreQueryFilters()
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync();

        return list.ToDictionary(rp => (rp.ModuloId, rp.AccionId), rp => rp);
    }

    private static async Task EnsurePermisoAsync(
        AppDbContext context,
        string roleId,
        ModuloSistema modulo,
        AccionModulo accion,
        Dictionary<(int ModuloId, int AccionId), RolPermiso> existingByKey)
    {
        var key = (modulo.Id, accion.Id);
        var claimValue = ClaimValue(modulo.Clave, accion.Clave);

        if (!existingByKey.TryGetValue(key, out var permiso))
        {
            permiso = new RolPermiso
            {
                RoleId = roleId,
                ModuloId = modulo.Id,
                AccionId = accion.Id,
                ClaimValue = claimValue,
                IsDeleted = false
            };
            context.RolPermisos.Add(permiso);
            existingByKey[key] = permiso;
            return;
        }

        // revive + normalize
        permiso.IsDeleted = false;
        permiso.ClaimValue = claimValue;
    }

    private static async Task AsignarTodosLosPermisosAsync(AppDbContext context, string roleId, List<ModuloSistema> modulos, string[]? exceptoAcciones = null)
    {
        var excepto = (exceptoAcciones ?? Array.Empty<string>())
            .Select(Canon) // "usuarios.delete" ya viene con punto; Canon lo baja a lower/trim
            .ToHashSet(StringComparer.Ordinal);

        var existing = await LoadPermisosRoleAsync(context, roleId);

        foreach (var modulo in modulos)
        {
            foreach (var accion in modulo.Acciones)
            {
                var claimValue = ClaimValue(modulo.Clave, accion.Clave);
                if (excepto.Contains(claimValue))
                    continue;

                await EnsurePermisoAsync(context, roleId, modulo, accion, existing);
            }
        }
    }

    private static async Task AsignarPermisosModulosAsync(AppDbContext context, string roleId, List<ModuloSistema> modulos, string[] modulosClaves)
    {
        var set = modulosClaves.Select(Canon).ToHashSet(StringComparer.Ordinal);
        var existing = await LoadPermisosRoleAsync(context, roleId);

        foreach (var modulo in modulos)
        {
            if (!set.Contains(Canon(modulo.Clave)))
                continue;

            foreach (var accion in modulo.Acciones)
            {
                await EnsurePermisoAsync(context, roleId, modulo, accion, existing);
            }
        }
    }

    private static async Task AsignarPermisosEspecificosAsync(AppDbContext context, string roleId, List<ModuloSistema> modulos, Dictionary<string, string[]> permisosEspecificos)
    {
        var existing = await LoadPermisosRoleAsync(context, roleId);

        foreach (var (moduloClaveRaw, accionesClavesRaw) in permisosEspecificos)
        {
            var moduloClave = Canon(moduloClaveRaw);
            var modulo = modulos.FirstOrDefault(m => Canon(m.Clave) == moduloClave);
            if (modulo == null) continue;

            var accionesSet = accionesClavesRaw.Select(Canon).ToHashSet(StringComparer.Ordinal);
            foreach (var accion in modulo.Acciones)
            {
                if (!accionesSet.Contains(Canon(accion.Clave)))
                    continue;

                await EnsurePermisoAsync(context, roleId, modulo, accion, existing);
            }
        }
    }
}
