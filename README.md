# revit-bimwright-herramientas

Herramientas para que Claude trabaje en Revit con
**[bimwright/rvt-mcp](https://github.com/bimwright/rvt-mcp)**, un MCP de Revit en C#.

bimwright trae unas 240 herramientas (consultas, vistas, láminas, etiquetas, parámetros y
también `send_code`, que ejecuta C# dentro de Revit). Este repositorio suma lo que le falta
para el trabajo diario de una oficina de arquitectura: 10 herramientas propias en C# y una
skill de Claude Code con las reglas de uso.

bimwright no se incluye: se instala desde su repositorio oficial (licencia Apache-2.0).

> **Estado:** v0.1.0, primera versión para probar. Úsalo en modelos de ejemplo antes de
> tocar un proyecto real (ver [Reglas de seguridad](#reglas-de-seguridad)).

## Requisitos

- Windows 10 u 11.
- **Revit 2024**. Las herramientas se probaron en Revit 2024.3. bimwright admite Revit 2022
  a 2027, pero las herramientas propias no se han probado en otras versiones.
- **Claude Code**, en la app de escritorio de Claude o en la terminal.
- **bimwright v1.2.2** (se instala en el paso 2).
- [git](https://git-scm.com/download/win) y [Python 3](https://www.python.org/downloads/).
  Python solo se usa para generar los módulos (paso 5).

## Instalación

Resumen: clonar este repositorio, instalar bimwright, elegir sus toolsets, probar la conexión
y guardar las herramientas como módulos. Unos 15 minutos.

### 1. Clonar el repositorio

En PowerShell, en una carpeta fuera de OneDrive (git y OneDrive no se llevan bien):

```powershell
mkdir $env:USERPROFILE\source -Force
cd $env:USERPROFILE\source
git clone https://github.com/erivas-droid/revit-bimwright-herramientas.git
```

Para actualizar después a la última versión: `git pull` dentro de esa carpeta, y repetir el
paso 5 si cambió alguna herramienta.

### 2. Instalar bimwright v1.2.2

Hacerlo con Revit cerrado y desde una PowerShell propia, no pidiéndoselo a Claude. La app de
escritorio de Claude es un paquete MSIX: lo que instala Claude puede quedar en una copia
aislada de AppData que Revit no ve.

1. Descargar `RvtMcp.Setup-v1.2.2-win-x64.zip` y `RvtMcp.Setup-v1.2.2-win-x64.zip.sha256`
   desde [la versión 1.2.2 de bimwright](https://github.com/bimwright/rvt-mcp/releases/tag/v1.2.2).
   Comprobar que el archivo está completo: el resultado de
   `Get-FileHash .\RvtMcp.Setup-v1.2.2-win-x64.zip` debe coincidir con el `.sha256`.
2. Descomprimirlo y, en esa carpeta:

   ```powershell
   .\install.ps1 -WhatIf -Client claude   # muestra lo que hará
   .\install.ps1 -Client claude
   ```

   Si PowerShell no deja ejecutar scripts, usar
   `powershell -ExecutionPolicy Bypass -File .\install.ps1 -Client claude`.
3. El instalador deja:
   - el servidor en `%LOCALAPPDATA%\Bimwright\rvt-mcp\server\current\rvt-mcp.exe`;
   - la configuración en `%LOCALAPPDATA%\Bimwright\rvt-mcp\rvtmcp.config.json`;
   - el add-in en `%APPDATA%\Autodesk\Revit\Addins\<año>\` para cada Revit instalado;
   - el MCP `rvt-mcp` registrado en Claude Code.

bimwright no está firmado. En el PC donde se probó, Smart App Control estaba activo y no lo
bloqueó. Si Windows lo bloquea, avisar antes de cambiar cualquier ajuste de seguridad.

### 3. Elegir los toolsets

Con todos los toolsets, Claude ve unas 240 herramientas y gasta contexto. Se recomiendan 14
de los 23. Para dejarlos así, correr en PowerShell (conserva el resto de la configuración y
escribe el archivo sin BOM):

```powershell
$f = "$env:LOCALAPPDATA\Bimwright\rvt-mcp\rvtmcp.config.json"
$c = Get-Content $f -Raw | ConvertFrom-Json
$t = 'query','create','view','meta','annotation','sheets','schedule','parameters',
     'modify','families','graphics','rooms','lint','workflows'
$c | Add-Member -NotePropertyName toolsets -NotePropertyValue $t -Force
$c | Add-Member -NotePropertyName enableSendCode -NotePropertyValue $true -Force
[IO.File]::WriteAllText($f, ($c | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding $false))
```

`enableSendCode` es obligatorio: las herramientas propias corren con `send_code`.

### 4. Probar la conexión

1. Reiniciar Claude Code para que cargue `rvt-mcp`.
2. Abrir Revit con un modelo de ejemplo, por ejemplo
   `C:\Program Files\Autodesk\Revit 2024\Samples\Snowdon Towers Sample Architectural.rvt`.
3. Abrir Claude Code **en la carpeta del repositorio**. Así se carga sola la skill
   `revit-bimwright` (`.claude/skills/`), que explica a Claude cómo usar todo esto.
4. Pedirle: "revisa la conexión con Revit". Debería llamar a `revit_health` y responder con
   el servidor y el add-in en 1.2.2.

### 5. Guardar las herramientas como módulos

Las herramientas se guardan en bimwright como módulos de código, para llamarlas con una línea
en vez de enviar 10 a 25 KB de C# cada vez. Los módulos quedan en el almacenamiento local de
bimwright, así que se hace una vez por PC y otra vez cada vez que cambie una herramienta.

```powershell
python -I bimwright-mcp\herramientas\generar_modulos.py
```

Eso escribe `bimwright-mcp\modulos\` (no va a git). Después pedirle a Claude: "guarda en
bimwright los módulos de `bimwright-mcp/modulos`". Usará `revit_save_code_module`, uno por
herramienta, y `revit_list_code_modules` para comprobarlos.

## Herramientas propias

Cada una tiene su `herramienta.cs` y un `README.md` con parámetros y ejemplos. Todas las que
modifican el modelo aceptan `dry_run`, que por defecto es `true`: simulan, informan y deshacen.

| Herramienta | Para qué |
|---|---|
| [grupos](bimwright-mcp/herramientas/grupos/README.md) | Desagrupar, editar un tipo de grupo en todas sus instancias, cambiar, duplicar o renombrar tipo |
| [estandares-graficos](bimwright-mcp/herramientas/estandares-graficos/README.md) | Estilos y patrones de línea según un estándar en JSON: capturar, auditar, migrar nombres y aplicar. Incluye un estándar de ejemplo |
| [leyenda-de-colores](bimwright-mcp/herramientas/leyenda-de-colores/README.md) | Colorear una vista por parámetro, con un color por valor, y crear la leyenda |
| [rango-de-vista-en-lote](bimwright-mcp/herramientas/rango-de-vista-en-lote/README.md) | Leer y cambiar el rango de vista de muchas plantas o plantillas a la vez |
| [duplicar-lamina-con-contenido](bimwright-mcp/herramientas/duplicar-lamina-con-contenido/README.md) | Completar una lámina duplicada: anotaciones, tipos de ventana gráfica y parámetros |
| [vistas-desde-recintos](bimwright-mcp/herramientas/vistas-desde-recintos/README.md) | Planta ampliada por recinto, recortada a su contorno, y sus 4 alzados interiores |
| [transferir-parametros](bimwright-mcp/herramientas/transferir-parametros/README.md) | Copiar un parámetro a otro e igualar parámetros desde un elemento modelo |
| [modificar-parametros-masivo](bimwright-mcp/herramientas/modificar-parametros-masivo/README.md) | Fijar, reemplazar, prefijo, sufijo, transformar o vaciar un parámetro en muchos elementos |
| [renumerar](bimwright-mcp/herramientas/renumerar/README.md) | Marca o número de recinto en secuencia por posición, parámetro o lista |
| [limpiar-cad](bimwright-mcp/herramientas/limpiar-cad/README.md) | Listar, reubicar y limpiar importaciones y vínculos CAD |

Las pruebas de cada una están en [bimwright-mcp/pruebas](bimwright-mcp/pruebas). La lista
completa de herramientas de bimwright, con su toolset, está en
[INVENTARIO.md](bimwright-mcp/herramientas/INVENTARIO.md).

## Reglas de seguridad

- **Probar en modelos de ejemplo** (los de `Revit <año>\Samples`) y no guardarlos.
- **En modelos reales, solo lectura**, salvo que el responsable del modelo lo pida y el
  modelo esté sincronizado. Un `dry_run` también escribe y deshace, así que cuenta como
  escritura.
- Simular siempre con `dry_run: true`, revisar el resultado y recién entonces aplicar.
- `dry_run` no detecta lo que Revit rechaza al confirmar. Por ejemplo, cambiar elementos que
  están dentro de grupos deshace toda la transacción. Las herramientas omiten esos elementos.

## Reportar errores y proponer cambios

- **Errores y mejoras:** abrir un [issue](https://github.com/erivas-droid/revit-bimwright-herramientas/issues).
  Para un error, indicar la herramienta, la versión de Revit, los parámetros usados (`data`)
  y la respuesta completa.
- **Cambios de código:** hacer un fork, trabajar en una rama del fork y abrir un pull request
  hacia `main`. El mantenedor revisa, aprueba y hace el merge.

## Versiones

Los cambios se anotan en [CHANGELOG.md](CHANGELOG.md), con versiones semánticas
(`vMAYOR.MENOR.PARCHE`). Cada versión lleva una etiqueta de git y una
[release](https://github.com/erivas-droid/revit-bimwright-herramientas/releases).
