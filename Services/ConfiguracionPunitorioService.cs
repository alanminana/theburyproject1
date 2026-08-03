using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.DTOs;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Exceptions;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;

namespace TheBuryProject.Services
{
    /// <summary>
    /// Implementación canónica de <see cref="IConfiguracionPunitorioService"/> (PUN-ML3).
    ///
    /// Versionado append-only con un único invariante estructural: cada versión nueva debe tener
    /// <c>VigenteDesde</c> estrictamente posterior a la de cualquier versión ya existente. Esto
    /// resuelve de una sola vez tres de los casos pedidos: "dos versiones con la misma vigencia" y
    /// "una versión anterior a otra ya existente" quedan rechazados por la misma comparación
    /// (&lt;=), y la selección de vigente ("última con VigenteDesde &lt;= fecha") queda garantizada
    /// libre de empates.
    ///
    /// La verificación previa (lectura + comparación) da un mensaje de rechazo específico; el
    /// índice único filtrado sobre <c>VigenteDesde</c> (ver AppDbContext) es la garantía real contra
    /// una carrera concurrente — dos altas simultáneas con la misma vigencia: una gana, la otra
    /// recibe <see cref="DbUpdateException"/>, que se traduce acá a un rechazo explícito en vez de
    /// dejar escapar la excepción de EF/ADO.NET cruda.
    /// </summary>
    public sealed class ConfiguracionPunitorioService : IConfiguracionPunitorioService
    {
        private readonly AppDbContext _context;
        private readonly IRelojComercial _reloj;

        public ConfiguracionPunitorioService(AppDbContext context, IRelojComercial reloj)
        {
            _context = context;
            _reloj = reloj;
        }

        public async Task<ConfiguracionPunitorioVigente> ObtenerVigenteAsync(
            DateOnly fechaComercial, CancellationToken cancellationToken = default)
        {
            var version = await _context.ConfiguracionesPunitorio
                .AsNoTracking()
                .Where(c => !c.IsDeleted && c.VigenteDesde <= fechaComercial)
                .OrderByDescending(c => c.VigenteDesde)
                .ThenByDescending(c => c.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (version is null)
                return ConfiguracionPunitorioVigente.SinConfiguracion;

            var estado = version.Activa
                ? EstadoConfiguracionPunitorio.Activa
                : EstadoConfiguracionPunitorio.Inactiva;

            return new ConfiguracionPunitorioVigente(estado, version);
        }

        public async Task<ConfiguracionPunitorio> CrearNuevaVersionAsync(
            ConfiguracionPunitorioComando comando, CancellationToken cancellationToken = default)
        {
            ValidarValores(comando);

            var hoy = _reloj.HoyComercial;
            var esRetroactiva = comando.VigenteDesde < hoy;

            if (esRetroactiva)
                ValidarRetroactividad(comando);

            var ultimaVigencia = await _context.ConfiguracionesPunitorio
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.VigenteDesde)
                .Select(c => (DateOnly?)c.VigenteDesde)
                .FirstOrDefaultAsync(cancellationToken);

            if (ultimaVigencia.HasValue && comando.VigenteDesde <= ultimaVigencia.Value)
            {
                var motivoTexto = comando.VigenteDesde == ultimaVigencia.Value ? "igual a" : "anterior a";
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.Conflicto,
                    $"Ya existe una versión con vigencia {ultimaVigencia.Value:yyyy-MM-dd}. El versionado es " +
                    $"append-only: no se puede crear una versión con vigencia {motivoTexto} una ya existente.");
            }

            var nueva = new ConfiguracionPunitorio
            {
                Porcentaje = comando.Porcentaje,
                PeriodoDias = comando.PeriodoDias,
                DiasGracia = comando.DiasGracia,
                ProrrateoDiario = comando.ProrrateoDiario,
                AplicacionRetroactiva = comando.AplicacionRetroactiva,
                VigenteDesde = comando.VigenteDesde,
                Activa = comando.Activa,
                MotivoCambio = comando.MotivoCambio
            };

            _context.ConfiguracionesPunitorio.Add(nueva);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Única causa realista de un DbUpdateException en un INSERT de una fila nueva sin
                // RowVersion propio todavía asignado: el índice único filtrado de VigenteDesde
                // (carrera concurrente que pasó la verificación previa antes de que la otra confirmara).
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.Conflicto,
                    "Ya existe una versión con esa vigencia (conflicto de concurrencia). Recargá el historial e intentá nuevamente.",
                    ex);
            }

            return nueva;
        }

        public async Task<IReadOnlyList<ConfiguracionPunitorio>> ListarHistorialAsync(
            CancellationToken cancellationToken = default)
        {
            return await _context.ConfiguracionesPunitorio
                .AsNoTracking()
                .Where(c => !c.IsDeleted)
                .OrderByDescending(c => c.VigenteDesde)
                .ToListAsync(cancellationToken);
        }

        private static void ValidarValores(ConfiguracionPunitorioComando comando)
        {
            if (comando.Porcentaje < 0m)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.SolicitudInvalida,
                    "El porcentaje no puede ser negativo.");

            if (comando.PeriodoDias < 1)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.SolicitudInvalida,
                    "El período debe ser de al menos 1 día.");

            if (comando.DiasGracia < 0)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.SolicitudInvalida,
                    "Los días de gracia no pueden ser negativos.");

            if (!comando.ProrrateoDiario)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.SolicitudInvalida,
                    "ProrrateoDiario=false no está soportado: el calculador vigente (PUN-ML4) exige prorrateo diario.");

            if (comando.VigenteDesde == default)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.SolicitudInvalida,
                    "La fecha de vigencia es obligatoria.");
        }

        private static void ValidarRetroactividad(ConfiguracionPunitorioComando comando)
        {
            if (!comando.AplicacionRetroactiva)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.Conflicto,
                    "Una vigencia anterior a hoy requiere AplicacionRetroactiva=true.");

            if (string.IsNullOrWhiteSpace(comando.MotivoCambio))
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.Conflicto,
                    "Una vigencia retroactiva requiere motivo.");

            if (!comando.AutorizadoParaRetroactivo)
                throw new ConfiguracionPunitorioRechazadaException(
                    MotivoRechazoConfiguracionPunitorio.Conflicto,
                    "Una vigencia retroactiva requiere autorización administrativa.");
        }
    }
}
