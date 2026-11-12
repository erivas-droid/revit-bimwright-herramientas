// herramienta: leyenda-de-colores (bimwright-mcp)
// Lee los colores aplicados como sobrescritura en una vista (por bimwright color_elements,
// por archi_colores de pyRevit o a mano) y crea una vista de dibujo con la leyenda.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Acciones: leer (solo lectura) y crear (dry_run por defecto true). Ver README.md.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
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
            case "colorear":
                return Colorear(doc, app.ActiveUIDocument.ActiveView, data, dryRun);
            case "leer":
                return Leer(doc, app.ActiveUIDocument.ActiveView, data);
            case "crear":
                return Crear(doc, app.ActiveUIDocument.ActiveView, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use colorear, leer o crear.");
        }
    }

    // ---------- colorear ----------

    // Un color distinto por valor (bimwright color_elements repite una paleta de 10).
    // colores: { "valor": [r,g,b] } fija colores; el resto se reparte en el circulo de tonos.
    static Dictionary<string, object> Colorear(Document doc, View activa, JObject data, bool dryRun)
    {
        var vista = data["vista"] == null ? activa : doc.GetElement(new ElementId((int)data["vista"])) as View;
        string categoria = (string)data["categoria"];
        string parametro = (string)data["parametro"];
        if (string.IsNullOrEmpty(categoria) || string.IsNullOrEmpty(parametro)) throw new ArgumentException("Faltan 'categoria' y 'parametro'.");
        var fijos = new Dictionary<string, int[]>();
        var cj = data["colores"] as JObject;
        if (cj != null) foreach (var p in cj.Properties()) fijos[p.Name] = p.Value.Select(x => (int)x).ToArray();

        var porValor = new SortedDictionary<string, List<ElementId>>(StringComparer.CurrentCulture);
        foreach (var e in new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType())
        {
            if (e.Category == null || e.Category.Name != categoria) continue;
            string v = Valor(doc, e, parametro);
            List<ElementId> l;
            if (!porValor.TryGetValue(v, out l)) { l = new List<ElementId>(); porValor[v] = l; }
            l.Add(e.Id);
        }
        if (porValor.Count == 0) throw new InvalidOperationException("No hay elementos de '" + categoria + "' en la vista " + vista.Name + ".");

        var solido = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
            .First(f => f.GetFillPattern().IsSolidFill);
        var salida = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        using (var t = new Transaction(doc, "Leyenda de colores: colorear"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            int i = 0, n = porValor.Count(kv => !fijos.ContainsKey(kv.Key));
            foreach (var kv in porValor)
            {
                int[] rgb;
                if (!fijos.TryGetValue(kv.Key, out rgb)) { rgb = Tono(i, n); i++; }
                var c = new Color((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);
                var og = new OverrideGraphicSettings();
                og.SetSurfaceForegroundPatternId(solido.Id);
                og.SetSurfaceForegroundPatternColor(c);
                og.SetSurfaceForegroundPatternVisible(true);
                og.SetCutForegroundPatternId(solido.Id);
                og.SetCutForegroundPatternColor(c);
                og.SetCutForegroundPatternVisible(true);
                foreach (var id in kv.Value) vista.SetElementOverrides(id, og);
                salida.Add(new { valor = kv.Key, rgb, cantidad = kv.Value.Count });
            }
            if (dryRun) t.RollBack(); else t.Commit();
        }
        return new Dictionary<string, object>
        {
            { "vista", vista.Name }, { "vista_id", vista.Id.IntegerValue }, { "valores", salida.Count },
            { "entradas", salida }, { "dry_run", dryRun }, { "estado", dryRun ? "simulado y deshecho" : "Committed" },
            { "avisos_revit", fallas.Messages }
        };
    }

    // Colores bien separados: tono por razon aurea, alternando luminosidad para N grande.
    static int[] Tono(int i, int n)
    {
        double h = (i * 0.618033988749895) % 1.0;
        double s = 0.65, v = (i % 2 == 0) ? 0.92 : 0.72;
        int hi = (int)(h * 6); double f = h * 6 - hi;
        double p = v * (1 - s), q = v * (1 - f * s), u = v * (1 - (1 - f) * s);
        double r, g, b;
        switch (hi % 6)
        {
            case 0: r = v; g = u; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = u; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = u; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return new int[] { (int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255) };
    }

    class Entrada { public string Valor; public int[] Rgb; public int Cantidad; public int Ejemplo; }

    // ---------- leer ----------

    static Dictionary<string, object> Leer(Document doc, View activa, JObject data)
    {
        var vista = data["vista"] == null ? activa : doc.GetElement(new ElementId((int)data["vista"])) as View;
        if (vista == null) throw new ArgumentException("No existe la vista " + data["vista"]);
        var entradas = Entradas(doc, vista, (string)data["categoria"], (string)data["parametro"]);
        var porValor = entradas.GroupBy(e => e.Valor).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var porColor = entradas.GroupBy(e => string.Join(",", e.Rgb)).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        return new Dictionary<string, object>
        {
            { "vista", vista.Name }, { "vista_id", vista.Id.IntegerValue },
            { "parametro", (string)data["parametro"] }, { "categoria", (string)data["categoria"] },
            { "entradas", entradas.Select(e => new { valor = e.Valor, rgb = e.Rgb, cantidad = e.Cantidad, ejemplo = e.Ejemplo }).ToList() },
            { "valores_con_varios_colores", porValor },
            { "colores_con_varios_valores", porColor }
        };
    }

    static List<Entrada> Entradas(Document doc, View vista, string categoria, string parametro)
    {
        var grupos = new Dictionary<string, Entrada>();
        foreach (var e in new FilteredElementCollector(doc, vista.Id).WhereElementIsNotElementType())
        {
            if (e.Category == null) continue;
            if (!string.IsNullOrEmpty(categoria) && e.Category.Name != categoria) continue;
            var rgb = ColorDe(vista.GetElementOverrides(e.Id));
            if (rgb == null) continue;
            string valor = string.IsNullOrEmpty(parametro) ? "" : Valor(doc, e, parametro);
            string clave = valor + "|" + string.Join(",", rgb);
            Entrada en;
            if (!grupos.TryGetValue(clave, out en))
            {
                en = new Entrada { Valor = valor, Rgb = rgb, Ejemplo = e.Id.IntegerValue };
                grupos[clave] = en;
            }
            en.Cantidad++;
        }
        return grupos.Values.OrderBy(x => x.Valor, StringComparer.CurrentCulture).ThenBy(x => string.Join(",", x.Rgb)).ToList();
    }

    // Color de la sobrescritura: superficie, luego corte, luego linea de proyeccion.
    static int[] ColorDe(OverrideGraphicSettings o)
    {
        foreach (var c in new[] { o.SurfaceForegroundPatternColor, o.CutForegroundPatternColor, o.ProjectionLineColor })
            if (c != null && c.IsValid) return new int[] { c.Red, c.Green, c.Blue };
        return null;
    }

    static string Valor(Document doc, Element e, string parametro)
    {
        var p = e.LookupParameter(parametro);
        if (p == null)
        {
            var t = doc.GetElement(e.GetTypeId());
            if (t != null) p = t.LookupParameter(parametro);
        }
        if (p == null || !p.HasValue) return "(sin valor)";
        if (p.StorageType == StorageType.ElementId)
        {
            var r = doc.GetElement(p.AsElementId());
            if (r != null)
            {
                var fs = r as ElementType;
                return fs != null && !string.IsNullOrEmpty(fs.FamilyName) ? fs.FamilyName + ": " + fs.Name : r.Name;
            }
        }
        var s = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
        return string.IsNullOrEmpty(s) ? "(sin valor)" : s;
    }

    // ---------- crear ----------

    static Dictionary<string, object> Crear(Document doc, View activa, JObject data, bool dryRun)
    {
        // Entradas: explicitas [{texto|valor, rgb, cantidad?}] o leidas de una vista.
        List<Entrada> entradas;
        string origen;
        if (data["entradas"] != null)
        {
            entradas = data["entradas"].Select(x => new Entrada
            {
                Valor = (string)(x["texto"] ?? x["valor"]),
                Rgb = x["rgb"].Select(v => (int)v).ToArray(),
                Cantidad = x["cantidad"] == null ? 0 : (int)x["cantidad"]
            }).ToList();
            origen = "entradas";
        }
        else
        {
            var vista = data["vista"] == null ? activa : doc.GetElement(new ElementId((int)data["vista"])) as View;
            entradas = Entradas(doc, vista, (string)data["categoria"], (string)data["parametro"]);
            origen = "vista " + vista.Name;
        }
        if (entradas.Count == 0) throw new InvalidOperationException("No hay entradas: la vista no tiene elementos con color sobrescrito (o no coinciden con la categoria).");
        // Un valor con varios colores (p. ej. sobrescrituras viejas en elementos que el colector
        // de la vista incluye a veces): queda el color de la mayoria y se informa el resto.
        var descartados = new List<object>();
        entradas = entradas.GroupBy(e => e.Valor).Select(g =>
        {
            var orden = g.OrderByDescending(x => x.Cantidad).ToList();
            foreach (var x in orden.Skip(1)) descartados.Add(new { valor = x.Valor, rgb = x.Rgb, cantidad = x.Cantidad, ejemplo = x.Ejemplo });
            return orden[0];
        }).ToList();

        string titulo = (string)data["titulo"] ?? ("LEYENDA" + (data["parametro"] == null ? "" : " - " + ((string)data["parametro"]).ToUpper()));
        string nombre = (string)data["nombre_vista"] ?? titulo;
        bool cantidades = data["mostrar_cantidad"] != null && (bool)data["mostrar_cantidad"];
        double ancho = (data["ancho_mm"] == null ? 12.0 : (double)data["ancho_mm"]) / MM;
        double alto = (data["alto_mm"] == null ? 6.0 : (double)data["alto_mm"]) / MM;
        double sep = (data["separacion_mm"] == null ? 2.0 : (double)data["separacion_mm"]) / MM;

        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        var res = new Dictionary<string, object>();
        using (var t = new Transaction(doc, "Leyenda de colores: crear"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();

            // reemplazar: borra la vista de dibujo con el mismo nombre (para rehacer una leyenda).
            if (data["reemplazar"] != null && (bool)data["reemplazar"])
            {
                var vieja = new FilteredElementCollector(doc).OfClass(typeof(ViewDrafting)).Cast<View>().FirstOrDefault(x => x.Name == nombre);
                if (vieja != null) { res["reemplazada"] = vieja.Id.IntegerValue; doc.Delete(vieja.Id); }
            }
            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .First(x => x.ViewFamily == ViewFamily.Drafting);
            var v = ViewDrafting.Create(doc, vft.Id);
            v.Name = NombreLibre(doc, nombre);
            v.Scale = 1; // 1:1: los mm del dibujo son mm de papel

            var tipoTexto = BuscarTipo<TextNoteType>(doc, (string)data["tipo_texto"]) ?? doc.GetElement(doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)) as TextNoteType;
            var tipoRegion = BuscarTipo<FilledRegionType>(doc, (string)data["tipo_region"])
                ?? new FilteredElementCollector(doc).OfClass(typeof(FilledRegionType)).Cast<FilledRegionType>().First();
            var solido = new FilteredElementCollector(doc).OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                .First(f => f.GetFillPattern().IsSolidFill);

            var opTitulo = new TextNoteOptions(tipoTexto.Id) { HorizontalAlignment = HorizontalTextAlignment.Left, VerticalAlignment = VerticalTextAlignment.Bottom };
            TextNote.Create(doc, v.Id, new XYZ(0, sep, 0), titulo, opTitulo);

            var opTexto = new TextNoteOptions(tipoTexto.Id) { HorizontalAlignment = HorizontalTextAlignment.Left, VerticalAlignment = VerticalTextAlignment.Middle };
            int i = 0;
            foreach (var en in entradas)
            {
                double y = -(i * (alto + sep)) - alto;
                var p0 = new XYZ(0, y, 0); var p1 = new XYZ(ancho, y, 0); var p2 = new XYZ(ancho, y + alto, 0); var p3 = new XYZ(0, y + alto, 0);
                var lazo = CurveLoop.Create(new List<Curve> { Line.CreateBound(p0, p1), Line.CreateBound(p1, p2), Line.CreateBound(p2, p3), Line.CreateBound(p3, p0) });
                var fr = FilledRegion.Create(doc, tipoRegion.Id, v.Id, new List<CurveLoop> { lazo });
                var og = new OverrideGraphicSettings();
                og.SetSurfaceForegroundPatternId(solido.Id);
                og.SetSurfaceForegroundPatternColor(new Color((byte)en.Rgb[0], (byte)en.Rgb[1], (byte)en.Rgb[2]));
                og.SetSurfaceForegroundPatternVisible(true);
                og.SetSurfaceBackgroundPatternVisible(false);
                v.SetElementOverrides(fr.Id, og);
                string texto = string.IsNullOrEmpty(en.Valor) ? "(sin parametro)" : en.Valor;
                if (cantidades && en.Cantidad > 0) texto += " (" + en.Cantidad + ")";
                TextNote.Create(doc, v.Id, new XYZ(ancho + sep, y + alto / 2, 0), texto, opTexto);
                i++;
            }

            res["vista_leyenda"] = v.Id.IntegerValue;
            res["nombre"] = v.Name;
            res["origen"] = origen;
            res["entradas"] = entradas.Count;
            if (descartados.Count > 0) res["colores_descartados"] = descartados;
            res["tipo_texto"] = tipoTexto.Name;
            res["tipo_region"] = tipoRegion.Name;

            if (dryRun) t.RollBack(); else t.Commit();
            res["dry_run"] = dryRun;
            res["estado"] = dryRun ? "simulado y deshecho" : t.GetStatus().ToString();
            res["avisos_revit"] = fallas.Messages;
        }
        return res;
    }

    // ---------- utilidades ----------

    static T BuscarTipo<T>(Document doc, string nombre) where T : ElementType
    {
        if (string.IsNullOrEmpty(nombre)) return null;
        var t = new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().FirstOrDefault(x => x.Name == nombre);
        if (t == null) throw new ArgumentException("No existe el tipo '" + nombre + "'.");
        return t;
    }

    static string NombreLibre(Document doc, string nombre)
    {
        var usados = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(x => x.Name));
        if (!usados.Contains(nombre)) return nombre;
        for (int i = 2; ; i++) if (!usados.Contains(nombre + " " + i)) return nombre + " " + i;
    }
}
