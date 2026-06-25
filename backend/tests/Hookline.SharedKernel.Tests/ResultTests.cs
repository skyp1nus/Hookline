using Hookline.SharedKernel.Common;

namespace Hookline.SharedKernel.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_has_no_error()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_carries_the_error()
    {
        var error = Error.Validation("bad input");
        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Implicit_conversion_from_Error_produces_failure()
    {
        Result result = Error.NotFound;

        Assert.False(result.IsSuccess);
        Assert.Equal(Error.NotFound, result.Error);
    }

    [Fact]
    public void Generic_Success_carries_the_value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Generic_Failure_has_default_value()
    {
        var error = Error.Unauthorized;
        var result = Result<string>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public void Generic_implicit_conversion_from_value()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Generic_implicit_conversion_from_error()
    {
        Result<int> result = Error.Forbidden;

        Assert.False(result.IsSuccess);
        Assert.Equal(Error.Forbidden, result.Error);
    }

    [Fact]
    public void Error_factory_Conflict_uses_409()
    {
        var error = Error.Conflict("duplicate");

        Assert.Equal("conflict", error.Code);
        Assert.Equal("duplicate", error.Message);
        Assert.Equal(409, error.Status);
    }

    [Fact]
    public void Error_factory_Validation_uses_400()
    {
        var error = Error.Validation("oops");

        Assert.Equal("validation", error.Code);
        Assert.Equal(400, error.Status);
    }

    [Fact]
    public void Predefined_errors_have_correct_status_codes()
    {
        Assert.Equal(404, Error.NotFound.Status);
        Assert.Equal(401, Error.Unauthorized.Status);
        Assert.Equal(403, Error.Forbidden.Status);
    }
}
