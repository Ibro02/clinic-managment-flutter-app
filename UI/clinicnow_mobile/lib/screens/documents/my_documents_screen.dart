import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../models/medical_document.dart';
import '../../providers/medical_document_provider.dart';

/// The patient's own medical documents (CLAUDE.md §6: "patient views/
/// downloads own documents only"). Ownership is enforced server-side - this
/// endpoint returns *only* the caller's own records for a Patient token,
/// regardless of any filter, so there's nothing to scope client-side.
class MyDocumentsScreen extends StatefulWidget {
  const MyDocumentsScreen({super.key});

  @override
  State<MyDocumentsScreen> createState() => _MyDocumentsScreenState();
}

class _MyDocumentsScreenState extends State<MyDocumentsScreen> {
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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Moji dokumenti')),
      body: _error != null
          ? Center(child: Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)))
          : _documents == null
              ? const Center(child: CircularProgressIndicator())
              : _documents!.isEmpty
                  ? const Center(child: Text('Nemate priloženih medicinskih dokumenata.'))
                  : RefreshIndicator(
                      onRefresh: _load,
                      child: ListView.separated(
                        padding: const EdgeInsets.all(12),
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
                              '${_dateFormat.format(document.createdAtUtc.toLocal())}',
                            ),
                            isThreeLine: true,
                            trailing: IconButton(
                              tooltip: 'Preuzmi',
                              icon: const Icon(Icons.download_outlined),
                              onPressed: () => _download(document),
                            ),
                          );
                        },
                      ),
                    ),
    );
  }
}
