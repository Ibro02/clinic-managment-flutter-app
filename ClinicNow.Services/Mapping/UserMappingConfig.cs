using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

/// <summary>
/// <c>User -&gt; UserDto</c> needs one explicit rule Mapster's convention-based
/// mapping can't infer on its own: flattening the <c>UserRoles</c> join collection
/// down to plain role-name strings. Discovered automatically at startup via
/// <c>TypeAdapterConfig.GlobalSettings.Scan(...)</c> in <c>Program.cs</c> - Mapster's
/// own convention for auto-registering every <see cref="IRegister"/> in a scanned
/// assembly.
/// </summary>
public class UserMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<User, UserDto>()
            .Map(dest => dest.Roles, src => src.UserRoles.Select(ur => ur.Role.Name).ToList());
    }
}
