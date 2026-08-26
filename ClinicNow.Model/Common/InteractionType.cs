namespace ClinicNow.Model.Common;

/// <summary>
/// The kind of real user action a <c>RecommenderInteraction</c> row records
/// (doc §3: "otvaranje detalja doktora ili usluge, pretraga"). Serialized as
/// an int on the wire, same convention as <see cref="AppointmentStatus"/>.
/// </summary>
public enum InteractionType
{
    DoctorView = 0,
    MedicalServiceView = 1,
    Search = 2
}
