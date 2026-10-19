# Quick Recall testing policy

This document defines automated testing and in-game acceptance boundaries for Quick Recall. The implementation and its ownership boundaries are described in [Architecture](architecture.md).

## Testing principle

Tests should protect meaningful project-owned behavior and realistic regressions. Test count and broad method-level coverage are not goals by themselves.

Terraria owns item use, selection buffering, animations and teleportation. TerrariaModder owns loading, keybind dispatch, configuration and frame events. Their interaction with Quick Recall requires verification in the game. A mock of these dependencies cannot establish their actual behavior.

Builds, automated tests, in-game acceptance and publication are separate results. A successful build or test run does not establish compatibility with another game version, loader version or multiplayer mode.

## Validation status

On **2026-10-19**, the maintainer reported successful in-game validation of the installed Quick Recall **1.0.0 Release candidate** in **Terraria 1.4.5.8 / TerrariaModder Core 0.4.2, single-player**.

The validation record contains no candidate hash, source revision or per-scenario results. The scenarios below define repeatable maintenance checks and their expected results.

Host & Play, dedicated servers, multiplayer and other game or Core versions are outside the verified compatibility scope. The broader constraints declared in the [package manifest](../mod/manifest.json) do not establish verification of every accepted version.

## Automated test boundary

The existing [QuickRecall.Tests project](../tests/QuickRecall.Tests/QuickRecall.Tests.csproj) targets .NET Framework 4.8 and uses NUnit with the NUnit test adapter and Microsoft.NET.Test.Sdk. It has no reference to the production project and does not compile or exercise Quick Recall's production source.

Its only test, [TestRunnerStarts](../tests/QuickRecall.Tests/Project/ProjectSmokeTests.cs), calls `Assert.Pass()`. A successful run checks test discovery and execution infrastructure. It does not cover recall-item priority, potion settings, pending actions, selection restoration, lifecycle cleanup or the Harmony selection observer.

Build and test entry points, prerequisites and local configuration are documented in [Development](../README.md#development). Running the test project alone does not build or validate the production mod.

## Runtime acceptance boundary

Use the game, Core version and installed mod candidate relevant to the change. For the established baseline, use Terraria 1.4.5.8 and Core 0.4.2 in single-player. Record the actual mod version, available source or package identity, relevant settings and observed result for a new verification run. Keep source inspection and expected behavior distinct from observations made in the game.

Start with a live character and a known inventory and selection. Use the currently assigned shortcut; the default for a fresh binding is **Alt + Q**. Account for an existing saved binding when checking the default. Prepare the relevant recall items and enable **Use Recall Potions** only for scenarios that require it.

| Scenario | Preconditions and action | Expected result |
| --- | --- | --- |
| Loading and controls | Install the matching DLL and manifest, launch through TerrariaModder, enter a world and locate Quick Recall's keybind and setting. Rebind the shortcut and use it. | The mod loads without attributable errors; the assigned shortcut invokes one recall operation and the setting is available. A saved user binding is not treated as a fresh default. |
| Item priority | Place several eligible recall items in the inventory. Invoke recall, then remove the highest-priority group and repeat. Exercise all four Shellphone variants separately. | The order is Shellphone variants, Cell Phone, Magic Mirror / Ice Mirror, then an allowed Recall Potion. The selected item's normal Terraria behavior and destination apply. |
| Equal priority and inventory position | Put two items from the same priority group in different inventory slots, with no higher-priority item available. Repeat with the winning item outside the hotbar. | The first eligible item in inventory order is chosen; an item outside the hotbar can be selected and used. |
| Potion setting | With only Recall Potions available, invoke recall with Use Recall Potions off, then turn it on and repeat. Add a reusable recall item and repeat while potions remain enabled. | The disabled setting prevents potion use. The enabled setting permits a potion when no higher-priority item is available. A reusable item takes priority over a potion. |
| Configuration persistence | Change Use Recall Potions, verify its effect, then reload the mod or game and inspect the saved setting and binding. | The current setting takes effect through the config callback; saved preferences are retained by the configured Core environment. |
| No eligible item | Remove eligible items, or keep only potions while their use is disabled, and press the shortcut. | The message "No usable recall item found." appears; no recall item is used. |
| Selection restoration | Begin with another item selected, invoke recall and allow it to complete without manual selection. Repeat with the recall item already selected. | A temporary change is restored when Terraria allows it. An already selected recall item does not cause an unnecessary switch to another slot. |
| Last potion | Enable potions, place a stack of one in a non-hotbar inventory slot, select another item and invoke recall. | The potion is consumed by normal item use; the empty recall slot does not prevent restoration of the previous valid selection. |
| Manual selection | After the mod has selected the recall item or while restoration is pending, manually select a different hotbar slot. Also reselect the current hotbar slot. | The player's selection takes precedence. The mod cancels its pending use/restoration rather than forcing its previous selection back. |
| Busy player and repeated input | Invoke recall while another item action is active or a selection is buffered. Press the shortcut repeatedly while a mod operation is pending. Also exercise a recall item whose animation has already started before the pending use is processed. | Blocked selection is allowed to settle without overlapping pending operations or use of an unrelated item. If an item animation is already active after the recall selection takes effect, the mod ends its pending use and schedules applicable restoration without issuing a second synthetic use. |
| Temporary selection override | Exercise Smart Select or another available vanilla selection override while a recall or restoration is pending, then release the override. | The mod respects the temporary override and waits for the relevant selection state to settle. An independent player choice still takes precedence. |
| Pause and timeout | Stop gameplay updates using single-player pause while an operation is pending, then resume. Where reproducible, keep selection or restoration blocked while game updates advance until its 300-update budget expires. | A stopped game-update counter does not consume the budget; opening a menu that leaves updates running is not equivalent. A timed-out use ends and may schedule applicable restoration; restoration has its own budget. Pending work eventually clears rather than remaining stuck, and later recall attempts can proceed. |
| Inventory interruption | While recall is waiting, remove, move or replace the queued item, or disable potion use for a queued potion. | The queued slot and item are revalidated; an invalid queued item is not used. Any applicable restoration follows the normal selection rules. |
| Invalid restoration target | During a recall that temporarily changed selection, empty the previous non-hotbar slot before restoration. | The mod abandons restoration to the empty slot rather than repeatedly requesting an invalid selection. |
| Player and world lifecycle | Interrupt a pending action with player death or a world transition. Disable/unload and then reload the mod, and invoke recall again. | Pending state does not carry into another player/world session. Cleanup removes the mod's frame subscription and selection patch; reinitialization does not duplicate recall handling. |
| Input after recall or interruption | Complete or interrupt recall, then use normal items and change slots. Inspect logs for attributable failures. | Synthetic input does not remain held after the item-use call, and ordinary item use and selection remain responsive. |

If a timing-dependent branch cannot be exercised with the available setup, record that limitation for the new run. Do not infer its result from compilation, another scenario or the existence of a guard in the source. Any additional mod used to reproduce a condition becomes part of that run's stated environment, not a general interoperability claim.

Multiplayer or another runtime configuration requires its own agreed verification scope. Single-player acceptance must not be presented as coverage of those modes.

## Release candidate validation

When preparing or changing an installable candidate:

1. Identify the intended Quick Recall source state, mod version and Terraria/Core dependency combination. Use the build instructions in [Development](../README.md#development), choosing deployment behavior explicitly.
2. Run the existing automated suite and report its smoke-test boundary. Keep this result separate from production build and game results.
3. Assemble the candidate from `QuickRecall.dll` and its matching `manifest.json`. Verify that the manifest's ID, version and entry DLL agree with the intended build and that the files are placed directly in `TerrariaModder/mods/quick-recall/`, as described in [Installation](../README.md#installation).
4. Launch the installed candidate and perform the applicable acceptance scenarios above. Record actual results and any material unexercised conditions; do not substitute a check of a different development build for the candidate being assessed.
5. After a relevant correction, repeat the affected scenarios and the checks needed to establish that the final installed candidate still loads and behaves correctly.

Repeat verification when implementation, dependencies, configuration handling, packaging or another relevant condition changes, or when verification is explicitly requested. Documentation-only changes do not by themselves require rebuilding or repeating in-game acceptance.

An installed and accepted candidate is not proof of publication. Public download availability requires separate confirmation.

## Adding tests

Add deterministic tests when they protect a meaningful project-owned contract or reproduce a realistic regression. Recall-item ordering and relevant state transitions are possible future candidates where they can be exercised with explicit inputs and an appropriate production boundary. They are not covered by the current smoke test.

Use the existing test infrastructure where suitable. Do not add a framework, duplicate Terraria's runtime or reorganize production code solely to increase test count or fill this policy. Any production refactoring needed for testing requires its own implementation scope.

Extend in-game scenarios when a change affects selection timing, item use, input ownership, the Harmony observer, lifecycle or Core configuration integration. Keep expected behavior, executed results and supported runtime scope distinct.