# modificar-parametros-masivo

Cambia un parámetro en muchos elementos, o en sus tipos, con una operación: fijar un valor,
buscar y reemplazar, agregar prefijo o sufijo, transformar el texto (incluido un formato
de código para nombres) o vaciar.

En bimwright, `set_element_parameter_values` y `set_type_parameter_values` solo fijan
valores; buscar/reemplazar, prefijo y sufijo no existen como operación.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-modificar-parametros-masivo.md`).

## Parámetros (`data`)

| Parámetro | Qué hace |
|---|---|
| `parametro` | Nombre del parámetro. |
| `operacion` | `fijar`, `reemplazar`, `prefijo`, `sufijo`, `transformar` o `vaciar`. |
| `en_tipo` | `true`: cambia el parámetro en los **tipos** de los elementos elegidos (cada tipo una vez). Afecta a todos los ejemplares de esos tipos. |
| `filtro_valor` | Regex: solo cambia donde el valor actual calza. |
| `dry_run` | Por defecto `true`. |
| `max_detalle` | Cuántos elementos mostrar en `detalle` (20). |

Elementos: `ids`, `seleccion: true`, o `categoria` (+ `vista` id o `"activa"`) (+ `donde`:
`{parametro: valor}`), igual que en `transferir-parametros`.

### Operaciones

| Operación | Datos | Ejemplo |
|---|---|---|
| `fijar` | `valor` | `"valor": "MURO EXTERIOR"`. En números, el texto se lee en las unidades del proyecto. |
| `reemplazar` | `buscar`, `reemplazo`, `regex` | `"buscar": "^P-(\\d+)$", "reemplazo": "PTA-$1", "regex": true` |
| `prefijo` / `sufijo` | `texto` | No lo agrega si ya está. |
| `transformar` | `modo` | `mayusculas`, `minusculas`, `sin_tildes`, `codigo` |
| `vaciar` | — | Texto vacío, o "ninguno" en parámetros que apuntan a otro elemento. |

`codigo` deja el texto como nombre de código: mayúsculas, sin tildes ni ñ, solo letras,
números y guion medio; los espacios pasan a guion. Ejemplo: "Puerta Ñandú acústica 2º piso"
→ "PUERTA-NANDU-ACUSTICA-2-PISO".

`reemplazar`, `prefijo`, `sufijo` y `transformar` solo actúan sobre parámetros de texto.

## Protecciones

- **Elementos en grupos:** al cambiar ejemplares, se omiten (`omitidos_en_grupo`). Revit
  rechaza el cambio al confirmar y deshace todo, y la simulación no lo detecta. Con
  `en_tipo` no hace falta: cambiar el tipo funciona aunque tenga ejemplares en grupos
  (probado).
- **Marcas y números repetidos:** si la operación dejaría el mismo valor de Mark, número de
  puerta o número de recinto en más de un elemento, se rechaza antes de escribir. Revit da
  un aviso al confirmar, y con la política de bimwright deshace todo. Para numerar, usar
  `renumerar`.
- Primero calcula todos los valores nuevos y después escribe, cada elemento en su
  subtransacción.

## Limitaciones

- La protección de repetidos solo compara los elementos que se cambian, no el resto del
  modelo.
- Texto a número usa las unidades del proyecto: "1200" en un proyecto en milímetros es
  1.200 mm, pero en uno en pies sería 1.200'.
