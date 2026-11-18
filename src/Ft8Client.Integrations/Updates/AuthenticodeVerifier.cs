// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Ft8Client.Integrations.Updates;

/// <summary>
/// Checks an installer's Authenticode signature with Windows' WinVerifyTrust (the check Explorer and SmartScreen use),
/// then reads the signer's common name. Revocation is not checked: an update check must not fail because a CRL server
/// cannot be reached, and the publisher name is what keeps out a release signed by anyone else.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AuthenticodeVerifier : IInstallerSignatureVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint RevocationCheckNone = 0x10;
    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);

    /// <inheritdoc />
    public InstallerSignature Check(string path)
    {
        var status = Verify(path);
        if (status == TrustENoSignature || status == TrustESubjectFormUnknown) return new InstallerSignature(InstallerSignatureStatus.Unsigned, null);
        if (status != 0)
            return new InstallerSignature(InstallerSignatureStatus.Invalid, null, string.Create(CultureInfo.InvariantCulture, $"WinVerifyTrust 0x{status:X8}"));
        try
        {
#pragma warning disable SYSLIB0057 // Reads the signer of a signed file; there is no X509CertificateLoader equivalent.
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            using var cert = new X509Certificate2(signer);
            return new InstallerSignature(InstallerSignatureStatus.Valid, cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
        }
        catch (CryptographicException ex)
        {
            return new InstallerSignature(InstallerSignatureStatus.Invalid, null, ex.Message);
        }
    }

    private static int Verify(string path)
    {
        var file = new WinTrustFileInfo { Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(), FilePath = Marshal.StringToCoTaskMemUni(path) };
        var filePtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                File = filePtr,
                StateAction = StateActionVerify,
                ProvFlags = RevocationCheckNone,
            };
            var action = GenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            data.StateAction = StateActionClose;
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return result;
        }
        finally
        {
            Marshal.FreeCoTaskMem(filePtr);
            Marshal.FreeCoTaskMem(file.FilePath);
        }
    }

    [LibraryImport("wintrust.dll", EntryPoint = "WinVerifyTrust")]
    private static partial int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    // WINTRUST_FILE_INFO
    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    // WINTRUST_DATA
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }
}
