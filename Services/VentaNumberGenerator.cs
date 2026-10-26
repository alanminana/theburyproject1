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
                var prefijoVenta = await ObtenerPrefijoVentaAsync(estado);
                var periodoVenta = DateTime.UtcNow.ToString(VentaConstants.FORMATO_PERIODO);
                var siguienteAtomico = await SiguienteAtomicoAsync($"{prefijoVenta}-{periodoVenta}");
                var numeroAtomico = string.Format(VentaConstants.FORMATO_NUMERO_VENTA, prefijoVenta, periodoVenta, siguienteAtomico);
                _logger.LogDebug("Número generado (contador atómico): {Numero}", numeroAtomico);
                return numeroAtomico;
            }

            await _semaphore.WaitAsync();
            try
            {
                var prefijo = await ObtenerPrefijoVentaAsync(estado);

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
                    if (partes.Length >= 3 && int.TryParse(partes[^1], out int ultimo))
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
        /// Entrega el próximo número del prefijo/período con UNA sentencia sobre <c>NumeracionVentaContadores</c>: incrementa
        /// la fila de <paramref name="prefijoCompleto"/> y devuelve el valor. Si la fila no existe (primer uso, o numeración
        /// previa a la tabla) se crea partiendo del mayor sufijo NUMÉRICO ya presente en <c>Ventas</c> (no del orden de texto, que
        /// falla pasados 999999); ante una creación simultánea gana una sola y la otra reintenta el incremento.
        /// </summary>
        private async Task<int> SiguienteAtomicoAsync(string prefijoCompleto)
        {
            const string sql = @"
SET NOCOUNT ON;
DECLARE @v int;
UPDATE [NumeracionVentaContadores] SET @v = [Valor] = [Valor] + 1 WHERE [Clave] = @clave;
IF @@ROWCOUNT = 0
BEGIN
    DECLARE @semilla int = ISNULL((
        SELECT MAX(TRY_CAST(SUBSTRING([Numero], LEN(@clave) + 2, 20) AS int))
          FROM [Ventas]
         WHERE [Numero] LIKE @clave + N'-%'), 0);
    BEGIN TRY
        INSERT INTO [NumeracionVentaContadores] ([Clave], [Valor]) VALUES (@clave, @semilla + 1);
        SET @v = @semilla + 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601, 2627) THROW;
        UPDATE [NumeracionVentaContadores] SET @v = [Valor] = [Valor] + 1 WHERE [Clave] = @clave;
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
                var prefijo = await ObtenerPrefijoFacturaAsync(tipo);
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

        // Cotización y venta comparten tabla pero llevan prefijo propio (configurable en Configuración → Prefijos).
        private Task<string> ObtenerPrefijoVentaAsync(EstadoVenta estado) =>
            PrefijosNumeracion.ObtenerAsync(
                _context,
                estado == EstadoVenta.Cotizacion ? TipoPrefijoNumeracion.Cotizacion : TipoPrefijoNumeracion.Venta);

        private Task<string> ObtenerPrefijoFacturaAsync(TipoFactura tipo)
        {
            var tipoPrefijo = tipo switch
            {
                TipoFactura.A => TipoPrefijoNumeracion.FacturaA,
                TipoFactura.B => TipoPrefijoNumeracion.FacturaB,
                TipoFactura.C => TipoPrefijoNumeracion.FacturaC,
                TipoFactura.NotaCredito => TipoPrefijoNumeracion.NotaCreditoFiscal,
                TipoFactura.NotaDebito => TipoPrefijoNumeracion.NotaDebitoFiscal,
                _ => TipoPrefijoNumeracion.FacturaGenerica
            };
            return PrefijosNumeracion.ObtenerAsync(_context, tipoPrefijo);
        }
    }
}