import 'package:clinicnow_desktop/models/appointment.dart';
import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:flutter_test/flutter_test.dart';

/// One appointment payload, parsed twice, is what the referral / lab-finding
/// dialogs actually deal with: the caller hands them an `Appointment` from the
/// appointments grid, while the dialog's dropdown items come from its own
/// fetch. Both describe the same row.
Map<String, dynamic> _payload({int id = 7}) => {
      'id': id,
      'patientId': 3,
      'patientName': 'Amina Hodžić',
      'doctorId': 5,
      'doctorName': 'Dr. Emir Begić',
      'medicalServiceId': 2,
      'medicalServiceName': 'Kardiološki pregled',
      'locationId': 1,
      'locationName': 'Centrala',
      'startUtc': '2026-09-09T08:00:00Z',
      'endUtc': '2026-09-09T08:30:00Z',
      'status': 2,
      'statusName': 'Završen',
      'createdAtUtc': '2026-09-01T10:00:00Z',
      'allowedActions': <String>[],
    };

void main() {
  test('two parses of the same appointment are the same value', () {
    final fromGrid = Appointment.fromJson(_payload());
    final fromDialogFetch = Appointment.fromJson(_payload());

    expect(fromGrid, equals(fromDialogFetch));
    expect(fromGrid.hashCode, equals(fromDialogFetch.hashCode));
    expect(fromGrid, isNot(equals(Appointment.fromJson(_payload(id: 8)))));
  });

  testWidgets(
      'a pre-filled appointment survives a rebuild of the dialog around it',
      (WidgetTester tester) async {
    // The value the caller pre-fills the form with (ReferralsScreen /
    // LabFindingsScreen `initialAppointment`).
    final preFilled = Appointment.fromJson(_payload());
    // The same row as the dropdown re-fetched it.
    final options = [Appointment.fromJson(_payload()), Appointment.fromJson(_payload(id: 8))];

    final formKey = GlobalKey<FormBuilderState>();
    var rebuilds = 0;

    await tester.pumpWidget(MaterialApp(
      home: Scaffold(
        body: StatefulBuilder(
          builder: (context, setState) => FormBuilder(
            key: formKey,
            initialValue: {'appointment': preFilled},
            child: Column(
              children: [
                FormBuilderDropdown<Appointment>(
                  name: 'appointment',
                  enabled: false,
                  items: options
                      .map((a) => DropdownMenuItem(value: a, child: Text(a.medicalServiceName)))
                      .toList(),
                ),
                // Stands in for the specialization dropdown: picking one calls
                // setState on the dialog, which is what rebuilds the field
                // above and runs its `didUpdateWidget` check.
                TextButton(
                  onPressed: () => setState(() => rebuilds++),
                  child: Text('rebuild $rebuilds'),
                ),
              ],
            ),
          ),
        ),
      ),
    ));

    await tester.tap(find.byType(TextButton));
    await tester.pumpAndSettle();

    // No assertion from FormBuilderDropdown.didUpdateWidget...
    expect(tester.takeException(), isNull);
    // ...and the locked field still carries the appointment the caller chose,
    // instead of being silently reset to null (which then fails validation on
    // save, with no way for the user to re-pick a disabled field).
    expect(formKey.currentState!.fields['appointment']!.value, equals(preFilled));
  });
}
