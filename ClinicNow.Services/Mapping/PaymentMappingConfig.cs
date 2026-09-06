using ClinicNow.Model.Common;
using ClinicNow.Model.Dto;
using ClinicNow.Services.Database.Entities;
using Mapster;

namespace ClinicNow.Services.Mapping;

public class PaymentMappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Payment, PaymentDto>()
            .Map(dest => dest.StatusName, src => src.Status.ToDisplayName())
            .Map(dest => dest.RefundedAmountEur, src => src.Refunds.Sum(r => r.AmountEur))
            // Refundable against what was actually captured, not what was
            // ordered (review item C13a) - the two only differ on a payment
            // flagged RequiresReconciliation, and that is exactly the one where
            // showing staff the wrong ceiling would cost money.
            .Map(dest => dest.RemainingRefundableEur,
                src => (src.CapturedAmountEur ?? src.AmountEur) - src.Refunds.Sum(r => r.AmountEur))
            // `!` because Mapster's Ignore takes Expression<Func<TDest, object>>
            // and `ApproveUrl` is `string?` - boxing it would otherwise trip CS8603.
            .Ignore(dest => dest.ApproveUrl!);
    }
}
