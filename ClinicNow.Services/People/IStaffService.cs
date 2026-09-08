using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;

namespace ClinicNow.Services.People;

/// <summary>Plain CRUD over Staff-role <c>User</c> rows - no extra operations needed beyond the generic shape.</summary>
public interface IStaffService : ICRUDService<UserDto, StaffSearchObject, StaffInsertRequest, StaffUpdateRequest>
{
}
