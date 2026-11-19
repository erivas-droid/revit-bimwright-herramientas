# auditar-plantillas-de-vista

Audita el uso de plantillas de vista (`View Templates`), detecta plantillas huérfanas (0 usos) y
vistas sin plantilla asignada, compara parámetros entre dos plantillas y permite asignar plantillas
en lote o limpiar plantillas sin uso.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `auditar` es solo lectura (admite `documento`). `asignar` y `limpiar_huerfanas`
  usan `dry_run: true` por defecto.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `auditar` *(por defecto)* | no | Lista todas las plantillas con su cantidad de usos, identifica huérfanas y vistas sin plantilla; si se pasa `comparar: ["Plantilla A", "Plantilla B"]`, devuelve las diferencias de parámetros y control entre ambas. |
| `asignar` | sí (`dry_run`) | Asigna `plantilla` a las vistas que coincidan con `vista_ids`, `filtro_nombre`, `tipo_vista` o `nivel` (opcionalmente `solo_sin_plantilla: true`). |
| `limpiar_huerfanas` | sí (`dry_run`) | Borra las plantillas de vista que ninguna vista utiliza (filtrable con `filtro_nombre`). |

## Ejemplo

```json
{
  "accion": "auditar",
  "comparar": ["Planta Arquitectura 1:50", "Planta Arquitectura 1:100"]
}
```
