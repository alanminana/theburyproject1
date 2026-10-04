using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Numeración documental por tipo. El incremento es un UPDATE atómico de la fila del tipo: el
    /// motor toma un lock de escritura que se mantiene hasta que termina la transacción, así que dos
    /// procesos concurrentes nunca leen el mismo valor. Además existe un índice único
    /// (TipoDocumentoId, Numero) como red de seguridad.
    /// Formato: {Prefijo}-{yyyyMM}-{secuencia de 6 dígitos} (la secuencia no se reinicia por período,
    /// igual que la numeración legada de contratos).
    /// </summary>
    public class DocumentoNumeracionService : IDocumentoNumeracionService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DocumentoNumeracionService> _logger;

        public DocumentoNumeracionService(AppDbContext context, ILogger<DocumentoNumeracionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> SiguienteNumeroAsync(int tipoDocumentoId)
        {
            var transaccionPropia = _context.Database.CurrentTransaction == null;
            await using var tx = transaccionPropia ? await _context.Database.BeginTransactionAsync() : null;

            try
            {
                // Hasta 20 intentos: salta números ya ocupados (ej. importados del sistema legado o
                // asignados a mano) en vez de fallar al insertar.
                for (var intento = 0; intento < 20; intento++)
                {
                    var filas = await _context.TiposDocumento
                        .IgnoreQueryFilters()
                        .Where(t => t.Id == tipoDocumentoId && !t.IsDeleted)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.UltimoNumero, t => t.UltimoNumero + 1));

                    if (filas == 0)
                        throw new DocumentoException(DocumentoErrores.ErrorNumeracion,
                            $"No existe el tipo de documento {tipoDocumentoId} para numerar.");

                    var tipo = await _context.TiposDocumento
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(t => t.Id == tipoDocumentoId)
                        .Select(t => new { t.Prefijo, t.UltimoNumero })
                        .FirstAsync();

                    var numero = $"{tipo.Prefijo}-{DateTime.UtcNow:yyyyMM}-{tipo.UltimoNumero:D6}";
                    if (await _context.DocumentosGenerados.AsNoTracking()
                            .AnyAsync(d => d.TipoDocumentoId == tipoDocumentoId && d.Numero == numero))
                        continue;

                    if (tx != null)
                        await tx.CommitAsync();

                    return numero;
                }

                throw new DocumentoException(DocumentoErrores.ErrorNumeracion,
                    $"No se encontró un número libre para el tipo de documento {tipoDocumentoId}.");
            }
            catch (DocumentoException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al numerar documento del tipo {TipoDocumentoId}", tipoDocumentoId);
                throw new DocumentoException(DocumentoErrores.ErrorNumeracion,
                    "No se pudo asignar el número de documento.", ex);
            }
        }
    }
}
