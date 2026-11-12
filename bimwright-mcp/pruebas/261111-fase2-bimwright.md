# Prueba 2026-11-11 · Fase 2 · bimwright en Snowdon Towers

- **PC:** oficina, Revit 2024.3, pid 36192.
- **bimwright:** v1.2.2 (servidor y add-in, build `6cfdfe5`), 13 toolsets activos.
- **Modelo:** `Snowdon Towers Sample Architectural.rvt` (ejemplo de Autodesk, no se guarda).
  También estaba abierto un modelo real (en segundo plano): no se tocó.
- Antes de escribir: `revit_switch_target("pid:36192", verify=true)` confirmó el documento
  (`d8e5c543…`).

## Resultados

| Paso | Prueba | Resultado |
|---|---|---|
| 1 | Conexión, niveles, vistas | **OK.** `get_current_view_info` y una consulta con `send_code`: 18 niveles, 1128 muros, vistas por tipo. Lectura en 84 ms. |
| 3 | `send_code`: consulta y cambio | **OK.** El código abre su propia `Transaction` (con `RvtMcp.Plugin.SafeFailuresPreprocessor`). Comentario escrito en el muro 619340. bimwright devuelve qué cambió (`_changes`: 1 muro modificado). |
| 2a | Rollback por advertencia | **OK.** Dos muros superpuestos: Revit avisa "Highlighted walls overlap" y bimwright deshace todo (`REVIT_FAILURE_ROLLED_BACK`), con el texto y los ids. Política por defecto: `warningPolicy: rollback`. |
| 2b | Rollback por excepción | **OK.** Excepción después de crear un muro: error devuelto, el muro no quedó. |
| 4 | Documentación | **OK.** Planta nueva `MCP PRUEBA L2`, lámina `MCP-001` (D 22x34), vista colocada, 177 etiquetas de muro en una llamada, cota de 5 ejes. Imagen: `261111-fase2-planta-l2.png`. |
| 5 | Convivencia con `pyrevit` | **OK.** `get_context` y `run_query` de pyRevit funcionan con bimwright conectado. Sin errores en Revit. |
| 6 | Velocidad | **bimwright mucho más rápido** (tabla abajo). |

### Velocidad

Misma tarea: recorrer los 34.709 elementos del modelo, contarlos por categoría (140),
contar los que tienen Comentarios y sumar el largo de muros (3886,3 m). Resultados idénticos.

| MCP | Motor | Recorrido | Total de la llamada |
|---|---|---:|---:|
| bimwright `send_code` | C# compilado | **0,9 s** | ~2,2 s (1,3 s de compilación) |
| pyrevit `run_query` | CPython 3.12 | 25,8 s | 30,7 s |
| pyrevit `run_query` | IronPython 2.7 | 34,1 s | 37,2 s |

La primera compilación de la sesión tardó 6,9 s (arranque de Roslyn); las siguientes, 0,9 a 1,6 s.

## Hallazgos

- **bimwright no pide aprobación en Revit.** pyRevit (`policy: ask`) muestra Conservar o
  Descartar; bimwright aplica el cambio de inmediato. La protección queda en los permisos
  de Claude Code (no autorizar `send_code` ni herramientas de escritura en automático) y
  en Deshacer de Revit. Cada operación queda registrada (historial por modelo).
- **`create_dimensions` no publica el formato de sus referencias.** El esquema que ve Claude
  dice solo "array". El código (`CreateDimensionsHandler.cs`) pide
  `{"element_id": <id>, "mode": "grid_curve" | "wall_centerline" | "explicit_reference_stable"}`
  y `line` con `start`/`end` en mm. Anotar en la skill de bimwright.
- `capture_view_image` guarda en `%LOCALAPPDATA%\Bimwright\rvt-mcp\captures\` (lo escribe
  Revit, así que queda en la carpeta real).
- bimwright pide llamar a `revit_report_task_result` al terminar un trabajo que cambió el
  modelo: aparece un aviso en Revit y una ventana Reports con enlaces a los elementos.
- pyRevit marcó el modelo real como modificado. Las llamadas de bimwright
  sobre ese modelo fueron solo de lectura y no registraron cambios: el cambio viene de
  otra cosa (el usuario o la resolución de referencias al abrir).

## Cambios que quedaron en Snowdon Towers (no guardar)

Comentario en muro 619340, vista 2492779, lámina 2492789, 177 etiquetas, cota 2492981.
