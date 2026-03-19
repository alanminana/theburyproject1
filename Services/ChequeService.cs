using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheBuryProject.Data;
using TheBuryProject.Models.Entities;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Services
{
    public class ChequeService : IChequeService
    {
        private readonly AppDbContext _context;
        private readonly ILogger<ChequeService> _logger;

        public ChequeService(AppDbContext context, ILogger<ChequeService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<Cheque>> GetAllAsync()
        {
            return await _context.Cheques
                .Where(c => !c.IsDeleted)
                .AsNoTracking()
                .Include(c => c.Proveedor)
                .Include(c => c.OrdenCompra)
                .OrderByDescending(c => c.FechaEmision)
                .ToListAsync();
        }

        public async Task<Cheque?> GetByIdAsync(int id)
        {
            return await _context.Cheques
                .AsNoTracking()
                .Include(c => c.Proveedor)
                .Include(c => c.OrdenCompra)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        }

        public async Task<Cheque> CreateAsync(Cheque cheque)
        {
            if (await NumeroExisteAsync(cheque.Numero))
                throw new InvalidOperationException($"Ya existe un cheque con el número {cheque.Numero}");

            var proveedor = await _context.Proveedores.FirstOrDefaultAsync(p => p.Id == cheque.ProveedorId && !p.IsDeleted);
            if (proveedor == null)
                throw new InvalidOperationException("El proveedor especificado no existe");

            if (cheque.OrdenCompraId.HasValue)
            {
                var orden = await _context.OrdenesCompra.FirstOrDefaultAsync(o => o.Id == cheque.OrdenCompraId.Value && !o.IsDeleted);
                if (orden == null)
                    throw new InvalidOperationException("La orden de compra especificada no existe");
                if (orden.ProveedorId != cheque.ProveedorId)
                    throw new InvalidOperationException("La orden de compra no pertenece al proveedor seleccionado");
            }

            if (cheque.FechaVencimiento.HasValue && cheque.FechaVencimiento.Value < cheque.FechaEmision)
                throw new InvalidOperationException("La fecha de vencimiento no puede ser anterior a la fecha de emisión");

            _context.Cheques.Add(cheque);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cheque {Numero} creado exitosamente", cheque.Numero);
            return cheque;
        }

        public async Task<Cheque> UpdateAsync(Cheque cheque)
        {
            if (cheque.RowVersion == null || cheque.RowVersion.Length == 0)
                throw new InvalidOperationException("Falta información de concurrencia (RowVersion). Recargá el cheque e intentá nuevamente.");

            var chequeExistente = await _context.Cheques
                .Include(c => c.Proveedor)
                .Include(c => c.OrdenCompra)
                .FirstOrDefaultAsync(c => c.Id == cheque.Id && !c.IsDeleted);

            if (chequeExistente == null)
                throw new InvalidOperationException("El cheque no existe");

            if (await NumeroExisteAsync(cheque.Numero, cheque.Id))
                throw new InvalidOperationException($"Ya existe otro cheque con el número {cheque.Numero}");

            if (cheque.FechaVencimiento.HasValue && cheque.FechaVencimiento.Value < cheque.FechaEmision)
                throw new InvalidOperationException("La fecha de vencimiento no puede ser anterior a la fecha de emisión");

            // Validar proveedor y orden como en Create
            var proveedor = await _context.Proveedores.FirstOrDefaultAsync(p => p.Id == cheque.ProveedorId && !p.IsDeleted);
            if (proveedor == null)
                throw new InvalidOperationException("El proveedor especificado no existe");

            if (cheque.OrdenCompraId.HasValue)
            {
                var orden = await _context.OrdenesCompra.FirstOrDefaultAsync(o => o.Id == cheque.OrdenCompraId.Value && !o.IsDeleted);
                if (orden == null)
                    throw new InvalidOperationException("La orden de compra especificada no existe");
                if (orden.ProveedorId != cheque.ProveedorId)
                    throw new InvalidOperationException("La orden de compra no pertenece al proveedor seleccionado");
            }

            _context.Entry(chequeExistente).Property(c => c.RowVersion).OriginalValue = cheque.RowVersion;

            // Actualizar propiedades
            chequeExistente.Numero = cheque.Numero;
            chequeExistente.Banco = cheque.Banco;
            chequeExistente.Monto = cheque.Monto;
            chequeExistente.FechaEmision = cheque.FechaEmision;
            chequeExistente.FechaVencimiento = cheque.FechaVencimiento;
            chequeExistente.Estado = cheque.Estado;
            chequeExistente.ProveedorId = cheque.ProveedorId;
            chequeExistente.OrdenCompraId = cheque.OrdenCompraId;
            chequeExistente.Observaciones = cheque.Observaciones;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new InvalidOperationException(
                    "El cheque fue modificado por otro usuario. Recargá la página y volvé a intentar.");
            }

            _logger.LogInformation("Cheque {Numero} actualizado exitosamente", cheque.Numero);
            return chequeExistente;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var cheque = await _context.Cheques.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (cheque == null)
                return false;

            if (cheque.Estado == EstadoCheque.Cobrado || cheque.Estado == EstadoCheque.Depositado)
                throw new InvalidOperationException("No se puede eliminar un cheque que está depositado o cobrado");

            cheque.IsDeleted = true;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cheque {Id} eliminado exitosamente", id);
            return true;
        }

        public async Task<IEnumerable<Cheque>> SearchAsync(
            string? searchTerm = null,
            int? proveedorId = null,
            EstadoCheque? estado = null,
            DateTime? fechaEmisionDesde = null,
            DateTime? fechaEmisionHasta = null,
            DateTime? fechaVencimientoDesde = null,
            DateTime? fechaVencimientoHasta = null,
            bool soloVencidos = false,
            bool soloPorVencer = false,
            string? orderBy = null,
            string? orderDirection = "asc")
        {
            var query = _context.Cheques
                .AsNoTracking()
                .Include(c => c.Proveedor)
                .Include(c => c.OrdenCompra)
                .AsQueryable();

            query = query.Where(c => !c.IsDeleted);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(c =>
                    c.Numero.Contains(searchTerm) ||
                    c.Banco.Contains(searchTerm) ||
                    (c.Proveedor != null && (
                        c.Proveedor.RazonSocial.Contains(searchTerm) ||
                        (c.Proveedor.NombreFantasia != null && c.Proveedor.NombreFantasia.Contains(searchTerm))
                    )));
            }

            if (proveedorId.HasValue)
                query = query.Where(c => c.ProveedorId == proveedorId.Value);

            if (estado.HasValue)
                query = query.Where(c => c.Estado == estado.Value);

            if (fechaEmisionDesde.HasValue)
                query = query.Where(c => c.FechaEmision >= fechaEmisionDesde.Value);

            if (fechaEmisionHasta.HasValue)
                query = query.Where(c => c.FechaEmision <= fechaEmisionHasta.Value);

            if (fechaVencimientoDesde.HasValue)
                query = query.Where(c => c.FechaVencimiento.HasValue &&
                                         c.FechaVencimiento.Value >= fechaVencimientoDesde.Value);

            if (fechaVencimientoHasta.HasValue)
                query = query.Where(c => c.FechaVencimiento.HasValue &&
                                         c.FechaVencimiento.Value <= fechaVencimientoHasta.Value);

            if (soloVencidos)
            {
                var hoy = DateTime.Today;
                query = query.Where(c => c.FechaVencimiento.HasValue &&
                                         c.FechaVencimiento.Value < hoy &&
                                         c.Estado != EstadoCheque.Cobrado &&
                                         c.Estado != EstadoCheque.Rechazado &&
                                         c.Estado != EstadoCheque.Anulado);
            }

            if (soloPorVencer)
            {
                var hoy = DateTime.Today;
                var limite = hoy.AddDays(7);
                query = query.Where(c => c.FechaVencimiento.HasValue &&
                                         c.FechaVencimiento.Value >= hoy &&
                                         c.FechaVencimiento.Value <= limite &&
                                         c.Estado != EstadoCheque.Cobrado &&
                                         c.Estado != EstadoCheque.Rechazado &&
                                         c.Estado != EstadoCheque.Anulado);
            }

            query = (orderBy?.ToLower()) switch
            {
                "numero" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.Numero)
                    : query.OrderBy(c => c.Numero),

                "banco" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.Banco)
                    : query.OrderBy(c => c.Banco),

                "proveedor" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.Proveedor != null ? c.Proveedor.RazonSocial : string.Empty)
                    : query.OrderBy(c => c.Proveedor != null ? c.Proveedor.RazonSocial : string.Empty),

                "fechaemision" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.FechaEmision)
                    : query.OrderBy(c => c.FechaEmision),

                "fechavencimiento" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.FechaVencimiento)
                    : query.OrderBy(c => c.FechaVencimiento),

                "monto" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.Monto)
                    : query.OrderBy(c => c.Monto),

                "estado" => orderDirection == "desc"
                    ? query.OrderByDescending(c => c.Estado)
                    : query.OrderBy(c => c.Estado),

                _ => query.OrderBy(c => c.FechaVencimiento ?? c.FechaEmision)
            };

            return await query.ToListAsync();
        }

        public async Task<IEnumerable<Cheque>> GetByProveedorIdAsync(int proveedorId)
        {
            return await _context.Cheques
                .AsNoTracking()
                .Include(c => c.OrdenCompra)
                .Where(c => c.ProveedorId == proveedorId && !c.IsDeleted)
                .OrderByDescending(c => c.FechaEmision)
                .ToListAsync();
        }

        public async Task<IEnumerable<Cheque>> GetByOrdenCompraIdAsync(int ordenCompraId)
        {
            return await _context.Cheques
                .AsNoTracking()
                .Include(c => c.Proveedor)
                .Where(c => c.OrdenCompraId == ordenCompraId && !c.IsDeleted)
                .OrderByDescending(c => c.FechaEmision)
                .ToListAsync();
        }

        public async Task<IEnumerable<Cheque>> GetVencidosAsync()
        {
            var hoy = DateTime.Today;
            return await _context.Cheques
                .AsNoTracking()
                .Include(c => c.Proveedor)
                .Include(c => c.OrdenCompra)
                .Where(c => !c.IsDeleted &&
                            c.FechaVencimiento.HasValue &&
                            c.FechaVencimiento.Value < hoy &&
                            c.Estado != EstadoCheque.Cobrado &&
                            c.Estado != EstadoCheque.Rechazado &&
                            c.Estado != EstadoCheque.Anulado)
                .OrderBy(c => c.FechaVencimiento)
                .ToListAsync();
        }

        public async Task<IEnumerable<Cheque>> GetPorVencerAsync(int dias = 7)
        {
            var hoy = DateTime.Today;
            var limite = hoy.AddDays(dias);

            return await _context.Cheques
                .AsNoTracking()
                .Include(c => c.Proveedor)
                .Include(c => c.OrdenCompra)
                .Where(c => !c.IsDeleted &&
                            c.FechaVencimiento.HasValue &&
                            c.FechaVencimiento.Value >= hoy &&
                            c.FechaVencimiento.Value <= limite &&
                            c.Estado != EstadoCheque.Cobrado &&
                            c.Estado != EstadoCheque.Rechazado &&
                            c.Estado != EstadoCheque.Anulado)
                .OrderBy(c => c.FechaVencimiento)
                .ToListAsync();
        }

        public async Task<bool> CambiarEstadoAsync(int id, EstadoCheque nuevoEstado)
        {
            var cheque = await _context.Cheques.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
            if (cheque == null)
                return false;

            cheque.Estado = nuevoEstado;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Estado de cheque {Id} cambiado a {Estado}", id, nuevoEstado);
            return true;
        }

        public async Task<bool> NumeroExisteAsync(string numero, int? excludeId = null)
        {
            return await _context.Cheques
                .AnyAsync(c =>
                    c.Numero == numero &&
                    !c.IsDeleted &&
                    (excludeId == null || c.Id != excludeId.Value));
        }
    }
}