using Xunit;

namespace FpingSharp.Tests.Unit;

public class FpingOptionsTests
{
    [Fact]
    public void Test_Default_Values()
    {
        var options = new FpingOptions();

        Assert.Equal(1, options.Count);
        Assert.False(options.Loop);
        Assert.Equal(10, options.IntervalMs);
        Assert.Equal(1000, options.PerHostIntervalMs);
        Assert.Equal(500, options.TimeoutMs);
        Assert.Equal(3, options.Retry);
        Assert.Equal(1.5, options.Backoff);
        Assert.Equal(56, options.PacketSize);
        Assert.Null(options.Ttl);
        Assert.Null(options.Tos);
        Assert.False(options.DontFragment);
        Assert.Equal(FpingAddressFamily.IPv4, options.AddressFamily);
    }

    [Fact]
    public void Test_Validate_ValidOptions_NoException()
    {
        var options = new FpingOptions();

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Test_Validate_CountZero_NonLoop_Throws()
    {
        var options = new FpingOptions { Count = 0, Loop = false };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void Test_Validate_CountZero_Loop_DoesNotThrow()
    {
        var options = new FpingOptions { Count = 0, Loop = true };

        var exception = Record.Exception(() => options.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Test_Validate_NegativeInterval_Throws()
    {
        var options = new FpingOptions { IntervalMs = -1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void Test_Validate_BackoffLessThan1_Throws()
    {
        var options = new FpingOptions { Backoff = 0.5 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void Test_Validate_PacketSizeTooLarge_Throws()
    {
        var options = new FpingOptions { PacketSize = 70000 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public void Test_Validate_TtlOutOfRange_Throws(int ttl)
    {
        var options = new FpingOptions { Ttl = ttl };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void Test_Validate_TosOutOfRange_Throws(int tos)
    {
        var options = new FpingOptions { Tos = tos };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    // --- SourceAddress tests ---

    [Fact]
    public void Test_SourceAddress_Default_IsNull()
    {
        var options = new FpingOptions();
        Assert.Null(options.SourceAddress);
    }

    [Fact]
    public void Test_SourceAddress_ValidIp_DoesNotThrow()
    {
        var options = new FpingOptions { SourceAddress = "192.168.1.100" };
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Test_SourceAddress_InvalidString_Throws()
    {
        var options = new FpingOptions { SourceAddress = "not_an_ip" };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    // --- InterfaceName tests ---

    [Fact]
    public void Test_InterfaceName_Default_IsNull()
    {
        var options = new FpingOptions();
        Assert.Null(options.InterfaceName);
    }

    [Fact]
    public void Test_InterfaceName_ValidName_DoesNotThrow()
    {
        var options = new FpingOptions { InterfaceName = "eth0" };
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Test_InterfaceName_EmptyString_Throws()
    {
        var options = new FpingOptions { InterfaceName = "" };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    [Fact]
    public void Test_InterfaceName_WhitespaceOnly_Throws()
    {
        var options = new FpingOptions { InterfaceName = "   " };
        Assert.Throws<ArgumentException>(() => options.Validate());
    }

    // --- StatsIntervalMs tests ---

    [Fact]
    public void Test_StatsIntervalMs_Default_IsNull()
    {
        var options = new FpingOptions();
        Assert.Null(options.StatsIntervalMs);
    }

    [Fact]
    public void Test_StatsIntervalMs_PositiveValue_DoesNotThrow()
    {
        var options = new FpingOptions { StatsIntervalMs = 1000 };
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Test_StatsIntervalMs_Zero_DoesNotThrow()
    {
        var options = new FpingOptions { StatsIntervalMs = 0 };
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }

    [Fact]
    public void Test_StatsIntervalMs_Negative_Throws()
    {
        var options = new FpingOptions { StatsIntervalMs = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
