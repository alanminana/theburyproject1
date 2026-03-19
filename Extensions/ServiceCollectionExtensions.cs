using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Validators;

namespace TheBuryProject.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services)
        {
            services.AddScoped<ICurrentUserService, CurrentUserService>();

            return services;
        }

        public static IServiceCollection AddVentaServices(this IServiceCollection services)
        {
            services.AddScoped<IFinancialCalculationService, FinancialCalculationService>();
            services.AddScoped<IPrequalificationService, PrequalificationService>();
            services.AddScoped<IVentaValidator, VentaValidator>();
            services.AddScoped<VentaNumberGenerator>();

            return services;
        }

        public static IServiceCollection AddMoraServices(this IServiceCollection services)
        {
            // Motor de cálculo puro (sin estado, sin DB)
            services.AddSingleton<ICalculoMoraService, CalculoMoraService>();

            // Automatización por tramos (sin estado directo de DB)
            services.AddScoped<ICobranzaAutomatizacionService, CobranzaAutomatizacionService>();

            // Gestión de promesas de pago (requiere DB)
            services.AddScoped<IPromesaPagoService, PromesaPagoService>();

            return services;
        }

        public static IServiceCollection AddCreditoServices(this IServiceCollection services)
        {
            // Servicio de aptitud crediticia (semáforo Apto/NoApto/RequiereAutorizacion)
            services.AddScoped<IClienteAptitudService, ClienteAptitudService>();

            // Servicio de dominio para cálculo de límite/saldo/disponible por puntaje
            services.AddScoped<ICreditoDisponibleService, CreditoDisponibleService>();

            // Servicio de validación unificada para ventas con crédito personal
            services.AddScoped<IValidacionVentaService, ValidacionVentaService>();

            return services;
        }
    }
}