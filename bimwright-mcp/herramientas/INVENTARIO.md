# Inventario de herramientas de bimwright

Generado el 2026-11-11 desde el propio servidor (`tools/list` por MCP), bimwright **v1.2.2**
(build `6cfdfe5`). Total: **242** herramientas con `--toolsets all`.
Las descripciones son las del servidor (en inglés), recortadas a la primera oración.

- **Activo:** el toolset está en la configuración actual (`rvtmcp.config.json`): `query`, `create`, `view`, `meta`, `annotation`, `sheets`, `schedule`, `parameters`, `modify`, `families`, `graphics`, `rooms`, `lint`.
- **Lectura:** `readOnlyHint` del servidor (sí = no modifica el modelo). **Destr.:** `destructiveHint`.
- Cómo regenerarlo: pedir `tools/list` al servidor (`rvt-mcp.exe`), una vez con `--toolsets all` y otra por cada toolset.
- Los toolsets `structural` y `workflows` no aparecen en la tabla del README de bimwright.

## Resumen por toolset

| Toolset | Herramientas | Activo |
|---|---:|---|
| `query` | 17 | sí |
| `create` | 7 | sí |
| `view` | 8 | sí |
| `meta` | 23 | sí |
| `lint` | 4 | sí |
| `schedule` | 10 | sí |
| `families` | 10 | sí |
| `modify` | 6 | sí |
| `delete` | 1 | no |
| `annotation` | 14 | sí |
| `export` | 16 | no |
| `mep` | 16 | no |
| `graphics` | 12 | sí |
| `toolbaker` | 6 | no |
| `sheets` | 14 | sí |
| `materials` | 10 | no |
| `geometry` | 12 | no |
| `rooms` | 10 | sí |
| `links` | 12 | no |
| `parameters` | 8 | sí |
| `organization` | 10 | no |
| `structural` | 12 | no |
| `workflows` | 8 | no |

## `query`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `ai_element_filter` | sí |  | Filter elements by category + parameter. |
| `analyze_model_statistics` | sí |  | Count elements grouped by category (Walls, Doors, Pipes, etc.). |
| `get_assembly_members` | sí |  | Read an assembly instance and a page of members. |
| `get_available_family_types` | sí |  | List loadable family types. |
| `get_current_view_info` | sí |  | Read the active view's viewName, viewType (FloorPlan/Section/3D/Sheet), level, scale, detailLevel and displayStyle. |
| `get_element_details` | sí |  | Read detailed metadata for one or more elements. |
| `get_element_parameters` | sí |  | Read instance parameters for one or more elements. |
| `get_element_relationships` | sí |  | Read host, group, assembly, owner view, design option, family nesting, and dependent-element relationships for elements. |
| `get_group_members` | sí |  | Read a group instance and a page of member elements. |
| `get_material_quantities` | sí |  | Sum material quantities (area m², volume m³) by category. |
| `get_selected_elements` | sí |  | Get currently selected Revit elements with paging. |
| `get_type_parameters` | sí |  | Read type parameters from explicit type ids or from the types of provided element ids. |
| `list_assemblies` | sí |  | List a page of assembly instances. |
| `list_groups` | sí |  | List model/detail/attached groups with type, owner view, parent, and optional member ids. |
| `list_project_parameters` | sí |  | List project/shared parameter bindings, including instance/type binding kind and bound categories. |
| `list_worksets` | sí |  | List document worksets and active workset. |
| `survey_change_impact` | sí |  | Survey ten relationship groups for explicit active-document targets before proposing a change. |

## `create`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `create_grid` | no |  | Create a grid line. |
| `create_group_from_elements` | no |  | Create a Revit group from two or more element ids. |
| `create_level` | no |  | Create a level at specified elevation. |
| `create_line_based_element` | no |  | Create a line-based element (wall). |
| `create_point_based_element` | no |  | Create a OneLevelBased or OneLevelBasedHosted family (door, window, furniture). |
| `create_room` | no |  | Create and place a room. |
| `create_surface_based_element` | no |  | Create a surface-based element (floor, ceiling). |

## `view`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `activate_view` | sí |  | Set the active view in Revit UI. |
| `analyze_sheet_layout` | sí |  | Analyze title block and a bounded viewport page in mm. |
| `capture_view_image` | no | sí | Export the active or specified view as png/jpeg (default 1600 pixels), optionally to an absolute output_path inside %TEMP% or %LOCALAPPDATA%\Bimwright\rvt-mcp\captures\. |
| `create_view` | no |  | Create a view (floorplan or 3d). |
| `place_view_on_sheet` | no |  | Place a view on a sheet. |
| `set_view_crop` | no | sí | Replace saved view crop settings; restore them with Revit Undo. |
| `set_view_scale` | no | sí | Replace a saved view scale; restore it with Revit Undo. |
| `show_element_in_view` | sí |  | Activate view, select elements, zoom to elements. |

## `meta`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `analyze_usage_patterns` | sí |  | Analyze MCP tool usage. |
| `batch_execute` | no | sí | Run multiple MCP commands atomically inside one Revit TransactionGroup (single undo on success). |
| `delete_code_module` | no | sí | Delete an unused module head from local storage without Revit. _(también en `toolbaker`)_ |
| `get_change_records` | sí |  | Query local per-model MCP change history by server-issued modelKey, optional elementId or uniqueId, ISO UTC date range and limit (1-100). |
| `get_current_target` | sí |  | Read the next command's Revit binding, connected pid, generation and confirmation state. |
| `get_failure_records` | sí |  | Read the local failure ledger without dispatching Revit; works while native UI is modal and with history or send-code disabled. |
| `get_operation_status` | sí |  | Read an issued operationId's local execution/outcome receipts while Revit is modal; finalResult.data is available while retained, and waitSeconds (0-90, default 0) waits without holding Revit. |
| `get_report_history` | sí |  | Read exact SQLite History for a saved Agent Report: capture call IDs, record IDs, reasons and before/after. |
| `health` | sí |  | Read gateway/plugin identity and last observed host state after selecting a target; defaults offline, with check_updates=true opting into a release lookup. |
| `list_available_targets` | sí |  | List discovered live Revit instances with pid, window_title, target identity and connection state. |
| `list_code_modules` | sí |  | List stored C# helper modules without Revit; use before reusing, replacing or deleting a module. _(también en `toolbaker`)_ |
| `list_recent_models` | sí |  | List up to 50 recent model paths from this Revit's Revit.ini, most recent first, even without an open document. |
| `lookup_message` | sí |  | Explain a Revit dialog or failure code from the catalogue of the bound Revit version. |
| `open_model` | no |  | Open a confirmed absolute path on the Revit machine, including when no document is open; older files may upgrade in memory, reaching disk only if saved. |
| `purge_unused` | no | sí | Delete unused family symbols; restore them with Revit Undo. |
| `record_change` | no |  | Attach the user's reason to explicit server-issued change callIds in one model's local history. |
| `report_task_result` | sí |  | Report the finished task to the Revit user in the Reports window. |
| `resolve_history_identity` | no | sí | Record the owner's explicit choice for the active saved model's local history after Save As, a copy or an uncertain path change. |
| `save_code_module` | no | sí | Save a reusable C# declarations-only helper after compile-only validation on the pinned capable Revit host. _(también en `toolbaker`)_ |
| `send_code_to_revit` | ? |  | Full-trust C#; confirm edits, prefer typed tools. _(también en `toolbaker`)_ |
| `set_project_info` | no | sí | Replace project information fields; restore them with Revit Undo. |
| `show_message` | sí |  | Show a Revit TaskDialog. |
| `switch_target` | no |  | Pin an instance listed by revit_list_available_targets using pid:<n>, id:revit-<year>-<pid>, auto or a calendar year (never R-codes); verify=true confirms the document for writes. |

## `lint`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `analyze_view_naming_patterns` | sí |  | Infer naming patterns with bounded detail. |
| `detect_firm_profile` | sí |  | Fingerprint project naming (views + sheets + levels), match against firm-profile library. |
| `get_model_warnings_summary` | sí |  | Group warnings by description with bounded types/examples. |
| `suggest_view_name_corrections` | sí |  | Propose corrected view names for outliers. |

## `schedule`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `add_schedule_field` | no |  | Add one new field to an existing schedule. |
| `apply_schedule_filter_sort` | no | sí | Replace schedule filters and settings; restore them with Revit Undo. |
| `create_schedule` | no |  | Create a new schedule from a declarative spec. |
| `find_schedule_elements` | sí |  | Find Revit elements aggregated by a schedule (using FilteredElementCollector scoped to the schedule's id). |
| `get_schedulable_fields` | sí |  | List parameters that CAN be added as fields to a schedule but have not been added yet. |
| `get_schedule_data` | sí |  | Get the rendered tabular content of a schedule (header row + body rows) with pagination. |
| `get_schedule_definition` | sí |  | Get the full structural definition of a schedule: fields (parameter/formula/combined), filters, sort/group, and settings. |
| `get_schedule_formulas` | sí |  | Extract all calculated (formula) and combined-parameter fields from a schedule, with parsed formula dependencies. |
| `list_schedules` | sí |  | List all schedules in the project. |
| `update_schedule_field` | no | sí | Modify existing schedule fields; restore them with Revit Undo. |

## `families`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `audit_families` | sí |  | Read-only family audit with bounded sections. |
| `duplicate_family_type` | no |  | Duplicate a FamilySymbol or system type within its family under newTypeName, optionally setting type parameter overrides (JSON object as string, parameter name → value). |
| `export_family_to_path` | no | sí | Save a loadable family from the current project back to an .rfa file at outputPath. |
| `get_family_instances` | sí |  | List placed instances of a Family (or a specific type within it) with location/host/level DTOs in mm. |
| `list_family_types_in_family` | sí |  | List a bounded page of types in one family. |
| `list_loaded_families` | sí |  | List all loaded families (loadable + in-place + system) in the active document grouped by category. |
| `load_family_from_path` | no | sí | Load or replace family definitions; restore model changes with Revit Undo. |
| `rename_family_type` | no | sí | Rename an existing type; restore its name with Revit Undo. |
| `replace_family_type` | no | sí | Replace instance types; restore them with Revit Undo. |
| `unload_family` | no | sí | Delete a family and optionally its instances; restore them with Revit Undo. |

## `modify`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `assign_elements_to_workset` | no | sí | Replace element workset assignments; restore them with Revit Undo. |
| `change_element_type` | no | sí | Replace element types; restore them with Revit Undo. |
| `color_elements` | no | sí | Overwrite element graphics; restore them with Revit Undo. |
| `operate_element` | no | sí | Change element visibility or graphics; restore document changes with Revit Undo. |
| `set_element_parameter_values` | no | sí | Replace instance parameter values; restore them with Revit Undo. |
| `set_type_parameter_values` | no | sí | Replace type parameter values; restore them with Revit Undo. |

## `delete` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `delete_element` | no | sí | Delete elements by ID; restore them with Revit Undo in Revit (no MCP undo tool). |

## `annotation`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `apply_keynote_to_element` | no | sí | Replace keynote values; restore them with Revit Undo. |
| `create_callout_view` | no |  | Create a callout view in a parent view. |
| `create_detail_line` | no |  | Create a detail line element in a view. |
| `create_dimensions` | no |  | Create a dimension element in a view. |
| `create_filled_region` | no |  | Create a filled region element in a view. |
| `create_text_note` | no |  | Create a text note element in a view. |
| `find_undimensioned_elements` | sí |  | Find visible elements of a specific category in a view that do not have dimensions. |
| `find_untagged_elements` | sí |  | Find visible elements of a specific category in a view that do not have tags. |
| `list_keynotes` | sí |  | List all keynotes loaded in the model with prefix and search filters. |
| `tag_all_by_category` | no |  | Tag all elements of a specific category in a view. |
| `tag_all_rooms` | no |  | Tag all rooms in current view at location point. |
| `tag_all_walls` | no |  | Tag all walls in current view at midpoint. |
| `tag_elements` | no |  | Tag one or more elements in a view. |
| `wipe_empty_tags` | no | sí | Delete empty tags; restore them with Revit Undo. |

## `export` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `batch_export_sheets` | no | sí | Export many sheets at once to PDF or DWG. |
| `create_view_sheet_set` | no |  | Create a named ViewSheetSet (a saved set of views/sheets) for batch printing/exporting. |
| `export_dgn` | no | sí | Export sheets or views to MicroStation DGN. |
| `export_dwf` | no | sí | Export sheets or views to Autodesk DWF/DWFx. |
| `export_dwg` | no | sí | Export sheets or views to AutoCAD DWG. |
| `export_elements_data` | no | sí | Export element parameter data for a category to a JSON or CSV file. |
| `export_fbx` | no | sí | Export a 3D view to Autodesk FBX. |
| `export_gbxml` | no | sí | Export the model's energy analytical data to gbXML. |
| `export_ifc` | no | sí | Export the model to IFC. |
| `export_image` | no | sí | Export a view to a raster image. |
| `export_nwc` | no | sí | Export the model to Navisworks NWC. |
| `export_pdf` | no | sí | Export sheets or views to PDF. |
| `export_room_data` | no |  | Export all rooms. |
| `export_schedule_csv` | no | sí | Export a Revit schedule's data to a delimited text/CSV file. |
| `get_print_settings` | sí |  | Report PrintManager state and bounded setting lists. |
| `list_export_settings` | sí |  | List bounded export settings. |

## `mep` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `analyze_mep_network` | sí |  | Analyze one MEP system. |
| `connect_mep_elements` | no | sí | Change MEP connectivity; restore connections with Revit Undo. |
| `create_air_terminal` | no |  | Place an air terminal (diffuser/grille) family instance at a point (mm). |
| `create_cable_tray` | no |  | Create an electrical cable tray between two points (mm). |
| `create_conduit` | no |  | Create an electrical conduit between two points (mm). |
| `create_duct` | no |  | Create an HVAC duct between two points (mm). |
| `create_lighting_fixture` | no |  | Place a lighting fixture family instance at a point (mm). |
| `create_mep_fitting` | no |  | Insert an MEP fitting at connectors of existing MEP elements. |
| `create_pipe` | no |  | Create a pipe (mm). |
| `detect_system_elements` | sí |  | Walk an MEP system and page IDs per category with startElement + maxElements (hard max 5000). |
| `find_mep_disconnects` | sí |  | Find MEP elements with open/unconnected End connectors (potential gaps in ductwork, piping, conduit). |
| `get_mep_element_connectors` | sí |  | Inspect all connectors on an MEP element (duct/pipe/fitting/equipment/terminal): domain, shape, position (mm), connection status, flow, direction. |
| `get_panel_schedule` | sí |  | Read panel metadata and a bounded circuit page. |
| `get_system_inventory` | sí |  | Return the full element inventory of one MEP system: network members, terminals, and base equipment, deduplicated by element ID, with category/type plus a category breakdown. |
| `list_mep_systems` | sí |  | List all MEP systems (mechanical/HVAC, piping/plumbing, electrical). |
| `set_system_classification` | no | sí | Change MEP system membership; restore it with Revit Undo. |

## `graphics`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `apply_filter_to_view` | no | sí | Change view filter visibility; restore it with Revit Undo. |
| `clear_element_overrides` | no | sí | Clear saved element graphics; restore them with Revit Undo. |
| `create_view_filter` | no |  | Create a parameter-based view filter (ParameterFilterElement) targeting one or more categories. |
| `get_view_visibility` | sí |  | Report a view's visibility/graphics state: hidden categories, applied filters, detail level, discipline, scale, view template, graphics-overrides-allowed. |
| `list_phases` | sí |  | List all project phases (in sequence order) and all phase filters. |
| `list_view_filters` | sí |  | List all view filter definitions (ParameterFilterElement) in the document. |
| `override_element_graphics` | no | sí | Replace element graphics; restore them with Revit Undo. |
| `remove_filter_from_view` | no | sí | Remove a view filter and optionally delete its definition; restore it with Revit Undo. |
| `set_category_visibility` | no | sí | Change saved category visibility; restore it with Revit Undo. |
| `set_element_phase` | no | sí | Replace element phase assignments; restore them with Revit Undo. |
| `set_filter_overrides` | no | sí | Replace view filter graphics; restore them with Revit Undo. |
| `set_view_phase` | no | sí | Replace saved view phase settings; restore them with Revit Undo. |

## `toolbaker` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `list_baked_tools` | sí |  | List baked tools with nameFilter and bounded paging. |
| `run_baked_tool` | no | sí | Run a baked tool by name. |

## `sheets`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `align_viewports` | no | sí | Replace viewport positions on a sheet; restore them with Revit Undo. |
| `assign_revision_to_sheet` | no | sí | Replace sheet revision assignments; restore them with Revit Undo. |
| `create_placeholder_sheet` | no |  | Create a new placeholder sheet |
| `create_revision` | no |  | Create a new document revision |
| `create_sheet` | no |  | Create a new sheet with a titleblock |
| `duplicate_sheet` | no |  | Duplicate an existing sheet with viewport and schedule layout |
| `get_titleblock_parameters` | sí |  | Get titleblock instance and type parameters for a sheet |
| `get_viewport_geometry` | sí |  | Read viewport positions on a sheet in sheet-space millimetres: visible crop rectangle, view outline, scale, rotation, pinned and crop flags. |
| `list_revisions` | sí |  | List project revisions and optionally their assigned sheets |
| `list_sheets` | sí |  | List sheets matching filters, with viewport and schedule counts, title blocks, and revisions |
| `list_titleblocks` | sí |  | List loaded titleblock family types and count their sheet placements |
| `place_schedule_on_sheet` | no |  | Place a schedule on a sheet using sheet paper coordinates in millimeters |
| `renumber_sheets` | no | sí | Replace sheet numbers or names; restore them with Revit Undo. |
| `set_titleblock_parameters` | no | sí | Replace titleblock parameter values; restore them with Revit Undo. |

## `materials` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `assign_material_to_element` | no | sí | Replace element or type material assignments; restore them with Revit Undo. |
| `create_material` | no |  | Create a new material with optional graphics parameters |
| `duplicate_material` | no |  | Duplicate an existing material with a new name |
| `get_material_properties` | sí |  | Get detailed properties of a material by ID or name |
| `get_material_takeoff` | no |  | Calculate material takeoff. |
| `list_materials` | sí |  | List and filter materials in the active document |
| `set_material_appearance` | no | sí | Replace material graphics; restore them with Revit Undo. |
| `set_material_identity` | no | sí | Replace material identity values; restore them with Revit Undo. |
| `set_material_structural_asset` | no | sí | Replace material structural properties; restore them with Revit Undo. |
| `set_material_thermal_asset` | no | sí | Replace material thermal properties; restore them with Revit Undo. |

## `geometry` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `analyze_geometry_complexity` | sí |  | Infer geometry complexity based on solid/face count and complexity metrics |
| `clash_detection` | sí |  | Detect clashes between elements of category A and category B. |
| `compute_element_area` | sí |  | Compute the total face area of one or more elements in square meters (m2) |
| `compute_element_volume` | sí |  | Compute the geometric solid volume of one or more elements in cubic meters (m3) |
| `find_elements_in_volume` | sí |  | Find elements inside or intersecting an axis-aligned 3D volume or a room's bounding box |
| `find_overlapping_elements` | sí |  | Find potentially overlapping elements of the same category, using bounding box intersection analysis |
| `get_element_bounding_box` | sí |  | Get the bounding box of one or more elements, optionally relative to a view, and optionally including transform data |
| `get_element_centroid` | sí |  | Compute the geometric centroid of one or more elements |
| `get_element_geometry` | sí |  | Get geometric details and optional vertex samples for one or more elements |
| `measure_distance_between_elements` | sí |  | Measure shortest distance between two elements based on bounding boxes, element locations, or bounding box pre-filter |
| `project_point_onto_face` | sí |  | Project a 3D point onto a specific face of a Revit element |
| `raycast_from_point` | sí |  | Cast a ray from a 3D origin point in a given direction to find the nearest element hit within a 3D view |

## `rooms`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `auto_create_rooms_from_walls` | no |  | Automatically generate room elements in all enclosed boundary circuits found on the target level. |
| `compute_room_finishes` | no |  | Calculate room finishes and boundary materials. |
| `create_area` | no |  | Create an area element at specified coordinates (mm) in an area plan. |
| `create_room_separator` | no |  | Create room separator lines from an array of {x,y,z} points (mm). |
| `create_space` | no |  | Create an MEP space element at specified coordinates (mm) on a target level. |
| `get_room_boundaries` | sí |  | Report room boundary segments (points in mm) and their bounding elements. |
| `get_room_openings` | sí |  | Retrieve doors and windows belonging to a room boundary. |
| `list_areas` | sí |  | List area elements in the model with optional scheme/level filters. |
| `list_rooms` | sí |  | List all rooms in the model with status classification (placed, unplaced, not_enclosed) and optional level/phase filters. |
| `tag_all_areas` | no |  | Place area tags for a bounded batch of untagged areas. |

## `links` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `acquire_coordinates_from_link` | no | sí | Replace host shared coordinates; restore them with Revit Undo. |
| `get_link_coordinate_system` | sí |  | Inspect a Revit or CAD link's instance and total transforms and map its origins into host internal/shared coordinates. |
| `get_link_elements` | sí |  | Retrieve elements from a Revit link instance document. |
| `get_project_coordinate_system` | sí |  | Read the complete project coordinate setup: immutable Internal Origin, Project Base Point, Survey Point, active and named Project Locations, coordinates under each location, angle to True North, and geographic site data. |
| `import_cad_to_view` | no |  | Import or link a CAD drawing (.dwg, .dxf) into a specific view. |
| `link_revit_model` | no |  | Link a Revit model (.rvt) into the project. |
| `list_linked_cad` | sí |  | List imported or linked CAD files in the model. |
| `list_linked_models` | sí |  | List external Revit linked models and instances. |
| `publish_coordinates_to_link` | no | sí | Publish host shared coordinates to a loaded Revit link. |
| `reload_link` | no | sí | Reload a Revit link type by type or instance ID. |
| `set_project_base_point` | no | sí | Replace base point or survey point values; restore them with Revit Undo. |
| `unload_link` | no | sí | Unload a Revit link type by type or instance ID. |

## `parameters`

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `bind_shared_parameter` | no | sí | Add or replace parameter bindings; restore document changes with Revit Undo. |
| `create_project_parameter` | no |  | Create a pure project parameter. |
| `create_shared_parameter` | no |  | Create a shared parameter definition in the shared parameter file. |
| `export_shared_parameter_file` | no |  | Export a shared parameter file as structured DTO data. |
| `list_project_parameter_bindings` | sí |  | List all project parameter bindings in the document, including name, GUID (if shared), categories, and binding type. |
| `list_shared_parameters` | sí |  | List all shared parameters defined in the shared parameter file. |
| `remove_parameter_binding` | no | sí | Remove parameter bindings or categories; restore them with Revit Undo. |
| `set_parameter_value_by_guid` | no | sí | Replace parameter values by GUID; restore them with Revit Undo. |

## `organization` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `apply_view_template` | no | sí | Replace view settings or template assignments; restore them with Revit Undo. |
| `create_view_template_from_view` | no |  | Create a new view template from an existing view. |
| `delete_saved_selection` | no | sí | Delete a saved selection filter; restore it with Revit Undo. |
| `delete_view_template` | no | sí | Delete a view template and optionally detach dependent views; restore them with Revit Undo. |
| `duplicate_view_template` | no |  | Duplicate an existing view template. |
| `list_saved_selections` | sí |  | List all saved selection filters in the document. |
| `list_view_templates` | sí |  | List view templates in the document. |
| `load_selection` | sí |  | Load a bounded page from a saved selection filter. |
| `save_selection` | no | sí | Create or replace a saved selection; restore document changes with Revit Undo. |
| `select_elements` | sí |  | Update active selection in the Revit UI. |

## `structural` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `analyze_structural_connections` | sí |  | Audit structural joins between columns and beams. |
| `create_foundation_isolated` | no |  | Create an isolated/spread footing at a point or under an existing column. |
| `create_foundation_wall` | no |  | Create a wall foundation under an existing wall. |
| `create_rebar_set` | no |  | Create a straight rebar set (single bar or arrayed) inside a structural host. |
| `create_rebar_stirrup` | no |  | Create a shape-driven rebar (typically a closed stirrup) inside a concrete host. |
| `create_structural_beam` | no |  | Create a structural beam between two points. |
| `create_structural_column` | no |  | Create a structural column at a point. |
| `create_structural_wall` | no |  | Create a structural wall between two points. |
| `get_structural_loads` | sí |  | List structural loads (point, line, area). |
| `list_rebar` | sí |  | List rebar instances. |
| `set_structural_load` | no | sí | Replace structural load values; restore them with Revit Undo. |
| `tag_structural_framing` | no |  | Place structural framing tags on beams in the active or specified view. |

## `workflows` (no activo)

| Herramienta | Lectura | Destr. | Descripción |
|---|---|---|---|
| `workflow_clash_review` | no | sí | Create clash-review views and overwrite graphics; restore model changes with Revit Undo. |
| `workflow_data_roundtrip` | no | sí | Export/import category parameter data with validation. |
| `workflow_model_audit` | sí |  | Run a read-only composite model audit covering warnings, families, views, schedules, and MEP connectivity signals. |
| `workflow_naming_normalization` | no | sí | Rename existing model elements; restore their names with Revit Undo. |
| `workflow_room_documentation` | no |  | Generate room documentation records, optional callouts, room tags, finish schedule, and sheet placement. |
| `workflow_sheet_set` | no | sí | Create sheets and replace sheet data; restore model changes with Revit Undo. |
| `workflow_takeoff_report` | no | sí | Generate a takeoff report and optionally overwrite a JSON/CSV file via output_path; Revit Undo cannot restore files, so back up the destination. |
| `workflow_view_cleanup` | no | sí | Delete eligible views or schedules; restore them with Revit Undo. |

## Cruce con las herramientas propias de la fase 3

Lectura del catálogo, **sin probar todavía**: cada "cubierta" hay que confirmarla en la
fase 2 antes de descartar la herramienta propia. Prioridad: A alta, B media, C baja.

| Prio | Herramienta propia | En bimwright | Veredicto |
|---|---|---|---|
| A | `grupos` | `list_groups`, `get_group_members`, `get_element_relationships`, `create_group_from_elements` | **Parcial:** lee y crea; falta desagrupar, cambiar o duplicar tipo de grupo y el ciclo "editar". **Construida** (2026-11-11): `herramientas/grupos/`. |
| A | `duplicar-tipo-familia` | `duplicate_family_type` (con `typeParameterOverrides`), `rename_family_type` | **Cubierta.** |
| A | `modificar-parametros-masivo` | `set_element_parameter_values`, `set_type_parameter_values`, `set_parameter_value_by_guid` | **Parcial:** fija valores; buscar/reemplazar y prefijo/sufijo no existen como tales (Claude puede leer, calcular y escribir). Revisar el esquema. **Construida** (2026-11-11): `herramientas/modificar-parametros-masivo/` (fijar, reemplazar, prefijo, sufijo, transformar, vaciar; ejemplar o tipo). |
| A | `renumerar` | `renumber_sheets` | **Parcial:** solo láminas. Puertas y recintos, con `set_element_parameter_values` sobre Mark/Number o herramienta propia. **Construida** (2026-11-11): `herramientas/renumerar/` (por filas, columnas, ruta, parámetro o ids; formato con ceros y nivel; revisa choques). |
| A | `renombrar-vistas` | `workflow_naming_normalization` (toolset `workflows`), `analyze_view_naming_patterns`, `suggest_view_name_corrections` | **Probable**, si `workflows` se activa. Falta ver si acepta un patrón de nombres propio. |
| A | `laminas-en-lote` | `create_sheet`, `create_placeholder_sheet`, `workflow_sheet_set` (`workflows`) | **Probable**, con `workflows`. |
| A | `excel-ida-y-vuelta` | `workflow_data_roundtrip` (`workflows`), `export_elements_data`, `export_schedule_csv` (`export`) | **Probable** en CSV/JSON (no `.xlsx`). |
| A | `estandares-graficos` | — | **No existe.** **Construida** (2026-11-11), solo estilos y patrones de línea: `herramientas/estandares-graficos/`. Con estándar en JSON y migración de nombres probada. |
| B | `documentar-recintos` | `workflow_room_documentation` (`workflows`) | **Probable**, con `workflows`. |
| B | `limpiar-cad` | `list_linked_cad` (`links`), `delete_element` (`delete`) | **Parcial:** lista y borra por id; no hay limpieza en un paso. **Construida** (2026-11-11): `herramientas/limpiar-cad/` (listar, reubicar vínculos rotos buscando el archivo, limpiar importaciones, vínculos rotos y patrones IMPORT). |
| B | `borrar-etiquetas-vacias` | `wipe_empty_tags` | **Cubierta.** |
| B | `leyenda-de-colores` | — | **No existe.** **Construida** (2026-11-11), con `colorear` propio: `revit_color_elements` repite una paleta de 10 colores. |
| B | `rango-de-vista-en-lote` | — | **No existe.** **Construida** (2026-11-11): `herramientas/rango-de-vista-en-lote/`, también para plantillas y copiando desde una vista. |
| B | `duplicar-lamina-con-contenido` | `duplicate_sheet` ("with viewport and schedule layout") | **Parcial** (probado 2026-11-11): duplica vistas con detalle, reutiliza leyendas, coloca tablas; no copia anotaciones de la lámina, tipos de ventana ni parámetros de proyecto. **Construida** la parte que falta: `herramientas/duplicar-lamina-con-contenido/` (`completar`). |
| B | `vistas-desde-recintos` | `create_callout_view`, `workflow_room_documentation` (callouts opcionales) | **Parcial.** **Construida** (2026-11-11): `herramientas/vistas-desde-recintos/`, llamada o planta por recinto con recorte al contorno, y alzados interiores. |
| B | `transferir-parametros` | `get_element_parameters` + `set_element_parameter_values` | **Parcial:** se arma con dos llamadas. **Construida** (2026-11-11): `herramientas/transferir-parametros/` (`copiar` entre parámetros e `igualar` desde un elemento modelo, con cambio de tipo). |
| C | IFC | `export_ifc` (`export`) | **No:** solo exporta; no abre ni reconstruye. |

**Conclusión provisional:** de 17 herramientas propias, 2 están cubiertas, 5 probablemente
lo están (4 de ellas solo si se activa `workflows`), 6 están a medias y 4 no existen
(`estandares-graficos`, `leyenda-de-colores`, `rango-de-vista-en-lote` e IFC).