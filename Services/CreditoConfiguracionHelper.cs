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

    }
}
