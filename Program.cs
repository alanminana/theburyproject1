using AutoMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TheBuryProject.Data;
using TheBuryProject.Extensions;
using TheBuryProject.Helpers;
using TheBuryProject.Hubs;
using TheBuryProject.Middleware;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// 0. Alícuota general de IVA (fallback interno, no visible en UI). Configurable por
// appsettings "Iva:PorcentajeDefault"; si no está definida se mantiene 21.
ProductoIvaResolver.Configurar(builder.Configuration.GetValue<decimal?>("Iva:PorcentajeDefault"));

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
// Docker/produccion: la cadena base va sin credenciales y el login SQL dedicado llega por ErpDb:User / ErpDb:Password
// (SqlConnectionStringBuilder escapa cualquier caracter). Sin ErpDb:* se usa la cadena tal cual (LocalDB/Windows Auth en desarrollo).
var defaultConnectionString = ProductionSecrets.ConnectionString(builder.Configuration, builder.Environment.IsProduction());
if (builder.Environment.IsProduction() && !string.IsNullOrEmpty(builder.Configuration["MercadoLibre:ClientId"]))
    ProductionSecrets.Require(builder.Configuration["MercadoLibre:ClientSecret"], "MercadoLibre:ClientSecret");

builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    options.UseSqlServer(defaultConnectionString);
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

    // El esquema real (migraciones) usa nvarchar(450) en las claves compuestas de AspNetUserLogins/Tokens.
    // Identity.UI fija MaxLengthForKeys=128 en runtime, lo que hacía divergir el modelo del snapshot;
    // EF Core 9+ lo trata como error (PendingModelChangesWarning) en MigrateAsync. 0 = sin límite explícito,
    // idéntico al modelo de diseño con el que se generaron las migraciones (sin cambio de esquema).
    options.Stores.MaxLengthForKeys = 0;
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

// Fuente temporal única para reglas de negocio (Micro-lote 6). TimeProvider.System en producción;
// RelojComercial resuelve la fecha comercial de Argentina de forma portable.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRelojComercial, RelojComercial>();

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
builder.Services.AddScoped<IProductoCreditoPersonalConfigService, ProductoCreditoPersonalConfigService>();
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
builder.Services.AddScoped<IMovimientoStockReferenciaResolver, MovimientoStockReferenciaResolver>();
builder.Services.AddScoped<IClienteService, ClienteService>();
builder.Services.AddScoped<IGaranteService, GaranteService>();
builder.Services.AddScoped<ICreditoService, CreditoService>();
builder.Services.AddScoped<IPagoCuotaBackfillService, PagoCuotaBackfillService>();
builder.Services.AddScoped<IConfiguracionPunitorioService, ConfiguracionPunitorioService>();
builder.Services.AddScoped<IPunitorioCalculator, PunitorioCalculator>();
builder.Services.AddScoped<IPunitorioService, PunitorioService>();
builder.Services.AddScoped<IVentaService, VentaService>();
builder.Services.AddScoped<IVentaEnvioService, VentaEnvioService>();
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
builder.Services.AddScoped<ITerminosCondicionesService, TerminosCondicionesService>();

builder.Services.AddScoped<IPrecioService, PrecioService>();
builder.Services.AddScoped<IPrecioVigenteResolver, PrecioVigenteResolver>();

builder.Services.AddScoped<IChequeService, ChequeService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IMoraService, MoraService>();
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

// 5.8 Health checks. Liveness y readiness separados por tag:
//   live  -> solo prueba que el proceso ASP.NET responde; NO depende de SQL ni de nada externo.
//   ready -> ademas verifica que AppDbContext puede conectarse a SQL Server (CanConnectAsync: abre la conexion configurada, no lee ni escribe datos).
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" })
    .AddDbContextCheck<AppDbContext>("sqlserver", failureStatus: HealthStatus.Unhealthy, tags: new[] { "ready" });
// Con SQL caido el connect puede colgar hasta el timeout de SqlClient (15 s); el check se corta antes que el timeout del HEALTHCHECK de Docker (5 s).
builder.Services.Configure<HealthCheckServiceOptions>(options =>
{
    foreach (var registration in options.Registrations.Where(r => r.Name == "sqlserver"))
    {
        registration.Timeout = TimeSpan.FromSeconds(4);
    }
});

// 5.6 Background services (están bien: crean scope por iteración)
builder.Services.AddHostedService<MoraBackgroundService>();
builder.Services.AddHostedService<AlertaStockBackgroundService>();
builder.Services.AddHostedService<DocumentoVencidoBackgroundService>();
builder.Services.AddHostedService<CotizacionVencimientoBackgroundService>();

// 6. MVC
// Binder decimal invariante-flexible global: los inputs type="number" postean con punto
// y el binding por cultura del servidor (es-AR) multiplicaba x100. Ver DecimalModelBinderProvider.
var mvcBuilder = builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new DecimalModelBinderProvider());
    // Mismo problema que el decimal de arriba pero para <input type="date">: siempre
    // postea ISO 8601 sin importar la cultura del navegador (ver DateOnlyModelBinder).
    options.ModelBinderProviders.Insert(0, new DateOnlyModelBinderProvider());
});
if (builder.Environment.IsDevelopment())
    mvcBuilder.AddRazorRuntimeCompilation();

// Rework visual Mercado Libre: el MercadoLibreController sirve las vistas de
// /Views/MercadoLibre1/ (copia exacta del standalone) con fallback al folder original.
mvcBuilder.AddRazorOptions(options =>
    options.ViewLocationExpanders.Add(new TheBuryProject.Extensions.MercadoLibre1ViewLocationExpander()));

// 7. Razor Pages (Identity UI)
builder.Services.AddRazorPages();

// PUN-ML9-E: el JS de pago múltiple (fetch con body JSON) manda el antiforgery token en el
// header "RequestVerificationToken" — sin HeaderName configurado, [ValidateAntiForgeryToken]
// sólo mira Request.Form (inexistente en un POST application/json) y rechaza cualquier envío con
// 400, sin importar qué token se mande. Bug real preexistente (nunca funcionó en un navegador
// real), no introducido por esta superficie: se corrige acá, globalmente, porque es la única
// forma correcta de que un fetch JSON pase antiforgery en ASP.NET Core.
builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");

// Compresion de estaticos de texto (css/js/svg) — MOBILE-DEBT-01. tailwind.css pesa 230 KB y en redes
// moviles se nota. Solo estos tipos: HTML y JSON quedan fuera a proposito (pueden reflejar tokens antiforgery;
// comprimirlos sobre HTTPS abre BREACH). Los estaticos no llevan secretos, asi que EnableForHttps es seguro.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = new[] { "text/css", "text/javascript", "application/javascript", "image/svg+xml" };
});

// 7.1 Forwarded Headers (detras de Caddy). Solo se confia en cabeceras que llegan de los proxies conocidos:
// loopback (default del framework) + las redes CIDR de "ForwardedHeaders:KnownNetworks" (la subred de la red Docker
// bury-net, fijada en docker-compose.yml). NO se usa ASPNETCORE_FORWARDEDHEADERS_ENABLED porque vacia las listas y confia en cualquiera.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = 1; // un solo salto: Caddy
    var knownNetworks = builder.Configuration["ForwardedHeaders:KnownNetworks"];
    foreach (var cidr in (knownNetworks ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
    }
});

var app = builder.Build();

// 7.2 Modo `--migrate` (servicio one-shot `migrate` de docker-compose): aplica migraciones + seeds con el usuario de migracion
// (ErpDb:User = ERP_MIGRATION_USER, con DDL) y termina SIN levantar Kestrel ni background services. Exit 0 = OK, 1 = fallo.
if (args.Contains("--migrate"))
{
    return await DbMigrationRunner.RunAsync(app);
}

// 8. Pipeline
// Forwarded Headers PRIMERO: todo lo que dependa de Request.Scheme/Host/RemoteIp (HSTS, redirects, cookies, auth, audit) lo necesita ya resuelto.
app.UseForwardedHeaders();


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

// La redireccion HTTP->HTTPS la hace Caddy (borde TLS). Kestrel solo escucha HTTP interno y no tiene puerto HTTPS,
// por lo que UseHttpsRedirection() no puede resolver a donde redirigir ("Failed to determine the https port"): se
// registra solo si se configura explicitamente un puerto HTTPS (HTTPS_PORT / ASPNETCORE_HTTPS_PORT), p. ej. Kestrel con TLS propio.
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")
    && !string.IsNullOrWhiteSpace(app.Configuration["HTTPS_PORT"]))
{
    app.UseHttpsRedirection();
}
app.UseResponseCompression();

// Documentos de clientes (DNI, comprobantes) se guardan bajo wwwroot con nombre predecible
// ({ClienteId}_{Tipo}_{timestamp}); bloqueado ANTES de UseStaticFiles para que solo sean accesibles
// vía DocumentoClienteController.Descargar (autenticado + permiso "clientes.viewdocs"), que lee el
// archivo directo del disco sin pasar por este middleware.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads/documentos-clientes"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

app.UseStaticFiles(new StaticFileOptions
{
    // Cache explicito de estaticos (MOBILE-DEBT-01): sin Cache-Control cada visita revalida y en redes moviles
    // el costo se nota. Las URLs con ?v=<hash> (asp-append-version) cambian cuando cambia el contenido, asi que
    // pueden ser inmutables; las fuentes locales no llevan version, por eso solo 30 dias.
    OnPrepareResponse = context =>
    {
        var request = context.Context.Request;
        if (request.Query.ContainsKey("v"))
        {
            context.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        }
        else if (request.Path.StartsWithSegments("/fonts"))
        {
            context.Context.Response.Headers.CacheControl = "public,max-age=2592000";
        }
    }
});

app.UseRouting();

app.UseRateLimiter();

// 8.1 Ver TransientDbUnavailableMiddleware: SQL caído/reiniciando no debe verse como un 500 genérico.
// Antes de Auth porque PermissionClaimsTransformation (dentro de UseAuthentication) es el punto más
// probable donde una caída transitoria de SQL se manifiesta primero en cada request autenticado.
app.UseMiddleware<TheBuryProject.Middleware.TransientDbUnavailableMiddleware>();

// 9. Auth
app.UseAuthentication();
app.UseMiddleware<TerminosCondicionesMiddleware>();
app.UseMiddleware<AuditMiddleware>();
app.UseAuthorization();

// 10. Routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
app.MapHub<NotificacionesHub>("/hubs/notificaciones");
// Respuesta de texto plano (Healthy/Unhealthy) sin detalle de checks ni excepciones; Unhealthy -> 503.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") }).AllowAnonymous();

// 13. Init DB
// Docker/produccion: `Database:InitializeOnStartup=false` (el servicio `migrate` ya aplico migraciones y seeds; el usuario SQL de `app`
// no tiene DDL). Por defecto (LocalDB / desarrollo Windows) sigue inicializando al arrancar, como siempre.
if (!app.Environment.IsEnvironment("Testing") && !app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    app.Logger.LogInformation("Database:InitializeOnStartup=false: la app no ejecuta migraciones ni seeds (lo hace el servicio migrate).");
}
else if (!app.Environment.IsEnvironment("Testing"))
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
return 0;

public partial class Program { }
