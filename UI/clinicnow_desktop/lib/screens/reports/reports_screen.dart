import 'package:flutter/material.dart';

import 'appointments_report_tab.dart';
import 'revenue_report_tab.dart';

/// Hosts the two Phase 9 PDF reports as tabs. Each tab is its own focused
/// widget owning its own filter form + generated-PDF state.
class ReportsScreen extends StatefulWidget {
  const ReportsScreen({super.key});

  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> with SingleTickerProviderStateMixin {
  late final TabController _tabController;

  @override
  void initState() {
    super.initState();
    _tabController = TabController(length: 2, vsync: this);
  }

  @override
  void dispose() {
    _tabController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        TabBar(
          controller: _tabController,
          tabs: const [Tab(text: 'Izvještaj o terminima'), Tab(text: 'Izvještaj o prihodima')],
        ),
        Expanded(
          child: TabBarView(
            controller: _tabController,
            children: const [AppointmentsReportTab(), RevenueReportTab()],
          ),
        ),
      ],
    );
  }
}
