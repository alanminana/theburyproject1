using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Funciones puras para resolución de parámetros de configuración de crédito.
    /// No acceden a base de datos ni a infraestructura.
    /// </summary>
    public static class CreditoConfiguracionHelper
    {
        /// <summary>
        /// Determina el rango de cuotas permitidas y la descripción del método aplicado,
        /// a partir de los objetos de dominio ya cargados desde DB.
        /// No realiza accesos a base de datos.
        /// </summary>
        public static (int Min, int Max, string Descripcion) ResolverRangoCuotasPermitidos(
            MetodoCalculoCredito metodo,
            PerfilCredito? perfil,
            Cliente? cliente)
        {
            switch (metodo)
            {
                case MetodoCalculoCredito.Manual:
                    return (1, 120, "Manual");

                case MetodoCalculoCredito.UsarPerfil:
                case MetodoCalculoCredito.AutomaticoPorCliente when perfil != null:
                    if (perfil != null)
                        return (perfil.MinCuotas, perfil.MaxCuotas, $"Perfil '{perfil.Nombre}'");
                    // perfil no cargado: caer en rango global por defecto
                    return (1, 120, "");

                case MetodoCalculoCredito.UsarCliente:
                    if (cliente?.CuotasMaximasPersonalizadas.HasValue == true)
                        return (1, cliente.CuotasMaximasPersonalizadas.Value, "Cliente");
                    return (1, 24, "Cliente (sin config)");

                case MetodoCalculoCredito.Global:
                case MetodoCalculoCredito.AutomaticoPorCliente:
                default:
                    return (1, 24, "Global");
            }
        }

        /// <summary>
        /// Resuelve la tasa mensual y los gastos administrativos a aplicar al crédito,
        /// a partir de los datos ya cargados. No accede a base de datos ni a infraestructura.
        ///
        /// Comportamiento por fuente:
        ///   PorCliente, cliente != null → tasa del cliente si tiene, sino tasaGlobal;
        ///                                 gastos del cliente si gastosDelModelo es null, sino 0
        ///   PorCliente, cliente == null → tasaGlobal, gastos sin cambio (gastosDelModelo ?? 0)
        ///   Global (o cualquier otro)   → tasaGlobal, gastos sin cambio
        ///
        /// El caso Manual no pasa por este método — el caller lo maneja directamente.
        /// </summary>
        public static (decimal? Tasa, decimal Gastos) ResolverTasaYGastos(
            FuenteConfiguracionCredito fuente,
            decimal? tasaMensualDelModelo,
            decimal? gastosDelModelo,
            decimal tasaGlobal,
            Cliente? cliente)
        {
            var gastosBase = gastosDelModelo ?? 0m;

            if (fuente == FuenteConfiguracionCredito.PorCliente && cliente != null)
            {
                var tasa = cliente.TasaInteresMensualPersonalizada ?? tasaGlobal;
                var gastos = gastosDelModelo.HasValue
                    ? gastosBase
                    : cliente.GastosAdministrativosPersonalizados ?? 0m;
                return (tasa, gastos);
            }

            // Global, cliente no encontrado en PorCliente, o cualquier otro valor de fuente
            return (tasaGlobal, gastosBase);
        }
    }
}
