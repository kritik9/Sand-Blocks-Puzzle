using UnityEngine;
using UnityEngine.UI;

namespace CrazyGames
{
    public class StoreModuleDemo : MonoBehaviour
    {
        public InputField skuInput;
        public Toggle sandboxToggle;
        public Button buyButton;
        public Button getInventoryButton;
        public Button getCatalogButton;
        public InputField consumeSkuInput;
        public InputField consumeQuantityInput;
        public Button consumeButton;

        private void Start()
        {
            sandboxToggle.onValueChanged.AddListener(ToggleSandbox);
            buyButton.onClick.AddListener(BuyItem);
            getInventoryButton.onClick.AddListener(GetInventory);
            getCatalogButton.onClick.AddListener(GetCatalog);
            consumeButton.onClick.AddListener(ConsumeItem);

            CrazySDK.Init(() =>
            {
                CrazySDK.Store.SetSandbox(sandboxToggle.isOn);
            });
        }

        private void ToggleSandbox(bool sandbox)
        {
            if (!CrazySDK.IsInitialized)
            {
                return;
            }

            Debug.Log("Use sandbox payments: " + sandbox);
            CrazySDK.Store.SetSandbox(sandbox);
        }

        private async void BuyItem()
        {
            var sku = skuInput.text.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                Debug.Log("No SKU provided");
                return;
            }

            if (MainDemoScene.UseAsyncMethods)
            {
                try
                {
                    var result = await CrazySDK.Store.BuyItemAsync(sku);
                    Debug.Log("Purchase successful (async): " + result);
                }
                catch (SdkError e)
                {
                    Debug.LogError("Purchase failed (async): " + e);
                }
            }
            else
            {
                CrazySDK.Store.BuyItem(
                    sku,
                    (error, result) =>
                    {
                        if (error != null)
                        {
                            Debug.LogError("Purchase failed: " + error);
                            return;
                        }

                        Debug.Log("Purchase successful: " + result);
                    }
                );
            }
        }

        private async void ConsumeItem()
        {
            var sku = consumeSkuInput.text.Trim();
            if (string.IsNullOrEmpty(sku))
            {
                Debug.Log("No SKU provided");
                return;
            }

            // an empty quantity consumes a single unit, 0 or negative values are passed on so the SDK validation can be tested
            var quantityText = consumeQuantityInput.text.Trim();
            var quantity = 1;
            if (!string.IsNullOrEmpty(quantityText) && !int.TryParse(quantityText, out quantity))
            {
                Debug.Log("The quantity must be a whole number");
                return;
            }

            if (MainDemoScene.UseAsyncMethods)
            {
                try
                {
                    await CrazySDK.Store.ConsumeItemAsync(sku, quantity);
                    Debug.Log($"Consumed {quantity} of {sku} (async)");
                }
                catch (SdkError e)
                {
                    Debug.LogError("Consume item failed (async): " + e);
                }
            }
            else
            {
                CrazySDK.Store.ConsumeItem(
                    sku,
                    quantity,
                    (error) =>
                    {
                        if (error != null)
                        {
                            Debug.LogError("Consume item failed: " + error);
                            return;
                        }

                        Debug.Log($"Consumed {quantity} of {sku}");
                    }
                );
            }
        }

        private async void GetCatalog()
        {
            if (MainDemoScene.UseAsyncMethods)
            {
                try
                {
                    var catalog = await CrazySDK.Store.GetCatalogItemsAsync();
                    LogCatalog(catalog);
                }
                catch (SdkError e)
                {
                    Debug.LogError("Get catalog items failed (async): " + e);
                }
            }
            else
            {
                CrazySDK.Store.GetCatalogItems(
                    (error, catalog) =>
                    {
                        if (error != null)
                        {
                            Debug.LogError("Get catalog items failed: " + error);
                            return;
                        }

                        LogCatalog(catalog);
                    }
                );
            }
        }

        private void LogCatalog(CatalogPage catalog)
        {
            if (catalog == null || catalog.items == null || catalog.items.Count == 0)
            {
                Debug.Log("The catalog is empty");
                return;
            }

            Debug.Log($"Catalog items: {catalog.items.Count}, has more: {catalog.hasMore}");
            foreach (var item in catalog.items)
            {
                var price = item.HasPrice ? $"{item.price.amount} {item.price.currency}" : "no real money price";
                Debug.Log($"Catalog item: {item.name} ({item.sku}), {price}, can be bought: {item.canBeBought}");
            }
        }

        private async void GetInventory()
        {
            if (MainDemoScene.UseAsyncMethods)
            {
                try
                {
                    var inventory = await CrazySDK.Store.GetPlayerInventoryAsync();
                    LogInventory(inventory);
                }
                catch (SdkError e)
                {
                    Debug.LogError("Get player inventory failed (async): " + e);
                }
            }
            else
            {
                CrazySDK.Store.GetPlayerInventory(
                    (error, inventory) =>
                    {
                        if (error != null)
                        {
                            Debug.LogError("Get player inventory failed: " + error);
                            return;
                        }

                        LogInventory(inventory);
                    }
                );
            }
        }

        private void LogInventory(InventoryPage inventory)
        {
            if (inventory == null || inventory.items == null || inventory.items.Count == 0)
            {
                Debug.Log("Inventory is empty");
                return;
            }

            foreach (var item in inventory.items)
            {
                Debug.Log($"Inventory item: {item.name} x{item.quantity} ({item.sku})");
            }
        }
    }
}
