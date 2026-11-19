# consolidar-y-purgar-tipos

Audita tipos de sistema y cargables con **0 instancias** y posibles tipos duplicados por nombre
(`(1)`, ` 2`, `Copia`), consolida tipos repetidos migrando sus ejemplares a un tipo destino y purga
tipos sin uso de forma controlada por categoría o expresión regular.

En bimwright, `revit_purge_unused` borra símbolos de familia sin filtro ni vista previa y no
consolida tipos duplicados.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `auditar` es solo lectura (admite `documento`). `consolidar` y `purgar_sin_uso`
  usan `dry_run: true` por defecto, omiten ejemplares en grupos y conservan siempre al menos un
  tipo por familia.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `auditar` *(por defecto)* | no | Cuenta instancias por tipo, lista tipos sin uso por categoría y detecta posibles tipos duplicados por sufijo. |
| `consolidar` | sí (`dry_run`) | Recibe `mapa: { "tipo_origen": "tipo_destino" }` (por nombre o id), migra los ejemplares fuera de grupos al tipo destino y opcionalmente borra el origen si queda sin uso. |
| `purgar_sin_uso` | sí (`dry_run`) | Borra tipos con 0 instancias filtrados por `categoria` y/o `filtro_nombre` (o `todos: true`), protegiendo el último tipo de cada familia. |

## Ejemplo

```json
{
  "accion": "consolidar",
  "dry_run": true,
  "categoria": "Walls",
  "mapa": {
    "Muro Basico 150 Copia 1": "Muro Basico 150"
  },
  "borrar_origen": true
}
```
