# Alpine Tuning

Alpine Tuning adds mechanical tuning and setup options to the Sledders garage while matching the style of the game's existing menus.

Current public version: **2026.07.17**
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

The main tuning categories are:

* **Engine** — engines, internal parts, intake, exhaust, turbo, and engine swaps.
* **Drivetrain** — clutch setup, clutch weights, gearing, and brake calibration.
* **Suspension** — suspension parts, chassis setup, shocks, springs, balance, and weight transfer.
* **Track** — track type, lug height, grip, and traction adjustments.
* **Steering** — skis, ski stance, grip, and steering geometry.
* **Lighting** — headlight colour, brightness, beam type, and aim.
* **Settings** — display units and headlight hotkey settings.
* **Setups** — save, load, rename, recover, and manage tunes.

Changes are added to your current working setup immediately.

Use:

* **Save** to keep the setup.
* **Reset** to return the current sled to its factory setup.
* **DYNO** to view estimated performance information.
* **Back** to return to the previous menu.

Some changes require the sled to be rebuilt before they take effect. Alpine handles this automatically when the setup is saved.

When leaving with unsaved changes, you can:

* Save and exit.
* Continue tuning.
* Exit without saving.

## Comparing Parts

Alpine shows how the current sled compares with its factory setup.

When viewing another part or adjustment, it also previews how that choice would change the sled.

Comparison bars use the following colours:

* **Gray** — factory value.
* **Lime** — an improvement.
* **Orange** — a reduction.
* **Blue** — a change that is not automatically better or worse, such as ski stance.

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

* **Display Units** — Metric or Imperial.
* **Headlight Hotkey** — enable, disable, change, or clear the binding.

While choosing a new hotkey, the menu will display **Waiting**.

Press Escape, use the controller Cancel button, or select Cancel to stop without changing the binding.

Clearing an existing binding requires confirmation.

## Saved Setups

Open **Setups** from the main tuning menu.

The list includes:

* **Current Draft** — the setup currently being edited.
* Saved setups.
* **Recovery** options when older or damaged setup data can be restored.

Saved setups include names and short summaries to help identify them.

You can:

* Save the current tune as a new setup.
* Rename saved setups.
* Choose a default setup.
* Preview a setup before loading it.
* Recover removed or damaged setups.
* Restore older revisions.

Alpine keeps setups separated by sled. A setup created for one sled cannot accidentally overwrite a different sled.

Loading a saved setup while you have unsaved changes requires confirmation.

Existing compatible setups from older versions are kept when possible.

## Units and Tuning Behaviour

* Engine output is shown in **kW** with Metric units and **hp** with Imperial units.
* Weight is shown in **kg** or **lb**.
* Ski stance is shown in millimetres or inches.
* The game's Power, Climbing, and Agility ratings remain on their normal 0–100 scale.
* Brake settings are shown as a percentage of the factory brake strength.
* Steering, suspension, grip, and drivetrain changes are applied from the sled's original factory values to prevent repeated setup changes from stacking incorrectly.
* The Climbing Track Kit changes traction, rotating weight, overall weight, and balance. It does not replace the visible track model.

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

When Steam uses another library location, edit `GAME_DIR` near the top of `build-release.bat`.

## License and Attribution

Alpine Tuning is an unofficial community mod and is not affiliated with the developers of Sledders.

See [license.txt](license.txt) for source use, redistribution, attribution, and warranty terms.

Back up important setup data before updating. Use the mod at your own risk.
