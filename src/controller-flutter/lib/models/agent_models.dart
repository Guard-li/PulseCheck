class AgentConnection {
  const AgentConnection({required this.baseUrl, required this.token, required this.deviceName, this.fingerprint});

  final String baseUrl;
  final String token;
  final String deviceName;
  final String? fingerprint;

  Map<String, dynamic> toJson() => {'baseUrl': baseUrl, 'token': token, 'deviceName': deviceName, 'fingerprint': fingerprint};
  factory AgentConnection.fromJson(Map<String, dynamic> json) => AgentConnection(
        baseUrl: json['baseUrl'] as String,
        token: json['token'] as String,
        deviceName: (json['deviceName'] as String?) ?? 'PulseCheck Agent',
        fingerprint: json['fingerprint'] as String?,
      );
}

class MetricSnapshot {
  MetricSnapshot({required this.snapshotId, required this.deviceId, required this.timestampUtc, required this.raw});

  final String snapshotId;
  final String deviceId;
  final DateTime timestampUtc;
  final Map<String, dynamic> raw;

  double get cpuUsage => _number(raw['cpu']?['totalUsagePercent']);
  double get memoryUsage => _number(raw['memory']?['usagePercent']);
  double get downloadRate => _number(raw['network']?['downloadBytesPerSecond']);
  double get uploadRate => _number(raw['network']?['uploadBytesPerSecond']);
  double get diskReadRate => _number(raw['disk']?['totalReadBytesPerSecond']);
  double get diskWriteRate => _number(raw['disk']?['totalWriteBytesPerSecond']);
  List<dynamic> get topProcesses => (raw['topProcesses'] as List<dynamic>?) ?? const [];
  Map<String, dynamic>? get system => raw['system'] as Map<String, dynamic>?;
  Map<String, dynamic>? get disk => raw['disk'] as Map<String, dynamic>?;
  List<dynamic> get gpus => (raw['gpus'] as List<dynamic>?) ?? const [];

  factory MetricSnapshot.fromJson(Map<String, dynamic> json) => MetricSnapshot(
        snapshotId: json['snapshotId'] as String? ?? '',
        deviceId: json['deviceId'] as String? ?? '',
        timestampUtc: DateTime.tryParse(json['timestampUtc'] as String? ?? '')?.toUtc() ?? DateTime.now().toUtc(),
        raw: json,
      );

  static double _number(dynamic value) => value is num ? value.toDouble() : 0;
}

class ScanRecord {
  const ScanRecord(this.raw);
  final Map<String, dynamic> raw;
  String get id => raw['scanId'] as String? ?? '';
  String get status => raw['status'] as String? ?? 'queued';
  int get percent => (raw['progress']?['percent'] as num?)?.toInt() ?? 0;
  String get message => raw['progress']?['message'] as String? ?? '';
}

class ReportSummary {
  const ReportSummary(this.raw);
  final Map<String, dynamic> raw;
  String get id => raw['reportId'] as String? ?? '';
  String get deviceName => raw['deviceName'] as String? ?? '';
  int get score => (raw['score'] as num?)?.round() ?? 0;
  String get riskLevel => raw['riskLevel'] as String? ?? '';
  DateTime? get generatedAt => DateTime.tryParse(raw['generatedAtUtc'] as String? ?? '')?.toLocal();
}