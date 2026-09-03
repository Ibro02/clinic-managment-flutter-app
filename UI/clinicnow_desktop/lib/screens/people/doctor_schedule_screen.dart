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
import '../../core/design_tokens.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';

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
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Novo radno vrijeme' : 'Uredi radno vrijeme',
          subtitle: 'Termini se mogu zakazati samo unutar radnog vremena.',
          icon: Icons.schedule_outlined,
          width: 480,
          actions: [
            OutlinedButton(
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
          child: FormBuilder(
            key: formKey,
            initialValue: {'dayOfWeek': initial?.dayOfWeek ?? 1},
            child: AppFormSection(
              children: [
                AppField(
                  label: 'Dan u sedmici',
                  required: true,
                  child: FormBuilderDropdown<int>(
                    name: 'dayOfWeek',
                    decoration: InputDecoration(errorText: fieldErrors['dayOfWeek']?.first),
                    items: List.generate(
                      7,
                      (i) => DropdownMenuItem(value: i, child: Text(kDayOfWeekNames[i])),
                    ),
                  ),
                ),
                // Rulebook §K: times are picked, never typed into a raw textbox.
                AppFieldRow(
                  children: [
                    AppField(
                      label: 'Početak',
                      required: true,
                      child: _timeButton(
                        context: dialogContext,
                        value: startTime,
                        onPicked: (picked) => setDialogState(() => startTime = picked),
                      ),
                    ),
                    AppField(
                      label: 'Kraj',
                      required: true,
                      help: fieldErrors['endTime']?.first,
                      child: _timeButton(
                        context: dialogContext,
                        value: endTime,
                        onPicked: (picked) => setDialogState(() => endTime = picked),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
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
        builder: (dialogContext, setDialogState) => AppDialog(
          title: initial == null ? 'Nova blokada' : 'Uredi blokadu',
          subtitle: 'Blokirani period se ne nudi pacijentima pri zakazivanju.',
          icon: Icons.event_busy_outlined,
          tone: AppTone.warning,
          width: 520,
          actions: [
            OutlinedButton(
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
          child: FormBuilder(
            key: formKey,
            initialValue: {
              'startUtc': initial?.startUtc,
              'endUtc': initial?.endUtc,
              'reason': initial?.reason ?? '',
            },
            child: AppFormSection(
              children: [
                AppFieldRow(
                  children: [
                    AppField(
                      label: 'Početak',
                      required: true,
                      child: FormBuilderDateTimePicker(
                        name: 'startUtc',
                        inputType: InputType.both,
                        format: _dateTimeFormat,
                        decoration: InputDecoration(errorText: fieldErrors['startUtc']?.first),
                        validator: FormBuilderValidators.required(errorText: 'Početak je obavezan.'),
                      ),
                    ),
                    AppField(
                      label: 'Kraj',
                      required: true,
                      child: FormBuilderDateTimePicker(
                        name: 'endUtc',
                        inputType: InputType.both,
                        format: _dateTimeFormat,
                        decoration: InputDecoration(errorText: fieldErrors['endUtc']?.first),
                        validator: FormBuilderValidators.required(errorText: 'Kraj je obavezan.'),
                      ),
                    ),
                  ],
                ),
                AppField(
                  label: 'Razlog',
                  required: true,
                  help: 'Vidljiv samo osoblju, npr. godišnji odmor ili sastanak.',
                  child: FormBuilderTextField(
                    name: 'reason',
                    decoration: InputDecoration(
                      hintText: 'npr. Godišnji odmor',
                      errorText: fieldErrors['reason']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Razlog je obavezan.'),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );

    _blocksTableKey.currentState?.load();
  }

  /// Time picker styled as a field-height button, so a picked time sits on the
  /// same baseline as the text inputs beside it instead of looking like a
  /// stray action button.
  Widget _timeButton({
    required BuildContext context,
    required TimeOfDay value,
    required ValueChanged<TimeOfDay> onPicked,
  }) {
    final c = context.colors;

    return SizedBox(
      height: AppSizes.controlHeight,
      child: OutlinedButton(
        onPressed: () async {
          final picked = await showTimePicker(context: context, initialTime: value);
          if (picked != null) onPicked(picked);
        },
        style: OutlinedButton.styleFrom(
          alignment: Alignment.centerLeft,
          side: BorderSide(color: c.border),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.sm + 2),
        ),
        child: Row(
          children: [
            Icon(Icons.access_time_rounded, size: 16, color: c.textMuted),
            const SizedBox(width: AppSpacing.xs),
            Text(
              formatTimeOfDay(value),
              style: context.text.bodyMedium?.copyWith(fontFeatures: AppTypography.tabular),
            ),
          ],
        ),
      ),
    );
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
                    buildColumns: () => [
                      AppColumn(
                        label: 'Dan',
                        flex: 2,
                        cell: (context, hours) => Text(kDayOfWeekNames[hours.dayOfWeek]),
                      ),
                      AppColumn(
                        label: 'Početak',
                        width: 110,
                        numeric: true,
                        cell: (context, hours) => Text(formatTimeOfDay(hours.startTime)),
                      ),
                      AppColumn(
                        label: 'Kraj',
                        width: 110,
                        numeric: true,
                        cell: (context, hours) => Text(formatTimeOfDay(hours.endTime)),
                      ),
                    ],
                    addLabel: 'Dodaj radno vrijeme',
                    onAdd: () => _openHoursForm(),
                    onEdit: (hours) => _openHoursForm(initial: hours),
                    onDelete: (hours) => _hoursProvider.delete(hours.id),
                    itemLabel: (hours) =>
                        '${kDayOfWeekNames[hours.dayOfWeek]} ${formatTimeOfDay(hours.startTime)}-${formatTimeOfDay(hours.endTime)}',
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
                    buildColumns: () => [
                      AppColumn(
                        label: 'Period',
                        flex: 2,
                        cell: (context, block) => Text(
                          '${_dateTimeFormat.format(block.startUtc)} — ${_dateTimeFormat.format(block.endUtc)}',
                        ),
                      ),
                      AppColumn(
                        label: 'Razlog',
                        flex: 2,
                        cell: (context, block) => Text(block.reason, overflow: TextOverflow.ellipsis),
                      ),
                    ],
                    addLabel: 'Dodaj blokadu',
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
