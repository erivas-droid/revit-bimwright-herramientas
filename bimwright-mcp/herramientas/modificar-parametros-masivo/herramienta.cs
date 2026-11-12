// herramienta: modificar-parametros-masivo (bimwright-mcp)
// Cambia un parametro en muchos elementos (o en sus tipos) con una operacion:
// fijar, reemplazar (buscar/reemplazar, con regex opcional), prefijo, sufijo,
// transformar (mayusculas, minusculas, sin_tildes, codigo) o vaciar.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// dry_run por defecto true. Omite elementos en grupos (al editar ejemplares). Ver README.md.
using System;
using System.Collections.Generic;
using System.Globalization;
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
        var uidoc = app.ActiveUIDocument;
        var doc = uidoc.Document;
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        int max = data["max_detalle"] == null ? 20 : (int)data["max_detalle"];
        string op = (string)data["operacion"] ?? "";
        string nombre = (string)data["parametro"];
        if (string.IsNullOrEmpty(nombre)) throw new ArgumentException("Falta 'parametro'.");
        bool enTipo = data["en_tipo"] != null && (bool)data["en_tipo"];
        var nuevo = Operacion(op, data);
        Regex filtro = data["filtro_valor"] == null ? null : new Regex((string)data["filtro_valor"]);

        var elementos = Destinos(uidoc, data);
        var enGrupo = new List<Element>();
        List<Element> objetivos;
        if (enTipo)
            objetivos = elementos.Select(e => doc.GetElement(e.GetTypeId())).Where(t => t != null)
                .GroupBy(t => t.Id.IntegerValue).Select(g => g.First()).ToList();
        else
        {
            enGrupo = elementos.Where(e => e.GroupId != ElementId.InvalidElementId).ToList();
            objetivos = elementos.Where(e => e.GroupId == ElementId.InvalidElementId).ToList();
        }

        // Calcular primero todos los valores nuevos (sin escribir).
        var resumen = new Dictionary<string, int>();
        var planes = new List<Tuple<Element, Parameter, string, string>>();
        foreach (var e in objetivos)
        {
            var p = e.LookupParameter(nombre);
            if (p == null)
            {
                if (!enTipo && doc.GetElement(e.GetTypeId()) is Element t && t.LookupParameter(nombre) != null)
                    Contar(resumen, "es de tipo (usar en_tipo=true)");
                else Contar(resumen, "sin el parametro");
                continue;
            }
            if (p.IsReadOnly) { Contar(resumen, "solo lectura"); continue; }
            string antes = Texto(p);
            if (filtro != null && !filtro.IsMatch(antes)) { Contar(resumen, "no calza con filtro_valor"); continue; }
            if (op != "fijar" && op != "vaciar" && p.StorageType != StorageType.String) { Contar(resumen, "no es texto (" + p.StorageType + ")"); continue; }
            string despues = nuevo(antes);
            if (despues == antes) { Contar(resumen, "sin cambio"); continue; }
            planes.Add(Tuple.Create(e, p, antes, despues));
        }

        // Marcas y numeros repetidos dan un aviso de Revit al confirmar, que deshace todo.
        var bip = planes.Count == 0 ? BuiltInParameter.INVALID
            : ((planes[0].Item2.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID);
        if (bip == BuiltInParameter.ALL_MODEL_MARK || bip == BuiltInParameter.ROOM_NUMBER || bip == BuiltInParameter.DOOR_NUMBER)
        {
            var rep = planes.GroupBy(x => x.Item4).Where(g => g.Key != "" && g.Count() > 1).Select(g => g.Key + " (x" + g.Count() + ")").ToList();
            if (rep.Count > 0)
                throw new InvalidOperationException("'" + nombre + "' quedaria repetido en: " + string.Join(", ", rep.Take(10))
                    + ". Revit avisa por marcas o numeros repetidos y deshace todo. Use renumerar o un valor unico.");
        }

        var detalle = new List<object>();
        var errores = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        using (var tr = new Transaction(doc, "Modificar parametros: " + nombre))
        {
            var o = tr.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            tr.SetFailureHandlingOptions(o);
            tr.Start();
            foreach (var pl in planes)
            {
                using (var st = new SubTransaction(doc))
                {
                    st.Start();
                    try
                    {
                        Escribir(pl.Item2, pl.Item4, op == "vaciar");
                        st.Commit();
                        Contar(resumen, "cambiados");
                        detalle.Add(new { elemento = Etiqueta(pl.Item1), antes = pl.Item3, despues = Texto(pl.Item2) });
                    }
                    catch (Exception ex)
                    {
                        if (st.HasStarted() && !st.HasEnded()) st.RollBack();
                        errores.Add(new { elemento = Etiqueta(pl.Item1), valor = pl.Item4, error = ex.Message });
                    }
                }
            }
            estado = dryRun ? tr.RollBack() : tr.Commit();
        }
        return new Dictionary<string, object>
        {
            { "dry_run", dryRun }, { "transaccion", estado.ToString() }, { "operacion", op }, { "parametro", nombre },
            { "en_tipo", enTipo }, { "elementos", elementos.Count }, { "objetivos", objetivos.Count },
            { "omitidos_en_grupo", enGrupo.Count }, { "resumen", resumen }, { "errores", errores.Count },
            { "detalle", detalle.Take(max).ToList() }, { "detalle_truncado", detalle.Count > max },
            { "detalle_errores", errores.Take(max).ToList() }
        };
    }

    // ---------- operaciones (texto -> texto) ----------

    static Func<string, string> Operacion(string op, JObject data)
    {
        switch (op)
        {
            case "fijar":
                if (data["valor"] == null) throw new ArgumentException("Falta 'valor'.");
                string v = (string)data["valor"];
                return s => v;
            case "vaciar":
                return s => "";
            case "reemplazar":
            {
                string buscar = (string)data["buscar"];
                if (string.IsNullOrEmpty(buscar)) throw new ArgumentException("Falta 'buscar'.");
                string por = (string)data["reemplazo"] ?? "";
                bool rx = data["regex"] != null && (bool)data["regex"];
                if (rx) { var r = new Regex(buscar); return s => r.Replace(s, por); }
                return s => s.Replace(buscar, por);
            }
            case "prefijo":
            {
                string tx = (string)data["texto"];
                if (string.IsNullOrEmpty(tx)) throw new ArgumentException("Falta 'texto'.");
                return s => s.StartsWith(tx) ? s : tx + s;
            }
            case "sufijo":
            {
                string tx = (string)data["texto"];
                if (string.IsNullOrEmpty(tx)) throw new ArgumentException("Falta 'texto'.");
                return s => s.EndsWith(tx) ? s : s + tx;
            }
            case "transformar":
            {
                string modo = (string)data["modo"] ?? "";
                switch (modo)
                {
                    case "mayusculas": return s => s.ToUpperInvariant();
                    case "minusculas": return s => s.ToLowerInvariant();
                    case "sin_tildes": return SinTildes;
                    case "codigo": return Codigo;
                    default: throw new ArgumentException("modo debe ser mayusculas, minusculas, sin_tildes o codigo.");
                }
            }
            default:
                throw new ArgumentException("operacion desconocida: '" + op + "'. Use fijar, reemplazar, prefijo, sufijo, transformar o vaciar.");
        }
    }

    // Quita tildes y dieresis; la ñ pasa a n (nombres tipo codigo: sin tildes ni ñ).
    static string SinTildes(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    // Nombre tipo codigo: mayusculas, sin tildes ni ñ, solo letras, numeros y guion medio; espacios a guion.
    static string Codigo(string s)
    {
        string t = SinTildes(s).ToUpperInvariant();
        t = Regex.Replace(t, @"\s+", "-");
        t = Regex.Replace(t, @"[^A-Z0-9\-]", "");
        t = Regex.Replace(t, @"-{2,}", "-").Trim('-');
        return t;
    }

    // ---------- escritura ----------

    static void Escribir(Parameter p, string valor, bool vaciar)
    {
        switch (p.StorageType)
        {
            case StorageType.String:
                p.Set(valor);
                return;
            case StorageType.ElementId:
                if (vaciar) { p.Set(ElementId.InvalidElementId); return; }
                throw new InvalidOperationException("parametro de tipo Id: solo admite vaciar");
            default:
                if (vaciar) throw new InvalidOperationException("un numero no se puede vaciar");
                if (!p.SetValueString(valor)) throw new InvalidOperationException("Revit no acepto '" + valor + "' (se lee en las unidades del proyecto)");
                return;
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
        return lista;
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

    static void Contar(Dictionary<string, int> r, string k) { int n; r.TryGetValue(k, out n); r[k] = n + 1; }

    static string Etiqueta(Element e)
    {
        return e.Id.IntegerValue + " " + (e.Category == null ? "" : e.Category.Name + ": ") + e.Name;
    }
}
