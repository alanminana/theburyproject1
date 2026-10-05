using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Documentos;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// Configuración del sistema documental: tipos, plantillas (versionadas), reglas y paquetes.
    /// Toda la lógica y validación vive en <see cref="IDocumentoConfiguracionService"/>; acá solo se
    /// resuelven permisos (uno por área) y la navegación.
    /// </summary>
    [Authorize]
    public class DocumentosConfigController : Controller
    {
        private readonly IDocumentoConfiguracionService _config;
        private readonly IDocumentoPdfService _pdf;
        private readonly ILogger<DocumentosConfigController> _logger;

        public DocumentosConfigController(IDocumentoConfiguracionService config, IDocumentoPdfService pdf, ILogger<DocumentosConfigController> logger)
        {
            _config = config;
            _pdf = pdf;
            _logger = logger;
        }

        /// <summary>Entrada a la configuración: lleva a la primera área que el usuario puede administrar.</summary>
        [HttpGet]
        public IActionResult Index()
        {
            if (User.TienePermiso("documentos", "managetemplates")) return RedirectToAction(nameof(Plantillas));
            if (User.TienePermiso("documentos", "managerules")) return RedirectToAction(nameof(Reglas));
            if (User.TienePermiso("documentos", "managetypes")) return RedirectToAction(nameof(Tipos));
            return Forbid();
        }

        // ------------------------------------------------------------------ Plantillas

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> Plantillas(int? tipoId)
        {
            ViewBag.Tipos = await _config.ListarTiposAsync();
            ViewBag.TipoId = tipoId;
            return View(await _config.ListarPlantillasAsync(tipoId));
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PlantillaEdit(int? id, int? tipoId)
        {
            var input = new PlantillaDocumentoInput { TipoDocumentoId = tipoId ?? 0 };
            if (id is int plantillaId)
            {
                var plantilla = await _config.ObtenerPlantillaAsync(plantillaId);
                if (plantilla == null)
                    return NotFound();

                var version = plantilla.Versiones.FirstOrDefault(v => v.Numero == plantilla.VersionActual);
                input = new PlantillaDocumentoInput
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
                    Contenido = version?.Contenido ?? string.Empty,
                    VariablesRequeridas = version?.VariablesRequeridas
                };
                ViewBag.Versiones = plantilla.Versiones.OrderByDescending(v => v.Numero).ToList();
                ViewBag.VersionActual = plantilla.VersionActual;
            }

            await CargarListasPlantillaAsync();
            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PlantillaEdit(PlantillaDocumentoInput input)
        {
            try
            {
                var plantilla = await _config.GuardarPlantillaAsync(input);
                TempData["Success"] = $"Plantilla \"{plantilla.Nombre}\" guardada (versión {plantilla.VersionActual}).";
                return RedirectToAction(nameof(PlantillaEdit), new { id = plantilla.Id });
            }
            catch (DocumentoException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await CargarListasPlantillaAsync();
            if (input.Id != 0)
            {
                var existente = await _config.ObtenerPlantillaAsync(input.Id);
                ViewBag.Versiones = existente?.Versiones.OrderByDescending(v => v.Numero).ToList();
                ViewBag.VersionActual = existente?.VersionActual;
            }

            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public Task<IActionResult> PlantillaActivar(int id, bool activa)
            => EjecutarAsync(() => _config.SetPlantillaActivaAsync(id, activa),
                activa ? "Plantilla activada." : "Plantilla desactivada.", nameof(Plantillas));

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PlantillaRestaurar(int id, int version)
        {
            try
            {
                var plantilla = await _config.RestaurarVersionAsync(id, version);
                TempData["Success"] = $"Se creó la versión {plantilla.VersionActual} a partir de la versión {version}.";
            }
            catch (DocumentoException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(nameof(PlantillaEdit), new { id });
        }

        /// <summary>Vista previa (AJAX): renderiza el contenido con datos de ejemplo o de una operación real. No guarda nada.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PlantillaPreview(
            string? contenido, string? variablesRequeridas, string? evento,
            int? ventaId, int? pagoCuotaId, int? cotizacionId)
        {
            try
            {
                var preview = await _config.PrevisualizarAsync(
                    contenido ?? string.Empty, variablesRequeridas,
                    string.IsNullOrWhiteSpace(evento) ? EventosDocumentales.ContratoCreditoSolicitado : evento,
                    ventaId, pagoCuotaId, cotizacionId);

                return Json(new
                {
                    success = true,
                    texto = preview.Texto,
                    errores = preview.ErroresPlantilla,
                    vacias = preview.VariablesVacias,
                    noResueltas = preview.VariablesNoResueltas,
                    requeridasFaltantes = preview.RequeridasFaltantes,
                    datosReales = preview.UsaDatosReales,
                    generable = preview.EsGenerable
                });
            }
            catch (DocumentoException ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        private async Task CargarListasPlantillaAsync()
        {
            ViewBag.Tipos = await _config.ListarTiposAsync();
            ViewBag.Eventos = EventosDocumentales.Todos;
            ViewBag.Operaciones = await _config.ListarOperacionesRecientesAsync();
        }

        /// <summary>
        /// Vista previa en PDF (con el mismo formato de impresión que el documento real): renderiza el contenido del editor con una
        /// operación real o datos de ejemplo y lo abre en una pestaña. No guarda nada ni consume numeración.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PlantillaPreviewPdf(
            string? contenido, string? variablesRequeridas, string? evento, string? firmantes,
            int? ventaId, int? pagoCuotaId, int? cotizacionId)
        {
            try
            {
                var preview = await _config.PrevisualizarAsync(
                    contenido ?? string.Empty, variablesRequeridas,
                    string.IsNullOrWhiteSpace(evento) ? EventosDocumentales.ContratoCreditoSolicitado : evento,
                    ventaId, pagoCuotaId, cotizacionId);

                var documento = new DocumentoGenerado
                {
                    Numero = "VISTA-PREVIA",
                    ContenidoRenderizado = preview.Texto,
                    Estado = EstadoDocumentoGenerado.Generado,
                    FechaGeneracionUtc = DateTime.UtcNow,
                    FirmantesRequeridos = firmantes,
                    TipoDocumento = new TipoDocumento { Nombre = "Vista previa" }
                };
                var archivo = _pdf.GenerarPdf(new[] { documento });
                Response.Headers.ContentDisposition = "inline; filename=\"vista-previa.pdf\"";
                return File(archivo.Contenido, archivo.TipoContenido);
            }
            catch (DocumentoException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ------------------------------------------------------------------ Vista previa del resultado final (PDF)

        /// <summary>PDF de una plantilla guardada (versión vigente) con datos de ejemplo. No guarda ni consume numeración.</summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PlantillaVistaPreviaPdf(int id)
        {
            var plantilla = await _config.ObtenerPlantillaAsync(id);
            if (plantilla == null) return NotFound();

            var reglas = await _config.ListarReglasAsync();
            var paquetes = await _config.ListarPaquetesAsync();
            var evento = EventoDe(reglas, plantilla.Id, paquetes.Where(p => p.Items.Any(i => i.PlantillaDocumentoId == plantilla.Id)).Select(p => p.Id));
            return await PdfDePlantillasAsync(new[] { plantilla }, evento, $"vista-previa-{plantilla.Codigo}.pdf");
        }

        /// <summary>PDF del paquete completo: todas sus plantillas en orden, impresas juntas como en la emisión real.</summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> PaqueteVistaPreviaPdf(int id)
        {
            var paquete = await _config.ObtenerPaqueteAsync(id);
            if (paquete == null) return NotFound();

            var plantillas = new List<PlantillaDocumento>();
            foreach (var item in paquete.Items.OrderBy(i => i.Orden))
            {
                var p = await _config.ObtenerPlantillaAsync(item.PlantillaDocumentoId);
                if (p != null) plantillas.Add(p);
            }

            if (plantillas.Count == 0)
                return BadRequest("El paquete no tiene documentos.");

            var reglas = await _config.ListarReglasAsync();
            var evento = EventoDe(reglas, null, new[] { paquete.Id });
            return await PdfDePlantillasAsync(plantillas, evento, $"vista-previa-{paquete.Codigo}.pdf");
        }

        /// <summary>PDF de lo que genera una regla (su plantilla o su paquete) con datos de ejemplo del evento de la regla.</summary>
        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> ReglaVistaPreviaPdf(int id)
        {
            var regla = await _config.ObtenerReglaAsync(id);
            if (regla == null) return NotFound();

            var plantillas = new List<PlantillaDocumento>();
            if (regla.PaqueteDocumentalId is int paqueteId)
            {
                var paquete = await _config.ObtenerPaqueteAsync(paqueteId);
                foreach (var item in paquete?.Items.OrderBy(i => i.Orden) ?? Enumerable.Empty<PaqueteDocumentalItem>())
                {
                    var p = await _config.ObtenerPlantillaAsync(item.PlantillaDocumentoId);
                    if (p != null) plantillas.Add(p);
                }
            }
            else if (regla.PlantillaDocumentoId is int plantillaId)
            {
                var p = await _config.ObtenerPlantillaAsync(plantillaId);
                if (p != null) plantillas.Add(p);
            }

            if (plantillas.Count == 0)
                return BadRequest("La regla no genera ningún documento.");

            return await PdfDePlantillasAsync(plantillas, regla.EventoCodigo, $"vista-previa-regla-{regla.Id}.pdf");
        }

        private static string EventoDe(IEnumerable<ReglaDocumento> reglas, int? plantillaId, IEnumerable<int> paqueteIds)
        {
            var ids = paqueteIds.ToHashSet();
            var regla = reglas.OrderBy(r => r.Activa ? 0 : 1).ThenByDescending(r => r.Prioridad).FirstOrDefault(r =>
                (plantillaId != null && r.PlantillaDocumentoId == plantillaId)
                || (r.PaqueteDocumentalId != null && ids.Contains(r.PaqueteDocumentalId.Value)));
            return regla?.EventoCodigo ?? EventosDocumentales.ContratoCreditoSolicitado;
        }

        private async Task<IActionResult> PdfDePlantillasAsync(IReadOnlyList<PlantillaDocumento> plantillas, string evento, string nombreArchivo)
        {
            try
            {
                var documentos = new List<DocumentoGenerado>();
                foreach (var plantilla in plantillas)
                {
                    var version = plantilla.Versiones.FirstOrDefault(v => v.Numero == plantilla.VersionActual);
                    var preview = await _config.PrevisualizarAsync(version?.Contenido ?? string.Empty, version?.VariablesRequeridas, evento);
                    documentos.Add(new DocumentoGenerado
                    {
                        Numero = "VISTA-PREVIA",
                        ContenidoRenderizado = preview.Texto,
                        Estado = EstadoDocumentoGenerado.Generado,
                        FechaGeneracionUtc = DateTime.UtcNow,
                        FirmantesRequeridos = plantilla.RequiereFirma ? plantilla.FirmantesRequeridos : null,
                        PlantillaDocumento = plantilla,
                        TipoDocumento = plantilla.TipoDocumento ?? new TipoDocumento { Nombre = plantilla.Nombre }
                    });
                }

                var archivo = _pdf.GenerarPdf(documentos);
                Response.Headers.ContentDisposition = $"inline; filename=\"{nombreArchivo}\"";
                return File(archivo.Contenido, archivo.TipoContenido);
            }
            catch (DocumentoException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        // ------------------------------------------------------------------ Reglas

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> Reglas()
        {
            ViewBag.Eventos = EventosDocumentales.Todos;
            return View(await _config.ListarReglasAsync());
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> ReglaEdit(int? id, string? evento)
        {
            var input = new ReglaDocumentoInput
            {
                EventoCodigo = EventosDocumentales.Existe(evento) ? evento! : EventosDocumentales.ContratoCreditoSolicitado
            };

            if (id is int reglaId)
            {
                var regla = await _config.ObtenerReglaAsync(reglaId);
                if (regla == null)
                    return NotFound();

                input = new ReglaDocumentoInput
                {
                    Id = regla.Id,
                    Nombre = regla.Nombre,
                    EventoCodigo = regla.EventoCodigo,
                    CondicionJson = regla.CondicionJson,
                    PlantillaDocumentoId = regla.PlantillaDocumentoId,
                    PaqueteDocumentalId = regla.PaqueteDocumentalId,
                    Prioridad = regla.Prioridad,
                    Activa = regla.Activa,
                    Obligatoria = regla.Obligatoria,
                    GrupoExclusion = regla.GrupoExclusion,
                    ExigeFirmaParaContinuar = regla.ExigeFirmaParaContinuar
                };
            }

            await CargarListasReglaAsync();
            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> ReglaEdit(ReglaDocumentoInput input)
        {
            try
            {
                var regla = await _config.GuardarReglaAsync(input);
                TempData["Success"] = $"Regla \"{regla.Nombre}\" guardada.";
                return RedirectToAction(nameof(Reglas));
            }
            catch (DocumentoException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            await CargarListasReglaAsync();
            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public Task<IActionResult> ReglaActivar(int id, bool activa)
            => EjecutarAsync(() => _config.SetReglaActivaAsync(id, activa),
                activa ? "Regla activada." : "Regla desactivada.", nameof(Reglas));

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public Task<IActionResult> ReglaEliminar(int id)
            => EjecutarAsync(() => _config.EliminarReglaAsync(id), "Regla eliminada.", nameof(Reglas));

        private async Task CargarListasReglaAsync()
        {
            ViewBag.Eventos = EventosDocumentales.Todos;
            ViewBag.Plantillas = await _config.ListarPlantillasAsync();
            ViewBag.Paquetes = await _config.ListarPaquetesAsync();
            ViewBag.Campos = CatalogoDocumento.Campos;
            ViewBag.Operadores = OperadoresDocumento.Todos;
        }

        // ------------------------------------------------------------------ Paquetes

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> Paquetes()
            => View(await _config.ListarPaquetesAsync());

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> PaqueteEdit(int? id)
        {
            var input = new PaqueteDocumentalInput();
            if (id is int paqueteId)
            {
                var paquete = await _config.ObtenerPaqueteAsync(paqueteId);
                if (paquete == null)
                    return NotFound();

                input = new PaqueteDocumentalInput
                {
                    Id = paquete.Id,
                    Codigo = paquete.Codigo,
                    Nombre = paquete.Nombre,
                    Descripcion = paquete.Descripcion,
                    Activo = paquete.Activo,
                    PlantillaIds = paquete.Items.OrderBy(i => i.Orden).Select(i => i.PlantillaDocumentoId).ToList()
                };
            }

            ViewBag.Plantillas = await _config.ListarPlantillasAsync();
            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managerules")]
        public async Task<IActionResult> PaqueteEdit(PaqueteDocumentalInput input)
        {
            try
            {
                var paquete = await _config.GuardarPaqueteAsync(input);
                TempData["Success"] = $"Paquete \"{paquete.Nombre}\" guardado.";
                return RedirectToAction(nameof(Paquetes));
            }
            catch (DocumentoException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            ViewBag.Plantillas = await _config.ListarPlantillasAsync();
            return View(input);
        }

        // ------------------------------------------------------------------ Tipos

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetypes")]
        public async Task<IActionResult> Tipos()
            => View(await _config.ListarTiposAsync());

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetypes")]
        public async Task<IActionResult> TipoEdit(int? id)
        {
            var input = new TipoDocumentoInput();
            if (id is int tipoId)
            {
                var tipo = (await _config.ListarTiposAsync()).FirstOrDefault(t => t.Id == tipoId);
                if (tipo == null)
                    return NotFound();

                input = new TipoDocumentoInput
                {
                    Id = tipo.Id,
                    Codigo = tipo.Codigo,
                    Nombre = tipo.Nombre,
                    Descripcion = tipo.Descripcion,
                    Categoria = tipo.Categoria,
                    Activo = tipo.Activo,
                    PermiteMultiples = tipo.PermiteMultiples,
                    RequiereFirma = tipo.RequiereFirma,
                    Prefijo = tipo.Prefijo
                };
            }

            return View(input);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetypes")]
        public async Task<IActionResult> TipoEdit(TipoDocumentoInput input)
        {
            try
            {
                var tipo = await _config.GuardarTipoAsync(input);
                TempData["Success"] = $"Tipo \"{tipo.Nombre}\" guardado.";
                return RedirectToAction(nameof(Tipos));
            }
            catch (DocumentoException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            return View(input);
        }

        // ------------------------------------------------------------------ Empresa

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> Empresa()
        {
            var e = await _config.ObtenerEmpresaAsync();
            return View(e == null ? new EmpresaInput() : new EmpresaInput
            {
                Nombre = e.Nombre, Cuit = e.Cuit, Dni = e.Dni, Domicilio = e.Domicilio, Ciudad = e.Ciudad,
                Jurisdiccion = e.Jurisdiccion, InteresMoraDiarioPorcentaje = e.InteresMoraDiarioPorcentaje,
                NombreComercial = e.NombreComercial, DomicilioCompleto = e.DomicilioCompleto,
                CondicionFiscalClientePorDefecto = e.CondicionFiscalClientePorDefecto,
                PagareVencimientoModo = e.PagareVencimientoModo, PagareVencimientoDias = e.PagareVencimientoDias
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public async Task<IActionResult> Empresa(EmpresaInput input)
        {
            try
            {
                await _config.GuardarEmpresaAsync(input);
                TempData["Success"] = "Datos de la empresa guardados.";
                return RedirectToAction(nameof(Empresa));
            }
            catch (DocumentoException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
            }

            // El interés por mora es de solo lectura (viene de Configuración → Mora): se recompone al volver a mostrar el form.
            input.InteresMoraDiarioPorcentaje = (await _config.ObtenerEmpresaAsync())?.InteresMoraDiarioPorcentaje ?? 0;
            ModelState.Remove(nameof(EmpresaInput.InteresMoraDiarioPorcentaje));
            return View(input);
        }

        // ------------------------------------------------------------------ Variables

        [HttpGet]
        [PermisoRequerido(Modulo = "documentos", Accion = "managetemplates")]
        public IActionResult Variables()
        {
            ViewBag.Eventos = EventosDocumentales.Todos;
            return View();
        }

        private async Task<IActionResult> EjecutarAsync(Func<Task> accion, string mensajeOk, string redirect)
        {
            try
            {
                await accion();
                TempData["Success"] = mensajeOk;
            }
            catch (DocumentoException ex)
            {
                TempData["Error"] = ex.Message;
            }

            return RedirectToAction(redirect);
        }
    }
}
