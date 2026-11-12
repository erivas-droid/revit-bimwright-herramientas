# Prueba 2026-11-11 · Fase 3 · herramienta `leyenda-de-colores`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda). Vista
  "MCP PRUEBA L2" (planta L2, escala 1:96, 177 muros de 28 tipos).

## Pasos y resultados

1. `revit_color_elements` de bimwright, Walls por "Family and Type": coloreó 1.128 muros
   (todos los del modelo) en 51 grupos, **con una paleta de 10 colores repetida** (por ejemplo
   RGB 255,0,0 en tres tipos distintos).
2. `leer`: 36 entradas en la vista; los 10 colores aparecen en `colores_con_varios_valores`.
   Una leyenda con esos colores sería ambigua → se agregó la acción `colorear`.
3. `colorear` (real): 36 valores, 36 colores distintos (tono por razón áurea, alternando
   luminosidad). Superficie y corte con relleno sólido.
4. `crear` (real): 37 entradas. Un valor ("Curtain Wall: _Not Defined") tenía dos colores:
   6 muros con el nuevo y 4 con el viejo de bimwright. Esos 4 muros cortina no estaban en la
   vista (`en_vista: false`) pero el colector los había incluido.
   - Correcciones: `crear` usa el color de la mayoría y lo informa en
     `colores_descartados`; se oculta el patrón de fondo de las regiones; `reemplazar`
     borra la leyenda anterior con el mismo nombre.
5. `crear` con `reemplazar` (real): 28 entradas, sin descartes. Comprobación directa: el
   colector da 177 muros de 28 tipos, los 177 con color. **Los conteos anteriores (36, 37)
   estaban inflados** por elementos que el colector de la vista incluía antes de que la vista
   se regenerara.
6. Imagen de la leyenda: `261111-fase3-leyenda.png`. Título, 28 rectángulos sólidos, texto
   `Familia: Tipo (cantidad)`.

## Observaciones

- La vista de leyenda es de dibujo a escala 1:1, con el tipo de texto por defecto
  ("3/32\" Trebuchet MS" en Snowdon) y el primer tipo de región ("Diagonal Crosshatch") con
  sobrescritura sólida.
- Con 28 valores algunos colores se parecen (dos rojos, dos azules).
