// herramienta: renombrar-vistas-y-laminas (bimwright-mcp)
// Audita y renombra vistas y/o laminas por patron: reemplazar (regex), prefijo, sufijo,
// transformar (mayusculas, minusculas, sin_tildes, codigo) o plantilla con tokens
// ({nivel}, {tipo}, {escala}, {nombre}, {numero}). Revisa choques antes de escribir.
// Acciones: auditar (solo lectura, admite `documento`) y renombrar (dry_run por defecto true).
// Ver README.md de esta carpeta.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public class McpDynamicScript
{
    public static object Run(UIApplication app, JObject data)
    {
        if (data == null) data = new JObject();
        string accion = ((string)data["accion"] ?? "auditar").ToLowerInvariant();
        var activo = app.ActiveUIDocument == null ? null : app.ActiveUIDocument.Document;
        var doc = ResolverDocumento(app, (string)data["documento"]);
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];

        switch (accion)
        {
            case "auditar":
                return Ejecutar(doc, data, false, true);
            case "renombrar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("renombrar solo trabaja sobre el documento activo.");
                return Ejecutar(doc, data, true, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use auditar o renombrar.");
        }
    }

    static Document ResolverDocumento(UIApplication app, string titulo)
    {
        if (string.IsNullOrEmpty(titulo))
        {
            if (app.ActiveUIDocument == null) throw new InvalidOperationException("No hay un documento activo.");
            return app.ActiveUIDocument.Document;
        }
        var docs = app.Application.Documents.Cast<Document>().Where(d => !d.IsLinked).ToList();
        var exacto = docs.FirstOrDefault(d => string.Equals(d.Title, titulo, StringComparison.OrdinalIgnoreCase));
        if (exacto != null) return exacto;
        var prefijo = docs.FirstOrDefault(d => d.Title.StartsWith(titulo, StringComparison.OrdinalIgnoreCase));
        if (prefijo != null) return prefijo;
        throw new ArgumentException("No hay un modelo abierto con titulo '" + titulo + "'.");
    }

    static object Ejecutar(Document doc, JObject data, bool aplicar, bool dryRun)
    {
        string objetivo = ((string)data["objetivo"] ?? "vistas").ToLowerInvariant();
        if (objetivo != "vistas" && objetivo != "laminas")
            throw new ArgumentException("objetivo debe ser 'vistas' o 'laminas'.");

        string campo = ((string)data["campo"] ?? "nombre").ToLowerInvariant();
        if (objetivo == "vistas" && campo != "nombre")
            throw new ArgumentException("En vistas el campo debe ser 'nombre'.");
        if (objetivo == "laminas" && campo != "nombre" && campo != "numero")
            throw new ArgumentException("En laminas el campo debe ser 'nombre' o 'numero'.");

        bool incluirPlantillas = data["incluir_plantillas"] != null && (bool)data["incluir_plantillas"];
        Regex rxFiltro = data["filtro_nombre"] == null ? null : new Regex((string)data["filtro_nombre"], RegexOptions.IgnoreCase);
        string tipoFiltro = (string)data["tipo_vista"];
        string nivelFiltro = (string)data["nivel"];
        var idsFiltro = (data["ids"] as JArray)?.Select(x => (int)x).ToHashSet();
        int maxDetalle = data["max_detalle"] == null ? 50 : (int)data["max_detalle"];

        // 1. Seleccionar vistas o laminas
        var todas = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => incluirPlantillas || !v.IsTemplate)
            .Where(v => objetivo == "laminas" ? (v is ViewSheet) : !(v is ViewSheet))
            .Where(v => v.ViewType != ViewType.Internal && v.ViewType != ViewType.ProjectBrowser && v.ViewType != ViewType.SystemBrowser)
            .ToList();

        var seleccion = todas.Where(v =>
        {
            if (idsFiltro != null && !idsFiltro.Contains(v.Id.IntegerValue)) return false;
            string actual = ValorActual(v, campo);
            if (rxFiltro != null && !rxFiltro.IsMatch(actual)) return false;
            if (!string.IsNullOrEmpty(tipoFiltro) && !string.Equals(v.ViewType.ToString(), tipoFiltro, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(nivelFiltro))
            {
                string niv = NivelDe(v);
                if (!string.Equals(niv, nivelFiltro, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }).OrderBy(v => ValorActual(v, campo)).ToList();

        // 2. Solo auditoria contra regla_nombre si no se indica operacion
        string op = (string)data["operacion"];
        if (!aplicar && string.IsNullOrEmpty(op))
        {
            Regex rxRegla = data["regla_nombre"] == null ? null : new Regex((string)data["regla_nombre"]);
            var cumplen = new List<object>();
            var noCumplen = new List<object>();
            foreach (var v in seleccion)
            {
                string actual = ValorActual(v, campo);
                bool ok = rxRegla == null || rxRegla.IsMatch(actual);
                var item = new { id = v.Id.IntegerValue, tipo = v.ViewType.ToString(), nivel = NivelDe(v), valor = actual };
                if (ok) cumplen.Add(item); else noCumplen.Add(item);
            }
            return new
            {
                accion = "auditar",
                documento = doc.Title,
                objetivo,
                campo,
                evaluadas = seleccion.Count,
                cumplen = cumplen.Count,
                no_cumplen = noCumplen.Count,
                muestra_no_cumplen = noCumplen.Take(maxDetalle).ToList()
            };
        }

        // 3. Calcular nuevos nombres
        var fn = Operacion(op, data);
        var planes = new List<Tuple<View, string, string>>();
        foreach (var v in seleccion)
        {
            string antes = ValorActual(v, campo);
            string despues = fn(v, antes);
            if (string.IsNullOrWhiteSpace(despues)) continue;
            if (despues != antes) planes.Add(Tuple.Create(v, antes, despues));
        }

        // 4. Detectar choques (duplicados dentro del plan o contra existentes del mismo tipo/campo)
        var existentes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var idsEnPlan = new HashSet<int>(planes.Select(p => p.Item1.Id.IntegerValue));
        foreach (var v in todas)
        {
            if (idsEnPlan.Contains(v.Id.IntegerValue)) continue;
            string clave = ClaveUnicidad(v, campo, ValorActual(v, campo));
            if (!existentes.ContainsKey(clave)) existentes[clave] = v.Id.IntegerValue;
        }
        var choques = new List<object>();
        var vistosPlan = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pl in planes)
        {
            string clave = ClaveUnicidad(pl.Item1, campo, pl.Item3);
            int otroId;
            if (existentes.TryGetValue(clave, out otroId))
                choques.Add(new { id = pl.Item1.Id.IntegerValue, antes = pl.Item2, despues = pl.Item3, choque_con = otroId, motivo = "ya existe en el modelo" });
            else if (vistosPlan.TryGetValue(clave, out otroId))
                choques.Add(new { id = pl.Item1.Id.IntegerValue, antes = pl.Item2, despues = pl.Item3, choque_con = otroId, motivo = "duplicado dentro del lote" });
            else
                vistosPlan[clave] = pl.Item1.Id.IntegerValue;
        }

        if (!aplicar)
        {
            return new
            {
                accion = "auditar",
                documento = doc.Title,
                objetivo,
                campo,
                operacion = op,
                evaluadas = seleccion.Count,
                por_cambiar = planes.Count,
                choques = choques.Count,
                muestra_choques = choques.Take(20).ToList(),
                vista_previa = planes.Take(maxDetalle).Select(p => new { id = p.Item1.Id.IntegerValue, antes = p.Item2, despues = p.Item3 }).ToList(),
                truncado = planes.Count > maxDetalle
            };
        }

        if (choques.Count > 0)
        {
            return new
            {
                accion = "renombrar",
                aplicado = false,
                motivo = choques.Count + " choques de nombre/numero detectados; resuelvalos antes de renombrar.",
                choques = choques.Take(20).ToList()
            };
        }

        // 5. Aplicar cambios (doble pasada si hay intercambio A->B y B->A dentro del mismo lote)
        var hechos = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;

        using (var tr = new Transaction(doc, "Renombrar " + objetivo))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            // Pasada 1: prefijo temporal para evitar colisiones internas (A->B, B->A)
            string tmpTag = "__TMP_" + Guid.NewGuid().ToString("N").Substring(0, 6) + "_";
            var activos = new List<Tuple<View, string, string>>();
            foreach (var pl in planes)
            {
                try
                {
                    AsignarValor(pl.Item1, campo, tmpTag + pl.Item1.Id.IntegerValue);
                    activos.Add(pl);
                }
                catch (Exception ex)
                {
                    errores.Add(new { id = pl.Item1.Id.IntegerValue, antes = pl.Item2, despues = pl.Item3, error = ex.Message });
                }
            }
            // Pasada 2: nombre definitivo
            foreach (var pl in activos)
            {
                try
                {
                    AsignarValor(pl.Item1, campo, pl.Item3);
                    hechos.Add(new { id = pl.Item1.Id.IntegerValue, antes = pl.Item2, despues = ValorActual(pl.Item1, campo) });
                }
                catch (Exception ex)
                {
                    errores.Add(new { id = pl.Item1.Id.IntegerValue, antes = pl.Item2, despues = pl.Item3, error = ex.Message });
                }
            }

            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new
        {
            accion = "renombrar",
            dry_run = dryRun,
            transaccion = estado.ToString(),
            objetivo,
            campo,
            operacion = op,
            evaluadas = seleccion.Count,
            renombradas = hechos.Count,
            errores = errores.Count,
            detalle = hechos.Take(maxDetalle).ToList(),
            detalle_truncado = hechos.Count > maxDetalle,
            detalle_errores = errores.Take(20).ToList(),
            avisos_revit = fallas.Messages
        };
    }

    static Func<View, string, string> Operacion(string op, JObject data)
    {
        switch ((op ?? "").ToLowerInvariant())
        {
            case "reemplazar":
            {
                string buscar = (string)data["buscar"];
                if (string.IsNullOrEmpty(buscar)) throw new ArgumentException("Falta 'buscar'.");
                string por = (string)data["reemplazo"] ?? "";
                bool rx = data["regex"] != null && (bool)data["regex"];
                if (rx) { var r = new Regex(buscar); return (v, s) => r.Replace(s, por); }
                return (v, s) => s.Replace(buscar, por);
            }
            case "prefijo":
            {
                string tx = (string)data["texto"];
                if (string.IsNullOrEmpty(tx)) throw new ArgumentException("Falta 'texto'.");
                return (v, s) => s.StartsWith(tx) ? s : tx + s;
            }
            case "sufijo":
            {
                string tx = (string)data["texto"];
                if (string.IsNullOrEmpty(tx)) throw new ArgumentException("Falta 'texto'.");
                return (v, s) => s.EndsWith(tx) ? s : s + tx;
            }
            case "transformar":
            {
                string modo = ((string)data["modo"] ?? "").ToLowerInvariant();
                switch (modo)
                {
                    case "mayusculas": return (v, s) => s.ToUpperInvariant();
                    case "minusculas": return (v, s) => s.ToLowerInvariant();
                    case "sin_tildes": return (v, s) => SinTildes(s);
                    case "codigo": return (v, s) => Codigo(s);
                    default: throw new ArgumentException("modo debe ser mayusculas, minusculas, sin_tildes o codigo.");
                }
            }
            case "plantilla":
            {
                string patron = (string)data["plantilla"];
                if (string.IsNullOrEmpty(patron)) throw new ArgumentException("Falta 'plantilla'.");
                return (v, s) => patron
                    .Replace("{nombre}", v.Name ?? "")
                    .Replace("{numero}", (v as ViewSheet)?.SheetNumber ?? "")
                    .Replace("{nivel}", NivelDe(v))
                    .Replace("{tipo}", v.ViewType.ToString())
                    .Replace("{escala}", v.Scale > 0 ? v.Scale.ToString() : "");
            }
            default:
                throw new ArgumentException("operacion desconocida: '" + op + "'. Use reemplazar, prefijo, sufijo, transformar o plantilla.");
        }
    }

    static string ValorActual(View v, string campo)
    {
        if (campo == "numero" && v is ViewSheet vs) return vs.SheetNumber ?? "";
        return v.Name ?? "";
    }

    static void AsignarValor(View v, string campo, string val)
    {
        if (campo == "numero" && v is ViewSheet vs) vs.SheetNumber = val;
        else v.Name = val;
    }

    static string ClaveUnicidad(View v, string campo, string val)
    {
        if (v is ViewSheet) return campo == "numero" ? ("SHEET_NUM|" + val) : ("SHEET_ID|" + v.Id.IntegerValue + "|" + val);
        return v.ViewType.ToString() + "|" + val;
    }

    static string NivelDe(View v)
    {
        return (v as ViewPlan)?.GenLevel?.Name ?? "";
    }

    static string SinTildes(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    static string Codigo(string s)
    {
        string t = SinTildes(s).ToUpperInvariant();
        t = Regex.Replace(t, @"\s+", "-");
        t = Regex.Replace(t, @"[^A-Z0-9\-]", "");
        t = Regex.Replace(t, @"-{2,}", "-").Trim('-');
        return t;
    }
}
