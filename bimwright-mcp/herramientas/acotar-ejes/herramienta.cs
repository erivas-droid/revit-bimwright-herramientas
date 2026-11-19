// herramienta: acotar-ejes (bimwright-mcp)
// Acota automaticamente ejes paralelos (Grids) en una o varias plantas: genera cadenas de
// cotas parciales entre ejes adyacentes y cota total exterior por cada familia de ejes paralelos,
// evitando duplicar cotas existentes entre los mismos ejes.
// Acciones: listar (solo lectura, admite `documento`) y acotar (dry_run por defecto true).
// Ver README.md de esta carpeta.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public class McpDynamicScript
{
    const double MM = 304.8;

    class EjeInfo
    {
        public Grid Grid;
        public XYZ P0;
        public XYZ P1;
        public XYZ Dir;    // vector unitario normalizado (X >= 0; si X~0, Y >= 0)
        public XYZ Perp;   // perpendicular 2D (-Dir.Y, Dir.X, 0)
        public double CoordPerp; // posicion a lo largo de Perp
        public double MinPar;    // proyeccion minima a lo largo de Dir
        public double MaxPar;    // proyeccion maxima a lo largo de Dir
    }

    public static object Run(UIApplication app, JObject data)
    {
        if (data == null) data = new JObject();
        string accion = ((string)data["accion"] ?? "listar").ToLowerInvariant();
        var activo = app.ActiveUIDocument == null ? null : app.ActiveUIDocument.Document;
        var doc = ResolverDocumento(app, (string)data["documento"]);
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];

        switch (accion)
        {
            case "listar":
                return Listar(doc, app.ActiveUIDocument, data);
            case "acotar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("acotar solo trabaja sobre el documento activo.");
                return Acotar(doc, app.ActiveUIDocument, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use listar o acotar.");
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

    static List<ViewPlan> ResolverVistas(Document doc, UIDocument uidoc, JObject data)
    {
        var ids = data["vista_ids"] as JArray;
        if (ids != null && ids.Count > 0)
            return ids.Select(x => doc.GetElement(new ElementId((int)x)) as ViewPlan).Where(v => v != null && !v.IsTemplate).ToList();
        if (data["vista"] != null)
        {
            View v = data["vista"].Type == JTokenType.Integer
                ? doc.GetElement(new ElementId((int)data["vista"])) as View
                : new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                    .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, (string)data["vista"], StringComparison.OrdinalIgnoreCase));
            var vp = v as ViewPlan;
            if (vp == null) throw new ArgumentException("La vista indicada no es una planta valida.");
            return new List<ViewPlan> { vp };
        }
        if (uidoc != null && uidoc.Document.Equals(doc) && uidoc.ActiveView is ViewPlan av)
            return new List<ViewPlan> { av };
        throw new ArgumentException("La vista activa no es una planta; indique 'vista' o 'vista_ids'.");
    }

    static List<List<EjeInfo>> FamiliasDeEjes(Document doc, ViewPlan vista)
    {
        var ejes = new List<EjeInfo>();
        foreach (var g in new FilteredElementCollector(doc, vista.Id).OfClass(typeof(Grid)).Cast<Grid>())
        {
            if (g.IsCurved) continue;
            var curvas = g.GetCurvesInView(DatumExtentType.ViewSpecific, vista);
            var line = (curvas != null && curvas.Count > 0 ? curvas[0] : g.Curve) as Line;
            if (line == null) continue;
            var p0 = line.GetEndPoint(0);
            var p1 = line.GetEndPoint(1);
            var d = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0);
            double len = d.GetLength();
            if (len < 1e-6) continue;
            d = d / len;
            // Normalizar direccion canonica
            if (d.X < -1e-6 || (Math.Abs(d.X) <= 1e-6 && d.Y < 0)) d = -d;
            var perp = new XYZ(-d.Y, d.X, 0);
            double s0 = p0.X * d.X + p0.Y * d.Y;
            double s1 = p1.X * d.X + p1.Y * d.Y;
            ejes.Add(new EjeInfo
            {
                Grid = g, P0 = p0, P1 = p1, Dir = d, Perp = perp,
                CoordPerp = p0.X * perp.X + p0.Y * perp.Y,
                MinPar = Math.Min(s0, s1),
                MaxPar = Math.Max(s0, s1)
            });
        }

        // Agrupar ejes paralelos (tolerancia angular ~0.5 grados)
        var familias = new List<List<EjeInfo>>();
        foreach (var e in ejes)
        {
            var fam = familias.FirstOrDefault(f => Math.Abs(f[0].Dir.DotProduct(e.Dir)) > 0.99996);
            if (fam == null) { fam = new List<EjeInfo>(); familias.Add(fam); }
            fam.Add(e);
        }

        return familias
            .Select(f => f.OrderBy(e => e.CoordPerp).ToList())
            .OrderByDescending(f => f.Count)
            .ToList();
    }

    // ---------- listar ----------

    static object Listar(Document doc, UIDocument uidoc, JObject data)
    {
        var vistas = ResolverVistas(doc, uidoc, data);
        var salida = vistas.Select(v =>
        {
            var fams = FamiliasDeEjes(doc, v);
            return new
            {
                vista_id = v.Id.IntegerValue,
                vista = v.Name,
                escala = v.Scale,
                familias = fams.Select((f, idx) => new
                {
                    familia = idx + 1,
                    angulo_grados = Math.Round(Math.Atan2(f[0].Dir.Y, f[0].Dir.X) * 180.0 / Math.PI, 1),
                    cantidad = f.Count,
                    ejes = f.Select(e => e.Grid.Name).ToList()
                }).ToList()
            };
        }).ToList();
        return new { documento = doc.Title, vistas = salida };
    }

    // ---------- acotar ----------

    static object Acotar(Document doc, UIDocument uidoc, JObject data, bool dryRun)
    {
        var vistas = ResolverVistas(doc, uidoc, data);
        double margenFt = (data["margen_mm"] == null ? 1200.0 : (double)data["margen_mm"]) / MM;
        double sepFt = (data["separacion_mm"] == null ? 800.0 : (double)data["separacion_mm"]) / MM;
        bool parciales = data["parciales"] == null || (bool)data["parciales"];
        bool total = data["total"] == null || (bool)data["total"];
        bool ambosLados = data["ambos_lados"] != null && (bool)data["ambos_lados"];
        bool evitarDuplicadas = data["evitar_duplicadas"] == null || (bool)data["evitar_duplicadas"];
        double distMinFt = (data["distancia_min_mm"] == null ? 50.0 : (double)data["distancia_min_mm"]) / MM;

        DimensionType tipoCota = null;
        if (data["tipo_cota"] != null)
        {
            string tn = (string)data["tipo_cota"];
            tipoCota = new FilteredElementCollector(doc).OfClass(typeof(DimensionType)).Cast<DimensionType>()
                .FirstOrDefault(t => string.Equals(t.Name, tn, StringComparison.OrdinalIgnoreCase));
            if (tipoCota == null) throw new ArgumentException("No existe el tipo de cota '" + tn + "'.");
        }

        var resultadoVistas = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        int totalCreadas = 0;

        using (var tr = new Transaction(doc, "Acotar ejes"))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            foreach (var v in vistas)
            {
                var existentes = evitarDuplicadas ? ParesEjesAcotados(doc, v) : new HashSet<string>();
                var fams = FamiliasDeEjes(doc, v).Where(f => f.Count >= 2).ToList();
                int creadasVista = 0, omitidasVista = 0;
                var errores = new List<string>();
                double z = v.GenLevel == null ? 0.0 : v.GenLevel.ProjectElevation;

                foreach (var fam in fams)
                {
                    // Filtrar ejes colineales / demasiado juntos
                    var unicos = new List<EjeInfo>();
                    foreach (var e in fam)
                    {
                        if (unicos.Count == 0 || Math.Abs(e.CoordPerp - unicos[unicos.Count - 1].CoordPerp) > distMinFt)
                            unicos.Add(e);
                    }
                    if (unicos.Count < 2) continue;

                    double minPar = unicos.Min(e => e.MinPar);
                    double maxPar = unicos.Max(e => e.MaxPar);
                    var dir = unicos[0].Dir;
                    var perp = unicos[0].Perp;
                    double c0 = unicos[0].CoordPerp;
                    double c1 = unicos[unicos.Count - 1].CoordPerp;

                    var lados = ambosLados ? new[] { 1.0, -1.0 } : new[] { 1.0 };
                    foreach (double signo in lados)
                    {
                        double basePar = signo > 0 ? (maxPar + margenFt) : (minPar - margenFt);

                        // 1. Cadena parcial
                        if (parciales && unicos.Count >= 2)
                        {
                            string claveParc = ClaveCadena(unicos.Select(e => e.Grid.Id.IntegerValue));
                            if (evitarDuplicadas && existentes.Contains(claveParc) && !ambosLados)
                            {
                                omitidasVista++;
                            }
                            else
                            {
                                var refs = new ReferenceArray();
                                foreach (var e in unicos) refs.Append(new Reference(e.Grid));
                                var a = perp * c0 + dir * basePar + XYZ.BasisZ * z;
                                var b = perp * c1 + dir * basePar + XYZ.BasisZ * z;
                                try
                                {
                                    var ln = Line.CreateBound(a, b);
                                    var dim = tipoCota == null
                                        ? doc.Create.NewDimension(v, ln, refs)
                                        : doc.Create.NewDimension(v, ln, refs, tipoCota);
                                    if (dim != null) { creadasVista++; totalCreadas++; }
                                }
                                catch (Exception ex) { errores.Add("parcial: " + ex.Message); }
                            }
                        }

                        // 2. Cota total (solo si hay >= 3 ejes; con 2 ejes la parcial ya es total)
                        if (total && unicos.Count >= 3)
                        {
                            double totPar = signo > 0 ? (basePar + sepFt) : (basePar - sepFt);
                            string claveTot = ClaveCadena(new[] { unicos[0].Grid.Id.IntegerValue, unicos[unicos.Count - 1].Grid.Id.IntegerValue });
                            if (evitarDuplicadas && existentes.Contains(claveTot) && !ambosLados)
                            {
                                omitidasVista++;
                            }
                            else
                            {
                                var refs = new ReferenceArray();
                                refs.Append(new Reference(unicos[0].Grid));
                                refs.Append(new Reference(unicos[unicos.Count - 1].Grid));
                                var a = perp * c0 + dir * totPar + XYZ.BasisZ * z;
                                var b = perp * c1 + dir * totPar + XYZ.BasisZ * z;
                                try
                                {
                                    var ln = Line.CreateBound(a, b);
                                    var dim = tipoCota == null
                                        ? doc.Create.NewDimension(v, ln, refs)
                                        : doc.Create.NewDimension(v, ln, refs, tipoCota);
                                    if (dim != null) { creadasVista++; totalCreadas++; }
                                }
                                catch (Exception ex) { errores.Add("total: " + ex.Message); }
                            }
                        }
                    }
                }

                resultadoVistas.Add(new
                {
                    vista_id = v.Id.IntegerValue,
                    vista = v.Name,
                    familias_acotadas = fams.Count,
                    cotas_creadas = creadasVista,
                    omitidas_existentes = omitidasVista,
                    errores
                });
            }

            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new
        {
            accion = "acotar",
            dry_run = dryRun,
            transaccion = estado.ToString(),
            vistas_procesadas = vistas.Count,
            cotas_creadas = totalCreadas,
            detalle = resultadoVistas,
            avisos_revit = fallas.Messages
        };
    }

    static HashSet<string> ParesEjesAcotados(Document doc, ViewPlan vista)
    {
        var set = new HashSet<string>();
        foreach (var d in new FilteredElementCollector(doc, vista.Id).OfClass(typeof(Dimension)).Cast<Dimension>())
        {
            var ids = new List<int>();
            foreach (Reference r in d.References)
                if (doc.GetElement(r.ElementId) is Grid) ids.Add(r.ElementId.IntegerValue);
            if (ids.Count >= 2) set.Add(ClaveCadena(ids));
        }
        return set;
    }

    static string ClaveCadena(IEnumerable<int> ids)
    {
        return string.Join("-", ids.Distinct().OrderBy(x => x));
    }
}
