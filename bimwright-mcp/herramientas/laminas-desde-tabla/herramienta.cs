// herramienta: laminas-desde-tabla (bimwright-mcp)
// Exporta el listado maestro de laminas y parametros de lamina/cajetin a JSON o CSV,
// y sincroniza de vuelta: crea laminas faltantes (con cajetin o placeholder) y actualiza
// nombres y parametros en las existentes.
// Acciones: exportar (solo lectura, admite `documento`) y sincronizar (dry_run por defecto true).
// Ver README.md de esta carpeta.
using System;
using System.Collections.Generic;
using System.IO;
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
        string accion = ((string)data["accion"] ?? "exportar").ToLowerInvariant();
        var activo = app.ActiveUIDocument == null ? null : app.ActiveUIDocument.Document;
        var doc = ResolverDocumento(app, (string)data["documento"]);
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];

        switch (accion)
        {
            case "exportar":
                return Exportar(doc, data);
            case "sincronizar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("sincronizar solo trabaja sobre el documento activo.");
                return Sincronizar(doc, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use exportar o sincronizar.");
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

    // ---------- exportar ----------

    static object Exportar(Document doc, JObject data)
    {
        Regex rx = data["filtro_numero"] == null ? null : new Regex((string)data["filtro_numero"], RegexOptions.IgnoreCase);
        bool incluirPh = data["incluir_placeholders"] == null || (bool)data["incluir_placeholders"];
        var paramsLam = (data["parametros"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList()
            ?? new List<string>();
        var paramsCaj = (data["parametros_cajetin"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList()
            ?? new List<string>();

        var cajetinesPorHoja = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
            .GroupBy(e => e.OwnerViewId.IntegerValue)
            .ToDictionary(g => g.Key, g => g.First());

        var hojas = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => incluirPh || !s.IsPlaceholder)
            .Where(s => rx == null || rx.IsMatch(s.SheetNumber ?? ""))
            .OrderBy(s => s.SheetNumber, new Natural())
            .ToList();

        var filas = new List<Dictionary<string, object>>();
        foreach (var s in hojas)
        {
            Element tb;
            cajetinesPorHoja.TryGetValue(s.Id.IntegerValue, out tb);
            var tipoTb = tb == null ? null : doc.GetElement(tb.GetTypeId()) as ElementType;
            string nombreCaj = tipoTb == null ? "" : (tipoTb.FamilyName + ": " + tipoTb.Name);

            var pLam = new Dictionary<string, string>();
            foreach (var n in paramsLam) pLam[n] = Texto(s.LookupParameter(n));

            var pCaj = new Dictionary<string, string>();
            if (tb != null)
                foreach (var n in paramsCaj) pCaj[n] = Texto(tb.LookupParameter(n));

            filas.Add(new Dictionary<string, object>
            {
                { "id", s.Id.IntegerValue },
                { "numero", s.SheetNumber ?? "" },
                { "nombre", s.Name ?? "" },
                { "placeholder", s.IsPlaceholder },
                { "cajetin", nombreCaj },
                { "parametros", pLam },
                { "parametros_cajetin", pCaj }
            });
        }

        string csvEscrito = null;
        if (data["exportar_csv"] != null)
        {
            string ruta = Environment.ExpandEnvironmentVariables((string)data["exportar_csv"]);
            string dir = Path.GetDirectoryName(ruta);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var cols = new List<string> { "Numero", "Nombre", "Placeholder", "Cajetin" };
            cols.AddRange(paramsLam);
            cols.AddRange(paramsCaj.Select(x => "TB:" + x));
            sb.AppendLine(string.Join(";", cols.Select(CsvEsc)));
            foreach (var f in filas)
            {
                var pL = (Dictionary<string, string>)f["parametros"];
                var pC = (Dictionary<string, string>)f["parametros_cajetin"];
                var vals = new List<string>
                {
                    (string)f["numero"], (string)f["nombre"],
                    ((bool)f["placeholder"]) ? "SI" : "NO", (string)f["cajetin"]
                };
                foreach (var n in paramsLam) vals.Add(pL.ContainsKey(n) ? pL[n] : "");
                foreach (var n in paramsCaj) vals.Add(pC.ContainsKey(n) ? pC[n] : "");
                sb.AppendLine(string.Join(";", vals.Select(CsvEsc)));
            }
            File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
            csvEscrito = ruta;
        }

        var res = new Dictionary<string, object>
        {
            { "documento", doc.Title },
            { "laminas", filas.Count },
            { "lista", filas }
        };
        if (csvEscrito != null) res["csv_exportado"] = csvEscrito;
        return res;
    }

    // ---------- sincronizar ----------

    static object Sincronizar(Document doc, JObject data, bool dryRun)
    {
        var specs = CargarSpecs(data);
        if (specs.Count == 0) throw new ArgumentException("Indique 'laminas' (lista JSON) o 'archivo_csv'.");

        bool crearFaltantes = data["crear_faltantes"] == null || (bool)data["crear_faltantes"];
        bool actualizarExistentes = data["actualizar_existentes"] == null || (bool)data["actualizar_existentes"];
        string cajetinDef = (string)data["cajetin_por_defecto"];

        var tiposCaj = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Cast<FamilySymbol>().ToList();
        var hojasPorNum = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .GroupBy(s => s.SheetNumber ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var cajetinesPorHoja = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType()
            .GroupBy(e => e.OwnerViewId.IntegerValue)
            .ToDictionary(g => g.Key, g => g.First());

        var creadas = new List<object>();
        var actualizadas = new List<object>();
        var sinCambio = new List<string>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;

        using (var tr = new Transaction(doc, "Sincronizar laminas desde tabla"))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();

            foreach (var sp in specs)
            {
                string num = ((string)sp["numero"] ?? "").Trim();
                if (string.IsNullOrEmpty(num)) { errores.Add(new { error = "fila sin numero" }); continue; }
                string nom = (string)sp["nombre"];
                bool ph = sp["placeholder"] != null && (bool)sp["placeholder"];
                string cajNombre = (string)sp["cajetin"] ?? cajetinDef;

                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        ViewSheet sheet;
                        bool nueva = !hojasPorNum.TryGetValue(num, out sheet);
                        if (nueva)
                        {
                            if (!crearFaltantes) { st.RollBack(); continue; }
                            if (ph) sheet = ViewSheet.CreatePlaceholder(doc);
                            else
                            {
                                var tbType = BuscarCajetin(tiposCaj, cajNombre);
                                sheet = ViewSheet.Create(doc, tbType == null ? ElementId.InvalidElementId : tbType.Id);
                            }
                            sheet.SheetNumber = num;
                            if (!string.IsNullOrEmpty(nom)) sheet.Name = nom;
                            hojasPorNum[num] = sheet;
                            doc.Regenerate();
                        }
                        else if (!actualizarExistentes)
                        {
                            st.RollBack();
                            continue;
                        }

                        var cambios = new List<string>();
                        if (!nueva && !string.IsNullOrEmpty(nom) && sheet.Name != nom)
                        {
                            cambios.Add("Nombre: '" + sheet.Name + "' -> '" + nom + "'");
                            sheet.Name = nom;
                        }

                        var pLam = sp["parametros"] as JObject;
                        if (pLam != null)
                        {
                            foreach (var p in pLam.Properties())
                                if (EscribirParam(sheet.LookupParameter(p.Name), (string)p.Value))
                                    cambios.Add(p.Name + " = " + (string)p.Value);
                        }

                        var pCaj = sp["parametros_cajetin"] as JObject;
                        if (pCaj != null && !sheet.IsPlaceholder)
                        {
                            Element tb;
                            if (!cajetinesPorHoja.TryGetValue(sheet.Id.IntegerValue, out tb))
                            {
                                tb = new FilteredElementCollector(doc, sheet.Id)
                                    .OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().FirstOrDefault();
                                if (tb != null) cajetinesPorHoja[sheet.Id.IntegerValue] = tb;
                            }
                            if (tb != null)
                            {
                                foreach (var p in pCaj.Properties())
                                    if (EscribirParam(tb.LookupParameter(p.Name), (string)p.Value))
                                        cambios.Add("TB:" + p.Name + " = " + (string)p.Value);
                            }
                        }

                        st.Commit();
                        if (nueva) creadas.Add(new { id = sheet.Id.IntegerValue, numero = num, nombre = sheet.Name, placeholder = sheet.IsPlaceholder, cambios });
                        else if (cambios.Count > 0) actualizadas.Add(new { id = sheet.Id.IntegerValue, numero = num, cambios });
                        else sinCambio.Add(num);
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        errores.Add(new { numero = num, error = ex.Message });
                    }
                }
            }

            estado = dryRun ? tr.RollBack() : tr.Commit();
        }

        return new
        {
            accion = "sincronizar",
            dry_run = dryRun,
            transaccion = estado.ToString(),
            procesadas = specs.Count,
            creadas = creadas.Count,
            actualizadas = actualizadas.Count,
            sin_cambio = sinCambio.Count,
            errores = errores.Count,
            detalle_creadas = creadas.Take(50).ToList(),
            detalle_actualizadas = actualizadas.Take(50).ToList(),
            detalle_errores = errores.Take(20).ToList(),
            avisos_revit = fallas.Messages
        };
    }

    static List<JObject> CargarSpecs(JObject data)
    {
        if (data["laminas"] is JArray arr)
            return arr.OfType<JObject>().ToList();
        if (data["archivo_csv"] != null)
        {
            string ruta = Environment.ExpandEnvironmentVariables((string)data["archivo_csv"]);
            if (!File.Exists(ruta)) throw new ArgumentException("No existe el archivo CSV: " + ruta);
            var lineas = File.ReadAllLines(ruta, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lineas.Count < 2) return new List<JObject>();
            char sep = lineas[0].Contains(';') ? ';' : ',';
            var headers = SplitCsv(lineas[0], sep);
            var list = new List<JObject>();
            for (int i = 1; i < lineas.Count; i++)
            {
                var vals = SplitCsv(lineas[i], sep);
                var obj = new JObject();
                var pLam = new JObject();
                var pCaj = new JObject();
                for (int c = 0; c < Math.Min(headers.Count, vals.Count); c++)
                {
                    string h = headers[c].Trim();
                    string v = vals[c].Trim();
                    if (string.Equals(h, "Numero", StringComparison.OrdinalIgnoreCase)) obj["numero"] = v;
                    else if (string.Equals(h, "Nombre", StringComparison.OrdinalIgnoreCase)) obj["nombre"] = v;
                    else if (string.Equals(h, "Cajetin", StringComparison.OrdinalIgnoreCase)) obj["cajetin"] = v;
                    else if (string.Equals(h, "Placeholder", StringComparison.OrdinalIgnoreCase))
                        obj["placeholder"] = v.Equals("SI", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (h.StartsWith("TB:", StringComparison.OrdinalIgnoreCase)) pCaj[h.Substring(3)] = v;
                    else pLam[h] = v;
                }
                if (pLam.Properties().Any()) obj["parametros"] = pLam;
                if (pCaj.Properties().Any()) obj["parametros_cajetin"] = pCaj;
                list.Add(obj);
            }
            return list;
        }
        return new List<JObject>();
    }

    static List<string> SplitCsv(string line, char sep)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        bool inQ = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else inQ = !inQ;
            }
            else if (ch == sep && !inQ) { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(ch);
        }
        res.Add(cur.ToString());
        return res;
    }

    static FamilySymbol BuscarCajetin(List<FamilySymbol> tipos, string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return tipos.FirstOrDefault();
        return tipos.FirstOrDefault(t => string.Equals(t.FamilyName + ": " + t.Name, nombre, StringComparison.OrdinalIgnoreCase))
            ?? tipos.FirstOrDefault(t => string.Equals(t.Name, nombre, StringComparison.OrdinalIgnoreCase))
            ?? tipos.FirstOrDefault(t => string.Equals(t.FamilyName, nombre, StringComparison.OrdinalIgnoreCase));
    }

    static bool EscribirParam(Parameter p, string valor)
    {
        if (p == null || p.IsReadOnly) return false;
        string antes = Texto(p);
        if (antes == (valor ?? "")) return false;
        if (p.StorageType == StorageType.String) { p.Set(valor ?? ""); return true; }
        return p.SetValueString(valor ?? "");
    }

    static string Texto(Parameter p)
    {
        if (p == null || !p.HasValue) return "";
        return p.StorageType == StorageType.String ? (p.AsString() ?? "") : (p.AsValueString() ?? "");
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
