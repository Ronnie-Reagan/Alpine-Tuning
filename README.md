# Alpine Tuning — Northern-Built. Mountain-Proven

Alpine Tuning adds mechanical tuning, setup options, customization, and experimental systems to the **Sledders** garage while matching the style of the game's existing menus.

**Current public version:** `2026.09.12`  
**Made for Sledders:** `1.1.6`

---

<details>
<summary><strong>Windows Installation</strong></summary>

<br>

### 1. Install MelonLoader

Close Sledders and install [MelonLoader](https://melonwiki.xyz/) for Sledders.

### 2. Launch Sledders once

Start Sledders normally through Steam.

Wait until you reach the main menu, then close the game.

This allows MelonLoader to finish creating its folders, including `Mods`.

### 3. Download Alpine Tuning

Download `Alpine Tuning.dll` from the [latest Alpine Tuning release](https://github.com/Ronnie-Reagan/Alpine-Tuning/releases/latest).

### 4. Open the Sledders folder

The easiest method is through Steam:

1. Open your Steam Library.
2. Right-click **Sledders**.
3. Select **Manage > Browse local files**.
4. Open the `Mods` folder.

For a standard Steam installation, the Mods folder is:

```text
C:\Program Files (x86)\Steam\steamapps\common\Sledders\Mods
```

If Sledders is installed in another Steam Library, use **Browse local files** instead of manually looking for this path.

### 5. Install Alpine Tuning

Copy:

```text
Alpine Tuning.dll
```

directly into:

```text
Sledders\Mods
```

The installation should look roughly like this:

```text
Sledders/
├── MelonLoader/
├── Mods/
│   └── Alpine Tuning.dll
└── Sledders.exe
```

Do **not** put `Alpine Tuning.dll` inside another folder within `Mods`.

### 6. Launch Sledders

Start Sledders normally through Steam.

If MelonLoader and Alpine Tuning are installed correctly, Alpine Tuning should load with the game.

Once in Sledders, open the garage, select a sled, and choose **TUNING**.

</details>

---

<details>
<summary><strong>Linux / Steam Deck Installation</strong></summary>

<br>

> These steps were tested successfully on Steam Deck/Linux by community member **goatly** using MelonLoader **0.7.3**.

### 1. Download MelonLoader

Download the Linux MelonLoader installer:

```text
MelonLoader.Installer.Linux
```

Do **not** download or run the Windows `.exe` installer.

### 2. Make the installer executable

In Desktop Mode:

1. Open **Dolphin** and go to your `Downloads` folder.
2. Right-click `MelonLoader.Installer.Linux`.
3. Select **Properties > Permissions**.
4. Enable **Is executable** / **Allow executing file as program**.
5. Close the Properties window.
6. Launch the installer.

### 3. Install MelonLoader

When the installer opens:

1. Select **Sledders**.
2. Select **MelonLoader 0.7.3**.
3. Select **Install**.
4. Close the installer when installation finishes.

### 4. Launch Sledders once

Start Sledders normally through Steam.

Wait until you reach the main menu, then close the game.

This allows MelonLoader to finish creating its folders, including `Mods`.

### 5. Find the Sledders folder

The easiest method is through Steam:

1. Open your Steam Library.
2. Open **Sledders > Manage > Browse local files**.
3. Open the `Mods` folder.

For a standard Steam installation on Linux, Sledders is usually located at:

```text
~/.local/share/Steam/steamapps/common/Sledders
```

If `.local` is hidden in Dolphin, press:

```text
Ctrl+H
```

to show hidden files.

If Sledders is installed on an SD card or another Steam Library, use **Browse local files** instead of manually looking for the path above.

### 6. Install Alpine Tuning

Download `Alpine Tuning.dll` from the [latest Alpine Tuning release](https://github.com/Ronnie-Reagan/Alpine-Tuning/releases/latest).

Copy the DLL directly into:

```text
Sledders/Mods
```

Do **not** put `Alpine Tuning.dll` inside another folder within `Mods`.

### 7. Add the Steam launch option

In Steam:

1. Open **Library > Sledders**.
2. Open **Properties > General**.
3. Find **Launch Options**.
4. Paste the following exactly:

```bash
WINEDLLOVERRIDES="version=n,b" %command%
```

There **must be a space** between `"version=n,b"` and `%command%`.

### 8. Launch Sledders

Close the Properties window and start Sledders normally through Steam.

If MelonLoader, Alpine Tuning, and the Steam launch option are installed correctly, Alpine Tuning should load with the game.

Once in Sledders, open the garage, select a sled, and choose **TUNING**.

</details>

---

## Using Alpine Tuning

Open the garage, select a sled, and choose **TUNING**.

The normal **STYLE** option remains Sledders' cosmetic editor. Alpine Tuning handles mechanical, performance, setup, and supported customization changes.

The tuning screen is organized into five main domains:

- **Performance** — engines and swaps, internal parts, intake, turbo, refillable nitrous kits, clutch setup, clutch weights, gearing, and brake calibration.
- **Chassis & Handling** — chassis, lightweight running boards and cooling, suspension, limiter, shocks, springs, skis, steering geometry, and compatible track-length kits.
- **Lighting** — colour, brightness, beam type, aim, operating mode, controller/keyboard controls, and a compatibility-gated carbon headlight delete.
- **Utility** — fuel tanks, backpack reserves, Backpack-o-fuel, fuel/nitrous displays and refill behavior, saved setups, and stock reset modes.
- **Experimental** — six-axis head tracking and tracking lean, expanded track compatibility, validated hidden sled discovery, and world-prop bodies.

**Settings** is available from the tuning root for runtime, resource, display, binding, and calibration options.

Experimental systems are independent and disabled by default.

Changes are added to your current working setup immediately.

Use:

- **Save** — keep the setup.
- **Reset** — choose either Alpine's Realistic Stock baseline or captured Sledders Default values.
- **DYNO** — view estimated performance information.
- **Back** — return to the previous menu.

Some changes require the sled to be rebuilt before they take effect. Alpine handles this automatically when the setup is saved.

When leaving with unsaved changes, you can:

- Save and exit.
- Continue tuning.
- Exit without saving.

---

## Comparing Parts

Alpine shows how the current sled compares with its factory setup.

When viewing another part or adjustment, Alpine also previews how that choice would change the sled.

Comparison bars use the following colours:

- **Gray** — factory value.
- **Lime** — an improvement.
- **Orange** — a reduction.
- **Blue** — a change that is not automatically better or worse, such as ski stance.

Exact values are shown where Sledders provides enough information.

Alpine avoids displaying made-up values when the game does not provide the required data.

---

## Dyno

Select **DYNO** from the tuning interface to view performance information.

### Game Model

Shows performance calculated from values provided by Sledders, including delivered track power and force where available.

### Estimated Engine

Shows estimated horsepower and torque curves for the selected engine family.

These results are clearly marked as estimates because Sledders does not provide a complete engine torque curve.

The Dyno window can be moved and resized.

Select **FIT** to return it to its default size and position.

Press **Back** or **Escape** to close it.

---

<details>
<summary><strong>Settings</strong></summary>

<br>

Open **Settings** from the tuning root to configure Alpine's runtime and supporting systems.

### Display Units

Choose:

- Metric
- Imperial

Engine output is displayed in **kW** with Metric units and **hp** with Imperial units.

### Runtime

The master runtime setting can completely stop Alpine gameplay input, fuel, background, visual, and tuning behavior while retaining garage editing and saved setups.

### Fuel

Fuel settings control:

- Idle consumption.
- Model-level persistence.
- The optional Alpine fuel readout.

A sled model keeps the liters it was left with across every Alpine setup and owned ride, while each setup still respects its selected tank capacity.

Emergency reserve-refuel controls remain available when the readout is hidden.

### Headlight Hotkey

You can:

- Enable or disable the hotkey.
- Change the binding.
- Clear the binding.
- Use keyboard inputs.
- Use physical Unity Input System controller inputs.

Bindings survive controller reconnects.

With no binding configured, lights follow game time.

While choosing a new hotkey, the menu displays **Waiting**.

Press **Escape**, use the controller **Cancel** button, or select **Cancel** to stop without changing the binding.

Clearing an existing binding requires confirmation.

### Nitrous

Nitrous settings allow you to:

- Choose **Hold to Spray** or **Automatic WOT**.
- Set the WOT threshold.
- Choose Fuel, Nitrous, or Both for station refilling.
- Toggle the nitrous meter.
- Bind keyboard or controller activation.

### Head Tracking

Head Tracking supports TrackIR or OpenTrack.

You can:

- Choose a provider.
- View live six-axis tracking data.
- Recenter tracking.
- Select a calibration preset.
- Tune individual axes.
- Configure deadzones.
- Configure clamps.
- Invert axes.
- Adjust smoothing.
- Adjust curves.
- Configure the camera mask.
- Configure additive rider lean mapping.

</details>

---

<details>
<summary><strong>Nitrous System</strong></summary>

<br>

Nitrous kits are available under:

**Performance > Nitrous System**

Available bottle sizes:

- **5 lb Compact**
- **10 lb Race**
- **20 lb Drag**

Setup-specific boost can be adjusted from:

```text
+25% to +200%
```

Higher boost consumes nitrous charge proportionally faster.

By default, hold:

```text
Left Shift
```

to spray.

You can also bind another keyboard/controller input or choose **Automatic WOT**.

A newly fitted sled receives one full bottle.

After that, charge persists through:

- Resets.
- Teleports.
- Respawns.
- Reloads.

### Refilling

At an active gas station, use the normal refuel control.

The selected Utility/Settings refill target determines whether the station fills:

- Gasoline.
- Nitrous.
- Both.

The small station prompt remains visible even when the optional nitrous meter is hidden.

On maps without stations, press:

```text
N
```

while parked with the engine off to refill the fitted bottle.

An empty bottle immediately returns engine output to its unboosted value.

</details>

---

<details>
<summary><strong>Experimental Systems</strong></summary>

<br>

Experimental systems are opt-in and can be enabled or disabled independently.

### Track Compatibility

Exposes geometry-validated cross-platform rear assemblies and clearly labelled, front-anchored scaled fallbacks across the native:

```text
120–174 in
```

track range.

### Hidden Vehicles

Appends validated native assets only when they are not locked or entitlement-controlled.

### World-Prop Bodies

World-prop bodies retain the native:

- Drivetrain.
- Rider.
- Suspension.
- Collision.

Alpine then projects supported trucks and scenery with adjustable:

- Fit.
- Mass.
- Center of mass.

Static sled props are intentionally excluded because their fixed skis and handlebars cannot safely follow a rideable sled.

### Tracking Lean

Adds calibrated tracking output to physical rider input without replacing the controller signal.

### Multiplayer Behavior

These experimental systems are local visual/gameplay projections.

Other connected players may see the underlying native sled and mounted rider state.

The master Runtime switch remounts the rider and restores all experimental projections.

</details>

---

<details>
<summary><strong>Sled Forge</strong></summary>

<br>

**Sled Forge** is Alpine's donor-part workshop.

Choose compatible donor cosmetic assemblies for:

- Body shell.
- Hood.
- Seat.
- Bumper.
- Handlebars.
- Skis.
- Running boards.

Every donor projection keeps the source sled's:

- Rider.
- Camera.
- Collision.
- Controls.
- Simulation graph.

The Forge displays a compatibility score and always keeps native moving parts visible when a donor cannot provide a safe animated replacement.

### World Sled Props

World sled props use the same hybrid articulated projection system.

Separated prop:

- Skis.
- Handlebars.
- Track groups.

follow the live native anchors.

Any missing group falls back to the source sled's moving assembly.

Rear-track physics packages remain separately validated native grafts.

Unsafe front/rear physics graphs are never installed.

</details>

---

<details>
<summary><strong>Multiplayer & Build Showcase</strong></summary>

<br>

Compatible Alpine Tuning clients automatically discover one another through Sledders' internal relay when available, with Steam P2P as a fallback.

The **Build Showcase** lists active compatible builds in the garage.

It can:

- Display compatible player builds.
- Request a shared setup.
- Import a shared setup.
- Display compact nearby-rider build tags.

Build sharing, visual receiving, and tags are configurable from the Showcase.

### Compatibility

Both riders should use the corrected networking build.

Internal tune data is sent only after the receiving game server acknowledges Alpine support.

Owning a lobby does not guarantee that its server runs Alpine.

Unsupported servers leave internal sharing waiting, with Steam P2P available when peer Steam IDs can be resolved.

This prevents tune broadcasts from being mistaken for race commands.

### Remote Physics

Networked projection is presentation-only.

It can synchronize supported:

- Donor visuals.
- Prop configuration.
- Lights.
- Audio.
- Build metadata.

It never changes:

- Remote physics.
- Ownership.
- Game-server authority.

A version, protocol, catalog, or game-build mismatch safely retains the native sled appearance.

</details>

---

<details>
<summary><strong>Saved Setups & Recovery</strong></summary>

<br>

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

Alpine keeps setups separated by sled.

A setup created for one sled cannot accidentally overwrite a different sled.

Loading a saved setup while unsaved changes exist requires confirmation.

Existing compatible setups from older versions are kept when possible.

</details>

---

<details>
<summary><strong>Units & Tuning Behaviour</strong></summary>

<br>

### Units

- Engine output is shown in **kW** with Metric units and **hp** with Imperial units.
- Weight is shown in **kg** or **lb**.
- Ski stance is shown in millimetres or inches.
- Sledders' Power, Climbing, and Agility ratings remain on their normal `0–100` scale.
- Brake settings are shown as a percentage of factory brake strength.

### Factory Baselines

Steering, suspension, grip, and drivetrain changes are applied from the sled's original factory values.

This prevents repeated setup changes from stacking incorrectly.

### Track Tuning

Generic track tuning changes physics only.

Length kits reload the original sled and validate a native donor using tunnel seam and track geometry.

Alpine then replaces only the required rear components:

- Tunnel/rear bumper.
- Skid.
- Rails.
- Wheels.
- Animated track.
- Contact mesh.
- Trax graph.

The following remain native to the source sled:

- Hood.
- Seat.
- Cockpit.
- Front suspension.
- Lighting.
- Engine.
- Fuel.
- Accessories.
- Sled identity.

Arctic Cat's embedded `146`, `154`, and `165` rear variants are preferred where complete.

Otherwise Alpine uses either:

1. A validated direct graft, or
2. With **Experimental Track Compatibility** enabled, an explicitly labelled front-anchored scaled fallback.

</details>

---

<details>
<summary><strong>Updating Alpine Tuning</strong></summary>

<br>

Close Sledders before replacing the Alpine Tuning DLL.

Download the latest version from:

[Alpine Tuning Releases](https://github.com/Ronnie-Reagan/Alpine-Tuning/releases/latest)

Replace the existing:

```text
Alpine Tuning.dll
```

inside the Sledders `Mods` folder.

It is recommended that you back up important saved setups before installing a major update.

</details>

---

<details>
<summary><strong>Developer Build Instructions</strong></summary>

<br>

This section is only needed when building Alpine Tuning from source.

### Build

Run:

```text
build-release.bat
```

The completed DLL is placed at:

```text
SleddersTuner\bin\x64\Release\Alpine Tuning.dll
```

The build script:

- Checks release files.
- Runs automated tests.
- Builds the mod.
- Installs the verified DLL into the standard Sledders `Mods` folder.

### Validation-Only Build

To run the same release gate without installing the DLL, use PowerShell:

```powershell
$env:ALPINE_VALIDATE_ONLY = '1'
try { .\build-release.bat } finally { Remove-Item Env:ALPINE_VALIDATE_ONLY }
```

Validation builds from the Git public-file inventory in temporary staging and removes that staging afterward.

It includes allowlisted untracked files, so add all intended source, test, script, and asset changes before committing a release.

Validation-only mode does not update the local release DLL.

### Build Requirements

Required:

- Git.
- .NET SDK.
- .NET Framework 4.7.2 targeting assemblies.
- A Sledders installation.
- MelonLoader.

The test runner uses the installed game assemblies.

### Release Smoke Test

Before publishing a release, test the completed DLL inside Sledders.

At minimum:

1. Launch Sledders.
2. Open the garage.
3. Open **TUNING**.
4. Apply a tune.
5. Restore the tune.
6. Save a setup.
7. Reload the setup.
8. Switch sleds.
9. Confirm expected behavior.
10. Check multiplayer replication with another player.

Automated checks do not exercise a running Unity scene or confirm visual behavior in-game.

### Non-Standard Steam Libraries

When Steam uses another library location, edit:

```text
GAME_DIR
```

near the top of:

```text
build-release.bat
```

</details>

---

## License & Attribution

Alpine Tuning is an unofficial community mod and is not affiliated with the developers of **Sledders**.

See [`license.txt`](license.txt) for source use, redistribution, attribution, and warranty terms.

Back up important setup data before updating.

**Use the mod at your own risk.**
