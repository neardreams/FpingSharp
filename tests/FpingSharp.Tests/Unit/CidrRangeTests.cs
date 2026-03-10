using System;
using System.Linq;
using Xunit;

namespace FpingSharp.Tests.Unit;

public class CidrRangeTests
{
    [Fact]
    public void Test_Slash24_Returns254Addresses()
    {
        var addresses = CidrRange.Expand("192.168.1.0/24").ToList();

        Assert.Equal(254, addresses.Count);
        Assert.Equal("192.168.1.1", addresses[0]);
        Assert.Equal("192.168.1.254", addresses[253]);
        // Network (192.168.1.0) and broadcast (192.168.1.255) are excluded
        Assert.DoesNotContain("192.168.1.0", addresses);
        Assert.DoesNotContain("192.168.1.255", addresses);
    }

    [Fact]
    public void Test_Slash32_Returns1Address()
    {
        var addresses = CidrRange.Expand("10.0.0.5/32").ToList();

        Assert.Single(addresses);
        Assert.Equal("10.0.0.5", addresses[0]);
    }

    [Fact]
    public void Test_Slash31_Returns2Addresses()
    {
        // RFC 3021: point-to-point links
        var addresses = CidrRange.Expand("10.0.0.0/31").ToList();

        Assert.Equal(2, addresses.Count);
        Assert.Equal("10.0.0.0", addresses[0]);
        Assert.Equal("10.0.0.1", addresses[1]);
    }

    [Fact]
    public void Test_Slash16_Returns65534Addresses()
    {
        var addresses = CidrRange.Expand("172.16.0.0/16").ToList();

        Assert.Equal(65534, addresses.Count);
        Assert.Equal("172.16.0.1", addresses[0]);
        Assert.Equal("172.16.255.254", addresses[65533]);
    }

    [Fact]
    public void Test_RangeMode_SingleAddress()
    {
        var addresses = CidrRange.Expand("10.0.0.1", "10.0.0.1").ToList();

        Assert.Single(addresses);
        Assert.Equal("10.0.0.1", addresses[0]);
    }

    [Fact]
    public void Test_RangeMode_NormalRange()
    {
        var addresses = CidrRange.Expand("192.168.1.1", "192.168.1.5").ToList();

        Assert.Equal(5, addresses.Count);
        Assert.Equal("192.168.1.1", addresses[0]);
        Assert.Equal("192.168.1.2", addresses[1]);
        Assert.Equal("192.168.1.3", addresses[2]);
        Assert.Equal("192.168.1.4", addresses[3]);
        Assert.Equal("192.168.1.5", addresses[4]);
    }

    [Fact]
    public void Test_RangeMode_ReverseOrder_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("10.0.0.5", "10.0.0.1").ToList());
    }

    [Fact]
    public void Test_NoPrefixLength_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("192.168.1.0").ToList());
    }

    [Fact]
    public void Test_InvalidIp_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("999.999.999.999/24").ToList());
    }

    [Fact]
    public void Test_PrefixGreaterThan32_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("192.168.1.0/33").ToList());
    }

    [Fact]
    public void Test_NegativePrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("192.168.1.0/-1").ToList());
    }

    [Fact]
    public void Test_IPv6_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("::1/128").ToList());
    }

    [Fact]
    public void Test_NullInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CidrRange.Expand((string)null!).ToList());
    }

    [Fact]
    public void Test_RangeMode_NullStart_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CidrRange.Expand(null!, "10.0.0.1").ToList());
    }

    [Fact]
    public void Test_RangeMode_NullEnd_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CidrRange.Expand("10.0.0.1", null!).ToList());
    }

    [Fact]
    public void Test_RangeMode_InvalidStartIp_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("not_an_ip", "10.0.0.1").ToList());
    }

    [Fact]
    public void Test_RangeMode_IPv6_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            CidrRange.Expand("::1", "::2").ToList());
    }
}
