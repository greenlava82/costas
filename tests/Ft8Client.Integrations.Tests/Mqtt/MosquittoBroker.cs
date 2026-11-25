// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Diagnostics;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Formatter;

namespace Ft8Client.Integrations.Tests.Mqtt;

/// <summary>
/// A real Mosquitto broker in a child process, on its own port with its own configuration, for tests that check
/// Costas against the broker most Home Assistant users run. Skips the test when Mosquitto is not installed, unless
/// COSTAS_REQUIRE_MOSQUITTO=1 (Linux CI), where a missing broker fails instead of passing silently.
/// <para>
/// What the broker receives is watched by a separate MQTT 5 client subscribed to everything, with retain-as-published
/// so each message keeps its retain flag. Only the process this class started is ever stopped.
/// </para>
/// </summary>
public sealed class MosquittoBroker : IAsyncDisposable
{
    private readonly string _dir;
    private readonly string _config;
    private readonly object _gate = new();
    private readonly List<(string Topic, string Payload, bool Retain)> _published = [];
    private Process? _process;
    private IMqttClient? _watcher;

    private MosquittoBroker(string exe, int port, string? user, string? password)
    {
        Exe = exe;
        Port = port;
        User = user;
        Password = password;
        _dir = Directory.CreateTempSubdirectory("costas-mosquitto-").FullName;
        _config = Path.Combine(_dir, "mosquitto.conf");
        var lines = new List<string>
        {
            $"listener {port} 127.0.0.1",
            "persistence false",
            "log_dest stdout",
            "log_type error",
            "log_type warning",
            "log_type notice",
            // Run as whoever started it (root in a container would otherwise switch to a "mosquitto" user that cannot read
            // the temp folder).
            "user " + Environment.UserName,
        };
        if (user is null)
        {
            lines.Add("allow_anonymous true");
        }
        else
        {
            var passwords = Path.Combine(_dir, "passwords");
            File.WriteAllText(passwords, string.Empty);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(passwords, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var passwd = Path.Combine(Path.GetDirectoryName(exe)!, OperatingSystem.IsWindows() ? "mosquitto_passwd.exe" : "mosquitto_passwd");
            if (!File.Exists(passwd)) passwd = Find("mosquitto_passwd") ?? throw new FileNotFoundException("mosquitto_passwd not found");
            Run(passwd, "-b", passwords, user, password ?? string.Empty);
            lines.Add("allow_anonymous false");
            lines.Add($"password_file {passwords}");
        }
        File.WriteAllLines(_config, lines);
    }

    /// <summary>The mosquitto executable.</summary>
    public string Exe { get; }

    /// <summary>The port it listens on (loopback only).</summary>
    public int Port { get; }

    /// <summary>The one user it accepts, or null for anonymous.</summary>
    public string? User { get; }

    private string? Password { get; }

    /// <summary>Everything the broker has passed on, in order, with retain flags as published.</summary>
    public IReadOnlyList<(string Topic, string Payload, bool Retain)> Published
    {
        get { lock (_gate) return [.. _published]; }
    }

    /// <summary>Mosquitto, or null when it is not installed.</summary>
    public static string? Locate() => Find("mosquitto");

    /// <summary>
    /// Skips the test when Mosquitto is missing, or fails it when COSTAS_REQUIRE_MOSQUITTO=1; otherwise starts a broker.
    /// </summary>
    public static async Task<MosquittoBroker> StartAsync(string? user = null, string? password = null)
    {
        var exe = Locate();
        if (exe is null)
        {
            if (Environment.GetEnvironmentVariable("COSTAS_REQUIRE_MOSQUITTO") == "1")
                throw new InvalidOperationException("COSTAS_REQUIRE_MOSQUITTO=1 but mosquitto is not installed.");
            Assert.Skip("Mosquitto is not installed.");
        }
        var broker = new MosquittoBroker(exe!, MqttTestBroker.FreePort(), user, password);
        await broker.StartProcessAsync();
        await broker.WatchAsync();
        return broker;
    }

    /// <summary>Stops the broker (as a crash would: no goodbye to clients), leaving its port free for a restart.</summary>
    public async Task StopAsync()
    {
        if (_watcher is { } w)
        {
            _watcher = null;
            w.Dispose();
        }
        if (_process is not { } p) return;
        _process = null;
        try
        {
            p.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        await p.WaitForExitAsync();
        p.Dispose();
    }

    /// <summary>Starts it again on the same port after <see cref="StopAsync"/>. Nothing retained survives (no persistence).</summary>
    public async Task RestartAsync()
    {
        await StartProcessAsync();
        await WatchAsync();
    }

    /// <summary>Waits (up to 30 s) until a message matching <paramref name="match"/> has passed through the broker.</summary>
    public async Task<(string Topic, string Payload, bool Retain)> WaitForAsync(Func<(string Topic, string Payload, bool Retain), bool> match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var m in Published)
                if (match(m)) return m;
            await Task.Delay(20);
        }
        throw new TimeoutException("The expected MQTT message did not pass through Mosquitto within 30 s. Seen: "
                                   + string.Join(" | ", Published.Select(m => $"{m.Topic}={m.Payload}")));
    }

    /// <summary>
    /// Waits (up to 30 s) until a new subscriber to <paramref name="topic"/> is handed a retained message matching
    /// <paramref name="match"/> (or, with <paramref name="match"/> null, until nothing is retained there).
    /// </summary>
    public async Task<string?> RetainedAsync(string topic, Func<string, bool>? match)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        string? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last = await RetainedNowAsync(topic);
            if (match is null ? last is null : last is not null && match(last)) return last;
            await Task.Delay(100);
        }
        throw new TimeoutException($"Mosquitto did not hold the expected retained message on {topic} within 30 s; it holds \"{last}\".");
    }

    // What a new subscriber is handed on the topic now. Mosquitto queues the retained message when it handles the
    // subscription, so a message on a topic of our own, published after subscribing, arrives after it and marks the end.
    private async Task<string?> RetainedNowAsync(string topic)
    {
        using var client = new MqttClientFactory().CreateMqttClient();
        var marker = $"costas-test/marker/{Guid.NewGuid():N}";
        string? retained = null;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        client.ApplicationMessageReceivedAsync += e =>
        {
            if (e.ApplicationMessage.Topic == marker) done.TrySetResult();
            else if (e.ApplicationMessage.Retain && e.ApplicationMessage.Topic == topic) retained = e.ApplicationMessage.ConvertPayloadToString();
            return Task.CompletedTask;
        };
        await client.ConnectAsync(ClientOptions($"costas-test-r-{Guid.NewGuid():N}"[..22]));
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(topic, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(marker, MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build());
        await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(marker).WithPayload("x")
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce).Build());
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await client.DisconnectAsync();
        return retained;
    }

    private MqttClientOptions ClientOptions(string id)
    {
        var b = new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", Port).WithClientId(id).WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanSession(true);
        if (User is not null) b = b.WithCredentials(User, Password);
        return b.Build();
    }

    private async Task WatchAsync()
    {
        var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            var m = e.ApplicationMessage;
            if (!m.Topic.StartsWith("costas-test/", StringComparison.Ordinal))
                lock (_gate) _published.Add((m.Topic, m.ConvertPayloadToString() ?? string.Empty, m.Retain));
            return Task.CompletedTask;
        };
        await client.ConnectAsync(ClientOptions($"costas-test-w-{Guid.NewGuid():N}"[..22]));
        // Retained messages already held are not news: only what is published from now on is recorded.
        await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic("#").WithAtLeastOnceQoS().WithRetainAsPublished(true)
                .WithRetainHandling(MQTTnet.Protocol.MqttRetainHandling.DoNotSendOnSubscribe)).Build());
        _watcher = client;
    }

    private async Task StartProcessAsync()
    {
        var psi = new ProcessStartInfo(Exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(_config);
        var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start mosquitto");
        var output = new System.Text.StringBuilder();
        p.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _process = p;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (p.HasExited)
            {
                string log;
                lock (output) log = output.ToString();
                throw new InvalidOperationException($"mosquitto exited with {p.ExitCode}: {log}");
            }
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync("127.0.0.1", Port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(50);
            }
        }
        throw new TimeoutException("mosquitto did not start listening within 15 s.");
    }

    private static string? Find(string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(OperatingSystem.IsWindows() ? [@"C:\Program Files\mosquitto"] : ["/usr/sbin", "/usr/local/sbin", "/opt/homebrew/sbin"]);
        return dirs.Select(d => Path.Combine(d, file)).FirstOrDefault(File.Exists);
    }

    private static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{exe} failed ({p.ExitCode}): {err}");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
