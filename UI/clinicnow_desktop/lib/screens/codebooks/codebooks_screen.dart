import 'package:flutter/material.dart';

import 'appointment_status_screen.dart';
import 'city_screen.dart';
import 'location_screen.dart';
import 'medical_service_screen.dart';
import 'specialization_screen.dart';

/// One screen, five tabs - the four editable codebooks (Phase 2) plus the
/// read-only "Statusi termina" tab (review item S2) share this container
/// rather than each claiming their own top-level nav destination, so the
/// staff nav rail stays uncluttered as later phases add Patients/Doctors/
/// Appointments/Reports.
class CodebooksScreen extends StatelessWidget {
  const CodebooksScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 5,
      child: Column(
        children: [
          const Material(
            color: Colors.transparent,
            child: TabBar(
              isScrollable: true,
              tabs: [
                Tab(text: 'Gradovi'),
                Tab(text: 'Specijalizacije'),
                Tab(text: 'Lokacije'),
                Tab(text: 'Usluge'),
                Tab(text: 'Statusi termina'),
              ],
            ),
          ),
          const Expanded(
            child: TabBarView(
              children: [
                CityScreen(),
                SpecializationScreen(),
                LocationScreen(),
                MedicalServiceScreen(),
                AppointmentStatusScreen(),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
