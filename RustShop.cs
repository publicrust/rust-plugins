using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("RustShop", "ВашеИмя", "1.2.1")]
    [Description("Плагин для авторизации пользователя и отображения QR-кода через GUI.")]
    public class RustShop : RustPlugin
    {
        private const string AuthApiEndpoint = "http://localhost:5151/api/Auth/steam-login";
        private const string QrApiEndpoint = "http://localhost:5151/api/QrVerification/generate";
        private const string ProductsApiEndpoint = "http://localhost:5151/api/Product";
        private const string CartApiEndpoint = "http://localhost:5151/api/Purchase/cart";
        private const string CreatePurchaseEndpoint = "http://localhost:5151/api/Purchase";
        private const string PermissionUse = "storeqrplugin.use";

        private Dictionary<string, string> playerTokens = new Dictionary<string, string>();

        private void Init()
        {
            permission.RegisterPermission(PermissionUse, this);
            Puts("StoreQRPlugin успешно инициализирован.");
        }

        [ChatCommand("store")]
        private void StoreCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("У вас нет разрешения на использование этой команды.");
                Puts($"Игрок {player.displayName} попытался использовать команду без разрешения.");
                return;
            }

            Puts($"Игрок {player.displayName} использует команду /store.");
            AuthenticatePlayer(player);
        }

        private void AuthenticatePlayer(BasePlayer player)
        {
            Puts($"Проверка авторизации для игрока {player.displayName}...");

            string steamId = player.UserIDString;

            var request = new UnityEngine.Networking.UnityWebRequest(AuthApiEndpoint, "POST")
            {
                uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { SteamId = steamId }))),
                downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    // Пользователь авторизован
                    var response = JsonConvert.DeserializeObject<AuthResponse>(request.downloadHandler.text);
                    Puts($"Игрок {player.displayName} авторизован. Отображение информации об аккаунте.");
                    DisplayAccountInfo(player, response);
                }
                else if (request.responseCode == 401) // Unauthorized
                {
                    Puts($"Игрок {player.displayName} не авторизован. Генерация QR-кода...");
                    GenerateQRCode(player);
                }
                else
                {
                    player.ChatMessage("Произошла ошибка при проверке авторизации. Попробуйте позже.");
                    Puts($"Ошибка авторизации: {request.error}");
                }
            };
        }

        private void GenerateQRCode(BasePlayer player)
        {
            Puts($"Начата генерация QR-кода для игрока {player.displayName}...");

            string steamId = player.UserIDString;

            var request = new UnityEngine.Networking.UnityWebRequest(QrApiEndpoint, "POST")
            {
                uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { SteamId = steamId }))),
                downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage("Не удалось получить QR-код. Попробуйте позже.");
                    Puts($"Ошибка получения QR-кода: {request.error}");
                    return;
                }

                var response = JsonConvert.DeserializeObject<QrResponse>(request.downloadHandler.text);
                if (string.IsNullOrEmpty(response.QrCodeUrl))
                {
                    player.ChatMessage("Не удалось получить ссылку на QR-код. Попробуйте позже.");
                    Puts("Полученный ответ не содержит ссылки на QR-код.");
                    return;
                }

                Puts($"QR-код успешно получен для игрока {player.displayName}. Отображение...");
                DisplayQRCode(player, response.QrCodeUrl);
            };
        }

        private void DisplayQRCode(BasePlayer player, string qrCodeUrl)
        {
            var container = new CuiElementContainer();
            string panelName = CuiHelper.GetGuid();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                CursorEnabled = true
            }, "Overlay", panelName);

            container.Add(new CuiElement
            {
                Parent = panelName,
                Components =
                {
                    new CuiRawImageComponent { Url = qrCodeUrl },
                    new CuiRectTransformComponent { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" }
                }
            });

            container.Add(new CuiButton
            {
                Button = { Close = panelName, Color = "0.8 0 0 0.8" },
                RectTransform = { AnchorMin = "0.4 0.05", AnchorMax = "0.6 0.15" },
                Text = { Text = "Закрыть", FontSize = 20, Align = TextAnchor.MiddleCenter }
            }, panelName);

            CuiHelper.AddUi(player, container);
        }

        private void DisplayAccountInfo(BasePlayer player, AuthResponse response)
        {
            playerTokens[player.UserIDString] = response.Token;
            ShowCart(player, true); // true indicates this is the main view
        }

        private void ShowCart(BasePlayer player, bool isMainView = false)
        {
            if (!playerTokens.TryGetValue(player.UserIDString, out string token))
            {
                player.ChatMessage("Please authenticate first using /store command");
                return;
            }

            var request = new UnityEngine.Networking.UnityWebRequest(CartApiEndpoint, "GET");
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage($"Failed to fetch cart: {request.error}");
                    return;
                }

                var cartItems = JsonConvert.DeserializeObject<List<PurchaseDto>>(request.downloadHandler.text);
                DisplayCart(player, cartItems, isMainView);
            };
        }

        private void DisplayCart(BasePlayer player, List<PurchaseDto> cartItems, bool isMainView)
        {
            var container = new CuiElementContainer();
            string panelName = CuiHelper.GetGuid();

            // Main panel
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.95" },
                RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.9" },
                CursorEnabled = true
            }, "Overlay", panelName);

            // Header panel
            string headerPanel = CuiHelper.GetGuid();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.15 1" },
                RectTransform = { AnchorMin = "0 0.9", AnchorMax = "1 1" }
            }, panelName, headerPanel);

            // Title
            container.Add(new CuiLabel
            {
                Text = { Text = "Shopping Cart", FontSize = 20, Align = TextAnchor.MiddleCenter },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, headerPanel);

            // Content panel
            string contentPanel = CuiHelper.GetGuid();
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0 0.1", AnchorMax = "1 0.9" }
            }, panelName, contentPanel);

            float currentY = 1f;
            float itemHeight = 0.1f;
            decimal total = 0;

            if (cartItems.Count == 0)
            {
                container.Add(new CuiLabel
                {
                    Text = { Text = "Your cart is empty", FontSize = 16, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0 0.4", AnchorMax = "1 0.6" }
                }, contentPanel);
            }
            else
            {
                foreach (var item in cartItems)
                {
                    string itemPanel = CuiHelper.GetGuid();
                    container.Add(new CuiPanel
                    {
                        Image = { Color = "0.2 0.2 0.2 0.95" },
                        RectTransform = { AnchorMin = $"0.05 {currentY - itemHeight}", AnchorMax = $"0.95 {currentY - 0.01f}" }
                    }, contentPanel, itemPanel);

                    // Item name
                    container.Add(new CuiLabel
                    {
                        Text = { Text = item.Product?.Name ?? "Unknown", FontSize = 14, Align = TextAnchor.MiddleLeft },
                        RectTransform = { AnchorMin = "0.05 0", AnchorMax = "0.6 1" }
                    }, itemPanel);

                    // Price
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"${item.Price}", FontSize = 14, Align = TextAnchor.MiddleRight },
                        RectTransform = { AnchorMin = "0.6 0", AnchorMax = "0.8 1" }
                    }, itemPanel);

                    // Remove button
                    container.Add(new CuiButton
                    {
                        Button = { Command = $"shop.cart.remove {item.Id}", Color = "0.8 0 0 0.8" },
                        RectTransform = { AnchorMin = "0.85 0.2", AnchorMax = "0.95 0.8" },
                        Text = { Text = "X", FontSize = 14, Align = TextAnchor.MiddleCenter }
                    }, itemPanel);

                    currentY -= itemHeight;
                    total += item.Price;
                }

                // Total
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.25 0.25 0.25 1" },
                    RectTransform = { AnchorMin = "0.05 0.02", AnchorMax = "0.95 0.1" }
                }, contentPanel);

                container.Add(new CuiLabel
                {
                    Text = { Text = $"Total: ${total}", FontSize = 16, Align = TextAnchor.MiddleRight },
                    RectTransform = { AnchorMin = "0.05 0.02", AnchorMax = "0.95 0.1" }
                }, contentPanel);
            }

            // Footer panel
            string footerPanel = CuiHelper.GetGuid();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.15 1" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.08" }
            }, panelName, footerPanel);

            // Shop button (only show in cart view)
            container.Add(new CuiButton
            {
                Button = { Command = "shop.view", Color = "0.2 0.6 0.2 0.95" },
                RectTransform = { AnchorMin = "0.7 0.2", AnchorMax = "0.95 0.8" },
                Text = { Text = "Open Shop", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, footerPanel);

            // Close button
            container.Add(new CuiButton
            {
                Button = { Close = panelName, Color = "0.8 0.2 0.2 0.95" },
                RectTransform = { AnchorMin = "0.05 0.2", AnchorMax = "0.3 0.8" },
                Text = { Text = "Close", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, footerPanel);

            CuiHelper.AddUi(player, container);
        }

        [ChatCommand("shop.view")]
        private void ViewShopCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("У вас нет разрешения на использование этой команды.");
                return;
            }

            if (!playerTokens.TryGetValue(player.UserIDString, out string token))
            {
                player.ChatMessage("Please authenticate first using /store command");
                return;
            }

            var request = new UnityEngine.Networking.UnityWebRequest(ProductsApiEndpoint, "GET");
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage($"Failed to fetch products: {request.error}");
                    return;
                }

                var products = JsonConvert.DeserializeObject<List<ProductDto>>(request.downloadHandler.text);
                DisplayStore(player, JsonConvert.DeserializeObject<AuthResponse>(request.downloadHandler.text), products);
            };
        }

        [ChatCommand("shop.buy")]
        private void BuyCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("У вас нет разрешения на использование этой команды.");
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage("Usage: /shop.buy <productId>");
                return;
            }

            if (!playerTokens.TryGetValue(player.UserIDString, out string token))
            {
                player.ChatMessage("Please authenticate first using /store command");
                return;
            }

            var purchaseRequest = new CreatePurchaseRequest
            {
                ProductId = Guid.Parse(args[0])
            };

            var jsonData = JsonConvert.SerializeObject(purchaseRequest);
            var request = new UnityEngine.Networking.UnityWebRequest(CreatePurchaseEndpoint, "POST");
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage($"Failed to add item to cart: {request.error}");
                    return;
                }

                player.ChatMessage("Item added to cart successfully!");
                ShowCart(player);
            };
        }

        [ChatCommand("shop.cart")]
        private void CartCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("У вас нет разрешения на использование этой команды.");
                return;
            }

            ShowCart(player);
        }

        [ChatCommand("shop.cart.remove")]
        private void RemoveFromCartCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("У вас нет разрешения на использование этой команды.");
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage("Usage: /shop.cart.remove <purchaseId>");
                return;
            }

            if (!playerTokens.TryGetValue(player.UserIDString, out string token))
            {
                player.ChatMessage("Please authenticate first using /store command");
                return;
            }

            var request = new UnityEngine.Networking.UnityWebRequest($"{CreatePurchaseEndpoint}/{args[0]}", "DELETE");
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage($"Failed to remove item from cart: {request.error}");
                    return;
                }

                player.ChatMessage("Item removed from cart successfully!");
                ShowCart(player);
            };
        }

        private void FetchAndDisplayProducts(BasePlayer player, AuthResponse response)
        {
            var request = new UnityEngine.Networking.UnityWebRequest(ProductsApiEndpoint, "GET");
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {response.Token}");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage($"Failed to fetch products: {request.error}");
                    return;
                }

                var products = JsonConvert.DeserializeObject<List<ProductDto>>(request.downloadHandler.text);
                DisplayStore(player, response, products);
            };
        }

        private void DisplayStore(BasePlayer player, AuthResponse response, List<ProductDto> products)
        {
            var container = new CuiElementContainer();
            string panelName = CuiHelper.GetGuid();

            // Main panel
            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.95" },
                RectTransform = { AnchorMin = "0.2 0.1", AnchorMax = "0.8 0.9" },
                CursorEnabled = true
            }, "Overlay", panelName);

            // Header with balance
            string headerPanel = CuiHelper.GetGuid();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.15 1" },
                RectTransform = { AnchorMin = "0 0.92", AnchorMax = "1 1" }
            }, panelName, headerPanel);

            container.Add(new CuiLabel
            {
                Text = { Text = $"Balance: {response.Balance?.Amount} {response.Balance?.Currency}", FontSize = 16, Align = TextAnchor.MiddleRight },
                RectTransform = { AnchorMin = "0.5 0", AnchorMax = "0.95 1" }
            }, headerPanel);

            // Grid container
            string gridPanel = CuiHelper.GetGuid();
            container.Add(new CuiPanel
            {
                RectTransform = { AnchorMin = "0.02 0.15", AnchorMax = "0.98 0.9" }
            }, panelName, gridPanel);

            // Create a 4x4 grid
            int columns = 4;
            int rows = 4;
            float itemSize = 0.23f; // Size of each item
            float spacing = 0.02f; // Space between items

            for (int i = 0; i < products.Count && i < 16; i++)
            {
                int row = i / columns;
                int col = i % columns;

                float minX = col * (itemSize + spacing);
                float maxX = minX + itemSize;
                float minY = 1f - ((row + 1) * (itemSize + spacing));
                float maxY = minY + itemSize;

                var product = products[i];

                // Item background
                string itemPanel = CuiHelper.GetGuid();
                container.Add(new CuiPanel
                {
                    Image = { Color = "0.2 0.2 0.2 0.95" },
                    RectTransform = { AnchorMin = $"{minX} {minY}", AnchorMax = $"{maxX} {maxY}" }
                }, gridPanel, itemPanel);

                // Clickable button over the entire item
                container.Add(new CuiButton
                {
                    Button = { Command = $"shop.select {product.Id}", Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    Text = { Text = "" }
                }, itemPanel);

                // Item image (if available)
                if (!string.IsNullOrEmpty(product.ImageUrl))
                {
                    container.Add(new CuiElement
                    {
                        Parent = itemPanel,
                        Components =
                        {
                            new CuiRawImageComponent { Url = product.ImageUrl },
                            new CuiRectTransformComponent { AnchorMin = "0.1 0.3", AnchorMax = "0.9 0.9" }
                        }
                    });
                }

                // Item name
                container.Add(new CuiLabel
                {
                    Text = { Text = product.Name, FontSize = 12, Align = TextAnchor.MiddleCenter },
                    RectTransform = { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.25" }
                }, itemPanel);

                // Amount (if applicable)
                if (product.Amount > 0)
                {
                    container.Add(new CuiLabel
                    {
                        Text = { Text = $"{product.Amount} шт.", FontSize = 10, Align = TextAnchor.UpperRight },
                        RectTransform = { AnchorMin = "0.6 0.8", AnchorMax = "0.9 0.95" }
                    }, itemPanel);
                }
            }

            // Navigation buttons
            string navPanel = CuiHelper.GetGuid();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.15 0.15 0.15 1" },
                RectTransform = { AnchorMin = "0.1 0.05", AnchorMax = "0.9 0.12" }
            }, panelName, navPanel);

            // Back button
            container.Add(new CuiButton
            {
                Button = { Command = "shop.cart", Color = "0.3 0.3 0.3 0.95" },
                RectTransform = { AnchorMin = "0.05 0.1", AnchorMax = "0.2 0.9" },
                Text = { Text = "Назад", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, navPanel);

            // Take all button
            container.Add(new CuiButton
            {
                Button = { Command = "shop.takeall", Color = "0.3 0.3 0.3 0.95" },
                RectTransform = { AnchorMin = "0.4 0.1", AnchorMax = "0.6 0.9" },
                Text = { Text = "Забрать всё", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, navPanel);

            // Close button
            container.Add(new CuiButton
            {
                Button = { Close = panelName, Color = "0.8 0.2 0.2 0.95" },
                RectTransform = { AnchorMin = "0.8 0.1", AnchorMax = "0.95 0.9" },
                Text = { Text = "X", FontSize = 12, Align = TextAnchor.MiddleCenter }
            }, navPanel);

            CuiHelper.AddUi(player, container);
        }

        [ChatCommand("shop.select")]
        private void SelectItemCommand(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                player.ChatMessage("У вас нет разрешения на использование этой команды.");
                return;
            }

            if (args.Length < 1)
            {
                player.ChatMessage("Usage: /shop.select <productId>");
                return;
            }

            if (!playerTokens.TryGetValue(player.UserIDString, out string token))
            {
                player.ChatMessage("Please authenticate first using /store command");
                return;
            }

            // Add to cart
            var purchaseRequest = new CreatePurchaseRequest
            {
                ProductId = Guid.Parse(args[0])
            };

            var jsonData = JsonConvert.SerializeObject(purchaseRequest);
            var request = new UnityEngine.Networking.UnityWebRequest(CreatePurchaseEndpoint, "POST");
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(jsonData));
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {token}");

            request.SendWebRequest().completed += operation =>
            {
                if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    player.ChatMessage($"Failed to select item: {request.error}");
                    return;
                }

                player.ChatMessage("Item selected successfully!");
                // Refresh the store view
                ViewShopCommand(player, command, args);
            };
        }

        private class ProductDto
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("description")]
            public string Description { get; set; }

            [JsonProperty("price")]
            public decimal Price { get; set; }

            [JsonProperty("imageUrl")]
            public string ImageUrl { get; set; }

            [JsonProperty("amount")]
            public int Amount { get; set; }
        }

        private class PurchaseDto
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }

            [JsonProperty("productId")]
            public Guid ProductId { get; set; }

            [JsonProperty("product")]
            public ProductDto Product { get; set; }

            [JsonProperty("price")]
            public decimal Price { get; set; }

            [JsonProperty("status")]
            public string Status { get; set; }
        }

        private class CreatePurchaseRequest
        {
            [JsonProperty("productId")]
            public Guid ProductId { get; set; }

            [JsonProperty("promoCode")]
            public string PromoCode { get; set; }
        }

        private class AuthResponse
        {
            [JsonProperty("token")]
            public string Token { get; set; }

            [JsonProperty("expiresIn")]
            public int ExpiresIn { get; set; }

            [JsonProperty("user")]
            public UserDto User { get; set; }

            [JsonProperty("balance")]
            public BalanceDto Balance { get; set; }
        }

        private class UserDto
        {
            [JsonProperty("id")]
            public Guid Id { get; set; }

            [JsonProperty("username")]
            public string Username { get; set; }
        }

        private class BalanceDto
        {
            [JsonProperty("amount")]
            public decimal Amount { get; set; }

            [JsonProperty("currency")]
            public string Currency { get; set; }

            [JsonProperty("lastUpdated")]
            public DateTime LastUpdated { get; set; }
        }

        private class QrResponse
        {
            [JsonProperty("QrCodeUrl")]
            public string QrCodeUrl { get; set; }
        }
    }
}
