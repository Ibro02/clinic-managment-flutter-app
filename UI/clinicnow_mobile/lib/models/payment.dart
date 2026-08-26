/// Mirrors the backend's `PaymentDto`.
class Payment {
  final int id;
  final int appointmentId;
  final double amountEur;
  final int status;
  final String statusName;
  final double refundedAmountEur;
  final double remainingRefundableEur;
  final DateTime createdAtUtc;
  final DateTime? paidAtUtc;
  final String? approveUrl;

  Payment({
    required this.id,
    required this.appointmentId,
    required this.amountEur,
    required this.status,
    required this.statusName,
    required this.refundedAmountEur,
    required this.remainingRefundableEur,
    required this.createdAtUtc,
    this.paidAtUtc,
    this.approveUrl,
  });

  factory Payment.fromJson(Map<String, dynamic> json) => Payment(
        id: json['id'] as int,
        appointmentId: json['appointmentId'] as int,
        amountEur: (json['amountEur'] as num).toDouble(),
        status: json['status'] as int,
        statusName: json['statusName'] as String,
        refundedAmountEur: (json['refundedAmountEur'] as num).toDouble(),
        remainingRefundableEur: (json['remainingRefundableEur'] as num).toDouble(),
        createdAtUtc: DateTime.parse(json['createdAtUtc'] as String).toLocal(),
        paidAtUtc: json['paidAtUtc'] == null ? null : DateTime.parse(json['paidAtUtc'] as String).toLocal(),
        approveUrl: json['approveUrl'] as String?,
      );
}
