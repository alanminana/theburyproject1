using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBuryProyect.Migrations
{
    /// <inheritdoc />
    public partial class NormalizarPlanCreditoPersonalCuotaNullHistorico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ML4 — Fase 5: data-fix auditable, no runtime fallback. Antes de ML2 (contrato
            // congelado en curso, sin commit/push todavia — ver Services/CreditoConfiguracionVentaService.cs
            // y Services/CreditoSimulacionVentaService.cs), un plan global activo con TasaMensual
            // NULL resolvia, en la rama "Global" (sin producto/cliente con configuracion propia),
            // exactamente a ConfiguracionPago.TasaInteresMensualCreditoPersonal del medio
            // CreditoPersonal ("BuscarPlan(cuotas)?.TasaMensual ?? tasaGlobal.Value", identico en
            // ambos servicios). Es el unico valor efectivo historico posible para ese caso: no se
            // copia desde Producto/Perfil/Cliente (nunca fueron autoridad de este campo) ni se
            // hardcodea un porcentaje de negocio, se lee tal cual esta persistido hoy en la columna
            // legacy. Alcance deliberadamente acotado: solo toca planes ACTIVOS con TasaMensual
            // NULL cuya configuracion global tenga un valor explicito; un plan inactivo con NULL
            // se deja intacto (Fase 2 del goal: el historico de un plan inactivo no se normaliza
            // automaticamente) y un plan activo NULL sin tasa global configurada tampoco se toca
            // (no hay valor inequivoco del que partir).
            migrationBuilder.Sql(@"
                UPDATE cuota
                SET cuota.TasaMensual = medio.TasaInteresMensualCreditoPersonal
                FROM [ConfiguracionCreditoPersonalCuotas] cuota
                INNER JOIN [ConfiguracionesPago] medio
                    ON medio.TipoPago = 5 -- TipoPago.CreditoPersonal
                WHERE cuota.Activo = 1
                  AND cuota.TasaMensual IS NULL
                  AND medio.TasaInteresMensualCreditoPersonal IS NOT NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reversion: no hay forma de distinguir, despues del Up, cuales filas volvian a NULL
            // (la unica marca era la ausencia de valor). Down() no intenta "adivinar" cuales
            // revertir: es una migracion de datos de un solo sentido, documentada como tal. Esto
            // es deliberado (ver Up()) y no revierte nada; si hace falta deshacer, restaurar desde
            // backup.
        }
    }
}
