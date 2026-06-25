using System.Net;

using Google;
using Google.Apis.Requests;

using Hookline.Modules.YouTubeComments.Infrastructure;

namespace Hookline.Modules.YouTubeComments.Tests;

public sealed class GoogleApiExceptionExtensionsTests
{
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void IsTransientStatus_classifies_correctly(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, GoogleApiExceptionExtensions.IsTransientStatus(status));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void IsTransient_extension_uses_HttpStatusCode(HttpStatusCode status, bool expected)
    {
        var ex = new GoogleApiException("test") { HttpStatusCode = status };

        Assert.Equal(expected, ex.IsTransient());
    }

    [Fact]
    public void HasReason_returns_true_when_reason_matches()
    {
        var ex = new GoogleApiException("test")
        {
            Error = new RequestError
            {
                Errors = [new SingleError { Reason = "quotaExceeded" }]
            }
        };

        Assert.True(ex.HasReason("quotaExceeded"));
    }

    [Fact]
    public void HasReason_is_case_insensitive()
    {
        var ex = new GoogleApiException("test")
        {
            Error = new RequestError
            {
                Errors = [new SingleError { Reason = "QUOTAEXCEEDED" }]
            }
        };

        Assert.True(ex.HasReason("quotaExceeded"));
    }

    [Fact]
    public void HasReason_returns_false_when_no_match()
    {
        var ex = new GoogleApiException("test")
        {
            Error = new RequestError
            {
                Errors = [new SingleError { Reason = "notFound" }]
            }
        };

        Assert.False(ex.HasReason("quotaExceeded"));
    }

    [Fact]
    public void HasReason_returns_false_when_errors_null()
    {
        var ex = new GoogleApiException("test");

        Assert.False(ex.HasReason("quotaExceeded"));
    }

    [Fact]
    public void HasReason_returns_false_when_error_property_null()
    {
        var ex = new GoogleApiException("test") { Error = null };

        Assert.False(ex.HasReason("quotaExceeded"));
    }
}
