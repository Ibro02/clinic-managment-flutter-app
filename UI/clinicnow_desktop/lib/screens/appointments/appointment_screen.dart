import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/clinic_colors.dart';
import '../../core/roles.dart';
import '../../models/appointment.dart';
import '../../models/doctor.dart';
import '../../models/patient.dart';
import '../../providers/appointment_provider.dart';
import '../../providers/doctor_provider.dart';
import '../../providers/patient_provider.dart';
import 'schedule_appointment_dialog.dart';

/// Staff/doctor appointment management: list with ≥1 search param (patient AND
/// doctor filters, plus status), and the full Confirm/Complete/Cancel lifecycle
/// with confirmation dialogs - buttons for actions the current status doesn't
/// allow are simply not rendered, driven entirely by the server's own
/// `allowedActions` (PLAN.md Phase 4 item 5, rulebook Part II §K).
class AppointmentScreen extends StatefulWidget {
  const AppointmentScreen({super.key});

  @override
  State<AppointmentScreen> createState() => _AppointmentScreenState();
}

class _AppointmentScreenState extends State<AppointmentScreen> {
  static const int _pageSize = 10;
  static final _dateTimeFormat = DateFormat('dd.MM.yyyy HH:mm');

  late final AppointmentProvider _appointmentProvider;
  late final PatientProvider _patientProvider;
  late final DoctorProvider _doctorProvider;

  int _page = 1;
  bool _isLoading = true;
  String? _error;
  List<Appointment> _appointments = [];
  int _count = 0;

  int? _filterPatientId;
  int? _filterDoctorId;
  int? _filterStatus;

  List<Patient> _patients = [];
  List<Doctor> _doctors = [];

  static const _statusOptions = [
    (0, 'Na čekanju'),
    (1, 'Potvrđen'),
    (2, 'Završen'),
    (3, 'Otkazan'),
  ];

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _appointmentProvider = AppointmentProvider(authSession);
    _patientProvider = PatientProvider(authSession);
    _doctorProvider = DoctorProvider(authSession);
    _loadFilterOptions();
    _load();
  }

  Future<void> _loadFilterOptions() async {
    final patients = await _patientProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'});
    final doctors = await _doctorProvider.getPaged({'pageSize': 100, 'orderBy': 'LastName'});
    if (!mounted) return;
    setState(() {
      _patients = patients.resultList;
      _doctors = doctors.resultList;
    });
  }

  Future<void> _load() async {
    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final search = <String, dynamic>{
        'page': _page,
        'pageSize': _pageSize,
        'orderBy': 'StartUtc',
        'sortDirection': 'desc',
        if (_filterPatientId != null) 'patientId': _filterPatientId,
        if (_filterDoctorId != null) 'doctorId': _filterDoctorId,
        if (_filterStatus != null) 'status': _filterStatus,
      };
      final result = await _appointmentProvider.getPaged(search);
      if (!mounted) return;
      setState(() {
        _appointments = result.resultList;
        _count = result.count;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _error = e.message);
    } finally {
      if (mounted) setState(() => _isLoading = false);
    }
  }

  void _resetPageAndLoad() {
    _page = 1;
    _load();
  }

  Future<void> _openScheduleDialog() async {
    final created = await showDialog<bool>(
      context: context,
      builder: (_) => const ScheduleAppointmentDialog(),
    );
    if (created == true) _load();
  }

  Future<void> _confirm(Appointment appointment) async {
    final ok = await _confirmDialog(
      title: 'Potvrda termina',
      message: 'Potvrditi termin za ${appointment.patientName} kod ${appointment.doctorName} (${_dateTimeFormat.format(appointment.startUtc)})?',
      actionLabel: 'Potvrdi',
    );
    if (ok != true) return;

    try {
      await _appointmentProvider.confirm(appointment.id);
      await _load();
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  Future<void> _complete(Appointment appointment) async {
    final ok = await _confirmDialog(
      title: 'Završetak termina',
      message: 'Označiti termin za ${appointment.patientName} kao završen?',
      actionLabel: 'Završi',
    );
    if (ok != true) return;

    try {
      await _appointmentProvider.complete(appointment.id);
      await _load();
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  Future<void> _cancel(Appointment appointment) async {
    final reasonController = TextEditingController();
    String? reasonError;

    final reason = await showDialog<String>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: const Text('Otkazivanje termina'),
          content: SizedBox(
            width: 400,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Termin za ${appointment.patientName} kod ${appointment.doctorName} - ${_dateTimeFormat.format(appointment.startUtc)}'),
                const SizedBox(height: 16),
                TextField(
                  controller: reasonController,
                  decoration: InputDecoration(labelText: 'Razlog otkazivanja', errorText: reasonError),
                  maxLines: 2,
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
              onPressed: () {
                if (reasonController.text.trim().isEmpty) {
                  setDialogState(() => reasonError = 'Razlog otkazivanja je obavezan.');
                  return;
                }
                Navigator.of(dialogContext).pop(reasonController.text.trim());
              },
              child: const Text('Otkaži termin'),
            ),
          ],
        ),
      ),
    );

    if (reason == null) return;

    try {
      await _appointmentProvider.cancel(appointment.id, reason);
      await _load();
    } on ApiException catch (e) {
      _showError(e.message);
    }
  }

  Future<bool?> _confirmDialog({required String title, required String message, required String actionLabel}) {
    return showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text(title),
        content: Text(message),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Odustani')),
          FilledButton(onPressed: () => Navigator.of(dialogContext).pop(true), child: Text(actionLabel)),
        ],
      ),
    );
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }

  Color _statusColor(int status) => switch (status) {
        0 => Colors.orange,
        1 => Colors.blue,
        2 => Colors.green,
        3 => Colors.red,
        _ => Colors.grey,
      };

  int get _totalPages => _count == 0 ? 1 : ((_count - 1) ~/ _pageSize) + 1;

  @override
  Widget build(BuildContext context) {
    // Backend only allows Administrator/Staff/Patient to schedule an
    // appointment (a Doctor manages their existing schedule via
    // Confirm/Complete/Cancel, but never books new ones from the desktop app)
    // - the button must not be shown to a role that would get a 403 on submit.
    final authSession = context.watch<AuthSession>();
    final canSchedule = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: Text('Termini', style: Theme.of(context).textTheme.titleLarge)),
              if (canSchedule)
                FilledButton.icon(
                  onPressed: _openScheduleDialog,
                  icon: const Icon(Icons.add),
                  label: const Text('Novi termin'),
                ),
            ],
          ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 12,
            runSpacing: 12,
            children: [
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int?>(
                  initialValue: _filterPatientId,
                  decoration: const InputDecoration(labelText: 'Pacijent', isDense: true),
                  items: [
                    const DropdownMenuItem(value: null, child: Text('Svi pacijenti')),
                    ..._patients.map((p) => DropdownMenuItem(value: p.id, child: Text(p.fullName))),
                  ],
                  onChanged: (value) {
                    _filterPatientId = value;
                    _resetPageAndLoad();
                  },
                ),
              ),
              SizedBox(
                width: 220,
                child: DropdownButtonFormField<int?>(
                  initialValue: _filterDoctorId,
                  decoration: const InputDecoration(labelText: 'Doktor', isDense: true),
                  itemHeight: null, // items are two lines (name + clinic)
                  items: [
                    const DropdownMenuItem(value: null, child: Text('Svi doktori')),
                    ..._doctors.map((d) => DropdownMenuItem(
                          value: d.id,
                          child: Padding(
                            padding: const EdgeInsets.symmetric(vertical: 4),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Text(d.fullName),
                                Text(d.locationName, style: TextStyle(fontSize: 12, color: clinicColor(d.locationId))),
                              ],
                            ),
                          ),
                        )),
                  ],
                  onChanged: (value) {
                    _filterDoctorId = value;
                    _resetPageAndLoad();
                  },
                ),
              ),
              SizedBox(
                width: 180,
                child: DropdownButtonFormField<int?>(
                  initialValue: _filterStatus,
                  decoration: const InputDecoration(labelText: 'Status', isDense: true),
                  items: [
                    const DropdownMenuItem(value: null, child: Text('Svi statusi')),
                    ..._statusOptions.map((s) => DropdownMenuItem(value: s.$1, child: Text(s.$2))),
                  ],
                  onChanged: (value) {
                    _filterStatus = value;
                    _resetPageAndLoad();
                  },
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ),
          Expanded(
            child: _isLoading
                ? const Center(child: CircularProgressIndicator())
                : _appointments.isEmpty
                    ? const Center(child: Text('Nema termina za prikaz.'))
                    : SingleChildScrollView(
                        child: SingleChildScrollView(
                          scrollDirection: Axis.horizontal,
                          child: DataTable(
                            columns: const [
                              DataColumn(label: Text('Datum i vrijeme')),
                              DataColumn(label: Text('Pacijent')),
                              DataColumn(label: Text('Doktor')),
                              DataColumn(label: Text('Usluga')),
                              DataColumn(label: Text('Lokacija')),
                              DataColumn(label: Text('Status')),
                              DataColumn(label: Text('Akcije')),
                            ],
                            rows: _appointments.map((appointment) {
                              return DataRow(cells: [
                                DataCell(Text(_dateTimeFormat.format(appointment.startUtc))),
                                DataCell(Text(appointment.patientName)),
                                DataCell(Text(appointment.doctorName)),
                                DataCell(Text(appointment.medicalServiceName)),
                                DataCell(Text(appointment.locationName)),
                                DataCell(Chip(
                                  label: Text(appointment.statusName, style: const TextStyle(color: Colors.white, fontSize: 12)),
                                  backgroundColor: _statusColor(appointment.status),
                                  visualDensity: VisualDensity.compact,
                                  padding: EdgeInsets.zero,
                                )),
                                DataCell(Row(
                                  mainAxisSize: MainAxisSize.min,
                                  children: [
                                    if (appointment.canConfirm)
                                      IconButton(
                                        tooltip: 'Potvrdi',
                                        icon: const Icon(Icons.check_circle_outline, color: Colors.blue),
                                        onPressed: () => _confirm(appointment),
                                      )
                                    else
                                      IconButton(
                                        tooltip: 'Potvrda nije moguća u ovom statusu',
                                        icon: const Icon(Icons.check_circle_outline),
                                        color: Theme.of(context).disabledColor,
                                        onPressed: null,
                                      ),
                                    if (appointment.canComplete)
                                      IconButton(
                                        tooltip: 'Završi',
                                        icon: const Icon(Icons.task_alt, color: Colors.green),
                                        onPressed: () => _complete(appointment),
                                      )
                                    else
                                      IconButton(
                                        tooltip: 'Završetak nije moguć u ovom statusu',
                                        icon: const Icon(Icons.task_alt),
                                        color: Theme.of(context).disabledColor,
                                        onPressed: null,
                                      ),
                                    if (appointment.canCancel)
                                      IconButton(
                                        tooltip: 'Otkaži',
                                        icon: const Icon(Icons.cancel_outlined, color: Colors.red),
                                        onPressed: () => _cancel(appointment),
                                      )
                                    else
                                      IconButton(
                                        tooltip: 'Otkazivanje nije moguće u ovom statusu',
                                        icon: const Icon(Icons.cancel_outlined),
                                        color: Theme.of(context).disabledColor,
                                        onPressed: null,
                                      ),
                                  ],
                                )),
                              ]);
                            }).toList(),
                          ),
                        ),
                      ),
          ),
          const SizedBox(height: 8),
          Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              IconButton(
                icon: const Icon(Icons.chevron_left),
                onPressed: _page > 1 ? () { setState(() => _page--); _load(); } : null,
              ),
              Text('Strana $_page od $_totalPages'),
              IconButton(
                icon: const Icon(Icons.chevron_right),
                onPressed: _page < _totalPages ? () { setState(() => _page++); _load(); } : null,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
