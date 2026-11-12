# Cambios

Formato basado en [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/). Versiones
según [Semantic Versioning](https://semver.org/lang/es/).

## [Sin publicar]

## [0.1.0] - 2026-11-12

Primera versión. Herramientas probadas en Revit 2024.3 con bimwright v1.2.2, sobre el modelo
de ejemplo Snowdon Towers.

### Agregado

- Herramientas propias para `send_code` de bimwright: `grupos`, `estandares-graficos`,
  `leyenda-de-colores`, `rango-de-vista-en-lote`, `duplicar-lamina-con-contenido`,
  `vistas-desde-recintos`, `transferir-parametros`, `modificar-parametros-masivo`,
  `renumerar` y `limpiar-cad`.
- Estándar de estilos de línea de ejemplo para `estandares-graficos`.
- `generar_modulos.py`, que convierte cada herramienta en un módulo de bimwright.
- Skill de Claude Code `revit-bimwright`.
- Inventario de las herramientas de bimwright por toolset y registro de pruebas.

### Pendiente

- Probar el toolset `workflows` de bimwright, ya incluido en la configuración recomendada.
- Probar las herramientas en Revit 2025.

[Sin publicar]: https://github.com/erivas-droid/revit-bimwright-herramientas/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/erivas-droid/revit-bimwright-herramientas/releases/tag/v0.1.0
