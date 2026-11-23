// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.
namespace Ft8Client.Integrations.Wsjtx;

/// <summary>
/// Configure (type 15): another program sets options, as DXLab SpotCollector does when a spot is double-clicked (it
/// fills in the DX call and grid). Empty strings mean no change. Costas uses only the DX call and grid.
/// </summary>
/// <param name="TargetId">The client it is meant for.</param>
/// <param name="Mode">Mode name, or empty.</param>
/// <param name="DxCall">DX call, or empty.</param>
/// <param name="DxGrid">DX grid, or empty.</param>
/// <param name="GenerateMessages">True to have the standard messages generated for the DX call.</param>
public sealed record WsjtxConfigure(string TargetId, string Mode, string DxCall, string DxGrid, bool GenerateMessages) : WsjtxInboundMessage(TargetId);
