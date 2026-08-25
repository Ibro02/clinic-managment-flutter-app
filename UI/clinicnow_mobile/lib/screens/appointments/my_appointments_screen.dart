import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/appointment.dart';
import '../../providers/appointment_provider.dart';
import 'appointment_detail_screen.dart';
import 'book_appointment_screen.dart';

/// "My appointments" master-detail (PLAN.md Phase 4 item 6). The list is
/// already scoped server-side to the caller's own appointments (no `patientId`
/// query param needed - the backend resolves it from the JWT, rulebook §5), so
/// this screen only has to render whatever it gets back.
class MyAppointmentsScreen extends StatefulWidget {
  const MyAppointmentsScreen({super.key});

  @override
  State<MyAppointmentsScreen> createState() => _MyAppointmentsScreenState();
}

class _MyAppointmentsScreenState extends State<MyAppointmentsScreen> {
  static final _dateTimeFormat = DateFormat('dd.MM.yyyy HH:mm');

  late final AppointmentProvider _provider;
  bool _isLoading = true;
  String? _error;
  List<Appointment> _appointments = [];

  @override
  void initState() {
    super.initState();
    _provider = AppointmentProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });
    try {
      final result = await _provider.getPaged({'pageSize': 50, 'orderBy': 'StartUtc', 'sortDirection': 'desc'});
      if (!mounted) return;
      setState(() => _appointments = result.resultList);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  Future<void> _openBooking() async {
    final booked = await Navigator.of(context).push<bool>(
      MaterialPageRoute(builder: (_) => const BookAppointmentScreen()),
    );
    if (booked == true) _load();
  }

  Future<void> _openDetail(Appointment appointment) async {
    await Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => AppointmentDetailScreen(appointment: appointment, provider: _provider)),
    );
    _load(); // refresh in case it was cancelled in the detail screen
  }

  Color _statusColor(int status) => switch (status) {
        0 => Colors.orange,
        1 => Colors.blue,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: RefreshIndicator(
        onRefresh: _load,
        child: _isLoading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
                ? Center(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)))
                : _appointments.isEmpty
                    ? ListView(
                        // ListView (not Center) so pull-to-refresh still works on an empty list.
                        children: const [
                          Padding(
                            padding: EdgeInsets.all(32),
                            child: Text('Nemate zakazanih termina.', textAlign: TextAlign.center),
                          ),
                        ],
                      )
                    : ListView.builder(
                        itemCount: _appointments.length,
                        itemBuilder: (context, index) {
                          final appointment = _appointments[index];
                          return ListTile(
                            leading: CircleAvatar(
                              backgroundColor: _statusColor(appointment.status),
                              child: const Icon(Icons.event, color: Colors.white),
                            ),
                            title: Text('${appointment.doctorName} — ${appointment.medicalServiceName}'),
                            subtitle: Text('${_dateTimeFormat.format(appointment.startUtc)} · ${appointment.locationName}'),
                            trailing: Chip(
                              label: Text(appointment.statusName, style: const TextStyle(fontSize: 12)),
                              visualDensity: VisualDensity.compact,
                            ),
                            onTap: () => _openDetail(appointment),
                          );
                        },
                      ),
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _openBooking,
        icon: const Icon(Icons.add),
        label: const Text('Zakaži termin'),
      ),
    );
  }
}
