using System.Linq;
using System.Reflection;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Guardia estructural de autoridad única de estado (corrección PUN-ML7).
///
/// Hasta esta corrección, <c>CreditoService</c> declaraba un resolver privado de 3 argumentos
/// (<c>ResolverEstadoCuota(decimal montoPagado, decimal totalCuota, decimal punitorioPendiente)</c>)
/// que decidía Pagada/Parcial/Pendiente sin conocer la fecha de vencimiento ni el estado terminal —
/// una segunda autoridad en paralelo a <see cref="EstadoCuotaResolver.Resolver"/>. Fue eliminado: los
/// tests de comportamiento que antes vivían acá (monto vs. total, gate de punitorio pendiente) ahora
/// están cubiertos, con el contrato completo, en <c>EstadoCuotaResolverTests</c>; la prueba de que los
/// caminos productivos realmente delegan en él está en <c>CreditoServicePagarCuotaSeguridadTests</c>
/// (pago individual/primera cuota/adelanto, todos comparten <c>RegistrarPagoCuotaAsync</c>) y en
/// <c>PunitorioServiceTests</c> (aplicar/anular).
///
/// Este archivo se queda solo con la guardia estructural: falla si <c>CreditoService</c> vuelve a
/// declarar, con cualquier nombre, un método con la forma de un resolver de estado independiente.
/// </summary>
public class CreditoServiceEstadoCuotaTests
{
    [Fact]
    public void CreditoService_NoDeclaraUnResolverDeEstadoIndependiente()
    {
        // Cualquier método declarado directamente en CreditoService (público o no, estático o de
        // instancia) que reciba solo decimals y devuelva EstadoCuota tiene exactamente la forma del
        // resolver eliminado — sin importar el nombre que se le ponga. La firma real de la única
        // autoridad (EstadoCuotaResolver.Resolver) es distinta a propósito: exige estado actual,
        // vencimiento y fecha comercial, así que nunca matchea este patrón por delegación legítima.
        var candidatos = typeof(CreditoService)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(EstadoCuota) &&
                        m.GetParameters().Length > 0 &&
                        m.GetParameters().All(p => p.ParameterType == typeof(decimal)))
            .Select(m => m.Name)
            .ToList();

        Assert.True(candidatos.Count == 0,
            "CreditoService declara un método con forma de resolver de estado independiente: " +
            string.Join(", ", candidatos) +
            ". El estado de una cuota debe resolverse exclusivamente vía EstadoCuotaResolver.Resolver.");
    }

    [Fact]
    public void CreditoService_YaNoExponeElMetodoResolverEstadoCuotaLegacy()
    {
        // Guardia adicional, específica del nombre histórico del resolver eliminado — más fácil de
        // leer que la anterior como señal directa de qué se rompió si alguien lo reintroduce igual.
        var metodo = typeof(CreditoService)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
                        BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.Name == "ResolverEstadoCuota");

        Assert.Null(metodo);
    }
}
