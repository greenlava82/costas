// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.Integrations.Wsjtx;

/// <summary>Replay (type 7): send the decodes still on screen again, marked not new, then a Status.</summary>
/// <param name="TargetId">The client it is meant for.</param>
public sealed record WsjtxReplay(string TargetId) : WsjtxInboundMessage(TargetId);
