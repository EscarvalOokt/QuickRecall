using System;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.ID;
using TerrariaModder.Core;
using TerrariaModder.Core.Events;
using TerrariaModder.Core.Input;
using TerrariaModder.Core.Logging;

namespace QuickRecall
{
    internal class QuickRecallMod : IMod, IModLifecycle
    {
        private const int MaxPendingUseFrames = 300;
        private const int MaxRestoreFrames = 300;
        private const string SelectionObserverId = "quick-recall.selection-observer";

        private static QuickRecallMod _selectionObserverOwner;

        private ModContext _context;

        private bool _externalHotbarSelection;
        private bool _hasRestoreCandidate;
        private bool _isSelectingItem;
        private uint _lastGameUpdateCount;
        private ILogger _log;
        private int _pendingUseExpectedHotbar = -1;
        private int _pendingUseFramesLeft;
        private int _pendingUseItemType = -1;
        private int _pendingUsePlayerWhoAmI = -1;
        private bool _pendingUseSelected;
        private int _pendingUseSlot = -1;
        private int _restoreCandidateHotbar = -1;
        private int _restoreCandidatePlayerWhoAmI = -1;
        private int _restoreCandidateSelectedSlot = -1;
        private int _restoreExpectedHotbar = -1;
        private int _restoreFramesLeft;
        private int _restoreHotbar = -1;

        private bool _restorePending;
        private int _restorePlayerWhoAmI = -1;
        private int _restoreRecallSlot = -1;
        private bool _restoreRequested;
        private int _restoreSelectedSlot = -1;

        private Harmony _selectionHarmony;
        private MethodInfo _selectionMethod;
        private MethodInfo _selectionPrefix;
        private bool _shouldUsePotions;

        private bool _usePending;

        public string Id => "quick-recall";
        public string Name => "Quick Recall";
        public string Version => "1.0.0";

        public void Initialize(ModContext context)
        {
            _context = context;
            _log = context.Logger;

            try
            {
                LoadConfig();
                InstallSelectionObserver();

                _lastGameUpdateCount = Main.GameUpdateCount;
                FrameEvents.OnPostUpdate += OnPostUpdate;

                context.RegisterKeybind("quick-recall", "Quick Recall", "Use recall item", "Alt + Q", OnQuickRecall);
            }
            catch
            {
                // Core does not call Unload after a failed Initialize.
                try
                {
                    Keybind keybind = context.GetKeybind("quick-recall");
                    if (keybind != null)
                        keybind.Enabled = false;
                }
                finally
                {
                    Unload();
                }

                throw;
            }
        }

        public void Unload()
        {
            if (_selectionObserverOwner == this)
                _selectionObserverOwner = null;

            FrameEvents.OnPostUpdate -= OnPostUpdate;

            ClearPendingUseState();
            ClearRestoreState();
            ClearRestoreCandidateState();
            _externalHotbarSelection = false;
            _lastGameUpdateCount = Main.GameUpdateCount;

            RemoveSelectionObserver();
        }

        public void OnContentReady(ModContext context)
        {
        }

        public void OnWorldLoad()
        {
            ClearPendingUseState();
            ClearRestoreState();
            ClearRestoreCandidateState();
            _externalHotbarSelection = false;
            _lastGameUpdateCount = Main.GameUpdateCount;
        }

        public void OnWorldUnload()
        {
            ClearPendingUseState();
            ClearRestoreState();
            ClearRestoreCandidateState();
            _externalHotbarSelection = false;
            _lastGameUpdateCount = Main.GameUpdateCount;
        }

        public void OnConfigChanged()
        {
            LoadConfig();
        }

        private void LoadConfig()
        {
            _shouldUsePotions = _context.GetConfig<QuickRecallConfig>()?.ShouldUsePotions ?? false;
        }

        private void InstallSelectionObserver()
        {
            if (_selectionObserverOwner != null)
                throw new InvalidOperationException("A Quick Recall selection observer is already active.");

            _selectionMethod = typeof(Player.SelectedItemState).GetMethod(
                "Select",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(int) },
                null);
            if (_selectionMethod == null || _selectionMethod.ReturnType != typeof(void))
                throw new MissingMethodException("Terraria.Player.SelectedItemState.Select(int) was not found.");

            _selectionPrefix = typeof(QuickRecallMod).GetMethod(
                nameof(ObserveHotbarSelection),
                BindingFlags.NonPublic | BindingFlags.Static);
            _selectionHarmony = new Harmony(SelectionObserverId);
            _selectionHarmony.Patch(_selectionMethod, prefix: new HarmonyMethod(_selectionPrefix));
            _selectionObserverOwner = this;
        }

        private void RemoveSelectionObserver()
        {
            if (_selectionObserverOwner == this)
                _selectionObserverOwner = null;

            if (_selectionHarmony == null)
                return;

            _selectionHarmony.Unpatch(_selectionMethod, _selectionPrefix);
            _selectionHarmony = null;
            _selectionMethod = null;
            _selectionPrefix = null;
        }

        // ReSharper disable once InconsistentNaming
        private static void ObserveHotbarSelection(Player ___player, int __0)
        {
            QuickRecallMod mod = _selectionObserverOwner;
            if (mod == null ||
                mod._isSelectingItem ||
                __0 < 0 ||
                __0 >= 10 ||
                (!mod._pendingUseSelected && !mod._restorePending))
                return;

            // SelectedItemState is a struct; its player field identifies the owner.
            Player[] players = Main.player;
            int localPlayer = Main.myPlayer;
            if (___player != null &&
                players != null &&
                localPlayer >= 0 &&
                localPlayer < players.Length &&
                ___player == players[localPlayer])
                mod._externalHotbarSelection = true;
        }

        private bool CancelForExternalHotbarSelection()
        {
            if (!_externalHotbarSelection)
                return false;

            // Do not use CancelPendingUse: it would schedule another restoration.
            ClearPendingUseState();
            ClearRestoreCandidateState();
            ClearRestoreState();
            _externalHotbarSelection = false;
            return true;
        }

        private void OnQuickRecall()
        {
            if (_selectionObserverOwner != this)
                return;

            try
            {
                Player player = Main.player[Main.myPlayer];
                if (player == null || !player.active || player.dead)
                    return;

                if (_usePending || _restorePending)
                    return;

                Item[] inventory = player.inventory;
                if (inventory == null)
                    return;

                int inventorySlotIndex = FindRecallItemSlot(inventory);
                if (inventorySlotIndex == -1)
                {
                    Main.NewText("No usable recall item found.", 255, 100, 100);
                    return;
                }

                QueueRecallUse(player, inventorySlotIndex);
            }
            catch (Exception ex)
            {
                _log.Error("[Quick Recall] OnQuickRecall error: " + ex);
            }
        }

        private void OnPostUpdate()
        {
            uint gameUpdateCount = Main.GameUpdateCount;
            uint elapsedUpdates = unchecked(gameUpdateCount - _lastGameUpdateCount);
            _lastGameUpdateCount = gameUpdateCount;

            if (CancelForExternalHotbarSelection())
                return;

            // Core also fires frame events while gameplay is paused.
            if (elapsedUpdates == 0)
                return;

            bool restoreWasPending = _restorePending;

            try
            {
                ProcessPendingUse(elapsedUpdates);
            }
            catch (Exception ex)
            {
                _log.Error("[Quick Recall] ProcessPendingUse error: " + ex);
                Player player = _pendingUsePlayerWhoAmI >= 0 && _pendingUsePlayerWhoAmI < Main.maxPlayers
                    ? Main.player[_pendingUsePlayerWhoAmI]
                    : null;
                CancelPendingUse(player);
            }

            // An external Select during ItemCheck must also win over a restore created above.
            if (CancelForExternalHotbarSelection())
                return;

            try
            {
                // A restore created above has not waited through these past updates.
                ProcessPendingRestore(restoreWasPending ? elapsedUpdates : 0);
            }
            catch (Exception ex)
            {
                _log.Error("[Quick Recall] ProcessPendingRestore error: " + ex);
                ClearRestoreState();
            }

            if (!_usePending && !_restorePending)
                _externalHotbarSelection = false;
        }

        private int FindRecallItemSlot(Item[] inventory)
        {
            int bestSlot = -1;
            int bestPriority = int.MaxValue;

            for (var i = 0; i < inventory.Length; i++)
            {
                Item item = inventory[i];

                if (item == null || item.IsAir || item.stack <= 0)
                    continue;

                int priority = GetRecallItemPriority(item.type);
                if (priority == -1)
                    continue;

                if (priority < bestPriority)
                {
                    bestPriority = priority;
                    bestSlot = i;

                    if (bestPriority == 0)
                        return bestSlot;
                }
            }

            return bestSlot;
        }

        private int GetRecallItemPriority(int itemType)
        {
            if (IsShellphone(itemType))
                return 0;

            if (itemType == ItemID.CellPhone)
                return 1;

            if (itemType == ItemID.MagicMirror || itemType == ItemID.IceMirror)
                return 2;

            if (itemType == ItemID.RecallPotion && _shouldUsePotions)
                return 3;

            return -1;
        }

        private bool IsShellphone(int itemType)
        {
            return itemType == ItemID.Shellphone ||
                   itemType == ItemID.ShellphoneSpawn ||
                   itemType == ItemID.ShellphoneOcean ||
                   itemType == ItemID.ShellphoneHell;
        }

        private void QueueRecallUse(Player player, int inventorySlotIndex)
        {
            if (player.inventory == null)
                return;

            if (inventorySlotIndex < 0 || inventorySlotIndex >= player.inventory.Length)
                return;

            Item item = player.inventory[inventorySlotIndex];
            if (item == null || item.IsAir || item.stack <= 0)
                return;

            _externalHotbarSelection = false;
            _usePending = true;
            _pendingUseSlot = inventorySlotIndex;
            _pendingUseItemType = item.type;
            _pendingUsePlayerWhoAmI = player.whoAmI;
            _pendingUseSelected = false;
            _pendingUseFramesLeft = MaxPendingUseFrames;
        }

        private void ProcessPendingUse(uint elapsedUpdates)
        {
            if (!_usePending)
                return;

            if (_pendingUsePlayerWhoAmI < 0 || _pendingUsePlayerWhoAmI >= Main.maxPlayers)
            {
                ClearPendingUseState();
                ClearRestoreCandidateState();
                return;
            }

            Player player = Main.player[_pendingUsePlayerWhoAmI];
            if (player == null || !player.active || player.dead || player != Main.LocalPlayer)
            {
                ClearPendingUseState();
                ClearRestoreCandidateState();
                return;
            }

            _pendingUseFramesLeft -= (int)Math.Min(elapsedUpdates, (uint)_pendingUseFramesLeft);
            if (_pendingUseFramesLeft <= 0)
            {
                _log.Debug("[Quick Recall] Pending use timed out.");
                CancelPendingUse(player);
                return;
            }

            Item[] inventory = player.inventory;
            if (inventory == null || _pendingUseSlot < 0 || _pendingUseSlot >= inventory.Length)
            {
                CancelPendingUse(player);
                return;
            }

            Item item = inventory[_pendingUseSlot];
            if (item == null ||
                item.IsAir ||
                item.stack <= 0 ||
                item.type != _pendingUseItemType ||
                GetRecallItemPriority(item.type) < 0)
            {
                CancelPendingUse(player);
                return;
            }

            if (!_pendingUseSelected)
            {
                // An active override or buffered choice must settle before capturing the player's selection.
                if (!player.selectedItemState.CanChangeSelectedItemImmediately ||
                    player.selectedItemState.HasActiveOverride ||
                    player.selectedItemState.HasBufferedChange)
                    return;

                int previousSelectedSlot = player.selectedItemState.LastNonOverridenSelection;
                if (previousSelectedSlot < 0 || previousSelectedSlot >= inventory.Length)
                {
                    CancelPendingUse(player);
                    return;
                }

                if (_pendingUseSlot != previousSelectedSlot)
                {
                    int previousHotbar = player.selectedItemState.Hotbar;
                    if (!TrySelectItem(player, _pendingUseSlot))
                    {
                        CancelPendingUse(player);
                        return;
                    }

                    _hasRestoreCandidate = true;
                    _restoreCandidateHotbar = previousHotbar;
                    _restoreCandidateSelectedSlot = previousSelectedSlot;
                    _restoreCandidatePlayerWhoAmI = player.whoAmI;
                }
                else
                {
                    ClearRestoreCandidateState();
                }

                _pendingUseExpectedHotbar = player.selectedItemState.Hotbar;
                _pendingUseSelected = true;
                return;
            }

            if (player.selectedItemState.Hotbar != _pendingUseExpectedHotbar ||
                (player.selectedItemState.HasBufferedChange &&
                 player.selectedItemState.LastNonOverridenSelection != _pendingUseSlot))
            {
                ClearPendingUseState();
                ClearRestoreCandidateState();
                return;
            }

            // The request is not proof that the game applied it. Never use whatever happens to
            // occupy the player's selected slot while a selection is still pending.
            if (player.selectedItemState.HasBufferedChange || player.selectedItemState.HasActiveOverride)
                return;

            if (player.selectedItem != _pendingUseSlot)
            {
                // An unrelated selection takes precedence over the mod's temporary selection.
                CancelPendingUse(player);
                return;
            }

            // Held input can start the recall during Terraria's own Player.Update.
            if (player.itemAnimation > 0)
            {
                CancelPendingUse(player);
                return;
            }

            if (!player.selectedItemState.CanChangeSelectedItemImmediately)
                return;

            // Even a rejected ItemCheck must return control to the original item.
            try
            {
                TriggerItemUse(player);
            }
            finally
            {
                CancelPendingUse(player);
            }
        }

        private void CancelPendingUse(Player player)
        {
            if (_hasRestoreCandidate &&
                player != null &&
                player.active &&
                !player.dead &&
                player.whoAmI == _restoreCandidatePlayerWhoAmI)
            {
                _restorePending = true;
                _restoreSelectedSlot = _restoreCandidateSelectedSlot;
                _restorePlayerWhoAmI = _restoreCandidatePlayerWhoAmI;
                _restoreRecallSlot = _pendingUseSlot;
                _restoreHotbar = _restoreCandidateHotbar;
                _restoreExpectedHotbar = _pendingUseExpectedHotbar;
                _restoreRequested = false;
                _restoreFramesLeft = MaxRestoreFrames;
            }

            ClearPendingUseState();
            ClearRestoreCandidateState();
        }

        private bool TrySelectItem(Player player, int slotIndex)
        {
            bool wasSelectingItem = _isSelectingItem;
            _isSelectingItem = true;
            try
            {
                player.selectedItemState.Select(slotIndex);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("[Quick Recall] TrySelectItem error: " + ex);
                return false;
            }
            finally
            {
                _isSelectingItem = wasSelectingItem;
            }
        }

        private void TriggerItemUse(Player player)
        {
            bool previousControlUseItem = player.controlUseItem;
            bool previousReleaseUseItem = player.releaseUseItem;
            int previousAltFunctionUse = player.altFunctionUse;

            try
            {
                player.altFunctionUse = 0;
                player.releaseUseItem = true;
                player.controlUseItem = true;
                player.ItemCheck();
            }
            finally
            {
                // Synthetic input is only owned for this one ItemCheck invocation.
                player.controlUseItem = previousControlUseItem;
                player.releaseUseItem = previousReleaseUseItem;
                player.altFunctionUse = previousAltFunctionUse;
            }
        }

        private void ProcessPendingRestore(uint elapsedUpdates)
        {
            if (!_restorePending)
                return;

            if (_restorePlayerWhoAmI < 0 || _restorePlayerWhoAmI >= Main.maxPlayers)
            {
                ClearRestoreState();
                return;
            }

            Player player = Main.player[_restorePlayerWhoAmI];
            if (player == null ||
                !player.active ||
                player.dead ||
                player != Main.LocalPlayer ||
                player.inventory == null ||
                _restoreSelectedSlot < 0 ||
                _restoreSelectedSlot >= player.inventory.Length)
            {
                ClearRestoreState();
                return;
            }

            _restoreFramesLeft -= (int)Math.Min(elapsedUpdates, (uint)_restoreFramesLeft);
            if (_restoreFramesLeft <= 0)
            {
                _log.Debug("[Quick Recall] Selection restore timed out.");
                ClearRestoreState();
                return;
            }

            int selectedSlot = player.selectedItem;
            bool hasBufferedChange = player.selectedItemState.HasBufferedChange;
            bool hasOverride = player.selectedItemState.HasActiveOverride;

            int expectedSelection = _restoreRequested ? _restoreSelectedSlot : _restoreRecallSlot;
            if (player.selectedItemState.Hotbar != _restoreExpectedHotbar ||
                (hasBufferedChange && player.selectedItemState.LastNonOverridenSelection != expectedSelection))
            {
                ClearRestoreState();
                return;
            }

            if (selectedSlot == _restoreSelectedSlot && !hasBufferedChange && !hasOverride)
            {
                ClearRestoreState();
                return;
            }

            // Smart Select can temporarily change the effective selection without a user choice.
            if (hasOverride)
                return;

            // A consumed item in a non-hotbar slot can make Terraria fall back to Hotbar.
            bool fellBackToHotbar = _restoreRecallSlot >= 10 &&
                                    _restoreRecallSlot < player.inventory.Length &&
                                    player.inventory[_restoreRecallSlot].IsAir &&
                                    selectedSlot == _restoreHotbar;

            // Otherwise an unrelated selection takes precedence over the mod's restoration.
            if (selectedSlot != _restoreRecallSlot && selectedSlot != _restoreSelectedSlot && !fellBackToHotbar)
            {
                ClearRestoreState();
                return;
            }

            // Wait for a buffered choice to settle. A manual choice can replace our request.
            if (hasBufferedChange)
                return;

            if (_restoreRequested)
            {
                // Our request did not take effect; retry only when the game allows it again.
                _restoreRequested = false;
                return;
            }

            if (!player.selectedItemState.CanChangeSelectedItemImmediately)
                return;

            // Select silently refuses an empty non-hotbar slot.
            if (_restoreSelectedSlot >= 10 && player.inventory[_restoreSelectedSlot].IsAir)
            {
                ClearRestoreState();
                return;
            }

            if (!TrySelectItem(player, _restoreSelectedSlot))
            {
                ClearRestoreState();
                return;
            }

            _restoreExpectedHotbar = player.selectedItemState.Hotbar;
            _restoreRequested = true;
        }

        private void ClearPendingUseState()
        {
            _usePending = false;
            _pendingUseSlot = -1;
            _pendingUsePlayerWhoAmI = -1;
            _pendingUseSelected = false;
            _pendingUseExpectedHotbar = -1;
            _pendingUseItemType = -1;
            _pendingUseFramesLeft = 0;
        }

        private void ClearRestoreState()
        {
            _restorePending = false;
            _restoreSelectedSlot = -1;
            _restorePlayerWhoAmI = -1;
            _restoreRecallSlot = -1;
            _restoreHotbar = -1;
            _restoreExpectedHotbar = -1;
            _restoreRequested = false;
            _restoreFramesLeft = 0;
        }

        private void ClearRestoreCandidateState()
        {
            _hasRestoreCandidate = false;
            _restoreCandidateHotbar = -1;
            _restoreCandidateSelectedSlot = -1;
            _restoreCandidatePlayerWhoAmI = -1;
        }
    }
}