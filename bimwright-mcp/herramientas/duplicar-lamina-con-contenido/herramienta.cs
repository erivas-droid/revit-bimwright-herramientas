// herramienta: duplicar-lamina-con-contenido (bimwright-mcp)
// Completa lo que revit_duplicate_sheet de bimwright no copia: las anotaciones propias de la
// lamina (timbres, textos, lineas, imagenes), el tipo y la posicion del titulo de cada ventana
// grafica y los parametros de la lamina (incluidos los de proyecto).
// Uso: primero revit_duplicate_sheet; despues esta herramienta con origen y destino.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Accion unica: completar (dry_run por defecto true). Ver README.md.
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
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        bool hacerAnot = data["anotaciones"] == null || (bool)data["anotaciones"];
        bool hacerVps = data["ventanas"] == null || (bool)data["ventanas"];
        bool hacerParams = data["parametros"] == null || (bool)data["parametros"];
        bool forzar = data["forzar"] != null && (bool)data["forzar"];
        var origen = Hoja(doc, data["origen"]);
        var destino = Hoja(doc, data["destino"]);
        if (origen.Id == destino.Id) throw new ArgumentException("origen y destino son la misma lamina.");

        var salida = new Dictionary<string, object>
        {
            { "dry_run", dryRun },
            { "origen", origen.SheetNumber + " - " + origen.Name },
            { "destino", destino.SheetNumber + " - " + destino.Name }
        };
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        using (var t = new Transaction(doc, "Completar lamina duplicada"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            if (hacerAnot) salida["anotaciones"] = Anotaciones(doc, origen, destino, forzar);
            if (hacerVps) salida["ventanas"] = Ventanas(doc, origen, destino);
            if (hacerParams) salida["parametros"] = Parametros(origen, destino);
            salida["transaccion"] = (dryRun ? t.RollBack() : t.Commit()).ToString();
        }
        return salida;
    }

    static ViewSheet Hoja(Document doc, JToken tok)
    {
        if (tok == null) throw new ArgumentException("Faltan 'origen' y 'destino' (id o numero de lamina).");
        ViewSheet s = null;
        if (tok.Type == JTokenType.Integer) s = doc.GetElement(new ElementId((int)tok)) as ViewSheet;
        else s = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .FirstOrDefault(x => x.SheetNumber == (string)tok);
        if (s == null) throw new ArgumentException("No existe la lamina '" + tok + "'.");
        return s;
    }

    // Elementos propios de la lamina que vale la pena copiar: con categoria, sin cajetin,
    // ventanas graficas ni tablas (esas las pone revit_duplicate_sheet). Se excluyen las vistas
    // propias de la lamina (la tabla de revisiones del cajetin es una ViewSchedule de cada una).
    static List<Element> Propios(Document doc, ViewSheet s)
    {
        return new FilteredElementCollector(doc).OwnedByView(s.Id).WhereElementIsNotElementType().ToElements()
            .Where(e => !(e is Viewport) && !(e is ScheduleSheetInstance) && !(e is View) && e.Category != null
                && e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_TitleBlocks
                && e.Category.Id.IntegerValue != (int)BuiltInCategory.OST_Viewports
                && !(e is SketchPlane) && !(e is GraphicsStyle))
            .ToList();
    }

    static object Anotaciones(Document doc, ViewSheet origen, ViewSheet destino, bool forzar)
    {
        var src = Propios(doc, origen);
        var ya = Propios(doc, destino);
        var porCat = src.GroupBy(e => e.Category.Name).ToDictionary(g => g.Key, g => g.Count());
        if (src.Count == 0) return new { copiadas = 0, detalle = porCat };
        if (ya.Count > 0 && !forzar)
            return new
            {
                copiadas = 0, detalle = porCat,
                aviso = "El destino ya tiene " + ya.Count + " anotaciones propias; no se copia para no duplicar. Usar forzar=true."
            };
        var opts = new CopyPasteOptions();
        var nuevos = ElementTransformUtils.CopyElements(origen, src.Select(e => e.Id).ToList(), destino, Transform.Identity, opts);
        return new
        {
            copiadas = src.Count, detalle = porCat,
            nuevos_ids = nuevos.Select(i => i.IntegerValue).ToList()
        };
    }

    // Empareja cada ventana del destino con la del origen: misma vista (leyendas) o, si la
    // vista fue duplicada, el centro mas cercano (revit_duplicate_sheet conserva la posicion).
    static object Ventanas(Document doc, ViewSheet origen, ViewSheet destino)
    {
        var vo = origen.GetAllViewports().Select(i => (Viewport)doc.GetElement(i)).ToList();
        var vd = destino.GetAllViewports().Select(i => (Viewport)doc.GetElement(i)).ToList();
        var usados = new HashSet<ElementId>();
        var cambios = new List<object>();
        var sinPar = new List<string>();
        foreach (var d in vd)
        {
            var o = vo.FirstOrDefault(x => x.ViewId == d.ViewId && !usados.Contains(x.Id))
                ?? vo.Where(x => !usados.Contains(x.Id))
                     .OrderBy(x => x.GetBoxCenter().DistanceTo(d.GetBoxCenter()))
                     .FirstOrDefault(x => x.GetBoxCenter().DistanceTo(d.GetBoxCenter()) * MM < 5);
            string vista = doc.GetElement(d.ViewId).Name;
            if (o == null) { sinPar.Add(vista); continue; }
            usados.Add(o.Id);
            var hecho = new List<string>();
            if (d.GetTypeId() != o.GetTypeId())
            {
                hecho.Add("tipo: " + doc.GetElement(d.GetTypeId()).Name + " -> " + doc.GetElement(o.GetTypeId()).Name);
                d.ChangeTypeId(o.GetTypeId());
            }
            if (!d.LabelOffset.IsAlmostEqualTo(o.LabelOffset)) { d.LabelOffset = o.LabelOffset; hecho.Add("posicion del titulo"); }
            if (Math.Abs(d.LabelLineLength - o.LabelLineLength) > 1e-6) { d.LabelLineLength = o.LabelLineLength; hecho.Add("largo de la linea del titulo"); }
            if (d.Rotation != o.Rotation) { d.Rotation = o.Rotation; hecho.Add("rotacion"); }
            if (hecho.Count > 0) cambios.Add(new { vista, cambios = hecho });
        }
        return new { revisadas = vd.Count, cambiadas = cambios.Count, detalle = cambios, sin_par = sinPar };
    }

    // Copia los parametros escribibles de la lamina (salvo numero y nombre). De los que apuntan a
    // otro elemento solo copia la reticula guia: los demas son internos (plano de trabajo, tipo).
    static object Parametros(ViewSheet origen, ViewSheet destino)
    {
        var copiados = new Dictionary<string, string>();
        var fallidos = new Dictionary<string, string>();
        foreach (Parameter p in origen.Parameters)
        {
            if (p.IsReadOnly || !p.HasValue) continue;
            var bip = (p.Definition as InternalDefinition)?.BuiltInParameter ?? BuiltInParameter.INVALID;
            if (bip == BuiltInParameter.SHEET_NUMBER || bip == BuiltInParameter.SHEET_NAME) continue;
            if (p.StorageType == StorageType.ElementId && bip != BuiltInParameter.SHEET_GUIDE_GRID) continue;
            var q = destino.get_Parameter(p.Definition);
            if (q == null || q.IsReadOnly) continue;
            try
            {
                bool igual;
                switch (p.StorageType)
                {
                    case StorageType.String: igual = q.AsString() == p.AsString(); if (!igual) q.Set(p.AsString() ?? ""); break;
                    case StorageType.Integer: igual = q.HasValue && q.AsInteger() == p.AsInteger(); if (!igual) q.Set(p.AsInteger()); break;
                    case StorageType.Double: igual = q.HasValue && Math.Abs(q.AsDouble() - p.AsDouble()) < 1e-9; if (!igual) q.Set(p.AsDouble()); break;
                    case StorageType.ElementId: igual = q.AsElementId() == p.AsElementId(); if (!igual) q.Set(p.AsElementId()); break;
                    default: igual = true; break;
                }
                if (!igual) copiados[p.Definition.Name] = p.AsValueString() ?? p.AsString();
            }
            catch (Exception ex) { fallidos[p.Definition.Name] = ex.Message; }
        }
        return new { copiados, fallidos };
    }
}
