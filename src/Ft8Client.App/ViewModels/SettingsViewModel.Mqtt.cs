// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.


using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ft8Client.Core.Mqtt;
using Ft8Client.Data.Secrets;
using Ft8Client.Data.Settings;

namespace Ft8Client.App.ViewModels;

/// <summary>Settings › MQTT: publishing station events to a broker, one way only.</summary>
public sealed partial class SettingsViewModel
{
    /// <summary>Publish station events.</summary>
    [ObservableProperty]
    public partial bool MqttEnabled { get; set; }

    /// <summary>Broker host.</summary>
    [ObservableProperty]
    public partial string MqttHost { get; set; } = string.Empty;

    /// <summary>Broker port.</summary>
    [ObservableProperty]
    public partial string MqttPort { get; set; } = "1883";

    /// <summary>Connect with TLS.</summary>
    [ObservableProperty]
    public partial bool MqttTls { get; set; }

    /// <summary>Broker user name.</summary>
    [ObservableProperty]
    public partial string MqttUsername { get; set; } = string.Empty;

    /// <summary>Broker password (kept in the OS credential store).</summary>
    [ObservableProperty]
    public partial string MqttPassword { get; set; } = string.Empty;

    /// <summary>Also announce the device to Home Assistant.</summary>
    [ObservableProperty]
    public partial bool MqttHomeAssistant { get; set; }

    /// <summary>Publish a qso_logged event for each contact.</summary>
    [ObservableProperty]
    public partial bool MqttPublishContacts { get; set; }

    /// <summary>Minutes without transmitting before the on-air session ends.</summary>
    [ObservableProperty]
    public partial string MqttIdleMinutes { get; set; } = "5";

    /// <summary>Longest session in hours.</summary>
    [ObservableProperty]
    public partial string MqttMaxHours { get; set; } = "4";

    /// <summary>The result of Test connection.</summary>
    [ObservableProperty]
    public partial string MqttTestResult { get; set; } = string.Empty;

    /// <summary>Test connection is running.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestMqttCommand))]
    public partial bool MqttTesting { get; set; }

    /// <summary>The topic base shown on the page, such as costas/w7lit.</summary>
    public string MqttTopicBase => $"costas/{MqttStationIdShown}";

    private string MqttStationIdShown => string.IsNullOrWhiteSpace(_host.Settings.Current.Mqtt.StationId)
        ? MqttStationId.From(_host.Settings.Current.Profile.Callsign)
        : _host.Settings.Current.Mqtt.StationId;

    private void LoadMqtt(AppSettings s)
    {
        var m = s.Mqtt;
        MqttEnabled = m.Enabled;
        MqttHost = m.Host;
        MqttPort = m.Port.ToString(CultureInfo.InvariantCulture);
        MqttTls = m.Tls;
        MqttUsername = m.Username;
        MqttPassword = _host.Secrets.Get(SecretNames.MqttPassword) ?? string.Empty;
        MqttHomeAssistant = m.HomeAssistantDiscovery;
        MqttPublishContacts = m.PublishContacts;
        MqttIdleMinutes = m.SessionIdleMinutes.ToString(CultureInfo.InvariantCulture);
        MqttMaxHours = (m.SessionMaxMinutes / 60.0).ToString("0.#", CultureInfo.InvariantCulture);
    }

    // Before the settings are saved, so the publisher picks up a new password when it reconnects on the change.
    private void SaveMqttPassword() => Set(SecretNames.MqttPassword, MqttPassword);

    private void StoreMqtt(AppSettings s) => StoreMqttFields(s.Mqtt);

    private void StoreMqttFields(MqttOptions m)
    {
        m.Enabled = MqttEnabled;
        m.Host = MqttHost.Trim();
        m.Port = int.TryParse(MqttPort, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? Math.Clamp(port, 1, 65535) : 1883;
        m.Tls = MqttTls;
        m.Username = MqttUsername.Trim();
        m.HomeAssistantDiscovery = MqttHomeAssistant;
        m.PublishContacts = MqttPublishContacts;
        m.SessionIdleMinutes = int.TryParse(MqttIdleMinutes, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idle)
            ? Math.Clamp(idle, 1, 60) : 5;
        m.SessionMaxMinutes = double.TryParse(MqttMaxHours, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours)
            ? (int)Math.Round(Math.Clamp(hours, 0.5, 12) * 60) : 240;
    }

    private bool CanTestMqtt() => !MqttTesting;

    /// <summary>Connects once with the values on the page and says how it went.</summary>
    [RelayCommand(CanExecute = nameof(CanTestMqtt))]
    private async Task TestMqtt()
    {
        if (string.IsNullOrWhiteSpace(MqttHost))
        {
            MqttTestResult = "Enter the broker's address first.";
            return;
        }
        MqttTesting = true;
        MqttTestResult = "Testing…";
        try
        {
            var o = new MqttOptions
            {
                StationId = _host.Settings.Current.Mqtt.StationId,
                ClientId = _host.Settings.Current.Mqtt.ClientId,
                DiscoveryPrefix = _host.Settings.Current.Mqtt.DiscoveryPrefix,
            };
            StoreMqttFields(o);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var r = await _host.Mqtt.TestAsync(o, MqttPassword, cts.Token);
            MqttTestResult = r.Message;
        }
        catch (OperationCanceledException)
        {
            MqttTestResult = "The broker did not answer in 20 seconds.";
        }
        finally
        {
            MqttTesting = false;
        }
    }
}
