using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CrazyGames
{
    public class StoreModule : MonoBehaviour
    {
        public const int MaxCatalogLimit = 50;

        private CrazySDK _crazySDK;
        private Action<SdkError, PurchaseResult> _buyItemCallback;
        private Action<SdkError, CatalogPage> _getCatalogItemsCallback;
        private Action<SdkError, InventoryPage> _getPlayerInventoryCallback;
        private Action<SdkError> _consumeItemCallback;
        private bool _sandbox;

        public void Init(CrazySDK crazySDK)
        {
            _crazySDK = crazySDK;
        }

        /// <summary>
        /// Toggles whether the purchases run against the sandbox (test) environment.
        /// </summary>
        public void SetSandbox(bool sandbox)
        {
            _sandbox = sandbox;
            _crazySDK.WrapSDKAction(
                () =>
                {
                    StoreSetSandboxSDK(sandbox ? 1 : 0);
                },
                () =>
                {
                    _crazySDK.DebugLog("Setting sandbox mode to " + sandbox);
                }
            );
        }

        /// <summary>
        /// Opens the payment widget, allowing the user to buy a single item.
        /// Only one purchase is allowed at a time, concurrent calls will receive an error.
        /// Callback param1 = error (null if everything is fine), param2 = purchase result.
        /// Possible error codes: "userNotAuthenticated", "purchaseInitFailed", "purchaseInProgress", "purchaseCancelled", "unexpectedError", "other".<br/><br/>
        /// You can customize the response returned in the editor in the CrazyGamesSettings file.
        /// </summary>
        public void BuyItem(string itemId, Action<SdkError, PurchaseResult> callback)
        {
            _crazySDK.WrapSDKAction(
                () =>
                {
                    if (_buyItemCallback != null)
                    {
                        callback(new SdkError("purchaseInProgress", "A purchase is already in progress."), null);
                        return;
                    }

                    _buyItemCallback = callback;
                    StoreBuyItemSDK(itemId);
                },
                () =>
                {
                    var settings = _crazySDK.Settings;
                    _crazySDK.DebugLog(
                        $"Buy item \"{itemId}\" simulation for Unity editor (sandbox: {_sandbox}), returning: {settings.buyItemResponse}"
                    );
                    switch (settings.buyItemResponse)
                    {
                        case CrazySettingsBuyItemResponse.Success:
                            callback(null, new PurchaseResult { itemId = itemId });
                            return;
                        case CrazySettingsBuyItemResponse.PurchaseCancelled:
                            callback(new SdkError("purchaseCancelled", "The purchase was cancelled."), null);
                            return;
                        case CrazySettingsBuyItemResponse.UserLoggedOut:
                            callback(new SdkError("userNotAuthenticated", "User must be logged in to perform this action."), null);
                            return;
                        case CrazySettingsBuyItemResponse.UnexpectedError:
                            callback(new SdkError("unexpectedError", "Simulated error in editor"), null);
                            return;
                        default:
                            Debug.LogError("Unhandled case " + settings.buyItemResponse + ", returning 'unexpectedError'");
                            callback(new SdkError("unexpectedError", "Simulated error in editor"), null);
                            return;
                    }
                }
            );
        }

        /// <summary>
        /// Opens the payment widget, allowing the user to buy a single item.
        /// Only one purchase is allowed at a time, concurrent calls will throw.
        /// Possible error codes: "userNotAuthenticated", "purchaseInitFailed", "purchaseInProgress", "purchaseCancelled", "unexpectedError", "other".<br/><br/>
        /// You can customize the response returned in the editor in the CrazyGamesSettings file.
        /// </summary>
        public System.Threading.Tasks.Task<PurchaseResult> BuyItemAsync(string itemId)
        {
            return CrazyTaskUtil.FromCallback<PurchaseResult>(cb => BuyItem(itemId, cb));
        }

        /// <summary>
        /// Lists the first page of the items of the game's catalog.
        /// Callback param1 = error (null if everything is fine), param2 = catalog page.
        /// </summary>
        public void GetCatalogItems(Action<SdkError, CatalogPage> callback)
        {
            GetCatalogItems(MaxCatalogLimit, 0, callback);
        }

        /// <summary>
        /// Lists one page of the items of the game's catalog.
        /// Only one catalog request is allowed at a time, concurrent calls will receive an error.
        /// Callback param1 = error (null if everything is fine), param2 = catalog page.
        /// Possible error codes: "catalogFetchFailed", "invalidArgument", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="limit">How many items to return, between 1 and 50</param>
        /// <param name="offset">How many items to skip, 0 for the first page</param>
        /// <param name="callback">Callback with error (null if ok) and CatalogPage result</param>
        public void GetCatalogItems(int limit, int offset, Action<SdkError, CatalogPage> callback)
        {
            _crazySDK.WrapSDKAction(
                () =>
                {
                    if (_getCatalogItemsCallback != null)
                    {
                        callback(new SdkError("requestInProgress", "A catalog request is already in progress."), null);
                        return;
                    }

                    if (!ValidatePageArguments(limit, offset, callback))
                    {
                        return;
                    }

                    _getCatalogItemsCallback = callback;
                    StoreGetCatalogItemsSDK(limit, offset);
                },
                () =>
                {
                    if (!ValidatePageArguments(limit, offset, callback))
                    {
                        return;
                    }

                    _crazySDK.DebugLog($"Get catalog items simulation for Unity editor, limit: {limit}, offset: {offset}");

                    var allItems = new List<CatalogItem>
                    {
                        new CatalogItem
                        {
                            sku = "sword",
                            name = "Sword",
                            description = "A demo sword",
                            imageUrl = "https://images.crazygames.com/userportal/avatars/1.png",
                            price = new CatalogItemPrice
                            {
                                amount = "4.99",
                                amountWithoutDiscount = "4.99",
                                currency = "USD",
                            },
                            isFree = false,
                            canBeBought = true,
                        },
                        new CatalogItem
                        {
                            sku = "gem_pack_100",
                            name = "100 gems",
                            description = "A demo discounted gem pack",
                            imageUrl = "https://images.crazygames.com/userportal/avatars/2.png",
                            price = new CatalogItemPrice
                            {
                                amount = "9.99",
                                amountWithoutDiscount = "12.99",
                                currency = "USD",
                            },
                            isFree = false,
                            canBeBought = true,
                        },
                        new CatalogItem
                        {
                            sku = "starter_pack",
                            name = "Starter pack",
                            description = "A demo item without a real money price",
                            imageUrl = null,
                            price = null,
                            isFree = true,
                            canBeBought = false,
                        },
                    };

                    var start = Math.Min(Math.Max(0, offset), allItems.Count);
                    var count = Math.Min(limit, allItems.Count - start);
                    callback(null, new CatalogPage { items = allItems.GetRange(start, count), hasMore = start + count < allItems.Count });
                }
            );
        }

        /// <summary>
        /// Lists the first page of the items of the game's catalog.
        /// </summary>
        public System.Threading.Tasks.Task<CatalogPage> GetCatalogItemsAsync()
        {
            return GetCatalogItemsAsync(MaxCatalogLimit, 0);
        }

        /// <summary>
        /// Lists one page of the items of the game's catalog.
        /// Only one catalog request is allowed at a time, concurrent calls will throw.
        /// Possible error codes: "catalogFetchFailed", "invalidArgument", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="limit">How many items to return, between 1 and 50</param>
        /// <param name="offset">How many items to skip, 0 for the first page</param>
        public System.Threading.Tasks.Task<CatalogPage> GetCatalogItemsAsync(int limit, int offset)
        {
            return CrazyTaskUtil.FromCallback<CatalogPage>(cb => GetCatalogItems(limit, offset, cb));
        }

        private bool ValidatePageArguments<T>(int limit, int offset, Action<SdkError, T> callback)
            where T : class
        {
            if (limit < 1 || limit > MaxCatalogLimit)
            {
                callback(new SdkError("invalidArgument", $"The limit must be a whole number between 1 and {MaxCatalogLimit}."), null);
                return false;
            }

            if (offset < 0)
            {
                callback(new SdkError("invalidArgument", "The offset must be a whole number of 0 or more."), null);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Lists the first page of the items the signed in player owns.
        /// Callback param1 = error (null if everything is fine), param2 = inventory page.
        /// </summary>
        public void GetPlayerInventory(Action<SdkError, InventoryPage> callback)
        {
            GetPlayerInventory(MaxCatalogLimit, 0, callback);
        }

        /// <summary>
        /// Lists one page of the items the signed in player owns.
        /// Only one inventory request is allowed at a time, concurrent calls will receive an error.
        /// Callback param1 = error (null if everything is fine), param2 = inventory page.
        /// Possible error codes: "userNotAuthenticated", "inventoryFetchFailed", "invalidArgument", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="limit">How many items to return, between 1 and 50</param>
        /// <param name="offset">How many items to skip, 0 for the first page</param>
        /// <param name="callback">Callback with error (null if ok) and InventoryPage result</param>
        public void GetPlayerInventory(int limit, int offset, Action<SdkError, InventoryPage> callback)
        {
            _crazySDK.WrapSDKAction(
                () =>
                {
                    if (_getPlayerInventoryCallback != null)
                    {
                        callback(new SdkError("requestInProgress", "An inventory request is already in progress."), null);
                        return;
                    }

                    if (!ValidatePageArguments(limit, offset, callback))
                    {
                        return;
                    }

                    _getPlayerInventoryCallback = callback;
                    StoreGetPlayerInventorySDK(limit, offset);
                },
                () =>
                {
                    if (!ValidatePageArguments(limit, offset, callback))
                    {
                        return;
                    }

                    _crazySDK.DebugLog($"Get player inventory simulation for Unity editor, limit: {limit}, offset: {offset}");

                    var allItems = new List<InventoryItem>
                    {
                        new InventoryItem
                        {
                            sku = "sword",
                            name = "Sword",
                            description = "A demo sword",
                            imageUrl = "https://images.crazygames.com/userportal/avatars/1.png",
                            quantity = 1,
                        },
                        new InventoryItem
                        {
                            sku = "gems",
                            name = "Gems",
                            description = "Gems",
                            imageUrl = "https://images.crazygames.com/userportal/avatars/2.png",
                            quantity = 3,
                        },
                    };

                    var start = Math.Min(Math.Max(0, offset), allItems.Count);
                    var count = Math.Min(limit, allItems.Count - start);
                    callback(null, new InventoryPage { items = allItems.GetRange(start, count) });
                }
            );
        }

        /// <summary>
        /// Lists the first page of the items the signed in player owns.
        /// </summary>
        public System.Threading.Tasks.Task<InventoryPage> GetPlayerInventoryAsync()
        {
            return GetPlayerInventoryAsync(MaxCatalogLimit, 0);
        }

        /// <summary>
        /// Lists one page of the items the signed in player owns.
        /// Only one inventory request is allowed at a time, concurrent calls will throw.
        /// Possible error codes: "userNotAuthenticated", "inventoryFetchFailed", "invalidArgument", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="limit">How many items to return, between 1 and 50</param>
        /// <param name="offset">How many items to skip, 0 for the first page</param>
        public System.Threading.Tasks.Task<InventoryPage> GetPlayerInventoryAsync(int limit, int offset)
        {
            return CrazyTaskUtil.FromCallback<InventoryPage>(cb => GetPlayerInventory(limit, offset, cb));
        }

        /// <summary>
        /// Removes a quantity of an item from the signed in player's inventory, for example when the player uses a potion.
        /// Only one consume request is allowed at a time, concurrent calls will receive an error.
        /// Callback param = error (null if everything is fine).
        /// Possible error codes: "userNotAuthenticated", "invalidArgument", "itemNotConsumable", "itemNotInInventory",
        /// "insufficientItemQuantity", "consumeItemFailed", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="sku">The SKU of the item</param>
        /// <param name="quantity">How many units to consume, 1 or more</param>
        /// <param name="callback">Callback with error (null if ok)</param>
        public void ConsumeItem(string sku, int quantity, Action<SdkError> callback)
        {
            _crazySDK.WrapSDKAction(
                () =>
                {
                    if (_consumeItemCallback != null)
                    {
                        callback(new SdkError("requestInProgress", "A consume request is already in progress."));
                        return;
                    }

                    if (!ValidateConsumeArguments(sku, quantity, callback))
                    {
                        return;
                    }

                    _consumeItemCallback = callback;
                    StoreConsumeItemSDK(sku, quantity);
                },
                () =>
                {
                    if (!ValidateConsumeArguments(sku, quantity, callback))
                    {
                        return;
                    }

                    _crazySDK.DebugLog($"Consume item \"{sku}\" simulation for Unity editor, quantity: {quantity}");
                    callback(null);
                }
            );
        }

        /// <summary>
        /// Removes a quantity of an item from the signed in player's inventory, for example when the player uses a potion.
        /// Only one consume request is allowed at a time, concurrent calls will throw.
        /// Possible error codes: "userNotAuthenticated", "invalidArgument", "itemNotConsumable", "itemNotInInventory",
        /// "insufficientItemQuantity", "consumeItemFailed", "requestInProgress", "unexpectedError", "other".
        /// </summary>
        /// <param name="sku">The SKU of the item</param>
        /// <param name="quantity">How many units to consume, 1 or more</param>
        public System.Threading.Tasks.Task ConsumeItemAsync(string sku, int quantity)
        {
            return CrazyTaskUtil.FromCallbackWithoutResult(cb => ConsumeItem(sku, quantity, cb));
        }

        private bool ValidateConsumeArguments(string sku, int quantity, Action<SdkError> callback)
        {
            if (string.IsNullOrEmpty(sku))
            {
                callback(new SdkError("invalidArgument", "sku must be a non-empty string."));
                return false;
            }

            if (quantity < 1)
            {
                callback(new SdkError("invalidArgument", "quantity must be a positive integer."));
                return false;
            }

            return true;
        }

        private void JSLibCallback_StoreBuyItem(string responseStr)
        {
            var response = JsonUtility.FromJson<PurchaseCallbackReply>(responseStr);
            var callback = _buyItemCallback;
            // clear the callback before invoking it, the game may start a new purchase from the callback
            _buyItemCallback = null;
            callback?.Invoke(response.Error, response.PurchaseResult);
        }

        private void JSLibCallback_StoreGetCatalogItems(string responseStr)
        {
            var response = JsonUtility.FromJson<CatalogCallbackReply>(responseStr);
            var callback = _getCatalogItemsCallback;
            // clear the callback before invoking it, the game may request another page from the callback
            _getCatalogItemsCallback = null;
            callback?.Invoke(response.Error, response.CatalogPage);
        }

        private void JSLibCallback_StoreGetPlayerInventory(string responseStr)
        {
            var response = JsonUtility.FromJson<InventoryCallbackReply>(responseStr);
            var callback = _getPlayerInventoryCallback;
            // clear the callback before invoking it, the game may request another page from the callback
            _getPlayerInventoryCallback = null;
            callback?.Invoke(response.Error, response.InventoryPage);
        }

        private void JSLibCallback_StoreConsumeItem(string responseStr)
        {
            var response = JsonUtility.FromJson<ConsumeCallbackReply>(responseStr);
            var callback = _consumeItemCallback;
            // clear the callback before invoking it, the game may consume another item from the callback
            _consumeItemCallback = null;
            callback?.Invoke(response.Error);
        }

#if UNITY_WEBGL
        [DllImport("__Internal")]
        private static extern void StoreBuyItemSDK(string itemId);

        [DllImport("__Internal")]
        private static extern void StoreSetSandboxSDK(int sandbox);

        [DllImport("__Internal")]
        private static extern void StoreGetCatalogItemsSDK(int limit, int offset);

        [DllImport("__Internal")]
        private static extern void StoreGetPlayerInventorySDK(int limit, int offset);

        [DllImport("__Internal")]
        private static extern void StoreConsumeItemSDK(string sku, int quantity);
#else
        // Preventing build to fail when using another platform than WebGL
        private void StoreBuyItemSDK(string itemId) { }

        private void StoreSetSandboxSDK(int sandbox) { }

        private void StoreGetCatalogItemsSDK(int limit, int offset) { }

        private void StoreGetPlayerInventorySDK(int limit, int offset) { }

        private void StoreConsumeItemSDK(string sku, int quantity) { }
#endif

        [Serializable]
        private class PurchaseCallbackReply
        {
            public string errorJson; // JsonUtility initializes null class with empty fields, so pass them as JSON strings and parse them here
            public string purchaseResultJson;

            public SdkError Error =>
                string.IsNullOrEmpty(errorJson) || errorJson == "null" || errorJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<SdkError>(errorJson);

            public PurchaseResult PurchaseResult =>
                string.IsNullOrEmpty(purchaseResultJson) || purchaseResultJson == "null" || purchaseResultJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<PurchaseResult>(purchaseResultJson);
        }

        [Serializable]
        private class CatalogCallbackReply
        {
            public string errorJson; // JsonUtility initializes null class with empty fields, so pass them as JSON strings and parse them here
            public string catalogPageJson;

            public SdkError Error =>
                string.IsNullOrEmpty(errorJson) || errorJson == "null" || errorJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<SdkError>(errorJson);

            public CatalogPage CatalogPage =>
                string.IsNullOrEmpty(catalogPageJson) || catalogPageJson == "null" || catalogPageJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<CatalogPage>(catalogPageJson);
        }

        [Serializable]
        private class InventoryCallbackReply
        {
            public string errorJson; // JsonUtility initializes null class with empty fields, so pass them as JSON strings and parse them here
            public string inventoryPageJson;

            public SdkError Error =>
                string.IsNullOrEmpty(errorJson) || errorJson == "null" || errorJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<SdkError>(errorJson);

            public InventoryPage InventoryPage =>
                string.IsNullOrEmpty(inventoryPageJson) || inventoryPageJson == "null" || inventoryPageJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<InventoryPage>(inventoryPageJson);
        }

        [Serializable]
        private class ConsumeCallbackReply
        {
            public string errorJson; // JsonUtility initializes null class with empty fields, so pass it as a JSON string and parse it here

            public SdkError Error =>
                string.IsNullOrEmpty(errorJson) || errorJson == "null" || errorJson == "undefined"
                    ? null
                    : JsonUtility.FromJson<SdkError>(errorJson);
        }
    }

    [Serializable]
    public class PurchaseResult
    {
        public string itemId;

        public override string ToString()
        {
            return base.ToString() + "ItemId = " + itemId;
        }
    }

    [Serializable]
    public class CatalogItemPrice
    {
        public string amount;

        public string amountWithoutDiscount;

        public string currency;

        public override string ToString()
        {
            return base.ToString() + $"Amount = {amount}, AmountWithoutDiscount = {amountWithoutDiscount}, Currency = {currency}";
        }
    }

    [Serializable]
    public class CatalogItem
    {
        public string sku;
        public string name;
        public string description;
        public string imageUrl;

        public CatalogItemPrice price;

        public bool isFree;

        public bool canBeBought;

        public bool HasPrice => price != null && !string.IsNullOrEmpty(price.currency);

        public override string ToString()
        {
            return base.ToString()
                + $"Sku = {sku}, Name = {name}, Description = {description}, ImageUrl = {imageUrl}, "
                + $"Price = {(HasPrice ? price.ToString() : "none")}, IsFree = {isFree}, CanBeBought = {canBeBought}";
        }
    }

    [Serializable]
    public class CatalogPage
    {
        public List<CatalogItem> items;

        public bool hasMore;
    }

    [Serializable]
    public class InventoryItem
    {
        public string sku;
        public string name;
        public string description;
        public string imageUrl;
        public int quantity;

        public override string ToString()
        {
            return base.ToString()
                + $"Sku = {sku}, Name = {name}, Description = {description}, ImageUrl = {imageUrl}, Quantity = {quantity}";
        }
    }

    [Serializable]
    public class InventoryPage
    {
        public List<InventoryItem> items;
    }
}
