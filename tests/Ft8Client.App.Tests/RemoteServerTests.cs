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
using Ft8Client.App.Engine;
using Ft8Client.App.Remote;
using Ft8Client.Core;
using Ft8Client.Core.Remote;
using Ft8Client.Core.Time;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.Tests;

public sealed class RemoteServerTests : IAsyncLifetime
{
    private readonly ManualClock _clock = new(new DateTime(2026, 10, 12, 20, 31, 0, DateTimeKind.Utc));
    private readonly FakeRemoteTarget _target = new()
    {
        Snapshot = new SessionSnapshot { MyCall = "W7LIT", Band = "20m", Mode = Mode.Ft8, DialHz = 14_074_000 },
    };
    private readonly HttpClient _http = new();
    private readonly RemoteOptions _options = new();
    private readonly InMemorySecretStore _secrets = new();
    private RemoteUsers _users = null!;
    private RemoteControl _control = null!;
    private RemoteServer _server = null!;
    private int _port;

    public async ValueTask InitializeAsync()
    {
        _users = new RemoteUsers(() => _options, _secrets, _clock);
        _control = new RemoteControl(_target, _clock, new DeviceTokens(), _ => { }, users: _users);
        _server = new RemoteServer(_control, id => RemoteStateBuilder.Build(_target.Snapshot, _clock.UtcNow, new RemoteContext { Units = DistanceUnit.Miles },
            _control.ControlFor(id), _control.ViewingFor(id)));
        _port = FreePort();
        await _server.StartAsync(_port);
        _http.BaseAddress = new Uri($"http://127.0.0.1:{_port}/");
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _server.DisposeAsync();
    }

    [Fact]
    public async Task Get_Root_ServesThePageWithSecurityHeaders()
    {
        var r = await _http.GetAsync("/", TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("Costas remote");
        r.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'none'");
    }

    [Fact]
    public async Task Get_Manifest_ServesTheHomeScreenAppAndItsIcons()
    {
        var page = await _http.GetAsync("/", TestContext.Current.CancellationToken);
        page.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("manifest-src 'self'").And.Contain("img-src 'self'");

        var m = await _http.GetAsync("/manifest.webmanifest", TestContext.Current.CancellationToken);
        m.StatusCode.Should().Be(HttpStatusCode.OK);
        m.Content.Headers.ContentType!.MediaType.Should().Be("application/manifest+json");
        var j = await m.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        j.GetProperty("display").GetString().Should().Be("standalone");

        foreach (var icon in j.GetProperty("icons").EnumerateArray().Select(i => i.GetProperty("src").GetString()).Append("/icon-180.png"))
        {
            var r = await _http.GetAsync(icon, TestContext.Current.CancellationToken);
            r.StatusCode.Should().Be(HttpStatusCode.OK, icon);
            r.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        }
    }

    [Theory]
    [InlineData("/favicon.svg", "image/svg+xml")]
    [InlineData("/favicon.ico", "image/x-icon")]
    public async Task Get_Favicon_LinkedAndServed(string path, string type)
    {
        var page = await (await _http.GetAsync("/", TestContext.Current.CancellationToken)).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        page.Should().Contain($"rel=\"icon\" href=\"{path}\"");

        var r = await _http.GetAsync(path, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        r.Content.Headers.ContentType!.MediaType.Should().Be(type);
        (await r.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Pair_NoCode_EachPhoneGetsItsOwnToken()
    {
        var a = await PairAsync();
        var b = await PairAsync();
        a.Should().NotBe(b);
        _control.SignIn(a).Should().NotBe(_control.SignIn(b));
    }

    [Fact]
    public async Task Socket_PairedPhone_GetsStateAndCommandsReachTheSession()
    {
        var token = await PairAsync();
        using var ws = await ConnectAsync();
        await SendAsync(ws, new { type = "hello", token });

        var first = await ReceiveAsync(ws);
        first.GetProperty("type").GetString().Should().Be("state");
        first.GetProperty("state").GetProperty("myCall").GetString().Should().Be("W7LIT");
        first.GetProperty("state").GetProperty("frequency").GetString().Should().Be("14.074 · 20 m FT8");
        first.GetProperty("state").GetProperty("control").GetString().Should().Be("desk");

        await SendAsync(ws, new { type = "call", call = "VE7XYZ" });
        var after = await ReceiveAsync(ws);
        after.GetProperty("state").GetProperty("control").GetString().Should().Be("you");
        _target.Calls.Should().Equal("call VE7XYZ");
    }

    [Fact]
    public async Task Socket_Show_SendsThatStationsDetailsWithoutTakingControl()
    {
        var token = await PairAsync();
        using var ws = await ConnectAsync();
        await SendAsync(ws, new { type = "hello", token });
        var first = await ReceiveAsync(ws);
        first.GetProperty("state").GetProperty("details").ValueKind.Should().Be(JsonValueKind.Null);

        await SendAsync(ws, new { type = "show", call = "ve7xyz" });
        var after = await ReceiveAsync(ws);
        after.GetProperty("state").GetProperty("details").GetProperty("call").GetString().Should().Be("VE7XYZ");
        after.GetProperty("state").GetProperty("control").GetString().Should().Be("desk");
        _target.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Socket_UnpairedToken_ClosedAndNothingReachesTheSession()
    {
        using var ws = await ConnectAsync();
        await SendAsync(ws, new { type = "hello", token = "guess" });
        var buf = new byte[256];
        var r = await ws.ReceiveAsync(buf, TestContext.Current.CancellationToken);
        r.MessageType.Should().Be(WebSocketMessageType.Close);
        ws.CloseStatusDescription.Should().Be("unpaired");
        _target.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Page_Script_Parses()
    {
        // A syntax error leaves phones on a blank page, and the server tests above never run the script.
        var node = FindNode();
        Assert.SkipWhen(node is null, "node is not installed.");
        using var s = typeof(RemoteServer).Assembly.GetManifestResourceStream("Ft8Client.App.Remote.index.html")!;
        var html = new StreamReader(s).ReadToEnd();
        var start = html.LastIndexOf("<script>", StringComparison.Ordinal) + "<script>".Length;
        var end = html.LastIndexOf("</script>", StringComparison.Ordinal);
        var file = Path.Combine(Path.GetTempPath(), $"costas-remote-{Guid.NewGuid():N}.js");
        File.WriteAllText(file, html[start..end]);
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(node!, ["--check", file])
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(30_000).Should().BeTrue();
            p.ExitCode.Should().Be(0, err);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string? FindNode()
    {
        var name = OperatingSystem.IsWindows() ? "node.exe" : "node";
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
    }

    [Fact]
    public async Task Socket_OtherSiteOrigin_Refused()
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "http://evil.example");
        var act = () => ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws"), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<WebSocketException>();
    }

    [Fact]
    public async Task Config_SignInOff_PairsWithoutCode()
    {
        (await GetJsonAsync("/api/config")).GetProperty("signIn").GetBoolean().Should().BeFalse();
        var r = await _http.PostAsJsonAsync("/api/login", new { username = "dan", password = "correct horse battery" }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest, "there is nothing to sign in to");
    }

    [Fact]
    public async Task Pair_SignInOn_RefusedAndAsksForSignIn()
    {
        AddUser("dan", "correct horse battery");
        (await GetJsonAsync("/api/config")).GetProperty("signIn").GetBoolean().Should().BeTrue();
        var r = await _http.PostAsJsonAsync("/api/pair", new { }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("signIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Login_RightPassword_SocketSignedInAsThatUser()
    {
        AddUser("Dan", "correct horse battery");
        AddUser("guest", "another long password");
        var token = await LoginAsync("dan", "correct horse battery");

        using var ws = await ConnectAsync();
        await SendAsync(ws, new { type = "hello", token });
        var user = await ReceiveAsync(ws);
        user.GetProperty("type").GetString().Should().Be("user");
        user.GetProperty("username").GetString().Should().Be("Dan");
        (await ReceiveAsync(ws)).GetProperty("state").GetProperty("myCall").GetString().Should().Be("W7LIT");
        _control.SignIn(token)!.Username.Should().Be("Dan");
    }

    [Fact]
    public async Task Login_SecondUser_FirstUserToldNotTheNewcomer()
    {
        AddUser("Dan", "correct horse battery");
        AddUser("Ash", "another long password");
        var dan = await LoginAsync("dan", "correct horse battery");
        var ash = await LoginAsync("ash", "another long password");

        _control.Notices.For(RemoteIdentity.DeviceIdFor(dan), _clock.UtcNow).Select(n => n.Text)
            .Should().Equal("Ash signed in on another device.");
        _control.Notices.For(RemoteIdentity.DeviceIdFor(ash), _clock.UtcNow).Select(n => n.Text)
            .Should().Equal("Dan signed in on another device.");
        _control.Notices.ForDesk(_clock.UtcNow).Should().Be("Ash signed in on another device.");

        await _control.HandleAsync(_control.SignIn(ash)!.DeviceId, new RemoteCommand("call", "ve7xyz"));
        _control.Notices.For(RemoteIdentity.DeviceIdFor(dan), _clock.UtcNow)[0].Text.Should().Be("Ash took control from the desk.");
    }

    [Fact]
    public async Task HandleAsync_LinkedUserStationIdle_SwitchesThenCarriesOutAndTellsOthers()
    {
        AddUser("Ash", "another long password");
        var token = await LoginAsync("ash", "another long password");
        var asked = new List<string>();
        var rc = new RemoteControl(_target, _clock, new DeviceTokens(), _ => { }, users: _users,
            switchProfile: id => { asked.Add(id); return (null, "AB1CD (Ash profile)"); });
        var device = rc.SignIn(token)!.DeviceId;

        (await rc.HandleAsync(device, new RemoteCommand("call", "ve7xyz"))).Should().BeNull();

        asked.Should().Equal(_options.Users[0].Id);
        _target.Calls.Should().Equal("call VE7XYZ");
        rc.Notices.For("desk-or-other", _clock.UtcNow).Select(n => n.Text)
            .Should().Equal("Now operating as AB1CD (Ash profile) for Ash.", "Ash took control from the desk.");
    }

    [Fact]
    public async Task HandleAsync_LinkedUserStationBusy_RefusedWithoutTakingControl()
    {
        AddUser("Ash", "another long password");
        var token = await LoginAsync("ash", "another long password");
        var rc = new RemoteControl(_target, _clock, new DeviceTokens(), _ => { }, users: _users,
            switchProfile: _ => ("Not done: the station is busy while calling CQ as K1ABC.", null));
        var device = rc.SignIn(token)!.DeviceId;

        (await rc.HandleAsync(device, new RemoteCommand("call", "ve7xyz"))).Should().StartWith("Not done: the station is busy");

        _target.Calls.Should().BeEmpty("nothing goes out under another operator's callsign");
        rc.Lease.IsHeld.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_LinkedUserHalts_StopsWithoutSwitching()
    {
        AddUser("Ash", "another long password");
        var token = await LoginAsync("ash", "another long password");
        var asked = 0;
        var rc = new RemoteControl(_target, _clock, new DeviceTokens(), _ => { }, users: _users,
            switchProfile: _ => { asked++; return ("busy", null); });

        await rc.HandleAsync(rc.SignIn(token)!.DeviceId, new RemoteCommand("halt"));

        asked.Should().Be(0, "Stop always works");
        _target.Halts.Should().HaveCount(1);
    }

    [Fact]
    public async Task HandleAsync_LinkedUserStopsCq_StopsWithoutSwitching()
    {
        AddUser("Ash", "another long password");
        var token = await LoginAsync("ash", "another long password");
        var asked = 0;
        var rc = new RemoteControl(_target, _clock, new DeviceTokens(), _ => { }, users: _users,
            switchProfile: _ => { asked++; return ("busy", null); });

        (await rc.HandleAsync(rc.SignIn(token)!.DeviceId, new RemoteCommand("cqStop"))).Should().BeNull();

        asked.Should().Be(0, "stopping a CQ only ends a transmission, so it works whoever's profile is in use");
        _target.Calls.Should().Equal("cqStop");
    }

    [Fact]
    public async Task Login_WrongPassword_RefusedThenTooManyTries()
    {
        AddUser("dan", "correct horse battery");
        for (var i = 0; i < LoginThrottle.FreeFailures; i++)
        {
            var r = await PostLoginAsync("dan", "wrong password!");
            r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        (await PostLoginAsync("nobody", "wrong password!")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an unknown name reads the same");
        var locked = await PostLoginAsync("dan", "correct horse battery");
        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "the address is held back even for the right password");
        locked.Headers.RetryAfter.Should().NotBeNull();
        _clock.Advance(LoginThrottle.MaxLockout);
        (await PostLoginAsync("dan", "correct horse battery")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Socket_SignInTurnedOn_PairedPhoneCutOff()
    {
        var token = await PairAsync();
        using var ws = await ConnectAsync();
        await SendAsync(ws, new { type = "hello", token });
        await ReceiveAsync(ws);

        AddUser("dan", "correct horse battery");
        await _server.BroadcastAsync();
        await ExpectClosedAsync(ws, "unpaired");
    }

    [Fact]
    public async Task Socket_UserDisabledOrSignedOut_CutOff()
    {
        AddUser("dan", "correct horse battery");
        var token = await LoginAsync("dan", "correct horse battery");
        using var ws = await ConnectAsync();
        await SendAsync(ws, new { type = "hello", token });
        await ReceiveAsync(ws);

        _options.Users[0].Enabled = false;
        await _server.BroadcastAsync();
        await ExpectClosedAsync(ws, "unpaired");
        _options.Users[0].Enabled = true;
        _control.SignIn(token).Should().NotBeNull("disabling does not end the sign-in by itself");

        var r = await _http.PostAsJsonAsync("/api/logout", new { token }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        _control.SignIn(token).Should().BeNull();
    }

    [Fact]
    public async Task SignIn_UnusedFor30Days_SignInAgain()
    {
        AddUser("dan", "correct horse battery");
        var token = await LoginAsync("dan", "correct horse battery");
        _clock.Advance(TimeSpan.FromDays(29));
        _control.SignIn(token).Should().NotBeNull();
        _clock.Advance(TimeSpan.FromDays(29));
        _control.SignIn(token).Should().NotBeNull("used 29 days ago");
        _clock.Advance(TimeSpan.FromDays(30));
        _control.SignIn(token).Should().BeNull();
    }

    [Fact]
    public async Task SetPassword_Changed_SignedOutEverywhere()
    {
        AddUser("dan", "correct horse battery");
        var token = await LoginAsync("dan", "correct horse battery");
        _users.SetPassword(_options.Users[0].Id, "a brand new password");
        _control.SignIn(token).Should().BeNull();
        (await PostLoginAsync("dan", "correct horse battery")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await LoginAsync("dan", "a brand new password");
    }

    [Fact]
    public async Task Login_FromThisComputerWithoutProxyHeaders_Refused()
    {
        // A browser here, or nginx here without its headers: no telling whether the password crossed a network in the clear.
        AddUser("dan", "correct horse battery");
        var r = await _http.PostAsJsonAsync("/api/login", new { username = "dan", password = "correct horse battery" }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("error").GetString().Should().Contain("X-Forwarded-Proto");
    }

    [Fact]
    public async Task Get_FromThisComputerWithSignInOff_ServedButNoticed()
    {
        var noticed = 0;
        _server.LoopbackNoticed += () => noticed++;
        _server.LoopbackSeen.Should().BeFalse();
        (await _http.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _http.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        _server.LoopbackSeen.Should().BeTrue();
        noticed.Should().Be(1, "Settings is told once");
    }

    [Fact]
    public async Task Get_FromThisComputerWithSignInOn_NotNoticed()
    {
        AddUser("dan", "correct horse battery");
        await _http.GetAsync("/", TestContext.Current.CancellationToken);
        _server.LoopbackSeen.Should().BeFalse("with sign-in on a password protects the station anyway");
    }

    [Fact]
    public async Task Request_ThroughProxyWithSignInOff_Refused()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/");
        req.Headers.Add("X-Forwarded-For", "203.0.113.7");
        var r = await _http.SendAsync(req, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await r.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Contain("sign-in is off");
    }

    [Theory]
    [InlineData("http", HttpStatusCode.Forbidden)]
    [InlineData(null, HttpStatusCode.Forbidden)]
    [InlineData("https", HttpStatusCode.OK)]
    public async Task Login_ThroughProxy_OnlyOverHttps(string? proto, HttpStatusCode expected)
    {
        AddUser("dan", "correct horse battery");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/login")
        {
            Content = JsonContent.Create(new { username = "dan", password = "correct horse battery" }),
        };
        req.Headers.Add("X-Forwarded-For", "203.0.113.7");
        if (proto is not null) req.Headers.Add("X-Forwarded-Proto", proto);
        (await _http.SendAsync(req, TestContext.Current.CancellationToken)).StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task Socket_ThroughProxy_OriginOfTheForwardedHostAccepted()
    {
        AddUser("dan", "correct horse battery");
        var token = await LoginAsync("dan", "correct horse battery");
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Origin", "https://radio.example.org");
        ws.Options.SetRequestHeader("X-Forwarded-For", "203.0.113.7");
        ws.Options.SetRequestHeader("X-Forwarded-Proto", "https");
        ws.Options.SetRequestHeader("X-Forwarded-Host", "radio.example.org");
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws"), TestContext.Current.CancellationToken);
        await SendAsync(ws, new { type = "hello", token });
        (await ReceiveAsync(ws)).GetProperty("type").GetString().Should().Be("user");

        using var evil = new ClientWebSocket();
        evil.Options.SetRequestHeader("Origin", "https://evil.example");
        evil.Options.SetRequestHeader("X-Forwarded-For", "203.0.113.7");
        evil.Options.SetRequestHeader("X-Forwarded-Host", "radio.example.org");
        var act = () => evil.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws"), TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<WebSocketException>();
    }

    private void AddUser(string username, string password)
    {
        var user = new RemoteUser { Username = username };
        _options.Users.Add(user);
        _options.RequireSignIn = true;
        _users.SetPassword(user.Id, password);
    }

    // The test talks to 127.0.0.1, so it signs in the way nginx on this computer would pass a browser's https sign-in.
    private Task<HttpResponseMessage> PostLoginAsync(string username, string password)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/login") { Content = JsonContent.Create(new { username, password }) };
        req.Headers.Add("X-Forwarded-For", "203.0.113.7");
        req.Headers.Add("X-Forwarded-Proto", "https");
        return _http.SendAsync(req, TestContext.Current.CancellationToken);
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        var r = await PostLoginAsync(username, password);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("token").GetString()!;
    }

    private async Task<JsonElement> GetJsonAsync(string path) =>
        await _http.GetFromJsonAsync<JsonElement>(path, TestContext.Current.CancellationToken);

    private static async Task ExpectClosedAsync(ClientWebSocket ws, string reason)
    {
        var buf = new byte[64 * 1024];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        WebSocketReceiveResult r;
        do r = await ws.ReceiveAsync(buf, cts.Token);
        while (r.MessageType != WebSocketMessageType.Close);
        ws.CloseStatusDescription.Should().Be(reason);
    }

    private async Task<string> PairAsync()
    {
        var r = await _http.PostAsJsonAsync("/api/pair", new { }, TestContext.Current.CancellationToken);
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var j = await r.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return j.GetProperty("token").GetString()!;
    }

    private async Task<ClientWebSocket> ConnectAsync()
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{_port}/ws"), TestContext.Current.CancellationToken);
        return ws;
    }

    private static Task SendAsync(ClientWebSocket ws, object message) =>
        ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, true, TestContext.Current.CancellationToken);

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket ws)
    {
        var buf = new byte[64 * 1024];
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
