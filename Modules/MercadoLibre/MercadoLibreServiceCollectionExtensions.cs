using TheBuryProject.Modules.MercadoLibre.Options;
using TheBuryProject.Modules.MercadoLibre.Services;
using TheBuryProject.Modules.MercadoLibre.Services.Interfaces;

namespace TheBuryProject.Modules.MercadoLibre
{
    /// <summary>
    /// Registración DI del módulo MercadoLibre. Punto único de wiring:
    /// Program.cs solo llama AddMercadoLibreModule.
    /// </summary>
    public static class MercadoLibreServiceCollectionExtensions
    {
        public static IServiceCollection AddMercadoLibreModule(
            this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<MercadoLibreOptions>(
                configuration.GetSection(MercadoLibreOptions.SectionName));

            services.AddSingleton<IMercadoLibreTokenProtector, MercadoLibreTokenProtector>();

            services.AddHttpClient<IMercadoLibreApiClient, MercadoLibreApiClient>((sp, client) =>
            {
                var options = configuration
                    .GetSection(MercadoLibreOptions.SectionName)
                    .Get<MercadoLibreOptions>() ?? new MercadoLibreOptions();

                client.BaseAddress = new Uri(options.ApiBaseUrl);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Accept.Add(
                    new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.AddScoped<IMercadoLibreAuthService, MercadoLibreAuthService>();
            services.AddScoped<IMercadoLibreAccountService, MercadoLibreAccountService>();
            services.AddScoped<IMercadoLibreListingService, MercadoLibreListingService>();
            services.AddScoped<IMercadoLibreConfiguracionService, MercadoLibreConfiguracionService>();
            services.AddScoped<IMercadoLibrePricingService, MercadoLibrePricingService>();
            services.AddScoped<IMercadoLibreSyncService, MercadoLibreSyncService>();
            services.AddScoped<IMercadoLibreOrderService, MercadoLibreOrderService>();
            services.AddScoped<IMercadoLibreListingAdminService, MercadoLibreListingAdminService>();
            services.AddScoped<IMercadoLibrePriceBatchService, MercadoLibrePriceBatchService>();
            services.AddScoped<IMercadoLibrePublicacionService, MercadoLibrePublicacionService>();
            services.AddScoped<IMercadoLibreCategoriaService, MercadoLibreCategoriaService>();
            services.AddScoped<IMercadoLibreCategoryCatalogImportService, MercadoLibreCategoryCatalogImportService>();
            services.AddScoped<IMercadoLibreCategoryCatalogService, MercadoLibreCategoryCatalogService>();
            services.AddScoped<IMercadoLibreDashboardService, MercadoLibreDashboardService>();
            services.AddScoped<IMercadoLibreQuestionService, MercadoLibreQuestionService>();
            services.AddScoped<IMercadoLibreMessageService, MercadoLibreMessageService>();
            services.AddScoped<IMercadoLibreWebhookProcessor, MercadoLibreWebhookProcessor>();
            services.AddHostedService<MercadoLibreWebhookBackgroundService>();

            return services;
        }
    }
}
