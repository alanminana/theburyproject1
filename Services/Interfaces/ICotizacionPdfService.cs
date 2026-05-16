using TheBuryProject.Services.Models;

namespace TheBuryProject.Services.Interfaces;

public interface ICotizacionPdfService
{
    byte[] Generar(CotizacionResultado cotizacion);
}
