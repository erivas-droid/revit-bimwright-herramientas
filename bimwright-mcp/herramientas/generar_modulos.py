"""Genera la version "modulo de bimwright" de cada herramienta propia.

Cada herramientas/<nombre>/herramienta.cs (clase McpDynamicScript) se copia a
modulos/arch_<nombre_con_guiones_bajos>.cs con la clase renombrada a Arch<NombreEnPascal>.
Ese archivo se guarda en bimwright con revit_save_code_module (nombre arch_<...>), una vez por
PC y cada vez que cambie la herramienta. Despues se llama con una linea:

    public class McpDynamicScript { public static object Run(UIApplication app, JObject data)
        { return ArchLimpiarCad.Run(app, data); } }      + modules: ["arch_limpiar_cad"]

Uso:  python -I generar_modulos.py
"""
import hashlib
import json
from pathlib import Path

AQUI = Path(__file__).resolve().parent
SALIDA = AQUI.parent / "modulos"


def pascal(nombre):
    return "".join(p.capitalize() for p in nombre.split("-"))


def main():
    SALIDA.mkdir(exist_ok=True)
    indice = []
    for cs in sorted(AQUI.glob("*/herramienta.cs")):
        nombre = cs.parent.name
        modulo = "arch_" + nombre.replace("-", "_")
        clase = "Arch" + pascal(nombre)
        texto = cs.read_text(encoding="utf-8")
        if texto.count("public class McpDynamicScript") != 1:
            raise SystemExit(f"{cs}: no tiene exactamente una 'public class McpDynamicScript'")
        texto = texto.replace("public class McpDynamicScript", f"public static class {clase}")
        (SALIDA / f"{modulo}.cs").write_text(texto, encoding="utf-8")
        indice.append({
            "herramienta": nombre, "modulo": modulo, "clase": clase,
            "llamada": f"{clase}.Run(app, data)",
            "sha256_herramienta": hashlib.sha256(cs.read_bytes()).hexdigest()[:16],
        })
    (SALIDA / "indice.json").write_text(json.dumps(indice, ensure_ascii=False, indent=1), encoding="utf-8")
    for m in indice:
        print(f"{m['modulo']:40} {m['clase']}")


if __name__ == "__main__":
    main()
