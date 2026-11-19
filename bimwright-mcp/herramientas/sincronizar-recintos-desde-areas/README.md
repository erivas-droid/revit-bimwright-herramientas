# sincronizar-recintos-desde-areas

Cruza geométricamente los recintos (`Rooms`) ubicados en uno o varios niveles contra los polígonos
de áreas (`Areas`) de un `AreaScheme`, compara los parámetros indicados en un mapa configurable y
traspasa la información desde cada área hacia los recintos contenidos en ella (por ejemplo: edificio
o torre, número de departamento, tipología, uso o zona).

En bimwright no existe una herramienta que cruce espacialmente `Rooms` con `Area Plans` ni que
sincronice parámetros entre ambos.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `auditar` es solo lectura (y admite `documento` para consultar otro modelo abierto).
  `aplicar` usa `dry_run: true` por defecto, trabaja solo sobre el documento activo y omite recintos
  dentro de grupos.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `auditar` *(por defecto)* | no | Cruza cada `Room` contra las `Areas` del `esquema` en el mismo nivel, informa cuántos coinciden, cuántos tienen diferencias, cuáles quedaron sin área y muestra el detalle antes/después. |
| `aplicar` | sí (`dry_run`) | Escribe en cada `Room` los valores correspondientes desde su `Area` contenedora según `mapa`, usando una `SubTransaction` por recinto para aislar errores. |

## Parámetros

| Parámetro | Obligatorio | Por defecto | Descripción |
|---|---|---|---|
| `esquema` | sí | — | Nombre del `AreaScheme` (por ejemplo `"Rentable"`, `"Municipal"`). |
| `mapa` | sí | — | Objeto `{ "parametro_en_area": "parametro_en_room", ... }`. |
| `nivel` | no | todos | Nombre de un nivel específico a evaluar. |
| `niveles` | no | todos | Lista de nombres de niveles a evaluar. |
| `recinto_ids` | no | todos | Lista de `ElementId` de recintos específicos. |
| `solo_vacios` | no | `false` | Si es `true`, solo escribe parámetros actualmente vacíos en el `Room`. |
| `sobrescribir_con_vacio` | no | `false` | Si es `false`, un valor vacío en el `Area` no borra el valor existente en el `Room`. |
| `incluir_en_grupo` | no | `false` | Si es `true`, permite escribir en recintos que pertenecen a un grupo **solo** cuando el parámetro destino admite variar por instancia de grupo (`VariesAcrossGroups` o integrados como `Number`, `Name`, `Comments`, `Department`). |
| `prioridad_uso` | no | — | Objeto `{ "parametro": "...", "valor": "..." }` para priorizar ciertas áreas en caso de superposición. |
| `tolerancia_borde_m` | no | `0.5` | Distancia máxima en metros para asociar un recinto cuyo punto caiga levemente fuera del borde del área. |
| `documento` | no | activo | Solo en `auditar`: título de otro modelo abierto para consultar sin cambiar de pestaña. |
| `dry_run` | no | `true` | Solo en `aplicar`: simula, informa y deshace. |
| `max_detalle` | no | `50` | Máximo de recintos en la lista de detalle. |

## Ejemplo

```json
{
  "accion": "auditar",
  "esquema": "Municipal",
  "nivel": "Nivel 3",
  "mapa": {
    "Edificio": "Torre",
    "Numero": "Departamento",
    "Tipologia": "Tipologia",
    "Uso": "Uso"
  }
}
```

Para simular la escritura (sin confirmar) y luego aplicar:

```json
{
  "accion": "aplicar",
  "dry_run": true,
  "esquema": "Municipal",
  "nivel": "Nivel 3",
  "solo_vacios": false,
  "mapa": {
    "Edificio": "Torre",
    "Numero": "Departamento",
    "Tipologia": "Tipologia"
  }
}
```

## Detalles y limitaciones

- Solo evalúa `Rooms` ubicados y con área > 0 (`Location != null && Area > 0`) y `Areas` cerradas (`Area > 0`).
- Al aplicar, omite recintos que pertenezcan a un grupo (`GroupId != InvalidElementId`), ya que Revit deshace la transacción al confirmar cambios en miembros de grupos fuera del modo edición.
- Si el tipo de dato difiere entre el parámetro del `Area` y el del `Room`, convierte mediante texto (`SetValueString`).
