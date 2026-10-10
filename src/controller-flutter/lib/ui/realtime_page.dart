import 'package:fl_chart/fl_chart.dart';
import 'package:flutter/material.dart';
import '../models/agent_models.dart';
import '../state/agent_controller.dart';

class RealtimePage extends StatefulWidget {
  const RealtimePage({super.key, required this.controller});
  final AgentController controller;
  @override
  State<RealtimePage> createState() => _RealtimePageState();
}

class _RealtimePageState extends State<RealtimePage> {
  bool paused = false;

  @override
  Widget build(BuildContext context) {
    final snapshots = widget.controller.snapshots;
    final latest = widget.controller.latest;
    return Scaffold(
      appBar: AppBar(title: const Text('实时性能'), actions: [TextButton(onPressed: () => setState(() => paused = !paused), child: Text(paused ? '继续' : '暂停'))]),
      body: ListView(padding: const EdgeInsets.all(20), children: [
        SizedBox(height: 260, child: _chartCard('CPU', Icons.memory, snapshots, (item) => item.cpuUsage, Colors.blue)),
        const SizedBox(height: 14),
        SizedBox(height: 260, child: _chartCard('内存', Icons.developer_board, snapshots, (item) => item.memoryUsage, Colors.orange)),
        const SizedBox(height: 14),
        SizedBox(height: 260, child: _chartCard('磁盘读取 MB/s', Icons.storage, snapshots, (item) => item.diskReadRate / 1048576, Colors.teal)),
        const SizedBox(height: 14),
        Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Top 10 进程', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
          const SizedBox(height: 12),
          ...(latest?.topProcesses ?? []).map((dynamic item) {
            final process = item as Map<String, dynamic>;
            return ListTile(contentPadding: EdgeInsets.zero, leading: const Icon(Icons.apps), title: Text('${process['name']}  #${process['processId']}'), trailing: Text('${(process['cpuUsagePercent'] as num?)?.toStringAsFixed(1) ?? '--'}%'));
          }),
        ]))),
      ]),
    );
  }

  Widget _chartCard(String title, IconData icon, List<MetricSnapshot> snapshots, double Function(MetricSnapshot) selector, Color color) {
    final spots = List.generate(snapshots.length, (index) => FlSpot(index.toDouble(), selector(snapshots[index])));
    return Card(child: Padding(padding: const EdgeInsets.all(16), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Row(children: [Icon(icon), const SizedBox(width: 8), Text(title, style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 17))]), const SizedBox(height: 14), Expanded(child: LineChart(LineChartData(minX: 0, maxX: spots.length <= 1 ? 1 : (spots.length - 1).toDouble(), minY: 0, gridData: const FlGridData(show: true, drawVerticalLine: false), titlesData: const FlTitlesData(leftTitles: AxisTitles(sideTitles: SideTitles(showTitles: true, reservedSize: 34)), rightTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)), topTitles: AxisTitles(sideTitles: SideTitles(showTitles: false)), bottomTitles: AxisTitles(sideTitles: SideTitles(showTitles: false))), borderData: FlBorderData(show: false), lineBarsData: [LineChartBarData(spots: spots, color: color, isCurved: true, dotData: const FlDotData(show: false))])))]))));
  }
}