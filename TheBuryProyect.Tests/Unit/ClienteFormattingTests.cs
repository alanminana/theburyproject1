using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests unitarios para ClienteFormatting.
/// Función pura — no requiere base de datos.
/// </summary>
public class ClienteFormattingTests
{
    [Fact]
    public void ToDisplayName_Cliente_FormatoApellidoNombreDni()
    {
        var cliente = new Cliente
        {
            Apellido = "García",
            Nombre = "Juan",
            NumeroDocumento = "12345678"
        };

        var resultado = cliente.ToDisplayName();

        Assert.Equal("García, Juan - DNI: 12345678", resultado);
    }

    [Fact]
    public void ToDisplayName_Garante_SinClienteAsociado_FormatoDirecto()
    {
        var garante = new Garante
        {
            Apellido = "Pérez",
            Nombre = "Ana",
            NumeroDocumento = "87654321",
            GaranteCliente = null
        };

        var resultado = garante.ToDisplayName();

        Assert.Equal("Pérez, Ana - DNI: 87654321", resultado);
    }

    [Fact]
    public void ToDisplayName_Garante_ConClienteAsociado_UsaDatosDelCliente()
    {
        var clienteAsociado = new Cliente
        {
            Apellido = "López",
            Nombre = "María",
            NumeroDocumento = "11112222"
        };
        var garante = new Garante
        {
            Apellido = "OtroApellido",
            Nombre = "OtroNombre",
            NumeroDocumento = "99999999",
            GaranteCliente = clienteAsociado
        };

        var resultado = garante.ToDisplayName();

        Assert.Equal("López, María - DNI: 11112222", resultado);
    }
}
