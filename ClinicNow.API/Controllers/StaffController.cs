using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Model.Requests;
using ClinicNow.Model.SearchObjects;
using ClinicNow.Model.Security;
using ClinicNow.Services.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicNow.API.Controllers;

/// <summary>
/// Staff (front-desk/administrative) accounts. Administrator-only on every
/// action, read included - unlike Doctor/Patient (which Staff itself may
/// manage), granting Staff accounts the ability to list or manage their own
/// colleagues was not asked for and would be a privilege-escalation surface,
/// so every action here is overridden the same way <see cref="DoctorController"/>
/// overrides its write actions.
/// </summary>
public class StaffController : BaseCRUDController<UserDto, StaffSearchObject, StaffInsertRequest, StaffUpdateRequest>
{
    public StaffController(IStaffService service) : base(service)
    {
    }

    [Authorize(Roles = Roles.Administrator)]
    public override Task<ActionResult<PagedResult<UserDto>>> GetPaged(StaffSearchObject search, CancellationToken cancellationToken) =>
        base.GetPaged(search, cancellationToken);

    [Authorize(Roles = Roles.Administrator)]
    public override Task<ActionResult<UserDto>> GetById(int id, CancellationToken cancellationToken) =>
        base.GetById(id, cancellationToken);

    [Authorize(Roles = Roles.Administrator)]
    public override Task<ActionResult<UserDto>> Insert(StaffInsertRequest request, CancellationToken cancellationToken) =>
        base.Insert(request, cancellationToken);

    [Authorize(Roles = Roles.Administrator)]
    public override Task<ActionResult<UserDto>> Update(int id, StaffUpdateRequest request, CancellationToken cancellationToken) =>
        base.Update(id, request, cancellationToken);

    [Authorize(Roles = Roles.Administrator)]
    public override Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        base.Delete(id, cancellationToken);
}
