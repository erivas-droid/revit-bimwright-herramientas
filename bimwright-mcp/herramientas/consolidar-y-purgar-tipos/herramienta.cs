// herramienta: consolidar-y-purgar-tipos (bimwright-mcp)
// Audita tipos de sistema y cargables con 0 instancias y posibles tipos duplicados por nombre;
// consolida tipos migrando ejemplares de un tipo origen a un tipo destino (omitiendo grupos)
// y purga tipos sin uso filtrados por categoria/regex conservando al menos un tipo por familia.
// Acciones: auditar (solo lectura, admite `documento`), consolidar y purgar_sin_uso (dry_run por defecto true).
// Ver README.md de esta carpeta.
using System;
using System.Collections.Generic;
using System.Linq;
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
                return Auditar(doc, data);
            case "consolidar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("consolidar solo trabaja sobre el documento activo.");
                return Consolidar(doc, data, dryRun);
            case "purgar_sin_uso":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("purgar_sin_uso solo trabaja sobre el documento activo.");
                return PurgarSinUso(doc, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use auditar, consolidar o purgar_sin_uso.");
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

    static Dictionary<int, int> ConteoInstanciasPorTipo(Document doc)
    {
        var d = new Dictionary<int, int>();
        foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            var tid = e.GetTypeId();
            if (tid == null || tid == ElementId.InvalidElementId) continue;
            int id = tid.IntegerValue;
            int n; d.TryGetValue(id, out n); d[id] = n + 1;
        }
        return d;
    }

    static List<ElementType> TiposFiltrados(Document doc, JObject data)
    {
        string cat = (string)data["categoria"];
        Regex rx = data["filtro_nombre"] == null ? null : new Regex((string)data["filtro_nombre"], RegexOptions.IgnoreCase);
        return new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>()
            .Where(t => t.Category != null)
            .Where(t => string.IsNullOrEmpty(cat) || string.Equals(t.Category.Name, cat, StringComparison.OrdinalIgnoreCase))
            .Where(t => rx == null || rx.IsMatch(t.Name) || rx.IsMatch(t.FamilyName + ": " + t.Name))
            .ToList();
    }

    // ---------- auditar ----------

    static object Auditar(Document doc, JObject data)
    {
        int maxDetalle = data["max_detalle"] == null ? 50 : (int)data["max_detalle"];
        var usos = ConteoInstanciasPorTipo(doc);
        var tipos = TiposFiltrados(doc, data);

        var sinUso = new List<object>();
        var posiblesDup = new List<object>();
        var rxDup = new Regex(@"(\s+\d+|\s*\(\d+\)|\s*-?\s*copia.*|\s*-?\s*copy.*)$", RegexOptions.IgnoreCase);

        var porCat = tipos.GroupBy(t => t.Category.Name)
            .OrderByDescending(g => g.Count(t => !usos.ContainsKey(t.Id.IntegerValue)))
            .Select(g => new
            {
                categoria = g.Key,
                tipos_totales = g.Count(),
                sin_uso = g.Count(t => !usos.ContainsKey(t.Id.IntegerValue))
            }).ToList();

        foreach (var t in tipos.OrderBy(x => x.Category.Name).ThenBy(x => x.FamilyName).ThenBy(x => x.Name))
        {
            int n; usos.TryGetValue(t.Id.IntegerValue, out n);
            if (n == 0)
            {
                sinUso.Add(new
                {
                    id = t.Id.IntegerValue,
                    categoria = t.Category.Name,
                    familia = t.FamilyName,
                    tipo = t.Name
                });
            }
            if (rxDup.IsMatch(t.Name))
            {
                posiblesDup.Add(new
                {
                    id = t.Id.IntegerValue,
                    categoria = t.Category.Name,
                    familia = t.FamilyName,
                    tipo = t.Name,
                    instancias = n
                });
            }
        }

        return new
        {
            documento = doc.Title,
            tipos_evaluados = tipos.Count,
            tipos_sin_uso = sinUso.Count,
            posibles_duplicados = posiblesDup.Count,
            resumen_por_categoria = porCat.Take(30).ToList(),
            muestra_sin_uso = sinUso.Take(maxDetalle).ToList(),
            muestra_posibles_duplicados = posiblesDup.Take(maxDetalle).ToList()
        };
    }

    // ---------- consolidar ----------

    // mapa: { "tipo_origen": "tipo_destino" } (nombres o ids enteros).
    // borrar_origen: true por defecto (solo si queda con 0 instancias y no es el ultimo tipo de su familia).
    static object Consolidar(Document doc, JObject data, bool dryRun)
    {
        var mapaObj = data["mapa"] as JObject;
        if (mapaObj == null || !mapaObj.Properties().Any())
            throw new ArgumentException("Falta 'mapa': objeto { \"tipo_origen\": \"tipo_destino\", ... }.");
        string cat = (string)data["categoria"];
        bool borrarOrigen = data["borrar_origen"] == null || (bool)data["borrar_origen"];

        var todosTipos = new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>()
            .Where(t => t.Category != null && (string.IsNullOrEmpty(cat) || string.Equals(t.Category.Name, cat, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var instPorTipo = new Dictionary<int, List<Element>>();
        foreach (var e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
        {
            var tid = e.GetTypeId();
            if (tid == null || tid == ElementId.InvalidElementId) continue;
            List<Element> l;
            if (!instPorTipo.TryGetValue(tid.IntegerValue, out l)) { l = new List<Element>(); instPorTipo[tid.IntegerValue] = l; }
            l.Add(e);
        }

        var resultado = new List<object>();
        var aBorrar = new List<ElementId>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;

        using (var tr = new Transaction(doc, "Consolidar tipos"))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            foreach (var prop in mapaObj.Properties())
            {
                var orig = BuscarTipo(doc, todosTipos, prop.Name);
                var dest = BuscarTipo(doc, todosTipos, (string)prop.Value);
                if (orig == null || dest == null)
                {
                    resultado.Add(new { origen = prop.Name, destino = (string)prop.Value, error = orig == null ? "no existe el tipo origen" : "no existe el tipo destino" });
                    continue;
                }
                if (orig.Id == dest.Id)
                {
                    resultado.Add(new { origen = orig.Name, destino = dest.Name, omitido = "son el mismo tipo" });
                    continue;
                }

                List<Element> insts;
                instPorTipo.TryGetValue(orig.Id.IntegerValue, out insts);
                if (insts == null) insts = new List<Element>();

                int migrados = 0, enGrupo = 0, errores = 0;
                foreach (var e in insts)
                {
                    if (e.GroupId != ElementId.InvalidElementId) { enGrupo++; continue; }
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try
                        {
                            if (!e.IsValidType(dest.Id)) { st.RollBack(); errores++; continue; }
                            e.ChangeTypeId(dest.Id);
                            st.Commit();
                            migrados++;
                        }
                        catch
                        {
                            if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                            errores++;
                        }
                    }
                }

                bool puedeBorrar = borrarOrigen && enGrupo == 0 && errores == 0 && !EsUnicoDeFamilia(todosTipos, orig, aBorrar);
                if (puedeBorrar) aBorrar.Add(orig.Id);

                resultado.Add(new
                {
                    origen = orig.FamilyName + ": " + orig.Name,
                    destino = dest.FamilyName + ": " + dest.Name,
                    migrados,
                    omitidos_en_grupo = enGrupo,
                    errores,
                    origen_marcado_para_borrar = puedeBorrar
                });
            }

            int borrados = 0;
            if (aBorrar.Count > 0)
            {
                doc.Regenerate();
                foreach (var id in aBorrar)
                {
                    using (var st = new SubTransaction(doc))
                    {
                        st.Start();
                        try { doc.Delete(id); st.Commit(); borrados++; }
                        catch { if (st.HasStarted() && !st.HasEnded()) st.RollBack(); }
                    }
                }
            }

            estado = dryRun ? tr.RollBack() : tr.Commit();
            return new
            {
                accion = "consolidar",
                dry_run = dryRun,
                transaccion = estado.ToString(),
                tipos_origen_borrados = borrados,
                detalle = resultado,
                avisos_revit = fallas.Messages
            };
        }
    }

    // ---------- purgar_sin_uso ----------

    static object PurgarSinUso(Document doc, JObject data, bool dryRun)
    {
        if (data["categoria"] == null && data["filtro_nombre"] == null && (data["todos"] == null || !(bool)data["todos"]))
            throw new ArgumentException("Indique 'categoria', 'filtro_nombre' o todos=true.");

        var usos = ConteoInstanciasPorTipo(doc);
        var todosTipos = new FilteredElementCollector(doc).WhereElementIsElementType().Cast<ElementType>()
            .Where(t => t.Category != null).ToList();
        var candidatos = TiposFiltrados(doc, data)
            .Where(t => !usos.ContainsKey(t.Id.IntegerValue))
            .OrderBy(t => t.Category.Name).ThenBy(t => t.FamilyName).ThenBy(t => t.Name)
            .ToList();

        var borrados = new List<object>();
        var omitidos = new List<object>();
        // Tipos que quedan por (categoria, familia), contados ANTES de borrar: un elemento borrado
        // no se puede leer (InvalidObjectException), asi que no se vuelve a consultar.
        var restantes = todosTipos
            .GroupBy(x => Tuple.Create(x.Category.Id.IntegerValue, (x.FamilyName ?? "").ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.Count());
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;

        using (var tr = new Transaction(doc, "Purgar tipos sin uso"))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            foreach (var t in candidatos)
            {
                // Datos del tipo leidos antes de borrarlo.
                int tid = t.Id.IntegerValue;
                string cat = t.Category.Name, fam = t.FamilyName, nom = t.Name;
                var clave = Tuple.Create(t.Category.Id.IntegerValue, (fam ?? "").ToLowerInvariant());
                int quedan;
                if (!restantes.TryGetValue(clave, out quedan) || quedan <= 1)
                {
                    omitidos.Add(new { id = tid, tipo = fam + ": " + nom, motivo = "ultimo tipo de la familia" });
                    continue;
                }
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        doc.Delete(t.Id);
                        st.Commit();
                        restantes[clave] = quedan - 1;
                        borrados.Add(new { id = tid, categoria = cat, familia = fam, tipo = nom });
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        omitidos.Add(new { id = tid, tipo = fam + ": " + nom, motivo = ex.Message });
                    }
                }
            }

            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new
        {
            accion = "purgar_sin_uso",
            dry_run = dryRun,
            transaccion = estado.ToString(),
            candidatos = candidatos.Count,
            borrados = borrados.Count,
            omitidos = omitidos.Count,
            muestra_borrados = borrados.Take(50).ToList(),
            muestra_omitidos = omitidos.Take(20).ToList(),
            avisos_revit = fallas.Messages
        };
    }

    static ElementType BuscarTipo(Document doc, List<ElementType> tipos, string clave)
    {
        int id;
        if (int.TryParse(clave, out id)) return doc.GetElement(new ElementId(id)) as ElementType;
        return tipos.FirstOrDefault(t => string.Equals(t.FamilyName + ": " + t.Name, clave, StringComparison.OrdinalIgnoreCase))
            ?? tipos.FirstOrDefault(t => string.Equals(t.Name, clave, StringComparison.OrdinalIgnoreCase));
    }

    static bool EsUnicoDeFamilia(List<ElementType> todos, ElementType t, ICollection<ElementId> yaBorrados)
    {
        int restantes = todos.Count(x => x.Category.Id == t.Category.Id
            && string.Equals(x.FamilyName, t.FamilyName, StringComparison.OrdinalIgnoreCase)
            && !yaBorrados.Contains(x.Id));
        return restantes <= 1;
    }
}
