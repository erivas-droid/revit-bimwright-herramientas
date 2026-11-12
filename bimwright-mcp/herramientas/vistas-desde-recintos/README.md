# vistas-desde-recintos

Crea una planta ampliada por recinto, recortada a su contorno, con escala, plantilla y
nombre. Si se pide, agrega los 4 alzados interiores, recortados al recinto: de ancho, del
piso al cielo y de profundidad.

bimwright tiene las piezas sueltas (`revit_create_callout_view` con un rectángulo, de a una,
y `revit_set_view_crop`). Esta herramienta hace el flujo completo para muchos recintos en
una llamada.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`. Para ponerlas en láminas: `revit_place_view_on_sheet`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-vistas-desde-recintos.md`).

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `listar` | no | Recintos elegidos con número, nombre, nivel, área, estado (ok, sin ubicar, no cerrado) y la planta padre que se usaría. |
| `crear` | sí (`dry_run`) | Crea las vistas. Informa por recinto la vista, el tipo de recorte y los alzados con sus medidas. |

## Elegir recintos (se combinan)

`recinto_ids`, `nivel` (nombre), `nombre` (regex sobre el nombre del recinto), `numero` (regex),
o `todos: true`. `crear` exige alguno.

## Parámetros de `crear`

| Parámetro | Por defecto | Qué hace |
|---|---|---|
| `modo` | `llamada` | `llamada`: llamada desde la planta del nivel; en la planta queda la marca de llamada. `planta`: planta independiente del nivel, recortada. |
| `vista_padre` | (automática) | Id de la planta desde la que se hacen las llamadas. Si no se da, se toma la primera planta de piso del nivel por nombre que no sea llamada ni dependiente, o la que calce con `padre_nombre` (regex). |
| `escala` | `50` | Escala de la planta y de los alzados. |
| `margen_mm` | `300` | Holgura del recorte alrededor del recinto. |
| `recorte` | `rectangulo` | `contorno` recorta con la forma del recinto (útil en recintos en L). Si no se puede, queda el rectángulo y lo informa. |
| `plantilla` | — | Plantilla de vista para la planta ampliada (nombre). |
| `tipo_vista` | (la del padre o la primera de planta) | Tipo de vista (nombre). |
| `nombre_vista` | `{numero} {nombre} - AMPLIADA` | Patrón; también `{nivel}`. |
| `alzados` | `false` | Crea un marcador con los 4 alzados interiores. |
| `marcador` | `punto` | Dónde va el marcador: `punto` (el punto del recinto, siempre adentro) o `centro` (centro del rectángulo; puede caer fuera en recintos en L). |
| `tipo_alzado` | (el que tenga "Interior" en el nombre, o el primero) | Tipo de vista de alzado (nombre). |
| `plantilla_alzado` | — | Plantilla para los alzados. |
| `nombre_alzado` | `{numero} {nombre} - ALZADO {dir}` | `{dir}` = N, S, E u O según hacia dónde mira, en coordenadas del proyecto. |

## Detalles

- Cada recinto va en su subtransacción: si uno falla, los demás siguen (`detalle_errores`).
- Se omiten los recintos sin ubicar o no cerrados, y los que ya tienen una vista con ese
  nombre. Correr dos veces no duplica.
- La planta padre de cada nivel se elige antes de crear nada. Si no, una llamada recién
  creada (por ejemplo "202 ...", que va antes que "L2" en orden alfabético) quedaba como padre
  del siguiente recinto.
- Alzados: recorte horizontal al ancho del recinto visto desde el marcador, vertical del
  piso a la altura del recinto (más el margen), y recorte lejano activado a la profundidad
  del recinto.
- Una plantilla que no controla el recorte respeta el recorte hecho por la herramienta
  (probado con "Architectural Plan").

## Limitaciones

- Si la plantilla controla "Crop View", puede quitar el recorte.
- Los alzados se nombran por dirección del proyecto (N/S/E/O), no por norte real.
- La herramienta no pone vistas en láminas: para eso, `revit_place_view_on_sheet`.
