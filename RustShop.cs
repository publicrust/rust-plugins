using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using UnityEngine;
using System;

namespace Oxide.Plugins
{
    [Info("RustShop", "ВашеИмя", "1.2.1")]
    [Description("Плагин для авторизации пользователя и отображения QR-кода через GUI.")]
    public class RustShop : RustPlugin
    {
        private const string AuthApiEndpoint = "http://localhost:5151/api/Auth/steam-login";
        private const string QrApiEndpoint = "http://localhost:5151/api/QrVerification/generate";
        private const string PermissionUse = "storeqrplugin.use";

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
            var container = new CuiElementContainer();
            string panelName = CuiHelper.GetGuid();

            container.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.1 0.8" },
                RectTransform = { AnchorMin = "0.3 0.3", AnchorMax = "0.7 0.7" },
                CursorEnabled = true
            }, "Overlay", panelName);

            container.Add(new CuiLabel
            {
                Text = { Text = $"Вы авторизованы как: {response.User.Username ?? "Неизвестно"}", FontSize = 18, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.05 0.7", AnchorMax = "0.95 0.8" }
            }, panelName);

            container.Add(new CuiLabel
            {
                Text = { Text = $"Баланс: {response.Balance?.Amount ?? 0} {response.Balance?.Currency ?? "Stars"}", FontSize = 18, Align = TextAnchor.MiddleLeft },
                RectTransform = { AnchorMin = "0.05 0.5", AnchorMax = "0.95 0.6" }
            }, panelName);

            container.Add(new CuiButton
            {
                Button = { Close = panelName, Color = "0.8 0 0 0.8" },
                RectTransform = { AnchorMin = "0.4 0.05", AnchorMax = "0.6 0.15" },
                Text = { Text = "Закрыть", FontSize = 20, Align = TextAnchor.MiddleCenter }
            }, panelName);

            CuiHelper.AddUi(player, container);
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
