using System;
using System.Collections.Generic;
using System.Linq;
using TheBuryProject.ViewModels;

namespace TheBuryProject.Helpers
{
    public static class ClienteControllerHelper
    {
        // Documentos obligatorios: DNI, Recibo de Sueldo, Servicio
        // Nota: Veraz/CUIL son opcionales (dan puntos extra en evaluación pero no son requisito)
        public static bool VerificaDocumentosRequeridos(List<string> tiposDocumentosVerificados)
        {
            if (tiposDocumentosVerificados is null || tiposDocumentosVerificados.Count == 0)
                return false;

            var set = NormalizarTipos(tiposDocumentosVerificados);

            return set.Contains("DNI")
                   && set.Contains("Recibo de Sueldo")
                   && TieneServicio(set);
        }

        public static List<string> ObtenerDocumentosFaltantes(List<string> tiposVerificados)
        {
            var faltantes = new List<string>();

            if (tiposVerificados is null || tiposVerificados.Count == 0)
            {
                faltantes.Add("DNI");
                faltantes.Add("Recibo de Sueldo");
                faltantes.Add("Servicio");
                return faltantes;
            }

            var set = NormalizarTipos(tiposVerificados);

            if (!set.Contains("DNI"))
                faltantes.Add("DNI");

            if (!set.Contains("Recibo de Sueldo"))
                faltantes.Add("Recibo de Sueldo");

            if (!TieneServicio(set))
                faltantes.Add("Servicio");

            return faltantes;
        }

        public static string DeterminarNivelRiesgo(int score)
        {
            return score switch
            {
                >= 700 => "Bajo",
                >= 500 => "Medio",
                _ => "Alto"
            };
        }

        public static void GenerarAlertasYRecomendaciones(EvaluacionCreditoResult evaluacion)
        {
            if (!evaluacion.TieneDocumentosCompletos && evaluacion.DocumentosFaltantes?.Count > 0)
                evaluacion.AlertasYRecomendaciones.Add($"⚠️ Faltan documentos: {string.Join(", ", evaluacion.DocumentosFaltantes)}");

            if (evaluacion.PorcentajeEndeudamiento > 40)
                evaluacion.AlertasYRecomendaciones.Add($"⚠️ Endeudamiento alto: {evaluacion.PorcentajeEndeudamiento:F1}%");

            if (evaluacion.RequiereGarante && !evaluacion.TieneGarante)
                evaluacion.AlertasYRecomendaciones.Add("⚠️ Se requiere garante");

            if (evaluacion.ScoreCrediticio < 500)
                evaluacion.AlertasYRecomendaciones.Add($"⚠️ Score crediticio bajo: {evaluacion.ScoreCrediticio}");

            if (evaluacion.MontoMaximoDisponible <= 0)
                evaluacion.AlertasYRecomendaciones.Add("⚠️ Sin capacidad de pago disponible");

            if (evaluacion.CumpleRequisitos)
                evaluacion.AlertasYRecomendaciones.Add("✅ El cliente cumple con todos los requisitos");
            else if (evaluacion.PuedeAprobarConExcepcion)
                evaluacion.AlertasYRecomendaciones.Add("⚠️ Puede aprobarse con excepción autorizada");
        }

        private static HashSet<string> NormalizarTipos(IEnumerable<string> tipos)
        {
            return new HashSet<string>(
                tipos
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim()),
                StringComparer.OrdinalIgnoreCase);
        }

        private static bool TieneServicio(ISet<string> tipos)
        {
            return tipos.Contains("Servicio")
                   || tipos.Contains("Servicio de Luz")
                   || tipos.Contains("Servicio de Gas")
                   || tipos.Contains("Servicio de Agua");
        }
    }
}
