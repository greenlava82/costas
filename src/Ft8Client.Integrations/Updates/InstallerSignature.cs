// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

namespace Ft8Client.Integrations.Updates;

/// <summary>The result of checking an installer's signature.</summary>
/// <param name="Status">Whether it is signed, and validly.</param>
/// <param name="Publisher">The signing certificate's subject common name (CN) when the signature is valid, else null.</param>
/// <param name="Detail">Why the check failed, for the log and the message shown, or null.</param>
public sealed record InstallerSignature(InstallerSignatureStatus Status, string? Publisher, string? Detail = null);
