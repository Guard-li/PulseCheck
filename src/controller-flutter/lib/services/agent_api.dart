import 'dart:convert';
import 'dart:io';
import 'package:crypto/crypto.dart';
import 'package:http/http.dart' as http;
import 'package:http/io_client.dart';
import '../models/agent_models.dart';

class AgentApi {
  AgentApi(this.connection) : _client = _clientFor(connection);

  final AgentConnection connection;
  final http.Client _client;
  String get _root => '${connection.baseUrl.replaceAll(RegExp(r'/+$'), '')}/api/v1';
  Map<String, String> get _headers => {'Authorization': 'Bearer ${connection.token}', 'Content-Type': 'application/json'};

  static http.Client _clientFor(AgentConnection connection) {
    final client = HttpClient();
    if (connection.fingerprint != null && connection.fingerprint!.isNotEmpty) {
      client.badCertificateCallback = (certificate, host, port) {
        final actual = _sha256(certificate.der).toLowerCase();
        return actual == connection.fingerprint!.replaceAll(':', '').toLowerCase();
      };
    }
    client.connectionTimeout = const Duration(seconds: 8);
    return IOClient(client);
  }

  static String _sha256(List<int> bytes) => sha256.convert(bytes).toString();

  Future<AgentConnection> pair(String baseUrl, String code, {String? fingerprint, String controllerName = 'Flutter Controller'}) async {
    final root = baseUrl.replaceAll(RegExp(r'/+$'), '');
    final client = HttpClient();
    client.connectionTimeout = const Duration(seconds: 8);
    if (fingerprint != null && fingerprint.isNotEmpty) {
      client.badCertificateCallback = (certificate, host, port) => _sha256(certificate.der).toLowerCase() == fingerprint.replaceAll(':', '').toLowerCase();
    }
    final response = await IOClient(client).post(
      Uri.parse('$root/api/v1/auth/pair'),
      headers: const {'Content-Type': 'application/json'},
      body: jsonEncode({'pairingCode': code, 'controllerName': controllerName, 'role': 'owner'}),
    );
    final json = _decode(response);
    final data = json['data'] as Map<String, dynamic>;
    return AgentConnection(baseUrl: root, token: data['token'] as String, deviceName: data['deviceName'] as String? ?? 'PulseCheck Agent', fingerprint: fingerprint);
  }

  Future<MetricSnapshot> current() async {
    final json = _decode(await _client.get(Uri.parse('$_root/metrics/current'), headers: _headers));
    return MetricSnapshot.fromJson(json['data'] as Map<String, dynamic>);
  }

  Future<List<MetricSnapshot>> history({Duration duration = const Duration(hours: 1), int limit = 2000}) async {
    final to = DateTime.now().toUtc();
    final from = to.subtract(duration);
    final uri = Uri.parse('$_root/metrics/history').replace(queryParameters: {'from': from.toIso8601String(), 'to': to.toIso8601String(), 'limit': '$limit'});
    final json = _decode(await _client.get(uri, headers: _headers));
    return (json['data'] as List<dynamic>).map((item) => MetricSnapshot.fromJson(item as Map<String, dynamic>)).toList();
  }

  Future<ScanRecord> startScan({bool full = false, bool generatePdf = true}) async {
    final json = _decode(await _client.post(Uri.parse('$_root/scans'), headers: _headers, body: jsonEncode({'mode': full ? 'full' : 'quick', 'intensity': 'low', 'generatePdf': generatePdf, 'redactSensitiveData': true, 'allowDiskWriteTest': false})));
    return ScanRecord(json['data'] as Map<String, dynamic>);
  }

  Future<ScanRecord> scan(String id) async {
    final json = _decode(await _client.get(Uri.parse('$_root/scans/$id'), headers: _headers));
    return ScanRecord(json['data'] as Map<String, dynamic>);
  }

  Future<List<ReportSummary>> reports() async {
    final json = _decode(await _client.get(Uri.parse('$_root/reports?limit=100'), headers: _headers));
    return (json['data'] as List<dynamic>).map((item) => ReportSummary(item as Map<String, dynamic>)).toList();
  }

  Map<String, dynamic> _decode(http.Response response) {
    final json = jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
    if (response.statusCode < 200 || response.statusCode >= 300 || json['success'] == false) {
      throw AgentApiException(json['error']?['message'] as String? ?? 'Agent API 请求失败');
    }
    return json;
  }

  void close() => _client.close();
}

class AgentApiException implements Exception {
  const AgentApiException(this.message);
  final String message;
  @override
  String toString() => message;
}