# Prueba: herramientas propias en Revit 2025 (2026-11-13)

Revit 2025.5, bimwright 1.2.2 por pipe, modelo de ejemplo Snowdon Towers Sample Architectural
(sin guardar). Las 10 herramientas como módulos `arch_*`, llamadas desde un solo
`send_code`, todas con `dry_run: true`.

## Resultado

Las 13 llamadas terminaron sin errores ni avisos de Revit. La huella del modelo de bimwright
(`activeModelBefore` = `activeModelAfter`) quedó igual: todo se deshizo.

| Herramienta | Acción y datos | Resultado | ms |
|---|---|---|---|
| grupos | `desagrupar` el único grupo "Live_Work Core - Standard (Narrow)" | 87 miembros sueltos, referencia calculada | 693 |
| grupos | `duplicar_tipo` del mismo tipo | tipo nuevo creado y deshecho | 1134 |
| estandares-graficos | `aplicar` con `ejemplo-estandar.json` y `crear_faltantes` | 2 patrones y 3 estilos creados | 1885 |
| leyenda-de-colores | `colorear` "Rooms - L1 - Block 37" por Name | 8 valores, 8 colores distintos | 10327 |
| leyenda-de-colores | `crear` con 2 entradas explícitas | vista de dibujo con regiones y textos | 668 |
| rango-de-vista-en-lote | `aplicar` corte 1500 a "L3 Wall Base" | +1219 → +1500 | 93 |
| duplicar-lamina-con-contenido | SD102 → SD103, `forzar` | 1 anotación copiada; la ventana no tiene par (vistas distintas, esperado) | 9515 |
| vistas-desde-recintos | `crear` Café 101, llamada, recorte contorno, alzados | planta ampliada + 4 alzados O/N/E/S | 12360 |
| transferir-parametros | `copiar` Name → Comments en Rooms | 54 de 54 | 940 |
| modificar-parametros-masivo | `fijar` Comments con tildes y ñ | 2 cambiados, texto intacto | 33 |
| modificar-parametros-masivo | `transformar` Name modo `codigo` | "Café Kitchen" → `CAFE-KITCHEN` | 21 |
| renumerar | Rooms de L3, `filas`, formato `3{n:00}` | 8 recintos, sin choques | 937 |
| limpiar-cad | `limpiar` importados y patrones | nada que limpiar (Snowdon no tiene CAD) | 97 |

## Observaciones

- `codigo` borra la barra en vez de convertirla en guion: "Live/Work Unit" → `LIVEWORK-UNIT`.
  Cumple la regla escrita; `LIVE-WORK-UNIT` se leería mejor. Pendiente decidir.
- No se probó `reagrupar` (necesita un `desagrupar` confirmado) ni `reubicar` de limpiar-cad
  (no hay vínculos rotos en Snowdon).
- `dry_run` no detecta lo que Revit rechaza al confirmar; esta prueba no confirma ningún cambio.
