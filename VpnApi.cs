/* SPDX-License-Identifier: GPL-2.0
 *
 * Copyright (C) 2021 Jason A. Donenfeld <Jason@zx2c4.com>. All Rights Reserved.
 */

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace AzireVPN
{
    struct VpnServer
    {
        public string Name { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string CountryISO { get; set; }
        public Uri RegistrationEndpoint { get; set; }

        public override string ToString()
        {
            return string.Format("{0} - {1}, {2}", Name, City, Country);
        }
    }
    class VpnApi
    {
        private static readonly string apiBase = "https://api.azirevpn.com/v1/";

        private static HttpClient httpClient = new HttpClient();

        public static async Task<List<VpnServer>> GetServers()
        {
            var servers = new List<VpnServer>();
            var json = await JsonDocument.ParseAsync(await httpClient.GetStreamAsync(apiBase + "locations"));
            var locations = json.RootElement.GetProperty("locations");
            for (var i = 0; i < locations.GetArrayLength(); ++i)
            {
                servers.Add(new VpnServer
                {
                    Name = locations[i].GetProperty("name").GetString(),
                    City = locations[i].GetProperty("city").GetString(),
                    Country = locations[i].GetProperty("country").GetString(),
                    CountryISO = locations[i].GetProperty("iso").GetString(),
                    RegistrationEndpoint = new Uri(locations[i].GetProperty("endpoints").GetProperty("wireguard").GetString())
                });
            }
            return servers;
        }

        public static async Task<string> Login(string username, string password)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
            });
            var response = await httpClient.PostAsync(apiBase + "token/generate", form);
            var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = json.RootElement;
            var message = root.GetProperty("message").GetString();
            if (root.GetProperty("status").GetString() != "success" || string.IsNullOrEmpty(message))
            {
                if (message != null)
                    throw new Exception(message);
                else
                    throw new Exception("An unknown API error has occurred. Please try again later.");
            }
            return message;
        }

        public static async void Logout(string token)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", token),
            });
            var response = await httpClient.PostAsync(apiBase + "token/delete", form);
            var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = json.RootElement;
            var message = root.GetProperty("message").GetString();
            if (root.GetProperty("status").GetString() != "success" || string.IsNullOrEmpty(message))
            {
                if (message != null)
                    throw new Exception(message);
                else
                    throw new Exception("An unknown API error has occurred. Please try again later.");
            }
        }

        public static async Task<string> Connect(VpnServer server, string token, Tunnel.Keypair keypair)
        {
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("token", token),
                new KeyValuePair<string, string>("pubkey", keypair.Public)
            });
            var response = await httpClient.PostAsync(server.RegistrationEndpoint, form);
            var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = json.RootElement;
            if (root.GetProperty("status").GetString() != "success")
            {
                var message = root.GetProperty("message").GetString();
                if (message != null)
                    throw new Exception(message);
                else
                    throw new Exception("An unknown API error has occurred. Please try again later.");
            }
            var data = root.GetProperty("data");
            return string.Format(
@"[Interface]
PrivateKey = {0}
Address = {1}
DNS = {2}

[Peer]
PublicKey = {3}
Endpoint = {4}
AllowedIPs = 0.0.0.0/0, ::/0
"
            , keypair.Private, data.GetProperty("Address").GetString(), data.GetProperty("DNS").GetString(), data.GetProperty("PublicKey").GetString(), data.GetProperty("Endpoint").GetString());
        }
    }
}
