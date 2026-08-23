using System.Security.Claims;
using Server.Services;

namespace Server.Tests.Services;

public class AuthPrincipalFactoryTests
{
    [Fact]
    public void Create_ContainsRoleAndStudentIdentityClaims()
    {
        var result = new LoginResult(AccountRole.Student, 42, "Test Student", "student42", "STU-42");

        var principal = AuthPrincipalFactory.Create(result, "LAB-PC-42", isClientAgent: true);

        Assert.Equal("42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Student", principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("STU-42", principal.FindFirstValue(AuthPrincipalFactory.StudentNumberClaim));
        Assert.Equal("LAB-PC-42", principal.FindFirstValue(AuthPrincipalFactory.PcNameClaim));
        Assert.Equal(bool.TrueString, principal.FindFirstValue(AuthPrincipalFactory.ClientAgentClaim));
    }
}
