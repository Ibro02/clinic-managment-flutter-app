import 'package:flutter/material.dart';

/// A fixed palette cycled by `Location.id` so the same clinic always gets the
/// same color everywhere a doctor's clinic is shown - a purely visual aid to
/// tell doctors at different clinics apart at a glance (e.g. in a doctor
/// picker dropdown), not a data attribute of its own.
const List<Color> _clinicPaletteLight = [
  Colors.indigo,
  Colors.teal,
  Colors.deepOrange,
  Colors.purple,
  Colors.green,
  Colors.blue,
  Colors.brown,
  Colors.pink,
];

// Lighter variants for legibility as small text on a dark surface - the
// light-mode palette's mid-tones (e.g. Colors.indigo, Colors.brown,
// Colors.purple) fail contrast at 12px against a near-black background.
const List<Color> _clinicPaletteDark = [
  Colors.indigoAccent,
  Colors.tealAccent,
  Colors.deepOrangeAccent,
  Colors.purpleAccent,
  Colors.greenAccent,
  Colors.lightBlueAccent,
  Colors.brown,
  Colors.pinkAccent,
];

Color clinicColor(int locationId, Brightness brightness) {
  final palette = brightness == Brightness.dark ? _clinicPaletteDark : _clinicPaletteLight;
  return palette[locationId % palette.length];
}
