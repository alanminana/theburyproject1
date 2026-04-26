using TheBuryProject.Models.Entities;

namespace TheBuryProject.Services.Models
{
    public class ContratoVentaCreditoValidacionResult
    {
        public bool EsValido => !Errores.Any();
        public List<string> Errores { get; set; } = new();
    }

    public class ContratoVentaCreditoPdfArchivo
    {
        public string NombreArchivo { get; init; } = string.Empty;
        public string TipoContenido { get; init; } = "application/pdf";
        public byte[] Contenido { get; init; } = Array.Empty<byte>();
    }

    internal sealed class ContratoVentaCreditoSnapshot
    {
        public required VendedorSnapshot Vendedor { get; init; }
        public required CompradorSnapshot Comprador { get; init; }
        public GaranteSnapshot? Garante { get; init; }
        public required VentaSnapshot Venta { get; init; }
        public required CreditoSnapshot Credito { get; init; }
        public required ContratoSnapshot Contrato { get; init; }
        public string UsuarioGeneracion { get; init; } = string.Empty;
        public string? Sucursal { get; init; }
        public string? Caja { get; init; }
    }

    internal sealed class VendedorSnapshot
    {
        public string Nombre { get; init; } = string.Empty;
        public string Domicilio { get; init; } = string.Empty;
        public string? DNI { get; init; }
        public string? CUIT { get; init; }
        public string CiudadFirma { get; init; } = string.Empty;
        public string Jurisdiccion { get; init; } = string.Empty;
        public decimal InteresMoraDiarioPorcentaje { get; init; }
    }

    internal sealed class CompradorSnapshot
    {
        public string NombreCompleto { get; init; } = string.Empty;
        public string DNI { get; init; } = string.Empty;
        public string? CUITCUIL { get; init; }
        public string Domicilio { get; init; } = string.Empty;
        public string Localidad { get; init; } = string.Empty;
        public string Telefono { get; init; } = string.Empty;
    }

    internal sealed class GaranteSnapshot
    {
        public string NombreCompleto { get; init; } = string.Empty;
        public string DNI { get; init; } = string.Empty;
        public string Domicilio { get; init; } = string.Empty;
        public string? Relacion { get; init; }
    }

    internal sealed class VentaSnapshot
    {
        public string Numero { get; init; } = string.Empty;
        public DateTime Fecha { get; init; }
        public decimal Total { get; init; }
        public List<ProductoVentaSnapshot> Productos { get; init; } = new();
    }

    internal sealed class ProductoVentaSnapshot
    {
        public string Codigo { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
        public int Cantidad { get; init; }
        public decimal PrecioUnitario { get; init; }
        public decimal Descuento { get; init; }
        public decimal Subtotal { get; init; }
    }

    internal sealed class CreditoSnapshot
    {
        public string Numero { get; init; } = string.Empty;
        public int CantidadCuotas { get; init; }
        public decimal MontoCuota { get; init; }
        public decimal TotalAPagar { get; init; }
        public DateTime FechaPrimeraCuota { get; init; }
        public List<CuotaContratoSnapshot> PlanCuotas { get; init; } = new();
    }

    internal sealed class CuotaContratoSnapshot
    {
        public int NumeroCuota { get; init; }
        public decimal MontoCapital { get; init; }
        public decimal MontoInteres { get; init; }
        public decimal MontoTotal { get; init; }
        public DateTime FechaVencimiento { get; init; }
    }

    internal sealed class ContratoSnapshot
    {
        public string Numero { get; init; } = string.Empty;
        public string NumeroPagare { get; init; } = string.Empty;
        public DateTime FechaEmision { get; init; }
    }

    internal sealed class DatosContratoContexto
    {
        public required Venta Venta { get; init; }
        public required Credito Credito { get; init; }
        public required Cliente Cliente { get; init; }
        public required PlantillaContratoCredito Plantilla { get; init; }
        public required List<CuotaContratoSnapshot> PlanCuotas { get; init; }
    }
}
