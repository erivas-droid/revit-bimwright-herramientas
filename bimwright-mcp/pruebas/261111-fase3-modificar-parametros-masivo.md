# Prueba 2026-11-11 · Fase 3 · herramienta `modificar-parametros-masivo`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).
- **Elementos:** puertas visibles en la planta L2 (28; 7 en grupos). Su "Comments" tenía la
  Type Mark de la prueba de `transferir-parametros`.

## Pasos y resultados

1. `prefijo` "P-" en Comments (real): 21 cambiadas ("77" → "P-77") y 7 omitidas por estar en
   grupos. Transacción confirmada.
2. `fijar` en una puerta (real): "Puerta Ñandú acústica  2º piso".
3. `transformar` modo `codigo` en las 21 (real): solo cambió esa puerta, a
   "PUERTA-NANDU-ACUSTICA-2-PISO". Las otras 20 ("P-77") ya cumplían y quedaron sin cambio.
4. `fijar` Mark = "X" en las puertas: rechazado antes de escribir ("'Mark' quedaría repetido
   en: X (x21)"). Esta comprobación se hizo con una copia recortada de la herramienta, con
   la misma lógica de protección.
5. `sufijo` "-A" en la Type Mark con `en_tipo` (simulación y real): 14 tipos distintos,
   "77" → "77-A". Confirmada sin error aunque el tipo "Cores" tiene ejemplares en grupos:
   los cambios de tipo no chocan con los grupos. `_changes` incluye 2 componentes de
   leyenda y 4 cotas que muestran esos tipos.
