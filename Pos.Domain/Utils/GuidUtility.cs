using System;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Domain.Utils
{
    public static class GuidUtility
    {
        /// <summary>
        /// Creates a deterministic Guid from any input string
        /// (useful for generating stable keys for composite entities in sync/outbox).
        /// </summary>
        public static Guid FromString(string input)
        {
            using var provider = MD5.Create();
            byte[] hash = provider.ComputeHash(Encoding.UTF8.GetBytes(input));
            return new Guid(hash);
        }
    }
}
