# Prueba 2026-11-11 · Fase 3 · duplicar lámina con contenido

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).

## 1. `revit_duplicate_sheet` de bimwright

| Lámina | Contenido | Resultado |
|---|---|---|
| A101 → A101-MCP | planta L1 + tabla "Planting Schedule" + 6 anotaciones genéricas | Vista duplicada con detalle ("L1 Copy 1", mismo contenido y plantilla), tabla en la misma posición, revisión copiada |
| G101 → G101-MCP | planta + leyenda + timbre "Not for Construction" + 4 botones de ayuda | Vista duplicada, leyenda reutilizada (la misma vista), revisión copiada |

Lo que **no** hace:

- No copia las anotaciones propias de la lámina (0 de 6 y 0 de 5).
- Cambia el tipo de ventana gráfica al tipo por defecto: "Title w Line (Multiple Views)" y
  "No Title" pasan a "Title w Line".
- No copia los parámetros de proyecto de la lámina ("Sheet Sort", "Sheet Group") ni la
  retícula guía.
- Sí copia los parámetros del cajetín (Drawn By, Checked By, fecha).

`_changes` informa 4 elementos borrados en cada duplicación. Son piezas internas de los
bocetos de las regiones de planta de la vista original, que Revit regenera. La vista original
conserva todo su contenido (comparación por categoría: idéntica a la copia).

## 2. Herramienta `completar`

- Simulación en G101-MCP: encontró dos errores, ya corregidos.
  - Contaba la tabla de revisiones del cajetín (una `ViewSchedule` propia de cada lámina)
    como anotación. Eso activaba el aviso "ya tiene anotaciones".
  - Intentaba copiar `VIEW_FIXED_SKETCH_PLANE`, el plano de trabajo interno de la lámina.
- Real en G101-MCP: 5 anotaciones copiadas, 2 ventanas cambiadas a "No Title" con la
  posición del título, "Sheet Sort" y "Sheet Group" copiados.
- Real en A101-MCP: 6 anotaciones, 1 ventana a "Title w Line (Multiple Views)" con el título,
  y "Guide Grid", "Sheet Sort" y "Sheet Group" copiados.
- Comparación visual: `261111-fase3-lamina-G101-origen.png` y
  `261111-fase3-lamina-G101-copia.png` son iguales, salvo el número y el nombre.

## Observaciones

- Recorrer el contenido de todas las láminas de Snowdon tomó 230 s: Revit regenera cada
  lámina al leerla. `revit_list_sheets` tardó más de 3 minutos por lo mismo. Para buscar
  láminas, filtrar antes por número o nombre.
- La herramienta tarda ~20 s por lámina, por la misma razón.
