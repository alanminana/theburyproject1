using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TheBuryProject.Data;
using TheBuryProject.Models.Enums;

namespace TheBuryProject.Services;

/// <summary>
/// Un cheque (banco + número) no puede cobrarse dos veces. Los cheques de venta viven en <c>DatosCheque</c>. Las cuotas de crédito
/// ya NO aceptan cheques (decisión D5), pero los cobros históricos de cuota con cheque viajan en
/// <c>MovimientoCaja.MedioPagoDetalle</c> con un formato fijo (<see cref="Detalle"/>): <see cref="ExisteEnCuotasAsync"/> es solo
/// lectura para que un cheque ya usado en una cuota histórica no se reutilice en una venta.
/// Banco y número se guardan y se comparan NORMALIZADOS (<see cref="Normalizar"/>): « Galicia » y «GALICIA» son el mismo banco.
/// </summary>
internal static class ChequeDuplicados
{
    private const string Marca = " · vence";
    private static readonly Regex Espacios = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Trim, espacios internos colapsados y mayúsculas (cultura invariante).</summary>
    public static string Normalizar(string? valor) =>
        Espacios.Replace((valor ?? string.Empty).Trim(), " ").ToUpperInvariant();

    /// <summary>Clave canónica del cheque (banco + número normalizados).</summary>
    public static string Clave(string? banco, string? numero) => $"{Normalizar(banco)}|{Normalizar(numero)}";

    /// <summary>Texto del cheque en el ingreso de caja de una cuota histórica: «Cheque · BANCO N° 123 · vence dd/MM/yyyy».</summary>
    public static string Detalle(string banco, string numero, DateTime vencimiento) =>
        $"{Prefijo(banco, numero)} {vencimiento:dd/MM/yyyy}";

    public static string Prefijo(string banco, string numero) => $"Cheque · {Normalizar(banco)} N° {Normalizar(numero)}{Marca}";

    /// <summary>
    /// Serializa el «chequeo de duplicado + inserción» de un mismo cheque con un lock de aplicación (<c>sp_getapplock</c>, dueño
    /// = transacción, recurso = hash de la clave normalizada). DEBE llamarse con una transacción abierta: el lock vive hasta el
    /// commit/rollback, así que quien llega segundo espera, y al entrar ve el cheque ya registrado por el primero.
    /// No toma ninguna fila ni tabla: es una hoja de la cadena de locks (nadie retiene otro lock esperando este). Otros
    /// proveedores (SQLite de tests): un único escritor ya serializa, no-op.
    /// </summary>
    public static async Task BloquearAsync(AppDbContext context, string? banco, string? numero, CancellationToken ct = default)
    {
        if (!context.Database.IsSqlServer())
            return;

        var recurso = "h10:cheque:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Clave(banco, numero))));
        var resultado = new SqlParameter("@rc", System.Data.SqlDbType.Int) { Direction = System.Data.ParameterDirection.Output };
        await context.Database.ExecuteSqlRawAsync(
            "EXEC @rc = sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 20000",
            new object[] { resultado, new SqlParameter("@r", recurso) }, ct);

        if (resultado.Value is int rc && rc < 0)
            throw new InvalidOperationException("No se pudo reservar el cheque para registrarlo (otra operación lo está procesando). Reintentá.");
    }

    /// <summary>Solo lectura: true si el cheque ya cobró una cuota (histórico; ingreso vigente: no anulado ni revertido).</summary>
    public static Task<bool> ExisteEnCuotasAsync(AppDbContext context, string banco, string numero)
    {
        var prefijo = Prefijo(banco, numero);
        return context.MovimientosCaja.AsNoTracking().AnyAsync(m =>
            !m.IsDeleted
            && m.Concepto == ConceptoMovimientoCaja.CobroCuota
            && m.TipoPago == TipoPago.Cheque
            && m.EstadoAcreditacion != EstadoAcreditacionMovimientoCaja.Anulado
            && m.EstadoAcreditacion != EstadoAcreditacionMovimientoCaja.Revertido
            && m.MedioPagoDetalle != null
            && m.MedioPagoDetalle.StartsWith(prefijo));
    }
}
