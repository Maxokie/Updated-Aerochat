using Aerochat.Helpers;

namespace Aerotest;

public class CrashReportRedactorTests
{
    [Test]
    public void Sanitize_Null_ReturnsEmpty()
    {
        Assert.That(CrashReportRedactor.Sanitize(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Sanitize_AuthorizationHeader_IsRedacted()
    {
        const string input = "GET /x\r\nAuthorization: Bearer secret-token-here\r\n";
        string s = CrashReportRedactor.Sanitize(input);
        Assert.That(s, Does.Not.Contain("secret-token"));
        Assert.That(s, Does.Contain("[redacted: authorization header]"));
    }

    [Test]
    public void Sanitize_AccessTokenQuery_IsRedacted()
    {
        const string input = "https://example.com/callback?access_token=abc123&foo=1";
        string s = CrashReportRedactor.Sanitize(input);
        Assert.That(s, Does.Not.Contain("abc123"));
        Assert.That(s, Does.Contain("access_token=[redacted]"));
    }

    [Test]
    public void Sanitize_DiscordTokenLike_IsRedacted()
    {
        const string input = "token eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XjsL6nUE51uWmkPrv0Co";
        string s = CrashReportRedactor.Sanitize(input);
        Assert.That(s, Does.Not.Contain("dozjgNryP4J3jVmNHl0w5N_XjsL6nUE51uWmkPrv0Co"));
        Assert.That(s, Does.Contain("[redacted:token]"));
    }
}
