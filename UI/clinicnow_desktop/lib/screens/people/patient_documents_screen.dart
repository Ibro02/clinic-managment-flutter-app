import 'dart:async';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/error_text.dart';
import '../../core/roles.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_fields.dart';
import '../../widgets/ui/app_states.dart';
import '../../models/medical_document.dart';
import '../../models/patient.dart';
import '../../providers/medical_document_provider.dart';

/// Attach/view files on a patient's record (CLAUDE.md §6). Administrator/Staff
/// (and, per the backend, Doctor) can upload; every role that can see the
/// patient can view/download here. Files are validated server-side against
/// both the declared MIME type and the file's real magic bytes - a rejected
/// upload shows the exact backend validation message (rulebook §4).
///
/// Searching by file name is a server-side filter (review item C18), not a
/// filter over the fetched page: a chart with more documents than one page
/// holds is exactly the chart someone needs to search.
class PatientDocumentsScreen extends StatefulWidget {
  final Patient patient;

  const PatientDocumentsScreen({super.key, required this.patient});

  @override
  State<PatientDocumentsScreen> createState() => _PatientDocumentsScreenState();
}

class _PatientDocumentsScreenState extends State<PatientDocumentsScreen> {
  late final MedicalDocumentProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');
  final _searchController = TextEditingController();
  Timer? _debounce;

  List<MedicalDocument>? _documents;
  String? _error;
  bool _isUploading = false;
  bool _isLoading = false;

  /// The term the currently-displayed list was actually fetched with. The empty
  /// state reads from this rather than the controller, so a half-typed query
  /// never captions results that predate it.
  String _appliedSearch = '';

  /// Discards a slow response for a term the user has already typed past.
  int _requestSequence = 0;

  static const _allowedExtensions = ['pdf', 'png', 'jpg', 'jpeg'];
  static const _contentTypeByExtension = {
    'pdf': 'application/pdf',
    'png': 'image/png',
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
  };

  @override
  void initState() {
    super.initState();
    _provider = MedicalDocumentProvider(context.read<AuthSession>());
    _load();
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _searchController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    final sequence = ++_requestSequence;
    final term = _searchController.text.trim();

    setState(() {
      _isLoading = true;
      _error = null;
    });

    try {
      final documents = await _provider.getPaged(patientId: widget.patient.id, fileName: term);
      if (!mounted || sequence != _requestSequence) return;
      setState(() {
        _documents = documents;
        _appliedSearch = term;
      });
    } catch (e) {
      // Catch-all, not `on ApiException`: a stopped API throws a transport
      // exception, and letting that escape left the screen on its spinner
      // forever with nothing said (rulebook Part II: unhappy paths surfaced).
      if (!mounted || sequence != _requestSequence) return;
      setState(() {
        _error = failureCause(e);
        _appliedSearch = term;
      });
    } finally {
      if (mounted && sequence == _requestSequence) setState(() => _isLoading = false);
    }
  }

  /// Same 350ms debounce every other searchable grid in the app uses, so
  /// typing feels identical across screens.
  void _onSearchChanged(String _) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 350), _load);
  }

  void _clearSearch() {
    _debounce?.cancel();
    if (_searchController.text.isEmpty) return;
    _searchController.clear();
    _load();
  }

  Future<void> _pickAndUpload() async {
    final result = await FilePicker.pickFiles(type: FileType.custom, allowedExtensions: _allowedExtensions);
    final file = result.isEmpty ? null : result.single;
    if (file == null) return;

    final bytes = await file.readAsBytes();

    final extension = file.extension?.toLowerCase() ?? '';
    final contentType = _contentTypeByExtension[extension];
    if (contentType == null) {
      _showFailure(
        'Dokument nije priložen.',
        'Podržani su samo PDF, PNG i JPEG fajlovi, a "${file.name}" nije nijedan od njih.',
        stillTrue: 'Odaberite drugi fajl i pokušajte ponovo.',
      );
      return;
    }

    setState(() => _isUploading = true);
    try {
      await _provider.upload(
        patientId: widget.patient.id,
        fileName: file.name,
        contentType: contentType,
        bytes: bytes,
      );
      if (!mounted) return;
      // A new upload has no reason to be hidden by a filter the user forgot
      // about - clearing it guarantees the document they just attached is
      // visible in the list they are looking at (rulebook Part II §K).
      _debounce?.cancel();
      _searchController.clear();
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Dokument "${file.name}" je priložen na karton pacijenta.')),
        );
      }
    } catch (e) {
      if (mounted) {
        _showFailure(
          'Dokument nije priložen.',
          e,
          stillTrue: 'Karton pacijenta nije promijenjen.',
        );
      }
    } finally {
      if (mounted) setState(() => _isUploading = false);
    }
  }

  Future<void> _download(MedicalDocument document) async {
    try {
      final response = await http.get(
        Uri.parse(_provider.absoluteDownloadUrl(document)),
        headers: _provider.authHeaders(),
      );

      if (response.statusCode != 200) {
        // This request bypasses BaseProvider.decode (it wants raw bytes, not
        // JSON), so the status has to be translated here rather than left as
        // "HTTP 403" - a number is not something a receptionist can act on.
        _showFailure('Dokument nije preuzet.', switch (response.statusCode) {
          401 => 'Vaša prijava je istekla. Prijavite se ponovo pa pokušajte opet.',
          403 => 'Nemate dozvolu za pristup ovom dokumentu.',
          404 => 'Dokument više ne postoji - vjerovatno je u međuvremenu obrisan.',
          _ => 'Došlo je do greške na serveru. Pokušajte ponovo za nekoliko trenutaka.',
        });
        return;
      }

      final savedUri = await FilePicker.saveFile(fileName: document.fileName, bytes: response.bodyBytes);
      if (!mounted || savedUri == null) return; // null = the user cancelled the save dialog
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Dokument "${document.fileName}" je sačuvan.')),
      );
    } catch (e) {
      if (mounted) _showFailure('Dokument nije preuzet.', e);
    }
  }

  Future<void> _confirmDelete(MedicalDocument document) async {
    final confirmed = await showConfirmDialog(
      context: context,
      title: 'Potvrda brisanja',
      message:
          'Da li ste sigurni da želite obrisati "${document.fileName}"? '
          'Ova radnja se ne može poništiti.',
      confirmLabel: 'Obriši',
      destructive: true,
    );
    if (!confirmed) return;

    try {
      await _provider.delete(document.id);
      await _load();
    } catch (e) {
      if (mounted) {
        _showFailure(
          'Dokument nije obrisan.',
          e,
          stillTrue: 'Dokument je i dalje na kartonu pacijenta.',
        );
      }
    }
  }

  /// Outcome first, then why, then what is still true - so staff never have to
  /// infer from an error string whether the patient's chart changed.
  ///
  /// [cause] is either a caught exception - translated by [failureCause] - or,
  /// where this screen diagnosed the problem itself, the finished sentence.
  void _showFailure(String outcome, Object cause, {String? stillTrue}) {
    final why = cause is String ? cause : failureCause(cause);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text([outcome, why, ?stillTrue].join(' ')),
        duration: const Duration(seconds: 6),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final authSession = context.watch<AuthSession>();
    final canWrite =
        authSession.hasRole(Roles.administrator) ||
        authSession.hasRole(Roles.staff) ||
        authSession.hasRole(Roles.doctor);

    return Scaffold(
      appBar: AppBar(
        title: Text('Dokumenti - ${widget.patient.fullName}'),
        leading: IconButton(icon: const Icon(Icons.close), onPressed: () => Navigator.of(context).pop()),
      ),
      floatingActionButton: canWrite
          ? FloatingActionButton.extended(
              onPressed: _isUploading ? null : _pickAndUpload,
              icon: _isUploading
                  ? const SizedBox(height: 18, width: 18, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.upload_file),
              label: const Text('Priloži dokument'),
            )
          : null,
      body: Padding(
        padding: AppSpacing.page,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // The toolbar stays mounted through every load. Swapping the whole
            // body for a spinner - as this screen used to - takes the search
            // box away from under the cursor of the person still typing in it.
            AppToolbar(
              filters: [
                AppSearchField(
                  controller: _searchController,
                  hint: 'Pretraži po nazivu fajla…',
                  onChanged: _onSearchChanged,
                  onClear: _clearSearch,
                ),
              ],
            ),
            // Reserved height so results don't shift by two pixels each time a
            // keystroke starts a new query.
            SizedBox(
              height: 2,
              child: _isLoading && _documents != null ? const LinearProgressIndicator(minHeight: 2) : null,
            ),
            const SizedBox(height: AppSpacing.sm),
            Expanded(child: _content(canWrite)),
          ],
        ),
      ),
    );
  }

  Widget _content(bool canWrite) {
    if (_error != null) {
      return AppErrorState(
        title: 'Dokumenti nisu učitani',
        message: _error!,
        onRetry: _load,
      );
    }

    if (_documents == null) {
      return const Center(child: CircularProgressIndicator());
    }

    if (_documents!.isEmpty) {
      // Two genuinely different situations: an empty chart is something to act
      // on, a filtered-out list is something to undo. Telling a user with 40
      // documents "nema dokumenata" because they mistyped a name is the error
      // this branch exists to avoid.
      return _appliedSearch.isNotEmpty
          ? AppEmptyState(
              icon: Icons.search_off_rounded,
              title: 'Nema rezultata pretrage',
              message:
                  'Nijedan dokument ovog pacijenta ne sadrži "$_appliedSearch" u nazivu. '
                  'Provjerite naziv ili očistite pretragu da vidite sve dokumente.',
              action: OutlinedButton.icon(
                onPressed: _clearSearch,
                icon: const Icon(Icons.close_rounded, size: 18),
                label: const Text('Očisti pretragu'),
              ),
            )
          : AppEmptyState(
              icon: Icons.folder_open_outlined,
              title: 'Nema dokumenata',
              message: 'Za ovog pacijenta još nije priložen nijedan nalaz ili dokument.',
              action: canWrite
                  ? FilledButton.icon(
                      onPressed: _isUploading ? null : _pickAndUpload,
                      icon: const Icon(Icons.upload_file, size: 18),
                      label: const Text('Priloži dokument'),
                    )
                  : null,
            );
    }

    return ListView.separated(
      itemCount: _documents!.length,
      separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
      itemBuilder: (context, index) => _documentCard(_documents![index], canWrite),
    );
  }

  Widget _documentCard(MedicalDocument document, bool canWrite) {
    final isPdf = document.contentType == 'application/pdf';

    return AppCard(
      padding: const EdgeInsets.all(AppSpacing.sm),
      child: Row(
        children: [
          Container(
            width: 38,
            height: 38,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: (isPdf ? AppTone.danger : AppTone.info).background(context),
              borderRadius: AppRadius.all(AppRadius.sm),
            ),
            child: Icon(
              isPdf ? Icons.picture_as_pdf_outlined : Icons.image_outlined,
              size: 19,
              color: (isPdf ? AppTone.danger : AppTone.info).foreground(context),
            ),
          ),
          const SizedBox(width: AppSpacing.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(document.fileName, style: context.text.titleSmall),
                const SizedBox(height: 2),
                Text(
                  '${document.description ?? 'Bez opisa'} · '
                  '${document.uploadedByName} · '
                  '${_dateFormat.format(document.createdAtUtc.toLocal())}',
                  style: context.text.bodySmall?.copyWith(color: context.colors.textMuted),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
              ],
            ),
          ),
          const SizedBox(width: AppSpacing.xs),
          AppRowAction(
            icon: Icons.download_outlined,
            tooltip: 'Preuzmi',
            onPressed: () => _download(document),
          ),
          if (canWrite)
            AppRowAction(
              icon: Icons.delete_outline_rounded,
              tooltip: 'Obriši',
              destructive: true,
              onPressed: () => _confirmDelete(document),
            ),
        ],
      ),
    );
  }
}
