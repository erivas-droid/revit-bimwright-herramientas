# duplicar-lamina-con-contenido

Duplicar una lámina con todo su contenido se hace en **dos pasos**:

1. **`revit_duplicate_sheet`** de bimwright (toolset `sheets`). Crea la lámina con el mismo
   cajetín y duplica las vistas con su detalle (`duplicateViewOption: "with_detailing"`).
   Reutiliza las leyendas y vuelve a colocar las tablas, todo en la misma posición. También
   copia las revisiones y los parámetros del cajetín.
2. **Esta herramienta, `completar`**: copia lo que el paso 1 deja fuera.
   - Las anotaciones propias de la lámina: timbres como "Not for Construction", textos,
     líneas, regiones e imágenes puestas en la lámina, no en una vista.
   - El tipo de cada ventana gráfica. bimwright usa el tipo por defecto, por ejemplo cambia
     "No Title" por "Title w Line". Copia también la posición y el largo del título y la
     rotación.
   - Los parámetros de la lámina: los de proyecto, como "Sheet Sort" y "Sheet Group", y la
     retícula guía.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, PC oficina, 2026-11-11, Snowdon Towers
  (`pruebas/261111-fase3-duplicar-lamina.md`).

## Ejemplo

```json
// 1. bimwright
revit_duplicate_sheet { "sourceSheetNumber": "G101", "newSheetNumber": "G101-B", "newSheetName": "..." }
// 2. esta herramienta
{ "origen": "G101", "destino": "G101-B", "dry_run": false }
```

## Parámetros (`data`)

| Parámetro | Por defecto | Qué hace |
|---|---|---|
| `origen`, `destino` | (obligatorios) | Número de lámina (texto) o id (número). |
| `dry_run` | `true` | Simula y deshace. |
| `anotaciones` | `true` | Copia las anotaciones propias de la lámina. |
| `ventanas` | `true` | Iguala el tipo, el título y la rotación de cada ventana gráfica. |
| `parametros` | `true` | Copia los parámetros escribibles de la lámina, salvo número y nombre. |
| `forzar` | `false` | Copia las anotaciones aunque el destino ya tenga alguna. |

## Detalles

- Las ventanas del destino se emparejan con las del origen por la vista (leyendas
  reutilizadas) o por el centro más cercano, a menos de 5 mm. bimwright conserva las
  posiciones. Las que no tienen par salen en `sin_par`.
- **Para no duplicar:** si el destino ya tiene anotaciones propias, no se copian (`aviso`).
  Correr la herramienta dos veces es seguro.
- Excluye lo que cada lámina ya tiene propio: el cajetín, la tabla de revisiones del cajetín
  (una vista propia de cada lámina) y el plano de trabajo interno.
- De los parámetros que apuntan a otro elemento solo copia "Guide Grid". Los demás son
  internos, como el plano de trabajo de la lámina.

## Limitaciones

- Las vistas duplicadas quedan con el nombre que les da Revit ("L1 Copy 1"). Renombrarlas
  según el estándar de la oficina con `revit_operate_element` o a mano.
- Leer el contenido de una lámina es lento en modelos grandes, porque Revit la regenera:
  en Snowdon, unos 20 s por llamada.
- Las tablas normales quedan en dos láminas a la vez: el original y la copia.
