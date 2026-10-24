using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers;

/// <summary>
/// Model binder que acepta decimales con coma o punto como separador,
/// independientemente de la cultura del servidor.
/// Se aplica globalmente vía <see cref="DecimalModelBinderProvider"/>; también puede
/// declararse con [ModelBinder(typeof(DecimalModelBinder))] en el ViewModel.
/// Vacío bindea 0 en decimal y null en decimal? (preserva la semántica de opcional).
/// </summary>
public class DecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueResult == ValueProviderResult.None)
            return Task.CompletedTask;

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueResult);

        var raw = valueResult.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            var esNullable = bindingContext.ModelMetadata.ModelType == typeof(decimal?);
            bindingContext.Result = ModelBindingResult.Success(esNullable ? null : 0m);
            return Task.CompletedTask;
        }

        // FIN-S-017: en un campo de dinero ([MontoArgentino]) un único separador seguido de exactamente tres dígitos
        // ("1.500", "1,500") se leía como 1,5 (decimal) cuando quien lo escribe puede querer 1500 (miles): una
        // subestimación x1000 silenciosa. Dinero nunca lleva tres decimales, así que se pide el formato sin ambigüedad.
        if (EsCampoDeDinero(bindingContext.ModelMetadata) && DecimalParsingHelper.EsMontoAmbiguo(raw))
        {
            bindingContext.ModelState.AddModelError(
                bindingContext.ModelName,
                $"El monto \"{raw.Trim()}\" es ambiguo: puede leerse como {raw.Trim().Replace(',', '.')} o como miles. " +
                "Escribilo sin separador de miles (por ejemplo 1500 o 1500,00) o con ambos (1.500,00).");
            return Task.CompletedTask;
        }

        if (DecimalParsingHelper.TryParseFlexibleDecimal(
                raw,
                out var result,
                NumberStyles.Any,
                allowMixedSeparators: true))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        else
        {
            bindingContext.ModelState.AddModelError(bindingContext.ModelName, "Ingresá un monto válido.");
        }

        return Task.CompletedTask;
    }

    // Un campo de dinero es el que declara [MontoArgentino] (validador) o [MontoSinAmbiguedad] (marcador sin validación).
    // El marcador no es un ValidationAttribute, así que no entra en ValidatorMetadata: se busca entre los atributos del
    // metadato (propiedad o parámetro de acción).
    private static bool EsCampoDeDinero(ModelMetadata metadata) =>
        metadata.ValidatorMetadata.OfType<TheBuryProject.Validation.MontoArgentinoAttribute>().Any()
        || (metadata is Microsoft.AspNetCore.Mvc.ModelBinding.Metadata.DefaultModelMetadata detallado
            && detallado.Attributes.Attributes.OfType<TheBuryProject.Validation.MontoSinAmbiguedadAttribute>().Any());
}
