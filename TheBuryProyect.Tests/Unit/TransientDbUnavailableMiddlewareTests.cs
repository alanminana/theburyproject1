using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using TheBuryProject.Middleware;

namespace TheBuryProject.Tests.Unit;

/// <summary>
/// Tests para TransientDbUnavailableMiddleware: durante un restart/caída transitoria de SQL Server
/// (ej. error 4060 "Cannot open database"), el pipeline debe responder 503 + Retry-After en vez de
/// dejar que UseExceptionHandler lo convierta en un 500 genérico. Cualquier otro SqlException no
/// listado como transitorio debe seguir propagándose intacto.
/// </summary>
public class TransientDbUnavailableMiddlewareTests
{
    [Theory]
    [InlineData(4060, true)]   // Cannot open database (restart de SQL en curso)
    [InlineData(40613, true)]  // Azure SQL: servicio ocupado
    [InlineData(10928, true)]  // Azure SQL: límite de recursos
    [InlineData(53, false)]    // "A network-related or instance-specific error" no está en la lista (config, no restart transitorio)
    [InlineData(547, false)]   // Violación de constraint (FK/check) - error real de datos, nunca transitorio
    [InlineData(2627, false)]  // Violación de unique/PK - error real de datos, nunca transitorio
    public void IsTransient_ClasificaCodigosConocidosYRechazaElResto(int sqlErrorNumber, bool esperado)
    {
        var resultado = TransientDbUnavailableMiddleware.IsTransient(sqlErrorNumber);

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public async Task InvokeAsync_SqlExceptionTransitoriaDirecta_Responde503ConRetryAfter()
    {
        var sqlException = CreateSqlException(4060, "Cannot open database \"TheBuryProjectDb\" requested by the login.");
        var middleware = new TransientDbUnavailableMiddleware(
            _ => throw sqlException,
            NullLogger<TransientDbUnavailableMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("2", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task InvokeAsync_SqlExceptionTransitoriaEnvueltaPorEfCore_Responde503()
    {
        // Reproduce el caso real observado en staging: sin EnableRetryOnFailure, EF Core envuelve el
        // SqlException transitorio en un InvalidOperationException ("...enabling transient error
        // resiliency...") en vez de dejarlo propagar directo.
        var sqlException = CreateSqlException(4060, "Cannot open database \"TheBuryProjectDb\" requested by the login.");
        var wrapped = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure. Consider enabling transient error resiliency by adding 'EnableRetryOnFailure' to the 'UseSqlServer' call.",
            sqlException);
        var middleware = new TransientDbUnavailableMiddleware(
            _ => throw wrapped,
            NullLogger<TransientDbUnavailableMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("2", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task InvokeAsync_SqlExceptionNoTransitoria_SePropaga()
    {
        var sqlException = CreateSqlException(547, "The INSERT statement conflicted with the FOREIGN KEY constraint.");
        var middleware = new TransientDbUnavailableMiddleware(
            _ => throw sqlException,
            NullLogger<TransientDbUnavailableMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var thrown = await Assert.ThrowsAsync<SqlException>(() => middleware.InvokeAsync(context));

        Assert.Same(sqlException, thrown);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode); // sin modificar: no se atrapó
    }

    [Fact]
    public async Task InvokeAsync_ExcepcionNoSql_SePropagaSinTocarLaRespuesta()
    {
        var otra = new InvalidOperationException("bug real de aplicacion, no relacionado a SQL");
        var middleware = new TransientDbUnavailableMiddleware(
            _ => throw otra,
            NullLogger<TransientDbUnavailableMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(otra, thrown);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_SinExcepcion_LlamaAlSiguienteYNoTocaLaRespuesta()
    {
        var llamado = false;
        var middleware = new TransientDbUnavailableMiddleware(
            _ => { llamado = true; return Task.CompletedTask; },
            NullLogger<TransientDbUnavailableMiddleware>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(llamado);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    // Microsoft.Data.SqlClient.SqlException no tiene constructor público: se construye vía reflexión
    // (mismo patrón usado en la propia suite de tests de EF Core / Microsoft.Data.SqlClient) para poder
    // simular un error de servidor con un Number específico sin depender de una instancia SQL real.
    private static SqlException CreateSqlException(int number, string message)
    {
        var errorCollectionCtor = typeof(SqlErrorCollection)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException("No se encontró el constructor interno de SqlErrorCollection.");
        var errorCollection = errorCollectionCtor.Invoke(null);

        var errorCtor = typeof(SqlError).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception) },
            null)
            ?? throw new InvalidOperationException("No se encontró el constructor interno de SqlError.");
        var error = errorCtor.Invoke(new object?[] { number, (byte)0, (byte)0, "test-server", message, "test-proc", 0, null });

        var addMethod = typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("No se encontró SqlErrorCollection.Add.");
        addMethod.Invoke(errorCollection, new[] { error });

        var createException = typeof(SqlException).GetMethod(
            "CreateException",
            BindingFlags.NonPublic | BindingFlags.Static,
            null,
            new[] { typeof(SqlErrorCollection), typeof(string) },
            null)
            ?? throw new InvalidOperationException("No se encontró SqlException.CreateException(SqlErrorCollection, string).");

        return (SqlException)createException.Invoke(null, new[] { errorCollection, "7.0.0.0" })!;
    }
}
