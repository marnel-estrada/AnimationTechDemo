# AGENTS.md

## Project Overview
This is a Unity 6 tech demo project for character animation, using DOTS/ECS (Entities + Entities Graphics) and URP.

Unity version:
- `6000.3.18f1`

Key packages:
- `com.unity.entities` 1.4.8
- `com.unity.entities.graphics` 1.4.21
- `com.unity.render-pipelines.universal` 17.3.0
- `com.unity.inputsystem` 1.19.0
- `com.unity.timeline` 1.8.12
- `com.unity.test-framework` 1.6.0

Primary areas:
- Scenes: `Assets/Scenes`
- Render pipeline / project assets: `Assets/Settings`
- Third-party animation pack: `Assets/Zombie_27` (FBX animations, Animator controller, demo scene)
- Unity template leftovers: `Assets/TutorialInfo`
- Packages: `Packages/manifest.json`

There is no project code or asmdef structure yet. When adding the first code, propose a folder and asmdef layout before creating it.

## Integrated AI Guidelines
General AI/code-generation preferences:
- Keep edits focused on the file or area relevant to the requested change.
- Do not remove existing code unless the requested change requires it.
- Do not delete existing files unless explicitly asked.
- Avoid garbage-producing code at all times, especially in animation, gameplay and ECS paths.
- Prefer composition over inheritance. Use inheritance only when it matches existing project patterns or is clearly the least complex option.
- Always show code changes first and ask for permission to make the changes.

DOTS-specific preferences:
- Do not use `IJobEntity`.
- Do not use idiomatic `foreach` in hot ECS paths.
- Prefer value types and avoid reference types in ECS code.
- Do not add properties to component structs when a variable could already be accessed.

## General Instructions
- Keep changes focused and consistent with nearby code.
- Prefer existing project patterns over introducing new architecture.
- Avoid broad refactors.
- Preserve Unity `.meta` files and serialized references.
- Do not generate `.meta` files. Let Unity create these.
- Do not edit generated files such as `.csproj`/`.sln` files, generated input action code, `Library`, `Temp`, `obj`, `Logs`, or `UserSettings`.
- When checking for null of Unity objects, use its lifetime instead. Use `if (unityObject)` instead of `if (unityObject != null)`.

## Unity Asset Safety
- Keep `.meta` files paired with their assets.
- Be careful editing `.unity`, `.prefab`, `.asset`, `.asmdef`, `.controller`, and `.anim` files.
- Preserve Unity YAML IDs, GUIDs, and object references.
- Avoid manual asset YAML edits unless they are clearly required and minimal.
- Do not modify `ProjectSettings` or `Packages` unless the task explicitly requires it.

## Third-Party Assets
- Treat `Assets/Zombie_27` as vendor content: do not modify its FBX files, import settings, materials, or controller in place.
- When different import settings or controller changes are needed, copy or create project-owned assets elsewhere, or ask first.
- Do not mass-change FBX import settings (rig type, animation compression, root motion) without explicit approval; it affects many clips at once.

## Assembly Structure
- Put new code in project-owned asmdefs rather than `Assembly-CSharp`.
- Keep runtime, authoring, and editor code in separate assemblies.
- Do not add asmdef references casually; keep dependencies minimal and explicit.
- If adding generic DOTS job/component usage that requires registration (`RegisterGenericJobType`, `RegisterGenericComponentType`), keep the registrations in one `AssemblyInfo.cs` per assembly.

## DOTS / ECS Style
- Prefer these ECS patterns:
  - `ISystem` with explicit `EntityQuery`, type handles, and `IJobChunk`
  - `SystemBase` only where managed access is genuinely required
  - explicit `EntityTypeHandle`, `ComponentTypeHandle<T>`, `BufferTypeHandle<T>`, and `ComponentLookup<T>`
  - `ChunkEntityEnumerator` when enabled masks are involved
- Do not introduce `IJobEntity` unless explicitly requested.
- Avoid idiomatic `foreach` in ECS systems and jobs.
- Prefer `IJobChunk` for chunk iteration.
- Use `EntityQueryBuilder` and `RequireForUpdate` where appropriate.
- Update type handles/lookups before scheduling jobs.
- Add `[BurstCompile]` to jobs and compatible systems.
- Keep ECS component data unmanaged/blittable when possible.
- Avoid managed allocations, LINQ, boxing, closures, reflection, and string work in hot paths.
- Use `Unity.Mathematics`, `Unity.Collections`, and native containers for Burst/job code.
- Dispose native containers deterministically, or use `state.WorldUpdateAllocator` when lifetime is frame-scoped.
- In job structs, write the ECS handle variables first (like `ComponentLookup` and `ComponentTypeHandle`) before other variable types.

## Static Fields
- Every static field needs a reset method decorated with `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` so state does not leak across play sessions when domain reload is disabled.

## Structural Changes
- Use `EntityCommandBuffer` for structural changes in jobs and systems unless immediate changes are intentionally required.
- Prefer the built-in `EndSimulationEntityCommandBufferSystem` / `BeginSimulationEntityCommandBufferSystem` singletons.
- Call `AddJobHandleForProducer` after scheduling jobs that write to ECBs from a non-singleton ECB system.

## Animation And Rendering
- Keep animation sampling/evaluation data in blob assets or native containers so it is Burst-friendly.
- Keep animation simulation (clip time, blending, state) separate from rendering/presentation (Entities Graphics, skinning, materials).
- Use `UpdateBefore` and `UpdateAfter` only when ordering is required; be explicit about ordering between animation evaluation, transform systems, and rendering/skinning.
- Respect URP and Entities Graphics constraints (SRP Batcher-compatible materials, supported material property overrides).

## Authoring And Baking
- Use authoring MonoBehaviours only for editor/inspector-facing data or scene/prefab hooks.
- Keep authoring classes separate from runtime component data.
- Avoid managed references in runtime ECS components.
- Convert clips, rigs, and other animation data in bakers into runtime-friendly formats rather than reading Unity animation assets at runtime.

## Performance
- Treat per-frame ECS and animation code as allocation-sensitive.
- Avoid LINQ and managed collection allocations in systems/jobs.
- Prefer value types, explicit loops, native collections, and cached handles/lookups.
- Do not add premature abstractions that obscure data flow in hot code.

## C# Style
- Follow nearby formatting and naming.
- Use clear domain names for components, systems, jobs, authoring classes, and bakers.
- Keep comments short and useful, especially around non-obvious scheduling, ordering, or Burst constraints.
- Prefer explicit data flow over hidden state.

## Tests And Validation
- Prefer Unity Edit Mode tests for focused logic changes.
- If command-line Unity is available, run relevant Edit Mode tests through the Unity Test Runner.
- If tests cannot be run, at least reason through compile risks and mention what was not validated.
- For DOTS changes, verify:
  - correct system group/order,
  - correct query filters,
  - type handles/lookups are updated,
  - dependencies are assigned,
  - ECB producer handles are registered,
  - native containers are disposed or scoped correctly,
  - Burst compatibility is preserved.

## Dependency Rules
- Do not add packages unless explicitly requested or clearly necessary.
- Prefer existing Unity packages over new third-party dependencies.

## Before Finishing
- Summarize what changed and why.
- Mention validation performed.
- Mention validation that could not be run.
- Call out risks involving Unity serialization, asmdef dependencies, DOTS system ordering, or animation import settings.
