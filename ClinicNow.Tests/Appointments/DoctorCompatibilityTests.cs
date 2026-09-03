using ClinicNow.Services.Appointments;
using ClinicNow.Services.Database.Entities;

namespace ClinicNow.Tests.Appointments;

/// <summary>
/// The in-memory half of the doctor↔service rule (review item C2). The DB half
/// (<see cref="DoctorCompatibility.CanPerformAsync"/>) is the same predicate
/// expressed as a query and is covered by the live-API checks recorded in
/// GOALS.md; what matters here is that both halves agree on the rule, since the
/// recommender relies on this one to prune candidate pairings.
/// </summary>
public class DoctorCompatibilityTests
{
    private static Doctor DoctorWith(params int[] specializationIds) => new()
    {
        Id = 1,
        DoctorSpecializations = specializationIds
            .Select(id => new DoctorSpecialization { DoctorId = 1, SpecializationId = id })
            .ToList()
    };

    private static MedicalService ServiceIn(int specializationId) =>
        new() { Id = 1, Name = "Test", SpecializationId = specializationId };

    [Fact]
    public void CanPerform_WhenDoctorHoldsTheServicesSpecialization_IsTrue()
    {
        Assert.True(DoctorCompatibility.CanPerform(DoctorWith(2), ServiceIn(2)));
    }

    [Fact]
    public void CanPerform_WhenDoctorHoldsADifferentSpecialization_IsFalse()
    {
        Assert.False(DoctorCompatibility.CanPerform(DoctorWith(4), ServiceIn(2)));
    }

    /// <summary>A doctor may hold several specializations - holding any one of them qualifies.</summary>
    [Fact]
    public void CanPerform_WithMultipleSpecializations_MatchesOnAnyOfThem()
    {
        var doctor = DoctorWith(1, 2);

        Assert.True(DoctorCompatibility.CanPerform(doctor, ServiceIn(1)));
        Assert.True(DoctorCompatibility.CanPerform(doctor, ServiceIn(2)));
        Assert.False(DoctorCompatibility.CanPerform(doctor, ServiceIn(3)));
    }

    [Fact]
    public void CanPerform_WithNoSpecializations_IsFalse()
    {
        Assert.False(DoctorCompatibility.CanPerform(DoctorWith(), ServiceIn(1)));
    }
}
