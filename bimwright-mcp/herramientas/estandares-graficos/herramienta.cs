// herramienta: estandares-graficos (bimwright-mcp)
// Estilos de linea (subcategorias de Lineas) y patrones de linea.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Acciones: capturar, auditar (solo lectura; aceptan `documento` = titulo de otro modelo abierto)
//           y aplicar (solo documento activo; dry_run por defecto true).
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
    const double MM = 304.8;
    const string SOLIDO = "SOLIDO";

    public static object Run(UIApplication app, JObject data)
    {
        string accion = (string)data["accion"] ?? "";
        var activo = app.ActiveUIDocument.Document;
        var doc = activo;
        string titulo = (string)data["documento"];
        if (!string.IsNullOrEmpty(titulo))
        {
            doc = app.Application.Documents.Cast<Document>().FirstOrDefault(d => d.Title == titulo && !d.IsLinked);
            if (doc == null) throw new ArgumentException("No hay un modelo abierto con titulo '" + titulo + "'.");
        }
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        // El estandar puede venir en data.estandar o leerse de un archivo JSON
        // (estandar_archivo admite variables de entorno, p. ej. %OneDriveCommercial%).
        if (data["estandar"] == null && data["estandar_archivo"] != null)
        {
            string ruta = Environment.ExpandEnvironmentVariables((string)data["estandar_archivo"]);
            data["estandar"] = JObject.Parse(System.IO.File.ReadAllText(ruta));
        }
        // Un plan de migracion (renombrar, renombrar_reglas, renombrar_patrones, reasignar, borrar)
        // puede venir de un archivo; lo que venga en data tiene prioridad.
        if (data["plan_archivo"] != null)
        {
            string ruta = Environment.ExpandEnvironmentVariables((string)data["plan_archivo"]);
            foreach (var p in JObject.Parse(System.IO.File.ReadAllText(ruta)).Properties())
                if (data[p.Name] == null && !p.Name.StartsWith("_")) data[p.Name] = p.Value;
        }

        switch (accion)
        {
            case "capturar":
                return Capturar(doc, (string)data["filtro"] ?? ".*");
            case "auditar":
                return Auditar(doc, data);
            case "aplicar":
                if (!doc.Equals(activo)) throw new InvalidOperationException("aplicar solo trabaja sobre el documento activo.");
                return Aplicar(doc, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use capturar, auditar o aplicar.");
        }
    }

    // ---------- capturar ----------

    static object Capturar(Document doc, string filtro)
    {
        var re = new Regex(filtro);
        var usos = Usos(doc);
        var estilos = Estilos(doc).Where(c => re.IsMatch(c.Name)).OrderBy(c => c.Name).ToList();
        var nombresPatron = new HashSet<string>();
        var salidaEstilos = estilos.Select(c =>
        {
            string pat = NombrePatron(doc, c);
            nombresPatron.Add(pat);
            return new
            {
                nombre = c.Name,
                grosor = c.GetLineWeight(GraphicsStyleType.Projection),
                rgb = Rgb(c.LineColor),
                patron = pat,
                usos = Uso(usos, c)
            };
        }).ToList();
        var patrones = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
            .Where(p => nombresPatron.Contains(p.Name) || re.IsMatch(p.Name))
            .OrderBy(p => p.Name)
            .Select(p => new { nombre = p.Name, segmentos = Segmentos(p.GetLinePattern()) }).ToList();
        return new
        {
            documento = doc.Title,
            filtro,
            estilos = salidaEstilos,
            patrones,
            total_estilos_modelo = Estilos(doc).Count(),
            sin_uso = salidaEstilos.Count(e => e.usos == 0)
        };
    }

    // ---------- auditar ----------

    static object Auditar(Document doc, JObject data)
    {
        var est = data["estandar"] as JObject;
        if (est == null) throw new ArgumentException("Falta 'estandar' o 'estandar_archivo' (ver ejemplo-estandar.json).");
        string filtro = (string)est["filtro"] ?? ".*";
        var reFiltro = new Regex(filtro);
        var reNombre = est["regla_nombre"] == null ? null : new Regex((string)est["regla_nombre"]);
        var usos = Usos(doc);
        var porNombre = Estilos(doc).ToDictionary(c => c.Name, c => c);
        var patrones = new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
            .GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());

        var faltan = new List<object>();
        var difieren = new List<object>();
        int cumplen = 0;
        var enEstandar = new HashSet<string>();
        foreach (JObject e in est["estilos"] ?? new JArray())
        {
            string n = (string)e["nombre"];
            enEstandar.Add(n);
            Category c;
            if (!porNombre.TryGetValue(n, out c)) { faltan.Add(n); continue; }
            var dif = Diferencias(doc, c, e);
            if (dif.Count == 0) cumplen++;
            else difieren.Add(new { nombre = n, usos = Uso(usos, c), diferencias = dif });
        }
        var sobran = porNombre.Values.Where(c => reFiltro.IsMatch(c.Name) && !enEstandar.Contains(c.Name))
            .OrderByDescending(c => Uso(usos, c))
            .Select(c => new { nombre = c.Name, usos = Uso(usos, c), nombre_valido = reNombre == null ? (bool?)null : reNombre.IsMatch(c.Name) }).ToList();

        var patFaltan = new List<string>();
        var patDifieren = new List<object>();
        foreach (JObject p in est["patrones"] ?? new JArray())
        {
            string n = (string)p["nombre"];
            LinePatternElement lpe;
            if (!patrones.TryGetValue(n, out lpe)) { patFaltan.Add(n); continue; }
            var actual = Segmentos(lpe.GetLinePattern());
            if (!MismosSegmentos(actual, (JArray)p["segmentos"])) patDifieren.Add(new { nombre = n, actual });
        }

        var res = new Dictionary<string, object>
        {
            { "documento", doc.Title }, { "estilos_estandar", enEstandar.Count }, { "cumplen", cumplen },
            { "faltan", faltan }, { "difieren", difieren }, { "sobran", sobran },
            { "patrones_faltan", patFaltan }, { "patrones_difieren", patDifieren }
        };

        // Vista previa del plan (solo lectura): que pasaria con cada estilo que sobra.
        if (data["renombrar"] != null || data["renombrar_reglas"] != null || data["reasignar"] != null || data["borrar"] != null)
        {
            var mapa = MapaRenombres(doc, data);
            var reas = Mapa(data["reasignar"]).ToDictionary(k => k.Key, k => k.Value);
            var bor = new HashSet<string>((data["borrar"] ?? new JArray()).Select(x => (string)x));
            var previa = new List<object>();
            var sinPlan = new List<object>();
            foreach (var c in porNombre.Values.Where(c => reFiltro.IsMatch(c.Name)).OrderBy(c => c.Name))
            {
                string destino;
                int u = Uso(usos, c);
                if (mapa.TryGetValue(c.Name, out destino)) previa.Add(new { estilo = c.Name, usos = u, accion = "renombrar", a = destino, nombre_valido = reNombre == null || reNombre.IsMatch(destino) });
                else if (reas.TryGetValue(c.Name, out destino)) previa.Add(new { estilo = c.Name, usos = u, accion = "reasignar", a = destino, nombre_valido = reNombre == null || reNombre.IsMatch(destino) });
                else if (bor.Contains(c.Name)) previa.Add(new { estilo = c.Name, usos = u, accion = u == 0 ? "borrar" : "borrar (omitido: tiene lineas)", a = (string)null, nombre_valido = (bool?)null });
                else if (!enEstandar.Contains(c.Name)) sinPlan.Add(new { estilo = c.Name, usos = u });
            }
            res["plan_previo"] = previa;
            res["plan_sin_cubrir"] = sinPlan;
        }
        return res;
    }

    // renombrar explicito + renombrar_reglas ([{buscar, reemplazo}], regex, en orden) sobre los
    // estilos existentes que calzan con el filtro (de data o del estandar). Lo explicito manda.
    static Dictionary<string, string> MapaRenombres(Document doc, JObject data)
    {
        var est = data["estandar"] as JObject;
        string filtro = (string)data["filtro"] ?? (est == null ? null : (string)est["filtro"]);
        var ra = data["renombrar_reglas"] as JArray;
        if (filtro == null && ra != null && ra.Count > 0)
            throw new ArgumentException("renombrar_reglas necesita 'filtro' (en data o en el estandar), para no tocar los estilos de Revit.");
        var reFiltro = new Regex(filtro ?? "^$");
        var mapa = Mapa(data["renombrar"]).ToDictionary(k => k.Key, k => k.Value);
        var reglas = (data["renombrar_reglas"] as JArray ?? new JArray()).Cast<JObject>()
            .Select(r => Tuple.Create(new Regex((string)r["buscar"]), (string)r["reemplazo"] ?? "")).ToList();
        if (reglas.Count > 0)
            foreach (var c in Estilos(doc).Where(c => reFiltro.IsMatch(c.Name)))
            {
                if (mapa.ContainsKey(c.Name)) continue;
                string n = c.Name;
                foreach (var r in reglas) n = r.Item1.Replace(n, r.Item2);
                if (n != c.Name) mapa[c.Name] = n;
            }
        return mapa;
    }

    // ---------- aplicar ----------

    static object Aplicar(Document doc, JObject data, bool dryRun)
    {
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        var log = new Dictionary<string, object>();
        indice = null;
        enGrupo = 0;
        using (var t = new Transaction(doc, "Estandares graficos: aplicar"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();

            var lineas = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
            var est = data["estandar"] as JObject;

            // 1. Renombrar patrones: { "viejo": "nuevo" }. Va primero, para que el paso 2
            //    cree o corrija despues los nombres del estandar.
            var patRen = new List<object>();
            foreach (var kv in Mapa(data["renombrar_patrones"]))
            {
                var lpe = LinePatternElement.GetLinePatternElementByName(doc, kv.Key);
                if (lpe == null) { patRen.Add(new { viejo = kv.Key, error = "no existe" }); continue; }
                try { lpe.Name = kv.Value; patRen.Add(new { viejo = kv.Key, nuevo = kv.Value }); }
                catch (Exception ex) { patRen.Add(new { viejo = kv.Key, error = ex.Message }); }
            }
            log["patrones_renombrados"] = patRen;

            // 2. Patrones del estandar: crear o corregir.
            var patHechos = new List<object>();
            if (est != null)
            {
                foreach (JObject p in est["patrones"] ?? new JArray())
                {
                    string n = (string)p["nombre"];
                    var lpe = LinePatternElement.GetLinePatternElementByName(doc, n);
                    var segs = (JArray)p["segmentos"];
                    if (lpe == null)
                    {
                        var lp = new LinePattern(n);
                        lp.SetSegments(ASegmentos(segs));
                        LinePatternElement.Create(doc, lp);
                        patHechos.Add(new { nombre = n, accion = "creado" });
                    }
                    else if (!MismosSegmentos(Segmentos(lpe.GetLinePattern()), segs))
                    {
                        var lp = lpe.GetLinePattern();
                        lp.SetSegments(ASegmentos(segs));
                        lpe.SetLinePattern(lp);
                        patHechos.Add(new { nombre = n, accion = "corregido" });
                    }
                }
            }
            log["patrones"] = patHechos;

            // 3. Renombrar estilos: { "viejo": "nuevo" } y renombrar_reglas (regex). La API no
            //    renombra subcategorias: se crea el nuevo con las propiedades del viejo (o se usa el
            //    que ya existe), se pasan las lineas y se borra el viejo. El paso 5 corrige despues
            //    grosor, color y patron segun el estandar.
            // Los estilos viejos se borran todos al final del paso y luego se regenera: la lista de
            // subcategorias sigue mostrando los borrados (y no muestra los nuevos) hasta regenerar,
            // y leerlos da "Category is unexpectedly NULL".
            var aBorrar = new List<ElementId>();
            var creadosAhora = new Dictionary<string, Category>();
            var renombrados = new List<object>();
            foreach (var kv in MapaRenombres(doc, data))
            {
                var viejo = BuscarEstilo(doc, kv.Key);
                if (viejo == null) { renombrados.Add(new { viejo = kv.Key, error = "no existe" }); continue; }
                // varios viejos pueden ir al mismo nuevo (ej. ...-ROJA y ...-ROJO -> ...-RO)
                Category nuevo;
                if (!creadosAhora.TryGetValue(kv.Value, out nuevo)) nuevo = BuscarEstilo(doc, kv.Value);
                bool creado = false;
                if (nuevo == null)
                {
                    nuevo = doc.Settings.Categories.NewSubcategory(lineas, kv.Value);
                    nuevo.SetLineWeight(viejo.GetLineWeight(GraphicsStyleType.Projection) ?? 1, GraphicsStyleType.Projection);
                    nuevo.LineColor = viejo.LineColor;
                    nuevo.SetLinePatternId(viejo.GetLinePatternId(GraphicsStyleType.Projection), GraphicsStyleType.Projection);
                    creadosAhora[kv.Value] = nuevo;
                    creado = true;
                }
                var r = Reasignar(doc, viejo, nuevo);
                bool borrado = r.Item2 == 0;
                if (borrado) aBorrar.Add(viejo.Id);
                // detalle solo de los que movieron lineas o no se pudieron borrar
                if (r.Item1 > 0 || !borrado)
                    renombrados.Add(new { viejo = kv.Key, nuevo = kv.Value, nuevo_creado = creado, lineas_pasadas = r.Item1, lineas_con_error = r.Item2, viejo_borrado = borrado });
            }
            if (aBorrar.Count > 0) doc.Delete(aBorrar);
            doc.Regenerate();
            log["renombrados_y_borrados"] = aBorrar.Count;
            log["renombrados"] = renombrados;

            // 4. Reasignar: { "origen": "destino" } (destino debe existir). borrar_reasignados: true por defecto.
            bool borrarReasignados = data["borrar_reasignados"] == null || (bool)data["borrar_reasignados"];
            var reasignados = new List<object>();
            aBorrar.Clear();
            foreach (var kv in Mapa(data["reasignar"]))
            {
                var origen = BuscarEstilo(doc, kv.Key);
                var destino = BuscarEstilo(doc, kv.Value);
                if (origen == null || destino == null) { reasignados.Add(new { origen = kv.Key, destino = kv.Value, error = origen == null ? "no existe el origen" : "no existe el destino" }); continue; }
                var r = Reasignar(doc, origen, destino);
                bool borrado = borrarReasignados && r.Item2 == 0;
                if (borrado) aBorrar.Add(origen.Id);
                reasignados.Add(new { origen = kv.Key, destino = kv.Value, lineas_pasadas = r.Item1, lineas_con_error = r.Item2, origen_borrado = borrado });
            }
            if (aBorrar.Count > 0) doc.Delete(aBorrar);
            doc.Regenerate();
            log["reasignados"] = reasignados;

            // 5. Estilos del estandar: corregir grosor, color y patron de los que existen (incluidos
            //    los recien creados al renombrar). Los que faltan se crean solo con crear_faltantes.
            bool crearFaltantes = data["crear_faltantes"] != null && (bool)data["crear_faltantes"];
            var estHechos = new List<object>();
            if (est != null)
            {
                foreach (JObject e in est["estilos"] ?? new JArray())
                {
                    string n = (string)e["nombre"];
                    var c = BuscarEstilo(doc, n);
                    bool nuevo = c == null;
                    if (nuevo && !crearFaltantes) continue;
                    if (nuevo) c = doc.Settings.Categories.NewSubcategory(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines), n);
                    var dif = nuevo ? new List<string> { "creado" } : Diferencias(doc, c, e);
                    if (dif.Count == 0) continue;
                    Fijar(doc, c, e);
                    estHechos.Add(new { nombre = n, cambios = dif });
                }
            }
            log["estilos"] = estHechos;

            // 6. Borrar: lista de nombres. Solo sin uso, salvo forzar_borrado.
            bool forzar = data["forzar_borrado"] != null && (bool)data["forzar_borrado"];
            var usos = Indice(doc).ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            var borrados = new List<object>();
            aBorrar.Clear();
            foreach (var n in (data["borrar"] ?? new JArray()).Select(x => (string)x))
            {
                var c = BuscarEstilo(doc, n);
                if (c == null) { borrados.Add(new { nombre = n, error = "no existe" }); continue; }
                int u = Uso(usos, c);
                if (u > 0 && !forzar) { borrados.Add(new { nombre = n, omitido = "tiene " + u + " lineas; usar reasignar o forzar_borrado" }); continue; }
                aBorrar.Add(c.Id);
                borrados.Add(new { nombre = n, borrado = true, lineas_que_tenia = u });
            }
            if (aBorrar.Count > 0) doc.Delete(aBorrar);
            log["borrados"] = borrados;
            log["lineas_en_grupos_sin_tocar"] = enGrupo;

            if (dryRun) t.RollBack(); else t.Commit();
            log["dry_run"] = dryRun;
            log["estado"] = dryRun ? "simulado y deshecho" : t.GetStatus().ToString();
            log["avisos_revit"] = fallas.Messages;
            return log;
        }
    }

    // ---------- utilidades ----------

    static IEnumerable<Category> Estilos(Document doc)
    {
        return doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories.Cast<Category>();
    }

    static Category BuscarEstilo(Document doc, string nombre)
    {
        return Estilos(doc).FirstOrDefault(c => c.Name == nombre);
    }

    // Cantidad de lineas (modelo y detalle) por estilo, por id de subcategoria.
    static Dictionary<int, int> Usos(Document doc)
    {
        var d = new Dictionary<int, int>();
        foreach (CurveElement ce in new FilteredElementCollector(doc).OfClass(typeof(CurveElement)))
        {
            var gs = ce.LineStyle as GraphicsStyle;
            if (gs == null || gs.GraphicsStyleCategory == null) continue;
            int id = gs.GraphicsStyleCategory.Id.IntegerValue;
            int n; d.TryGetValue(id, out n); d[id] = n + 1;
        }
        return d;
    }

    static int Uso(Dictionary<int, int> usos, Category c)
    {
        int n; return usos.TryGetValue(c.Id.IntegerValue, out n) ? n : 0;
    }

    // Lineas por estilo, indexadas una sola vez por llamada (un modelo real tenia ~140.000 lineas ARCH).
    static Dictionary<int, List<CurveElement>> indice;

    static Dictionary<int, List<CurveElement>> Indice(Document doc)
    {
        if (indice != null) return indice;
        indice = new Dictionary<int, List<CurveElement>>();
        foreach (CurveElement ce in new FilteredElementCollector(doc).OfClass(typeof(CurveElement)))
        {
            var gs = ce.LineStyle as GraphicsStyle;
            if (gs == null || gs.GraphicsStyleCategory == null) continue;
            int id = gs.GraphicsStyleCategory.Id.IntegerValue;
            List<CurveElement> l;
            if (!indice.TryGetValue(id, out l)) { l = new List<CurveElement>(); indice[id] = l; }
            l.Add(ce);
        }
        return indice;
    }

    // Pasa las lineas de origen a destino. Las que estan en grupos NO se tocan (Revit rechaza el
    // cambio al confirmar y deshace toda la transaccion; dry_run no lo detecta): cuentan como
    // error, asi el estilo viejo no se borra. Devuelve (pasadas, con error, en grupo).
    static Tuple<int, int> Reasignar(Document doc, Category origen, Category destino)
    {
        var gs = destino.GetGraphicsStyle(GraphicsStyleType.Projection);
        var idx = Indice(doc);
        List<CurveElement> lista;
        if (!idx.TryGetValue(origen.Id.IntegerValue, out lista)) return Tuple.Create(0, 0);
        List<CurveElement> dest;
        if (!idx.TryGetValue(destino.Id.IntegerValue, out dest)) { dest = new List<CurveElement>(); idx[destino.Id.IntegerValue] = dest; }
        int ok = 0, err = 0;
        var quedan = new List<CurveElement>();
        foreach (var ce in lista)
        {
            if (ce.GroupId != ElementId.InvalidElementId) { err++; enGrupo++; quedan.Add(ce); continue; }
            try { ce.LineStyle = gs; ok++; dest.Add(ce); } catch { err++; quedan.Add(ce); }
        }
        idx[origen.Id.IntegerValue] = quedan;
        return Tuple.Create(ok, err);
    }

    static int enGrupo;

    static string NombrePatron(Document doc, Category c)
    {
        var id = c.GetLinePatternId(GraphicsStyleType.Projection);
        if (id == LinePatternElement.GetSolidPatternId()) return SOLIDO;
        var e = doc.GetElement(id);
        return e == null ? SOLIDO : e.Name;
    }

    static ElementId IdPatron(Document doc, string nombre)
    {
        if (string.IsNullOrEmpty(nombre) || nombre == SOLIDO) return LinePatternElement.GetSolidPatternId();
        var lpe = LinePatternElement.GetLinePatternElementByName(doc, nombre);
        if (lpe == null) throw new ArgumentException("No existe el patron de linea '" + nombre + "'.");
        return lpe.Id;
    }

    static List<string> Diferencias(Document doc, Category c, JObject e)
    {
        var dif = new List<string>();
        if (e["grosor"] != null && c.GetLineWeight(GraphicsStyleType.Projection) != (int)e["grosor"])
            dif.Add("grosor " + c.GetLineWeight(GraphicsStyleType.Projection) + " -> " + (int)e["grosor"]);
        if (e["rgb"] != null)
        {
            var actual = Rgb(c.LineColor);
            var quiero = e["rgb"].Select(x => (int)x).ToArray();
            if (!actual.SequenceEqual(quiero)) dif.Add("color " + string.Join(",", actual) + " -> " + string.Join(",", quiero));
        }
        if (e["patron"] != null && NombrePatron(doc, c) != (string)e["patron"])
            dif.Add("patron " + NombrePatron(doc, c) + " -> " + (string)e["patron"]);
        return dif;
    }

    static void Fijar(Document doc, Category c, JObject e)
    {
        if (e["grosor"] != null) c.SetLineWeight((int)e["grosor"], GraphicsStyleType.Projection);
        if (e["rgb"] != null)
        {
            var v = e["rgb"].Select(x => (byte)(int)x).ToArray();
            c.LineColor = new Color(v[0], v[1], v[2]);
        }
        if (e["patron"] != null) c.SetLinePatternId(IdPatron(doc, (string)e["patron"]), GraphicsStyleType.Projection);
    }

    static int[] Rgb(Color c)
    {
        return c == null || !c.IsValid ? new[] { 0, 0, 0 } : new int[] { c.Red, c.Green, c.Blue };
    }

    static List<object> Segmentos(LinePattern lp)
    {
        return lp.GetSegments().Select(s => (object)new
        {
            tipo = s.Type == LinePatternSegmentType.Dash ? "raya" : s.Type == LinePatternSegmentType.Space ? "espacio" : "punto",
            mm = Math.Round(s.Length * MM, 3)
        }).ToList();
    }

    static List<LinePatternSegment> ASegmentos(JArray segs)
    {
        return segs.Select(s =>
        {
            string tipo = (string)s["tipo"];
            var t = tipo == "raya" ? LinePatternSegmentType.Dash : tipo == "espacio" ? LinePatternSegmentType.Space : LinePatternSegmentType.Dot;
            return new LinePatternSegment(t, ((double)s["mm"]) / MM);
        }).ToList();
    }

    static bool MismosSegmentos(List<object> actual, JArray quiero)
    {
        var a = JArray.FromObject(actual);
        if (quiero == null || a.Count != quiero.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if ((string)a[i]["tipo"] != (string)quiero[i]["tipo"]) return false;
            if (Math.Abs((double)a[i]["mm"] - (double)quiero[i]["mm"]) > 0.01) return false;
        }
        return true;
    }

    static List<KeyValuePair<string, string>> Mapa(JToken t)
    {
        var o = t as JObject;
        if (o == null) return new List<KeyValuePair<string, string>>();
        return o.Properties().Select(p => new KeyValuePair<string, string>(p.Name, (string)p.Value)).ToList();
    }
}
