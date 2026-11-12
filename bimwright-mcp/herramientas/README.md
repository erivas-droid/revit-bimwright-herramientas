# Herramientas propias

El código fuente de cada herramienta vive aquí. Para usarlas se guardan en bimwright como
módulos (`generar_modulos.py` y `revit_save_code_module`), que quedan en el almacenamiento
local de bimwright de cada PC.

## Convención

Una subcarpeta por herramienta, en minúsculas y con guiones (`grupos/`,
`renumerar/`...). Cada una lleva:

- `herramienta.cs`: código para `revit_send_code_to_revit`, en la forma
  `public static object Run(UIApplication app, JObject data)`.
  - Abre su propia `Transaction` (no está documentado que bimwright la abra).
  - Si modifica el modelo, acepta `data["dry_run"]`: ejecuta, informa y deshace.
  - Devuelve un resumen de lo cambiado (ids, cantidades) y no textos largos.
  - Sin rutas `C:\Users\...` escritas en el código.
- `README.md`: qué hace, parámetros de `data`, ejemplo de llamada, versión de bimwright y
  PC donde se probó, limitaciones conocidas.

Antes de crear una herramienta, revisar en `INVENTARIO.md` que bimwright no la tenga ya.
Al cambiar una, anotarlo en el `CHANGELOG.md` del repositorio.
