// herramienta: limpiar-cad (bimwright-mcp)
// Lista y limpia CAD en el modelo: importaciones (DWG/DXF/DGN insertados), vinculos CAD
// (rotos o no) y patrones de linea "IMPORT-*" que dejan las importaciones. Reubica vinculos
// rotos buscando el archivo por nombre bajo una o varias carpetas.
// Se envia completa con revit_send_code_to_revit, con los parametros en `data`.
// Acciones: listar (solo lectura), reubicar y limpiar (dry_run por defecto true). Ver README.md.
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
        var doc = app.ActiveUIDocument.Document;
        string accion = (string)data["accion"] ?? "";
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];
        switch (accion)
        {
            case "listar": return Listar(doc);
            case "limpiar": return Limpiar(doc, data, dryRun);
            case "reubicar": return Reubicar(doc, data, dryRun);
            default: throw new ArgumentException("accion desconocida: '" + accion + "'. Use listar, reubicar o limpiar.");
        }
    }

    // ---------- lectura ----------

    static List<ImportInstance> Instancias(Document doc)
    {
        return new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
    }

    // El nombre de la instancia es la posicion ("location <Not Shared>"); el util es el del tipo.
    static string NombreArchivo(Document doc, ImportInstance i)
    {
        var t = doc.GetElement(i.GetTypeId());
        return t == null ? i.Name : t.Name;
    }

    static string Estado(CADLinkType t)
    {
        if (!t.IsExternalFileReference()) return "importado";
        try { return t.GetExternalFileReference().GetLinkedFileStatus().ToString(); }
        catch { return "desconocido"; }
    }

    static string Ruta(CADLinkType t)
    {
        if (!t.IsExternalFileReference()) return null;
        try { return ModelPathUtils.ConvertModelPathToUserVisiblePath(t.GetExternalFileReference().GetAbsolutePath()); }
        catch { return null; }
    }

    // GetLinkedFileStatus sigue diciendo "Loaded" aunque el archivo ya no exista (lo que hay en
    // memoria): se revisa el archivo en disco. En rutas de nube (ACC/BIM 360) no se puede: null.
    static bool? ArchivoExiste(CADLinkType t)
    {
        string r = Ruta(t);
        if (string.IsNullOrEmpty(r) || !System.IO.Path.IsPathRooted(r) || r.Contains("://")) return null;
        return System.IO.File.Exists(r);
    }

    static bool Roto(CADLinkType t)
    {
        return t.IsExternalFileReference() && (Estado(t) != "Loaded" || ArchivoExiste(t) == false);
    }

    // Patrones de linea usados por alguna categoria o subcategoria (proyeccion o corte).
    static HashSet<int> PatronesEnUso(Document doc)
    {
        var usados = new HashSet<int>();
        foreach (Category c in doc.Settings.Categories)
        {
            Marcar(c, usados);
            foreach (Category s in c.SubCategories) Marcar(s, usados);
        }
        return usados;
    }

    static void Marcar(Category c, HashSet<int> usados)
    {
        foreach (var gst in new[] { GraphicsStyleType.Projection, GraphicsStyleType.Cut })
        {
            try { usados.Add(c.GetLinePatternId(gst).IntegerValue); } catch { }
        }
    }

    static List<LinePatternElement> PatronesImport(Document doc)
    {
        return new FilteredElementCollector(doc).OfClass(typeof(LinePatternElement)).Cast<LinePatternElement>()
            .Where(p => p.Name.StartsWith("IMPORT", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    static object Listar(Document doc)
    {
        var inst = Instancias(doc);
        var porTipo = inst.GroupBy(i => i.GetTypeId().IntegerValue).ToDictionary(g => g.Key, g => g.Count());
        var tipos = new FilteredElementCollector(doc).OfClass(typeof(CADLinkType)).Cast<CADLinkType>().Select(t =>
        {
            int n; porTipo.TryGetValue(t.Id.IntegerValue, out n);
            return new { id = t.Id.IntegerValue, nombre = t.Name, estado = Estado(t), ruta = Ruta(t), archivo_existe = ArchivoExiste(t), roto = Roto(t), instancias = n };
        }).OrderBy(t => t.estado).ThenBy(t => t.nombre).ToList();
        var usados = PatronesEnUso(doc);
        var pats = PatronesImport(doc);
        return new
        {
            documento = doc.Title,
            importaciones = inst.Count(i => !i.IsLinked),
            vinculos = inst.Count(i => i.IsLinked),
            vinculos_rotos = tipos.Count(t => t.roto),
            tipos,
            instancias = inst.Select(i => new
            {
                id = i.Id.IntegerValue, nombre = NombreArchivo(doc, i), vinculado = i.IsLinked,
                solo_en_vista = i.OwnerViewId == ElementId.InvalidElementId ? null : doc.GetElement(i.OwnerViewId).Name,
                en_grupo = i.GroupId != ElementId.InvalidElementId, fijado = i.Pinned
            }).OrderBy(i => i.vinculado).ThenBy(i => i.nombre).ToList(),
            patrones_import = pats.Count,
            patrones_import_sin_uso = pats.Count(p => !usados.Contains(p.Id.IntegerValue)),
            patrones_ejemplos = pats.Take(10).Select(p => p.Name).ToList()
        };
    }

    // ---------- reubicar ----------

    // Vinculos rotos cuyo archivo esta en otra carpeta (tipico: ruta de otro PC o usuario).
    // Busca cada archivo por nombre bajo carpeta_base (admite %OneDriveCommercial% etc.) y, si
    // hay exactamente uno, recarga el vinculo desde ahi. Con varios o ninguno, lo informa.
    static object Reubicar(Document doc, JObject data, bool dryRun)
    {
        // carpeta_base: una ruta o una lista (p. ej. la sincronizacion de SharePoint y OneDrive).
        var tok = data["carpeta_base"];
        var bases = (tok is JArray ? ((JArray)tok).Select(x => (string)x) : new[] { (string)tok ?? "" })
            .Select(Environment.ExpandEnvironmentVariables).ToList();
        var faltan = bases.Where(b => string.IsNullOrEmpty(b) || !System.IO.Directory.Exists(b)).ToList();
        if (faltan.Count > 0) throw new ArgumentException("carpeta_base no existe: '" + string.Join("', '", faltan) + "'.");
        var nombres = new HashSet<string>((data["vinculos"] as JArray ?? new JArray()).Select(x => (string)x));
        var rotos = new FilteredElementCollector(doc).OfClass(typeof(CADLinkType)).Cast<CADLinkType>()
            .Where(t => Roto(t) && (nombres.Count == 0 || nombres.Contains(t.Name))).ToList();

        // Un solo recorrido de la carpeta: nombre de archivo -> rutas.
        var buscados = new HashSet<string>(rotos.Select(t => System.IO.Path.GetFileName(Ruta(t) ?? t.Name)), StringComparer.OrdinalIgnoreCase);
        var hallados = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var pendientes = new Stack<string>(bases);
        int carpetas = 0;
        while (pendientes.Count > 0)
        {
            string d = pendientes.Pop();
            carpetas++;
            try
            {
                foreach (var f in System.IO.Directory.EnumerateFiles(d))
                {
                    string n = System.IO.Path.GetFileName(f);
                    if (!buscados.Contains(n)) continue;
                    List<string> l;
                    if (!hallados.TryGetValue(n, out l)) { l = new List<string>(); hallados[n] = l; }
                    l.Add(f);
                }
                foreach (var s in System.IO.Directory.EnumerateDirectories(d)) pendientes.Push(s);
            }
            catch { } // carpetas sin permiso
        }

        var resultado = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        TransactionStatus estado;
        using (var t = new Transaction(doc, "Reubicar vinculos CAD"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            foreach (var tipo in rotos)
            {
                string archivo = System.IO.Path.GetFileName(Ruta(tipo) ?? tipo.Name);
                List<string> rutas;
                hallados.TryGetValue(archivo, out rutas);
                if (rutas == null || rutas.Count == 0) { resultado.Add(new { vinculo = tipo.Name, resultado = "no encontrado bajo la carpeta base" }); continue; }
                if (rutas.Count > 1) { resultado.Add(new { vinculo = tipo.Name, resultado = "varios candidatos; indique cual", candidatos = rutas.Take(5).ToList() }); continue; }
                try
                {
                    var r = tipo.LoadFrom(rutas[0]);
                    resultado.Add(new { vinculo = tipo.Name, resultado = r.LoadResult.ToString(), ruta_nueva = rutas[0] });
                }
                catch (Exception ex) { resultado.Add(new { vinculo = tipo.Name, resultado = "error: " + ex.Message, ruta_nueva = rutas[0] }); }
            }
            estado = dryRun ? t.RollBack() : t.Commit();
        }
        return new { dry_run = dryRun, transaccion = estado.ToString(), carpetas_base = bases, carpetas_revisadas = carpetas, vinculos_rotos = rotos.Count, resultado, avisos_revit = fallas.Messages };
    }

    // ---------- limpiar ----------

    // importados: true borra las importaciones (no vinculos). vinculos_rotos: true borra los tipos
    // de vinculo que no estan cargados (con sus instancias). vinculos: [nombres] borra esos vinculos.
    // patrones_import: true borra los patrones IMPORT-* que no usa ninguna categoria.
    static object Limpiar(Document doc, JObject data, bool dryRun)
    {
        bool importados = data["importados"] != null && (bool)data["importados"];
        bool rotos = data["vinculos_rotos"] != null && (bool)data["vinculos_rotos"];
        var nombres = new HashSet<string>((data["vinculos"] as JArray ?? new JArray()).Select(x => (string)x));
        bool patrones = data["patrones_import"] != null && (bool)data["patrones_import"];
        if (!importados && !rotos && nombres.Count == 0 && !patrones)
            throw new ArgumentException("Indique que limpiar: importados, vinculos_rotos, vinculos y/o patrones_import.");

        var log = new Dictionary<string, object>();
        var omitidos = new List<object>();
        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        using (var t = new Transaction(doc, "Limpiar CAD"))
        {
            var o = t.GetFailureHandlingOptions();
            o.SetFailuresPreprocessor(fallas);
            t.SetFailureHandlingOptions(o);
            t.Start();
            var inst = Instancias(doc);
            // Elementos en grupos: borrarlos fuera del modo edicion de grupo deshace todo al confirmar.
            var enGrupo = new HashSet<int>(inst.Where(i => i.GroupId != ElementId.InvalidElementId).Select(i => i.Id.IntegerValue));

            var borrarInst = new List<ImportInstance>();
            if (importados)
                foreach (var i in inst.Where(i => !i.IsLinked))
                    if (enGrupo.Contains(i.Id.IntegerValue)) omitidos.Add(new { id = i.Id.IntegerValue, nombre = NombreArchivo(doc, i), motivo = "en grupo" });
                    else borrarInst.Add(i);

            var borrarTipos = new List<CADLinkType>();
            foreach (var tipo in new FilteredElementCollector(doc).OfClass(typeof(CADLinkType)).Cast<CADLinkType>())
            {
                if (!tipo.IsExternalFileReference()) continue;
                bool porNombre = nombres.Contains(tipo.Name);
                if (!porNombre && !(rotos && Roto(tipo))) continue;
                var suyas = inst.Where(i => i.GetTypeId() == tipo.Id).ToList();
                if (suyas.Any(i => enGrupo.Contains(i.Id.IntegerValue)))
                { omitidos.Add(new { id = tipo.Id.IntegerValue, nombre = tipo.Name, motivo = "tiene instancias en grupos" }); continue; }
                borrarTipos.Add(tipo);
            }

            // Tipos de importacion que quedan sin instancias tambien se borran.
            var tiposImportSueltos = new List<ElementId>();
            if (importados)
            {
                var quedan = new HashSet<int>(inst.Where(i => !borrarInst.Contains(i)).Select(i => i.GetTypeId().IntegerValue));
                foreach (var i in borrarInst)
                {
                    var tid = i.GetTypeId();
                    var tipo = doc.GetElement(tid) as CADLinkType;
                    if (tipo != null && !tipo.IsExternalFileReference() && !quedan.Contains(tid.IntegerValue) && !tiposImportSueltos.Contains(tid))
                        tiposImportSueltos.Add(tid);
                }
            }

            var borrados = new List<object>();
            var ids = new List<ElementId>();
            foreach (var i in borrarInst) { ids.Add(i.Id); borrados.Add(new { id = i.Id.IntegerValue, nombre = NombreArchivo(doc, i), que = "importacion" }); }
            foreach (var tipo in borrarTipos) { ids.Add(tipo.Id); borrados.Add(new { id = tipo.Id.IntegerValue, nombre = tipo.Name, que = Roto(tipo) ? "vinculo roto" : "vinculo" }); }
            ids.AddRange(tiposImportSueltos);
            int total = 0;
            if (ids.Count > 0) total = doc.Delete(ids).Count;
            doc.Regenerate();

            var patBorrados = new List<string>();
            if (patrones)
            {
                var usados = PatronesEnUso(doc);
                var libres = PatronesImport(doc).Where(p => !usados.Contains(p.Id.IntegerValue)).ToList();
                patBorrados = libres.Select(p => p.Name).ToList();
                if (libres.Count > 0) total += doc.Delete(libres.Select(p => p.Id).ToList()).Count;
            }

            log["borrados"] = borrados;
            log["tipos_de_importacion_sin_uso_borrados"] = tiposImportSueltos.Count;
            log["patrones_borrados"] = patBorrados.Count;
            log["patrones_ejemplos"] = patBorrados.Take(10).ToList();
            log["omitidos"] = omitidos;
            log["elementos_eliminados_en_total"] = total;
            var estado = dryRun ? t.RollBack() : t.Commit();
            log["dry_run"] = dryRun;
            log["transaccion"] = estado.ToString();
            log["avisos_revit"] = fallas.Messages;
        }
        return log;
    }
}
