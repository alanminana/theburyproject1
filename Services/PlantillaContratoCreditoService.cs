using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Services
{
    public class PlantillaContratoCreditoService : IPlantillaContratoCreditoService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<PlantillaContratoCreditoService> _logger;

        public PlantillaContratoCreditoService(
            AppDbContext context,
            ILogger<PlantillaContratoCreditoService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<PlantillaContratoCreditoViewModel> ObtenerParaEdicionAsync()
        {
            var plantilla = await _context.PlantillasContratoCredito
                .AsNoTracking()
                .OrderByDescending(p => p.Activa)
                .ThenByDescending(p => p.VigenteDesde)
                .ThenByDescending(p => p.Id)
                .FirstOrDefaultAsync();

            return plantilla == null
                ? CrearModeloInicial()
                : MapToViewModel(plantilla);
        }

        public async Task<PlantillaContratoCreditoViewModel> GuardarAsync(PlantillaContratoCreditoViewModel model)
        {
            if (model.VigenteHasta.HasValue && model.VigenteHasta.Value.Date < model.VigenteDesde.Date)
                throw new InvalidOperationException("La fecha de fin de vigencia no puede ser anterior al inicio.");

            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                PlantillaContratoCredito plantilla;

                if (model.Id > 0)
                {
                    plantilla = await _context.PlantillasContratoCredito
                        .FirstOrDefaultAsync(p => p.Id == model.Id)
                        ?? throw new InvalidOperationException("La plantilla indicada no existe.");
                }
                else
                {
                    plantilla = new PlantillaContratoCredito();
                    _context.PlantillasContratoCredito.Add(plantilla);
                }

                MapToEntity(model, plantilla);

                if (plantilla.Activa)
                {
                    var otrasActivas = await _context.PlantillasContratoCredito
                        .Where(p => p.Id != plantilla.Id && p.Activa)
                        .ToListAsync();

                    foreach (var otra in otrasActivas)
                        otra.Activa = false;
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation(
                    "Plantilla de contrato de crédito {PlantillaId} guardada. Activa:{Activa}",
                    plantilla.Id,
                    plantilla.Activa);

                return MapToViewModel(plantilla);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private static PlantillaContratoCreditoViewModel CrearModeloInicial()
        {
            return new PlantillaContratoCreditoViewModel
            {
                Nombre = "Contrato de venta - Crédito personal",
                Activa = true,
                VigenteDesde = DateTime.Today,
                InteresMoraDiarioPorcentaje = 0.20m
            };
        }

        private static PlantillaContratoCreditoViewModel MapToViewModel(PlantillaContratoCredito plantilla)
        {
            return new PlantillaContratoCreditoViewModel
            {
                Id = plantilla.Id,
                Nombre = plantilla.Nombre,
                Activa = plantilla.Activa,
                NombreVendedor = plantilla.NombreVendedor,
                DomicilioVendedor = plantilla.DomicilioVendedor,
                DniVendedor = plantilla.DniVendedor,
                CuitVendedor = plantilla.CuitVendedor,
                CiudadFirma = plantilla.CiudadFirma,
                Jurisdiccion = plantilla.Jurisdiccion,
                InteresMoraDiarioPorcentaje = plantilla.InteresMoraDiarioPorcentaje,
                TextoContrato = plantilla.TextoContrato,
                TextoPagare = plantilla.TextoPagare,
                VigenteDesde = plantilla.VigenteDesde.Date,
                VigenteHasta = plantilla.VigenteHasta?.Date,
                CreatedAt = plantilla.CreatedAt,
                UpdatedAt = plantilla.UpdatedAt,
                CreatedBy = plantilla.CreatedBy,
                UpdatedBy = plantilla.UpdatedBy
            };
        }

        private static void MapToEntity(PlantillaContratoCreditoViewModel model, PlantillaContratoCredito plantilla)
        {
            plantilla.Nombre = model.Nombre.Trim();
            plantilla.Activa = model.Activa;
            plantilla.NombreVendedor = model.NombreVendedor.Trim();
            plantilla.DomicilioVendedor = model.DomicilioVendedor.Trim();
            plantilla.DniVendedor = string.IsNullOrWhiteSpace(model.DniVendedor) ? null : model.DniVendedor.Trim();
            plantilla.CuitVendedor = string.IsNullOrWhiteSpace(model.CuitVendedor) ? null : model.CuitVendedor.Trim();
            plantilla.CiudadFirma = model.CiudadFirma.Trim();
            plantilla.Jurisdiccion = model.Jurisdiccion.Trim();
            plantilla.InteresMoraDiarioPorcentaje = model.InteresMoraDiarioPorcentaje;
            plantilla.TextoContrato = model.TextoContrato.Trim();
            plantilla.TextoPagare = model.TextoPagare.Trim();
            plantilla.VigenteDesde = model.VigenteDesde.Date;
            plantilla.VigenteHasta = model.VigenteHasta?.Date;
        }
    }
}
