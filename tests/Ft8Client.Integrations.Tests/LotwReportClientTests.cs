// Costas - a station-centric FT8/FT4 client.
// Copyright (C) 2026 Costas contributors
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. This program is distributed WITHOUT ANY WARRANTY;
// see the GNU General Public License in LICENSE for details.

using System.Net;
using System.Text;
using System.Threading.Channels;
using Ft8Client.Integrations.Lotw;
using Microsoft.Extensions.Time.Testing;

namespace Ft8Client.Integrations.Tests;

// Issue #219: LoTW took longer than the old 2-minute timeout to answer, so confirmations never downloaded. Time here is a
// FakeTimeProvider moved by hand, so nothing waits on the wall clock.
public class LotwReportClientTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);

    private const string Report = """
        ARRL Logbook of the World Status Report
        <PROGRAMID:4>LoTW
        <APP_LoTW_LASTQSL:19>2026-10-11 18:22:05
        <APP_LoTW_NUMREC:1>2
        <eoh>
        <CALL:5>JA1AB
        <BAND:3>20M
        <MODE:3>FT8
        <QSO_DATE:8>20261010
        <TIME_ON:6>201500
        <QSL_RCVD:1>Y
        <eor>
        <CALL:5>KL7RA
        <BAND:3>40M
        <MODE:3>FT8
        <QSO_DATE:8>20261009
        <TIME_ON:6>063000
        <QSL_RCVD:1>Y
        <eor>
        <APP_LoTW_EOF>
        """;

    [Fact]
    public async Task FetchAsync_AnswerAfterFourteenMinutes_Downloads()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var handler = new Handler(body);
        var client = new LotwReportClient(Http(handler), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, "PE1LIF", TestContext.Current.CancellationToken);
        await handler.Requested.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await body.WaitForReadAsync();
        time.Advance(TimeSpan.FromMinutes(14));
        body.Send(Report);
        body.End();

        var r = await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Ok.Should().BeTrue(r.Error);
        r.Records.Select(x => x["call"]).Should().Equal("JA1AB", "KL7RA");
        r.LastQsl.Should().Be("2026-10-11 18:22:05");
    }

    [Fact]
    public async Task FetchAsync_SlowHeaders_StillWaitsForFirstByteTimeout()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var handler = new Handler(body, holdHeaders: true);
        var client = new LotwReportClient(Http(handler), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await handler.Requested.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(10));
        handler.ReleaseHeaders();
        body.Send(Report);
        body.End();

        (await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Ok.Should().BeTrue();
    }

    [Fact]
    public async Task FetchAsync_NoAnswerForFifteenMinutes_SaysLotwDidNotAnswer()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var handler = new Handler(body, holdHeaders: true);
        var client = new LotwReportClient(Http(handler), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await handler.Requested.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        time.Advance(LotwReportClient.FirstByteTimeout);

        var r = await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Ok.Should().BeFalse();
        r.Error.Should().Be("LoTW did not answer in 15 minutes");
        r.LastQsl.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_HeadersButNoBodyForFifteenMinutes_SaysLotwDidNotAnswer()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body)), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await body.WaitForReadAsync();
        time.Advance(LotwReportClient.FirstByteTimeout);

        (await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Error.Should().Be("LoTW did not answer in 15 minutes");
    }

    [Fact]
    public async Task FetchAsync_TrickleWithTwoMinuteGaps_DownloadsPastTheFirstByteTimeout()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body)), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        // 12 pieces two minutes apart: 24 minutes in all, longer than the first-byte timeout, never 3 minutes quiet.
        var pieces = Split(Report, 12);
        foreach (var piece in pieces)
        {
            await body.WaitForReadAsync();
            time.Advance(TimeSpan.FromMinutes(2));
            body.Send(piece);
        }
        await body.WaitForReadAsync();
        body.End();

        var r = await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Ok.Should().BeTrue(r.Error);
        r.Records.Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchAsync_GoesQuietAfterStarting_SaysLotwStoppedSending()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body)), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await body.WaitForReadAsync();
        body.Send(Report[..80]);
        await body.WaitForReadAsync();
        time.Advance(LotwReportClient.IdleTimeout);

        var r = await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Error.Should().Be("LoTW stopped sending for 3 minutes");
        r.LastQsl.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_SendingForMoreThanThirtyMinutes_GivesUp()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body)), time);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        // A piece every 2 minutes for 28 minutes, then 2 more quiet minutes: the 30-minute limit ends it before the
        // 3-minute quiet limit would.
        for (var i = 0; i < 14; i++)
        {
            await body.WaitForReadAsync();
            time.Advance(TimeSpan.FromMinutes(2));
            body.Send(" ");
        }
        await body.WaitForReadAsync();
        time.Advance(TimeSpan.FromMinutes(2));

        (await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Error.Should().Be("LoTW did not finish sending in 30 minutes");
    }

    [Fact]
    public async Task FetchAsync_CallerCancels_Throws()
    {
        var time = new FakeTimeProvider();
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body)), time);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, cts.Token);
        await body.WaitForReadAsync();
        await cts.CancelAsync();

        await FluentActions.Awaiting(() => fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchAsync_ServerError_SaysWhatLotwAnswered()
    {
        var client = new LotwReportClient(Http(new Handler(new GatedStream(), status: HttpStatusCode.ServiceUnavailable)), new FakeTimeProvider());
        var r = await client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Error.Should().Be("LoTW answered 503");
    }

    [Fact]
    public async Task FetchAsync_NoConnection_SaysUnreachable()
    {
        var client = new LotwReportClient(Http(new Handler(new GatedStream(), fail: true)), new FakeTimeProvider());
        var r = await client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken).WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Error.Should().Be("LoTW unreachable");
    }

    [Fact]
    public async Task FetchAsync_ConnectionDropsPartWay_SaysCutOff()
    {
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body)), new FakeTimeProvider());

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await body.WaitForReadAsync();
        body.Send(Report[..80]);
        await body.WaitForReadAsync();
        body.Fail();

        var r = await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        r.Error.Should().Be("The LoTW download was cut off");
        r.LastQsl.Should().BeNull();
    }

    [Fact]
    public async Task FetchAsync_Latin1Charset_DecodesAsSent()
    {
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body, charset: "iso-8859-1")), new FakeTimeProvider());
        var text = Report.Replace("<QSO_DATE:8>20261010", "<NAME:4>José<QSO_DATE:8>20261010", StringComparison.Ordinal);

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await body.WaitForReadAsync();
        body.Send(text, Encoding.Latin1);
        await body.WaitForReadAsync();
        body.End();

        (await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Records[0]["name"].Should().Be("José");
    }

    [Fact]
    public async Task FetchAsync_UnknownCharset_FallsBackToUtf8()
    {
        var body = new GatedStream();
        var client = new LotwReportClient(Http(new Handler(body, charset: "x-no-such-charset")), new FakeTimeProvider());

        var fetch = client.FetchAsync("PE1LIF", "pw", null, null, TestContext.Current.CancellationToken);
        await body.WaitForReadAsync();
        body.Send(Report);
        await body.WaitForReadAsync();
        body.End();

        (await fetch.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Ok.Should().BeTrue();
    }

    [Fact]
    public void Parse_FewerRecordsThanHeaderAndNoEndMarker_RefusesAndKeepsTheOldPlace()
    {
        var cut = Report[..Report.IndexOf("<CALL:5>KL7RA", StringComparison.Ordinal)];
        var r = LotwReportClient.Parse(cut);
        r.Ok.Should().BeFalse();
        r.Error.Should().Be("LoTW sent 1 of 2 confirmations; the rest come with the next download");
        r.LastQsl.Should().BeNull();
    }

    [Fact]
    public void Parse_FewerRecordsThanHeaderButEndMarker_TrustsTheEndMarker()
    {
        var text = Report.Replace("<APP_LoTW_NUMREC:1>2", "<APP_LoTW_NUMREC:1>5", StringComparison.Ordinal);
        var r = LotwReportClient.Parse(text);
        r.Ok.Should().BeTrue();
        r.Records.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_NoRecordCountInHeader_ReadsWhatCame()
    {
        var text = Report.Replace("<APP_LoTW_NUMREC:1>2", "", StringComparison.Ordinal).Replace("<APP_LoTW_EOF>", "", StringComparison.Ordinal);
        LotwReportClient.Parse(text).Records.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_EmptyReport_IsOk()
    {
        var r = LotwReportClient.Parse("<APP_LoTW_LASTQSL:19>2026-10-11 18:22:05\n<APP_LoTW_NUMREC:1>0\n<eoh>\n<APP_LoTW_EOF>\n");
        r.Ok.Should().BeTrue();
        r.Records.Should().BeEmpty();
        r.LastQsl.Should().Be("2026-10-11 18:22:05");
    }

    private static HttpClient Http(HttpMessageHandler handler) => new(handler) { Timeout = Timeout.InfiniteTimeSpan };

    private static IEnumerable<string> Split(string text, int pieces)
    {
        var size = (text.Length + pieces - 1) / pieces;
        for (var i = 0; i < text.Length; i += size) yield return text.Substring(i, Math.Min(size, text.Length - i));
    }

    private sealed class Handler(GatedStream body, bool holdHeaders = false, HttpStatusCode status = HttpStatusCode.OK, bool fail = false,
                                 string charset = "utf-8") : HttpMessageHandler
    {
        private readonly TaskCompletionSource _headers = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseHeaders() => _headers.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.TrySetResult();
            if (fail) throw new HttpRequestException("No connection");
            if (holdHeaders) await _headers.Task.WaitAsync(cancellationToken);
            var content = new StreamContent(body);
            content.Headers.TryAddWithoutValidation("Content-Type", $"text/plain; charset={charset}");
            return new HttpResponseMessage(status) { Content = content };
        }
    }

    // A response body the test feeds piece by piece. WaitForReadAsync returns once the reader is waiting for the next
    // piece, so time is only moved while the download is blocked.
    private sealed class GatedStream : Stream
    {
        private readonly Channel<byte[]?> _pieces = Channel.CreateUnbounded<byte[]?>();
        private readonly SemaphoreSlim _reading = new(0);
        private byte[] _left = [];
        private int _at;

        public void Send(string text, Encoding? encoding = null) => _pieces.Writer.TryWrite((encoding ?? Encoding.UTF8).GetBytes(text));

        public void End() => _pieces.Writer.TryComplete();

        public void Fail() => _pieces.Writer.TryWrite(null);

        public async Task WaitForReadAsync()
        {
            if (!await _reading.WaitAsync(Deadline, TestContext.Current.CancellationToken)) throw new TimeoutException("The download never asked for more");
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_at >= _left.Length)
            {
                _reading.Release();
                if (!await _pieces.Reader.WaitToReadAsync(cancellationToken)) return 0;
                var next = await _pieces.Reader.ReadAsync(cancellationToken) ?? throw new IOException("Connection reset");
                _left = next;
                _at = 0;
            }
            var n = Math.Min(buffer.Length, _left.Length - _at);
            _left.AsMemory(_at, n).CopyTo(buffer);
            _at += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
