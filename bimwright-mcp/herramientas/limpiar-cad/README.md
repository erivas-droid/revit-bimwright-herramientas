# limpiar-cad

CAD en el modelo: lista importaciones y vínculos, reubica vínculos rotos buscando el archivo
por nombre y borra lo que sobra: importaciones, vínculos rotos y patrones de línea `IMPORT-*`.

En bimwright, `list_linked_cad` (toolset `links`) y `delete_element` (toolset `delete`) no
están activos, y no hay limpieza en un paso ni reubicación.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon con un DWG propio;
  un modelo real en solo lectura (`pruebas/261111-fase3-limpiar-cad.md`).

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `listar` | no | Tipos CAD con su estado, ruta, si el archivo existe en este PC, si está roto y cuántas instancias tiene; instancias con nombre, si son vínculo, si están solo en una vista, en grupo o fijadas; patrones `IMPORT-*` y cuántos no se usan. |
| `reubicar` | sí (`dry_run`) | Para los vínculos rotos, o los nombrados en `vinculos`, busca el archivo por nombre bajo `carpeta_base` (una ruta o una lista; admite `%OneDriveCommercial%`, `%USERPROFILE%`). Si hay exactamente un candidato, recarga el vínculo desde ahí (`LoadFrom`). Si hay varios o ninguno, lo informa. |
| `limpiar` | sí (`dry_run`) | `importados: true` borra las importaciones (no los vínculos) y sus tipos si quedan sin uso. `vinculos_rotos: true` borra los vínculos rotos. `vinculos: [nombres]` borra esos vínculos. `patrones_import: true` borra los `IMPORT-*` que no usa ninguna categoría. |

`dry_run` por defecto `true`.

## Cuidado con "roto"

- **Revit sigue diciendo `Loaded`** aunque el archivo ya no exista: el estado describe lo que
  tiene en memoria. Por eso la herramienta también revisa si el archivo existe en el disco
  de este PC (`archivo_existe`). En rutas de nube (ACC, BIM 360) no se puede revisar y queda
  `null`.
- **Roto en este PC no significa perdido.** En un modelo real, los 10 vínculos estaban `NotFound`
  porque apuntaban a carpetas de otros PC y usuarios (`C:\Users\<otro>\…`), pero los 10 archivos están en las carpetas sincronizadas de este PC. **Primero `reubicar`**, y
  después, si hace falta, `limpiar` con `vinculos_rotos`.

Ejemplo:

```json
{ "accion": "reubicar", "dry_run": true,
  "carpeta_base": ["%USERPROFILE%\\Proyectos", "%OneDriveCommercial%\\Proyectos"] }
```

## Detalles

- Las instancias en grupos no se borran (`omitidos`). Borrarlas fuera del modo edición de
  grupo deshace toda la transacción al confirmar.
- Al borrar un vínculo se borra su tipo, y con él todas sus instancias y las subcategorías de
  capas.
- `reubicar` recorre las carpetas una sola vez: en un modelo real, 10 archivos en 2 carpetas grandes
  tardan ~15 s.
- El nombre de una instancia CAD es su posición ("location <Not Shared>"); la herramienta
  muestra el del tipo, que es el del archivo.

## Limitaciones

- No reubica vínculos de nube (ACC, BIM 360).
- La reubicación busca solo por nombre de archivo, no por contenido. Si hay dos copias, no
  elige.
- `patrones_import` solo revisa el uso en categorías y subcategorías, no en filtros de vista
  ni sobrescrituras. Revisar la simulación.
