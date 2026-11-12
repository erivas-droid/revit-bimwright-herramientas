# Prueba 2026-11-11 · Fase 3 · herramienta `vistas-desde-recintos`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).

## Pasos y resultados

1. `listar`, `nivel: "L2"`: 7 recintos, todos ubicados. Planta padre propuesta: "L2".
2. `crear` simulado en 202 y 203, modo `llamada`, `recorte: "contorno"`, alzados con el marcador
   al centro. Funcionó, pero la segunda llamada tomó como padre la primera
   ("202 Live/Work Unit - AMPLIADA" va antes que "L2" en orden alfabético).
   - Corrección: la planta padre de cada nivel se elige antes de crear nada, y se excluyen
     las llamadas (parámetro "Parent View").
3. `crear` real en 202 y 203, modo `llamada`: 2 llamadas desde "L2" recortadas al contorno, y
   8 alzados interiores.
   - Imagen de la llamada 202: `261111-fase3-recinto-202-llamada.png`.
   - El marcador quedó en el centro del rectángulo, frente al núcleo de baños. En un recinto
     en L podría caer fuera. Se agregó `marcador` (`punto` por defecto, `centro` opcional).
4. `crear` real en 202, 203 y 205, modo `planta`, plantilla "Architectural Plan", nombres
   "- PLANTA" e "- INT {dir}": 3 plantas y 12 alzados.
   - La plantilla respetó el recorte.
   - El marcador quedó en el punto del recinto, en el espacio libre del departamento.
   - Imagen del alzado S de 205: `261111-fase3-recinto-205-alzado-S.png`. Recorte de eje 8 a
     eje 6 y del piso al cielo, con el núcleo a la vista.
5. Medidas de los alzados: alto 3.724 mm (altura del recinto + 2 × 300 de margen). Los
   anchos coinciden entre alzados opuestos (por ejemplo 9.696 mm en E y O de 202). El recorte
   lejano queda a la distancia del marcador a la pared más 300 mm.

## Observaciones

- ~5 s para 2 recintos con alzados en modo llamada, ~25 s para 3 en modo planta con
  plantilla.
- `_changes` muestra por cada vista elementos internos ("Sun Path", "Work Plane Grid",
  bocetos), que Revit crea con cada vista.
