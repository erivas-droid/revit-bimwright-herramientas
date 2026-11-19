# acotar-ejes

Acota automáticamente los ejes rectos (`Grids`) visibles en una o varias plantas: agrupa los ejes
en familias paralelas, los ordena por posición y crea las cadenas de cotas parciales entre ejes
adyacentes más la cota total exterior, evitando duplicar cotas existentes sobre los mismos ejes.

- **Archivo:** `herramienta.cs` (fuente completa, forma `McpDynamicScript.Run(UIApplication, JObject)`).
- **Seguridad:** `listar` es solo lectura (admite `documento`). `acotar` usa `dry_run: true` por defecto.

## Acciones (`data.accion`)

| Acción | Modifica | Qué hace |
|---|---|---|
| `listar` *(por defecto)* | no | Muestra las familias de ejes paralelos detectadas en la planta (ángulo, cantidad y nombres ordenados). |
| `acotar` | sí (`dry_run`) | Crea las cadenas de cotas parciales y totales para cada familia de ejes paralelos en una o varias plantas. |

## Parámetros de `acotar`

| Parámetro | Obligatorio | Por defecto | Descripción |
|---|---|---|---|
| `vista` | no | activa | Id o nombre de la planta a acotar. |
| `vista_ids` | no | — | Lista de ids de plantas para acotar en lote. |
| `margen_mm` | no | `1200` | Desfase de la cadena parcial más allá del extremo de los ejes (en mm de modelo). |
| `separacion_mm` | no | `800` | Separación entre la cadena parcial y la cota total (en mm de modelo). |
| `parciales` | no | `true` | Crear cadena de cotas entre ejes adyacentes. |
| `total` | no | `true` | Crear cota total entre primer y último eje (cuando hay 3 o más ejes). |
| `ambos_lados` | no | `false` | Si es `true`, acota en ambos extremos de cada familia de ejes. |
| `tipo_cota` | no | por defecto | Nombre de un `DimensionType` específico del modelo. |
| `evitar_duplicadas` | no | `true` | Omite cadenas que ya existen sobre exactamente los mismos ejes en la vista. |
| `dry_run` | no | `true` | Simula, informa y deshace. |

## Ejemplo

```json
{
  "accion": "acotar",
  "dry_run": true,
  "margen_mm": 1200,
  "separacion_mm": 800,
  "ambos_lados": false
}
```
