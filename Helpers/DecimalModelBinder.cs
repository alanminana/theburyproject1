using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers;

/// <summary>
/// Model binder que acepta decimales con coma o punto como separador,
/// independientemente de la cultura del servidor.
/// Registrar con [ModelBinder(typeof(DecimalModelBinder))] en el ViewModel.
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
            bindingContext.Result = ModelBindingResult.Success(0m);
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
}
