using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    public class ClienteDatosContractualesService : IClienteDatosContractualesService
    {
        // Mismos topes que Cliente (StringLength) y mismos campos que ContratoVentaCreditoService.ValidarCliente.
        private static readonly (string Propiedad, string Etiqueta, int Max, Func<ClienteDatosContractualesDto, string> Leer)[] Campos =
        {
            (nameof(ClienteDatosContractualesDto.Nombre), "Nombre", 100, d => d.Nombre),
            (nameof(ClienteDatosContractualesDto.Apellido), "Apellido", 100, d => d.Apellido),
            (nameof(ClienteDatosContractualesDto.TipoDocumento), "Tipo de documento", 20, d => d.TipoDocumento),
            (nameof(ClienteDatosContractualesDto.NumeroDocumento), "Número de documento", 20, d => d.NumeroDocumento),
            (nameof(ClienteDatosContractualesDto.Domicilio), "Domicilio", 200, d => d.Domicilio),
            (nameof(ClienteDatosContractualesDto.Localidad), "Localidad", 100, d => d.Localidad),
            (nameof(ClienteDatosContractualesDto.Telefono), "Teléfono", 20, d => d.Telefono),
        };

        private readonly AppDbContext _context;
        private readonly ILogger<ClienteDatosContractualesService> _logger;

        public ClienteDatosContractualesService(AppDbContext context, ILogger<ClienteDatosContractualesService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ClienteDatosContractualesEstado?> ObtenerAsync(int clienteId)
        {
            var cliente = await _context.Clientes
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

            if (cliente == null)
                return null;

            var datos = new ClienteDatosContractualesDto
            {
                ClienteId = cliente.Id,
                Nombre = cliente.Nombre ?? string.Empty,
                Apellido = cliente.Apellido ?? string.Empty,
                TipoDocumento = cliente.TipoDocumento ?? string.Empty,
                NumeroDocumento = cliente.NumeroDocumento ?? string.Empty,
                Domicilio = cliente.Domicilio ?? string.Empty,
                Localidad = cliente.Localidad ?? string.Empty,
                Telefono = cliente.Telefono ?? string.Empty,
            };

            return new ClienteDatosContractualesEstado
            {
                Datos = datos,
                Faltantes = Campos
                    .Where(c => string.IsNullOrWhiteSpace(c.Leer(datos)))
                    .Select(c => c.Propiedad)
                    .ToList()
            };
        }

        public async Task<ClienteDatosContractualesResultado> ActualizarAsync(ClienteDatosContractualesDto datos)
        {
            var limpio = new ClienteDatosContractualesDto
            {
                ClienteId = datos.ClienteId,
                Nombre = (datos.Nombre ?? string.Empty).Trim(),
                Apellido = (datos.Apellido ?? string.Empty).Trim(),
                TipoDocumento = (datos.TipoDocumento ?? string.Empty).Trim(),
                NumeroDocumento = (datos.NumeroDocumento ?? string.Empty).Trim(),
                Domicilio = (datos.Domicilio ?? string.Empty).Trim(),
                Localidad = (datos.Localidad ?? string.Empty).Trim(),
                Telefono = (datos.Telefono ?? string.Empty).Trim(),
            };

            var cliente = await _context.Clientes
                .FirstOrDefaultAsync(c => c.Id == limpio.ClienteId && !c.IsDeleted);

            if (cliente == null)
                return new ClienteDatosContractualesResultado { Errores = { "Cliente no encontrado." } };

            // El documento es la identidad del cliente (y de él se arma el CUIL): si ya está cargado
            // no se modifica desde acá, sólo se completa cuando falta. Se cambia en la edición del cliente.
            var conservaDocumento = !string.IsNullOrWhiteSpace(cliente.TipoDocumento)
                && !string.IsNullOrWhiteSpace(cliente.NumeroDocumento);
            if (conservaDocumento)
            {
                limpio.TipoDocumento = cliente.TipoDocumento;
                limpio.NumeroDocumento = cliente.NumeroDocumento;
            }

            var errores = new List<string>();
            foreach (var campo in Campos)
            {
                var valor = campo.Leer(limpio);
                if (string.IsNullOrWhiteSpace(valor))
                    errores.Add($"{campo.Etiqueta} es obligatorio.");
                else if (valor.Length > campo.Max)
                    errores.Add($"{campo.Etiqueta} no puede superar {campo.Max} caracteres.");
            }

            // Formato: las mismas reglas que el formulario completo de Cliente (nombre, documento, teléfono).
            if (errores.Count == 0)
            {
                var formato = new List<ValidationResult>();
                Validator.TryValidateObject(limpio, new ValidationContext(limpio), formato, validateAllProperties: true);
                errores.AddRange(formato
                    .Where(r => !(conservaDocumento && r.MemberNames.Contains(nameof(limpio.NumeroDocumento))))
                    .Select(r => r.ErrorMessage ?? "Dato inválido."));
            }

            if (errores.Count > 0)
                return new ClienteDatosContractualesResultado { Errores = errores };

            var documentoCambio = cliente.TipoDocumento != limpio.TipoDocumento
                || cliente.NumeroDocumento != limpio.NumeroDocumento;

            if (documentoCambio && await _context.Clientes.AnyAsync(c =>
                    c.Id != cliente.Id && !c.IsDeleted &&
                    c.TipoDocumento == limpio.TipoDocumento &&
                    c.NumeroDocumento == limpio.NumeroDocumento))
            {
                return new ClienteDatosContractualesResultado
                {
                    Errores = { "Ya existe otro cliente con ese tipo y número de documento." }
                };
            }

            cliente.Nombre = limpio.Nombre;
            cliente.Apellido = limpio.Apellido;
            cliente.TipoDocumento = limpio.TipoDocumento;
            cliente.NumeroDocumento = limpio.NumeroDocumento;
            cliente.Domicilio = limpio.Domicilio;
            cliente.Localidad = limpio.Localidad;
            cliente.Telefono = limpio.Telefono;
            cliente.UpdatedAt = DateTime.UtcNow;
            // Completar estos datos equivale a revisarlos (aviso periódico de actualización).
            cliente.FechaUltimaActualizacionDatos = cliente.UpdatedAt;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Conflicto de concurrencia al completar datos contractuales del cliente {ClienteId}", cliente.Id);
                return new ClienteDatosContractualesResultado
                {
                    Errores = { "El cliente fue modificado por otro usuario. Volvé a abrir el formulario e intentá de nuevo." }
                };
            }

            _logger.LogInformation("Datos contractuales completados - Cliente {ClienteId}", cliente.Id);
            return new ClienteDatosContractualesResultado { Exito = true };
        }
    }
}
