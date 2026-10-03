# Alpine Tuning — Northern-Built. Mountain-Proven

Alpine Tuning adds mechanical tuning and setup options to the Sledders garage while matching the style of the game's existing menus.

Current public version: **2026.10.02**
Made for Sledders **1.1.6**

## Installation

1. Close Sledders.
2. Install [MelonLoader](https://melonwiki.xyz/) for Sledders.
3. Launch the game once, then close it.
4. Download `Alpine Tuning.dll` from the [official releases page](https://github.com/Ronnie-Reagan/Alpine-Tuning/releases/latest).
5. Copy the DLL into the Sledders `Mods` folder.

For a standard Steam installation, the folder is:

```text
C:\Program Files (x86)\Steam\steamapps\common\Sledders\Mods
```

When Sledders is installed somewhere else:

1. Open Steam.
2. Right-click Sledders.
3. Select **Manage > Browse local files**.
4. Open the `Mods` folder.

## Using Alpine Tuning

Open the garage, select a sled, and choose **TUNING**.

The normal **STYLE** option remains the game's cosmetic editor. Alpine Tuning is used for mechanical and performance changes.

The tuning screen is organized into five domains:

- **Performance** — engines and swaps, internal parts, intake, turbo, refillable nitrous kits, clutch setup (including fixed 6,000–7,000 RPM racing engagement), clutch weights, gearing, and brake calibration.
- **Chassis & Handling** — chassis, lightweight running boards and cooling, suspension, limiter, shocks, springs, skis, steering geometry, and compatible visual track-length kits.
- **Lighting** — colour, brightness, beam type, aim, operating mode, controller/keyboard controls, and a compatibility-gated carbon headlight delete.
- **Utility** — Light and Default tanks, separate backpack reserves, Backpack-o-fuel, fuel/nitrous displays and refill behavior, saved setups, and both stock reset modes.
- **Experimental** — six-axis head tracking and tracking lean, expanded track compatibility, validated hidden sled discovery, and stable world-prop bodies.

**Settings** remains available from the tuning root for runtime, resource, display, binding, and calibration options. Experimental systems are independent and disabled by default.

Changes are added to your current working setup immediately.

Use:

- **Save** to keep the setup.
- **Reset** to choose either Alpine's Realistic Stock baseline or captured Sledders Default values.
- **DYNO** to view estimated performance information.
- **Back** to return to the previous menu.

Some changes require the sled to be rebuilt before they take effect. Alpine handles this automatically when the setup is saved.

When leaving with unsaved changes, you can:

- Save and exit.
- Continue tuning.
- Exit without saving.

## Comparing Parts

Alpine shows how the current sled compares with its factory setup.

When viewing another part or adjustment, it also previews how that choice would change the sled.

Comparison bars use the following colours:

- **Gray** — factory value.
- **Lime** — an improvement.
- **Orange** — a reduction.
- **Blue** — a change that is not automatically better or worse, such as ski stance.

Exact values are shown where Sledders provides enough information. Alpine avoids displaying made-up values when the game does not provide the required data.

## Dyno

The **DYNO** window provides two types of information:

### Game Model

Shows performance calculated from values provided by Sledders, including delivered track power and force where available.

### Estimated Engine

Shows estimated horsepower and torque curves for the selected engine family.

These results are clearly marked as estimates because Sledders does not provide a complete engine torque curve.

The Dyno window can be moved and resized. Select **FIT** to return it to its default size and position.

Press Back or Escape to close it.

## Settings

Open **Settings** to change:

- **Display Units** — Metric or Imperial.
- **Runtime** — completely stop Alpine gameplay input, fuel, background, visual, and tuning behavior while retaining garage editing and saved setups.
- **Fuel** — control idle consumption, model-level persistence, and the optional Alpine fuel readout. A sled model keeps the liters it was left with across every Alpine setup and owned ride, while each setup still respects its selected tank capacity. Emergency reserve-refuel controls remain available when the readout is hidden.
- **Headlight Hotkey** — enable, disable, change, or clear keyboard and physical Unity Input System controller bindings. Bindings survive controller reconnects; with no binding, lights follow game time.
- **Nitrous** — choose Hold to Spray or Automatic WOT, set the WOT threshold, choose Fuel/Nitrous/Both station refilling, toggle the meter, and bind keyboard/controller activation.
- **Head Tracking** — opt into TrackIR or OpenTrack, choose a provider, view live six-axis data, recenter, select a calibration preset, or tune every axis, deadzone, clamp, inversion, smoothing, curve, camera mask, and additive lean mapping.

While choosing a new hotkey, the menu will display **Waiting**.

Press Escape, use the controller Cancel button, or select Cancel to stop without changing the binding.

Clearing an existing binding requires confirmation.

## Nitrous

Fit a 5 lb Compact, 10 lb Race, or 20 lb Drag kit under **Performance > Nitrous System**. The setup-specific boost is adjustable from +25% to +200%; higher boost consumes charge proportionally faster. Hold Left Shift by default, bind another keyboard/controller input, or choose Automatic WOT. A newly fitted sled receives one full bottle, after which its charge persists through resets, teleports, respawns, and reloads.

At an active gas station, use the normal refuel control. The selected Utility/Settings refill target determines whether the station fills gasoline, nitrous, or both. The small station prompt remains visible even when the optional nitrous meter is hidden. On maps without stations, press **N** while parked with the engine off to refill the fitted bottle. An empty bottle immediately returns engine output to its unboosted value.

## Experimental systems

Each experiment is opt-in and can be disabled independently:

- **Track compatibility** exposes geometry-validated cross-platform rear assemblies and clearly labelled, front-anchored scaled fallbacks across the native 120–174 in track range.
- **Hidden vehicles** appends validated native assets only when they are not locked or entitlement-controlled.
- **World-prop bodies** retain the native drivetrain, rider, suspension, and collision while projecting trucks, scenery, and experimental sled bodies with adjustable fit, mass, and center of mass. Separated rigid sled-prop skis and handlebars can follow native pivots; unmapped moving parts and the animated track remain native.
- **Tracking Lean** adds calibrated tracking output to physical rider input without replacing the controller signal.

These experiments are local visual/gameplay projections. Other connected players may see the underlying native sled and mounted rider state. The master runtime switch remounts the rider and restores all experimental projections.

## Sled Forge and Multiplayer Build Showcase

**Sled Forge** is Alpine's donor-part workshop. Choose donor cosmetic assemblies for the body shell, hood, seat, bumper, handlebars, skis, and running boards. Every donor projection keeps the source sled's rider, camera, collision, controls, and simulation graph. Donor pages expose the full available list. The displayed heuristic score ranks candidate sleds; it does not certify panel fit.

Each selected slot has saved position, rotation, and uniform-scale adjustments, **RESET FIT**, and a per-slot native restore control. Adjustments update the existing preview without reloading its donor. Body panels are aligned to the recipient assembly bounds and scaled to its footprint before saved adjustments are applied at the recipient mount. Running boards match width and length while retaining their thickness. Front and rear bumpers have separate mounts; skis and handlebars use their respective native pivot frames. Native paint lists are filtered because their names do not reliably describe physical assemblies.

The preview reports loading, installed, retrying, or native-fallback status per slot. Failed asset loads retry up to three attempts; **RETRY FORGE PREVIEW** starts a fresh attempt. Failed installations roll back their objects and leave native parts visible. A rigid donor ski or handlebar is installed only when its corresponding pivots and meshes can be mapped; unsupported sides or assemblies remain native. Forge selects one donor level of detail and suppresses every identified native level of detail. A slot combined into another native mesh, or an unsupported skinned assembly, stays native with a reason in the preview. Cross-family panel seams and pivot orientation still require in-game inspection and fit adjustment.

World sled props use a hybrid articulated projection: unambiguous, separated rigid left/right skis and handlebars follow the corresponding native anchors. Unmapped prop moving meshes are suppressed and native moving parts remain visible. Static prop tracks cannot reproduce native track animation, so the track remains native. Rear-track physics packages remain separately validated native grafts, and unsafe front/rear physics graphs are never installed.

### Multiplayer

Alpine watches the native multiplayer start/stop lifecycle, detects joins and reconnects, represents the native host ID zero explicitly, and clears departed riders and their projections. Compatible Alpine Tuning clients automatically discover one another through Sledders' internal relay when available, with Steam P2P as a fallback using verified platform IDs from the native player records. Steam packets are accepted only from players in the current session and are mapped back to their native sled identities. The **Build Showcase** lists active compatible builds in the garage, lets you request or import a shared setup, and can display compact nearby-rider build tags. Build sharing, visual receiving, and tags are configurable from the Showcase.

Both riders must use this networking build (build protocol 2). The Showcase updates as riders join and build details arrive, and imports active-build payloads into Setups. Internal tune data is sent only after the receiving game server acknowledges Alpine support. Owning a lobby does not guarantee that its server runs Alpine; unsupported servers leave internal sharing waiting, with Steam P2P available when peer Steam IDs can be resolved. This prevents tune broadcasts from being mistaken for race commands.

Networked projection is presentation-only. It synchronizes supported donor visuals, lights, audio, and build metadata between compatible Alpine clients; it never changes remote physics, ownership, or the game server's authority. Prop configuration is currently metadata only and is not projected onto remote riders. Both clients need the updated build to exchange saved Forge fits. A version, protocol, catalog, game-build, or profile-checksum mismatch retains the native sled appearance.

### Alpine Garage service

Open **Settings > Garage Connection** and choose **CONNECT / RETRY** to connect to [Alpine Garage](https://garage.donreagan.ca). The production address is configured by default. Old localhost defaults migrate to production and require pairing again. New installations start disconnected. Choose **COPY PAIRING CODE**, then enter it under **Connect Alpine** in the Alpine Garage Discord Activity. The panel shows connection status and lets you disconnect. Pairing expiry or a rejected installation stops the connection until you reconnect.

The client syncs saved-setup inventory and processes publish/import actions while connected. Pending acknowledgements are persisted with their original payload and retried after outages or restarts. The Garage service handles repeated completions once, so acknowledgements do not duplicate published builds or inflate import counts. Disconnect pauses networking and retains local setups and the linked installation for reconnecting. Changing the service address clears the previous credentials. Connection records are saved atomically with a recovery backup under MelonLoader UserData at `AlpineTuning/Garage/connection.json`; backup recovery requires reconnecting. Import retries keep the existing setup, including rider edits, and do not resurrect a removed setup while its recovery archive exists.

Publish selected setups from the Discord Activity, choosing private, unlisted, or public visibility. Imports add a saved setup to your local library; load it from **Setups** when you want to ride it. Garage v1 exports mechanical setups and excludes Forge selections, fit adjustments, and prop configuration. Multiplayer sharing between updated Alpine clients preserves Forge fit data.

## Saved Setups

Open **Setups** from the main tuning menu.

The list includes:

- **Current Draft** — the setup currently being edited.
- Saved setups.
- **Recovery** options when older or damaged setup data can be restored.

Saved setups include names and short summaries to help identify them.

You can:

- Save the current tune as a new setup.
- Rename saved setups.
- Choose a default setup.
- Preview a setup before loading it.
- Recover removed or damaged setups.
- Restore older revisions.

Alpine keeps setups separated by sled. A setup created for one sled cannot accidentally overwrite a different sled.

Loading a saved setup while you have unsaved changes requires confirmation.

Existing compatible setups from older versions are kept when possible.

## Units and Tuning Behaviour

- Engine output is shown in **kW** with Metric units and **hp** with Imperial units.
- Weight is shown in **kg** or **lb**.
- Ski stance is shown in millimetres or inches.
- The game's Power, Climbing, and Agility ratings remain on their normal 0-100 scale.
- Brake settings are shown as a percentage of the factory brake strength.
- Steering, suspension, grip, and drivetrain changes are applied from the sled's original factory values to prevent repeated setup changes from stacking incorrectly.
- Generic track tuning changes physics only. Length kits reload the original sled, validate a native donor by tunnel seam and track geometry, then replace only the tunnel/rear bumper, skid, rails, wheels, animated track, contact mesh, and Trax graph. The hood, seat, cockpit, front suspension, lighting, engine, fuel, accessories, and sled identity remain native to the source sled. Arctic Cat's embedded 146/154/165 rear variants are preferred where complete; otherwise Alpine uses a validated direct graft or, with Experimental Track Compatibility enabled, an explicitly labelled front-anchored scaled fallback.

## Updating

Close Sledders before replacing the mod DLL.

It is recommended that you back up important saved setups before installing a major update.

## Developer Build Instructions

This section is only needed when building Alpine Tuning from source.

Run:

```text
build-release.bat
```

The completed DLL will be placed at:

```text
SleddersTuner\bin\x64\Release\Alpine Tuning.dll
```

The build script checks the release files, runs automated tests, and installs the verified DLL into the standard Sledders `Mods` folder.

To run the same release gate without installing the DLL, use PowerShell:

```powershell
$env:ALPINE_VALIDATE_ONLY = '1'
try { .\build-release.bat } finally { Remove-Item Env:ALPINE_VALIDATE_ONLY }
```

Validation builds from the Git public-file inventory in temporary staging and removes that staging afterward. It includes allowlisted untracked files, so add all intended source, test, script, and asset changes before committing a release. Validation-only mode does not update the local release DLL.

Build prerequisites are Git, the .NET SDK, .NET Framework 4.7.2 targeting assemblies, and a Sledders installation with MelonLoader. The test runner uses the installed game assemblies.

Before publishing, also smoke-test the built DLL in Sledders: open the garage, apply and restore a tune, save and reload a setup, switch sleds, and check multiplayer replication with another player. Automated checks do not exercise a running Unity scene or confirm visual behavior in game.

When Steam uses another library location, edit `GAME_DIR` near the top of `build-release.bat`.

## License and Attribution

Alpine Tuning is an unofficial community mod and is not affiliated with the developers of Sledders.

See [license.txt](license.txt) for source use, redistribution, attribution, and warranty terms.

Back up important setup data before updating. Use the mod at your own risk.
