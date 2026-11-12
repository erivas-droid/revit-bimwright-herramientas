---
name: revit-bimwright
description: Reglas para trabajar en Revit con bimwright (MCP rvt-mcp, herramientas mcp__rvt-mcp__revit_*), incluido send_code en C# y las herramientas propias de bimwright-mcp/herramientas (grupos, estándares gráficos, renumerar y otras). Usar antes de consultar o cambiar un modelo de Revit con bimwright.
---

# Revit con bimwright

**bimwright** (`rvt-mcp`, C#) da acceso a Revit con herramientas tipadas y con `send_code`,
que ejecuta C# dentro de Revit. Herramientas propias, inventario y pruebas en
`bimwright-mcp/`. Si hay otras skills de Revit con reglas de la oficina o de otro MCP,
seguirlas también.

## Qué usar

| Tarea | Cómo |
|---|---|
| Consultas y cambios en general, documentación (vistas, láminas, etiquetas, cotas), parámetros, tablas | **bimwright** (herramienta tipada si existe; si no, `send_code`) |
| Recorrer muchos elementos | **bimwright** `send_code`: unas 27 veces más rápido que pyRevit (pruebas 2026-11-11) |
| Grupos (desagrupar, editar tipo, cambiar o duplicar tipo) | herramienta propia `bimwright-mcp/herramientas/grupos` |

Lista completa de herramientas de bimwright, con el toolset de cada una:
`bimwright-mcp/herramientas/INVENTARIO.md`. Toolsets activos en
`%LOCALAPPDATA%\Bimwright\rvt-mcp\rvtmcp.config.json`.

## Flujo

1. `revit_list_available_targets` y luego `revit_switch_target("pid:<n>", verify=true)`
   antes de cualquier escritura. Confirmar en la respuesta que el documento es el esperado.
2. **Modelos reales:** nunca guardar ni sincronizar. Revisar en `window_title` que el
   documento activo sea el correcto: Revit puede tener abierto un modelo real además del
   de prueba.
3. Para probar, usar los modelos de ejemplo de Autodesk
   (`C:\Program Files\Autodesk\Revit 2024\Samples\`).
4. Al terminar un trabajo que cambió el modelo, llamar una vez a
   `revit_report_task_result`: deja un reporte en Revit con enlaces a los elementos.

## Seguridad

- **bimwright aplica los cambios de inmediato**, sin preguntar en Revit. Antes de usar
  `send_code` o una herramienta que escribe, decir al usuario qué se va a cambiar.
- Las advertencias de Revit deshacen la transacción por defecto (`warningPolicy: rollback`)
  y llegan en `_failure` con el texto y los ids. No reintentar sin cambiar el plan.
- `_changes` informa lo agregado, modificado y borrado. Revisarlo después de cada escritura.

## send_code (`revit_send_code_to_revit`)

- Cuerpo con `app`, `doc`, `uidoc` que termina en `return ...;`, o fuente completa con
  `public class McpDynamicScript { public static object Run(UIApplication app, JObject data) }`
  (incluir `using Autodesk.Revit.UI; using Newtonsoft.Json.Linq;`).
- Pasar valores en `data` (objeto JSON), no pegarlos en el código.
- El código abre su propia `Transaction` y le pone
  `new RvtMcp.Plugin.SafeFailuresPreprocessor()` en las opciones de fallas.
- `compile_only: true` para validar sin ejecutar. `usage_context`: `"test"` o `"project"`.
- Unidades internas en pies: mm = pies × 304,8. Las herramientas tipadas de bimwright usan mm.
- La primera compilación de la sesión tarda unos 7 s; las siguientes, 1 a 2 s.

## Herramientas propias (`bimwright-mcp/herramientas/<nombre>/`)

Cada una tiene `herramienta.cs` (la fuente) y `README.md`. Todo lo que modifica acepta
`dry_run` (por defecto `true`): primero simular, revisar y después aplicar.

**Cómo llamarlas (preferido: módulo).** Cada herramienta se guarda en bimwright como módulo
`arch_<nombre>` con la clase `Arch<Nombre>`; la lista está en `bimwright-mcp/modulos/indice.json`.
Así la llamada es una línea en vez de 10 a 25 KB de código:

```csharp
using Autodesk.Revit.UI; using Newtonsoft.Json.Linq;
public class McpDynamicScript { public static object Run(UIApplication app, JObject data) { return ArchRenumerar.Run(app, data); } }
```

con `modules: ["arch_renumerar"]` y los parámetros en `data`.

- Antes de usar uno, revisar con `revit_list_code_modules` que exista. Los módulos se guardan
  en el almacenamiento local de bimwright de cada PC y no se sincronizan.
- Si no existe, o si cambió `herramienta.cs`:
  1. correr `python -I bimwright-mcp/herramientas/generar_modulos.py`, que escribe
     `bimwright-mcp/modulos/`;
  2. guardar con `revit_save_code_module` (nombre `arch_<nombre>`, código el de
     `modulos/arch_<nombre>.cs`). Para reemplazar uno, pasar su `expected_hash` de la lista.
- Alternativa sin módulo: enviar el contenido completo de `herramienta.cs` como `code`.

| Herramienta | Para qué |
|---|---|
| `grupos` | Desagrupar, editar un tipo de grupo (reagrupar y reemplazar en todas sus instancias, con verificación), cambiar, duplicar o renombrar tipo |
| `estandares-graficos` | Estilos y patrones de línea según un estándar en JSON: capturar, auditar (solo lectura, con vista previa del plan) y aplicar (renombrar por reglas, reasignar, corregir, crear, borrar). Ejemplo en `ejemplo-estandar.json`. Las líneas en grupos no se tocan |
| `leyenda-de-colores` | Colorear una vista por parámetro con un color distinto por valor (también parámetros Id como "Family and Type") y crear la leyenda como vista de dibujo. Preferirla a `revit_color_elements`, que repite 10 colores |
| `rango-de-vista-en-lote` | Leer y cambiar el rango de vista de muchas plantas o plantillas a la vez (por nombre, nivel o tipo), o copiarlo de una vista modelo. Omite las vistas cuyo rango controla la plantilla |
| `duplicar-lamina-con-contenido` | Después de `revit_duplicate_sheet`: copia las anotaciones propias de la lámina, el tipo y título de cada ventana gráfica y los parámetros de proyecto de la lámina, que bimwright no copia |
| `vistas-desde-recintos` | Planta ampliada por recinto (llamada desde la planta del nivel o planta propia), recortada a su contorno, con escala, plantilla y nombre; opcionalmente los 4 alzados interiores recortados al recinto |
| `transferir-parametros` | `copiar` un parámetro a otro en cada elemento (por ejemplo, de tipo a ejemplar, con conversión) e `igualar` parámetros y tipo desde un elemento modelo. Omite elementos en grupos |
| `modificar-parametros-masivo` | Un parámetro en muchos elementos o en sus tipos: fijar, reemplazar (regex), prefijo, sufijo, transformar (mayúsculas, sin tildes, nombre tipo código) o vaciar. Rechaza marcas repetidas |
| `renumerar` | Mark o número de recinto en secuencia por posición (filas, columnas, a lo largo de una línea), por parámetro o por lista; formato `P-{n:000}`, `{nivel}`; revisa choques con números existentes antes de escribir |
| `limpiar-cad` | Listar importaciones y vínculos CAD (detecta rotos por archivo, no solo por estado), reubicar vínculos rotos buscando el archivo bajo carpetas (OneDrive/SharePoint) y borrar importaciones, vínculos rotos y patrones `IMPORT-*` |

**Modelos reales:** las herramientas se pueden usar en solo lectura (`listar`, `leer`,
`auditar` con `documento`). Escribir en un modelo real solo a pedido explícito del usuario,
con el modelo sincronizado. Un `dry_run` también escribe y deshace: no usarlo en un modelo real
sin ese permiso.

**`dry_run` no detecta los fallos que Revit da al confirmar.** Por ejemplo, cambiar miembros
de grupos fuera del modo edición deshace toda la transacción al confirmar. Filtrar antes los
elementos en grupos (`GroupId`) en cualquier `send_code` que escriba.

Leer el contenido de láminas es lento en modelos grandes (Revit las regenera): no recorrer
todas las láminas; filtrar antes por número o nombre.

Si la herramienta lee un archivo (como `estandar_archivo`), pasar la ruta completa. Revit
expande variables de entorno (`%USERPROFILE%`, `%OneDriveCommercial%`).

## Detalles de herramientas de bimwright

- `revit_create_dimensions`: su esquema no lo dice, pero cada referencia es
  `{"element_id": <id>, "mode": "grid_curve" | "wall_centerline" | "explicit_reference_stable"}`;
  `line` = `{"start": {x,y,z}, "end": {x,y,z}}` en mm.
- `revit_capture_view_image` guarda en `%LOCALAPPDATA%\Bimwright\rvt-mcp\captures\`.
- La ubicación de un grupo (`Group.Location`) no tiene rotación en la API: para saber el
  giro de una instancia, usar un miembro (ver `herramientas/grupos`).
- La lista de subcategorías (`Category.SubCategories`) no muestra las recién creadas ni
  quita las borradas hasta `doc.Regenerate()`. Leer una borrada da "Category is unexpectedly
  NULL". Borrar en lote al final y regenerar.
- Crear un grupo en la misma transacción en que se modificó uno de sus elementos da un aviso
  que, con `warningPolicy: rollback`, deshace todo. Separar en dos transacciones.
- Un estilo de línea no se puede renombrar por API (`GraphicsStyle.Name`); la interfaz de
  Revit sí lo permite.
- `CADLinkType.Reload()` necesita transacción. `GetLinkedFileStatus()` puede decir `Loaded`
  aunque el archivo ya no exista.
