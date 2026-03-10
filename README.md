# FpingSharp

A pure C# high-performance parallel ping library inspired by [fping](https://github.com/schweikert/fping). No native dependencies required.

## Features

- Async and synchronous ICMP ping
- IPv4 and IPv6 support
- CIDR range scanning (e.g. `192.168.1.0/24`)
- Parallel pinging of thousands of hosts
- Per-host and global RTT statistics (min/max/avg)
- Real-time event callbacks for replies, timeouts, and periodic stats
- Configurable retry, backoff, TTL, ToS, packet size, and more

## Requirements

- .NET Standard 2.1+ (.NET Core 3.0+, .NET 5+)
- **Linux**: raw sockets require `CAP_NET_RAW` or root privileges, or DGRAM sockets are used automatically
- **Windows**: requires Administrator privileges for raw ICMP sockets

## Installation

```
dotnet add package FpingSharp
```

## Quick Start

```csharp
using FpingSharp;

// Simple ping
using var client = new FpingClient();
var result = client.Run(new[] { "8.8.8.8", "1.1.1.1" });

foreach (var host in result.Hosts)
{
    Console.WriteLine($"{host.Name}: {(host.IsAlive ? "alive" : "unreachable")} " +
                      $"({host.Received}/{host.Sent}, avg={host.AvgRtt.TotalMilliseconds:F1}ms)");
}
```

### With Options

```csharp
using var client = new FpingClient(new FpingOptions
{
    Count = 5,            // 5 pings per host
    TimeoutMs = 1000,     // 1s timeout
    IntervalMs = 20,      // 20ms between pings
    Retry = 2,            // 2 retries
    AddressFamily = FpingAddressFamily.IPv4
});

var result = client.Run(new[] { "google.com", "github.com" });
```

### CIDR Range Scanning

```csharp
var hosts = CidrRange.Expand("192.168.1.0/24");
var result = client.Run(hosts);

Console.WriteLine($"Alive: {result.AliveCount}, Unreachable: {result.UnreachableCount}");
```

### Real-time Event Callbacks

```csharp
using var client = new FpingClient(new FpingOptions { Count = 10 });
client.OnReply += (sender, reply) =>
    Console.WriteLine($"{reply.HostName}: seq={reply.SequenceNumber} time={reply.RoundTripTime?.TotalMilliseconds:F1}ms");
client.OnTimeout += (sender, reply) =>
    Console.WriteLine($"{reply.HostName}: seq={reply.SequenceNumber} timeout");

client.Run(new[] { "8.8.8.8" });
```

### Async Usage

```csharp
var result = await client.RunAsync(new[] { "8.8.8.8" }, cancellationToken);
```

## FpingOptions Reference

| Property | Default | Description |
|---|---|---|
| `Count` | 1 | Number of pings per host (`-c`) |
| `Loop` | false | Continuous ping mode (`-l`) |
| `IntervalMs` | 10 | Min interval between any ping in ms (`-i`) |
| `PerHostIntervalMs` | 1000 | Interval between pings to same host in ms (`-p`) |
| `TimeoutMs` | 500 | Per-ping timeout in ms (`-t`) |
| `Retry` | 3 | Number of retries (`-r`) |
| `Backoff` | 1.5 | Retry backoff factor (`-B`) |
| `PacketSize` | 56 | ICMP payload size in bytes (`-b`) |
| `Ttl` | null | IP Time-To-Live |
| `Tos` | null | IP Type-Of-Service |
| `DontFragment` | false | Set the DF bit |
| `AddressFamily` | IPv4 | `IPv4`, `IPv6`, or `Both` |
| `SourceAddress` | null | Source address to bind (`-S`) |
| `InterfaceName` | null | Network interface to bind (`-I`, Linux only) |
| `StatsIntervalMs` | null | Periodic stats interval in ms (`-Q`) |

## License

[MIT](LICENSE)

This project includes code derived from [fping](https://github.com/schweikert/fping) (BSD-4-Clause). See [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES) for details.
