import 'package:flutter/material.dart';
import 'package:flutter_form_builder/flutter_form_builder.dart';
import 'package:form_builder_validators/form_builder_validators.dart';
import 'package:intl/intl.dart';
import 'package:provider/provider.dart';

import '../../core/api_exception.dart';
import '../../core/auth_session.dart';
import '../../core/roles.dart';
import '../../models/news_item.dart';
import '../../providers/news_item_provider.dart';
import '../../widgets/paged_codebook_table.dart';

/// Administrator/Staff CRUD for news/announcements (rulebook Part II §G). The
/// seeded demo data already includes real images (see migration
/// `AddNotificationsAndNews`); this form covers title/text - a full image
/// upload picker is deferred (tracked in GOALS.md), the seed data already
/// satisfies "news shows with images".
class NewsScreen extends StatefulWidget {
  const NewsScreen({super.key});

  @override
  State<NewsScreen> createState() => _NewsScreenState();
}

class _NewsScreenState extends State<NewsScreen> {
  final _tableKey = GlobalKey<PagedCodebookTableState<NewsItem>>();
  late final NewsItemProvider _provider;
  final _dateFormat = DateFormat('dd.MM.yyyy HH:mm');

  @override
  void initState() {
    super.initState();
    _provider = NewsItemProvider(context.read<AuthSession>());
  }

  Future<void> _openForm({NewsItem? initial}) async {
    final formKey = GlobalKey<FormBuilderState>();
    var isSubmitting = false;
    Map<String, List<String>> fieldErrors = {};

    await showDialog<void>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (dialogContext, setDialogState) => AlertDialog(
          title: Text(initial == null ? 'Nova obavijest' : 'Uredi obavijest'),
          content: SizedBox(
            width: 480,
            child: FormBuilder(
              key: formKey,
              initialValue: {
                'title': initial?.title ?? '',
                'text': initial?.text ?? '',
              },
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  FormBuilderTextField(
                    name: 'title',
                    decoration: InputDecoration(
                      labelText: 'Naslov',
                      errorText: fieldErrors['title']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Naslov je obavezan.'),
                  ),
                  const SizedBox(height: 12),
                  FormBuilderTextField(
                    name: 'text',
                    maxLines: 4,
                    decoration: InputDecoration(
                      labelText: 'Tekst',
                      errorText: fieldErrors['text']?.first,
                    ),
                    validator: FormBuilderValidators.required(errorText: 'Tekst je obavezan.'),
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
                      final request = {
                        'title': form.value['title'],
                        'text': form.value['text'],
                      };
                      try {
                        if (initial == null) {
                          await _provider.insert(request);
                        } else {
                          await _provider.update(initial.id, request);
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

    _tableKey.currentState?.load();
  }

  @override
  Widget build(BuildContext context) {
    final canWrite = context.watch<AuthSession>().hasRole(Roles.administrator) ||
        context.watch<AuthSession>().hasRole(Roles.staff);

    return PagedCodebookTable<NewsItem>(
      key: _tableKey,
      title: 'Obavijesti',
      searchHint: 'Pretraga po naslovu',
      provider: _provider,
      orderBy: 'Title',
      canWrite: canWrite,
      buildColumns: () => const [
        DataColumn(label: Text('Slika')),
        DataColumn(label: Text('Naslov')),
        DataColumn(label: Text('Datum')),
      ],
      buildCells: (item) => [
        DataCell(
          item.hasImage
              ? ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: Image.network(
                    _provider.absoluteImageUrl(item)!,
                    width: 40,
                    height: 40,
                    fit: BoxFit.cover,
                    errorBuilder: (context, error, stackTrace) => const Icon(Icons.broken_image_outlined),
                  ),
                )
              : const Icon(Icons.image_not_supported_outlined),
        ),
        DataCell(Text(item.title)),
        DataCell(Text(_dateFormat.format(item.createdAtUtc.toLocal()))),
      ],
      onAdd: () => _openForm(),
      onEdit: (item) => _openForm(initial: item),
      onDelete: (item) => _provider.delete(item.id),
      itemLabel: (item) => item.title,
    );
  }
}
