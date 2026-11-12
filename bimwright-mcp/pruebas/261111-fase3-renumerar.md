# Prueba 2026-11-11 · Fase 3 · herramienta `renumerar`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).

## Pasos y resultados

1. Recintos de L2 (`categoria: "Rooms"`, `donde: {"Level": "L2"}`), orden `filas`, formato
   `2{n:00}`, simulado: 7 recintos y sin choques. Orden:
   - Fila de arriba (Y ≈ 10,2 m): Studio Unit, Two Story Studio Unit y Live/Work Unit.
   - Después Mezzanine Dining, Corridor, Stair y el Live/Work Unit de abajo.
   - Corrección: en los recintos, `nombre` mostraba el nombre con el número nuevo; ahora
     muestra solo el nombre del recinto.
2. Lo mismo, real: transacción confirmada.
   - Números antes: 203, 204, 205, 200, 201, S3 y 202. Después: 201 a 207.
   - Los números se intercambiaron dentro del mismo grupo de recintos sin aviso de Revit,
     porque solo los revisa al confirmar.
   - `_changes`: 192 modificados (rutas de evacuación, luminarias, equipos, tablas), que
     Revit recalcula al cambiar los números.
3. Puertas visibles en la planta L2, Mark con formato `{n}`, simulado: 21 puertas (7 en
   grupos omitidas), 1 a 21 por filas, sin choques. Las marcas existentes son del tipo "109A".
4. Mismas puertas, formato `3{n:00}A`: **no aplicó nada**. Encontró 8 choques con puertas de
   L3 que ya usan 301A, 302A… (por ejemplo, 301A en la 1919948), y los listó con los ids.

## Observaciones

- Revisar los choques recorre toda la categoría del modelo: ~3,5 s en Snowdon.
