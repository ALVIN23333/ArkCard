# AGENTS.md

<!-- Funplay Unity MCP managed project skills -->
<!-- Funplay Unity MCP project skill versions: unity-mcp-workflow@1.0.2 -->

# Funplay Unity MCP Project Guidance

This section is managed by Funplay MCP for Unity. Everything between the begin and end markers is regenerated on each sync; edit outside this block.

## Installed project skills

- `funplay-unity-mcp-workflow` v1.0.2 - Efficient workflow for using Unity MCP to edit, import, compile, inspect, and test Unity projects.

## Codex workflow rules

- Prefer project-local Funplay skills under `.codex/skills/`.
- Use `execute_code` as the primary Unity automation tool. For new snippets, include `using Funplay.Editor.Tools.Scripting;`, implement `IFunplayCommand`, and use `ctx.RegisterObjectCreation` / `RegisterObjectModification` / `DestroyObject` so changes participate in Undo automatically.
- Confirm the Unity project root, active scene, and real object/prefab/asset path before edits. Treat user-provided object names as hints, not paths.
- Inspect Unity objects through MCP before changing user-named scene or prefab targets. Carry the returned `instanceId` into follow-up calls (`find_method=by_id`) instead of re-resolving by name.
- Tool returns are structured JSON (`{success, message, data}` / `{success: false, code, error, data}`). Branch on `code`, not free-form text.
- Set component fields with `set_component_property(ies)` — it picks up `[SerializeField] private` fields and accepts Object references as `{"fileID": <instanceId>}` or `{"assetPath": "Assets/..."}`.
- For field-only prefab asset edits, use `set_prefab_property(ies)` with a verified `Assets/**/*.prefab` path. If it reports duplicate paths or components, retry only with an index from that response; use Prefab Mode for structural edits.
- Read editor state through dedicated tools (`get_selection`, `get_prefab_stage`, `get_tags`, `get_layers`, `get_build_settings`); use `execute_menu_item` before falling back to ad-hoc `execute_code`.
- Never edit `.unity`, `.prefab`, or `.asset` files with shell text tools or patches; use Unity MCP / Editor APIs for scenes, prefabs, and ScriptableObject assets.
- Save only the scene or prefab assets intentionally modified, then read back exact values.
- With default `core` exposure, use the focused workflow tools. With default `full` exposure, prefer specific MCP tools for simple editor operations.
- `execute_code` refreshes the asset database and waits for compilation before running. For other tools that depend on freshly compiled code, still call `request_recompile` after external script edits.
- In `execute_code`, null-guard every lookup and return explicit missing path/object/component messages; do not run self-healing fallback loops.
- For Unity object references, do not use `??=` for lazy rebinding; use explicit `if (field == null) field = Resolve();`.
- After code or resource edits, exit Play Mode if needed, call `request_recompile`, `wait_for_compilation`, then read compilation or console errors.
- `request_recompile` is rejected while Unity is in Play Mode. Call `exit_play_mode` first, then retry.
- After `enter_play_mode`, the HTTP server briefly drops while Unity reloads the domain. Poll `tools/list` or `get_reload_recovery_status` until it responds again before issuing the next tool call.
- If recompilation triggers a domain reload or interrupts a request, treat the result as unknown until `get_reload_recovery_status`, compilation checks, and MCP readback confirm it.
- Avoid changing `Library/`, `Temp/`, `Logs/`, or `obj/`.

## Project

- Project root: `D:\unityXiangmu\ArkCard`
- Product name: `明日方舟卡牌`

## Notes

- Re-run `Funplay > Project Skills` after changing selected skills or platforms.
<!-- /Funplay Unity MCP managed project skills -->
