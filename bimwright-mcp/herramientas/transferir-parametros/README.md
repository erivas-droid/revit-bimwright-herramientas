# transferir-parametros

Copia valores de parámetros en lote, de dos maneras:

- **`copiar`**: en cada elemento, pasa el valor de un parámetro a otro. Por ejemplo, la
  "Type Mark" del tipo al "Comments" del ejemplar, o un parámetro compartido a otro. Si los
  tipos de dato difieren, convierte a través del texto.
- **`igualar`**: copia parámetros de un elemento modelo a otros, como "igualar propiedades".
  Opcionalmente también les cambia el tipo.

En bimwright se puede hacer con `get_element_parameters` y `set_element_parameter_values`,
pero en dos llamadas por grupo de valores y sin conversión. Esta herramienta lo hace en una
sola llamada.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-transferir-parametros.md`).

## Elegir elementos (para las dos acciones)

| Parámetro | Ejemplo | Nota |
|---|---|---|
| `ids` | `[786784, 786853]` | |
| `seleccion` | `true` | Lo seleccionado en Revit. |
| `categoria` | `"Doors"` | Nombre de categoría como lo muestra Revit (en inglés en un Revit en inglés). |
| `vista` | `1350631` o `"activa"` | Con `categoria`: solo lo visible en esa vista. |
| `donde` | `{"Level": "L2"}` | Filtra por valor de parámetro (texto como lo muestra Revit, del ejemplar o del tipo). |

**Los elementos que están en grupos se omiten** (`omitidos_en_grupo`). Revit no deja
cambiarlos fuera del modo edición de grupo: rechaza el cambio al confirmar y **deshace toda la
transacción**, y la simulación no lo detecta porque deshace antes de confirmar. Para cambiar
miembros de grupo, ver la herramienta `grupos`.

## `copiar`

| Parámetro | Qué hace |
|---|---|
| `desde` | Nombre del parámetro origen. Se busca en el ejemplar y, si no está, en el tipo. |
| `hacia` | Nombre del parámetro destino. Tiene que ser del ejemplar: no escribe parámetros de tipo, porque cambiaría todos los ejemplares del tipo. |
| `solo_vacios` | `true`: no pisa los destinos que ya tienen valor. |

El `resumen` cuenta los copiados y los casos saltados: origen vacío, ya igual, destino de
tipo, de solo lectura o inexistente.

## `igualar`

| Parámetro | Qué hace |
|---|---|
| `origen` | Id del elemento modelo. |
| `parametros` | Lista de nombres a copiar. |
| `todos` | `true`: todos los parámetros escribibles del modelo, con exclusiones (ver abajo). |
| `cambiar_tipo` | `true`: les pone el tipo del modelo, si es compatible. |
| `incluir_restricciones` | `true`: con `todos`, incluye el grupo Restricciones (nivel, desfases, alturas). Por defecto se excluye porque **mueve** elementos. |

Exclusiones de `todos`, que salen en `excluidos_por_defecto`:
- marca, número de puerta y número de recinto, que deben ser únicos;
- parámetros que apuntan a otro elemento (nivel, fase, familia y tipo);
- todo lo que tenga "GUID" en el nombre, como IfcGUID;
- el grupo Restricciones.

## Detalles

- Conversión entre tipos de dato: si coinciden, se copia el valor interno, sin redondeo. Si
  no, se pasa el texto que muestra Revit; a un destino de texto va tal cual, y a uno numérico
  con `SetValueString`, en las unidades del proyecto.
- Cada elemento va en su subtransacción: si uno falla, los demás siguen (`detalle_errores`).
- `detalle` muestra hasta `max_detalle` elementos (20 por defecto).

## Limitaciones

- Texto a número usa las unidades del proyecto: "77" en un proyecto en pies se lee como
  77'. Revisar la simulación antes de aplicar.
- No escribe parámetros de tipo (para eso, `revit_set_type_parameter_values`).
- Cambiar el tipo hace que Revit recalcule lo que depende del elemento: recintos, muros
  anfitriones, rutas de evacuación. `_changes` puede mostrar cientos de elementos
  "modificados" por eso.
