# Prueba 2026-11-11 · Fase 3 · herramienta `limpiar-cad`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelos:** Snowdon (ejemplo, no se guarda) y un modelo real (**solo lectura**).

## Preparación en Snowdon (no tenía CAD)

- Exporté la planta L2 a DWG en `%TEMP%\…\bimwright-prueba-cad` (31 KB, ~29 s).
- Hice una importación solo en la vista L2, un vínculo del mismo DWG y un vínculo a una copia
  cuyo archivo después se borró ("roto").
- La recarga del roto dio `LinkNotFound`. `Reload()` necesita transacción; la primera vez
  falló por eso.
- **Hallazgo:** después de esa recarga, `GetLinkedFileStatus()` seguía diciendo `Loaded`. La
  herramienta revisa además si el archivo existe en disco.

## Pasos y resultados

1. `listar`: 1 importación y 2 vínculos; el roto, detectado (`archivo_existe: false`). El
   nombre de las instancias salía como "location <Not Shared>"; ahora se usa el del tipo.
2. `limpiar` real (`importados`, `vinculos_rotos`, `patrones_import`):
   - borró la importación con su tipo y el vínculo roto (48 elementos con las subcategorías
     de capas);
   - dejó el vínculo bueno.
   - Este DWG no traía patrones `IMPORT-*`, así que esa parte quedó sin probar con datos.
3. **Modelo real, solo lectura:**
   - 2 importaciones, solo en vistas y ninguna en grupos;
   - 27 instancias de vínculo de 10 tipos, **los 10 `NotFound`**;
   - 93 patrones `IMPORT-*`.
   - Las rutas apuntan a otros usuarios y PC (`C:\Users\<otro>\…`, `C:\0\…`).
4. **¿Están los archivos en este PC?** Búsqueda desde Windows en una carpeta de `%USERPROFILE%` y
   una carpeta de proyectos en `%OneDriveCommercial%`, 15 s: los 10 aparecen, 8 con un candidato y 2
   con dos. Por eso se agregó la acción **`reubicar`**.
5. `reubicar` real en Snowdon: el DWG del vínculo bueno se movió a `movido\otra\`, quedó roto
   y se volvió a encontrar y cargar (`LinkLoaded`). Después se agregó que `carpeta_base`
   acepte una lista; el archivo completo compila (`compile_only`).

