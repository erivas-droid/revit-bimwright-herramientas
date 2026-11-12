# Prueba 2026-11-11 · Fase 3 · herramienta `transferir-parametros`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).

## Pasos y resultados

1. `copiar` simulado, puertas visibles en la planta L2, "Type Mark" (tipo) → "Comments"
   (ejemplar): 28 de 28 copiados.
2. `igualar` simulado: puerta modelo 660428 (180 MIN), destinos 785960 y 786598 (Cores),
   `cambiar_tipo` + `todos`. Salieron **dos problemas**:
   - bimwright informó el fallo de Revit "Changes to groups are allowed only in group edit
     mode": esas puertas están en grupos. El fallo aparece **al confirmar**, deshace toda la
     transacción, y una simulación no lo ve porque deshace antes de confirmar. Por la misma
     razón, el 28 de 28 del paso 1 estaba inflado: 7 de esas puertas están en grupos.
     Corrección: se omiten los elementos en grupos y se informan en `omitidos_en_grupo`.
   - `todos` copiaba el **IfcGUID**, que es único por elemento. Corrección: se excluye todo
     parámetro con "GUID" en el nombre.
   - Además, "Masonry Frame: 'No' -> 'No'" figuraba como cambio. Corrección: no se cuenta lo
     que queda igual.
3. `copiar` real, mismo caso: 21 copiados y 7 omitidos en grupo, 0 errores. Transacción
   confirmada. `_changes`: 21 puertas y 5 muros cortina (los anfitriones de las puertas de
   escaparate).
4. `igualar` real: modelo 660428, destinos 786784 y 786853 (60 MIN) y 785960 (en grupo).
   - 2 igualadas: tipo 60 MIN → 180 MIN, Comments 78 → 77, Hardware A1 → A3.
   - 785960 omitida por estar en grupo.
   - Excluidos por defecto: IfcGUID, fases, familia y tipo, Sill Height (Restricciones),
     Level y Mark.
   - `_changes`: 219 modificados (recintos, muros, rutas de evacuación, tablas). Revit los
     recalcula al cambiar el tipo de una puerta; no son cambios de parámetros.

## Observaciones

- Lección general para las herramientas que escriben: **la simulación (`dry_run`) no
  detecta los fallos que Revit da al confirmar** (grupos, avisos de geometría). Los cambios
  sobre elementos en grupos hay que filtrarlos antes.
