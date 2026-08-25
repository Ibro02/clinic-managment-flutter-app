/// Mirrors the backend's `ClinicNow.Model.Common.Gender` enum (int-serialized: Male=0, Female=1, Other=2).
enum Gender {
  male,
  female,
  other;

  static Gender fromInt(int value) => Gender.values[value];

  int toInt() => index;

  String get label => switch (this) {
        Gender.male => 'Muško',
        Gender.female => 'Žensko',
        Gender.other => 'Ostalo',
      };
}
