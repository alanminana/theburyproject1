using AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Extensions;
using TheBuryProject.Helpers;
using TheBuryProject.Hubs;
using TheBuryProject.Middleware;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// 1. Infra
builder.Services.AddHttpContextAccessor();

// 1.1 Data Protection: persistir claves para que los tokens cifrados de Mercado Libre,
// las cookies de autenticación y los tokens antiforgery sobrevivan a reinicios y se
// compartan entre instancias. En contenedores, montar un volumen en DataProtection:KeysPath.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("TheBuryProject");
// En Testing se usa el almacén efímero por defecto (evita I/O y contención de
// claves entre tests de integración en paralelo). En el resto, persistir a disco.
if (!builder.Environment.IsEnvironment("Testing"))
{
    var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath, "keys");
    }
    Directory.CreateDirectory(dataProtectionKeysPath);
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// 2. EF Core (evitar mezclar AddDbContext + AddDbContextFactory)
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Si tu app ya inyecta AppDbContext en servicios/scopes (MVC), crealo desde el factory:
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// 3. Identity
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    // Contraseña
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;

    // Bloqueo de cuenta
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;

    // Usuario
    options.User.RequireUniqueEmail = true;

    // IMPORTANTE: NO requerir email confirmado para login (útil para testing)
    options.SignIn.RequireConfirmedEmail = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<AppDbContext>();

// Claims (factory + transformation)
builder.Services.AddScoped<IClaimsTransformation, PermissionClaimsTransformation>();

// 4. AutoMapper
builder.Services.AddSingleton<IMapper>(sp =>
{
    var loggerFactory = sp.GetService<ILoggerFactory>();
    var config = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<MappingProfile>();
        cfg.AddProfile<MercadoLibreMappingProfile>();
    }, loggerFactory);
    return config.CreateMapper();
});

// 5. Servicios (DI)
builder.Services.AddCoreServices();
builder.Services.AddVentaServices();
builder.Services.AddCreditoServices();
builder.Services.AddTicketServices();
builder.Services.AddMercadoLibreModule(builder.Configuration);

builder.Services.AddScoped<ICategoriaService, CategoriaService>();
builder.Services.AddScoped<IMarcaService, MarcaService>();
builder.Services.AddScoped<IProductoService, ProductoService>();
builder.Services.AddScoped<IProductoCreditoRestriccionService, ProductoCreditoRestriccionService>();
builder.Services.AddScoped<ICreditoRangoProductoService, CreditoRangoProductoService>();
builder.Services.AddScoped<ICreditoConfiguracionVentaService, CreditoConfiguracionVentaService>();
builder.Services.AddScoped<ICreditoSimulacionVentaService, CreditoSimulacionVentaService>();
builder.Services.AddScoped<ICreditoUiQueryService, CreditoUiQueryService>();
builder.Services.AddScoped<ICatalogLookupService, CatalogLookupService>();
builder.Services.AddScoped<ICatalogoService, CatalogoService>();
builder.Services.AddScoped<IPrecioHistoricoService, PrecioHistoricoService>();
builder.Services.AddScoped<IProveedorService, ProveedorService>();
builder.Services.AddScoped<IOrdenCompraService, OrdenCompraService>();
builder.Services.AddScoped<IMovimientoStockService, MovimientoStockService>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IGaranteService, GaranteService>();
builder.Services.AddScoped<ICreditoService, CreditoService>();
builder.Services.AddScoped<IVentaService, VentaService>();
builder.Services.AddScoped<IConfiguracionPagoService, ConfiguracionPagoService>();
builder.Services.AddScoped<IConfiguracionPagoGlobalAdminService, ConfiguracionPagoService>();
builder.Services.AddScoped<IConfiguracionPagoGlobalQueryService, ConfiguracionPagoGlobalQueryService>();
builder.Services.AddScoped<ICotizacionPagoCalculator, CotizacionPagoCalculator>();
builder.Services.AddScoped<ICotizacionService, CotizacionService>();
builder.Services.AddScoped<ICotizacionConversionService, CotizacionConversionService>();
builder.Services.AddSingleton<ICotizacionPdfService, CotizacionPdfService>();
builder.Services.AddScoped<IPlantillaContratoCreditoService, PlantillaContratoCreditoService>();
builder.Services.AddScoped<IContratoVentaCreditoService, ContratoVentaCreditoService>();
builder.Services.AddScoped<IConfiguracionMoraService, ConfiguracionMoraService>();
builder.Services.AddScoped<IRolService, RolService>();
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<ISeguridadAuditoriaService, SeguridadAuditoriaService>();

builder.Services.AddScoped<IPrecioService, PrecioService>();
builder.Services.AddScoped<IPrecioVigenteResolver, PrecioVigenteResolver>();

builder.Services.AddScoped<IChequeService, ChequeService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IMoraService, MoraService>();
builder.Services.AddScoped<IEvaluacionCreditoService, EvaluacionCreditoService>();
builder.Services.AddScoped<IDocumentoClienteService, DocumentoClienteService>();
builder.Services.AddScoped<IAlertaStockService, AlertaStockService>();
builder.Services.AddScoped<IConfiguracionRentabilidadService, ConfiguracionRentabilidadService>();
builder.Services.AddScoped<IReporteService, ReporteService>();
builder.Services.AddScoped<IAutorizacionService, AutorizacionService>();
builder.Services.AddScoped<IDevolucionService, DevolucionService>();
builder.Services.AddScoped<ICajaService, CajaService>();
builder.Services.AddScoped<ICajaVendedorService, CajaVendedorService>();
builder.Services.AddScoped<INotificacionService, NotificacionService>();
builder.Services.AddScoped<IDocumentacionService, DocumentacionService>();
builder.Services.AddScoped<IClienteLookupService, ClienteLookupService>();
builder.Services.AddScoped<IProductoUnidadService, ProductoUnidadService>();
builder.Services.AddScoped<VentaViewBagBuilder>();
builder.Services.AddScoped<CreditoViewBagBuilder>();

// 5.4 BCRA Central de Deudores
builder.Services.AddHttpClient<ISituacionCrediticiaBcraService, SituacionCrediticiaBcraService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});

// 5.5 SignalR
builder.Services.AddSignalR();

// 5.7 Rate limiting: protege el webhook anónimo de Mercado Libre de floods que
// inflen la tabla de eventos. Límite holgado para no afectar tráfico legítimo.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("mercadolibre-webhook", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 1000;
        o.QueueLimit = 0;
    });
});

// 5.8 Health checks (liveness para el orquestador)
builder.Services.AddHealthChecks();

// 5.6 Background services (están bien: crean scope por iteración)
builder.Services.AddHostedService<MoraBackgroundService>();
builder.Services.AddHostedService<AlertaStockBackgroundService>();
builder.Services.AddHostedService<DocumentoVencidoBackgroundService>();
builder.Services.AddHostedService<CotizacionVencimientoBackgroundService>();

// 6. MVC
var mvcBuilder = builder.Services.AddControllersWithViews();
if (builder.Environment.IsDevelopment())
    mvcBuilder.AddRazorRuntimeCompilation();

// Rework visual Mercado Libre: el MercadoLibreController sirve las vistas de
// /Views/MercadoLibre1/ (copia exacta del standalone) con fallback al folder original.
mvcBuilder.AddRazorOptions(options =>
    options.ViewLocationExpanders.Add(new TheBuryProject.Extensions.MercadoLibre1ViewLocationExpander()));

// 7. Razor Pages (Identity UI)
builder.Services.AddRazorPages();

var app = builder.Build();

// 8. Pipeline
// Security headers (defensa básica). CSP se omite a propósito: requiere QA visual
// porque las vistas usan estilos/scripts inline y SignalR; ver docs/despliegue-produccion.md.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Permitted-Cross-Domain-Policies"] = "none";
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

// 9. Auth
app.UseAuthentication();
app.UseMiddleware<AuditMiddleware>();
app.UseAuthorization();

// 10. Routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.MapHub<NotificacionesHub>("/hubs/notificaciones");
app.MapHealthChecks("/health");

// 13. Init DB
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        await DbInitializer.Initialize(services);

        if (app.Environment.IsDevelopment())
        {
            await DbInitializer.CreateTestUsersAsync(services);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error durante la inicialización de la base de datos");

        // En desarrollo es mejor fallar rápido: evita que la app quede "arrancada" pero rota
        // (background services y pantallas van a seguir fallando sin DB).
        if (app.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

app.Run();

public partial class Program { }
