# renumerar

Numera en secuencia la Mark de puertas, ventanas, equipos, etc., el número de los recintos u
otro parámetro de texto, según la posición de los elementos o un orden dado. Antes de
escribir revisa que no queden números repetidos.

bimwright solo trae `revit_renumber_sheets`, que renumera láminas.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-renumerar.md`).

## Parámetros (`data`)

| Parámetro | Por defecto | Qué hace |
|---|---|---|
| elementos | — | `ids`, `seleccion: true` o `categoria` (+ `vista`) (+ `donde`), igual que en `transferir-parametros`. |
| `parametro` | Mark, o Number en recintos | Otro parámetro de texto del ejemplar. |
| `orden` | `filas` | Ver tabla de abajo. |
| `formato` | `{n}` | `{n}` número; `{n:000}` con ceros (001); `{nivel}` nombre del nivel. Ej.: `P-{n:000}`, `2{n:00}`, `{nivel}-{n:00}`. |
| `inicio`, `paso` | 1, 1 | |
| `por_nivel` | `false` | Reinicia la cuenta en cada nivel, ordenados por cota. Suele ir con `{nivel}` en el formato. |
| `tolerancia_fila_mm` | 1000 | Para `filas` y `columnas`: cuánto pueden diferir en Y (o X) dos elementos de la misma fila. |
| `dry_run` | `true` | |

### Órdenes

| `orden` | Cómo numera |
|---|---|
| `filas` | De arriba abajo; dentro de cada fila, de izquierda a derecha. |
| `columnas` | De izquierda a derecha; dentro de cada columna, de arriba abajo. |
| `x` / `y` | Solo por X (izquierda a derecha) o solo por Y (arriba abajo). |
| `ruta` | A lo largo de una línea de modelo o de detalle (`ruta_id`), en el sentido en que se dibujó. Sirve para numerar siguiendo un pasillo. |
| `parametro` | Por el valor de otro parámetro (`parametro_orden`), en orden natural ("2" antes que "10"). |
| `ids` | En el orden de la lista `ids`. |

La posición es el punto de inserción; en elementos por línea, el punto medio; si no hay ninguno
de los dos, el centro de la caja.

## Protecciones

- **Repetidos dentro de la secuencia** (por ejemplo, `por_nivel` sin `{nivel}`): se rechaza.
- **Choques con otros elementos de la misma categoría** que ya usan alguno de los números
  nuevos, incluidos los que están en grupos: no aplica nada y los lista en `choques`. Revit
  avisaría por repetidos al confirmar y, con la política de bimwright, desharía todo.
  `forzar: true` lo intenta igual, pero con esa política no sirve de nada.
- **Intercambiar números dentro del mismo grupo de elementos funciona**, por ejemplo cuando
  el 203 pasa a 201 y el 201 a 205: Revit solo revisa los repetidos al confirmar, cuando ya
  no quedan.
- Los elementos en grupos se omiten (`omitidos_en_grupo`). Los recintos sin ubicar, también.

## Limitaciones

- "Arriba" es +Y en coordenadas del proyecto, no el norte real ni la orientación de una vista
  girada.
- La revisión de choques compara con toda la categoría en el modelo, incluidas otras fases
  y opciones de diseño: puede avisar de más, nunca de menos.
- No renumera láminas: para eso, `revit_renumber_sheets` de bimwright.
