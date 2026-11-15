using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Helpers;
using TheBuryProject.Models.Enums;
using TheBuryProject.Services.Interfaces;
using TheBuryProject.Services.Models;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Controllers
{
    /// <summary>
    /// D-03: libro administrativo de cuentas por pagar. Ver = cuentasporpagar.view; registrar comprobantes =
    /// cuentasporpagar.createinvoice; registrar pagos y aplicar notas de crédito = cuentasporpagar.pay;
    /// anular (comprobantes, pagos y aplicaciones) = cuentasporpagar.cancel. Toda regla de negocio vive en
    /// <see cref="ICuentaPorPagarService"/>; el controller solo coordina.
    /// </summary>
    [Authorize]
    [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "view")]
    public class CuentaPorPagarController : Controller
    {
        private readonly ICuentaPorPagarService _service;
        private readonly ICurrentUserService _currentUser;

        public CuentaPorPagarController(ICuentaPorPagarService service, ICurrentUserService currentUser)
        {
            _service = service;
            _currentUser = currentUser;
        }

        // ---------- listados y consultas ----------

        [HttpGet]
        public async Task<IActionResult> Index(FiltroComprobantesProveedor filtro)
        {
            filtro ??= new FiltroComprobantesProveedor();
            var vm = new CuentaPorPagarIndexViewModel
            {
                Filtro = filtro,
                Listado = await _service.ListarComprobantesAsync(filtro),
                Proveedores = await _service.ObtenerProveedoresAsync(),
                PuedeRegistrarComprobantes = User.TienePermiso("cuentasporpagar", "createinvoice"),
                PuedeRegistrarPagos = User.TienePermiso("cuentasporpagar", "pay")
            };
            return View("Index_tw", vm);
        }

        [HttpGet]
        public async Task<IActionResult> Detalle(int id)
        {
            var detalle = await _service.ObtenerComprobanteAsync(id);
            return detalle is null ? NotFound() : View("Detalle_tw", detalle);
        }

        [HttpGet]
        public async Task<IActionResult> Pago(int id)
        {
            var pago = await _service.ObtenerPagoAsync(id);
            return pago is null ? NotFound() : View("Pago_tw", pago);
        }

        [HttpGet]
        public async Task<IActionResult> EstadoCuenta(int proveedorId)
        {
            var cuenta = await _service.ObtenerEstadoCuentaAsync(proveedorId);
            if (cuenta is null)
                return NotFound();

            return View("EstadoCuenta_tw", new EstadoCuentaViewModel
            {
                Cuenta = cuenta,
                Pendientes = await _service.ObtenerPendientesAsync(proveedorId),
                Pagos = await _service.ListarPagosAsync(proveedorId),
                PuedeRegistrarComprobantes = User.TienePermiso("cuentasporpagar", "createinvoice"),
                PuedeRegistrarPagos = User.TienePermiso("cuentasporpagar", "pay")
            });
        }

        [HttpGet]
        public async Task<IActionResult> Antiguedad(int? proveedorId)
        {
            return View("Antiguedad_tw", new AntiguedadDeudaViewModel
            {
                Antiguedad = await _service.ObtenerAntiguedadAsync(proveedorId),
                Proveedores = await _service.ObtenerProveedoresAsync(),
                ProveedorId = proveedorId
            });
        }

        // ---------- comprobantes ----------

        [HttpGet]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "createinvoice")]
        public async Task<IActionResult> Crear(int? proveedorId, int? ordenCompraId, TipoComprobanteProveedor tipo = TipoComprobanteProveedor.Factura)
        {
            if (!proveedorId.HasValue && ordenCompraId.HasValue)
                proveedorId = await _service.ObtenerProveedorDeOrdenCompraAsync(ordenCompraId.Value);

            var datos = new ComprobanteProveedorRequest
            {
                ProveedorId = proveedorId ?? 0,
                OrdenCompraId = ordenCompraId,
                Tipo = tipo
            };
            return View("Crear_tw", await ArmarFormularioComprobanteAsync(datos));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "createinvoice")]
        public async Task<IActionResult> Crear(ComprobanteProveedorRequest datos)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = PrimerError();
                return View("Crear_tw", await ArmarFormularioComprobanteAsync(datos));
            }

            var resultado = await _service.RegistrarComprobanteAsync(datos, _currentUser.GetUsername());
            if (!resultado.Ok)
            {
                ViewBag.Error = resultado.Error;
                return View("Crear_tw", await ArmarFormularioComprobanteAsync(datos));
            }

            TempData["Success"] = "Comprobante registrado.";
            return RedirectToAction(nameof(Detalle), new { id = resultado.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "cancel")]
        public async Task<IActionResult> Anular(int id, string? motivo)
        {
            var resultado = await _service.AnularComprobanteAsync(id, motivo, _currentUser.GetUsername());
            Avisar(resultado, "Comprobante anulado.");
            return RedirectToAction(nameof(Detalle), new { id });
        }

        // ---------- pagos ----------

        [HttpGet]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "pay")]
        public async Task<IActionResult> Pagar(int? proveedorId, int? comprobanteId)
        {
            var datos = new PagoProveedorRequest
            {
                ProveedorId = proveedorId ?? 0,
                ClaveIdempotencia = Guid.NewGuid().ToString("N")
            };
            var vm = await ArmarFormularioPagoAsync(datos);
            if (comprobanteId.HasValue)
            {
                var elegido = vm.Pendientes.FirstOrDefault(p => p.Id == comprobanteId.Value);
                if (elegido is not null)
                {
                    datos.Monto = elegido.SaldoPendiente;
                    datos.Imputaciones = vm.Pendientes
                        .Select(p => new ImputacionRequest
                        { ComprobanteId = p.Id, Monto = p.Id == elegido.Id ? p.SaldoPendiente : null })
                        .ToList();
                }
            }
            return View("Pagar_tw", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "pay")]
        public async Task<IActionResult> Pagar(PagoProveedorRequest datos)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Error = PrimerError();
                return View("Pagar_tw", await ArmarFormularioPagoAsync(datos));
            }

            var resultado = await _service.RegistrarPagoAsync(datos, _currentUser.GetUsername());
            if (!resultado.Ok)
            {
                ViewBag.Error = resultado.Error;
                return View("Pagar_tw", await ArmarFormularioPagoAsync(datos));
            }

            TempData["Success"] = resultado.YaRegistrado
                ? "Ese pago ya estaba registrado; no se duplicó."
                : "Pago registrado.";
            return RedirectToAction(nameof(Pago), new { id = resultado.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "cancel")]
        public async Task<IActionResult> AnularPago(int id, string? motivo)
        {
            var resultado = await _service.AnularPagoAsync(id, motivo, _currentUser.GetUsername());
            Avisar(resultado, "Pago anulado: los saldos fueron restituidos.");
            return RedirectToAction(nameof(Pago), new { id });
        }

        // ---------- notas de crédito ----------

        [HttpGet]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "pay")]
        public async Task<IActionResult> ImputarNotaCredito(int id)
        {
            var vm = await ArmarImputacionNotaCreditoAsync(id, null, Guid.NewGuid().ToString("N"));
            return vm is null ? NotFound() : View("ImputarNotaCredito_tw", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "pay")]
        public async Task<IActionResult> ImputarNotaCredito(int id, List<ImputacionRequest> imputaciones, string? claveIdempotencia)
        {
            if (!ModelState.IsValid)
            {
                var invalido = await ArmarImputacionNotaCreditoAsync(id, imputaciones, claveIdempotencia);
                if (invalido is null)
                    return NotFound();
                ViewBag.Error = PrimerError();
                return View("ImputarNotaCredito_tw", invalido);
            }

            var resultado = await _service.ImputarNotaCreditoAsync(id, imputaciones ?? new(), claveIdempotencia, _currentUser.GetUsername());
            if (!resultado.Ok)
            {
                var vm = await ArmarImputacionNotaCreditoAsync(id, imputaciones, claveIdempotencia);
                if (vm is null)
                    return NotFound();
                ViewBag.Error = resultado.Error;
                return View("ImputarNotaCredito_tw", vm);
            }

            TempData["Success"] = resultado.YaRegistrado
                ? "Esa aplicación ya estaba registrada; no se duplicó."
                : "Nota de crédito aplicada.";
            return RedirectToAction(nameof(Detalle), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "cuentasporpagar", Accion = "cancel")]
        public async Task<IActionResult> AnularImputacionNotaCredito(int id, int comprobanteId, string? motivo)
        {
            var resultado = await _service.AnularImputacionNotaCreditoAsync(id, motivo, _currentUser.GetUsername());
            Avisar(resultado, "Aplicación anulada: los saldos fueron restituidos.");

            // Se vuelve al comprobante desde el que se operó solo si realmente forma parte de la aplicación;
            // un comprobanteId arbitrario del POST no se usa como destino.
            var destino = comprobanteId;
            var partes = await _service.ObtenerComprobantesDeImputacionAsync(id);
            if (partes is { } p && comprobanteId != p.NotaCreditoId && comprobanteId != p.ComprobanteId)
                destino = p.NotaCreditoId;
            else if (partes is null)
                return RedirectToAction(nameof(Index));

            return RedirectToAction(nameof(Detalle), new { id = destino });
        }

        // ---------- armado de pantallas ----------

        private async Task<ComprobanteProveedorFormViewModel> ArmarFormularioComprobanteAsync(ComprobanteProveedorRequest datos)
        {
            var vm = new ComprobanteProveedorFormViewModel { Datos = datos };
            if (datos.ProveedorId > 0)
            {
                vm.Proveedor = await _service.ObtenerProveedorAsync(datos.ProveedorId);
                if (vm.Proveedor is not null)
                    vm.OrdenesCompra = await _service.ObtenerOrdenesCompraAsync(datos.ProveedorId);
            }
            if (vm.Proveedor is null)
                vm.Proveedores = await _service.ObtenerProveedoresAsync();
            return vm;
        }

        private async Task<PagoProveedorFormViewModel> ArmarFormularioPagoAsync(PagoProveedorRequest datos)
        {
            var vm = new PagoProveedorFormViewModel { Datos = datos };
            if (datos.ProveedorId > 0)
            {
                vm.Proveedor = await _service.ObtenerProveedorAsync(datos.ProveedorId);
                if (vm.Proveedor is not null)
                {
                    vm.Pendientes = await _service.ObtenerPendientesAsync(datos.ProveedorId);
                    vm.Cheques = await _service.ObtenerChequesAsync(datos.ProveedorId);
                }
            }
            if (vm.Proveedor is null)
                vm.Proveedores = await _service.ObtenerProveedoresAsync();
            if (string.IsNullOrWhiteSpace(datos.ClaveIdempotencia))
                datos.ClaveIdempotencia = Guid.NewGuid().ToString("N");
            return vm;
        }

        private async Task<ImputarNotaCreditoViewModel?> ArmarImputacionNotaCreditoAsync(
            int notaCreditoId, List<ImputacionRequest>? previas, string? clave)
        {
            var nc = await _service.ObtenerComprobanteAsync(notaCreditoId);
            if (nc is null || nc.Cabecera.Tipo != TipoComprobanteProveedor.NotaCredito)
                return null;

            return new ImputarNotaCreditoViewModel
            {
                NotaCredito = nc.Cabecera,
                Pendientes = await _service.ObtenerPendientesAsync(nc.Cabecera.ProveedorId),
                Imputaciones = previas ?? new List<ImputacionRequest>(),
                ClaveIdempotencia = string.IsNullOrWhiteSpace(clave) ? Guid.NewGuid().ToString("N") : clave
            };
        }

        private void Avisar(ResultadoCxp resultado, string mensajeOk)
        {
            if (resultado.Ok)
                TempData["Success"] = mensajeOk;
            else
                TempData["Error"] = resultado.Error;
        }

        private string PrimerError() =>
            ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)
                .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m)) ?? "Datos inválidos.";
    }
}
