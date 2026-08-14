using System.Security.Cryptography;
using DevForge.Application.Contracts;

namespace DevForge.Infrastructure.Publication;

public sealed class CryptographicPublicationNonceGenerator : IPublicationNonceGenerator
{
    public string CreateOwnershipNonce() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
