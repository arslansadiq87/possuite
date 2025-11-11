// Pos.Persistence/Services/UserReadService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Pos.Domain.Entities;
using Pos.Domain.Services;
using Pos.Persistence;

namespace Pos.Persistence.Services
{
    public sealed class UserReadService : IUserReadService
    {
        private readonly IDbContextFactory<PosClientDbContext> _dbf;
        public UserReadService(IDbContextFactory<PosClientDbContext> dbf) => _dbf = dbf;

        public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("Username is required.");

            await using var db = await _dbf.CreateDbContextAsync(ct);
            return await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username, ct);
        }
    }
}
