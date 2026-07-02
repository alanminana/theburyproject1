using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class ClienteService : IClienteService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ClienteService> _logger;

        public ClienteService(AppDbContext context, ILogger<ClienteService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<Cliente>> GetAllAsync()
        {
            // AppDbContext no aplica QueryFilter global para Cliente (por compatibilidad con otras entidades).
            return await _context.Clientes
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();
        }

        public async Task<Cliente?> GetByIdAsync(int id)
        {
            return await _context.Clientes
                .AsNoTracking()
                .Include(c => c.Creditos.Where(cr => !cr.IsDeleted))
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        }

        public async Task<Cliente?> GetByDocumentoAsync(string tipoDocumento, string numeroDocumento)
        {
            return await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c =>
                    c.TipoDocumento == tipoDocumento &&
                    c.NumeroDocumento == numeroDocumento &&
                    !c.IsDeleted);
        }

        public async Task<bool> ExisteDocumentoAsync(string tipoDocumento, string numeroDocumento, int? excludeId = null)
        {
            return await _context.Clientes
                .AsNoTracking()
                .AnyAsync(c =>
                    c.TipoDocumento == tipoDocumento &&
                    c.NumeroDocumento == numeroDocumento &&
                    !c.IsDeleted &&
                    (!excludeId.HasValue || c.Id != excludeId.Value));
        }

        public async Task<Cliente> CreateAsync(Cliente cliente)
        {
            if (await ExisteDocumentoAsync(cliente.TipoDocumento, cliente.NumeroDocumento))
                throw new InvalidOperationException("Ya existe un cliente con ese tipo y número de documento.");

            // Calcular PuntajeRiesgo basado en NivelRiesgo (1-5 → 2-10)
            cliente.PuntajeRiesgo = (int)cliente.NivelRiesgo * 2m;

            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cliente creado - Id {ClienteId} - Documento {TipoDocumento} {NumeroDocumento}",
                cliente.Id, cliente.TipoDocumento, cliente.NumeroDocumento);

            return cliente;
        }

        public async Task<Cliente> UpdateAsync(Cliente cliente)
        {
            var clienteExistente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == cliente.Id && !c.IsDeleted);

            if (clienteExistente == null)
                throw new InvalidOperationException("Cliente no encontrado.");

            if (await ExisteDocumentoAsync(cliente.TipoDocumento, cliente.NumeroDocumento, cliente.Id))
                throw new InvalidOperationException("Ya existe un cliente con ese tipo y número de documento.");

            // Datos personales
            clienteExistente.Nombre = cliente.Nombre;
            clienteExistente.Apellido = cliente.Apellido;
            clienteExistente.TipoDocumento = cliente.TipoDocumento;
            clienteExistente.NumeroDocumento = cliente.NumeroDocumento;
            clienteExistente.FechaNacimiento = cliente.FechaNacimiento;
            clienteExistente.EstadoCivil = cliente.EstadoCivil;

            // Datos de cónyuge (opcionales)
            clienteExistente.ConyugeNombreCompleto = cliente.ConyugeNombreCompleto;
            clienteExistente.ConyugeTipoDocumento = cliente.ConyugeTipoDocumento;
            clienteExistente.ConyugeNumeroDocumento = cliente.ConyugeNumeroDocumento;
            clienteExistente.ConyugeTelefono = cliente.ConyugeTelefono;
            clienteExistente.ConyugeSueldo = cliente.ConyugeSueldo;

            clienteExistente.Telefono = cliente.Telefono;
            clienteExistente.Email = cliente.Email;
            clienteExistente.Domicilio = cliente.Domicilio;
            clienteExistente.Provincia = cliente.Provincia;
            clienteExistente.Localidad = cliente.Localidad;

            // Datos laborales / financieros
            clienteExistente.TipoEmpleo = cliente.TipoEmpleo;
            clienteExistente.Sueldo = cliente.Sueldo;

            clienteExistente.TieneReciboSueldo = cliente.TieneReciboSueldo;

            // Calificación crediticia manual (NivelRiesgo 1-5)
            // Se actualiza desde el formulario y el PuntajeRiesgo se calcula automáticamente
            clienteExistente.NivelRiesgo = cliente.NivelRiesgo;
            clienteExistente.PuntajeRiesgo = (int)cliente.NivelRiesgo * 2m; // Convierte 1-5 a 2-10

            // Estado
            clienteExistente.Activo = cliente.Activo;

            clienteExistente.UpdatedAt = DateTime.UtcNow;

            // Si el cliente enviado incluye RowVersion, usarlo para detectar conflictos de concurrencia
            if (cliente.RowVersion != null)
            {
                _context.Entry(clienteExistente).Property("RowVersion").OriginalValue = cliente.RowVersion;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al actualizar cliente {ClienteId}", cliente.Id);
                throw new InvalidOperationException("Conflicto de concurrencia: el cliente fue modificado por otro usuario. Recargá e intentá nuevamente.");
            }

            _logger.LogInformation("Cliente actualizado - Id {ClienteId}", clienteExistente.Id);

            return clienteExistente;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);

            if (cliente == null)
                return false;

            cliente.IsDeleted = true;
            cliente.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Cliente eliminado (soft-delete) - Id {ClienteId}", id);

            return true;
        }

        public async Task<IEnumerable<Cliente>> SearchAsync(
            string? searchTerm,
            string? tipoDocumento,
            bool? soloActivos,
            bool? conCreditosActivos,
            decimal? puntajeMinimo,
            string? orderBy,
            string? orderDirection)
        {
            // QueryFilter no aplica IsDeleted automáticamente.
            var query = _context.Clientes
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                // Necesario para que AutoMapper calcule correctamente CreditosActivos/MontoAdeudado en Index.
                .Include(c => c.Creditos.Where(cr => !cr.IsDeleted && cr.Estado == EstadoCredito.Activo))
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim();

                query = query.Where(c =>
                    (c.Nombre ?? string.Empty).Contains(term) ||
                    (c.Apellido ?? string.Empty).Contains(term) ||
                    (c.NumeroDocumento ?? string.Empty).Contains(term) ||
                    (c.Email ?? string.Empty).Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(tipoDocumento))
                query = query.Where(c => c.TipoDocumento == tipoDocumento);

            if (soloActivos.HasValue)
                query = query.Where(c => c.Activo == soloActivos.Value);

            if (conCreditosActivos.HasValue && conCreditosActivos.Value)
            {
                query = query.Where(c =>
                    c.Creditos.Any(cr => !cr.IsDeleted && cr.Estado == EstadoCredito.Activo));
            }

            if (puntajeMinimo.HasValue)
                query = query.Where(c => c.PuntajeRiesgo >= puntajeMinimo.Value);

            var desc = string.Equals(orderDirection, "desc", StringComparison.OrdinalIgnoreCase);

            query = (orderBy?.Trim().ToLowerInvariant()) switch
            {
                "documento" => desc
                    ? query.OrderByDescending(c => c.NumeroDocumento).ThenByDescending(c => c.TipoDocumento)
                    : query.OrderBy(c => c.NumeroDocumento).ThenBy(c => c.TipoDocumento),

                "nombre" => desc
                    ? query.OrderByDescending(c => c.Apellido).ThenByDescending(c => c.Nombre)
                    : query.OrderBy(c => c.Apellido).ThenBy(c => c.Nombre),

                "puntaje" => desc
                    ? query.OrderByDescending(c => c.PuntajeRiesgo)
                    : query.OrderBy(c => c.PuntajeRiesgo),

                _ => desc
                    ? query.OrderByDescending(c => c.CreatedAt)
                    : query.OrderBy(c => c.CreatedAt)
            };

            return await query.ToListAsync();
        }

        public async Task ActualizarPuntajeRiesgoAsync(int clienteId, decimal nuevoPuntaje, string actualizadoPor)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
                throw new InvalidOperationException("Cliente no encontrado.");

            var puntajeAnterior = cliente.PuntajeRiesgo;
            cliente.PuntajeRiesgo = nuevoPuntaje;
            cliente.UpdatedBy = actualizadoPor;
            cliente.UpdatedAt = DateTime.UtcNow;

            _context.ClientesPuntajeHistorial.Add(new ClientePuntajeHistorial
            {
                ClienteId = clienteId,
                Puntaje = nuevoPuntaje,
                NivelRiesgo = cliente.NivelRiesgo,
                Fecha = DateTime.UtcNow,
                Origen = "ActualizacionManual",
                Observacion = $"Puntaje actualizado de {puntajeAnterior} a {nuevoPuntaje}",
                RegistradoPor = actualizadoPor
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Puntaje de riesgo actualizado - ClienteId {ClienteId} - De {PuntajeAnterior} a {NuevoPuntaje} - Por {Usuario}",
                clienteId, puntajeAnterior, nuevoPuntaje, actualizadoPor);
        }

        public async Task<bool> AsignarNivelCreditoManualAsync(
            int clienteId,
            int nivel,
            string motivo,
            string usuario)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
                return false;

            if (string.IsNullOrWhiteSpace(motivo))
                throw new InvalidOperationException("El motivo del nivel manual es obligatorio.");

            var config = await _context.ClientesCreditoConfiguraciones
                .FirstOrDefaultAsync(c => c.ClienteId == clienteId);

            if (config == null)
            {
                config = new ClienteCreditoConfiguracion
                {
                    ClienteId = clienteId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ClientesCreditoConfiguraciones.Add(config);
            }

            var nivelAnterior = config.NivelCreditoManual;
            config.NivelCreditoManual = nivel;
            config.MotivoNivelCreditoManual = motivo.Trim();
            config.NivelCreditoManualAsignadoPor = usuario;
            config.NivelCreditoManualAsignadoEnUtc = DateTime.UtcNow;
            config.UpdatedAt = DateTime.UtcNow;

            _context.ClientesPuntajeHistorial.Add(new ClientePuntajeHistorial
            {
                ClienteId = clienteId,
                Puntaje = nivel,
                NivelRiesgo = cliente.NivelRiesgo,
                Fecha = DateTime.UtcNow,
                Origen = "PuntajeCreditoManual",
                Observacion = nivelAnterior.HasValue
                    ? $"Puntaje manual actualizado de {nivelAnterior.Value} a {nivel}. Motivo: {motivo.Trim()}"
                    : $"Puntaje manual asignado a {nivel}. Motivo: {motivo.Trim()}",
                RegistradoPor = usuario
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Nivel crediticio manual asignado - ClienteId {ClienteId} - Nivel {Nivel} - Usuario {Usuario}",
                clienteId, nivel, usuario);

            return true;
        }

        public async Task<bool> LimpiarNivelCreditoManualAsync(int clienteId, string motivo, string usuario)
        {
            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
                return false;

            if (string.IsNullOrWhiteSpace(motivo))
                throw new InvalidOperationException("El motivo para limpiar el nivel manual es obligatorio.");

            var config = await _context.ClientesCreditoConfiguraciones
                .FirstOrDefaultAsync(c => c.ClienteId == clienteId);

            if (config == null || !config.NivelCreditoManual.HasValue)
                return true;

            var nivelAnterior = config.NivelCreditoManual.Value;
            config.NivelCreditoManual = null;
            config.MotivoNivelCreditoManual = null;
            config.NivelCreditoManualAsignadoPor = null;
            config.NivelCreditoManualAsignadoEnUtc = null;
            config.UpdatedAt = DateTime.UtcNow;

            _context.ClientesPuntajeHistorial.Add(new ClientePuntajeHistorial
            {
                ClienteId = clienteId,
                Puntaje = cliente.PuntajeRiesgo,
                NivelRiesgo = cliente.NivelRiesgo,
                Fecha = DateTime.UtcNow,
                Origen = "PuntajeCreditoManualLimpio",
                Observacion = $"Puntaje manual {nivelAnterior} limpiado. Motivo: {motivo.Trim()}",
                RegistradoPor = usuario
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Nivel crediticio manual limpiado - ClienteId {ClienteId} - NivelAnterior {NivelAnterior} - Usuario {Usuario}",
                clienteId, nivelAnterior, usuario);

            return true;
        }

        /// <summary>
        /// Últimos N registros de ClientePuntajeHistorial para un cliente, ordenados por fecha
        /// descendente. PuntajeAnterior se deriva del registro cronológicamente previo (no es
        /// una columna persistida).
        /// </summary>
        public async Task<List<ClientePuntajeHistorialItemViewModel>> GetHistorialPuntajeAsync(int clienteId, int top = 5)
        {
            var registros = await _context.ClientesPuntajeHistorial
                .AsNoTracking()
                .Where(h => h.ClienteId == clienteId)
                .OrderByDescending(h => h.Fecha)
                .ThenByDescending(h => h.Id)
                .Take(top + 1)
                .ToListAsync();

            var resultado = new List<ClientePuntajeHistorialItemViewModel>();
            for (int i = 0; i < registros.Count && i < top; i++)
            {
                resultado.Add(new ClientePuntajeHistorialItemViewModel
                {
                    Fecha = registros[i].Fecha,
                    PuntajeNuevo = registros[i].Puntaje,
                    PuntajeAnterior = i + 1 < registros.Count ? registros[i + 1].Puntaje : null,
                    Origen = registros[i].Origen,
                    RegistradoPor = registros[i].RegistradoPor,
                    Observacion = registros[i].Observacion
                });
            }

            return resultado;
        }
    }
}
