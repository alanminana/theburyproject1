using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Filters;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    [Authorize]
    [PermisoRequerido(Modulo = "configuracion", Accion = "view")]
    public class PlantillaContratoCreditoController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUser;

        public PlantillaContratoCreditoController(AppDbContext context, ICurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<IActionResult> Index()
        {
            var plantillas = await _context.PlantillasContratoCredito
                .AsNoTracking()
                .OrderByDescending(p => p.Activa)
                .ThenByDescending(p => p.VigenteDesde)
                .Select(p => new PlantillaContratoCreditoViewModel
                {
                    Id            = p.Id,
                    Nombre        = p.Nombre,
                    Activa        = p.Activa,
                    NombreVendedor        = p.NombreVendedor,
                    DomicilioVendedor     = p.DomicilioVendedor,
                    DniVendedor           = p.DniVendedor,
                    CuitVendedor          = p.CuitVendedor,
                    CiudadFirma           = p.CiudadFirma,
                    Jurisdiccion          = p.Jurisdiccion,
                    InteresMoraDiarioPorcentaje = p.InteresMoraDiarioPorcentaje,
                    VigenteDesde  = p.VigenteDesde,
                    VigenteHasta  = p.VigenteHasta,
                    CreatedAt     = p.CreatedAt,
                    UpdatedAt     = p.UpdatedAt,
                    CreatedBy     = p.CreatedBy,
                    UpdatedBy     = p.UpdatedBy,
                    TextoContrato = p.TextoContrato,
                    TextoPagare   = p.TextoPagare
                })
                .ToListAsync();

            return View(plantillas);
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public IActionResult Create()
        {
            return View("CreateEdit", new PlantillaContratoCreditoViewModel
            {
                Activa       = true,
                VigenteDesde = DateTime.Today
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> Create(PlantillaContratoCreditoViewModel model)
        {
            if (!ModelState.IsValid)
                return View("CreateEdit", model);

            var plantilla = new PlantillaContratoCredito
            {
                Nombre                    = model.Nombre.Trim(),
                Activa                    = model.Activa,
                NombreVendedor            = model.NombreVendedor.Trim(),
                DomicilioVendedor         = model.DomicilioVendedor.Trim(),
                DniVendedor               = model.DniVendedor?.Trim(),
                CuitVendedor              = model.CuitVendedor?.Trim(),
                CiudadFirma               = model.CiudadFirma.Trim(),
                Jurisdiccion              = model.Jurisdiccion.Trim(),
                InteresMoraDiarioPorcentaje = model.InteresMoraDiarioPorcentaje,
                TextoContrato             = model.TextoContrato.Trim(),
                TextoPagare               = model.TextoPagare.Trim(),
                VigenteDesde              = model.VigenteDesde.Date,
                VigenteHasta              = model.VigenteHasta?.Date,
                CreatedBy                 = _currentUser.GetUsername()
            };

            _context.PlantillasContratoCredito.Add(plantilla);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Plantilla \"{plantilla.Nombre}\" creada exitosamente.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> Edit(int id)
        {
            var plantilla = await _context.PlantillasContratoCredito.FindAsync(id);
            if (plantilla == null)
            {
                TempData["Error"] = "Plantilla no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            return View("CreateEdit", new PlantillaContratoCreditoViewModel
            {
                Id                          = plantilla.Id,
                Nombre                      = plantilla.Nombre,
                Activa                      = plantilla.Activa,
                NombreVendedor              = plantilla.NombreVendedor,
                DomicilioVendedor           = plantilla.DomicilioVendedor,
                DniVendedor                 = plantilla.DniVendedor,
                CuitVendedor                = plantilla.CuitVendedor,
                CiudadFirma                 = plantilla.CiudadFirma,
                Jurisdiccion                = plantilla.Jurisdiccion,
                InteresMoraDiarioPorcentaje = plantilla.InteresMoraDiarioPorcentaje,
                TextoContrato               = plantilla.TextoContrato,
                TextoPagare                 = plantilla.TextoPagare,
                VigenteDesde                = plantilla.VigenteDesde,
                VigenteHasta                = plantilla.VigenteHasta,
                CreatedAt                   = plantilla.CreatedAt,
                UpdatedAt                   = plantilla.UpdatedAt,
                CreatedBy                   = plantilla.CreatedBy,
                UpdatedBy                   = plantilla.UpdatedBy
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> Edit(int id, PlantillaContratoCreditoViewModel model)
        {
            if (id != model.Id)
                return BadRequest();

            if (!ModelState.IsValid)
                return View("CreateEdit", model);

            var plantilla = await _context.PlantillasContratoCredito.FindAsync(id);
            if (plantilla == null)
            {
                TempData["Error"] = "Plantilla no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            plantilla.Nombre                      = model.Nombre.Trim();
            plantilla.Activa                      = model.Activa;
            plantilla.NombreVendedor              = model.NombreVendedor.Trim();
            plantilla.DomicilioVendedor           = model.DomicilioVendedor.Trim();
            plantilla.DniVendedor                 = model.DniVendedor?.Trim();
            plantilla.CuitVendedor                = model.CuitVendedor?.Trim();
            plantilla.CiudadFirma                 = model.CiudadFirma.Trim();
            plantilla.Jurisdiccion                = model.Jurisdiccion.Trim();
            plantilla.InteresMoraDiarioPorcentaje = model.InteresMoraDiarioPorcentaje;
            plantilla.TextoContrato               = model.TextoContrato.Trim();
            plantilla.TextoPagare                 = model.TextoPagare.Trim();
            plantilla.VigenteDesde                = model.VigenteDesde.Date;
            plantilla.VigenteHasta                = model.VigenteHasta?.Date;
            plantilla.UpdatedBy                   = _currentUser.GetUsername();

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Plantilla \"{plantilla.Nombre}\" actualizada.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "configuracion", Accion = "update")]
        public async Task<IActionResult> ToggleActiva(int id)
        {
            var plantilla = await _context.PlantillasContratoCredito.FindAsync(id);
            if (plantilla == null)
            {
                TempData["Error"] = "Plantilla no encontrada.";
                return RedirectToAction(nameof(Index));
            }

            plantilla.Activa    = !plantilla.Activa;
            plantilla.UpdatedBy = _currentUser.GetUsername();

            await _context.SaveChangesAsync();

            TempData["Success"] = plantilla.Activa
                ? $"Plantilla \"{plantilla.Nombre}\" activada."
                : $"Plantilla \"{plantilla.Nombre}\" desactivada.";

            return RedirectToAction(nameof(Index));
        }
    }
}
