import 'package:flutter/material.dart';

import '../core/clinic_colors.dart';
import '../models/doctor.dart';

/// A doctor-picking dropdown: name (+ specializations, if any) with the
/// clinic they practice at shown smaller underneath, colored per-clinic
/// (rulebook Part II §K). Shared by the booking and reschedule flows so the
/// two fixes below live in exactly one place instead of being reproduced -
/// and potentially forgotten - a second time:
///
/// - `isExpanded: true` - without it, DropdownButtonFormField sizes its
///   internal row to the selected item's intrinsic width instead of the
///   width actually available, which overflowed once specializations made
///   the item text longer (review item C2).
/// - `selectedItemBuilder` - the closed field is a separate render path from
///   the open menu and Flutter caps its height at one text line regardless
///   of `itemHeight`, so the two-line option content that's fine in the open
///   menu overflows vertically in the closed field. This renders a
///   single-line, ellipsized name there instead.
class DoctorDropdownField extends StatelessWidget {
  final List<Doctor> doctors;
  final Doctor? value;
  final String hintText;
  final ValueChanged<Doctor?> onChanged;

  const DoctorDropdownField({
    super.key,
    required this.doctors,
    required this.value,
    required this.onChanged,
    this.hintText = 'Odaberite doktora',
  });

  @override
  Widget build(BuildContext context) {
    return DropdownButtonFormField<Doctor>(
      initialValue: value,
      decoration: InputDecoration(hintText: hintText),
      isExpanded: true,
      // null, not the default 48 - each item is two lines (name/
      // specializations + clinic), so a fixed single-line height would clip
      // the clinic subtext in the open menu.
      itemHeight: null,
      items: doctors.map((d) => DropdownMenuItem(value: d, child: _DoctorOption(doctor: d))).toList(),
      selectedItemBuilder: (context) =>
          doctors.map((d) => Text(d.fullName, maxLines: 1, overflow: TextOverflow.ellipsis)).toList(),
      onChanged: onChanged,
    );
  }
}

class _DoctorOption extends StatelessWidget {
  final Doctor doctor;

  const _DoctorOption({required this.doctor});

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          '${doctor.fullName}${doctor.specializations.isNotEmpty ? ' (${doctor.specializations.join(', ')})' : ''}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        Text(
          doctor.locationName,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(fontSize: 12, color: clinicColor(doctor.locationId, Theme.of(context).brightness)),
        ),
      ],
    );
  }
}
