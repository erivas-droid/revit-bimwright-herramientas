// herramienta: sincronizar-recintos-desde-areas (bimwright-mcp)
// Cruza geometricamente cada Room ubicado contra los poligonos de Areas de un AreaScheme
// en el mismo nivel, compara un mapa de parametros (Area -> Room) y opcionalmente traspasa
// los valores (numero de departamento, torre/edificio, tipologia, uso, etc.).
// Acciones: auditar (solo lectura, admite `documento`) y aplicar (dry_run por defecto true).
// Omite recintos dentro de grupos al escribir. Ver README.md de esta carpeta.
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
    const double FT_TO_M = 0.3048;
    const double FT2_TO_M2 = 0.09290304;

    class AreaPoly
    {
        public Area Elem;
        public int Id;
        public string Nivel;
        public string Etiqueta;
        public bool Prioritaria;
        public List<List<XYZ>> Loops = new List<List<XYZ>>();
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;
    }

    class PlanCambio
    {
        public Room Recinto;
        public AreaPoly Area;
        public string ModoCruce;
        public List<Tuple<string, string, Parameter, Parameter, string, string>> Campos =
            new List<Tuple<string, string, Parameter, Parameter, string, string>>();
    }

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
            case "aplicar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("aplicar solo trabaja sobre el documento activo.");
                return Ejecutar(doc, data, true, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use auditar o aplicar.");
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

    static object Ejecutar(Document doc, JObject data, bool escribir, bool dryRun)
    {
        string esquema = (string)data["esquema"];
        if (string.IsNullOrEmpty(esquema))
            throw new ArgumentException("Falta 'esquema' (nombre del AreaScheme).");

        var mapaObj = data["mapa"] as JObject;
        if (mapaObj == null || !mapaObj.Properties().Any())
            throw new ArgumentException("Falta 'mapa': objeto { \"param_area\": \"param_recinto\", ... }.");
        var mapa = mapaObj.Properties()
            .Select(p => new KeyValuePair<string, string>(p.Name, (string)p.Value))
            .ToList();

        string nivelUnico = (string)data["nivel"];
        var nivelesArr = (data["niveles"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList();
        HashSet<string> filtroNiveles = null;
        if (!string.IsNullOrEmpty(nivelUnico))
            filtroNiveles = new HashSet<string>(new[] { nivelUnico }, StringComparer.OrdinalIgnoreCase);
        else if (nivelesArr != null && nivelesArr.Count > 0)
            filtroNiveles = new HashSet<string>(nivelesArr, StringComparer.OrdinalIgnoreCase);

        var idsFiltro = (data["recinto_ids"] as JArray)?.Select(x => (int)x).ToHashSet();
        bool soloVacios = data["solo_vacios"] != null && (bool)data["solo_vacios"];
        bool sobrescribirVacio = data["sobrescribir_con_vacio"] != null && (bool)data["sobrescribir_con_vacio"];
        bool incluirEnGrupo = data["incluir_en_grupo"] != null && (bool)data["incluir_en_grupo"];
        double tolBordeM = data["tolerancia_borde_m"] == null ? 0.5 : (double)data["tolerancia_borde_m"];
        int maxDetalle = data["max_detalle"] == null ? 50 : (int)data["max_detalle"];

        var prioObj = data["prioridad_uso"] as JObject;
        string prioParam = prioObj == null ? null : (string)prioObj["parametro"];
        string prioValor = prioObj == null ? null : (string)prioObj["valor"];

        // 1. Extraer Areas del esquema agrupadas por nivel
        var areasPorNivel = ExtraerAreasPorNivel(doc, esquema, filtroNiveles, prioParam, prioValor);
        if (areasPorNivel.Count == 0)
            throw new InvalidOperationException("No hay areas con superficie > 0 en el esquema '" + esquema + "'"
                + (filtroNiveles != null ? " para los niveles indicados." : "."));

        // 2. Recolectar Rooms ubicados en esos niveles
        var recintos = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>()
            .Where(r => r.Location != null && r.Area > 1e-6 && r.Level != null)
            .Where(r => areasPorNivel.ContainsKey(r.Level.Name))
            .Where(r => idsFiltro == null || idsFiltro.Contains(r.Id.IntegerValue))
            .OrderBy(r => r.Level.ProjectElevation).ThenBy(r => r.Number, new Natural())
            .ToList();

        var resumenParam = new Dictionary<string, Dictionary<string, int>>();
        foreach (var kv in mapa)
            resumenParam[kv.Key + " -> " + kv.Value] = new Dictionary<string, int>();

        var sinArea = new List<object>();
        var omitidosGrupo = new List<object>();
        var planes = new List<PlanCambio>();
        int conArea = 0, coincidenTodos = 0;

        foreach (var r in recintos)
        {
            var areasNivel = areasPorNivel[r.Level.Name];
            string modo;
            var area = BuscarAreaParaRecinto(r, areasNivel, tolBordeM, out modo);
            if (area == null)
            {
                sinArea.Add(new
                {
                    id = r.Id.IntegerValue,
                    nivel = r.Level.Name,
                    numero = r.Number,
                    nombre = NombreRecinto(r),
                    motivo = modo
                });
                continue;
            }
            conArea++;

            bool enGrupo = r.GroupId != ElementId.InvalidElementId;
            if (escribir && enGrupo && !incluirEnGrupo)
            {
                omitidosGrupo.Add(new
                {
                    id = r.Id.IntegerValue,
                    nivel = r.Level.Name,
                    numero = r.Number,
                    nombre = NombreRecinto(r),
                    area_coincidente = area.Etiqueta
                });
                continue;
            }

            var plan = new PlanCambio { Recinto = r, Area = area, ModoCruce = modo };
            foreach (var kv in mapa)
            {
                string clave = kv.Key + " -> " + kv.Value;
                var stats = resumenParam[clave];

                var pSrc = area.Elem.LookupParameter(kv.Key);
                var pDst = r.LookupParameter(kv.Value);
                if (pSrc == null) { Contar(stats, "sin parametro en Area"); continue; }
                if (pDst == null) { Contar(stats, "sin parametro en Room"); continue; }
                if (pDst.IsReadOnly) { Contar(stats, "Room solo lectura"); continue; }
                if (enGrupo && !VariaPorGrupo(pDst))
                {
                    Contar(stats, "en grupo (no varia por instancia)");
                    continue;
                }

                string valSrc = Texto(pSrc);
                string valDst = Texto(pDst);
                if (string.IsNullOrEmpty(valSrc) && !sobrescribirVacio) { Contar(stats, "Area vacio (omitido)"); continue; }
                if (soloVacios && !string.IsNullOrEmpty(valDst)) { Contar(stats, "Room con valor (solo_vacios)"); continue; }
                if (valSrc == valDst) { Contar(stats, "ya igual"); continue; }

                Contar(stats, "por cambiar");
                plan.Campos.Add(Tuple.Create(kv.Key, kv.Value, pSrc, pDst, valDst, valSrc));
            }

            if (plan.Campos.Count == 0) coincidenTodos++;
            else planes.Add(plan);
        }

        // 3. Si es solo auditoria, devolver plan detallado
        if (!escribir)
        {
            var difs = planes.Take(maxDetalle).Select(pl => new
            {
                id = pl.Recinto.Id.IntegerValue,
                nivel = pl.Recinto.Level.Name,
                numero = pl.Recinto.Number,
                nombre = NombreRecinto(pl.Recinto),
                en_grupo = pl.Recinto.GroupId != ElementId.InvalidElementId,
                area_id = pl.Area.Id,
                area = pl.Area.Etiqueta,
                cruce = pl.ModoCruce,
                cambios = pl.Campos.Select(c => new
                {
                    de_area = c.Item1,
                    a_recinto = c.Item2,
                    antes = c.Item5,
                    despues = c.Item6
                }).ToList()
            }).ToList();

            return new Dictionary<string, object>
            {
                { "accion", "auditar" },
                { "documento", doc.Title },
                { "esquema", esquema },
                { "niveles_evaluados", areasPorNivel.Keys.OrderBy(x => x).ToList() },
                { "recintos_evaluados", recintos.Count },
                { "con_area", conArea },
                { "sin_area", sinArea.Count },
                { "ya_coinciden", coincidenTodos },
                { "con_diferencias", planes.Count },
                { "con_diferencias_en_grupo", planes.Count(p => p.Recinto.GroupId != ElementId.InvalidElementId) },
                { "resumen_por_parametro", resumenParam },
                { "detalle_diferencias", difs },
                { "detalle_truncado", planes.Count > maxDetalle },
                { "sin_area_muestra", sinArea.Take(20).ToList() }
            };
        }

        // 4. Aplicar cambios con Transaction + SubTransaction por recinto
        var aplicados = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        int camposEscritos = 0;

        using (var tr = new Transaction(doc, "Sincronizar recintos desde areas (" + esquema + ")"))
        {
            var opts = tr.GetFailureHandlingOptions();
            opts.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(opts);
            tr.Start();

            foreach (var pl in planes)
            {
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        var hechos = new List<object>();
                        foreach (var c in pl.Campos)
                        {
                            Asignar(c.Item4, c.Item3);
                            camposEscritos++;
                            hechos.Add(new { parametro = c.Item2, antes = c.Item5, despues = Texto(c.Item4) });
                        }
                        st.Commit();
                        aplicados.Add(new
                        {
                            id = pl.Recinto.Id.IntegerValue,
                            nivel = pl.Recinto.Level.Name,
                            numero = pl.Recinto.Number,
                            nombre = NombreRecinto(pl.Recinto),
                            area = pl.Area.Etiqueta,
                            cambios = hechos
                        });
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        errores.Add(new
                        {
                            id = pl.Recinto.Id.IntegerValue,
                            numero = pl.Recinto.Number,
                            nombre = NombreRecinto(pl.Recinto),
                            error = ex.Message
                        });
                    }
                }
            }
            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new Dictionary<string, object>
        {
            { "accion", "aplicar" },
            { "dry_run", dryRun },
            { "transaccion", estado.ToString() },
            { "documento", doc.Title },
            { "esquema", esquema },
            { "recintos_evaluados", recintos.Count },
            { "con_area", conArea },
            { "sin_area", sinArea.Count },
            { "omitidos_en_grupo", omitidosGrupo.Count },
            { "recintos_actualizados", aplicados.Count },
            { "campos_escritos", camposEscritos },
            { "errores", errores.Count },
            { "resumen_por_parametro", resumenParam },
            { "detalle", aplicados.Take(maxDetalle).ToList() },
            { "detalle_truncado", aplicados.Count > maxDetalle },
            { "detalle_errores", errores.Take(20).ToList() },
            { "avisos_revit", fallas.Messages }
        };
    }

    // ---------- geometria y cruce ----------

    static Dictionary<string, List<AreaPoly>> ExtraerAreasPorNivel(Document doc, string esquema,
        HashSet<string> filtroNiveles, string prioParam, string prioValor)
    {
        var porNivel = new Dictionary<string, List<AreaPoly>>(StringComparer.OrdinalIgnoreCase);
        var bOpts = new SpatialElementBoundaryOptions();

        var areas = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().Cast<Area>()
            .Where(a => a.Area > 1e-6 && a.AreaScheme != null && a.Level != null
                && string.Equals(a.AreaScheme.Name, esquema, StringComparison.OrdinalIgnoreCase))
            .Where(a => filtroNiveles == null || filtroNiveles.Contains(a.Level.Name))
            .ToList();

        foreach (var a in areas)
        {
            var ap = new AreaPoly
            {
                Elem = a,
                Id = a.Id.IntegerValue,
                Nivel = a.Level.Name,
                Etiqueta = ((a.Number ?? "") + " " + TextoBuiltIn(a, BuiltInParameter.ROOM_NAME)).Trim(),
                Prioritaria = !string.IsNullOrEmpty(prioParam)
                    && string.Equals(Texto(a.LookupParameter(prioParam)), prioValor, StringComparison.OrdinalIgnoreCase)
            };
            if (string.IsNullOrEmpty(ap.Etiqueta)) ap.Etiqueta = "Area " + ap.Id;

            var segLists = a.GetBoundarySegments(bOpts);
            if (segLists != null)
            {
                foreach (var segList in segLists)
                {
                    var pts = new List<XYZ>();
                    foreach (var seg in segList)
                    {
                        var tess = seg.GetCurve().Tessellate();
                        for (int i = 0; i < tess.Count - 1; i++)
                        {
                            pts.Add(tess[i]);
                            if (tess[i].X < ap.MinX) ap.MinX = tess[i].X;
                            if (tess[i].Y < ap.MinY) ap.MinY = tess[i].Y;
                            if (tess[i].X > ap.MaxX) ap.MaxX = tess[i].X;
                            if (tess[i].Y > ap.MaxY) ap.MaxY = tess[i].Y;
                        }
                    }
                    if (pts.Count >= 3) ap.Loops.Add(pts);
                }
            }
            if (ap.Loops.Count == 0) continue;

            List<AreaPoly> lista;
            if (!porNivel.TryGetValue(ap.Nivel, out lista)) { lista = new List<AreaPoly>(); porNivel[ap.Nivel] = lista; }
            lista.Add(ap);
        }

        foreach (var k in porNivel.Keys.ToList())
            porNivel[k] = porNivel[k].OrderByDescending(x => x.Prioritaria).ToList();

        return porNivel;
    }

    static AreaPoly BuscarAreaParaRecinto(Room r, List<AreaPoly> areas, double tolM, out string modo)
    {
        var lp = r.Location as LocationPoint;
        if (lp != null)
        {
            foreach (var a in areas)
                if (PuntoEnArea(lp.Point.X, lp.Point.Y, a)) { modo = "punto"; return a; }
        }

        // Respaldo: centro del BoundingBox del recinto
        var bb = r.get_BoundingBox(null);
        XYZ refPt = lp != null ? lp.Point : (bb == null ? null : (bb.Min + bb.Max) / 2.0);
        if (bb != null)
        {
            var c = (bb.Min + bb.Max) / 2.0;
            foreach (var a in areas)
                if (PuntoEnArea(c.X, c.Y, a)) { modo = "centro_bbox"; return a; }
        }

        if (refPt == null) { modo = "sin ubicacion"; return null; }

        AreaPoly mejor = null;
        double mejorD = double.MaxValue;
        foreach (var a in areas)
        {
            double d = DistAArea(refPt.X, refPt.Y, a);
            if (d < mejorD) { mejorD = d; mejor = a; }
        }
        double dm = mejorD * FT_TO_M;
        if (mejor != null && dm <= tolM) { modo = "cercano (" + Math.Round(dm, 2) + " m)"; return mejor; }
        modo = "fuera de tolerancia (" + Math.Round(dm, 2) + " m)";
        return null;
    }

    static bool PuntoEnLoop(double x, double y, List<XYZ> poly)
    {
        bool dentro = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = poly[i].X, yi = poly[i].Y;
            double xj = poly[j].X, yj = poly[j].Y;
            bool cruza = ((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi + 1e-12) + xi);
            if (cruza) dentro = !dentro;
        }
        return dentro;
    }

    static bool PuntoEnArea(double x, double y, AreaPoly a)
    {
        if (x < a.MinX - 1e-4 || x > a.MaxX + 1e-4 || y < a.MinY - 1e-4 || y > a.MaxY + 1e-4) return false;
        if (!PuntoEnLoop(x, y, a.Loops[0])) return false;
        for (int i = 1; i < a.Loops.Count; i++)
            if (PuntoEnLoop(x, y, a.Loops[i])) return false;
        return true;
    }

    static double DistSegmento(double px, double py, double x1, double y1, double x2, double y2)
    {
        double dx = x2 - x1, dy = y2 - y1;
        double len2 = dx * dx + dy * dy;
        if (len2 < 1e-12) return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        double t = Math.Max(0, Math.Min(1, ((px - x1) * dx + (py - y1) * dy) / len2));
        double cx = x1 + t * dx, cy = y1 + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    static double DistAArea(double x, double y, AreaPoly a)
    {
        double mejor = double.MaxValue;
        foreach (var loop in a.Loops)
        {
            int n = loop.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double d = DistSegmento(x, y, loop[j].X, loop[j].Y, loop[i].X, loop[i].Y);
                if (d < mejor) mejor = d;
            }
        }
        return mejor;
    }

    // ---------- parametros y utilidades ----------

    static bool VariaPorGrupo(Parameter p)
    {
        var id = p.Definition as InternalDefinition;
        if (id == null) return false;
        if (id.VariesAcrossGroups) return true;
        // Parametros integrados de instancia en Room que Revit permite variar por instancia de grupo
        var bip = id.BuiltInParameter;
        return bip == BuiltInParameter.ROOM_NUMBER || bip == BuiltInParameter.ROOM_NAME
            || bip == BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS || bip == BuiltInParameter.ROOM_DEPARTMENT;
    }

    static string NombreRecinto(Room r)
    {
        return TextoBuiltIn(r, BuiltInParameter.ROOM_NAME);
    }

    static string TextoBuiltIn(Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        return Texto(p);
    }

    static string Texto(Parameter p)
    {
        if (p == null || !p.HasValue) return "";
        if (p.StorageType == StorageType.String) return p.AsString() ?? "";
        return p.AsValueString() ?? "";
    }

    static void Asignar(Parameter dst, Parameter src)
    {
        if (dst.StorageType == src.StorageType)
        {
            switch (src.StorageType)
            {
                case StorageType.String: dst.Set(src.AsString() ?? ""); return;
                case StorageType.Integer: dst.Set(src.AsInteger()); return;
                case StorageType.Double: dst.Set(src.AsDouble()); return;
                case StorageType.ElementId: dst.Set(src.AsElementId()); return;
            }
        }
        string txt = Texto(src);
        if (dst.StorageType == StorageType.String) { dst.Set(txt); return; }
        if (!dst.SetValueString(txt))
            throw new InvalidOperationException("No se pudo convertir '" + txt + "' a " + dst.StorageType);
    }

    static void Contar(Dictionary<string, int> d, string k)
    {
        int n; d.TryGetValue(k, out n); d[k] = n + 1;
    }

    class Natural : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            var ra = Regex.Split(a ?? "", @"(\d+)");
            var rb = Regex.Split(b ?? "", @"(\d+)");
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
}
