using TrackFlow.Services;
using Xunit;

namespace TrackFlow.Tests;

public class DccAccessoryAddressValidatorTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2048, true)]
    [InlineData(-1, false)]
    [InlineData(2049, false)]
    public void IsValid_ReturnsExpected(int value, bool expected)
    {
        Assert.Equal(expected, DccAccessoryAddressValidator.IsValid(value));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2048, true)]
    [InlineData(-1, false)]
    [InlineData(2049, false)]
    public void IsAssigned_ReturnsExpected(int value, bool expected)
    {
        Assert.Equal(expected, DccAccessoryAddressValidator.IsAssigned(value));
    }

    [Fact]
    public void GetValidationError_InvalidValue_ReturnsMessage()
    {
        Assert.Equal(
            DccAccessoryAddressValidator.ValidationErrorText,
            DccAccessoryAddressValidator.GetValidationError(9999));
    }

    [Fact]
    public void GetValidationError_ValidValue_ReturnsEmpty()
    {
        Assert.Empty(DccAccessoryAddressValidator.GetValidationError(120));
    }
}

