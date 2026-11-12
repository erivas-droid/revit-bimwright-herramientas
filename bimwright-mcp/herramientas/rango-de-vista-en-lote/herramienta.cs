// herramienta: rango-de-vista-en-lote (bimwright-mcp)
// Lee y cambia el rango de vista (superior, corte, inferior, profundidad) de muchas plantas
// a la vez: plantas de piso, de cielo, estructurales y de area, y sus plantillas.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Acciones: leer (solo lectura) y aplicar (dry_run por defecto true). Ver README.md.
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
    static readonly PlanViewPlane[] PLANOS =
    {
        PlanViewPlane.TopClipPlane, PlanViewPlane.CutPlane,
        PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane
    };
    static readonly string[] NOMBRES = { "superior", "corte", "inferior", "profundidad" };

    public static object Run(UIApplication app, JObject data)
    {
        var doc = app.ActiveUIDocument.Document;
        string accion = (string)data["accion"] ?? "";
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        var niveles = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.ProjectElevation).ToList();
        switch (accion)
        {
            case "leer":
                return Leer(doc, data, niveles);
            case "aplicar":
                return Aplicar(doc, data, niveles, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use leer o aplicar.");
        }
    }

    // ---------- seleccion de vistas ----------

    // Filtros (se combinan): vista_ids, nombre (regex), nivel (nombre del nivel asociado),
    // tipo (planta, cielo, estructural, area), incluir_plantillas. aplicar exige alguno o todas=true.
    static List<ViewPlan> Seleccionar(Document doc, JObject data, bool exigirFiltro)
    {
        var ids = data["vista_ids"] as JArray;
        string nombre = (string)data["nombre"];
        string nivel = (string)data["nivel"];
        string tipo = (string)data["tipo"];
        bool plantillas = data["incluir_plantillas"] != null && (bool)data["incluir_plantillas"];
        bool todas = data["todas"] != null && (bool)data["todas"];
        if (exigirFiltro && ids == null && nombre == null && nivel == null && tipo == null && !todas)
            throw new ArgumentException("Indique vista_ids, nombre, nivel o tipo, o todas=true.");

        IEnumerable<ViewPlan> vistas;
        if (ids != null)
            vistas = ids.Select(x => doc.GetElement(new ElementId((int)x)) as ViewPlan).Where(v => v != null);
        else
            vistas = new FilteredElementCollector(doc).OfClass(typeof(ViewPlan)).Cast<ViewPlan>()
                .Where(v => plantillas || !v.IsTemplate);
        if (nombre != null)
        {
            var rx = new Regex(nombre, RegexOptions.IgnoreCase);
            vistas = vistas.Where(v => rx.IsMatch(v.Name));
        }
        if (nivel != null) vistas = vistas.Where(v => v.GenLevel != null && v.GenLevel.Name == nivel);
        if (tipo != null)
        {
            var vt = TipoVista(tipo);
            vistas = vistas.Where(v => v.ViewType == vt);
        }
        return vistas.Where(TieneRango).OrderBy(v => v.IsTemplate).ThenBy(v => v.Name).ToList();
    }

    static ViewType TipoVista(string t)
    {
        switch (t.ToLowerInvariant())
        {
            case "planta": return ViewType.FloorPlan;
            case "cielo": return ViewType.CeilingPlan;
            case "estructural": return ViewType.EngineeringPlan;
            case "area": return ViewType.AreaPlan;
            default: return (ViewType)Enum.Parse(typeof(ViewType), t, true);
        }
    }

    static bool TieneRango(ViewPlan v)
    {
        try { using (v.GetViewRange()) return true; }
        catch { return false; }
    }

    // Nombre de la plantilla y si esta controla el rango de vista.
    static string Plantilla(Document doc, View v, out bool controla)
    {
        controla = false;
        if (v.ViewTemplateId == ElementId.InvalidElementId) return null;
        var t = doc.GetElement(v.ViewTemplateId) as View;
        if (t == null) return null;
        var libres = t.GetNonControlledTemplateParameterIds();
        controla = !libres.Contains(new ElementId(BuiltInParameter.PLAN_VIEW_RANGE));
        return t.Name;
    }

    // ---------- niveles ----------

    static Level Arriba(Level gen, List<Level> niveles)
    {
        return gen == null ? null : niveles.FirstOrDefault(l => l.ProjectElevation > gen.ProjectElevation + 1e-6);
    }

    static Level Abajo(Level gen, List<Level> niveles)
    {
        return gen == null ? null : niveles.LastOrDefault(l => l.ProjectElevation < gen.ProjectElevation - 1e-6);
    }

    // Nivel de un plano escrito como lo usa aplicar: asociado, superior, inferior, ilimitado o un nombre.
    static string Relativo(Document doc, ViewPlan v, ElementId id, List<Level> niveles)
    {
        if (id.Equals(PlanViewRange.Unlimited)) return "ilimitado";
        if (id.Equals(PlanViewRange.Current)) return "asociado";
        if (id.Equals(PlanViewRange.LevelAbove)) return "superior";
        if (id.Equals(PlanViewRange.LevelBelow)) return "inferior";
        var gen = v.GenLevel;
        if (gen != null && id.Equals(gen.Id)) return "asociado";
        var a = Arriba(gen, niveles);
        if (a != null && id.Equals(a.Id)) return "superior";
        var b = Abajo(gen, niveles);
        if (b != null && id.Equals(b.Id)) return "inferior";
        var l = doc.GetElement(id) as Level;
        return l != null ? l.Name : "?";
    }

    // Nivel real de un plano en esta vista (null si es ilimitado o no se puede saber).
    static Level NivelReal(Document doc, ViewPlan v, ElementId id, List<Level> niveles)
    {
        if (id.Equals(PlanViewRange.Unlimited)) return null;
        if (id.Equals(PlanViewRange.Current)) return v.GenLevel;
        if (id.Equals(PlanViewRange.LevelAbove)) return Arriba(v.GenLevel, niveles);
        if (id.Equals(PlanViewRange.LevelBelow)) return Abajo(v.GenLevel, niveles);
        return doc.GetElement(id) as Level;
    }

    static ElementId IdNivel(string s, List<Level> niveles)
    {
        switch (s.ToLowerInvariant())
        {
            case "asociado": return PlanViewRange.Current;
            case "superior": return PlanViewRange.LevelAbove;
            case "inferior": return PlanViewRange.LevelBelow;
            case "ilimitado": return PlanViewRange.Unlimited;
        }
        var l = niveles.FirstOrDefault(x => x.Name == s);
        if (l == null) throw new ArgumentException("No existe el nivel '" + s + "'.");
        return l.Id;
    }

    // ---------- descripcion del rango ----------

    static string Texto(Document doc, ViewPlan v, PlanViewRange r, List<Level> niveles)
    {
        var partes = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            string n = Relativo(doc, v, r.GetLevelId(PLANOS[i]), niveles);
            double off = Math.Round(r.GetOffset(PLANOS[i]) * MM);
            partes.Add(NOMBRES[i] + ": " + n + (n == "ilimitado" ? "" : " " + (off >= 0 ? "+" : "") + off));
        }
        return string.Join(" | ", partes);
    }

    static Dictionary<string, object> Detalle(Document doc, ViewPlan v, PlanViewRange r, List<Level> niveles)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < 4; i++)
        {
            var id = r.GetLevelId(PLANOS[i]);
            double off = r.GetOffset(PLANOS[i]);
            var lv = NivelReal(doc, v, id, niveles);
            d[NOMBRES[i]] = new Dictionary<string, object>
            {
                { "nivel", Relativo(doc, v, id, niveles) },
                { "nivel_real", lv == null ? null : lv.Name },
                { "desfase_mm", Math.Round(off * MM, 1) },
                { "cota_mm", lv == null ? (double?)null : Math.Round((lv.ProjectElevation + off) * MM, 1) }
            };
        }
        return d;
    }

    // ---------- leer ----------

    static Dictionary<string, object> Leer(Document doc, JObject data, List<Level> niveles)
    {
        bool detalle = data["detalle"] != null && (bool)data["detalle"];
        var vistas = Seleccionar(doc, data, false);
        var lista = new List<object>();
        var combinaciones = new Dictionary<string, List<string>>();
        foreach (var v in vistas)
        {
            bool controla;
            string plantilla = Plantilla(doc, v, out controla);
            using (var r = v.GetViewRange())
            {
                string txt = Texto(doc, v, r, niveles);
                List<string> l;
                if (!combinaciones.TryGetValue(txt, out l)) { l = new List<string>(); combinaciones[txt] = l; }
                l.Add(v.Name);
                var item = new Dictionary<string, object>
                {
                    { "id", v.Id.IntegerValue }, { "nombre", v.Name }, { "tipo", v.ViewType.ToString() },
                    { "nivel", v.GenLevel == null ? null : v.GenLevel.Name },
                    { "es_plantilla", v.IsTemplate }, { "plantilla", plantilla },
                    { "controlada_por_plantilla", controla }, { "rango", txt }
                };
                if (detalle) item["detalle"] = Detalle(doc, v, r, niveles);
                lista.Add(item);
            }
        }
        return new Dictionary<string, object>
        {
            { "vistas", lista.Count },
            { "combinaciones", combinaciones.OrderByDescending(kv => kv.Value.Count).Select(kv => new
                {
                    rango = kv.Key, cantidad = kv.Value.Count, ejemplos = kv.Value.Take(5).ToList()
                }).ToList() },
            { "lista", lista }
        };
    }

    // ---------- aplicar ----------

    // rango: { "corte": {"nivel": "asociado", "desfase_mm": 1200}, "profundidad": {...}, ... }.
    // Cada plano es opcional y cada campo tambien: lo que no se indica queda como esta.
    // desde_vista: id de una vista cuyo rango se copia, en forma relativa (asociado, superior...).
    static Dictionary<string, object> Aplicar(Document doc, JObject data, List<Level> niveles, bool dryRun)
    {
        var specs = new JObject[4];
        if (data["desde_vista"] != null)
        {
            var origen = doc.GetElement(new ElementId((int)data["desde_vista"])) as ViewPlan;
            if (origen == null || !TieneRango(origen)) throw new ArgumentException("desde_vista no es una planta con rango de vista.");
            using (var r = origen.GetViewRange())
                for (int i = 0; i < 4; i++)
                    specs[i] = new JObject
                    {
                        ["nivel"] = Relativo(doc, origen, r.GetLevelId(PLANOS[i]), niveles),
                        ["desfase_mm"] = r.GetOffset(PLANOS[i]) * MM
                    };
        }
        var rango = data["rango"] as JObject;
        if (rango != null)
            for (int i = 0; i < 4; i++)
            {
                var s = rango[NOMBRES[i]] as JObject;
                if (s == null) continue;
                if (specs[i] == null) specs[i] = new JObject();
                foreach (var p in s.Properties()) specs[i][p.Name] = p.Value;
            }
        if (specs.All(s => s == null)) throw new ArgumentException("Falta 'rango' o 'desde_vista'.");
        foreach (var s in specs)
            if (s != null && s["nivel"] != null) IdNivel((string)s["nivel"], niveles); // valida nombres antes de empezar

        var vistas = Seleccionar(doc, data, true);
        var cambiadas = new List<object>();
        var sinCambio = new List<object>();
        var omitidas = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        using (var t = new Transaction(doc, "Rango de vista en lote"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            foreach (var v in vistas)
            {
                bool controla;
                string plantilla = Plantilla(doc, v, out controla);
                var item = new Dictionary<string, object> { { "id", v.Id.IntegerValue }, { "nombre", v.Name } };
                if (controla)
                {
                    item["motivo"] = "el rango lo controla la plantilla '" + plantilla + "'; aplicar a la plantilla";
                    omitidas.Add(item);
                    continue;
                }
                string antes;
                using (var r0 = v.GetViewRange()) antes = Texto(doc, v, r0, niveles);
                item["antes"] = antes;
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        using (var r = v.GetViewRange())
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                var s = specs[i];
                                if (s == null) continue;
                                if (s["nivel"] != null) r.SetLevelId(PLANOS[i], IdNivel((string)s["nivel"], niveles));
                                bool ilimitado = r.GetLevelId(PLANOS[i]).Equals(PlanViewRange.Unlimited);
                                if (s["desfase_mm"] != null && !ilimitado) r.SetOffset(PLANOS[i], (double)s["desfase_mm"] / MM);
                            }
                            v.SetViewRange(r);
                        }
                        st.Commit();
                        string despues;
                        using (var r1 = v.GetViewRange()) despues = Texto(doc, v, r1, niveles);
                        item["despues"] = despues;
                        (despues == antes ? sinCambio : cambiadas).Add(item);
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        item["error"] = ex.Message;
                        if (ex.Message.Contains("not valid"))
                            item["pista"] = v.ViewType == ViewType.CeilingPlan
                                ? "En cielo los planos van al reves: inferior <= corte <= superior <= profundidad."
                                : "Debe cumplirse superior >= corte >= inferior >= profundidad (en cota).";
                        errores.Add(item);
                    }
                }
            }
            estado = dryRun ? t.RollBack() : t.Commit();
        }
        return new Dictionary<string, object>
        {
            { "dry_run", dryRun }, { "transaccion", estado.ToString() },
            { "vistas", vistas.Count }, { "cambiadas", cambiadas.Count }, { "sin_cambio", sinCambio.Count },
            { "omitidas", omitidas.Count }, { "errores", errores.Count },
            { "detalle_cambiadas", cambiadas }, { "detalle_omitidas", omitidas }, { "detalle_errores", errores },
            { "detalle_sin_cambio", sinCambio.Select(x => ((Dictionary<string, object>)x)["nombre"]).ToList() }
        };
    }
}
