import 'package:flutter/material.dart';
import '../models/agent_models.dart';
import '../state/agent_controller.dart';

class ScanPage extends StatefulWidget {
  const ScanPage({super.key, required this.controller});
  final AgentController controller;
  @override
  State<ScanPage> createState() => _ScanPageState();
}

class _ScanPageState extends State<ScanPage> {
  bool full = false;
  bool pdf = true;
  ScanRecord? active;
  String log = '';

  Future<void> _start() async {
    final scan = await widget.controller.startScan(full: full, generatePdf: pdf);
    if (scan == null) return;
    setState(() { active = scan; log = '任务已创建：${scan.id}'; });
    while (mounted && scan.status != 'completed' && scan.status != 'failed') {
      await Future<void>.delayed(const Duration(milliseconds: 800));
      await widget.controller.refreshScan(scan);
      if (!mounted) return;
      setState(() { active = widget.controller.scans.firstWhere((item) => item.id == scan.id, orElse: () => scan); log = '${active!.status} · ${active!.percent}%\n${active!.message}'; });
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('体检')),
    body: ListView(padding: const EdgeInsets.all(20), children: [
      Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        const Text('检测模式', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
        SwitchListTile(contentPadding: EdgeInsets.zero, value: full, onChanged: (value) => setState(() => full = value), title: const Text('完整检测'), subtitle: const Text('包含更多系统、存储和网络检查；不会执行破坏性写入测试')),
        CheckboxListTile(contentPadding: EdgeInsets.zero, value: pdf, onChanged: (value) => setState(() => pdf = value ?? true), title: const Text('生成 PDF 报告')),
        const SizedBox(height: 12),
        FilledButton.icon(onPressed: _start, icon: const Icon(Icons.health_and_safety), label: Text(full ? '开始完整检测' : '开始快速检测')),
      ]))),
      const SizedBox(height: 16),
      Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Text(active == null ? '等待检测' : '${active!.status} · ${active!.percent}%', style: const TextStyle(fontWeight: FontWeight.bold)),
        const SizedBox(height: 12),
        LinearProgressIndicator(value: active == null ? 0 : active!.percent / 100),
        const SizedBox(height: 14),
        Text(log, style: const TextStyle(fontFamily: 'monospace')),
      ]))),
      const SizedBox(height: 16),
      Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        const Text('记录', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
        ...widget.controller.scans.take(10).map((scan) => ListTile(title: Text(scan.id.substring(0, 8)), subtitle: Text('${scan.status} · ${scan.message}'), trailing: Text('${scan.percent}%'))),
      ]))),
    ]),
  );
}