// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Security;
using Ft8Client.Core.Messages;
using Ft8Client.Core.Notifications;

namespace Ft8Client.App.Services.Notifications;

/// <summary>
/// The XML of a Windows toast, and the station its launch string names. The toast has a title and a line of text, no
/// buttons and no sound, and the default scenario, so Focus Assist treats it like any app's.
/// </summary>
public static class ToastXml
{
    private const string Prefix = "select=";

    /// <summary>The toast XML for <paramref name="n"/>.</summary>
    public static string For(DesktopNotification n) =>
        $"<toast launch=\"{Escape(Prefix + n.Call)}\"><visual><binding template=\"ToastGeneric\">" +
        $"<text>{Escape(n.Title)}</text><text>{Escape(n.Body)}</text>" +
        "</binding></visual><audio silent=\"true\"/></toast>";

    /// <summary>The call in a toast's launch string, or null when it names none.</summary>
    public static string? CallOf(string arguments)
    {
        if (!arguments.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var call = Callsign.Normalize(arguments[Prefix.Length..].Trim());
        return call.Length > 0 ? call : null;
    }

    private static string Escape(string s) => SecurityElement.Escape(s);
}
