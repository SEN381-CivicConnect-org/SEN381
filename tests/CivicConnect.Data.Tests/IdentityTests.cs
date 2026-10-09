using CivicConnect.Data.Configurations;
using CivicConnect.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CivicConnect.Data.Tests;

/// Covers create user, grant role, add to team, remove from team, read effective roles.
[Collection("Database")]
public sealed class IdentityTests
{
    private readonly CivicConnectDatabaseFixture _fixture;

    public IdentityTests(CivicConnectDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateUser_StoresEmailFullNameAndPasswordHash()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var user = new AppUser
        {
            Email = $"ann.{Guid.NewGuid():N}@example.com",
            FullName = "Ann Administrator",
            PasswordHash = "hashed-password",
        };

        context.AppUsers.Add(user);
        await context.SaveChangesAsync();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.True(user.IsActive);

        var reloaded = await context.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(user.Email, reloaded.Email);
        Assert.Equal("Ann Administrator", reloaded.FullName);
        Assert.Equal("hashed-password", reloaded.PasswordHash);
    }

    [Fact]
    public async Task GrantRole_RecordsGrantedByAndGrantedAt()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var admin = await CreateUserAsync(context, "admin");
        var technician = await CreateUserAsync(context, "tech");

        var grant = new UserRole
        {
            UserId = technician.Id,
            RoleId = RoleId("technician"),
            GrantedByUserId = admin.Id,
        };

        context.UserRoles.Add(grant);
        await context.SaveChangesAsync();

        Assert.NotEqual(default, grant.GrantedAt);

        var reloaded = await context.UserRoles
            .AsNoTracking()
            .Include(ur => ur.Role)
            .SingleAsync(ur => ur.UserId == technician.Id);

        Assert.Equal("technician", reloaded.Role.Name);
        Assert.Equal(admin.Id, reloaded.GrantedByUserId);
    }

    [Fact]
    public async Task AddUserToTeam_CreatesActiveMembership()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var user = await CreateUserAsync(context, "member");
        var team = await CreateTeamAsync(context, "Facilities");

        var membership = new TeamMember
        {
            UserId = user.Id,
            ServiceTeamId = team.Id,
            TeamRoleId = TeamRoleId("technician"),
        };

        context.TeamMembers.Add(membership);
        await context.SaveChangesAsync();

        Assert.Null(membership.LeftAt);
        Assert.NotEqual(default, membership.JoinedAt);
    }

    [Fact]
    public async Task RemoveUserFromTeam_SetsLeftAtAndKeepsRow()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var user = await CreateUserAsync(context, "leaver");
        var team = await CreateTeamAsync(context, "IT Support");

        var membership = new TeamMember
        {
            UserId = user.Id,
            ServiceTeamId = team.Id,
            TeamRoleId = TeamRoleId("supervisor"),
        };
        context.TeamMembers.Add(membership);
        await context.SaveChangesAsync();

        membership.LeftAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync();

        var row = await context.TeamMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(tm => tm.UserId == user.Id && tm.ServiceTeamId == team.Id);

        Assert.NotNull(row);
        Assert.NotNull(row!.LeftAt);
    }

    [Fact]
    public async Task ReadEffectiveRoles_ReturnsAllGrantedRoles()
    {
        await using var context = _fixture.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var admin = await CreateUserAsync(context, "admin2");
        var user = await CreateUserAsync(context, "multi-role");

        context.UserRoles.AddRange(
            new UserRole { UserId = user.Id, RoleId = RoleId("reporter"), GrantedByUserId = admin.Id },
            new UserRole { UserId = user.Id, RoleId = RoleId("technician"), GrantedByUserId = admin.Id });
        await context.SaveChangesAsync();

        var effectiveRoles = await context.AppUsers
            .AsNoTracking()
            .Where(u => u.Id == user.Id)
            .SelectMany(u => u.UserRoles.Select(ur => ur.Role.Name))
            .ToListAsync();

        Assert.Equal(new[] { "reporter", "technician" }, effectiveRoles.OrderBy(name => name));
    }

    private static short RoleId(string name) =>
        RoleConfiguration.BaselineRoles.Single(r => r.Name == name).Id;

    private static short TeamRoleId(string name) =>
        TeamRoleConfiguration.BaselineTeamRoles.Single(r => r.Name == name).Id;

    private static async Task<AppUser> CreateUserAsync(CivicConnectDbContext context, string label)
    {
        var user = new AppUser
        {
            Email = $"{label}.{Guid.NewGuid():N}@example.com",
            FullName = label,
            PasswordHash = "hashed-password",
        };

        context.AppUsers.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<ServiceTeam> CreateTeamAsync(CivicConnectDbContext context, string namePrefix)
    {
        var team = new ServiceTeam
        {
            Name = $"{namePrefix} {Guid.NewGuid():N}",
        };

        context.ServiceTeams.Add(team);
        await context.SaveChangesAsync();
        return team;
    }
}
