# leyenda-de-colores

Colorea elementos de una vista por el valor de un parámetro (un color distinto por valor) y
crea la leyenda como vista de dibujo, lista para colocar en una lámina. También arma la
leyenda de colores que puso otra herramienta (`revit_color_elements` de bimwright,
`archi_colores` de pyRevit o a mano), leyendo las sobrescrituras de la vista.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`. Para la lámina: `revit_place_view_on_sheet` con la vista creada.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-leyenda-de-colores.md`).

## Acciones (`data.accion`)

| Acción | Modifica | Parámetros | Qué hace |
|---|---|---|---|
| `colorear` | sí (`dry_run`) | `categoria` (nombre, p. ej. `Walls`), `parametro`, `vista`?, `colores`? | Sobrescribe superficie y corte con relleno sólido, un color por valor. `colores: {"valor": [r,g,b]}` fija colores; el resto se reparte en el círculo de tonos. |
| `leer` | no | `categoria`?, `parametro`?, `vista`? | Entradas `{valor, rgb, cantidad, ejemplo}` según las sobrescrituras de la vista. Avisa `valores_con_varios_colores` y `colores_con_varios_valores`. |
| `crear` | sí (`dry_run`) | `categoria`/`parametro`/`vista` **o** `entradas: [{texto, rgb, cantidad?}]`; `titulo`?, `nombre_vista`?, `mostrar_cantidad`?, `reemplazar`?, `tipo_texto`?, `tipo_region`?, `ancho_mm` (12), `alto_mm` (6), `separacion_mm` (2) | Vista de dibujo a escala 1:1 con el título y, por entrada, una región rellena del color y su texto. `reemplazar: true` borra antes la vista de dibujo con el mismo nombre. |

`vista` por defecto es la activa. `dry_run` por defecto `true`.

## Detalles

- El valor de parámetros de tipo Id (como "Family and Type") se muestra como
  `Familia: Tipo`. Esto resuelve el problema de `archi_colores`, que agrupaba todo en uno.
- **`revit_color_elements` de bimwright repite una paleta de 10 colores**: con más de 10
  valores la leyenda es ambigua (`leer` lo avisa en `colores_con_varios_valores`). Para
  leyendas, colorear con esta herramienta.
- Si un valor aparece con varios colores, `crear` usa el de la mayoría y lo informa en
  `colores_descartados`.
- Las regiones usan el primer tipo de región rellena (o `tipo_region`) con sobrescritura de
  relleno sólido y sin patrón de fondo: no crea tipos nuevos. El texto usa el tipo de texto
  por defecto (o `tipo_texto`, por ejemplo el tipo de texto del estándar de la oficina).

## Limitaciones

- El colector de elementos de una vista puede incluir elementos que no se ven (en la prueba,
  muros de otros niveles) hasta que la vista se regenera; los conteos de `colorear` pueden
  salir más altos. La leyenda de `crear` hecha después fue correcta. Si importa, correr
  `leer` antes de `crear` y revisar.
- Con muchos valores (más de ~20) algunos colores se parecen. Usar `colores` para fijar los
  importantes.
- Solo una columna. Ordena por valor, alfabéticamente.
- No usa los esquemas de color nativos de Revit (recintos y áreas tienen su propia leyenda).
