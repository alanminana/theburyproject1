using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Motor documental: dado un evento y una operación decide (reglas), renderiza (plantillas),
    /// numera y persiste los documentos. Es el único punto que genera documentos; ningún flujo
    /// (venta, crédito, cobranza) contiene lógica del tipo "si es crédito generar contrato".
    /// </summary>
    public class DocumentoService : IDocumentoService
    {
        private const string TipoContrato = "CONTRATO";
        private const string TipoPagare = "PAGARE";

        private readonly AppDbContext _context;
        private readonly IDocumentoContextoBuilder _contextoBuilder;
        private readonly IDocumentoNumeracionService _numeracion;
        private readonly IDocumentoPdfService _pdf;
        private readonly ICurrentUserService _currentUser;
        private readonly ISeguridadAuditoriaService _auditoria;
        private readonly IRelojComercial _reloj;
        private readonly ILogger<DocumentoService> _logger;

        public DocumentoService(
            AppDbContext context,
            IDocumentoContextoBuilder contextoBuilder,
            IDocumentoNumeracionService numeracion,
            IDocumentoPdfService pdf,
            ICurrentUserService currentUser,
            ISeguridadAuditoriaService auditoria,
            IRelojComercial reloj,
            ILogger<DocumentoService> logger)
        {
            _context = context;
            _contextoBuilder = contextoBuilder;
            _numeracion = numeracion;
            _pdf = pdf;
            _currentUser = currentUser;
            _auditoria = auditoria;
            _reloj = reloj;
            _logger = logger;
        }

        // ------------------------------------------------------------------ Generación

        private sealed class ItemPlan
        {
            public required ReglaDocumento Regla { get; init; }
            public required PlantillaDocumento Plantilla { get; init; }
            public PlantillaDocumentoVersion Version { get; set; } = null!;
            public bool Obligatoria { get; init; }
            public string Clave { get; init; } = string.Empty;
            public string Numero { get; set; } = string.Empty;
            public RenderResultado? Render { get; set; }
            public DocumentoContexto Contexto { get; set; } = null!;
        }

        public Task<DocumentoEventoResultado> ProcesarEventoAsync(DocumentoEventoRequest request, CancellationToken cancellationToken = default)
            => ProcesarEventoInternoAsync(request, 1, cancellationToken);

        private async Task<DocumentoEventoResultado> ProcesarEventoInternoAsync(DocumentoEventoRequest request, int intento, CancellationToken ct)
        {
            if (!EventosDocumentales.Existe(request.Evento))
                throw new DocumentoException(DocumentoErrores.Operacion, $"Evento documental desconocido: {request.Evento}.");

            var evento = EventosDocumentales.Obtener(request.Evento)!.Codigo;
            var resultado = new DocumentoEventoResultado();
            var usuario = string.IsNullOrWhiteSpace(request.Usuario) ? _currentUser.GetUsername() : request.Usuario!.Trim();

            var reglas = await CargarReglasAsync(evento, ct);
            if (reglas.Count == 0)
            {
                _logger.LogDebug("Evento documental {Evento}: sin reglas activas", evento);
                return resultado;
            }

            DocumentoContexto contexto;
            List<ItemPlan> items;
            List<DocumentoErrorItem> errores;
            try
            {
                contexto = request.Contexto ?? await _contextoBuilder.ConstruirAsync(evento, request.Origen);

                if (string.IsNullOrWhiteSpace(contexto.ClaveAncla))
                    contexto.ClaveAncla = DerivarAncla(request.Origen);

                (items, errores) = await PlanificarAsync(evento, reglas, contexto, ct);

                // Idempotencia: lo que ya existe no se vuelve a generar (reintento, doble clic, job repetido).
                var claves = items.Select(i => i.Clave).ToList();
                if (claves.Count > 0)
                {
                    var existentes = await _context.DocumentosGenerados
                        .AsNoTracking()
                        .Include(d => d.TipoDocumento)
                        .Where(d => claves.Contains(d.ClaveIdempotencia))
                        .ToListAsync(ct);

                    foreach (var ya in existentes)
                    {
                        resultado.YaExistentes.Add(ya);
                        items.RemoveAll(i => i.Clave == ya.ClaveIdempotencia);
                    }
                }

                // Un tipo que no admite múltiples no se emite dos veces para la misma operación.
                items = await FiltrarTiposYaEmitidosAsync(items, contexto, resultado, ct);

                ValidarYRenderizarProvisorio(items, contexto, errores);
            }
            catch (Exception ex) when (ex is not DocumentoObligatorioFallidoException and not OperationCanceledException)
            {
                // Nada se escribió todavía. Un error al evaluar (datos de la operación, consulta, plantilla) se
                // trata igual que cualquier documento que no puede generarse: si hay una regla obligatoria
                // activa en este evento el paso falla, si no el flujo continúa y el error queda registrado.
                var esDocumental = ex is DocumentoException;
                var obligatoria = reglas.Any(r => r.Obligatoria);
                if (esDocumental)
                    _logger.LogWarning("Evento documental {Evento}: {Codigo} {Mensaje}", evento, ((DocumentoException)ex).Codigo, ex.Message);
                else
                    _logger.LogError(ex, "Evento documental {Evento}: error inesperado al evaluar los documentos", evento);

                var error = new DocumentoErrorItem(
                    esDocumental ? ((DocumentoException)ex).Codigo : "DOCUMENT_ENGINE_ERROR",
                    esDocumental ? ex.Message : "Error inesperado al evaluar los documentos de la operación.",
                    null, null, obligatoria);

                if (obligatoria)
                    throw new DocumentoObligatorioFallidoException(new[] { error });

                resultado.Errores.Add(error);
                return resultado;
            }

            var obligatorios = errores.Where(e => e.Obligatoria).ToList();
            if (obligatorios.Count > 0)
            {
                _logger.LogWarning("Evento documental {Evento} ({Ancla}): {Cantidad} documento(s) obligatorio(s) fallaron",
                    evento, contexto.ClaveAncla, obligatorios.Count);
                throw new DocumentoObligatorioFallidoException(obligatorios);
            }

            resultado.Errores.AddRange(errores);
            foreach (var e in errores)
                _logger.LogWarning("Documento no obligatorio omitido en {Evento} ({Ancla}): {Codigo} regla={Regla} plantilla={Plantilla}",
                    evento, contexto.ClaveAncla, e.Codigo, e.Regla, e.Plantilla);

            if (items.Count == 0)
                return resultado;

            var transaccionPropia = _context.Database.CurrentTransaction == null;
            await using var tx = transaccionPropia ? await _context.Database.BeginTransactionAsync(ct) : null;

            var nuevos = new List<DocumentoGenerado>();
            var clavesAGenerar = items.Select(i => i.Clave).ToList();
            try
            {
                await NumerarAsync(items, request, ct);
                var ahora = DateTime.UtcNow;
                var grupo = request.GrupoImpresionId ?? Guid.NewGuid();
                resultado.GrupoImpresionId = grupo;

                var numerosPorTipo = items.ToDictionary(i => i.Plantilla.TipoDocumento.Codigo, i => i.Numero, StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    item.Render = RenderizarFinal(item, contexto, numerosPorTipo, usuario);
                    nuevos.Add(CrearDocumento(item, contexto, evento, usuario, ahora, grupo));
                }

                _context.DocumentosGenerados.AddRange(nuevos);
                await _context.SaveChangesAsync(ct);

                if (tx != null)
                    await tx.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                foreach (var doc in nuevos)
                    _context.Entry(doc).State = EntityState.Detached;
                if (tx != null)
                    await tx.RollbackAsync(CancellationToken.None);

                // Carrera real: otra petición generó alguno de estos documentos entre el chequeo y el insert.
                // Se reintenta una vez; el segundo pase los encuentra como ya existentes.
                if (intento == 1 && await ExisteAlgunaClaveAsync(clavesAGenerar, ct))
                {
                    _logger.LogInformation("Evento documental {Evento} ({Ancla}): conflicto de idempotencia, reintentando", evento, contexto.ClaveAncla);
                    return await ProcesarEventoInternoAsync(request, 2, ct);
                }

                var fallo = new DocumentoErrorItem(DocumentoErrores.ErrorNumeracion,
                    "No se pudieron guardar los documentos generados.", null, null, items.Any(i => i.Obligatoria));
                _logger.LogError(ex, "Error al persistir documentos del evento {Evento} ({Ancla})", evento, contexto.ClaveAncla);
                if (fallo.Obligatoria)
                    throw new DocumentoObligatorioFallidoException(new[] { fallo });

                resultado.Errores.Add(fallo);
                return resultado;
            }
            catch
            {
                foreach (var doc in nuevos)
                    if (_context.Entry(doc).State == EntityState.Added)
                        _context.Entry(doc).State = EntityState.Detached;
                if (tx != null)
                    await tx.RollbackAsync(CancellationToken.None);
                throw;
            }

            foreach (var doc in nuevos)
            {
                resultado.Generados.Add(doc);
                _logger.LogInformation(
                    "Documento {Numero} ({Tipo}) generado por {Evento} para {Ancla} (plantilla {Plantilla} v{Version}, regla {Regla})",
                    doc.Numero, items.First(i => i.Numero == doc.Numero).Plantilla.TipoDocumento.Codigo, evento,
                    contexto.ClaveAncla, doc.PlantillaDocumentoId, items.First(i => i.Numero == doc.Numero).Version.Numero, doc.ReglaDocumentoId);

                await _auditoria.RegistrarEventoAsync("documentos", "generar", nameof(DocumentoGenerado),
                    $"{doc.Numero} evento={evento} id={doc.Id}");
            }

            return resultado;
        }

        public Task<DocumentoEventoResultado> ReintentarEventoAsync(string evento, DocumentoOrigen origen)
            => ProcesarEventoAsync(new DocumentoEventoRequest { Evento = evento, Origen = origen });

        private async Task<List<ReglaDocumento>> CargarReglasAsync(string evento, CancellationToken ct)
            => await _context.ReglasDocumento
                .AsNoTracking()
                .AsSplitQuery()
                .Include(r => r.PlantillaDocumento!).ThenInclude(p => p.TipoDocumento)
                .Include(r => r.PaqueteDocumental!).ThenInclude(p => p.Items.Where(i => !i.IsDeleted).OrderBy(i => i.Orden))
                    .ThenInclude(i => i.PlantillaDocumento).ThenInclude(p => p.TipoDocumento)
                .Where(r => r.EventoCodigo == evento && r.Activa)
                .OrderByDescending(r => r.Prioridad).ThenBy(r => r.Id)
                .ToListAsync(ct);

        private async Task<(List<ItemPlan> Items, List<DocumentoErrorItem> Errores)> PlanificarAsync(
            string evento, List<ReglaDocumento> reglas, DocumentoContexto contexto, CancellationToken ct)
        {
            var items = new List<ItemPlan>();
            var errores = new List<DocumentoErrorItem>();
            var gruposCubiertos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);

            foreach (var regla in reglas)
            {
                var parse = CondicionDocumentoParser.Parsear(regla.CondicionJson);
                if (!parse.EsValida)
                {
                    errores.Add(new DocumentoErrorItem(DocumentoErrores.ReglaInvalida,
                        $"La regla '{regla.Nombre}' tiene condiciones inválidas: {string.Join("; ", parse.Errores)}",
                        regla.Nombre, null, regla.Obligatoria));
                    continue;
                }

                var aplica = CondicionDocumentoEvaluador.Evaluar(parse.Condicion, contexto);
                _logger.LogDebug("Regla '{Regla}' (prioridad {Prioridad}) evaluada para {Evento}: {Aplica}",
                    regla.Nombre, regla.Prioridad, evento, aplica);
                if (!aplica)
                    continue;

                if (!string.IsNullOrWhiteSpace(regla.GrupoExclusion))
                {
                    // Regla específica (mayor prioridad) reemplaza a las generales del mismo grupo.
                    if (!gruposCubiertos.Add(regla.GrupoExclusion.Trim()))
                    {
                        _logger.LogDebug("Regla '{Regla}' omitida: el grupo '{Grupo}' ya fue cubierto por una de mayor prioridad",
                            regla.Nombre, regla.GrupoExclusion);
                        continue;
                    }
                }

                var plantillas = new List<PlantillaDocumento>();
                if (regla.PaqueteDocumental != null)
                {
                    if (!regla.PaqueteDocumental.Activo)
                    {
                        errores.Add(new DocumentoErrorItem(DocumentoErrores.PlantillaInactiva,
                            $"El paquete '{regla.PaqueteDocumental.Nombre}' de la regla '{regla.Nombre}' está inactivo.",
                            regla.Nombre, null, regla.Obligatoria));
                        continue;
                    }
                    plantillas.AddRange(regla.PaqueteDocumental.Items.Select(i => i.PlantillaDocumento));
                }
                else if (regla.PlantillaDocumento != null)
                {
                    plantillas.Add(regla.PlantillaDocumento);
                }

                if (plantillas.Count == 0)
                {
                    errores.Add(new DocumentoErrorItem(DocumentoErrores.PlantillaNoEncontrada,
                        $"La regla '{regla.Nombre}' no tiene plantilla ni paquete válido.", regla.Nombre, null, regla.Obligatoria));
                    continue;
                }

                foreach (var plantilla in plantillas)
                {
                    var yaPlanificada = items.FirstOrDefault(i => i.Plantilla.Id == plantilla.Id);
                    if (yaPlanificada != null)
                        continue;

                    if (!plantilla.Activa || !plantilla.TipoDocumento.Activo ||
                        plantilla.VigenteDesde.Date > hoy || (plantilla.VigenteHasta.HasValue && plantilla.VigenteHasta.Value.Date < hoy))
                    {
                        errores.Add(new DocumentoErrorItem(DocumentoErrores.PlantillaInactiva,
                            $"La plantilla '{plantilla.Nombre}' no está activa o vigente.", regla.Nombre, plantilla.Codigo, regla.Obligatoria));
                        continue;
                    }

                    items.Add(new ItemPlan
                    {
                        Regla = regla,
                        Plantilla = plantilla,
                        Obligatoria = regla.Obligatoria,
                        Clave = $"{evento}|{contexto.ClaveAncla}|R{regla.Id}|P{plantilla.Id}"
                    });
                }
            }

            if (items.Count > 0)
            {
                var plantillaIds = items.Select(i => i.Plantilla.Id).ToList();
                var versiones = await _context.PlantillasDocumentoVersion
                    .AsNoTracking()
                    .Where(v => plantillaIds.Contains(v.PlantillaDocumentoId))
                    .ToListAsync(ct);

                foreach (var item in items.ToList())
                {
                    var version = versiones.FirstOrDefault(v => v.PlantillaDocumentoId == item.Plantilla.Id && v.Numero == item.Plantilla.VersionActual);
                    if (version == null)
                    {
                        errores.Add(new DocumentoErrorItem(DocumentoErrores.PlantillaNoEncontrada,
                            $"La plantilla '{item.Plantilla.Nombre}' no tiene una versión vigente.",
                            item.Regla.Nombre, item.Plantilla.Codigo, item.Obligatoria));
                        items.Remove(item);
                        continue;
                    }

                    item.Version = version;
                }
            }

            return (items, errores);
        }

        private async Task<List<ItemPlan>> FiltrarTiposYaEmitidosAsync(
            List<ItemPlan> items, DocumentoContexto contexto, DocumentoEventoResultado resultado, CancellationToken ct)
        {
            var resultadoItems = new List<ItemPlan>();
            var tiposTomados = new HashSet<int>();

            foreach (var item in items)
            {
                var tipo = item.Plantilla.TipoDocumento;
                if (!tipo.PermiteMultiples)
                {
                    if (!tiposTomados.Add(tipo.Id) || await ExisteVigenteDelTipoAsync(tipo.Id, contexto, ct))
                    {
                        resultado.Advertencias.Add($"Ya existe un documento '{tipo.Nombre}' para esta operación; se omitió '{item.Plantilla.Nombre}'.");
                        continue;
                    }
                }

                resultadoItems.Add(item);
            }

            return resultadoItems;
        }

        private Task<bool> ExisteVigenteDelTipoAsync(int tipoId, DocumentoContexto ctx, CancellationToken ct)
        {
            var q = _context.DocumentosGenerados.Where(d =>
                d.TipoDocumentoId == tipoId &&
                d.Estado != EstadoDocumentoGenerado.Cancelado &&
                d.Estado != EstadoDocumentoGenerado.Reemplazado);

            if (ctx.PagoCuotaId is int pago)
                q = q.Where(d => d.PagoCuotaId == pago);
            else if (ctx.CotizacionId is int cot)
                q = q.Where(d => d.CotizacionId == cot);
            else if (ctx.VentaId is int venta)
                q = q.Where(d => d.VentaId == venta && d.PagoCuotaId == null);
            else
                return Task.FromResult(false);

            return q.AnyAsync(ct);
        }

        private void ValidarYRenderizarProvisorio(List<ItemPlan> items, DocumentoContexto contexto, List<DocumentoErrorItem> errores)
        {
            foreach (var item in items.ToList())
            {
                var provisorio = contexto.Clonar();
                provisorio.Set("documento.numero", "(sin asignar)");
                provisorio.Set("documento.numeroContrato", "(sin asignar)");
                provisorio.Set("documento.numeroPagare", "(sin asignar)");

                var faltantes = VariablesFaltantes(item.Version.VariablesRequeridas, provisorio);
                if (faltantes.Count > 0)
                {
                    errores.Add(new DocumentoErrorItem(DocumentoErrores.ContextoIncompleto,
                        $"Faltan datos requeridos para '{item.Plantilla.Nombre}': {string.Join(", ", faltantes)}.",
                        item.Regla.Nombre, item.Plantilla.Codigo, item.Obligatoria));
                    items.Remove(item);
                }
            }
        }

        /// <summary>Variables requeridas (coma-separadas) que no tienen valor en el contexto.</summary>
        public static List<string> VariablesFaltantes(string? requeridas, DocumentoContexto contexto)
        {
            var faltantes = new List<string>();
            foreach (var nombre in (requeridas ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (contexto.Colecciones.TryGetValue(nombre, out var coleccion))
                {
                    if (coleccion.Count == 0) faltantes.Add(nombre);
                    continue;
                }

                if (!contexto.TryResolver(nombre, out var valor) || string.IsNullOrWhiteSpace(PlantillaRenderer.Formatear(valor)))
                    faltantes.Add(nombre);
            }

            return faltantes;
        }

        private async Task NumerarAsync(List<ItemPlan> items, DocumentoEventoRequest request, CancellationToken ct)
        {
            foreach (var item in items)
            {
                var codigo = item.Plantilla.TipoDocumento.Codigo;
                if (request.NumerosPreasignados != null && request.NumerosPreasignados.TryGetValue(codigo, out var preasignado)
                    && !string.IsNullOrWhiteSpace(preasignado))
                {
                    item.Numero = preasignado;
                    continue;
                }

                item.Numero = await _numeracion.SiguienteNumeroAsync(item.Plantilla.TipoDocumento.Id);
            }
        }

        private RenderResultado RenderizarFinal(ItemPlan item, DocumentoContexto contexto, Dictionary<string, string> numerosPorTipo, string usuario)
        {
            var c = contexto.Clonar();
            c.Set("documento.numero", item.Numero);
            c.Set("documento.fecha", _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue));
            c.Set("documento.fechaHora", _reloj.AhoraComercial.ToString("dd/MM/yyyy HH:mm"));
            c.Set("documento.usuario", usuario);
            c.Set("documento.tipo", item.Plantilla.TipoDocumento.Nombre);
            c.Set("documento.numeroContrato", numerosPorTipo.GetValueOrDefault(TipoContrato));
            c.Set("documento.numeroPagare", numerosPorTipo.GetValueOrDefault(TipoPagare));
            item.Contexto = c;
            return PlantillaRenderer.Renderizar(item.Version.Contenido, c);
        }

        private DocumentoGenerado CrearDocumento(ItemPlan item, DocumentoContexto contexto, string evento, string usuario, DateTime ahora, Guid grupo)
        {
            var requiereFirma = item.Plantilla.RequiereFirma || item.Plantilla.TipoDocumento.RequiereFirma;
            var texto = item.Render!.Texto;

            var metadata = new
            {
                regla = item.Regla.Nombre,
                reglaId = item.Regla.Id,
                plantilla = item.Plantilla.Codigo,
                version = item.Version.Numero,
                variablesVacias = item.Render.Vacias,
                variablesNoResueltas = item.Render.NoResueltas
            };

            return new DocumentoGenerado
            {
                TipoDocumentoId = item.Plantilla.TipoDocumentoId,
                PlantillaDocumentoId = item.Plantilla.Id,
                PlantillaDocumentoVersionId = item.Version.Id,
                Numero = item.Numero,
                ClienteId = contexto.ClienteId,
                VentaId = contexto.VentaId,
                CreditoId = contexto.CreditoId,
                CuotaId = contexto.CuotaId,
                PagoCuotaId = contexto.PagoCuotaId,
                CotizacionId = contexto.CotizacionId,
                EventoOrigen = evento,
                ReglaDocumentoId = item.Regla.Id == 0 ? null : item.Regla.Id,
                ClaveIdempotencia = item.Clave,
                GrupoImpresionId = grupo,
                Estado = requiereFirma ? EstadoDocumentoGenerado.PendienteFirma : EstadoDocumentoGenerado.Generado,
                FechaGeneracionUtc = ahora,
                UsuarioGeneracion = usuario,
                ContenidoRenderizado = texto,
                DatosSnapshotJson = item.Contexto.ToSnapshotJson(),
                MetadataJson = JsonSerializer.Serialize(metadata),
                ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))),
                RequiereFirma = requiereFirma,
                FirmantesRequeridos = item.Plantilla.FirmantesRequeridos,
                ExigeFirmaParaContinuar = item.Regla.ExigeFirmaParaContinuar && requiereFirma
            };
        }

        private Task<bool> ExisteAlgunaClaveAsync(List<string> claves, CancellationToken ct)
            => _context.DocumentosGenerados.AsNoTracking().AnyAsync(d => claves.Contains(d.ClaveIdempotencia), ct);

        private static string DerivarAncla(DocumentoOrigen o)
            => o.PagoCuotaId is int p ? $"pago:{p}"
             : o.CotizacionId is int c ? $"cotizacion:{c}"
             : $"venta:{o.VentaId}";

        // ------------------------------------------------------------------ Consultas

        public Task<DocumentoGenerado?> ObtenerAsync(int id)
            => _context.DocumentosGenerados
                .AsNoTracking()
                .Include(d => d.TipoDocumento)
                .Include(d => d.PlantillaDocumento)
                .Include(d => d.PlantillaDocumentoVersion)
                .FirstOrDefaultAsync(d => d.Id == id);

        public async Task<(List<DocumentoGenerado> Items, int Total)> BuscarAsync(DocumentoFiltro filtro)
        {
            var q = _context.DocumentosGenerados.AsNoTracking().AsQueryable();

            if (filtro.TipoDocumentoId is int tipo)
                q = q.Where(d => d.TipoDocumentoId == tipo);
            if (filtro.Estado is EstadoDocumentoGenerado estado)
                q = q.Where(d => d.Estado == estado);
            if (filtro.Desde is DateTime desde)
                q = q.Where(d => d.FechaGeneracionUtc >= desde.Date);
            if (filtro.Hasta is DateTime hasta)
                q = q.Where(d => d.FechaGeneracionUtc < hasta.Date.AddDays(1));

            var texto = filtro.Texto?.Trim();
            if (!string.IsNullOrEmpty(texto))
                q = q.Where(d => d.Numero.Contains(texto)
                    || (d.Cliente != null && (d.Cliente.Nombre.Contains(texto) || d.Cliente.Apellido.Contains(texto)
                        || d.Cliente.NumeroDocumento.Contains(texto))));

            var total = await q.CountAsync();
            var tamano = Math.Clamp(filtro.Tamano, 5, 100);
            var pagina = Math.Max(1, filtro.Pagina);
            var items = await q
                .Include(d => d.TipoDocumento)
                .Include(d => d.Cliente)
                .OrderByDescending(d => d.FechaGeneracionUtc).ThenByDescending(d => d.Id)
                .Skip((pagina - 1) * tamano).Take(tamano)
                .ToListAsync();

            return (items, total);
        }

        private IQueryable<DocumentoGenerado> ConsultaBase()
            => _context.DocumentosGenerados.AsNoTracking().Include(d => d.TipoDocumento).Include(d => d.PlantillaDocumento);

        public Task<List<DocumentoGenerado>> ObtenerPorVentaAsync(int ventaId)
            => ConsultaBase().Where(d => d.VentaId == ventaId).OrderBy(d => d.FechaGeneracionUtc).ThenBy(d => d.Id).ToListAsync();

        public Task<List<DocumentoGenerado>> ObtenerPorCreditoAsync(int creditoId)
            => ConsultaBase().Where(d => d.CreditoId == creditoId).OrderBy(d => d.FechaGeneracionUtc).ThenBy(d => d.Id).ToListAsync();

        public Task<List<DocumentoGenerado>> ObtenerPorPagoAsync(int pagoCuotaId)
            => ConsultaBase().Where(d => d.PagoCuotaId == pagoCuotaId).OrderBy(d => d.FechaGeneracionUtc).ThenBy(d => d.Id).ToListAsync();

        public Task<List<DocumentoGenerado>> ObtenerPorCotizacionAsync(int cotizacionId)
            => ConsultaBase().Where(d => d.CotizacionId == cotizacionId).OrderBy(d => d.FechaGeneracionUtc).ThenBy(d => d.Id).ToListAsync();

        public Task<List<DocumentoGenerado>> ObtenerPorClienteAsync(int clienteId, int take = 100)
            => ConsultaBase().Where(d => d.ClienteId == clienteId).OrderByDescending(d => d.FechaGeneracionUtc).Take(take).ToListAsync();

        public Task<List<DocumentoGenerado>> ObtenerPorGrupoAsync(Guid grupoImpresionId)
            => ConsultaBase().Where(d => d.GrupoImpresionId == grupoImpresionId).OrderBy(d => d.Id).ToListAsync();

        public Task<List<DocumentoGenerado>> ObtenerBloqueantesDeFirmaAsync(int ventaId)
            => ConsultaBase()
                .Where(d => d.VentaId == ventaId && d.ExigeFirmaParaContinuar && d.Estado == EstadoDocumentoGenerado.PendienteFirma)
                .ToListAsync();

        // ------------------------------------------------------------------ Impresión

        private async Task<List<DocumentoGenerado>> CargarParaImprimirAsync(IReadOnlyCollection<int> ids)
        {
            if (ids.Count == 0)
                throw new DocumentoException(DocumentoErrores.Operacion, "No se indicó ningún documento.");

            var docs = await ConsultaBase().Where(d => ids.Contains(d.Id)).ToListAsync();
            if (docs.Count != ids.Distinct().Count())
                throw new DocumentoException(DocumentoErrores.Operacion, "Alguno de los documentos solicitados no existe.");

            // Respeta el orden pedido (contrato antes que pagaré, etc.).
            var orden = ids.ToList();
            return docs.OrderBy(d => orden.IndexOf(d.Id)).ToList();
        }

        public async Task<DocumentoPdfArchivo> VerPdfAsync(IReadOnlyCollection<int> ids)
            => _pdf.GenerarPdf(await CargarParaImprimirAsync(ids));

        public async Task<DocumentoPdfArchivo> ReimprimirAsync(IReadOnlyCollection<int> ids)
        {
            var docs = await CargarParaImprimirAsync(ids);
            var archivo = _pdf.GenerarPdf(docs);

            var ahora = DateTime.UtcNow;
            var idsDocs = docs.Select(d => d.Id).ToList();
            await _context.DocumentosGenerados
                .Where(d => idsDocs.Contains(d.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(d => d.ContadorReimpresiones, d => d.ContadorReimpresiones + 1)
                    .SetProperty(d => d.UltimaReimpresionUtc, ahora));

            await _auditoria.RegistrarEventoAsync("documentos", "reimprimir", nameof(DocumentoGenerado),
                string.Join(",", docs.Select(d => d.Numero)));

            return archivo;
        }

        // ------------------------------------------------------------------ Ciclo de vida

        public async Task CancelarAsync(int id, string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
                throw new DocumentoException(DocumentoErrores.Operacion, "Indique el motivo de la cancelación (mínimo 5 caracteres).");

            var doc = await _context.DocumentosGenerados.FirstOrDefaultAsync(d => d.Id == id)
                ?? throw new DocumentoException(DocumentoErrores.Operacion, "El documento no existe.");

            if (doc.Estado is EstadoDocumentoGenerado.Cancelado or EstadoDocumentoGenerado.Reemplazado)
                throw new DocumentoException(DocumentoErrores.Operacion, "El documento ya fue cancelado o reemplazado.");

            doc.Estado = EstadoDocumentoGenerado.Cancelado;
            doc.CanceladoPor = _currentUser.GetUsername();
            doc.FechaCancelacionUtc = DateTime.UtcNow;
            doc.MotivoCancelacion = motivo.Trim();
            await _context.SaveChangesAsync();

            _logger.LogInformation("Documento {Numero} cancelado por {Usuario}", doc.Numero, doc.CanceladoPor);
            await _auditoria.RegistrarEventoAsync("documentos", "cancelar", nameof(DocumentoGenerado), $"{doc.Numero}: {doc.MotivoCancelacion}");
        }

        public async Task<DocumentoGenerado> RegenerarAsync(int id, string motivo, bool confirmarSobreFirmado = false)
        {
            if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
                throw new DocumentoException(DocumentoErrores.Operacion, "Indique el motivo de la regeneración (mínimo 5 caracteres).");

            var original = await _context.DocumentosGenerados
                .Include(d => d.TipoDocumento)
                .FirstOrDefaultAsync(d => d.Id == id)
                ?? throw new DocumentoException(DocumentoErrores.Operacion, "El documento no existe.");

            if (original.Estado is EstadoDocumentoGenerado.Cancelado or EstadoDocumentoGenerado.Reemplazado)
                throw new DocumentoException(DocumentoErrores.Operacion, "Solo se puede regenerar un documento vigente.");

            if (original.Estado == EstadoDocumentoGenerado.Firmado && !confirmarSobreFirmado)
                throw new DocumentoException(DocumentoErrores.Operacion,
                    "El documento está firmado: regenerarlo lo reemplaza y requiere confirmación explícita.");

            var plantilla = await _context.PlantillasDocumento
                .AsNoTracking()
                .Include(p => p.TipoDocumento)
                .FirstOrDefaultAsync(p => p.Id == original.PlantillaDocumentoId)
                ?? throw new DocumentoException(DocumentoErrores.PlantillaNoEncontrada, "La plantilla del documento ya no existe.");

            // Un documento importado del sistema anterior cuelga de una plantilla de archivo (inactiva a propósito):
            // al regenerarlo se usa la plantilla activa vigente de su mismo tipo.
            if (original.ContratoLegadoId != null)
            {
                plantilla = await _context.PlantillasDocumento
                    .AsNoTracking()
                    .Include(p => p.TipoDocumento)
                    .Where(p => p.TipoDocumentoId == original.TipoDocumentoId && p.Activa && !p.Codigo.EndsWith("_LEGADO"))
                    .OrderBy(p => p.Id)
                    .FirstOrDefaultAsync()
                    ?? throw new DocumentoException(DocumentoErrores.PlantillaNoEncontrada,
                        "No hay una plantilla activa de este tipo para regenerar el documento importado.");
            }

            var hoy = _reloj.HoyComercial.ToDateTime(TimeOnly.MinValue);
            if (!plantilla.Activa || !plantilla.TipoDocumento.Activo || plantilla.VigenteDesde.Date > hoy ||
                (plantilla.VigenteHasta.HasValue && plantilla.VigenteHasta.Value.Date < hoy))
                throw new DocumentoException(DocumentoErrores.PlantillaInactiva, "La plantilla del documento no está activa o vigente.");

            var version = await _context.PlantillasDocumentoVersion.AsNoTracking()
                .FirstOrDefaultAsync(v => v.PlantillaDocumentoId == plantilla.Id && v.Numero == plantilla.VersionActual)
                ?? throw new DocumentoException(DocumentoErrores.PlantillaNoEncontrada, "La plantilla no tiene versión vigente.");

            var origen = new DocumentoOrigen { VentaId = original.VentaId, PagoCuotaId = original.PagoCuotaId, CotizacionId = original.CotizacionId };
            var contexto = await _contextoBuilder.ConstruirAsync(original.EventoOrigen, origen);

            var faltantes = VariablesFaltantes(version.VariablesRequeridas, contexto);
            if (faltantes.Count > 0)
                throw new DocumentoException(DocumentoErrores.ContextoIncompleto,
                    $"Faltan datos requeridos para regenerar: {string.Join(", ", faltantes)}.");

            var regla = original.ReglaDocumentoId is int rid
                ? await _context.ReglasDocumento.AsNoTracking().FirstOrDefaultAsync(r => r.Id == rid)
                : null;

            var transaccionPropia = _context.Database.CurrentTransaction == null;
            await using var tx = transaccionPropia ? await _context.Database.BeginTransactionAsync() : null;

            try
            {
                var usuario = _currentUser.GetUsername();
                var numero = await _numeracion.SiguienteNumeroAsync(plantilla.TipoDocumentoId);

                var item = new ItemPlan
                {
                    Regla = regla ?? new ReglaDocumento { Id = original.ReglaDocumentoId ?? 0, Nombre = "(regeneración)" },
                    Plantilla = plantilla,
                    Version = version,
                    Obligatoria = false,
                    Clave = ClaveDeRegeneracion(original.ClaveIdempotencia),
                    Numero = numero
                };
                item.Render = RenderizarFinal(item, contexto, new Dictionary<string, string> { [plantilla.TipoDocumento.Codigo] = numero }, usuario);

                var nuevo = CrearDocumento(item, contexto, original.EventoOrigen, usuario, DateTime.UtcNow, original.GrupoImpresionId ?? Guid.NewGuid());
                nuevo.ReemplazaADocumentoId = original.Id;
                nuevo.ExigeFirmaParaContinuar = original.ExigeFirmaParaContinuar && nuevo.RequiereFirma;
                _context.DocumentosGenerados.Add(nuevo);
                await _context.SaveChangesAsync();

                original.Estado = EstadoDocumentoGenerado.Reemplazado;
                original.ReemplazadoPorDocumentoId = nuevo.Id;
                original.CanceladoPor = usuario;
                original.FechaCancelacionUtc = DateTime.UtcNow;
                original.MotivoCancelacion = motivo.Trim();
                await _context.SaveChangesAsync();

                if (tx != null)
                    await tx.CommitAsync();

                _logger.LogInformation("Documento {Original} reemplazado por {Nuevo} (regeneración por {Usuario})", original.Numero, nuevo.Numero, usuario);
                await _auditoria.RegistrarEventoAsync("documentos", "regenerar", nameof(DocumentoGenerado),
                    $"{original.Numero} -> {nuevo.Numero}: {motivo.Trim()}");
                return nuevo;
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        // La clave original + sufijo único: una regeneración es un documento nuevo e intencional,
        // no un reintento, así que no debe chocar con el índice de idempotencia.
        private static string ClaveDeRegeneracion(string claveOriginal)
        {
            var baseClave = claveOriginal.Split('#')[0];
            return $"{baseClave}#r{Guid.NewGuid():N}"[..Math.Min(200, baseClave.Length + 10)];
        }

        private const string PrefijoPng = "data:image/png;base64,";
        private const int MaxBase64Firma = 200_000;
        private const int MaxAnchoFirma = 1600;
        private const int MaxAltoFirma = 800;

        /// <summary>Valida una firma manuscrita (PNG real, tamaño y dimensiones acotados). null/vacío = sin imagen.</summary>
        internal static byte[]? ValidarImagenFirma(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl))
                return null;

            if (!dataUrl.StartsWith(PrefijoPng, StringComparison.Ordinal) || dataUrl.Length > MaxBase64Firma + PrefijoPng.Length)
                throw new DocumentoException(DocumentoErrores.Operacion, "La imagen de la firma no es válida o es demasiado grande.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(dataUrl[PrefijoPng.Length..]);
            }
            catch (FormatException)
            {
                throw new DocumentoException(DocumentoErrores.Operacion, "La imagen de la firma no es válida.");
            }

            // PNG real: firma de 8 bytes + chunk IHDR (ancho y alto en big-endian).
            byte[] firmaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (bytes.Length < 24 || !bytes.AsSpan(0, 8).SequenceEqual(firmaPng))
                throw new DocumentoException(DocumentoErrores.Operacion, "La firma debe ser una imagen PNG.");

            var ancho = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            var alto = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            if (ancho <= 0 || alto <= 0 || ancho > MaxAnchoFirma || alto > MaxAltoFirma)
                throw new DocumentoException(DocumentoErrores.Operacion, "Las dimensiones de la firma exceden lo permitido.");

            return bytes;
        }

        public async Task FirmarAsync(int id, string rol, string? firmante, string? imagenFirma = null)
        {
            var imagen = ValidarImagenFirma(imagenFirma);

            if (string.IsNullOrWhiteSpace(rol))
                throw new DocumentoException(DocumentoErrores.Operacion, "Indique el rol de quien firma.");

            var doc = await _context.DocumentosGenerados.FirstOrDefaultAsync(d => d.Id == id)
                ?? throw new DocumentoException(DocumentoErrores.Operacion, "El documento no existe.");

            if (!doc.RequiereFirma)
                throw new DocumentoException(DocumentoErrores.Operacion, "El documento no requiere firma.");
            if (doc.Estado != EstadoDocumentoGenerado.PendienteFirma)
                throw new DocumentoException(DocumentoErrores.Operacion, "El documento no está pendiente de firma.");

            var requeridos = (doc.FirmantesRequeridos ?? string.Empty)
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
            rol = rol.Trim();

            if (requeridos.Count > 0 && !requeridos.Contains(rol, StringComparer.OrdinalIgnoreCase))
                throw new DocumentoException(DocumentoErrores.Operacion, $"El rol '{rol}' no es un firmante de este documento.");

            var firmas = DocumentoPdfService.LeerFirmas(doc.FirmasJson);
            if (firmas.Any(f => string.Equals(f.Rol, rol, StringComparison.OrdinalIgnoreCase)))
                throw new DocumentoException(DocumentoErrores.Operacion, $"El rol '{rol}' ya firmó este documento.");

            var usuario = _currentUser.GetUsername();
            firmas.Add(new FirmaRegistrada
            {
                Rol = rol,
                Firmante = string.IsNullOrWhiteSpace(firmante) ? usuario : firmante.Trim(),
                FechaUtc = DateTime.UtcNow,
                Usuario = usuario,
                ImagenPng = imagen == null ? null : Convert.ToBase64String(imagen),
                HashImagen = imagen == null ? null : Convert.ToHexString(SHA256.HashData(imagen))
            });

            doc.FirmasJson = JsonSerializer.Serialize(firmas);
            var completo = requeridos.Count == 0 || requeridos.All(r => firmas.Any(f => string.Equals(f.Rol, r, StringComparison.OrdinalIgnoreCase)));
            if (completo)
                doc.Estado = EstadoDocumentoGenerado.Firmado;

            await _context.SaveChangesAsync();

            _logger.LogInformation("Documento {Numero} firmado por rol {Rol}; estado {Estado}", doc.Numero, rol, doc.Estado);
            await _auditoria.RegistrarEventoAsync("documentos", "firmar", nameof(DocumentoGenerado),
                $"{doc.Numero} rol={rol}{(imagen != null ? " firma-manuscrita" : string.Empty)}");
        }
    }
}
