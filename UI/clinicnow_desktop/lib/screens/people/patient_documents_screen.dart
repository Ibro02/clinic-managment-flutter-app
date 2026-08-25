import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
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
    final result = await FilePicker.pickFiles(
      type: FileType.custom,
      allowedExtensions: _allowedExtensions,
    );
    final file = result.isEmpty ? null : result.single;
    if (file == null) return;

    final bytes = await file.readAsBytes();

    final extension = file.extension?.toLowerCase() ?? '';
    final contentType = _contentTypeByExtension[extension];
    if (contentType == null) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Dozvoljeni su samo PDF, PNG i JPEG fajlovi.')),
        );
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
      await FilePicker.saveFile(
        fileName: document.fileName,
        bytes: response.bodyBytes,
      );
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Preuzimanje nije uspjelo: $e')),
        );
      }
    }
  }

  Future<void> _confirmDelete(MedicalDocument document) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Potvrda brisanja'),
        content: Text('Da li ste sigurni da želite obrisati "${document.fileName}"?'),
        actions: [
          TextButton(onPressed: () => Navigator.of(dialogContext).pop(false), child: const Text('Odustani')),
          FilledButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.error),
            child: const Text('Obriši'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;

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
    final canWrite = authSession.hasRole(Roles.administrator) ||
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
          ? Center(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)))
          : _documents == null
              ? const Center(child: CircularProgressIndicator())
              : _documents!.isEmpty
                  ? const Center(child: Text('Nema priloženih dokumenata za ovog pacijenta.'))
                  : ListView.separated(
                      padding: const EdgeInsets.all(16),
                      itemCount: _documents!.length,
                      separatorBuilder: (context, index) => const Divider(),
                      itemBuilder: (context, index) {
                        final document = _documents![index];
                        return ListTile(
                          leading: Icon(document.contentType == 'application/pdf'
                              ? Icons.picture_as_pdf_outlined
                              : Icons.image_outlined),
                          title: Text(document.fileName),
                          subtitle: Text(
                            '${document.description ?? 'Bez opisa'}\n'
                            'Postavio/la: ${document.uploadedByName} · ${_dateFormat.format(document.createdAtUtc.toLocal())}',
                          ),
                          isThreeLine: true,
                          trailing: Row(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              IconButton(
                                tooltip: 'Preuzmi',
                                icon: const Icon(Icons.download_outlined),
                                onPressed: () => _download(document),
                              ),
                              if (canWrite)
                                IconButton(
                                  tooltip: 'Obriši',
                                  icon: const Icon(Icons.delete_outline),
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
