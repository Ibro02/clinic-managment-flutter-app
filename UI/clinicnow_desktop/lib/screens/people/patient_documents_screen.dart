import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/design_tokens.dart';
import '../../core/roles.dart';
import '../../widgets/ui/app_badge.dart';
import '../../widgets/ui/app_card.dart';
import '../../widgets/ui/app_data_table.dart';
import '../../widgets/ui/app_dialog.dart';
import '../../widgets/ui/app_states.dart';
import '../../models/medical_document.dart';
import '../../models/patient.dart';
import '../../providers/medical_document_provider.dart';

/// Attach/view files on a patient's record (CLAUDE.md §6). Administrator/Staff
/// (and, per the backend, Doctor) can upload; every role that can see the
/// patient can view/download here. Files are validated server-side against
/// both the declared MIME type and the file's real magic bytes - a rejected
/// upload shows the exact backend validation message (rulebook §4).
class PatientDocumentsScreen extends StatefulWidget {
  final Patient patient;

  const PatientDocumentsScreen({super.key, required this.patient});

  @override
  State<PatientDocumentsScreen> createState() => _PatientDocumentsScreenState();
}

class _PatientDocumentsScreenState extends State<PatientDocumentsScreen> {
  late final MedicalDocumentProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  List<MedicalDocument>? _documents;
  String? _error;
  bool _isUploading = false;

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

  Future<void> _load() async {
    setState(() => _error = null);
    try {
      final documents = await _provider.getPaged(patientId: widget.patient.id);
      if (mounted) setState(() => _documents = documents);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e.message);
    }
  }

  Future<void> _pickAndUpload() async {
    final result = await FilePicker.pickFiles(type: FileType.custom, allowedExtensions: _allowedExtensions);
    final file = result.isEmpty ? null : result.single;
    if (file == null) return;

    final bytes = await file.readAsBytes();

    final extension = file.extension?.toLowerCase() ?? '';
    final contentType = _contentTypeByExtension[extension];
    if (contentType == null) {
      if (mounted) {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(const SnackBar(content: Text('Dozvoljeni su samo PDF, PNG i JPEG fajlovi.')));
      }
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
      await _load();
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
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
        throw Exception('HTTP ${response.statusCode}');
      }
      await FilePicker.saveFile(fileName: document.fileName, bytes: response.bodyBytes);
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Preuzimanje nije uspjelo: $e')));
      }
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
    } on ApiException catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.message)));
    }
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
      body: _error != null
          ? Padding(
              padding: AppSpacing.page,
              child: AppErrorState(message: _error!, onRetry: _load),
            )
          : _documents == null
          ? const Center(child: CircularProgressIndicator())
          : _documents!.isEmpty
          ? Padding(
              padding: AppSpacing.page,
              child: AppEmptyState(
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
              ),
            )
          : ListView.separated(
              padding: AppSpacing.page,
              itemCount: _documents!.length,
              separatorBuilder: (context, index) => const SizedBox(height: AppSpacing.xs),
              itemBuilder: (context, index) {
                final document = _documents![index];
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
              },
            ),
    );
  }
}
