// herramienta: renumerar (bimwright-mcp)
// Numera en secuencia la Mark (o el numero de recinto, u otro parametro de texto) de muchos
// elementos segun su posicion: por filas, por columnas, a lo largo de una linea guia, por
// otro parametro o en el orden dado. Formato con ceros y nivel, reinicio por nivel.
// Revisa antes que los numeros nuevos no choquen con los de otros elementos (Revit avisa por
// repetidos al confirmar y deshace todo). Omite elementos en grupos.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// dry_run por defecto true. Ver README.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public class McpDynamicScript
{
    const double MM = 304.8;

    public static object Run(UIApplication app, JObject data)
    {
        var uidoc = app.ActiveUIDocument;
        var doc = uidoc.Document;
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        int max = data["max_detalle"] == null ? 50 : (int)data["max_detalle"];
        string orden = (string)data["orden"] ?? "filas";
        string formato = (string)data["formato"] ?? "{n}";
        int inicio = data["inicio"] == null ? 1 : (int)data["inicio"];
        int paso = data["paso"] == null ? 1 : (int)data["paso"];
        bool porNivel = data["por_nivel"] != null && (bool)data["por_nivel"];
        double tol = (data["tolerancia_fila_mm"] == null ? 1000.0 : (double)data["tolerancia_fila_mm"]) / MM;
        bool forzar = data["forzar"] != null && (bool)data["forzar"];

        var todos = Destinos(uidoc, data);
        var enGrupo = todos.Where(e => e.GroupId != ElementId.InvalidElementId).ToList();
        var els = todos.Where(e => e.GroupId == ElementId.InvalidElementId).ToList();
        if (els.Count == 0) throw new ArgumentException("No hay elementos para numerar (los que estan en grupos se omiten).");

        // Parametro: el indicado, o Mark / Number segun la categoria.
        string nombreParam = (string)data["parametro"];
        Func<Element, Parameter> P = e => nombreParam != null ? e.LookupParameter(nombreParam)
            : (e is Room ? e.get_Parameter(BuiltInParameter.ROOM_NUMBER) : e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK));
        var sinParam = els.Where(e => { var p = P(e); return p == null || p.IsReadOnly || p.StorageType != StorageType.String; }).ToList();
        els = els.Except(sinParam).ToList();
        string nombreReal = els.Count > 0 ? P(els[0]).Definition.Name : nombreParam;

        // Orden.
        var pos = els.ToDictionary(e => e.Id, e => Punto(e));
        var sinPunto = els.Where(e => pos[e.Id] == null).ToList();
        if (orden != "ids" && orden != "parametro" && sinPunto.Count > 0)
            throw new ArgumentException(sinPunto.Count + " elementos sin ubicacion (ej. " + sinPunto[0].Id.IntegerValue + "); use orden 'ids' o 'parametro'.");
        Curve ruta = null;
        if (orden == "ruta")
        {
            if (data["ruta_id"] == null) throw new ArgumentException("orden 'ruta' necesita ruta_id (una linea de modelo o de detalle).");
            var ce = doc.GetElement(new ElementId((int)data["ruta_id"])) as CurveElement;
            if (ce == null) throw new ArgumentException("ruta_id no es una linea.");
            ruta = ce.GeometryCurve;
        }
        string paramOrden = (string)data["parametro_orden"];
        var idsOrden = (data["ids"] as JArray)?.Select(x => (int)x).ToList();

        Func<IEnumerable<Element>, List<Element>> ordenar = grupo =>
        {
            switch (orden)
            {
                case "x": return grupo.OrderBy(e => pos[e.Id].X).ThenByDescending(e => pos[e.Id].Y).ToList();
                case "y": return grupo.OrderByDescending(e => pos[e.Id].Y).ThenBy(e => pos[e.Id].X).ToList();
                case "filas": // de arriba hacia abajo, cada fila de izquierda a derecha
                    return grupo.OrderByDescending(e => Math.Round(pos[e.Id].Y / tol)).ThenBy(e => pos[e.Id].X).ToList();
                case "columnas": // de izquierda a derecha, cada columna de arriba hacia abajo
                    return grupo.OrderBy(e => Math.Round(pos[e.Id].X / tol)).ThenByDescending(e => pos[e.Id].Y).ToList();
                case "ruta":
                    return grupo.OrderBy(e => Avance(ruta, pos[e.Id])).ToList();
                case "parametro":
                    if (paramOrden == null) throw new ArgumentException("orden 'parametro' necesita parametro_orden.");
                    return grupo.OrderBy(e => Texto(Buscar(e, paramOrden)), new Natural()).ToList();
                case "ids":
                    if (idsOrden == null) throw new ArgumentException("orden 'ids' necesita la lista ids.");
                    return grupo.OrderBy(e => idsOrden.IndexOf(e.Id.IntegerValue)).ToList();
                default:
                    throw new ArgumentException("orden desconocido: " + orden + ". Use filas, columnas, x, y, ruta, parametro o ids.");
            }
        };

        // Secuencia (por nivel si se pide).
        var plan = new List<Tuple<Element, string, string>>();
        var grupos = porNivel
            ? els.GroupBy(e => NivelId(doc, e)).OrderBy(g => Elevacion(doc, g.Key)).Select(g => g.AsEnumerable()).ToList()
            : new List<IEnumerable<Element>> { els };
        foreach (var g in grupos)
        {
            int n = inicio;
            foreach (var e in ordenar(g))
            {
                string nuevo = Formatear(formato, n, NombreNivel(doc, e));
                plan.Add(Tuple.Create(e, Texto(P(e)), nuevo));
                n += paso;
            }
        }

        // Repetidos dentro de la secuencia y choques con otros elementos de la misma categoria.
        var repetidos = plan.GroupBy(x => x.Item3).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (repetidos.Count > 0)
            throw new InvalidOperationException("El formato produce numeros repetidos: " + string.Join(", ", repetidos.Take(10))
                + ". Con por_nivel, incluya {nivel} en el formato.");
        var ids = new HashSet<ElementId>(els.Select(e => e.Id));
        var cats = new HashSet<int>(els.Where(e => e.Category != null).Select(e => e.Category.Id.IntegerValue));
        var otros = new Dictionary<string, List<int>>();
        foreach (var o in new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements()
            .Where(o => o.Category != null && cats.Contains(o.Category.Id.IntegerValue) && !ids.Contains(o.Id)))
        {
            var q = P(o);
            if (q == null) continue;
            string v = Texto(q);
            if (v == "") continue;
            List<int> l;
            if (!otros.TryGetValue(v, out l)) { l = new List<int>(); otros[v] = l; }
            l.Add(o.Id.IntegerValue);
        }
        var choques = plan.Where(x => otros.ContainsKey(x.Item3))
            .Select(x => new { numero = x.Item3, elemento = x.Item1.Id.IntegerValue, ya_lo_tienen = otros[x.Item3].Take(3).ToList() }).ToList();
        if (choques.Count > 0 && !forzar)
            return new Dictionary<string, object>
            {
                { "aplicado", false },
                { "motivo", choques.Count + " numeros nuevos ya los usan otros elementos de la categoria (incluidos los de grupos). Revit avisaria por repetidos y desharia todo. Cambie formato o inicio, o incluya esos elementos." },
                { "choques", choques.Take(20).ToList() }, { "omitidos_en_grupo", enGrupo.Count }
            };

        var detalle = new List<object>();
        var errores = new List<object>();
        int cambiados = 0;
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        using (var tr = new Transaction(doc, "Renumerar " + nombreReal))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();
            foreach (var x in plan)
            {
                try
                {
                    if (x.Item2 != x.Item3) { P(x.Item1).Set(x.Item3); cambiados++; }
                    var pt = pos.ContainsKey(x.Item1.Id) ? pos[x.Item1.Id] : null;
                    detalle.Add(new
                    {
                        elemento = x.Item1.Id.IntegerValue,
                        nombre = x.Item1 is Room ? (x.Item1.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "") : x.Item1.Name,
                        antes = x.Item2, despues = x.Item3,
                        x_mm = pt == null ? (double?)null : Math.Round(pt.X * MM), y_mm = pt == null ? (double?)null : Math.Round(pt.Y * MM)
                    });
                }
                catch (Exception ex) { errores.Add(new { elemento = x.Item1.Id.IntegerValue, error = ex.Message }); }
            }
            estado = dryRun ? tr.RollBack() : tr.Commit();
        }
        return new Dictionary<string, object>
        {
            { "dry_run", dryRun }, { "transaccion", estado.ToString() }, { "parametro", nombreReal },
            { "orden", orden }, { "formato", formato }, { "por_nivel", porNivel },
            { "elementos", plan.Count }, { "cambiados", cambiados }, { "omitidos_en_grupo", enGrupo.Count },
            { "sin_parametro", sinParam.Count }, { "choques_forzados", forzar ? choques.Count : 0 }, { "errores", errores },
            { "detalle", detalle.Take(max).ToList() }, { "detalle_truncado", detalle.Count > max }
        };
    }

    // ---------- posicion y nivel ----------

    static XYZ Punto(Element e)
    {
        var lp = e.Location as LocationPoint;
        if (lp != null) return lp.Point;
        var lc = e.Location as LocationCurve;
        if (lc != null) return lc.Curve.Evaluate(0.5, true);
        var bb = e.get_BoundingBox(null);
        return bb == null ? null : (bb.Min + bb.Max) / 2;
    }

    static double Avance(Curve c, XYZ p)
    {
        var q = new XYZ(p.X, p.Y, c.GetEndPoint(0).Z);
        var r = c.Project(q);
        return r == null ? double.MaxValue : c.ComputeNormalizedParameter(r.Parameter);
    }

    static ElementId NivelId(Document doc, Element e)
    {
        if (e.LevelId != ElementId.InvalidElementId) return e.LevelId;
        var p = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM) ?? e.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);
        return p == null ? ElementId.InvalidElementId : p.AsElementId();
    }

    static double Elevacion(Document doc, ElementId id)
    {
        var l = doc.GetElement(id) as Level;
        return l == null ? double.MinValue : l.ProjectElevation;
    }

    static string NombreNivel(Document doc, Element e)
    {
        var l = doc.GetElement(NivelId(doc, e)) as Level;
        return l == null ? "" : l.Name;
    }

    // {n} sin ceros, {n:000} con ceros a la izquierda; {nivel} = nombre del nivel.
    static string Formatear(string f, int n, string nivel)
    {
        string s = Regex.Replace(f, @"\{n(?::(0+))?\}", m => m.Groups[1].Success ? n.ToString(new string('0', m.Groups[1].Value.Length)) : n.ToString());
        return s.Replace("{nivel}", nivel);
    }

    // Orden natural: "2" antes que "10".
    class Natural : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            var ra = Regex.Split(a ?? "", @"(\d+)"); var rb = Regex.Split(b ?? "", @"(\d+)");
            for (int i = 0; i < Math.Min(ra.Length, rb.Length); i++)
            {
                if (ra[i] == rb[i]) continue;
                long x, y;
                if (long.TryParse(ra[i], out x) && long.TryParse(rb[i], out y)) return x.CompareTo(y);
                return string.Compare(ra[i], rb[i], StringComparison.OrdinalIgnoreCase);
            }
            return ra.Length.CompareTo(rb.Length);
        }
    }

    // ---------- seleccion y utilidades (iguales a transferir-parametros) ----------

    static List<Element> Destinos(UIDocument uidoc, JObject data)
    {
        var doc = uidoc.Document;
        IEnumerable<Element> els;
        var ids = data["ids"] as JArray;
        if (ids != null) els = ids.Select(x => doc.GetElement(new ElementId((int)x)));
        else if (data["seleccion"] != null && (bool)data["seleccion"]) els = uidoc.Selection.GetElementIds().Select(doc.GetElement);
        else if (data["categoria"] != null)
        {
            string cat = (string)data["categoria"];
            FilteredElementCollector col;
            var vt = data["vista"];
            if (vt == null) col = new FilteredElementCollector(doc);
            else if (vt.Type == JTokenType.String && (string)vt == "activa") col = new FilteredElementCollector(doc, uidoc.ActiveView.Id);
            else
            {
                var vid = new ElementId((int)vt);
                if (!(doc.GetElement(vid) is View)) throw new ArgumentException("vista " + vt + " no es una vista.");
                col = new FilteredElementCollector(doc, vid);
            }
            els = col.WhereElementIsNotElementType().ToElements().Where(e => e.Category != null && e.Category.Name == cat);
        }
        else throw new ArgumentException("Indique los elementos: ids, seleccion=true o categoria.");
        var lista = els.Where(e => e != null).ToList();
        var donde = data["donde"] as JObject;
        if (donde != null)
            foreach (var p in donde.Properties())
            {
                string n = p.Name, valor = (string)p.Value;
                lista = lista.Where(e => { var q = Buscar(e, n); return q != null && Texto(q) == valor; }).ToList();
            }
        // recintos sin ubicar no se numeran
        return lista.Where(e => !(e is Room) || ((Room)e).Location != null).ToList();
    }

    static Parameter Buscar(Element e, string nombre)
    {
        var p = e.LookupParameter(nombre);
        if (p != null) return p;
        var t = e.Document.GetElement(e.GetTypeId());
        return t == null ? null : t.LookupParameter(nombre);
    }

    static string Texto(Parameter p)
    {
        if (p == null || !p.HasValue) return "";
        if (p.StorageType == StorageType.String) return p.AsString() ?? "";
        return p.AsValueString() ?? "";
    }
}
