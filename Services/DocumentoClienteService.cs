using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class DocumentoClienteService : IDocumentoClienteService
    {
        private readonly AppDbContext _context;
        private readonly IMapper _mapper;
        private readonly ILogger<DocumentoClienteService> _logger;
        private readonly IWebHostEnvironment _environment;
        private const string UPLOAD_FOLDER = "uploads/documentos-clientes";

        public DocumentoClienteService(
            AppDbContext context,
            IMapper mapper,
            ILogger<DocumentoClienteService> logger,
            IWebHostEnvironment environment)
        {
            _context = context;
            _mapper = mapper;
            _logger = logger;
            _environment = environment;
        }

        public async Task<List<DocumentoClienteViewModel>> GetAllAsync()
        {
            var documentos = await _context.Set<DocumentoCliente>()
                .Include(d => d.Cliente)
                .Where(d => !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted)
                .OrderByDescending(d => d.FechaSubida)
                .ToListAsync();

            return _mapper.Map<List<DocumentoClienteViewModel>>(documentos);
        }

        public async Task<DocumentoClienteViewModel?> GetByIdAsync(int id)
        {
            var documento = await _context.Set<DocumentoCliente>()
                .Include(d => d.Cliente)
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted);

            return documento != null ? _mapper.Map<DocumentoClienteViewModel>(documento) : null;
        }

        public async Task<List<DocumentoClienteViewModel>> GetByClienteIdAsync(int clienteId)
        {
            var documentos = await _context.Set<DocumentoCliente>()
                .Include(d => d.Cliente)
                .Where(d => d.ClienteId == clienteId && !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted)
                .OrderByDescending(d => d.FechaSubida)
                .ToListAsync();

            return _mapper.Map<List<DocumentoClienteViewModel>>(documentos);
        }

        public async Task<DocumentoClienteViewModel> UploadAsync(DocumentoClienteViewModel viewModel)
        {
            try
            {
                if (viewModel.Archivo == null || viewModel.Archivo.Length == 0)
                    throw new Exception("Debe seleccionar un archivo");

                var (isValid, errorMessage) = DocumentoValidationHelper.ValidateFile(viewModel.Archivo);
                if (!isValid)
                    throw new Exception(errorMessage);

                DocumentoCliente? documentoAnterior = null;
                if (viewModel.ReemplazarExistente && viewModel.DocumentoAReemplazarId.HasValue)
                {
                    documentoAnterior = await _context.Set<DocumentoCliente>()
                        .FirstOrDefaultAsync(d =>
                            d.Id == viewModel.DocumentoAReemplazarId.Value &&
                            d.ClienteId == viewModel.ClienteId);

                    if (documentoAnterior == null)
                        throw new Exception("El documento a reemplazar no existe o pertenece a otro cliente");
                }

                var uploadPath = Path.Combine(_environment.WebRootPath, UPLOAD_FOLDER);
                if (!Directory.Exists(uploadPath))
                    Directory.CreateDirectory(uploadPath);

                var extension = Path.GetExtension(viewModel.Archivo.FileName).ToLowerInvariant();
                var nombreArchivo = $"{viewModel.ClienteId}_{viewModel.TipoDocumento}_{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";

                var (pathValid, rutaCompleta, pathError) = DocumentoValidationHelper.NormalizePath(uploadPath, nombreArchivo);
                if (!pathValid)
                    throw new Exception(pathError);

                using (var stream = new FileStream(rutaCompleta, FileMode.Create))
                {
                    await viewModel.Archivo.CopyToAsync(stream);
                }

                _logger.LogInformation("Archivo guardado: {Ruta}", rutaCompleta);

                var documento = new DocumentoCliente
                {
                    ClienteId = viewModel.ClienteId,
                    TipoDocumento = viewModel.TipoDocumento,
                    NombreArchivo = viewModel.Archivo.FileName,
                    RutaArchivo = Path.Combine(UPLOAD_FOLDER, nombreArchivo),
                    TipoMIME = viewModel.Archivo.ContentType,
                    TamanoBytes = viewModel.Archivo.Length,
                    Estado = EstadoDocumento.Pendiente,
                    FechaSubida = DateTime.UtcNow,
                    FechaVencimiento = viewModel.FechaVencimiento,
                    Observaciones = viewModel.Observaciones
                };

                _context.Set<DocumentoCliente>().Add(documento);

                if (documentoAnterior != null)
                    documentoAnterior.IsDeleted = true;

                await _context.SaveChangesAsync();

                if (documentoAnterior != null && !string.IsNullOrWhiteSpace(documentoAnterior.RutaArchivo))
                {
                    var rutaAnteriorCompleta = Path.Combine(_environment.WebRootPath, documentoAnterior.RutaArchivo);
                    if (File.Exists(rutaAnteriorCompleta))
                    {
                        File.Delete(rutaAnteriorCompleta);
                        _logger.LogInformation("Archivo anterior eliminado: {Ruta}", rutaAnteriorCompleta);
                    }
                }

                viewModel.Id = documento.Id;
                viewModel.NombreArchivo = documento.NombreArchivo;
                viewModel.RutaArchivo = documento.RutaArchivo;
                viewModel.TamanoBytes = documento.TamanoBytes;
                viewModel.FechaSubida = documento.FechaSubida;
                viewModel.Estado = documento.Estado;

                _logger.LogInformation("Documento {Id} subido exitosamente para cliente {ClienteId}", documento.Id, viewModel.ClienteId);

                return viewModel;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al subir documento");
                throw;
            }
        }

        public async Task<DocumentacionClienteEstadoViewModel> ValidarDocumentacionObligatoriaAsync(
            int clienteId,
            IEnumerable<TipoDocumentoCliente>? requeridos = null)
        {
            var tiposRequeridos = requeridos?.ToList() 
                ?? DropdownConstants.DocumentosClienteRequeridos.ToList();

            var documentosCliente = await _context.Set<DocumentoCliente>()
                .Where(d => d.ClienteId == clienteId && !d.IsDeleted)
                .ToListAsync();

            var faltantes = new List<TipoDocumentoCliente>();

            foreach (var tipo in tiposRequeridos)
            {
                var tieneDocumento = documentosCliente.Any(d =>
                    d.TipoDocumento == tipo &&
                    d.Estado == EstadoDocumento.Verificado);

                if (!tieneDocumento)
                    faltantes.Add(tipo);
            }

            return new DocumentacionClienteEstadoViewModel
            {
                Completa = !faltantes.Any(),
                Faltantes = faltantes
            };
        }

        public async Task<bool> VerificarAsync(int id, string verificadoPor, string? observaciones = null)
        {
            try
            {
                var documento = await _context.Set<DocumentoCliente>()
                    .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

                if (documento == null)
                    return false;

                documento.Estado = EstadoDocumento.Verificado;
                documento.FechaVerificacion = DateTime.UtcNow;
                documento.VerificadoPor = verificadoPor;
                if (!string.IsNullOrEmpty(observaciones))
                    documento.Observaciones = observaciones;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Documento {Id} verificado por {Usuario}", id, verificadoPor);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar documento {Id}", id);
                throw;
            }
        }

        public async Task<bool> RechazarAsync(int id, string motivo, string rechazadoPor)
        {
            try
            {
                var documento = await _context.Set<DocumentoCliente>()
                    .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

                if (documento == null)
                    return false;

                documento.Estado = EstadoDocumento.Rechazado;
                documento.FechaVerificacion = DateTime.UtcNow;
                documento.VerificadoPor = rechazadoPor;
                documento.MotivoRechazo = motivo;

                await _context.SaveChangesAsync();

                _logger.LogInformation("Documento {Id} rechazado por {Usuario}. Motivo: {Motivo}", id, rechazadoPor, motivo);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar documento {Id}", id);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            try
            {
                var documento = await _context.Set<DocumentoCliente>()
                    .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

                if (documento == null)
                    return false;

                documento.IsDeleted = true;
                await _context.SaveChangesAsync();

                var rutaCompleta = Path.Combine(_environment.WebRootPath, documento.RutaArchivo);
                if (File.Exists(rutaCompleta))
                {
                    File.Delete(rutaCompleta);
                    _logger.LogInformation("Archivo físico eliminado: {Ruta}", rutaCompleta);
                }

                _logger.LogInformation("Documento {Id} eliminado", id);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al eliminar documento {Id}", id);
                throw;
            }
        }

        public async Task<byte[]> DescargarArchivoAsync(int id)
        {
            try
            {
                var documento = await _context.Set<DocumentoCliente>()
                    .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

                if (documento == null)
                    throw new Exception("Documento no encontrado");

                var rutaCompleta = Path.Combine(_environment.WebRootPath, documento.RutaArchivo);

                if (!File.Exists(rutaCompleta))
                    throw new Exception("Archivo no encontrado en el servidor");

                return await File.ReadAllBytesAsync(rutaCompleta);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al descargar documento {Id}", id);
                throw;
            }
        }

        public async Task<(List<DocumentoClienteViewModel> Documentos, int Total)> BuscarAsync(DocumentoClienteFilterViewModel filtro)
        {
            try
            {
                var query = _context.Set<DocumentoCliente>()
                    .Include(d => d.Cliente)
                    .Where(d => !d.IsDeleted && d.Cliente != null && !d.Cliente.IsDeleted)
                    .AsQueryable();

                if (filtro.ClienteId.HasValue)
                    query = query.Where(d => d.ClienteId == filtro.ClienteId.Value);

                if (filtro.TipoDocumento.HasValue)
                    query = query.Where(d => d.TipoDocumento == (TipoDocumentoCliente)filtro.TipoDocumento.Value);

                if (filtro.Estado.HasValue)
                    query = query.Where(d => d.Estado == filtro.Estado.Value);

                if (filtro.SoloPendientes)
                    query = query.Where(d => d.Estado == EstadoDocumento.Pendiente);

                if (filtro.SoloVencidos)
                    query = query.Where(d =>
                        d.Estado == EstadoDocumento.Vencido ||
                        (d.FechaVencimiento.HasValue && d.FechaVencimiento.Value < DateTime.UtcNow.Date));

                var total = await query.CountAsync();

                var pageNumber = filtro.PageNumber > 0 ? filtro.PageNumber : 1;
                var pageSize = filtro.PageSize > 0 ? filtro.PageSize : 10;

                var documentos = await query
                    .OrderByDescending(d => d.FechaSubida)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                filtro.TotalResultados = total;

                return (_mapper.Map<List<DocumentoClienteViewModel>>(documentos), total);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al buscar documentos");
                throw;
            }
        }

        public async Task<int> VerificarTodosAsync(int clienteId, string verificadoPor, string? observaciones = null)
        {
            try
            {
                var documentosPendientes = await _context.Set<DocumentoCliente>()
                    .Where(d => d.ClienteId == clienteId 
                             && !d.IsDeleted 
                             && d.Estado == EstadoDocumento.Pendiente)
                    .ToListAsync();

                if (!documentosPendientes.Any())
                    return 0;

                var ahora = DateTime.UtcNow;
                foreach (var documento in documentosPendientes)
                {
                    documento.Estado = EstadoDocumento.Verificado;
                    documento.FechaVerificacion = ahora;
                    documento.VerificadoPor = verificadoPor;
                    if (!string.IsNullOrEmpty(observaciones))
                        documento.Observaciones = observaciones;
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Se verificaron {Cantidad} documentos del cliente {ClienteId} por {Usuario}",
                    documentosPendientes.Count, clienteId, verificadoPor);

                return documentosPendientes.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar todos los documentos del cliente {ClienteId}", clienteId);
                throw;
            }
        }

        public async Task MarcarVencidosAsync()
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var now = DateTime.UtcNow;

                var updated = await _context.Set<DocumentoCliente>()
                    .Where(d => !d.IsDeleted
                             && d.Estado == EstadoDocumento.Verificado
                             && d.FechaVencimiento.HasValue
                             && d.FechaVencimiento.Value < today)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(d => d.Estado, EstadoDocumento.Vencido)
                        .SetProperty(d => d.UpdatedAt, now));

                _logger.LogInformation("Se marcaron {Count} documentos como vencidos", updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al marcar documentos vencidos");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<BatchOperacionResultado> VerificarBatchAsync(
            IEnumerable<int> ids, 
            string verificadoPor, 
            string? observaciones = null)
        {
            var resultado = new BatchOperacionResultado();
            var idsList = ids.Distinct().ToList();
            
            if (!idsList.Any())
                return resultado;

            try
            {
                var documentos = await _context.Set<DocumentoCliente>()
                    .Where(d => idsList.Contains(d.Id) && !d.IsDeleted)
                    .ToListAsync();

                var ahora = DateTime.UtcNow;
                
                foreach (var id in idsList)
                {
                    var doc = documentos.FirstOrDefault(d => d.Id == id);
                    
                    if (doc == null)
                    {
                        resultado.Fallidos++;
                        resultado.Errores.Add(new BatchItemError 
                        { 
                            Id = id, 
                            Mensaje = "Documento no encontrado" 
                        });
                        continue;
                    }

                    if (doc.Estado != EstadoDocumento.Pendiente)
                    {
                        resultado.Fallidos++;
                        resultado.Errores.Add(new BatchItemError 
                        { 
                            Id = id, 
                            Mensaje = $"No se puede verificar: estado actual es {doc.Estado}" 
                        });
                        continue;
                    }

                    doc.Estado = EstadoDocumento.Verificado;
                    doc.FechaVerificacion = ahora;
                    doc.VerificadoPor = verificadoPor;
                    if (!string.IsNullOrEmpty(observaciones))
                        doc.Observaciones = observaciones;

                    resultado.Exitosos++;
                }

                if (resultado.Exitosos > 0)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "Se verificaron {Exitosos} documentos en batch por {Usuario}. Fallidos: {Fallidos}",
                        resultado.Exitosos, verificadoPor, resultado.Fallidos);
                }

                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al verificar documentos en batch");
                throw;
            }
        }

        /// <inheritdoc/>
        public async Task<BatchOperacionResultado> RechazarBatchAsync(
            IEnumerable<int> ids, 
            string motivo, 
            string rechazadoPor)
        {
            var resultado = new BatchOperacionResultado();
            var idsList = ids.Distinct().ToList();
            
            if (!idsList.Any())
                return resultado;

            if (string.IsNullOrWhiteSpace(motivo))
            {
                foreach (var id in idsList)
                {
                    resultado.Fallidos++;
                    resultado.Errores.Add(new BatchItemError 
                    { 
                        Id = id, 
                        Mensaje = "Debe especificar el motivo del rechazo" 
                    });
                }
                return resultado;
            }

            try
            {
                var documentos = await _context.Set<DocumentoCliente>()
                    .Where(d => idsList.Contains(d.Id) && !d.IsDeleted)
                    .ToListAsync();

                var ahora = DateTime.UtcNow;
                
                foreach (var id in idsList)
                {
                    var doc = documentos.FirstOrDefault(d => d.Id == id);
                    
                    if (doc == null)
                    {
                        resultado.Fallidos++;
                        resultado.Errores.Add(new BatchItemError 
                        { 
                            Id = id, 
                            Mensaje = "Documento no encontrado" 
                        });
                        continue;
                    }

                    if (doc.Estado != EstadoDocumento.Pendiente)
                    {
                        resultado.Fallidos++;
                        resultado.Errores.Add(new BatchItemError 
                        { 
                            Id = id, 
                            Mensaje = $"No se puede rechazar: estado actual es {doc.Estado}" 
                        });
                        continue;
                    }

                    doc.Estado = EstadoDocumento.Rechazado;
                    doc.FechaVerificacion = ahora;
                    doc.VerificadoPor = rechazadoPor;
                    doc.MotivoRechazo = motivo;

                    resultado.Exitosos++;
                }

                if (resultado.Exitosos > 0)
                {
                    await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "Se rechazaron {Exitosos} documentos en batch por {Usuario}. Motivo: {Motivo}. Fallidos: {Fallidos}",
                        resultado.Exitosos, rechazadoPor, motivo, resultado.Fallidos);
                }

                return resultado;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al rechazar documentos en batch");
                throw;
            }
        }
    }
}
