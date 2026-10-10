import 'package:flutter/material.dart';
import '../state/agent_controller.dart';
import 'dashboard_page.dart';
import 'realtime_page.dart';
import 'scan_page.dart';
import 'settings_page.dart';

class HomeShell extends StatelessWidget {
  const HomeShell({super.key, required this.controller});
  final AgentController controller;

  @override
  Widget build(BuildContext context) {
    final pages = [DashboardPage(controller: controller), RealtimePage(controller: controller), ScanPage(controller: controller), SettingsPage(controller: controller)];
    final wide = MediaQuery.sizeOf(context).width >= 900;
    return Scaffold(
      body: Row(children: [
        if (wide) NavigationRail(
          selectedIndex: controller.selectedIndex,
          onDestinationSelected: controller.selectTab,
          extended: MediaQuery.sizeOf(context).width >= 1200,
          leading: const Padding(padding: EdgeInsets.all(16), child: Icon(Icons.monitor_heart, size: 36)),
          destinations: const [
            NavigationRailDestination(icon: Icon(Icons.dashboard_outlined), selectedIcon: Icon(Icons.dashboard), label: Text('总览')),
            NavigationRailDestination(icon: Icon(Icons.show_chart), label: Text('实时性能')),
            NavigationRailDestination(icon: Icon(Icons.health_and_safety_outlined), label: Text('体检')),
            NavigationRailDestination(icon: Icon(Icons.settings_outlined), label: Text('设置')),
          ],
        ),
        Expanded(child: pages[controller.selectedIndex]),
      ]),
      bottomNavigationBar: wide ? null : NavigationBar(
        selectedIndex: controller.selectedIndex,
        onDestinationSelected: controller.selectTab,
        destinations: const [
          NavigationDestination(icon: Icon(Icons.dashboard_outlined), label: '总览'),
          NavigationDestination(icon: Icon(Icons.show_chart), label: '实时'),
          NavigationDestination(icon: Icon(Icons.health_and_safety_outlined), label: '体检'),
          NavigationDestination(icon: Icon(Icons.settings_outlined), label: '设置'),
        ],
      ),
    );
  }
}