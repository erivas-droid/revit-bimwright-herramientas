// herramienta: grupos (bimwright-mcp)
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Acciones: listar_tipos, desagrupar, reagrupar, cambiar_tipo, duplicar_tipo, renombrar_tipo.
// Todo lo que modifica acepta dry_run (por defecto true): ejecuta, informa y deshace.
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
    const double GRAD = 180.0 / Math.PI;

    public static object Run(UIApplication app, JObject data)
    {
        var doc = app.ActiveUIDocument.Document;
        string accion = (string)data["accion"] ?? "";
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];

        switch (accion)
        {
            case "listar_tipos":
                return ListarTipos(doc);
            case "desagrupar":
                return ConTransaccion(doc, "Grupos: desagrupar", dryRun, () => Desagrupar(doc, Ids(data["grupos"])));
            case "reagrupar":
                return ConTransaccion(doc, "Grupos: reagrupar", dryRun, () => Reagrupar(doc, data));
            case "cambiar_tipo":
                return ConTransaccion(doc, "Grupos: cambiar tipo", dryRun, () => CambiarTipo(doc, Ids(data["grupos"]), data["tipo"]));
            case "duplicar_tipo":
                return ConTransaccion(doc, "Grupos: duplicar tipo", dryRun, () =>
                {
                    var t = BuscarTipo(doc, data["tipo"]);
                    var nuevo = t.Duplicate(NombreLibre(doc, (string)data["nuevo_nombre"]));
                    return new Dictionary<string, object> { { "tipo_origen", t.Id.IntegerValue }, { "tipo_nuevo", nuevo.Id.IntegerValue }, { "nombre", nuevo.Name } };
                });
            case "renombrar_tipo":
                return ConTransaccion(doc, "Grupos: renombrar tipo", dryRun, () =>
                {
                    var t = BuscarTipo(doc, data["tipo"]);
                    string antes = t.Name;
                    t.Name = (string)data["nuevo_nombre"];
                    return new Dictionary<string, object> { { "tipo", t.Id.IntegerValue }, { "antes", antes }, { "despues", t.Name } };
                });
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use listar_tipos, desagrupar, reagrupar, cambiar_tipo, duplicar_tipo o renombrar_tipo.");
        }
    }

    // ---------- acciones ----------

    static object ListarTipos(Document doc)
    {
        return new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>()
            .Select(t => new
            {
                id = t.Id.IntegerValue,
                nombre = t.Name,
                categoria = t.Category == null ? "" : t.Category.Name,
                instancias = t.Groups.Size,
                ids_instancias = t.Groups.Cast<Group>().Select(g => g.Id.IntegerValue).ToList()
            })
            .OrderBy(t => t.categoria).ThenBy(t => t.nombre).ToList();
    }

    static Dictionary<string, object> Desagrupar(Document doc, List<ElementId> ids)
    {
        var salida = new List<object>();
        foreach (var id in ids)
        {
            var g = doc.GetElement(id) as Group;
            if (g == null) throw new ArgumentException("No es un grupo: " + id.IntegerValue);
            var tipo = g.GroupType;
            var miembros = g.GetMemberIds().ToList();
            int k = IndiceReferencia(doc, miembros);
            object referencia = null;
            if (k >= 0)
            {
                var e = doc.GetElement(miembros[k]);
                var m = MarcoDe(e);
                referencia = new
                {
                    indice = k,
                    tipo_miembro = e.GetTypeId().IntegerValue,
                    x = m.O.X * MM, y = m.O.Y * MM, z = m.O.Z * MM,
                    ang = m.A * GRAD,
                    espejado = m.Esp
                };
            }
            int otras = tipo.Groups.Size - 1;
            bool fijado = g.Pinned;
            if (fijado) g.Pinned = false;
            var sueltos = g.UngroupMembers().Select(x => x.IntegerValue).ToList();
            salida.Add(new
            {
                grupo = id.IntegerValue,
                tipo_id = tipo.Id.IntegerValue,
                tipo_nombre = tipo.Name,
                otras_instancias = otras,
                miembros = sueltos,
                referencia,
                aviso = referencia == null ? "Sin miembro de referencia (familia o elemento con linea): reagrupar no podra reemplazar el tipo." : null
            });
        }
        return new Dictionary<string, object> { { "desagrupados", salida } };
    }

    static Dictionary<string, object> Reagrupar(Document doc, JObject data)
    {
        var ids = Ids(data["elementos"]);
        if (ids.Count == 0) throw new ArgumentException("Faltan 'elementos'.");
        string nombre = (string)data["nombre"];
        double tolMm = data["tolerancia_mm"] == null ? 2.0 : (double)data["tolerancia_mm"];

        var g = doc.Create.NewGroup(ids);
        var nt = g.GroupType;
        var res = new Dictionary<string, object>();
        res["grupo_nuevo"] = g.Id.IntegerValue;

        if (data["tipo_anterior"] == null)
        {
            if (!string.IsNullOrEmpty(nombre)) nt.Name = NombreLibre(doc, nombre);
            res["tipo_nuevo"] = nt.Id.IntegerValue;
            res["nombre"] = nt.Name;
            return res;
        }

        var viejo = BuscarTipo(doc, data["tipo_anterior"]);
        var r = data["referencia"] as JObject;
        if (r == null) throw new ArgumentException("Para reemplazar el tipo hace falta 'referencia' (la devuelve desagrupar).");
        int k = (int)r["indice"];
        int tipoRef = (int)r["tipo_miembro"];
        var Fe = new Marco
        {
            O = new XYZ((double)r["x"] / MM, (double)r["y"] / MM, (double)r["z"] / MM),
            A = (double)r["ang"] / GRAD,
            Esp = r["espejado"] != null && (bool)r["espejado"]
        };
        string nombreViejo = viejo.Name;
        nt.Name = NombreLibre(doc, string.IsNullOrEmpty(nombre) ? nombreViejo + " (editado)" : nombre);
        doc.Regenerate();

        var nuevos = g.GetMemberIds().ToList();
        var marcosNuevos = nuevos.Select(i => MarcoDe(doc.GetElement(i))).ToList();
        int rIdx = IndiceReferencia(doc, nuevos);
        if (rIdx < 0) throw new InvalidOperationException("El grupo nuevo no tiene un miembro de referencia (familia o elemento con linea).");

        var instancias = new List<object>();
        var omitidas = new List<object>();
        foreach (var q in viejo.Groups.Cast<Group>().ToList())
        {
            var qm = q.GetMemberIds().ToList();
            if (k >= qm.Count || doc.GetElement(qm[k]).GetTypeId().IntegerValue != tipoRef)
            {
                omitidas.Add(new { grupo = q.Id.IntegerValue, motivo = "el miembro de referencia no coincide" });
                continue;
            }
            var Fq = MarcoDe(doc.GetElement(qm[k]));
            if (Fq == null || Fq.Esp != Fe.Esp)
            {
                omitidas.Add(new { grupo = q.Id.IntegerValue, motivo = "instancia en espejo: no soportada" });
                continue;
            }
            // Donde deben quedar los miembros nuevos en esta instancia
            var deseados = marcosNuevos.Select(m => m == null ? null : new Marco { O = Aplicar(m.O, Fe, Fq), A = m.A + (Fq.A - Fe.A) }).ToList();

            using (var st = new SubTransaction(doc))
            {
                st.Start();
                bool fijado = q.Pinned;
                if (fijado) q.Pinned = false;
                q.GroupType = nt;
                doc.Regenerate();
                var qn = q.GetMemberIds().ToList();
                if (qn.Count != nuevos.Count)
                {
                    st.RollBack();
                    omitidas.Add(new { grupo = q.Id.IntegerValue, motivo = "distinto numero de miembros tras cambiar el tipo" });
                    continue;
                }
                var Ar = MarcoDe(doc.GetElement(qn[rIdx]));
                var Dr = deseados[rIdx];
                double da = Norm(Dr.A - Ar.A);
                if (Math.Abs(da) > 1e-9)
                {
                    ElementTransformUtils.RotateElement(doc, q.Id, Line.CreateBound(Ar.O, Ar.O + XYZ.BasisZ), da);
                    doc.Regenerate();
                }
                var Ar2 = MarcoDe(doc.GetElement(q.GetMemberIds().ToList()[rIdx]));
                var mov = Dr.O - Ar2.O;
                if (mov.GetLength() > 1e-9)
                {
                    ElementTransformUtils.MoveElement(doc, q.Id, mov);
                    doc.Regenerate();
                }
                // Verificacion: cada miembro con marco debe quedar donde corresponde
                var qf = q.GetMemberIds().ToList();
                double maxErr = 0, maxAng = 0; int comparados = 0;
                var errores = new List<Tuple<double, double, int>>();
                for (int i = 0; i < qf.Count; i++)
                {
                    if (deseados[i] == null) continue;
                    var a = MarcoDe(doc.GetElement(qf[i]));
                    if (a == null) continue;
                    comparados++;
                    var dif = a.O - deseados[i].O;
                    if (a.Curva)
                    {
                        // En elementos lineales (muros) las uniones mueven los extremos a lo largo
                        // del eje; solo cuenta la desviacion perpendicular.
                        var dir = new XYZ(Math.Cos(deseados[i].A), Math.Sin(deseados[i].A), 0);
                        dif = dif - dir * dif.DotProduct(dir);
                    }
                    double em = dif.GetLength() * MM;
                    double eg = Math.Abs(Norm(a.A - deseados[i].A)) * GRAD;
                    maxErr = Math.Max(maxErr, em);
                    maxAng = Math.Max(maxAng, eg);
                    if (em > tolMm || eg > 0.1) errores.Add(Tuple.Create(em, eg, i));
                }
                if (maxErr > tolMm || maxAng > 0.1)
                {
                    var peores = errores.OrderByDescending(x => x.Item1).Take(8).Select(x =>
                    {
                        var en = doc.GetElement(nuevos[x.Item3]);
                        return new { indice = x.Item3, elemento_editado = nuevos[x.Item3].IntegerValue, categoria = en.Category == null ? "" : en.Category.Name, error_mm = Math.Round(x.Item1, 1), error_grados = Math.Round(x.Item2, 2) };
                    }).ToList();
                    st.RollBack();
                    omitidas.Add(new { grupo = q.Id.IntegerValue, motivo = "la verificacion fallo", error_mm = Math.Round(maxErr, 1), error_grados = Math.Round(maxAng, 2), miembros_fuera = errores.Count, de = comparados, peores });
                    continue;
                }
                if (fijado) q.Pinned = true;
                st.Commit();
                instancias.Add(new { grupo = q.Id.IntegerValue, giro_grados = Math.Round(da * GRAD, 3), movido_mm = Math.Round(mov.GetLength() * MM, 1), miembros_verificados = comparados, error_max_mm = Math.Round(maxErr, 2) });
            }
        }

        bool todas = omitidas.Count == 0;
        if (todas)
        {
            doc.Delete(viejo.Id);
            nt.Name = nombreViejo;
        }
        res["tipo_nuevo"] = nt.Id.IntegerValue;
        res["nombre"] = nt.Name;
        res["tipo_anterior_borrado"] = todas;
        res["instancias_actualizadas"] = instancias;
        res["instancias_omitidas"] = omitidas;
        if (!todas) res["aviso"] = "Quedaron instancias con el tipo anterior: se conservaron los dos tipos.";
        return res;
    }

    static Dictionary<string, object> CambiarTipo(Document doc, List<ElementId> ids, JToken tipo)
    {
        var t = BuscarTipo(doc, tipo);
        var hechos = new List<int>();
        foreach (var id in ids)
        {
            var g = doc.GetElement(id) as Group;
            if (g == null) throw new ArgumentException("No es un grupo: " + id.IntegerValue);
            g.GroupType = t;
            hechos.Add(id.IntegerValue);
        }
        return new Dictionary<string, object> { { "tipo", t.Id.IntegerValue }, { "nombre", t.Name }, { "grupos", hechos } };
    }

    // ---------- utilidades ----------

    class Marco { public XYZ O; public double A; public bool Esp; public bool Curva; }

    // Marco local de un miembro: familias por su transformada; elementos con linea por su curva.
    static Marco MarcoDe(Element e)
    {
        if (e == null) return null;
        var fi = e as FamilyInstance;
        if (fi != null)
        {
            var t = fi.GetTransform();
            return new Marco { O = t.Origin, A = Math.Atan2(t.BasisX.Y, t.BasisX.X), Esp = fi.Mirrored };
        }
        var lc = e.Location as LocationCurve;
        if (lc != null && lc.Curve != null && lc.Curve.IsBound)
        {
            var p0 = lc.Curve.GetEndPoint(0);
            var d = lc.Curve.GetEndPoint(1) - p0;
            if (d.GetLength() < 1e-6) return null;
            return new Marco { O = p0, A = Math.Atan2(d.Y, d.X), Esp = false, Curva = true };
        }
        return null;
    }

    // Primer miembro que sirve de referencia; prefiere familias (informan si estan en espejo).
    static int IndiceReferencia(Document doc, List<ElementId> miembros)
    {
        for (int i = 0; i < miembros.Count; i++)
            if (doc.GetElement(miembros[i]) is FamilyInstance) return i;
        for (int i = 0; i < miembros.Count; i++)
            if (MarcoDe(doc.GetElement(miembros[i])) != null) return i;
        return -1;
    }

    // Lleva un punto del marco de la instancia editada (e) al de otra instancia (q).
    static XYZ Aplicar(XYZ w, Marco e, Marco q) { return q.O + Rot(w - e.O, q.A - e.A); }

    static XYZ Rot(XYZ v, double a)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        return new XYZ(c * v.X - s * v.Y, s * v.X + c * v.Y, v.Z);
    }

    static double Norm(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    static List<ElementId> Ids(JToken t)
    {
        if (t == null) return new List<ElementId>();
        if (t.Type == JTokenType.Array) return t.Select(x => new ElementId((int)x)).ToList();
        return new List<ElementId> { new ElementId((int)t) };
    }

    static GroupType BuscarTipo(Document doc, JToken t)
    {
        if (t == null) throw new ArgumentException("Falta el tipo de grupo.");
        GroupType gt = null;
        if (t.Type == JTokenType.Integer) gt = doc.GetElement(new ElementId((int)t)) as GroupType;
        else
        {
            string n = (string)t;
            gt = new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>().FirstOrDefault(x => x.Name == n);
        }
        if (gt == null) throw new ArgumentException("No existe el tipo de grupo: " + t);
        return gt;
    }

    static string NombreLibre(Document doc, string nombre)
    {
        var usados = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>().Select(x => x.Name));
        if (!usados.Contains(nombre)) return nombre;
        for (int i = 2; ; i++) if (!usados.Contains(nombre + " " + i)) return nombre + " " + i;
    }

    static object ConTransaccion(Document doc, string titulo, bool dryRun, Func<Dictionary<string, object>> f)
    {
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        using (var t = new Transaction(doc, titulo))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            var res = f();
            TransactionStatus st;
            if (dryRun) { t.RollBack(); st = TransactionStatus.RolledBack; }
            else st = t.Commit();
            res["dry_run"] = dryRun;
            res["estado"] = dryRun ? "simulado y deshecho" : st.ToString();
            res["avisos_revit"] = fallas.Messages;
            return res;
        }
    }
}
