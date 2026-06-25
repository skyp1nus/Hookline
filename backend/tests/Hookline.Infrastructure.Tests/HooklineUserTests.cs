using Hookline.Infrastructure.Auth;
using Hookline.SharedKernel.Auth;

namespace Hookline.Infrastructure.Tests;

public sealed class HooklineUserTests
{
    [Fact]
    public void Anonymous_is_not_authenticated()
    {
        Assert.False(HooklineUser.Anonymous.IsAuthenticated);
        Assert.Null(HooklineUser.Anonymous.UserId);
        Assert.Null(HooklineUser.Anonymous.Role);
        Assert.False(HooklineUser.Anonymous.IsSystem);
    }

    [Fact]
    public void System_is_authenticated_with_owner_role()
    {
        Assert.True(HooklineUser.System.IsAuthenticated);
        Assert.Equal(UserRole.Owner, HooklineUser.System.Role);
        Assert.True(HooklineUser.System.IsSystem);
    }

    [Fact]
    public void Authenticated_factory_sets_all_fields()
    {
        var id = Guid.NewGuid();
        var user = HooklineUser.Authenticated(id, "user@example.com", UserRole.Admin);

        Assert.True(user.IsAuthenticated);
        Assert.Equal(id, user.UserId);
        Assert.Equal("user@example.com", user.Email);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.IsSystem);
    }

    [Theory]
    [InlineData(UserRole.Owner, UserRole.Owner, true)]
    [InlineData(UserRole.Owner, UserRole.Admin, true)]
    [InlineData(UserRole.Owner, UserRole.Member, true)]
    [InlineData(UserRole.Admin, UserRole.Admin, true)]
    [InlineData(UserRole.Admin, UserRole.Member, true)]
    [InlineData(UserRole.Admin, UserRole.Owner, false)]
    [InlineData(UserRole.Member, UserRole.Member, true)]
    [InlineData(UserRole.Member, UserRole.Admin, false)]
    [InlineData(UserRole.Member, UserRole.Owner, false)]
    public void HasAtLeast_compares_role_hierarchy(UserRole actual, UserRole required, bool expected)
    {
        var user = HooklineUser.Authenticated(Guid.NewGuid(), null, actual);

        Assert.Equal(expected, user.HasAtLeast(required));
    }

    [Fact]
    public void HasAtLeast_returns_false_for_anonymous()
    {
        Assert.False(HooklineUser.Anonymous.HasAtLeast(UserRole.Member));
    }
}
