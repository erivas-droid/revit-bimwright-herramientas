# estandares-graficos

Estilos de línea (subcategorías de Líneas) y patrones de línea según un estándar de oficina
escrito en JSON: capturarlo desde un modelo bueno, auditar un modelo contra él y aplicarlo.
bimwright no trae nada para esto.

- **Archivos:**
  - `herramienta.cs`: fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`.
  - `ejemplo-estandar.json`: un estándar de ejemplo (3 estilos, 2 patrones) para copiar y
    adaptar.
- **Cómo se usa:** `revit_send_code_to_revit` con el contenido de `herramienta.cs` en `code` y
  los parámetros en `data`. El estándar se pasa con `"estandar_archivo"` y la ruta completa
  al JSON (Revit lo lee directo y admite variables de entorno, como `%USERPROFILE%`), o
  completo en `"estandar"`.
- **Probado:** bimwright 1.2.2, Revit 2024.3, 2026-11-11:
  - `capturar` y `auditar`: en solo lectura sobre un modelo real.
  - `aplicar`: en Snowdon, todos los pasos, incluida una migración de nombres con 86 estilos.

## Acciones (`data.accion`)

| Acción | Modifica | Parámetros | Qué hace |
|---|---|---|---|
| `capturar` | no | `filtro` (regex, por defecto todos), `documento`? | Lista estilos (grosor, RGB, patrón, cantidad de líneas) y sus patrones con segmentos en mm. Sirve para armar el estándar desde un modelo bueno. |
| `auditar` | no | `estandar` o `estandar_archivo`, `documento`?, plan? | Compara: `cumplen`, `faltan`, `difieren` (con qué cambia), `sobran` (con usos y si el nombre cumple `regla_nombre`), patrones que faltan o difieren. Si recibe un plan (`plan_archivo` o sus claves), agrega `plan_previo`, con qué pasaría con cada estilo, y `plan_sin_cubrir`. |
| `aplicar` | sí | ver abajo | Solo sobre el documento activo. `dry_run` por defecto `true`. |

`documento`: título de otro modelo abierto, para leerlo sin activarlo. Solo para
`capturar` y `auditar`. Ojo: bimwright igual exige que el documento activo esté confirmado
con `revit_switch_target`.

### `aplicar`

El plan puede ir en `data` o en un archivo con `plan_archivo` (un JSON con
estas claves); lo que venga en `data` manda. Se ejecuta en este orden, en una sola
transacción:

1. **`renombrar_patrones`:** `{ "viejo": "nuevo" }` (por ejemplo `MI-SEG-PTO` → `MI-SEG-PTO-SEG`).
2. **Patrones del estándar:** crea los que faltan y corrige los segmentos de los que difieren.
3. **Renombrar estilos:**
   - `renombrar`: `{ "viejo": "nuevo" }`.
   - `renombrar_reglas`: `[{ "buscar": regex, "reemplazo": texto }]`, aplicadas en orden a
     cada estilo que calza con el filtro (`filtro` en `data` o el del estándar; sin filtro
     no se aceptan reglas, para no tocar los estilos de Revit). Lo explícito manda sobre las
     reglas.
   - La API no renombra estilos de línea (probado: "does not support assignment of a
     user-specified name"). Por eso crea el nuevo con las propiedades del viejo, o usa el
     que ya existe; pasa las líneas y borra el viejo.
   - Varios viejos pueden ir al mismo nuevo, por ejemplo `…-ROJA` y `…-ROJO` → `…-RO`.
4. **`reasignar`:** `{ "origen": "destino" }`. Pasa las líneas al destino (que debe existir)
   y borra el origen, salvo `"borrar_reasignados": false`.
5. **Estilos del estándar:** corrige grosor, color y patrón de los que existen, incluidos los
   recién creados al renombrar. Los que faltan se crean solo con `"crear_faltantes": true`.
   Va después de los renombres porque los nuevos heredan los valores del viejo.
6. **`borrar`:** lista de nombres. Solo los que no tienen líneas, salvo `"forzar_borrado": true`.

**Líneas en grupos:** no se tocan. Cambiarlas fuera del modo edición de grupo hace que Revit
deshaga toda la transacción al confirmar, y `dry_run` no lo detecta. Cuentan en
`lineas_con_error` y en `lineas_en_grupos_sin_tocar`, y el estilo viejo no se borra mientras
tenga líneas. Para esos estilos, renombrar a mano en Revit antes de correr la herramienta
(Gestionar > Configuración adicional > Estilos de línea > Renombrar): la interfaz sí
renombra en el mismo lugar.

Los borrados se hacen todos juntos al final de cada paso, con una regeneración. Si se borra
una subcategoría y se sigue leyendo la lista sin regenerar, Revit da "Category is
unexpectedly NULL". Las líneas se indexan una sola vez por estilo, así que sirve en modelos con
decenas de miles de líneas. Probado: 86 estilos en 1 s.

La respuesta detalla cada paso, con el detalle solo de los estilos que movieron líneas o
tuvieron problemas.

## El estándar (JSON)

```json
{ "filtro": "^MI-", "regla_nombre": "<regex>",
  "patrones": [ { "nombre": "MI-SEG", "segmentos": [ { "tipo": "raya", "mm": 3.0 }, { "tipo": "espacio", "mm": 2.0 } ] } ],
  "estilos": [ { "nombre": "MI-LN-01-CON-NE", "grosor": 1, "rgb": [0,0,0], "patron": "SOLIDO" } ] }
```

- `filtro`: regex de los estilos que maneja el estándar (los demás no se tocan).
- `regla_nombre`: regex que debe cumplir el nombre; `auditar` marca los que no la cumplen.
- `tipo` de segmento: `raya`, `espacio` o `punto` (mm 0). `patron` `SOLIDO` = línea continua.
- Si el estándar es grande (muchas combinaciones de grosor, tipo y color), conviene generarlo
  con un script en vez de escribirlo a mano.

**Migrar nombres antiguos:** armar un plan con `renombrar`, `renombrar_reglas`,
`renombrar_patrones`, `reasignar` y `borrar`, revisarlo con `auditar` (`plan_previo`) y
aplicarlo con `dry_run` primero. Los estilos con líneas dentro de grupos se renombran antes a
mano en Revit.

## Limitaciones

- Cuenta y reasigna líneas de modelo y de detalle (`CurveElement`). No revisa estilos usados
  en bordes de regiones rellenas, filtros de vista ni sobrescrituras de visibilidad.
- No toca la tabla de grosores de línea (Gestionar > Grosores de línea): la API no la expone.
- Patrones de relleno y estilos de texto o cota: fuera de alcance por ahora.
