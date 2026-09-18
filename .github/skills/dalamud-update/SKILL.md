---
name: dalamud-update
description: 'Update ChatBound for a new Dalamud or FFXIV release. Use when Dalamud updates, the Dalamud API level changes, the Dalamud.NET.Sdk changes, or the plugin stops loading after an FFXIV patch.'
argument-hint: '[Dalamud version, API level, or FFXIV patch details]'
user-invocable: true
disable-model-invocation: false
---

# Update ChatBound for Dalamud

Use this workflow when Dalamud releases a new API level or an FFXIV patch requires plugin updates.

## Constraints

- Keep ChatBound local-only and consent-oriented.
- Do not add remote control, stealth, outgoing-chat modification, or behavior that changes another player's client.
- Never print, commit, package, or publish tokens, local secrets, server persistence files, deployment paths, or infrastructure details.
- Preserve the fixed Owner/Pet model, profile and dictionary model, automatic server synchronization, and production endpoint behavior.
- Do not create a release until the user explicitly asks for publication.

## Procedure

1. Establish the current baseline:
   - Read `.github/copilot-instructions.md`, `ChatBound.csproj`, `ChatBound.json`, `pluginmaster.json`, and `package-release.ps1`.
   - Check `git status` and keep unrelated user changes intact.
   - Record the current `Dalamud.NET.Sdk` version, `DalamudApiLevel`, target framework, plugin assembly version, and release asset naming.

2. Identify the target compatibility:
   - Use the user-provided Dalamud/API version when available.
   - Otherwise inspect the installed Dalamud SDK and current Dalamud release information before editing.
   - Determine whether the change is only an API-level metadata bump or requires source changes.
   - Search for compiler errors and obsolete APIs after changing the SDK/API target.

3. Update the smallest compatible surface:
   - Update `Dalamud.NET.Sdk/<version>` in `ChatBound.csproj` when required.
   - Update `DalamudApiLevel` in both `ChatBound.json` and `pluginmaster.json` when required.
   - Keep `TargetFramework` aligned with the SDK requirements.
   - Make source changes only for concrete compiler errors or documented API changes.
   - Keep `AssemblyVersion`, `Version`, `FileVersion`, and release metadata aligned when making a release version.

4. Validate before publishing:
   - Run `dotnet restore` if package resolution requires it.
   - Run `dotnet build .\ChatBound.sln -c Release`.
   - Run `package-release.ps1`.
   - Inspect the ZIP directly and verify it contains `ChatBound.dll`, `ChatBound.json`, and the bundled `Dictionaries` directory.
   - Verify the DLL assembly version equals the packaged JSON assembly version and the feed's `AssemblyVersion`.
   - Confirm no secrets or deployment files entered the package.
   - Run `get_errors` on touched source files when available.

5. Review release metadata without publishing:
   - Ensure `DownloadLinkInstall` and `DownloadLinkUpdate` point to the exact new asset filename.
   - Prefer a new versioned asset filename when replacing an existing GitHub release asset, because stale GitHub CDN responses can persist for old asset URLs.
   - Validate the public raw feed and exact asset URL only after the user requests publication.

6. Publish only by explicit request:
   - Create the next semantic version and tag.
   - Upload the validated ZIP using the exact filename referenced by `pluginmaster.json`.
   - Commit and push source and metadata changes to `origin/main`.
   - Verify the public feed returns the new version and the advertised asset returns HTTP 200.
   - Report the unchanged custom repository URL and the version delivered.

## Common FFXIV Patch Checks

- API level or SDK package changed: update project SDK and both plugin metadata files.
- ImGui or Dalamud service signatures changed: update call sites, then rebuild.
- Chat message types changed: re-check `ChatFilterService` and ensure filtering remains incoming-client-local.
- Configuration serialization changed: preserve existing Owner/Pet tokens, profiles, dictionaries, and connection settings.
- Packaging changed: verify the ZIP still contains the plugin manifest and bundled dictionaries.

## Completion Criteria

An update is complete only when the requested target builds in Release mode, the package contents and versions agree, metadata points to the exact package, and no release is published without explicit user approval.
