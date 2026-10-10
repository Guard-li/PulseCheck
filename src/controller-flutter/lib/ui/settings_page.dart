import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:qr_flutter/qr_flutter.dart';
import '../state/agent_controller.dart';

class SettingsPage extends StatefulWidget {
  const SettingsPage({super.key, required this.controller});
  final AgentController controller;
  @override
  State<SettingsPage> createState() => _SettingsPageState();
}

class _SettingsPageState extends State<SettingsPage> {
  final baseController = TextEditingController(text: 'https://192.168.1.10:17821');
  final codeController = TextEditingController();
  final fingerprintController = TextEditingController();

  Future<void> _scanQr() async {
    final result = await showDialog<String>(
      context: context,
      builder: (context) => Dialog(
        child: SizedBox(
          width: 380,
          height: 480,
          child: Column(children: [
            const Padding(padding: EdgeInsets.all(14), child: Text('扫描 PulseCheck 配对二维码', style: TextStyle(fontWeight: FontWeight.bold))),
            Expanded(child: MobileScanner(onDetect: (capture) {
              final raw = capture.barcodes.isEmpty ? null : capture.barcodes.first.rawValue;
              if (raw != null && raw.startsWith('pulsecheck://pair')) Navigator.of(context).pop(raw);
            })),
            TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('取消')),
          ]),
        ),
      ),
    );
    if (result == null) return;
    final uri = Uri.parse(result);
    final query = uri.queryParameters;
    setState(() {
      baseController.text = 'https://${query['host']}:${query['port']}';
      codeController.text = query['code'] ?? '';
      fingerprintController.text = query['fingerprint'] ?? '';
    });
  }

  @override
  Widget build(BuildContext context) {
    final agents = widget.controller.discoveredAgents;
    return Scaffold(
      appBar: AppBar(title: const Text('设备与设置'), actions: [IconButton(onPressed: widget.controller.changeTheme, icon: const Icon(Icons.brightness_6))]),
      body: ListView(padding: const EdgeInsets.all(20), children: [
        Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('配对 Agent', style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold)),
          const SizedBox(height: 14),
          TextField(controller: baseController, decoration: const InputDecoration(labelText: 'Agent 地址', border: OutlineInputBorder())),
          const SizedBox(height: 12),
          TextField(controller: codeController, keyboardType: TextInputType.number, decoration: const InputDecoration(labelText: '6 位配对码', border: OutlineInputBorder())),
          const SizedBox(height: 12),
          TextField(controller: fingerprintController, decoration: const InputDecoration(labelText: 'TLS SHA256 指纹（二维码配对时自动填写）', border: OutlineInputBorder())),
          const SizedBox(height: 14),
          Row(children: [FilledButton(onPressed: () => widget.controller.pair(baseUrl: baseController.text, code: codeController.text, fingerprint: fingerprintController.text.ifEmptyNull), child: const Text('配对')), const SizedBox(width: 10), OutlinedButton(onPressed: widget.controller.discover, child: const Text('局域网发现')), const SizedBox(width: 10), OutlinedButton.icon(onPressed: _scanQr, icon: const Icon(Icons.qr_code_scanner), label: const Text('扫描二维码'))]),
        ]))),
        if (agents.isNotEmpty) Card(child: Padding(padding: const EdgeInsets.all(18), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [const Text('发现的设备', style: TextStyle(fontWeight: FontWeight.bold)), ...agents.map((agent) => ListTile(title: Text(agent.displayName), subtitle: Text('${agent.host}:${agent.port}'), trailing: TextButton(onPressed: () { baseController.text = 'https://${agent.host}:${agent.port}'; fingerprintController.text = agent.fingerprint; }, child: const Text('选择'))))]))),
        if (widget.controller.connection != null) Card(child: Padding(padding: const EdgeInsets.all(18), child: Row(children: [Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [const Text('已连接', style: TextStyle(fontWeight: FontWeight.bold)), Text(widget.controller.connection!.baseUrl), Text(widget.controller.connection!.deviceName)])), SizedBox(width: 150, child: QrImageView(data: widget.controller.connection!.baseUrl, size: 120))]))),
        Card(child: SwitchListTile(value: widget.controller.themeMode == ThemeMode.dark, onChanged: (_) => widget.controller.changeTheme(), title: const Text('深色主题'))),
      ]),
    );
  }
}

extension on String {
  String? get ifEmptyNull => trim().isEmpty ? null : trim();
}