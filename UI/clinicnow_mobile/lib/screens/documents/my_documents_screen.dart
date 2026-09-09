import '../../core/base_provider.dart';
import '../../core/api_http.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../models/lab_finding.dart';
import '../../models/medical_document.dart';
import '../../models/referral.dart';
import '../../providers/lab_finding_provider.dart';
import '../../providers/medical_document_provider.dart';
import '../../providers/referral_provider.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_states.dart';
import '../../widgets/ui/app_tiles.dart';
import '../appointments/book_appointment_screen.dart';

/// The patient's own documentation (CLAUDE.md §6: "patient views/downloads
/// own documents/findings only"). Three tabs - generic documents, lab
/// findings tied to a specific appointment (review item C4: "mobilni dio
/// treba pacijentu prikazati te nalaze u okviru njegove dokumentacije" - the
/// findings live *inside* this same documentation screen, not a separate nav
/// item), and specialist referrals (review item C5, same reasoning). Ownership
/// for all three is enforced server-side - each endpoint returns only the
/// caller's own records for a Patient token, regardless of any filter, so
/// there's nothing to scope client-side.
class MyDocumentsScreen extends StatelessWidget {
  const MyDocumentsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 3,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Moja dokumentacija'),
          bottom: const TabBar(
            tabs: [
              Tab(text: 'Dokumenti'),
              Tab(text: 'Nalazi'),
              Tab(text: 'Uputnice'),
            ],
          ),
        ),
        body: const TabBarView(children: [_MyDocumentsTab(), _MyLabFindingsTab(), _MyReferralsTab()]),
      ),
    );
  }
}

class _MyDocumentsTab extends StatefulWidget {
  const _MyDocumentsTab();

  @override
  State<_MyDocumentsTab> createState() => _MyDocumentsTabState();
}

class _MyDocumentsTabState extends State<_MyDocumentsTab> {
  late final MedicalDocumentProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  List<MedicalDocument>? _documents;
  String? _error;

  @override
  void initState() {
    super.initState();
    _provider = MedicalDocumentProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final documents = await _provider.getPaged();
      if (mounted) setState(() => _documents = documents);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _download(MedicalDocument document) async {
    try {
      final response = await apiGet(
        Uri.parse(_provider.absoluteDownloadUrl(document)),
        headers: _provider.authHeaders(),
        timeout: BaseProvider.fileTransferTimeout,
      );
      if (response.statusCode != 200) {
        throw Exception('HTTP ${response.statusCode}');
      }
      await FilePicker.saveFile(fileName: document.fileName, bytes: response.bodyBytes);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Preuzimanje nije uspjelo: $e')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: AppErrorState(message: _error!, onRetry: _load),
      );
    }
    if (_documents == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_documents!.isEmpty) {
      return const Padding(
        padding: EdgeInsets.all(AppSpacing.md),
        child: AppEmptyState(
          icon: Icons.folder_open_outlined,
          title: 'Nemate dokumenata',
          message: 'Nalazi i dokumenti koje klinika priloži uz vaš karton pojavit će se ovdje.',
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        padding: const EdgeInsets.all(AppSpacing.md),
        itemCount: _documents!.length,
        separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
        itemBuilder: (context, index) {
          final document = _documents![index];
          final isPdf = document.contentType == 'application/pdf';

          return AppListCard(
            icon: isPdf ? Icons.picture_as_pdf_outlined : Icons.image_outlined,
            tone: isPdf ? AppTone.danger : AppTone.info,
            title: document.fileName,
            subtitle: document.description ?? 'Bez opisa',
            meta: _dateFormat.format(document.createdAtUtc.toLocal()),
            actions: [
              IconButton(
                tooltip: 'Preuzmi',
                icon: const Icon(Icons.download_outlined),
                onPressed: () => _download(document),
              ),
            ],
          );
        },
      ),
    );
  }
}

class _MyLabFindingsTab extends StatefulWidget {
  const _MyLabFindingsTab();

  @override
  State<_MyLabFindingsTab> createState() => _MyLabFindingsTabState();
}

class _MyLabFindingsTabState extends State<_MyLabFindingsTab> {
  late final LabFindingProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  List<LabFinding>? _findings;
  String? _error;

  @override
  void initState() {
    super.initState();
    _provider = LabFindingProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final findings = await _provider.getPaged();
      if (mounted) setState(() => _findings = findings);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _download(LabFinding finding) async {
    try {
      final response = await apiGet(
        Uri.parse(_provider.absoluteDownloadUrl(finding)),
        headers: _provider.authHeaders(),
        timeout: BaseProvider.fileTransferTimeout,
      );
      if (response.statusCode != 200) {
        throw Exception('HTTP ${response.statusCode}');
      }
      // Only reachable when hasFile is true, so fileName is populated.
      await FilePicker.saveFile(fileName: finding.fileName ?? 'nalaz', bytes: response.bodyBytes);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Preuzimanje nije uspjelo: $e')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: AppErrorState(message: _error!, onRetry: _load),
      );
    }
    if (_findings == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_findings!.isEmpty) {
      return const Padding(
        padding: EdgeInsets.all(AppSpacing.md),
        child: AppEmptyState(
          icon: Icons.biotech_outlined,
          title: 'Nemate laboratorijskih nalaza',
          message: 'Nalazi koje unese doktor ili laboratorijsko osoblje uz vaš termin pojavit će se ovdje.',
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        padding: const EdgeInsets.all(AppSpacing.md),
        itemCount: _findings!.length,
        separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
        itemBuilder: (context, index) {
          final finding = _findings![index];
          final isPdf = finding.contentType == 'application/pdf';
          // "13.9 g/dL (ref. 12.0 - 16.0)" - built from whichever of the three
          // optional measurement fields this finding carries.
          final measurement = [
            if ((finding.value ?? '').isNotEmpty) finding.value!,
            if ((finding.unit ?? '').isNotEmpty) finding.unit!,
            if ((finding.referenceRange ?? '').isNotEmpty) '(ref. ${finding.referenceRange})',
          ].join(' ');

          return AppListCard(
            icon: !finding.hasFile
                ? Icons.science_outlined
                : isPdf
                ? Icons.picture_as_pdf_outlined
                : Icons.image_outlined,
            tone: isPdf ? AppTone.danger : AppTone.info,
            title: finding.testName,
            subtitle: [
              if (measurement.isNotEmpty) measurement,
              finding.result,
              if ((finding.doctorNote ?? '').isNotEmpty) 'Napomena: ${finding.doctorNote}',
              '${finding.medicalServiceName} · ${_dateFormat.format(finding.appointmentStartUtc.toLocal())}',
            ].join('\n'),
            subtitleMaxLines: 4,
            meta: _dateFormat.format(finding.createdAtUtc.toLocal()),
            actions: [
              // Disabled with the reason rather than hidden (rulebook §6) - the
              // finding is complete without an attachment, there is simply
              // nothing to download.
              IconButton(
                tooltip: finding.hasFile ? 'Preuzmi' : 'Uz ovaj nalaz nije priložen dokument',
                icon: const Icon(Icons.download_outlined),
                onPressed: finding.hasFile ? () => _download(finding) : null,
              ),
            ],
          );
        },
      ),
    );
  }
}

class _MyReferralsTab extends StatefulWidget {
  const _MyReferralsTab();

  @override
  State<_MyReferralsTab> createState() => _MyReferralsTabState();
}

class _MyReferralsTabState extends State<_MyReferralsTab> {
  late final ReferralProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  bool _showArchived = false;
  List<Referral>? _referrals;
  String? _error;

  @override
  void initState() {
    super.initState();
    _provider = ReferralProvider(context.read<AuthSession>());
    _load();
  }

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final referrals = await _provider.getPaged(onlyArchived: _showArchived);
      if (mounted) setState(() => _referrals = referrals);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  void _setShowArchived(bool value) {
    if (_showArchived == value) return;
    setState(() {
      _showArchived = value;
      _referrals = null;
    });
    _load();
  }

  Future<void> _bookWithSpecialist(Referral referral) async {
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) => BookAppointmentScreen(
          initialSpecializationId: referral.targetSpecializationId,
          referralId: referral.id,
        ),
      ),
    );
    // Booking may have used up this referral (or, on return, it may already
    // have moved to Arhiva if the resulting appointment was somehow already
    // completed) - refresh so the button state stays honest either way.
    await _load();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(AppSpacing.md, AppSpacing.md, AppSpacing.md, 0),
          child: SegmentedButton<bool>(
            segments: const [
              ButtonSegment(value: false, label: Text('Aktivne')),
              ButtonSegment(value: true, label: Text('Arhiva')),
            ],
            selected: {_showArchived},
            onSelectionChanged: (selection) => _setShowArchived(selection.first),
          ),
        ),
        Expanded(child: _body(context)),
      ],
    );
  }

  Widget _body(BuildContext context) {
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: AppErrorState(message: _error!, onRetry: _load),
      );
    }
    if (_referrals == null) {
      return const Center(child: CircularProgressIndicator());
    }
    if (_referrals!.isEmpty) {
      return Padding(
        padding: const EdgeInsets.all(AppSpacing.md),
        child: AppEmptyState(
          icon: Icons.assignment_outlined,
          title: _showArchived ? 'Arhiva je prazna' : 'Nemate uputnica',
          message: _showArchived
              ? 'Uputnice se ovdje pojavljuju čim se iskoriste za zakazivanje termina, ili budu uklonjene.'
              : 'Uputnice koje vam doktor izda tokom pregleda pojavit će se ovdje.',
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView.separated(
        padding: const EdgeInsets.all(AppSpacing.md),
        itemCount: _referrals!.length,
        separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
        itemBuilder: (context, index) {
          final referral = _referrals![index];
          // A referral moves to Arhiva the moment it's used (server-side
          // filter, not just once its resulting appointment finishes) - so
          // everything in the active list is guaranteed still bookable, and
          // everything in Arhiva is inert. No per-item used/unused split
          // needed here anymore.
          return AppListCard(
            icon: Icons.assignment_outlined,
            tone: AppTone.primary,
            title: 'Uputnica: ${referral.targetSpecializationName}',
            subtitle: referral.reason,
            meta: '${referral.referringDoctorName} · ${_dateFormat.format(referral.createdAtUtc.toLocal())}',
            onTap: _showArchived ? null : () => _bookWithSpecialist(referral),
          );
        },
      ),
    );
  }
}
