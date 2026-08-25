import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
import '../../models/doctor.dart';
import '../../models/schedule_block.dart';
import '../../models/working_hours.dart';
import '../../providers/schedule_block_provider.dart';
import '../../providers/working_hours_provider.dart';
import '../../widgets/paged_codebook_table.dart';

/// Per-doctor schedule editor (PLAN.md Phase 3 item 3: "schedule/blocks
/// editor") - a recurring weekly availability table plus a blocked-periods
/// table (vacation, meetings, ...), both scoped to one doctor via
/// `extraSearchParams: {'doctorId': doctor.id}` on the shared
/// `PagedCodebookTable`.
class DoctorScheduleScreen extends StatefulWidget {
  final Doctor doctor;

  const DoctorScheduleScreen({super.key, required this.doctor});

  @override
  State<DoctorScheduleScreen> createState() => _DoctorScheduleScreenState();
}

class _DoctorScheduleScreenState extends State<DoctorScheduleScreen> {
  final _hoursTableKey = GlobalKey<PagedCodebookTableState<WorkingHours>>();
  final _blocksTableKey = GlobalKey<PagedCodebookTableState<ScheduleBlock>>();

  late final WorkingHoursProvider _hoursProvider;
  late final ScheduleBlockProvider _blocksProvider;

  static final _dateTimeFormat = DateFormat('dd.MM.yyyy HH:mm');

  @override
  void initState() {
    super.initState();
    final authSession = context.read<AuthSession>();
    _hoursProvider = WorkingHoursProvider(authSession);
    _blocksProvider = ScheduleBlockProvider(authSession);
  }

  Future<void> _openHoursForm({WorkingHours? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};
    var startTime = initial?.startTime ?? const TimeOfDay(hour: 8, minute: 0);
    var endTime = initial?.endTime ?? const TimeOfDay(hour: 16, minute: 0);

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Novo radno vrijeme' : 'Uredi radno vrijeme'),
          content: SizedBox(
            width: 400,
            child: FormBuilder(
              key: formKey,
              initialValue: {'dayOfWeek': initial?.dayOfWeek ?? 1},
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  FormBuilderDropdown<int>(
                    name: 'dayOfWeek',
                    decoration: InputDecoration(labelText: 'Dan u sedmici', errorText: fieldErrors['dayOfWeek']?.first),
                    items: List.generate(
                      7,
                      (i) => DropdownMenuItem(value: i, child: Text(kDayOfWeekNames[i])),
                    ),
                  ),
                  const SizedBox(height: 16),
                  Row(
                    children: [
                      Expanded(
                        child: OutlinedButton(
                          onPressed: () async {
                            final picked = await showTimePicker(context: dialogContext, initialTime: startTime);
                            if (picked != null) setDialogState(() => startTime = picked);
                          },
                          child: Text('Početak: ${formatTimeOfDay(startTime)}'),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: OutlinedButton(
                          onPressed: () async {
                            final picked = await showTimePicker(context: dialogContext, initialTime: endTime);
                            if (picked != null) setDialogState(() => endTime = picked);
                          },
                          child: Text('Kraj: ${formatTimeOfDay(endTime)}'),
                        ),
                      ),
                    ],
                  ),
                  if (fieldErrors['endTime'] != null) ...[
                    const SizedBox(height: 8),
                    Text(fieldErrors['endTime']!.first,
                        style: TextStyle(color: Theme.of(context).colorScheme.error, fontSize: 12)),
                  ],
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: isSubmitting ? null : () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              onPressed: isSubmitting
                  ? null
                  : () async {
                      final form = formKey.currentState;
                      if (form == null || !form.saveAndValidate()) return;
                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });

                      final request = {
                        'doctorId': widget.doctor.id,
                        'dayOfWeek': form.value['dayOfWeek'],
                        'startTime': timeOfDayToApi(startTime),
                        'endTime': timeOfDayToApi(endTime),
                      };

                      try {
                        if (initial == null) {
                          await _hoursProvider.insert(request);
                        } else {
                          await _hoursProvider.update(initial.id, request);
                        }
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
                      } on ApiException catch (e) {
                        setDialogState(() {
                          fieldErrors = e.fieldErrors;
                          isSubmitting = false;
                        });
                      }
                    },
              child: isSubmitting
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Sačuvaj'),
            ),
          ],
        ),
      ),
    );

    _hoursTableKey.currentState?.load();
  }

  Future<void> _openBlockForm({ScheduleBlock? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Nova blokada' : 'Uredi blokadu'),
          content: SizedBox(
            width: 420,
            child: FormBuilder(
              key: formKey,
              initialValue: {
                'startUtc': initial?.startUtc,
                'endUtc': initial?.endUtc,
                'reason': initial?.reason ?? '',
              },
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  FormBuilderDateTimePicker(
                    name: 'startUtc',
                    inputType: InputType.both,
                    format: _dateTimeFormat,
                    decoration: InputDecoration(labelText: 'Početak', errorText: fieldErrors['startUtc']?.first),
                    validator: FormBuilderValidators.required(errorText: 'Početak je obavezan.'),
                  ),
                  const SizedBox(height: 12),
                  FormBuilderDateTimePicker(
                    name: 'endUtc',
                    inputType: InputType.both,
                    format: _dateTimeFormat,
                    decoration: InputDecoration(labelText: 'Kraj', errorText: fieldErrors['endUtc']?.first),
                    validator: FormBuilderValidators.required(errorText: 'Kraj je obavezan.'),
                  ),
                  const SizedBox(height: 12),
                  FormBuilderTextField(
                    name: 'reason',
                    decoration: InputDecoration(labelText: 'Razlog', errorText: fieldErrors['reason']?.first),
                    validator: FormBuilderValidators.required(errorText: 'Razlog je obavezan.'),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: isSubmitting ? null : () => Navigator.of(dialogContext).pop(),
              child: const Text('Odustani'),
            ),
            FilledButton(
              onPressed: isSubmitting
                  ? null
                  : () async {
                      final form = formKey.currentState;
                      if (form == null || !form.saveAndValidate()) return;
                      setDialogState(() {
                        isSubmitting = true;
                        fieldErrors = {};
                      });

                      final startLocal = form.value['startUtc'] as DateTime;
                      final endLocal = form.value['endUtc'] as DateTime;

                      final request = {
                        'doctorId': widget.doctor.id,
                        'startUtc': startLocal.toUtc().toIso8601String(),
                        'endUtc': endLocal.toUtc().toIso8601String(),
                        'reason': form.value['reason'],
                      };

                      try {
                        if (initial == null) {
                          await _blocksProvider.insert(request);
                        } else {
                          await _blocksProvider.update(initial.id, request);
                        }
                        if (dialogContext.mounted) Navigator.of(dialogContext).pop();
                      } on ApiException catch (e) {
                        setDialogState(() {
                          fieldErrors = e.fieldErrors;
                          isSubmitting = false;
                        });
                      }
                    },
              child: isSubmitting
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Text('Sačuvaj'),
            ),
          ],
        ),
      ),
    );

    _blocksTableKey.currentState?.load();
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final canWrite = authSession.hasRole(Roles.administrator) || authSession.hasRole(Roles.staff);

    return Scaffold(
      appBar: AppBar(title: Text('Raspored — ${widget.doctor.fullName}')),
      body: DefaultTabController(
        length: 2,
        child: Column(
          children: [
            const Material(
              color: Colors.transparent,
              child: TabBar(
                tabs: [
                  Tab(text: 'Radno vrijeme'),
                  Tab(text: 'Blokade'),
                ],
              ),
            ),
            Expanded(
              child: TabBarView(
                children: [
                  PagedCodebookTable<WorkingHours>(
                    key: _hoursTableKey,
                    title: 'Radno vrijeme',
                    searchHint: '',
                    showSearch: false,
                    provider: _hoursProvider,
                    orderBy: 'DayOfWeek',
                    canWrite: canWrite,
                    extraSearchParams: {'doctorId': widget.doctor.id},
                    buildColumns: () => const [
                      DataColumn(label: Text('Dan')),
                      DataColumn(label: Text('Početak')),
                      DataColumn(label: Text('Kraj')),
                    ],
                    buildCells: (hours) => [
                      DataCell(Text(kDayOfWeekNames[hours.dayOfWeek])),
                      DataCell(Text(formatTimeOfDay(hours.startTime))),
                      DataCell(Text(formatTimeOfDay(hours.endTime))),
                    ],
                    onAdd: () => _openHoursForm(),
                    onEdit: (hours) => _openHoursForm(initial: hours),
                    onDelete: (hours) => _hoursProvider.delete(hours.id),
                    itemLabel: (hours) => '${kDayOfWeekNames[hours.dayOfWeek]} ${formatTimeOfDay(hours.startTime)}-${formatTimeOfDay(hours.endTime)}',
                  ),
                  PagedCodebookTable<ScheduleBlock>(
                    key: _blocksTableKey,
                    title: 'Blokade',
                    searchHint: '',
                    showSearch: false,
                    provider: _blocksProvider,
                    orderBy: 'StartUtc',
                    canWrite: canWrite,
                    extraSearchParams: {'doctorId': widget.doctor.id},
                    buildColumns: () => const [
                      DataColumn(label: Text('Period')),
                      DataColumn(label: Text('Razlog')),
                    ],
                    buildCells: (block) => [
                      DataCell(Text('${_dateTimeFormat.format(block.startUtc)} — ${_dateTimeFormat.format(block.endUtc)}')),
                      DataCell(Text(block.reason)),
                    ],
                    onAdd: () => _openBlockForm(),
                    onEdit: (block) => _openBlockForm(initial: block),
                    onDelete: (block) => _blocksProvider.delete(block.id),
                    itemLabel: (block) => block.reason,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
