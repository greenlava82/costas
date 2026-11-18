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
    public Task State_LinkedUserAnotherProfileInUse_OffersOperateAsThenSwitchesOnlyWhenTapped() => WithRemoteHost(async (host, http, port) =>
    {
        var token = await LoginAsync(http);
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), TestContext.Current.CancellationToken);
        await SendAsync(ws, new { type = "hello", token });
        var state = (await NextAsync(ws, "state")).GetProperty("state");

        state.GetProperty("myCall").GetString().Should().Be("K1UTA");
        var profile = state.GetProperty("profile");
        profile.GetProperty("inUse").GetString().Should().Be("K1UTA (K1UTA profile)");
        profile.GetProperty("mineCall").GetString().Should().Be("K1GLV");
        profile.GetProperty("onMine").GetBoolean().Should().BeFalse();

        // Calling CQ does not switch: it is refused until the user taps Operate as.
        await SendAsync(ws, new { type = "cq" });
        (await ReplyAsync(ws)).Should().Be("Not done: the station is on K1UTA (K1UTA profile). Tap Operate as K1GLV first.");
        host.Settings.Current.Profile.Callsign.Should().Be("K1UTA");
        host.Session.Snapshot.CallingCq.Should().BeFalse();

        await SendAsync(ws, new { type = "switchProfile" });
        (await ReplyAsync(ws)).Should().BeNull();
        host.Settings.Current.Profile.Callsign.Should().Be("K1GLV");
        host.Session.Snapshot.CallingCq.Should().BeFalse("switching sends nothing");
        host.Remote.Control.Notices.ForDesk(host.Clock.UtcNow).Should().Be("dan switched the station to K1GLV (Home profile).");
    });

    [Fact]
    public Task SwitchProfileForRemoteUser_LinkedProfileHasOwnLicence_TransmitGuardTakesIt() => WithRemoteHost((host, _, _) =>
    {
        host.Settings.Update(s =>
        {
            s.Profiles.Single(p => p.Id == "home").LicenceClass = "technician";
            s.Profiles.Single(p => p.Id == "ash").LicenceClass = "extra";
        });
        host.Frequencies.Licence.Should().Be(Ft8Client.Core.Bands.UsLicenceClass.AmateurExtra);

        host.SwitchProfileForRemoteUser("u1").Refusal.Should().BeNull();

        host.Settings.Current.Profile.Callsign.Should().Be("K1GLV");
        host.Frequencies.Licence.Should().Be(Ft8Client.Core.Bands.UsLicenceClass.Technician, "a linked user transmits within their own profile's class");
        return Task.CompletedTask;
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

    private static Task SendAsync(ClientWebSocket ws, object message) =>
        ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken);

    // The next message of a type, skipping the others (states keep coming).
    private static async Task<JsonElement> NextAsync(ClientWebSocket ws, string type)
    {
        for (var i = 0; i < 200; i++)
        {
            var m = await ReceiveAsync(ws);
            if (m.GetProperty("type").GetString() == type) return m;
        }
        throw new TimeoutException($"No {type} message");
    }

    // The station's answer to a command: the state sent back with its note (null when it was carried out).
    private static async Task<string?> ReplyAsync(ClientWebSocket ws)
    {
        for (var i = 0; i < 200; i++)
        {
            var m = await ReceiveAsync(ws);
            if (m.TryGetProperty("note", out var note)) return note.ValueKind == JsonValueKind.Null ? null : note.GetString();
        }
        throw new TimeoutException("No reply");
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
