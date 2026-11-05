using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheBuryProject.Filters;
using TheBuryProject.Services;
using TheBuryProject.Services.Interfaces;

namespace TheBuryProject.Controllers
{
    /// <summary>Carga del costo real de entrega y armado de una venta (reemplaza al costo estándar congelado).</summary>
    [Authorize]
    public class VentaCostoServiciosController : Controller
    {
        private readonly IVentaCostoServiciosService _servicio;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<VentaCostoServiciosController> _logger;

        public VentaCostoServiciosController(
            IVentaCostoServiciosService servicio,
            ICurrentUserService currentUser,
            ILogger<VentaCostoServiciosController> logger)
        {
            _servicio = servicio;
            _currentUser = currentUser;
            _logger = logger;
        }

        [HttpPost("/Venta/CostoServicios/{id:int}")]
        [ValidateAntiForgeryToken]
        [PermisoRequerido(Modulo = "ventas", Accion = "update")]
        public async Task<IActionResult> Guardar(int id, IFormCollection form)
        {
            try
            {
                var armados = new Dictionary<int, decimal?>();
                var versiones = new Dictionary<int, string>();
                foreach (var clave in form.Keys.Where(k => k.StartsWith("armado[", StringComparison.Ordinal) && k.EndsWith(']')))
                {
                    var detalleId = LeerIdDetalle(clave, "armado[");
                    armados[detalleId] = Parsear(form[clave].ToString(), "costo real del armado");
                }

                foreach (var clave in form.Keys.Where(k => k.StartsWith("armadoRv[", StringComparison.Ordinal) && k.EndsWith(']')))
                    versiones[LeerIdDetalle(clave, "armadoRv[")] = form[clave].ToString();

                var envio = form.ContainsKey("envio") ? Parsear(form["envio"].ToString(), "costo real de entrega") : null;
                await _servicio.GuardarAsync(
                    id,
                    new VentaCostoServiciosComando(envio, armados, form["envioRv"].ToString(), versiones),
                    _currentUser.GetUsername());
                TempData["Success"] = "Costo de envío y armado actualizado.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["Error"] = ex.Message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al guardar el costo de servicios de la venta {Id}", id);
                TempData["Error"] = "No se pudo guardar el costo de envío y armado.";
            }

            return RedirectToAction("Details", "Venta", new { id });
        }

        private static int LeerIdDetalle(string clave, string prefijo)
        {
            var idTexto = clave[prefijo.Length..^1];
            if (!int.TryParse(idTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var detalleId))
                throw new InvalidOperationException("Línea de armado inválida.");
            return detalleId;
        }

        /// <summary>Vacío = sin costo real (vuelve al estándar). Acepta solo punto decimal (input numérico del formulario).</summary>
        private static decimal? Parsear(string texto, string campo)
        {
            texto = texto.Trim();
            if (texto.Length == 0)
                return null;
            if (!decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor))
                throw new InvalidOperationException($"El {campo} no es un importe válido.");
            return valor;
        }
    }
}
