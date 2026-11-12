# Prueba 2026-11-11 · Fase 3 · herramienta `grupos`

- **PC:** oficina, Revit 2024.3, bimwright 1.2.2.
- **Modelo:** Snowdon Towers Sample Architectural (ejemplo, no se guarda).
- **Grupo:** tipo "Live_Work Core - SE" (id 907744), 2 instancias: 907743 (L4) y 908597 (L3),
  57 miembros (muros, muebles, equipos, puertas, luminarias, piso, cielo, molduras, bocetos).

## Pasos y resultados

1. `compile_only`: compila sin diagnósticos.
2. `desagrupar` 907743 (`dry_run: false`): 57 miembros sueltos. Referencia: miembro índice 1
   (mueble, tipo 771924) a 90°, sin espejo.
3. Edición simulando al usuario:
   - Comentario "EDITADO-EN-GRUPO" en el mueble 906881.
   - Equipo 906882 movido 300 mm en Y.
   - Una luminaria (2101106, alojada en el cielo) se intentó mover 300 mm en X; **no se movió**
     (queda anotado como limitación).
4. `reagrupar` con reemplazo, `dry_run: true`, primera versión: **la verificación rechazó la
   instancia 908597** (error 29,2 mm) y no la tocó.
   - Diagnóstico: dos muros (906892, 906904) ya diferían 29,2 mm entre las dos instancias
     originales, en el extremo inicial y **a lo largo del eje** (perpendicular 0,0 mm). Es el
     ajuste de las uniones con muros de fuera del grupo, no un error.
   - Corrección: en elementos lineales la verificación mide solo la desviación perpendicular
     y el ángulo. Se agregó a la respuesta la lista de los peores miembros.
5. `reagrupar` `dry_run: true`, versión corregida: 1 instancia actualizada, 45 miembros
   verificados, error máximo **0,0 mm**, corrección de 112,4 mm. Todo deshecho.
6. `reagrupar` `dry_run: false`: igual que la simulación. Tipo anterior borrado y tipo nuevo
   (2493275) con el nombre original. Tardó 14,6 s.
7. Verificación independiente: las 2 instancias (908597 y 2493274) tienen 57 miembros, el
   comentario en el mueble y los 6 equipos en las mismas posiciones relativas al origen,
   incluido el movido.

## Observaciones

- `_changes` de bimwright informó 128 elementos borrados, 303 modificados y 65 agregados:
  los miembros viejos de 908597 y su reemplazo, más recintos, recorridos de evacuación,
  etiquetas de puerta y tablas que Revit actualiza al cambiar un tipo de grupo. No se
  revisaron uno por uno.
- Enviar los 15 KB de `herramienta.cs` en cada llamada funciona, pero gasta contexto.
  Opción a evaluar: guardarla como módulo de bimwright (`revit_save_code_module`) y
  llamarla con un cuerpo corto.
