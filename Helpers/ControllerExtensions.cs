using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TheBuryProject.Helpers
{
    public static class ControllerExtensions
    {
        public static JsonResult JsonModelErrors(this Controller controller)
        {
            var errors = controller.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "No se pudo completar la operación." : e.ErrorMessage)
                .Distinct()
                .ToArray();

            return controller.Json(new
            {
                success = false,
                errors = errors.Length > 0 ? errors : new[] { "No se pudo completar la operación." }
            });
        }
    }
}
