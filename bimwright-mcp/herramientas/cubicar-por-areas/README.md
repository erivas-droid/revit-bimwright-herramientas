# cubicar-por-areas

Cubica elementos modelados (pisos, muros, cielos, puertas, ventanas, mobiliario, etc.) cruzándolos
geométricamente contra los polígonos de un **Area Plan** (`AreaScheme`) o de **Rooms** del mismo
nivel. Devuelve totales por tipo, subtotales por grupo (por ejemplo, edificio o torre) y desglose
por unidad (por ejemplo, departamento u oficina), con exportación opcional a CSV.

En bimwright, `get_material_quantities` suma globales por categoría y `compute_room_finishes` solo
revisa muros/pisos límite de `Rooms`; ninguna cruza elementos contra `Area Plans` ni reparte
proporcionalmente elementos que cruzan varias zonas o quedan en el borde exterior.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Solo lectura:** no abre transacciones ni modifica el modelo. Admite `documento` para consultar
  otro proyecto abierto sin cambiar la pestaña activa en Revit.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `esquemas` | no | Lista los `AreaScheme` del modelo, la cantidad y superficie de áreas por esquema y nivel, los recintos por nivel y la vista objetivo. |
| `cubicar` *(por defecto)* | no | Cruza geométricamente los elementos de las categorías pedidas en la vista contra las zonas del nivel y devuelve totales por tipo, por grupo y por unidad. |

## Parámetros de `cubicar`

| Parámetro | Obligatorio | Por defecto | Descripción |
|---|---|---|---|
| `documento` | no | activo | Título (exacto o prefijo) de otro modelo abierto para consultar en segundo plano. |
| `vista` | no | activa | Id numérico o nombre de la vista a cubicar (obligatorio si se usa `documento`). |
| `nivel` | no | nivel de la vista | Nombre del nivel cuyas áreas o recintos se usarán como zonas. |
| `origen_zonas` | no | `"areas"` | `"areas"` (Area Plan) o `"recintos"` (Rooms). |
| `esquema` | sí con `"areas"` | — | Nombre del `AreaScheme` (por ejemplo `"Rentable"`, `"Municipal"`). |
| `categorias` | no | `["Floors", "Walls"]` | Lista de categorías (`Floors`/`pisos`, `Walls`/`muros`, `Ceilings`/`cielos`, `Doors`/`puertas`, `Windows`/`ventanas`, `Furniture`/`mobiliario`, `Casework`, `PlumbingFixtures`, `LightingFixtures`, `GenericModel`, o cualquier `BuiltInCategory`). |
| `filtro_tipo` | no | — | Regex sobre el nombre del tipo para incluir solo ciertos tipos (ej. `"^PAV-\|^REV-"`). |
| `excluir_tipo` | no | — | Regex sobre el nombre del tipo para excluir tipos (ej. `"LOSA\|ESTRUCTURA"`). |
| `param_grupo` | no | `"General"` | Parámetro de la zona para agrupar subtotales (ej. `"Torre"`, `"Edificio"`, `"Bloque"`). |
| `param_unidad` | no | `Number` | Parámetro de la zona que identifica cada unidad (ej. `"Numero"`, `"Depto"`). Si está vacío usa el nombre de la zona. |
| `param_extra` | no | `[]` | Parámetros adicionales de la zona a incluir como metadatos en cada unidad (ej. `["Tipologia", "Uso"]`). |
| `prioridad_uso` | no | — | Objeto `{ "parametro": "...", "valor": "..." }` para priorizar ciertas zonas en bordes compartidos. |
| `tolerancia_borde_m` | no | `1.0` | Distancia máxima en metros para asignar elementos en el borde exterior (como revestimientos de fachada) a la zona más cercana. |
| `muestras_muro` | no | `10` | Puntos de muestreo a lo largo de la curva de cada muro para repartir su área si cruza dos zonas. |
| `incluir_detalle_elementos` | no | `false` | Si es `true`, devuelve el desglose por `ElementId`. |
| `exportar_csv` | no | — | Ruta absoluta de archivo CSV (separado por `;`, UTF-8 con BOM; admite `%USERPROFILE%`, etc.). |

## Ejemplo

```json
{
  "accion": "cubicar",
  "vista": "Nivel 3 - Planta",
  "origen_zonas": "areas",
  "esquema": "Municipal",
  "categorias": ["Floors", "Walls"],
  "filtro_tipo": "^PAV-|^REV-",
  "param_grupo": "Torre",
  "param_unidad": "Numero",
  "param_extra": ["Tipologia"],
  "prioridad_uso": { "parametro": "Uso", "valor": "Vivienda" },
  "exportar_csv": "%USERPROFILE%\\Desktop\\cubicacion-nivel-3.csv"
}
```

## Cómo reparte la geometría

- **Pisos y cielos:** triangula la cara horizontal superior del sólido y asigna cada triángulo a la zona que contiene su centroide, ponderando el área calculada por Revit (`HOST_AREA_COMPUTED`). Si un piso cruza dos zonas, su superficie se reparte proporcionalmente.
- **Muros y revestimientos:** evalúa `muestras_muro` puntos a lo largo de su curva; si un punto cae sobre la línea de borde, prueba pequeños desplazamientos a lo largo de la normal (`Orientation`) y, si sigue fuera (como un revestimiento exterior), busca la zona más cercana dentro de `tolerancia_borde_m`.
- **Elementos puntuales** (puertas, ventanas, mobiliario): usa su punto de ubicación o el centro de su caja envolvente.

## Limitaciones

- La geometría se cruza en 2D (planta) contra las zonas del nivel indicado; elementos de varios pisos en una sola pieza (por ejemplo un muro de altura completa en varios niveles) se evalúan en la vista consultada.
- Las áreas sin contorno cerrado (`Area == 0`) se omiten.
