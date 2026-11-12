// herramienta: transferir-parametros (bimwright-mcp)
// Dos acciones (dry_run por defecto true):
//  - copiar: en cada elemento, copia el valor de un parametro a otro (por ejemplo, un parametro
//    de tipo a uno de ejemplar), convirtiendo entre tipos de dato si hace falta.
//  - igualar: copia parametros de un elemento modelo a otros ("igualar propiedades"), y
//    opcionalmente les cambia el tipo.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`. Ver README.md.
// Omite los elementos que estan en grupos (ver herramienta grupos).
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public class McpDynamicScript
{
    public static object Run(UIApplication app, JObject data)
    {
        var uidoc = app.ActiveUIDocument;
        var doc = uidoc.Document;
        string accion = (string)data["accion"] ?? "";
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        int max = data["max_detalle"] == null ? 20 : (int)data["max_detalle"];
        Func<Document, List<Element>, JObject, List<object>, List<object>, Dictionary<string, int>, object> fn;
        switch (accion)
        {
            case "copiar": fn = Copiar; break;
            case "igualar": fn = Igualar; break;
            default: throw new ArgumentException("accion desconocida: '" + accion + "'. Use copiar o igualar.");
        }
        var elementos = Destinos(uidoc, data);
        // Los miembros de grupos no se pueden cambiar fuera del modo edicion de grupo: Revit lo
        // rechaza al confirmar y deshace TODO (una simulacion no lo detecta). Se omiten.
        var enGrupo = elementos.Where(e => e.GroupId != ElementId.InvalidElementId).ToList();
        elementos = elementos.Where(e => e.GroupId == ElementId.InvalidElementId).ToList();
        var detalle = new List<object>();
        var errores = new List<object>();
        var resumen = new Dictionary<string, int>();
        object extra;
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        using (var t = new Transaction(doc, "Transferir parametros: " + accion))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            extra = fn(doc, elementos, data, detalle, errores, resumen);
            estado = dryRun ? t.RollBack() : t.Commit();
        }
        return new Dictionary<string, object>
        {
            { "dry_run", dryRun }, { "transaccion", estado.ToString() }, { "accion", accion },
            { "elementos", elementos.Count }, { "resumen", resumen }, { "errores", errores.Count },
            { "omitidos_en_grupo", enGrupo.Count },
            { "ejemplos_en_grupo", enGrupo.Take(5).Select(e => e.Id.IntegerValue).ToList() },
            { "extra", extra },
            { "detalle", detalle.Take(max).ToList() }, { "detalle_truncado", detalle.Count > max },
            { "detalle_errores", errores.Take(max).ToList() }
        };
    }

    // ---------- seleccion ----------

    // ids | seleccion=true (lo seleccionado en Revit) | categoria (+ vista: id o "activa") (+ donde: {parametro: valor}).
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
                string nombre = p.Name, valor = (string)p.Value;
                lista = lista.Where(e => { var q = Buscar(e, nombre); return q != null && Texto(q) == valor; }).ToList();
            }
        return lista;
    }

    // ---------- utilidades ----------

    // Parametro del ejemplar; si no existe, el del tipo.
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

    static bool EsDelTipo(Element e, Parameter p)
    {
        return p.Element != null && p.Element.Id != e.Id;
    }

    // Asigna src a dst. Mismo tipo de dato: valor directo. Si no, por texto.
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
        if (!dst.SetValueString(txt)) throw new InvalidOperationException("no se pudo convertir '" + txt + "' a " + dst.StorageType);
    }

    static void Contar(Dictionary<string, int> r, string k) { int n; r.TryGetValue(k, out n); r[k] = n + 1; }

    static string Etiqueta(Element e)
    {
        return e.Id.IntegerValue + " " + (e.Category == null ? "" : e.Category.Name + ": ") + e.Name;
    }

    // ---------- copiar ----------

    // desde, hacia (nombres). solo_vacios: no pisa valores existentes. El destino debe ser del ejemplar.
    static object Copiar(Document doc, List<Element> els, JObject data, List<object> detalle, List<object> errores, Dictionary<string, int> resumen)
    {
        string desde = (string)data["desde"], hacia = (string)data["hacia"];
        if (string.IsNullOrEmpty(desde) || string.IsNullOrEmpty(hacia)) throw new ArgumentException("Faltan 'desde' y 'hacia'.");
        bool soloVacios = data["solo_vacios"] != null && (bool)data["solo_vacios"];
        foreach (var e in els)
        {
            var src = Buscar(e, desde);
            var dst = e.LookupParameter(hacia);
            if (src == null) { Contar(resumen, "sin '" + desde + "'"); continue; }
            if (dst == null)
            {
                var dt = Buscar(e, hacia);
                Contar(resumen, dt != null ? "'" + hacia + "' es de tipo (no se escribe)" : "sin '" + hacia + "'");
                continue;
            }
            if (dst.IsReadOnly) { Contar(resumen, "'" + hacia + "' de solo lectura"); continue; }
            string v = Texto(src), antes = Texto(dst);
            if (v == "") { Contar(resumen, "origen vacio"); continue; }
            if (soloVacios && antes != "") { Contar(resumen, "destino con valor (solo_vacios)"); continue; }
            if (antes == v) { Contar(resumen, "ya igual"); continue; }
            using (var st = new SubTransaction(doc))
            {
                st.Start();
                try
                {
                    Asignar(dst, src);
                    st.Commit();
                    Contar(resumen, "copiados");
                    detalle.Add(new { elemento = Etiqueta(e), antes, despues = Texto(dst), origen_en_tipo = EsDelTipo(e, src) });
                }
                catch (Exception ex)
                {
                    if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                    errores.Add(new { elemento = Etiqueta(e), valor = v, error = ex.Message });
                }
            }
        }
        return new { desde, hacia, solo_vacios = soloVacios };
    }

    // ---------- igualar ----------

    // origen (id); parametros (lista de nombres) o todos=true; cambiar_tipo; incluir_restricciones.
    static object Igualar(Document doc, List<Element> els, JObject data, List<object> detalle, List<object> errores, Dictionary<string, int> resumen)
    {
        if (data["origen"] == null) throw new ArgumentException("Falta 'origen' (id del elemento modelo).");
        var o = doc.GetElement(new ElementId((int)data["origen"]));
        if (o == null) throw new ArgumentException("No existe el elemento origen.");
        bool cambiarTipo = data["cambiar_tipo"] != null && (bool)data["cambiar_tipo"];
        bool todos = data["todos"] != null && (bool)data["todos"];
        bool restricciones = data["incluir_restricciones"] != null && (bool)data["incluir_restricciones"];
        var lista = data["parametros"] as JArray;
        if (lista == null && !todos && !cambiarTipo) throw new ArgumentException("Indique 'parametros', todos=true o cambiar_tipo=true.");

        List<string> nombres;
        var excluidos = new List<string>();
        if (lista != null) nombres = lista.Select(x => (string)x).ToList();
        else if (todos)
        {
            nombres = new List<string>();
            foreach (Parameter p in o.Parameters)
            {
                if (p.IsReadOnly || !p.HasValue || p.StorageType == StorageType.None) continue;
                var bip = (p.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;
                bool marca = bip == BuiltInParameter.ALL_MODEL_MARK || bip == BuiltInParameter.DOOR_NUMBER || bip == BuiltInParameter.ROOM_NUMBER;
                bool idRef = p.StorageType == StorageType.ElementId;
                bool restr = p.Definition.GetGroupTypeId() == GroupTypeId.Constraints;
                bool guid = p.Definition.Name.IndexOf("GUID", StringComparison.OrdinalIgnoreCase) >= 0;
                if (marca || idRef || guid || (restr && !restricciones)) { excluidos.Add(p.Definition.Name); continue; }
                nombres.Add(p.Definition.Name);
            }
            nombres = nombres.Distinct().ToList();
        }
        else nombres = new List<string>();

        foreach (var e in els)
        {
            if (e.Id == o.Id) continue;
            var cambios = new List<string>();
            using (var st = new SubTransaction(doc))
            {
                st.Start();
                try
                {
                    if (cambiarTipo && e.GetTypeId() != o.GetTypeId())
                    {
                        if (!e.IsValidType(o.GetTypeId())) { Contar(resumen, "tipo no compatible"); st.RollBack(); continue; }
                        string antesT = doc.GetElement(e.GetTypeId()).Name;
                        e.ChangeTypeId(o.GetTypeId());
                        cambios.Add("tipo: " + antesT + " -> " + doc.GetElement(o.GetTypeId()).Name);
                        Contar(resumen, "tipo cambiado");
                    }
                    foreach (var n in nombres)
                    {
                        var src = o.LookupParameter(n);
                        var dst = e.LookupParameter(n);
                        if (src == null || dst == null || dst.IsReadOnly) { Contar(resumen, "'" + n + "' no aplica"); continue; }
                        string v = Texto(src), antes = Texto(dst);
                        if (v == antes) continue;
                        Asignar(dst, src);
                        string despues = Texto(dst);
                        if (despues == antes) continue;
                        cambios.Add(n + ": '" + antes + "' -> '" + despues + "'");
                        Contar(resumen, n);
                    }
                    st.Commit();
                    if (cambios.Count > 0) detalle.Add(new { elemento = Etiqueta(e), cambios });
                    else Contar(resumen, "ya iguales");
                }
                catch (Exception ex)
                {
                    if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                    errores.Add(new { elemento = Etiqueta(e), error = ex.Message });
                }
            }
        }
        return new { origen = Etiqueta(o), parametros = nombres, excluidos_por_defecto = excluidos };
    }
}
