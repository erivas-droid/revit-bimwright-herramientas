# renombrar-vistas-y-laminas

Audita y renombra vistas y/o láminas en lote mediante reglas configurables: buscar/reemplazar
(texto o regex), prefijo, sufijo, transformaciones (`mayusculas`, `minusculas`, `sin_tildes`,
`codigo`) o plantillas con tokens (`{nivel}`, `{tipo}`, `{escala}`, `{nombre}`, `{numero}`).
Revisa choques de nombre/número antes de escribir y usa doble pasada temporal para permitir
intercambios sin colisiones.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `auditar` es solo lectura (admite `documento`). `renombrar` usa `dry_run: true` por defecto.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `auditar` *(por defecto)* | no | Si se pasa `regla_nombre`, valida qué vistas/láminas cumplen la regex. Si se pasa `operacion`, devuelve la vista previa antes/después y los posibles choques sin abrir transacción. |
| `renombrar` | sí (`dry_run`) | Aplica el renombrado en lote tras comprobar que no haya choques de nombre o número de lámina. |

## Parámetros

| Parámetro | Obligatorio | Por defecto | Descripción |
|---|---|---|---|
| `objetivo` | no | `"vistas"` | `"vistas"` o `"laminas"`. |
| `campo` | no | `"nombre"` | `"nombre"` (vistas y láminas) o `"numero"` (solo láminas). |
| `operacion` | en `renombrar` | — | `"reemplazar"`, `"prefijo"`, `"sufijo"`, `"transformar"` o `"plantilla"`. |
| `regla_nombre` | no | — | Regex para auditar cumplimiento de nomenclatura en `auditar`. |
| `filtro_nombre` | no | — | Regex para filtrar qué vistas/láminas incluir. |
| `tipo_vista` | no | — | Filtra vistas por `ViewType` (`FloorPlan`, `CeilingPlan`, `Section`, `Elevation`, `ThreeD`, `AreaPlan`, etc.). |
| `nivel` | no | — | Filtra plantas por nombre de nivel asociado. |
| `ids` | no | — | Lista explícita de `ElementId`. |
| `incluir_plantillas` | no | `false` | Incluir plantillas de vista (`IsTemplate`). |
| `buscar` / `reemplazo` / `regex` | con `reemplazar` | — | Texto o expresión regular a sustituir. |
| `texto` | con `prefijo`/`sufijo` | — | Texto a anteponer o agregar (evita duplicarlo si ya existe). |
| `modo` | con `transformar` | — | `"mayusculas"`, `"minusculas"`, `"sin_tildes"` o `"codigo"`. |
| `plantilla` | con `plantilla` | — | Patrón con tokens `{nivel}`, `{tipo}`, `{escala}`, `{nombre}`, `{numero}`. |
| `dry_run` | no | `true` | Solo en `renombrar`: simula y deshace. |

## Ejemplo

```json
{
  "accion": "renombrar",
  "dry_run": true,
  "objetivo": "vistas",
  "tipo_vista": "FloorPlan",
  "filtro_nombre": "^Planta",
  "operacion": "plantilla",
  "plantilla": "ARQ-{nivel}-PLANTA-1_{escala}"
}
```
