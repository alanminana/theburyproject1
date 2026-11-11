using TheBuryProject.Services;

namespace TheBuryProject.Tests.Concurrency;

/// <summary>
/// Reloj del harness SQL Server de concurrencia (<see cref="ConcSqlHostFixture"/>).
/// </summary>
/// <remarks>
/// <para>
/// Causa que corrige (H10-B13 [B13e], corrida de CI 42948109750, 2026-11-11 02:59:54–03:00:01 UTC): el harness usaba
/// <see cref="TimeProvider.System"/>. Un test que sembraba una caja antes de las 00:00 de Argentina (03:00 UTC) y operaba
/// después encontraba la caja «abierta desde el día anterior» (<c>CajaService.EsAperturaVencida</c>) y la operación real
/// era rechazada por una regla de negocio correcta, ajena a lo que el test mide. No era una carrera ni un defecto del
/// producto: era el reloj de pared cruzando la medianoche comercial en mitad de la prueba.
/// </para>
/// <para>
/// Por defecto se comporta como <see cref="TimeProviderDiaComercialAcotado"/>: el tiempo avanza con normalidad pero nunca
/// pasa del final del día comercial en que se creó el fixture (misma solución que el stack E2E, sin cambiar ninguna
/// regla). <see cref="EnElDiaComercialSiguiente"/> permite a un test cruzar la medianoche A PROPÓSITO y de forma
/// determinista para afirmar la regla de turno vencido.
/// </para>
/// </remarks>
public sealed class RelojConcurrencia : TimeProvider
{
    private readonly TimeProviderDiaComercialAcotado _acotado;
    private long _adelantoTicks;

    public RelojConcurrencia() : this(System) { }

    public RelojConcurrencia(TimeProvider sistema) => _acotado = new TimeProviderDiaComercialAcotado(sistema);

    /// <summary>Último instante (UTC) que el reloj devuelve mientras no se adelanta a propósito.</summary>
    public DateTimeOffset LimiteUtc => _acotado.LimiteUtc;

    public override DateTimeOffset GetUtcNow() =>
        _acotado.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _adelantoTicks));

    /// <summary>
    /// Adelanta el reloj un día comercial completo hasta que se libera el resultado. Usar con <c>using</c>: el fixture
    /// es compartido por toda la clase de pruebas y el adelanto no debe filtrarse al test siguiente.
    /// </summary>
    public IDisposable EnElDiaComercialSiguiente()
    {
        Interlocked.Exchange(ref _adelantoTicks, TimeSpan.FromDays(1).Ticks);
        return new Restaurar(this);
    }

    private sealed class Restaurar(RelojConcurrencia reloj) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref reloj._adelantoTicks, 0);
    }
}
