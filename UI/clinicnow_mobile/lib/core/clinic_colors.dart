import 'package:flutter/material.dart';

/// A fixed palette cycled by `Location.id` so the same clinic always gets the
/// same color everywhere a doctor's clinic is shown - a purely visual aid to
/// tell doctors at different clinics apart at a glance (e.g. in a doctor
/// picker dropdown), not a data attribute of its own.
const List<Color> _clinicPalette = [
  Colors.indigo,
  Colors.teal,
  Colors.deepOrange,
  Colors.purple,
  Colors.green,
  Colors.blue,
  Colors.brown,
  Colors.pink,
];

Color clinicColor(int locationId) => _clinicPalette[locationId % _clinicPalette.length];
