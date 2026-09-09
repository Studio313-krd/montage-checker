using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using MontageMonitor.Server.Domain;

namespace MontageMonitor.Server.Infrastructure.Security;

public sealed class AgentCredentialService(TimeProvider timeProvider)
{
    public AgentCredentialData Create(Guid agentId)
    {
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(48));
        var now = timeProvider.GetUtcNow();
        var credential = new AgentCredential
        {
            AgentId = agentId,
            SecretHash = Hash(secret),
            ExpiresAtUtc = now.AddDays(365),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        return new AgentCredentialData(
            $"{credential.Id:N}.{secret}",
            credential);
    }

    public bool Verify(AgentCredential credential, string secret)
    {
        try
        {
            var expected = Convert.FromHexString(credential.SecretHash);
            var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record AgentCredentialData(string AccessToken, AgentCredential Entity);
