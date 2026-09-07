using ClinicNow.Model.Common;
using ClinicNow.Model.Configuration;
using ClinicNow.Model.Security;
using ClinicNow.Services.Appointments;
using ClinicNow.Services.Database;
using ClinicNow.Services.Database.Entities;
using ClinicNow.Services.Recommender;
using ClinicNow.Tests.Appointments;
using ClinicNow.Tests.TestSupport;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNow.Tests.Recommender;

/// <summary>
/// Review item C19: the recommender used to call
/// <c>IAppointmentService.GetAvailableSlotsAsync</c> inside a doctor x service x
/// day triple loop - several queries per call - and the popularity fallback
/// looped it too. It now batch-loads working hours, schedule blocks and busy
/// appointments once for the whole lookahead and evaluates every pair in memory
/// through the shared <see cref="SlotGeneration"/>.
///
/// The risk in that change is not the arithmetic - it is that the batched
/// loader could disagree with the per-call path (a wrong window bound, a missed
/// day-of-week filter, blocks dropped). So the tests here assert *equivalence*
/// against the very method the recommender no longer calls, rather than
/// re-asserting the slot maths.
/// </summary>
public class RecommenderSlotBatchingTests
{
    private const int PatientUserId = 4; // seeded patient@clinicnow.test -> Patient 1

    private static RecommenderService BuildRecommender(ClinicNowContext context) =>
        new(context,
            TestContextFactory.CreateHttpContextAccessor(PatientUserId, Roles.Patient),
            new MemoryCache(new MemoryCacheOptions()),
            new RecommenderOptions(),
            NullLogger<RecommenderService>.Instance);

    private static AppointmentService BuildAppointments(ClinicNowContext context) =>
        new(context,
            TestContextFactory.CreateMapper(),
            new ServiceCollection().BuildServiceProvider(),
            TestContextFactory.CreateHttpContextAccessor(PatientUserId, Roles.Patient),
            new ThrowingNotificationService(),
            new ThrowingEmailPublisher(),
            new ThrowingPaymentService());

    /// <summary>
    /// The suggestion must be a slot the booking endpoint would actually accept -
    /// the whole point of reusing the real availability rule rather than guessing.
    /// </summary>
    [Fact]
    public async Task EverySuggestedSlotIsOneGetAvailableSlotsWouldReturn()
    {
        await using var context = TestContextFactory.CreateContext();
        var recommendations = await BuildRecommender(context).GetRecommendationsAsync();
        var appointments = BuildAppointments(context);

        Assert.NotEmpty(recommendations);

        foreach (var recommendation in recommendations)
        {
            var localDate = ClinicTimeZone.LocalDateOf(recommendation.SuggestedStartUtc);
            var slots = await appointments.GetAvailableSlotsAsync(
                recommendation.DoctorId, recommendation.MedicalServiceId, localDate);

            Assert.Contains(recommendation.SuggestedStartUtc, slots);
        }
    }

    /// <summary>
    /// The DTO promises the *earliest* free slot, and the old code got there by
    /// breaking out of the day loop on the first hit. The batched version must
    /// still skip no earlier day.
    /// </summary>
    [Fact]
    public async Task SuggestedSlotIsTheEarliestInTheLookahead()
    {
        await using var context = TestContextFactory.CreateContext();
        var recommendations = await BuildRecommender(context).GetRecommendationsAsync();
        var appointments = BuildAppointments(context);
        var lookahead = new RecommenderOptions().CandidateLookaheadDays;

        Assert.NotEmpty(recommendations);

        foreach (var recommendation in recommendations)
        {
            var firstLocalDate = ClinicTimeZone.LocalDateOf(DateTime.UtcNow);
            var suggestedDate = ClinicTimeZone.LocalDateOf(recommendation.SuggestedStartUtc);

            for (var date = firstLocalDate; date < suggestedDate; date = date.AddDays(1))
            {
                var earlierSlots = await appointments.GetAvailableSlotsAsync(
                    recommendation.DoctorId, recommendation.MedicalServiceId, date);
                Assert.Empty(earlierSlots);
            }

            // And nothing earlier on the suggested day itself.
            var sameDay = await appointments.GetAvailableSlotsAsync(
                recommendation.DoctorId, recommendation.MedicalServiceId, suggestedDate);
            Assert.Equal(sameDay.Min(), recommendation.SuggestedStartUtc);
            Assert.True(suggestedDate < firstLocalDate.AddDays(lookahead));
        }
    }

    /// <summary>
    /// Blocks are the part most easily lost in a batch load - they are now
    /// fetched once for the window instead of once per day. Block every doctor
    /// across the whole lookahead and nothing may be recommended.
    /// </summary>
    [Fact]
    public async Task ScheduleBlocksCoveringTheWindowSuppressEveryRecommendation()
    {
        await using var context = TestContextFactory.CreateContext();
        var lookahead = new RecommenderOptions().CandidateLookaheadDays;
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(lookahead + 1);

        foreach (var doctorId in context.Doctors.Select(d => d.Id).ToList())
        {
            context.ScheduleBlocks.Add(new ScheduleBlock
            {
                DoctorId = doctorId,
                StartUtc = from,
                EndUtc = to,
                Reason = "Godišnji odmor"
            });
        }
        await context.SaveChangesAsync();

        var recommendations = await BuildRecommender(context).GetRecommendationsAsync();

        Assert.Empty(recommendations);
    }
}
