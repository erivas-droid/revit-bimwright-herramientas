// herramienta: vistas-desde-recintos (bimwright-mcp)
// Por cada recinto crea una planta ampliada (llamada desde la planta del nivel, o planta nueva)
// recortada a su contorno, con escala, plantilla y nombre, y opcionalmente sus 4 alzados
// interiores recortados al recinto.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Acciones: listar (solo lectura) y crear (dry_run por defecto true). Ver README.md.
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
        var doc = app.ActiveUIDocument.Document;
        string accion = (string)data["accion"] ?? "";
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        switch (accion)
        {
            case "listar": return Listar(doc, data);
            case "crear": return Crear(doc, data, dryRun);
            default: throw new ArgumentException("accion desconocida: '" + accion + "'. Use listar o crear.");
        }
    }

    // ---------- recintos ----------

    // Filtros (se combinan): recinto_ids, nivel, nombre (regex), numero (regex). crear exige alguno o todos=true.
    static List<Room> Recintos(Document doc, JObject data, bool exigir)
    {
        var ids = data["recinto_ids"] as JArray;
        string nivel = (string)data["nivel"];
        string nombre = (string)data["nombre"];
        string numero = (string)data["numero"];
        bool todos = data["todos"] != null && (bool)data["todos"];
        if (exigir && ids == null && nivel == null && nombre == null && numero == null && !todos)
            throw new ArgumentException("Indique recinto_ids, nivel, nombre o numero, o todos=true.");
        IEnumerable<Room> rs = ids != null
            ? ids.Select(x => doc.GetElement(new ElementId((int)x)) as Room).Where(r => r != null)
            : new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>();
        if (nivel != null) rs = rs.Where(r => r.Level != null && r.Level.Name == nivel);
        if (nombre != null) { var rx = new Regex(nombre, RegexOptions.IgnoreCase); rs = rs.Where(r => rx.IsMatch(NombreRecinto(r))); }
        if (numero != null) { var rx = new Regex(numero, RegexOptions.IgnoreCase); rs = rs.Where(r => rx.IsMatch(r.Number ?? "")); }
        return rs.OrderBy(r => r.Level == null ? 0 : r.Level.ProjectElevation).ThenBy(r => r.Number).ToList();
    }

    static string NombreRecinto(Room r)
    {
        var p = r.get_Parameter(BuiltInParameter.ROOM_NAME);
        return p == null ? "" : (p.AsString() ?? "");
    }

    static bool Ubicado(Room r) { return r.Location != null && r.Area > 1e-6; }

    // Contorno exterior del recinto (cara de acabado), como lista de puntos teselados y CurveLoop.
    static CurveLoop Contorno(Room r, out List<XYZ> puntos)
    {
        var opts = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish };
        var lazos = r.GetBoundarySegments(opts);
        puntos = new List<XYZ>();
        if (lazos == null || lazos.Count == 0) return null;
        foreach (var s in lazos[0]) puntos.AddRange(s.GetCurve().Tessellate());
        try { return CurveLoop.Create(lazos[0].Select(s => s.GetCurve()).ToList()); }
        catch { return null; }
    }

    static ViewPlan Padre(Document doc, Room r, JObject data)
    {
        if (data["vista_padre"] != null)
        {
            var v = doc.GetElement(new ElementId((int)data["vista_padre"])) as ViewPlan;
            if (v == null) throw new ArgumentException("vista_padre no es una planta.");
            if (v.GenLevel == null || v.GenLevel.Id != r.LevelId) return null;
            return v;
        }
        string pn = (string)data["padre_nombre"];
        var rx = pn == null ? null : new Regex(pn, RegexOptions.IgnoreCase);
        return new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
            .Where(v => !v.IsTemplate && v.ViewType == ViewType.FloorPlan && v.GenLevel != null && v.GenLevel.Id == r.LevelId
                && v.GetPrimaryViewId() == ElementId.InvalidElementId && !EsLlamada(v) && (rx == null || rx.IsMatch(v.Name)))
            .OrderBy(v => v.Name).FirstOrDefault();
    }

    static bool EsLlamada(View v)
    {
        var p = v.get_Parameter(BuiltInParameter.SECTION_PARENT_VIEW_NAME);
        return p != null && !string.IsNullOrEmpty(p.AsString());
    }

    // ---------- listar ----------

    static object Listar(Document doc, JObject data)
    {
        bool callout = ((string)data["modo"] ?? "llamada") == "llamada";
        var lista = Recintos(doc, data, false).Select(r =>
        {
            var padre = callout && Ubicado(r) ? Padre(doc, r, data) : null;
            return new
            {
                id = r.Id.IntegerValue, numero = r.Number, nombre = NombreRecinto(r),
                nivel = r.Level == null ? null : r.Level.Name,
                area_m2 = Math.Round(r.Area * 0.09290304, 2),
                estado = r.Location == null ? "sin ubicar" : (r.Area > 1e-6 ? "ok" : "no cerrado"),
                vista_padre = padre == null ? null : padre.Name
            };
        }).ToList();
        return new { recintos = lista.Count, ubicados = lista.Count(x => x.estado == "ok"), lista };
    }

    // ---------- crear ----------

    static object Crear(Document doc, JObject data, bool dryRun)
    {
        string modo = (string)data["modo"] ?? "llamada";
        if (modo != "llamada" && modo != "planta") throw new ArgumentException("modo debe ser 'llamada' o 'planta'.");
        double margen = (data["margen_mm"] == null ? 300.0 : (double)data["margen_mm"]) / MM;
        int escala = data["escala"] == null ? 50 : (int)data["escala"];
        string recorte = (string)data["recorte"] ?? "rectangulo";
        string patron = (string)data["nombre_vista"] ?? "{numero} {nombre} - AMPLIADA";
        bool alzados = data["alzados"] != null && (bool)data["alzados"];
        string patronAlz = (string)data["nombre_alzado"] ?? "{numero} {nombre} - ALZADO {dir}";
        bool enCentro = ((string)data["marcador"] ?? "punto") == "centro";

        var tipos = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().ToList();
        ViewFamilyType tipoVista = null;
        if (data["tipo_vista"] != null)
        {
            tipoVista = tipos.FirstOrDefault(t => t.Name == (string)data["tipo_vista"]);
            if (tipoVista == null) throw new ArgumentException("No existe el tipo de vista '" + data["tipo_vista"] + "'.");
        }
        else if (modo == "planta") tipoVista = tipos.First(t => t.ViewFamily == ViewFamily.FloorPlan);
        ViewFamilyType tipoAlz = null;
        if (alzados)
        {
            var elevs = tipos.Where(t => t.ViewFamily == ViewFamily.Elevation).ToList();
            tipoAlz = data["tipo_alzado"] != null
                ? elevs.FirstOrDefault(t => t.Name == (string)data["tipo_alzado"])
                : elevs.FirstOrDefault(t => t.Name.IndexOf("interior", StringComparison.OrdinalIgnoreCase) >= 0) ?? elevs.FirstOrDefault();
            if (tipoAlz == null) throw new ArgumentException("No hay tipo de alzado '" + data["tipo_alzado"] + "'.");
        }
        View plantilla = null, plantillaAlz = null;
        var plantillas = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => v.IsTemplate).ToList();
        if (data["plantilla"] != null)
        {
            plantilla = plantillas.FirstOrDefault(v => v.Name == (string)data["plantilla"]);
            if (plantilla == null) throw new ArgumentException("No existe la plantilla '" + data["plantilla"] + "'.");
        }
        if (data["plantilla_alzado"] != null)
        {
            plantillaAlz = plantillas.FirstOrDefault(v => v.Name == (string)data["plantilla_alzado"]);
            if (plantillaAlz == null) throw new ArgumentException("No existe la plantilla '" + data["plantilla_alzado"] + "'.");
        }

        var nombresUsados = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
            .Where(v => !v.IsTemplate).Select(v => v.Name));
        var recintos = Recintos(doc, data, true);
        // La planta padre de cada nivel se elige antes de crear nada: si no, una llamada recien
        // creada podria quedar como padre del siguiente recinto.
        var padres = new Dictionary<ElementId, ViewPlan>();
        if (modo == "llamada")
            foreach (var r in recintos.Where(Ubicado))
                if (!padres.ContainsKey(r.LevelId)) padres[r.LevelId] = Padre(doc, r, data);
        var hechos = new List<object>();
        var omitidos = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        using (var t = new Transaction(doc, "Vistas desde recintos"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            foreach (var r in recintos)
            {
                string etiqueta = r.Number + " " + NombreRecinto(r);
                if (!Ubicado(r)) { omitidos.Add(new { recinto = etiqueta, motivo = "sin ubicar o no cerrado" }); continue; }
                string nombre = Nombre(patron, r, "");
                if (nombresUsados.Contains(nombre)) { omitidos.Add(new { recinto = etiqueta, motivo = "ya existe una vista '" + nombre + "'" }); continue; }
                List<XYZ> pts;
                var lazo = Contorno(r, out pts);
                if (pts.Count == 0) { omitidos.Add(new { recinto = etiqueta, motivo = "sin contorno" }); continue; }
                ViewPlan padre = null;
                if (modo == "llamada")
                {
                    padre = padres[r.LevelId];
                    if (padre == null) { omitidos.Add(new { recinto = etiqueta, motivo = "sin planta padre en el nivel " + (r.Level == null ? "?" : r.Level.Name) }); continue; }
                }
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        var res = UnRecinto(doc, r, modo, padre, tipoVista, tipoAlz, plantilla, plantillaAlz, lazo, pts,
                            margen, escala, recorte, nombre, alzados, patronAlz, nombresUsados, enCentro);
                        st.Commit();
                        res["recinto"] = etiqueta;
                        hechos.Add(res);
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        errores.Add(new { recinto = etiqueta, error = ex.Message });
                    }
                }
            }
            estado = dryRun ? t.RollBack() : t.Commit();
        }
        return new Dictionary<string, object>
        {
            { "dry_run", dryRun }, { "transaccion", estado.ToString() }, { "modo", modo },
            { "recintos", recintos.Count }, { "creados", hechos.Count }, { "omitidos", omitidos.Count }, { "errores", errores.Count },
            { "detalle", hechos }, { "detalle_omitidos", omitidos }, { "detalle_errores", errores }
        };
    }

    static string Nombre(string patron, Room r, string dir)
    {
        return patron.Replace("{numero}", r.Number ?? "").Replace("{nombre}", NombreRecinto(r))
            .Replace("{nivel}", r.Level == null ? "" : r.Level.Name).Replace("{dir}", dir).Trim();
    }

    static Dictionary<string, object> UnRecinto(Document doc, Room r, string modo, ViewPlan padre, ViewFamilyType tipoVista,
        ViewFamilyType tipoAlz, View plantilla, View plantillaAlz, CurveLoop lazo, List<XYZ> pts, double margen, int escala,
        string recorte, string nombre, bool alzados, string patronAlz, HashSet<string> nombresUsados, bool enCentro)
    {
        double minX = pts.Min(p => p.X) - margen, minY = pts.Min(p => p.Y) - margen;
        double maxX = pts.Max(p => p.X) + margen, maxY = pts.Max(p => p.Y) + margen;
        double z = pts[0].Z;
        var res = new Dictionary<string, object>();
        View v;
        if (modo == "llamada")
        {
            var tipoId = tipoVista != null ? tipoVista.Id : padre.GetTypeId();
            v = ViewSection.CreateCallout(doc, padre.Id, tipoId, new XYZ(minX, minY, z), new XYZ(maxX, maxY, z));
            res["vista_padre"] = padre.Name;
        }
        else
        {
            v = ViewPlan.Create(doc, tipoVista.Id, r.LevelId);
            var cb = v.CropBox;
            var inv = cb.Transform.Inverse;
            var a = inv.OfPoint(new XYZ(minX, minY, z));
            var b = inv.OfPoint(new XYZ(maxX, maxY, z));
            var nb = new BoundingBoxXYZ { Transform = cb.Transform };
            nb.Min = new XYZ(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), cb.Min.Z);
            nb.Max = new XYZ(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), cb.Max.Z);
            v.CropBoxActive = true;
            v.CropBox = nb;
        }
        v.Name = nombre;
        nombresUsados.Add(nombre);
        v.Scale = escala;
        if (plantilla != null) v.ViewTemplateId = plantilla.Id;
        res["vista"] = nombre;
        res["vista_id"] = v.Id.IntegerValue;
        res["recorte"] = "rectangulo";
        if (recorte == "contorno")
        {
            string nota = Contornear(v, lazo, margen);
            res["recorte"] = nota;
        }
        if (alzados) res["alzados"] = Alzados(doc, r, v, tipoAlz, plantillaAlz, pts, margen, escala, patronAlz, nombresUsados, enCentro);
        return res;
    }

    // Recorte con la forma del recinto, ampliada en el margen. Si no se puede, queda el rectangulo.
    static string Contornear(View v, CurveLoop lazo, double margen)
    {
        if (lazo == null) return "rectangulo (contorno no cerrado)";
        var mgr = v.GetCropRegionShapeManager();
        if (!mgr.CanHaveShape) return "rectangulo (la vista no admite forma)";
        CurveLoop mejor = null;
        double area = -1;
        foreach (var d in new[] { margen, -margen })
        {
            try
            {
                var c = CurveLoop.CreateViaOffset(lazo, d, XYZ.BasisZ);
                var ps = c.SelectMany(x => x.Tessellate()).ToList();
                double a = (ps.Max(p => p.X) - ps.Min(p => p.X)) * (ps.Max(p => p.Y) - ps.Min(p => p.Y));
                if (a > area) { area = a; mejor = c; }
            }
            catch { }
        }
        if (mejor == null) return "rectangulo (no se pudo desplazar el contorno)";
        try { mgr.SetCropShape(mejor); return "contorno"; }
        catch (Exception ex) { return "rectangulo (" + ex.Message + ")"; }
    }

    // Marcador de alzado dentro del recinto, con las 4 vistas recortadas al recinto.
    static List<object> Alzados(Document doc, Room r, View planta, ViewFamilyType tipoAlz, View plantillaAlz, List<XYZ> pts,
        double margen, int escala, string patronAlz, HashSet<string> nombresUsados, bool enCentro)
    {
        var salida = new List<object>();
        if (!(planta is ViewPlan)) { salida.Add(new { error = "la vista ampliada no es planta; no se crean alzados" }); return salida; }
        double z0 = r.Level.ProjectElevation + r.BaseOffset;
        double z1 = z0 + r.UnboundedHeight;
        // marcador: "punto" (punto del recinto, siempre dentro) o "centro" (centro del rectangulo,
        // puede caer fuera en recintos en L).
        var lp = ((LocationPoint)r.Location).Point;
        var centro = enCentro
            ? new XYZ((pts.Min(p => p.X) + pts.Max(p => p.X)) / 2, (pts.Min(p => p.Y) + pts.Max(p => p.Y)) / 2, r.Level.Elevation)
            : new XYZ(lp.X, lp.Y, r.Level.Elevation);
        var marcador = ElevationMarker.CreateElevationMarker(doc, tipoAlz.Id, centro, escala);
        var esquinas = pts.SelectMany(p => new[] { new XYZ(p.X, p.Y, z0), new XYZ(p.X, p.Y, z1) }).ToList();
        for (int i = 0; i < 4; i++)
        {
            if (!marcador.IsAvailableIndex(i)) continue;
            var ev = marcador.CreateElevation(doc, planta.Id, i);
            doc.Regenerate();
            var mira = -ev.ViewDirection;
            string dir = Math.Abs(mira.X) > Math.Abs(mira.Y) ? (mira.X > 0 ? "E" : "O") : (mira.Y > 0 ? "N" : "S");
            string nombre = Nombre(patronAlz, r, dir);
            if (nombresUsados.Contains(nombre)) nombre += " (" + ev.Id.IntegerValue + ")";
            ev.Name = nombre;
            nombresUsados.Add(nombre);
            var cb = ev.CropBox;
            var inv = cb.Transform.Inverse;
            var loc = esquinas.Select(p => inv.OfPoint(p)).ToList();
            // solo lo que esta delante del marcador (z local negativo)
            var delante = loc.Where(p => p.Z < 1e-6).ToList();
            if (delante.Count == 0) delante = loc;
            var nb = new BoundingBoxXYZ { Transform = cb.Transform };
            nb.Min = new XYZ(delante.Min(p => p.X) - margen, loc.Min(p => p.Y) - margen, cb.Min.Z);
            nb.Max = new XYZ(delante.Max(p => p.X) + margen, loc.Max(p => p.Y) + margen, cb.Max.Z);
            ev.CropBoxActive = true;
            ev.CropBox = nb;
            double prof = -loc.Min(p => p.Z) + margen;
            var pClip = ev.get_Parameter(BuiltInParameter.VIEWER_BOUND_FAR_CLIPPING);
            var pOff = ev.get_Parameter(BuiltInParameter.VIEWER_BOUND_OFFSET_FAR);
            string lejano = "sin cambiar";
            if (pClip != null && !pClip.IsReadOnly && pOff != null && !pOff.IsReadOnly)
            {
                pClip.Set(1);
                pOff.Set(prof);
                lejano = Math.Round(prof * MM) + " mm";
            }
            if (plantillaAlz != null) ev.ViewTemplateId = plantillaAlz.Id;
            salida.Add(new
            {
                vista = nombre, id = ev.Id.IntegerValue,
                ancho_mm = Math.Round((nb.Max.X - nb.Min.X) * MM), alto_mm = Math.Round((nb.Max.Y - nb.Min.Y) * MM),
                recorte_lejano = lejano
            });
        }
        return salida;
    }
}
