using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TheBuryProject.Data;
using TheBuryProject.Models.Constants;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services
{
    public class VentaNumberGenerator
    {
        private readonly AppDbContext _context;
        private readonly ILogger<VentaNumberGenerator> _logger;
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public VentaNumberGenerator(AppDbContext context, ILogger<VentaNumberGenerator> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<string> GenerarNumeroAsync(EstadoVenta estado)
        {
            // FS2-NUM: en SQL Server el número sale de un contador atómico (una sola sentencia), sin carreras entre altas
            // simultáneas. El camino "máximo + 1" bajo semáforo queda para los proveedores de prueba (SQLite).
            if (_context.Database.IsSqlServer())
            {
                var prefijoVenta = estado == EstadoVenta.Cotizacion
                    ? VentaConstants.PREFIJO_COTIZACION
                    : VentaConstants.PREFIJO_VENTA;
                var periodoVenta = DateTime.UtcNow.ToString(VentaConstants.FORMATO_PERIODO);
                var siguienteAtomico = await SiguienteAtomicoAsync($"{prefijoVenta}-{periodoVenta}");
                var numeroAtomico = string.Format(VentaConstants.FORMATO_NUMERO_VENTA, prefijoVenta, periodoVenta, siguienteAtomico);
                _logger.LogDebug("Número generado (contador atómico): {Numero}", numeroAtomico);
                return numeroAtomico;
            }

            await _semaphore.WaitAsync();
            try
            {
                var prefijo = estado == EstadoVenta.Cotizacion
                    ? VentaConstants.PREFIJO_COTIZACION
                    : VentaConstants.PREFIJO_VENTA;

                var fecha = DateTime.UtcNow;
                var periodo = fecha.ToString(VentaConstants.FORMATO_PERIODO);
                var prefijoCompleto = $"{prefijo}-{periodo}";

                var ultimaVenta = await _context.Ventas
                    .Where(v => v.Numero.StartsWith(prefijoCompleto))
                    .OrderByDescending(v => v.Numero)
                    .FirstOrDefaultAsync();

                int siguiente = 1;

                if (ultimaVenta != null)
                {
                    var partes = ultimaVenta.Numero.Split('-');
                    if (partes.Length == 3 && int.TryParse(partes[2], out int ultimo))
                    {
                        siguiente = ultimo + 1;
                    }
                }

                var numero = string.Format(VentaConstants.FORMATO_NUMERO_VENTA, prefijo, periodo, siguiente);
                _logger.LogDebug("Número generado: {Numero}", numero);
                return numero;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// Entrega el próximo número del prefijo/período con UNA sentencia atómica sobre <c>NumeracionVentaContadores</c>.
        /// El contador nunca queda por debajo del mayor número ya existente (<paramref name="prefijoCompleto"/> con sufijo),
        /// de modo que numeración previa o creada por otro camino no genera colisiones. Si la fila no existe se crea;
        /// ante una creación simultánea gana una sola y la otra reintenta el incremento.
        /// </summary>
        private async Task<int> SiguienteAtomicoAsync(string prefijoCompleto)
        {
            var ultimo = await _context.Ventas
                .IgnoreQueryFilters()
                .Where(v => v.Numero.StartsWith(prefijoCompleto))
                .OrderByDescending(v => v.Numero)
                .Select(v => v.Numero)
                .FirstOrDefaultAsync();

            var semilla = 0;
            if (ultimo != null)
            {
                var partes = ultimo.Split('-');
                if (partes.Length == 3 && int.TryParse(partes[2], out var parsed)) semilla = parsed;
            }

            const string sql = @"
SET NOCOUNT ON;
DECLARE @v int;
UPDATE [NumeracionVentaContadores]
   SET @v = [Valor] = CASE WHEN [Valor] < @semilla THEN @semilla + 1 ELSE [Valor] + 1 END
 WHERE [Clave] = @clave;
IF @@ROWCOUNT = 0
BEGIN
    BEGIN TRY
        INSERT INTO [NumeracionVentaContadores] ([Clave], [Valor]) VALUES (@clave, @semilla + 1);
        SET @v = @semilla + 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        UPDATE [NumeracionVentaContadores]
           SET @v = [Valor] = CASE WHEN [Valor] < @semilla THEN @semilla + 1 ELSE [Valor] + 1 END
         WHERE [Clave] = @clave;
    END CATCH
END
SELECT @v;";

            var conexion = _context.Database.GetDbConnection();
            var abrirYCerrar = conexion.State != ConnectionState.Open;
            if (abrirYCerrar) await _context.Database.OpenConnectionAsync();
            try
            {
                await using var comando = conexion.CreateCommand();
                comando.CommandText = sql;
                comando.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

                var pSemilla = comando.CreateParameter();
                pSemilla.ParameterName = "@semilla";
                pSemilla.DbType = DbType.Int32;
                pSemilla.Value = semilla;
                comando.Parameters.Add(pSemilla);

                var pClave = comando.CreateParameter();
                pClave.ParameterName = "@clave";
                pClave.DbType = DbType.String;
                pClave.Size = 40;
                pClave.Value = prefijoCompleto;
                comando.Parameters.Add(pClave);

                return Convert.ToInt32(await comando.ExecuteScalarAsync());
            }
            finally
            {
                if (abrirYCerrar) await _context.Database.CloseConnectionAsync();
            }
        }

        public async Task<string> GenerarNumeroFacturaAsync(TipoFactura tipo)
        {
            await _semaphore.WaitAsync();
            try
            {
                var prefijo = ObtenerPrefijoFactura(tipo);
                var fecha = DateTime.UtcNow;
                var periodo = fecha.ToString(VentaConstants.FORMATO_PERIODO);
                var prefijoCompleto = $"{prefijo}-{periodo}";

                var ultimaFactura = await _context.Facturas
                    .Where(f => f.Numero.StartsWith(prefijoCompleto))
                    .OrderByDescending(f => f.Numero)
                    .FirstOrDefaultAsync();

                int siguiente = 1;

                if (ultimaFactura != null)
                {
                    var partes = ultimaFactura.Numero.Split('-');
                    if (partes.Length >= 3 && int.TryParse(partes[^1], out int ultimo))
                    {
                        siguiente = ultimo + 1;
                    }
                }

                var numero = string.Format(VentaConstants.FORMATO_NUMERO_VENTA, prefijo, periodo, siguiente);
                _logger.LogDebug("Número de factura generado: {Numero} - Tipo {Tipo}", numero, tipo);
                return numero;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static string ObtenerPrefijoFactura(TipoFactura tipo)
        {
            return tipo switch
            {
                TipoFactura.A => VentaConstants.FacturaPrefijos.TIPO_A,
                TipoFactura.B => VentaConstants.FacturaPrefijos.TIPO_B,
                TipoFactura.C => VentaConstants.FacturaPrefijos.TIPO_C,
                TipoFactura.NotaCredito => VentaConstants.FacturaPrefijos.NOTA_CREDITO,
                TipoFactura.NotaDebito => VentaConstants.FacturaPrefijos.NOTA_DEBITO,
                _ => VentaConstants.FacturaPrefijos.GENERICO
            };
        }
    }
}