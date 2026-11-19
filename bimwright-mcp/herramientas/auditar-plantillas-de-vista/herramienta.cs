// herramienta: auditar-plantillas-de-vista (bimwright-mcp)
// Audita el uso de plantillas de vista (View Templates): cantidad de vistas por plantilla,
// plantillas huerfanas (0 usos), vistas sin plantilla y comparacion entre dos plantillas.
// Permite asignar una plantilla en lote y limpiar plantillas sin uso.
// Acciones: auditar (solo lectura, admite `documento`), asignar y limpiar_huerfanas (dry_run por defecto true).
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
            case "asignar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("asignar solo trabaja sobre el documento activo.");
                return Asignar(doc, data, dryRun);
            case "limpiar_huerfanas":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("limpiar_huerfanas solo trabaja sobre el documento activo.");
                return LimpiarHuerfanas(doc, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use auditar, asignar o limpiar_huerfanas.");
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

    static bool VistaNormal(View v)
    {
        return !v.IsTemplate && !(v is ViewSheet) && !(v is ViewSchedule)
            && v.ViewType != ViewType.Internal && v.ViewType != ViewType.ProjectBrowser
            && v.ViewType != ViewType.SystemBrowser && v.ViewType != ViewType.Legend
            && v.ViewType != ViewType.DraftingView;
    }

    // ---------- auditar ----------

    static object Auditar(Document doc, JObject data)
    {
        string tipoFiltro = (string)data["tipo_vista"];
        Regex rx = data["filtro_nombre"] == null ? null : new Regex((string)data["filtro_nombre"], RegexOptions.IgnoreCase);
        int maxDetalle = data["max_detalle"] == null ? 50 : (int)data["max_detalle"];

        var todasVistas = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var plantillas = todasVistas.Where(v => v.IsTemplate).OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name).ToList();
        var vistas = todasVistas.Where(VistaNormal).ToList();

        var usos = new Dictionary<int, List<string>>();
        foreach (var v in vistas)
        {
            if (v.ViewTemplateId == ElementId.InvalidElementId) continue;
            int tid = v.ViewTemplateId.IntegerValue;
            List<string> l;
            if (!usos.TryGetValue(tid, out l)) { l = new List<string>(); usos[tid] = l; }
            l.Add(v.Name);
        }

        var listaPlantillas = plantillas.Select(t =>
        {
            List<string> l;
            usos.TryGetValue(t.Id.IntegerValue, out l);
            int n = l == null ? 0 : l.Count;
            return new
            {
                id = t.Id.IntegerValue,
                nombre = t.Name,
                tipo = t.ViewType.ToString(),
                escala = t.Scale,
                usos = n,
                ejemplos = l == null ? new List<string>() : l.Take(3).ToList()
            };
        }).ToList();

        var huerfanas = listaPlantillas.Where(x => x.usos == 0).ToList();

        var sinPlantilla = vistas
            .Where(v => v.ViewTemplateId == ElementId.InvalidElementId)
            .Where(v => string.IsNullOrEmpty(tipoFiltro) || string.Equals(v.ViewType.ToString(), tipoFiltro, StringComparison.OrdinalIgnoreCase))
            .Where(v => rx == null || rx.IsMatch(v.Name))
            .OrderBy(v => v.ViewType.ToString()).ThenBy(v => v.Name)
            .Select(v => new { id = v.Id.IntegerValue, tipo = v.ViewType.ToString(), nombre = v.Name })
            .ToList();

        object comparacion = null;
        var compArr = data["comparar"] as JArray;
        if (compArr != null && compArr.Count == 2)
        {
            var a = plantillas.FirstOrDefault(t => string.Equals(t.Name, (string)compArr[0], StringComparison.OrdinalIgnoreCase));
            var b = plantillas.FirstOrDefault(t => string.Equals(t.Name, (string)compArr[1], StringComparison.OrdinalIgnoreCase));
            if (a == null || b == null)
                throw new ArgumentException("No se encontraron las plantillas para comparar.");
            comparacion = CompararPlantillas(a, b);
        }

        var res = new Dictionary<string, object>
        {
            { "documento", doc.Title },
            { "total_plantillas", plantillas.Count },
            { "plantillas_huerfanas", huerfanas.Count },
            { "total_vistas_modelo", vistas.Count },
            { "vistas_sin_plantilla", sinPlantilla.Count },
            { "plantillas", listaPlantillas },
            { "muestra_sin_plantilla", sinPlantilla.Take(maxDetalle).ToList() }
        };
        if (comparacion != null) res["comparacion"] = comparacion;
        return res;
    }

    static object CompararPlantillas(View a, View b)
    {
        var difs = new List<object>();
        var noCtrlA = new HashSet<int>(a.GetNonControlledTemplateParameterIds().Select(x => x.IntegerValue));
        var noCtrlB = new HashSet<int>(b.GetNonControlledTemplateParameterIds().Select(x => x.IntegerValue));

        var mapA = a.Parameters.Cast<Parameter>().GroupBy(p => p.Definition.Name).ToDictionary(g => g.Key, g => g.First());
        var mapB = b.Parameters.Cast<Parameter>().GroupBy(p => p.Definition.Name).ToDictionary(g => g.Key, g => g.First());

        foreach (var k in mapA.Keys.Union(mapB.Keys).OrderBy(x => x))
        {
            Parameter pa, pb;
            mapA.TryGetValue(k, out pa);
            mapB.TryGetValue(k, out pb);
            string va = pa == null ? "(no existe)" : (pa.StorageType == StorageType.String ? (pa.AsString() ?? "") : (pa.AsValueString() ?? ""));
            string vb = pb == null ? "(no existe)" : (pb.StorageType == StorageType.String ? (pb.AsString() ?? "") : (pb.AsValueString() ?? ""));
            bool ctrlA = pa != null && !noCtrlA.Contains(pa.Id.IntegerValue);
            bool ctrlB = pb != null && !noCtrlB.Contains(pb.Id.IntegerValue);
            if (va != vb || ctrlA != ctrlB)
                difs.Add(new { parametro = k, a = va, b = vb, controlado_a = ctrlA, controlado_b = ctrlB });
        }
        return new { plantilla_a = a.Name, plantilla_b = b.Name, diferencias = difs.Count, detalle = difs };
    }

    // ---------- asignar ----------

    static object Asignar(Document doc, JObject data, bool dryRun)
    {
        string nombrePlantilla = (string)data["plantilla"];
        if (string.IsNullOrEmpty(nombrePlantilla)) throw new ArgumentException("Falta 'plantilla'.");
        var plantilla = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, nombrePlantilla, StringComparison.OrdinalIgnoreCase));
        if (plantilla == null) throw new ArgumentException("No existe la plantilla '" + nombrePlantilla + "'.");

        string tipoFiltro = (string)data["tipo_vista"];
        string nivelFiltro = (string)data["nivel"];
        Regex rx = data["filtro_nombre"] == null ? null : new Regex((string)data["filtro_nombre"], RegexOptions.IgnoreCase);
        var ids = (data["vista_ids"] as JArray)?.Select(x => (int)x).ToHashSet();
        bool soloSin = data["solo_sin_plantilla"] != null && (bool)data["solo_sin_plantilla"];
        if (ids == null && rx == null && string.IsNullOrEmpty(tipoFiltro) && string.IsNullOrEmpty(nivelFiltro))
            throw new ArgumentException("Indique vista_ids, filtro_nombre, tipo_vista o nivel.");

        var vistas = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(VistaNormal)
            .Where(v => ids == null || ids.Contains(v.Id.IntegerValue))
            .Where(v => string.IsNullOrEmpty(tipoFiltro) || string.Equals(v.ViewType.ToString(), tipoFiltro, StringComparison.OrdinalIgnoreCase))
            .Where(v => string.IsNullOrEmpty(nivelFiltro) || string.Equals((v as ViewPlan)?.GenLevel?.Name, nivelFiltro, StringComparison.OrdinalIgnoreCase))
            .Where(v => rx == null || rx.IsMatch(v.Name))
            .Where(v => !soloSin || v.ViewTemplateId == ElementId.InvalidElementId)
            .ToList();

        var asignadas = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;

        using (var tr = new Transaction(doc, "Asignar plantilla: " + plantilla.Name))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            foreach (var v in vistas)
            {
                if (v.ViewTemplateId == plantilla.Id) continue;
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        string antes = v.ViewTemplateId == ElementId.InvalidElementId ? "(ninguna)" : doc.GetElement(v.ViewTemplateId)?.Name;
                        v.ViewTemplateId = plantilla.Id;
                        st.Commit();
                        asignadas.Add(new { id = v.Id.IntegerValue, vista = v.Name, antes, despues = plantilla.Name });
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        errores.Add(new { id = v.Id.IntegerValue, vista = v.Name, error = ex.Message });
                    }
                }
            }
            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new
        {
            accion = "asignar",
            dry_run = dryRun,
            transaccion = estado.ToString(),
            plantilla = plantilla.Name,
            evaluadas = vistas.Count,
            asignadas = asignadas.Count,
            errores = errores.Count,
            detalle = asignadas.Take(50).ToList(),
            detalle_errores = errores.Take(20).ToList(),
            avisos_revit = fallas.Messages
        };
    }

    // ---------- limpiar_huerfanas ----------

    static object LimpiarHuerfanas(Document doc, JObject data, bool dryRun)
    {
        Regex rx = data["filtro_nombre"] == null ? null : new Regex((string)data["filtro_nombre"], RegexOptions.IgnoreCase);
        var todas = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        var usadas = new HashSet<int>(todas.Where(v => !v.IsTemplate && v.ViewTemplateId != ElementId.InvalidElementId)
            .Select(v => v.ViewTemplateId.IntegerValue));

        var huerfanas = todas.Where(v => v.IsTemplate && !usadas.Contains(v.Id.IntegerValue))
            .Where(v => rx == null || rx.IsMatch(v.Name))
            .OrderBy(v => v.Name).ToList();

        var borradas = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;

        using (var tr = new Transaction(doc, "Limpiar plantillas de vista huerfanas"))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            foreach (var t in huerfanas)
            {
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        doc.Delete(t.Id);
                        st.Commit();
                        borradas.Add(new { id = t.Id.IntegerValue, nombre = t.Name, tipo = t.ViewType.ToString() });
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        errores.Add(new { id = t.Id.IntegerValue, nombre = t.Name, error = ex.Message });
                    }
                }
            }
            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new
        {
            accion = "limpiar_huerfanas",
            dry_run = dryRun,
            transaccion = estado.ToString(),
            candidatas = huerfanas.Count,
            borradas = borradas.Count,
            errores = errores.Count,
            detalle = borradas,
            detalle_errores = errores,
            avisos_revit = fallas.Messages
        };
    }
}
