// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using Ft8Client.Rig.Tests.Flex;

// A pretend FLEX-8600 with a PowerGenius XL and TunerGenius XL, for trying Costas's amp and tuner buttons without the
// hardware (docs/05-integrations.md section 2a). In Costas, type 127.0.0.1 in Settings › Station › FlexRadio amp and
// tuner. It only answers the SmartSDR API's amplifier commands; it does not stand in for the radio itself.
//
//   dotnet run --project tools/Ft8Client.FakeFlex                 listens on 127.0.0.1:4992
//   dotnet run --project tools/Ft8Client.FakeFlex -- --port 5000  another port (type 127.0.0.1:5000 in Costas)

var port = 4992;
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[++i], out var p)) port = p;

const string Amp = "0x18E5F0AA";
const string Tuner = "0x2C01D3B4";
await using var radio = new FakeFlexRadio(new IPEndPoint(IPAddress.Loopback, port));
radio.OnSubscribe.Add($"S0|amplifier {Amp} ip=127.0.0.1 model=PowerGeniusXL serial_num=FAKE ant=ANT1:PORTA,ANT2:NONE state=STANDBY");
radio.OnSubscribe.Add($"S0|amplifier {Tuner} ip=127.0.0.1 model=TunerGeniusXL serial_num=FAKE operate=1 bypass=0 tuning=0");
radio.Received += c => Console.WriteLine($"{DateTime.Now:HH:mm:ss}  <- {c}");

Console.WriteLine($"Pretend FlexRadio on 127.0.0.1:{port}: PowerGenius XL in standby, TunerGenius XL in operate.");
Console.WriteLine("Keys: t = amp transmits for 5 s, f = amp fault, i = amp back to operate (idle), u = tuner tunes for 3 s,");
Console.WriteLine("      r = amp removed, a = amp back, x = drop the connection, q = quit.");

if (Console.IsInputRedirected)
{
    // No keyboard (run from a script): just serve until stopped.
    await Task.Delay(Timeout.Infinite);
}

while (true)
{
    var key = Console.ReadKey(intercept: true).KeyChar;
    try
    {
        switch (key)
        {
            case 't':
                await Send($"S0|amplifier {Amp} state=TRANSMIT_A");
                _ = Task.Delay(5000).ContinueWith(_ => Send($"S0|amplifier {Amp} state=IDLE"));
                break;
            case 'f': await Send($"S0|amplifier {Amp} state=FAULT"); break;
            case 'i': await Send($"S0|amplifier {Amp} state=IDLE"); break;
            case 'u':
                await Send($"S0|amplifier {Tuner} tuning=1");
                _ = Task.Delay(3000).ContinueWith(_ => Send($"S0|amplifier {Tuner} tuning=0"));
                break;
            case 'r': await Send($"S0|amplifier {Amp} removed"); break;
            case 'a': await Send(radio.OnSubscribe[0]); break;
            case 'x': radio.Drop(); Console.WriteLine("Connection dropped; Costas should reconnect."); break;
            case 'q': return;
        }
    }
    catch (InvalidOperationException)
    {
        Console.WriteLine("Costas is not connected yet.");
    }
}

async Task Send(string line)
{
    await radio.SendAsync(line);
    Console.WriteLine($"{DateTime.Now:HH:mm:ss}  -> {line}");
}
