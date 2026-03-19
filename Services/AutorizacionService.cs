using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services;

/// <summary>
/// Implementación del servicio de autorizaciones y umbrales
/// </summary>
public class AutorizacionService : IAutorizacionService
{
    private readonly AppDbContext _context;

    public AutorizacionService(AppDbContext context)
    {
        _context = context;
    }

    #region Gestión de Umbrales

    public async Task<List<UmbralAutorizacion>> ObtenerTodosUmbralesAsync()
    {
        return await _context.UmbralesAutorizacion
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.Rol)
            .ThenBy(u => u.TipoUmbral)
            .ToListAsync();
    }

    public async Task<List<UmbralAutorizacion>> ObtenerUmbralesPorRolAsync(string rol)
    {
        return await _context.UmbralesAutorizacion
            .Where(u => !u.IsDeleted && u.Rol == rol && u.Activo)
            .OrderBy(u => u.TipoUmbral)
            .ToListAsync();
    }

    public async Task<UmbralAutorizacion?> ObtenerUmbralAsync(int id)
    {
        return await _context.UmbralesAutorizacion
            .FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted);
    }

    public async Task<UmbralAutorizacion> CrearUmbralAsync(UmbralAutorizacion umbral)
    {
        if (!Roles.GetAllRoles().Contains(umbral.Rol))
        {
            throw new InvalidOperationException($"El rol {umbral.Rol} no es válido para umbrales de autorización.");
        }

        // Verificar si ya existe un umbral para ese rol y tipo
        var existente = await _context.UmbralesAutorizacion
            .FirstOrDefaultAsync(u => u.Rol == umbral.Rol &&
                                     u.TipoUmbral == umbral.TipoUmbral &&
                                     !u.IsDeleted);

        if (existente != null)
        {
            throw new InvalidOperationException(
                $"Ya existe un umbral de {umbral.TipoUmbral} para el rol {umbral.Rol}");
        }

        _context.UmbralesAutorizacion.Add(umbral);
        await _context.SaveChangesAsync();
        return umbral;
    }

    public async Task<UmbralAutorizacion> ActualizarUmbralAsync(UmbralAutorizacion umbral)
    {
        var existente = await ObtenerUmbralAsync(umbral.Id);
        if (existente == null)
        {
            throw new KeyNotFoundException($"Umbral con ID {umbral.Id} no encontrado");
        }

        existente.ValorMaximo = umbral.ValorMaximo;
        existente.Descripcion = umbral.Descripcion;
        existente.Activo = umbral.Activo;
        existente.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return existente;
    }

    public async Task EliminarUmbralAsync(int id)
    {
        var umbral = await ObtenerUmbralAsync(id);
        if (umbral == null)
        {
            throw new KeyNotFoundException($"Umbral con ID {id} no encontrado");
        }

        umbral.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    #endregion

    #region Validación de Autorizaciones

    public async Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarDescuentoVentaAsync(string rol, decimal descuento)
    {
        return await ValidarUmbralAsync(rol, TipoUmbral.DescuentoVenta, descuento, "descuento en venta");
    }

    public async Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarMontoVentaAsync(string rol, decimal monto)
    {
        return await ValidarUmbralAsync(rol, TipoUmbral.MontoTotalVenta, monto, "monto de venta");
    }

    public async Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarMontoCreditoAsync(string rol, decimal monto)
    {
        return await ValidarUmbralAsync(rol, TipoUmbral.MontoCredito, monto, "monto de crédito");
    }

    public async Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarDescuentoCompraAsync(string rol, decimal descuento)
    {
        return await ValidarUmbralAsync(rol, TipoUmbral.DescuentoCompra, descuento, "descuento en compra");
    }

    private async Task<(bool Permitido, decimal ValorPermitido, string Mensaje)> ValidarUmbralAsync(
        string rol, TipoUmbral tipoUmbral, decimal valor, string descripcionOperacion)
    {
        // Roles administrativos siempre tienen permisos ilimitados
        if (Roles.IsAdminRole(rol))
        {
            return (true, decimal.MaxValue, "Autorizado - Rol administrativo");
        }

        if (!Roles.GetAllRoles().Contains(rol))
        {
            return (false, 0, $"El rol {rol} no es válido para validar umbrales.");
        }

        // Buscar umbral configurado para el rol
        var umbral = await _context.UmbralesAutorizacion
            .FirstOrDefaultAsync(u => u.Rol == rol &&
                                     u.TipoUmbral == tipoUmbral &&
                                     u.Activo &&
                                     !u.IsDeleted);

        // Si no hay umbral configurado, denegar por defecto
        if (umbral == null)
        {
            return (false, 0,
                $"No hay umbral configurado de {descripcionOperacion} para el rol {rol}. " +
                "Solicite autorización a un superior.");
        }

        // Validar si el valor está dentro del umbral
        if (valor <= umbral.ValorMaximo)
        {
            return (true, umbral.ValorMaximo, "Autorizado");
        }

        // Valor excede el umbral
        return (false, umbral.ValorMaximo,
            $"El {descripcionOperacion} de ${valor:N2} excede el límite permitido de ${umbral.ValorMaximo:N2}. " +
            "Solicite autorización a un superior.");
    }

    #endregion

    #region Gestión de Solicitudes

    public async Task<List<SolicitudAutorizacion>> ObtenerTodasSolicitudesAsync()
    {
        return await _context.SolicitudesAutorizacion
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<SolicitudAutorizacion>> ObtenerSolicitudesPendientesAsync()
    {
        return await _context.SolicitudesAutorizacion
            .Where(s => !s.IsDeleted && s.Estado == EstadoSolicitud.Pendiente)
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<SolicitudAutorizacion>> ObtenerSolicitudesPorUsuarioAsync(string usuario)
    {
        return await _context.SolicitudesAutorizacion
            .Where(s => !s.IsDeleted && s.UsuarioSolicitante == usuario)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<SolicitudAutorizacion?> ObtenerSolicitudAsync(int id)
    {
        return await _context.SolicitudesAutorizacion
            .FirstOrDefaultAsync(s => s.Id == id && !s.IsDeleted);
    }

    public async Task<SolicitudAutorizacion> CrearSolicitudAsync(SolicitudAutorizacion solicitud)
    {
        solicitud.Estado = EstadoSolicitud.Pendiente;
        solicitud.FechaResolucion = null;
        solicitud.UsuarioAutorizador = null;
        solicitud.ComentarioResolucion = null;

        _context.SolicitudesAutorizacion.Add(solicitud);
        await _context.SaveChangesAsync();
        return solicitud;
    }

    public async Task<SolicitudAutorizacion> AprobarSolicitudAsync(int id, string autorizador, string? comentario = null)
    {
        var solicitud = await ObtenerSolicitudAsync(id);
        if (solicitud == null)
        {
            throw new KeyNotFoundException($"Solicitud con ID {id} no encontrada");
        }

        if (solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            throw new InvalidOperationException($"La solicitud ya fue {solicitud.Estado}");
        }

        solicitud.Estado = EstadoSolicitud.Aprobada;
        solicitud.UsuarioAutorizador = autorizador;
        solicitud.FechaResolucion = DateTime.UtcNow;
        solicitud.ComentarioResolucion = comentario ?? "Aprobado";
        solicitud.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return solicitud;
    }

    public async Task<SolicitudAutorizacion> RechazarSolicitudAsync(int id, string autorizador, string comentario)
    {
        var solicitud = await ObtenerSolicitudAsync(id);
        if (solicitud == null)
        {
            throw new KeyNotFoundException($"Solicitud con ID {id} no encontrada");
        }

        if (solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            throw new InvalidOperationException($"La solicitud ya fue {solicitud.Estado}");
        }

        solicitud.Estado = EstadoSolicitud.Rechazada;
        solicitud.UsuarioAutorizador = autorizador;
        solicitud.FechaResolucion = DateTime.UtcNow;
        solicitud.ComentarioResolucion = comentario;
        solicitud.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return solicitud;
    }

    public async Task<SolicitudAutorizacion> CancelarSolicitudAsync(int id)
    {
        var solicitud = await ObtenerSolicitudAsync(id);
        if (solicitud == null)
        {
            throw new KeyNotFoundException($"Solicitud con ID {id} no encontrada");
        }

        if (solicitud.Estado != EstadoSolicitud.Pendiente)
        {
            throw new InvalidOperationException($"Solo se pueden cancelar solicitudes pendientes");
        }

        solicitud.Estado = EstadoSolicitud.Cancelada;
        solicitud.FechaResolucion = DateTime.UtcNow;
        solicitud.ComentarioResolucion = "Cancelada por el solicitante";
        solicitud.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return solicitud;
    }

    #endregion
}