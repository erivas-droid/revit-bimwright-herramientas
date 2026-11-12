# Prueba 2026-11-11 · Fase 3 · herramienta `rango-de-vista-en-lote`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).

## Pasos y resultados

1. `leer` con `nombre: "^(L\d|Level|MCP)"`: 43 plantas en 6 combinaciones de rango.
   - 32 con corte a 1219 mm (4'), 5 plantas de área "Gross" con corte a 1829 mm.
   - Plantas de cielo con superior y profundidad en el nivel de arriba (o en L3, R2 -51).
   - Detecta qué vistas tienen el rango controlado por su plantilla (Architectural Plan,
     Life Safety, Area Plan).
2. `aplicar` simulado, `nivel: "L2"`, corte +1100 y profundidad en el nivel inferior:
   - 10 vistas: 4 cambiadas, 4 omitidas por plantilla, 2 con error.
   - Los 2 errores son plantas de cielo: el mismo rango no es válido allí (orden inverso).
     Cada error quedó aislado en su subtransacción. Se agregó la `pista` al error.
3. `aplicar` real, `nombre: "^L\d_SD$"`, mismo rango: 5 cambiadas, 0 errores. `_changes`:
   9 vistas y 4 notas de texto modificadas (las notas son de las láminas donde están).
4. `aplicar` real a la plantilla "Architectural Plan" con `desde_vista` = L2_SD:
   - La plantilla quedó con corte +1100 y profundidad en el nivel inferior.
   - `_changes` muestra modificadas sus plantas L1 a L5 (propagación de la plantilla).
5. Verificación con lectura directa de la API:
   - L2 (con plantilla): corte L2 +1100, profundidad **M1** +0 (nivel concreto).
   - L3 (con plantilla): profundidad **L2** +0.
   - L2_SD y L1_SD (sin plantilla): profundidad con el id especial -4 = "nivel de abajo".
   - O sea, en las vistas Revit guarda "nivel de abajo" como relativo, y la plantilla lo
     resuelve a un nivel concreto en cada vista que controla.

## Observaciones

- `GetLevelId` devuelve el nivel concreto para "nivel asociado" y "nivel de arriba" en las
  vistas leídas, y el id especial solo para "nivel de abajo" fijado por la API.
- Unos 14 KB de código por llamada; compila en ~1 s.
