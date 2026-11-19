# laminas-desde-tabla

Exporta el listado maestro de láminas y sus parámetros (de lámina y de cajetín) a JSON o CSV,
y permite sincronizar cambios de vuelta: crea láminas faltantes (reales o *placeholder*) y
actualiza nombres y parámetros de láminas existentes en una sola pasada.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `exportar` es solo lectura (admite `documento`). `sincronizar` usa `dry_run: true`
  por defecto y aísla cada lámina con `SubTransaction`.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `exportar` *(por defecto)* | no | Lista láminas con número, nombre, tipo de cajetín y los parámetros indicados en `parametros` y `parametros_cajetin`. Opcionalmente guarda un CSV (`exportar_csv`). |
| `sincronizar` | sí (`dry_run`) | Desde `laminas` (JSON) o `archivo_csv`, crea las láminas faltantes y actualiza nombre y parámetros de lámina/cajetín en las existentes. |

## Convención de columnas en CSV

- Columnas reservadas: `Numero`, `Nombre`, `Placeholder` (`SI`/`NO`), `Cajetin`.
- Cualquier otra columna se interpreta como parámetro de la lámina (`ViewSheet`).
- Las columnas con prefijo `TB:` (por ejemplo `TB:Dibujo`, `TB:Reviso`) se escriben en el cajetín (`TitleBlock`).

## Ejemplo

```json
{
  "accion": "sincronizar",
  "dry_run": true,
  "cajetin_por_defecto": "A1 Metrico",
  "laminas": [
    {
      "numero": "A-101",
      "nombre": "PLANTA NIVEL 1",
      "parametros": { "Drawn By": "ER", "Checked By": "SO" }
    }
  ]
}
```
