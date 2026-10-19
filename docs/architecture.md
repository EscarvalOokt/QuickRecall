# Quick Recall architecture

This document describes the current Quick Recall 1.0.0 source and its implementation boundaries.

## Sources of truth

- [QuickRecallMod](../src/QuickRecallMod.cs) defines item selection, pending use, restoration and runtime integration.
- [QuickRecallConfig](../src/QuickRecallConfig.cs) defines the typed configuration contract.
- Terraria and TerrariaModder define the external item-use, selection and loader contracts used by the mod.
- [The manifest](../mod/manifest.json) defines package identity and declared dependency requirements; [the project file](../src/QuickRecall.csproj) defines build references.
- This document records responsibility boundaries. [Testing policy](testing.md) defines the separate automated and in-game verification boundaries.

Resolve discrepancies with the implementation explicitly. The presence of a code path or declared dependency does not establish that a runtime scenario has passed.

## Runtime baseline

- Mod ID: `quick-recall`; production assembly: `QuickRecall.dll`; project version: `1.0.0`.
- The manifest declares Terraria `1.4.5` and TerrariaModder Core `>=0.4.1`.
- The documented single-player validation configuration is Terraria `1.4.5.8` with Core `0.4.2`. This does not establish compatibility with Core `0.4.1`, other versions or other game modes.
- The production project targets .NET Framework 4.8 and references Terraria, TerrariaModder Core, Harmony, XNA Game and XNA Content Pipeline assemblies.
- Recall processing is scoped to the active local player. The mod implements no separate multiplayer synchronization.

## Runtime flow

1. The registered `Use recall item` keybind, defaulting to **Alt + Q**, calls `OnQuickRecall`. It ignores requests for an inactive or dead player and requests made while use or restoration is already pending.
2. `FindRecallItemSlot` scans `player.inventory`, skipping empty items and nonpositive stacks. Priority is Shellphone, Shellphone Spawn, Shellphone Ocean or Shellphone Hell; then Cell Phone; then Magic Mirror or Ice Mirror; then Recall Potion when enabled. Equal-priority items retain the first matching inventory slot. If no eligible item is found, the mod displays a chat message.
3. `QueueRecallUse` records the slot, item type and player. `ProcessPendingUse`, called from `FrameEvents.OnPostUpdate`, rechecks that identity and eligibility. It waits for active selection overrides and buffered changes to settle before capturing `LastNonOverridenSelection` and the current hotbar slot.
4. If a different slot is needed, `TrySelectItem` calls `selectedItemState.Select` and retains a restoration candidate. A later update verifies that the selection has actually taken effect. An existing item animation ends the pending request without issuing another synthetic use.
5. `TriggerItemUse` temporarily sets `altFunctionUse = 0`, `releaseUseItem = true` and `controlUseItem = true`, then calls `Player.ItemCheck()`. All three previous values are restored in `finally`. Terraria owns the selected item's action, destination and consumption; Quick Recall does not perform its own teleportation.
6. Ending or cancelling the use request schedules restoration only when a valid restoration candidate remains for the active player. `ProcessPendingRestore` waits until the game permits selection, requests the original slot and checks whether that request took effect. Death, loss of the local player or invalid state clears the corresponding pending work.

Pending use and restoration each have a budget of **300 game updates**. `OnPostUpdate` derives elapsed updates from `Main.GameUpdateCount`; these budgets do not measure wall-clock seconds and do not advance when the count is unchanged. A restoration created during the current callback is not charged for updates that elapsed before it existed.

## Selection ownership and restoration

Harmony installs a prefix on `Player.SelectedItemState.Select(int)` under `quick-recall.selection-observer`. Initialization requires that method and rejects a second active Quick Recall observer.

Once the recall selection has been requested, or while restoration is pending, the observer records external calls selecting a local-player hotbar slot `0–9`. This includes reselecting the same slot. `TrySelectItem` marks the mod's own calls so they do not count as external choices. The observer does not distinguish a player's selection from another component's external call.

`CancelForExternalHotbarSelection` clears use, restoration and the restoration candidate without scheduling another restoration. `OnPostUpdate` checks this before processing and again after item use, so a selection made during `ItemCheck` also takes precedence. The first cancellation check runs even when the game-update count has not advanced.

Additional checks compare the expected hotbar, buffered selection and effective selected slot. Temporary overrides are allowed to settle. Restoration handles Terraria falling back to the hotbar after a consumed recall item leaves an empty inventory slot, but abandons an unrelated selection and does not request an empty original slot outside the hotbar. A selection request alone is never treated as proof that it was applied.

## Configuration

`QuickRecallConfig : ModConfig` has schema version `1`, independent of product version `1.0.0`. Its client setting `ShouldUsePotions`, labelled **Use Recall Potions**, defaults to `false`.

`LoadConfig` reads `context.GetConfig<QuickRecallConfig>()` and copies the setting into `_shouldUsePotions`, falling back to `false` if no configuration is returned. `OnConfigChanged` reloads it. Pending item eligibility is checked again during processing, so disabling potions can invalidate a queued potion use. TerrariaModder owns configuration storage and keybind management; the mod has no separate configuration file format.

## Lifecycle

`QuickRecallMod` implements `IMod` and `IModLifecycle`.

- `Initialize` acquires logging and configuration, installs the selection observer, captures the game-update count, subscribes to `FrameEvents.OnPostUpdate` and registers the keybind. There is no `OnPreUpdate` subscription.
- If initialization throws, it attempts to disable the registered keybind, invokes `Unload` for cleanup and rethrows the error.
- `OnWorldLoad` and `OnWorldUnload` clear pending use, restoration, restoration candidates and the external-selection flag, then reset the update-count baseline.
- `OnConfigChanged` reloads the typed setting. `OnContentReady` has no project-specific work.
- `Unload` releases observer ownership, removes the frame-event subscription, clears transient state and removes the mod's Harmony prefix.

These are implementation responsibilities; their verification in the target loader belongs to the testing policy.

## Project boundaries

- Production uses one assembly. `QuickRecallMod` owns priority selection, pending-operation state, selection restoration and the Harmony observer; `QuickRecallConfig` owns the typed setting declaration.
- Terraria owns inventory contents, selection buffering and overrides, item animation and the actual item action. The mod uses those contracts instead of implementing a teleport or inventory system.
- TerrariaModder owns lifecycle dispatch, frame events, logging, keybind registration and configuration infrastructure. Harmony supplies the selection interception mechanism.
- Pending-operation state is transient and is cleared across world transitions. The implementation does not introduce its own save format or network protocol.
- Machine-specific build paths belong in ignored `src/QuickRecall.Local.props`; the portable example is [QuickRecall.Local.props.example](QuickRecall.Local.props.example). Build and test entry points belong to [Development](../README.md#development), and verification procedures belong to [Testing policy](testing.md).