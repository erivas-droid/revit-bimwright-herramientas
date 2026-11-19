# verificar-recintos-y-areas

Control de calidad espacial pre-entrega para `Rooms` y `Areas`: detecta elementos sin ubicar,
no cerrados o redundantes, números duplicados por nivel, recintos sin nombre y contrasta las
sumatorias de superficie entre `Areas` y `Rooms` nivel por nivel. Incluye limpieza opcional de
elementos sin ubicar (`Location == null`).

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `auditar` es solo lectura y admite `documento` para consultar otro modelo abierto.
  `limpiar_sin_ubicar` usa `dry_run: true` por defecto y omite elementos dentro de grupos.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `auditar` *(por defecto)* | no | Revisa todos los `Rooms` y `Areas` (filtrables por `esquema` y `nivel`), informa problemas geométricos/de datos y genera una tabla de contraste `Areas` vs `Rooms` por nivel. |
| `limpiar_sin_ubicar` | sí (`dry_run`) | Elimina `Rooms` y/o `Areas` sin ubicar (`Location == null`), omitiendo los que pertenezcan a grupos. |

## Parámetros

| Parámetro | Obligatorio | Por defecto | Descripción |
|---|---|---|---|
| `esquema` | no | todos | Filtra `Areas` por nombre de `AreaScheme`. |
| `nivel` | no | todos | Filtra `Rooms` y `Areas` por nombre de nivel. |
| `documento` | no | activo | Solo en `auditar`: título de otro modelo abierto. |
| `max_detalle` | no | `30` | Máximo de elementos en cada muestra de incidencias. |
| `recintos` | no | `true` | Solo en `limpiar_sin_ubicar`: incluir `Rooms` sin ubicar. |
| `areas` | no | `true` | Solo en `limpiar_sin_ubicar`: incluir `Areas` sin ubicar. |
| `dry_run` | no | `true` | Solo en `limpiar_sin_ubicar`: simula y deshace. |

## Ejemplo

```json
{
  "accion": "auditar",
  "esquema": "Municipal"
}
```
