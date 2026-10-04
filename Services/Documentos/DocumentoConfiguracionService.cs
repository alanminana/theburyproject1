using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services.Documentos
{
    /// <summary>
    /// Administración del sistema documental: tipos, plantillas (con versionado inmutable), reglas,
    /// paquetes y vista previa. Valida todo lo que el usuario configura (contenido, condiciones,
    /// variables) y deja traza de auditoría de cada cambio.
    /// </summary>
    public class DocumentoConfiguracionService : IDocumentoConfiguracionService
    {
        private const int MaxContenido = 100_000;
        private static readonly Regex CodigoRegex = new(@"^[A-Z0-9_]{2,50}$", RegexOptions.Compiled);
        private static readonly Regex PrefijoRegex = new(@"^[A-Z0-9]{2,10}$", RegexOptions.Compiled);

        private readonly AppDbContext _context;
        private readonly IDocumentoContextoBuilder _contextoBuilder;
        private readonly ISeguridadAuditoriaService _auditoria;
        private readonly ILogger<DocumentoConfiguracionService> _logger;

        public DocumentoConfiguracionService(
            AppDbContext context,
            IDocumentoContextoBuilder contextoBuilder,
            ISeguridadAuditoriaService auditoria,
            ILogger<DocumentoConfiguracionService> logger)
        {
            _context = context;
            _contextoBuilder = contextoBuilder;
            _auditoria = auditoria;
            _logger = logger;
        }

        private static DocumentoException Invalido(string mensaje) => new(DocumentoErrores.Operacion, mensaje);

        // ------------------------------------------------------------------ Tipos

        public Task<List<TipoDocumento>> ListarTiposAsync()
            => _context.TiposDocumento.AsNoTracking().OrderBy(t => t.Nombre).ToListAsync();

        public async Task<TipoDocumento> GuardarTipoAsync(TipoDocumentoInput input)
        {
            var codigo = (input.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            var prefijo = (input.Prefijo ?? string.Empty).Trim().ToUpperInvariant();

            if (string.IsNullOrWhiteSpace(input.Nombre))
                throw Invalido("El nombre del tipo es obligatorio.");
            if (!PrefijoRegex.IsMatch(prefijo))
                throw Invalido("El prefijo debe tener entre 2 y 10 letras o números (ej. REC).");

            TipoDocumento tipo;
            var esNuevo = input.Id == 0;
            if (esNuevo)
            {
                if (!CodigoRegex.IsMatch(codigo))
                    throw Invalido("El código debe tener entre 2 y 50 caracteres: letras mayúsculas, números o guion bajo.");
                if (await _context.TiposDocumento.AnyAsync(t => t.Codigo == codigo))
                    throw Invalido($"Ya existe un tipo con el código {codigo}.");

                tipo = new TipoDocumento { Codigo = codigo };
                _context.TiposDocumento.Add(tipo);
            }
            else
            {
                tipo = await _context.TiposDocumento.FirstOrDefaultAsync(t => t.Id == input.Id)
                    ?? throw Invalido("El tipo de documento no existe.");
            }

            tipo.Nombre = input.Nombre.Trim();
            tipo.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();
            tipo.Categoria = input.Categoria;
            tipo.Activo = input.Activo;
            tipo.PermiteMultiples = input.PermiteMultiples;
            tipo.RequiereFirma = input.RequiereFirma;
            tipo.Prefijo = prefijo;

            await _context.SaveChangesAsync();
            await _auditoria.RegistrarEventoAsync("documentos", esNuevo ? "crear-tipo" : "modificar-tipo", nameof(TipoDocumento), tipo.Codigo);
            return tipo;
        }

        // ------------------------------------------------------------------ Plantillas

        public Task<List<PlantillaDocumento>> ListarPlantillasAsync(int? tipoDocumentoId = null)
            => _context.PlantillasDocumento.AsNoTracking()
                .Include(p => p.TipoDocumento)
                .Where(p => tipoDocumentoId == null || p.TipoDocumentoId == tipoDocumentoId)
                .OrderBy(p => p.TipoDocumento.Nombre).ThenBy(p => p.Nombre)
                .ToListAsync();

        public Task<PlantillaDocumento?> ObtenerPlantillaAsync(int id)
            => _context.PlantillasDocumento.AsNoTracking()
                .Include(p => p.TipoDocumento)
                .Include(p => p.Versiones.OrderByDescending(v => v.Numero))
                .FirstOrDefaultAsync(p => p.Id == id);

        public async Task<PlantillaDocumentoVersion?> ObtenerVersionVigenteAsync(int plantillaId)
        {
            var actual = await _context.PlantillasDocumento.AsNoTracking()
                .Where(p => p.Id == plantillaId).Select(p => p.VersionActual).FirstOrDefaultAsync();
            return await _context.PlantillasDocumentoVersion.AsNoTracking()
                .FirstOrDefaultAsync(v => v.PlantillaDocumentoId == plantillaId && v.Numero == actual);
        }

        public async Task<PlantillaDocumento> GuardarPlantillaAsync(PlantillaDocumentoInput input)
        {
            var codigo = (input.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            var contenido = (input.Contenido ?? string.Empty).Replace("\r\n", "\n").Trim();
            var requeridas = NormalizarLista(input.VariablesRequeridas);
            var firmantes = NormalizarLista(input.FirmantesRequeridos, minusculas: true);

            if (string.IsNullOrWhiteSpace(input.Nombre))
                throw Invalido("El nombre de la plantilla es obligatorio.");
            if (input.Copias is < 1 or > 5)
                throw Invalido("Las copias deben estar entre 1 y 5.");
            if (input.VigenteHasta.HasValue && input.VigenteHasta.Value.Date < input.VigenteDesde.Date)
                throw Invalido("La vigencia hasta no puede ser anterior a la vigencia desde.");

            var erroresContenido = ValidarContenido(contenido, requeridas);
            if (erroresContenido.Count > 0)
                throw Invalido(string.Join(" ", erroresContenido));

            if (!await _context.TiposDocumento.AnyAsync(t => t.Id == input.TipoDocumentoId))
                throw Invalido("El tipo de documento no existe.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                PlantillaDocumento plantilla;
                var esNueva = input.Id == 0;
                string accion;

                if (esNueva)
                {
                    if (!CodigoRegex.IsMatch(codigo))
                        throw Invalido("El código debe tener entre 2 y 50 caracteres: letras mayúsculas, números o guion bajo.");
                    if (await _context.PlantillasDocumento.AnyAsync(p => p.Codigo == codigo))
                        throw Invalido($"Ya existe una plantilla con el código {codigo}.");

                    plantilla = new PlantillaDocumento { Codigo = codigo, TipoDocumentoId = input.TipoDocumentoId };
                    _context.PlantillasDocumento.Add(plantilla);
                    accion = "crear-plantilla";
                }
                else
                {
                    plantilla = await _context.PlantillasDocumento.FirstOrDefaultAsync(p => p.Id == input.Id)
                        ?? throw Invalido("La plantilla no existe.");
                    accion = "modificar-plantilla";
                }

                plantilla.Nombre = input.Nombre.Trim();
                plantilla.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();
                plantilla.Activa = input.Activa;
                plantilla.VigenteDesde = DateTime.SpecifyKind(input.VigenteDesde.Date, DateTimeKind.Utc);
                plantilla.VigenteHasta = input.VigenteHasta.HasValue ? DateTime.SpecifyKind(input.VigenteHasta.Value.Date, DateTimeKind.Utc) : null;
                plantilla.RequiereFirma = input.RequiereFirma;
                plantilla.FirmantesRequeridos = string.IsNullOrEmpty(firmantes) ? null : firmantes;
                plantilla.Copias = input.Copias;

                var versionActual = esNueva
                    ? null
                    : await _context.PlantillasDocumentoVersion.AsNoTracking()
                        .FirstOrDefaultAsync(v => v.PlantillaDocumentoId == plantilla.Id && v.Numero == plantilla.VersionActual);

                var cambioContenido = versionActual == null
                    || NormalizarSaltos(versionActual.Contenido) != contenido
                    || (versionActual.VariablesRequeridas ?? string.Empty) != requeridas;

                await _context.SaveChangesAsync();

                if (cambioContenido)
                {
                    var siguiente = (await _context.PlantillasDocumentoVersion.IgnoreQueryFilters()
                        .Where(v => v.PlantillaDocumentoId == plantilla.Id)
                        .MaxAsync(v => (int?)v.Numero) ?? 0) + 1;

                    _context.PlantillasDocumentoVersion.Add(new PlantillaDocumentoVersion
                    {
                        PlantillaDocumentoId = plantilla.Id,
                        Numero = siguiente,
                        Contenido = contenido,
                        VariablesRequeridas = string.IsNullOrEmpty(requeridas) ? null : requeridas,
                        Comentario = string.IsNullOrWhiteSpace(input.ComentarioVersion) ? null : input.ComentarioVersion.Trim()
                    });
                    plantilla.VersionActual = siguiente;
                    await _context.SaveChangesAsync();
                    if (!esNueva) accion = "nueva-version-plantilla";
                }

                await tx.CommitAsync();
                await _auditoria.RegistrarEventoAsync("documentos", accion, nameof(PlantillaDocumento),
                    $"{plantilla.Codigo} v{plantilla.VersionActual}");
                return plantilla;
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                _logger.LogWarning(ex, "Conflicto al guardar la plantilla {Codigo}", codigo);
                throw Invalido("No se pudo guardar la plantilla: otra persona la modificó al mismo tiempo. Recargue e intente de nuevo.");
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        public async Task SetPlantillaActivaAsync(int id, bool activa)
        {
            var plantilla = await _context.PlantillasDocumento.FirstOrDefaultAsync(p => p.Id == id)
                ?? throw Invalido("La plantilla no existe.");
            plantilla.Activa = activa;
            await _context.SaveChangesAsync();
            await _auditoria.RegistrarEventoAsync("documentos", activa ? "activar-plantilla" : "desactivar-plantilla",
                nameof(PlantillaDocumento), plantilla.Codigo);
        }

        public async Task<PlantillaDocumento> RestaurarVersionAsync(int plantillaId, int numeroVersion)
        {
            var plantilla = await _context.PlantillasDocumento.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == plantillaId) ?? throw Invalido("La plantilla no existe.");
            var version = await _context.PlantillasDocumentoVersion.AsNoTracking()
                .FirstOrDefaultAsync(v => v.PlantillaDocumentoId == plantillaId && v.Numero == numeroVersion)
                ?? throw Invalido("La versión indicada no existe.");

            return await GuardarPlantillaAsync(new PlantillaDocumentoInput
            {
                Id = plantilla.Id,
                TipoDocumentoId = plantilla.TipoDocumentoId,
                Codigo = plantilla.Codigo,
                Nombre = plantilla.Nombre,
                Descripcion = plantilla.Descripcion,
                Activa = plantilla.Activa,
                VigenteDesde = plantilla.VigenteDesde,
                VigenteHasta = plantilla.VigenteHasta,
                RequiereFirma = plantilla.RequiereFirma,
                FirmantesRequeridos = plantilla.FirmantesRequeridos,
                Copias = plantilla.Copias,
                Contenido = version.Contenido,
                VariablesRequeridas = version.VariablesRequeridas,
                ComentarioVersion = $"Restaurada desde la versión {numeroVersion}"
            });
        }

        /// <summary>Errores del contenido de una plantilla (sintaxis, variables, requeridas, contenido peligroso).</summary>
        public static List<string> ValidarContenido(string contenido, string? requeridas)
        {
            var errores = new List<string>();
            if (string.IsNullOrWhiteSpace(contenido))
                errores.Add("El contenido de la plantilla es obligatorio.");
            if (contenido.Length > MaxContenido)
                errores.Add($"El contenido supera el máximo de {MaxContenido} caracteres.");
            if (Regex.IsMatch(contenido, @"<\s*(script|iframe|object|embed)\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(contenido, @"javascript\s*:", RegexOptions.IgnoreCase))
                errores.Add("El contenido no puede incluir scripts ni elementos activos.");

            errores.AddRange(PlantillaRenderer.Validar(contenido));

            foreach (var req in (requeridas ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                if (!CatalogoDocumento.EsTokenConocido(req))
                    errores.Add($"La variable requerida '{req}' no existe.");

            return errores;
        }

        private static string NormalizarLista(string? valor, bool minusculas = false)
        {
            var items = (valor ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => minusculas ? x.ToLowerInvariant() : x)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(",", items);
        }

        private static string NormalizarSaltos(string s) => s.Replace("\r\n", "\n").Trim();

        // ------------------------------------------------------------------ Reglas

        public Task<List<ReglaDocumento>> ListarReglasAsync()
            => _context.ReglasDocumento.AsNoTracking()
                .Include(r => r.PlantillaDocumento)
                .Include(r => r.PaqueteDocumental)
                .OrderBy(r => r.EventoCodigo).ThenByDescending(r => r.Prioridad).ThenBy(r => r.Id)
                .ToListAsync();

        public Task<ReglaDocumento?> ObtenerReglaAsync(int id)
            => _context.ReglasDocumento.AsNoTracking()
                .Include(r => r.PlantillaDocumento).Include(r => r.PaqueteDocumental)
                .FirstOrDefaultAsync(r => r.Id == id);

        public async Task<ReglaDocumento> GuardarReglaAsync(ReglaDocumentoInput input)
        {
            if (string.IsNullOrWhiteSpace(input.Nombre))
                throw Invalido("El nombre de la regla es obligatorio.");

            var evento = EventosDocumentales.Obtener(input.EventoCodigo)
                ?? throw Invalido("El evento indicado no existe.");

            if (input.Prioridad is < 0 or > 10000)
                throw Invalido("La prioridad debe estar entre 0 y 10000.");

            if ((input.PlantillaDocumentoId == null) == (input.PaqueteDocumentalId == null))
                throw Invalido("Indique una plantilla o un paquete documental (solo uno de los dos).");

            if (input.PlantillaDocumentoId is int pid && !await _context.PlantillasDocumento.AnyAsync(p => p.Id == pid))
                throw Invalido("La plantilla indicada no existe.");
            if (input.PaqueteDocumentalId is int qid && !await _context.PaquetesDocumentales.AnyAsync(p => p.Id == qid))
                throw Invalido("El paquete indicado no existe.");

            var condicionJson = string.IsNullOrWhiteSpace(input.CondicionJson) ? null : input.CondicionJson.Trim();
            var parse = CondicionDocumentoParser.Parsear(condicionJson);
            if (!parse.EsValida)
                throw Invalido("Condiciones inválidas: " + string.Join(" ", parse.Errores));

            // Los campos de la condición deben estar disponibles en el contexto de ese evento; si no,
            // la condición evaluaría siempre contra un valor vacío sin avisar.
            foreach (var campo in CamposUsados(parse.Condicion))
            {
                var info = CatalogoDocumento.Buscar(campo)!;
                if (info.Anclas != null && !info.Anclas.Contains(evento.Ancla))
                    throw Invalido($"El campo '{info.Etiqueta}' no está disponible para el evento '{evento.Nombre}'.");
            }

            ReglaDocumento regla;
            var esNueva = input.Id == 0;
            if (esNueva)
            {
                regla = new ReglaDocumento();
                _context.ReglasDocumento.Add(regla);
            }
            else
            {
                regla = await _context.ReglasDocumento.FirstOrDefaultAsync(r => r.Id == input.Id)
                    ?? throw Invalido("La regla no existe.");
            }

            regla.Nombre = input.Nombre.Trim();
            regla.EventoCodigo = evento.Codigo;
            regla.CondicionJson = condicionJson;
            regla.PlantillaDocumentoId = input.PlantillaDocumentoId;
            regla.PaqueteDocumentalId = input.PaqueteDocumentalId;
            regla.Prioridad = input.Prioridad;
            regla.Activa = input.Activa;
            regla.Obligatoria = input.Obligatoria;
            regla.GrupoExclusion = string.IsNullOrWhiteSpace(input.GrupoExclusion) ? null : input.GrupoExclusion.Trim();
            regla.ExigeFirmaParaContinuar = input.ExigeFirmaParaContinuar;

            await _context.SaveChangesAsync();
            await _auditoria.RegistrarEventoAsync("documentos", esNueva ? "crear-regla" : "modificar-regla",
                nameof(ReglaDocumento), $"{regla.Nombre} ({regla.EventoCodigo}) id={regla.Id}");
            return regla;
        }

        private static IEnumerable<string> CamposUsados(CondicionDocumento? c)
        {
            if (c == null) yield break;
            if (c.EsGrupo)
            {
                foreach (var h in c.Hijos)
                    foreach (var campo in CamposUsados(h))
                        yield return campo;
            }
            else if (c.Campo != null)
            {
                yield return c.Campo;
            }
        }

        public async Task SetReglaActivaAsync(int id, bool activa)
        {
            var regla = await _context.ReglasDocumento.FirstOrDefaultAsync(r => r.Id == id)
                ?? throw Invalido("La regla no existe.");
            regla.Activa = activa;
            await _context.SaveChangesAsync();
            await _auditoria.RegistrarEventoAsync("documentos", activa ? "activar-regla" : "desactivar-regla",
                nameof(ReglaDocumento), $"{regla.Nombre} id={regla.Id}");
        }

        public async Task EliminarReglaAsync(int id)
        {
            var regla = await _context.ReglasDocumento.FirstOrDefaultAsync(r => r.Id == id)
                ?? throw Invalido("La regla no existe.");
            regla.IsDeleted = true;
            regla.Activa = false;
            await _context.SaveChangesAsync();
            await _auditoria.RegistrarEventoAsync("documentos", "eliminar-regla", nameof(ReglaDocumento), $"{regla.Nombre} id={regla.Id}");
        }

        // ------------------------------------------------------------------ Paquetes

        public Task<List<PaqueteDocumental>> ListarPaquetesAsync()
            => _context.PaquetesDocumentales.AsNoTracking()
                .Include(p => p.Items.OrderBy(i => i.Orden)).ThenInclude(i => i.PlantillaDocumento)
                .OrderBy(p => p.Nombre).ToListAsync();

        public Task<PaqueteDocumental?> ObtenerPaqueteAsync(int id)
            => _context.PaquetesDocumentales.AsNoTracking()
                .Include(p => p.Items.OrderBy(i => i.Orden)).ThenInclude(i => i.PlantillaDocumento)
                .FirstOrDefaultAsync(p => p.Id == id);

        public async Task<PaqueteDocumental> GuardarPaqueteAsync(PaqueteDocumentalInput input)
        {
            var codigo = (input.Codigo ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(input.Nombre))
                throw Invalido("El nombre del paquete es obligatorio.");

            var ids = input.PlantillaIds.Distinct().ToList();
            if (ids.Count == 0)
                throw Invalido("El paquete debe incluir al menos una plantilla.");

            var existentes = await _context.PlantillasDocumento.Where(p => ids.Contains(p.Id)).Select(p => p.Id).ToListAsync();
            if (existentes.Count != ids.Count)
                throw Invalido("Alguna de las plantillas del paquete no existe.");

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                PaqueteDocumental paquete;
                var esNuevo = input.Id == 0;
                if (esNuevo)
                {
                    if (!CodigoRegex.IsMatch(codigo))
                        throw Invalido("El código debe tener entre 2 y 50 caracteres: letras mayúsculas, números o guion bajo.");
                    if (await _context.PaquetesDocumentales.AnyAsync(p => p.Codigo == codigo))
                        throw Invalido($"Ya existe un paquete con el código {codigo}.");
                    paquete = new PaqueteDocumental { Codigo = codigo };
                    _context.PaquetesDocumentales.Add(paquete);
                }
                else
                {
                    paquete = await _context.PaquetesDocumentales.Include(p => p.Items)
                        .FirstOrDefaultAsync(p => p.Id == input.Id) ?? throw Invalido("El paquete no existe.");
                    foreach (var item in paquete.Items.ToList())
                        _context.PaquetesDocumentalesItems.Remove(item);
                }

                paquete.Nombre = input.Nombre.Trim();
                paquete.Descripcion = string.IsNullOrWhiteSpace(input.Descripcion) ? null : input.Descripcion.Trim();
                paquete.Activo = input.Activo;
                await _context.SaveChangesAsync();

                var orden = 1;
                foreach (var plantillaId in ids)
                    _context.PaquetesDocumentalesItems.Add(new PaqueteDocumentalItem
                    {
                        PaqueteDocumentalId = paquete.Id,
                        PlantillaDocumentoId = plantillaId,
                        Orden = orden++
                    });

                await _context.SaveChangesAsync();
                await tx.CommitAsync();
                await _auditoria.RegistrarEventoAsync("documentos", esNuevo ? "crear-paquete" : "modificar-paquete",
                    nameof(PaqueteDocumental), paquete.Codigo);
                return paquete;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ------------------------------------------------------------------ Vista previa

        public async Task<DocumentoPreviewResultado> PrevisualizarAsync(
            string contenido, string? variablesRequeridas, string evento,
            int? ventaId = null, int? pagoCuotaId = null, int? cotizacionId = null)
        {
            var info = EventosDocumentales.Obtener(evento) ?? throw Invalido("El evento indicado no existe.");
            var errores = ValidarContenido(contenido ?? string.Empty, variablesRequeridas);

            var real = ventaId != null || pagoCuotaId != null || cotizacionId != null;
            DocumentoContexto ctx;
            if (real)
            {
                try
                {
                    ctx = await _contextoBuilder.ConstruirAsync(info.Codigo,
                        new DocumentoOrigen { VentaId = ventaId, PagoCuotaId = pagoCuotaId, CotizacionId = cotizacionId });
                }
                catch (DocumentoException ex)
                {
                    errores.Add(ex.Message);
                    ctx = DocumentoEjemplo.Construir(info.Ancla);
                    real = false;
                }
            }
            else
            {
                ctx = DocumentoEjemplo.Construir(info.Ancla);
            }

            ctx.Set("documento.numero", "(sin asignar)");
            ctx.Set("documento.fecha", DateTime.Today);
            ctx.Set("documento.fechaHora", DateTime.Now.ToString("dd/MM/yyyy HH:mm"));
            ctx.Set("documento.tipo", "(vista previa)");

            var render = PlantillaRenderer.Renderizar(contenido ?? string.Empty, ctx);
            return new DocumentoPreviewResultado
            {
                Texto = render.Texto,
                ErroresPlantilla = errores,
                VariablesVacias = render.Vacias,
                VariablesNoResueltas = render.NoResueltas,
                RequeridasFaltantes = DocumentoService.VariablesFaltantes(variablesRequeridas, ctx),
                UsaDatosReales = real
            };
        }
    }
}
