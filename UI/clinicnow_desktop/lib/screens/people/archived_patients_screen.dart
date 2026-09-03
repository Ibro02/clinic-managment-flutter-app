import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/patient.dart';
import '../../providers/patient_provider.dart';
import '../../widgets/paged_codebook_table.dart';
import '../../widgets/ui/app_data_table.dart';

/// Archived (soft-deleted) patients - Administrator/Staff only, reached from
/// the "Arhivirani pacijenti" button on [PatientScreen]. Lists patients the
/// same way the main grid does, but sourced from `onlyDeleted: true`, with a
/// single "Vrati" action instead of edit/delete (restoring isn't destructive,
/// so it skips the confirm dialog `PagedCodebookTable`'s own delete uses).
class ArchivedPatientsScreen extends StatefulWidget {
  const ArchivedPatientsScreen({super.key});

  @override
  State<ArchivedPatientsScreen> createState() => _ArchivedPatientsScreenState();
}

class _ArchivedPatientsScreenState extends State<ArchivedPatientsScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<Patient>>();
  late final PatientProvider _provider;
  static final _dateFormat = DateFormat('dd.MM.yyyy');

  @override
  void initState() {
    super.initState();
    _provider = PatientProvider(context.read<AuthSession>());
  }

  Future<void> _restore(Patient patient) async {
    try {
      await _provider.restore(patient.id);
      _tableKey.currentState?.load();
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text('${patient.fullName} je vraćen(a) iz arhive.')));
      }
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Arhivirani pacijenti'),
        leading: IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.of(context).pop()),
      ),
      body: PagedCodebookTable<Patient>(
        key: _tableKey,
        title: 'Arhivirani pacijenti',
        subtitle: 'Pacijenti čiji je zapis arhiviran. Vraćanjem se ponovo aktivira i njihov korisnički nalog.',
        searchHint: 'Pretraga po imenu i prezimenu',
        provider: _provider,
        orderBy: 'LastName',
        canWrite: false,
        extraSearchParams: const {'onlyDeleted': true},
        buildColumns: () => [
          AppColumn(
            label: 'Ime i prezime',
            sortKey: 'LastName',
            flex: 2,
            cell: (context, patient) => Text(patient.fullName, overflow: TextOverflow.ellipsis),
          ),
          AppColumn(
            label: 'JMBG / ID',
            width: 150,
            numeric: true,
            cell: (context, patient) => Text(patient.personalIdNumber ?? '—'),
          ),
          AppColumn(
            label: 'Datum arhiviranja',
            width: 160,
            numeric: true,
            cell: (context, patient) =>
                Text(patient.deletedAtUtc == null ? '—' : _dateFormat.format(patient.deletedAtUtc!.toLocal())),
          ),
        ],
        extraRowActions: (patient) => [
          AppRowAction(icon: Icons.restore_outlined, tooltip: 'Vrati pacijenta', onPressed: () => _restore(patient)),
        ],
        itemLabel: (patient) => patient.fullName,
      ),
    );
  }
}
