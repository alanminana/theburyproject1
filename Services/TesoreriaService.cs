using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Serializa por origen de fondos: cada pago o anticipo lo toma al validar el origen (dentro de la transacción del proveedor) y la baja
    /// del origen lo toma antes de contar sus movimientos. Orden de locks: proveedor -> origen (la baja solo toma el origen: sin ciclo).
    /// SQL Server: UPDLOCK, HOLDLOCK, ROWLOCK sobre la PK; SQLite de tests: no-op (un único escritor).
    /// </summary>
    internal static class OrigenConcurrencia
    {
        public static async Task BloquearAsync(AppDbContext context, int origenId, CancellationToken cancellationToken = default)
        {
            if (!context.Database.IsSqlServer())
                return;

            await context.Database
                .SqlQuery<int>($"SELECT [Id] AS [Value] FROM [OrigenesFondos] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [Id] = {origenId}")
                .ToListAsync(cancellationToken);
        }
    }

    public class TesoreriaService : ITesoreriaService
    {
        private static readonly Regex CodigoValido = new(@"^[A-Z0-9][A-Z0-9_\-]*$", RegexOptions.Compiled);

        private readonly AppDbContext _context;
        private readonly IRelojComercial _reloj;
        private readonly ILogger<TesoreriaService> _logger;
        private readonly ISeguridadAuditoriaService? _auditoria;

        public TesoreriaService(AppDbContext context, IRelojComercial reloj, ILogger<TesoreriaService> logger,
            ISeguridadAuditoriaService? auditoria = null)
        {
            _context = context;
            _reloj = reloj;
            _logger = logger;
            _auditoria = auditoria;
        }

        public async Task<List<OrigenFondosItem>> ListarOrigenesAsync(bool soloActivos = false)
        {
            var origenes = await _context.OrigenesFondos.AsNoTracking()
                .Where(o => !o.IsDeleted && (!soloActivos || o.Activo))
                .OrderBy(o => o.Orden).ThenBy(o => o.Nombre)
                .ToListAsync();
            var ids = origenes.Select(o => o.Id).ToList();
            var porPagos = await _context.PagosProveedor.AsNoTracking()
                .Where(p => p.OrigenFondosId != null && ids.Contains(p.OrigenFondosId.Value))
                .GroupBy(p => p.OrigenFondosId!.Value).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync();
            var porAnticipos = await _context.AnticiposProveedor.AsNoTracking()
                .Where(a => ids.Contains(a.OrigenFondosId))
                .GroupBy(a => a.OrigenFondosId).Select(g => new { Id = g.Key, N = g.Count() }).ToListAsync();

            return origenes.Select(o => ToItem(o,
                porPagos.Where(x => x.Id == o.Id).Sum(x => x.N) + porAnticipos.Where(x => x.Id == o.Id).Sum(x => x.N))).ToList();
        }

        public async Task<OrigenFondosItem?> ObtenerOrigenAsync(int id) =>
            (await ListarOrigenesAsync()).FirstOrDefault(o => o.Id == id);

        public async Task<ResultadoCxp> GuardarOrigenAsync(OrigenFondosRequest request, string usuario)
        {
            if (request is null)
                return ResultadoCxp.Falla("Datos inválidos.");

            var codigo = (request.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            if (codigo.Length == 0)
                return ResultadoCxp.Falla("El código es obligatorio.");
            if (codigo.Length > 30 || !CodigoValido.IsMatch(codigo))
                return ResultadoCxp.Falla("El código admite letras, números, guion y guion bajo (hasta 30 caracteres).");
            var nombre = (request.Nombre ?? string.Empty).Trim();
            if (nombre.Length == 0)
                return ResultadoCxp.Falla("El nombre es obligatorio.");
            if (nombre.Length > 100)
                return ResultadoCxp.Falla("El nombre no puede superar los 100 caracteres.");
            if (!Enum.IsDefined(request.Tipo))
                return ResultadoCxp.Falla("El tipo de origen no es válido.");
            var descripcion = string.IsNullOrWhiteSpace(request.Descripcion) ? null : request.Descripcion.Trim();
            if (descripcion is { Length: > 500 })
                return ResultadoCxp.Falla("La descripción no puede superar los 500 caracteres.");
            if (request.Orden is < 0 or > 9999)
                return ResultadoCxp.Falla("El orden debe estar entre 0 y 9999.");

            var duplicado = await _context.OrigenesFondos.AsNoTracking()
                .AnyAsync(o => !o.IsDeleted && o.Codigo == codigo && o.Id != (request.Id ?? 0));
            if (duplicado)
                return ResultadoCxp.Falla($"Ya existe un origen de fondos con el código {codigo}.");

            try
            {
                OrigenFondos origen;
                string accion;
                if (request.Id.HasValue)
                {
                    origen = await _context.OrigenesFondos.FirstOrDefaultAsync(o => o.Id == request.Id.Value && !o.IsDeleted)
                        ?? null!;
                    if (origen is null)
                        return ResultadoCxp.Falla("El origen de fondos no existe.");
                    if (request.RowVersion is null || request.RowVersion.Length == 0)
                        return ResultadoCxp.Falla("Falta el control de concurrencia: recargá la pantalla.");
                    _context.Entry(origen).Property(o => o.RowVersion).OriginalValue = request.RowVersion;
                    accion = "EditarOrigenFondos";
                }
                else
                {
                    origen = new OrigenFondos { CreatedBy = usuario };
                    _context.OrigenesFondos.Add(origen);
                    accion = "CrearOrigenFondos";
                }

                origen.Codigo = codigo;
                origen.Nombre = nombre;
                origen.Tipo = request.Tipo;
                origen.Descripcion = descripcion;
                origen.Activo = request.Activo;
                origen.Orden = request.Orden;
                if (request.Id.HasValue)
                    origen.UpdatedBy = usuario;

                _auditoria?.AgregarEvento("Tesoreria", accion, $"OrigenFondos:{codigo}",
                    $"{nombre}; tipo {request.Tipo}; {(request.Activo ? "activo" : "inactivo")}; por {usuario}");
                await _context.SaveChangesAsync();
                return ResultadoCxp.Exito(origen.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                _context.ChangeTracker.Clear();
                return ResultadoCxp.Falla("Otro usuario modificó este origen de fondos. Recargá la pantalla e intentá nuevamente.");
            }
            catch (DbUpdateException ex)
            {
                _context.ChangeTracker.Clear();
                _logger.LogWarning(ex, "No se pudo guardar el origen de fondos {Codigo}", codigo);
                return ResultadoCxp.Falla($"No se pudo guardar el origen: puede que otro usuario haya creado uno con el código {codigo}. Recargá e intentá nuevamente.");
            }
        }

        public async Task<ResultadoCxp> EliminarOrigenAsync(int id, string usuario)
        {
            // La comprobación «sin movimientos» y la baja ocurren bajo el MISMO lock del origen que toma cada pago o anticipo al
            // validarlo (OrigenConcurrencia): sin él, un egreso registrado entre la lectura y la baja quedaría con un origen eliminado.
            await using var transaccion = await _context.Database.BeginTransactionAsync();
            try
            {
                await OrigenConcurrencia.BloquearAsync(_context, id);
                var item = await ObtenerOrigenAsync(id);
                if (item is null)
                {
                    await transaccion.RollbackAsync();
                    return ResultadoCxp.Falla("El origen de fondos no existe.");
                }
                if (item.Movimientos > 0)
                {
                    await transaccion.RollbackAsync();
                    return ResultadoCxp.Falla("El origen tiene pagos o anticipos registrados: no se puede eliminar, desactivalo para que deje de ofrecerse.");
                }

                var origen = await _context.OrigenesFondos.FirstAsync(o => o.Id == id);
                origen.IsDeleted = true;
                origen.UpdatedBy = usuario;
                _auditoria?.AgregarEvento("Tesoreria", "EliminarOrigenFondos", $"OrigenFondos:{origen.Codigo}", $"Eliminado por {usuario}");
                await _context.SaveChangesAsync();
                await transaccion.CommitAsync();
                return ResultadoCxp.Exito(id);
            }
            catch (DbUpdateException)
            {
                await transaccion.RollbackAsync(CancellationToken.None);
                _context.ChangeTracker.Clear();
                return ResultadoCxp.Falla("No se pudo eliminar el origen: otro usuario lo modificó o ya tiene movimientos. Recargá e intentá nuevamente.");
            }
        }

        public async Task<ResumenTesoreria> ObtenerResumenAsync(DateTime? desde, DateTime? hasta)
        {
            var hoy = _reloj.InicioDiaComercial;
            var d = (desde ?? new DateTime(hoy.Year, hoy.Month, 1)).Date;
            var h = (hasta ?? hoy).Date;
            if (h < d)
                (d, h) = (h, d);

            var pagos = await _context.PagosProveedor.AsNoTracking()
                .Where(p => !p.Anulado && p.Fecha >= d && p.Fecha <= h)
                .Select(p => new { p.OrigenFondosId, p.Monto }).ToListAsync();
            var anticipos = await _context.AnticiposProveedor.AsNoTracking()
                .Where(a => !a.Anulado && a.Fecha >= d && a.Fecha <= h)
                .Select(a => new { a.OrigenFondosId, a.Monto }).ToListAsync();
            // Defensivo: un origen dado de baja que (por datos anteriores o una carrera) tenga egresos sigue apareciendo en el resumen,
            // para que ningún egreso vigente quede fuera de los totales.
            var conEgresos = pagos.Where(p => p.OrigenFondosId.HasValue).Select(p => p.OrigenFondosId!.Value)
                .Concat(anticipos.Select(a => a.OrigenFondosId)).Distinct().ToList();
            var origenes = (await _context.OrigenesFondos.AsNoTracking().Where(o => !o.IsDeleted || conEgresos.Contains(o.Id))
                .Select(o => new { o.Id, o.Nombre, o.Orden, o.IsDeleted }).ToListAsync())
                .Select(o => new { o.Id, Nombre = o.IsDeleted ? o.Nombre + " (eliminado)" : o.Nombre, o.Orden }).ToList();

            var filas = new List<EgresosOrigenFondos>();
            foreach (var o in origenes.OrderBy(o => o.Orden).ThenBy(o => o.Nombre))
            {
                var ps = pagos.Where(p => p.OrigenFondosId == o.Id).ToList();
                var an = anticipos.Where(a => a.OrigenFondosId == o.Id).ToList();
                filas.Add(new EgresosOrigenFondos
                {
                    OrigenFondosId = o.Id, Nombre = o.Nombre, Pagos = ps.Sum(p => p.Monto), Anticipos = an.Sum(a => a.Monto),
                    Cantidad = ps.Count + an.Count
                });
            }

            var sinOrigen = pagos.Where(p => p.OrigenFondosId is null).ToList();
            if (sinOrigen.Count > 0)
                filas.Add(new EgresosOrigenFondos
                {
                    OrigenFondosId = null, Nombre = "Sin origen (pagos anteriores a Tesorería)", Pagos = sinOrigen.Sum(p => p.Monto),
                    Cantidad = sinOrigen.Count
                });

            return new ResumenTesoreria { Desde = d, Hasta = h, Origenes = filas };
        }

        private static OrigenFondosItem ToItem(OrigenFondos o, int movimientos) => new()
        {
            Id = o.Id, Codigo = o.Codigo, Nombre = o.Nombre, Tipo = o.Tipo, Descripcion = o.Descripcion, Activo = o.Activo,
            Orden = o.Orden, RowVersion = o.RowVersion, Movimientos = movimientos
        };
    }
}
