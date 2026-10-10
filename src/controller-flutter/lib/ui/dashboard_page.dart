import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import '../models/agent_models.dart';
import '../state/agent_controller.dart';

class DashboardPage extends StatelessWidget {
  const DashboardPage({super.key, required this.controller});
  final AgentController controller;

  @override
  Widget build(BuildContext context) {
    final snapshot = controller.latest;
    return Scaffold(
      appBar: AppBar(title: const Text('总览'), actions: [IconButton(onPressed: controller.refresh, icon: const Icon(Icons.refresh)), IconButton(onPressed: controller.changeTheme, icon: const Icon(Icons.brightness_6))]),
      body: RefreshIndicator(
        onRefresh: controller.refresh,
        child: ListView(padding: const EdgeInsets.all(20), children: [
          if (controller.error != null) Card(color: Theme.of(context).colorScheme.errorContainer, child: Padding(padding: const EdgeInsets.all(14), child: Text(controller.error!))),
          if (snapshot == null) const Card(child: Padding(padding: EdgeInsets.all(28), child: Center(child: Text('连接 Agent 后显示真实指标')))),
          if (snapshot != null) ...[
            _metricGrid(context, snapshot),
            const SizedBox(height: 16),
            Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Text('最近 1 分钟 CPU / 内存', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
              const SizedBox(height: 18),
              SizedBox(height: 240, child: LineChart(_chartData(controller.snapshots))),
            ]))),
            const SizedBox(height: 16),
            Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              const Text('设备信息', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
              const SizedBox(height: 12),
              _row('设备', snapshot.system?['computerName']),
              _row('系统', '${snapshot.system?['osName'] ?? ''} ${snapshot.system?['osVersion'] ?? ''}'),
              _row('运行模式', snapshot.system?['isAdministrator'] == true ? '完整模式' : '受限模式'),
              _row('运行时长', snapshot.system?['uptime']?.toString()),
              _row('CPU', snapshot.raw['cpu']?['name']),
              _row('GPU', snapshot.gpus.isEmpty ? '未检测到' : snapshot.gpus.first['name']),
            ]))),
          ],
        ]),
      ),
    );
  }

  Widget _metricGrid(BuildContext context, MetricSnapshot snapshot) {
    final disk = snapshot.disk?['volumes'] as List<dynamic>?;
    Map<String, dynamic>? systemVolume;
    for (final item in disk ?? const []) {
      final volume = item as Map<String, dynamic>;
      if (volume['isSystemVolume'] == true) { systemVolume = volume; break; }
    }
    final gpu = snapshot.gpus.isEmpty ? null : snapshot.gpus.first as Map<String, dynamic>;
    return GridView.count(
      crossAxisCount: MediaQuery.sizeOf(context).width >= 900 ? 4 : 2,
      shrinkWrap: true,
      physics: const NeverScrollableScrollPhysics(),
      mainAxisSpacing: 12,
      crossAxisSpacing: 12,
      childAspectRatio: 1.45,
      children: [
        _MetricCard(title: 'CPU', value: '${snapshot.cpuUsage.toStringAsFixed(1)}%', icon: Icons.memory),
        _MetricCard(title: '内存', value: '${snapshot.memoryUsage.toStringAsFixed(1)}%', icon: Icons.developer_board),
        _MetricCard(title: '系统盘可用', value: systemVolume == null ? '--' : _bytes((systemVolume['freeBytes'] as num?)?.toDouble()), icon: Icons.storage),
        _MetricCard(title: 'GPU', value: gpu?['usagePercent'] == null ? '--' : '${(gpu!['usagePercent'] as num).toStringAsFixed(1)}%', icon: Icons.view_in_ar),
      ],
    );
  }

  LineChartData _chartData(List<MetricSnapshot> all) {
    final now = DateTime.now().toUtc();
    final snapshots = all.where((item) => now.difference(item.timestampUtc).inMinutes <= 1).toList();
    return LineChartData(
      minX: 0,
      maxX: snapshots.length <= 1 ? 1 : (snapshots.length - 1).toDouble(),
      minY: 0,
      maxY: 100,
      gridData: FlGridData(show: true, drawVerticalLine: false, horizontalInterval: 25),
      titlesData: const FlTitlesData(leftTitles: AxisTitles(sideTitles: SideTitles(showTitles: true, reservedSize: 32)), rightTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)), topTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)), bottomTitles: AxisTitles(sideTitles: SideTitles(showTitles: false))),
      borderData: FlBorderData(show: false),
      lineBarsData: [
        LineChartBarData(spots: _spots(snapshots, (item) => item.cpuUsage), color: Colors.blue, isCurved: true, dotData: const FlDotData(show: false)),
        LineChartBarData(spots: _spots(snapshots, (item) => item.memoryUsage), color: Colors.orange, isCurved: true, dotData: const FlDotData(show: false)),
      ],
    );
  }

  List<FlSpot> _spots(List<MetricSnapshot> snapshots, double Function(MetricSnapshot) selector) => List.generate(snapshots.length, (index) => FlSpot(index.toDouble(), selector(snapshots[index])));

  Widget _row(String label, Object? value) => Padding(padding: const EdgeInsets.symmetric(vertical: 6), child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [SizedBox(width: 100, child: Text(label, style: const TextStyle(color: Colors.grey))), Expanded(child: SelectableText(value?.toString() ?? '--'))]));

  String _bytes(double? value) {
    if (value == null) return '--';
    final units = ['B', 'KB', 'MB', 'GB', 'TB'];
    var current = value; var unit = 0;
    while (current >= 1024 && unit < units.length - 1) { current /= 1024; unit++; }
    return '${current.toStringAsFixed(unit > 1 ? 1 : 0)} ${units[unit]}';
  }
}

class _MetricCard extends StatelessWidget {
  const _MetricCard({required this.title, required this.value, required this.icon});
  final String title;
  final String value;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Card(child: Padding(padding: const EdgeInsets.all(16), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Icon(icon), const Spacer(), Text(title, style: const TextStyle(color: Colors.grey)), Text(value, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.bold))])));
}