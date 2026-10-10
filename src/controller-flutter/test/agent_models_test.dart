import 'package:flutter_test/flutter_test.dart';
import 'package:pulsecheck_controller/models/agent_models.dart';

void main() {
  test('parses metric snapshot from Agent JSON', () {
    final snapshot = MetricSnapshot.fromJson({
      'snapshotId': 'abc',
      'deviceId': 'device',
      'timestampUtc': '2026-10-10T00:00:00Z',
      'cpu': {'totalUsagePercent': 12.5},
      'memory': {'usagePercent': 75},
      'network': {'downloadBytesPerSecond': 1024, 'uploadBytesPerSecond': 2048},
      'disk': {'totalReadBytesPerSecond': 4096, 'totalWriteBytesPerSecond': 8192},
      'topProcesses': [],
    });
    expect(snapshot.cpuUsage, 12.5);
    expect(snapshot.memoryUsage, 75);
    expect(snapshot.downloadRate, 1024);
    expect(snapshot.diskWriteRate, 8192);
  });
}