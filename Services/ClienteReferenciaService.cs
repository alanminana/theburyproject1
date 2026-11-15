using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class ClienteReferenciaService : IClienteReferenciaService
    {
        private const int MinDigitosTelefono = 7;
        private const int MaxDigitosTelefono = 14;
        private const int MaxObservaciones = 1000;
        private const string MensajeConcurrencia = "La referencia fue modificada por otro usuario. Recargá la página e intentá de nuevo.";
        private const string MensajeRowVersionFaltante = "Falta la versión de la referencia. Recargá la página e intentá de nuevo.";

        private readonly AppDbContext _context;
        private readonly ILogger<ClienteReferenciaService> _logger;

        public ClienteReferenciaService(AppDbContext context, ILogger<ClienteReferenciaService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<ClienteReferenciaViewModel>> ListarPorClienteAsync(int clienteId)
        {
            var items = await _context.ClienteReferencias
                .AsNoTracking()
                .Where(r => r.ClienteId == clienteId && !r.IsDeleted && !r.Cliente.IsDeleted)
                .OrderBy(r => r.Tipo).ThenBy(r => r.Nombre).ThenBy(r => r.Id)
                .ToListAsync();

            return items.Select(ToViewModel).ToList();
        }

        public async Task<(bool Ok, string? Error, int? Id)> CrearAsync(int clienteId, ClienteReferenciaViewModel modelo, string usuario)
        {
            var cliente = await ObtenerClienteAsync(clienteId);
            if (cliente == null)
                return (false, "El cliente no existe.", null);

            var (error, normalizado) = await ValidarAsync(cliente, modelo, referenciaIdExcluida: null);
            if (error != null)
                return (false, error, null);

            var referencia = new ClienteReferencia
            {
                ClienteId = clienteId,
                EstadoVerificacion = EstadoVerificacionReferencia.Pendiente
            };
            Aplicar(referencia, modelo, normalizado);

            _context.ClienteReferencias.Add(referencia);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Referencia {Id} ({Tipo}) creada para cliente {ClienteId} por {Usuario}",
                referencia.Id, referencia.Tipo, clienteId, usuario);
            return (true, null, referencia.Id);
        }

        public async Task<(bool Ok, string? Error)> EditarAsync(int clienteId, int referenciaId, ClienteReferenciaViewModel modelo, string usuario)
        {
            if (modelo?.RowVersion is not { Length: > 0 })
                return (false, MensajeRowVersionFaltante);

            var referencia = await ObtenerReferenciaAsync(clienteId, referenciaId);
            if (referencia == null)
                return (false, "La referencia no existe.");

            var cliente = await ObtenerClienteAsync(clienteId);
            if (cliente == null)
                return (false, "El cliente no existe.");

            var (error, normalizado) = await ValidarAsync(cliente, modelo, referenciaId);
            if (error != null)
                return (false, error);

            // Si cambia el número o el tipo, la verificación anterior ya no corresponde a lo que se guarda.
            var cambiaIdentidad = referencia.TelefonoNormalizado != normalizado || referencia.Tipo != modelo.Tipo;

            _context.Entry(referencia).Property(r => r.RowVersion).OriginalValue = modelo.RowVersion;

            Aplicar(referencia, modelo, normalizado);
            if (cambiaIdentidad && referencia.EstadoVerificacion != EstadoVerificacionReferencia.Pendiente)
            {
                referencia.EstadoVerificacion = EstadoVerificacionReferencia.Pendiente;
                referencia.FechaVerificacion = null;
                referencia.VerificadaPor = null;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return (false, MensajeConcurrencia);
            }

            _logger.LogInformation("Referencia {Id} del cliente {ClienteId} editada por {Usuario}", referenciaId, clienteId, usuario);
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> DarDeBajaAsync(int clienteId, int referenciaId, string usuario, byte[]? rowVersion)
        {
            if (rowVersion is not { Length: > 0 })
                return (false, MensajeRowVersionFaltante);

            var referencia = await ObtenerReferenciaAsync(clienteId, referenciaId);
            if (referencia == null)
                return (false, "La referencia no existe.");

            _context.Entry(referencia).Property(r => r.RowVersion).OriginalValue = rowVersion;
            referencia.IsDeleted = true;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return (false, MensajeConcurrencia);
            }

            _logger.LogInformation("Referencia {Id} del cliente {ClienteId} dada de baja por {Usuario}", referenciaId, clienteId, usuario);
            return (true, null);
        }

        public async Task<(bool Ok, string? Error)> CambiarVerificacionAsync(
            int clienteId, int referenciaId, EstadoVerificacionReferencia estado, string? observaciones, string usuario, byte[]? rowVersion)
        {
            if (rowVersion is not { Length: > 0 })
                return (false, MensajeRowVersionFaltante);

            if (!Enum.IsDefined(estado))
                return (false, "El estado de verificación no es válido.");

            if (observaciones != null && observaciones.Trim().Length > 1000)
                return (false, "Las observaciones no pueden superar los 1000 caracteres.");

            var referencia = await ObtenerReferenciaAsync(clienteId, referenciaId);
            if (referencia == null)
                return (false, "La referencia no existe.");

            // Las observaciones generales no se pisan: la nota de verificación se agrega con fecha.
            string? observacionesNuevas = null;
            if (!string.IsNullOrWhiteSpace(observaciones))
            {
                var nota = $"[Verificación {DateTime.UtcNow:dd/MM/yyyy}] {observaciones.Trim()}";
                observacionesNuevas = string.IsNullOrWhiteSpace(referencia.Observaciones)
                    ? nota
                    : $"{referencia.Observaciones.Trim()}\n{nota}";
                if (observacionesNuevas.Length > MaxObservaciones)
                    return (false, $"Las observaciones acumuladas superarían los {MaxObservaciones} caracteres. Acortá la nota o editá las observaciones de la referencia.");
            }

            _context.Entry(referencia).Property(r => r.RowVersion).OriginalValue = rowVersion;

            referencia.EstadoVerificacion = estado;
            if (estado == EstadoVerificacionReferencia.Pendiente)
            {
                referencia.FechaVerificacion = null;
                referencia.VerificadaPor = null;
            }
            else
            {
                referencia.FechaVerificacion = DateTime.UtcNow;
                referencia.VerificadaPor = string.IsNullOrWhiteSpace(usuario) ? null : usuario;
            }

            if (observacionesNuevas != null)
                referencia.Observaciones = observacionesNuevas;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return (false, MensajeConcurrencia);
            }

            _logger.LogInformation("Referencia {Id} del cliente {ClienteId}: verificación {Estado} por {Usuario}",
                referenciaId, clienteId, estado, usuario);
            return (true, null);
        }

        // ─────────────────────────────────────────────────────────────────────

        private Task<Cliente?> ObtenerClienteAsync(int clienteId) =>
            _context.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId && !c.IsDeleted);

        private Task<ClienteReferencia?> ObtenerReferenciaAsync(int clienteId, int referenciaId) =>
            _context.ClienteReferencias.FirstOrDefaultAsync(r =>
                r.Id == referenciaId && r.ClienteId == clienteId && !r.IsDeleted && !r.Cliente.IsDeleted);

        private async Task<(string? Error, string Normalizado)> ValidarAsync(
            Cliente cliente, ClienteReferenciaViewModel modelo, int? referenciaIdExcluida)
        {
            if (modelo == null || string.IsNullOrWhiteSpace(modelo.Nombre))
                return ("El nombre es obligatorio.", string.Empty);
            if (string.IsNullOrWhiteSpace(modelo.Telefono))
                return ("El teléfono es obligatorio.", string.Empty);
            if (!Enum.IsDefined(modelo.Tipo))
                return ("El tipo de referencia no es válido.", string.Empty);

            if (modelo.Nombre.Trim().Length > 150) return ("El nombre no puede superar los 150 caracteres.", string.Empty);
            if (modelo.Telefono.Trim().Length > 30) return ("El teléfono no puede superar los 30 caracteres.", string.Empty);
            if ((modelo.Relacion?.Trim().Length ?? 0) > 100) return ("La relación no puede superar los 100 caracteres.", string.Empty);
            if ((modelo.Email?.Trim().Length ?? 0) > 200) return ("El email no puede superar los 200 caracteres.", string.Empty);
            if ((modelo.Empresa?.Trim().Length ?? 0) > 150) return ("La empresa no puede superar los 150 caracteres.", string.Empty);
            if ((modelo.Observaciones?.Trim().Length ?? 0) > 1000) return ("Las observaciones no pueden superar los 1000 caracteres.", string.Empty);

            var cantidadDigitos = modelo.Telefono.Count(char.IsDigit);
            if (cantidadDigitos < MinDigitosTelefono || cantidadDigitos > MaxDigitosTelefono
                || modelo.Telefono.Any(ch => !(char.IsDigit(ch) || char.IsWhiteSpace(ch) || "+().-".Contains(ch))))
            {
                return ($"El teléfono debe tener entre {MinDigitosTelefono} y {MaxDigitosTelefono} dígitos y solo números o los símbolos + - ( ).", string.Empty);
            }

            var normalizado = TelefonoNormalizador.Normalizar(modelo.Telefono);
            if (normalizado.Length == 0)
                return ("El teléfono no es válido.", string.Empty);

            var propios = new[] { cliente.Telefono, cliente.TelefonoAlternativo, cliente.TelefonoLaboral }
                .Select(TelefonoNormalizador.Normalizar)
                .Where(t => t.Length > 0);
            if (propios.Contains(normalizado))
                return ("El teléfono coincide con uno de los teléfonos del propio cliente.", string.Empty);

            var duplicada = await _context.ClienteReferencias.AsNoTracking().AnyAsync(r =>
                r.ClienteId == cliente.Id && !r.IsDeleted
                && r.Tipo == modelo.Tipo
                && r.TelefonoNormalizado == normalizado
                && (referenciaIdExcluida == null || r.Id != referenciaIdExcluida));
            if (duplicada)
                return ("Ya existe una referencia de ese tipo con el mismo teléfono para este cliente.", string.Empty);

            return (null, normalizado);
        }

        private static void Aplicar(ClienteReferencia destino, ClienteReferenciaViewModel origen, string normalizado)
        {
            destino.Tipo = origen.Tipo;
            destino.Nombre = origen.Nombre.Trim();
            destino.Relacion = Limpiar(origen.Relacion);
            destino.Telefono = origen.Telefono.Trim();
            destino.TelefonoNormalizado = normalizado;
            destino.Email = Limpiar(origen.Email);
            // La empresa solo aplica a referencias laborales y comerciales.
            destino.Empresa = origen.Tipo == TipoReferenciaCliente.Personal ? null : Limpiar(origen.Empresa);
            destino.Observaciones = Limpiar(origen.Observaciones);
        }

        private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

        private static ClienteReferenciaViewModel ToViewModel(ClienteReferencia r) => new()
        {
            Id = r.Id,
            ClienteId = r.ClienteId,
            Tipo = r.Tipo,
            Nombre = r.Nombre,
            Relacion = r.Relacion,
            Telefono = r.Telefono,
            Email = r.Email,
            Empresa = r.Empresa,
            Observaciones = r.Observaciones,
            EstadoVerificacion = r.EstadoVerificacion,
            FechaVerificacion = r.FechaVerificacion,
            VerificadaPor = r.VerificadaPor,
            RowVersion = r.RowVersion
        };
    }
}
