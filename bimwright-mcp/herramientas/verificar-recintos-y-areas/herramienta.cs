// herramienta: verificar-recintos-y-areas (bimwright-mcp)
// Control de calidad espacial de Rooms y Areas: detecta elementos sin ubicar, no cerrados,
// redundantes, numeros duplicados, nombres vacios y compara sumatorias de superficie entre
// Rooms y Areas por nivel.
// Acciones: auditar (solo lectura, admite `documento`) y limpiar_sin_ubicar (dry_run por defecto true).
// Ver README.md de esta carpeta.
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

public class McpDynamicScript
{
    const double FT2_TO_M2 = 0.09290304;

    public static object Run(UIApplication app, JObject data)
    {
        if (data == null) data = new JObject();
        string accion = ((string)data["accion"] ?? "auditar").ToLowerInvariant();
        var activo = app.ActiveUIDocument == null ? null : app.ActiveUIDocument.Document;
        var doc = ResolverDocumento(app, (string)data["documento"]);
        bool dryRun = data["dry_run"] == null || (bool)data["dry_run"];

        switch (accion)
        {
            case "auditar":
                return Auditar(doc, data);
            case "limpiar_sin_ubicar":
                if (activo == null || !doc.Equals(activo))
                    throw new InvalidOperationException("limpiar_sin_ubicar solo trabaja sobre el documento activo.");
                return LimpiarSinUbicar(doc, data, dryRun);
            default:
                throw new ArgumentException("accion desconocida: '" + accion + "'. Use auditar o limpiar_sin_ubicar.");
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

    // ---------- auditar ----------

    static object Auditar(Document doc, JObject data)
    {
        string esquema = (string)data["esquema"];
        string nivel = (string)data["nivel"];
        int maxMuestra = data["max_detalle"] == null ? 30 : (int)data["max_detalle"];

        // 1. Rooms
        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>()
            .Where(r => string.IsNullOrEmpty(nivel) || (r.Level != null && string.Equals(r.Level.Name, nivel, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var roomsSinUbicar = new List<object>();
        var roomsNoCerrados = new List<object>();
        var roomsSinNombre = new List<object>();
        var roomsPorNivel = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var roomsPorNumero = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        int roomsOk = 0;
        foreach (var r in rooms)
        {
            string niv = r.Level == null ? "(Sin nivel)" : r.Level.Name;
            string nom = Texto(r.get_Parameter(BuiltInParameter.ROOM_NAME));
            string num = r.Number ?? "";

            if (r.Location == null)
            {
                roomsSinUbicar.Add(new { id = r.Id.IntegerValue, nivel = niv, numero = num, nombre = nom, en_grupo = r.GroupId != ElementId.InvalidElementId });
                continue;
            }
            if (r.Area <= 1e-6)
            {
                roomsNoCerrados.Add(new { id = r.Id.IntegerValue, nivel = niv, numero = num, nombre = nom, en_grupo = r.GroupId != ElementId.InvalidElementId });
                continue;
            }
            roomsOk++;
            double cur; roomsPorNivel.TryGetValue(niv, out cur); roomsPorNivel[niv] = cur + r.Area * FT2_TO_M2;
            if (string.IsNullOrWhiteSpace(nom))
                roomsSinNombre.Add(new { id = r.Id.IntegerValue, nivel = niv, numero = num });
            if (!string.IsNullOrWhiteSpace(num))
            {
                string claveNum = niv + "|" + num;
                List<int> l;
                if (!roomsPorNumero.TryGetValue(claveNum, out l)) { l = new List<int>(); roomsPorNumero[claveNum] = l; }
                l.Add(r.Id.IntegerValue);
            }
        }
        var roomsDupNum = roomsPorNumero.Where(kv => kv.Value.Count > 1)
            .Select(kv => new { clave = kv.Key, cantidad = kv.Value.Count, ids = kv.Value.Take(5).ToList() })
            .OrderByDescending(x => x.cantidad).ToList();

        // 2. Areas
        var areas = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().Cast<Area>()
            .Where(a => string.IsNullOrEmpty(esquema) || (a.AreaScheme != null && string.Equals(a.AreaScheme.Name, esquema, StringComparison.OrdinalIgnoreCase)))
            .Where(a => string.IsNullOrEmpty(nivel) || (a.Level != null && string.Equals(a.Level.Name, nivel, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var areasSinUbicar = new List<object>();
        var areasNoCerradas = new List<object>();
        var areasPorEsquemaNivel = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        int areasOk = 0;

        foreach (var a in areas)
        {
            string esq = a.AreaScheme == null ? "(Sin esquema)" : a.AreaScheme.Name;
            string niv = a.Level == null ? "(Sin nivel)" : a.Level.Name;
            string nom = Texto(a.get_Parameter(BuiltInParameter.ROOM_NAME));
            string num = a.Number ?? "";

            if (a.Location == null)
            {
                areasSinUbicar.Add(new { id = a.Id.IntegerValue, esquema = esq, nivel = niv, numero = num, nombre = nom });
                continue;
            }
            if (a.Area <= 1e-6)
            {
                areasNoCerradas.Add(new { id = a.Id.IntegerValue, esquema = esq, nivel = niv, numero = num, nombre = nom });
                continue;
            }
            areasOk++;
            string k = esq + "||" + niv;
            double cur; areasPorEsquemaNivel.TryGetValue(k, out cur); areasPorEsquemaNivel[k] = cur + a.Area * FT2_TO_M2;
        }

        // 3. Contraste Rooms vs Areas por nivel (si se indico esquema o si hay un solo esquema)
        var contraste = new List<object>();
        var esquemasPresentes = areas.Where(a => a.AreaScheme != null).Select(a => a.AreaScheme.Name).Distinct().OrderBy(x => x).ToList();
        var nivelesOrdenados = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.ProjectElevation).Select(l => l.Name).ToList();

        foreach (var esq in esquemasPresentes)
        {
            foreach (var niv in nivelesOrdenados)
            {
                double aArea = 0; areasPorEsquemaNivel.TryGetValue(esq + "||" + niv, out aArea);
                double rArea = 0; roomsPorNivel.TryGetValue(niv, out rArea);
                if (aArea <= 1e-6 && rArea <= 1e-6) continue;
                double dif = aArea - rArea;
                double pct = aArea > 1e-6 ? (dif / aArea) * 100.0 : 0;
                contraste.Add(new
                {
                    esquema = esq,
                    nivel = niv,
                    areas_m2 = Math.Round(aArea, 2),
                    recintos_m2 = Math.Round(rArea, 2),
                    diferencia_m2 = Math.Round(dif, 2),
                    diferencia_pct = Math.Round(pct, 1)
                });
            }
        }

        return new
        {
            documento = doc.Title,
            filtro_esquema = esquema,
            filtro_nivel = nivel,
            recintos = new
            {
                total = rooms.Count,
                ok = roomsOk,
                sin_ubicar = roomsSinUbicar.Count,
                no_cerrados_o_redundantes = roomsNoCerrados.Count,
                sin_nombre = roomsSinNombre.Count,
                numeros_duplicados_en_nivel = roomsDupNum.Count,
                muestra_sin_ubicar = roomsSinUbicar.Take(maxMuestra).ToList(),
                muestra_no_cerrados = roomsNoCerrados.Take(maxMuestra).ToList(),
                muestra_sin_nombre = roomsSinNombre.Take(maxMuestra).ToList(),
                muestra_duplicados = roomsDupNum.Take(maxMuestra).ToList()
            },
            areas = new
            {
                total = areas.Count,
                ok = areasOk,
                sin_ubicar = areasSinUbicar.Count,
                no_cerradas_o_redundantes = areasNoCerradas.Count,
                muestra_sin_ubicar = areasSinUbicar.Take(maxMuestra).ToList(),
                muestra_no_cerradas = areasNoCerradas.Take(maxMuestra).ToList()
            },
            contraste_areas_vs_recintos = contraste
        };
    }

    // ---------- limpiar_sin_ubicar ----------

    static object LimpiarSinUbicar(Document doc, JObject data, bool dryRun)
    {
        bool incluirRooms = data["recintos"] == null || (bool)data["recintos"];
        bool incluirAreas = data["areas"] == null || (bool)data["areas"];
        string esquema = (string)data["esquema"];

        var borrarIds = new List<ElementId>();
        var omitidos = new List<object>();
        int cntRooms = 0, cntAreas = 0;

        if (incluirRooms)
        {
            foreach (var r in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().Cast<Room>()
                .Where(r => r.Location == null))
            {
                if (r.GroupId != ElementId.InvalidElementId)
                {
                    omitidos.Add(new { tipo = "Room", id = r.Id.IntegerValue, motivo = "en grupo" });
                    continue;
                }
                borrarIds.Add(r.Id);
                cntRooms++;
            }
        }

        if (incluirAreas)
        {
            foreach (var a in new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Areas).WhereElementIsNotElementType().Cast<Area>()
                .Where(a => a.Location == null)
                .Where(a => string.IsNullOrEmpty(esquema) || (a.AreaScheme != null && string.Equals(a.AreaScheme.Name, esquema, StringComparison.OrdinalIgnoreCase))))
            {
                if (a.GroupId != ElementId.InvalidElementId)
                {
                    omitidos.Add(new { tipo = "Area", id = a.Id.IntegerValue, motivo = "en grupo" });
                    continue;
                }
                borrarIds.Add(a.Id);
                cntAreas++;
            }
        }

        var fallas = new RvtMcp.Plugin.SafeFailuresPreprocessor();
        int eliminados = 0;
        TransactionStatus estado = TransactionStatus.Uninitialized;
        if (borrarIds.Count > 0)
        {
            using (var tr = new Transaction(doc, "Limpiar recintos y areas sin ubicar"))
            {
                var o = tr.GetFailureHandlingOptions();
                o.SetFailuresPreprocessor(fallas);
                tr.SetFailureHandlingOptions(o);
                tr.Start();
                eliminados = doc.Delete(borrarIds).Count;
                estado = dryRun ? tr.RollBack() : tr.Commit();
            }
        }

        return new
        {
            accion = "limpiar_sin_ubicar",
            dry_run = dryRun,
            transaccion = borrarIds.Count == 0 ? "sin elementos que borrar" : estado.ToString(),
            recintos_sin_ubicar = cntRooms,
            areas_sin_ubicar = cntAreas,
            elementos_eliminados_en_total = eliminados,
            omitidos_en_grupo = omitidos.Count,
            avisos_revit = fallas.Messages
        };
    }

    static string Texto(Parameter p)
    {
        if (p == null || !p.HasValue) return "";
        return p.StorageType == StorageType.String ? (p.AsString() ?? "") : (p.AsValueString() ?? "");
    }
}
