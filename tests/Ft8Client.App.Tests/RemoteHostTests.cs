// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Ft8Client.Data.Database;
using Ft8Client.Data.Settings;
using Microsoft.Data.Sqlite;

namespace Ft8Client.App.Tests;

/// <summary>The remote page served by a whole app, as a browser sees it.</summary>
public class RemoteHostTests
{
    [Fact]
    public Task State_LinkedUserAnotherProfileInUse_CarriesTheProfileNote() => WithRemoteHost(async (host, http, port) =>
    {
        var token = await LoginAsync(http);
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), TestContext.Current.CancellationToken);
        await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { type = "hello", token }), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken);
        JsonElement state;
        do state = await ReceiveAsync(ws);
        while (state.GetProperty("type").GetString() != "state");

        state.GetProperty("state").GetProperty("myCall").GetString().Should().Be("K1UTA");
        state.GetProperty("state").GetProperty("profileNote").GetString()
            .Should().Be("You will operate as K1GLV (Home profile) when you take control, if the station is idle.");
    });

    // A whole app with two profiles (K1UTA in use), sign-in on, and the user "dan" linked to the Home profile (K1GLV).
    internal static async Task WithRemoteHost(Func<AppHost, HttpClient, int, Task> test)
    {
        var dir = Path.Combine(Path.GetTempPath(), "costas-remote-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var port = FreePort();
        var settings = new AppSettings { SetupComplete = true };
        settings.Profile.Callsign = "K1GLV";
        settings.Profile.Grid = "FN42";
        settings.Profiles.Add(new StationProfile { Id = "ash", Name = "K1UTA", Callsign = "K1UTA", Grid = "FN42" });
        settings.ActiveProfile = "ash";
        settings.Updates.CheckEnabled = false;
        settings.PskReporter.FeedEnabled = false;
        settings.Usage.Enabled = false;
        settings.Dxpeditions.Enabled = false;
        settings.Remote.Enabled = true;
        settings.Remote.Port = port;
        settings.Remote.RequireSignIn = true;
        settings.Remote.Users.Add(new RemoteUser { Id = "u1", Username = "dan", ProfileId = "home" });
        new SettingsStore(Path.Combine(dir, "settings.json")).Save(settings);
        SqliteDatabase? database = null;
        try
        {
            await using var host = await AppHost.StartAsync(TestHost.Args(dir));
            database = host.Database;
            host.Remote.Control.Users!.SetPassword("u1", "correct horse battery");
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };
            await test(host, http, port);
        }
        finally
        {
            if (database is not null)
            {
                using var connection = database.Open();
                SqliteConnection.ClearPool(connection);
            }
            AppearanceTests.DeleteWhenSettled(dir);
        }
    }

    // As nginx on this computer would pass a browser's https sign-in; the server may still be starting.
    internal static async Task<string> LoginAsync(HttpClient http)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Post, "/api/login") { Content = JsonContent.Create(new { username = "dan", password = "correct horse battery" }) };
                req.Headers.Add("X-Forwarded-For", "203.0.113.7");
                req.Headers.Add("X-Forwarded-Proto", "https");
                var r = await http.SendAsync(req, TestContext.Current.CancellationToken);
                r.StatusCode.Should().Be(HttpStatusCode.OK);
                return (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("token").GetString()!;
            }
            catch (HttpRequestException) when (attempt < 50)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }
    }

    internal static async Task<JsonElement> ReceiveAsync(ClientWebSocket ws)
    {
        var buf = new byte[256 * 1024];
        var count = 0;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var r = await ws.ReceiveAsync(new ArraySegment<byte>(buf, count, buf.Length - count), cts.Token);
            count += r.Count;
            if (r.EndOfMessage) return JsonDocument.Parse(Encoding.UTF8.GetString(buf, 0, count)).RootElement.Clone();
        }
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
