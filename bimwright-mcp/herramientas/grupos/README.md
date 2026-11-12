# grupos

Trabajo con grupos de modelo y de detalle, en lo que bimwright no cubre: desagrupar,
cambiar o duplicar el tipo y, sobre todo, **editar un tipo de grupo** (el equivalente a
"Editar grupo" de Revit, que la API no ofrece).

bimwright ya trae `list_groups`, `get_group_members` y `create_group_from_elements`:
usarlos para listar y crear grupos simples.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code`
  y los parámetros en `data`. `usage_context`: `"project"` en modelos reales.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-grupos.md`).

## Acciones (`data.accion`)

Todo lo que modifica acepta `dry_run`, **por defecto `true`**: ejecuta, informa y deshace.
Para aplicar hay que mandar `"dry_run": false`.

| Acción | Parámetros | Qué hace |
|---|---|---|
| `listar_tipos` | — | Tipos de grupo con categoría, cantidad e ids de instancias. |
| `desagrupar` | `grupos`: id o lista | Desagrupa. Devuelve los miembros y una `referencia` (necesaria para `reagrupar` con reemplazo). |
| `reagrupar` | `elementos`, `nombre`?, `tipo_anterior`?, `referencia`?, `tolerancia_mm`? (2) | Crea un grupo con los elementos. Con `tipo_anterior` y `referencia`, además reemplaza ese tipo en todas sus instancias (ver abajo). |
| `cambiar_tipo` | `grupos`, `tipo` (id o nombre) | Cambia el tipo de las instancias. |
| `duplicar_tipo` | `tipo`, `nuevo_nombre` | Duplica un tipo de grupo. |
| `renombrar_tipo` | `tipo`, `nuevo_nombre` | Renombra un tipo de grupo. |

## Editar un tipo de grupo (flujo)

1. `desagrupar` una instancia (`dry_run: false`). Guardar `miembros` y `referencia` de la respuesta.
2. Hacer los cambios sobre esos elementos (con herramientas de bimwright o `send_code`).
   Si se agregan elementos, sumarlos a la lista; si se borran, quitarlos.
3. `reagrupar` con `elementos` (la lista), `tipo_anterior` (el `tipo_id`) y `referencia`.
   Primero con `dry_run: true` y revisar la respuesta; después con `false`.

Qué hace `reagrupar` con reemplazo, instancia por instancia del tipo anterior (cada una en
su propia subtransacción):

- Calcula dónde debe quedar cada miembro, usando el miembro de referencia (posición y giro
  de la instancia; la API no da la rotación de un grupo).
- Cambia el tipo de la instancia, la gira y la mueve a su lugar.
- **Verifica** cada miembro con posición (familias y elementos con línea). En elementos
  lineales solo cuenta la desviación perpendicular y el ángulo, porque las uniones de muros
  mueven los extremos a lo largo del eje (en Snowdon, 29,2 mm).
- Si algo queda fuera de tolerancia, deshace esa instancia y la informa en
  `instancias_omitidas` con los peores miembros.

Si todas las instancias se actualizaron, borra el tipo anterior y deja el nuevo con su
nombre. Si alguna se omitió, conserva los dos tipos y lo avisa.

## Limitaciones

- **Instancias en espejo:** no soportadas; se omiten y se informan.
- **Grupos de detalle adjuntos** a un grupo de modelo: no se manejan.
- El orden de los miembros entre instancias se asume igual (lo es en un mismo tipo); la
  verificación detecta si no.
- Familias alojadas en una cara (por ejemplo, luminarias en el cielo) pueden no moverse con
  `MoveElement`: en la prueba, una luminaria no se movió.
- Al cambiar el tipo de una instancia, Revit reemplaza sus miembros (ids nuevos) y puede
  borrar o modificar elementos asociados (etiquetas, recintos). Revisar `_changes` de la
  respuesta de bimwright.
- No guarda ni sincroniza.
