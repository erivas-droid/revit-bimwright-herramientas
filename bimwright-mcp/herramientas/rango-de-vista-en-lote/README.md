# rango-de-vista-en-lote

Lee y cambia el rango de vista (superior, corte, inferior y profundidad) de muchas plantas a
la vez: plantas de piso, de cielo, estructurales y de área, y sus plantillas. Se puede dar el
rango plano por plano o copiarlo de una vista modelo.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-rango-de-vista-en-lote.md`).

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `leer` | no | Rango de cada vista en texto (`superior: asociado +2286 \| corte: ...`), su plantilla y si esta controla el rango. Agrupa las vistas por `combinaciones` de rango iguales. `detalle: true` agrega nivel real, desfase y cota de cada plano. |
| `aplicar` | sí (`dry_run`) | Cambia los planos indicados en `rango` y/o copia el rango de `desde_vista`. Informa por vista `antes` y `despues`. |

`dry_run` por defecto `true`.

## Elegir vistas (para las dos acciones; los filtros se combinan)

| Parámetro | Ejemplo | Nota |
|---|---|---|
| `vista_ids` | `[607509, 29485]` | Puede incluir plantillas. |
| `nombre` | `"^L\\d_SD$"` | Expresión regular sobre el nombre, sin distinguir mayúsculas. |
| `nivel` | `"L2"` | Nombre del nivel asociado. |
| `tipo` | `"planta"` | `planta`, `cielo`, `estructural`, `area` (o el nombre del `ViewType`). |
| `incluir_plantillas` | `true` | Sin esto, las plantillas solo entran por `vista_ids`. |
| `todas` | `true` | `aplicar` exige algún filtro o esto. |

## El rango en `aplicar`

```json
"rango": {
  "corte":       { "desfase_mm": 1100 },
  "profundidad": { "nivel": "inferior", "desfase_mm": 0 }
}
```

- Planos: `superior`, `corte`, `inferior`, `profundidad`. Cada uno y cada campo son
  opcionales: lo que no se indica queda como está.
- `nivel`: `asociado`, `superior` (nivel de arriba), `inferior` (nivel de abajo),
  `ilimitado`, o el nombre de un nivel. Los relativos se resuelven en cada vista.
- `desde_vista: <id>` copia el rango de esa vista en forma relativa: si allí el corte está
  en su nivel asociado +1100, en cada destino queda en el nivel asociado del destino +1100.
  Se puede combinar con `rango` para cambiar algún plano encima de lo copiado.

## Detalles

- **Vistas con plantilla que controla el rango:** se omiten (`detalle_omitidas`, con el
  nombre de la plantilla). Para cambiarlas, aplicar a la plantilla
  (`incluir_plantillas: true` y su nombre, o su id): la plantilla lo pasa a sus vistas.
- Cada vista va en su propia subtransacción: si Revit rechaza el rango de una, esa queda
  como estaba (`detalle_errores`, con `pista`) y las demás siguen.
- Revit exige, en cota, superior ≥ corte ≥ inferior ≥ profundidad en plantas de piso; en
  plantas de cielo el orden es el inverso. Por eso un mismo `rango` no sirve para plantas y
  cielos a la vez: filtrar con `tipo`.
- En el texto, `asociado`, `superior` e `inferior` se deducen comparando con el nivel de la
  vista y los niveles vecinos por cota. Revit guarda "nivel de abajo" como valor especial
  en las vistas, y como nivel concreto en las vistas que lo reciben de una plantilla.

## Limitaciones

- "Nivel de arriba/abajo" lo calcula la herramienta por cota entre todos los niveles; Revit
  podría usar solo los niveles que son planta de edificio. La `cota_mm` de `detalle` es
  aproximada en esos casos.
- No toca el rango de la subyacente (`Underlay`).
