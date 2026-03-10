using BenchmarkDotNet.Attributes;
using FpingSharp.Internal;

namespace FpingSharp.Benchmark;

[MemoryDiagnoser]
public class IcmpPacketBenchmark
{
    private byte[] _buffer = null!;
    private byte[] _replyBuffer = null!;
    private int _replyLength;

    [GlobalSetup]
    public void Setup()
    {
        _buffer = new byte[IcmpPacket.HeaderSize + 56];

        // Build a valid echo request, then turn it into a reply for parsing benchmarks
        IcmpPacket.BuildEchoRequest(_buffer, 0x1234, 1, 56, false);

        // Simulate a raw IPv4 echo reply: 20-byte IP header + ICMP
        _replyLength = 20 + IcmpPacket.HeaderSize + 56;
        _replyBuffer = new byte[_replyLength];
        _replyBuffer[0] = 0x45; // IPv4, IHL=5
        // Copy ICMP data starting at offset 20
        _buffer[0] = IcmpPacket.EchoReply; // change type to reply
        Array.Copy(_buffer, 0, _replyBuffer, 20, IcmpPacket.HeaderSize + 56);
    }

    [Benchmark(Description = "BuildEchoRequest (56B payload)")]
    public int Build() => IcmpPacket.BuildEchoRequest(_buffer, 0x1234, 1, 56, false);

    [Benchmark(Description = "BuildEchoRequest IPv6 (56B payload)")]
    public int BuildV6() => IcmpPacket.BuildEchoRequest(_buffer, 0x1234, 1, 56, true);

    [Benchmark(Description = "ParseRawV4")]
    public bool ParseRaw() => IcmpPacket.ParseRawV4(_replyBuffer, _replyLength, 0x1234).HasValue;

    [Benchmark(Description = "ParseDgramV4")]
    public bool ParseDgram() => IcmpPacket.ParseDgramV4(_buffer, IcmpPacket.HeaderSize + 56, 0x1234).HasValue;
}
