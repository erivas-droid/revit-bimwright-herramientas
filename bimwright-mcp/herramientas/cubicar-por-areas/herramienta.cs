// herramienta: cubicar-por-areas (bimwright-mcp)
// Cubica elementos modelados (pisos, muros, cielos, puertas, ventanas, mobiliario, etc.)
// cruzandolos geometricamente contra los poligonos de un Area Plan (AreaScheme) o de Rooms.
// Solo lectura: no abre transacciones y admite `documento` para consultar otro modelo abierto
// sin cambiar la pestana activa.
// Acciones: esquemas (lista esquemas de area, niveles y tipos visibles) y cubicar (por defecto).
// Ver README.md de esta carpeta.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public class McpDynamicScript
{
    const double FT_TO_M = 0.3048;
    const double FT2_TO_M2 = 0.09290304;
    const double FT3_TO_M3 = 0.028316846592;

    class ZonaPoly
    {
        public int Id;
        public string Grupo;
        public string Unidad;
        public string Nombre;
        public double AreaM2;
        public bool Prioritaria;
        public Dictionary<string, string> Extra = new Dictionary<string, string>();
        public List<List<XYZ>> Loops = new List<List<XYZ>>();
        public double MinX = double.MaxValue, MinY = double.MaxValue;
        public double MaxX = double.MinValue, MaxY = double.MinValue;
    }

    class Acumulador
    {
        public double Cantidad;
        public double AreaM2;
        public double LongitudM;
        public double VolumenM3;
    }

    public static object Run(UIApplication app, JObject data)
    {
        if (data == null) data = new JObject();
        var doc = ResolverDocumento(app, (string)data["documento"]);
        string accion = ((string)data["accion"] ?? "cubicar").ToLowerInvariant();
        switch (accion)
        {
            case "esquemas":
            case "listar_esquemas":
                return ListarEsquemas(doc, app.ActiveUIDocument, data);
            case "cubicar":
                return Cubicar(doc, app.ActiveUIDocument, data);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use esquemas o cubicar.");
        }
    }

    // ---------- documento y vista ----------

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
        throw new ArgumentException("No hay un modelo abierto con titulo '" + titulo + "'. Abiertos: "
            + string.Join(", ", docs.Select(d => d.Title)));
    }

    static View ResolverVista(Document doc, UIDocument uidoc, JToken tok)
    {
        if (tok == null)
        {
            if (uidoc != null && uidoc.Document.Equals(doc)) return uidoc.ActiveView;
            throw new ArgumentException("Al consultar otro 'documento', indique 'vista' (id o nombre).");
        }
        View v = null;
        if (tok.Type == JTokenType.Integer)
            v = doc.GetElement(new ElementId((int)tok)) as View;
        else
        {
            string nombre = (string)tok;
            v = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>()
                .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, nombre, StringComparison.OrdinalIgnoreCase));
        }
        if (v == null) throw new ArgumentException("No existe la vista '" + tok + "'.");
        return v;
    }

    // ---------- accion: esquemas ----------

    static object ListarEsquemas(Document doc, UIDocument uidoc, JObject data)
    {
        var esquemas = new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>()
            .Select(s => new { id = s.Id.IntegerValue, nombre = s.Name })
            .OrderBy(x => x.nombre).ToList();

        var areasPorEsquemaNivel = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().Cast<Area>()
            .Where(a => a.Area > 1e-6 && a.AreaScheme != null && a.Level != null)
            .GroupBy(a => new { esquema = a.AreaScheme.Name, nivel = a.Level.Name, elev = a.Level.ProjectElevation })
            .OrderBy(g => g.Key.esquema).ThenBy(g => g.Key.elev)
            .Select(g => new
            {
                esquema = g.Key.esquema,
                nivel = g.Key.nivel,
                areas = g.Count(),
                superficie_m2 = Math.Round(g.Sum(a => a.Area) * FT2_TO_M2, 2)
            }).ToList();

        var recintosPorNivel = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>()
            .Where(r => r.Location != null && r.Area > 1e-6 && r.Level != null)
            .GroupBy(r => new { nivel = r.Level.Name, elev = r.Level.ProjectElevation })
            .OrderBy(g => g.Key.elev)
            .Select(g => new
            {
                nivel = g.Key.nivel,
                recintos = g.Count(),
                superficie_m2 = Math.Round(g.Sum(r => r.Area) * FT2_TO_M2, 2)
            }).ToList();

        object vistaInfo = null;
        if (data["vista"] != null || (uidoc != null && uidoc.Document.Equals(doc)))
        {
            var v = ResolverVista(doc, uidoc, data["vista"]);
            vistaInfo = new
            {
                id = v.Id.IntegerValue,
                nombre = v.Name,
                tipo = v.ViewType.ToString(),
                nivel = (v as ViewPlan)?.GenLevel?.Name
            };
        }

        return new
        {
            documento = doc.Title,
            vista = vistaInfo,
            esquemas_area = esquemas,
            areas_por_esquema_y_nivel = areasPorEsquemaNivel,
            recintos_por_nivel = recintosPorNivel
        };
    }

    // ---------- accion: cubicar ----------

    static object Cubicar(Document doc, UIDocument uidoc, JObject data)
    {
        var vista = ResolverVista(doc, uidoc, data["vista"]);
        string nivel = (string)data["nivel"] ?? (vista as ViewPlan)?.GenLevel?.Name;
        if (string.IsNullOrEmpty(nivel))
            throw new ArgumentException("No se pudo inferir el nivel desde la vista; indique 'nivel'.");

        string origen = ((string)data["origen_zonas"] ?? "areas").ToLowerInvariant();
        if (origen != "areas" && origen != "recintos")
            throw new ArgumentException("origen_zonas debe ser 'areas' o 'recintos'.");

        string esquema = (string)data["esquema"];
        if (origen == "areas" && string.IsNullOrEmpty(esquema))
            throw new ArgumentException("Con origen_zonas='areas' falta 'esquema' (use accion='esquemas' para verlos).");

        string paramGrupo = (string)data["param_grupo"];
        string paramUnidad = (string)data["param_unidad"];
        var paramsExtra = (data["param_extra"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList()
            ?? new List<string>();
        var prioObj = data["prioridad_uso"] as JObject;
        string prioParam = prioObj == null ? null : (string)prioObj["parametro"];
        string prioValor = prioObj == null ? null : (string)prioObj["valor"];

        double tolBordeM = data["tolerancia_borde_m"] == null ? 1.0 : (double)data["tolerancia_borde_m"];
        int muestrasMuro = data["muestras_muro"] == null ? 10 : Math.Max(1, (int)data["muestras_muro"]);
        bool incluirDetalle = data["incluir_detalle_elementos"] != null && (bool)data["incluir_detalle_elementos"];

        Regex rxTipo = data["filtro_tipo"] == null ? null : new Regex((string)data["filtro_tipo"], RegexOptions.IgnoreCase);
        Regex rxExcluir = data["excluir_tipo"] == null ? null : new Regex((string)data["excluir_tipo"], RegexOptions.IgnoreCase);

        var categorias = ResolverCategorias(data["categorias"] as JArray);

        // 1. Extraer poligonos de zonas (Areas o Rooms)
        var zonas = ExtraerZonas(doc, origen, esquema, nivel, paramGrupo, paramUnidad, paramsExtra, prioParam, prioValor);
        if (zonas.Count == 0)
            throw new InvalidOperationException("No se encontraron " + origen + " con superficie > 0 en el nivel '" + nivel + "'"
                + (origen == "areas" ? " del esquema '" + esquema + "'." : "."));

        var zonasOrdenadas = zonas
            .OrderByDescending(z => z.Prioritaria)
            .ThenBy(z => string.IsNullOrEmpty(z.Unidad) ? 1 : 0)
            .ToList();

        // 2. Recolectar elementos de las categorias indicadas en la vista
        var catIds = new HashSet<int>(categorias.Select(c => (int)c));
        var elementos = new FilteredElementCollector(doc, vista.Id)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(e => e.Category != null && catIds.Contains(e.Category.Id.IntegerValue))
            .Where(e =>
            {
                string tn = NombreTipo(doc, e);
                if (rxTipo != null && !rxTipo.IsMatch(tn)) return false;
                if (rxExcluir != null && rxExcluir.IsMatch(tn)) return false;
                return true;
            })
            .ToList();

        // 3. Cruce espacial
        var gOpts = new Options { DetailLevel = ViewDetailLevel.Coarse };
        var zonaPorId = zonas.ToDictionary(z => z.Id, z => z);
        var porUnidad = new Dictionary<string, Dictionary<string, Acumulador>>();
        var porGrupo = new Dictionary<string, Dictionary<string, Acumulador>>();
        var porTipo = new Dictionary<string, Acumulador>();
        var sinAsignar = new List<object>();
        var detalleElementos = new List<object>();

        foreach (var e in elementos)
        {
            string cat = e.Category.Name;
            string tipo = NombreTipo(doc, e);
            string claveTipo = cat + " | " + tipo;

            double areaM2 = ParamDouble(e, BuiltInParameter.HOST_AREA_COMPUTED) * FT2_TO_M2;
            double volM3 = ParamDouble(e, BuiltInParameter.HOST_VOLUME_COMPUTED) * FT3_TO_M3;
            double longM = ParamDouble(e, BuiltInParameter.CURVE_ELEM_LENGTH) * FT_TO_M;

            Dictionary<int, double> reparto;
            string motivoSin = null;

            if (e is Floor || e is Ceiling)
                reparto = RepartirSuperficie(e, gOpts, zonasOrdenadas, tolBordeM, out motivoSin);
            else if (e is Wall w)
                reparto = RepartirCurva(w, muestrasMuro, zonasOrdenadas, tolBordeM, out motivoSin);
            else
                reparto = RepartirPunto(e, zonasOrdenadas, tolBordeM, out motivoSin);

            foreach (var kv in reparto)
            {
                ZonaPoly z = null;
                if (kv.Key >= 0) zonaPorId.TryGetValue(kv.Key, out z);
                double frac = kv.Value;
                double pCant = frac;
                double pArea = areaM2 * frac;
                double pLong = longM * frac;
                double pVol = volM3 * frac;

                string gKey = z == null ? "(Sin asignar)" : z.Grupo;
                string uKey = z == null ? "(Sin asignar)" : (z.Grupo + "||" + z.Unidad);

                Acumular(porTipo, claveTipo, pCant, pArea, pLong, pVol);
                AcumularNested(porGrupo, gKey, claveTipo, pCant, pArea, pLong, pVol);
                AcumularNested(porUnidad, uKey, claveTipo, pCant, pArea, pLong, pVol);

                if (z == null)
                {
                    sinAsignar.Add(new
                    {
                        id = e.Id.IntegerValue,
                        categoria = cat,
                        tipo,
                        area_m2 = Math.Round(pArea, 2),
                        motivo = motivoSin
                    });
                }
                if (incluirDetalle)
                {
                    detalleElementos.Add(new
                    {
                        id = e.Id.IntegerValue,
                        categoria = cat,
                        tipo,
                        grupo = gKey,
                        unidad = z == null ? "(Sin asignar)" : z.Unidad,
                        fraccion = Math.Round(frac, 4),
                        area_m2 = Math.Round(pArea, 3),
                        longitud_m = Math.Round(pLong, 3),
                        volumen_m3 = Math.Round(pVol, 3)
                    });
                }
            }
        }

        // 4. Armar salidas ordenadas
        var metaUnidad = zonas
            .GroupBy(z => z.Grupo + "||" + z.Unidad)
            .ToDictionary(g => g.Key, g => new
            {
                grupo = g.First().Grupo,
                unidad = g.First().Unidad,
                superficie_zona_m2 = Math.Round(g.Sum(x => x.AreaM2), 2),
                extra = g.First().Extra
            });

        var salidaUnidades = porUnidad
            .OrderBy(kv => kv.Key, new Natural())
            .Select(kv =>
            {
                var partes = kv.Key.Split(new[] { "||" }, StringSplitOptions.None);
                string g = partes[0];
                string u = partes.Length > 1 ? partes[1] : partes[0];
                double sup = metaUnidad.ContainsKey(kv.Key) ? metaUnidad[kv.Key].superficie_zona_m2 : 0;
                var extra = metaUnidad.ContainsKey(kv.Key) ? metaUnidad[kv.Key].extra : new Dictionary<string, string>();
                return new
                {
                    grupo = g,
                    unidad = u,
                    superficie_zona_m2 = sup,
                    extra,
                    area_total_m2 = Math.Round(kv.Value.Values.Sum(a => a.AreaM2), 2),
                    elementos = kv.Value.OrderBy(x => x.Key).Select(x => FormatearAcum(x.Key, x.Value)).ToList()
                };
            }).ToList();

        var salidaGrupos = porGrupo
            .OrderBy(kv => kv.Key, new Natural())
            .Select(kv => new
            {
                grupo = kv.Key,
                area_total_m2 = Math.Round(kv.Value.Values.Sum(a => a.AreaM2), 2),
                elementos = kv.Value.OrderBy(x => x.Key).Select(x => FormatearAcum(x.Key, x.Value)).ToList()
            }).ToList();

        var salidaTipos = porTipo
            .OrderBy(kv => kv.Key)
            .Select(kv => FormatearAcum(kv.Key, kv.Value)).ToList();

        string csvEscrito = null;
        if (data["exportar_csv"] != null)
        {
            string ruta = Environment.ExpandEnvironmentVariables((string)data["exportar_csv"]);
            string dir = Path.GetDirectoryName(ruta);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("Grupo;Unidad;Categoria;Tipo;Cantidad;Area_m2;Longitud_m;Volumen_m3");
            foreach (var kv in porUnidad.OrderBy(x => x.Key, new Natural()))
            {
                var p = kv.Key.Split(new[] { "||" }, StringSplitOptions.None);
                string g = p[0], u = p.Length > 1 ? p[1] : p[0];
                foreach (var t in kv.Value.OrderBy(x => x.Key))
                {
                    var ct = t.Key.Split(new[] { " | " }, 2, StringSplitOptions.None);
                    sb.AppendLine(string.Join(";", new[]
                    {
                        CsvEsc(g), CsvEsc(u), CsvEsc(ct[0]), CsvEsc(ct.Length > 1 ? ct[1] : ""),
                        Math.Round(t.Value.Cantidad, 3).ToString(CultureInfo.InvariantCulture),
                        Math.Round(t.Value.AreaM2, 3).ToString(CultureInfo.InvariantCulture),
                        Math.Round(t.Value.LongitudM, 3).ToString(CultureInfo.InvariantCulture),
                        Math.Round(t.Value.VolumenM3, 3).ToString(CultureInfo.InvariantCulture)
                    }));
                }
            }
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
            csvEscrito = ruta;
        }

        var res = new Dictionary<string, object>
        {
            { "documento", doc.Title },
            { "vista", vista.Name },
            { "vista_id", vista.Id.IntegerValue },
            { "nivel", nivel },
            { "origen_zonas", origen },
            { "esquema", origen == "areas" ? esquema : null },
            { "zonas_encontradas", zonas.Count },
            { "elementos_evaluados", elementos.Count },
            { "sin_asignar_cantidad", sinAsignar.Count },
            { "sin_asignar_muestra", sinAsignar.Take(20).ToList() },
            { "totales_por_tipo", salidaTipos },
            { "totales_por_grupo", salidaGrupos },
            { "por_unidad", salidaUnidades }
        };
        if (csvEscrito != null) res["csv_exportado"] = csvEscrito;
        if (incluirDetalle) res["detalle_elementos"] = detalleElementos;
        return res;
    }

    // ---------- zonas y geometria 2D ----------

    static List<ZonaPoly> ExtraerZonas(Document doc, string origen, string esquema, string nivel,
        string paramGrupo, string paramUnidad, List<string> paramsExtra, string prioParam, string prioValor)
    {
        var lista = new List<ZonaPoly>();
        var bOpts = new SpatialElementBoundaryOptions();
        if (origen == "recintos")
            bOpts.SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish;

        IEnumerable<SpatialElement> elems;
        if (origen == "areas")
        {
            elems = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().Cast<Area>()
                .Where(a => a.Area > 1e-6 && a.AreaScheme != null
                    && string.Equals(a.AreaScheme.Name, esquema, StringComparison.OrdinalIgnoreCase)
                    && a.Level != null && string.Equals(a.Level.Name, nivel, StringComparison.OrdinalIgnoreCase))
                .Cast<SpatialElement>();
        }
        else
        {
            elems = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>()
                .Where(r => r.Location != null && r.Area > 1e-6
                    && r.Level != null && string.Equals(r.Level.Name, nivel, StringComparison.OrdinalIgnoreCase))
                .Cast<SpatialElement>();
        }

        foreach (var se in elems)
        {
            string nombre = TextoParam(se, BuiltInParameter.ROOM_NAME);
            string numero = se.Number ?? "";
            string grupo = string.IsNullOrEmpty(paramGrupo) ? "General" : TextoParamNombre(se, paramGrupo);
            if (string.IsNullOrEmpty(grupo)) grupo = "General";

            string unidad = string.IsNullOrEmpty(paramUnidad) ? numero : TextoParamNombre(se, paramUnidad);
            if (string.IsNullOrEmpty(unidad)) unidad = !string.IsNullOrEmpty(nombre) ? nombre : ("Zona-" + se.Id.IntegerValue);

            var z = new ZonaPoly
            {
                Id = se.Id.IntegerValue,
                Grupo = grupo,
                Unidad = unidad,
                Nombre = nombre,
                AreaM2 = se.Area * FT2_TO_M2,
                Prioritaria = !string.IsNullOrEmpty(prioParam)
                    && string.Equals(TextoParamNombre(se, prioParam), prioValor, StringComparison.OrdinalIgnoreCase)
            };
            foreach (var pe in paramsExtra)
            {
                string val = TextoParamNombre(se, pe);
                if (!string.IsNullOrEmpty(val)) z.Extra[pe] = val;
            }

            var segLists = se.GetBoundarySegments(bOpts);
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
                            if (tess[i].X < z.MinX) z.MinX = tess[i].X;
                            if (tess[i].Y < z.MinY) z.MinY = tess[i].Y;
                            if (tess[i].X > z.MaxX) z.MaxX = tess[i].X;
                            if (tess[i].Y > z.MaxY) z.MaxY = tess[i].Y;
                        }
                    }
                    if (pts.Count >= 3) z.Loops.Add(pts);
                }
            }
            if (z.Loops.Count > 0) lista.Add(z);
        }
        return lista;
    }

    static Dictionary<int, double> RepartirSuperficie(Element e, Options gOpts, List<ZonaPoly> zonas, double tolM, out string motivo)
    {
        motivo = "";
        var pesos = new Dictionary<int, double>();
        double total = 0;
        var geom = e.get_Geometry(gOpts);
        if (geom != null)
        {
            foreach (var obj in geom)
            {
                var solid = obj as Solid;
                if (solid == null || solid.Volume <= 0) continue;
                foreach (Face face in solid.Faces)
                {
                    var pf = face as PlanarFace;
                    if (pf == null || pf.FaceNormal.Z < 0.9) continue;
                    var mesh = pf.Triangulate();
                    for (int i = 0; i < mesh.NumTriangles; i++)
                    {
                        var tri = mesh.get_Triangle(i);
                        var p0 = tri.get_Vertex(0);
                        var p1 = tri.get_Vertex(1);
                        var p2 = tri.get_Vertex(2);
                        double a = 0.5 * (p1 - p0).CrossProduct(p2 - p0).GetLength();
                        if (a < 1e-8) continue;
                        var c = (p0 + p1 + p2) / 3.0;
                        string m;
                        var z = BuscarZona(c.X, c.Y, null, zonas, tolM, out m);
                        int id = z == null ? -1 : z.Id;
                        if (z == null) motivo = m;
                        double cur; pesos.TryGetValue(id, out cur); pesos[id] = cur + a;
                        total += a;
                    }
                }
            }
        }
        if (total <= 1e-6) return RepartirPunto(e, zonas, tolM, out motivo);
        var res = new Dictionary<int, double>();
        foreach (var kv in pesos) res[kv.Key] = kv.Value / total;
        return res;
    }

    static Dictionary<int, double> RepartirCurva(Wall w, int n, List<ZonaPoly> zonas, double tolM, out string motivo)
    {
        motivo = "";
        var lc = w.Location as LocationCurve;
        if (lc == null || lc.Curve == null) return RepartirPunto(w, zonas, tolM, out motivo);
        var pesos = new Dictionary<int, double>();
        for (int i = 0; i < n; i++)
        {
            var pt = lc.Curve.Evaluate((i + 0.5) / n, true);
            string m;
            var z = BuscarZona(pt.X, pt.Y, w.Orientation, zonas, tolM, out m);
            int id = z == null ? -1 : z.Id;
            if (z == null) motivo = m;
            double cur; pesos.TryGetValue(id, out cur); pesos[id] = cur + 1.0 / n;
        }
        return pesos;
    }

    static Dictionary<int, double> RepartirPunto(Element e, List<ZonaPoly> zonas, double tolM, out string motivo)
    {
        motivo = "";
        XYZ pt = null;
        if (e.Location is LocationPoint lp) pt = lp.Point;
        else if (e.Location is LocationCurve lc && lc.Curve != null) pt = lc.Curve.Evaluate(0.5, true);
        else
        {
            var bb = e.get_BoundingBox(null);
            if (bb != null) pt = (bb.Min + bb.Max) / 2.0;
        }
        var res = new Dictionary<int, double>();
        if (pt == null) { motivo = "sin geometria"; res[-1] = 1.0; return res; }
        var z = BuscarZona(pt.X, pt.Y, null, zonas, tolM, out motivo);
        res[z == null ? -1 : z.Id] = 1.0;
        return res;
    }

    static ZonaPoly BuscarZona(double x, double y, XYZ normal, List<ZonaPoly> zonas, double tolM, out string motivo)
    {
        foreach (var z in zonas)
            if (PuntoEnZona(x, y, z)) { motivo = "interior"; return z; }

        if (normal != null)
        {
            foreach (double d in new[] { 0.15, 0.35, 0.7, 1.2 })
            {
                foreach (double s in new[] { 1.0, -1.0 })
                {
                    double nx = x + normal.X * d * s;
                    double ny = y + normal.Y * d * s;
                    foreach (var z in zonas)
                        if (PuntoEnZona(nx, ny, z)) { motivo = "normal"; return z; }
                }
            }
        }

        ZonaPoly mejor = null;
        double mejorD = double.MaxValue;
        foreach (var z in zonas)
        {
            double d = DistAZona(x, y, z);
            if (d < mejorD) { mejorD = d; mejor = z; }
        }
        double dm = mejorD * FT_TO_M;
        if (mejor != null && dm <= tolM) { motivo = "cercano"; return mejor; }
        motivo = "fuera de tolerancia (" + Math.Round(dm, 2) + " m)";
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

    static bool PuntoEnZona(double x, double y, ZonaPoly z)
    {
        if (x < z.MinX - 1e-4 || x > z.MaxX + 1e-4 || y < z.MinY - 1e-4 || y > z.MaxY + 1e-4) return false;
        if (!PuntoEnLoop(x, y, z.Loops[0])) return false;
        for (int i = 1; i < z.Loops.Count; i++)
            if (PuntoEnLoop(x, y, z.Loops[i])) return false;
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

    static double DistAZona(double x, double y, ZonaPoly z)
    {
        double mejor = double.MaxValue;
        foreach (var loop in z.Loops)
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

    // ---------- utilidades ----------

    static List<BuiltInCategory> ResolverCategorias(JArray arr)
    {
        var mapa = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            { "floors", BuiltInCategory.OST_Floors }, { "pisos", BuiltInCategory.OST_Floors }, { "suelos", BuiltInCategory.OST_Floors },
            { "walls", BuiltInCategory.OST_Walls }, { "muros", BuiltInCategory.OST_Walls },
            { "ceilings", BuiltInCategory.OST_Ceilings }, { "cielos", BuiltInCategory.OST_Ceilings }, { "techos", BuiltInCategory.OST_Ceilings },
            { "doors", BuiltInCategory.OST_Doors }, { "puertas", BuiltInCategory.OST_Doors },
            { "windows", BuiltInCategory.OST_Windows }, { "ventanas", BuiltInCategory.OST_Windows },
            { "furniture", BuiltInCategory.OST_Furniture }, { "mobiliario", BuiltInCategory.OST_Furniture },
            { "casework", BuiltInCategory.OST_Casework }, { "muebles", BuiltInCategory.OST_Casework },
            { "plumbingfixtures", BuiltInCategory.OST_PlumbingFixtures }, { "artefactos", BuiltInCategory.OST_PlumbingFixtures },
            { "lightingfixtures", BuiltInCategory.OST_LightingFixtures }, { "luminarias", BuiltInCategory.OST_LightingFixtures },
            { "genericmodel", BuiltInCategory.OST_GenericModel }, { "genericos", BuiltInCategory.OST_GenericModel },
            { "specialtyequipment", BuiltInCategory.OST_SpecialityEquipment }
        };
        if (arr == null || arr.Count == 0)
            return new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Walls };
        var res = new List<BuiltInCategory>();
        foreach (string s in arr.Select(x => (string)x))
        {
            if (string.IsNullOrEmpty(s)) continue;
            BuiltInCategory bic;
            if (mapa.TryGetValue(s.Trim(), out bic)) { res.Add(bic); continue; }
            if (Enum.TryParse(s.Trim(), true, out bic)) { res.Add(bic); continue; }
            throw new ArgumentException("Categoria desconocida: '" + s + "'.");
        }
        return res.Distinct().ToList();
    }

    static string NombreTipo(Document doc, Element e)
    {
        var t = doc.GetElement(e.GetTypeId()) as ElementType;
        return t == null ? e.Name : t.Name;
    }

    static double ParamDouble(Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        return (p != null && p.HasValue) ? p.AsDouble() : 0.0;
    }

    static string TextoParam(Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        if (p == null || !p.HasValue) return "";
        return p.StorageType == StorageType.String ? (p.AsString() ?? "") : (p.AsValueString() ?? "");
    }

    static string TextoParamNombre(Element e, string nombre)
    {
        var p = e.LookupParameter(nombre);
        if (p == null || !p.HasValue) return "";
        return p.StorageType == StorageType.String ? (p.AsString() ?? "") : (p.AsValueString() ?? "");
    }

    static void Acumular(Dictionary<string, Acumulador> d, string k, double cant, double area, double lon, double vol)
    {
        Acumulador a;
        if (!d.TryGetValue(k, out a)) { a = new Acumulador(); d[k] = a; }
        a.Cantidad += cant;
        a.AreaM2 += area;
        a.LongitudM += lon;
        a.VolumenM3 += vol;
    }

    static void AcumularNested(Dictionary<string, Dictionary<string, Acumulador>> d, string k1, string k2,
        double cant, double area, double lon, double vol)
    {
        Dictionary<string, Acumulador> sub;
        if (!d.TryGetValue(k1, out sub)) { sub = new Dictionary<string, Acumulador>(); d[k1] = sub; }
        Acumular(sub, k2, cant, area, lon, vol);
    }

    static object FormatearAcum(string clave, Acumulador a)
    {
        var p = clave.Split(new[] { " | " }, 2, StringSplitOptions.None);
        return new
        {
            categoria = p[0],
            tipo = p.Length > 1 ? p[1] : clave,
            cantidad = Math.Round(a.Cantidad, 2),
            area_m2 = Math.Round(a.AreaM2, 2),
            longitud_m = Math.Round(a.LongitudM, 2),
            volumen_m3 = Math.Round(a.VolumenM3, 3)
        };
    }

    static string CsvEsc(string s)
    {
        if (s == null) return "";
        if (s.Contains(";") || s.Contains("\"") || s.Contains("\n"))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
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
