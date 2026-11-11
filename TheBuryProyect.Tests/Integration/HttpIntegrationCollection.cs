namespace TheBuryProject.Tests.Integration;

/// <summary>
/// Grupo serial histórico de las pruebas HTTP (cada clase levanta su propio host web con una base SQLite :memory: propia).
/// </summary>
/// <remarks>
/// Las clases de una MISMA colección xUnit se ejecutan en serie; colecciones distintas, en paralelo. Con una única colección las
/// 104 clases corrían una detrás de otra y esa cola era lo último en terminar (~8 de los 11,7 minutos de la suite, medido el 2026-11-11).
/// Se reparten en 4 particiones <c>HttpIntegration.1..4</c>, equilibradas con las duraciones medidas (la 1 es una sola clase pesada):
/// a lo sumo 4 hosts web arrancan a la vez. NO quitar las colecciones por completo: sin límite, un GET simple de
/// <c>CspHttpTests</c> expiró a los 100 s por inanición de CPU. Una clase nueva puede usar <c>[Collection("HttpIntegration")]</c>
/// (grupo serial) o cualquiera de las particiones; <c>HttpIntegrationCollectionConventionTests</c> exige una de ellas.
/// </remarks>
[CollectionDefinition("HttpIntegration")]
public sealed class HttpIntegrationCollection
{
}
