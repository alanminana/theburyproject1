namespace TheBuryProject.Validation;

/// <summary>
/// Marca un decimal como IMPORTE EN DINERO sin agregarle validación propia (a diferencia de <c>[MontoArgentino]</c>, que
/// además impone tope y dos decimales). <see cref="TheBuryProject.Helpers.DecimalModelBinder"/> rechaza en estos campos los
/// montos ambiguos como "1.500" o "1,500" (decimal con tres decimales o miles), que de otro modo se guardarían como 1,50.
/// Aplicarlo a propiedades y a parámetros de acción que reciben importes y no tienen <c>[MontoArgentino]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class MontoSinAmbiguedadAttribute : Attribute
{
}
